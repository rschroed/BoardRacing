using System;
using System.Collections.Generic;
using System.Linq;
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
            // Most pit hand-offs move the car between separate paths, and the
            // exit rejoin jumps TotalDistance forward, so they must snap. The
            // arrival at a stall is different: the moving entry spline ends at
            // the exact parked pose. Preserve its final fixed step instead of
            // popping straight to the box.
            if (before.Pit.Phase != current.Pit.Phase)
            {
                if (ArrivedAtStall(before.Pit.Phase, current.Pit.Phase))
                    return BlendStallArrival(before, current, t, track);
                return current;
            }
            // Releasing a parked car changes only its traffic state; the exit
            // spline still starts at that same parked pose. Interpolate the
            // first moving step so it pulls away as smoothly as later steps.
            if ((before.Pit.TrafficState != current.Pit.TrafficState &&
                 current.Pit.Phase != PitPhase.Exiting) ||
                current.TotalDistance < before.TotalDistance)
                return current;
            float distance = Lerp(before.TotalDistance, current.TotalDistance, t);
            var pit = new RacerPitSnapshot(current.Pit.SelectedService, current.Pit.Phase,
                BlendProgress(before.Pit.ServiceProgress, current.Pit.ServiceProgress, t),
                current.Pit.CompletedServices, current.Pit.FinishEligible,
                BlendProgress(before.Pit.PhaseProgress, current.Pit.PhaseProgress, t),
                current.Pit.TrafficState, Lerp(before.Pit.QueueOffset, current.Pit.QueueOffset, t));
            return new RacerSnapshot(current.PlayerId, Lerp(before.Speed, current.Speed, t), distance,
                current.CompletedLaps, current.Place, current.Finished, current.FinishTime,
                track.Sample(distance), Lerp(before.LateralOffset, current.LateralOffset, t),
                current.IncidentThisStep, current.RecoveryRemaining, current.IncidentCount,
                current.Condition, pit);
        }

        private static bool ArrivedAtStall(PitPhase before, PitPhase current) =>
            (before == PitPhase.Entering && current == PitPhase.InService) ||
            (before == PitPhase.Parking && current == PitPhase.Parked);

        private static RacerSnapshot BlendStallArrival(RacerSnapshot before,
            RacerSnapshot current, float t, TrackDefinition track)
        {
            float distance = Lerp(before.TotalDistance, current.TotalDistance, t);
            var pit = new RacerPitSnapshot(current.Pit.SelectedService, before.Pit.Phase,
                BlendProgress(before.Pit.ServiceProgress, current.Pit.ServiceProgress, t),
                current.Pit.CompletedServices, current.Pit.FinishEligible,
                Lerp(before.Pit.PhaseProgress, 1f, t), before.Pit.TrafficState,
                Lerp(before.Pit.QueueOffset, current.Pit.QueueOffset, t));
            return new RacerSnapshot(current.PlayerId, Lerp(before.Speed, current.Speed, t),
                distance, current.CompletedLaps, current.Place, current.Finished,
                current.FinishTime, track.Sample(distance),
                Lerp(before.LateralOffset, current.LateralOffset, t),
                current.IncidentThisStep, current.RecoveryRemaining,
                current.IncidentCount, current.Condition, pit);
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
            : this(pitLine, entry, new[] { playerOneBox, playerTwoBox },
                exit, mergeApproach, exitRejoin,
                default, default)
        {
        }

        public PitLanePresentationLayout(Vec2 pitLine, Vec2 entry, Vec2 playerOneBox,
            Vec2 playerTwoBox, Vec2 exit, Vec2 mergeApproach, Vec2 exitRejoin,
            Vec2 entryDirection, Vec2 rejoinDirection)
            : this(pitLine, entry, new[] { playerOneBox, playerTwoBox },
                exit, mergeApproach, exitRejoin, entryDirection, rejoinDirection)
        {
        }

        public PitLanePresentationLayout(Vec2 pitLine, Vec2 entry, IReadOnlyList<Vec2> boxes,
            Vec2 exit, Vec2 mergeApproach, Vec2 exitRejoin,
            Vec2 entryDirection = default, Vec2 rejoinDirection = default)
            : this(pitLine, entry, LegacyStalls(boxes), exit, mergeApproach,
                exitRejoin, entryDirection, rejoinDirection)
        {
        }

        public PitLanePresentationLayout(Vec2 pitLine, Vec2 entry,
            IReadOnlyList<PitStallDefinition> stalls,
            Vec2 exit, Vec2 mergeApproach, Vec2 exitRejoin,
            Vec2 entryDirection = default, Vec2 rejoinDirection = default,
            PitRoadbedKind roadbedKind = PitRoadbedKind.Trackside)
        {
            PitStallDefinition[] authoredStalls = stalls?.ToArray() ??
                throw new ArgumentNullException(nameof(stalls));
            if (authoredStalls.Length < 2 || authoredStalls.Length > 4)
                throw new ArgumentException("Pit presentation requires two to four stalls.",
                    nameof(stalls));
            PitLine = pitLine; Entry = entry;
            Stalls = Array.AsReadOnly(authoredStalls);
            LaneAnchors = Array.AsReadOnly(authoredStalls.Select(x => x.LaneAnchor).ToArray());
            EntryAnchors = Array.AsReadOnly(authoredStalls.Select(x => x.EntryAnchor).ToArray());
            ExitAnchors = Array.AsReadOnly(authoredStalls.Select(x => x.ExitAnchor).ToArray());
            LaneWaypoints = OrderedLaneWaypoints(authoredStalls);
            Boxes = Array.AsReadOnly(authoredStalls.Select(x => x.ParkedPosition).ToArray());
            MergeApproach = mergeApproach; ExitRejoin = exitRejoin;
            Exit = exit;
            EntryDirection = entryDirection; RejoinDirection = rejoinDirection;
            RoadbedKind = roadbedKind;
        }
        public Vec2 PitLine { get; }
        public Vec2 Entry { get; }
        public IReadOnlyList<PitStallDefinition> Stalls { get; }
        public IReadOnlyList<Vec2> LaneAnchors { get; }
        public IReadOnlyList<Vec2> EntryAnchors { get; }
        public IReadOnlyList<Vec2> ExitAnchors { get; }
        public IReadOnlyList<Vec2> LaneWaypoints { get; }
        public IReadOnlyList<Vec2> Boxes { get; }
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
        public PitRoadbedKind RoadbedKind { get; }
        public Vec2 Box(PlayerId playerId)
        {
            int index = (int)playerId - 1;
            if (index < 0 || index >= Boxes.Count)
                throw new ArgumentOutOfRangeException(nameof(playerId),
                    "The pit layout has no box for that racer.");
            return Boxes[index];
        }

        public Vec2 LaneAnchor(PlayerId playerId)
        {
            int index = (int)playerId - 1;
            if (index < 0 || index >= LaneAnchors.Count)
                throw new ArgumentOutOfRangeException(nameof(playerId),
                    "The pit layout has no lane anchor for that racer.");
            return LaneAnchors[index];
        }

        public Vec2 EntryAnchor(PlayerId playerId)
        {
            int index = (int)playerId - 1;
            if (index < 0 || index >= EntryAnchors.Count)
                throw new ArgumentOutOfRangeException(nameof(playerId),
                    "The pit layout has no entry anchor for that racer.");
            return EntryAnchors[index];
        }

        public Vec2 ExitAnchor(PlayerId playerId)
        {
            int index = (int)playerId - 1;
            if (index < 0 || index >= ExitAnchors.Count)
                throw new ArgumentOutOfRangeException(nameof(playerId),
                    "The pit layout has no exit anchor for that racer.");
            return ExitAnchors[index];
        }

        public Vec2 ParkedHeading(PlayerId playerId)
        {
            int index = (int)playerId - 1;
            if (index < 0 || index >= Stalls.Count)
                throw new ArgumentOutOfRangeException(nameof(playerId),
                    "The pit layout has no parked heading for that racer.");
            return Stalls[index].ParkedHeading;
        }

        // The one way a course's authored pit complex becomes presentation
        // geometry (issue #107 phase 1) — RacePrototype and the geometry tests
        // used to each assemble this by hand from duplicated constants.
        public static PitLanePresentationLayout ForCourse(CourseDefinition course) =>
            new PitLanePresentationLayout(course.Track.Sample(0f).Position,
                course.Pit.Entry, course.Pit.Stalls,
                course.Pit.Exit, course.Pit.MergeApproach,
                course.Track.Sample(course.Pit.ExitRejoinDistance).Position,
                TrackPresentation.SmoothHeading(course.Track, 0f),
                TrackPresentation.SmoothHeading(course.Track, course.Pit.ExitRejoinDistance),
                course.PitRoadbed);

        private static IReadOnlyList<PitStallDefinition> LegacyStalls(
            IReadOnlyList<Vec2> boxes)
        {
            Vec2[] authoredBoxes = boxes?.ToArray() ??
                throw new ArgumentNullException(nameof(boxes));
            if (authoredBoxes.Length < 2) return Array.Empty<PitStallDefinition>();
            Vec2 first = authoredBoxes[0], last = authoredBoxes[authoredBoxes.Length - 1];
            var heading = new Vec2(last.X - first.X, last.Y - first.Y);
            return authoredBoxes.Select(box => new PitStallDefinition(box, box, heading)).ToArray();
        }

        private static IReadOnlyList<Vec2> OrderedLaneWaypoints(
            IReadOnlyList<PitStallDefinition> stalls)
        {
            Vec2 heading = stalls[0].ParkedHeading;
            Vec2 origin = stalls[0].LaneAnchor;
            var ordered = stalls
                .SelectMany(stall => new[] { stall.EntryAnchor, stall.ExitAnchor })
                .OrderBy(point =>
                    (point.X - origin.X) * heading.X +
                    (point.Y - origin.Y) * heading.Y)
                .ToArray();
            var unique = new List<Vec2>(ordered.Length);
            foreach (Vec2 point in ordered)
            {
                if (unique.Count == 0 ||
                    Math.Abs(unique[unique.Count - 1].X - point.X) > .0001f ||
                    Math.Abs(unique[unique.Count - 1].Y - point.Y) > .0001f)
                    unique.Add(point);
            }
            return Array.AsReadOnly(unique.ToArray());
        }
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
        private const int SplineSamplesPerSegment = 48;
        // A car has a wheelbase, not a point tangent. Reading the route across
        // half its body length prevents concentrated spline curvature at an
        // internal control seam from becoming a one-frame steering flick.
        private const float PathHeadingHalfSpan = 14f;
        // Preserve the racing-line lateral position at the exact course-to-pit
        // handoff, then settle onto the pit centerline across the opening part
        // of the route. Dropping the offset on the phase-change frame made the
        // high-fidelity body visibly jump even though both centerlines meet.
        public const float EntryLateralSettleProgress = .12f;

        internal static float PresentedLateralOffset(RacerSnapshot racer)
        {
            if (racer.Pit.Phase == PitPhase.OnTrack ||
                racer.Pit.Phase == PitPhase.Requested)
                return racer.LateralOffset;
            if (racer.Pit.Phase != PitPhase.Entering) return 0f;
            float t = Math.Max(0f, Math.Min(1f,
                racer.Pit.PhaseProgress / EntryLateralSettleProgress));
            float eased = t * t * (3f - 2f * t);
            return racer.LateralOffset * (1f - eased);
        }

        public static CarPresentationPose From(RacerSnapshot racer, Vec2 trackPosition,
            Vec2 trackTangent, PitLanePresentationLayout layout)
        {
            Vec2 box = layout.Box(racer.PlayerId);
            if ((racer.Pit.Phase == PitPhase.Entering || racer.Pit.Phase == PitPhase.Parking) &&
                racer.Pit.TrafficState == PitTrafficState.Queued)
            {
                Vec2 heading = Normalize(layout.EntryDirection);
                return new CarPresentationPose(new Vec2(
                    layout.PitLine.X - heading.X * racer.Pit.QueueOffset,
                    layout.PitLine.Y - heading.Y * racer.Pit.QueueOffset),
                    heading, racer.Finished);
            }
            if (racer.Pit.Phase == PitPhase.Entering || racer.Pit.Phase == PitPhase.Parking)
                return EntryPose(racer.PlayerId, racer.Pit.PhaseProgress, racer.Finished, layout);
            if (racer.Pit.Phase == PitPhase.Parked)
                return new CarPresentationPose(box, layout.ParkedHeading(racer.PlayerId), racer.Finished);
            if (racer.Pit.Phase == PitPhase.InService)
                return new CarPresentationPose(box, layout.ParkedHeading(racer.PlayerId), racer.Finished);
            if (racer.Pit.Phase == PitPhase.Exiting)
                return ExitPose(racer.PlayerId, racer.Pit.PhaseProgress, racer.Finished, layout);
            return new CarPresentationPose(trackPosition, Normalize(trackTangent), racer.Finished);
        }

        public static CarPresentationPose EntryPose(PlayerId playerId, float progress, bool finished,
            PitLanePresentationLayout layout) => Along(
                EntrySamples(playerId, layout), EaseIntoLane(progress), finished,
                layout.EntryDirection, layout.ParkedHeading(playerId));

        public static CarPresentationPose ExitPose(PlayerId playerId, float progress, bool finished,
            PitLanePresentationLayout layout) => Along(
                ExitSamples(playerId, layout), EaseOutOfBox(progress), finished,
                layout.ParkedHeading(playerId), layout.RejoinDirection);

        internal static Vec2[] EntryRoute(PlayerId playerId, PitLanePresentationLayout layout)
        {
            var points = new List<Vec2> { layout.PitLine, layout.Entry };
            Vec2 entryAnchor = layout.EntryAnchor(playerId);
            foreach (Vec2 waypoint in layout.LaneWaypoints)
            {
                AddIfDistinct(points, waypoint);
                if (SamePoint(waypoint, entryAnchor)) break;
            }
            AddIfDistinct(points, layout.Box(playerId));
            return points.ToArray();
        }

        internal static Vec2[] ExitRoute(PlayerId playerId, PitLanePresentationLayout layout)
        {
            var points = new List<Vec2> { layout.Box(playerId) };
            Vec2 exitAnchor = layout.ExitAnchor(playerId);
            AddIfDistinct(points, exitAnchor);
            bool pastExit = false;
            foreach (Vec2 waypoint in layout.LaneWaypoints)
            {
                if (!pastExit)
                {
                    if (SamePoint(waypoint, exitAnchor)) pastExit = true;
                    continue;
                }
                AddIfDistinct(points, waypoint);
            }
            AddIfDistinct(points, layout.MergeApproach);
            AddIfDistinct(points, layout.ExitRejoin);
            return points.ToArray();
        }

        internal static Vec2[] ServiceCurveSamples(PlayerId playerId,
            PitLanePresentationLayout layout)
        {
            Vec2 heading = layout.ParkedHeading(playerId);
            Vec2[] arrival = HermiteSamples(layout.EntryAnchor(playerId),
                layout.Box(playerId), heading);
            Vec2[] release = HermiteSamples(layout.Box(playerId),
                layout.ExitAnchor(playerId), heading);
            return JoinSamples(arrival, release);
        }

        private static Vec2[] EntrySamples(PlayerId playerId,
            PitLanePresentationLayout layout)
        {
            Vec2[] route = EntryRoute(playerId, layout);
            Vec2 heading = layout.ParkedHeading(playerId);
            // The common lane ends at the owned entry anchor. Pin that shared
            // route to the row heading, then use an explicit private S-curve to
            // the parked apex. Letting Catmull-Rom infer the anchor tangent from
            // the next stall concentrated a visible counter-steer at this seam.
            Vec2[] commonControls = route.Take(route.Length - 1).ToArray();
            Vec2[] common = SplineSamples(commonControls,
                layout.EntryDirection, heading);
            Vec2[] branch = HermiteSamples(layout.EntryAnchor(playerId),
                layout.Box(playerId), heading);
            return JoinSamples(common, branch);
        }

        private static Vec2[] ExitSamples(PlayerId playerId,
            PitLanePresentationLayout layout)
        {
            Vec2[] route = ExitRoute(playerId, layout);
            Vec2 heading = layout.ParkedHeading(playerId);
            Vec2[] branch = HermiteSamples(layout.Box(playerId),
                layout.ExitAnchor(playerId), heading);
            Vec2[] commonControls = route.Skip(1).ToArray();
            Vec2[] common = SplineSamples(commonControls,
                heading, layout.RejoinDirection);
            return JoinSamples(branch, common);
        }

        private static Vec2[] HermiteSamples(Vec2 start, Vec2 end, Vec2 direction)
        {
            Vec2 heading = Normalize(direction);
            float forwardReach = Math.Max(.0001f,
                (end.X - start.X) * heading.X + (end.Y - start.Y) * heading.Y);
            var tangent = new Vec2(
                heading.X * forwardReach, heading.Y * forwardReach);
            var samples = new Vec2[SplineSamplesPerSegment + 1];
            for (int sample = 0; sample <= SplineSamplesPerSegment; sample++)
            {
                float t = sample / (float)SplineSamplesPerSegment;
                float t2 = t * t, t3 = t2 * t;
                float h00 = 2f * t3 - 3f * t2 + 1f;
                float h10 = t3 - 2f * t2 + t;
                float h01 = -2f * t3 + 3f * t2;
                float h11 = t3 - t2;
                samples[sample] = new Vec2(
                    h00 * start.X + h10 * tangent.X +
                    h01 * end.X + h11 * tangent.X,
                    h00 * start.Y + h10 * tangent.Y +
                    h01 * end.Y + h11 * tangent.Y);
            }
            return samples;
        }

        private static Vec2[] JoinSamples(Vec2[] before, Vec2[] after)
        {
            var joined = new Vec2[before.Length + after.Length - 1];
            Array.Copy(before, joined, before.Length);
            Array.Copy(after, 1, joined, before.Length, after.Length - 1);
            return joined;
        }

        private static void AddIfDistinct(List<Vec2> points, Vec2 point)
        {
            Vec2 prior = points[points.Count - 1];
            if (Math.Abs(point.X - prior.X) <= .0001f &&
                Math.Abs(point.Y - prior.Y) <= .0001f) return;
            points.Add(point);
        }

        private static bool SamePoint(Vec2 left, Vec2 right) =>
            Math.Abs(left.X - right.X) <= .0001f &&
            Math.Abs(left.Y - right.Y) <= .0001f;

        internal static CarPresentationPose SharedEntryPose(float progress,
            PitLanePresentationLayout layout) => AlongSpline(new[]
            {
                layout.PitLine, layout.Entry, layout.LaneWaypoints[0]
            }, progress, false, layout.EntryDirection,
                layout.ParkedHeading(PlayerId.Player1));

        internal static CarPresentationPose SharedMergePose(float progress,
            PitLanePresentationLayout layout) => AlongSpline(new[]
            {
                layout.LaneWaypoints[layout.LaneWaypoints.Count - 1],
                layout.MergeApproach, layout.ExitRejoin
            }, progress, false, default, layout.RejoinDirection);

        // The simulation's phase progress is linear time; the eased progress
        // shapes the drawn speed so both lane hand-offs stay continuous (issue
        // #110 hardware feel review: the old symmetric smoothstep started AND
        // ended every leg at zero velocity, so the car stopped dead on the pit
        // line and again at the merge). Hermite curves match the boundary
        // speeds instead: unit slope — the crawl the legs are paced at — on the
        // track end of each leg, zero slope at the box, peaking at 4/3 of the
        // crawl mid-leg (still under the corner-speed baseline). The simulation
        // meets both hand-offs at the same crawl: the approach braking delivers
        // the car to the line at it, and the exit resumes on track from it.
        private static float EaseIntoLane(float progress)
        {
            // Hermite with slopes (1, 0): cross the line at the crawl, settle to
            // a stop at the box.
            float t = Math.Max(0f, Math.Min(1f, progress));
            return t + t * t - t * t * t;
        }

        private static float EaseOutOfBox(float progress)
        {
            // Hermite with slopes (0, 1): pull away from the box, land on the
            // track at the crawl.
            float t = Math.Max(0f, Math.Min(1f, progress));
            return 2f * t * t - t * t * t;
        }

        private static CarPresentationPose AlongSpline(Vec2[] points, float progress, bool finished,
            Vec2 inDirection, Vec2 outDirection)
            => Along(SplineSamples(points, inDirection, outDirection), progress, finished,
                inDirection, outDirection);

        private static Vec2[] SplineSamples(Vec2[] points,
            Vec2 inDirection, Vec2 outDirection)
        {
            // 48 keeps every chord's turn small even where a pinned end
            // concentrates curvature into an S-bend. The extra endpoint
            // resolution also keeps the widened pit-road ribbon from turning
            // a smooth Fishhook handoff into a visible miter tooth (issue #89;
            // originally 12, then 24).
            var samples = new Vec2[
                (points.Length - 1) * SplineSamplesPerSegment + 1];
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
                for (int sample = 1; sample <= SplineSamplesPerSegment; sample++)
                    samples[index++] = CatmullRom(p0, p1, p2, p3,
                        sample / (float)SplineSamplesPerSegment);
            }
            return samples;
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

        private static CarPresentationPose Along(Vec2[] points, float progress, bool finished,
            Vec2 inDirection, Vec2 outDirection)
        {
            float total = 0f;
            for (int i = 0; i < points.Length - 1; i++) total += Distance(points[i], points[i + 1]);
            if (total <= 0f) return new CarPresentationPose(points[points.Length - 1],
                HasDirection(outDirection) ? Normalize(outDirection) : new Vec2(1f, 0f),
                finished);
            float pathDistance = Math.Max(0f, Math.Min(1f, progress)) * total;
            float remaining = pathDistance;
            for (int i = 0; i < points.Length - 1; i++)
            {
                float length = Distance(points[i], points[i + 1]);
                if (remaining <= length || i == points.Length - 2)
                {
                    float t = length <= 0f ? 1f : Math.Min(1f, remaining / length);
                    return new CarPresentationPose(Lerp(points[i], points[i + 1], t),
                        SmoothedPathHeading(points, pathDistance, total,
                            inDirection, outDirection), finished);
                }
                remaining -= length;
            }
            return new CarPresentationPose(points[points.Length - 1],
                HasDirection(outDirection) ? Normalize(outDirection) : new Vec2(1f, 0f),
                finished);
        }

        private static Vec2 SmoothedPathHeading(Vec2[] points, float distance,
            float total, Vec2 inDirection, Vec2 outDirection)
        {
            Vec2 behind = PointAtDistance(points,
                Math.Max(0f, distance - PathHeadingHalfSpan));
            Vec2 ahead = PointAtDistance(points,
                Math.Min(total, distance + PathHeadingHalfSpan));
            Vec2 heading = Unit(ahead, behind);

            // The central difference becomes one-sided at an endpoint. Blend
            // through the same spatial window so the moving pose still lands
            // on, and releases from, the exact authored hand-off tangent.
            if (HasDirection(inDirection) && distance < PathHeadingHalfSpan)
                heading = Normalize(Lerp(Normalize(inDirection), heading,
                    SmoothStep(distance / PathHeadingHalfSpan)));
            float distanceFromEnd = total - distance;
            if (HasDirection(outDirection) && distanceFromEnd < PathHeadingHalfSpan)
                heading = Normalize(Lerp(Normalize(outDirection), heading,
                    SmoothStep(distanceFromEnd / PathHeadingHalfSpan)));
            return heading;
        }

        private static Vec2 PointAtDistance(Vec2[] points, float distance)
        {
            float remaining = Math.Max(0f, distance);
            for (int i = 0; i < points.Length - 1; i++)
            {
                float length = Distance(points[i], points[i + 1]);
                if (remaining <= length || i == points.Length - 2)
                    return Lerp(points[i], points[i + 1],
                        length <= .00001f ? 1f : Math.Min(1f, remaining / length));
                remaining -= length;
            }
            return points[points.Length - 1];
        }

        private static float SmoothStep(float value)
        {
            float t = Math.Max(0f, Math.Min(1f, value));
            return t * t * (3f - 2f * t);
        }

        private static Vec2 Lerp(Vec2 from, Vec2 to, float t) =>
            new Vec2(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);

        private static Vec2 Unit(Vec2 to, Vec2 from) => Normalize(new Vec2(to.X - from.X, to.Y - from.Y));
        private static bool HasDirection(Vec2 value) =>
            Math.Abs(value.X) > .00001f || Math.Abs(value.Y) > .00001f;
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
