// このスクリプトは UniVRM パッケージのインストール完了後に自動でシーンを作成します。
// メニュー「VRMTracker → シーンを作成」でも手動実行できます。
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using VRMTracker.Network;
using VRMTracker.Avatar;
using VRMTracker.CameraControl;
using VRMTracker.UI;

[InitializeOnLoad]
static class ProjectAutoSetup
{
    const string DoneKey = "VRMTrackerSetupDone_v17";

    // ── デザインパレット（ポートフォリオの世界観: ライト + グラス + 3色グラデ） ──
    static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f,
                                           ((rgb >> 8) & 0xFF) / 255f,
                                           (rgb & 0xFF) / 255f, 1f);
    static readonly Color cText      = Hex(0x111113);
    static readonly Color cTextLight = Hex(0xC8C8D0);   // ダークバー上の淡色テキスト
    static readonly Color cOrange    = Hex(0xFF5E00);
    static readonly Color cBlue      = Hex(0x05D5FF);
    static readonly Color cPurple    = Hex(0xA855F7);
    static readonly Color cGlass     = new Color(0.11f, 0.11f, 0.12f, 0.94f);   // ダーク半透明バー（インスペクタと統一）

    static ProjectAutoSetup()
    {
        if (EditorPrefs.GetBool(DoneKey)) return;
        EditorApplication.delayCall += TrySetup;
    }

    [MenuItem("VRMTracker/シーンを作成")]
    static void SetupFromMenu()
    {
        EditorPrefs.DeleteKey(DoneKey);
        TrySetup();
    }

    public static void EnsureScene()
    {
        EditorPrefs.DeleteKey(DoneKey);
        TrySetup();
    }

    static void TrySetup()
    {
        if (Type.GetType("UniVRM10.Vrm10Instance, VRM10") == null)
        {
            Debug.Log("[ProjectAutoSetup] UniVRM のコンパイル待ち。パッケージ取得後に自動で再実行されます。");
            return;
        }
        SetupURP();
        EnsureUIToolkitAssets();
        BuildScene();
    }

    /// 左ドックインスペクタ用の PanelSettings（テーマ割当済み）を生成。
    /// 実行時に Resources から読み込むことで「No Theme Style Sheet」警告を回避する。
    public static void EnsureUIToolkitAssets()
    {
        const string themePath = "Assets/Resources/UI/UnityDefaultRuntimeTheme.tss";
        const string psPath    = "Assets/Resources/UI/InspectorPanelSettings.asset";

        var theme = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(themePath);
        if (theme == null)
        {
            Debug.LogWarning($"[ProjectAutoSetup] UITK テーマが見つかりません: {themePath}");
            return;
        }

        var ps = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(psPath);
        if (ps == null)
        {
            ps = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
            ps.themeStyleSheet = theme;   // 生成時にテーマを割当（警告回避）
            AssetDatabase.CreateAsset(ps, psPath);
        }
        ps.themeStyleSheet = theme;
        ps.scaleMode       = UnityEngine.UIElements.PanelScaleMode.ConstantPixelSize;
        ps.sortingOrder    = 10;          // uGUI バーより前面
        EditorUtility.SetDirty(ps);
        AssetDatabase.SaveAssets();
        Debug.Log("[ProjectAutoSetup] ✓ UI Toolkit PanelSettings 準備完了");
    }

    static void SetupURP()
    {
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Settings"));

        const string rendererPath = "Assets/Settings/URPRenderer.asset";
        const string assetPath    = "Assets/Settings/URPAsset.asset";

        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (rendererData == null)
        {
            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, rendererPath);
        }

        var urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
        if (urpAsset == null)
        {
            urpAsset = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(urpAsset, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        GraphicsSettings.defaultRenderPipeline = urpAsset;
        QualitySettings.renderPipeline = urpAsset;

        EnsureShadersIncluded();
        Debug.Log("[ProjectAutoSetup] ✓ URP configured as default render pipeline");
    }

    static void EnsureShadersIncluded()
    {
        var gsObjs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (gsObjs.Length == 0) return;

        var so = new SerializedObject(gsObjs[0]);
        var shadersProp = so.FindProperty("m_AlwaysIncludedShaders");

        // UniVRM package shaders (MToon, MToon10-URP, etc.)
        string[] searchFolders = {
            "Packages/com.vrmc.vrm",
            "Packages/com.vrmc.univrm",
            "Packages/com.vrmc.gltf"
        };
        var guids = AssetDatabase.FindAssets("t:Shader", searchFolders);
        int added = 0;
        foreach (var guid in guids)
        {
            var path   = AssetDatabase.GUIDToAssetPath(guid);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null) continue;

            bool found = false;
            for (int i = 0; i < shadersProp.arraySize; i++)
                if (shadersProp.GetArrayElementAtIndex(i).objectReferenceValue == shader) { found = true; break; }

            if (!found)
            {
                int idx = shadersProp.arraySize;
                shadersProp.InsertArrayElementAtIndex(idx);
                shadersProp.GetArrayElementAtIndex(idx).objectReferenceValue = shader;
                added++;
            }
        }

        if (added > 0)
        {
            so.ApplyModifiedProperties();
            Debug.Log($"[ProjectAutoSetup] Added {added} UniVRM shaders to Always Included Shaders");
        }
    }

    static void BuildScene()
    {
        // ── 新規シーン ─────────────────────────────────────────────────────
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── カメラ ─────────────────────────────────────────────────────────
        var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam   = camGO.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f);
        // yaw=180° → カメラをアバター正面（+Z 側）に配置
        cam.transform.SetPositionAndRotation(new Vector3(0, 1.4f, 2.0f), Quaternion.identity);
        camGO.AddComponent<AudioListener>();
        var camUrpData = camGO.AddComponent<UniversalAdditionalCameraData>();
        camUrpData.renderType = CameraRenderType.Base;
        camGO.AddComponent<CameraController>();

        // 背景画像用クアッド（カメラの子・アバターより奥・初期非表示）
        var bgQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bgQuad.name = "BackgroundQuad";
        UnityEngine.Object.DestroyImmediate(bgQuad.GetComponent<Collider>());
        bgQuad.transform.SetParent(camGO.transform, false);
        bgQuad.transform.localPosition = new Vector3(0, 0, 12f);
        bgQuad.transform.localRotation = Quaternion.identity;
        bgQuad.transform.localScale    = new Vector3(16, 9, 1);
        bgQuad.GetComponent<MeshRenderer>().sharedMaterial = BgMaterial();
        bgQuad.SetActive(false);

        // ── ライト（カメラが +Z 側 yaw=180° → 顔は +Z を向く → ライトは -Z 方向に照射） ──
        RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.30f, 0.30f, 0.30f);

        // キーライト: 顔の正面やや上から -Z 方向に照射
        var keyGO = new GameObject("Key Light");
        var key   = keyGO.AddComponent<Light>();
        key.type      = LightType.Directional;
        key.intensity = 0.50f;
        key.color     = Color.white;
        key.shadows   = LightShadows.None;
        keyGO.transform.eulerAngles = new Vector3(20, 180, 0);

        // フィルライト: 左斜め前
        var fillGO = new GameObject("Fill Light");
        var fill   = fillGO.AddComponent<Light>();
        fill.type      = LightType.Directional;
        fill.intensity = 0.20f;
        fill.color     = Color.white;
        fill.shadows   = LightShadows.None;
        fillGO.transform.eulerAngles = new Vector3(10, 120, 0);

        // リムライト: 右斜め前
        var rimGO = new GameObject("Rim Light");
        var rim   = rimGO.AddComponent<Light>();
        rim.type      = LightType.Directional;
        rim.intensity = 0.15f;
        rim.color     = Color.white;
        rim.shadows   = LightShadows.None;
        rimGO.transform.eulerAngles = new Vector3(10, 240, 0);

        // ── VRMController ──────────────────────────────────────────────────
        var ctrlGO     = new GameObject("VRMController");
        var netRecv    = ctrlGO.AddComponent<NetworkReceiver>();
        var faceMp     = ctrlGO.AddComponent<FaceMapper>();
        var boneMp     = ctrlGO.AddComponent<BoneMapper>();
        var avatarCtrl = ctrlGO.AddComponent<AvatarController>();

        var soCtrl = new SerializedObject(avatarCtrl);
        soCtrl.FindProperty("receiver").objectReferenceValue   = netRecv;
        soCtrl.FindProperty("faceMapper").objectReferenceValue = faceMp;
        soCtrl.FindProperty("boneMapper").objectReferenceValue = boneMp;
        soCtrl.FindProperty("mainCamera").objectReferenceValue = cam;
        soCtrl.ApplyModifiedPropertiesWithoutUndo();

        // ── Canvas ────────────────────────────────────────────────────────
        var canvasGO = new GameObject("Canvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        const float topH = 54f;

        // uGUI クローム（上バー・アクセント・ヒント）をまとめる領域。
        // 実行時に AvatarController が左端をインスペクタ幅ぶん右へ寄せ、
        // 左のインスペクタと右の表示エリアが重ならないようにする（ボタンが隠れて押せなくなる問題の解消）。
        var chromeGO = new GameObject("Chrome", typeof(RectTransform));
        var chromeRT = chromeGO.GetComponent<RectTransform>();
        chromeRT.SetParent(canvasGO.transform, false);
        chromeRT.anchorMin = Vector2.zero; chromeRT.anchorMax = Vector2.one;
        chromeRT.offsetMin = Vector2.zero; chromeRT.offsetMax = Vector2.zero;

        // ── 上バーのみ（下バーは廃止し操作はすべてインスペクタへ集約） ──────
        var topPanel = MakePanel(chromeGO, new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -topH), new Vector2(0, 0), cGlass, rounded: false);

        // 3色グラデーションのアクセントライン（上バー下端）
        MakeGradientLine(chromeGO, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -topH - 3), new Vector2(0, -topH));

        // ── 上バー：ステータス + FPS + 接続 ───────────────────────────────
        var statusGO = MakeText(topPanel, "StatusLabel",
            "左のインスペクタから VRM を読み込んでください",
            new Vector2(18, 0), new Vector2(300, 24),
            new Vector2(0, 0.5f), TextAnchor.MiddleLeft, 13, cTextLight);

        var fpsGO = MakeText(topPanel, "FPSLabel", "0 fps",
            new Vector2(330, 0), new Vector2(180, 24),
            new Vector2(0, 0.5f), TextAnchor.MiddleLeft, 12, cTextLight);

        var connGO = MakeText(topPanel, "ConnectionLabel",
            "UDP :49983  待機中",
            new Vector2(-18, 0), new Vector2(300, 24),
            new Vector2(1, 0.5f), TextAnchor.MiddleRight, 13, cTextLight);

        // ── EventSystem（forceModuleActive で確実に動作） ─────────────────
        var eventSysGO = new GameObject("EventSystem");
        eventSysGO.AddComponent<EventSystem>();
        var inputModule = eventSysGO.AddComponent<StandaloneInputModule>();
        inputModule.forceModuleActive = true;

        // ── UI 参照をコントローラにセット ────────────────────────────────
        var soUi = new SerializedObject(avatarCtrl);
        soUi.FindProperty("statusLabel").objectReferenceValue     = statusGO.GetComponent<Text>();
        soUi.FindProperty("connectionLabel").objectReferenceValue = connGO.GetComponent<Text>();
        soUi.FindProperty("fpsLabel").objectReferenceValue        = fpsGO.GetComponent<Text>();
        soUi.FindProperty("cameraController").objectReferenceValue = camGO.GetComponent<CameraController>();
        soUi.FindProperty("bgQuad").objectReferenceValue          = bgQuad;
        soUi.FindProperty("uiChrome").objectReferenceValue        = chromeRT;
        soUi.ApplyModifiedPropertiesWithoutUndo();

        // ── シーン保存 ─────────────────────────────────────────────────────
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
        const string scenePath = "Assets/Scenes/Main.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };

        EditorPrefs.SetBool(DoneKey, true);
        Debug.Log("[ProjectAutoSetup] ✓ シーン作成完了 (v17): " + scenePath);
        EditorUtility.DisplayDialog("セットアップ完了",
            "Assets/Scenes/Main.unity を作成しました。\n\n" +
            "▶ Play ボタンで動作確認できます。", "OK");
    }

    // ── UI ヘルパー ────────────────────────────────────────────────────────

    static Font GetFont()
    {
        var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
             ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Helvetica Neue", 14);
        return f;
    }

    // 角丸スプライト（Unity 組み込み UISprite の 9-slice）。エディタでは ExtraResource から取得。
    static Sprite _rounded; static bool _roundedTried;
    static Sprite RoundedSprite()
    {
        if (_roundedTried) return _rounded;
        _roundedTried = true;
        _rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        return _rounded;   // 取得失敗時は null → 角なし矩形にフォールバック
    }

    // 3色グラデーション Texture を生成してアセット化（RawImage で確実に表示。Sprite 取込の不確実性を回避）
    static Texture2D _accentTex;
    static Texture2D AccentTexture()
    {
        if (_accentTex != null) return _accentTex;
        const string path = "Assets/UI/AccentGradient.png";

        Directory.CreateDirectory(Path.Combine(Application.dataPath, "UI"));
        int w = 256, h = 8;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int x = 0; x < w; x++)
        {
            float t = x / (float)(w - 1);
            Color c = t < 0.5f ? Color.Lerp(cOrange, cPurple, t / 0.5f)
                               : Color.Lerp(cPurple, cBlue, (t - 0.5f) / 0.5f);
            for (int y = 0; y < h; y++) tex.SetPixel(x, y, c);
        }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(path) is TextureImporter imp)
        {
            imp.textureType = TextureImporterType.Default;
            imp.wrapMode    = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
        _accentTex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        return _accentTex;
    }

    // 背景画像用の URP/Unlit マテリアル（両面表示）をアセット化
    static Material BgMaterial()
    {
        const string path = "Assets/UI/Background.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
        m = new Material(sh);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // 両面表示
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "UI"));
        AssetDatabase.CreateAsset(m, path);
        AssetDatabase.SaveAssets();
        return m;
    }

    static GameObject MakePanel(GameObject parent, Vector2 anchorMin, Vector2 anchorMax,
                                Vector2 offsetMin, Vector2 offsetMax, Color color, bool rounded)
    {
        var go = new GameObject("Panel");
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        var rs = RoundedSprite();
        if (rounded && rs != null) { img.sprite = rs; img.type = Image.Type.Sliced; }
        return go;
    }

    static GameObject MakeGradientLine(GameObject parent, Vector2 anchorMin, Vector2 anchorMax,
                                       Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject("GradientLine");
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        var img = go.AddComponent<RawImage>();
        img.texture = AccentTexture();
        img.raycastTarget = false;
        return go;
    }

    static GameObject MakeText(GameObject parent, string name, string text,
                               Vector2 anchoredPos, Vector2 size,
                               Vector2 anchor, TextAnchor alignment, int fontSize,
                               Color color, bool bold = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
        rt.sizeDelta = size; rt.anchoredPosition = anchoredPos;
        var t = go.AddComponent<Text>();
        t.text = text; t.font = GetFont(); t.fontSize = fontSize;
        t.color = color; t.alignment = alignment;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow   = VerticalWrapMode.Overflow;
        return go;
    }

    // 注: 旧UIヘルパー（MakeButton / MakeInputField / MakeGradientMark）は、
    // 操作系をインスペクタ(UI Toolkit)へ集約した際に不要になったため削除。
    // 残る uGUI ヘルパーは GetFont / RoundedSprite / AccentTexture / BgMaterial /
    // MakePanel / MakeGradientLine / MakeText。ビルドは BuildScript が唯一の入口。
}
