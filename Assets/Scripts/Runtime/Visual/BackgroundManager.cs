using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using VRMTracker.Platform;

namespace VRMTracker.Visual
{
    /// <summary>
    /// 背景（クロマキー単色 / 画像）の管理。AvatarController から抽出。
    /// 単色は mainCamera の SolidColor、画像は bgQuad に貼り付けてカメラ視野に追従させる。
    /// </summary>
    public class BackgroundManager : MonoBehaviour
    {
        Camera                mainCamera;
        GameObject            bgQuad;      // カメラ子の背景画像用クアッド（既定で非表示）
        Renderer              _bgRenderer; // 背景クアッドのレンダラ（画像表示用）
        System.Action<string> _onStatus;

        Color bgColor = new Color(0.05f, 0.75f, 0.22f);   // 既定はクロマキー緑

        public Color CurrentColor => bgColor;

        // ── Setup ─────────────────────────────────────────────────────────────

        public void Setup(Camera mainCamera, GameObject bgQuad, System.Action<string> onStatus)
        {
            this.mainCamera = mainCamera;
            this.bgQuad     = bgQuad;
            _onStatus       = onStatus;
            if (bgQuad != null) _bgRenderer = bgQuad.GetComponent<Renderer>();
        }

        // ── Background color ──────────────────────────────────────────────────

        public void SetBackground(Color color)
        {
            bgColor = color;
            if (mainCamera != null)
            {
                mainCamera.clearFlags      = CameraClearFlags.SolidColor;  // クロマキー用に単色塗り
                mainCamera.backgroundColor = color;
            }
            if (bgQuad != null) bgQuad.SetActive(false);   // 単色選択時は背景画像を消す
            PlayerPrefs.SetString(PrefKeys.BgImg, "");
            PlayerPrefs.SetFloat(PrefKeys.BgR, color.r);
            PlayerPrefs.SetFloat(PrefKeys.BgG, color.g);
            PlayerPrefs.SetFloat(PrefKeys.BgB, color.b);
        }

        // ── 背景画像 ───────────────────────────────────────────────────────────

        void LoadBackgroundImage(string path, bool persist = true)
        {
            if (_bgRenderer == null || string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path))) { SetStatus("画像を読み込めませんでした"); return; }
                var mat = _bgRenderer.material;       // インスタンス化（共有アセットを汚さない）
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                bgQuad.SetActive(true);
                SizeBgQuad();
                if (persist) PlayerPrefs.SetString(PrefKeys.BgImg, path);
                SetStatus($"背景画像: {Path.GetFileName(path)}");
            }
            catch (System.Exception e) { SetStatus($"画像エラー: {e.Message}"); }
        }

        public void ClearBackgroundImage()
        {
            if (bgQuad != null) bgQuad.SetActive(false);
            PlayerPrefs.SetString(PrefKeys.BgImg, "");
            SetBackground(bgColor);
        }

        // 背景クアッドをカメラ視野いっぱいに合わせる（画角・アスペクトに追従）
        public void SizeBgQuad()
        {
            if (bgQuad == null || !bgQuad.activeSelf || mainCamera == null) return;
            float z  = bgQuad.transform.localPosition.z;
            float hh = 2f * z * Mathf.Tan(mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float ww = hh * mainCamera.aspect;
            bgQuad.transform.localScale = new Vector3(ww, hh, 1f);
        }

        public void PickBackgroundImage()
        {
            _ = PickBackgroundImageAsync();
        }

        async Task PickBackgroundImageAsync()
        {
            SetStatus("背景画像を選択中…");
            string path = await NativeFileDialog.PickImage();
            if (string.IsNullOrEmpty(path)) { SetStatus("キャンセルされました"); return; }
            LoadBackgroundImage(path);
        }

        public void LoadBackgroundPref()
        {
            if (PlayerPrefs.HasKey(PrefKeys.BgR))
                bgColor = new Color(PlayerPrefs.GetFloat(PrefKeys.BgR),
                                    PlayerPrefs.GetFloat(PrefKeys.BgG),
                                    PlayerPrefs.GetFloat(PrefKeys.BgB));
            SetBackground(bgColor);
        }

        void SetStatus(string msg) => _onStatus?.Invoke(msg);
    }
}
