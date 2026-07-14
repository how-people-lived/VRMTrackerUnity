using UnityEngine;
using UITK = UnityEngine.UIElements;
using VRMTracker.Network;
using VRMTracker.Avatar;
using VRMTracker.CameraControl;
using VRMTracker.Visual;
using VRMTracker.Voice;
using VRMTracker.Comments;
using VRMTracker.Motions;
using VRMTracker.Platform;
using UniVRM10;
using System.Collections.Generic;

namespace VRMTracker.UI
{
    /// 左ドックの UI Toolkit インスペクタ（ダーク／macOS 風）。
    /// AvatarController から Initialize で各サービス参照を受け取り、ツリーを構築する。
    /// AppRoot 側は PointerOver / WidthFraction / SetDocVisible を参照する。
    public class InspectorUI : MonoBehaviour
    {
        // 依存（AppRoot から注入）
        AvatarController _app;
        NetworkReceiver  _receiver;
        BoneMapper       _bone;
        VisualEnhancer   _visual;
        CameraController  _cam;
        CommentDanmaku   _danmaku;
        YouTubeLiveChat  _yt;
        VoiceLipSync     _voice;
        BackgroundManager _bg;
        MotionPlayer     _motion;
        Font   _uiFont;
        string _localIp = "—";

        // UITK
        UITK.UIDocument    _uiDoc;
        UITK.VisualElement _inspectorRoot;
        bool _pointerOver;

        // ライブ更新するラベル
        UITK.VisualElement _statusDot;
        UITK.Label _statusHeaderLabel, _modelValueLabel, _connStatusLabel, _sourceLabel, _reconnectLabel, _ytStatusLabel;
        UITK.Label _diagCountLabel, _diagFromLabel, _diagRawLabel;
        UITK.Label _macIpLabel;
        float _ipRefreshTimer;

        int    _fpsCap = 60;
        string _ytApiKey = "", _ytVideo = "";

        // クロマキー定番色 + 無彩色プリセット
        static readonly (string label, Color color)[] BgPresets =
        {
            ("緑 (Chroma)",  new Color(0.000f, 0.690f, 0.314f)),
            ("青 (Chroma)",  new Color(0.000f, 0.278f, 0.729f)),
            ("黒",           new Color(0.080f, 0.080f, 0.080f)),
            ("白",           new Color(0.930f, 0.930f, 0.930f)),
            ("グレー",       new Color(0.200f, 0.200f, 0.200f)),
            ("マゼンタ",     new Color(1.000f, 0.000f, 1.000f)),
        };

        static readonly (string label, int w, int h)[] ResPresets =
        {
            ("1280x720",  1280, 720),
            ("1920x1080", 1920, 1080),
            ("720x1280",  720, 1280),
        };

        // ── 公開 API（AppRoot が参照） ──────────────────────────────────────
        public bool PointerOver => _pointerOver;

        public float WidthFraction
        {
            get
            {
                if (_uiDoc == null || _inspectorRoot == null) return 0f;
                float panelW = _uiDoc.rootVisualElement.resolvedStyle.width;
                float insW   = _inspectorRoot.resolvedStyle.width;
                if (float.IsNaN(panelW) || panelW <= 1f || float.IsNaN(insW) || insW <= 0f) return 0f;
                return Mathf.Clamp01(insW / panelW);
            }
        }

        public void SetDocVisible(bool visible)
        {
            if (_uiDoc != null)
                _uiDoc.rootVisualElement.style.display = visible ? UITK.DisplayStyle.Flex : UITK.DisplayStyle.None;
        }

        // ── 構築 ─────────────────────────────────────────────────────────────
        public void Initialize(AvatarController app, NetworkReceiver receiver, BoneMapper bone, FaceMapper face,
                               VisualEnhancer visual, CameraController cam, CommentDanmaku danmaku, YouTubeLiveChat yt,
                               VoiceLipSync voice, BackgroundManager bg, MotionPlayer motion, Font uiFont, string localIp)
        {
            _app = app; _receiver = receiver; _bone = bone; _visual = visual; _cam = cam;
            _danmaku = danmaku; _yt = yt; _voice = voice; _bg = bg; _motion = motion; _uiFont = uiFont; _localIp = localIp;

            _ytApiKey = PlayerPrefs.GetString(PrefKeys.YtApi, "");
            _ytVideo  = PlayerPrefs.GetString(PrefKeys.YtVideo, "");

            BuildInspector();
        }

        void BuildInspector()
        {
            var styles = Resources.Load<UITK.StyleSheet>("UI/InspectorStyles");

            // テーマ割当済みの PanelSettings を読み込む（無ければ実行時生成にフォールバック）
            var ps = Resources.Load<UITK.PanelSettings>("UI/InspectorPanelSettings");
            if (ps == null)
            {
                var theme = Resources.Load<UITK.ThemeStyleSheet>("UI/UnityDefaultRuntimeTheme");
                if (theme == null) { Debug.LogError("[InspectorUI] UITK テーマ/PanelSettings が見つかりません"); return; }
                ps = ScriptableObject.CreateInstance<UITK.PanelSettings>();
                ps.themeStyleSheet = theme;
                ps.scaleMode       = UITK.PanelScaleMode.ConstantPixelSize;
                ps.sortingOrder    = 10;
            }

            // 非アクティブな子に追加 → panelSettings 設定後に有効化（No-Theme 警告回避）
            var docGO = new GameObject("InspectorUIDocument");
            docGO.transform.SetParent(transform, false);
            docGO.SetActive(false);
            _uiDoc = docGO.AddComponent<UITK.UIDocument>();
            _uiDoc.panelSettings = ps;
            docGO.SetActive(true);

            var root = _uiDoc.rootVisualElement;
            if (styles != null) root.styleSheets.Add(styles);
            if (_uiFont != null) root.style.unityFontDefinition = UITK.FontDefinition.FromFont(_uiFont);

            _inspectorRoot = new UITK.VisualElement();
            _inspectorRoot.AddToClassList("inspector-root");
            _inspectorRoot.RegisterCallback<UITK.PointerEnterEvent>(_ => _pointerOver = true);
            _inspectorRoot.RegisterCallback<UITK.PointerLeaveEvent>(_ => _pointerOver = false);
            root.Add(_inspectorRoot);

            _fpsCap = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60;
            BuildTree();
        }

        void BuildTree()
        {
            if (_inspectorRoot == null) return;
            _inspectorRoot.Clear();

            var scroll = new UITK.ScrollView(UITK.ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.contentContainer.AddToClassList("scroll-content");
            _inspectorRoot.Add(scroll);

            // ── 接続ステータスヘッダ ──
            var header = new UITK.VisualElement();
            header.AddToClassList("status-header");
            _statusDot = new UITK.VisualElement();
            _statusDot.AddToClassList("status-dot");
            _statusHeaderLabel = new UITK.Label("待機中");
            _statusHeaderLabel.AddToClassList("status-text");
            header.Add(_statusDot);
            header.Add(_statusHeaderLabel);
            scroll.Add(header);

            // ── モデル ──
            var sModel = Section(scroll, "モデル");
            sModel.Add(PrimaryButton("VRM を読み込む", () => _app?.OnLoadButtonClick()));
            _modelValueLabel = Muted($"現在: {ModelText()}");
            sModel.Add(_modelValueLabel);
            sModel.Add(Btn("スナップショット保存（PNG）", () => _app?.RequestSnapshot()));

            // ── コントロールパッド ──
            BuildControlPad(Section(scroll, "コントロールパッド"));

            // ── 接続 ──
            var sConn = Section(scroll, "接続");

            // このMacのIP（iOSアプリの送信先に入力する値）。ネットワーク変更に追従して更新。
            _macIpLabel = Strong($"このMacのIP: {_localIp}");
            sConn.Add(_macIpLabel);
            sConn.Add(Muted("↑【推奨】iOSアプリ側の「送信先/宛先IP」にこの値を入れてLive開始"));

            // iPhone IP（Mac発トリガ用・任意）
            sConn.Add(Muted("〔Mac発で接続する場合のみ〕iPhoneのIPを入力"));
            var ipField = new UITK.TextField("iPhone IP") { value = _app != null ? _app.IPhoneIp : "" };
            ipField.RegisterCallback<UITK.ChangeEvent<string>>(e => { if (_app != null) _app.IPhoneIp = e.newValue; });
            sConn.Add(ipField);

            // FaceMotion3D（主）
            if (_receiver != null && _receiver.listenFaceMotion3D)
            {
                sConn.Add(Muted($"■ FaceMotion3D（受信ポート {_receiver.faceMotion3DPort}）"));
                sConn.Add(PrimaryButton("FaceMotion3D 接続", () =>
                {
                    var ip = ipField.value;
                    if (_app != null) _app.IPhoneIp = ip;
                    _receiver.SendFaceMotion3DStart(ip);
                    _app?.Notify($"FaceMotion3D 接続要求を送信 ({ip}:{_receiver.faceMotion3DPort})");
                }));
            }

            // iFacialMocap（副・互換用）
            if (_receiver != null && _receiver.listenIFacialMocap)
            {
                sConn.Add(Muted($"■ iFacialMocap（受信ポート {_receiver.iFacialMocapPort}）"));
                sConn.Add(Btn("iFacialMocap 接続", () =>
                {
                    var ip = ipField.value;
                    if (_app != null) _app.IPhoneIp = ip;
                    _receiver.SendIFacialMocapHandshake(ip);
                    _app?.Notify($"iFacialMocap 接続要求を送信 ({ip}:{_receiver.iFacialMocapPort})");
                }));
            }

            sConn.Add(Muted($"カスタムアプリ送信先: {_localIp} : {(_receiver != null ? _receiver.port : 0)}"));

            _connStatusLabel = Muted("状態: 待機中");
            sConn.Add(_connStatusLabel);
            _sourceLabel = Muted(""); _sourceLabel.style.display = UITK.DisplayStyle.None;
            sConn.Add(_sourceLabel);
            _reconnectLabel = Muted(""); _reconnectLabel.style.display = UITK.DisplayStyle.None;
            sConn.Add(_reconnectLabel);

            // ── 接続診断（つながらない時の切り分け用・既定で展開） ──
            var sDiag = Section(scroll, "接続診断");
            sDiag.Add(Muted("接続後この数字が増えれば通信は届いています"));
            _diagCountLabel = Strong("受信パケット: 0");
            _diagFromLabel  = Muted("送信元: —");
            _diagRawLabel   = Muted("生データ: —");
            _diagRawLabel.style.whiteSpace = UITK.WhiteSpace.Normal;
            sDiag.Add(_diagCountLabel);
            sDiag.Add(_diagFromLabel);
            sDiag.Add(_diagRawLabel);

            // ── トラッキング ──
            if (_bone != null)
                Section(scroll, "トラッキング").Add(
                    SliderRow("頭部スムージング", 1f, 30f, _bone.HeadSmoothSpeed, v => _bone.HeadSmoothSpeed = v, "F1"));

            // ── 音声リップシンク ──
            var sVoice = Section(scroll, "音声リップシンク");
            if (_voice != null)
            {
                var tog = new UITK.Toggle(" マイクで口・頭を動かす") { value = _voice.Enabled };
                tog.RegisterCallback<UITK.ChangeEvent<bool>>(e => _voice.Enabled = e.newValue);
                sVoice.Add(tog);
                sVoice.Add(Muted("マイク音声に合わせて口（あ/い/う）と頭が動きます"));
            }

            // ── 表情（ワンタッチ切替：配信リアクション用） ──
            var sExpr = Section(scroll, "表情");
            _exprButtons.Clear();
            var exprRow = Row();
            AddExprBtn(exprRow, "喜", ExpressionPreset.happy);
            AddExprBtn(exprRow, "怒", ExpressionPreset.angry);
            AddExprBtn(exprRow, "哀", ExpressionPreset.sad);
            AddExprBtn(exprRow, "驚", ExpressionPreset.surprised);
            AddExprBtn(exprRow, "楽", ExpressionPreset.relaxed);
            sExpr.Add(exprRow);
            sExpr.Add(Btn("中立（解除）", () => { _app?.ClearExpression(); RefreshExprHighlight(); }));
            sExpr.Add(Muted("トラッキング表情に上書き。もう一度押すと解除"));
            RefreshExprHighlight();

            // ── モーション（.vrma） ──
            var sMot = Section(scroll, "モーション", expanded: false);
            sMot.Add(PrimaryButton("モーション(.vrma)を読み込む", () => _motion?.PickAndPlay()));
            if (_motion != null)
            {
                var found = _motion.ScanFolder();
                foreach (var (name, path) in found)
                    sMot.Add(Btn($"▶ {name}", () => _motion.PlayFile(path)));
                if (found.Count == 0)
                    sMot.Add(Muted("StreamingAssets/Motions に .vrma を置くと一覧表示"));
                var loopTog = new UITK.Toggle(" ループ再生") { value = _motion.Loop };
                loopTog.RegisterCallback<UITK.ChangeEvent<bool>>(e => _motion.Loop = e.newValue);
                sMot.Add(loopTog);
            }
            var motRow = Row();
            motRow.Add(Btn("停止", () => _motion?.Stop()));
            motRow.Add(Btn("一覧更新", () => BuildTree()));
            sMot.Add(motRow);
            sMot.Add(Muted("無償・再配布可の .vrma（VRM Animation）に対応"));

            // ── 立ちポーズ ──
            if (_bone != null)
            {
                var sPose = Section(scroll, "立ちポーズ");
                sPose.Add(SliderRow("腕の下げ", -1f, 0f,     _bone.ArmDown,    v => _bone.ArmDown = v));
                sPose.Add(SliderRow("腕の前後", -0.6f, 0.6f, _bone.ArmFront,   v => _bone.ArmFront = v));
                sPose.Add(SliderRow("肘の曲げ", -0.5f, 0.5f, _bone.Elbow,      v => _bone.Elbow = v));
                sPose.Add(SliderRow("指の曲げ", -1f, 0.3f,   _bone.FingerCurl, v => _bone.FingerCurl = v));
            }

            // ── 背景 ──
            var sBg = Section(scroll, "背景色（クロマキー）", expanded: false);
            var bgRow = Row();
            foreach (var (label, color) in BgPresets) bgRow.Add(Btn(label, () => _bg?.SetBackground(color)));
            sBg.Add(bgRow);
            Color c0 = _bg != null ? _bg.CurrentColor : Color.green;
            sBg.Add(SliderRow("R", 0f, 1f, c0.r, v => { var c = _bg.CurrentColor; _bg.SetBackground(new Color(v, c.g, c.b)); }));
            sBg.Add(SliderRow("G", 0f, 1f, c0.g, v => { var c = _bg.CurrentColor; _bg.SetBackground(new Color(c.r, v, c.b)); }));
            sBg.Add(SliderRow("B", 0f, 1f, c0.b, v => { var c = _bg.CurrentColor; _bg.SetBackground(new Color(c.r, c.g, v)); }));
            var imgRow = Row();
            imgRow.Add(Btn("画像を選択", () => _bg?.PickBackgroundImage()));
            imgRow.Add(Btn("画像をクリア", () => _bg?.ClearBackgroundImage()));
            sBg.Add(imgRow);

            // ── カメラ ──
            if (_cam != null)
            {
                var sCam = Section(scroll, "カメラ");
                sCam.Add(SliderRow("高さ", 0.5f, 1.8f, _cam.Height,   v => _cam.Height = v));
                sCam.Add(SliderRow("距離", 0.3f, 6f,   _cam.Distance, v => _cam.Distance = v));
                sCam.Add(SliderRow("画角", 15f, 70f,   _cam.Fov,      v => _cam.Fov = v, "F0"));
                sCam.Add(Btn("視点をリセット", () => _cam.ResetView()));
            }

            // ── 表示 ──
            if (_visual != null)
            {
                var sView = Section(scroll, "表示");
                var tog = new UITK.Toggle(" ポストプロセス（映画調）") { value = _visual.PostProcessingOn };
                tog.RegisterCallback<UITK.ChangeEvent<bool>>(e => _visual.SetPostProcessing(e.newValue));
                sView.Add(tog);
            }

            // ── YouTube コメント ──
            var sYt = Section(scroll, "YouTube コメント", expanded: false);
            var keyField = new UITK.TextField("APIキー") { value = _ytApiKey, isPasswordField = true };
            keyField.RegisterCallback<UITK.ChangeEvent<string>>(e => { _ytApiKey = e.newValue; PlayerPrefs.SetString(PrefKeys.YtApi, _ytApiKey); });
            sYt.Add(keyField);
            var vidField = new UITK.TextField("動画URL/ID") { value = _ytVideo };
            vidField.RegisterCallback<UITK.ChangeEvent<string>>(e => { _ytVideo = e.newValue; PlayerPrefs.SetString(PrefKeys.YtVideo, _ytVideo); });
            sYt.Add(vidField);
            var ytRow = Row();
            ytRow.Add(PrimaryButton("接続", () =>
            {
                if (_yt != null && _danmaku != null)
                    _yt.Connect(_ytVideo, _ytApiKey, (a, t) => _danmaku.Enqueue(a, t));
            }));
            ytRow.Add(Btn("切断", () => { _yt?.Disconnect(); _danmaku?.Clear(); }));
            sYt.Add(ytRow);
            _ytStatusLabel = Muted("状態: 未接続");
            sYt.Add(_ytStatusLabel);
            sYt.Add(Btn("コメントを消去", () => _danmaku?.Clear()));

            // ── 出力 ──
            var sOut = Section(scroll, "出力", expanded: false);
            var resRow = Row();
            foreach (var (label, w, ht) in ResPresets)
                resRow.Add(Btn(label, () => Screen.SetResolution(w, ht, FullScreenMode.Windowed)));
            sOut.Add(resRow);
            sOut.Add(SliderRow("FPS 上限", 30f, 120f, _fpsCap, v =>
            {
                _fpsCap = Mathf.RoundToInt(v / 10f) * 10;
                Application.targetFrameRate = _fpsCap;
            }, "F0"));

            // ── 保存 ──
            var sSave = Section(scroll, "保存（カメラ・背景）", expanded: false);
            var saveRow = Row();
            saveRow.Add(Btn("保存", () => _app?.SaveSettings()));
            saveRow.Add(Btn("読込", () => { _app?.LoadSettings(); BuildTree(); }));
            sSave.Add(saveRow);
            sSave.Add(Btn("UI を隠す（クリーンモード / H）", () => _app?.RequestCleanMode()));

            var hint = new UITK.Label("H: 上下バーを隠す（OBS用 / インスペクタは常時表示）");
            hint.AddToClassList("hint");
            scroll.Add(hint);
        }

        void BuildControlPad(UITK.VisualElement parent)
        {
            if (_cam == null) return;
            const float yawStep = 2.5f, pitchStep = 2.0f, zoomStep = 0.03f;

            var up = PadRow();
            up.Add(PadButton("▲", () => _cam.Orbit(0f, +pitchStep)));
            parent.Add(up);

            var mid = PadRow();
            mid.Add(PadButton("◀", () => _cam.Orbit(-yawStep, 0f)));
            mid.Add(PadBtn("中央", () => _cam.ResetView()));
            mid.Add(PadButton("▶", () => _cam.Orbit(+yawStep, 0f)));
            parent.Add(mid);

            var down = PadRow();
            down.Add(PadButton("▼", () => _cam.Orbit(0f, -pitchStep)));
            parent.Add(down);

            var zoom = Row();
            zoom.Add(PadButton("ズーム +", () => _cam.Zoom(+zoomStep)));
            zoom.Add(PadButton("ズーム −", () => _cam.Zoom(-zoomStep)));
            parent.Add(zoom);
        }

        // ── ライブ更新（自前 Update） ─────────────────────────────────────────
        void Update()
        {
            if (_inspectorRoot == null) return;

            if (_modelValueLabel != null) _modelValueLabel.text = $"現在: {ModelText()}";
            if (_ytStatusLabel != null && _yt != null) _ytStatusLabel.text = $"状態: {_yt.StatusText}";

            // MacのIPを定期再取得（テザリング等でネットワークが変わっても最新を表示）
            _ipRefreshTimer -= Time.deltaTime;
            if (_ipRefreshTimer <= 0f && _macIpLabel != null)
            {
                _ipRefreshTimer = 2f;
                _localIp = LanIpProbe.GetLocalIPAddress();
                _macIpLabel.text = $"このMacのIP: {_localIp}";
            }

            if (_receiver == null) return;

            // 接続診断（生の受信状況）
            if (_diagCountLabel != null)
            {
                _diagCountLabel.text = $"受信パケット: {_receiver.RawPacketCount}";
                _diagFromLabel.text  = $"送信元: {_receiver.LastRawFrom} ({_receiver.LastRawSource})";
                _diagRawLabel.text   = $"生: {(string.IsNullOrEmpty(_receiver.LastRawText) ? "—" : _receiver.LastRawText)}";
            }

            bool live    = ConnectionState.IsLive(_receiver);
            bool waiting = ConnectionState.Waiting(_receiver);

            if (_statusDot != null)
                _statusDot.style.backgroundColor = live ? ConnectionState.DotLive
                                                        : waiting ? ConnectionState.DotWaiting : ConnectionState.DotIdle;
            if (_statusHeaderLabel != null)
                _statusHeaderLabel.text = live ? $"受信中 — {_receiver.ActiveSource}"
                                               : waiting ? "信号待ち…" : "待機中";
            if (_connStatusLabel != null)
                _connStatusLabel.text = $"状態: {(live ? $"{_receiver.SenderIP} から受信中" : "待機中")}";
            if (_sourceLabel != null)
            {
                if (live) { _sourceLabel.text = $"プロトコル: {_receiver.ActiveSource}"; _sourceLabel.style.display = UITK.DisplayStyle.Flex; }
                else _sourceLabel.style.display = UITK.DisplayStyle.None;
            }
            if (_reconnectLabel != null)
            {
                if (_receiver.ReconnectCount > 0)
                {
                    _reconnectLabel.text = $"再接続回数: {_receiver.ReconnectCount}";
                    _reconnectLabel.style.display = UITK.DisplayStyle.Flex;
                }
                else _reconnectLabel.style.display = UITK.DisplayStyle.None;
            }
        }

        string ModelText() => string.IsNullOrEmpty(_app?.ModelName) ? "未読込" : _app.ModelName;

        // ── 表情ボタン（アクティブなものをハイライト） ─────────────────────────
        readonly List<(UITK.Button b, ExpressionPreset p)> _exprButtons = new List<(UITK.Button, ExpressionPreset)>();

        void AddExprBtn(UITK.VisualElement row, string label, ExpressionPreset p)
        {
            var b = Btn(label, () => { _app?.ToggleExpression(p); RefreshExprHighlight(); });
            _exprButtons.Add((b, p));
            row.Add(b);
        }

        void RefreshExprHighlight()
        {
            var cur = _app?.CurrentExpression;
            foreach (var (b, p) in _exprButtons)
            {
                if (cur.HasValue && cur.Value == p) b.AddToClassList("btn-active");
                else b.RemoveFromClassList("btn-active");
            }
        }

        // ── UITK 部品ヘルパー ─────────────────────────────────────────────────
        UITK.VisualElement Section(UITK.VisualElement parent, string title, bool expanded = true)
        {
            var f = new UITK.Foldout { text = title, value = expanded };
            parent.Add(f);
            return f;
        }

        static UITK.Label Muted(string text)  { var l = new UITK.Label(text); l.AddToClassList("muted");        return l; }
        static UITK.Label Strong(string text) { var l = new UITK.Label(text); l.AddToClassList("value-strong"); return l; }

        static UITK.VisualElement Row()    { var r = new UITK.VisualElement(); r.AddToClassList("row");     return r; }
        static UITK.VisualElement PadRow() { var r = new UITK.VisualElement(); r.AddToClassList("pad-row"); return r; }

        static UITK.Button Btn(string text, System.Action onClick)
        {
            var b = new UITK.Button(onClick) { text = text };
            b.AddToClassList("btn");
            return b;
        }

        static UITK.Button PrimaryButton(string text, System.Action onClick)
        {
            var b = Btn(text, onClick);
            b.AddToClassList("btn-primary");
            return b;
        }

        static UITK.Button PadBtn(string text, System.Action onClick)
        {
            var b = Btn(text, onClick);
            b.AddToClassList("pad-btn");
            return b;
        }

        // 押している間 16ms ごとに onStep を繰り返す方向ボタン
        static UITK.Button PadButton(string text, System.Action onStep)
        {
            var b = new UITK.Button { text = text };
            b.AddToClassList("btn");
            b.AddToClassList("pad-btn");
            UITK.IVisualElementScheduledItem repeat = null;
            void Stop() { repeat?.Pause(); repeat = null; }
            b.RegisterCallback<UITK.PointerDownEvent>(_ =>
            {
                onStep();
                repeat = b.schedule.Execute(onStep).Every(16);
            });
            b.RegisterCallback<UITK.PointerUpEvent>(_   => Stop());
            b.RegisterCallback<UITK.PointerLeaveEvent>(_ => Stop());
            return b;
        }

        UITK.VisualElement SliderRow(string label, float min, float max, float val,
                                     System.Action<float> onChange, string fmt = "F2")
        {
            var wrap = new UITK.VisualElement();
            var lbl  = new UITK.Label($"{label}: {val.ToString(fmt)}");
            lbl.AddToClassList("slider-label");
            var s = new UITK.Slider(min, max) { value = val };
            s.RegisterCallback<UITK.ChangeEvent<float>>(e =>
            {
                onChange(e.newValue);
                lbl.text = $"{label}: {e.newValue.ToString(fmt)}";
            });
            wrap.Add(lbl);
            wrap.Add(s);
            return wrap;
        }
    }
}
