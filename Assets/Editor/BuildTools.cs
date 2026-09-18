using System.IO;
using UnityEditor;
using UnityEngine;

// One-click Windows build you can zip and send to friends.
public static class BuildTools
{
    [MenuItem("Tools/Build Windows Game (for friends)")]
    public static void BuildWindows()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Improvisation"));
        Directory.CreateDirectory(folder);

        var scenes = new System.Collections.Generic.List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) scenes.Add(s.path);

        var options = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = Path.Combine(folder, "Improvisation.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log("Build finished: " + folder + "  - zip this whole folder and send it to your friends.");
            EditorUtility.RevealInFinder(options.locationPathName);
        }
        else
        {
            Debug.LogError("Build failed: " + report.summary.result);
        }
    }
}
