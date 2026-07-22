using System;
using System.IO;
using System.Reflection;
using BoardRacing.Runtime;
using UnityEditor;
using UnityEngine;

// Produces the staged code-and-capture review artifacts (docs/gameplay/wireframe-ui.md):
// plays the prototype and captures every RaceUiPreviewScenario at exactly 1920x1080.
// Run from a HEADED editor — screenshots need a real Game view backbuffer:
//   Unity -projectPath . -executeMethod BoardRacingCaptures.Run
// The method exits the editor itself when the last capture is on disk.
public static class BoardRacingCaptures
{
    private const string OutputDirectory = "docs/captures/surface-round1";
    private const string Prefix = "surface-round1";
    private const int SettleTicksPerScenario = 20;

    private static string[] scenarios;
    private static FieldInfo scenarioField;
    private static RacePrototype prototype;
    private static int current;
    private static int settleTicks;
    private static string pendingFile;
    private static bool prevOptionsEnabled;
    private static EnterPlayModeOptions prevOptions;

    public static void Run()
    {
        Directory.CreateDirectory(OutputDirectory);
        Type scenarioType = typeof(RacePrototype).Assembly
            .GetType("BoardRacing.Runtime.RaceUiPreviewScenario");
        scenarios = Enum.GetNames(scenarioType);
        scenarioField = typeof(RacePrototype).GetField("previewScenarioIndex",
            BindingFlags.NonPublic | BindingFlags.Instance);
        SetGameViewSize(1920, 1080);
        // Domain reload would wipe this class's orchestration state on play-mode
        // entry; disable it for the capture run and restore the prior settings.
        prevOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        prevOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions =
            EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Step;
        EditorApplication.EnterPlaymode();
    }

    private static void Step()
    {
        if (!EditorApplication.isPlaying) return;
        if (prototype == null)
        {
            prototype = UnityEngine.Object.FindObjectOfType<RacePrototype>();
            if (prototype == null) return;
            RacePrototype.SuppressEditorDiagnostics = true;
            ApplyScenario(0);
            return;
        }
        if (pendingFile != null)
        {
            if (!File.Exists(pendingFile)) return;
            Debug.Log("Captured " + pendingFile);
            pendingFile = null;
            if (current + 1 >= scenarios.Length) { Finish(); return; }
            ApplyScenario(current + 1);
            return;
        }
        if (++settleTicks < SettleTicksPerScenario) return;
        string file = Path.Combine(OutputDirectory,
            Prefix + "-" + (current + 1).ToString("00") + "-" + scenarios[current] + ".png");
        if (File.Exists(file)) File.Delete(file);
        ScreenCapture.CaptureScreenshot(file);
        pendingFile = file;
    }

    private static void ApplyScenario(int index)
    {
        current = index;
        settleTicks = 0;
        scenarioField.SetValue(prototype, index);
    }

    private static void Finish()
    {
        EditorApplication.update -= Step;
        RacePrototype.SuppressEditorDiagnostics = false;
        EditorSettings.enterPlayModeOptionsEnabled = prevOptionsEnabled;
        EditorSettings.enterPlayModeOptions = prevOptions;
        EditorApplication.Exit(0);
    }

    // The Game view renders (and ScreenCapture captures) at its selected size, so the
    // run pins a fixed 1920x1080 entry; the size list is editor-internal API.
    private static void SetGameViewSize(int width, int height)
    {
        Assembly editor = typeof(UnityEditor.Editor).Assembly;
        Type sizesType = editor.GetType("UnityEditor.GameViewSizes");
        Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        object sizes = singletonType.GetProperty("instance").GetValue(null);
        // The Game view reads sizes from the ACTIVE build target's group (this
        // project targets Android), not from Standalone.
        object currentGroup = sizesType.GetProperty("currentGroupType").GetValue(sizes);
        object group = sizesType.GetMethod("GetGroup").Invoke(sizes,
            new object[] { (int)currentGroup });
        Type groupType = group.GetType();
        int total = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
        int index = -1;
        for (int i = 0; i < total; i++)
        {
            object size = groupType.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
            Type sizeType = size.GetType();
            if ((int)sizeType.GetProperty("width").GetValue(size) == width &&
                (int)sizeType.GetProperty("height").GetValue(size) == height)
            { index = i; break; }
        }
        if (index < 0)
        {
            Type sizeType = editor.GetType("UnityEditor.GameViewSize");
            Type kindType = editor.GetType("UnityEditor.GameViewSizeType");
            object fixedResolution = Enum.Parse(kindType, "FixedResolution");
            object custom = Activator.CreateInstance(sizeType, fixedResolution, width, height,
                "Board Capture");
            groupType.GetMethod("AddCustomSize").Invoke(group, new object[] { custom });
            index = total;
        }
        Type gameViewType = editor.GetType("UnityEditor.GameView");
        EditorWindow view = EditorWindow.GetWindow(gameViewType);
        view.Focus();
        gameViewType.GetMethod("SizeSelectionCallback",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(view, new object[] { index, null });
    }
}
