using System;
using System.IO;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using UnityEditor;
using UnityEngine;

// Deterministic issue #176 evidence on the current Quarry course treatment.
//   Unity -batchmode -projectPath . -executeMethod CarResponseCaptures.Run
public static class CarResponseCaptures
{
    private const string OutputDirectory = "docs/captures/car-response";
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

        Capture(course, "01-Brake", (surface, track) =>
        {
            Place(surface, track, PlayerId.Player1, 550f, 0f, .88f, 1.06f);
            surface.SetCarResponse(PlayerId.Player1,
                new CarResponseState(0f, 1f, 0f, 0f), .25f);
        });
        Capture(course, "02-Drive", (surface, track) =>
        {
            Place(surface, track, PlayerId.Player2, 550f);
            surface.SetCarResponse(PlayerId.Player2,
                new CarResponseState(CarResponsePresentation.DriveIntensity, 0f, 0f, 0f), .8f);
        });
        Capture(course, "03-Boost", (surface, track) =>
        {
            Place(surface, track, PlayerId.Player3, 550f);
            surface.SetCarResponse(PlayerId.Player3,
                new CarResponseState(0f, 0f, 1f, 0f), .7f);
        });
        Capture(course, "04-FastCorner", (surface, track) =>
        {
            Place(surface, track, PlayerId.Player4, 2420f,
                CornerCharacter.MaxDriftDegrees);
            surface.SetCarResponse(PlayerId.Player4,
                new CarResponseState(0f, 0f, 0f, 1f), .35f);
        });
        Capture(course, "05-FourCarBoostCorner", (surface, track) =>
        {
            for (int i = 0; i < PhysicalPieceCatalog.All.Length; i++)
            {
                PlayerId id = (PlayerId)(i + 1);
                Place(surface, track, id, 2200f + i * 66f,
                    i % 2 == 0 ? CornerCharacter.MaxDriftDegrees :
                    -CornerCharacter.MaxDriftDegrees);
                surface.SetCarResponse(id,
                    new CarResponseState(0f, 0f, 1f, 1f), (i + 1) * .19f);
            }
        });
        EditorApplication.Exit(0);
    }

    private static void Capture(CourseDefinition course, string name,
        Action<RaceSurfaceRenderer, TrackDefinition> arrange)
    {
        RaceSurfaceStyle style = CourseSurfaceTheme.LoadStyleOrDefault();
        SurfaceMeshData data = RaceSurfaceGeometry.Build(course.Track,
            PitLanePresentationLayout.ForCourse(course), Accents, style);
        RaceSurfaceRenderer surface = RaceSurfaceRenderer.Create(data, style);
        try
        {
            for (int i = 0; i < PhysicalPieceCatalog.All.Length; i++)
                surface.AttachCar((PlayerId)(i + 1), PhysicalPieceCatalog.All[i]);
            surface.SetCarsVisible(false);
            arrange(surface, course.Track);
            // Only arranged cars are re-enabled below; this keeps each focused
            // state free of unrelated parked bodies.
            foreach (Transform car in surface.GetComponentsInChildren<Transform>(true))
                if (car.name.StartsWith("Race Car ", StringComparison.Ordinal) &&
                    car.localPosition.z == -1f)
                    car.gameObject.SetActive(car.localPosition.x != 0f ||
                        car.localPosition.y != 0f);
            Render(surface, Path.Combine(OutputDirectory,
                "car-response-" + name + ".png"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(surface.gameObject);
        }
    }

    private static void Place(RaceSurfaceRenderer surface, TrackDefinition track,
        PlayerId playerId, float distance, float yawDegrees = 0f,
        float alongScale = 1f, float acrossScale = 1f)
    {
        TrackSample sample = track.Sample(distance);
        Vec2 heading = TrackPresentation.SmoothHeading(track, distance);
        surface.SetCarPose(playerId,
            new Vector2(sample.Position.X, sample.Position.Y),
            Mathf.Atan2(heading.Y, heading.X) * Mathf.Rad2Deg + yawDegrees,
            new Vector2(alongScale, acrossScale));
        Transform car = surface.transform.Find("Race Car " + playerId);
        if (car != null) car.gameObject.SetActive(true);
    }

    private static void Render(RaceSurfaceRenderer surface, string outputPath)
    {
        var target = new RenderTexture(1920, 1080, 24) { antiAliasing = 1 };
        var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            Camera camera = surface.GetComponentInChildren<Camera>();
            target.Create();
            camera.targetTexture = target;
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
