using System;
using BoardRacing.Domain;

namespace BoardRacing.Runtime
{
    // How the drawn car body sits on its true position: rotated past the path
    // tangent by the drift angle, squashed by brake dive. Scale factors are
    // along the body's heading axis and across it.
    public readonly struct CarAttitude
    {
        public CarAttitude(float driftDegrees, float squashAlong, float stretchAcross)
        { DriftDegrees = driftDegrees; SquashAlong = squashAlong; StretchAcross = stretchAcross; }
        public float DriftDegrees { get; }
        public float SquashAlong { get; }
        public float StretchAcross { get; }
        public static CarAttitude Neutral => new CarAttitude(0f, 1f, 1f);
    }

    // Corner character (issue #117): what the drawn car does with the truth
    // through a corner. The simulation is one-dimensional — a distance along
    // the racing line — so corners are where presentation invents the most,
    // and where it lied the loudest: the close-cars passing split swept the
    // outside car around a wider arc at the same angular rate, a phantom
    // 25-50% speed-up on the tight corners. Everything here modulates pose
    // only. The car's center never leaves the racing line except for the
    // (tapered) split, and every value is a pure function of simulation
    // state — deterministic, never per-frame random — so both players see
    // the same race and captures stay stable.
    public static class CornerCharacter
    {
        // Half of the window the local curvature is measured over; it doubles
        // as the distance every effect eases in and out across a section
        // boundary, because the window straddles the boundary that far out.
        public const float CurvatureHalfSpan = 40f;

        // The radius whose curvature saturates the corner blend. Every
        // catalog corner (R72-R185) is tighter, so the blend reads 1 inside
        // any designed corner and 0 on the straights between.
        public const float BlendRadius = 200f;

        // The fraction of the passing split a corner keeps: enough that two
        // equal-distance cars never coincide, small enough that the outside
        // car's wider drawn arc stays inside an honest speed premium
        // (geometry pin: TaperCapsTheOutsideCarsPhantomCornerSpeed).
        public const float SplitFloor = .25f;

        // Drift: the body rotates past the path tangent toward the corner's
        // inside, proportional to how far over the corner's safe speed the
        // car is running — a car driven under the limit stays composed.
        public const float MaxDriftDegrees = 8f;
        public const float OverspeedForFullDrift = .5f;

        // The turn-in/counter-steer beat: the drift angle leads the corner by
        // comparing the slip ahead of the car against the slip behind it, so
        // the body rotates in slightly before the tangent turns and swings
        // past straight — opposite lock — as the corner releases it.
        public const float CounterSteerSpan = 30f;
        public const float CounterSteerGain = .35f;

        // Brake dive: the body compresses along its heading (and spreads a
        // little across) in proportion to deceleration against full braking.
        public const float DiveSquash = .12f;

        // Signed curvature (1/px) of the racing line: the turn between the
        // chords behind and ahead of the sample, over the arc between their
        // midpoints. Positive follows atan2 in reference space (Y down), the
        // same convention the drawn heading uses, so a positive drift angle
        // rotates into a positive-curvature corner by construction.
        public static float SignedCurvature(TrackDefinition track, float distance,
            float halfSpan = CurvatureHalfSpan)
        {
            Vec2 behind = track.Sample(distance - halfSpan).Position;
            Vec2 at = track.Sample(distance).Position;
            Vec2 ahead = track.Sample(distance + halfSpan).Position;
            float aX = at.X - behind.X, aY = at.Y - behind.Y;
            float bX = ahead.X - at.X, bY = ahead.Y - at.Y;
            float cross = aX * bY - aY * bX, dot = aX * bX + aY * bY;
            if (cross == 0f && dot == 0f) return 0f;
            return (float)Math.Atan2(cross, dot) / halfSpan;
        }

        // 0 on straights, 1 inside any catalog corner, easing across the
        // boundary over the curvature window.
        public static float CornerBlend(TrackDefinition track, float distance) =>
            Clamp01(Math.Abs(SignedCurvature(track, distance)) * BlendRadius);

        // How much of the sim's passing split to draw here. The split exists
        // to keep close cars from overlapping; corners run single-file, and
        // drawing the full split swept the outside car visibly faster than
        // its sim speed (issue #110's corner artifact).
        public static float SplitScale(TrackDefinition track, float distance) =>
            1f - (1f - SplitFloor) * CornerBlend(track, distance);

        public static float DriftDegrees(TrackDefinition track, float distance, float speed)
        {
            float slip = Slip(track, distance, speed);
            float beat = CounterSteerGain * (Slip(track, distance + CounterSteerSpan, speed) -
                Slip(track, distance - CounterSteerSpan, speed));
            return slip + beat;
        }

        // Deceleration is measured against the full braking rate; corner-entry
        // scrub shows as a one-step full dive — the car visibly takes the hit.
        public static float BrakeDive(float deceleration, float brakingRate) =>
            brakingRate <= 0f ? 0f : Clamp01(deceleration / brakingRate);

        public static CarAttitude Attitude(TrackDefinition track, float distance, float speed,
            float deceleration, float brakingRate)
        {
            float dive = BrakeDive(deceleration, brakingRate);
            return new CarAttitude(DriftDegrees(track, distance, speed),
                1f - DiveSquash * dive, 1f + DiveSquash * .5f * dive);
        }

        private static float Slip(TrackDefinition track, float distance, float speed)
        {
            float curvature = SignedCurvature(track, distance);
            float blend = Clamp01(Math.Abs(curvature) * BlendRadius);
            if (blend <= 0f) return 0f;
            // The corner's safe speed, seen as soon as the window touches the
            // corner so the ramp is the blend's, not a step at the boundary.
            float safeSpeed = Math.Min(track.Sample(distance).SafeSpeed,
                Math.Min(track.Sample(distance - CurvatureHalfSpan).SafeSpeed,
                    track.Sample(distance + CurvatureHalfSpan).SafeSpeed));
            if (float.IsInfinity(safeSpeed) || safeSpeed <= 0f) return 0f;
            float overspeed = Clamp01((speed / safeSpeed - 1f) / OverspeedForFullDrift);
            return MaxDriftDegrees * blend * overspeed * Math.Sign(curvature);
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
