using System;

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
        public PitComplexDefinition(Vec2 entry, Vec2 playerOneBox, Vec2 playerTwoBox,
            Vec2 exit, Vec2 mergeApproach, float exitRejoinDistance)
        {
            if (float.IsNaN(exitRejoinDistance) || float.IsInfinity(exitRejoinDistance) ||
                exitRejoinDistance <= 0f)
                throw new ArgumentException("The pit exit must rejoin at a positive track distance.",
                    nameof(exitRejoinDistance));
            Entry = entry;
            PlayerOneBox = playerOneBox;
            PlayerTwoBox = playerTwoBox;
            Exit = exit;
            MergeApproach = mergeApproach;
            ExitRejoinDistance = exitRejoinDistance;
        }

        public Vec2 Entry { get; }
        public Vec2 PlayerOneBox { get; }
        public Vec2 PlayerTwoBox { get; }
        public Vec2 Exit { get; }
        public Vec2 MergeApproach { get; }
        // The lane blends onto the track just before the rejoin sample — no
        // return trip: the simulation resumes the car where the pit lane
        // physically ends.
        public float ExitRejoinDistance { get; }

        public Vec2 Box(PlayerId playerId) =>
            playerId == PlayerId.Player1 ? PlayerOneBox : PlayerTwoBox;
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
            float cornerSafeSpeed = 190f)
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
        // The merge approach sits 50 px past player two's box and 27 px above the
        // lane center: it stretches the exit spline's climb to the rejoin across
        // ~230 px so the visible crossing stays a shallow slip-road angle (issue
        // #107 phase 2 — aiming through (1283, 452) packed a 58 px climb into
        // 70 px of run, a ~40° dive that read as the lane vanishing under the
        // track in three hardware reviews).
        // 6 laps × the Wedge's 2628 perimeter ≈ the placeholder's 5 × 3508 race
        // distance, keeping race duration roughly where the owner tuned it, with
        // the tight hairpin adding scrub time per lap (issue #88).
        public static CourseDefinition Wedge(float cornerSafeSpeed = 190f) => new CourseDefinition(
            "Wedge",
            TrackCatalog.Wedge(cornerSafeSpeed),
            new PitComplexDefinition(new Vec2(680f, 455f), new Vec2(860f, 455f),
                new Vec2(1120f, 455f), new Vec2(1353f, 455f), new Vec2(1240f, 428f), 850f),
            laps: 6);

        // Hourglass pit complex hangs off the 720 px top straight of the right
        // lobe (the crossing lives far away at (568, 550)): entry at 165 of the
        // straight, boxes at 300/485, rejoin at 690 — 30 before the sweeper.
        // The merge approach follows the Wedge's phase-2 tuning: 50 px past
        // player two's box QUAD (the 140 px quad ends at 1335), 27 px above the
        // lane center, for a shallow ~14° climb that starts visibly clear of
        // the box.
        // 5 laps × the ~2949 perimeter ≈ the Wedge's 6 × 2628 race distance.
        public static CourseDefinition Hourglass(float cornerSafeSpeed = 190f) => new CourseDefinition(
            "Hourglass",
            TrackCatalog.Hourglass(cornerSafeSpeed),
            new PitComplexDefinition(new Vec2(945f, 462f), new Vec2(1080f, 462f),
                new Vec2(1265f, 462f), new Vec2(1450f, 462f), new Vec2(1385f, 435f), 690f),
            laps: 5);

        // Infinity pit complex hangs off the ascending diagonal (886 px), 52 px
        // on the interior side, with the crossing at the diagonal's midpoint
        // (443): entry at 165, boxes at 265 and 610 — FLANKING the crossing at
        // 186/175 px clear — so the service row passes under the bridge between
        // them, as the owner sketched. Merge approach 50 px past the second
        // box's quad edge at 25 px offset (the Wedge phase-2 rule), rejoin at
        // 815 — 71 before the east lobe. 5 laps × ~3224 ≈ the Wedge's race
        // distance.
        public static CourseDefinition Infinity(float cornerSafeSpeed = 190f) => new CourseDefinition(
            "Infinity",
            TrackCatalog.Infinity(cornerSafeSpeed),
            new PitComplexDefinition(new Vec2(724f, 695f), new Vec2(816f, 657f),
                new Vec2(1134f, 524f), new Vec2(1305f, 452f), new Vec2(1235f, 452f), 815f),
            laps: 5);

        // Fishhook pit complex on the long climbing diagonal (895 px): entry
        // 165, boxes 345/545, merge approach 665 (50 past the box quad edge,
        // 25 px offset), rejoin 755 — 140 before the hook. 4 laps × ~4072 ≈
        // the Wedge's race distance.
        public static CourseDefinition Fishhook(float cornerSafeSpeed = 190f) => new CourseDefinition(
            "Fishhook",
            TrackCatalog.Fishhook(cornerSafeSpeed),
            new PitComplexDefinition(new Vec2(628f, 800f), new Vec2(807f, 781f),
                new Vec2(1006f, 761f), new Vec2(1195f, 742f), new Vec2(1122f, 722f), 755f),
            laps: 4);
    }
}
