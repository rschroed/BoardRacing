// Opt-in, and off by default. The bloom settings below were chosen to load the
// post-processing path for measurement, not by anyone's eye, so leaving this on
// would put un-art-directed glow in every device build and quietly change what
// the next hardware test looks at. Build a measurement APK by adding
// BOARDRACING_BLOOM_PROBE to the Android scripting define symbols.
//
// Device development players only, for the same reasons as FrameTimeProbe — and
// one more that matters here: the #153 capture criterion requires the editor to
// render exactly what the Built-in baseline rendered, so the post pass must
// never exist during a capture run.
#if BOARDRACING_BLOOM_PROBE && DEVELOPMENT_BUILD && UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoardRacing.UrpSpike
{
    /// <summary>
    /// Turns on a full-screen bloom pass so the frame-time probe measures a
    /// loaded post-processing path rather than an idle one (issue #153). This
    /// is the load the render-pipeline decision actually rests on: URP without
    /// post costs about what Built-in costs, and the question is whether the
    /// effects Tranche 6 wants fit in the frame.
    ///
    /// Tuned to be plausible rather than tasteful. Bloom's cost is in running
    /// the pass at all, not in its threshold, so the numbers do not depend on
    /// the settings looking good.
    ///
    /// Lives in its own assembly so that URP is referenced by nothing the game
    /// compiles against, and an abort verdict is a folder delete.
    /// </summary>
    internal static class BloomLoadProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                Debug.Log("[BloomLoadProbe] Built-in pipeline active; no post pass to enable.");
                return;
            }

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>();
            bloom.active = true;
            // overrideState must be set per-parameter or the volume system keeps
            // the default and the pass never runs.
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.9f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 1f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.7f;

            var host = new GameObject("Board Racing Bloom Load Probe");
            Object.DontDestroyOnLoad(host);
            Volume volume = host.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.profile = profile;

            // A Volume alone renders nothing: post-processing is per-camera in
            // URP and defaults off on a camera created in script.
            int cameras = 0;
            foreach (Camera camera in Camera.allCameras)
            {
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                if (data == null) continue;
                data.renderPostProcessing = true;
                cameras++;
            }

            Debug.Log($"[BloomLoadProbe] bloom enabled on {cameras} camera(s). " +
                      "Note: the seat HUD is a ScreenSpaceOverlay canvas, which composites " +
                      "after post-processing and is therefore NOT bloomed.");
        }
    }
}
#endif
