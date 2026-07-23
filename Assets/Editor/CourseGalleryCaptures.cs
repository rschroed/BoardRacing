using System.IO;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using UnityEditor;
using UnityEngine;

// Renders every catalog course's bare racing surface to docs/captures/courses/
// at 1920×1080 — the course review artifact for issue #107 phase 4. Unlike
// BoardRacingCaptures this never enters play mode: the surface renderer is
// plain GameObjects, so each course renders straight to a RenderTexture.
//   Unity -batchmode -projectPath . -executeMethod CourseGalleryCaptures.Run
public static class CourseGalleryCaptures
{
    private const string OutputDirectory = "docs/captures/courses";

    public static void Run()
    {
        Directory.CreateDirectory(OutputDirectory);
        foreach (CourseDefinition course in CourseCatalog.All())
            Capture(course);
        EditorApplication.Exit(0);
    }

    private static void Capture(CourseDefinition course)
    {
        SurfaceMeshData data = RaceSurfaceGeometry.Build(course.Track,
            PitLanePresentationLayout.ForCourse(course),
            new Color(.95f, .55f, .25f), new Color(.62f, .47f, .95f));
        RaceSurfaceRenderer surface = RaceSurfaceRenderer.Create(data);
        var texture = new RenderTexture(1920, 1080, 24);
        var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            Camera camera = surface.GetComponentInChildren<Camera>();
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0f, 0f, 1920f, 1080f), 0, 0);
            image.Apply();
            File.WriteAllBytes(
                Path.Combine(OutputDirectory, course.Name.ToLowerInvariant() + ".png"),
                image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = null;
            Object.DestroyImmediate(surface.gameObject);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(image);
        }
    }
}
