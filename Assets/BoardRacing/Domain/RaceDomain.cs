using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardRacing.Domain
{
    public enum RacePhase { Grid, Countdown, Racing, Finished }
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

        public static TrackDefinition Placeholder(float cornerSafeSpeed = 190f)
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
            ConditionRules conditionRules = default, PitRules pitRules = default)
        {
            var scalarValues = new[] { countdownSeconds, maxSpeed, acceleration, drag, braking, cornerSpeedScrub,
                cornerRecoverySeconds, recoveryAccelerationScale, passingDistance, passingOffset, rematchHoldSeconds };
            if (scalarValues.Any(x => float.IsNaN(x) || float.IsInfinity(x)))
                throw new ArgumentException("Race rules must contain finite values.");
            if (laps < 1 || countdownSeconds < 0f || maxSpeed <= 0f || acceleration <= 0f || drag <= 0f || braking <= 0f)
                throw new ArgumentException("Race rules contain invalid non-positive values.");
            if (cornerSpeedScrub <= 0f || cornerSpeedScrub > 1f || cornerRecoverySeconds < 0f ||
                recoveryAccelerationScale <= 0f || recoveryAccelerationScale > 1f || passingDistance < 0f ||
                passingOffset < 0f || rematchHoldSeconds <= 0f || requiredServiceCount < 0)
                throw new ArgumentException("Race rules contain invalid strategy or presentation values.");
            if (requiredServiceCount > 0 && !pitRules.Enabled)
                throw new ArgumentException("A required service count needs an enabled pit lifecycle.");
            Laps = laps; CountdownSeconds = countdownSeconds; MaxSpeed = maxSpeed; Acceleration = acceleration;
            Drag = drag; Braking = braking; CornerSpeedScrub = cornerSpeedScrub;
            CornerRecoverySeconds = cornerRecoverySeconds; RecoveryAccelerationScale = recoveryAccelerationScale;
            PassingDistance = passingDistance; PassingOffset = passingOffset; RematchHoldSeconds = rematchHoldSeconds;
            RequiredServiceCount = requiredServiceCount; Conditions = conditionRules; Pit = pitRules;
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
        public static RaceRules Defaults => new RaceRules(5, 3f, 360f, 220f, 120f, 300f, .55f, 1f, .35f, 180f, 38f, 1f);
        public static RaceRules TrancheThreeDefaults =>
            new RaceRules(5, 3f, 360f, 220f, 120f, 300f, .55f, 1f, .35f, 180f, 38f, 1f, 1,
                ConditionRules.Defaults, PitRules.Defaults);
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
        public PitRules(float entrySeconds, float exitSeconds, float exitRejoinDistance = 0f)
        {
            if (float.IsNaN(entrySeconds) || float.IsInfinity(entrySeconds) ||
                float.IsNaN(exitSeconds) || float.IsInfinity(exitSeconds) || entrySeconds <= 0f || exitSeconds <= 0f ||
                float.IsNaN(exitRejoinDistance) || float.IsInfinity(exitRejoinDistance) || exitRejoinDistance < 0f)
                throw new ArgumentException("Pit rules contain invalid values.");
            Enabled = true; EntrySeconds = entrySeconds; ExitSeconds = exitSeconds;
            ExitRejoinDistance = exitRejoinDistance;
        }
        public bool Enabled { get; }
        public float EntrySeconds { get; }
        public float ExitSeconds { get; }
        // Track distance past the start/finish line where the pit lane rejoins the
        // track: the car resumes where the lane physically ends instead of doubling
        // back to the line.
        public float ExitRejoinDistance { get; }
        public static PitRules Disabled => default;
        public static PitRules Defaults => new PitRules(.75f, .75f);
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
