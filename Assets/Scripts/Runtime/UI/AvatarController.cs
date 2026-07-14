using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UniVRM10;
using VRMTracker.Network;
using VRMTracker.Avatar;
using VRMTracker.CameraControl;
using VRMTracker.Visual;
using VRMTracker.Voice;
using VRMTracker.Comments;
using VRMTracker.Motions;
using VRMTracker.Platform;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VRMTracker.UI
{
    /// アプリのオーケストレータ（旧 AvatarController を分割した中核）。
    /// クラス名は AvatarController のまま（シーン/エディタ配線互換）。
    /// 直列化フィールド（ProjectAutoSetup が配線）を保持し、トラッキングポンプ
    /// （bone→face→voice→motion→表情）とライフサイクルを回し、UI・背景・スナップショット等は各サービスへ委譲する。
    public class AvatarController : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] NetworkReceiver receiver;
        [SerializeField] FaceMapper      faceMapper;
        [SerializeField] BoneMapper      boneMapper;

        [Header("UI - Labels")]
        [SerializeField] internal Text statusLabel;
        [SerializeField] internal Text connectionLabel;
        [SerializeField] internal Text fpsLabel;

        [Header("Scene")]
        [SerializeField] Camera           mainCamera;
        [SerializeField] CameraController cameraController;
        [SerializeField] GameObject       bgQuad;
        [SerializeField] RectTransform    uiChrome;   // 上下バー等をまとめた領域（右へ寄せて分割）

        [Header("Performance")]
        [Tooltip("目標フレームレート。OBS配信は60で十分")]
        [SerializeField] int targetFrameRate = 60;

        // ランタイム付与サービス
        Vrm10Instance    currentVrm;
        VisualEnhancer   visualEnhancer;
        BackgroundManager _bg;
        CommentDanmaku   _danmaku;
        YouTubeLiveChat  _ytChat;
        VoiceLipSync     _voice;
        MotionPlayer     _motion;
        InspectorUI      _inspector;

        Canvas uiCanvas;
        bool   cleanMode;      // 上下バーのみ隠す（インスペクタは常時表示）
        bool   loading;
        float  lastFpsTime;
        int    frameCount, lastFps;

        Font   _uiFont;
        string _localIp = "—";
        string _modelName;

        /// インスペクタ上にポインタがある間 true。CameraController がカメラ操作を止める。
        internal static bool PointerOverSettings;

        ExpressionPreset? _expr;   // 手動表情オーバーライド（喜/怒/哀/驚/楽）。null で解除

        // ── 公開プロパティ／ファサード（InspectorUI・uGUI から利用） ──────────
        public string ModelName => _modelName;

        internal ExpressionPreset? CurrentExpression => _expr;
        internal void ToggleExpression(ExpressionPreset p) => _expr = (_expr.HasValue && _expr.Value == p) ? (ExpressionPreset?)null : p;
        internal void ClearExpression() => _expr = null;

        /// iPhone の IP（iFacialMocap / FaceMotion3D 共通）。PlayerPrefs キーは互換のため IfmIp のまま。
        public string IPhoneIp
        {
            get => PlayerPrefs.GetString(PrefKeys.IfmIp, "");
            set => PlayerPrefs.SetString(PrefKeys.IfmIp, value ?? "");
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Start()
        {
            Application.targetFrameRate = targetFrameRate;
            QualitySettings.vSyncCount  = 0;
            Application.runInBackground  = true;

            uiCanvas = statusLabel != null ? statusLabel.canvas : FindFirstObjectByType<Canvas>();

            _uiFont  = FontLoader.LoadUIFont();
            FontLoader.ApplyToCanvas(uiCanvas, _uiFont);
            _localIp = LanIpProbe.GetLocalIPAddress();

            // 背景管理（色・画像・クアッド追従）
            _bg = gameObject.AddComponent<BackgroundManager>();
            _bg.Setup(mainCamera, bgQuad, SetStatus);
            _bg.LoadBackgroundPref();

            // 絵作り
            visualEnhancer = GetComponent<VisualEnhancer>() ?? gameObject.AddComponent<VisualEnhancer>();
            visualEnhancer.Setup(mainCamera);

            // YouTube コメント弾幕
            _danmaku = gameObject.AddComponent<CommentDanmaku>();
            _danmaku.Setup(mainCamera, cameraController, _uiFont);
            _ytChat  = gameObject.AddComponent<YouTubeLiveChat>();

            // 音声リップシンク
            _voice = gameObject.AddComponent<VoiceLipSync>();

            // モーション（.vrma）
            _motion = gameObject.AddComponent<MotionPlayer>();
            _motion.Setup(SetStatus);

            // 左ドックインスペクタ（UI Toolkit）
            _inspector = gameObject.AddComponent<InspectorUI>();
            _inspector.Initialize(this, receiver, boneMapper, faceMapper, visualEnhancer, cameraController,
                                  _danmaku, _ytChat, _voice, _bg, _motion, _uiFont, _localIp);

#if UNITY_STANDALONE_OSX
            _ = NativeFileDialog.PrewarmFileDialog();
#endif

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--vrm-path" && File.Exists(args[i + 1])) { _ = LoadVRMAsync(args[i + 1]); break; }
        }

        void Update()
        {
            frameCount++;
            if (Time.time - lastFpsTime >= 1f) { lastFps = frameCount; frameCount = 0; lastFpsTime = Time.time; }

            if (connectionLabel != null && receiver != null) UpdateConnectionLabel();

            if (Input.GetKeyDown(KeyCode.H)) SetCleanMode(!cleanMode);

            PointerOverSettings = _inspector != null && _inspector.PointerOver;
            UpdateCameraViewport();
        }

        void LateUpdate()
        {
            _bg?.SizeBgQuad();   // 背景をカメラ視野に追従（アバター未読込でも実行）

            if (currentVrm == null) return;

            // モーション再生中は body を UniVRM のリターゲットに明け渡す（BoneMapper は停止）
            bool motion = _motion != null && _motion.IsPlaying;
            if (boneMapper != null) boneMapper.SuspendIdle = motion;

            bool live = ConnectionState.IsLive(receiver);
            if (!live)
            {
                faceMapper?.ResetToNeutral();
                if (!motion) boneMapper?.ReturnHeadToNeutral();
                if (_voice != null && _voice.Enabled) _voice.Apply();   // 音声だけで口を動かせる
                ApplyIdleBlink();          // 未トラッキング時は自動まばたきで生き生きと
                ApplyExpressionOverride(); // 手動表情を維持
                UpdateDiagLabel(null, false);
                return;
            }

            var frame = receiver.GetFrame();
            if (frame == null) return;

            if (!motion) boneMapper?.Apply(frame.face);                 // モーション中は頭も motion 側
            if (frame.face != null) faceMapper?.Apply(frame.face);
            if (_voice != null && _voice.Enabled) _voice.Apply();       // face の後に口を上書き
            ApplyExpressionOverride();                                  // 手動表情を最後に上書き

            UpdateDiagLabel(frame, true);
        }

        // 手動表情オーバーライド（FaceMapper が毎フレームクリアするので最後に適用）
        void ApplyExpressionOverride()
        {
            if (currentVrm == null || !_expr.HasValue) return;
            try { currentVrm.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(_expr.Value), 1f); }
            catch { }
        }

        // 未トラッキング時の自動まばたき（約4.2秒周期・0.12秒だけ閉じる）
        void ApplyIdleBlink()
        {
            if (currentVrm == null) return;
            const float cycle = 4.2f, dur = 0.12f;
            float t = Time.time % cycle;
            if (t >= dur) return;
            float w = Mathf.Sin(t / dur * Mathf.PI);   // 0→1→0
            try
            {
                currentVrm.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.blinkLeft),  w);
                currentVrm.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.blinkRight), w);
            }
            catch { }
        }

        void UpdateDiagLabel(TrackingFrame frame, bool live)
        {
            if (fpsLabel == null) return;
            if (!live) { fpsLabel.text = $"{lastFps} fps  待機中"; return; }
            string faceStr  = frame?.face != null ? "Face✓" : "Face—";
            int    rc       = receiver?.ReconnectCount ?? 0;
            string reconn   = rc > 0 ? $"  再接続:{rc}" : "";
            fpsLabel.text   = $"{lastFps} fps  {faceStr}{reconn}";
        }

        void UpdateConnectionLabel()
        {
            if (!ConnectionState.Received(receiver))
            {
                connectionLabel.text  = $"UDP :{receiver.iFacialMocapPort}  待機中";
                connectionLabel.color = ConnectionState.BarIdle;
                return;
            }
            if (ConnectionState.BarActive(receiver))
            {
                string dot = (Time.time % 0.5f < 0.25f) ? "●" : "○";
                connectionLabel.text  = $"{dot} {receiver.SenderIP}  受信中";
                connectionLabel.color = ConnectionState.BarActiveColor;
            }
            else if (ConnectionState.Waiting(receiver))
            {
                connectionLabel.text  = $"○ {receiver.SenderIP}  待機中...";
                connectionLabel.color = ConnectionState.BarWaiting;
            }
            else
            {
                connectionLabel.text  = $"UDP :{receiver.iFacialMocapPort}  待機中";
                connectionLabel.color = ConnectionState.BarIdle;
            }
        }

        // ── 表示エリア分割（カメラ＋uGUIバーをインスペクタ右側に限定） ──────────
        void UpdateCameraViewport()
        {
            float frac = _inspector != null ? _inspector.WidthFraction : 0f;

            // カメラ描画を右側に限定
            if (mainCamera != null)
            {
                var rect = new Rect(frac, 0f, 1f - frac, 1f);
                if (mainCamera.rect != rect) mainCamera.rect = rect;
            }

            // uGUI クローム（上下バー・ヒント）も右側へ寄せる（インスペクタ裏に潜り込ませない）。
            // アンカーは正規化値なのでウィンドウリサイズにも自動追従する。
            if (uiChrome != null && !Mathf.Approximately(uiChrome.anchorMin.x, frac))
            {
                uiChrome.anchorMin = new Vector2(frac, 0f);
                uiChrome.anchorMax = Vector2.one;
                uiChrome.offsetMin = Vector2.zero;
                uiChrome.offsetMax = Vector2.zero;
            }
        }

        // ── クリーンモード（上下バーのみ隠す） ────────────────────────────────
        public void RequestCleanMode() => SetCleanMode(true);

        void SetCleanMode(bool clean)
        {
            cleanMode = clean;
            if (uiCanvas != null) uiCanvas.enabled = !clean;
        }

        // ── スナップショット（撮影の瞬間だけ全UIを隠して PNG 保存） ────────────
        public void RequestSnapshot() => StartCoroutine(SnapshotRoutine());

        IEnumerator SnapshotRoutine()
        {
            bool barWas = uiCanvas == null || uiCanvas.enabled;
            if (uiCanvas != null) uiCanvas.enabled = false;
            _inspector?.SetDocVisible(false);   // 非表示 → 次フレームで全画面ビューポート

            yield return null;
            yield return new WaitForEndOfFrame();

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
            try { if (!Directory.Exists(dir)) dir = Application.persistentDataPath; } catch { dir = Application.persistentDataPath; }
            string path = Path.Combine(dir, $"VRMTracker_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForEndOfFrame();
            yield return null;

            if (uiCanvas != null) uiCanvas.enabled = barWas;
            _inspector?.SetDocVisible(true);
            SetStatus($"スナップショットを保存: {path}");
        }

        // ── VRM 読み込み ──────────────────────────────────────────────────────
        public async void OnLoadButtonClick()
        {
            if (loading) return;
#if UNITY_EDITOR || UNITY_STANDALONE_OSX
            SetStatus("ファイルを選択中…");
            string path = await NativeFileDialog.PickVrm();
            if (string.IsNullOrEmpty(path)) { SetStatus("キャンセルされました"); return; }
            if (!path.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase)) { SetStatus(".vrm ファイルを選択してください"); return; }
            await LoadVRMAsync(path);
#else
            SetStatus("VRMファイルのパスをコマンドライン引数 --vrm-path で指定してください");
#endif
        }

#if UNITY_EDITOR
        // エディタのみ: VRM ファイルのドラッグ&ドロップ受付
        void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.DragUpdated) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.Use(); }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var p in DragAndDrop.paths)
                    if (p.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase)) { _ = LoadVRMAsync(p); break; }
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
                _voice?.Initialize(instance);
                _motion?.SetAvatar(instance);
                visualEnhancer?.ApplyToAvatar(instance.gameObject);
                _modelName = Path.GetFileNameWithoutExtension(path);
                SetStatus($"✓ {_modelName}");

                if (cameraController != null)
                {
                    var humanoid = instance.GetComponent<Animator>();
                    if (humanoid != null && humanoid.isHuman)
                    {
                        var chest = humanoid.GetBoneTransform(HumanBodyBones.Chest);
                        var hips  = humanoid.GetBoneTransform(HumanBodyBones.Hips);
                        if (chest != null)      cameraController.SetTarget(chest.position);
                        else if (hips != null)  cameraController.SetTarget(hips.position + Vector3.up * 0.5f);
                    }
                }
            }
            catch (Exception e)
            {
                SetStatus($"読込エラー: {e.Message}");
                Debug.LogError($"[AvatarController] {e}");
            }
            loading = false;
        }

        // ── 設定の保存／読込 ─────────────────────────────────────────────────
        public void SaveSettings()
        {
            cameraController?.SaveState();   // 背景色・画像は変更時に保存済み
            PlayerPrefs.Save();
            SetStatus("カメラ・背景設定を保存しました");
        }

        public void LoadSettings()
        {
            cameraController?.LoadState();
            _bg?.LoadBackgroundPref();
            SetStatus("保存した設定を読み込みました");
        }

        void SetStatus(string msg)
        {
            if (statusLabel != null) statusLabel.text = msg;
            Debug.Log($"[AvatarController] {msg}");
        }

        /// InspectorUI などからステータス欄へメッセージを出す。
        internal void Notify(string msg) => SetStatus(msg);
    }
}
