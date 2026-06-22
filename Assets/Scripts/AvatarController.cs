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

    [Header("Performance")]
    [Tooltip("目標フレームレート。OBS配信は60で十分")]
    [SerializeField] int targetFrameRate = 60;

    Vrm10Instance currentVrm;
    VisualEnhancer visualEnhancer;
    bool  loading;
    float lastFpsTime;
    int   frameCount;
    int   lastFps;

    // OBS / 背景・表示まわり
    Canvas uiCanvas;
    bool   cleanMode;
    bool   showPanel;
    Color  bgColor = new Color(0.05f, 0.75f, 0.22f);   // 既定はクロマキー緑

    const string BgKeyR = "bgR", BgKeyG = "bgG", BgKeyB = "bgB";

    // クロマキー定番色 + 無彩色プリセット
    static readonly (string label, Color color)[] BgPresets =
    {
        ("緑 (Chroma)",  new Color(0.000f, 0.690f, 0.314f)),  // #00B050 系
        ("青 (Chroma)",  new Color(0.000f, 0.278f, 0.729f)),  // #0047BA 系
        ("黒",           new Color(0.080f, 0.080f, 0.080f)),
        ("白",           new Color(0.930f, 0.930f, 0.930f)),
        ("グレー",       new Color(0.200f, 0.200f, 0.200f)),
        ("マゼンタ",     new Color(1.000f, 0.000f, 1.000f)),
    };

    // 出力解像度プリセット（OBS キャプチャ用に固定したい場合）
    static readonly (string label, int w, int h)[] ResPresets =
    {
        ("1280x720",  1280, 720),
        ("1920x1080", 1920, 1080),
        ("720x1280",  720, 1280),   // 縦
    };

    // ── Lifecycle ──────────────────────────────────────────────────────────

    void Start()
    {
        // パフォーマンス & OBS 連携の基本設定
        Application.targetFrameRate = targetFrameRate;
        QualitySettings.vSyncCount  = 0;            // フレームレートを targetFrameRate で制御
        Application.runInBackground  = true;        // 非フォーカス時もレンダリング継続（OBS で重要）

        loadButton?.onClick.AddListener(OnLoadButtonClick);
        bgBlackButton?.onClick.AddListener(SetBackgroundBlack);
        bgWhiteButton?.onClick.AddListener(SetBackgroundWhite);
        bgGreenButton?.onClick.AddListener(SetBackgroundGreen);

        uiCanvas = statusLabel != null ? statusLabel.canvas : FindFirstObjectByType<Canvas>();
        LoadBackgroundPref();

        // 絵作り（ポストプロセス・ライト・MToon 調整）。シーン編集不要で自動付与。
        visualEnhancer = GetComponent<VisualEnhancer>() ?? gameObject.AddComponent<VisualEnhancer>();
        visualEnhancer.Setup(mainCamera);

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

        // OBS 用ショートカット: H = UI を隠すクリーンモード / Tab = コントロールパネル
        if (Input.GetKeyDown(KeyCode.H))   SetCleanMode(!cleanMode);
        if (Input.GetKeyDown(KeyCode.Tab)) showPanel = !showPanel;
    }

    // ── Clean mode (OBS キャプチャ用に UI を隠す) ───────────────────────────

    void SetCleanMode(bool clean)
    {
        cleanMode = clean;
        if (uiCanvas != null) uiCanvas.enabled = !clean;   // Canvas を無効化（描画コストも消える）
        if (clean) showPanel = false;
    }

    void LateUpdate()
    {
        if (currentVrm == null) return;
        var frame = receiver?.GetFrame();
        if (frame == null) return;

        boneMapper?.Apply(frame.face);
        if (frame.face != null) faceMapper?.Apply(frame.face);

        // 診断: 表情データ受信状況をラベルに表示
        if (fpsLabel != null)
        {
            string f = frame.face != null ? "Face✓" : "Face—";
            fpsLabel.text = $"{lastFps} fps  {f}";
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

    void OnGUI()
    {
#if UNITY_EDITOR
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
#endif
        DrawControlPanel();
    }

    // ── OBS / 背景コントロールパネル（シーン編集不要の IMGUI） ────────────────

    void DrawControlPanel()
    {
        if (cleanMode) return;   // クリーンモード中は何も描かない

        // 操作ヒント（右上）
        var hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight };
        hintStyle.normal.textColor = new Color(1, 1, 1, 0.5f);
        GUI.Label(new Rect(Screen.width - 260, 8, 252, 20),
                  "Tab: パネル   H: UIを隠す(OBS用)", hintStyle);

        if (!showPanel) return;

        GUILayout.BeginArea(new Rect(10, 10, 280, 320), GUI.skin.box);
        GUILayout.Label("背景色（クロマキー）");
        GUILayout.BeginHorizontal();
        for (int i = 0; i < BgPresets.Length; i++)
        {
            if (GUILayout.Button(BgPresets[i].label, GUILayout.Height(26)))
                SetBackground(BgPresets[i].color);
            if (i % 2 == 1) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("カスタム RGB");
        float r = GUILayout.HorizontalSlider(bgColor.r, 0f, 1f);
        float g = GUILayout.HorizontalSlider(bgColor.g, 0f, 1f);
        float b = GUILayout.HorizontalSlider(bgColor.b, 0f, 1f);
        if (!Mathf.Approximately(r, bgColor.r) || !Mathf.Approximately(g, bgColor.g) || !Mathf.Approximately(b, bgColor.b))
            SetBackground(new Color(r, g, b));

        GUILayout.Space(6);
        GUILayout.Label("出力解像度");
        GUILayout.BeginHorizontal();
        foreach (var (label, w, h) in ResPresets)
            if (GUILayout.Button(label, GUILayout.Height(24)))
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        if (GUILayout.Button(cleanMode ? "UI を表示" : "UI を隠す (クリーンモード / H)", GUILayout.Height(28)))
            SetCleanMode(!cleanMode);

        GUILayout.EndArea();
    }

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
            visualEnhancer?.ApplyToAvatar(instance.gameObject);
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
    public void SetBackgroundGreen() => SetBackground(new Color(0.000f, 0.690f, 0.314f));

    void SetBackground(Color color)
    {
        bgColor = color;
        if (mainCamera != null)
        {
            mainCamera.clearFlags      = CameraClearFlags.SolidColor;  // クロマキー用に単色塗り
            mainCamera.backgroundColor = color;
        }
        PlayerPrefs.SetFloat(BgKeyR, color.r);
        PlayerPrefs.SetFloat(BgKeyG, color.g);
        PlayerPrefs.SetFloat(BgKeyB, color.b);
    }

    void LoadBackgroundPref()
    {
        if (PlayerPrefs.HasKey(BgKeyR))
            bgColor = new Color(PlayerPrefs.GetFloat(BgKeyR),
                                PlayerPrefs.GetFloat(BgKeyG),
                                PlayerPrefs.GetFloat(BgKeyB));
        SetBackground(bgColor);
    }

    void SetStatus(string msg)
    {
        if (statusLabel != null) statusLabel.text = msg;
        Debug.Log($"[AvatarController] {msg}");
    }
}
