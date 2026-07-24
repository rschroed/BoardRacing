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

    // One frame of a car's drawn launch: how far the body lags its true
    // position off the line, and the wheelspin shimmy that sells it.
    public readonly struct LaunchTwitch
    {
        public LaunchTwitch(float lag, float yawDegrees)
        { Lag = lag; YawDegrees = yawDegrees; }
        // ≥ 0, px BEHIND truth: hesitation only, never anticipation. A
        // drawn car may under-report its progress for a blink, but can
        // never be drawn across a line its true position has not crossed.
        public float Lag { get; }
        public float YawDegrees { get; }
        public static LaunchTwitch Still => new LaunchTwitch(0f, 0f);
    }

    // Presentation life (issue #119): bounded drawn embellishment so close
    // racing reads as a fight, not two magnets locked in formation — under
    // the honesty budget. The jockeying breath is LATERAL ONLY: the lateral
    // split is already presentation fiction, so it is free to move; drawn
    // along-track positions stay exactly true, because at a dead heat even a
    // few px of drawn lead would flip what a player believes about who is
    // ahead — precisely where the budget says deviation must be zero. The
    // launch twitch is the issue's one granted along-track exception: for
    // the first second after GO — when everyone knows the race is dead even
    // and nothing is being judged at the line — each drawn car may briefly
    // HESITATE behind its true position (never lead it), scrabbling off the
    // start; it converges to exact truth at the window's end, minutes before
    // any finish, final-straight, or pit read exists. All motion is a pure
    // function of sim state — deterministic, never per-frame random — and
    // the callers fade it with the line-truth envelope (finish reads) and
    // the corner blend (corners belong to #117's character).
    public static class PresentationLife
    {
        // One breath per this many px of pair travel (~1.25 s at base pace):
        // an unhurried probe, not a vibration.
        public const float BreathWavelength = 450f;
        // The ribbon fixes the lateral budget: a flared car must still fit,
        // splitOffset × (1 + ratio) + bodyHalfWidth ≤ trackHalfWidth (+1px
        // grace, the same tolerance the geometry pin allows the base split) —
        // 16 × 1.25 + 13 = 33 with the 26-wide bodies (owner direction
        // 2026-07-23; the 30-wide bodies capped this at .125). Four px of
        // travel per car pulses the 6px of tucked daylight to 14px at full
        // flare: the gap visibly breathes.
        public const float FlareRatio = .25f;
        // The lean toward the rival while tucked in. Rotation spends the
        // same budget through the body's half-length: tucked, 16 + 27·sin4°
        // + 13·cos4° ≈ 30.9 ≤ 33, and the lean gives way to the flare as
        // press rises, so every mid-breath pose fits too.
        public const float MaxStanceDegrees = 4f;

        // The launch twitch (issue #119): converge to exact truth within ~1s
        // of GO, per the issue. The lag ceiling is about a car's hesitation
        // beat against the ~110px a car truly covers in that second; the
        // wiring additionally clamps lag to the car's true distance, so a
        // slow-digging car pins AT the line — bogged at the lights — rather
        // than ever drawing behind where it started.
        public const float LaunchWindowSeconds = 1f;
        public const float MaxLaunchLag = 8f;
        // Scrabble beats per second: two-ish digs inside the window.
        public const float LaunchScrabbleHz = 2.2f;
        // The wheelspin shimmy riding the same envelope, degrees.
        public const float MaxLaunchYawDegrees = 2f;

        // The cars scrabble a half-cycle apart — they trade who digs, so
        // neither player's car always launches "worse" — and the course
        // length salts the phase so each course's launch is its own show.
        // Deterministic: both players and every replay see the same race.
        public static float LaunchPhase(int carIndex, float trackLength) =>
            carIndex * (float)Math.PI +
            (trackLength % BreathWavelength) / BreathWavelength * 2f * (float)Math.PI;

        public static LaunchTwitch Launch(float raceSeconds, float phase)
        {
            if (raceSeconds <= 0f || raceSeconds >= LaunchWindowSeconds) return LaunchTwitch.Still;
            float t = raceSeconds / LaunchWindowSeconds;
            // Rises from a clean GO (no jump at t = 0), peaks a third of the
            // way in, and lands at exactly zero as the window closes:
            // 27/4 · t · (1−t)² is the unit bump.
            float envelope = 6.75f * t * (1f - t) * (1f - t);
            double beat = 2.0 * Math.PI * LaunchScrabbleHz * raceSeconds;
            float dig = .5f + .5f * (float)Math.Sin(beat + phase);
            return new LaunchTwitch(
                MaxLaunchLag * envelope * dig,
                // An incommensurate rate so the shimmy doesn't sync with the
                // digs — scrabble, not metronome.
                MaxLaunchYawDegrees * envelope * (float)Math.Sin(beat * 1.7 + phase * 1.3));
        }

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
