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

    public readonly struct RaceRules
    {
        public RaceRules(int laps, float countdownSeconds, float maxSpeed, float acceleration, float drag,
            float braking, float cornerSpeedScrub, float cornerRecoverySeconds, float recoveryAccelerationScale,
            float passingDistance, float passingOffset, float rematchHoldSeconds, int requiredServiceCount = 0,
            ConditionRules conditionRules = default, PitRules pitRules = default, float pauseClearSeconds = 2f)
        {
            var scalarValues = new[] { countdownSeconds, maxSpeed, acceleration, drag, braking, cornerSpeedScrub,
                cornerRecoverySeconds, recoveryAccelerationScale, passingDistance, passingOffset, rematchHoldSeconds,
                pauseClearSeconds };
            if (scalarValues.Any(x => float.IsNaN(x) || float.IsInfinity(x)))
                throw new ArgumentException("Race rules must contain finite values.");
            if (laps < 1 || countdownSeconds < 0f || maxSpeed <= 0f || acceleration <= 0f || drag <= 0f || braking <= 0f)
                throw new ArgumentException("Race rules contain invalid non-positive values.");
            if (cornerSpeedScrub <= 0f || cornerSpeedScrub > 1f || cornerRecoverySeconds < 0f ||
                recoveryAccelerationScale <= 0f || recoveryAccelerationScale > 1f || passingDistance < 0f ||
                passingOffset < 0f || rematchHoldSeconds <= 0f || requiredServiceCount < 0 ||
                pauseClearSeconds <= 0f)
                throw new ArgumentException("Race rules contain invalid strategy or presentation values.");
            if (requiredServiceCount > 0 && !pitRules.Enabled)
                throw new ArgumentException("A required service count needs an enabled pit lifecycle.");
            Laps = laps; CountdownSeconds = countdownSeconds; MaxSpeed = maxSpeed; Acceleration = acceleration;
            Drag = drag; Braking = braking; CornerSpeedScrub = cornerSpeedScrub;
            CornerRecoverySeconds = cornerRecoverySeconds; RecoveryAccelerationScale = recoveryAccelerationScale;
            PassingDistance = passingDistance; PassingOffset = passingOffset; RematchHoldSeconds = rematchHoldSeconds;
            RequiredServiceCount = requiredServiceCount; Conditions = conditionRules; Pit = pitRules;
            PauseClearSeconds = pauseClearSeconds;
        }
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
        // Speeds derive from the pace scalar (issue #116) so the defaults —
        // and every balance test built on them — follow a pace retune.
        // The 18 px passing offset pairs with the 54×30 car bodies (issue
        // #117 round 2): ±18 gives 30-wide bodies visible daylight while a
        // side-by-side pair stays on the 64 px track ribbon — the old ±38
        // hung both cars off the pavement.
        public static RaceRules Defaults => new RaceRules(5, 3f, Pace.BasePace, Pace.Acceleration,
            Pace.Drag, Pace.Braking, .55f, 1f, .35f, 180f, 18f, 1f);
        public static RaceRules TrancheThreeDefaults =>
            new RaceRules(5, 3f, Pace.BasePace, Pace.Acceleration, Pace.Drag, Pace.Braking,
                .55f, 1f, .35f, 180f, 18f, 1f, 1, ConditionRules.Defaults, PitRules.Defaults);
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
        private readonly float playerOneEntryLength, playerOneExitLength;
        private readonly float playerTwoEntryLength, playerTwoExitLength;

        public PitRules(float laneSpeed, float playerOneEntryLength, float playerOneExitLength,
            float playerTwoEntryLength, float playerTwoExitLength, float exitRejoinDistance = 0f)
        {
            var values = new[] { laneSpeed, playerOneEntryLength, playerOneExitLength,
                playerTwoEntryLength, playerTwoExitLength, exitRejoinDistance };
            if (values.Any(x => float.IsNaN(x) || float.IsInfinity(x)) || laneSpeed <= 0f ||
                playerOneEntryLength <= 0f || playerOneExitLength <= 0f ||
                playerTwoEntryLength <= 0f || playerTwoExitLength <= 0f || exitRejoinDistance < 0f)
                throw new ArgumentException("Pit rules contain invalid values.");
            Enabled = true; LaneSpeed = laneSpeed;
            this.playerOneEntryLength = playerOneEntryLength;
            this.playerOneExitLength = playerOneExitLength;
            this.playerTwoEntryLength = playerTwoEntryLength;
            this.playerTwoExitLength = playerTwoExitLength;
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
        public float EntryLength(PlayerId playerId) =>
            playerId == PlayerId.Player1 ? playerOneEntryLength : playerTwoEntryLength;
        public float ExitLength(PlayerId playerId) =>
            playerId == PlayerId.Player1 ? playerOneExitLength : playerTwoExitLength;

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
                Length(pitLine, course.Pit.Entry, course.Pit.PlayerOneBox),
                Length(course.Pit.PlayerOneBox, course.Pit.MergeApproach, rejoin),
                Length(pitLine, course.Pit.Entry, course.Pit.PlayerTwoBox),
                Length(course.Pit.PlayerTwoBox, course.Pit.MergeApproach, rejoin),
                course.Pit.ExitRejoinDistance);
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
            float recoveryRemaining, int incidentCount, RacerConditionSnapshot condition, RacerPitSnapshot pit)
        {
            PlayerId = playerId; Speed = speed; TotalDistance = totalDistance; CompletedLaps = completedLaps;
            Place = place; Finished = finished; FinishTime = finishTime; Track = track; LateralOffset = lateralOffset;
            IncidentThisStep = incidentThisStep; RecoveryRemaining = recoveryRemaining; IncidentCount = incidentCount;
            Condition = condition; Pit = pit;
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
