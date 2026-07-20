using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    [CreateAssetMenu(menuName = "Board Racing/Tranche Three Settings", fileName = "TrancheThreeSettings")]
    public sealed class TrancheThreeSettings : ScriptableObject
    {
        [Header("Condition model")]
        // Fuel model (owner decision, issue #77 hardware review 2026-07-19): no passive
        // recovery. Drive burns at a somewhat sustainable rate (~125 s tank — a 5-lap
        // race at pure Drive takes ~99 s), Boost burns 5× faster, Brake burns nothing.
        // An empty tank is limp mode — power limited, but enough to crawl to the pit.
        public float fuelBurnPerSecondAtDrive = .008f;
        public float fuelBurnPerSecondAtBoost = .04f;
        [Range(0f, 1f)] public float fuelWarningThreshold = .75f;
        [Range(0f, 1f)] public float emptyMaximumSpeedScale = .35f;
        [Range(0f, 1f)] public float emptyAccelerationScale = .5f;
        public float tireWearPerCorner = .015f;
        public float tireWearPerUnsafeSpeed = .08f;
        [Range(0f, 1f)] public float tirePenaltyThreshold = .6f;
        [Range(0f, 1f)] public float fullyWornSafeSpeedScale = .75f;

        [Header("Pit lifecycle")]
        // No mandatory stop (owner decision, 2026-07-19, issue #92): a blocked finish
        // with no on-table read for why was worse than no rule — fuel and tires
        // already motivate pitting. The rules machinery stays for a future rethink.
        [Min(0)] public int requiredServiceCount = 0;
        [Min(.01f)] public float pitEntrySeconds = .75f;
        [Min(.01f)] public float pitExitSeconds = .75f;
        [Min(.01f)] public float pitCallHoldSeconds = .75f;
        // The pit lane rejoins the track where it physically ends, not back at the
        // start/finish line the car entered from: 850 units along the Wedge top
        // straight (911 long) puts the rejoin just before the sweeper (issue #88).
        [Min(0f)] public float pitExitRejoinDistance = 850f;

        [Header("Crew service regions")]
        // Per-seat dial centers measured from frame 40:23 component 44:124 (wireframe-ui.md,
        // issue #77 Round 2). The condition dial IS the service target, so the detection
        // center is the dial center; the half-size is the Robot placement slop around it.
        public Vector2 playerOneTiresCenter = new Vector2(1692f, 321f);
        public Vector2 playerOneFuelCenter = new Vector2(1590f, 212f);
        public Vector2 playerTwoTiresCenter = new Vector2(228f, 759f);
        public Vector2 playerTwoFuelCenter = new Vector2(330f, 868f);
        public Vector2 serviceHalfSize = new Vector2(50f, 50f);
        // Circular Robot motion drains the meter (owner decision, 2026-07-19):
        // at a comfortable ~1 turn per second, a full meter takes ~5 seconds.
        [Min(.1f)] public float serviceStirTurnsForFullService = 5f;

        public ConditionRules ToConditionRules() => new ConditionRules(fuelBurnPerSecondAtDrive,
            fuelBurnPerSecondAtBoost, fuelWarningThreshold, emptyMaximumSpeedScale, emptyAccelerationScale,
            tireWearPerCorner, tireWearPerUnsafeSpeed, tirePenaltyThreshold, fullyWornSafeSpeedScale);

        public PitRules ToPitRules() =>
            new PitRules(pitEntrySeconds, pitExitSeconds, pitExitRejoinDistance);

        public static TrancheThreeSettings Defaults()
        {
            var result = CreateInstance<TrancheThreeSettings>();
            result.hideFlags = HideFlags.HideAndDontSave;
            return result;
        }
    }
}
