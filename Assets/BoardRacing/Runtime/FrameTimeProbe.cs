// Device development players only. In the editor the probe would measure the
// editor's own frame loop rather than the Board's, and its per-window Debug.Log
// is pure noise in the automated suites and an extra actor in the capture run.
#if DEVELOPMENT_BUILD && UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoardRacing.Runtime
{
    /// <summary>
    /// Development-only frame-time readout for the render-pipeline decision
    /// (issue #153). The #86 dev fps readout lived in the IMGUI seat overlay and
    /// left with it, so there is currently no way to answer "what does this cost
    /// on the Board" — which is the whole evidentiary basis of the URP spike.
    ///
    /// Percentiles, not an averaged fps counter: a mean hides exactly the stalls
    /// a full-screen post pass introduces. p95/p99 and the worst frame are what
    /// decide whether bloom is affordable.
    ///
    /// Reports the active pipeline on every line so a captured log identifies
    /// itself as baseline or URP without relying on when it was taken.
    /// Compiled out of release builds entirely.
    /// </summary>
    internal sealed class FrameTimeProbe : MonoBehaviour
    {
        // Long enough that one window spans several corners and a pit pass at
        // race pace; short enough to attribute a window to a race state by eye
        // while watching the log stream.
        private const float WindowSeconds = 5f;
        // targetFrameRate is set in RacePrototype.Awake and the first frames
        // after level load carry shader compilation and allocation spikes that
        // describe startup, not steady state.
        private const int WarmupFrames = 120;

        private static readonly float[] Samples = new float[2048];
        private static readonly float[] Sorted = new float[2048];

        private int count;
        private int warmup;
        private float elapsed;
        private int window;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var host = new GameObject("Board Racing Frame Time Probe");
            host.AddComponent<FrameTimeProbe>();
            DontDestroyOnLoad(host);
        }

        private void Update()
        {
            if (warmup < WarmupFrames) { warmup++; return; }

            // Unscaled: the probe measures wall-clock cost of producing a frame,
            // which must stay independent of any time scaling the race applies.
            float milliseconds = Time.unscaledDeltaTime * 1000f;
            if (count < Samples.Length) Samples[count++] = milliseconds;
            elapsed += Time.unscaledDeltaTime;
            if (elapsed < WindowSeconds || count == 0) return;

            Report();
            count = 0;
            elapsed = 0f;
        }

        private void Report()
        {
            Array.Copy(Samples, Sorted, count);
            Array.Sort(Sorted, 0, count);

            float total = 0f;
            for (int i = 0; i < count; i++) total += Sorted[i];
            float mean = total / count;

            Debug.Log(string.Format(
                "[FrameTimeProbe] window={0} pipeline={1} colorSpace={2} target={3} frames={4} " +
                "mean={5:F2}ms p50={6:F2}ms p95={7:F2}ms p99={8:F2}ms max={9:F2}ms meanFps={10:F1}",
                ++window,
                GraphicsSettings.currentRenderPipeline == null
                    ? "BuiltIn"
                    : GraphicsSettings.currentRenderPipeline.GetType().Name,
                QualitySettings.activeColorSpace,
                Application.targetFrameRate,
                count,
                mean,
                Percentile(50f),
                Percentile(95f),
                Percentile(99f),
                Sorted[count - 1],
                mean > 0f ? 1000f / mean : 0f));
        }

        // Nearest-rank on the sorted window. Sample counts here are in the low
        // hundreds, so interpolating between ranks would imply a precision the
        // window size does not carry.
        private float Percentile(float percent)
        {
            int rank = Mathf.Clamp(
                Mathf.CeilToInt(percent / 100f * count) - 1, 0, count - 1);
            return Sorted[rank];
        }
    }
}
#endif
