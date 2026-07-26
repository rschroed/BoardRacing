using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Creates and assigns the URP 2D pipeline assets for the render-pipeline
// decision (issue #153). Scripted rather than clicked so the migration is
// reproducible from a clean checkout and so an abort is a branch delete rather
// than an unpicking of hand-made project settings.
//
//   Unity -quit -batchmode -nographics -projectPath . \
//     -executeMethod UrpSpikeSetup.Configure
//
// URP's own creation path (UniversalRenderPipelineAsset.CreateRendererAsset) is
// internal, so this replicates it: the renderer data must go through
// ResourceReloader or its shader and material references stay null and the
// pipeline renders nothing. The pipeline asset reloads its own resources inside
// Create(), so only the renderer data needs the explicit pass.
public static class UrpSpikeSetup
{
    private const string Directory = "Assets/Rendering";
    private const string RendererPath = Directory + "/BoardRacing2DRenderer.asset";
    private const string PipelinePath = Directory + "/BoardRacingUrpAsset.asset";

    public static void Configure()
    {
        if (!AssetDatabase.IsValidFolder(Directory))
            AssetDatabase.CreateFolder("Assets", "Rendering");

        var rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
        AssetDatabase.CreateAsset(rendererData, RendererPath);
        ResourceReloader.ReloadAllNullIn(rendererData, UniversalRenderPipelineAsset.packagePath);

        UniversalRenderPipelineAsset pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        AssetDatabase.CreateAsset(pipeline, PipelinePath);

        // Only the graphics default is set. Leaving every quality tier's
        // override null makes them all fall through to this one asset, so the
        // spike cannot accidentally measure one tier and ship another.
        GraphicsSettings.defaultRenderPipeline = pipeline;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[UrpSpikeSetup] pipeline={AssetDatabase.GetAssetPath(pipeline)} " +
                  $"renderer={AssetDatabase.GetAssetPath(rendererData)} " +
                  $"active={GraphicsSettings.currentRenderPipeline?.GetType().Name ?? "BuiltIn"}");
    }

    // Restores the Built-in pipeline and deletes the spike's assets, so an
    // abort verdict can be carried out without hand-editing project settings.
    public static void Revert()
    {
        GraphicsSettings.defaultRenderPipeline = null;
        AssetDatabase.DeleteAsset(PipelinePath);
        AssetDatabase.DeleteAsset(RendererPath);
        if (AssetDatabase.IsValidFolder(Directory)
            && System.IO.Directory.GetFiles(Directory).Length == 0)
            AssetDatabase.DeleteAsset(Directory);
        AssetDatabase.SaveAssets();
        Debug.Log("[UrpSpikeSetup] reverted to Built-in");
    }
}
