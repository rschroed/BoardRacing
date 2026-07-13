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
}
