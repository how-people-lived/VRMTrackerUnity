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

[InitializeOnLoad]
static class ProjectAutoSetup
{
    const string DoneKey = "VRMTrackerSetupDone_v12";

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
        BuildScene();
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

        // ── Hint panel（VRM 未読み込み時のガイド） ────────────────────────
        var hintPanelGO = MakeFullRect(canvasGO, "HintPanel",
            new Vector2(0, 58), new Vector2(0, -44),
            new Color(0.06f, 0.06f, 0.06f, 0.88f));

        MakeText(hintPanelGO, "HintTitle",
            "アバター未読み込み",
            new Vector2(0, 32), new Vector2(700, 42),
            new Vector2(0.5f, 0.5f), TextAnchor.MiddleCenter, 24);

        var hintSub = MakeText(hintPanelGO, "HintSub",
            "下の「VRM を開く」ボタンからファイルを選択してください",
            new Vector2(0, -18), new Vector2(700, 28),
            new Vector2(0.5f, 0.5f), TextAnchor.MiddleCenter, 14);
        hintSub.GetComponent<Text>().color = new Color(0.75f, 0.75f, 0.75f, 0.7f);

        // ── Top panel（ステータス + 接続） ────────────────────────────────
        var topPanel = MakePanel(canvasGO, new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -44), new Vector2(0, 0),
            new Color(0f, 0f, 0f, 0.55f));

        var statusGO = MakeText(topPanel, "StatusLabel",
            "「VRM を開く」でファイルを選択してください",
            new Vector2(12, 0), new Vector2(760, 28),
            new Vector2(0, 0.5f), TextAnchor.MiddleLeft, 14);

        var connGO = MakeText(topPanel, "ConnectionLabel",
            "UDP :12345  待機中",
            new Vector2(-12, 0), new Vector2(280, 28),
            new Vector2(1, 0.5f), TextAnchor.MiddleRight, 13);
        connGO.GetComponent<Text>().color = new Color(0.5f, 0.9f, 0.5f);

        // ── Bottom panel（コントロール群） ────────────────────────────────
        var bottomPanel = MakePanel(canvasGO, new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(0, 0), new Vector2(0, 58),
            new Color(0f, 0f, 0f, 0.55f));

        // VRM を開くボタン（ボタン参照だけ保存 → リスナーは AvatarController.Start() で登録）
        var loadBtnGO = MakeButton(bottomPanel, "LoadButton", "VRM を開く",
            new Vector2(10, 0), new Vector2(130, 38), new Vector2(0, 0.5f));

        // 背景色ボタン
        var bgBlackBtnGO = MakeButton(bottomPanel, "BgBlackButton", "黒",
            new Vector2(152, 0), new Vector2(48, 28), new Vector2(0, 0.5f));
        bgBlackBtnGO.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 0.95f);

        var bgWhiteBtnGO = MakeButton(bottomPanel, "BgWhiteButton", "白",
            new Vector2(208, 0), new Vector2(48, 28), new Vector2(0, 0.5f));
        bgWhiteBtnGO.GetComponent<Image>().color = new Color(0.88f, 0.88f, 0.88f, 0.95f);
        bgWhiteBtnGO.GetComponentInChildren<Text>().color = new Color(0.12f, 0.12f, 0.12f);

        var bgGreenBtnGO = MakeButton(bottomPanel, "BgGreenButton", "緑キー",
            new Vector2(264, 0), new Vector2(68, 28), new Vector2(0, 0.5f));
        bgGreenBtnGO.GetComponent<Image>().color = new Color(0.05f, 0.75f, 0.22f, 0.95f);

        // FPS ラベル
        var fpsGO = MakeText(bottomPanel, "FPSLabel",
            "0 fps",
            new Vector2(-232, 0), new Vector2(80, 24),
            new Vector2(1, 0.5f), TextAnchor.MiddleRight, 12);
        fpsGO.GetComponent<Text>().color = new Color(0.6f, 0.6f, 0.6f);

        // パス入力フィールド（フォールバック）
        var pathInput = MakeInputField(bottomPanel, "PathInputField",
            "パスを入力 → Enter",
            new Vector2(-10, 0), new Vector2(220, 32), new Vector2(1, 0.5f));

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
        soUi.FindProperty("pathInputField").objectReferenceValue  = pathInput.GetComponent<InputField>();
        soUi.FindProperty("hintPanel").objectReferenceValue       = hintPanelGO;
        soUi.FindProperty("cameraController").objectReferenceValue = camGO.GetComponent<CameraController>();
        soUi.FindProperty("loadButton").objectReferenceValue      = loadBtnGO.GetComponent<Button>();
        soUi.FindProperty("bgBlackButton").objectReferenceValue   = bgBlackBtnGO.GetComponent<Button>();
        soUi.FindProperty("bgWhiteButton").objectReferenceValue   = bgWhiteBtnGO.GetComponent<Button>();
        soUi.FindProperty("bgGreenButton").objectReferenceValue   = bgGreenBtnGO.GetComponent<Button>();
        soUi.ApplyModifiedPropertiesWithoutUndo();

        // ── シーン保存 ─────────────────────────────────────────────────────
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
        const string scenePath = "Assets/Scenes/Main.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };

        EditorPrefs.SetBool(DoneKey, true);
        Debug.Log("[ProjectAutoSetup] ✓ シーン作成完了 v7: " + scenePath);
        EditorUtility.DisplayDialog("セットアップ完了",
            "Assets/Scenes/Main.unity を作成しました（v5）。\n\n" +
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

    static GameObject MakeFullRect(GameObject parent, string name,
                                    Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        go.AddComponent<Image>().color         = color;
        go.GetComponent<Image>().raycastTarget = false;
        return go;
    }

    static GameObject MakePanel(GameObject parent, Vector2 anchorMin, Vector2 anchorMax,
                                  Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        var go = new GameObject("Panel");
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        var img            = go.AddComponent<Image>();
        img.color          = color;
        img.raycastTarget  = false;
        return go;
    }

    static GameObject MakeText(GameObject parent, string name, string text,
                                 Vector2 anchoredPos, Vector2 size,
                                 Vector2 anchor, TextAnchor alignment, int fontSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
        rt.sizeDelta = size; rt.anchoredPosition = anchoredPos;
        var t = go.AddComponent<Text>();
        t.text      = text;
        t.font      = GetFont();
        t.fontSize  = fontSize;
        t.color     = Color.white;
        t.alignment = alignment;
        var sh = go.AddComponent<Shadow>();
        sh.effectColor    = new Color(0, 0, 0, 0.7f);
        sh.effectDistance = new Vector2(1, -1);
        return go;
    }

    static GameObject MakeInputField(GameObject parent, string name, string placeholder,
                                      Vector2 anchoredPos, Vector2 size, Vector2 anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
        rt.sizeDelta = size; rt.anchoredPosition = anchoredPos;
        go.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(go.transform, false);
        var trt = txtGO.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(6, 2); trt.offsetMax = new Vector2(-6, -2);
        var txt = txtGO.AddComponent<Text>();
        txt.font = GetFont(); txt.fontSize = 12;
        txt.color = Color.white; txt.alignment = TextAnchor.MiddleLeft;

        var phGO = new GameObject("Placeholder");
        phGO.transform.SetParent(go.transform, false);
        var phrt = phGO.AddComponent<RectTransform>();
        phrt.anchorMin = Vector2.zero; phrt.anchorMax = Vector2.one;
        phrt.offsetMin = new Vector2(6, 2); phrt.offsetMax = new Vector2(-6, -2);
        var phTxt = phGO.AddComponent<Text>();
        phTxt.text = placeholder; phTxt.font = GetFont(); phTxt.fontSize = 12;
        phTxt.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
        phTxt.alignment = TextAnchor.MiddleLeft; phTxt.fontStyle = FontStyle.Italic;

        var field = go.AddComponent<InputField>();
        field.textComponent  = txt;
        field.placeholder    = phTxt;
        field.characterLimit = 512;
        return go;
    }

    static GameObject MakeButton(GameObject parent, string name, string label,
                                   Vector2 anchoredPos, Vector2 size, Vector2 anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
        rt.sizeDelta = size; rt.anchoredPosition = anchoredPos;
        go.AddComponent<Image>().color = new Color(0.18f, 0.42f, 0.88f, 0.92f);
        go.AddComponent<Button>();

        var lblGO = new GameObject("Label");
        lblGO.transform.SetParent(go.transform, false);
        var lrt = lblGO.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.sizeDelta = Vector2.zero; lrt.anchoredPosition = Vector2.zero;
        var t = lblGO.AddComponent<Text>();
        t.text = label; t.font = GetFont();
        t.fontSize = 14; t.color = Color.white; t.alignment = TextAnchor.MiddleCenter;
        return go;
    }

    [MenuItem("VRMTracker/macOS ビルド")]
    public static void BuildMacOS()
    {
        EnsureScene();
        string outPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "Build/macOS/VRMTracker.app");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes       = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = outPath,
            target       = BuildTarget.StandaloneOSX,
            options      = BuildOptions.None,
        });
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            Debug.Log($"[ProjectAutoSetup] ✓ ビルド完了: {outPath}");
        else
            Debug.LogError($"[ProjectAutoSetup] ✗ ビルド失敗: {report.summary.result}");
    }
}
