using UnityEngine;
using UnityEngine.UI;

namespace VRMTracker.Platform
{
    /// ヒラギノ角ゴシックの読み込みと Canvas への適用を担う静的ヘルパ。
    public static class FontLoader
    {
        /// UI 用フォントを読み込んで返す。プロジェクト同梱フォント（Resources）を最優先。
        /// OS 動的フォントは環境によって「null ではないが描画されない」ことがあるため
        /// フォールバック扱い。読み込めなければ null（呼び出し側は既定フォントを維持）。
        public static Font LoadUIFont()
        {
            // 1) 同梱フォント（確実に描画される）
            var uiFont = Resources.Load<Font>("HiraginoSansW8");

            // 2) フォールバック: OS から動的ロード
            if (uiFont == null)
                uiFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Hiragino Sans W8", "HiraginoSans-W8", "Hiragino Sans W6", "HiraginoSans-W6" }, 16);

            Debug.Log($"[FontLoader] UI font = {(uiFont != null ? uiFont.name : "なし（既定フォント使用）")}");

            return uiFont;
        }

        /// Canvas 配下の全 Text にフォントを適用する。font が null のときは何もしない。
        public static void ApplyToCanvas(Canvas canvas, Font font)
        {
            if (font == null || canvas == null) return;
            foreach (var t in canvas.GetComponentsInChildren<Text>(true))
                t.font = font;
        }
    }
}
