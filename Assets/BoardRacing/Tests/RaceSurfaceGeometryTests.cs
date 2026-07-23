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
        private static TrackDefinition Track => CourseCatalog.Wedge().Track;

        // The same course-authored pit complex the prototype renders (issue #107
        // phase 1) — no more hand-mirrored constants.
        private static PitLanePresentationLayout PitLayout() =>
            PitLanePresentationLayout.ForCourse(CourseCatalog.Wedge());

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

        [Test]
        public void CarBodiesKeepTheDrawnFootprint()
        {
            // P1 is the rounded square, P2 the disc — both fill the same 54×54
            // footprint the IMGUI pass drew (issue #86 round 2). The cockpit
            // wedge (issue #117) lives strictly inside it, so the footprint
            // pin is an upper bound plus proof the body still reaches it.
            SurfaceMeshData square = RaceSurfaceGeometry.BuildCarBody(PlayerId.Player1, Color.red);
            float maxAxis = 0f, maxDiagonal = 0f;
            foreach (Vector3 vertex in square.Vertices)
            {
                maxAxis = Mathf.Max(maxAxis, Mathf.Max(Mathf.Abs(vertex.x), Mathf.Abs(vertex.y)));
                maxDiagonal = Mathf.Max(maxDiagonal, new Vector2(vertex.x, vertex.y).magnitude);
            }
            Assert.That(maxAxis, Is.EqualTo(RaceSurfaceGeometry.CarBodyHalfSize).Within(.01f));
            // The corner radius must actually cut the diagonal: strictly inside
            // the sharp corner, strictly outside the inscribed circle.
            float sharpCorner = RaceSurfaceGeometry.CarBodyHalfSize * Mathf.Sqrt(2f);
            Assert.That(maxDiagonal, Is.LessThan(sharpCorner - 1f));
            Assert.That(maxDiagonal, Is.GreaterThan(RaceSurfaceGeometry.CarBodyHalfSize + 1f));

            SurfaceMeshData disc = RaceSurfaceGeometry.BuildCarBody(PlayerId.Player2, Color.blue);
            float maxRadius = 0f;
            foreach (Vector3 vertex in disc.Vertices)
                maxRadius = Mathf.Max(maxRadius, new Vector2(vertex.x, vertex.y).magnitude);
            Assert.That(maxRadius, Is.EqualTo(RaceSurfaceGeometry.CarBodyHalfSize).Within(.01f),
                "the disc fills the footprint and nothing escapes it");
            // Both bodies carry the darker nose wedge: rotation must be
            // readable on each (the disc showed nothing without it).
            Assert.That(square.Colors, Has.Some.Not.EqualTo(Color.red));
            Assert.That(disc.Colors, Has.Some.Not.EqualTo(Color.blue));
            Assert.That(square.Colors, Has.Some.EqualTo(Color.red));
            Assert.That(disc.Colors, Has.Some.EqualTo(Color.blue));
        }

        // The Y-junction pins (issue #107 phase 2): the pit lane meets the track
        // as clamped shared-edge gores instead of a full ribbon hidden by paint
        // order — no lane geometry in the roadway, each mouth running along the
        // track edge, and the merge climbing at a slip-road angle (the ~40°
        // dive read as the lane vanishing under the track in three hardware
        // reviews).
        private const float LaneFloor =
            RaceSurfaceGeometry.TrackWidth * .5f - RaceSurfaceGeometry.JunctionEdgeOverlap;

        [Test]
        public void PitLaneNeverEntersTheRoadway()
        {
            var track = Track;
            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(track, PitLayout(),
                Color.red, Color.blue);
            bool sawPit = false;
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                if (mesh.Colors[i] != RaceSurfaceGeometry.PitLaneColor &&
                    mesh.Colors[i] != RaceSurfaceGeometry.PitStripeColor) continue;
                sawPit = true;
                // Half a pixel of slack: near a chord seam the clamp and this
                // re-measurement can disagree about the nearest chord.
                Assert.That(RaceSurfaceGeometry.InteriorOffset(mesh.Vertices[i], track),
                    Is.GreaterThanOrEqualTo(LaneFloor - .5f),
                    $"pit vertex at {mesh.Vertices[i]} crosses into the roadway");
            }
            Assert.That(sawPit, Is.True);
        }

        [Test]
        public void MergeMouthRunsAlongTheTrackEdge()
        {
            AssertMouthHugsTheEdge(x => x > CourseCatalog.Wedge().Pit.PlayerTwoBox.X,
                minimumExtent: 80f, "merge");
        }

        [Test]
        public void EntryMouthRunsAlongTheTrackEdge()
        {
            AssertMouthHugsTheEdge(x => x < CourseCatalog.Wedge().Pit.Entry.X,
                minimumExtent: 50f, "entry");
        }

        // A junction mouth is a run of clamped boundary vertices riding exactly
        // JunctionEdgeOverlap inside the track edge — the gore's shared seam.
        // It must have real length: a point contact would be the old blunt end.
        private static void AssertMouthHugsTheEdge(System.Func<float, bool> inRegion,
            float minimumExtent, string junction)
        {
            var track = Track;
            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(track, PitLayout(),
                Color.red, Color.blue);
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                if (mesh.Colors[i] != RaceSurfaceGeometry.PitLaneColor) continue;
                Vector2 vertex = mesh.Vertices[i];
                if (!inRegion(vertex.x)) continue;
                if (Mathf.Abs(RaceSurfaceGeometry.InteriorOffset(vertex, track) - LaneFloor) > .01f)
                    continue;
                min = Mathf.Min(min, vertex.x);
                max = Mathf.Max(max, vertex.x);
            }
            Assert.That(max - min, Is.GreaterThanOrEqualTo(minimumExtent),
                $"the {junction} mouth must run along the track edge, not touch it at a point");
        }

        [Test]
        public void MergeClimbsToTheTrackAtASlipRoadAngle()
        {
            // The visible leg of the merge — outside the fill (interior offset
            // beyond half the lane past the edge) but already climbing — must
            // cross at a shallow angle relative to the straight it joins.
            var track = Track;
            var layout = PitLayout();
            Vector2 straightDirection = (new Vector2(track.Segments[0].End.X, track.Segments[0].End.Y)
                - new Vector2(track.Segments[0].Start.X, track.Segments[0].Start.Y)).normalized;
            Vec2 priorPosition = PitLanePresentationMapper
                .ExitPose(PlayerId.Player2, 0f, false, layout).Position;
            for (float progress = .02f; progress <= 1.0001f; progress += .02f)
            {
                Vec2 position = PitLanePresentationMapper
                    .ExitPose(PlayerId.Player2, progress, false, layout).Position;
                var prior = new Vector2(priorPosition.X, priorPosition.Y);
                var current = new Vector2(position.X, position.Y);
                priorPosition = position;
                float offset = RaceSurfaceGeometry.InteriorOffset((prior + current) * .5f, track);
                if (offset < RaceSurfaceGeometry.TrackWidth * .5f -
                    RaceSurfaceGeometry.PitLaneWidth * .5f || offset > 60f) continue;
                Vector2 chord = current - prior;
                if (chord.sqrMagnitude < 1e-6f) continue;
                Assert.That(Mathf.Abs(Vector2.Dot(chord.normalized, straightDirection)),
                    Is.GreaterThan(Mathf.Cos(26f * Mathf.Deg2Rad)),
                    $"merge crosses too steeply near {current}");
            }
        }

        [Test]
        public void WedgeDoesNotCrossItself()
        {
            Assert.That(RaceSurfaceGeometry.FindCrossings(Track), Is.Empty);
        }

        [Test]
        public void HourglassCrossesItselfOnceWhereTheGeneratorPutIt()
        {
            // The figure-8's identity (issue #107 phase 4): exactly one
            // crossing, at the internal-tangent intersection the tangent-circle
            // construction places at (568, 550), with the carousel-exit
            // diagonal as the later (bridge) strand.
            IReadOnlyList<TrackCrossing> crossings =
                RaceSurfaceGeometry.FindCrossings(TrackCatalog.Hourglass());
            Assert.That(crossings.Count, Is.EqualTo(1));
            Assert.That(Vector2.Distance(crossings[0].Point, new Vector2(568.4f, 550f)),
                Is.LessThan(2f));
            Assert.That(crossings[0].LaterSegment, Is.GreaterThan(crossings[0].EarlierSegment));
        }

        [Test]
        public void HourglassCrossingDeckDressesTheBridge()
        {
            // Paint order alone builds the bridge (later quads draw on top);
            // the deck dressing must exist to sell it: shadow strips near the
            // crossing, and parapet lines appended after them.
            CourseDefinition course = CourseCatalog.Hourglass();
            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track,
                PitLanePresentationLayout.ForCourse(course), Color.red, Color.blue);
            Vector2 crossing = RaceSurfaceGeometry.FindCrossings(course.Track)[0].Point;
            int firstShadowVertex = -1;
            bool parapetAfterShadow = false;
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                if (mesh.Colors[i] == RaceSurfaceGeometry.CrossingShadowColor &&
                    firstShadowVertex < 0 &&
                    Vector2.Distance(mesh.Vertices[i], crossing) < 150f)
                    firstShadowVertex = i;
                if (firstShadowVertex >= 0 && i > firstShadowVertex &&
                    mesh.Colors[i] == RaceSurfaceGeometry.StripeColor &&
                    Vector2.Distance(mesh.Vertices[i], crossing) < 150f)
                    parapetAfterShadow = true;
            }
            Assert.That(firstShadowVertex, Is.GreaterThan(-1), "expected crossing shadows");
            Assert.That(parapetAfterShadow, Is.True, "expected parapet lines over the shadows");
        }

        [Test]
        public void InfinityCrossesItselfExactlyAtTableCenter()
        {
            // The symmetric figure-8's identity (issue #107 phase 4b): equal
            // lobes and internal tangents put the one crossing dead on the
            // table center, with the return diagonal as the bridge strand.
            IReadOnlyList<TrackCrossing> crossings =
                RaceSurfaceGeometry.FindCrossings(TrackCatalog.Infinity());
            Assert.That(crossings.Count, Is.EqualTo(1));
            Assert.That(Vector2.Distance(crossings[0].Point, new Vector2(960f, 540f)),
                Is.LessThan(1f));
            Assert.That(crossings[0].LaterSegment, Is.GreaterThan(crossings[0].EarlierSegment));
        }

        [Test]
        public void InfinityBoxesFlankTheCrossingSoTheLanePassesUnderTheBridge()
        {
            // The owner's sketch: pit boxes on both sides of the X, the service
            // row threading beneath the bridge. Lint keeps ANCHORS 150 px clear
            // of a crossing; the lane between them is free to pass under.
            CourseDefinition course = CourseCatalog.Infinity();
            Vector2 crossing = RaceSurfaceGeometry.FindCrossings(course.Track)[0].Point;
            Vector2 boxOne = new Vector2(course.Pit.PlayerOneBox.X, course.Pit.PlayerOneBox.Y);
            Vector2 boxTwo = new Vector2(course.Pit.PlayerTwoBox.X, course.Pit.PlayerTwoBox.Y);
            Vector2 row = (boxTwo - boxOne).normalized;
            float alongOne = Vector2.Dot(boxOne - crossing, row);
            float alongTwo = Vector2.Dot(boxTwo - crossing, row);
            Assert.That(alongOne * alongTwo, Is.LessThan(0f),
                "the boxes must sit on opposite sides of the crossing");
            Assert.That(Vector2.Distance(boxOne, crossing), Is.GreaterThanOrEqualTo(150f));
            Assert.That(Vector2.Distance(boxTwo, crossing), Is.GreaterThanOrEqualTo(150f));
        }

        [Test]
        public void FishhookDoesNotCrossItself()
        {
            Assert.That(RaceSurfaceGeometry.FindCrossings(TrackCatalog.Fishhook()), Is.Empty);
        }

        [Test]
        public void BoxQuadsAndStartLineFollowADiagonalPitStraight()
        {
            // Horizontal pit straights were a Wedge special case: on the
            // Infinity's diagonal, the box quads and the start line must
            // rotate with the travel direction instead of staying axis-aligned.
            CourseDefinition course = CourseCatalog.Infinity();
            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track,
                PitLanePresentationLayout.ForCourse(course), Color.red, Color.blue);
            Vector2 boxOne = new Vector2(course.Pit.PlayerOneBox.X, course.Pit.PlayerOneBox.Y);
            Vector2 boxTwo = new Vector2(course.Pit.PlayerTwoBox.X, course.Pit.PlayerTwoBox.Y);
            Vector2 lane = (boxTwo - boxOne).normalized;
            Vector2 across = new Vector2(-lane.y, lane.x);
            Vector2 boxCorner = boxOne + lane * 70f + across * 32f;
            Vec2 start = course.Track.Sample(0f).Position;
            TrackSegment first = course.Track.Segments[0];
            Vector2 travel = (new Vector2(first.End.X, first.End.Y) -
                new Vector2(first.Start.X, first.Start.Y)).normalized;
            Vector2 lineCorner = new Vector2(start.X, start.Y) +
                travel * 12f + new Vector2(-travel.y, travel.x) * 28f;
            bool boxCornerFound = false, lineCornerFound = false;
            foreach (Vector3 vertex in mesh.Vertices)
            {
                if (Vector2.Distance(vertex, boxCorner) < .5f) boxCornerFound = true;
                if (Vector2.Distance(vertex, lineCorner) < .5f) lineCornerFound = true;
            }
            Assert.That(boxCornerFound, Is.True, $"no box vertex at rotated corner {boxCorner}");
            Assert.That(lineCornerFound, Is.True, $"no start-line vertex at rotated corner {lineCorner}");
        }

        [Test]
        public void PitLaneRendersUnderTheTrackFill()
        {
            // The clamped mouths tuck JunctionEdgeOverlap inside the edge; that
            // sliver must draw before the fill so the fill covers it and the
            // visible seam is exactly the track edge.
            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(Track, PitLayout(),
                Color.red, Color.blue);
            int firstTrackVertex = -1;
            int lastPitVertex = -1;
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                bool trackFill = mesh.Colors[i] == RaceSurfaceGeometry.CornerColor ||
                    mesh.Colors[i] == RaceSurfaceGeometry.StraightColor;
                if (trackFill && firstTrackVertex < 0) firstTrackVertex = i;
                if (mesh.Colors[i] == RaceSurfaceGeometry.PitLaneColor ||
                    mesh.Colors[i] == RaceSurfaceGeometry.PitStripeColor)
                    lastPitVertex = i;
            }
            Assert.That(lastPitVertex, Is.GreaterThan(-1));
            Assert.That(firstTrackVertex, Is.GreaterThan(-1));
            Assert.That(lastPitVertex, Is.LessThan(firstTrackVertex),
                "the pit lane must render before (under) the track fill");
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
