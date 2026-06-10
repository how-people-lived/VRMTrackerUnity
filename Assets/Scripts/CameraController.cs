using UnityEngine;
using UnityEngine.EventSystems;

/// カメラ操作（macOS トラックパッド最適化）
/// - 2本指横スワイプ          : Yaw 回転
/// - 2本指縦スクロール/ピンチ : ズーム
/// - Shift + 2本指ドラッグ    : 平行移動（パン）
/// - 左クリック＋ドラッグ     : 平行移動（パン）
/// - 右クリック＋ドラッグ     : 軌道回転（Pitch + Yaw）
public class CameraController : MonoBehaviour
{
    [Header("Orbit")]
    [SerializeField] float swipeSensitivity   = 3f;    // 横スワイプ → Yaw
    [SerializeField] float orbitSensitivity   = 5f;    // 右クリックドラッグ → 軌道
    [SerializeField] float minPitch           = -30f;
    [SerializeField] float maxPitch           = 80f;

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
    }

    void LateUpdate()
    {
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (overUI) { ApplyTransform(); return; }

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

        // ── 左クリック＋ドラッグ → 平行移動 ────────────────────────────────
        if (Input.GetMouseButton(0))
        {
            target -= transform.right * Input.GetAxis("Mouse X") * dragPanSensitivity * distance;
            target -= transform.up    * Input.GetAxis("Mouse Y") * dragPanSensitivity * distance;
        }

        // ── 右クリック＋ドラッグ → 軌道回転 ────────────────────────────────
        if (Input.GetMouseButton(1))
        {
            yaw   += Input.GetAxis("Mouse X") * orbitSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * orbitSensitivity;
            pitch  = Mathf.Clamp(pitch, minPitch, maxPitch);
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
    public Vector3 GetTarget()         => target;
}
