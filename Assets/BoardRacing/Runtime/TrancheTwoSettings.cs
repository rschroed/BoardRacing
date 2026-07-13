using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    [CreateAssetMenu(menuName = "Board Racing/Tranche Two Settings", fileName = "TrancheTwoSettings")]
    public sealed class TrancheTwoSettings : ScriptableObject
    {
        public bool preferBoardInputOnDevice = true;
        public float fixedStepSeconds = 1f / 60f;
        public int laps = 5;
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

        public RaceRules ToRules() => new RaceRules(laps, countdownSeconds, maximumSpeed, acceleration, drag,
            braking, cornerSpeedScrub, cornerRecoverySeconds, recoveryAccelerationScale,
            passingDistance, passingOffset, rematchHoldSeconds);

        public static TrancheTwoSettings Defaults() => CreateInstance<TrancheTwoSettings>();
    }
}
