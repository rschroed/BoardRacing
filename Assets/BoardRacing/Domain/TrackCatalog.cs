using System.Collections.Generic;

namespace BoardRacing.Domain
{
    /// <summary>
    /// The designed course library (issue #88). Courses are authored directly in
    /// the 1920×1080 presentation space — one track unit is one screen pixel, so
    /// simulation distance/speed and the drawn racing line agree exactly. More
    /// courses will join over time (owner direction, 2026-07-19); each is a named
    /// factory so course selection can layer on later without data migration.
    /// </summary>
    public static class TrackCatalog
    {
        // Wedge — the owner-selected asymmetric triangle (issue #88, option C):
        // three corner characters per lap instead of the placeholder's uniform
        // kinks. Corner arcs and connecting straights are mutually tangent
        // (generated from three circles and their external tangent lines), so
        // the racing line has no authored kinks — polyline steps are ≤12°.
        //   · fast sweeper, right (R150, ~179°): the table band is wide and
        //     shallow, so the right corner is inherently a turnaround; at this
        //     radius it reads as the fast sweeping feature.
        //   · medium corner, bottom-left (R84, ~62°)
        //   · tight hairpin, top-left (R78, ~119°): the braking moment, feeding
        //     the start/finish straight.
        // Start/finish opens the ~911 px top straight; the pit complex hangs off
        // it (rejoin distance re-derived in TrancheThreeSettings). Perimeter
        // ≈ 2628 px.
        public const float WedgeSweeperSpeedFactor = 1.35f;
        public const float WedgeMediumSpeedFactor = 1f;
        public const float WedgeTightSpeedFactor = .68f;

        public static TrackDefinition Wedge(float cornerSafeSpeed = Pace.CornerSafeSpeed)
        {
            var segments = new List<TrackSegment>(WedgePoints.Length - 1);
            int point = 0;
            foreach (var (count, factor) in WedgeRuns)
                for (int i = 0; i < count; i++, point++)
                    segments.Add(factor <= 0f
                        ? new TrackSegment(WedgePoints[point], WedgePoints[point + 1],
                            TrackSectionKind.Straight, float.PositiveInfinity)
                        : new TrackSegment(WedgePoints[point], WedgePoints[point + 1],
                            TrackSectionKind.Corner, cornerSafeSpeed * factor));
            return new TrackDefinition(segments);
        }

        // Segment runs along WedgePoints: (chord count, corner speed factor);
        // factor 0 marks a straight.
        private static readonly (int Count, float Factor)[] WedgeRuns =
        {
            (1, 0f), (15, WedgeSweeperSpeedFactor),
            (1, 0f), (6, WedgeMediumSpeedFactor),
            (1, 0f), (10, WedgeTightSpeedFactor),
        };

        // Closed polyline (last point repeats the first); Sample(0) is the
        // start/finish line at the west end of the top straight, travel is
        // clockwise (east along the top).
        private static readonly Vec2[] WedgePoints =
        {
            new Vec2(503.1f, 414.0f), new Vec2(1414.4f, 392.0f), new Vec2(1445.5f, 394.6f), new Vec2(1475.5f, 403.5f),
            new Vec2(1503.0f, 418.4f), new Vec2(1526.7f, 438.7f), new Vec2(1545.8f, 463.5f), new Vec2(1559.3f, 491.7f),
            new Vec2(1566.7f, 522.0f), new Vec2(1567.6f, 553.3f), new Vec2(1562.0f, 584.0f), new Vec2(1550.1f, 613.0f),
            new Vec2(1532.6f, 638.8f), new Vec2(1510.0f, 660.5f), new Vec2(1483.5f, 677.0f), new Vec2(1454.1f, 687.6f),
            new Vec2(1423.1f, 691.9f), new Vec2(597.9f, 720.0f), new Vec2(582.8f, 719.1f), new Vec2(568.1f, 715.6f),
            new Vec2(554.2f, 709.4f), new Vec2(541.7f, 700.9f), new Vec2(530.9f, 690.3f), new Vec2(522.2f, 678.0f),
            new Vec2(437.4f, 531.0f), new Vec2(430.9f, 516.3f), new Vec2(427.5f, 500.5f), new Vec2(427.4f, 484.4f),
            new Vec2(430.6f, 468.6f), new Vec2(437.0f, 453.8f), new Vec2(446.3f, 440.6f), new Vec2(458.1f, 429.7f),
            new Vec2(471.9f, 421.4f), new Vec2(487.1f, 416.1f), new Vec2(503.1f, 414.0f),
        };

        // Hourglass — the figure-8 (issue #107 phase 4, owner-selected option A):
        // the racing line crosses itself once, splitting the lap into two
        // counter-wound lobes. Generated from tangent circles like the Wedge:
        // the right lobe is a stadium (top pit straight y=410, east sweeper
        // R130, sloped bottom straight, west shoulder R140), the left lobe a
        // R85 carousel wrapping ~263°, and the two crossover straights are the
        // internal tangents between the counter-wound shoulder and carousel —
        // so every joint is kink-free and the crossing lands at (568, 550) at
        // 82.9°, well past the lint's 35° readability floor. Perimeter ≈ 2949.
        //   · east sweeper, right (R130, 180°): the flat-out feature.
        //   · west shoulder (R140, two 41° pieces flanking the crossover).
        //   · carousel, left (R85, ~263°): one long grinding commitment corner —
        //     a corner character no Wedge lap has.
        public const float HourglassSweeperSpeedFactor = 1.3f;
        public const float HourglassShoulderSpeedFactor = 1.2f;
        public const float HourglassCarouselSpeedFactor = .8f;

        public static TrackDefinition Hourglass(float cornerSafeSpeed = Pace.CornerSafeSpeed)
        {
            var segments = new List<TrackSegment>(HourglassPoints.Length - 1);
            int point = 0;
            foreach (var (count, factor) in HourglassRuns)
                for (int i = 0; i < count; i++, point++)
                    segments.Add(factor <= 0f
                        ? new TrackSegment(HourglassPoints[point], HourglassPoints[point + 1],
                            TrackSectionKind.Straight, float.PositiveInfinity)
                        : new TrackSegment(HourglassPoints[point], HourglassPoints[point + 1],
                            TrackSectionKind.Corner, cornerSafeSpeed * factor));
            return new TrackDefinition(segments);
        }

        private static readonly (int Count, float Factor)[] HourglassRuns =
        {
            (1, 0f), (14, HourglassSweeperSpeedFactor),
            (1, 0f), (4, HourglassShoulderSpeedFactor),
            (1, 0f), (21, HourglassCarouselSpeedFactor),
            (1, 0f), (4, HourglassShoulderSpeedFactor),
        };

        // Closed polyline; Sample(0) is the start/finish line at the west end of
        // the top straight. Travel is clockwise around the right lobe and
        // counterclockwise around the left carousel; the two long diagonals are
        // the crossover, and the later one (leaving the carousel) draws on top.
        private static readonly Vec2[] HourglassPoints =
        {
            new Vec2(780.0f, 410.0f), new Vec2(1500.0f, 410.0f), new Vec2(1528.8f, 413.2f), new Vec2(1556.2f, 422.8f),
            new Vec2(1580.8f, 438.1f), new Vec2(1601.3f, 458.5f), new Vec2(1616.8f, 483.0f), new Vec2(1626.6f, 510.3f),
            new Vec2(1630.0f, 539.1f), new Vec2(1627.0f, 567.9f), new Vec2(1617.6f, 595.4f), new Vec2(1602.4f, 620.0f),
            new Vec2(1582.2f, 640.7f), new Vec2(1557.8f, 656.4f), new Vec2(1530.6f, 666.4f), new Vec2(1501.8f, 670.0f),
            new Vec2(781.9f, 690.0f), new Vec2(756.3f, 688.0f), new Vec2(731.4f, 681.3f), new Vec2(708.2f, 670.2f),
            new Vec2(687.4f, 655.0f), new Vec2(496.2f, 486.3f), new Vec2(481.1f, 475.6f), new Vec2(464.0f, 468.5f),
            new Vec2(445.8f, 465.2f), new Vec2(427.2f, 466.0f), new Vec2(409.3f, 470.7f), new Vec2(392.9f, 479.3f),
            new Vec2(378.7f, 491.2f), new Vec2(367.4f, 505.8f), new Vec2(359.5f, 522.6f), new Vec2(355.5f, 540.7f),
            new Vec2(355.5f, 559.3f), new Vec2(359.5f, 577.4f), new Vec2(367.4f, 594.2f), new Vec2(378.7f, 608.8f),
            new Vec2(392.9f, 620.7f), new Vec2(409.3f, 629.3f), new Vec2(427.2f, 634.0f), new Vec2(445.8f, 634.8f),
            new Vec2(464.0f, 631.5f), new Vec2(481.1f, 624.4f), new Vec2(496.2f, 613.7f), new Vec2(687.4f, 445.0f),
            new Vec2(707.7f, 430.1f), new Vec2(730.5f, 419.1f), new Vec2(754.8f, 412.3f), new Vec2(780.0f, 410.0f),
        };

        // Infinity — the symmetric figure-8 (issue #107 phase 4b, from the
        // owner's sketch): two equal R185 lobes at (480, 540) and (1440, 540),
        // joined by their internal tangents, so the crossing lands exactly at
        // table center (960, 540) at 45.3° and the whole course is its own
        // 180°-rotation. (R195 grazed the seats' tilted TIRES label homes by
        // 6 px on both diagonally-mirrored corners; R185 clears them.) The
        // ascending diagonal is the pit straight — the service boxes flank the
        // crossing (186/175 px clear of it, above the lint's 150 floor), so
        // the pit lane itself passes UNDER the bridge between them, exactly as
        // sketched. Each lobe wraps 225.3°; at R185 the lobes are the
        // catalog's widest arcs and carry the Wedge sweeper's 1.35 factor —
        // the Infinity is the flat-out course, its challenge is rhythm and pit
        // timing, not braking. Perimeter ≈ 3224.
        public const float InfinityLobeSpeedFactor = 1.35f;

        public static TrackDefinition Infinity(float cornerSafeSpeed = Pace.CornerSafeSpeed)
        {
            var segments = new List<TrackSegment>(InfinityPoints.Length - 1);
            int point = 0;
            foreach (var (count, factor) in InfinityRuns)
                for (int i = 0; i < count; i++, point++)
                    segments.Add(factor <= 0f
                        ? new TrackSegment(InfinityPoints[point], InfinityPoints[point + 1],
                            TrackSectionKind.Straight, float.PositiveInfinity)
                        : new TrackSegment(InfinityPoints[point], InfinityPoints[point + 1],
                            TrackSectionKind.Corner, cornerSafeSpeed * factor));
            return new TrackDefinition(segments);
        }

        private static readonly (int Count, float Factor)[] InfinityRuns =
        {
            (1, 0f), (19, InfinityLobeSpeedFactor),
            (1, 0f), (19, InfinityLobeSpeedFactor),
        };

        // Closed polyline; Sample(0) is the start/finish line where the west
        // lobe hands the ascending pit diagonal off toward the east lobe. The
        // return diagonal is driven later in the lap, so it draws on top at
        // the center crossing — the bridge.
        private static readonly Vec2[] InfinityPoints =
        {
            new Vec2(551.3f, 710.7f), new Vec2(1368.7f, 369.3f), new Vec2(1405.3f, 358.3f), new Vec2(1443.4f, 355.0f),
            new Vec2(1481.3f, 359.7f), new Vec2(1517.5f, 372.0f), new Vec2(1550.4f, 391.5f), new Vec2(1578.5f, 417.4f),
            new Vec2(1600.8f, 448.5f), new Vec2(1616.2f, 483.5f), new Vec2(1624.0f, 520.9f), new Vec2(1624.0f, 559.1f),
            new Vec2(1616.2f, 596.5f), new Vec2(1600.8f, 631.5f), new Vec2(1578.5f, 662.6f), new Vec2(1550.4f, 688.5f),
            new Vec2(1517.5f, 708.0f), new Vec2(1481.3f, 720.3f), new Vec2(1443.4f, 725.0f), new Vec2(1405.3f, 721.7f),
            new Vec2(1368.7f, 710.7f), new Vec2(551.3f, 369.3f), new Vec2(514.7f, 358.3f), new Vec2(476.6f, 355.0f),
            new Vec2(438.7f, 359.7f), new Vec2(402.5f, 372.0f), new Vec2(369.6f, 391.5f), new Vec2(341.5f, 417.4f),
            new Vec2(319.2f, 448.5f), new Vec2(303.8f, 483.5f), new Vec2(296.0f, 520.9f), new Vec2(296.0f, 559.1f),
            new Vec2(303.8f, 596.5f), new Vec2(319.2f, 631.5f), new Vec2(341.5f, 662.6f), new Vec2(369.6f, 688.5f),
            new Vec2(402.5f, 708.0f), new Vec2(438.7f, 720.3f), new Vec2(476.6f, 725.0f), new Vec2(514.7f, 721.7f),
            new Vec2(551.3f, 710.7f),
        };

        // Fishhook — the hook-and-paperclip (issue #107 phase 4b, from the
        // owner's sketch): a long diagonal pit straight climbs into a big open
        // R185 hook (173.5°, the catalog's fastest sustained arc), the top arm
        // runs back west, and the left half is a paperclip of three nested
        // ~180° hairpins — R105 bulge down, R72 clip back (the catalog's
        // tightest corner), R80 clip out onto the pit straight. Four distinct
        // rhythm zones per lap: flat-out diagonal, one fast committed arc, two
        // long transit arms, then rapid-fire switchbacks. Generated from four
        // tangent circles ((450,685) R80, (1330,490) R185, (470,400) R105,
        // (930,555) R72 counter-wound); no crossing. Perimeter ≈ 4072 — the
        // catalog's longest lap.
        public const float FishhookHookSpeedFactor = 1.35f;
        public const float FishhookBulgeSpeedFactor = 1.05f;
        public const float FishhookClipTightSpeedFactor = .7f;
        public const float FishhookClipWideSpeedFactor = .8f;

        public static TrackDefinition Fishhook(float cornerSafeSpeed = Pace.CornerSafeSpeed)
        {
            var segments = new List<TrackSegment>(FishhookPoints.Length - 1);
            int point = 0;
            foreach (var (count, factor) in FishhookRuns)
                for (int i = 0; i < count; i++, point++)
                    segments.Add(factor <= 0f
                        ? new TrackSegment(FishhookPoints[point], FishhookPoints[point + 1],
                            TrackSectionKind.Straight, float.PositiveInfinity)
                        : new TrackSegment(FishhookPoints[point], FishhookPoints[point + 1],
                            TrackSectionKind.Corner, cornerSafeSpeed * factor));
            return new TrackDefinition(segments);
        }

        private static readonly (int Count, float Factor)[] FishhookRuns =
        {
            (1, 0f), (14, FishhookHookSpeedFactor),
            (1, 0f), (15, FishhookBulgeSpeedFactor),
            (1, 0f), (15, FishhookClipTightSpeedFactor),
            (1, 0f), (16, FishhookClipWideSpeedFactor),
        };

        // Closed polyline; Sample(0) is the start/finish line at the southwest
        // end of the pit diagonal, travel climbs northeast into the hook.
        private static readonly Vec2[] FishhookPoints =
        {
            new Vec2(458.1f, 764.6f), new Vec2(1348.7f, 674.1f), new Vec2(1387.8f, 665.7f), new Vec2(1424.2f, 649.2f),
            new Vec2(1456.1f, 625.3f), new Vec2(1482.3f, 595.1f), new Vec2(1501.3f, 560.0f), new Vec2(1512.3f, 521.6f),
            new Vec2(1514.8f, 481.7f), new Vec2(1508.7f, 442.2f), new Vec2(1494.3f, 405.0f), new Vec2(1472.2f, 371.7f),
            new Vec2(1443.5f, 343.9f), new Vec2(1409.5f, 323.0f), new Vec2(1371.8f, 309.8f), new Vec2(1332.1f, 305.0f),
            new Vec2(471.2f, 295.0f), new Vec2(449.0f, 297.1f), new Vec2(427.6f, 303.9f), new Vec2(408.3f, 315.1f),
            new Vec2(391.7f, 330.1f), new Vec2(378.6f, 348.3f), new Vec2(369.7f, 368.8f), new Vec2(365.4f, 390.7f),
            new Vec2(365.8f, 413.1f), new Vec2(371.0f, 434.9f), new Vec2(380.6f, 455.1f), new Vec2(394.3f, 472.7f),
            new Vec2(411.4f, 487.1f), new Vec2(431.2f, 497.6f), new Vec2(452.7f, 503.6f), new Vec2(475.1f, 504.9f),
            new Vec2(926.5f, 483.1f), new Vec2(942.0f, 484.0f), new Vec2(956.9f, 488.2f), new Vec2(970.6f, 495.5f),
            new Vec2(982.4f, 505.6f), new Vec2(991.7f, 518.0f), new Vec2(998.2f, 532.0f), new Vec2(1001.6f, 547.2f),
            new Vec2(1001.6f, 562.7f), new Vec2(998.3f, 577.8f), new Vec2(991.8f, 591.9f), new Vec2(982.5f, 604.3f),
            new Vec2(970.7f, 614.4f), new Vec2(957.1f, 621.7f), new Vec2(942.2f, 626.0f), new Vec2(926.7f, 626.9f),
            new Vec2(453.7f, 605.1f), new Vec2(437.3f, 606.0f), new Vec2(421.4f, 610.3f), new Vec2(406.8f, 617.7f),
            new Vec2(394.0f, 627.9f), new Vec2(383.5f, 640.6f), new Vec2(375.8f, 655.1f), new Vec2(371.3f, 670.8f),
            new Vec2(370.0f, 687.2f), new Vec2(372.2f, 703.5f), new Vec2(377.6f, 719.0f), new Vec2(386.0f, 733.0f),
            new Vec2(397.2f, 745.1f), new Vec2(410.6f, 754.6f), new Vec2(425.6f, 761.2f), new Vec2(441.7f, 764.6f),
            new Vec2(458.1f, 764.6f),
        };
    }
}
