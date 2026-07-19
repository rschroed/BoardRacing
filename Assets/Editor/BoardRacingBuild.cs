using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class BoardRacingBuild
{
    public static void BuildAndroidDevelopment()
    {
        var scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
        if (scenes.Length == 0) throw new InvalidOperationException("No enabled build scenes.");
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "Builds/Android/BoardRacing-development.apk",
            target = BuildTarget.Android,
            // A hardware-ready verification build must not inherit stale Bee outputs.
            // In particular, repeated IL2CPP builds can otherwise leave numbered native
            // library copies in the APK, materially inflating the artifact.
            options = BuildOptions.Development | BuildOptions.CleanBuildCache
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Android build failed: {report.summary.result}");
    }

    // TEMPORARY: builds ControlLab instead of RacePrototype so the throttle orientation
    // offset can be measured against real hardware (issue #77 Round 2 hardware review).
    // Delete this method and the BOARDRACING_CONTROL_LAB defines once calibrated.
    public static void BuildAndroidControlLab()
    {
        BuildTargetGroup group = BuildTargetGroup.Android;
        string priorDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
        try
        {
            string[] defines = priorDefines.Split(';').Where(x => x.Length > 0)
                .Append("BOARDRACING_CONTROL_LAB").ToArray();
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", defines));
            var scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException("No enabled build scenes.");
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/Android/BoardRacing-controllab.apk",
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.CleanBuildCache
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Android build failed: {report.summary.result}");
        }
        finally
        {
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, priorDefines);
        }
    }
}
