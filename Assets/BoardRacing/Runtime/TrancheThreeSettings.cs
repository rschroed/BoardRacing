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
        // Explicit per-seat service centers around each corner cluster (wireframe-ui.md, issue #85).
        // Tires sits between Call Pit and the corner; Cooling sits along the long edge.
        public Vector2 playerOneTiresCenter = new Vector2(1620f, 310f);
        public Vector2 playerOneCoolingCenter = new Vector2(1370f, 140f);
        public Vector2 playerTwoTiresCenter = new Vector2(300f, 770f);
        public Vector2 playerTwoCoolingCenter = new Vector2(550f, 940f);
        public Vector2 serviceHalfSize = new Vector2(110f, 110f);

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
