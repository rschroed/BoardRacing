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
        // The return anchor now sits after the parked apex instead of directly
        // opposite it. Compact courses need less authored centerline run than
        // the former one-anchor layout: Hourglass retains 115 px while its
        // shallow merge aim point still keeps the movements legible.
        public const float MinMergeRun = 110f;
        // Four-car pit review (#134): 94 px painted boxes keep 20 px of clear
        // pavement between neighboring edges, hence 114 px center spacing.
        public const float MinBoxGap = 20f;
        public const float MinBoxSpacing = RaceSurfaceGeometry.PitBoxHalfLength * 2f + MinBoxGap;
        public const float MinLaneJoinSpacing = PitRules.ProductionMinimumHeadway;
        public const float MinLaneToParkedCar = RaceSurfaceGeometry.PitLaneWidth * .5f +
            RaceSurfaceGeometry.CarBodyHalfWidth + 2f;
        public const float MinBranchToUnrelatedCar = RaceSurfaceGeometry.PitLaneWidth * .5f +
            RaceSurfaceGeometry.CarBodyHalfSize + 2f;
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
        // Infinity's detached lane intentionally threads beneath its bridge;
        // service joins may approach the crossing more closely than parked
        // cars because they remain on the already-authored lane centerline.
        public const float MinCrossingJoinClearance = 55f;
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
                {
                    anchors.Add((pit.EntryAnchors[i], $"player {i + 1} lane departure",
                        MinCrossingJoinClearance));
                    anchors.Add((pit.ExitAnchors[i], $"player {i + 1} lane return",
                        MinCrossingJoinClearance));
                    anchors.Add((pit.Boxes[i], $"player {i + 1} box",
                        MinCrossingBoxClearance));
                }
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
            // Skip the backdrop (issue #161): the ground covers the whole
            // 1920×1080 canvas on purpose, so it necessarily leaves the race
            // bounds and passes under the seat clusters. The question this
            // check exists to answer is whether the *course* fits, and the
            // ground is the table it is drawn on, not part of the course.
            for (int i = surface.BackdropVertexCount; i < surface.Vertices.Count; i++)
            {
                Vector3 vertex = surface.Vertices[i];
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
            for (int i = 0; i < pit.Stalls.Count; i++)
            {
                CheckAnchorOnSegment(course.Track, pit.EntryAnchors[i],
                    $"player {i + 1} lane departure", MinAnchorOffset, pitStraight, findings);
                CheckAnchorOnSegment(course.Track, pit.ExitAnchors[i],
                    $"player {i + 1} lane return", MinAnchorOffset, pitStraight, findings);
                CheckAnchorOnSegment(course.Track, pit.Boxes[i], $"player {i + 1} box",
                    MinBoxOffset, pitStraight, findings);
            }
            CheckAnchor(course.Track, pit.Exit, "exit", MinAnchorOffset, findings);
            // The merge approach is a spline aim point inside the taper — it
            // may legitimately hug or cross the edge line (the junction clamp
            // owns that region); it just has to stay on the interior side.
            CheckAnchor(course.Track, pit.MergeApproach, "merge approach", 0f, findings);

            float entry = DistanceAlongTrack(course.Track, pit.Entry);
            float[] departures = pit.EntryAnchors
                .Select(anchor => DistanceAlongTrack(course.Track, anchor, pitStraight)).ToArray();
            float[] returns = pit.ExitAnchors
                .Select(anchor => DistanceAlongTrack(course.Track, anchor, pitStraight)).ToArray();
            float[] centers = pit.LaneAnchors
                .Select(anchor => DistanceAlongTrack(course.Track, anchor, pitStraight)).ToArray();
            float approach = DistanceAlongTrack(course.Track, pit.MergeApproach);
            float rejoin = pit.ExitRejoinDistance;
            bool ordered = entry < departures[0] &&
                returns[returns.Length - 1] < approach &&
                approach < rejoin;
            for (int i = 0; i < departures.Length; i++)
                ordered &= departures[i] < returns[i];
            for (int i = 1; i < centers.Length; i++)
                ordered &= centers[i - 1] < centers[i];
            if (!ordered)
                findings.Add("The pit complex must run in travel order: entry, each departure " +
                    "before its return, ordered pit centers, merge approach, rejoin " +
                    $"(got {entry:0}, " +
                    $"{string.Join(", ", centers.Select(x => x.ToString("0")))}, " +
                    $"{approach:0}, {rejoin:0}).");
            if (entry < MinEntryRun)
                findings.Add($"The entry sits {entry:0} along the lap (min {MinEntryRun}) — " +
                    "the entry gore needs room to peel off past the start line.");
            if (rejoin - returns[returns.Length - 1] < MinMergeRun)
                findings.Add($"Only {rejoin - returns[returns.Length - 1]:0} px " +
                    "from the last lane return to the rejoin " +
                    $"(min {MinMergeRun}) — the merge would climb too steeply.");
            for (int i = 1; i < pit.Boxes.Count; i++)
            {
                float spacing = Vector2.Distance(Point(pit.Boxes[i - 1]), Point(pit.Boxes[i]));
                if (spacing < MinBoxSpacing - DistanceTolerance)
                    findings.Add($"Service boxes are {spacing:0} px apart " +
                        $"between positions {i} and {i + 1} (min {MinBoxSpacing}).");
                float departureSpacing = Vector2.Distance(Point(pit.EntryAnchors[i - 1]),
                    Point(pit.EntryAnchors[i]));
                float returnSpacing = Vector2.Distance(Point(pit.ExitAnchors[i - 1]),
                    Point(pit.ExitAnchors[i]));
                if (Mathf.Min(departureSpacing, returnSpacing) <
                    MinLaneJoinSpacing - DistanceTolerance)
                    findings.Add($"Stall lane joins are " +
                        $"{Mathf.Min(departureSpacing, returnSpacing):0} px apart " +
                        $"between positions {i} and {i + 1} " +
                        $"(min {MinLaneJoinSpacing}) — yielding cannot preserve car headway.");
            }
            float rowSpan = Vector2.Distance(Point(pit.Boxes[0]),
                Point(pit.Boxes[pit.Boxes.Count - 1]));
            if (pit.Boxes.Count == 4 && rowSpan < MinBoxSpacing * 3f - DistanceTolerance)
                findings.Add($"The four-box row spans only {rowSpan:0} px center-to-center " +
                    $"(min {MinBoxSpacing * 3f:0}).");
            CheckLaneAndBranchClearance(pit, findings);
            if (course.Track.Sample(rejoin).Kind == TrackSectionKind.Corner)
                findings.Add("The pit exit rejoins inside a corner — the merge gore needs a straight.");
        }

        private static void CheckLaneAndBranchClearance(PitComplexDefinition pit,
            List<string> findings)
        {
            var lane = new List<Vec2> { pit.Entry };
            lane.AddRange(OrderedLaneWaypoints(pit.Stalls));
            lane.Add(pit.MergeApproach);
            for (int stall = 0; stall < pit.Stalls.Count; stall++)
            {
                Vector2 parked = Point(pit.Boxes[stall]);
                float laneClearance = float.MaxValue;
                for (int segment = 0; segment < lane.Count - 1; segment++)
                    laneClearance = Mathf.Min(laneClearance, DistanceToSegment(
                        parked, Point(lane[segment]), Point(lane[segment + 1])));
                if (laneClearance < MinLaneToParkedCar - DistanceTolerance)
                    findings.Add($"The shared pit lane passes {laneClearance:0.0} px from " +
                        $"player {stall + 1}'s parked car (min {MinLaneToParkedCar:0.0}).");

                Vector2 branchEntry = Point(pit.EntryAnchors[stall]);
                Vector2 branchExit = Point(pit.ExitAnchors[stall]);
                for (int other = 0; other < pit.Stalls.Count; other++)
                {
                    if (other == stall) continue;
                    float clearance = Mathf.Min(
                        DistanceToSegment(Point(pit.Boxes[other]), branchEntry, parked),
                        DistanceToSegment(Point(pit.Boxes[other]), parked, branchExit));
                    if (clearance >= MinBranchToUnrelatedCar - DistanceTolerance) continue;
                    findings.Add($"Player {stall + 1}'s stall branch passes {clearance:0.0} px " +
                        $"from player {other + 1}'s parked car " +
                        $"(min {MinBranchToUnrelatedCar:0.0}).");
                }
            }
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
                    (point.Y - origin.Y) * heading.Y);
            var unique = new List<Vec2>();
            foreach (Vec2 point in ordered)
                if (unique.Count == 0 ||
                    Vector2.Distance(Point(unique[unique.Count - 1]), Point(point)) > .0001f)
                    unique.Add(point);
            return unique;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 direction = end - start;
            if (direction.sqrMagnitude <= .00001f) return Vector2.Distance(point, start);
            float t = Mathf.Clamp01(Vector2.Dot(point - start, direction) /
                direction.sqrMagnitude);
            return Vector2.Distance(point, start + direction * t);
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
