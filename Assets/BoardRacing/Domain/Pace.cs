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

        // The tuned absolutes at the reference dial position — for domain
        // defaults and test fixtures, so they follow a ratio retune.
        public const float Acceleration = BasePace * AccelerationRatio;
        public const float Drag = BasePace * DragRatio;
        public const float Braking = BasePace * BrakingRatio;
        public const float CornerSafeSpeed = BasePace * CornerSafeSpeedRatio;
    }
}
