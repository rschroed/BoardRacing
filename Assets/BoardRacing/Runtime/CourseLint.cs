using System.Collections.Generic;
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
        // Service boxes are 140 px wide; closer than this and they collide.
        public const float MinBoxSpacing = 180f;
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
                foreach ((Vec2 point, string name) in new[]
                {
                    (start, "start line"), (pit.Entry, "pit entry"),
                    (pit.PlayerOneBox, "player one box"), (pit.PlayerTwoBox, "player two box"),
                    (pit.Exit, "pit exit"), (rejoin, "exit rejoin"),
                })
                {
                    float distance = Vector2.Distance(crossing.Point,
                        new Vector2(point.X, point.Y));
                    if (distance < MinCrossingClearance)
                        findings.Add($"The {name} sits {distance:0} px from the crossing at " +
                            $"({crossing.Point.x:0}, {crossing.Point.y:0}) (min {MinCrossingClearance}) — " +
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
            CheckAnchor(course.Track, pit.Entry, "entry", MinAnchorOffset, findings);
            CheckAnchor(course.Track, pit.PlayerOneBox, "player one box", MinBoxOffset, findings);
            CheckAnchor(course.Track, pit.PlayerTwoBox, "player two box", MinBoxOffset, findings);
            CheckAnchor(course.Track, pit.Exit, "exit", MinAnchorOffset, findings);
            // The merge approach is a spline aim point inside the taper — it
            // may legitimately hug or cross the edge line (the junction clamp
            // owns that region); it just has to stay on the interior side.
            CheckAnchor(course.Track, pit.MergeApproach, "merge approach", 0f, findings);

            float entry = DistanceAlongTrack(course.Track, pit.Entry);
            float boxOne = DistanceAlongTrack(course.Track, pit.PlayerOneBox);
            float boxTwo = DistanceAlongTrack(course.Track, pit.PlayerTwoBox);
            float approach = DistanceAlongTrack(course.Track, pit.MergeApproach);
            float rejoin = pit.ExitRejoinDistance;
            if (!(entry < boxOne && boxOne < boxTwo && boxTwo < approach && approach < rejoin))
                findings.Add("The pit complex must run in travel order: entry, boxes, merge " +
                    $"approach, rejoin (got {entry:0}, {boxOne:0}, {boxTwo:0}, {approach:0}, {rejoin:0}).");
            if (entry < MinEntryRun)
                findings.Add($"The entry sits {entry:0} along the lap (min {MinEntryRun}) — " +
                    "the entry gore needs room to peel off past the start line.");
            if (rejoin - boxTwo < MinMergeRun)
                findings.Add($"Only {rejoin - boxTwo:0} px from the last box to the rejoin " +
                    $"(min {MinMergeRun}) — the merge would climb too steeply.");
            if (boxTwo - boxOne < MinBoxSpacing)
                findings.Add($"Service boxes are {boxTwo - boxOne:0} px apart (min {MinBoxSpacing}).");
            if (course.Track.Sample(rejoin).Kind == TrackSectionKind.Corner)
                findings.Add("The pit exit rejoins inside a corner — the merge gore needs a straight.");
        }

        private static void CheckAnchor(TrackDefinition track, Vec2 anchor, string name,
            float minimumOffset, List<string> findings)
        {
            float offset = RaceSurfaceGeometry.InteriorOffset(new Vector2(anchor.X, anchor.Y), track);
            if (offset < minimumOffset)
                findings.Add($"The pit {name} at ({anchor.X:0}, {anchor.Y:0}) sits {offset:0.0} " +
                    $"inside the loop (min {minimumOffset:0.0}) — on or under the roadway.");
            else if (offset > MaxAnchorOffset)
                findings.Add($"The pit {name} at ({anchor.X:0}, {anchor.Y:0}) sits {offset:0.0} " +
                    $"inside the loop (max {MaxAnchorOffset:0}) — the lane strays from the track.");
            if (NearestSegment(track, anchor).Kind == TrackSectionKind.Corner)
                findings.Add($"The pit {name} hangs off a corner chord — the complex needs a straight.");
        }

        private static float DistanceAlongTrack(TrackDefinition track, Vec2 point)
        {
            var target = new Vector2(point.X, point.Y);
            float best = float.MaxValue, along = 0f, cumulative = 0f;
            foreach (TrackSegment segment in track.Segments)
            {
                Vector2 start = Point(segment.Start), end = Point(segment.End);
                Vector2 direction = end - start;
                float length = direction.magnitude;
                float t = Mathf.Clamp01(Vector2.Dot(target - start, direction) / direction.sqrMagnitude);
                float distance = Vector2.Distance(target, start + direction * t);
                if (distance < best)
                {
                    best = distance;
                    along = cumulative + t * length;
                }
                cumulative += length;
            }
            return along;
        }

        private static TrackSegment NearestSegment(TrackDefinition track, Vec2 point)
        {
            var target = new Vector2(point.X, point.Y);
            TrackSegment nearest = track.Segments[0];
            float best = float.MaxValue;
            foreach (TrackSegment segment in track.Segments)
            {
                Vector2 start = Point(segment.Start), end = Point(segment.End);
                Vector2 direction = end - start;
                float t = Mathf.Clamp01(Vector2.Dot(target - start, direction) / direction.sqrMagnitude);
                float distance = Vector2.Distance(target, start + direction * t);
                if (distance >= best) continue;
                best = distance;
                nearest = segment;
            }
            return nearest;
        }

        private static Vector2 Point(Vec2 value) => new Vector2(value.X, value.Y);
    }
}
