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
        public void CommittedSurfaceStyleReproducesTheLegacyBuildExactly()
        {
            SurfaceMeshData legacy = RaceSurfaceGeometry.Build(
                Track, PitLayout(), Color.red, Color.blue);
            SurfaceMeshData styled = RaceSurfaceGeometry.Build(
                Track, PitLayout(), new[] { Color.red, Color.blue },
                RaceSurfaceStyle.Default);

            Assert.That(styled.Vertices, Is.EqualTo(legacy.Vertices));
            Assert.That(styled.Colors, Is.EqualTo(legacy.Colors));
            Assert.That(styled.Triangles, Is.EqualTo(legacy.Triangles));
        }

        [Test]
        public void StyleChangesRoadColorsAndCanHideStripesAndPitSurface()
        {
            RaceSurfaceStyle style = RaceSurfaceStyle.Default;
            style.StraightRoadColor = new Color(.11f, .22f, .33f, 1f);
            style.CornerRoadColor = new Color(.44f, .55f, .66f, 1f);
            style.StripeVisible = false;
            style.PitSurfaceVisible = false;

            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(
                Track, PitLayout(), new[] { Color.red, Color.blue }, style);

            Assert.That(mesh.Colors, Has.Some.EqualTo(style.StraightRoadColor));
            Assert.That(mesh.Colors, Has.Some.EqualTo(style.CornerRoadColor));
            Assert.That(mesh.Colors, Has.None.EqualTo(RaceSurfaceGeometry.StripeColor));
            Assert.That(mesh.Colors, Has.None.EqualTo(RaceSurfaceGeometry.PitStripeColor));
            Assert.That(mesh.Colors, Has.None.EqualTo(RaceSurfaceGeometry.PitLaneColor));
        }

        [Test]
        public void ShoulderUsesOpaquePrecomposedCoverageOnEveryCatalogCourse()
        {
            RaceSurfaceStyle style = RaceSurfaceStyle.Default;
            style.ShoulderOpacity = .8f;
            style.ShoulderSolidWidth = 12f;
            style.ShoulderFeatherWidth = 16f;
            style.DebugView = RaceSurfaceDebugView.ShoulderOnly;

            foreach (CourseDefinition course in CourseCatalog.All())
            {
                SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track,
                    PitLanePresentationLayout.ForCourse(course), new Color[0], style);
                Assert.That(mesh.Vertices.Count, Is.GreaterThan(0), course.Name);
                Assert.That(mesh.Colors.All(color => Mathf.Approximately(color.a, 1f)),
                    Is.True, course.Name + " must never compound translucent shoulder coverage");
                Assert.That(mesh.Colors, Has.Some.EqualTo(
                    new Color(style.GroundColor.r, style.GroundColor.g, style.GroundColor.b, 1f)),
                    course.Name + " shoulder outer edge must reach the ground");
                Assert.That(mesh.Vertices.All(vertex =>
                        float.IsFinite(vertex.x) && float.IsFinite(vertex.y)),
                    Is.True, course.Name + " shoulder join must remain finite");
            }
        }

        [Test]
        public void RoadBoundaryDebugViewDoesNotMoveTheAuthoredRoadRibbon()
        {
            RaceSurfaceStyle composedStyle = RaceSurfaceStyle.Default;
            RaceSurfaceStyle boundaryStyle = RaceSurfaceStyle.Default;
            boundaryStyle.DebugView = RaceSurfaceDebugView.RoadBoundary;
            SurfaceMeshData composed = RaceSurfaceGeometry.Build(
                Track, PitLayout(), new Color[0], composedStyle);
            SurfaceMeshData boundary = RaceSurfaceGeometry.Build(
                Track, PitLayout(), new Color[0], boundaryStyle);

            Vector3[] composedRoad = VerticesWithColor(composed, composedStyle.StraightRoadColor)
                .Concat(VerticesWithColor(composed, composedStyle.CornerRoadColor)).ToArray();
            Vector3[] boundaryRoad = VerticesWithColor(boundary, boundaryStyle.StraightRoadColor)
                .Concat(VerticesWithColor(boundary, boundaryStyle.CornerRoadColor)).ToArray();
            Assert.That(boundaryRoad, Is.EqualTo(composedRoad),
                "the debug outline may surround but must never alter the 64 px road");
        }

        [Test]
        public void DirectionEAssetsKeepTheDrawnFootprintAndPieceIdentity()
        {
            var resourcePaths = new HashSet<string>();
            foreach (PieceIdentity identity in PhysicalPieceCatalog.All)
            {
                string path = DirectionECarVisual.BodyResourcePath(identity);
                Assert.That(resourcePaths.Add(path), Is.True,
                    identity.VisualIdentity + " must own a distinct color/marker asset");
                Texture2D body = DirectionECarVisual.LoadBody(identity);
                Assert.That(body.width, Is.EqualTo(DirectionECarVisual.BodySourceWidth));
                Assert.That(body.height, Is.EqualTo(DirectionECarVisual.BodySourceHeight));
                Assert.That(body.width / DirectionECarVisual.BodyPixelsPerUnit,
                    Is.EqualTo(RaceSurfaceGeometry.CarBodyHalfSize * 2f));
                Assert.That(body.height / DirectionECarVisual.BodyPixelsPerUnit,
                    Is.EqualTo(RaceSurfaceGeometry.CarBodyHalfWidth * 2f));
            }

            Texture2D shadow = DirectionECarVisual.LoadContactShadow();
            Assert.That(shadow.width, Is.EqualTo(DirectionECarVisual.ShadowSourceWidth));
            Assert.That(shadow.height, Is.EqualTo(DirectionECarVisual.ShadowSourceHeight));
            Assert.That(shadow.width / DirectionECarVisual.ShadowPixelsPerUnit,
                Is.EqualTo(RaceSurfaceGeometry.CarBodyHalfSize * 2f));
            Assert.That(shadow.height / DirectionECarVisual.ShadowPixelsPerUnit,
                Is.EqualTo(RaceSurfaceGeometry.CarBodyHalfWidth * 2f));
        }

        [Test]
        public void CompactPitBoxesKeepTheApprovedCarClearance()
        {
            Assert.That(RaceSurfaceGeometry.PitBoxHalfLength -
                RaceSurfaceGeometry.CarBodyHalfSize, Is.EqualTo(20f),
                "front/rear clearance");
            Assert.That(RaceSurfaceGeometry.PitBoxHalfWidth -
                RaceSurfaceGeometry.CarBodyHalfWidth, Is.EqualTo(10f),
                "side clearance");
            Assert.That(CourseLint.MinBoxGap, Is.EqualTo(20f),
                "edge-to-edge clearance between neighboring boxes");
        }

        [Test]
        public void NonContiguousRosterAccentsOnlyItsOwnedPitBoxes()
        {
            PitLanePresentationLayout layout = PitLayout();
            var accents = new Dictionary<PlayerId, Color>
            {
                [PlayerId.Player2] = Color.blue,
                [PlayerId.Player4] = Color.yellow
            };
            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(Track, layout, accents);

            AssertAccentCenteredOn(mesh, Color.blue, layout.Box(PlayerId.Player2));
            AssertAccentCenteredOn(mesh, Color.yellow, layout.Box(PlayerId.Player4));
            Assert.That(mesh.Colors, Has.Some.EqualTo(RaceSurfaceGeometry.InactivePitBoxAccent),
                "inactive P1/P3 boxes stay neutral");
        }

        [Test]
        public void ASideBySidePairFitsTheTrackRibbon()
        {
            // The point of narrowing the bodies (issue #117 round 2): the
            // full passing split plus a half body stays on the pavement, so
            // the straightaway two-wide formation stopped being a fiction.
            Assert.That(RaceRules.Defaults.PassingOffset + RaceSurfaceGeometry.CarBodyHalfWidth,
                Is.LessThanOrEqualTo(RaceSurfaceGeometry.TrackWidth * .5f + 1f));
            // And the split still separates the bodies — a seam of daylight,
            // racing close (owner-tightened on hardware review).
            Assert.That(RaceRules.Defaults.PassingOffset * 2f,
                Is.GreaterThan(RaceSurfaceGeometry.CarBodyHalfWidth * 2f + 1f));
        }

        // The sim's placement is only half the answer (issue #143) — the drawn
        // split tapers through corners while the drawn pads open, and for a
        // stretch of every corner approach neither separation was doing the
        // job (owner report 2026-07-25: cars overlap). This walks a
        // dead-heat field around a whole course through the exact drawn
        // composition and pins the seam of daylight everywhere on it.

        // The floor is defined as the inverse of the clearance it buys, so the
        // two can never drift apart under a body-shape change.

        // The Y-junction pins (issue #107 phase 2): the pit lane meets the track
        // as clamped shared-edge gores instead of a full ribbon hidden by paint
        // order — no lane geometry in the roadway, each mouth running along the
        // track edge, and the merge climbing at a slip-road angle (the ~40°
        // dive read as the lane vanishing under the track in three hardware
        // reviews).
        private const float LaneFloor =
            RaceSurfaceGeometry.TrackWidth * .5f - RaceSurfaceGeometry.JunctionEdgeOverlap;

        private static void AssertAccentCenteredOn(SurfaceMeshData mesh, Color accent,
            Vec2 expected)
        {
            Vector3[] vertices = mesh.Vertices
                .Where((vertex, index) => mesh.Colors[index] == accent).ToArray();
            Assert.That(vertices, Is.Not.Empty);
            Vector3 center = vertices.Aggregate(Vector3.zero, (sum, vertex) => sum + vertex) /
                vertices.Length;
            Assert.That(Vector2.Distance(center, new Vector2(expected.X, expected.Y)),
                Is.LessThan(.01f));
        }

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
            AssertMouthHugsTheEdge(x => x > CourseCatalog.Wedge().Pit.Boxes[3].X,
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
            // row threading beneath the bridge. The compact inner boxes keep
            // their painted quads outside the crossing ribbon while the lane
            // itself remains free to pass under.
            CourseDefinition course = CourseCatalog.Infinity();
            Vector2 crossing = RaceSurfaceGeometry.FindCrossings(course.Track)[0].Point;
            Vector2 boxOne = new Vector2(course.Pit.Boxes[0].X, course.Pit.Boxes[0].Y);
            Vector2 boxFour = new Vector2(course.Pit.Boxes[3].X, course.Pit.Boxes[3].Y);
            Vector2 row = (boxFour - boxOne).normalized;
            Vector2 across = new Vector2(-row.y, row.x);
            float alongOne = Vector2.Dot(boxOne - crossing, row);
            float alongFour = Vector2.Dot(boxFour - crossing, row);
            Assert.That(alongOne * alongFour, Is.LessThan(0f),
                "the boxes must sit on opposite sides of the crossing");
            foreach (Vec2 box in course.Pit.Boxes)
                Assert.That(Vector2.Distance(new Vector2(box.X, box.Y), crossing),
                    Is.GreaterThanOrEqualTo(CourseLint.MinCrossingBoxClearance));
            float paintedEdgeClearance =
                Mathf.Abs(Vector2.Dot(boxOne - crossing, across)) -
                RaceSurfaceGeometry.PitBoxHalfWidth;
            Assert.That(paintedEdgeClearance,
                Is.GreaterThanOrEqualTo(RaceSurfaceGeometry.TrackWidth * .5f),
                "the compact pit-box paint must remain outside the bridge ribbon");
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
            Vector2 boxOne = new Vector2(course.Pit.Boxes[0].X, course.Pit.Boxes[0].Y);
            Vector2 boxFour = new Vector2(course.Pit.Boxes[3].X, course.Pit.Boxes[3].Y);
            Vector2 lane = (boxFour - boxOne).normalized;
            Vector2 across = new Vector2(-lane.y, lane.x);
            Vector2 boxCorner = boxOne + lane * RaceSurfaceGeometry.PitBoxHalfLength +
                across * RaceSurfaceGeometry.PitBoxHalfWidth;
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
        public void EveryCatalogPitBoxRendersAtTheAuthoredCompactFootprint()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout = PitLanePresentationLayout.ForCourse(course);
                SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track, layout,
                    Color.red, Color.blue);
                Vector2 first = new Vector2(layout.Boxes[0].X, layout.Boxes[0].Y);
                Vec2 lastBox = layout.Boxes[layout.Boxes.Count - 1];
                Vector2 along = (new Vector2(lastBox.X, lastBox.Y) - first).normalized;
                Vector2 across = new Vector2(-along.y, along.x);
                foreach (Vec2 box in layout.Boxes)
                {
                    Vector2 center = new Vector2(box.X, box.Y);
                    Vector2 expectedCorner = center +
                        along * RaceSurfaceGeometry.PitBoxHalfLength +
                        across * RaceSurfaceGeometry.PitBoxHalfWidth;
                    Assert.That(mesh.Vertices.Any(vertex =>
                            Vector2.Distance(vertex, expectedCorner) < .02f),
                        Is.True,
                        $"{course.Name} is missing the compact quad for box {box}");
                }
            }
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

        private static IEnumerable<Vector3> VerticesWithColor(SurfaceMeshData mesh, Color color)
        {
            for (int i = 0; i < mesh.Vertices.Count; i++)
                if (mesh.Colors[i] == color) yield return mesh.Vertices[i];
        }

        // ---- World-space detail mapping (issue #161) --------------------------

        private static RaceSurfaceStyle TexturedStyle()
        {
            RaceSurfaceStyle style = RaceSurfaceStyle.Default;
            style.GroundDetail = Texture2D.whiteTexture;
            style.RoadDetail = Texture2D.whiteTexture;
            style.ShoulderDetail = Texture2D.whiteTexture;
            style.DetailStrength = 1f;
            return style;
        }

        [Test]
        public void EveryVertexCarriesADetailWeightOnEveryCatalogCourse()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track,
                    PitLanePresentationLayout.ForCourse(course),
                    new[] { Color.red, Color.blue }, TexturedStyle());
                Assert.That(mesh.Details.Count, Is.EqualTo(mesh.Vertices.Count),
                    course.Name + " must carry one detail weight per vertex");
            }
        }

        [Test]
        public void GroundBackdropCoversTheWholeReferenceCanvasAndPaintsFirst()
        {
            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(
                Track, PitLayout(), new[] { Color.red, Color.blue }, TexturedStyle());

            Assert.That(mesh.BackdropVertexCount, Is.EqualTo(4),
                "the backdrop is one quad, appended before any course geometry");
            Vector3[] ground = mesh.Vertices.Take(mesh.BackdropVertexCount).ToArray();
            Assert.That(ground.Min(v => v.x), Is.EqualTo(0f).Within(.001f));
            Assert.That(ground.Min(v => v.y), Is.EqualTo(0f).Within(.001f));
            Assert.That(ground.Max(v => v.x),
                Is.EqualTo(RaceLayout.ReferenceWidth).Within(.001f));
            Assert.That(ground.Max(v => v.y),
                Is.EqualTo(RaceLayout.ReferenceHeight).Within(.001f));
            // Ground is the complement of road and shoulder, so it claims
            // neither weight while still asking for detail.
            foreach (Vector4 detail in mesh.Details.Take(mesh.BackdropVertexCount))
            {
                Assert.That(detail.x, Is.EqualTo(0f).Within(.001f));
                Assert.That(detail.y, Is.EqualTo(0f).Within(.001f));
                Assert.That(detail.z, Is.EqualTo(1f).Within(.001f));
            }
        }

        [Test]
        public void RoadRibbonAsksForRoadDetailAndMarkingsStayFlat()
        {
            RaceSurfaceStyle style = TexturedStyle();
            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(
                Track, PitLayout(), new[] { Color.red, Color.blue }, style);

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Color color = mesh.Colors[i];
                bool road = color == style.StraightRoadColor || color == style.CornerRoadColor;
                if (road)
                    Assert.That(mesh.Details[i].x, Is.EqualTo(1f).Within(.001f),
                        "the road ribbon must sample the road detail texture");
            }

            // Markings sit over textured surfaces and must not pick up their
            // grain: stripes, the start line, and the pit boxes stay flat.
            foreach (Color marking in new[]
                { style.StripeColor, style.PitStripeColor, Color.white })
            {
                bool sawMarking = false;
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    if (mesh.Colors[i] != marking) continue;
                    sawMarking = true;
                    Assert.That(mesh.Details[i].z, Is.EqualTo(0f).Within(.001f),
                        $"marking {marking} must keep flat vertex color");
                }
                Assert.That(sawMarking, Is.True, $"expected marking {marking} in the mesh");
            }
        }

        [Test]
        public void ShoulderFeatherRampsWeightWhileStayingOpaque()
        {
            RaceSurfaceStyle style = TexturedStyle();
            style.ShoulderOpacity = 1f;
            style.ShoulderSolidWidth = 13f;
            style.ShoulderFeatherWidth = 18f;

            foreach (CourseDefinition course in CourseCatalog.All())
            {
                SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track,
                    PitLanePresentationLayout.ForCourse(course),
                    new[] { Color.red, Color.blue }, style);

                float[] weights = mesh.Details.Select(d => d.y)
                    .Where(w => w > .001f).Distinct().ToArray();
                Assert.That(weights.Length, Is.GreaterThan(1),
                    course.Name + " feather must ramp, not step");
                Assert.That(weights.Max(), Is.EqualTo(1f).Within(.001f),
                    course.Name + " inner shoulder must reach full weight");
                // The whole point of carrying coverage per-vertex instead of in
                // alpha: overlapping shoulder ribbons at a self-crossing resolve
                // by paint order rather than compounding into a dark knot. Only
                // the shoulder is claimed here — stripes and the crossing shadow
                // are deliberately translucent markings.
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    if (mesh.Details[i].y <= .001f) continue;
                    Assert.That(mesh.Colors[i].a, Is.EqualTo(1f).Within(.001f),
                        course.Name + " shoulder must never introduce translucency");
                }
            }
        }

        [Test]
        public void SwappingDetailTexturesLeavesGeometryAndColorUntouched()
        {
            RaceSurfaceStyle before = TexturedStyle();
            RaceSurfaceStyle after = TexturedStyle();
            after.RoadDetail = Texture2D.blackTexture;
            after.RoadDetailTile = before.RoadDetailTile * 2f;

            SurfaceMeshData a = RaceSurfaceGeometry.Build(
                Track, PitLayout(), new[] { Color.red, Color.blue }, before);
            SurfaceMeshData b = RaceSurfaceGeometry.Build(
                Track, PitLayout(), new[] { Color.red, Color.blue }, after);

            // The swap test #161 asks for: a texture reference changes what the
            // course looks like without moving a vertex or altering a vertex
            // colour, because both tiling and the surface colour are resolved in
            // the shader. Since #161's coloured-tile revision the swap changes
            // colour too, so this pins the mesh, not the appearance.
            Assert.That(b.Vertices, Is.EqualTo(a.Vertices));
            Assert.That(b.Colors, Is.EqualTo(a.Colors));
            Assert.That(b.Details, Is.EqualTo(a.Details));
            Assert.That(b.Triangles, Is.EqualTo(a.Triangles));
        }

        [Test]
        public void FlatCommittedTreatmentRemainsAvailableAsAFallback()
        {
            // RaceSurfaceStyle.Default is still textureless, so the pre-#161
            // look stays reachable as a deterministic comparison baseline.
            RaceSurfaceStyle flat = RaceSurfaceStyle.Default;
            Assert.That(flat.GroundDetail, Is.Null);
            Assert.That(flat.RoadDetail, Is.Null);
            Assert.That(flat.ShoulderDetail, Is.Null);
            Assert.That(flat.DetailStrength, Is.EqualTo(0f));
            // Tints are grades on top of authored tile colour, so the baseline
            // is white: pit lane and corners share the road tile ungraded.
            Assert.That(flat.GroundDetailTint, Is.EqualTo(Color.white));
            Assert.That(flat.RoadDetailTint, Is.EqualTo(Color.white));
            Assert.That(flat.ShoulderDetailTint, Is.EqualTo(Color.white));

            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(
                Track, PitLayout(), new[] { Color.red, Color.blue }, flat);
            Assert.That(mesh.Details.Count, Is.EqualTo(mesh.Vertices.Count));
        }
    }
}
