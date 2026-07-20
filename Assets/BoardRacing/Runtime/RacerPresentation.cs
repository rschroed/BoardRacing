using System;
using BoardRacing.Domain;

namespace BoardRacing.Runtime
{
    public enum ConditionVisualLevel { Normal, Warning, Critical }

    public readonly struct CarConditionVisualState
    {
        public CarConditionVisualState(float fuelUsed, float tireWear,
            ConditionVisualLevel fuelLevel, ConditionVisualLevel tireLevel)
        { FuelUsed = fuelUsed; TireWear = tireWear; FuelLevel = fuelLevel; TireLevel = tireLevel; }
        public float FuelUsed { get; }
        public float TireWear { get; }
        public ConditionVisualLevel FuelLevel { get; }
        public ConditionVisualLevel TireLevel { get; }
    }

    public static class CarConditionVisualMapper
    {
        private const float WarningThresholdScale = .65f;

        public static CarConditionVisualState From(RacerSnapshot racer, ConditionRules rules) =>
            From(racer.Condition, rules);

        public static CarConditionVisualState From(RacerConditionSnapshot condition, ConditionRules rules)
        {
            if (!rules.Enabled)
                return new CarConditionVisualState(condition.FuelUsed, condition.TireWear,
                    ConditionVisualLevel.Normal, ConditionVisualLevel.Normal);
            return new CarConditionVisualState(condition.FuelUsed, condition.TireWear,
                FuelLevel(condition, rules), Level(condition.TireWear, rules.TirePenaltyThreshold));
        }

        // Fuel is critical only once the tank is actually empty (the limp-mode
        // penalty); the warning threshold is the reserve light.
        private static ConditionVisualLevel FuelLevel(RacerConditionSnapshot condition, ConditionRules rules)
        {
            if (condition.FuelPenaltyActive) return ConditionVisualLevel.Critical;
            if (condition.FuelUsed >= rules.FuelWarningThreshold) return ConditionVisualLevel.Warning;
            return ConditionVisualLevel.Normal;
        }

        private static ConditionVisualLevel Level(float value, float criticalThreshold)
        {
            if (value >= criticalThreshold) return ConditionVisualLevel.Critical;
            if (value >= criticalThreshold * WarningThresholdScale) return ConditionVisualLevel.Warning;
            return ConditionVisualLevel.Normal;
        }
    }

    // Presentation-side track heading: the simulation's tangent is the current
    // chord of the polyline racing line, which turns stepwise at every chord
    // seam (issue #89). The drawn car heading instead spans the seams.
    public static class TrackPresentation
    {
        // A designed corner chord spans ~16-31 px (TrackCatalog, ≤12° steps), so
        // a ±14 px central difference always bridges the nearest seam: the drawn
        // heading turns continuously while the position stays the exact
        // simulation sample.
        public const float HeadingHalfSpan = 14f;

        public static Vec2 SmoothHeading(TrackDefinition track, float distance,
            float halfSpan = HeadingHalfSpan)
        {
            Vec2 ahead = track.Sample(distance + halfSpan).Position;
            Vec2 behind = track.Sample(distance - halfSpan).Position;
            float dx = ahead.X - behind.X, dy = ahead.Y - behind.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            return length <= .00001f ? new Vec2(1f, 0f) : new Vec2(dx / length, dy / length);
        }
    }

    // The simulation advances in fixed steps behind an accumulator, so a display
    // frame usually lands between two steps: drawing the newest step directly
    // advances a car zero or two steps on misaligned frames — the temporal
    // stutter of issue #89. The drawn state instead blends the last two sim
    // states by the accumulator fraction, trading one step (~17 ms) of display
    // latency for continuous motion. The simulation itself is never touched.
    public static class SnapshotInterpolation
    {
        public static RaceSnapshot Blend(RaceSnapshot previous, RaceSnapshot current, float alpha,
            TrackDefinition track)
        {
            // A phase change is a reset boundary (a new race zeroes distances);
            // blending across it would sweep cars backwards through the course.
            if (previous.Racers == null || current.Racers == null || previous.Phase != current.Phase)
                return current;
            float t = Math.Max(0f, Math.Min(1f, alpha));
            var racers = new RacerSnapshot[current.Racers.Count];
            for (int i = 0; i < racers.Length; i++)
                racers[i] = BlendRacer(previous, current.Racers[i], t, track);
            return new RaceSnapshot(current.Phase,
                Lerp(previous.CountdownRemaining, current.CountdownRemaining, t),
                Lerp(previous.ElapsedSeconds, current.ElapsedSeconds, t),
                racers,
                Lerp(previous.RematchProgress, current.RematchProgress, t),
                current.AwaitingRematchRelease);
        }

        private static RacerSnapshot BlendRacer(RaceSnapshot previous, RacerSnapshot current, float t,
            TrackDefinition track)
        {
            if (!TryFindRacer(previous, current.PlayerId, out RacerSnapshot before)) return current;
            // Pit hand-offs move the car between the track and the lane splines,
            // and the exit rejoin jumps TotalDistance forward; only motion that
            // stayed on one continuous path through the step may interpolate.
            if (before.Pit.Phase != current.Pit.Phase || current.TotalDistance < before.TotalDistance)
                return current;
            float distance = Lerp(before.TotalDistance, current.TotalDistance, t);
            var pit = new RacerPitSnapshot(current.Pit.SelectedService, current.Pit.Phase,
                BlendProgress(before.Pit.ServiceProgress, current.Pit.ServiceProgress, t),
                current.Pit.CompletedServices, current.Pit.FinishEligible,
                BlendProgress(before.Pit.PhaseProgress, current.Pit.PhaseProgress, t));
            return new RacerSnapshot(current.PlayerId, Lerp(before.Speed, current.Speed, t), distance,
                current.CompletedLaps, current.Place, current.Finished, current.FinishTime,
                track.Sample(distance), Lerp(before.LateralOffset, current.LateralOffset, t),
                current.IncidentThisStep, current.RecoveryRemaining, current.IncidentCount,
                current.Condition, pit);
        }

        // Progress values reset when a service completes or a phase turns over;
        // never blend backwards through a reset.
        private static float BlendProgress(float before, float after, float t) =>
            after >= before ? Lerp(before, after, t) : after;

        private static bool TryFindRacer(RaceSnapshot snapshot, PlayerId playerId, out RacerSnapshot racer)
        {
            for (int i = 0; i < snapshot.Racers.Count; i++)
                if (snapshot.Racers[i].PlayerId == playerId) { racer = snapshot.Racers[i]; return true; }
            racer = default;
            return false;
        }

        private static float Lerp(float from, float to, float t) => from + (to - from) * t;
    }

    public readonly struct PitLanePresentationLayout
    {
        public PitLanePresentationLayout(Vec2 pitLine, Vec2 entry, Vec2 playerOneBox,
            Vec2 playerTwoBox, Vec2 exit, Vec2 mergeApproach, Vec2 exitRejoin)
            : this(pitLine, entry, playerOneBox, playerTwoBox, exit, mergeApproach, exitRejoin,
                default, default)
        {
        }

        public PitLanePresentationLayout(Vec2 pitLine, Vec2 entry, Vec2 playerOneBox,
            Vec2 playerTwoBox, Vec2 exit, Vec2 mergeApproach, Vec2 exitRejoin,
            Vec2 entryDirection, Vec2 rejoinDirection)
        {
            PitLine = pitLine; Entry = entry; PlayerOneBox = playerOneBox;
            PlayerTwoBox = playerTwoBox; Exit = exit;
            MergeApproach = mergeApproach; ExitRejoin = exitRejoin;
            EntryDirection = entryDirection; RejoinDirection = rejoinDirection;
        }
        public Vec2 PitLine { get; }
        public Vec2 Entry { get; }
        public Vec2 PlayerOneBox { get; }
        public Vec2 PlayerTwoBox { get; }
        public Vec2 Exit { get; }
        public Vec2 MergeApproach { get; }
        // Where the pit lane physically meets the track again — the simulation
        // resumes the car at the matching track distance, so the exit animation
        // is a short forward merge instead of a return trip to the start line.
        public Vec2 ExitRejoin { get; }
        // Track headings where the lane touches the track (issue #89): the entry
        // spline leaves the pit line along EntryDirection and the exit spline
        // lands on RejoinDirection, so neither hand-off snaps the car's heading.
        // Left default (zero), the splines fall back to endpoint extrapolation.
        public Vec2 EntryDirection { get; }
        public Vec2 RejoinDirection { get; }
        public Vec2 Box(PlayerId playerId) => playerId == PlayerId.Player1 ? PlayerOneBox : PlayerTwoBox;
    }

    public readonly struct CarPresentationPose
    {
        public CarPresentationPose(Vec2 position, Vec2 tangent, bool finished)
        { Position = position; Tangent = tangent; Finished = finished; }
        public Vec2 Position { get; }
        public Vec2 Tangent { get; }
        public bool Finished { get; }
    }

    public static class PitLanePresentationMapper
    {
        public static CarPresentationPose From(RacerSnapshot racer, Vec2 trackPosition,
            Vec2 trackTangent, PitLanePresentationLayout layout)
        {
            Vec2 box = layout.Box(racer.PlayerId);
            if (racer.Pit.Phase == PitPhase.Entering)
                return AlongSpline(new[] { layout.PitLine, layout.Entry, box },
                    Ease(racer.Pit.PhaseProgress), racer.Finished, layout.EntryDirection, default);
            if (racer.Pit.Phase == PitPhase.InService)
                return new CarPresentationPose(box, Unit(layout.Exit, box), racer.Finished);
            if (racer.Pit.Phase == PitPhase.Exiting)
                return ExitPose(racer.PlayerId, racer.Pit.PhaseProgress, racer.Finished, layout);
            return new CarPresentationPose(trackPosition, Normalize(trackTangent), racer.Finished);
        }

        public static CarPresentationPose ExitPose(PlayerId playerId, float progress, bool finished,
            PitLanePresentationLayout layout) => AlongSpline(new[]
            {
                layout.Box(playerId), layout.MergeApproach, layout.ExitRejoin
            }, Ease(progress), finished, default, layout.RejoinDirection);

        // The simulation's phase progress is linear time; the drawn car eases out
        // of one speed and into the next instead of jumping between them.
        private static float Ease(float progress)
        {
            float clamped = Math.Max(0f, Math.Min(1f, progress));
            return clamped * clamped * (3f - 2f * clamped);
        }

        private static CarPresentationPose AlongSpline(Vec2[] points, float progress, bool finished,
            Vec2 inDirection, Vec2 outDirection)
        {
            // 24 keeps every chord's turn small even where a pinned end
            // concentrates curvature into an S-bend (issue #89; was 12).
            const int SamplesPerSegment = 24;
            var samples = new Vec2[(points.Length - 1) * SamplesPerSegment + 1];
            int index = 0;
            samples[index++] = points[0];
            for (int segment = 0; segment < points.Length - 1; segment++)
            {
                // A known track heading at either hand-off pins the spline's end
                // direction (phantom control point along it); otherwise the end
                // extrapolates its own last chord as before.
                Vec2 p0 = segment == 0
                    ? PhantomBehind(points[0], points[1], inDirection)
                    : points[segment - 1];
                Vec2 p1 = points[segment];
                Vec2 p2 = points[segment + 1];
                Vec2 p3 = segment + 2 < points.Length
                    ? points[segment + 2]
                    : PhantomBeyond(points[points.Length - 1], points[points.Length - 2], outDirection);
                for (int sample = 1; sample <= SamplesPerSegment; sample++)
                    samples[index++] = CatmullRom(p0, p1, p2, p3, sample / (float)SamplesPerSegment);
            }
            return Along(samples, progress, finished);
        }

        private static Vec2 Extrapolate(Vec2 point, Vec2 neighbor) =>
            new Vec2(point.X * 2f - neighbor.X, point.Y * 2f - neighbor.Y);

        // A Catmull-Rom endpoint's tangent is (neighbor-side control − phantom)/2,
        // so pinning an end to a track heading places the phantom relative to the
        // NEIGHBOR control point along that heading — not behind the endpoint.
        private static Vec2 PhantomBehind(Vec2 end, Vec2 neighbor, Vec2 direction)
        {
            if (direction.X == 0f && direction.Y == 0f) return Extrapolate(end, neighbor);
            float reach = 3f * Distance(end, neighbor);
            return new Vec2(neighbor.X - direction.X * reach, neighbor.Y - direction.Y * reach);
        }

        private static Vec2 PhantomBeyond(Vec2 end, Vec2 neighbor, Vec2 direction)
        {
            if (direction.X == 0f && direction.Y == 0f) return Extrapolate(end, neighbor);
            float reach = 3f * Distance(end, neighbor);
            return new Vec2(neighbor.X + direction.X * reach, neighbor.Y + direction.Y * reach);
        }

        private static Vec2 CatmullRom(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return new Vec2(
                .5f * (2f * p1.X + (-p0.X + p2.X) * t +
                    (2f * p0.X - 5f * p1.X + 4f * p2.X - p3.X) * t2 +
                    (-p0.X + 3f * p1.X - 3f * p2.X + p3.X) * t3),
                .5f * (2f * p1.Y + (-p0.Y + p2.Y) * t +
                    (2f * p0.Y - 5f * p1.Y + 4f * p2.Y - p3.Y) * t2 +
                    (-p0.Y + 3f * p1.Y - 3f * p2.Y + p3.Y) * t3));
        }

        private static CarPresentationPose Along(Vec2[] points, float progress, bool finished)
        {
            float total = 0f;
            for (int i = 0; i < points.Length - 1; i++) total += Distance(points[i], points[i + 1]);
            if (total <= 0f) return new CarPresentationPose(points[points.Length - 1], new Vec2(1f, 0f), finished);
            float remaining = Math.Max(0f, Math.Min(1f, progress)) * total;
            for (int i = 0; i < points.Length - 1; i++)
            {
                float length = Distance(points[i], points[i + 1]);
                if (remaining <= length || i == points.Length - 2)
                {
                    float t = length <= 0f ? 1f : Math.Min(1f, remaining / length);
                    // Blend the neighbor chords' directions across the chord so
                    // the heading turns continuously through every sample seam
                    // instead of stepping per chord (issue #89).
                    Vec2 inbound = Unit(points[i + 1], points[Math.Max(0, i - 1)]);
                    Vec2 outbound = Unit(points[Math.Min(points.Length - 1, i + 2)], points[i]);
                    return new CarPresentationPose(Lerp(points[i], points[i + 1], t),
                        Normalize(Lerp(inbound, outbound, t)), finished);
                }
                remaining -= length;
            }
            return new CarPresentationPose(points[points.Length - 1], new Vec2(1f, 0f), finished);
        }

        private static Vec2 Lerp(Vec2 from, Vec2 to, float t) =>
            new Vec2(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);

        private static Vec2 Unit(Vec2 to, Vec2 from) => Normalize(new Vec2(to.X - from.X, to.Y - from.Y));
        private static Vec2 Normalize(Vec2 value)
        {
            float length = (float)Math.Sqrt(value.X * value.X + value.Y * value.Y);
            return length <= .00001f ? new Vec2(1f, 0f) : new Vec2(value.X / length, value.Y / length);
        }
        private static float Distance(Vec2 a, Vec2 b)
        {
            float x = b.X - a.X, y = b.Y - a.Y;
            return (float)Math.Sqrt(x * x + y * y);
        }
    }
}
