using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    [CreateAssetMenu(menuName = "Board Racing/Tranche Three Settings", fileName = "TrancheThreeSettings")]
    public sealed class TrancheThreeSettings : ScriptableObject
    {
        [Header("Condition model")]
        public float heatGainPerSecondAtFullThrottle = .045f;
        public float heatCoolingPerSecond = .08f;
        [Range(0f, 1f)] public float heatPenaltyThreshold = .7f;
        [Range(0f, 1f)] public float heatedMaximumSpeedScale = .6f;
        [Range(0f, 1f)] public float heatedAccelerationScale = .5f;
        public float tireWearPerCorner = .015f;
        public float tireWearPerUnsafeSpeed = .08f;
        [Range(0f, 1f)] public float tirePenaltyThreshold = .6f;
        [Range(0f, 1f)] public float fullyWornSafeSpeedScale = .75f;

        [Header("Pit lifecycle")]
        [Min(0)] public int requiredServiceCount = 1;
        [Min(.01f)] public float pitEntrySeconds = .75f;
        [Min(.01f)] public float pitExitSeconds = .75f;
        [Min(.01f)] public float pitCallHoldSeconds = .75f;

        [Header("Crew service regions")]
        [Min(1f)] public float serviceOffsetX = 190f;
        public Vector2 serviceHalfSize = new Vector2(140f, 120f);

        public ConditionRules ToConditionRules() => new ConditionRules(heatGainPerSecondAtFullThrottle,
            heatCoolingPerSecond, heatPenaltyThreshold, heatedMaximumSpeedScale, heatedAccelerationScale,
            tireWearPerCorner, tireWearPerUnsafeSpeed, tirePenaltyThreshold, fullyWornSafeSpeedScale);

        public PitRules ToPitRules() => new PitRules(pitEntrySeconds, pitExitSeconds);

        public static TrancheThreeSettings Defaults()
        {
            var result = CreateInstance<TrancheThreeSettings>();
            result.hideFlags = HideFlags.HideAndDontSave;
            return result;
        }
    }
}
