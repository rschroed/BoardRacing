using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardRacing.Domain
{
    public enum RacePhase { Grid, Countdown, Racing, Finished }
    public enum TrackSectionKind { Straight, Corner }

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
            float passingDistance, float passingOffset, float rematchHoldSeconds)
        {
            if (laps < 1 || countdownSeconds < 0f || maxSpeed <= 0f || acceleration <= 0f || drag <= 0f || braking <= 0f)
                throw new ArgumentException("Race rules contain invalid non-positive values.");
            Laps = laps; CountdownSeconds = countdownSeconds; MaxSpeed = maxSpeed; Acceleration = acceleration;
            Drag = drag; Braking = braking; CornerSpeedScrub = cornerSpeedScrub;
            CornerRecoverySeconds = cornerRecoverySeconds; RecoveryAccelerationScale = recoveryAccelerationScale;
            PassingDistance = passingDistance; PassingOffset = passingOffset; RematchHoldSeconds = rematchHoldSeconds;
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
        public static RaceRules Defaults => new RaceRules(5, 3f, 360f, 220f, 120f, 300f, .55f, 1f, .35f, 180f, 38f, 1f);
    }

    public readonly struct RacerCommand
    {
        public RacerCommand(PlayerId playerId, ThrottleStep throttle, bool carPresent, bool carTouched)
        { PlayerId = playerId; Throttle = throttle; CarPresent = carPresent; CarTouched = carTouched; }
        public PlayerId PlayerId { get; }
        public ThrottleStep Throttle { get; }
        public bool CarPresent { get; }
        public bool CarTouched { get; }
    }

    public readonly struct RacerSnapshot
    {
        public RacerSnapshot(PlayerId playerId, float speed, float totalDistance, int completedLaps, int place,
            bool finished, float finishTime, TrackSample track, float lateralOffset, bool incidentThisStep,
            float recoveryRemaining, int incidentCount)
        {
            PlayerId = playerId; Speed = speed; TotalDistance = totalDistance; CompletedLaps = completedLaps;
            Place = place; Finished = finished; FinishTime = finishTime; Track = track; LateralOffset = lateralOffset;
            IncidentThisStep = incidentThisStep; RecoveryRemaining = recoveryRemaining; IncidentCount = incidentCount;
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
            ThrottleStep result = ThrottleStep.Off;
            foreach (var point in points) { if (point.Time > time) break; result = point.Throttle; }
            return result;
        }
    }
}
