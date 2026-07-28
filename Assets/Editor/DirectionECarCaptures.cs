using System;
using System.IO;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using UnityEditor;
using UnityEngine;

// Focused issue #175 evidence: the four approved player variants rendered by
// the production car path on the committed Quarry course treatment. This is a
// verification view, not a new course composition.
//   Unity -batchmode -projectPath . -executeMethod DirectionECarCaptures.Run
public static class DirectionECarCaptures
{
    private const string OutputDirectory = "docs/captures/car-direction-e";
    private static readonly Color[] Accents =
    {
        new Color(.92f, .39f, .12f),
        new Color(.48f, .28f, .72f),
        new Color(.88f, .18f, .52f),
        new Color(.96f, .73f, .12f)
    };

    public static void Run()
    {
        Directory.CreateDirectory(OutputDirectory);
        CourseDefinition course = CourseCatalog.Wedge();
        CaptureScenario(course, "01-Grid", (surface, track, pits) =>
        {
            PlacePair(surface, track, 45f, PlayerId.Player1, PlayerId.Player2);
            PlacePair(surface, track, 120f, PlayerId.Player3, PlayerId.Player4);
        });
        CaptureScenario(course, "02-StableRacing", (surface, track, pits) =>
        {
            PlacePair(surface, track, 470f, PlayerId.Player1, PlayerId.Player2);
            PlacePair(surface, track, 550f, PlayerId.Player3, PlayerId.Player4);
        });
        CaptureScenario(course, "03-CloseDuel", (surface, track, pits) =>
        {
            PlacePair(surface, track, 700f, PlayerId.Player1, PlayerId.Player2);
            PlaceAtTrack(surface, track, 590f, PlayerId.Player3, -9f);
            PlaceAtTrack(surface, track, 520f, PlayerId.Player4, 9f);
        });
        CaptureScenario(course, "04-TightCorner", (surface, track, pits) =>
        {
            PlaceAtTrack(surface, track, 2420f, PlayerId.Player1, -8f, 7f);
            PlaceAtTrack(surface, track, 2345f, PlayerId.Player2, 8f, -6f);
            PlaceAtTrack(surface, track, 2265f, PlayerId.Player3, -6f, 5f);
            PlaceAtTrack(surface, track, 2185f, PlayerId.Player4, 6f, -4f);
        });
        CaptureScenario(course, "05-Pit", (surface, track, pits) =>
        {
            for (int i = 0; i < PhysicalPieceCatalog.All.Length; i++)
                PlaceAt(surface, (PlayerId)(i + 1), pits.Boxes[i], Vector2.right);
        });
        CaptureScenario(course, "06-SplitFinish", (surface, track, pits) =>
        {
            PlacePair(surface, track, 15f, PlayerId.Player1, PlayerId.Player2);
            PlaceAt(surface, PlayerId.Player3, pits.Box(PlayerId.Player3), Vector2.right);
            PlaceAtTrack(surface, track, 2520f, PlayerId.Player4);
        });
        CaptureScenario(course, "07-Results", (surface, track, pits) =>
        {
            for (int i = 0; i < PhysicalPieceCatalog.All.Length; i++)
                PlaceAt(surface, (PlayerId)(i + 1), pits.Boxes[i], Vector2.right);
        });
        EditorApplication.Exit(0);
    }

    private static void CaptureScenario(CourseDefinition course, string name,
        Action<RaceSurfaceRenderer, TrackDefinition, PitLanePresentationLayout> arrange)
    {
        RaceSurfaceStyle style = CourseSurfaceTheme.LoadStyleOrDefault();
        PitLanePresentationLayout pits = PitLanePresentationLayout.ForCourse(course);
        SurfaceMeshData data =
            RaceSurfaceGeometry.Build(course.Track, pits, Accents, style);
        RaceSurfaceRenderer surface = RaceSurfaceRenderer.Create(data, style);
        try
        {
            for (int i = 0; i < PhysicalPieceCatalog.All.Length; i++)
                surface.AttachCar((PlayerId)(i + 1), PhysicalPieceCatalog.All[i]);
            arrange(surface, course.Track, pits);
            Capture(surface, Path.Combine(OutputDirectory,
                "car-direction-e-" + name + ".png"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(surface.gameObject);
        }
    }

    private static void PlacePair(RaceSurfaceRenderer surface, TrackDefinition track,
        float distance, PlayerId left, PlayerId right)
    {
        PlaceAtTrack(surface, track, distance, left, -15f);
        PlaceAtTrack(surface, track, distance, right, 15f);
    }

    private static void PlaceAtTrack(RaceSurfaceRenderer surface, TrackDefinition track,
        float distance, PlayerId playerId, float laneOffset = 0f, float yawDegrees = 0f)
    {
        TrackSample sample = track.Sample(distance);
        var tangent = new Vector2(sample.Tangent.X, sample.Tangent.Y);
        var normal = new Vector2(-tangent.y, tangent.x);
        var center = new Vector2(sample.Position.X, sample.Position.Y);
        PlaceAt(surface, playerId, center + normal * laneOffset, tangent, yawDegrees);
    }

    private static void PlaceAt(RaceSurfaceRenderer surface, PlayerId playerId,
        Vec2 position, Vector2 tangent, float yawDegrees = 0f) =>
        PlaceAt(surface, playerId, new Vector2(position.X, position.Y), tangent, yawDegrees);

    private static void PlaceAt(RaceSurfaceRenderer surface, PlayerId playerId,
        Vector2 center, Vector2 tangent, float yawDegrees = 0f)
    {
        float angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
        surface.SetCarPose(playerId, center, angle + yawDegrees, Vector2.one);
    }

    private static void Capture(RaceSurfaceRenderer surface, string outputPath)
    {
        var target = new RenderTexture(1920, 1080, 24) { antiAliasing = 1 };
        var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            Camera camera = surface.GetComponentInChildren<Camera>();
            target.Create();
            camera.targetTexture = target;
            // The first explicit URP render in a fresh batch editor initializes
            // pipeline resources. Render the evidence frame after that warm-up.
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, 1920f, 1080f), 0, 0);
            image.Apply();
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
        }
        finally
        {
            Camera camera = surface.GetComponentInChildren<Camera>();
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
