using System;
using System.Collections.Generic;
using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
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
        public readonly List<int> Triangles = new List<int>();

        // Corners a→b→c→d in screen order (any winding — the sprite material
        // does not cull); each quad owns its four vertices so per-quad color
        // stays hard-edged instead of interpolating across shared vertices.
        public void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int baseIndex = Vertices.Count;
            Vertices.Add(new Vector3(a.x, a.y, 0f));
            Vertices.Add(new Vector3(b.x, b.y, 0f));
            Vertices.Add(new Vector3(c.x, c.y, 0f));
            Vertices.Add(new Vector3(d.x, d.y, 0f));
            for (int i = 0; i < 4; i++) Colors.Add(color);
            Triangles.Add(baseIndex); Triangles.Add(baseIndex + 1); Triangles.Add(baseIndex + 2);
            Triangles.Add(baseIndex); Triangles.Add(baseIndex + 2); Triangles.Add(baseIndex + 3);
        }

        public void AddRect(Rect rect, Color color) => AddQuad(
            new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
            new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), color);

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
            int centerIndex = Vertices.Count;
            Vertices.Add(new Vector3(center.x, center.y, 0f));
            Colors.Add(color);
            int first = Vertices.Count;
            for (int i = 0; i < perimeter.Count; i++)
            {
                Vertices.Add(new Vector3(perimeter[i].x, perimeter[i].y, 0f));
                Colors.Add(color);
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

    internal static class RaceSurfaceGeometry
    {
        public const float TrackWidth = 64f;
        public const float TrackStripeWidth = 3f;
        public const float PitLaneWidth = 30f;
        public const float PitStripeWidth = 2f;
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
        public static readonly Color CrossingShadowColor = new Color(0f, 0f, 0f, .35f);
        // How far a junction ribbon may tuck under the track fill: deep enough
        // that rasterization can never open a background sliver along the seam,
        // shallow enough that the fill always covers it.
        public const float JunctionEdgeOverlap = 2f;
        private const int LaneSteps = 36;

        public static SurfaceMeshData Build(TrackDefinition track,
            PitLanePresentationLayout pitLayout, Color playerOneAccent, Color playerTwoAccent)
        {
            var mesh = new SurfaceMeshData();
            // The pit lane draws first, under the track fill, as one knot-shared
            // chain: pit line ~entry spline~> entry -> box row -> ~exit spline~>
            // rejoin. Where a leg meets the track, AppendJunctionRibbon clamps
            // its boundary to the track edge so the lane closes as a wedge
            // running along the edge — a slip-road gore whose seam IS the edge
            // (issue #107 phase 2; the round-2.2 full ribbon under the fill
            // crossed at ~40° and read as the lane vanishing under the track).
            List<Vector2> entry = EntryLanePoints(pitLayout);
            var serviceRow = new List<Vector2>
                { ToVector(pitLayout.PlayerOneBox), ToVector(pitLayout.PlayerTwoBox) };
            List<Vector2> merge = MergeLanePoints(pitLayout);
            foreach (float width in new[] { PitLaneWidth, PitStripeWidth })
            {
                Color color = width == PitLaneWidth ? PitLaneColor : PitStripeColor;
                AppendJunctionRibbon(mesh, entry, width, color, track);
                AppendOpenRibbon(mesh, serviceRow, width, color);
                AppendJunctionRibbon(mesh, merge, width, color, track);
            }
            List<CenterlineSample> centerline = SmoothCenterline(track, SamplesPerChord);
            AppendClosedRibbon(mesh, centerline, TrackWidth, CornerColor, StraightColor);
            AppendClosedRibbon(mesh, centerline, TrackStripeWidth, StripeColor, StripeColor);
            foreach (TrackCrossing crossing in FindCrossings(track))
                AppendCrossingDeck(mesh, crossing);
            // The start line and box quads follow the local travel direction —
            // horizontal pit straights were a Wedge special case; the phase-4b
            // courses pit on diagonals.
            TrackSegment first = track.Segments[0];
            Vector2 startDirection = (ToVector(first.End) - ToVector(first.Start)).normalized;
            AppendOrientedRect(mesh, ToVector(track.Sample(0f).Position), startDirection,
                12f, 28f, Color.white);
            Vector2 laneDirection =
                (ToVector(pitLayout.PlayerTwoBox) - ToVector(pitLayout.PlayerOneBox)).normalized;
            AppendPitBox(mesh, pitLayout.PlayerOneBox, laneDirection, playerOneAccent);
            AppendPitBox(mesh, pitLayout.PlayerTwoBox, laneDirection, playerTwoAccent);
            return mesh;
        }

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
                    samples[i].Corner ? cornerColor : straightColor);
            }
        }

        public static void AppendOpenRibbon(SurfaceMeshData mesh, IReadOnlyList<Vector2> points,
            float width, Color color)
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
                mesh.AddQuad(left[i], left[i + 1], right[i + 1], right[i], color);
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

        // A pit-lane leg that meets the track: an open ribbon whose boundary
        // vertices are clamped to at most JunctionEdgeOverlap inside the track
        // edge. Where the leg dives toward the centerline the near boundary
        // locks onto the edge line first and the far boundary follows, so the
        // lane closes as a wedge running along the edge instead of poking a
        // blunt end through it; past full absorption the ribbon degenerates to
        // a zero-width sliver under the fill. The clamp also means no lane
        // geometry can overhang into the roadway whatever the spline does.
        public static void AppendJunctionRibbon(SurfaceMeshData mesh, IReadOnlyList<Vector2> points,
            float width, Color color, TrackDefinition track)
        {
            int count = points.Count;
            if (count < 2) return;
            var left = new Vector2[count];
            var right = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = MiterOffset(points[Math.Max(0, i - 1)], points[i],
                    points[Math.Min(count - 1, i + 1)], width * .5f);
                left[i] = ClampOutsideRoadway(points[i] + offset, track);
                right[i] = ClampOutsideRoadway(points[i] - offset, track);
            }
            for (int i = 0; i < count - 1; i++)
                mesh.AddQuad(left[i], left[i + 1], right[i + 1], right[i], color);
        }

        // The drawn lane legs are the very splines the cars drive: entry along
        // Player1's entering spline (the players' paths only diverge past the
        // shared straight), merge along Player2's exiting spline (the lane
        // leaves the service row after the last box). A car in a pit phase is
        // therefore always over pavement.
        private static List<Vector2> EntryLanePoints(PitLanePresentationLayout layout)
        {
            var points = new List<Vector2>(LaneSteps + 1);
            for (int i = 0; i <= LaneSteps; i++)
                points.Add(ToVector(PitLanePresentationMapper.EntryPose(PlayerId.Player1,
                    i / (float)LaneSteps, false, layout).Position));
            return points;
        }

        private static List<Vector2> MergeLanePoints(PitLanePresentationLayout layout)
        {
            var points = new List<Vector2>(LaneSteps + 1);
            for (int i = 0; i <= LaneSteps; i++)
                points.Add(ToVector(PitLanePresentationMapper.ExitPose(PlayerId.Player2,
                    i / (float)LaneSteps, false, layout).Position));
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

        private static void AppendCrossingDeck(SurfaceMeshData mesh, TrackCrossing crossing)
        {
            // Strips run along the LATER (bridge) strand, centered on the
            // crossing: shadows a few px outside the deck edges, parapets just
            // inside them. Both draw after the closed ribbons, over each strand.
            foreach ((float offset, float width, Color color) in new[]
            {
                (TrackWidth * .5f + 6f, 10f, CrossingShadowColor),
                (-(TrackWidth * .5f + 6f), 10f, CrossingShadowColor),
                (TrackWidth * .5f - 2f, 3f, StripeColor),
                (-(TrackWidth * .5f - 2f), 3f, StripeColor),
            })
            {
                Vector2 direction = crossing.LaterDirection;
                var normal = new Vector2(-direction.y, direction.x);
                Vector2 center = crossing.Point + normal * offset;
                Vector2 along = direction * CrossingDeckReach;
                Vector2 across = normal * (width * .5f);
                mesh.AddQuad(center - along - across, center + along - across,
                    center + along + across, center - along + across, color);
            }
        }

        private static Vector2 ClampOutsideRoadway(Vector2 point, TrackDefinition track)
        {
            NearestCenterline(point, track, out Vector2 nearest, out Vector2 interiorNormal);
            float floor = TrackWidth * .5f - JunctionEdgeOverlap;
            return Vector2.Dot(point - nearest, interiorNormal) >= floor
                ? point
                : nearest + interiorNormal * floor;
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

        private static void AppendPitBox(SurfaceMeshData mesh, Vec2 center, Vector2 along,
            Color accent)
        {
            AppendOrientedRect(mesh, ToVector(center), along, 70f, 32f,
                new Color(accent.r, accent.g, accent.b, .22f));
            AppendOrientedRectOutline(mesh, ToVector(center), along, 70f, 32f, 3f, accent);
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

        // Car bodies, centered on the origin so the renderer moves them by
        // transform, nose along +X. 54 long × 26 wide (owner direction
        // 2026-07-23, narrowed twice from the square 54×54 IMGUI footprint:
        // 30 made a pair fit the 64 px ribbon, 26 gives the duel visible
        // daylight — 6 px tucked instead of 2 — and frees breath budget):
        // car proportions make the heading rotation and drift angle read.
        // P1 is a rounded rectangle (corner radius 8); P2 a capsule (the
        // corner radius grown to the half width).
        public const float CarBodyHalfSize = 27f;
        public const float CarBodyHalfWidth = 13f;
        public const float CarBodyCornerRadius = 8f;

        public static SurfaceMeshData BuildCarBody(PlayerId playerId, Color accent)
        {
            var mesh = new SurfaceMeshData();
            mesh.AddFan(Vector2.zero, RoundedPerimeter(CarBodyHalfSize, CarBodyHalfWidth,
                playerId == PlayerId.Player1 ? CarBodyCornerRadius : CarBodyHalfWidth), accent);
            // A darker cockpit wedge pointing along +X, the body's nose: now
            // that the bodies rotate to their heading and drift past it
            // (issue #117), both silhouettes need a visible front. Inside the
            // footprint, so nothing else moves.
            mesh.AddFan(new Vector2(11f, 0f), new List<Vector2>
            {
                new Vector2(23f, 0f), new Vector2(5f, -9f), new Vector2(5f, 9f)
            }, Color.Lerp(accent, Color.black, .45f));
            return mesh;
        }

        private static List<Vector2> RoundedPerimeter(float halfLength, float halfWidth,
            float cornerRadius, int segmentsPerCorner = 6)
        {
            float insetX = halfLength - cornerRadius, insetY = halfWidth - cornerRadius;
            var points = new List<Vector2>(4 * (segmentsPerCorner + 1));
            for (int corner = 0; corner < 4; corner++)
            {
                float baseAngle = corner * 90f;
                var center = new Vector2(
                    Mathf.Sign(Mathf.Cos((baseAngle + 45f) * Mathf.Deg2Rad)) * insetX,
                    Mathf.Sign(Mathf.Sin((baseAngle + 45f) * Mathf.Deg2Rad)) * insetY);
                for (int step = 0; step <= segmentsPerCorner; step++)
                {
                    float angle = (baseAngle + 90f * step / segmentsPerCorner) * Mathf.Deg2Rad;
                    points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * cornerRadius);
                }
            }
            return points;
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

    internal sealed class RaceSurfaceRenderer : MonoBehaviour
    {
        // Cars sit one unit nearer the camera than the surface (z = 0), so the
        // transparent queue draws them over the pit boxes — the order the IMGUI
        // painter had.
        private const float CarDepth = -1f;

        private Material material;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly Dictionary<PlayerId, Transform> cars =
            new Dictionary<PlayerId, Transform>();

        public static RaceSurfaceRenderer Create(SurfaceMeshData data)
        {
            var root = new GameObject("Board Racing Race Surface");
            var surface = root.AddComponent<RaceSurfaceRenderer>();

            var cameraObject = new GameObject("Race Surface Camera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            var surfaceCamera = cameraObject.AddComponent<Camera>();
            surfaceCamera.orthographic = true;
            surfaceCamera.clearFlags = CameraClearFlags.SolidColor;
            surfaceCamera.backgroundColor = RaceSurfaceGeometry.BackgroundColor;
            surfaceCamera.nearClipPlane = .3f;
            surfaceCamera.farClipPlane = 50f;
            // Pin the projection to the reference rect, top-left origin and Y
            // down: world space is exactly RaceLayout's 1920×1080 pixel space,
            // and the image stretches with the backbuffer the same way the
            // IMGUI scale matrix does. Assigned explicitly, so Unity's aspect
            // handling never rewrites it.
            surfaceCamera.projectionMatrix = Matrix4x4.Ortho(
                0f, RaceLayout.ReferenceWidth, RaceLayout.ReferenceHeight, 0f, .3f, 50f);

            surface.material = new Material(Shader.Find("Sprites/Default"));
            surface.CreateMeshObject("Race Surface Mesh", data);
            return surface;
        }

        public void AttachCar(PlayerId playerId, SurfaceMeshData body) =>
            cars[playerId] = CreateMeshObject("Race Car " + playerId, body);

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

        private Transform CreateMeshObject(string objectName, SurfaceMeshData data)
        {
            var meshObject = new GameObject(objectName);
            meshObject.transform.SetParent(transform, false);
            var mesh = new Mesh { name = objectName };
            mesh.SetVertices(data.Vertices);
            mesh.SetColors(data.Colors);
            mesh.SetTriangles(data.Triangles, 0);
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return meshObject.transform;
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
