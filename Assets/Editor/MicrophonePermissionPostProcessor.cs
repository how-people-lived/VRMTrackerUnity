// ビルド後、macOS の .app に NSMicrophoneUsageDescription を追記する。
// リップシンク（VoiceLipSync）でマイクを使用するため、Info.plist に権限説明を入れないと
// 起動時にマイク要求が拒否される（あるいはクラッシュする）。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

static class MicrophonePermissionPostProcessor
{
    const string Key   = "NSMicrophoneUsageDescription";
    const string Value = "リップシンク用にマイクを使用します";

    [PostProcessBuild]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.StandaloneOSX) return;

        try
        {
            // StandaloneOSX の pathToBuiltProject は .app 自体を指す。
            string plistPath = pathToBuiltProject + "/Contents/Info.plist";
            if (!File.Exists(plistPath))
            {
                Debug.LogWarning($"[MicPermission] Info.plist が見つかりません: {plistPath}");
                return;
            }

            string plist = File.ReadAllText(plistPath);

            // 既にキーがあれば何もしない。
            if (plist.Contains(Key))
            {
                Debug.Log("[MicPermission] NSMicrophoneUsageDescription は既に存在します。スキップ。");
                return;
            }

            int idx = plist.LastIndexOf("</dict>", System.StringComparison.Ordinal);
            if (idx < 0)
            {
                Debug.LogWarning("[MicPermission] </dict> が見つからないため追記できませんでした。");
                return;
            }

            string entry = $"\t<key>{Key}</key>\n\t<string>{Value}</string>\n";
            plist = plist.Insert(idx, entry);
            File.WriteAllText(plistPath, plist);
            Debug.Log($"[MicPermission] ✓ NSMicrophoneUsageDescription を追記しました: {plistPath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MicPermission] Info.plist の更新に失敗しました: {e}");
        }
    }
}
#endif
