using System;
using System.Collections.Generic;
using System.Linq;
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

        // Corner formation (round 2, owner direction): side-by-side is the
        // straight formation; corners run nose-to-tail. Close cars whose true
        // along-track gap is smaller than one drawn car length get their
        // DRAWN gaps padded up to this spacing, scaled by the corner blend —
        // so with any real gap the formation is simply the truth, and the
        // pad only invents when bodies would overlap.
        public const float NoseToTailSpacing = 60f;

        // Per-car along-track pad (px, aligned to the input) that re-spaces
        // close cars through corners. The pad is honest by construction: it
        // only ever pushes cars apart in their existing SPATIAL order (a
        // lapping leader is physically behind the car it approaches — bodies
        // cannot pass through each other; dead heats keep input order, so
        // P1/P2 resolve the same way every frame), each cluster stays
        // centered on its true midpoint so the pack never advances, the
        // corner blend zeroes it on straights, and the line-truth envelope
        // zeroes it around the start/finish line, where laps, pit diversions,
        // and the finish are judged. Chain-based, not pairwise: four close
        // cars space into one legible train (issue #124).
        public static float[] CornerSpacingPads(TrackDefinition track, float[] distances,
            float passingDistance)
        {
            var pads = new float[distances.Length];
            if (distances.Length < 2) return pads;
            float lap = track.Length;
            int count = distances.Length;
            int[] order = Enumerable.Range(0, count)
                .OrderBy(i => Wrap(distances[i], lap)).ThenBy(i => i).ToArray();
            // Walk the field starting just past the widest circular gap, so
            // the arbitrary lap origin never splits a cluster in two.
            int start = 0;
            float widest = -1f;
            for (int i = 0; i < count; i++)
            {
                float here = Wrap(distances[order[i]], lap);
                float next = Wrap(distances[order[(i + 1) % count]], lap);
                float gap = i == count - 1 ? next - here + lap : next - here;
                if (gap > widest) { widest = gap; start = (i + 1) % count; }
            }
            var indices = new List<int>();
            var positions = new List<float>();
            for (int step = 0; step < count; step++)
            {
                int index = order[(start + step) % count];
                float wrapped = Wrap(distances[index], lap);
                float position = positions.Count == 0
                    ? wrapped
                    : positions[positions.Count - 1] +
                        Wrap(wrapped - positions[positions.Count - 1], lap);
                if (positions.Count > 0 && position - positions[positions.Count - 1] > passingDistance)
                {
                    ApplyClusterPads(track, indices, positions, pads);
                    indices.Clear(); positions.Clear();
                    position = wrapped;
                }
                indices.Add(index);
                positions.Add(position);
            }
            ApplyClusterPads(track, indices, positions, pads);
            return pads;
        }

        private static void ApplyClusterPads(TrackDefinition track, List<int> indices,
            List<float> positions, float[] pads)
        {
            if (indices.Count < 2) return;
            var drawn = new float[indices.Count];
            drawn[0] = positions[0];
            for (int i = 1; i < indices.Count; i++)
            {
                float gap = positions[i] - positions[i - 1];
                float middle = (positions[i] + positions[i - 1]) * .5f;
                float blend = CornerBlend(track, middle) * LineTruthEnvelope(track, middle);
                drawn[i] = drawn[i - 1] + gap + (Math.Max(NoseToTailSpacing, gap) - gap) * blend;
            }
            float shift = (positions.Sum() - drawn.Sum()) / indices.Count;
            for (int i = 0; i < indices.Count; i++)
                pads[indices[i]] = drawn[i] + shift - positions[i];
        }

        // Along-track truth is judged at the start/finish line — laps, pit
        // diversions, and the finish — and a course may run its line right
        // out of a corner (the Wedge hairpin ends AT the line), so the pad
        // fades explicitly around it rather than trusting geometry. The span
        // exceeds the nose-to-tail spacing, so a padded car can never be
        // drawn across a line it has not truly crossed: within the span the
        // pad is at most distance-to-line × (spacing / span) < distance.
        // Public: every drawn embellishment near the line owes this fade
        // (the duel breath of issue #119 stills under it too).
        public const float LineFadeSpan = 150f;

        public static float LineTruthEnvelope(TrackDefinition track, float distance)
        {
            float wrapped = Wrap(distance, track.Length);
            return Clamp01(Math.Min(wrapped, track.Length - wrapped) / LineFadeSpan);
        }

        private static float Wrap(float value, float length)
        {
            value %= length;
            return value < 0f ? value + length : value;
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
