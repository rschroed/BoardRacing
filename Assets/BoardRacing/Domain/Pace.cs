namespace BoardRacing.Domain
{
    /// <summary>
    /// The pace scalar (issue #116): one base pace — the top speed a car
    /// reaches on its own down a straight, in reference px/s — with every
    /// other speed-shaped constant a ratio of it. Turning the base pace
    /// retunes how fast the whole game feels as ONE dial while the
    /// relationships between accelerating, coasting, braking, and cornering
    /// hold by construction; the speed features still to come (the pit-lane
    /// crawl of issue #110, the slipstream of #118, overspeed slides) are
    /// born as ratios of the same dial. The ratios reproduce the owner-tuned
    /// absolutes at base pace 360 — acceleration 220, drag 120, braking 300,
    /// corner safe speed 190 — within float rounding (~1e-4 px/s, far inside
    /// every balance tolerance).
    /// </summary>
    public static class Pace
    {
        public const float BasePace = 360f;
        public const float AccelerationRatio = 220f / 360f;
        public const float DragRatio = 120f / 360f;
        public const float BrakingRatio = 300f / 360f;
        public const float CornerSafeSpeedRatio = 190f / 360f;
        // The pit-lane crawl (issue #110): under the tightest catalog corner's
        // safe speed (0.68 × the corner baseline ≈ 0.36 of base pace), so the
        // lane reads as the slowest driving on the board; even the drawn box
        // eases, which peak at 4/3 of the crawl mid-leg, stay under the
        // corner-speed baseline. Owner-approved on hardware 2026-07-23.
        public const float PitLaneSpeedRatio = .3f;
        // The slipstream tow (issue #118): the speed a close-trailing car
        // gains on a straight, additive over its throttle target — the only
        // way past a leader running the same throttle. 5% of base pace
        // closes ~45 px over a 900 px pit straight — a pass takes most of a
        // lap of drafting to convert (owner-tuned down from 8% on hardware,
        // 2026-07-23: the catch-up read as too easy).
        public const float SlipstreamBonusRatio = .05f;

        // The tuned absolutes at the reference dial position — for domain
        // defaults and test fixtures, so they follow a ratio retune.
        public const float Acceleration = BasePace * AccelerationRatio;
        public const float Drag = BasePace * DragRatio;
        public const float Braking = BasePace * BrakingRatio;
        public const float CornerSafeSpeed = BasePace * CornerSafeSpeedRatio;
        public const float PitLaneSpeed = BasePace * PitLaneSpeedRatio;
        public const float SlipstreamBonus = BasePace * SlipstreamBonusRatio;
    }
}
