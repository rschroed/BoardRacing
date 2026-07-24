using System;

namespace BoardRacing.Runtime
{
    // One frame of a dueling car's drawn body language: an outward-only
    // widening of its passing-split offset and a lean toward the rival.
    // Lateral and pose only — along-track positions are never touched.
    public readonly struct DuelBreath
    {
        public DuelBreath(float flareScale, float stanceDegrees)
        { FlareScale = flareScale; StanceDegrees = stanceDegrees; }
        // ≥ 1: multiplies the car's drawn split offset, so a duel only ever
        // breathes APART from the racing-close base — the breath can never
        // squeeze the bodies into each other.
        public float FlareScale { get; }
        // How hard the nose angles toward the rival (sign applied per car).
        public float StanceDegrees { get; }
        public static DuelBreath Still => new DuelBreath(1f, 0f);
    }

    // Presentation life (issue #119): bounded drawn embellishment so close
    // racing reads as a fight, not two magnets locked in formation — under
    // the honesty budget. The jockeying breath is LATERAL ONLY: the lateral
    // split is already presentation fiction, so it is free to move; drawn
    // along-track positions stay exactly true, because at a dead heat even a
    // few px of drawn lead would flip what a player believes about who is
    // ahead — precisely where the budget says deviation must be zero. All
    // motion is a pure function of the pair's travelled distance —
    // deterministic, never per-frame random — and the caller fades it to
    // stillness with the line-truth envelope (finish reads) and the corner
    // blend (corners belong to #117's character).
    public static class PresentationLife
    {
        // One breath per this many px of pair travel (~1.25 s at base pace):
        // an unhurried probe, not a vibration.
        public const float BreathWavelength = 450f;
        // The ribbon fixes the lateral budget: a flared car must still fit,
        // splitOffset × (1 + ratio) + bodyHalfWidth ≤ trackHalfWidth (+1px
        // grace, the same tolerance the geometry pin allows the base split) —
        // 16 × 1.125 + 15 = 33. Two px of travel per car turns the 2px of
        // tucked daylight into 6px at full flare: the gap visibly pulses.
        public const float FlareRatio = .125f;
        // The lean toward the rival while tucked in. Rotation spends the
        // same budget through the body's half-length: tucked, 16 + 27·sin4°
        // + 15·cos4° ≈ 32.9 ≤ 33, and the lean gives way to the flare as
        // press rises, so every mid-breath pose fits too.
        public const float MaxStanceDegrees = 4f;

        // side = the sign of the car's own lateral offset. The two sides
        // breathe in anti-phase: one car pulls out for a look while the
        // other tucks in and aims its nose — trading feints, not
        // synchronized calisthenics.
        public static DuelBreath Breathe(float pairDistance, float side, float amplitude)
        {
            float scale = Math.Max(0f, Math.Min(1f, amplitude));
            if (scale <= 0f) return DuelBreath.Still;
            double phase = pairDistance / BreathWavelength * 2.0 * Math.PI + (side < 0f ? Math.PI : 0.0);
            // 0 = tucked in racing close, 1 = flared out for a look.
            float press = .5f + .5f * (float)Math.Sin(phase);
            return new DuelBreath(
                1f + FlareRatio * press * scale,
                // The nose angles at the rival when tucked, straightens when
                // flared — crowding, then composing.
                MaxStanceDegrees * (1f - press) * scale);
        }
    }
}
