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

        [Header("Pace")]
        // The ONE overall-speed dial (issue #116): top speed down a straight in
        // reference px/s. Every speed-shaped setting below is a ratio of it, so
        // a race-feel retune is this edit alone and every relationship holds.
        // Actually turning it is an owner feel review on the Board (issue #110).
        [Min(1f)] public float basePace = Pace.BasePace;
        // Speed change per second as fractions of basePace (0.61 ≈ standing
        // start to top speed in 1.6 s). At base pace 360 these reproduce the
        // owner-tuned absolutes: acceleration 220, drag 120, braking 300.
        [Range(.05f, 2f)] public float accelerationRatio = Pace.AccelerationRatio;
        [Range(.05f, 2f)] public float dragRatio = Pace.DragRatio;
        [Range(.05f, 2f)] public float brakingRatio = Pace.BrakingRatio;
        // The corner-speed baseline the per-corner course factors multiply
        // (TrackCatalog: sweeper 1.35 / medium 1.0 / tight 0.68); 190 at 360.
        [Range(.1f, 1f)] public float cornerSafeSpeedRatio = Pace.CornerSafeSpeedRatio;

        [Header("Feel and geometry — consciously off the pace dial (issue #116)")]
        [Range(.1f, 1f)] public float cornerSpeedScrub = .55f;
        // The post-scrub slowdown is a human-perceptible penalty beat, not a
        // speed: it stays a wall-clock second at any pace.
        public float cornerRecoverySeconds = 1f;
        [Range(.1f, 1f)] public float recoveryAccelerationScale = .35f;
        // Reference px tied to car size and track width, which don't move with
        // the speed dial. The ±16 split leaves the 54×30 bodies (issue #117
        // round 2) a 2 px seam of daylight, racing close, fully on the 64 px
        // ribbon (owner-tightened on hardware review 2026-07-23).
        public float passingDistance = 180f;
        public float passingOffset = 16f;
        public float rematchHoldSeconds = 1f;

        [Header("Slipstream (issue #118)")]
        // The tow a close-trailing car gains on a straight, as a ratio of the
        // pace dial; the window is geometry (like the passing distance) and
        // reaches beyond the split so the reel-in starts before two-wide.
        [Range(0f, .5f)] public float slipstreamBonusRatio = Pace.SlipstreamBonusRatio;
        [Min(0f)] public float slipstreamWindow = RaceRules.DefaultSlipstreamWindow;
        // Every unfinished racer's Ship off the table for this long pauses the race
        // (issue #90). Just enough debounce that hands passing over the sensors don't
        // trigger it — 2 s read as lag on hardware, so the overlay must come up fast.
        [Min(.1f)] public float pauseClearSeconds = .75f;

        // The corner-speed absolute the course construction consumes — always
        // derived from the dial, never stored.
        public float CornerSafeSpeed => basePace * cornerSafeSpeedRatio;

        public RaceRules ToRules(int laps, int requiredServiceCount = 0, ConditionRules conditions = default,
            PitRules pit = default) => new RaceRules(laps, countdownSeconds, basePace,
            basePace * accelerationRatio, basePace * dragRatio, basePace * brakingRatio,
            cornerSpeedScrub, cornerRecoverySeconds, recoveryAccelerationScale,
            passingDistance, passingOffset, rematchHoldSeconds, requiredServiceCount, conditions, pit,
            pauseClearSeconds, basePace * slipstreamBonusRatio, slipstreamWindow);

        public static TrancheTwoSettings Defaults() => CreateInstance<TrancheTwoSettings>();
    }
}
