using System;
using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    /// <summary>
    /// Which world-space detail texture a vertex samples, and how strongly
    /// (issue #161). The surface stays one mesh with one material: rather than
    /// splitting into per-material submeshes — which would trade the painter
    /// order for renderer sorting — every vertex carries the blend itself.
    /// Ground/road/shoulder weights select among the theme's detail textures;
    /// Strength fades the whole sample back to flat vertex color, which is how
    /// markings (stripes, start line, boxes, crossing dressing) stay crisp.
    /// </summary>
    internal readonly struct SurfaceDetail
    {
        private SurfaceDetail(float roadWeight, float shoulderWeight, float strength)
        {
            RoadWeight = roadWeight;
            ShoulderWeight = shoulderWeight;
            Strength = strength;
        }

        public float RoadWeight { get; }
        public float ShoulderWeight { get; }
        public float Strength { get; }

        // The ground texture is the complement: a vertex with no road and no
        // shoulder weight samples ground, so the shader needs only three
        // samplers for four surfaces.
        public static readonly SurfaceDetail Flat = new SurfaceDetail(0f, 0f, 0f);
        public static readonly SurfaceDetail Ground = new SurfaceDetail(0f, 0f, 1f);
        public static readonly SurfaceDetail Road = new SurfaceDetail(1f, 0f, 1f);

        // The pit surface shares the road tile ungraded, so it is currently
        // indistinguishable from the roadway while textured — a deliberate flat
        // baseline for judging the raw assets (#161). Sharing a tile also keeps
        // the grain flowing across the boundary, which reads as wear; giving
        // pit its own tile would break the pattern there and read as a
        // different material instead. Which of those is wanted is a call for
        // the Board review.
        public static readonly SurfaceDetail PitSurface = Road;

        public static SurfaceDetail Shoulder(float weight) =>
            new SurfaceDetail(0f, Mathf.Clamp01(weight), 1f);

        public Vector4 ToUv() => new Vector4(RoadWeight, ShoulderWeight, Strength, 0f);
    }

    /// <summary>
    /// The static racing surface as a world-space mesh (issue #86, round 1):
    /// track ribbon, start/finish line, pit lane, and pit boxes leave IMGUI and
    /// become one vertex-colored mesh drawn by an orthographic camera whose
    /// projection is pinned to the 1920×1080 reference rect — world coordinates
    /// ARE RaceLayout's Y-down reference pixels, matching the IMGUI stretch at
    /// any resolution. Everything appends at z = 0 in paint order: the shared
    /// alpha-blended material draws triangles in index order, so append order
    /// reproduces IMGUI's painter layering exactly.
    /// </summary>
    internal sealed class SurfaceMeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<Vector4> Details = new List<Vector4>();
        public readonly List<int> Triangles = new List<int>();

        /// <summary>
        /// How many leading vertices are backdrop rather than course geometry
        /// (issue #161). The ground quad is appended first and covers the whole
        /// reference canvas by definition, so callers that ask "does the course
        /// fit?" — CourseLint above all — skip this prefix. Nothing else about
        /// the mesh distinguishes them: the backdrop is ordinary paint-ordered
        /// geometry, it just is not part of the course footprint.
        /// </summary>
        public int BackdropVertexCount { get; private set; }

        public void MarkBackdropComplete() => BackdropVertexCount = Vertices.Count;

        // Corners a→b→c→d in screen order (any winding — the sprite material
        // does not cull); each quad owns its four vertices so per-quad color
        // stays hard-edged instead of interpolating across shared vertices.
        public void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color) =>
            AddQuad(a, b, c, d, color, SurfaceDetail.Flat);

        public void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color,
            SurfaceDetail detail)
        {
            int baseIndex = Vertices.Count;
            Vertices.Add(new Vector3(a.x, a.y, 0f));
            Vertices.Add(new Vector3(b.x, b.y, 0f));
            Vertices.Add(new Vector3(c.x, c.y, 0f));
            Vertices.Add(new Vector3(d.x, d.y, 0f));
            Vector4 uv = detail.ToUv();
            for (int i = 0; i < 4; i++) { Colors.Add(color); Details.Add(uv); }
            Triangles.Add(baseIndex); Triangles.Add(baseIndex + 1); Triangles.Add(baseIndex + 2);
            Triangles.Add(baseIndex); Triangles.Add(baseIndex + 2); Triangles.Add(baseIndex + 3);
        }

        public void AddRect(Rect rect, Color color) => AddRect(rect, color, SurfaceDetail.Flat);

        public void AddRect(Rect rect, Color color, SurfaceDetail detail) => AddQuad(
            new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
            new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), color, detail);

        public void AddRectOutline(Rect rect, float width, Color color)
        {
            AddRect(new Rect(rect.x, rect.y, rect.width, width), color);
            AddRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            AddRect(new Rect(rect.x, rect.y, width, rect.height), color);
            AddRect(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        // Convex closed fan: center vertex plus the perimeter loop (last point
        // connects back to the first).
        public void AddFan(Vector2 center, IReadOnlyList<Vector2> perimeter, Color color)
        {
            Vector4 uv = SurfaceDetail.Flat.ToUv();
            int centerIndex = Vertices.Count;
            Vertices.Add(new Vector3(center.x, center.y, 0f));
            Colors.Add(color);
            Details.Add(uv);
            int first = Vertices.Count;
            for (int i = 0; i < perimeter.Count; i++)
            {
                Vertices.Add(new Vector3(perimeter[i].x, perimeter[i].y, 0f));
                Colors.Add(color);
                Details.Add(uv);
            }
            for (int i = 0; i < perimeter.Count; i++)
            {
                Triangles.Add(centerIndex);
                Triangles.Add(first + i);
                Triangles.Add(first + (i + 1) % perimeter.Count);
            }
        }
    }

    // A point where the racing line crosses itself (figure-8 courses, issue
    // #107 phase 4). The later segment is the bridge deck: appended later in
    // the ribbon, it draws on top by paint order.
    internal readonly struct TrackCrossing
    {
        public TrackCrossing(int earlierSegment, int laterSegment, Vector2 point,
            Vector2 laterDirection)
        {
            EarlierSegment = earlierSegment;
            LaterSegment = laterSegment;
            Point = point;
            LaterDirection = laterDirection;
        }

        public int EarlierSegment { get; }
        public int LaterSegment { get; }
        public Vector2 Point { get; }
        public Vector2 LaterDirection { get; }
    }

    internal readonly struct CenterlineSample
    {
        public CenterlineSample(Vector2 position, bool corner)
        { Position = position; Corner = corner; }
        public Vector2 Position { get; }
        // Kind of the authored chord this sample was interpolated from; ribbon
        // quads take the leading sample's kind, matching the per-chord coloring
        // the IMGUI draw had.
        public bool Corner { get; }
    }

    internal enum RaceSurfaceDebugView
    {
        Composed,
        ShoulderOnly,
        RoadBoundary,
    }

    /// <summary>
    /// Rendering-only course treatment. CourseDefinition remains the single
    /// source of truth for racing line, pits, laps, and simulation behavior;
    /// this value only changes how RaceSurfaceGeometry paints that definition.
    /// </summary>
    [Serializable]
    internal struct RaceSurfaceStyle
    {
        public Color GroundColor;
        public Color StraightRoadColor;
        public Color CornerRoadColor;
        public Color StripeColor;
        public Color PitSurfaceColor;
        public Color PitStripeColor;
        public Color InactivePitBoxAccent;
        public Color CrossingShadowColor;
        public Color ShoulderColor;
        [Range(0f, 1f)] public float ShoulderOpacity;
        [Min(0f)] public float ShoulderSolidWidth;
        [Min(0f)] public float ShoulderFeatherWidth;
        public Color RoadBoundaryColor;
        public bool StripeVisible;
        public bool PitSurfaceVisible;
        public RaceSurfaceDebugView DebugView;
        // World-space detail (issue #161). Tile sizes are in reference pixels,
        // the same units as TrackWidth, so "88" reads directly as "about two
        // repeats across the 64 px road". Null textures and zero strength both
        // fall back to the committed flat treatment, which is what keeps the
        // pre-#161 look available as a deterministic comparison baseline.
        // Referenced rather than found by name: a shader reached only
        // through Shader.Find is stripped from a player build, so the
        // committed material is what keeps detail alive on Android.
        public Material SurfaceMaterial;
        public Texture2D GroundDetail;
        public Texture2D RoadDetail;
        public Texture2D ShoulderDetail;
        [Min(1f)] public float GroundDetailTile;
        [Min(1f)] public float RoadDetailTile;
        [Min(1f)] public float ShoulderDetailTile;
        // Grades on top of the authored tile color, not the color source.
        // White is the baseline; pit lane and corners currently share the road
        // tile ungraded so every road-family surface reads identically.
        public Color GroundDetailTint;
        public Color RoadDetailTint;
        public Color ShoulderDetailTint;
        [Range(0f, 1f)] public float DetailStrength;

        public static RaceSurfaceStyle Default => new RaceSurfaceStyle
        {
            GroundColor = RaceSurfaceGeometry.BackgroundColor,
            StraightRoadColor = RaceSurfaceGeometry.StraightColor,
            CornerRoadColor = RaceSurfaceGeometry.CornerColor,
            StripeColor = RaceSurfaceGeometry.StripeColor,
            PitSurfaceColor = RaceSurfaceGeometry.PitLaneColor,
            PitStripeColor = RaceSurfaceGeometry.PitStripeColor,
            InactivePitBoxAccent = RaceSurfaceGeometry.InactivePitBoxAccent,
            CrossingShadowColor = RaceSurfaceGeometry.CrossingShadowColor,
            ShoulderColor = new Color(.28f, .24f, .18f, 1f),
            ShoulderOpacity = 0f,
            // Latent useful geometry: the committed look has no shoulder
            // contribution, but one opacity tap produces a reviewable result.
            ShoulderSolidWidth = 12f,
            ShoulderFeatherWidth = 24f,
            RoadBoundaryColor = new Color(.95f, .2f, .75f, 1f),
            StripeVisible = true,
            PitSurfaceVisible = true,
            DebugView = RaceSurfaceDebugView.Composed,
            // 1:1 with the 128 px source tiles. A tile shown smaller than its
            // source is a downscale, and the mip that results averages the
            // authored grain away — see Presentation/PROVENANCE.md.
            GroundDetailTile = 128f,
            RoadDetailTile = 128f,
            ShoulderDetailTile = 128f,
            GroundDetailTint = Color.white,
            RoadDetailTint = Color.white,
            ShoulderDetailTint = Color.white,
            // The committed default stays flat and textureless: the theme asset
            // is what opts a build into detail, so Default remains the
            // deterministic fallback the gallery captures compare against.
            DetailStrength = 0f,
        };
    }

    internal static class RaceSurfaceGeometry
    {
        public const float TrackWidth = 64f;
        public const float TrackStripeWidth = 3f;
        public const float PitLaneWidth = 30f;
        public const float PitStripeWidth = 2f;
        // The continuous service pad runs through the four parked positions.
        // Forty-two pixels is the maximum needed by the 54 px-offset layouts:
        // it reaches their shared lane by 3 px after accounting for its 15 px
        // half-width, while leaving enough depth behind the car for the
        // approved rail and arm roots. Shallower authored layouts derive a
        // smaller pad from their lane-to-bay spacing rather than overflowing
        // the common race bounds.
        public const float PitServicePadHalfWidth = 42f;
        public const float PitServicePadMinimumHalfWidth = 30f;
        private const float PitServicePadLaneOverlap = 3f;
        public const float PitBoxFrontRearClearance = 20f;
        public const float PitBoxSideClearance = 10f;
        public const float PitBoxHalfLength = CarBodyHalfSize + PitBoxFrontRearClearance;
        public const float PitBoxHalfWidth = CarBodyHalfWidth + PitBoxSideClearance;
        // The authored polyline steps ≤12-13° per chord (TrackCatalog); splitting
        // each chord six ways brings the drawn ribbon under ~2.2° per step, which
        // reads as a continuous arc at 64 px width — the chord scalloping fix.
        public const int SamplesPerChord = 6;

        public static readonly Color BackgroundColor = new Color(.025f, .035f, .05f);
        public static readonly Color StraightColor = new Color(.16f, .2f, .27f);
        public static readonly Color CornerColor = new Color(.22f, .28f, .36f);
        public static readonly Color StripeColor = new Color(.55f, .62f, .7f, .5f);
        public static readonly Color PitLaneColor = new Color(.08f, .11f, .15f);
        public static readonly Color PitStripeColor = new Color(.62f, .68f, .74f, .55f);
        public static readonly Color InactivePitBoxAccent = new Color(.48f, .52f, .58f);
        public static readonly Color CrossingShadowColor = new Color(0f, 0f, 0f, .35f);
        // How far a junction ribbon may tuck under the track fill: deep enough
        // that rasterization can never open a background sliver along the seam,
        // shallow enough that the fill always covers it.
        public const float JunctionEdgeOverlap = 2f;
        private const int LaneSteps = 36;

        public static SurfaceMeshData Build(TrackDefinition track,
            PitLanePresentationLayout pitLayout, Color playerOneAccent, Color playerTwoAccent)
            => Build(track, pitLayout, new[] { playerOneAccent, playerTwoAccent },
                RaceSurfaceStyle.Default);

        public static SurfaceMeshData Build(TrackDefinition track,
            PitLanePresentationLayout pitLayout, IReadOnlyList<Color> playerAccents)
            => Build(track, pitLayout, playerAccents, RaceSurfaceStyle.Default);

        public static SurfaceMeshData Build(TrackDefinition track,
            PitLanePresentationLayout pitLayout, IReadOnlyList<Color> playerAccents,
            RaceSurfaceStyle style)
        {
            if (playerAccents == null) throw new ArgumentNullException(nameof(playerAccents));
            return Build(track, pitLayout, playerAccents
                .Select((accent, index) =>
                    new KeyValuePair<PlayerId, Color>((PlayerId)(index + 1), accent))
                .ToDictionary(x => x.Key, x => x.Value), style);
        }

        public static SurfaceMeshData Build(TrackDefinition track,
            PitLanePresentationLayout pitLayout,
            IReadOnlyDictionary<PlayerId, Color> playerAccents)
            => Build(track, pitLayout, playerAccents, RaceSurfaceStyle.Default);

        public static SurfaceMeshData Build(TrackDefinition track,
            PitLanePresentationLayout pitLayout,
            IReadOnlyDictionary<PlayerId, Color> playerAccents,
            RaceSurfaceStyle style)
        {
            if (playerAccents == null) throw new ArgumentNullException(nameof(playerAccents));
            var mesh = new SurfaceMeshData();
            // The ground is explicit geometry now that it carries world-space
            // texture detail (issue #161). It is still the first thing painted,
            // so everything below keeps the order it had when the camera clear
            // color supplied this pixel — the clear color stays matched as a
            // belt-and-braces guard against a gap at an unexpected aspect.
            AppendGround(mesh, style);
            List<CenterlineSample> centerline = SmoothCenterline(track, SamplesPerChord);
            List<Vector2> entry = EntryLanePoints(pitLayout);
            var sharedLane = new List<Vector2>(pitLayout.LaneWaypoints.Count);
            foreach (Vec2 anchor in pitLayout.LaneWaypoints)
                sharedLane.Add(ToVector(anchor));
            List<Vector2> merge = MergeLanePoints(pitLayout);
            List<Vector2> pitRoad = SharedPitRoadPoints(entry, sharedLane, merge);
            var serviceCurves =
                new List<IReadOnlyList<Vector2>>(pitLayout.Stalls.Count);
            for (int i = 0; i < pitLayout.Stalls.Count; i++)
            {
                Vec2[] samples = PitLanePresentationMapper.ServiceCurveSamples(
                    (PlayerId)(i + 1), pitLayout);
                serviceCurves.Add(samples.Select(ToVector).ToArray());
            }
            float servicePadHalfWidth = ServicePadHalfWidth(pitLayout);
            Vector2[] parkedPositions = ExtendedServicePad(
                pitLayout.Boxes.Select(ToVector).ToArray(), servicePadHalfWidth);
            AppendComposedShoulder(mesh, centerline, pitRoad,
                serviceCurves, parkedPositions, servicePadHalfWidth, style);
            if (style.DebugView == RaceSurfaceDebugView.ShoulderOnly)
            {
                Color ground = Opaque(style.GroundColor);
                AppendClosedRibbon(mesh, centerline, TrackWidth,
                    ground, ground, SurfaceDetail.Ground);
                if (style.PitSurfaceVisible)
                    AppendPitPrimitives(mesh, pitRoad, serviceCurves,
                        parkedPositions, servicePadHalfWidth, 0f,
                        ground, SurfaceDetail.Ground);
                return mesh;
            }

            // All pit primitives share one opaque material and world-space
            // texture. Their deliberate overlaps therefore read as one surface
            // without a boolean mesh compiler: the common lane overlaps the
            // continuous parked row, and each driven service curve reinforces
            // that connection. The track still paints afterward so entry and
            // rejoin endpoints tuck cleanly under the authored road edge.
            if (style.PitSurfaceVisible)
            {
                AppendPitPrimitives(mesh, pitRoad, serviceCurves,
                    parkedPositions, servicePadHalfWidth, 0f, style.PitSurfaceColor,
                    SurfaceDetail.PitSurface);
                if (style.StripeVisible)
                    AppendOpenRibbon(mesh, pitRoad, PitStripeWidth, style.PitStripeColor);
            }

            if (style.DebugView == RaceSurfaceDebugView.RoadBoundary)
                AppendClosedRibbon(mesh, centerline, TrackWidth + 4f,
                    style.RoadBoundaryColor, style.RoadBoundaryColor);
            AppendClosedRibbon(mesh, centerline, TrackWidth,
                style.CornerRoadColor, style.StraightRoadColor, SurfaceDetail.Road);
            if (style.StripeVisible)
                AppendClosedRibbon(mesh, centerline, TrackStripeWidth,
                    style.StripeColor, style.StripeColor);
            foreach (TrackCrossing crossing in FindCrossings(track))
                AppendCrossingDeck(mesh, crossing, style);
            // The start line follows local travel direction. Pit ownership and
            // the compact 94×46 footprint now belong to the approved retained
            // pit kit (#183), not player-colored quads baked into this mesh.
            TrackSegment first = track.Segments[0];
            Vector2 startDirection = (ToVector(first.End) - ToVector(first.Start)).normalized;
            AppendOrientedRect(mesh, ToVector(track.Sample(0f).Position), startDirection,
                12f, 28f, Color.white);
            return mesh;
        }

        // The whole reference canvas, sampling the ground detail texture.
        private static void AppendGround(SurfaceMeshData mesh, RaceSurfaceStyle style)
        {
            mesh.AddRect(
                new Rect(0f, 0f, RaceLayout.ReferenceWidth, RaceLayout.ReferenceHeight),
                Opaque(style.GroundColor), SurfaceDetail.Ground);
            mesh.MarkBackdropComplete();
        }

        private static void AppendComposedShoulder(SurfaceMeshData mesh,
            IReadOnlyList<CenterlineSample> centerline,
            IReadOnlyList<Vector2> pitRoad,
            IReadOnlyList<IReadOnlyList<Vector2>> serviceCurves,
            IReadOnlyList<Vector2> parkedPositions,
            float servicePadHalfWidth,
            RaceSurfaceStyle style)
        {
            float opacity = Mathf.Clamp01(style.ShoulderOpacity);
            float solid = Mathf.Max(0f, style.ShoulderSolidWidth);
            float feather = Mathf.Max(0f, style.ShoulderFeatherWidth);
            if (opacity <= 0f || solid + feather <= 0f) return;

            // Compose by shoulder strength, not by object. The failed first pass
            // painted every track band and then every pit band, allowing the
            // pit's outer low-strength feather to overwrite the track's inner
            // high-strength shoulder. Here each width/weight is completed across
            // every spline before the next stronger band is drawn. Same-weight
            // overlaps are identical opaque world-textured paint, producing the
            // visual union we need without polygon clipping or tessellation.
            int steps = Mathf.Max(1, Mathf.CeilToInt(feather / 2f));
            Color shoulder = Opaque(style.ShoulderColor);
            Color ground = Opaque(style.GroundColor);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float eased = t * t * (3f - 2f * t);
                float weight = eased * opacity;
                float outsideWidth = solid + feather * (1f - t);
                Color color = Opaque(Color.Lerp(ground, shoulder, weight));
                SurfaceDetail detail = SurfaceDetail.Shoulder(weight);
                AppendClosedRibbon(mesh, centerline,
                    TrackWidth + outsideWidth * 2f, color, color, detail);
                if (style.PitSurfaceVisible)
                    AppendPitPrimitives(mesh, pitRoad, serviceCurves,
                        parkedPositions, servicePadHalfWidth,
                        outsideWidth, color, detail);
            }
        }

        private static void AppendPitPrimitives(SurfaceMeshData mesh,
            IReadOnlyList<Vector2> pitRoad,
            IReadOnlyList<IReadOnlyList<Vector2>> serviceCurves,
            IReadOnlyList<Vector2> parkedPositions,
            float servicePadHalfWidth,
            float outsideWidth, Color color, SurfaceDetail detail)
        {
            float expansion = Mathf.Max(0f, outsideWidth) * 2f;
            AppendOpenRibbon(mesh, pitRoad,
                PitLaneWidth + expansion, color, detail);
            AppendOpenRibbon(mesh, parkedPositions,
                servicePadHalfWidth * 2f + expansion, color, detail);
            for (int i = 0; i < serviceCurves.Count; i++)
                AppendOpenRibbon(mesh, serviceCurves[i],
                    PitLaneWidth + expansion, color, detail);
        }

        internal static float ServicePadHalfWidth(PitLanePresentationLayout layout)
        {
            if (layout.Boxes.Count != layout.LaneAnchors.Count || layout.Boxes.Count == 0)
                throw new ArgumentException(
                    "Pit boxes and lane anchors must be non-empty paired lists.",
                    nameof(layout));
            float laneOffset = 0f;
            for (int i = 0; i < layout.Boxes.Count; i++)
                laneOffset = Mathf.Max(laneOffset,
                    Vector2.Distance(ToVector(layout.Boxes[i]),
                        ToVector(layout.LaneAnchors[i])));
            float connectedDepth = laneOffset - PitLaneWidth * .5f +
                PitServicePadLaneOverlap;
            return Mathf.Clamp(connectedDepth,
                PitServicePadMinimumHalfWidth, PitServicePadHalfWidth);
        }

        private static Vector2[] ExtendedServicePad(
            IReadOnlyList<Vector2> parkedPositions, float halfWidth)
        {
            if (parkedPositions == null)
                throw new ArgumentNullException(nameof(parkedPositions));
            if (parkedPositions.Count < 2)
                throw new ArgumentException(
                    "A service pad needs at least two parked positions.",
                    nameof(parkedPositions));
            Vector2 direction = (parkedPositions[parkedPositions.Count - 1] -
                parkedPositions[0]).normalized;
            var extended = new Vector2[parkedPositions.Count + 2];
            // The row spline must continue past the end car centers into the
            // first and last service branches. Ending it at the centers leaves
            // triangular windows where a 30 px branch meets the wider pad. An
            // extension equal to the derived half-depth closes those windows
            // without imposing the deepest course's footprint on every layout.
            extended[0] = parkedPositions[0] - direction * halfWidth;
            for (int i = 0; i < parkedPositions.Count; i++)
                extended[i + 1] = parkedPositions[i];
            extended[extended.Length - 1] =
                parkedPositions[parkedPositions.Count - 1] +
                direction * halfWidth;
            return extended;
        }

        private static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);

        // Corner chords smooth through centripetal Catmull-Rom (interpolates
        // every authored point exactly; centripetal knots keep the long straight
        // neighbors from bulging the curve). Straight chords are authored
        // straight and subdivide linearly — a spline across a 911 px straight
        // between two arcs would sag several pixels mid-span.
        public static List<CenterlineSample> SmoothCenterline(TrackDefinition track,
            int samplesPerChord)
        {
            IReadOnlyList<TrackSegment> segments = track.Segments;
            int count = segments.Count;
            Vector2 Point(int index) => ToVector(segments[((index % count) + count) % count].Start);
            var samples = new List<CenterlineSample>(count * samplesPerChord);
            for (int chord = 0; chord < count; chord++)
            {
                bool corner = segments[chord].Kind == TrackSectionKind.Corner;
                for (int step = 0; step < samplesPerChord; step++)
                {
                    float t = step / (float)samplesPerChord;
                    samples.Add(new CenterlineSample(corner
                        ? CatmullRom(Point(chord - 1), Point(chord), Point(chord + 1),
                            Point(chord + 2), t)
                        : Vector2.LerpUnclamped(Point(chord), Point(chord + 1), t), corner));
                }
            }
            return samples;
        }

        public static void AppendClosedRibbon(SurfaceMeshData mesh,
            IReadOnlyList<CenterlineSample> samples, float width, Color cornerColor,
            Color straightColor)
            => AppendClosedRibbon(mesh, samples, width, cornerColor, straightColor,
                SurfaceDetail.Flat);

        public static void AppendClosedRibbon(SurfaceMeshData mesh,
            IReadOnlyList<CenterlineSample> samples, float width, Color cornerColor,
            Color straightColor, SurfaceDetail detail)
        {
            int count = samples.Count;
            var left = new Vector2[count];
            var right = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 position = samples[i].Position;
                Vector2 offset = MiterOffset(samples[((i - 1) % count + count) % count].Position,
                    position, samples[(i + 1) % count].Position, width * .5f);
                left[i] = position + offset;
                right[i] = position - offset;
            }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                mesh.AddQuad(left[i], left[next], right[next], right[i],
                    samples[i].Corner ? cornerColor : straightColor, detail);
            }
        }

        public static void AppendOpenRibbon(SurfaceMeshData mesh, IReadOnlyList<Vector2> points,
            float width, Color color)
            => AppendOpenRibbon(mesh, points, width, color, SurfaceDetail.Flat);

        public static void AppendOpenRibbon(SurfaceMeshData mesh, IReadOnlyList<Vector2> points,
            float width, Color color, SurfaceDetail detail)
        {
            int count = points.Count;
            if (count < 2) return;
            var left = new Vector2[count];
            var right = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = MiterOffset(points[Math.Max(0, i - 1)], points[i],
                    points[Math.Min(count - 1, i + 1)], width * .5f);
                left[i] = points[i] + offset;
                right[i] = points[i] - offset;
            }
            for (int i = 0; i < count - 1; i++)
                mesh.AddQuad(left[i], left[i + 1], right[i + 1], right[i], color, detail);
        }

        // Perpendicular offset at a polyline point, averaging the adjacent
        // directions (miter join). At an endpoint the degenerate neighbor drops
        // out and the offset is the plain perpendicular of the surviving chord.
        // The dot clamp caps the miter at 1.25× the half width: the ≤12°
        // authored track steps never approach it, and the pit exit spline's
        // sharper landing kink bevels instead of spiking (#86 hardware review).
        private static Vector2 MiterOffset(Vector2 previous, Vector2 current, Vector2 next,
            float halfWidth)
        {
            Vector2 into = Direction(previous, current);
            Vector2 outOf = Direction(current, next);
            if (into == Vector2.zero) into = outOf;
            if (outOf == Vector2.zero) outOf = into;
            Vector2 normalInto = new Vector2(-into.y, into.x);
            Vector2 normalOutOf = new Vector2(-outOf.y, outOf.x);
            Vector2 miter = (normalInto + normalOutOf).normalized;
            if (miter == Vector2.zero) miter = normalOutOf;
            return miter * (halfWidth / Mathf.Max(.8f, Vector2.Dot(miter, normalOutOf)));
        }

        private static Vector2 Direction(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            return delta.sqrMagnitude < 1e-8f ? Vector2.zero : delta.normalized;
        }

        // The drawn lane legs are the very splines the cars drive: entry along
        // Player1's entering spline (the players' paths only diverge past the
        // shared straight), merge along the last box's exiting spline (the lane
        // leaves the service row after that box). A car in a pit phase is
        // therefore always over pavement.
        private static List<Vector2> EntryLanePoints(PitLanePresentationLayout layout)
        {
            var points = new List<Vector2>(LaneSteps + 1);
            for (int i = 0; i <= LaneSteps; i++)
                points.Add(ToVector(PitLanePresentationMapper.SharedEntryPose(
                    i / (float)LaneSteps, layout).Position));
            return points;
        }

        private static List<Vector2> MergeLanePoints(PitLanePresentationLayout layout)
        {
            var points = new List<Vector2>(LaneSteps + 1);
            for (int i = 0; i <= LaneSteps; i++)
                points.Add(ToVector(PitLanePresentationMapper.SharedMergePose(
                    i / (float)LaneSteps, layout).Position));
            return points;
        }

        private static List<Vector2> SharedPitRoadPoints(IReadOnlyList<Vector2> entry,
            IReadOnlyList<Vector2> lane, IReadOnlyList<Vector2> merge)
        {
            var points = new List<Vector2>(entry.Count + lane.Count + merge.Count - 2);
            points.AddRange(entry);
            for (int i = 1; i < lane.Count; i++) points.Add(lane[i]);
            for (int i = 1; i < merge.Count; i++) points.Add(merge[i]);
            return points;
        }

        // Where the racing line crosses itself (a figure-8 course, issue #107
        // phase 4), the ribbon's paint order already builds the bridge: quads
        // append in lap order, so the strand driven LATER in the lap draws on
        // top. The deck dressing sells the over/under read: drop shadows just
        // outside the deck edges darken the strand passing underneath, and
        // parapet lines mark the deck's own edges across the crossing.
        public static IReadOnlyList<TrackCrossing> FindCrossings(TrackDefinition track)
        {
            var crossings = new List<TrackCrossing>();
            IReadOnlyList<TrackSegment> segments = track.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 2; j < segments.Count; j++)
                {
                    if (i == 0 && j == segments.Count - 1) continue;
                    if (TryIntersect(segments[i], segments[j], out Vector2 point))
                        crossings.Add(new TrackCrossing(i, j, point,
                            (new Vector2(segments[j].End.X, segments[j].End.Y) -
                             new Vector2(segments[j].Start.X, segments[j].Start.Y)).normalized));
                }
            }
            return crossings;
        }

        private static bool TryIntersect(TrackSegment a, TrackSegment b, out Vector2 point)
        {
            point = default;
            Vector2 p = new Vector2(a.Start.X, a.Start.Y), q = new Vector2(b.Start.X, b.Start.Y);
            Vector2 r = new Vector2(a.End.X, a.End.Y) - p, s = new Vector2(b.End.X, b.End.Y) - q;
            float denominator = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denominator) < 1e-6f) return false;
            Vector2 delta = q - p;
            float t = (delta.x * s.y - delta.y * s.x) / denominator;
            float u = (delta.x * r.y - delta.y * r.x) / denominator;
            if (t <= 0f || t >= 1f || u <= 0f || u >= 1f) return false;
            point = p + r * t;
            return true;
        }

        private const float CrossingDeckReach = 80f;

        private static void AppendCrossingDeck(SurfaceMeshData mesh, TrackCrossing crossing,
            RaceSurfaceStyle style)
        {
            // Strips run along the LATER (bridge) strand, centered on the
            // crossing: shadows a few px outside the deck edges, parapets just
            // inside them. Both draw after the closed ribbons, over each strand.
            foreach ((float offset, float width, Color color) in new[]
            {
                (TrackWidth * .5f + 6f, 10f, style.CrossingShadowColor),
                (-(TrackWidth * .5f + 6f), 10f, style.CrossingShadowColor),
                (TrackWidth * .5f - 2f, 3f,
                    style.StripeVisible ? style.StripeColor : Color.clear),
                (-(TrackWidth * .5f - 2f), 3f,
                    style.StripeVisible ? style.StripeColor : Color.clear),
            })
            {
                if (color.a <= 0f) continue;
                Vector2 direction = crossing.LaterDirection;
                var normal = new Vector2(-direction.y, direction.x);
                Vector2 center = crossing.Point + normal * offset;
                Vector2 along = direction * CrossingDeckReach;
                Vector2 across = normal * (width * .5f);
                mesh.AddQuad(center - along - across, center + along - across,
                    center + along + across, center - along + across, color);
            }
        }

        // Signed cross-track position: how far the point sits on the interior
        // side of the authored centerline (negative = across it, toward the
        // outside of the loop). The pit complex lives on the interior.
        internal static float InteriorOffset(Vector2 point, TrackDefinition track)
        {
            NearestCenterline(point, track, out Vector2 nearest, out Vector2 interiorNormal);
            return Vector2.Dot(point - nearest, interiorNormal);
        }

        // Nearest point on the authored centerline polyline plus the unit
        // normal of its chord pointing at the loop interior — travel is
        // clockwise in Y-down screen space, so the interior is 90° left of the
        // chord direction.
        private static void NearestCenterline(Vector2 point, TrackDefinition track,
            out Vector2 nearest, out Vector2 interiorNormal)
        {
            float best = float.MaxValue;
            nearest = default;
            interiorNormal = default;
            foreach (TrackSegment segment in track.Segments)
            {
                var start = new Vector2(segment.Start.X, segment.Start.Y);
                var end = new Vector2(segment.End.X, segment.End.Y);
                Vector2 direction = end - start;
                float t = Mathf.Clamp01(Vector2.Dot(point - start, direction) / direction.sqrMagnitude);
                Vector2 candidate = start + direction * t;
                float distance = Vector2.Distance(point, candidate);
                if (distance >= best) continue;
                best = distance;
                nearest = candidate;
                Vector2 unit = direction.normalized;
                interiorNormal = new Vector2(-unit.y, unit.x);
            }
        }

        // An axis-free rect: 2·halfLength along `along`, 2·halfWidth across it.
        private static void AppendOrientedRect(SurfaceMeshData mesh, Vector2 center, Vector2 along,
            float halfLength, float halfWidth, Color color)
        {
            Vector2 u = along * halfLength;
            Vector2 n = new Vector2(-along.y, along.x) * halfWidth;
            mesh.AddQuad(center - u - n, center + u - n, center + u + n, center - u + n, color);
        }

        private static void AppendOrientedRectOutline(SurfaceMeshData mesh, Vector2 center,
            Vector2 along, float halfLength, float halfWidth, float thickness, Color color)
        {
            Vector2 n = new Vector2(-along.y, along.x);
            float inset = thickness * .5f;
            AppendOrientedRect(mesh, center - n * (halfWidth - inset), along, halfLength, inset, color);
            AppendOrientedRect(mesh, center + n * (halfWidth - inset), along, halfLength, inset, color);
            AppendOrientedRect(mesh, center - along * (halfLength - inset), along, inset, halfWidth, color);
            AppendOrientedRect(mesh, center + along * (halfLength - inset), along, inset, halfWidth, color);
        }

        // Car visuals are centered on the origin so the renderer moves them by
        // transform, nose along +X. 54 long × 26 wide (owner direction
        // 2026-07-23, narrowed twice from the square 54×54 IMGUI footprint:
        // 30 made a pair fit the 64 px ribbon, 26 gives the duel visible
        // daylight — 6 px tucked instead of 2 — and frees breath budget):
        // car proportions make the heading rotation and drift angle read.
        // Direction E supplies the art, while these constants remain the
        // simulation and layout contract.
        public const float CarBodyHalfSize = 27f;
        public const float CarBodyHalfWidth = 13f;
        public const float CarBodyCornerRadius = 8f;

        // These values are the proven deterministic clearance contract from
        // the placeholder bodies. Direction E changes only presentation, so
        // the simulation keeps that conservative envelope unchanged.
        private const float PairBoxAlong =
            (CarBodyHalfSize - CarBodyCornerRadius) + (CarBodyHalfSize - CarBodyHalfWidth);
        private const float PairBoxAcross =
            (CarBodyHalfWidth - CarBodyCornerRadius) + (CarBodyHalfWidth - CarBodyHalfWidth);
        private const float PairRadii = CarBodyCornerRadius + CarBodyHalfWidth;

        public static float BodyClearance(float alongGap, float acrossGap)
        {
            float dx = Mathf.Max(Mathf.Abs(alongGap) - PairBoxAlong, 0f);
            float dy = Mathf.Max(Mathf.Abs(acrossGap) - PairBoxAcross, 0f);
            return Mathf.Sqrt(dx * dx + dy * dy) - PairRadii;
        }

        // Centripetal parameterization (alpha = .5), Barry-Goldman evaluation.
        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t0 = 0f;
            float t1 = t0 + Knot(p0, p1);
            float t2 = t1 + Knot(p1, p2);
            float t3 = t2 + Knot(p2, p3);
            float u = Mathf.Lerp(t1, t2, t);
            Vector2 a1 = Blend(p0, p1, t0, t1, u);
            Vector2 a2 = Blend(p1, p2, t1, t2, u);
            Vector2 a3 = Blend(p2, p3, t2, t3, u);
            Vector2 b1 = Blend(a1, a2, t0, t2, u);
            Vector2 b2 = Blend(a2, a3, t1, t3, u);
            return Blend(b1, b2, t1, t2, u);
        }

        private static float Knot(Vector2 from, Vector2 to) =>
            Mathf.Max(1e-4f, Mathf.Sqrt(Vector2.Distance(from, to)));

        private static Vector2 Blend(Vector2 from, Vector2 to, float tFrom, float tTo, float u) =>
            Vector2.LerpUnclamped(from, to, (u - tFrom) / (tTo - tFrom));

        private static Vector2 ToVector(Vec2 value) => new Vector2(value.X, value.Y);
    }

    internal sealed partial class RaceSurfaceRenderer : MonoBehaviour
    {
        // Cars sit one unit nearer the camera than the surface (z = 0), so the
        // transparent queue draws them over the pit boxes — the order the IMGUI
        // painter had.
        private const float CarDepth = -1f;

        public const string CourseSurfaceShaderName = "BoardRacing/CourseSurface";

        private static readonly int GroundTexId = Shader.PropertyToID("_GroundTex");
        private static readonly int RoadTexId = Shader.PropertyToID("_RoadTex");
        private static readonly int ShoulderTexId = Shader.PropertyToID("_ShoulderTex");
        private static readonly int GroundTileId = Shader.PropertyToID("_GroundTile");
        private static readonly int RoadTileId = Shader.PropertyToID("_RoadTile");
        private static readonly int ShoulderTileId = Shader.PropertyToID("_ShoulderTile");
        private static readonly int GroundTintId = Shader.PropertyToID("_GroundTint");
        private static readonly int RoadTintId = Shader.PropertyToID("_RoadTint");
        private static readonly int ShoulderTintId = Shader.PropertyToID("_ShoulderTint");
        private static readonly int GroundOnId = Shader.PropertyToID("_GroundOn");
        private static readonly int RoadOnId = Shader.PropertyToID("_RoadOn");
        private static readonly int ShoulderOnId = Shader.PropertyToID("_ShoulderOn");
        private static readonly int DetailStrengthId = Shader.PropertyToID("_DetailStrength");

        private Material material;
        private Material carMaterial;
        private Texture2D responseTexture;
        private Sprite responseSprite;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Sprite> sprites = new List<Sprite>();
        private readonly Dictionary<PlayerId, Transform> cars =
            new Dictionary<PlayerId, Transform>();
        private readonly Dictionary<PlayerId, CarResponseRig> carResponses =
            new Dictionary<PlayerId, CarResponseRig>();
        private Camera surfaceCamera;
        private MeshFilter surfaceFilter;
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
        private MeshFilter wireframeFilter;
        private readonly Dictionary<PlayerId, Transform> studyCars =
            new Dictionary<PlayerId, Transform>();
        private readonly Dictionary<PlayerId, CarResponseRig> studyResponses =
            new Dictionary<PlayerId, CarResponseRig>();
        private CarStudyPresentation carStudy = CarStudyPresentation.Live;
#endif
        private bool carsVisible = true;

        public static RaceSurfaceRenderer Create(SurfaceMeshData data)
            => Create(data, RaceSurfaceStyle.Default);

        public static RaceSurfaceRenderer Create(SurfaceMeshData data, Color groundColor)
        {
            RaceSurfaceStyle style = RaceSurfaceStyle.Default;
            style.GroundColor = groundColor;
            return Create(data, style);
        }

        public static RaceSurfaceRenderer Create(SurfaceMeshData data, RaceSurfaceStyle style)
        {
            Color groundColor = style.GroundColor;
            var root = new GameObject("Board Racing Race Surface");
            var surface = root.AddComponent<RaceSurfaceRenderer>();

            var cameraObject = new GameObject("Race Surface Camera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            surface.surfaceCamera = cameraObject.AddComponent<Camera>();
            surface.surfaceCamera.orthographic = true;
            surface.surfaceCamera.clearFlags = CameraClearFlags.SolidColor;
            surface.surfaceCamera.backgroundColor = groundColor;
            surface.surfaceCamera.nearClipPlane = .3f;
            surface.surfaceCamera.farClipPlane = 50f;
            // Pin the projection to the reference rect, top-left origin and Y
            // down: world space is exactly RaceLayout's 1920×1080 pixel space,
            // and the image stretches with the backbuffer the same way the
            // IMGUI scale matrix does. Assigned explicitly, so Unity's aspect
            // handling never rewrites it.
            surface.surfaceCamera.projectionMatrix = Matrix4x4.Ortho(
                0f, RaceLayout.ReferenceWidth, RaceLayout.ReferenceHeight, 0f, .3f, 50f);

            // An instance of the committed material when the theme supplies
            // one; otherwise the sprite material this replaced, so a project
            // without the presentation assets still renders the flat treatment
            // instead of magenta.
            surface.material = style.SurfaceMaterial != null
                ? new Material(style.SurfaceMaterial)
                : new Material(Shader.Find(CourseSurfaceShaderName)
                    ?? Shader.Find("Sprites/Default"));
            surface.carMaterial = new Material(
                Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                ?? Shader.Find("Sprites/Default"));
            surface.CreateResponseSprite();
            surface.ApplyStyle(style);
            Transform surfaceObject = surface.CreateMeshObject("Race Surface Mesh", data);
            surface.surfaceFilter = surfaceObject.GetComponent<MeshFilter>();
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
            Transform wireframeObject = surface.CreateWireframeObject(data);
            surface.wireframeFilter = wireframeObject.GetComponent<MeshFilter>();
            wireframeObject.gameObject.SetActive(false);
#endif
            return surface;
        }

        public void ReplaceSurface(SurfaceMeshData data, Color groundColor)
        {
            RaceSurfaceStyle style = RaceSurfaceStyle.Default;
            style.GroundColor = groundColor;
            ReplaceSurface(data, style);
        }

        public void ReplaceSurface(SurfaceMeshData data, RaceSurfaceStyle style)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            ReplaceOwnedMesh(surfaceFilter, CreateMesh("Race Surface Mesh", data));
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
            ReplaceOwnedMesh(wireframeFilter, CreateWireframeMesh(data));
#endif
            ApplyStyle(style);
        }

        // Detail lives on a material instance owned by this renderer, so Visual
        // Lab tuning never writes back into the committed theme asset.
        private void ApplyStyle(RaceSurfaceStyle style)
        {
            surfaceCamera.backgroundColor = style.GroundColor;
            if (material == null || !material.HasProperty(DetailStrengthId)) return;
            // The tiles carry color now, so an unbound slot cannot be made a
            // no-op by binding a neutral texture: the shader is told per surface
            // whether a tile exists and falls back to flat vertex color where
            // one does not.
            material.SetTexture(GroundTexId, style.GroundDetail ?? Texture2D.whiteTexture);
            material.SetTexture(RoadTexId, style.RoadDetail ?? Texture2D.whiteTexture);
            material.SetTexture(ShoulderTexId, style.ShoulderDetail ?? Texture2D.whiteTexture);
            material.SetFloat(GroundOnId, style.GroundDetail != null ? 1f : 0f);
            material.SetFloat(RoadOnId, style.RoadDetail != null ? 1f : 0f);
            material.SetFloat(ShoulderOnId, style.ShoulderDetail != null ? 1f : 0f);
            material.SetColor(GroundTintId, style.GroundDetailTint);
            material.SetColor(RoadTintId, style.RoadDetailTint);
            material.SetColor(ShoulderTintId, style.ShoulderDetailTint);
            material.SetFloat(GroundTileId, Mathf.Max(1f, style.GroundDetailTile));
            material.SetFloat(RoadTileId, Mathf.Max(1f, style.RoadDetailTile));
            material.SetFloat(ShoulderTileId, Mathf.Max(1f, style.ShoulderDetailTile));
            material.SetFloat(DetailStrengthId, Mathf.Clamp01(style.DetailStrength));
        }

        public void AttachCar(PlayerId playerId, PieceIdentity identity)
        {
            Transform car = CreateCar("Race Car " + playerId, identity,
                out CarResponseRig response);
            carResponses[playerId] = response;
            cars[playerId] = car;
            ApplyCarVisibility();
        }

        private Transform CreateCar(string objectName, PieceIdentity identity,
            out CarResponseRig response)
        {
            var carObject = new GameObject(objectName);
            Transform car = carObject.transform;
            car.SetParent(transform, false);
            AddSpriteLayer(car, "Contact Shadow", DirectionECarVisual.LoadContactShadow(),
                DirectionECarVisual.ShadowPixelsPerUnit, 1);

            var bodyObject = new GameObject("Body Response");
            Transform body = bodyObject.transform;
            body.SetParent(car, false);
            AddSpriteLayer(body, "Direction E Body", DirectionECarVisual.LoadBody(identity),
                DirectionECarVisual.BodyPixelsPerUnit, 2);
            response = CreateCarResponseRig(car, body);
            return car;
        }

        private SpriteRenderer AddSpriteLayer(Transform parent, string layerName, Texture2D texture,
            float pixelsPerUnit, int sortingOrder)
        {
            Sprite sprite = Sprite.Create(texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(.5f, .5f), pixelsPerUnit, 0,
                SpriteMeshType.FullRect, Vector4.zero, false);
            sprite.name = layerName;
            sprites.Add(sprite);

            var layer = new GameObject(layerName);
            layer.transform.SetParent(parent, false);
            var spriteRenderer = layer.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            ConfigureSpriteRenderer(spriteRenderer, sortingOrder);
            return spriteRenderer;
        }

        private SpriteRenderer AddResponseLayer(Transform parent, string layerName,
            int sortingOrder)
        {
            var layer = new GameObject(layerName);
            layer.transform.SetParent(parent, false);
            var spriteRenderer = layer.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = responseSprite;
            ConfigureSpriteRenderer(spriteRenderer, sortingOrder);
            spriteRenderer.enabled = false;
            return spriteRenderer;
        }

        private void ConfigureSpriteRenderer(SpriteRenderer spriteRenderer, int sortingOrder)
        {
            spriteRenderer.sharedMaterial = carMaterial;
            spriteRenderer.sortingOrder = sortingOrder;
            spriteRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            spriteRenderer.receiveShadows = false;
            spriteRenderer.lightProbeUsage =
                UnityEngine.Rendering.LightProbeUsage.Off;
            spriteRenderer.reflectionProbeUsage =
                UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private CarResponseRig CreateCarResponseRig(Transform car, Transform body)
        {
            return new CarResponseRig(body,
                AddResponseLayer(car, "Drive Exhaust", 3),
                AddResponseLayer(car, "Brake Cue Left", 4),
                AddResponseLayer(car, "Brake Cue Right", 4),
                AddResponseLayer(car, "Boost Flare", 3),
                AddResponseLayer(car, "Boost Core", 4),
                AddResponseLayer(car, "Boost Streak Left", 3),
                AddResponseLayer(car, "Boost Streak Center", 3),
                AddResponseLayer(car, "Boost Streak Right", 3),
                AddResponseLayer(car, "Corner Contact Left", 3),
                AddResponseLayer(car, "Corner Contact Right", 3));
        }

        private void CreateResponseSprite()
        {
            const int Size = 32;
            responseTexture = new Texture2D(Size, Size, TextureFormat.RGBA32,
                false, true)
            {
                name = "Car Response Soft Disc",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float nx = (x + .5f) / Size * 2f - 1f;
                    float ny = (y + .5f) / Size * 2f - 1f;
                    float edge = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny));
                    byte alpha = (byte)Mathf.RoundToInt(255f *
                        Mathf.SmoothStep(0f, 1f, edge));
                    pixels[y * Size + x] = new Color32(255, 255, 255, alpha);
                }
            responseTexture.SetPixels32(pixels);
            responseTexture.Apply(false, true);
            responseSprite = Sprite.Create(responseTexture,
                new Rect(0f, 0f, Size, Size), new Vector2(.5f, .5f), 1f, 0,
                SpriteMeshType.FullRect, Vector4.zero, false);
            responseSprite.name = "Car Response Soft Disc";
            sprites.Add(responseSprite);
        }

        public void SetCarResponse(PlayerId playerId, CarResponseState state, float pulse)
        {
            if (carResponses.TryGetValue(playerId, out CarResponseRig response))
                response.Apply(state, Mathf.Clamp01(pulse));
        }

        public void SetCarsVisible(bool visible)
        {
            carsVisible = visible;
            ApplyCarVisibility();
        }

        private void ApplyCarVisibility()
        {
            foreach (Transform car in cars.Values)
                if (car != null)
                    car.gameObject.SetActive(carsVisible
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
                        && !carStudy.Enabled
#endif
                    );
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
            foreach (Transform car in studyCars.Values)
                if (car != null)
                    car.gameObject.SetActive(carsVisible && carStudy.Enabled);
#endif
        }

#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
        public void SetCarStudy(CarStudyPresentation presentation)
        {
            carStudy = presentation;
            if (presentation.Enabled) EnsureStudyCars();
            ApplyCarVisibility();
            if (!presentation.Enabled) return;

            foreach (PlayerId playerId in StudyPlayerIds)
            {
                CarResponseState response = presentation.AppliesTo(playerId)
                    ? presentation.Response : CarResponseState.Still;
                Transform car = studyCars[playerId];
                float brake = response.Brake;
                float corner = response.Corner;
                car.localPosition = new Vector3(
                    560f + ((int)playerId - 1) * 220f, 540f, CarDepth);
                car.localRotation = Quaternion.Euler(
                    0f, 0f, CornerCharacter.MaxDriftDegrees * corner);
                car.localScale = new Vector3(
                    1f - CornerCharacter.DiveSquash * brake,
                    1f + CornerCharacter.DiveSquash * .5f * brake, 1f);
                studyResponses[playerId].Apply(response, .55f);
            }
        }

        private void EnsureStudyCars()
        {
            if (studyCars.Count != 0) return;
            foreach (PlayerId playerId in StudyPlayerIds)
            {
                PieceIdentity identity = PhysicalPieceCatalog.All[(int)playerId - 1];
                Transform car = CreateCar("Visual Lab Study Car " + playerId,
                    identity, out CarResponseRig response);
                studyCars.Add(playerId, car);
                studyResponses.Add(playerId, response);
            }
        }

        internal int StudyCarCount => studyCars.Count;
        internal Transform StudyCar(PlayerId playerId) =>
            studyCars.TryGetValue(playerId, out Transform car) ? car : null;

        public void SetWireframeVisible(bool visible)
        {
            if (wireframeFilter != null) wireframeFilter.gameObject.SetActive(visible);
        }

        private static readonly PlayerId[] StudyPlayerIds =
        {
            PlayerId.Player1,
            PlayerId.Player2,
            PlayerId.Player3,
            PlayerId.Player4
        };
#endif

        // Reference-pixel position (Y down), straight onto the transform — world
        // space is reference space by the camera's projection. Rotation turns
        // the body's nose (+X in the mesh) onto the heading: a rotation by the
        // heading's own atan2 angle maps local (1,0) to the heading vector in
        // reference coordinates, so the Y flip needs no special casing. Scale
        // is (along heading, across it) — the brake-dive squash (issue #117).
        public void SetCarPose(PlayerId playerId, Vector2 referencePosition,
            float rotationDegrees, Vector2 scale)
        {
            if (!cars.TryGetValue(playerId, out Transform car)) return;
            car.localPosition = new Vector3(referencePosition.x, referencePosition.y, CarDepth);
            car.localRotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            car.localScale = new Vector3(scale.x, scale.y, 1f);
        }

        private sealed class CarResponseRig
        {
            private static readonly Color DriveSmoke =
                new Color(.72f, .76f, .78f, 1f);
            private static readonly Color BrakeRed =
                new Color(1f, .16f, .04f, 1f);
            private static readonly Color BoostOrange =
                new Color(1f, .34f, .04f, 1f);
            private static readonly Color BoostIvory =
                new Color(1f, .9f, .58f, 1f);
            private static readonly Color TireChirp =
                new Color(.92f, .86f, .7f, 1f);

            private readonly Transform body;
            private readonly SpriteRenderer driveExhaust;
            private readonly SpriteRenderer brakeLeft, brakeRight;
            private readonly SpriteRenderer boostFlare, boostCore;
            private readonly SpriteRenderer boostLeft, boostCenter, boostRight;
            private readonly SpriteRenderer cornerLeft, cornerRight;

            public CarResponseRig(Transform body, SpriteRenderer driveExhaust,
                SpriteRenderer brakeLeft, SpriteRenderer brakeRight,
                SpriteRenderer boostFlare, SpriteRenderer boostCore,
                SpriteRenderer boostLeft, SpriteRenderer boostCenter,
                SpriteRenderer boostRight, SpriteRenderer cornerLeft,
                SpriteRenderer cornerRight)
            {
                this.body = body;
                this.driveExhaust = driveExhaust;
                this.brakeLeft = brakeLeft;
                this.brakeRight = brakeRight;
                this.boostFlare = boostFlare;
                this.boostCore = boostCore;
                this.boostLeft = boostLeft;
                this.boostCenter = boostCenter;
                this.boostRight = boostRight;
                this.cornerLeft = cornerLeft;
                this.cornerRight = cornerRight;
            }

            public void Apply(CarResponseState state, float pulse)
            {
                // The root carries truthful position/heading and the established
                // corner/brake attitude. Only the authored body child strains.
                float motorBeat = state.Drive * (.002f + .003f * pulse);
                body.localScale = new Vector3(
                    1f + .045f * state.Boost + motorBeat,
                    1f - .022f * state.Boost - motorBeat * .5f, 1f);

                Set(driveExhaust, new Vector2(-31f - pulse * 2f, 0f),
                    new Vector2(9f + pulse * 3f, 4.5f + pulse),
                    DriveSmoke, state.Drive * (.42f + .18f * pulse));

                Set(brakeLeft, new Vector2(-27f, -7.5f), new Vector2(9f, 5f),
                    BrakeRed, state.Brake);
                Set(brakeRight, new Vector2(-27f, 7.5f), new Vector2(9f, 5f),
                    BrakeRed, state.Brake);

                Set(boostFlare, new Vector2(-31f - pulse, 0f),
                    new Vector2(15f + pulse * 3f, 6.5f),
                    BoostOrange, state.Boost * (.78f + .20f * pulse));
                Set(boostCore, new Vector2(-27f, 0f), new Vector2(7f, 2.8f),
                    BoostIvory, state.Boost);
                Set(boostLeft, new Vector2(-36f, -6.5f), new Vector2(12f, 2.1f),
                    BoostIvory, state.Boost * .82f);
                Set(boostCenter, new Vector2(-39f, 0f), new Vector2(16f, 2.3f),
                    BoostIvory, state.Boost * .94f);
                Set(boostRight, new Vector2(-36f, 6.5f), new Vector2(12f, 2.1f),
                    BoostIvory, state.Boost * .82f);

                Set(cornerLeft, new Vector2(-22f, -10f), new Vector2(11f, 2.5f),
                    TireChirp, state.Corner * (.72f + .18f * pulse));
                Set(cornerRight, new Vector2(-22f, 10f), new Vector2(11f, 2.5f),
                    TireChirp, state.Corner * (.72f + .18f * (1f - pulse)));
            }

            private static void Set(SpriteRenderer renderer, Vector2 position,
                Vector2 size, Color color, float opacity)
            {
                float alpha = Mathf.Clamp01(opacity);
                renderer.enabled = alpha > .002f;
                if (!renderer.enabled) return;
                renderer.transform.localPosition =
                    new Vector3(position.x, position.y, 0f);
                renderer.transform.localScale =
                    new Vector3(size.x / 32f, size.y / 32f, 1f);
                color.a = alpha;
                renderer.color = color;
            }
        }

        private Transform CreateMeshObject(string objectName, SurfaceMeshData data)
        {
            var meshObject = new GameObject(objectName);
            meshObject.transform.SetParent(transform, false);
            Mesh mesh = CreateMesh(objectName, data);
            meshes.Add(mesh);
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            ConfigureMeshRenderer(meshObject);
            return meshObject.transform;
        }

#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
        private Transform CreateWireframeObject(SurfaceMeshData data)
        {
            var meshObject = new GameObject("Race Surface Wireframe");
            meshObject.transform.SetParent(transform, false);
            // Between the surface (0) and cars (-1): the topology overlays the
            // filled course while every racer remains readable above it.
            meshObject.transform.localPosition = new Vector3(0f, 0f, -.5f);
            Mesh mesh = CreateWireframeMesh(data);
            meshes.Add(mesh);
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            ConfigureMeshRenderer(meshObject);
            return meshObject.transform;
        }
#endif

        private void ConfigureMeshRenderer(GameObject meshObject)
        {
            var meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private static Mesh CreateMesh(string meshName, SurfaceMeshData data)
        {
            var mesh = new Mesh { name = meshName };
            if (data.Vertices.Count > ushort.MaxValue)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(data.Vertices);
            mesh.SetColors(data.Colors);
            // UV0 is the detail channel, not a texture coordinate: the shader
            // derives its own world-space UVs from vertex position, so this
            // carries (road, shoulder, strength) weights instead (issue #161).
            mesh.SetUVs(0, data.Details);
            mesh.SetTriangles(data.Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
        private static Mesh CreateWireframeMesh(SurfaceMeshData data)
        {
            var mesh = new Mesh { name = "Race Surface Wireframe" };
            if (data.Vertices.Count > ushort.MaxValue)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(data.Vertices);
            var colors = new List<Color>(data.Vertices.Count);
            for (int i = 0; i < data.Vertices.Count; i++)
                colors.Add(RaceSurfaceStyle.Default.RoadBoundaryColor);
            mesh.SetColors(colors);
            var lines = new List<int>(data.Triangles.Count * 2);
            for (int i = 0; i < data.Triangles.Count; i += 3)
            {
                int a = data.Triangles[i];
                int b = data.Triangles[i + 1];
                int c = data.Triangles[i + 2];
                lines.Add(a); lines.Add(b);
                lines.Add(b); lines.Add(c);
                lines.Add(c); lines.Add(a);
            }
            mesh.SetIndices(lines, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
#endif

        private void ReplaceOwnedMesh(MeshFilter filter, Mesh replacement)
        {
            Mesh previous = filter.sharedMesh;
            filter.sharedMesh = replacement;
            meshes.Add(replacement);
            meshes.Remove(previous);
            DestroyOwned(previous);
        }

        private static void DestroyOwned(UnityEngine.Object owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in meshes) DestroyOwned(mesh);
            foreach (Sprite sprite in sprites) DestroyOwned(sprite);
            DestroyOwned(responseTexture);
            DestroyOwned(carMaterial);
            DestroyOwned(material);
        }
    }
}
