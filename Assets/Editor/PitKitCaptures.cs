using System.IO;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using UnityEditor;
using UnityEngine;

// Deterministic 1920×1080 review evidence for issue #183. Every catalog course
// receives a quiet four-car composition; Wedge also receives a simultaneous
// occupied / servicing / ready / releasing state stress test.
//   Unity -batchmode -projectPath . -executeMethod PitKitCaptures.Run
public static class PitKitCaptures
{
    private const string OutputDirectory = "docs/captures/pit-kit";
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
        foreach (CourseDefinition course in CourseCatalog.All())
            CaptureQuietComplex(course);
        CaptureActiveStates(CourseCatalog.Wedge());
        EditorApplication.Exit(0);
    }

    private static void CaptureQuietComplex(CourseDefinition course)
    {
        RaceSurfaceRenderer surface = CreateSurface(course,
            out PitLanePresentationLayout pits);
        try
        {
            PlaceAllCars(surface, pits);
            var occupied = new RacerSnapshot[4];
            for (int i = 0; i < occupied.Length; i++)
                occupied[i] = Racer((PlayerId)(i + 1),
                    PitPhase.InService, PitService.None, 0f, 0);
            surface.SetPitPresentation(occupied, 1f, 1f / 60f);
            Render(surface, Path.Combine(OutputDirectory,
                course.Name.ToLowerInvariant() + "-quiet.png"));
        }
        finally
        {
            Object.DestroyImmediate(surface.gameObject);
        }
    }

    private static void CaptureActiveStates(CourseDefinition course)
    {
        RaceSurfaceRenderer surface = CreateSurface(course,
            out PitLanePresentationLayout pits);
        try
        {
            PlaceAllCars(surface, pits);
            var beforeCompletion = new[]
            {
                Racer(PlayerId.Player1, PitPhase.InService),
                Racer(PlayerId.Player2, PitPhase.InService, PitService.Tires, .55f),
                Racer(PlayerId.Player3, PitPhase.InService, PitService.Fuel, .8f),
                Racer(PlayerId.Player4, PitPhase.Exiting)
            };
            for (int frame = 0; frame < 60; frame++)
                surface.SetPitPresentation(beforeCompletion,
                    frame / 60f, 1f / 60f);

            var activeMoment = new[]
            {
                beforeCompletion[0],
                beforeCompletion[1],
                Racer(PlayerId.Player3, PitPhase.InService, PitService.Fuel, 1f, 1),
                beforeCompletion[3]
            };
            surface.SetPitPresentation(activeMoment, 1f, 1f / 60f);
            Render(surface, Path.Combine(OutputDirectory,
                "wedge-active-states.png"));
        }
        finally
        {
            Object.DestroyImmediate(surface.gameObject);
        }
    }

    private static RaceSurfaceRenderer CreateSurface(CourseDefinition course,
        out PitLanePresentationLayout pits)
    {
        RaceSurfaceStyle style = CourseSurfaceTheme.LoadStyleOrDefault();
        pits = PitLanePresentationLayout.ForCourse(course);
        SurfaceMeshData data = RaceSurfaceGeometry.Build(
            course.Track, pits, Accents, style);
        RaceSurfaceRenderer surface = RaceSurfaceRenderer.Create(data, style);
        var identities = new System.Collections.Generic.Dictionary<PlayerId, PieceIdentity>();
        for (int i = 0; i < PhysicalPieceCatalog.All.Length; i++)
        {
            PlayerId playerId = (PlayerId)(i + 1);
            PieceIdentity identity = PhysicalPieceCatalog.All[i];
            identities.Add(playerId, identity);
            surface.AttachCar(playerId, identity);
        }
        surface.AttachPitComplex(pits, identities);
        return surface;
    }

    private static void PlaceAllCars(RaceSurfaceRenderer surface,
        PitLanePresentationLayout pits)
    {
        for (int i = 0; i < pits.Stalls.Count; i++)
        {
            PlayerId playerId = (PlayerId)(i + 1);
            Vec2 center = pits.Box(playerId);
            Vec2 heading = pits.ParkedHeading(playerId);
            surface.SetCarPose(playerId,
                new Vector2(center.X, center.Y),
                Mathf.Atan2(heading.Y, heading.X) * Mathf.Rad2Deg,
                Vector2.one);
        }
    }

    private static RacerSnapshot Racer(PlayerId playerId, PitPhase phase,
        PitService service = PitService.None, float progress = 0f,
        int completed = 0) =>
        new RacerSnapshot(playerId, 0f, 0f, 0, (int)playerId,
            false, -1f,
            new TrackSample(default, new Vec2(1f, 0f), 0,
                TrackSectionKind.Straight, Pace.BasePace),
            0f, false, 0f, 0,
            new RacerConditionSnapshot(0f, 0f, false, false),
            new RacerPitSnapshot(service, phase, progress,
                completed, completed > 0));

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
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);
        }
    }
}
