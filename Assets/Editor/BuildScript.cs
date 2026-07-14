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
        BuildReport report = RunBuild();
        if (report.summary.result == BuildResult.Succeeded)
            Debug.Log($"[BuildScript] ✓ Build succeeded  {report.summary.totalSize / 1024 / 1024} MB");
        else
            Debug.LogError($"[BuildScript] ✗ Build failed: {report.summary.result}");
    }

    // -executeMethod BuildScript.BuildMacOSCLI
    public static void BuildMacOSCLI()
    {
        BuildReport report = RunBuild();
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    // シーン準備・universal 設定・マイク使用目的の設定を行ってから macOS ビルドを実行する。
    static BuildReport RunBuild()
    {
        ProjectAutoSetup.EnsureScene();
        ForceUniversalArchitecture();       // Apple Silicon + Intel の universal バイナリを強制
        EnsureMicrophoneUsageDescription(); // 空だとビルド中断するため必須

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputPath  = Path.Combine(projectRoot, OutputDir, AppName);
        Directory.CreateDirectory(Path.Combine(projectRoot, OutputDir));

        Debug.Log($"[BuildScript] Building (universal) → {outputPath}");
        return BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes           = new[] { ScenePath },
            locationPathName = outputPath,
            target           = BuildTarget.StandaloneOSX,
            options          = BuildOptions.None,
        });
    }

    /// macOS ビルドを Intel(x64) + Apple Silicon(arm64) の universal バイナリに固定する。
    static void ForceUniversalArchitecture()
    {
        UnityEditor.OSXStandalone.UserBuildSettings.architecture =
            UnityEditor.Build.OSArchitecture.x64ARM64;
    }

    /// VoiceLipSync が Microphone を使うため、ビルド前に使用目的説明を設定する。
    /// これが空だと Unity はビルドを中断する（Info.plist へも自動反映される）。
    /// API のパスが Unity バージョンで異なる（PlayerSettings.macOS 等）ため、リフレクションで設定する。
    static void EnsureMicrophoneUsageDescription()
    {
        const string desc = "音声リップシンク用にマイクを使用します。";
        var t = typeof(PlayerSettings);

        // ネスト型（iOS / macOS）の static string プロパティ microphoneUsageDescription を探す
        foreach (var nt in t.GetNestedTypes())
        {
            var p = nt.GetProperty("microphoneUsageDescription");
            if (p != null && p.CanWrite && p.PropertyType == typeof(string))
            {
                var cur = p.GetValue(null) as string;
                if (string.IsNullOrEmpty(cur)) p.SetValue(null, desc);
                Debug.Log($"[BuildScript] microphoneUsageDescription set via PlayerSettings.{nt.Name}");
                return;
            }
        }

        // フォールバック: SetPropertyString(name, value, targetGroup)
        var m = t.GetMethod("SetPropertyString", new[] { typeof(string), typeof(string), typeof(BuildTargetGroup) });
        if (m != null)
        {
            m.Invoke(null, new object[] { "microphoneUsageDescription", desc, BuildTargetGroup.Standalone });
            Debug.Log("[BuildScript] microphoneUsageDescription set via SetPropertyString");
        }
        else
        {
            Debug.LogWarning("[BuildScript] microphoneUsageDescription API が見つかりませんでした");
        }
    }
}
