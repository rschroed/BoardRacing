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
        public void ApprovedPitFieldIsContinuousFromSharedLaneThroughEveryStall()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track, layout,
                    Color.red, Color.blue);
                for (int stall = 0; stall < layout.Boxes.Count; stall++)
                {
                    Vector2 lane = ToVector(layout.LaneAnchors[stall]);
                    Vector2 box = ToVector(layout.Boxes[stall]);
                    Vector2 interior = (lane + box) * .5f;
                    Assert.That(PitSurfaceCovers(mesh, interior), Is.True,
                        $"{course.Name} opens a lane/service window at {interior}");
                    Vec2[] service = PitLanePresentationMapper.ServiceCurveSamples(
                        (PlayerId)(stall + 1), layout);
                    for (int sample = 0; sample < service.Length; sample++)
                    {
                        Vector2 point = ToVector(service[sample]);
                        Assert.That(PitSurfaceCovers(mesh, point), Is.True,
                            $"{course.Name} opens a gap along stall {stall + 1} at {point}");
                    }
                }
            }
        }

        [Test]
        public void PitRoadCenterlineIsContinuousThroughEntrySharedLaneAndMerge()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track, layout,
                    Color.red, Color.blue);

                for (int sample = 0; sample <= 40; sample++)
                {
                    float progress = sample / 40f;
                    Vector2 entry = ToVector(PitLanePresentationMapper
                        .SharedEntryPose(progress, layout).Position);
                    Vector2 merge = ToVector(PitLanePresentationMapper
                        .SharedMergePose(progress, layout).Position);
                    Assert.That(PitSurfaceCovers(mesh, entry), Is.True,
                        $"{course.Name} opens its entry road at {entry}");
                    Assert.That(PitSurfaceCovers(mesh, merge), Is.True,
                        $"{course.Name} opens its merge road at {merge}");
                }
                foreach (Vec2 waypoint in layout.LaneWaypoints)
                {
                    Vector2 lane = ToVector(waypoint);
                    Assert.That(PitSurfaceCovers(mesh, lane), Is.True,
                        $"{course.Name} opens its shared lane at {lane}");
                }
            }
        }

        [Test]
        public void DetachedPitRoadsRemainDistinctBetweenAuthoredJunctions()
        {
            // Infinity deliberately pulls away from the track. Its old
            // projection bridge filled the whole course-to-lane gap with
            // asphalt. The spline surface joins at the authored entry and
            // rejoin while leaving the middle separator to the shared tan
            // shoulder treatment. Fishhook runs close enough to the road that
            // its shoulder bands naturally meet, so it is covered by the route
            // continuity assertion instead of requiring a fixed-width gap.
            foreach (CourseDefinition course in new[] { CourseCatalog.Infinity() })
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track, layout,
                    Color.red, Color.blue);
                Vector2 pit = layout.LaneWaypoints
                    .Select(ToVector)
                    .OrderByDescending(point =>
                        Vector2.Distance(point, NearestPointOnTrack(point, course.Track)))
                    .First();
                Vector2 track = NearestPointOnTrack(pit, course.Track);
                Vector2 across = (pit - track).normalized;
                Vector2 trackEdge = track + across *
                    (RaceSurfaceGeometry.TrackWidth * .5f);
                Vector2 pitEdge = pit - across *
                    (RaceSurfaceGeometry.PitLaneWidth * .5f);
                Assert.That(Vector2.Distance(trackEdge, pitEdge), Is.GreaterThan(2f),
                    course.Name + " needs a visible separator for this assertion");
                Vector2 separator = (trackEdge + pitEdge) * .5f;
                Assert.That(RoadFamilyCovers(mesh, separator), Is.False,
                    $"{course.Name} reintroduced a broad asphalt bridge at {separator}");
            }
        }

        [Test]
        public void ApprovedPitFieldUsesTheCourseShoulderTreatment()
        {
            RaceSurfaceStyle style = RaceSurfaceStyle.Default;
            style.ShoulderOpacity = 1f;
            style.ShoulderSolidWidth = 13f;
            style.ShoulderFeatherWidth = 18f;
            Color apron = style.ShoulderColor;
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track, layout,
                    new[] { Color.red, Color.blue }, style);
                float servicePadHalfWidth =
                    RaceSurfaceGeometry.ServicePadHalfWidth(layout);
                for (int stall = 0; stall < layout.Boxes.Count; stall++)
                {
                    Vector2 anchor = ToVector(layout.LaneAnchors[stall]);
                    Vector2 box = ToVector(layout.Boxes[stall]);
                    Vector2 outward = (box - anchor).normalized;
                    Vector2 apronSample = box + outward *
                        (servicePadHalfWidth + style.ShoulderSolidWidth * .5f);
                    Assert.That(SurfaceColorCovers(mesh, apronSample, apron), Is.True,
                        $"{course.Name} does not carry the course apron through stall {stall + 1}");
                }
            }
        }

        [Test]
        public void ApprovedPitFieldHasNoDiscreteBayOverlayColors()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track, layout,
                    Color.red, Color.blue);
                Color overlay = Color.Lerp(
                    RaceSurfaceGeometry.PitLaneColor, Color.black, .18f);
                Assert.That(mesh.Colors, Has.None.EqualTo(overlay),
                    course.Name + " reintroduced a bounded per-bay surface");
            }
        }

        [Test]
        public void RosterAccentsDoNotPaintBackgroundsBehindTheAssetBasedPitKits()
        {
            PitLanePresentationLayout layout = PitLayout();
            var firstAccents = new Dictionary<PlayerId, Color>
            {
                [PlayerId.Player2] = Color.blue,
                [PlayerId.Player4] = Color.yellow
            };
            var secondAccents = new Dictionary<PlayerId, Color>
            {
                [PlayerId.Player1] = Color.red,
                [PlayerId.Player3] = Color.green
            };
            SurfaceMeshData first = RaceSurfaceGeometry.Build(Track, layout, firstAccents);
            SurfaceMeshData second = RaceSurfaceGeometry.Build(Track, layout, secondAccents);

            Assert.That(first.Vertices, Is.EqualTo(second.Vertices));
            Assert.That(first.Colors, Is.EqualTo(second.Colors),
                "player identity belongs to the retained marker/car assets, not the mesh");
            Assert.That(first.Colors, Has.None.EqualTo(Color.blue));
            Assert.That(first.Colors, Has.None.EqualTo(Color.yellow));
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

        [Test]
        public void SharedPitStripeDoesNotCrossTheServiceBays()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track, layout,
                    Color.red, Color.blue);
                for (int stall = 0; stall < layout.Boxes.Count; stall++)
                {
                    Vector2 box = new Vector2(layout.Boxes[stall].X, layout.Boxes[stall].Y);
                    float nearestStripe = mesh.Vertices
                        .Where((vertex, index) =>
                            mesh.Colors[index] == RaceSurfaceGeometry.PitStripeColor)
                        .Min(vertex => Vector2.Distance(vertex, box));
                    Assert.That(nearestStripe, Is.GreaterThanOrEqualTo(
                            RaceSurfaceGeometry.PitBoxHalfWidth -
                            RaceSurfaceGeometry.JunctionEdgeOverlap - .1f),
                        $"{course.Name} shared-lane marking enters stall {stall + 1}");
                }
            }
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
                .SharedMergePose(0f, layout).Position;
            for (float progress = .02f; progress <= 1.0001f; progress += .02f)
            {
                Vec2 position = PitLanePresentationMapper
                    .SharedMergePose(progress, layout).Position;
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
            // row threading beneath the bridge. The compact functional
            // footprints stay outside the crossing ribbon while the lane
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
                "the compact pit footprint must remain outside the bridge ribbon");
        }

        [Test]
        public void FishhookDoesNotCrossItself()
        {
            Assert.That(RaceSurfaceGeometry.FindCrossings(TrackCatalog.Fishhook()), Is.Empty);
        }

        [Test]
        public void StartLineFollowsADiagonalTrackStraightWithoutPlayerColorPaint()
        {
            // The retained pit-kit roots own the diagonal stall orientation.
            // The surface mesh keeps only its own start line, which still must
            // follow travel instead of staying axis-aligned.
            CourseDefinition course = CourseCatalog.Infinity();
            SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track,
                PitLanePresentationLayout.ForCourse(course), Color.red, Color.blue);
            Vec2 start = course.Track.Sample(0f).Position;
            TrackSegment first = course.Track.Segments[0];
            Vector2 travel = (new Vector2(first.End.X, first.End.Y) -
                new Vector2(first.Start.X, first.Start.Y)).normalized;
            Vector2 lineCorner = new Vector2(start.X, start.Y) +
                travel * 12f + new Vector2(-travel.y, travel.x) * 28f;
            bool lineCornerFound = false;
            foreach (Vector3 vertex in mesh.Vertices)
            {
                if (Vector2.Distance(vertex, lineCorner) < .5f) lineCornerFound = true;
            }
            Assert.That(lineCornerFound, Is.True, $"no start-line vertex at rotated corner {lineCorner}");
            Assert.That(mesh.Colors, Has.None.EqualTo(Color.red));
            Assert.That(mesh.Colors, Has.None.EqualTo(Color.blue));
        }

        [Test]
        public void EveryCatalogPitFieldStaysNeutralWithoutPerPlayerBoxPaint()
        {
            RaceSurfaceStyle style = RaceSurfaceStyle.Default;
            style.ShoulderOpacity = 1f;
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout = PitLanePresentationLayout.ForCourse(course);
                SurfaceMeshData mesh = RaceSurfaceGeometry.Build(course.Track, layout,
                    new[] { Color.red, Color.blue }, style);
                Assert.That(mesh.Colors, Has.None.EqualTo(Color.red), course.Name);
                Assert.That(mesh.Colors, Has.None.EqualTo(Color.blue), course.Name);
                Assert.That(mesh.Colors, Has.Some.EqualTo(
                    RaceSurfaceGeometry.PitLaneColor),
                    course.Name + " still needs one neutral continuous work field");
                Assert.That(mesh.Colors, Has.Some.EqualTo(
                    RaceSurfaceStyle.Default.ShoulderColor),
                    course.Name + " still needs the neutral exterior apron");
            }
        }

        [Test]
        public void PitLaneRendersUnderTheTrackFill()
        {
            // The continuous roadbed tucks beneath the road at each hand-off;
            // the opaque track fill must paint afterward so the visible seam
            // remains exactly the authored road edge.
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

        private static bool PitSurfaceCovers(SurfaceMeshData mesh, Vector2 point) =>
            SurfaceColorCovers(mesh, point, RaceSurfaceGeometry.PitLaneColor);

        private static bool RoadFamilyCovers(SurfaceMeshData mesh, Vector2 point) =>
            SurfaceColorCovers(mesh, point, RaceSurfaceGeometry.PitLaneColor) ||
            SurfaceColorCovers(mesh, point, RaceSurfaceGeometry.StraightColor) ||
            SurfaceColorCovers(mesh, point, RaceSurfaceGeometry.CornerColor);

        private static bool SurfaceColorCovers(SurfaceMeshData mesh,
            Vector2 point, Color color)
        {
            for (int triangle = 0; triangle < mesh.Triangles.Count; triangle += 3)
            {
                int ia = mesh.Triangles[triangle];
                int ib = mesh.Triangles[triangle + 1];
                int ic = mesh.Triangles[triangle + 2];
                if (mesh.Colors[ia] != color ||
                    mesh.Colors[ib] != color ||
                    mesh.Colors[ic] != color)
                    continue;
                if (PointInTriangle(point, mesh.Vertices[ia],
                    mesh.Vertices[ib], mesh.Vertices[ic]))
                    return true;
            }
            return false;
        }

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float ab = Cross(b - a, point - a);
            float bc = Cross(c - b, point - b);
            float ca = Cross(a - c, point - c);
            const float tolerance = .01f;
            bool negative = ab < -tolerance || bc < -tolerance || ca < -tolerance;
            bool positive = ab > tolerance || bc > tolerance || ca > tolerance;
            return !(negative && positive);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static Vector2 ToVector(Vec2 value) => new Vector2(value.X, value.Y);

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

        private static Vector2 NearestPointOnTrack(Vector2 point, TrackDefinition track)
        {
            float best = float.MaxValue;
            Vector2 nearest = default;
            foreach (TrackSegment segment in track.Segments)
            {
                Vector2 start = ToVector(segment.Start);
                Vector2 end = ToVector(segment.End);
                Vector2 direction = end - start;
                float t = Mathf.Clamp01(Vector2.Dot(point - start, direction) /
                    direction.sqrMagnitude);
                Vector2 candidate = start + direction * t;
                float distance = Vector2.SqrMagnitude(point - candidate);
                if (distance >= best) continue;
                best = distance;
                nearest = candidate;
            }
            return nearest;
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
            // grain: stripes and the start line stay flat.
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
