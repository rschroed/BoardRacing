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
            public float Speed, Distance, FinishTime = -1f, Recovery;
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
        private RacePhase phase = RacePhase.Grid;
        private float countdown, elapsed, rematchHeld, pauseHeld;
        private bool awaitingRematchRelease, resumingFromPause;
        private RaceSnapshot snapshot;

        public RaceSimulation(TrackDefinition track, RaceRules rules)
        {
            this.track = track ?? throw new ArgumentNullException(nameof(track));
            this.rules = rules;
            racers = new[]
            {
                new RacerState { Id = PlayerId.Player1, PriorKind = track.Sample(0f).Kind },
                new RacerState { Id = PlayerId.Player2, PriorKind = track.Sample(0f).Kind }
            };
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
            BurnFuel(racer, command.DrivingPiecePresent ? command.Throttle : ThrottleStep.Brake, delta);
            bool fuelPenalty = FuelPenaltyActive(racer);
            float maximumSpeed = rules.MaxSpeed * (fuelPenalty ? rules.Conditions.EmptyMaximumSpeedScale : 1f);
            float target = maximumSpeed * throttleFraction;
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
            racer.Distance += racer.Speed * delta;
            float finishDistance = track.Length * rules.Laps;
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
            float finishDistance = track.Length * rules.Laps;
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
                racer.Speed = racer.Distance = racer.Recovery = 0f; racer.FinishTime = -1f;
                racer.Finished = racer.IncidentThisStep = false; racer.Incidents = 0;
                racer.PriorKind = track.Sample(0f).Kind;
                racer.FuelUsed = racer.TireWear = racer.ServiceProgress = racer.PitTimer = 0f;
                racer.SelectedService = PitService.None; racer.PitPhase = PitPhase.OnTrack;
                racer.CompletedServices = 0;
            }
        }

        private RaceSnapshot BuildSnapshot()
        {
            var ordered = racers.OrderBy(x => x.Finished ? 0 : 1)
                .ThenBy(x => x.Finished ? x.FinishTime : -x.Distance).ThenBy(x => x.Id).ToArray();
            bool close = CircularDistance(racers[0].Distance, racers[1].Distance, track.Length) <= rules.PassingDistance;
            var result = racers.Select(racer =>
            {
                int place = Array.IndexOf(ordered, racer) + 1;
                float offset = close ? (racer.Id == PlayerId.Player1 ? -rules.PassingOffset : rules.PassingOffset) : 0f;
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
                    track.Sample(racer.Distance), offset, racer.IncidentThisStep, racer.Recovery, racer.Incidents,
                    condition, pit);
            }).ToArray();
            float progress = rules.RematchHoldSeconds <= 0f ? 1f : Math.Min(1f, rematchHeld / rules.RematchHoldSeconds);
            return new RaceSnapshot(phase, countdown, elapsed, result, progress, awaitingRematchRelease);
        }

        private static float MoveTowards(float current, float target, float maximumDelta)
        { return Math.Abs(target - current) <= maximumDelta ? target : current + Math.Sign(target - current) * maximumDelta; }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

        private static float CircularDistance(float a, float b, float length)
        {
            float delta = Math.Abs((a % length) - (b % length));
            return Math.Min(delta, length - delta);
        }
    }
}
