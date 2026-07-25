using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardRacing.Domain
{
    public sealed class RaceSimulation
    {
        private sealed class RacerState
        {
            public PlayerId Id;
            public float Speed, Distance, FinishTime = -1f, Recovery, Lateral, GridStart;
            public TrackSectionKind PriorKind;
            public int Incidents;
            public bool Finished, IncidentThisStep;
            public float FuelUsed, TireWear, ServiceProgress, PitTimer;
            public PitService SelectedService;
            public PitPhase PitPhase;
            public int CompletedServices;
        }

        private readonly TrackDefinition track;
        private readonly RaceRules rules;
        private readonly RacerState[] racers;
        private readonly float[] stepStartDistances;
        private RacePhase phase = RacePhase.Grid;
        private float countdown, elapsed, rematchHeld, pauseHeld;
        private bool awaitingRematchRelease, resumingFromPause;
        private RaceSnapshot snapshot;

        public RaceSimulation(TrackDefinition track, RaceRules rules)
            : this(track, rules, RacerRosters.Default)
        {
        }

        public RaceSimulation(TrackDefinition track, RaceRules rules,
            IReadOnlyList<PlayerId> racerRoster)
        {
            this.track = track ?? throw new ArgumentNullException(nameof(track));
            this.rules = rules;
            PlayerId[] roster = RacerRosters.ValidateAndCopy(racerRoster);
            racers = roster.Select(id =>
                new RacerState { Id = id, PriorKind = track.Sample(0f).Kind }).ToArray();
            ApplyStartingGrid();
            stepStartDistances = new float[racers.Length];
            snapshot = BuildSnapshot();
        }

        public TrackDefinition Track => track;
        public RaceRules Rules => rules;
        public RaceSnapshot Snapshot => snapshot;

        public RaceSnapshot Step(float fixedDeltaSeconds, IReadOnlyList<RacerCommand> commands)
        {
            if (fixedDeltaSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
            var byPlayer = commands?.ToDictionary(x => x.PlayerId) ?? new Dictionary<PlayerId, RacerCommand>();
            RacerCommand Command(PlayerId id) => byPlayer.TryGetValue(id, out var command)
                ? command : new RacerCommand(id, ThrottleStep.Brake, false, false);

            foreach (var racer in racers) racer.IncidentThisStep = false;
            if (phase == RacePhase.Grid)
            {
                if (racers.All(x => Command(x.Id).DrivingPiecePresent))
                { phase = RacePhase.Countdown; countdown = rules.CountdownSeconds; }
            }
            else if (phase == RacePhase.Countdown)
            {
                // A resume countdown only watches the unfinished racers' Ships — a
                // finished player's Ship may legitimately stay off the table — and
                // aborts back to the pause, never to a fresh grid.
                bool abort = resumingFromPause
                    ? racers.Where(x => !x.Finished).Any(x => !Command(x.Id).DrivingPiecePresent)
                    : racers.Any(x => !Command(x.Id).DrivingPiecePresent);
                if (abort) { phase = resumingFromPause ? RacePhase.Paused : RacePhase.Grid; countdown = 0f; }
                else if ((countdown -= fixedDeltaSeconds) <= 0f)
                { countdown = 0f; phase = RacePhase.Racing; resumingFromPause = false; }
            }
            else if (phase == RacePhase.Racing)
            {
                // Clearing the table (every unfinished racer's Ship absent long enough
                // to be deliberate) pauses the race in place.
                bool tableCleared = racers.Where(x => !x.Finished)
                    .All(x => !Command(x.Id).DrivingPiecePresent);
                pauseHeld = tableCleared ? pauseHeld + fixedDeltaSeconds : 0f;
                if (pauseHeld >= rules.PauseClearSeconds)
                {
                    pauseHeld = 0f;
                    phase = RacePhase.Paused;
                }
                else
                {
                    // The slipstream gap check reads every car's PRE-step
                    // distance: racers advance in sequence, and the live
                    // distances would show the second car a phantom half-step
                    // gap behind the first — two dead-heat cars would tow
                    // each other out of nothing.
                    for (int i = 0; i < racers.Length; i++)
                        stepStartDistances[i] = racers[i].Distance;
                    foreach (var racer in racers)
                    {
                        var command = Command(racer.Id);
                        CaptureStrategyIntent(racer, command);
                        AdvanceRacer(racer, command, fixedDeltaSeconds);
                    }
                    elapsed += fixedDeltaSeconds;
                    if (racers.All(x => x.Finished)) phase = RacePhase.Finished;
                }
            }
            else if (phase == RacePhase.Paused)
            {
                // Nothing advances while paused; the unfinished racers' Ships
                // returning starts the resume countdown.
                if (racers.Where(x => !x.Finished).All(x => Command(x.Id).DrivingPiecePresent))
                {
                    phase = RacePhase.Countdown; countdown = rules.CountdownSeconds;
                    resumingFromPause = true;
                }
            }
            else HandleRematch(commands == null ? Array.Empty<RacerCommand>() : commands, fixedDeltaSeconds);

            snapshot = BuildSnapshot();
            return snapshot;
        }

        private void CaptureStrategyIntent(RacerState racer, RacerCommand command)
        {
            if (!rules.Pit.Enabled) return;
            if (racer.PitPhase == PitPhase.InService && command.SelectedService != PitService.None)
                racer.SelectedService = command.SelectedService;
            if (command.RequestPit && racer.PitPhase == PitPhase.OnTrack)
            {
                racer.SelectedService = PitService.None;
                racer.PitPhase = PitPhase.Requested;
            }
        }

        private void AdvanceRacer(RacerState racer, RacerCommand command, float delta)
        {
            if (racer.Finished) return;
            if (racer.PitPhase == PitPhase.Entering || racer.PitPhase == PitPhase.InService ||
                racer.PitPhase == PitPhase.Exiting)
            {
                AdvancePit(racer, command, delta);
                return;
            }
            float throttleFraction = command.DrivingPiecePresent ? (int)command.Throttle / 100f : 0f;
            ThrottleStep commanded = command.DrivingPiecePresent ? command.Throttle : ThrottleStep.Brake;
            // Traffic is settled before the fuel is charged (issue #147, owner
            // report from hardware: a car on Boost caught in traffic paid the
            // usage cost without the benefit). You are billed for the speed
            // you actually got, not the throttle you asked for. Steering runs
            // first so the cap reads the line this car is on THIS step.
            float followingCap = float.MaxValue;
            if (rules.Lateral.Enabled)
            {
                // What this car COULD do if the road were clear, tow included.
                // Not its current speed: a held-up car has already been capped
                // to the speed of the car ahead, so comparing what the two are
                // doing can never reveal that the one behind is faster. A
                // driver knows they are quicker because they are having to
                // lift, not because they are going quicker.
                float desired = rules.MaxSpeed * throttleFraction +
                    (throttleFraction > 0f ? SlipstreamBonus(racer) : 0f);
                SteerLateral(racer, delta, desired);
                followingCap = FollowingSpeedCap(racer, delta);
            }
            BurnFuel(racer, ThrottleActuallyUsed(commanded, followingCap), delta);
            bool fuelPenalty = FuelPenaltyActive(racer);
            float maximumSpeed = rules.MaxSpeed * (fuelPenalty ? rules.Conditions.EmptyMaximumSpeedScale : 1f);
            float target = maximumSpeed * throttleFraction;
            // The slipstream tow (issue #118) rides on top of the throttle
            // target — additive, because matching the leader's throttle must
            // still close the gap or no pass ever happens; a braking car is
            // not dragged forward.
            if (throttleFraction > 0f) target += SlipstreamBonus(racer);
            // A car with a pit call brakes on the track toward the lane crawl, so
            // it crosses the line at pit-lane speed instead of stopping dead on it
            // (issue #110 hardware feel review). The cap follows the drag curve
            // into the line (v² = crawl² + 2·drag·distance): far out it exceeds
            // top speed and does nothing; close in it hands the car to the lane at
            // exactly the crawl the entry leg drives. A final-lap call that will
            // lose to the finish line (issue #95) must not slow the run-in.
            if (racer.PitPhase == PitPhase.Requested && rules.Pit.Enabled && WillDivertAtNextLine(racer))
            {
                float toLine = ((int)(racer.Distance / track.Length) + 1) * track.Length - racer.Distance;
                float allowed = (float)Math.Sqrt(
                    rules.Pit.LaneSpeed * rules.Pit.LaneSpeed + 2f * rules.Drag * toLine);
                target = Math.Min(target, allowed);
            }
            target = Math.Min(target, followingCap);
            float rate;
            if (target > racer.Speed)
                rate = rules.Acceleration * (racer.Recovery > 0f ? rules.RecoveryAccelerationScale : 1f) *
                    (fuelPenalty ? rules.Conditions.EmptyAccelerationScale : 1f);
            else rate = target <= 0f ? rules.Braking : rules.Drag;
            racer.Speed = MoveTowards(racer.Speed, target, rate * delta);
            racer.Recovery = Math.Max(0f, racer.Recovery - delta);

            var before = track.Sample(racer.Distance);
            // A designed corner is a fan of short chord segments (TrackCatalog), so
            // corner entry is the straight-to-corner boundary — never the chord
            // seams inside one arc. Scrub and wear charge once per corner, matching
            // the single-segment corners the model was tuned on. (Assumes arcs are
            // always separated by straights, which the catalog geometry tests pin.)
            bool enteringCorner = before.Kind == TrackSectionKind.Corner && racer.PriorKind != TrackSectionKind.Corner;
            float cornerEntrySpeed = racer.Speed;
            float effectiveSafeSpeed = before.SafeSpeed;
            if (before.Kind == TrackSectionKind.Corner && rules.Conditions.Enabled)
                effectiveSafeSpeed *= 1f - racer.TireWear * (1f - rules.Conditions.FullyWornSafeSpeedScale);
            // The other half of the racing line (issue #147, owner report from
            // hardware: being caught outside a big corner cost too much). The
            // outside is longer, which the path factor already charges for —
            // but it is also a WIDER ARC, and a wider arc corners faster. Grip
            // holds v² ∝ r, so the safe speed follows the square root of the
            // car's own radius. Without this the outside was pure cost and no
            // driver would ever want it; with it the line becomes the real
            // trade: short and slow against long and fast.
            if (rules.Lateral.Enabled && before.Kind == TrackSectionKind.Corner)
                effectiveSafeSpeed *= (float)Math.Sqrt(LateralPathFactor(racer));
            if (enteringCorner && racer.Speed > effectiveSafeSpeed)
            {
                racer.Speed *= rules.CornerSpeedScrub;
                racer.Recovery = rules.CornerRecoverySeconds;
                racer.IncidentThisStep = true;
                racer.Incidents++;
            }
            if (enteringCorner) AddCornerWear(racer, before.SafeSpeed, cornerEntrySpeed);
            racer.PriorKind = before.Kind;

            float prior = racer.Distance;
            if (rules.Lateral.Enabled) racer.Speed = Math.Min(racer.Speed, followingCap);
            // The car's speed is along the line IT is driving; progress along
            // the reference line is that travel divided by how much longer the
            // car's own arc is (issue #147). Inside a corner of signed
            // curvature k, an offset of lat rides a radius of R − k·lat·R, so
            // the inside line converts the same speed into more lap. The
            // clamp keeps a car from being credited more than a modest gain
            // when the sampled curvature spikes on a chord seam.
            racer.Distance += racer.Speed * delta / LateralPathFactor(racer);
            float finishDistance = track.Length * rules.Laps + racer.GridStart;
            // Reaching the line eligible to classify finishes the race even with a
            // pit call pending (issue #95) — the call expires with the race. Only an
            // ineligible racer's call may still divert them into the pit at the line.
            if (racer.Distance >= finishDistance && racer.CompletedServices >= rules.RequiredServiceCount)
            {
                float moved = racer.Distance - prior;
                float fraction = moved <= 0f ? 1f : Math.Max(0f, Math.Min(1f, (finishDistance - prior) / moved));
                FinishRacer(racer, finishDistance, elapsed + delta * fraction);
                return;
            }
            bool crossedPitLine = (int)(prior / track.Length) < (int)(racer.Distance / track.Length);
            if (racer.PitPhase == PitPhase.Requested && rules.Pit.Enabled && crossedPitLine)
            {
                racer.Distance = ((int)(prior / track.Length) + 1) * track.Length;
                racer.Speed = 0f; racer.PitPhase = PitPhase.Entering; racer.PitTimer = 0f;
            }
        }

        private void AdvancePit(RacerState racer, RacerCommand command, float delta)
        {
            racer.Speed = 0f;
            if (racer.PitPhase == PitPhase.Entering)
            {
                racer.ServiceProgress = 0f;
                racer.PitTimer += delta;
                if (racer.PitTimer >= rules.Pit.EntrySeconds(racer.Id))
                {
                    racer.PitPhase = PitPhase.InService; racer.PitTimer = 0f;
                }
                return;
            }
            if (racer.PitPhase == PitPhase.InService)
            {
                // The pit stop never ends itself: the player leaves by holding the
                // Robot in Leave Pit — allowed at any time, even mid-service.
                if (command.RequestExit)
                {
                    racer.SelectedService = PitService.None;
                    racer.ServiceProgress = 0f;
                    racer.PitPhase = PitPhase.Exiting; racer.PitTimer = 0f;
                    return;
                }
                if (racer.SelectedService == PitService.None) { racer.ServiceProgress = 0f; return; }
                float meterBefore = racer.SelectedService == PitService.Tires
                    ? racer.TireWear : racer.FuelUsed;
                if (command.ServiceDrain > 0f)
                {
                    if (racer.SelectedService == PitService.Tires)
                        racer.TireWear = Math.Max(0f, racer.TireWear - command.ServiceDrain);
                    else racer.FuelUsed = Math.Max(0f, racer.FuelUsed - command.ServiceDrain);
                }
                float meter = racer.SelectedService == PitService.Tires ? racer.TireWear : racer.FuelUsed;
                racer.ServiceProgress = 1f - meter;
                // Count the service only on the emptying stroke so stirring an
                // already-empty meter cannot count it again; both dials may be
                // serviced in one parked stop.
                if (meterBefore > 0f && meter <= 0f) racer.CompletedServices++;
                return;
            }

            racer.PitTimer += delta;
            if (racer.PitTimer < rules.Pit.ExitSeconds(racer.Id)) return;
            racer.PitPhase = PitPhase.OnTrack; racer.PitTimer = racer.ServiceProgress = 0f;
            racer.SelectedService = PitService.None;
            float finishDistance = track.Length * rules.Laps + racer.GridStart;
            if (racer.Distance >= finishDistance && racer.CompletedServices >= rules.RequiredServiceCount)
            {
                FinishRacer(racer, racer.Distance, elapsed + delta);
                return;
            }
            // The pit lane rejoins the track where it physically ends, not back at
            // the start/finish line the car entered from — and the car merges at
            // the lane crawl and accelerates away: resuming from a dead stop read
            // as stop-and-go at the rejoin (issue #110 hardware feel review).
            racer.Speed = rules.Pit.LaneSpeed;
            racer.Distance += rules.Pit.ExitRejoinDistance;
            racer.PriorKind = track.Sample(racer.Distance).Kind;
        }

        // The slipstream tow (issue #118): any car ahead within the window —
        // by SPATIAL gap, so a leader being lapped still gives a tow — grants
        // it, provided the trailing car is on a straight with room left in it
        // and the leader is physically on the racing line. Gaps compare the
        // cars' PRE-step distances (stepStartDistances) so the in-step update
        // order cannot manufacture a phantom gap between dead-heat cars.
        // Checks every other racer, not "the" opponent, so four cars chain
        // tows the day #124 lands.
        private float SlipstreamBonus(RacerState racer)
        {
            if (rules.SlipstreamBonus <= 0f || rules.SlipstreamWindow <= 0f) return 0f;
            int self = Array.IndexOf(racers, racer);
            float distance = stepStartDistances[self];
            if (track.Sample(distance).Kind != TrackSectionKind.Straight) return 0f;
            // The tow releases on the corner approach: with the bonus gone,
            // drag hands the car back its own throttle speed by the entry, so
            // carrying overspeed into a corner stays a THROTTLE choice — the
            // tow never converts into an incident the player didn't ask for.
            float release = (rules.MaxSpeed + rules.SlipstreamBonus * .5f) *
                rules.SlipstreamBonus / rules.Drag;
            if (DistanceToNextCorner(distance) <= release) return 0f;
            for (int i = 0; i < racers.Length; i++)
            {
                var other = racers[i];
                if (i == self || other.Finished) continue;
                if (other.PitPhase != PitPhase.OnTrack && other.PitPhase != PitPhase.Requested) continue;
                float gap = (stepStartDistances[i] - distance) % track.Length;
                if (gap < 0f) gap += track.Length;
                if (gap > 0f && gap <= rules.SlipstreamWindow) return rules.SlipstreamBonus;
            }
            return 0f;
        }

        // The throttle a car actually got. A driver held behind a rival is
        // billed for the step its speed was capped to, never for the one it
        // asked for — the generous rounding, since a car capped between two
        // steps did not get the higher one.
        private ThrottleStep ThrottleActuallyUsed(ThrottleStep commanded, float cap)
        {
            if (!rules.Lateral.Enabled || cap == float.MaxValue) return commanded;
            if (cap >= rules.MaxSpeed * (int)commanded / 100f) return commanded;
            return cap >= rules.MaxSpeed * (int)ThrottleStep.Drive / 100f
                ? ThrottleStep.Drive : ThrottleStep.Brake;
        }

        // How much longer this car's arc is than the reference line, as a
        // divisor on its progress. 1 on a straight and for a car on the line.
        private float LateralPathFactor(RacerState racer)
        {
            if (!rules.Lateral.Enabled || racer.Lateral == 0f) return 1f;
            float factor = 1f - SignedCurvature(racer.Distance) * racer.Lateral *
                rules.Lateral.PathCostScale;
            return Math.Max(.75f, Math.Min(1.25f, factor));
        }

        // Signed curvature of the racing line (1/px), positive turning toward
        // the +normal side — the same convention the drawn heading and the
        // lateral offset use, so a positive offset is the inside of a positive
        // corner.
        private float SignedCurvature(float distance)
        {
            const float halfSpan = 40f;
            Vec2 behind = track.Sample(distance - halfSpan).Position;
            Vec2 at = track.Sample(distance).Position;
            Vec2 ahead = track.Sample(distance + halfSpan).Position;
            float aX = at.X - behind.X, aY = at.Y - behind.Y;
            float bX = ahead.X - at.X, bY = ahead.Y - at.Y;
            float cross = aX * bY - aY * bX, dot = aX * bX + aY * bY;
            if (cross == 0f && dot == 0f) return 0f;
            return (float)Math.Atan2(cross, dot) / halfSpan;
        }

        // Automatic line choice (issue #147): hold the inside of what is
        // coming, unless a car ahead on that line is close enough to be in the
        // way, in which case try the other side. Deterministic and blind to
        // PlayerId — the only inputs are geometry and who is actually ahead.
        private void SteerLateral(RacerState racer, float delta, float desiredSpeed)
        {
            var lateral = rules.Lateral;
            float curvature = SignedCurvature(racer.Distance + lateral.LookAhead * .5f);
            // Inside is the side the corner turns toward; on a straight there
            // is no inside, so a car simply returns to the line.
            float inside = curvature > 0f ? lateral.MaximumOffset
                : curvature < 0f ? -lateral.MaximumOffset : 0f;
            float target = inside;
            RacerState blocker = BlockerOn(racer, inside, lateral);
            // Pulling out is a decision, not a reflex (owner report from
            // hardware). A car only leaves the inside when the move is
            // actually on — the car in the way cannot hold the speed this one
            // has in hand, so there is something to gain for the longer arc. Otherwise it tucks in
            // behind and waits for the straight, which is what the tow is
            // for. Before this, any car ahead sent a driver around the
            // outside to pay the distance for nothing.
            if (blocker != null && blocker.Speed < desiredSpeed - rules.MaxSpeed * .01f)
            {
                float outside = -inside;
                if (inside == 0f) outside = racer.Lateral >= 0f
                    ? lateral.MaximumOffset : -lateral.MaximumOffset;
                if (BlockerOn(racer, outside, lateral) == null) target = outside;
            }
            // Held up on the inside with no move on: sit behind, do not drift
            // out into the longer line by accident.
            else if (blocker != null) target = racer.Lateral;
            float moved = MoveTowards(racer.Lateral, target, lateral.MoveRate * delta);
            // A car may not move sideways into a body that is already there.
            // The speed cap holds a following gap open, but it says nothing
            // about a rival ALONGSIDE — without this a car steering across
            // simply drives through one, which the first run of the #147
            // experiment measured as a full body-width overlap.
            racer.Lateral = WouldStrikeAlongside(racer, moved, lateral) ? racer.Lateral : moved;
        }

        // Whether this lateral would put the car inside a rival's body, for a
        // rival close enough alongside — either direction, since a car being
        // passed is behind and still very much there.
        private bool WouldStrikeAlongside(RacerState racer, float lateral, LateralRules rules2)
        {
            int self = Array.IndexOf(racers, racer);
            for (int i = 0; i < racers.Length; i++)
            {
                var other = racers[i];
                if (i == self || other.Finished) continue;
                if (other.PitPhase != PitPhase.OnTrack && other.PitPhase != PitPhase.Requested) continue;
                float gap = AheadGap(racer, other);
                float along = Math.Min(gap, track.Length - gap);
                if (along >= rules2.MinimumGap) continue;
                // Only a move that closes on the rival is refused; a car
                // already inside the width must always be free to escape.
                if (Math.Abs(lateral - other.Lateral) < rules2.SameLineWidth &&
                    Math.Abs(lateral - other.Lateral) < Math.Abs(racer.Lateral - other.Lateral))
                    return true;
            }
            return false;
        }

        // The nearest car occupying this line ahead, or null if it is clear.
        private RacerState BlockerOn(RacerState racer, float line, LateralRules lateral)
        {
            int self = Array.IndexOf(racers, racer);
            RacerState nearest = null;
            float nearestGap = float.MaxValue;
            for (int i = 0; i < racers.Length; i++)
            {
                var other = racers[i];
                if (i == self || other.Finished) continue;
                if (other.PitPhase != PitPhase.OnTrack && other.PitPhase != PitPhase.Requested) continue;
                float gap = AheadGap(racer, other);
                if (gap <= 0f || gap > lateral.LookAhead) continue;
                if (Math.Abs(other.Lateral - line) >= lateral.SameLineWidth) continue;
                if (gap >= nearestGap) continue;
                nearestGap = gap; nearest = other;
            }
            return nearest;
        }

        // Bodies cannot pass through each other: a car sharing a line with the
        // car ahead may go no faster than keeps the minimum gap open at the
        // end of this step. A cap, never a shove, so the follower's position
        // stays the integral of its own speed and can never jump.
        private float FollowingSpeedCap(RacerState racer, float delta)
        {
            var lateral = rules.Lateral;
            int self = Array.IndexOf(racers, racer);
            float cap = float.MaxValue;
            for (int i = 0; i < racers.Length; i++)
            {
                var other = racers[i];
                if (i == self || other.Finished) continue;
                if (other.PitPhase != PitPhase.OnTrack && other.PitPhase != PitPhase.Requested) continue;
                if (Math.Abs(other.Lateral - racer.Lateral) >= lateral.SameLineWidth) continue;
                float gap = AheadGap(racer, other);
                if (gap <= 0f || gap > lateral.LookAhead) continue;
                // Close the surplus over the minimum gap within this step, no
                // faster: at the gap itself the cap is the leader's own speed.
                cap = Math.Min(cap, Math.Max(0f, other.Speed + (gap - lateral.MinimumGap) / delta));
            }
            return cap;
        }

        // Centerline distance from this car forward to another, wrapped.
        private float AheadGap(RacerState racer, RacerState other)
        {
            float gap = (other.Distance - racer.Distance) % track.Length;
            if (gap < 0f) gap += track.Length;
            return gap;
        }

        private float DistanceToNextCorner(float distance)
        {
            var sample = track.Sample(distance);
            float wrapped = ((distance % track.Length) + track.Length) % track.Length;
            float toSectionEnd = 0f;
            for (int step = 0; step < track.Segments.Count; step++)
            {
                int index = (sample.SectionIndex + step) % track.Segments.Count;
                var segment = track.Segments[index];
                if (segment.Kind == TrackSectionKind.Corner) return toSectionEnd;
                float start = SegmentStart(index);
                float end = start + segment.Length;
                toSectionEnd = step == 0 ? end - wrapped : toSectionEnd + segment.Length;
            }
            return float.MaxValue;
        }

        private float SegmentStart(int index)
        {
            float start = 0f;
            for (int i = 0; i < index; i++) start += track.Segments[i].Length;
            return start;
        }

        // Whether the next start/finish crossing diverts this racer into the pit:
        // every mid-race line does; the final line only if the racer is not yet
        // eligible to classify (issue #95 — an eligible finish beats the call).
        private bool WillDivertAtNextLine(RacerState racer) =>
            (int)(racer.Distance / track.Length) + 1 < rules.Laps ||
            racer.CompletedServices < rules.RequiredServiceCount;

        private static void FinishRacer(RacerState racer, float distance, float finishTime)
        {
            racer.Distance = distance; racer.Finished = true; racer.FinishTime = finishTime; racer.Speed = 0f;
            // A pending pit call expires with the race.
            racer.PitPhase = PitPhase.OnTrack;
        }

        private void BurnFuel(RacerState racer, ThrottleStep step, float delta)
        {
            if (!rules.Conditions.Enabled) return;
            float burn = step == ThrottleStep.Boost ? rules.Conditions.FuelBurnPerSecondAtBoost
                : step == ThrottleStep.Drive ? rules.Conditions.FuelBurnPerSecondAtDrive : 0f;
            racer.FuelUsed = Clamp01(racer.FuelUsed + burn * delta);
        }

        private void AddCornerWear(RacerState racer, float baseSafeSpeed, float entrySpeed)
        {
            if (!rules.Conditions.Enabled) return;
            float unsafeRatio = baseSafeSpeed <= 0f ? 0f : Math.Max(0f, entrySpeed / baseSafeSpeed - 1f);
            racer.TireWear = Clamp01(racer.TireWear + rules.Conditions.TireWearPerCorner +
                unsafeRatio * rules.Conditions.TireWearPerUnsafeSpeed);
        }

        private bool FuelPenaltyActive(RacerState racer) =>
            rules.Conditions.Enabled && racer.FuelUsed >= 1f;

        private bool TirePenaltyActive(RacerState racer) =>
            rules.Conditions.Enabled && racer.TireWear >= rules.Conditions.TirePenaltyThreshold;

        private void HandleRematch(IReadOnlyList<RacerCommand> commands, float delta)
        {
            bool allConfirming = racers.All(x => commands.Any(c => c.PlayerId == x.Id &&
                c.DrivingPiecePresent && c.RematchConfirming));
            bool allReleased = racers.All(x => commands.Any(c => c.PlayerId == x.Id &&
                c.DrivingPiecePresent && !c.RematchConfirming));
            if (!awaitingRematchRelease)
            {
                rematchHeld = allConfirming ? rematchHeld + delta : 0f;
                if (rematchHeld >= rules.RematchHoldSeconds) awaitingRematchRelease = true;
            }
            else if (allReleased) ResetForRematch();
        }

        // The overlays' START NEW RACE touch button — the game's one non-piece
        // control; honored only when no race is running (paused, or finished with
        // the RACE FINISHED overlay up — owner decisions, issues #90 and #97).
        public void RequestNewRace()
        {
            if (phase != RacePhase.Paused && phase != RacePhase.Finished) return;
            ResetForRematch();
            snapshot = BuildSnapshot();
        }

        private void ResetForRematch()
        {
            phase = RacePhase.Grid; countdown = elapsed = rematchHeld = pauseHeld = 0f;
            awaitingRematchRelease = resumingFromPause = false;
            foreach (var racer in racers)
            {
                racer.Speed = racer.Recovery = racer.Lateral = 0f; racer.Distance = 0f; racer.FinishTime = -1f;
                racer.Finished = racer.IncidentThisStep = false; racer.Incidents = 0;
                racer.PriorKind = track.Sample(0f).Kind;
                racer.FuelUsed = racer.TireWear = racer.ServiceProgress = racer.PitTimer = 0f;
                racer.SelectedService = PitService.None; racer.PitPhase = PitPhase.OnTrack;
                racer.CompletedServices = 0;
            }
            ApplyStartingGrid();
        }

        // With lateral modeled (issue #147) presentation stops inventing the
        // grid split, and nothing else was placing the cars: every racer began
        // stacked on the line at distance zero. That is an overlap on frame
        // one, and worse, the following cap reads a zero gap and pins the
        // whole field to a standstill until whoever nudges ahead first clears
        // a body length — the grid released one car at a time.
        //
        // So the grid becomes real too: two columns a body apart, staggered
        // back from the line. Each racer still covers exactly Laps × Length,
        // measured from its own slot, so a back-row start costs nothing —
        // there is no qualifying here to earn the front row with. Whether
        // that is the right answer, or whether grid position should be a real
        // advantage, is the open question on #147.
        // Each slot sits back from the one ahead and on the other side, so no
        // two cars are ever level: a staggered echelon, the way a real grid
        // is drawn. Two rows of two read as a stacked block instead (owner
        // report from hardware) — bodies are 54 long and 26 wide, so cars
        // level with each other showed 6px of daylight and cars in the row
        // behind 8px, which is one clump, not a grid. A stagger longer than
        // the body clears every pair along the ribbon as well as across it,
        // so each car is seen against open track rather than against another
        // car's flank.
        //
        // Tightened from a body and a half to just under one (owner review of
        // hardware captures 2026-07-25). A real grid staggers about 1.6 car
        // lengths, and at that proportion this one read as four cars strung
        // out rather than a grid — half a car of empty track between each,
        // and on a course whose line follows a corner the whole formation
        // wrapped the bend. Legibility on a table beats proportion. The floor
        // is about 34: below that the two cars sharing a column close inside
        // a body length of each other.
        public const float GridSlotStagger = 50f;

        private void ApplyStartingGrid()
        {
            if (!rules.Lateral.Enabled) return;
            for (int i = 0; i < racers.Length; i++)
            {
                racers[i].Lateral = (i % 2 == 0 ? -1f : 1f) * rules.Lateral.MaximumOffset;
                racers[i].GridStart = -i * GridSlotStagger;
                racers[i].Distance = racers[i].GridStart;
            }
        }

        private RaceSnapshot BuildSnapshot()
        {
            var ordered = racers.OrderBy(x => x.Finished ? 0 : 1)
                .ThenBy(x => x.Finished ? x.FinishTime : -x.Distance)
                .ThenBy(x => Array.IndexOf(racers, x)).ToArray();
            var candidates = racers.Select((racer, index) =>
                    new RacingLineCandidate(racer.Id, index, racer.Distance))
                .Where((candidate, index) => !racers[index].Finished &&
                    (racers[index].PitPhase == PitPhase.OnTrack ||
                     racers[index].PitPhase == PitPhase.Requested))
                .ToArray();
            // With lateral modeled (issue #147) the car IS somewhere: its own
            // offset is the answer, and nothing needs allocating or staggering.
            RacingLinePlacement[] placements = rules.Lateral.Enabled
                ? racers.Select(x => new RacingLinePlacement(x.Id,
                    0f, x.Finished ? 0f : x.Lateral)).ToArray()
                : RacingLineAllocator.Allocate(candidates,
                    track.Length, rules.PassingDistance, rules.PassingOffset);
            var result = racers.Select(racer =>
            {
                int place = Array.IndexOf(ordered, racer) + 1;
                RacingLinePlacement placement = placements.FirstOrDefault(x => x.PlayerId == racer.Id);
                var condition = new RacerConditionSnapshot(racer.FuelUsed, racer.TireWear,
                    FuelPenaltyActive(racer), TirePenaltyActive(racer));
                float phaseProgress = racer.PitPhase == PitPhase.Entering
                    ? Clamp01(racer.PitTimer / rules.Pit.EntrySeconds(racer.Id))
                    : racer.PitPhase == PitPhase.Exiting
                        ? Clamp01(racer.PitTimer / rules.Pit.ExitSeconds(racer.Id)) : 0f;
                var pit = new RacerPitSnapshot(racer.SelectedService, racer.PitPhase, racer.ServiceProgress,
                    racer.CompletedServices, racer.CompletedServices >= rules.RequiredServiceCount, phaseProgress);
                return new RacerSnapshot(racer.Id, racer.Speed, racer.Distance,
                    Math.Min(rules.Laps, (int)(racer.Distance / track.Length)), place, racer.Finished, racer.FinishTime,
                    track.Sample(racer.Distance), placement.LateralOffset, racer.IncidentThisStep,
                    racer.Recovery, racer.Incidents, condition, pit, placement.LongitudinalOffset);
            }).ToArray();
            float progress = rules.RematchHoldSeconds <= 0f ? 1f : Math.Min(1f, rematchHeld / rules.RematchHoldSeconds);
            return new RaceSnapshot(phase, countdown, elapsed, result, progress, awaitingRematchRelease);
        }

        private static float MoveTowards(float current, float target, float maximumDelta)
        { return Math.Abs(target - current) <= maximumDelta ? target : current + Math.Sign(target - current) * maximumDelta; }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

    }
}
