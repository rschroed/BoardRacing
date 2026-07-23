using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    [CreateAssetMenu(menuName = "Board Racing/Tranche Two Settings", fileName = "TrancheTwoSettings")]
    public sealed class TrancheTwoSettings : ScriptableObject
    {
        public bool preferBoardInputOnDevice = true;
        public float fixedStepSeconds = 1f / 60f;
        // Laps are course data (issue #107): each course's lap count is tuned to
        // its perimeter in CourseCatalog so race duration stays consistent.
        public float countdownSeconds = 3f;
        public float maximumSpeed = 360f;
        public float acceleration = 220f;
        public float drag = 120f;
        public float braking = 300f;
        public float cornerSafeSpeed = 190f;
        [Range(.1f, 1f)] public float cornerSpeedScrub = .55f;
        public float cornerRecoverySeconds = 1f;
        [Range(.1f, 1f)] public float recoveryAccelerationScale = .35f;
        public float passingDistance = 180f;
        public float passingOffset = 38f;
        public float rematchHoldSeconds = 1f;
        // Every unfinished racer's Ship off the table for this long pauses the race
        // (issue #90). Just enough debounce that hands passing over the sensors don't
        // trigger it — 2 s read as lag on hardware, so the overlay must come up fast.
        [Min(.1f)] public float pauseClearSeconds = .75f;

        public RaceRules ToRules(int laps, int requiredServiceCount = 0, ConditionRules conditions = default,
            PitRules pit = default) => new RaceRules(laps, countdownSeconds, maximumSpeed, acceleration, drag,
            braking, cornerSpeedScrub, cornerRecoverySeconds, recoveryAccelerationScale,
            passingDistance, passingOffset, rematchHoldSeconds, requiredServiceCount, conditions, pit,
            pauseClearSeconds);

        public static TrancheTwoSettings Defaults() => CreateInstance<TrancheTwoSettings>();
    }
}
