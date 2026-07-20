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
}
