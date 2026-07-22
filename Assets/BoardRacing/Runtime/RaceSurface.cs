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
        private const int MergeLaneSteps = 36;

        public static SurfaceMeshData Build(TrackDefinition track,
            PitLanePresentationLayout pitLayout, Color playerOneAccent, Color playerTwoAccent)
        {
            var mesh = new SurfaceMeshData();
            List<CenterlineSample> centerline = SmoothCenterline(track, SamplesPerChord);
            AppendClosedRibbon(mesh, centerline, TrackWidth, CornerColor, StraightColor);
            AppendClosedRibbon(mesh, centerline, TrackStripeWidth, StripeColor, StripeColor);
            Vec2 line = track.Sample(0f).Position;
            mesh.AddRect(new Rect(line.X - 12f, line.Y - 28f, 24f, 56f), Color.white);
            foreach (float width in new[] { PitLaneWidth, PitStripeWidth })
            {
                Color color = width == PitLaneWidth ? PitLaneColor : PitStripeColor;
                AppendOpenRibbon(mesh, new List<Vector2> { ToVector(pitLayout.PitLine), ToVector(pitLayout.Entry) }, width, color);
                AppendOpenRibbon(mesh, new List<Vector2> { ToVector(pitLayout.Entry), ToVector(pitLayout.Exit) }, width, color);
                AppendOpenRibbon(mesh, MergeLanePoints(pitLayout, track), width, color);
            }
            AppendPitBox(mesh, pitLayout.PlayerOneBox, playerOneAccent);
            AppendPitBox(mesh, pitLayout.PlayerTwoBox, playerTwoAccent);
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

        // The drawn merge lane stops where the exit spline reaches the track's
        // inner edge and tucks underneath the fill: the spline itself continues
        // to the rejoin point ON the centerline (cars ride it all the way), but
        // a full-width ribbon there visibly pokes across the track (rounds 1+2
        // hardware review, #86).
        private static List<Vector2> MergeLanePoints(PitLanePresentationLayout layout,
            TrackDefinition track)
        {
            var points = new List<Vector2>(MergeLaneSteps + 1);
            for (int i = 0; i <= MergeLaneSteps; i++)
            {
                Vector2 point = ToVector(PitLanePresentationMapper.ExitPose(PlayerId.Player1,
                    i / (float)MergeLaneSteps, false, layout).Position);
                points.Add(point);
                if (points.Count > 1 && DistanceToCenterline(point, track) < TrackWidth * .5f)
                    break;
            }
            return points;
        }

        internal static float DistanceToCenterline(Vector2 point, TrackDefinition track)
        {
            float best = float.MaxValue;
            foreach (TrackSegment segment in track.Segments)
            {
                var start = new Vector2(segment.Start.X, segment.Start.Y);
                var end = new Vector2(segment.End.X, segment.End.Y);
                Vector2 direction = end - start;
                float t = Mathf.Clamp01(Vector2.Dot(point - start, direction) / direction.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(point, start + direction * t));
            }
            return best;
        }

        private static void AppendPitBox(SurfaceMeshData mesh, Vec2 center, Color accent)
        {
            var box = new Rect(center.X - 70f, center.Y - 32f, 140f, 64f);
            mesh.AddRect(box, new Color(accent.r, accent.g, accent.b, .22f));
            mesh.AddRectOutline(box, 3f, accent);
        }

        // Car bodies (issue #86 round 2), centered on the origin so the renderer
        // moves them by transform: the same 54×54 footprint the IMGUI pass drew —
        // P1 a rounded square (corner radius 8), P2 a disc (the 27 px "radius"
        // rounded the square into a circle).
        public const float CarBodyHalfSize = 27f;
        public const float CarBodyCornerRadius = 8f;

        public static SurfaceMeshData BuildCarBody(PlayerId playerId, Color accent)
        {
            var mesh = new SurfaceMeshData();
            mesh.AddFan(Vector2.zero, playerId == PlayerId.Player1
                ? RoundedRectPerimeter(CarBodyHalfSize, CarBodyCornerRadius)
                : DiscPerimeter(CarBodyHalfSize), accent);
            return mesh;
        }

        private static List<Vector2> DiscPerimeter(float radius, int segments = 48)
        {
            var points = new List<Vector2>(segments);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * 2f * Mathf.PI / segments;
                points.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            return points;
        }

        private static List<Vector2> RoundedRectPerimeter(float halfSize, float cornerRadius,
            int segmentsPerCorner = 6)
        {
            float inset = halfSize - cornerRadius;
            var points = new List<Vector2>(4 * (segmentsPerCorner + 1));
            for (int corner = 0; corner < 4; corner++)
            {
                float baseAngle = corner * 90f;
                var center = new Vector2(
                    Mathf.Sign(Mathf.Cos((baseAngle + 45f) * Mathf.Deg2Rad)) * inset,
                    Mathf.Sign(Mathf.Sin((baseAngle + 45f) * Mathf.Deg2Rad)) * inset);
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
        // space is reference space by the camera's projection.
        public void SetCarPose(PlayerId playerId, Vector2 referencePosition)
        {
            if (cars.TryGetValue(playerId, out Transform car))
                car.localPosition = new Vector3(referencePosition.x, referencePosition.y, CarDepth);
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
