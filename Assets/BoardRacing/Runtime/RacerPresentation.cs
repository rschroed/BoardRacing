using System;
using BoardRacing.Domain;

namespace BoardRacing.Runtime
{
    public enum ConditionVisualLevel { Normal, Warning, Critical }

    public readonly struct CarConditionVisualState
    {
        public CarConditionVisualState(float heat, float tireWear,
            ConditionVisualLevel heatLevel, ConditionVisualLevel tireLevel)
        { Heat = heat; TireWear = tireWear; HeatLevel = heatLevel; TireLevel = tireLevel; }
        public float Heat { get; }
        public float TireWear { get; }
        public ConditionVisualLevel HeatLevel { get; }
        public ConditionVisualLevel TireLevel { get; }
    }

    public static class CarConditionVisualMapper
    {
        private const float WarningThresholdScale = .65f;

        public static CarConditionVisualState From(RacerSnapshot racer, ConditionRules rules) =>
            From(racer.Condition, rules);

        public static CarConditionVisualState From(RacerConditionSnapshot condition, ConditionRules rules)
        {
            if (!rules.Enabled)
                return new CarConditionVisualState(condition.Heat, condition.TireWear,
                    ConditionVisualLevel.Normal, ConditionVisualLevel.Normal);
            return new CarConditionVisualState(condition.Heat, condition.TireWear,
                Level(condition.Heat, rules.HeatPenaltyThreshold),
                Level(condition.TireWear, rules.TirePenaltyThreshold));
        }

        private static ConditionVisualLevel Level(float value, float criticalThreshold)
        {
            if (value >= criticalThreshold) return ConditionVisualLevel.Critical;
            if (value >= criticalThreshold * WarningThresholdScale) return ConditionVisualLevel.Warning;
            return ConditionVisualLevel.Normal;
        }
    }

    public readonly struct PitLanePresentationLayout
    {
        public PitLanePresentationLayout(Vec2 pitLine, Vec2 entry, Vec2 playerOneBox,
            Vec2 playerTwoBox, Vec2 exit, Vec2 returnBend, Vec2 returnLane, Vec2 mergeApproach)
        {
            PitLine = pitLine; Entry = entry; PlayerOneBox = playerOneBox;
            PlayerTwoBox = playerTwoBox; Exit = exit; ReturnBend = returnBend;
            ReturnLane = returnLane; MergeApproach = mergeApproach;
        }
        public Vec2 PitLine { get; }
        public Vec2 Entry { get; }
        public Vec2 PlayerOneBox { get; }
        public Vec2 PlayerTwoBox { get; }
        public Vec2 Exit { get; }
        public Vec2 ReturnBend { get; }
        public Vec2 ReturnLane { get; }
        public Vec2 MergeApproach { get; }
        public Vec2 Box(PlayerId playerId) => playerId == PlayerId.Player1 ? PlayerOneBox : PlayerTwoBox;
    }

    public readonly struct CarPresentationPose
    {
        public CarPresentationPose(Vec2 position, Vec2 tangent, bool finished)
        { Position = position; Tangent = tangent; Finished = finished; }
        public Vec2 Position { get; }
        public Vec2 Tangent { get; }
        public bool Finished { get; }
    }

    public static class PitLanePresentationMapper
    {
        public static CarPresentationPose From(RacerSnapshot racer, Vec2 trackPosition,
            Vec2 trackTangent, PitLanePresentationLayout layout)
        {
            Vec2 box = layout.Box(racer.PlayerId);
            if (racer.Pit.Phase == PitPhase.Entering)
                return AlongSpline(new[] { layout.PitLine, layout.Entry, box },
                    racer.Pit.PhaseProgress, racer.Finished);
            if (racer.Pit.Phase == PitPhase.InService)
                return new CarPresentationPose(box, Unit(layout.Exit, box), racer.Finished);
            if (racer.Pit.Phase == PitPhase.Exiting)
                return ExitPose(racer.PlayerId, racer.Pit.PhaseProgress, racer.Finished, layout);
            return new CarPresentationPose(trackPosition, Normalize(trackTangent), racer.Finished);
        }

        public static CarPresentationPose ExitPose(PlayerId playerId, float progress, bool finished,
            PitLanePresentationLayout layout) => AlongSpline(new[]
            {
                layout.Box(playerId), layout.Exit, layout.ReturnBend,
                layout.ReturnLane, layout.MergeApproach, layout.PitLine
            }, progress, finished);

        private static CarPresentationPose AlongSpline(Vec2[] points, float progress, bool finished)
        {
            const int SamplesPerSegment = 12;
            var samples = new Vec2[(points.Length - 1) * SamplesPerSegment + 1];
            int index = 0;
            samples[index++] = points[0];
            for (int segment = 0; segment < points.Length - 1; segment++)
            {
                Vec2 p0 = segment == 0 ? Extrapolate(points[0], points[1]) : points[segment - 1];
                Vec2 p1 = points[segment];
                Vec2 p2 = points[segment + 1];
                Vec2 p3 = segment + 2 < points.Length
                    ? points[segment + 2] : Extrapolate(points[points.Length - 1], points[points.Length - 2]);
                for (int sample = 1; sample <= SamplesPerSegment; sample++)
                    samples[index++] = CatmullRom(p0, p1, p2, p3, sample / (float)SamplesPerSegment);
            }
            return Along(samples, progress, finished);
        }

        private static Vec2 Extrapolate(Vec2 point, Vec2 neighbor) =>
            new Vec2(point.X * 2f - neighbor.X, point.Y * 2f - neighbor.Y);

        private static Vec2 CatmullRom(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return new Vec2(
                .5f * (2f * p1.X + (-p0.X + p2.X) * t +
                    (2f * p0.X - 5f * p1.X + 4f * p2.X - p3.X) * t2 +
                    (-p0.X + 3f * p1.X - 3f * p2.X + p3.X) * t3),
                .5f * (2f * p1.Y + (-p0.Y + p2.Y) * t +
                    (2f * p0.Y - 5f * p1.Y + 4f * p2.Y - p3.Y) * t2 +
                    (-p0.Y + 3f * p1.Y - 3f * p2.Y + p3.Y) * t3));
        }

        private static CarPresentationPose Along(Vec2[] points, float progress, bool finished)
        {
            float total = 0f;
            for (int i = 0; i < points.Length - 1; i++) total += Distance(points[i], points[i + 1]);
            if (total <= 0f) return new CarPresentationPose(points[points.Length - 1], new Vec2(1f, 0f), finished);
            float remaining = Math.Max(0f, Math.Min(1f, progress)) * total;
            for (int i = 0; i < points.Length - 1; i++)
            {
                float length = Distance(points[i], points[i + 1]);
                if (remaining <= length || i == points.Length - 2)
                {
                    float t = length <= 0f ? 1f : Math.Min(1f, remaining / length);
                    return new CarPresentationPose(Lerp(points[i], points[i + 1], t),
                        Unit(points[i + 1], points[i]), finished);
                }
                remaining -= length;
            }
            return new CarPresentationPose(points[points.Length - 1], new Vec2(1f, 0f), finished);
        }

        private static Vec2 Lerp(Vec2 from, Vec2 to, float t) =>
            new Vec2(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);

        private static Vec2 Unit(Vec2 to, Vec2 from) => Normalize(new Vec2(to.X - from.X, to.Y - from.Y));
        private static Vec2 Normalize(Vec2 value)
        {
            float length = (float)Math.Sqrt(value.X * value.X + value.Y * value.Y);
            return length <= .00001f ? new Vec2(1f, 0f) : new Vec2(value.X / length, value.Y / length);
        }
        private static float Distance(Vec2 a, Vec2 b)
        {
            float x = b.X - a.X, y = b.Y - a.Y;
            return (float)Math.Sqrt(x * x + y * y);
        }
    }
}
