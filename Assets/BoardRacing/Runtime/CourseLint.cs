using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    /// <summary>
    /// Authoring-time validation for a course (issue #107 phase 3): everything
    /// that makes a course presentable on the table but that CourseDefinition's
    /// constructor cannot know — the drawn surface must fit the shared race
    /// bounds and clear the fixed seat clusters, the racing line must turn
    /// gently enough for the ribbon smoothing, and the pit complex must leave
    /// room for its junctions. Runs in the EditMode suite over the whole
    /// catalog; a future course editor can call it directly. Findings are
    /// human-readable sentences; an empty list is a clean course.
    /// </summary>
    internal static class CourseLint
    {
        // The ribbon smoothing erases scalloping only if the authored polyline
        // already steps gently (TrackCatalog authors ≤12-13°).
        public const float MaxChordTurnDegrees = 13.5f;
        // Physical pieces and hands need the seat cluster untouched; a course
        // may approach it no closer than this.
        public const float SeatClearance = 12f;
        // Straight-run room the junction gores need to read as slip roads: the
        // entry mouth opens past the start line, the merge climbs from the last
        // box to the rejoin (issue #107 phase 2 landed at ~230 px and ≤26°).
        public const float MinEntryRun = 150f;
        public const float MinMergeRun = 200f;
        // Four-car pit review (#134): 94 px painted boxes keep 20 px of clear
        // pavement between neighboring edges, hence 114 px center spacing.
        public const float MinBoxGap = 20f;
        public const float MinBoxSpacing = RaceSurfaceGeometry.PitBoxHalfLength * 2f + MinBoxGap;
        private const float DistanceTolerance = .01f;
        // Pit anchors must sit off the pavement (entry/exit may hug the edge
        // mid-taper; parked boxes need the full lane width clear) but must not
        // wander into the middle of the infield either.
        public const float MinAnchorOffset = RaceSurfaceGeometry.TrackWidth * .5f + 2f;
        public const float MinBoxOffset = RaceSurfaceGeometry.TrackWidth * .5f +
            RaceSurfaceGeometry.PitLaneWidth * .5f;
        public const float MaxAnchorOffset = 120f;
        // A racing line may cross itself (figure-8, issue #107 phase 4) — but
        // the X must read at 64 px ribbon width, and the crossing must stay
        // away from the pit complex and the start line: near a crossing,
        // nearest-chord logic (junction clamping, anchor checks) is ambiguous
        // between the two strands.
        public const float MinCrossingAngle = 35f;
        public const float MinCrossingClearance = 150f;
        // Infinity deliberately runs the service row beneath its bridge. A box
        // center may approach more closely than other anchors provided its
        // 94×46 quad stays visibly outside the 64 px crossing ribbon.
        public const float MinCrossingBoxClearance = 75f;

        public static IReadOnlyList<string> Check(CourseDefinition course, RaceLayout seats)
        {
            var findings = new List<string>();
            CheckChords(course.Track, findings);
            CheckSurfaceFit(course, seats, findings);
            CheckPitComplex(course, findings);
            CheckCrossings(course, findings);
            return findings;
        }

        private static void CheckCrossings(CourseDefinition course, List<string> findings)
        {
            foreach (TrackCrossing crossing in RaceSurfaceGeometry.FindCrossings(course.Track))
            {
                TrackSegment earlier = course.Track.Segments[crossing.EarlierSegment];
                float angle = Vector2.Angle(
                    new Vector2(earlier.End.X - earlier.Start.X, earlier.End.Y - earlier.Start.Y),
                    crossing.LaterDirection);
                float acute = Mathf.Min(angle, 180f - angle);
                if (acute < MinCrossingAngle)
                    findings.Add($"The line crosses itself at {acute:0}° near " +
                        $"({crossing.Point.x:0}, {crossing.Point.y:0}) (min {MinCrossingAngle}°) — " +
                        "a shallow X reads as a smudge at ribbon width.");
                PitComplexDefinition pit = course.Pit;
                Vec2 start = course.Track.Sample(0f).Position;
                Vec2 rejoin = course.Track.Sample(pit.ExitRejoinDistance).Position;
                var anchors = new List<(Vec2 point, string name, float clearance)>
                {
                    (start, "start line", MinCrossingClearance),
                    (pit.Entry, "pit entry", MinCrossingClearance),
                    (pit.Exit, "pit exit", MinCrossingClearance),
                    (rejoin, "exit rejoin", MinCrossingClearance)
                };
                for (int i = 0; i < pit.Boxes.Count; i++)
                    anchors.Add((pit.Boxes[i], $"player {i + 1} box",
                        MinCrossingBoxClearance));
                foreach ((Vec2 point, string name, float clearance) in anchors)
                {
                    float distance = Vector2.Distance(crossing.Point,
                        new Vector2(point.X, point.Y));
                    if (distance < clearance)
                        findings.Add($"The {name} sits {distance:0} px from the crossing at " +
                            $"({crossing.Point.x:0}, {crossing.Point.y:0}) (min {clearance}) — " +
                            "nearest-chord logic is ambiguous between the strands there.");
                }
            }
        }

        private static void CheckChords(TrackDefinition track, List<string> findings)
        {
            IReadOnlyList<TrackSegment> segments = track.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                TrackSegment current = segments[i];
                TrackSegment next = segments[(i + 1) % segments.Count];
                if (Vector2.Distance(Point(current.End), Point(next.Start)) > .01f)
                {
                    findings.Add($"Chord {i} ends at {Point(current.End)} but chord " +
                        $"{(i + 1) % segments.Count} starts at {Point(next.Start)} — the racing line must close.");
                    continue;
                }
                float turn = Vector2.Angle(Point(current.End) - Point(current.Start),
                    Point(next.End) - Point(next.Start));
                if (turn > MaxChordTurnDegrees)
                    findings.Add($"Chords {i}->{(i + 1) % segments.Count} turn {turn:0.0}° " +
                        $"(max {MaxChordTurnDegrees}°) — the drawn ribbon would scallop.");
            }
        }

        // The one authority on what a course draws is the surface builder
        // itself: lint the vertices it actually emits (track, stripes, lane,
        // gores, boxes, start/finish) instead of re-deriving footprints.
        private static void CheckSurfaceFit(CourseDefinition course, RaceLayout seats,
            List<string> findings)
        {
            SurfaceMeshData surface = RaceSurfaceGeometry.Build(course.Track,
                PitLanePresentationLayout.ForCourse(course), Color.red, Color.blue);
            int outsideBounds = 0, intruding = 0;
            Vector3 firstOutside = default, firstIntruding = default;
            foreach (Vector3 vertex in surface.Vertices)
            {
                if (!seats.SharedRaceBounds.Contains(new Vector2(vertex.x, vertex.y)))
                {
                    if (outsideBounds++ == 0) firstOutside = vertex;
                }
                if (IntrudesOnSeat(vertex, seats.PlayerOne) || IntrudesOnSeat(vertex, seats.PlayerTwo))
                {
                    if (intruding++ == 0) firstIntruding = vertex;
                }
            }
            if (outsideBounds > 0)
                findings.Add($"{outsideBounds} surface vertices escape the shared race bounds " +
                    $"{seats.SharedRaceBounds} (first at {firstOutside}).");
            if (intruding > 0)
                findings.Add($"{intruding} surface vertices intrude on a seat cluster " +
                    $"(first at {firstIntruding}).");
        }

        private static bool IntrudesOnSeat(Vector2 vertex, PlayerLayout seat)
        {
            CornerControllerLayout controller = seat.Controller;
            if (InDisc(vertex, controller.ArcCenter, controller.ThrottleRadius)) return true;
            if (InDisc(vertex, controller.ShipWellCenter, controller.ShipWellRadius)) return true;
            foreach (Rect zone in new[] { seat.CallPit, seat.Tires, seat.Fuel,
                controller.BrakeLabel.Bounds, controller.DriveLabel.Bounds,
                controller.BoostLabel.Bounds, controller.TiresLabel.Bounds,
                controller.FuelLabel.Bounds, controller.CallPitLabel.Bounds })
            {
                var inflated = new Rect(zone.x - SeatClearance, zone.y - SeatClearance,
                    zone.width + SeatClearance * 2f, zone.height + SeatClearance * 2f);
                if (inflated.Contains(vertex)) return true;
            }
            return false;
        }

        private static bool InDisc(Vector2 vertex, Vector2 center, float radius) =>
            Vector2.Distance(vertex, center) < radius + SeatClearance;

        private static void CheckPitComplex(CourseDefinition course, List<string> findings)
        {
            PitComplexDefinition pit = course.Pit;
            if (pit.Boxes.Count != 4)
                findings.Add($"The production pit complex has {pit.Boxes.Count} boxes (requires exactly 4).");
            CheckAnchor(course.Track, pit.Entry, "entry", MinAnchorOffset, findings);
            int pitStraight = NearestSegmentIndex(course.Track, pit.Entry);
            for (int i = 0; i < pit.Boxes.Count; i++)
                CheckAnchorOnSegment(course.Track, pit.Boxes[i], $"player {i + 1} box",
                    MinBoxOffset, pitStraight, findings);
            CheckAnchor(course.Track, pit.Exit, "exit", MinAnchorOffset, findings);
            // The merge approach is a spline aim point inside the taper — it
            // may legitimately hug or cross the edge line (the junction clamp
            // owns that region); it just has to stay on the interior side.
            CheckAnchor(course.Track, pit.MergeApproach, "merge approach", 0f, findings);

            float entry = DistanceAlongTrack(course.Track, pit.Entry);
            float[] boxes = pit.Boxes
                .Select(box => DistanceAlongTrack(course.Track, box, pitStraight)).ToArray();
            float approach = DistanceAlongTrack(course.Track, pit.MergeApproach);
            float rejoin = pit.ExitRejoinDistance;
            bool ordered = entry < boxes[0] && boxes[boxes.Length - 1] < approach &&
                approach < rejoin;
            for (int i = 1; i < boxes.Length; i++) ordered &= boxes[i - 1] < boxes[i];
            if (!ordered)
                findings.Add("The pit complex must run in travel order: entry, boxes, merge " +
                    $"approach, rejoin (got {entry:0}, {string.Join(", ", boxes.Select(x => x.ToString("0")))}, " +
                    $"{approach:0}, {rejoin:0}).");
            if (entry < MinEntryRun)
                findings.Add($"The entry sits {entry:0} along the lap (min {MinEntryRun}) — " +
                    "the entry gore needs room to peel off past the start line.");
            if (rejoin - boxes[boxes.Length - 1] < MinMergeRun)
                findings.Add($"Only {rejoin - boxes[boxes.Length - 1]:0} px from the last box to the rejoin " +
                    $"(min {MinMergeRun}) — the merge would climb too steeply.");
            for (int i = 1; i < boxes.Length; i++)
            {
                float spacing = Vector2.Distance(Point(pit.Boxes[i - 1]), Point(pit.Boxes[i]));
                if (spacing < MinBoxSpacing - DistanceTolerance)
                    findings.Add($"Service boxes are {spacing:0} px apart " +
                        $"between positions {i} and {i + 1} (min {MinBoxSpacing}).");
            }
            float rowSpan = Vector2.Distance(Point(pit.Boxes[0]),
                Point(pit.Boxes[pit.Boxes.Count - 1]));
            if (boxes.Length == 4 && rowSpan < MinBoxSpacing * 3f - DistanceTolerance)
                findings.Add($"The four-box row spans only {rowSpan:0} px center-to-center " +
                    $"(min {MinBoxSpacing * 3f:0}).");
            if (course.Track.Sample(rejoin).Kind == TrackSectionKind.Corner)
                findings.Add("The pit exit rejoins inside a corner — the merge gore needs a straight.");
        }

        private static void CheckAnchor(TrackDefinition track, Vec2 anchor, string name,
            float minimumOffset, List<string> findings)
        {
            CheckAnchorOnSegment(track, anchor, name, minimumOffset,
                NearestSegmentIndex(track, anchor), findings);
        }

        private static void CheckAnchorOnSegment(TrackDefinition track, Vec2 anchor, string name,
            float minimumOffset, int segmentIndex, List<string> findings)
        {
            TrackSegment segment = track.Segments[segmentIndex];
            Vector2 start = Point(segment.Start), end = Point(segment.End);
            Vector2 direction = end - start;
            float t = Mathf.Clamp01(Vector2.Dot(Point(anchor) - start, direction) /
                direction.sqrMagnitude);
            Vector2 nearest = start + direction * t;
            Vector2 unit = direction.normalized;
            Vector2 interiorNormal = new Vector2(-unit.y, unit.x);
            float offset = Vector2.Dot(Point(anchor) - nearest, interiorNormal);
            if (offset < minimumOffset)
                findings.Add($"The pit {name} at ({anchor.X:0}, {anchor.Y:0}) sits {offset:0.0} " +
                    $"inside the loop (min {minimumOffset:0.0}) — on or under the roadway.");
            else if (offset > MaxAnchorOffset)
                findings.Add($"The pit {name} at ({anchor.X:0}, {anchor.Y:0}) sits {offset:0.0} " +
                    $"inside the loop (max {MaxAnchorOffset:0}) — the lane strays from the track.");
            if (segment.Kind == TrackSectionKind.Corner)
                findings.Add($"The pit {name} hangs off a corner chord — the complex needs a straight.");
        }

        private static float DistanceAlongTrack(TrackDefinition track, Vec2 point)
        {
            return DistanceAlongTrack(track, point, NearestSegmentIndex(track, point));
        }

        private static float DistanceAlongTrack(TrackDefinition track, Vec2 point, int segmentIndex)
        {
            var target = new Vector2(point.X, point.Y);
            float cumulative = 0f;
            for (int i = 0; i < track.Segments.Count; i++)
            {
                TrackSegment segment = track.Segments[i];
                Vector2 start = Point(segment.Start), end = Point(segment.End);
                Vector2 direction = end - start;
                float length = direction.magnitude;
                if (i == segmentIndex)
                    return cumulative + Mathf.Clamp01(
                        Vector2.Dot(target - start, direction) / direction.sqrMagnitude) * length;
                cumulative += length;
            }
            return cumulative;
        }

        private static int NearestSegmentIndex(TrackDefinition track, Vec2 point)
        {
            var target = new Vector2(point.X, point.Y);
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < track.Segments.Count; i++)
            {
                TrackSegment segment = track.Segments[i];
                Vector2 start = Point(segment.Start), end = Point(segment.End);
                Vector2 direction = end - start;
                float t = Mathf.Clamp01(Vector2.Dot(target - start, direction) / direction.sqrMagnitude);
                float distance = Vector2.Distance(target, start + direction * t);
                if (distance >= best) continue;
                best = distance;
                nearest = i;
            }
            return nearest;
        }

        private static Vector2 Point(Vec2 value) => new Vector2(value.X, value.Y);
    }
}
