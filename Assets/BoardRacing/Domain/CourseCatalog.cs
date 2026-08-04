using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardRacing.Domain
{
    public enum PitRoadbedKind
    {
        Trackside,
        Detached,
    }

    /// <summary>
    /// One authored service stall beside the common pit lane. A car leaves the
    /// lane at EntryAnchor, settles at ParkedPosition (the service curve apex),
    /// and returns at ExitAnchor. The parked heading is tangent to that apex.
    /// </summary>
    public readonly struct PitStallDefinition
    {
        public PitStallDefinition(Vec2 laneAnchor, Vec2 parkedPosition, Vec2 parkedHeading)
            : this(laneAnchor, laneAnchor, parkedPosition, parkedHeading)
        {
        }

        public PitStallDefinition(Vec2 entryAnchor, Vec2 exitAnchor,
            Vec2 parkedPosition, Vec2 parkedHeading)
        {
            float headingLength = (float)Math.Sqrt(
                parkedHeading.X * parkedHeading.X + parkedHeading.Y * parkedHeading.Y);
            if (float.IsNaN(headingLength) || float.IsInfinity(headingLength) ||
                headingLength <= .00001f)
                throw new ArgumentException("A pit stall needs a non-zero parked heading.",
                    nameof(parkedHeading));
            EntryAnchor = entryAnchor;
            ExitAnchor = exitAnchor;
            ParkedPosition = parkedPosition;
            ParkedHeading = new Vec2(
                parkedHeading.X / headingLength, parkedHeading.Y / headingLength);
        }

        public Vec2 EntryAnchor { get; }
        public Vec2 ExitAnchor { get; }
        // Compatibility/reference point for systems that need the lane opposite
        // the pit (kit orientation, distance-to-lane lint), not a traffic join.
        public Vec2 LaneAnchor => new Vec2(
            (EntryAnchor.X + ExitAnchor.X) * .5f,
            (EntryAnchor.Y + ExitAnchor.Y) * .5f);
        public Vec2 ParkedPosition { get; }
        public Vec2 ParkedHeading { get; }
    }

    /// <summary>
    /// The pit complex as authored course geometry (issues #107 and #182):
    /// entry ramp, one-way shared lane, one private branch and service stall per
    /// player, exit, merge approach, and physical track rejoin.
    /// </summary>
    public readonly struct PitComplexDefinition
    {
        public PitComplexDefinition(Vec2 entry, IEnumerable<Vec2> boxes,
            Vec2 exit, Vec2 mergeApproach, float exitRejoinDistance)
            : this(entry, LegacyStalls(boxes), exit, mergeApproach, exitRejoinDistance)
        {
        }

        public PitComplexDefinition(Vec2 entry, IEnumerable<PitStallDefinition> stalls,
            Vec2 exit, Vec2 mergeApproach, float exitRejoinDistance)
        {
            if (float.IsNaN(exitRejoinDistance) || float.IsInfinity(exitRejoinDistance) ||
                exitRejoinDistance <= 0f)
                throw new ArgumentException("The pit exit must rejoin at a positive track distance.",
                    nameof(exitRejoinDistance));
            PitStallDefinition[] authoredStalls = stalls?.ToArray() ??
                throw new ArgumentNullException(nameof(stalls));
            if (authoredStalls.Length < 2 || authoredStalls.Length > 4)
                throw new ArgumentException("A pit complex requires two to four ordered stalls.",
                    nameof(stalls));
            Entry = entry;
            Stalls = Array.AsReadOnly(authoredStalls);
            LaneAnchors = Array.AsReadOnly(authoredStalls.Select(x => x.LaneAnchor).ToArray());
            EntryAnchors = Array.AsReadOnly(authoredStalls.Select(x => x.EntryAnchor).ToArray());
            ExitAnchors = Array.AsReadOnly(authoredStalls.Select(x => x.ExitAnchor).ToArray());
            Boxes = Array.AsReadOnly(authoredStalls.Select(x => x.ParkedPosition).ToArray());
            Exit = exit;
            MergeApproach = mergeApproach;
            ExitRejoinDistance = exitRejoinDistance;
        }

        public PitComplexDefinition(Vec2 entry, Vec2 playerOneBox, Vec2 playerTwoBox,
            Vec2 exit, Vec2 mergeApproach, float exitRejoinDistance)
            : this(entry, new[] { playerOneBox, playerTwoBox }, exit, mergeApproach,
                exitRejoinDistance)
        {
        }

        public Vec2 Entry { get; }
        public IReadOnlyList<PitStallDefinition> Stalls { get; }
        public IReadOnlyList<Vec2> LaneAnchors { get; }
        public IReadOnlyList<Vec2> EntryAnchors { get; }
        public IReadOnlyList<Vec2> ExitAnchors { get; }
        public IReadOnlyList<Vec2> Boxes { get; }
        public Vec2 Exit { get; }
        public Vec2 MergeApproach { get; }
        // The lane blends onto the track just before the rejoin sample — no
        // return trip: the simulation resumes the car where the pit lane
        // physically ends.
        public float ExitRejoinDistance { get; }

        public Vec2 Box(PlayerId playerId)
        {
            int index = (int)playerId - 1;
            if (index < 0 || index >= Boxes.Count)
                throw new ArgumentOutOfRangeException(nameof(playerId),
                    "The pit complex has no box for that racer.");
            return Boxes[index];
        }

        public PitStallDefinition Stall(PlayerId playerId)
        {
            int index = (int)playerId - 1;
            if (index < 0 || index >= Stalls.Count)
                throw new ArgumentOutOfRangeException(nameof(playerId),
                    "The pit complex has no stall for that racer.");
            return Stalls[index];
        }

        private static IEnumerable<PitStallDefinition> LegacyStalls(IEnumerable<Vec2> boxes)
        {
            Vec2[] authoredBoxes = boxes?.ToArray() ??
                throw new ArgumentNullException(nameof(boxes));
            if (authoredBoxes.Length < 2) return Array.Empty<PitStallDefinition>();
            Vec2 first = authoredBoxes[0], last = authoredBoxes[authoredBoxes.Length - 1];
            var heading = new Vec2(last.X - first.X, last.Y - first.Y);
            return authoredBoxes.Select(box => new PitStallDefinition(box, box, heading));
        }
    }

    /// <summary>
    /// One authored course (issue #107 phase 1): everything a track IS lives in
    /// one artifact — the racing line (with per-corner safe speeds), the pit
    /// complex hanging off it, and the lap count that keeps race duration
    /// consistent across courses of different perimeters. Seat clusters are
    /// deliberately NOT course data: they are physical geometry (pieces, hand
    /// reach) and stay fixed whatever course is on the table.
    /// </summary>
    public sealed class CourseDefinition
    {
        public CourseDefinition(string name, TrackDefinition track, PitComplexDefinition pit,
            int laps, PitRoadbedKind pitRoadbed = PitRoadbedKind.Trackside)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A course needs a name.", nameof(name));
            Track = track ?? throw new ArgumentNullException(nameof(track));
            if (laps < 1)
                throw new ArgumentException("A race needs at least one lap.", nameof(laps));
            if (pit.ExitRejoinDistance >= track.Length)
                throw new ArgumentException("The pit exit must rejoin within one lap.",
                    nameof(pit));
            Name = name;
            Pit = pit;
            Laps = laps;
            PitRoadbed = pitRoadbed;
        }

        public string Name { get; }
        public TrackDefinition Track { get; }
        public PitComplexDefinition Pit { get; }
        public int Laps { get; }
        public PitRoadbedKind PitRoadbed { get; }
    }

    /// <summary>
    /// The designed course library (issues #88, #107). Racing lines stay in
    /// TrackCatalog; a course wraps one with its pit complex and race length.
    /// </summary>
    public static class CourseCatalog
    {
        // Direction A (#182): the parked row sits 54 px across the common lane.
        // Lane half-width (15) + parked car half-width (13) leaves 26 px of
        // visible separation between pavement centre and body, while the 94×46
        // service outline retains 16 px of clear ground from the lane edge.
        public const float ParallelStallOffset = 54f;
        // Owner path sketch (#183): each car leaves the common lane before its
        // pit, stops at the service-curve apex, and rejoins after it. Forty-six
        // pixels each side gives a readable loop while retaining 22 px between
        // neighboring 114 px stall centers.
        public const float ServiceCurveHalfSpan = 46f;
        // Fishhook has the shallowest board-edge margin in the catalog. Its
        // service apex sits a little closer to the lane so the full car-sized
        // pavement and exterior apron remain inside the shared race bounds.
        public const float FishhookParallelStallOffset = 35.5f;

        // Every course the game can put on the table. The course lint sweeps
        // this (issue #107 phase 3), and the between-race course selection
        // (phase 5) will draw from it — a new course added here is linted and
        // offered automatically.
        public static System.Collections.Generic.IEnumerable<CourseDefinition> All(
            float cornerSafeSpeed = Pace.CornerSafeSpeed)
        {
            yield return Wedge(cornerSafeSpeed);
            yield return Hourglass(cornerSafeSpeed);
            yield return Infinity(cornerSafeSpeed);
            yield return Fishhook(cornerSafeSpeed);
        }

        // Pit complex re-derived from the Wedge top straight (issue #88): entry
        // ramps off the start/finish line, the lane parallels the straight inside
        // the loop, and the exit rejoins the straight at 850 of its 911 units —
        // just before the sweeper.
        // The merge approach sits 120 px past the last compact box center and
        // 27 px above the lane center: it stretches the exit spline's climb across
        // ~230 px so the visible crossing stays a shallow slip-road angle (issue
        // #107 phase 2 — aiming through (1283, 452) packed a 58 px climb into
        // 70 px of run, a ~40° dive that read as the lane vanishing under the
        // track in three hardware reviews).
        // 6 laps × the Wedge's 2628 perimeter ≈ the placeholder's 5 × 3508 race
        // distance, keeping race duration roughly where the owner tuned it, with
        // the tight hairpin adding scrub time per lap (issue #88).
        public static CourseDefinition Wedge(float cornerSafeSpeed = Pace.CornerSafeSpeed) => new CourseDefinition(
            "Wedge",
            TrackCatalog.Wedge(cornerSafeSpeed),
            new PitComplexDefinition(new Vec2(680f, 455f), ParallelStalls(new[]
                {
                    new Vec2(778f, 455f), new Vec2(892f, 455f),
                    new Vec2(1006f, 455f), new Vec2(1120f, 455f)
                }),
                new Vec2(1353f, 455f), new Vec2(1240f, 428f), 850f),
            laps: 6);

        // Hourglass pit complex hangs off the 720 px top straight of the right
        // lobe (the crossing lives far away at (568, 550)). The approved
        // merge-safe four-box row uses entry 150, boxes 209/323/437/551, and
        // rejoin 712 — 8 before the sweeper. Hardware review moved the whole
        // row east so the first service curve clears the entry bend while the
        // final return retains room for the merge.
        // The merge approach follows the Wedge's phase-2 tuning: 50 px past
        // the last compact box quad and 27 px above the lane center, for a
        // shallow climb that starts visibly clear of the box.
        // 5 laps × the ~2949 perimeter ≈ the Wedge's 6 × 2628 race distance.
        public static CourseDefinition Hourglass(float cornerSafeSpeed = Pace.CornerSafeSpeed) => new CourseDefinition(
            "Hourglass",
            TrackCatalog.Hourglass(cornerSafeSpeed),
            new PitComplexDefinition(new Vec2(930f, 462f), ParallelStalls(new[]
                {
                    new Vec2(989f, 462f), new Vec2(1103f, 462f),
                    new Vec2(1217f, 462f), new Vec2(1331f, 462f)
                }),
                new Vec2(1450f, 462f), new Vec2(1389f, 435f), 712f),
            laps: 5);

        // Infinity's approved four-box row hangs off the ascending diagonal
        // (886 px), 60 px inside the loop. Its 114 px spacing threads the row
        // beneath the crossing without letting the compact painted quads touch
        // the bridge ribbon; the 70 px flat merge remains intact. 5 laps ×
        // ~3224 ≈ the Wedge's race distance.
        public static CourseDefinition Infinity(float cornerSafeSpeed = Pace.CornerSafeSpeed) => new CourseDefinition(
            "Infinity",
            TrackCatalog.Infinity(cornerSafeSpeed),
            new PitComplexDefinition(new Vec2(724f, 695f), ParallelStalls(new[]
                {
                    new Vec2(821.6974f, 662.6456f), new Vec2(926.8694f, 618.6586f),
                    new Vec2(1032.0414f, 574.6716f), new Vec2(1137.2133f, 530.6845f)
                }),
                new Vec2(1305f, 452f), new Vec2(1235f, 452f), 815f),
            laps: 5,
            pitRoadbed: PitRoadbedKind.Detached);

        // Fishhook's four-box row follows the long climbing diagonal (895 px)
        // with compact boxes at 114 px centers. The last box retains the old
        // final box anchor and existing 331 px merge run. 4 laps × ~4072 ≈
        // the Wedge's race distance.
        public static CourseDefinition Fishhook(float cornerSafeSpeed = Pace.CornerSafeSpeed) => new CourseDefinition(
            "Fishhook",
            TrackCatalog.Fishhook(cornerSafeSpeed),
            new PitComplexDefinition(FishhookPoint(628f, 787.375f),
                ParallelStalls(FishhookLaneAnchors(), FishhookParallelStallOffset,
                    branchHalfSpan: 30f),
                FishhookPoint(1195f, 730.825f),
                FishhookPoint(1122f, 711.325f), 755f),
            laps: 4,
            pitRoadbed: PitRoadbedKind.Detached);

        private static Vec2 FishhookPoint(float x, float y) =>
            new Vec2(TrackCatalog.FishhookBoardX(x), TrackCatalog.FishhookBoardY(y));

        private static IReadOnlyList<Vec2> FishhookLaneAnchors()
        {
            Vec2 first = FishhookPoint(665.5193f, 780.7046f);
            Vec2 last = FishhookPoint(1005.8885f, 747.3518f);
            float dx = last.X - first.X, dy = last.Y - first.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            var heading = new Vec2(dx / length, dy / length);
            var inward = new Vec2(-heading.Y, heading.X);
            // The board-fit Y compression slightly reduces perpendicular
            // clearance from the diagonal. Restore it without changing the
            // approved row direction or 114 px stall cadence.
            var start = new Vec2(first.X + inward.X * 2f, first.Y + inward.Y * 2f);
            return Enumerable.Range(0, 4).Select(index => new Vec2(
                start.X + heading.X * 114f * index,
                start.Y + heading.Y * 114f * index)).ToArray();
        }

        private static IReadOnlyList<PitStallDefinition> ParallelStalls(
            IReadOnlyList<Vec2> laneAnchors, float offset = ParallelStallOffset,
            float branchHalfSpan = ServiceCurveHalfSpan)
        {
            Vec2 first = laneAnchors[0], last = laneAnchors[laneAnchors.Count - 1];
            float dx = last.X - first.X, dy = last.Y - first.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            var heading = new Vec2(dx / length, dy / length);
            var inward = new Vec2(-heading.Y, heading.X);
            return laneAnchors.Select(anchor => new PitStallDefinition(
                new Vec2(anchor.X - heading.X * branchHalfSpan,
                    anchor.Y - heading.Y * branchHalfSpan),
                new Vec2(anchor.X + heading.X * branchHalfSpan,
                    anchor.Y + heading.Y * branchHalfSpan),
                new Vec2(anchor.X + inward.X * offset,
                    anchor.Y + inward.Y * offset),
                heading)).ToArray();
        }
    }
}
