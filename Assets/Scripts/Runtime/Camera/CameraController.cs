using UnityEngine;
using UnityEngine.EventSystems;
using VRMTracker.UI;

namespace VRMTracker.CameraControl
{
    /// カメラ操作（macOS トラックパッド最適化）
    /// - 左クリック＋ドラッグ     : 軌道回転（上下 Pitch ＋ 左右 Yaw）★主操作
    /// - 2本指横スワイプ          : Yaw 回転
    /// - 2本指縦スクロール/ピンチ : ズーム
    /// - 右クリック＋ドラッグ     : 平行移動（パン）
    /// - Shift + 左ドラッグ / Shift + 2本指 : 平行移動（パン）
    public class CameraController : MonoBehaviour
    {
        [Header("Orbit")]
        [SerializeField] float swipeSensitivity   = 3f;    // 横スワイプ → Yaw
        [SerializeField] float orbitSensitivity   = 5f;    // 左ドラッグ → 軌道回転
        [SerializeField] float minPitch           = -60f;
        [SerializeField] float maxPitch           = 85f;

        [Header("Zoom")]
        [SerializeField] float zoomSensitivity    = 0.15f; // ピンチ/縦スクロール
        [SerializeField] float minDistance        = 0.3f;
        [SerializeField] float maxDistance        = 10f;

        [Header("Pan")]
        [SerializeField] float panSensitivity     = 0.006f;   // スクロールデルタ基準
        [SerializeField] float dragPanSensitivity = 0.04f;    // ドラッグ基準

        Vector3 target   = new Vector3(0f, 1.4f, 0f);
        float   yaw      = 180f;
        float   pitch    = 0f;
        float   distance = 2.0f;

        void Start()
        {
            var offset = transform.position - target;
            if (offset.magnitude > 0.001f)
            {
                distance = offset.magnitude;
                yaw      = Mathf.Atan2(offset.x, -offset.z) * Mathf.Rad2Deg;
                pitch    = Mathf.Asin(Mathf.Clamp(offset.y / distance, -1f, 1f)) * Mathf.Rad2Deg;
            }
            LoadState();   // 前回保存したカメラ設定があれば復元
        }

        // ── カメラ設定の保存／読込（PlayerPrefs） ───────────────────────────────
        const string K = "cam_";

        public void SaveState()
        {
            PlayerPrefs.SetFloat(K + "yaw", yaw);
            PlayerPrefs.SetFloat(K + "pitch", pitch);
            PlayerPrefs.SetFloat(K + "dist", distance);
            PlayerPrefs.SetFloat(K + "height", target.y);
            PlayerPrefs.SetFloat(K + "fov", Fov);
            PlayerPrefs.Save();
        }

        public void LoadState()
        {
            if (!PlayerPrefs.HasKey(K + "yaw")) return;
            yaw      = PlayerPrefs.GetFloat(K + "yaw", yaw);
            pitch    = PlayerPrefs.GetFloat(K + "pitch", pitch);
            distance = PlayerPrefs.GetFloat(K + "dist", distance);
            target   = new Vector3(target.x, PlayerPrefs.GetFloat(K + "height", target.y), target.z);
            Fov      = PlayerPrefs.GetFloat(K + "fov", Fov);
        }

        bool _pressedOnUI;

        void LateUpdate()
        {
            // uGUI 上、または設定ウインドウ（IMGUI）上ではカメラ操作を無効化
            bool overUI = (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                          || VRMTracker.UI.AvatarController.PointerOverSettings;

            // 押し始めが UI 上なら離すまでブロック（スライダーをドラッグ中に視点が動くのを防ぐ）
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) _pressedOnUI = overUI;
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))       _pressedOnUI = false;

            if (overUI || _pressedOnUI) { ApplyTransform(); return; }

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            var scroll = Input.mouseScrollDelta;

            // ── 2本指ジェスチャー ──────────────────────────────────────────────
            if (scroll.magnitude > 0.001f)
            {
                if (shift)
                {
                    // Shift + 2本指 → 平行移動
                    target -= transform.right * scroll.x * panSensitivity * distance;
                    target += transform.up    * scroll.y * panSensitivity * distance;
                }
                else
                {
                    // 横スワイプ → Yaw 回転
                    if (Mathf.Abs(scroll.x) > 0.001f)
                        yaw += scroll.x * swipeSensitivity;

                    // 縦スクロール / ピンチ → ズーム
                    if (Mathf.Abs(scroll.y) > 0.001f)
                        distance = Mathf.Clamp(distance - scroll.y * zoomSensitivity,
                                               minDistance, maxDistance);
                }
            }

            // ── 左クリック＋ドラッグ → 軌道回転（上下＋左右）★主操作 ──────────
            //    Shift 併用時はパン
            if (Input.GetMouseButton(0))
            {
                if (shift)
                {
                    target -= transform.right * Input.GetAxis("Mouse X") * dragPanSensitivity * distance;
                    target -= transform.up    * Input.GetAxis("Mouse Y") * dragPanSensitivity * distance;
                }
                else
                {
                    yaw   += Input.GetAxis("Mouse X") * orbitSensitivity;
                    pitch -= Input.GetAxis("Mouse Y") * orbitSensitivity;
                    pitch  = Mathf.Clamp(pitch, minPitch, maxPitch);
                }
            }

            // ── 右クリック＋ドラッグ → 平行移動（パン） ────────────────────────
            if (Input.GetMouseButton(1))
            {
                target -= transform.right * Input.GetAxis("Mouse X") * dragPanSensitivity * distance;
                target -= transform.up    * Input.GetAxis("Mouse Y") * dragPanSensitivity * distance;
            }

            ApplyTransform();
        }

        void ApplyTransform()
        {
            float radY = yaw   * Mathf.Deg2Rad;
            float radP = pitch * Mathf.Deg2Rad;
            float cosP = Mathf.Cos(radP);
            transform.position = target + new Vector3(
                 distance * Mathf.Sin(radY) * cosP,
                 distance * Mathf.Sin(radP),
                -distance * Mathf.Cos(radY) * cosP);
            transform.LookAt(target);
        }

        public void SetTarget(Vector3 pos) => target = pos;

        // ── 設定パネルからの実行時調整 ───────────────────────────────────────────
        Camera _cam;
        Camera Cam => _cam != null ? _cam : (_cam = GetComponent<Camera>());

        public float Height   { get => target.y; set => target = new Vector3(target.x, value, target.z); }
        public float Distance { get => distance; set => distance = Mathf.Clamp(value, minDistance, maxDistance); }
        public float Fov      { get => Cam != null ? Cam.fieldOfView : 30f;
                                set { if (Cam != null) Cam.fieldOfView = Mathf.Clamp(value, 10f, 80f); } }

        /// 視点（角度・距離）を初期化。注視点（高さ）は維持。
        public void ResetView()
        {
            yaw = 180f; pitch = 8f; distance = 2.2f;
        }

        // ── コントロールパッドからの軌道操作 ─────────────────────────────────────

        /// 軌道回転を加算（度単位）。コントロールパッドのボタンから呼ぶ。
        public void Orbit(float dYaw, float dPitch)
        {
            yaw  += dYaw;
            pitch = Mathf.Clamp(pitch + dPitch, minPitch, maxPitch);
        }

        /// ズーム（+で寄る／-で引く）。
        public void Zoom(float delta)
        {
            distance = Mathf.Clamp(distance - delta, minDistance, maxDistance);
        }
    }
}
