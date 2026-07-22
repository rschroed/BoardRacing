using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace BoardRacing.Tests
{
    // The world-space racing surface (issue #86, round 1) replaces the IMGUI
    // track/pit drawing; these tests pin the generated geometry the way
    // WireframePresentationTests pins the HUD layout: the smoothed centerline
    // must honor the authored line, and the ribbon must reproduce the drawn
    // widths and section coloring the IMGUI pass had.
    public sealed class RaceSurfaceGeometryTests
    {
        private static TrackDefinition Track => TrackCatalog.Wedge();

        // Mirrors RacePrototype's pit complex constants (issue #88 geometry) and
        // the default rejoin distance pinned in TrackCatalogTests.
        private static PitLanePresentationLayout PitLayout()
        {
            var track = Track;
            return new PitLanePresentationLayout(track.Sample(0f).Position,
                new Vec2(680f, 455f), new Vec2(860f, 455f), new Vec2(1120f, 455f),
                new Vec2(1353f, 455f), new Vec2(1283f, 452f), track.Sample(850f).Position,
                TrackPresentation.SmoothHeading(track, 0f),
                TrackPresentation.SmoothHeading(track, 850f));
        }

        [Test]
        public void SmoothedCenterlineInterpolatesEveryAuthoredPoint()
        {
            var track = Track;
            List<CenterlineSample> samples =
                RaceSurfaceGeometry.SmoothCenterline(track, RaceSurfaceGeometry.SamplesPerChord);
            Assert.That(samples.Count,
                Is.EqualTo(track.Segments.Count * RaceSurfaceGeometry.SamplesPerChord));
            for (int chord = 0; chord < track.Segments.Count; chord++)
            {
                Vector2 sampled = samples[chord * RaceSurfaceGeometry.SamplesPerChord].Position;
                Vec2 authored = track.Segments[chord].Start;
                Assert.That(Vector2.Distance(sampled, new Vector2(authored.X, authored.Y)),
                    Is.LessThan(.01f), $"chord {chord} start not interpolated");
            }
        }

        [Test]
        public void SmoothedCenterlineStaysOnTheAuthoredLine()
        {
            // The curve may restore up to the arc sag the chords cut off (~1 px
            // at the sweeper) but must never wander from the racing line the
            // simulation drives — cars are drawn on simulation samples.
            var track = Track;
            var samples = RaceSurfaceGeometry.SmoothCenterline(track, RaceSurfaceGeometry.SamplesPerChord);
            foreach (var sample in samples)
                Assert.That(DistanceToPolyline(sample.Position, track), Is.LessThan(3f),
                    $"sample at {sample.Position} strays from the authored polyline");
        }

        [Test]
        public void SmoothedCenterlineTurnsGentlyEverywhere()
        {
            // The authored chords step ≤13°; the drawn ribbon exists to erase
            // that scalloping, so its own steps must stay a few degrees at most.
            var samples = RaceSurfaceGeometry.SmoothCenterline(Track, RaceSurfaceGeometry.SamplesPerChord);
            int count = samples.Count;
            for (int i = 0; i < count; i++)
            {
                Vector2 into = (samples[i].Position - samples[(i - 1 + count) % count].Position).normalized;
                Vector2 outOf = (samples[(i + 1) % count].Position - samples[i].Position).normalized;
                Assert.That(Vector2.Dot(into, outOf), Is.GreaterThan(Mathf.Cos(4f * Mathf.Deg2Rad)),
                    $"kink at smoothed sample {i}");
            }
        }

        [Test]
        public void ClosedRibbonHoldsTheDrawnTrackWidth()
        {
            var mesh = new SurfaceMeshData();
            var samples = RaceSurfaceGeometry.SmoothCenterline(Track, RaceSurfaceGeometry.SamplesPerChord);
            RaceSurfaceGeometry.AppendClosedRibbon(mesh, samples, RaceSurfaceGeometry.TrackWidth,
                RaceSurfaceGeometry.CornerColor, RaceSurfaceGeometry.StraightColor);
            // Quads are emitted [left, nextLeft, nextRight, right]: vertex 0 to
            // vertex 3 spans the ribbon at the leading ring.
            for (int quad = 0; quad < mesh.Vertices.Count / 4; quad++)
            {
                float span = Vector3.Distance(mesh.Vertices[quad * 4], mesh.Vertices[quad * 4 + 3]);
                Assert.That(span, Is.EqualTo(RaceSurfaceGeometry.TrackWidth).Within(1f),
                    $"ribbon width drifts at quad {quad}");
            }
        }

        [Test]
        public void RibbonColorsFollowTheAuthoredSectionKinds()
        {
            var track = Track;
            var mesh = new SurfaceMeshData();
            RaceSurfaceGeometry.AppendClosedRibbon(mesh,
                RaceSurfaceGeometry.SmoothCenterline(track, RaceSurfaceGeometry.SamplesPerChord),
                RaceSurfaceGeometry.TrackWidth, RaceSurfaceGeometry.CornerColor,
                RaceSurfaceGeometry.StraightColor);
            int cornerChords = track.Segments.Count(x => x.Kind == TrackSectionKind.Corner);
            int straightChords = track.Segments.Count - cornerChords;
            int cornerQuads = 0, straightQuads = 0;
            for (int i = 0; i < mesh.Colors.Count; i += 4)
            {
                if (mesh.Colors[i] == RaceSurfaceGeometry.CornerColor) cornerQuads++;
                else if (mesh.Colors[i] == RaceSurfaceGeometry.StraightColor) straightQuads++;
            }
            Assert.That(cornerQuads, Is.EqualTo(cornerChords * RaceSurfaceGeometry.SamplesPerChord));
            Assert.That(straightQuads, Is.EqualTo(straightChords * RaceSurfaceGeometry.SamplesPerChord));
        }

        [Test]
        public void StartFinishLineSitsOnSampleZero()
        {
            var track = Track;
            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(track, PitLayout(),
                Color.red, Color.blue);
            var whiteVertices = new List<Vector3>();
            for (int i = 0; i < mesh.Vertices.Count; i++)
                if (mesh.Colors[i] == Color.white) whiteVertices.Add(mesh.Vertices[i]);
            Assert.That(whiteVertices.Count, Is.EqualTo(4), "expected exactly the start/finish quad in white");
            Vector3 centroid = whiteVertices.Aggregate(Vector3.zero, (sum, v) => sum + v) / 4f;
            Vec2 line = track.Sample(0f).Position;
            Assert.That(Vector2.Distance(centroid, new Vector2(line.X, line.Y)), Is.LessThan(.01f));
        }

        [Test]
        public void SurfaceStaysInsideTheReferenceCanvas()
        {
            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(Track, PitLayout(),
                Color.red, Color.blue);
            Assert.That(mesh.Vertices.Count, Is.GreaterThan(0));
            Assert.That(mesh.Colors.Count, Is.EqualTo(mesh.Vertices.Count));
            Assert.That(mesh.Triangles.Count % 3, Is.Zero);
            foreach (Vector3 vertex in mesh.Vertices)
            {
                Assert.That(vertex.x, Is.InRange(0f, RaceLayout.ReferenceWidth));
                Assert.That(vertex.y, Is.InRange(0f, RaceLayout.ReferenceHeight));
                Assert.That(vertex.z, Is.Zero);
            }
        }

        private static float DistanceToPolyline(Vector2 point, TrackDefinition track)
        {
            float best = float.MaxValue;
            foreach (var segment in track.Segments)
            {
                var start = new Vector2(segment.Start.X, segment.Start.Y);
                var end = new Vector2(segment.End.X, segment.End.Y);
                Vector2 direction = end - start;
                float t = Mathf.Clamp01(Vector2.Dot(point - start, direction) / direction.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(point, start + direction * t));
            }
            return best;
        }
    }
}
