using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UniVRM10;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class AvatarController : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] NetworkReceiver receiver;
    [SerializeField] FaceMapper      faceMapper;
    [SerializeField] BoneMapper      boneMapper;

    [Header("UI - Labels")]
    [SerializeField] internal Text       statusLabel;
    [SerializeField] internal Text       connectionLabel;
    [SerializeField] internal Text       fpsLabel;
    [SerializeField] internal InputField pathInputField;
    [SerializeField] internal GameObject hintPanel;

    [Header("UI - Buttons")]
    [SerializeField] internal Button loadButton;
    [SerializeField] internal Button bgBlackButton;
    [SerializeField] internal Button bgWhiteButton;
    [SerializeField] internal Button bgGreenButton;

    [Header("Scene")]
    [SerializeField] Camera           mainCamera;
    [SerializeField] CameraController cameraController;

    Vrm10Instance currentVrm;
    bool  loading;
    float lastFpsTime;
    int   frameCount;
    int   lastFps;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    void Start()
    {
        loadButton?.onClick.AddListener(OnLoadButtonClick);
        bgBlackButton?.onClick.AddListener(SetBackgroundBlack);
        bgWhiteButton?.onClick.AddListener(SetBackgroundWhite);
        bgGreenButton?.onClick.AddListener(SetBackgroundGreen);

        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--vrm-path" && File.Exists(args[i + 1]))
            {
                _ = LoadVRMAsync(args[i + 1]);
                break;
            }
        }
    }

    // ── Per-frame ─────────────────────────────────────────────────────────

    void Update()
    {
        frameCount++;
        if (Time.time - lastFpsTime >= 1f)
        {
            lastFps     = frameCount;
            frameCount  = 0;
            lastFpsTime = Time.time;
        }

        if (connectionLabel != null && receiver != null)
            UpdateConnectionLabel();

        if (pathInputField != null && Input.GetKeyDown(KeyCode.Return))
        {
            var p = pathInputField.text.Trim();
            if (File.Exists(p)) _ = LoadVRMAsync(p);
        }
    }

    void LateUpdate()
    {
        if (currentVrm == null) return;
        var frame = receiver?.GetFrame();
        if (frame == null) return;

        boneMapper?.Apply(frame.face, frame.leftHand, frame.rightHand, frame.body);
        if (frame.face != null) faceMapper?.Apply(frame.face);

        // 診断: body/hand データ受信状況をラベルに表示
        if (fpsLabel != null)
        {
            string b  = frame.body      != null ? "B✓" : "B—";
            string lh = frame.leftHand  != null ? "LH✓" : "LH—";
            string rh = frame.rightHand != null ? "RH✓" : "RH—";
            fpsLabel.text = $"{lastFps} fps  {b} {lh} {rh}";
        }
    }

    // ── Connection status ─────────────────────────────────────────────────

    void UpdateConnectionLabel()
    {
        if (receiver.LastReceiveTime == System.DateTime.MinValue)
        {
            connectionLabel.text  = $"UDP :{receiver.port}  待機中";
            connectionLabel.color = new Color(0.55f, 0.55f, 0.55f);
            return;
        }

        double elapsed = (System.DateTime.UtcNow - receiver.LastReceiveTime).TotalSeconds;

        if (elapsed < 2.0)
        {
            string dot = (Time.time % 0.5f < 0.25f) ? "●" : "○";
            connectionLabel.text  = $"{dot} {receiver.SenderIP}  受信中";
            connectionLabel.color = new Color(0.3f, 0.95f, 0.4f);
        }
        else if (elapsed < 15.0)
        {
            connectionLabel.text  = $"○ {receiver.SenderIP}  待機中...";
            connectionLabel.color = new Color(0.95f, 0.85f, 0.3f);
        }
        else
        {
            connectionLabel.text  = $"UDP :{receiver.port}  待機中";
            connectionLabel.color = new Color(0.55f, 0.55f, 0.55f);
        }
    }

    // ── VRM loading ───────────────────────────────────────────────────────

    public void OnLoadButtonClick()
    {
#if UNITY_EDITOR
        var path = EditorUtility.OpenFilePanel("VRMファイルを選択", "", "vrm");
        if (!string.IsNullOrEmpty(path)) _ = LoadVRMAsync(path);
#elif UNITY_STANDALONE_OSX
        _ = OpenWithOsascriptAsync();
#else
        if (pathInputField != null && File.Exists(pathInputField.text.Trim()))
            _ = LoadVRMAsync(pathInputField.text.Trim());
        else
            SetStatus("下のパス欄にVRMファイルのフルパスを入力してEnterキーを押してください");
#endif
    }

#if UNITY_STANDALONE_OSX
    async Task OpenWithOsascriptAsync()
    {
        if (loading) return;
        SetStatus("ファイルを選択中…");

        string path = await Task.Run(() =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName                = "/usr/bin/osascript",
                    RedirectStandardOutput  = true,
                    RedirectStandardError   = true,
                    UseShellExecute         = false,
                    CreateNoWindow          = true,
                    StandardOutputEncoding  = System.Text.Encoding.UTF8,
                };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add("POSIX path of (choose file with prompt \"VRMファイルを選択\")");

                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    string result = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();
                    return proc.ExitCode == 0 ? result : null;
                }
            }
            catch { return null; }
        });

        if (string.IsNullOrEmpty(path))
        {
            SetStatus("キャンセルされました");
            return;
        }
        if (!path.EndsWith(".vrm", System.StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(".vrm ファイルを選択してください");
            return;
        }
        _ = LoadVRMAsync(path);
    }
#endif

#if UNITY_EDITOR
    void OnGUI()
    {
        var e = Event.current;
        if (e.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            e.Use();
        }
        else if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (var p in DragAndDrop.paths)
            {
                if (p.EndsWith(".vrm", System.StringComparison.OrdinalIgnoreCase))
                { _ = LoadVRMAsync(p); break; }
            }
            e.Use();
        }
    }
#endif

    async Task LoadVRMAsync(string path)
    {
        if (loading) return;
        loading = true;
        SetStatus($"読込中: {Path.GetFileName(path)} …");

        if (currentVrm != null) { Destroy(currentVrm.gameObject); currentVrm = null; }

        try
        {
            var instance = await Vrm10.LoadPathAsync(path, canLoadVrm0X: true);
            instance.transform.SetParent(transform.parent, false);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            currentVrm = instance;
            faceMapper?.Initialize(instance);
            boneMapper?.Initialize(instance);
            SetStatus($"✓ {Path.GetFileNameWithoutExtension(path)}");
            if (hintPanel != null) hintPanel.SetActive(false);

            // カメラ注視点をアバターの胸あたりに設定
            if (cameraController != null)
            {
                var humanoid = instance.GetComponent<Animator>();
                if (humanoid != null && humanoid.isHuman)
                {
                    var chest = humanoid.GetBoneTransform(HumanBodyBones.Chest);
                    var hips  = humanoid.GetBoneTransform(HumanBodyBones.Hips);
                    if (chest != null)
                        cameraController.SetTarget(chest.position);
                    else if (hips != null)
                        cameraController.SetTarget(hips.position + Vector3.up * 0.5f);
                }
            }
        }
        catch (System.Exception e)
        {
            SetStatus($"読込エラー: {e.Message}");
            Debug.LogError($"[AvatarController] {e}");
        }
        loading = false;
    }

    // ── Background color ──────────────────────────────────────────────────

    public void SetBackgroundBlack() => SetBackground(new Color(0.08f, 0.08f, 0.08f));
    public void SetBackgroundWhite() => SetBackground(new Color(0.93f, 0.93f, 0.93f));
    public void SetBackgroundGreen() => SetBackground(new Color(0.05f, 0.75f, 0.22f));

    void SetBackground(Color color)
    {
        if (mainCamera != null) mainCamera.backgroundColor = color;
    }

    void SetStatus(string msg)
    {
        if (statusLabel != null) statusLabel.text = msg;
        Debug.Log($"[AvatarController] {msg}");
    }
}
