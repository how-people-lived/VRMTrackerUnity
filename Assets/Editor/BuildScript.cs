using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

static class BuildScript
{
    const string OutputDir = "Build/macOS";
    const string AppName   = "VRMTracker.app";
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("VRMTracker/macOS App をビルド")]
    public static void BuildMacOS()
    {
        ProjectAutoSetup.EnsureScene();

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputPath  = Path.Combine(projectRoot, OutputDir, AppName);
        Directory.CreateDirectory(Path.Combine(projectRoot, OutputDir));

        var opt = new BuildPlayerOptions
        {
            scenes           = new[] { ScenePath },
            locationPathName = outputPath,
            target           = BuildTarget.StandaloneOSX,
            options          = BuildOptions.None,
        };

        Debug.Log($"[BuildScript] Building → {outputPath}");
        BuildReport report = BuildPipeline.BuildPlayer(opt);

        if (report.summary.result == BuildResult.Succeeded)
            Debug.Log($"[BuildScript] ✓ Build succeeded  {report.summary.totalSize / 1024 / 1024} MB");
        else
            Debug.LogError($"[BuildScript] ✗ Build failed: {report.summary.result}");
    }

    // -executeMethod BuildScript.BuildMacOSCLI
    public static void BuildMacOSCLI()
    {
        ProjectAutoSetup.EnsureScene();

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputPath  = Path.Combine(projectRoot, OutputDir, AppName);
        Directory.CreateDirectory(Path.Combine(projectRoot, OutputDir));

        var opt = new BuildPlayerOptions
        {
            scenes           = new[] { ScenePath },
            locationPathName = outputPath,
            target           = BuildTarget.StandaloneOSX,
            options          = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(opt);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
