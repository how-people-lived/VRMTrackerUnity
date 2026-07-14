using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VRMTracker.Platform
{
    /// ネイティブのファイル選択ダイアログを提供する静的ヘルパ。
    /// エディタでは EditorUtility.OpenFilePanel、macOS スタンドアロンでは osascript を使う。
    /// 選択された絶対パス、またはキャンセル時は null を返す（検証は呼び出し側で行う）。
    public static class NativeFileDialog
    {
        /// osascript(AppleScript ランタイム) の初回コールドスタート対策。
        /// 起動時に空打ちして温めておくことで、最初のダイアログでも即座に出せるようにする。
        public static Task PrewarmFileDialog()
        {
#if UNITY_STANDALONE_OSX
            _ = Task.Run(() =>
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName               = "/usr/bin/osascript",
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add("return 1");   // GUI を出さない最小スクリプトで初期化のみ
                    using var proc = System.Diagnostics.Process.Start(psi);
                    proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);
                }
                catch { /* 温め失敗は無視（実害なし） */ }
            });
#endif
            return Task.CompletedTask;
        }

#if UNITY_STANDALONE_OSX
        // osascript を実行し、標準出力（POSIX パス）を Trim して返す。失敗時は null。
        static Task<string> RunOsascript(string appleScript)
        {
            return Task.Run(() =>
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName               = "/usr/bin/osascript",
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8,
                    };
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add(appleScript);
                    using var proc = System.Diagnostics.Process.Start(psi);
                    string result = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();
                    return proc.ExitCode == 0 ? result : null;
                }
                catch { return null; }
            });
        }
#endif

        /// VRM ファイルを選択させる。選択された絶対パス、またはキャンセル/未対応時は null。
        /// （.vrm 拡張子の検証は呼び出し側で行う。）
        public static Task<string> PickVrm()
        {
#if UNITY_EDITOR
            var path = EditorUtility.OpenFilePanel("VRMファイルを選択", "", "vrm");
            return Task.FromResult(string.IsNullOrEmpty(path) ? null : path);
#elif UNITY_STANDALONE_OSX
            return RunOsascript("POSIX path of (choose file with prompt \"VRMファイルを選択\")");
#else
            return Task.FromResult<string>(null);
#endif
        }

        /// 背景画像（png/jpg/jpeg）を選択させる。選択された絶対パス、またはキャンセル/未対応時は null。
        public static Task<string> PickImage()
        {
#if UNITY_EDITOR
            var path = EditorUtility.OpenFilePanel("背景画像を選択", "", "png,jpg,jpeg");
            return Task.FromResult(string.IsNullOrEmpty(path) ? null : path);
#elif UNITY_STANDALONE_OSX
            return RunOsascript("POSIX path of (choose file with prompt \"背景画像を選択\" of type {\"png\",\"jpg\",\"jpeg\"})");
#else
            return Task.FromResult<string>(null);
#endif
        }

        /// VRM Animation（.vrma）モーションを選択させる。選択された絶対パス、またはキャンセル/未対応時は null。
        public static Task<string> PickVrma()
        {
#if UNITY_EDITOR
            var path = EditorUtility.OpenFilePanel("モーション(.vrma)を選択", "", "vrma");
            return Task.FromResult(string.IsNullOrEmpty(path) ? null : path);
#elif UNITY_STANDALONE_OSX
            return RunOsascript("POSIX path of (choose file with prompt \"モーション(.vrma)を選択\")");
#else
            return Task.FromResult<string>(null);
#endif
        }
    }
}
