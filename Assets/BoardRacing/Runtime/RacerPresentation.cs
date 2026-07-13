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
            Vec2 playerTwoBox, Vec2 exit)
        {
            PitLine = pitLine; Entry = entry; PlayerOneBox = playerOneBox;
            PlayerTwoBox = playerTwoBox; Exit = exit;
        }
        public Vec2 PitLine { get; }
        public Vec2 Entry { get; }
        public Vec2 PlayerOneBox { get; }
        public Vec2 PlayerTwoBox { get; }
        public Vec2 Exit { get; }
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
                return Along(new[] { layout.PitLine, layout.Entry, box }, racer.Pit.PhaseProgress, racer.Finished);
            if (racer.Pit.Phase == PitPhase.InService)
                return new CarPresentationPose(box, Unit(layout.Exit, box), racer.Finished);
            if (racer.Pit.Phase == PitPhase.Exiting)
                return Along(new[] { box, layout.Exit, layout.PitLine }, racer.Pit.PhaseProgress, racer.Finished);
            return new CarPresentationPose(trackPosition, Normalize(trackTangent), racer.Finished);
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
