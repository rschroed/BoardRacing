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

        public static TrackDefinition Wedge(float cornerSafeSpeed = 190f)
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
    }
}
