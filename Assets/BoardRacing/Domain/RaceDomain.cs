using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardRacing.Domain
{
    public enum RacePhase { Grid, Countdown, Racing, Finished, Paused }
    public enum TrackSectionKind { Straight, Corner }
    public enum PitService { None, Tires, Fuel }
    public enum PitPhase { OnTrack, Requested, Entering, InService, Exiting }
    public enum PitCallState { Unavailable, NeedsPlacement, Aligning, Holding, Requested }

    public static class RacerRosters
    {
        public static readonly PlayerId[] Default =
            { PlayerId.Player1, PlayerId.Player2 };

        public static PlayerId[] ValidateAndCopy(IReadOnlyList<PlayerId> roster)
        {
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            if (roster.Count < 2 || roster.Count > 4)
                throw new ArgumentException("A race requires an explicit roster of two to four racers.",
                    nameof(roster));
            var result = new PlayerId[roster.Count];
            var unique = new HashSet<PlayerId>();
            for (int i = 0; i < roster.Count; i++)
            {
                PlayerId id = roster[i];
                if (!Enum.IsDefined(typeof(PlayerId), id))
                    throw new ArgumentException("The racer roster contains an unknown player identity.",
                        nameof(roster));
                if (!unique.Add(id))
                    throw new ArgumentException("The racer roster cannot contain duplicate identities.",
                        nameof(roster));
                result[i] = id;
            }
            return result;
        }
    }

    public readonly struct RacingLineCandidate
    {
        public RacingLineCandidate(PlayerId playerId, int rosterIndex, float distance)
        {
            if (!Enum.IsDefined(typeof(PlayerId), playerId) || rosterIndex < 0 ||
                float.IsNaN(distance) || float.IsInfinity(distance))
                throw new ArgumentException("Racing-line candidate contains invalid values.");
            PlayerId = playerId; RosterIndex = rosterIndex; Distance = distance;
        }
        public PlayerId PlayerId { get; }
        public int RosterIndex { get; }
        public float Distance { get; }
    }

    public readonly struct RacingLinePlacement
    {
        public RacingLinePlacement(PlayerId playerId, float longitudinalOffset, float lateralOffset)
        {
            PlayerId = playerId; LongitudinalOffset = longitudinalOffset;
            LateralOffset = lateralOffset;
        }
        public PlayerId PlayerId { get; }
        // Presentation-only distance along the racing line. Classification,
        // laps, pit diversion, and finish truth continue to use TotalDistance.
        public float LongitudinalOffset { get; }
        public float LateralOffset { get; }
    }

    // Pure N-car formation allocator. Close cars use exactly two lateral lanes;
    // any third or fourth body sharing a lane is pushed only far enough along
    // the ribbon to maintain the proven 60 px nose-to-tail spacing. Lateral
    // side follows explicit roster order, not enum value or race place, so an
    // overtake cannot make cars swap sides.
    public static class RacingLineAllocator
    {
        public const float NoseToTailSpacing = 60f;

        public static RacingLinePlacement[] Allocate(
            IReadOnlyList<RacingLineCandidate> candidates, float trackLength,
            float clusterDistance, float lateralOffset)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (trackLength <= 0f || float.IsNaN(trackLength) || float.IsInfinity(trackLength) ||
                clusterDistance < 0f || float.IsNaN(clusterDistance) || float.IsInfinity(clusterDistance) ||
                lateralOffset < 0f || float.IsNaN(lateralOffset) || float.IsInfinity(lateralOffset))
                throw new ArgumentException("Racing-line geometry contains invalid values.");
            if (candidates.Count > 4)
                throw new ArgumentException("The racing-line allocator supports at most four racers.");
            var result = candidates.Select(x =>
                new RacingLinePlacement(x.PlayerId, 0f, 0f)).ToArray();
            if (candidates.Count < 2) return result;
            if (candidates.Select(x => x.PlayerId).Distinct().Count() != candidates.Count ||
                candidates.Select(x => x.RosterIndex).Distinct().Count() != candidates.Count)
                throw new ArgumentException("Racing-line candidates must have unique identities and roster slots.");

            int[] circularOrder = Enumerable.Range(0, candidates.Count)
                .OrderBy(i => Wrap(candidates[i].Distance, trackLength))
                .ThenBy(i => candidates[i].RosterIndex).ToArray();
            int start = StartAfterWidestGap(candidates, circularOrder, trackLength);
            var cluster = new List<int>();
            var positions = new List<float>();
            for (int step = 0; step < candidates.Count; step++)
            {
                int index = circularOrder[(start + step) % candidates.Count];
                float wrapped = Wrap(candidates[index].Distance, trackLength);
                float position = positions.Count == 0
                    ? wrapped
                    : positions[positions.Count - 1] +
                      Wrap(wrapped - positions[positions.Count - 1], trackLength);
                if (positions.Count > 0 && position - positions[positions.Count - 1] > clusterDistance)
                {
                    ApplyCluster(candidates, cluster, positions, lateralOffset, result);
                    cluster.Clear(); positions.Clear();
                    position = wrapped;
                }
                cluster.Add(index); positions.Add(position);
            }
            ApplyCluster(candidates, cluster, positions, lateralOffset, result);
            return result;
        }

        private static int StartAfterWidestGap(IReadOnlyList<RacingLineCandidate> candidates,
            int[] order, float trackLength)
        {
            int start = 0;
            float widest = -1f;
            for (int i = 0; i < order.Length; i++)
            {
                float here = Wrap(candidates[order[i]].Distance, trackLength);
                float next = Wrap(candidates[order[(i + 1) % order.Length]].Distance, trackLength);
                float gap = i == order.Length - 1 ? next - here + trackLength : next - here;
                if (gap > widest) { widest = gap; start = (i + 1) % order.Length; }
            }
            return start;
        }

        private static void ApplyCluster(IReadOnlyList<RacingLineCandidate> candidates,
            List<int> indices, List<float> positions, float lateralOffset,
            RacingLinePlacement[] result)
        {
            if (indices.Count < 2) return;
            int[] byRoster = Enumerable.Range(0, indices.Count)
                .OrderBy(i => candidates[indices[i]].RosterIndex).ToArray();
            var lane = new int[indices.Count];
            for (int rank = 0; rank < byRoster.Length; rank++)
                lane[byRoster[rank]] = rank % 2;

            var drawn = positions.ToArray();
            for (int side = 0; side < 2; side++)
            {
                int[] laneOrder = Enumerable.Range(0, indices.Count).Where(i => lane[i] == side)
                    .OrderBy(i => positions[i]).ThenBy(i => candidates[indices[i]].RosterIndex).ToArray();
                for (int i = 1; i < laneOrder.Length; i++)
                {
                    int prior = laneOrder[i - 1], current = laneOrder[i];
                    drawn[current] = Math.Max(drawn[current], drawn[prior] + NoseToTailSpacing);
                }
            }
            float shift = (positions.Sum() - drawn.Sum()) / indices.Count;
            for (int i = 0; i < indices.Count; i++)
            {
                int candidateIndex = indices[i];
                result[candidateIndex] = new RacingLinePlacement(
                    candidates[candidateIndex].PlayerId,
                    drawn[i] + shift - positions[i],
                    lane[i] == 0 ? -lateralOffset : lateralOffset);
            }
        }

        private static float Wrap(float value, float length)
        {
            value %= length;
            return value < 0f ? value + length : value;
        }
    }

    public readonly struct TrackSegment
    {
        public TrackSegment(Vec2 start, Vec2 end, TrackSectionKind kind, float safeSpeed)
        {
            Start = start; End = end; Kind = kind; SafeSpeed = safeSpeed;
            float dx = end.X - start.X, dy = end.Y - start.Y;
            Length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (Length <= 0f) throw new ArgumentException("Track segments must have positive length.");
        }
        public Vec2 Start { get; }
        public Vec2 End { get; }
        public TrackSectionKind Kind { get; }
        public float SafeSpeed { get; }
        public float Length { get; }
    }

    public readonly struct TrackSample
    {
        public TrackSample(Vec2 position, Vec2 tangent, int sectionIndex, TrackSectionKind kind, float safeSpeed)
        { Position = position; Tangent = tangent; SectionIndex = sectionIndex; Kind = kind; SafeSpeed = safeSpeed; }
        public Vec2 Position { get; }
        public Vec2 Tangent { get; }
        public int SectionIndex { get; }
        public TrackSectionKind Kind { get; }
        public float SafeSpeed { get; }
    }

    public sealed class TrackDefinition
    {
        private readonly TrackSegment[] segments;
        private readonly float[] starts;

        public TrackDefinition(IEnumerable<TrackSegment> segments)
        {
            this.segments = segments?.ToArray() ?? throw new ArgumentNullException(nameof(segments));
            if (this.segments.Length < 2) throw new ArgumentException("A closed track needs at least two segments.", nameof(segments));
            starts = new float[this.segments.Length];
            float total = 0f;
            for (int i = 0; i < this.segments.Length; i++) { starts[i] = total; total += this.segments[i].Length; }
            Length = total;
        }

        public IReadOnlyList<TrackSegment> Segments => segments;
        public float Length { get; }

        public TrackSample Sample(float distance)
        {
            float wrapped = Wrap(distance, Length);
            int index = segments.Length - 1;
            for (int i = 0; i < segments.Length; i++)
                if (wrapped < starts[i] + segments[i].Length) { index = i; break; }
            var segment = segments[index];
            float t = (wrapped - starts[index]) / segment.Length;
            float dx = segment.End.X - segment.Start.X, dy = segment.End.Y - segment.Start.Y;
            return new TrackSample(
                new Vec2(segment.Start.X + dx * t, segment.Start.Y + dy * t),
                new Vec2(dx / segment.Length, dy / segment.Length), index, segment.Kind, segment.SafeSpeed);
        }

        public static TrackDefinition Placeholder(float cornerSafeSpeed = Pace.CornerSafeSpeed)
        {
            var p = new[]
            {
                new Vec2(480f, 230f), new Vec2(1440f, 230f), new Vec2(1650f, 440f), new Vec2(1650f, 640f),
                new Vec2(1440f, 850f), new Vec2(480f, 850f), new Vec2(270f, 640f), new Vec2(270f, 440f)
            };
            var kinds = new[]
            {
                TrackSectionKind.Straight, TrackSectionKind.Corner, TrackSectionKind.Straight, TrackSectionKind.Corner,
                TrackSectionKind.Straight, TrackSectionKind.Corner, TrackSectionKind.Straight, TrackSectionKind.Corner
            };
            var result = new TrackSegment[p.Length];
            for (int i = 0; i < p.Length; i++)
                result[i] = new TrackSegment(p[i], p[(i + 1) % p.Length], kinds[i],
                    kinds[i] == TrackSectionKind.Corner ? cornerSafeSpeed : float.PositiveInfinity);
            return new TrackDefinition(result);
        }

        private static float Wrap(float value, float length)
        {
            value %= length;
            return value < 0f ? value + length : value;
        }
    }

    // Lateral position as a modeled quantity (issue #147). The game always
    // races with it — there is no setting, and the presentation-only formation
    // it replaced has been deleted. It stays optional at the RULES level for
    // the same reason ConditionRules and PitRules are: a unit test pinning pit
    // pacing or slipstream geometry on a synthetic two-segment track wants one
    // mechanic in isolation, and those tracks have no meaningful curvature for
    // a racing line to mean anything on.
    //
    // The model is deliberately small. A car holds a signed offset from the
    // racing line, and that offset costs or saves real distance against the
    // local curvature — the inside of a corner IS shorter, which is the whole
    // point. Cars cannot drive through each other, expressed as a speed cap on
    // the follower rather than as a shove, so position stays a consequence of
    // integrating speed and never jumps. Line choice is automatic and blind to
    // PlayerId: take the inside unless someone is already there.
    public readonly struct LateralRules
    {
        public LateralRules(float maximumOffset, float moveRate, float minimumGap,
            float lookAhead, float sameLineWidth, float pathCostScale)
        {
            var values = new[] { maximumOffset, moveRate, minimumGap, lookAhead,
                sameLineWidth, pathCostScale };
            if (values.Any(x => float.IsNaN(x) || float.IsInfinity(x) || x <= 0f))
                throw new ArgumentException("Lateral rules must contain finite positive values.");
            if (pathCostScale > 1f)
                throw new ArgumentException("The path cost cannot exceed the real geometry.");
            Enabled = true; MaximumOffset = maximumOffset; MoveRate = moveRate;
            MinimumGap = minimumGap; LookAhead = lookAhead; SameLineWidth = sameLineWidth;
            PathCostScale = pathCostScale;
        }
        public bool Enabled { get; }
        // How far off the racing line a car may run, each way.
        public float MaximumOffset { get; }
        // px of lateral travel per second. The drawn body slides at this rate
        // because the car does, so there is nothing left to smooth.
        public float MoveRate { get; }
        // Centerline gap a follower is held to behind a car on its line.
        // A body length plus margin: the cap reads the leader's speed at the
        // top of the step, and a leader that scrubs into a corner after that
        // sheds speed the follower did not plan for, so the gap has to absorb
        // one step of it.
        public float MinimumGap { get; }
        // How far ahead a car looks when choosing its line.
        public float LookAhead { get; }
        // Lateral distance within which two cars count as sharing a line, so
        // one blocks the other: a body width.
        public float SameLineWidth { get; }
        // How much of the true geometric cost of the wider arc to charge
        // (owner report from hardware: being caught outside a big corner is
        // still a significant penalty). The full price is too much here, and
        // the reason is that the offsets are not racing-line offsets — the
        // ±16 exists so two 26 px bodies fit side by side on a 64 px ribbon.
        // Against a 72 px hairpin that is a 22% difference in radius, where a
        // real racing line differs by two or three percent, so charging the
        // literal geometry over-taxes an offset that is really there for
        // legibility. Half price keeps the inside genuinely better without
        // making the outside a sentence.
        //
        // Note this is only half the story: the wider arc's reward — a higher
        // cornering limit — cannot pay at all while the cars are under that
        // limit, and at Drive (180) they are, since corners clear at 190. The
        // outside only earns anything back under Boost. Making corners
        // genuinely grip-limited at Drive is a pace-dial question, not one for
        // this rule.
        public float PathCostScale { get; }

        // A body length ahead, a body width across, and a lateral move that
        // crosses the 32 px between the two lanes in about a second.
        public static LateralRules Defaults => new LateralRules(
            maximumOffset: 16f, moveRate: 34f, minimumGap: 62f,
            lookAhead: 150f, sameLineWidth: 27f, pathCostScale: .5f);
    }

    public readonly struct RaceRules
    {
        public RaceRules(int laps, float countdownSeconds, float maxSpeed, float acceleration, float drag,
            float braking, float cornerSpeedScrub, float cornerRecoverySeconds, float recoveryAccelerationScale,
            float passingDistance, float passingOffset, float rematchHoldSeconds, int requiredServiceCount = 0,
            ConditionRules conditionRules = default, PitRules pitRules = default, float pauseClearSeconds = 2f,
            float slipstreamBonus = 0f, float slipstreamWindow = 0f, LateralRules lateralRules = default)
        {
            var scalarValues = new[] { countdownSeconds, maxSpeed, acceleration, drag, braking, cornerSpeedScrub,
                cornerRecoverySeconds, recoveryAccelerationScale, passingDistance, passingOffset, rematchHoldSeconds,
                pauseClearSeconds, slipstreamBonus, slipstreamWindow };
            if (scalarValues.Any(x => float.IsNaN(x) || float.IsInfinity(x)))
                throw new ArgumentException("Race rules must contain finite values.");
            if (laps < 1 || countdownSeconds < 0f || maxSpeed <= 0f || acceleration <= 0f || drag <= 0f || braking <= 0f)
                throw new ArgumentException("Race rules contain invalid non-positive values.");
            if (cornerSpeedScrub <= 0f || cornerSpeedScrub > 1f || cornerRecoverySeconds < 0f ||
                recoveryAccelerationScale <= 0f || recoveryAccelerationScale > 1f || passingDistance < 0f ||
                passingOffset < 0f || rematchHoldSeconds <= 0f || requiredServiceCount < 0 ||
                pauseClearSeconds <= 0f || slipstreamBonus < 0f || slipstreamWindow < 0f)
                throw new ArgumentException("Race rules contain invalid strategy or presentation values.");
            if (requiredServiceCount > 0 && !pitRules.Enabled)
                throw new ArgumentException("A required service count needs an enabled pit lifecycle.");
            Laps = laps; CountdownSeconds = countdownSeconds; MaxSpeed = maxSpeed; Acceleration = acceleration;
            Drag = drag; Braking = braking; CornerSpeedScrub = cornerSpeedScrub;
            CornerRecoverySeconds = cornerRecoverySeconds; RecoveryAccelerationScale = recoveryAccelerationScale;
            PassingDistance = passingDistance; PassingOffset = passingOffset; RematchHoldSeconds = rematchHoldSeconds;
            RequiredServiceCount = requiredServiceCount; Conditions = conditionRules; Pit = pitRules;
            PauseClearSeconds = pauseClearSeconds;
            SlipstreamBonus = slipstreamBonus; SlipstreamWindow = slipstreamWindow;
            Lateral = lateralRules;
        }
        public LateralRules Lateral { get; }
        public int Laps { get; }
        public float CountdownSeconds { get; }
        public float MaxSpeed { get; }
        public float Acceleration { get; }
        public float Drag { get; }
        public float Braking { get; }
        public float CornerSpeedScrub { get; }
        public float CornerRecoverySeconds { get; }
        public float RecoveryAccelerationScale { get; }
        public float PassingDistance { get; }
        public float PassingOffset { get; }
        public float RematchHoldSeconds { get; }
        public int RequiredServiceCount { get; }
        public ConditionRules Conditions { get; }
        public PitRules Pit { get; }
        // How long every unfinished racer's Ship must stay off the table mid-race
        // before the race pauses — long enough that hands sweeping over the sensors
        // never read as a deliberate table clear.
        public float PauseClearSeconds { get; }
        // The slipstream tow (issue #118): trailing within the window of any
        // car ahead on a straight adds this to the throttle target — REAL
        // jockeying, self-balancing because the passer becomes the passee.
        // Zero disables the mechanic (the pre-#118 sim).
        public float SlipstreamBonus { get; }
        public float SlipstreamWindow { get; }
        // Speeds derive from the pace scalar (issue #116) so the defaults —
        // and every balance test built on them — follow a pace retune.
        // The 16 px passing offset pairs with the 54×26 car bodies (issue
        // #117 round 2; tightened from 18 on owner hardware review — racing
        // close): ±16 leaves 26-wide bodies a 6 px seam of daylight and a
        // side-by-side pair stays on the 64 px track ribbon — the old ±38
        // hung both cars off the pavement.
        // The slipstream window is geometry (like the passing distance), not
        // pace: it reaches beyond the passing split so the reel-in starts
        // before the cars go two-wide.
        public const float DefaultSlipstreamWindow = 150f;
        // The same rules, racing with a modeled lateral position. Tests that
        // pin a mechanic in isolation take the defaults; tests about how cars
        // sit on the road ask for this.
        public RaceRules WithLateral(LateralRules lateral) => new RaceRules(
            Laps, CountdownSeconds, MaxSpeed, Acceleration, Drag, Braking, CornerSpeedScrub,
            CornerRecoverySeconds, RecoveryAccelerationScale, PassingDistance, PassingOffset,
            RematchHoldSeconds, RequiredServiceCount, Conditions, Pit, PauseClearSeconds,
            SlipstreamBonus, SlipstreamWindow, lateral);

        public static RaceRules Defaults => new RaceRules(5, 3f, Pace.BasePace, Pace.Acceleration,
            Pace.Drag, Pace.Braking, .55f, 1f, .35f, 180f, 16f, 1f,
            slipstreamBonus: Pace.SlipstreamBonus, slipstreamWindow: DefaultSlipstreamWindow);
        public static RaceRules TrancheThreeDefaults =>
            new RaceRules(5, 3f, Pace.BasePace, Pace.Acceleration, Pace.Drag, Pace.Braking,
                .55f, 1f, .35f, 180f, 16f, 1f, 1, ConditionRules.Defaults, PitRules.Defaults,
                slipstreamBonus: Pace.SlipstreamBonus, slipstreamWindow: DefaultSlipstreamWindow);
    }

    public readonly struct ConditionRules
    {
        public ConditionRules(float fuelBurnPerSecondAtDrive, float fuelBurnPerSecondAtBoost,
            float fuelWarningThreshold, float emptyMaximumSpeedScale, float emptyAccelerationScale,
            float tireWearPerCorner, float tireWearPerUnsafeSpeed, float tirePenaltyThreshold,
            float fullyWornSafeSpeedScale)
        {
            var values = new[] { fuelBurnPerSecondAtDrive, fuelBurnPerSecondAtBoost, fuelWarningThreshold,
                emptyMaximumSpeedScale, emptyAccelerationScale, tireWearPerCorner, tireWearPerUnsafeSpeed,
                tirePenaltyThreshold, fullyWornSafeSpeedScale };
            if (values.Any(x => float.IsNaN(x) || float.IsInfinity(x)) || fuelBurnPerSecondAtDrive <= 0f ||
                fuelBurnPerSecondAtBoost < fuelBurnPerSecondAtDrive || fuelWarningThreshold <= 0f ||
                fuelWarningThreshold >= 1f || emptyMaximumSpeedScale <= 0f || emptyMaximumSpeedScale > 1f ||
                emptyAccelerationScale <= 0f || emptyAccelerationScale > 1f ||
                tireWearPerCorner < 0f || tireWearPerUnsafeSpeed < 0f ||
                tirePenaltyThreshold <= 0f || tirePenaltyThreshold >= 1f || fullyWornSafeSpeedScale <= 0f ||
                fullyWornSafeSpeedScale > 1f)
                throw new ArgumentException("Condition rules contain invalid values.");
            Enabled = true; FuelBurnPerSecondAtDrive = fuelBurnPerSecondAtDrive;
            FuelBurnPerSecondAtBoost = fuelBurnPerSecondAtBoost; FuelWarningThreshold = fuelWarningThreshold;
            EmptyMaximumSpeedScale = emptyMaximumSpeedScale; EmptyAccelerationScale = emptyAccelerationScale;
            TireWearPerCorner = tireWearPerCorner; TireWearPerUnsafeSpeed = tireWearPerUnsafeSpeed;
            TirePenaltyThreshold = tirePenaltyThreshold; FullyWornSafeSpeedScale = fullyWornSafeSpeedScale;
        }
        public bool Enabled { get; }
        public float FuelBurnPerSecondAtDrive { get; }
        public float FuelBurnPerSecondAtBoost { get; }
        public float FuelWarningThreshold { get; }
        public float EmptyMaximumSpeedScale { get; }
        public float EmptyAccelerationScale { get; }
        public float TireWearPerCorner { get; }
        public float TireWearPerUnsafeSpeed { get; }
        public float TirePenaltyThreshold { get; }
        public float FullyWornSafeSpeedScale { get; }
        public static ConditionRules Disabled => default;
        public static ConditionRules Defaults => new ConditionRules(.008f, .04f, .75f, .35f, .5f, .015f, .08f, .6f, .75f);
    }

    public readonly struct PitRules
    {
        private readonly float[] entryLengths, exitLengths;

        public PitRules(float laneSpeed, float playerOneEntryLength, float playerOneExitLength,
            float playerTwoEntryLength, float playerTwoExitLength, float exitRejoinDistance = 0f)
            : this(laneSpeed,
                new[] { playerOneEntryLength, playerTwoEntryLength },
                new[] { playerOneExitLength, playerTwoExitLength },
                exitRejoinDistance)
        {
        }

        public PitRules(float laneSpeed, IReadOnlyList<float> entryLengths,
            IReadOnlyList<float> exitLengths, float exitRejoinDistance = 0f)
        {
            if (entryLengths == null) throw new ArgumentNullException(nameof(entryLengths));
            if (exitLengths == null) throw new ArgumentNullException(nameof(exitLengths));
            if (entryLengths.Count < 2 || entryLengths.Count > 4 ||
                exitLengths.Count != entryLengths.Count)
                throw new ArgumentException("Pit rules require matching lengths for two to four racers.");
            float[] entries = entryLengths.ToArray(), exits = exitLengths.ToArray();
            var values = entries.Concat(exits).Concat(new[] { laneSpeed, exitRejoinDistance });
            if (values.Any(x => float.IsNaN(x) || float.IsInfinity(x)) || laneSpeed <= 0f ||
                entries.Any(x => x <= 0f) || exits.Any(x => x <= 0f) || exitRejoinDistance < 0f)
                throw new ArgumentException("Pit rules contain invalid values.");
            Enabled = true; LaneSpeed = laneSpeed;
            this.entryLengths = entries;
            this.exitLengths = exits;
            ExitRejoinDistance = exitRejoinDistance;
        }
        public bool Enabled { get; }
        // The pit-lane crawl in reference px/s — a ratio of the pace dial
        // (Pace.PitLaneSpeedRatio, issues #110/#116).
        public float LaneSpeed { get; }
        // Track distance past the start/finish line where the pit lane rejoins the
        // track: the car resumes where the lane physically ends instead of doubling
        // back to the line.
        public float ExitRejoinDistance { get; }

        // Pit transit is paced by distance (issue #110): a leg's duration is its
        // lane length at the crawl, so the two players' different box positions
        // get honest, different transit times — and lane-geometry changes (new
        // courses, #107) keep pacing right automatically. The old shared fixed
        // duration covered Player 1's ~500 px exit in 0.75 s: the drawn car
        // launched out of the pit at 2-3× its racing top speed.
        public float EntrySeconds(PlayerId playerId) => EntryLength(playerId) / LaneSpeed;
        public float ExitSeconds(PlayerId playerId) => ExitLength(playerId) / LaneSpeed;
        public float EntryLength(PlayerId playerId) => LengthFor(entryLengths, playerId);
        public float ExitLength(PlayerId playerId) => LengthFor(exitLengths, playerId);

        // Leg lengths measured along the authored lane anchors — the same points
        // the drawn splines run through (PitLanePresentationLayout.ForCourse):
        // pit line → entry ramp → box, and box → merge approach → rejoin. The
        // anchor polyline stands in for the spline's arc length; the spline hugs
        // it within a few percent, against the 2-3× error of the fixed duration.
        public static PitRules ForCourse(CourseDefinition course, float laneSpeed)
        {
            Vec2 pitLine = course.Track.Sample(0f).Position;
            Vec2 rejoin = course.Track.Sample(course.Pit.ExitRejoinDistance).Position;
            return new PitRules(laneSpeed,
                course.Pit.Boxes.Select(box => Length(pitLine, course.Pit.Entry, box)).ToArray(),
                course.Pit.Boxes.Select(box => Length(box, course.Pit.MergeApproach, rejoin)).ToArray(),
                course.Pit.ExitRejoinDistance);
        }

        private static float LengthFor(float[] lengths, PlayerId playerId)
        {
            int index = (int)playerId - 1;
            if (lengths == null || index < 0 || index >= lengths.Length)
                throw new ArgumentOutOfRangeException(nameof(playerId),
                    "Pit rules have no route for that racer.");
            return lengths[index];
        }

        private static float Length(Vec2 a, Vec2 b, Vec2 c)
        {
            float abX = b.X - a.X, abY = b.Y - a.Y, bcX = c.X - b.X, bcY = c.Y - b.Y;
            return (float)(Math.Sqrt(abX * abX + abY * abY) + Math.Sqrt(bcX * bcX + bcY * bcY));
        }

        public static PitRules Disabled => default;
        // The Wedge complex at the reference crawl — the pit economics the
        // balance tests run on.
        public static PitRules Defaults => ForCourse(CourseCatalog.Wedge(), Pace.PitLaneSpeed);
    }

    public readonly struct RacerCommand
    {
        public RacerCommand(PlayerId playerId, ThrottleStep throttle, bool drivingPiecePresent, bool rematchConfirming)
            : this(playerId, throttle, drivingPiecePresent, rematchConfirming, PitService.None, false, 0f) { }

        public RacerCommand(PlayerId playerId, ThrottleStep throttle, bool drivingPiecePresent, bool rematchConfirming,
            PitService selectedService, bool requestPit, float serviceDrain, bool requestExit = false)
        {
            if (!Enum.IsDefined(typeof(PitService), selectedService) || serviceDrain < 0f ||
                serviceDrain > 1f || float.IsNaN(serviceDrain))
                throw new ArgumentException("Racer strategy command contains invalid values.");
            PlayerId = playerId; Throttle = throttle; DrivingPiecePresent = drivingPiecePresent;
            RematchConfirming = rematchConfirming;
            SelectedService = selectedService; RequestPit = requestPit;
            ServiceDrain = serviceDrain; RequestExit = requestExit;
        }
        public PlayerId PlayerId { get; }
        public ThrottleStep Throttle { get; }
        public bool DrivingPiecePresent { get; }
        public bool RematchConfirming { get; }
        public PitService SelectedService { get; }
        public bool RequestPit { get; }
        public float ServiceDrain { get; }
        public bool RequestExit { get; }
    }

    public readonly struct RacerConditionSnapshot
    {
        public RacerConditionSnapshot(float fuelUsed, float tireWear, bool fuelPenaltyActive, bool tirePenaltyActive)
        {
            if (fuelUsed < 0f || fuelUsed > 1f || tireWear < 0f || tireWear > 1f ||
                float.IsNaN(fuelUsed) || float.IsNaN(tireWear))
                throw new ArgumentException("Condition values must be normalized.");
            FuelUsed = fuelUsed; TireWear = tireWear;
            FuelPenaltyActive = fuelPenaltyActive; TirePenaltyActive = tirePenaltyActive;
        }
        public float FuelUsed { get; }
        public float TireWear { get; }
        public bool FuelPenaltyActive { get; }
        public bool TirePenaltyActive { get; }
    }

    public readonly struct RacerPitSnapshot
    {
        public RacerPitSnapshot(PitService selectedService, PitPhase phase, float serviceProgress,
            int completedServices, bool finishEligible, float phaseProgress = 0f)
        {
            if (!Enum.IsDefined(typeof(PitService), selectedService) || !Enum.IsDefined(typeof(PitPhase), phase) ||
                serviceProgress < 0f || serviceProgress > 1f || float.IsNaN(serviceProgress) ||
                phaseProgress < 0f || phaseProgress > 1f || float.IsNaN(phaseProgress) || completedServices < 0)
                throw new ArgumentException("Pit snapshot contains invalid values.");
            SelectedService = selectedService; Phase = phase; ServiceProgress = serviceProgress;
            CompletedServices = completedServices; FinishEligible = finishEligible; PhaseProgress = phaseProgress;
        }
        public PitService SelectedService { get; }
        public PitPhase Phase { get; }
        public float ServiceProgress { get; }
        public int CompletedServices { get; }
        public bool FinishEligible { get; }
        public float PhaseProgress { get; }
    }

    public readonly struct RacerSnapshot
    {
        public RacerSnapshot(PlayerId playerId, float speed, float totalDistance, int completedLaps, int place,
            bool finished, float finishTime, TrackSample track, float lateralOffset, bool incidentThisStep,
            float recoveryRemaining, int incidentCount, RacerConditionSnapshot condition, RacerPitSnapshot pit,
            float longitudinalOffset = 0f)
        {
            PlayerId = playerId; Speed = speed; TotalDistance = totalDistance; CompletedLaps = completedLaps;
            Place = place; Finished = finished; FinishTime = finishTime; Track = track; LateralOffset = lateralOffset;
            IncidentThisStep = incidentThisStep; RecoveryRemaining = recoveryRemaining; IncidentCount = incidentCount;
            Condition = condition; Pit = pit; LongitudinalOffset = longitudinalOffset;
        }
        public PlayerId PlayerId { get; }
        public float Speed { get; }
        public float TotalDistance { get; }
        public int CompletedLaps { get; }
        public int Place { get; }
        public bool Finished { get; }
        public float FinishTime { get; }
        public TrackSample Track { get; }
        public float LateralOffset { get; }
        public float LongitudinalOffset { get; }
        public bool IncidentThisStep { get; }
        public float RecoveryRemaining { get; }
        public int IncidentCount { get; }
        public RacerConditionSnapshot Condition { get; }
        public RacerPitSnapshot Pit { get; }
    }

    public readonly struct RaceSnapshot
    {
        public RaceSnapshot(RacePhase phase, float countdownRemaining, float elapsedSeconds,
            IReadOnlyList<RacerSnapshot> racers, float rematchProgress, bool awaitingRematchRelease)
        { Phase = phase; CountdownRemaining = countdownRemaining; ElapsedSeconds = elapsedSeconds; Racers = racers;
          RematchProgress = rematchProgress; AwaitingRematchRelease = awaitingRematchRelease; }
        public RacePhase Phase { get; }
        public float CountdownRemaining { get; }
        public float ElapsedSeconds { get; }
        public IReadOnlyList<RacerSnapshot> Racers { get; }
        public float RematchProgress { get; }
        public bool AwaitingRematchRelease { get; }
    }

    public readonly struct ThrottleTracePoint
    {
        public ThrottleTracePoint(float time, ThrottleStep throttle) { Time = time; Throttle = throttle; }
        public float Time { get; }
        public ThrottleStep Throttle { get; }
    }

    public sealed class ScriptedThrottleTrace
    {
        private readonly ThrottleTracePoint[] points;
        public ScriptedThrottleTrace(IEnumerable<ThrottleTracePoint> points)
        { this.points = points?.OrderBy(x => x.Time).ToArray() ?? throw new ArgumentNullException(nameof(points)); }
        public ThrottleStep At(float time)
        {
            ThrottleStep result = ThrottleStep.Brake;
            foreach (var point in points) { if (point.Time > time) break; result = point.Throttle; }
            return result;
        }
    }
}
