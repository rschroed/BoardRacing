using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardRacing.Domain
{
    /// <summary>
    /// The pit complex as authored course geometry (issue #107 phase 1): entry
    /// ramp point, one service box per player, exit, the merge approach the
    /// exit spline aims through, and the track distance where the lane
    /// physically rejoins. Previously scattered across RacePrototype constants
    /// and TrancheThreeSettings.
    /// </summary>
    public readonly struct PitComplexDefinition
    {
        public PitComplexDefinition(Vec2 entry, IEnumerable<Vec2> boxes,
            Vec2 exit, Vec2 mergeApproach, float exitRejoinDistance)
        {
            if (float.IsNaN(exitRejoinDistance) || float.IsInfinity(exitRejoinDistance) ||
                exitRejoinDistance <= 0f)
                throw new ArgumentException("The pit exit must rejoin at a positive track distance.",
                    nameof(exitRejoinDistance));
            Vec2[] authoredBoxes = boxes?.ToArray() ??
                throw new ArgumentNullException(nameof(boxes));
            if (authoredBoxes.Length < 2 || authoredBoxes.Length > 4)
                throw new ArgumentException("A pit complex requires two to four ordered boxes.",
                    nameof(boxes));
            Entry = entry;
            Boxes = Array.AsReadOnly(authoredBoxes);
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
            int laps)
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
        }

        public string Name { get; }
        public TrackDefinition Track { get; }
        public PitComplexDefinition Pit { get; }
        public int Laps { get; }
    }

    /// <summary>
    /// The designed course library (issues #88, #107). Racing lines stay in
    /// TrackCatalog; a course wraps one with its pit complex and race length.
    /// </summary>
    public static class CourseCatalog
    {
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
            new PitComplexDefinition(new Vec2(680f, 455f), new[]
                {
                    new Vec2(778f, 455f), new Vec2(892f, 455f),
                    new Vec2(1006f, 455f), new Vec2(1120f, 455f)
                },
                new Vec2(1353f, 455f), new Vec2(1240f, 428f), 850f),
            laps: 6);

        // Hourglass pit complex hangs off the 720 px top straight of the right
        // lobe (the crossing lives far away at (568, 550)). The approved
        // merge-safe four-box row uses entry 150, boxes 153/267/381/495, and
        // rejoin 695 — 25 before the sweeper. The five-pixel rejoin move and
        // westward entry preserve the 200 px merge run without shrinking the
        // 20 px gaps between 94 px boxes.
        // The merge approach follows the Wedge's phase-2 tuning: 50 px past
        // the last compact box quad and 27 px above the lane center, for a
        // shallow climb that starts visibly clear of the box.
        // 5 laps × the ~2949 perimeter ≈ the Wedge's 6 × 2628 race distance.
        public static CourseDefinition Hourglass(float cornerSafeSpeed = Pace.CornerSafeSpeed) => new CourseDefinition(
            "Hourglass",
            TrackCatalog.Hourglass(cornerSafeSpeed),
            new PitComplexDefinition(new Vec2(930f, 462f), new[]
                {
                    new Vec2(933f, 462f), new Vec2(1047f, 462f),
                    new Vec2(1161f, 462f), new Vec2(1275f, 462f)
                },
                new Vec2(1450f, 462f), new Vec2(1372f, 435f), 695f),
            laps: 5);

        // Infinity's approved four-box row hangs off the ascending diagonal
        // (886 px), 60 px inside the loop. Its 114 px spacing threads the row
        // beneath the crossing without letting the compact painted quads touch
        // the bridge ribbon; the 70 px flat merge remains intact. 5 laps ×
        // ~3224 ≈ the Wedge's race distance.
        public static CourseDefinition Infinity(float cornerSafeSpeed = Pace.CornerSafeSpeed) => new CourseDefinition(
            "Infinity",
            TrackCatalog.Infinity(cornerSafeSpeed),
            new PitComplexDefinition(new Vec2(724f, 695f), new[]
                {
                    new Vec2(821.6974f, 662.6456f), new Vec2(926.8694f, 618.6586f),
                    new Vec2(1032.0414f, 574.6716f), new Vec2(1137.2133f, 530.6845f)
                },
                new Vec2(1305f, 452f), new Vec2(1235f, 452f), 815f),
            laps: 5);

        // Fishhook's four-box row follows the long climbing diagonal (895 px)
        // with compact boxes at 114 px centers. The last box retains the old
        // final box anchor and existing 331 px merge run. 4 laps × ~4072 ≈
        // the Wedge's race distance.
        public static CourseDefinition Fishhook(float cornerSafeSpeed = Pace.CornerSafeSpeed) => new CourseDefinition(
            "Fishhook",
            TrackCatalog.Fishhook(cornerSafeSpeed),
            new PitComplexDefinition(new Vec2(628f, 800f), new[]
                {
                    new Vec2(665.7143f, 795.1996f), new Vec2(779.1428f, 783.7997f),
                    new Vec2(892.5714f, 772.3999f), new Vec2(1006f, 761f)
                },
                new Vec2(1195f, 742f), new Vec2(1122f, 722f), 755f),
            laps: 4);
    }
}
