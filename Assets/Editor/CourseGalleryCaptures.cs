using System.IO;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        CaptureAll(RaceSurfaceStyle.Default, OutputDirectory);
        EditorApplication.Exit(0);
    }

    // A repeatable non-play-mode review pass for issue #157's first useful
    // shoulder settings. It writes to the system temp directory so visual
    // inspection never promotes an experiment into committed course data.
    public static void RunShoulderReview()
    {
        RaceSurfaceStyle style = RaceSurfaceStyle.Default;
        style.ShoulderOpacity = .8f;
        style.ShoulderSolidWidth = 12f;
        style.ShoulderFeatherWidth = 24f;
        CaptureAll(style, Path.Combine(Path.GetTempPath(),
            "boardracing-course-surface-review"));
        EditorApplication.Exit(0);
    }

    // The committed theme (issue #161) rather than the flat fallback: the
    // phase-one review artifact for the approved Quarry direction. Separate
    // directory so the flat gallery stays the deterministic baseline.
    //   Unity -batchmode -projectPath . -executeMethod CourseGalleryCaptures.RunThemeReview
    public static void RunThemeReview()
    {
        CaptureAll(CourseSurfaceTheme.LoadStyleOrDefault(),
            "docs/captures/course-theme");
        EditorApplication.Exit(0);
    }

    private static void CaptureAll(RaceSurfaceStyle style, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        foreach (CourseDefinition course in CourseCatalog.All())
            Capture(course, style, outputDirectory);
    }

    // RenderTexture wants 1 for "no multisampling"; URP reports the sample
    // count directly and only ever uses 1, 2, 4, or 8.
    private static int PipelineMsaaSamples()
    {
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        return urp == null ? 1 : Mathf.Max(1, urp.msaaSampleCount);
    }

    private static void Capture(CourseDefinition course, RaceSurfaceStyle style,
        string outputDirectory)
    {
        SurfaceMeshData data = RaceSurfaceGeometry.Build(course.Track,
            PitLanePresentationLayout.ForCourse(course),
            new[] { new Color(.95f, .55f, .25f), new Color(.62f, .47f, .95f) }, style);
        RaceSurfaceRenderer surface = RaceSurfaceRenderer.Create(data, style);
        // Match the device (issue #165). A RenderTexture defaults to one sample
        // whatever the pipeline is set to, so without this the committed
        // captures keep aliased edges the Board no longer has and stop being
        // evidence of what ships. The URP asset is the authority here, not
        // QualitySettings.antiAliasing -- under URP that value is not what the
        // pipeline renders with, and it was observed out of step with the asset.
        var texture = new RenderTexture(1920, 1080, 24)
        {
            antiAliasing = PipelineMsaaSamples(),
        };
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
                Path.Combine(outputDirectory, course.Name.ToLowerInvariant() + ".png"),
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
