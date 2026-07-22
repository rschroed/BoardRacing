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
                AppendOpenRibbon(mesh, MergeLanePoints(pitLayout), width, color);
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
        // The dot clamp caps the miter at twice the half width, which the ≤12°
        // authored steps never approach.
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
            return miter * (halfWidth / Mathf.Max(.5f, Vector2.Dot(miter, normalOutOf)));
        }

        private static Vector2 Direction(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            return delta.sqrMagnitude < 1e-8f ? Vector2.zero : delta.normalized;
        }

        private static List<Vector2> MergeLanePoints(PitLanePresentationLayout layout)
        {
            var points = new List<Vector2>(MergeLaneSteps + 1);
            for (int i = 0; i <= MergeLaneSteps; i++)
                points.Add(ToVector(PitLanePresentationMapper.ExitPose(PlayerId.Player1,
                    i / (float)MergeLaneSteps, false, layout).Position));
            return points;
        }

        private static void AppendPitBox(SurfaceMeshData mesh, Vec2 center, Color accent)
        {
            var box = new Rect(center.X - 70f, center.Y - 32f, 140f, 64f);
            mesh.AddRect(box, new Color(accent.r, accent.g, accent.b, .22f));
            mesh.AddRectOutline(box, 3f, accent);
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
        private Mesh mesh;
        private Material material;

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

            var meshObject = new GameObject("Race Surface Mesh");
            meshObject.transform.SetParent(root.transform, false);
            surface.mesh = new Mesh { name = "Race Surface" };
            surface.mesh.SetVertices(data.Vertices);
            surface.mesh.SetColors(data.Colors);
            surface.mesh.SetTriangles(data.Triangles, 0);
            surface.mesh.RecalculateBounds();
            meshObject.AddComponent<MeshFilter>().sharedMesh = surface.mesh;
            var meshRenderer = meshObject.AddComponent<MeshRenderer>();
            surface.material = new Material(Shader.Find("Sprites/Default"));
            meshRenderer.sharedMaterial = surface.material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return surface;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
