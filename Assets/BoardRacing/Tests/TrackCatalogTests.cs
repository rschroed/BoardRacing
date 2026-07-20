using System;
using System.Linq;
using BoardRacing.Domain;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    // The Wedge is authored geometry (issue #88): these tests pin the design
    // invariants the pit complex and presentation rely on, so a future course
    // edit that breaks one fails here instead of on the table.
    public sealed class TrackCatalogTests
    {
        [Test]
        public void WedgeClosesIntoAContinuousLoop()
        {
            var track = TrackCatalog.Wedge();
            var segments = track.Segments;
            Assert.That(Distance(segments[segments.Count - 1].End, segments[0].Start), Is.LessThan(.001f));
            Assert.That(segments.All(x => x.Length > 0f), Is.True);
            Assert.That(track.Length, Is.EqualTo(2627.8f).Within(2f));
        }

        [Test]
        public void WedgeHasNoAuthoredKinks()
        {
            // Corner polylines step ≤12°; straights meet their arcs tangentially.
            // Anything sharper is an authoring mistake (the placeholder octagon
            // turned 45° per vertex — the snap issue #89 exists to smooth away).
            var segments = TrackCatalog.Wedge().Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                var current = segments[i];
                var next = segments[(i + 1) % segments.Count];
                float dot = (Tangent(current).X * Tangent(next).X + Tangent(current).Y * Tangent(next).Y);
                Assert.That(dot, Is.GreaterThan((float)Math.Cos(13.0 * Math.PI / 180.0)),
                    $"kink between segments {i} and {(i + 1) % segments.Count}");
            }
        }

        [Test]
        public void WedgeStaysInsideTheSeatSafeEnvelope()
        {
            // The corner seat clusters own the screen corners; the course must
            // hold the central band (wireframe-ui.md).
            foreach (var segment in TrackCatalog.Wedge().Segments)
                foreach (var point in new[] { segment.Start, segment.End })
                {
                    Assert.That(point.X, Is.InRange(280f, 1650f));
                    Assert.That(point.Y, Is.InRange(385f, 725f));
                }
        }

        [Test]
        public void WedgeCarriesThreeDistinctCornerSpeeds()
        {
            var track = TrackCatalog.Wedge(200f);
            float[] cornerSpeeds = track.Segments.Where(x => x.Kind == TrackSectionKind.Corner)
                .Select(x => x.SafeSpeed).Distinct().OrderByDescending(x => x).ToArray();
            Assert.That(cornerSpeeds, Is.EqualTo(new[]
            {
                200f * TrackCatalog.WedgeSweeperSpeedFactor,
                200f * TrackCatalog.WedgeMediumSpeedFactor,
                200f * TrackCatalog.WedgeTightSpeedFactor,
            }));
            Assert.That(track.Segments.Where(x => x.Kind == TrackSectionKind.Straight)
                .All(x => float.IsPositiveInfinity(x.SafeSpeed)), Is.True);

            // Distinct corner arcs never touch: every arc is entered from a
            // straight — the boundary the simulation charges scrub and wear on
            // exactly once per corner.
            var segments = track.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                var current = segments[i];
                var next = segments[(i + 1) % segments.Count];
                if (current.Kind == TrackSectionKind.Corner && next.Kind == TrackSectionKind.Corner)
                    Assert.That(next.SafeSpeed, Is.EqualTo(current.SafeSpeed),
                        $"corner arcs of different speeds touch at segment {i}");
            }
        }

        [Test]
        public void WedgePitRejoinLandsOnTheStartFinishStraight()
        {
            // The pit exit resumes the car at pitExitRejoinDistance (850); the
            // lane presentation assumes that sample sits on the opening straight,
            // before the sweeper.
            var sample = TrackCatalog.Wedge().Sample(850f);
            Assert.That(sample.Kind, Is.EqualTo(TrackSectionKind.Straight));
            Assert.That(sample.SectionIndex, Is.Zero);
        }

        private static Vec2 Tangent(TrackSegment segment) => new Vec2(
            (segment.End.X - segment.Start.X) / segment.Length,
            (segment.End.Y - segment.Start.Y) / segment.Length);

        private static float Distance(Vec2 a, Vec2 b) =>
            (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
