using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// モデルを一切編集せず、URP の描画（ポストプロセス・ライティング・MToon 調整）だけで
/// 「VRoid っぽい素の見た目」を抑え、キャラクター性のある絵作りにする。
///
/// AvatarController が起動時に Setup() を、VRM 読み込み後に ApplyToAvatar() を呼ぶ。
/// シーン編集は不要（必要なら同じ GameObject にこのコンポーネントを足して値を調整可能）。
public class VisualEnhancer : MonoBehaviour
{
    [Header("ポストプロセス")]
    [SerializeField] bool  enablePostProcessing = true;
    [SerializeField] bool  tonemapACES          = true;
    [SerializeField] bool  enableBloom          = true;
    [SerializeField] bool  enableColorGrading   = true;
    [SerializeField] bool  enableVignette       = true;
    [Tooltip("OBS クロマキー用に背景を平坦に保つ（Vignette を切り Bloom を弱める）")]
    [SerializeField] bool  obsChromaSafe        = false;

    [Header("Bloom")]
    [Range(0f, 2f)]  [SerializeField] float bloomIntensity = 0.55f;
    [Range(0f, 2f)]  [SerializeField] float bloomThreshold = 0.95f;

    [Header("Color Grading")]
    [Range(-2f, 2f)] [SerializeField] float postExposure = 0.08f;
    [Range(-50f,50f)][SerializeField] float contrast     = 10f;
    [Range(-50f,50f)][SerializeField] float saturation   = 6f;
    [Range(-50f,50f)][SerializeField] float temperature  = 6f;   // + で暖色
    [SerializeField] Color colorFilter = new Color(1.00f, 0.98f, 0.94f);

    [Header("Vignette")]
    [Range(0f, 1f)]  [SerializeField] float vignetteIntensity = 0.28f;

    [Header("リムライト（MToon・縁取り光）")]
    [SerializeField] bool  enableRimLight = true;
    [SerializeField] Color rimColor       = new Color(0.45f, 0.55f, 0.75f);  // 涼しげな縁光
    [Range(0f, 1f)]  [SerializeField] float rimLightingMix  = 1.0f;
    [Range(0f, 10f)] [SerializeField] float rimFresnelPower = 4.0f;

    [Header("陰影（MToon）")]
    [SerializeField] bool  tuneShade = true;
    [Tooltip("影色を少し濃く・寒色に寄せて立体感を出す乗算")]
    [SerializeField] Color shadeTint = new Color(0.85f, 0.86f, 0.95f);
    [Range(0f, 1f)]  [SerializeField] float shadingToony = 0.9f;

    [Header("バックライト（リム分離光）")]
    [SerializeField] bool  enableBackLight = true;
    [Range(0f, 3f)]  [SerializeField] float backLightIntensity = 0.7f;
    [SerializeField] Color backLightColor  = new Color(0.8f, 0.85f, 1.0f);

    Volume _volume;

    // MToon10 シェーダープロパティ ID（実名は UniVRM 0.128 で確認済み）
    static readonly int RimColorID      = Shader.PropertyToID("_RimColor");
    static readonly int RimLightMixID    = Shader.PropertyToID("_RimLightingMix");
    static readonly int RimFresnelID     = Shader.PropertyToID("_RimFresnelPower");
    static readonly int ShadeColorID     = Shader.PropertyToID("_ShadeColor");
    static readonly int ShadingToonyID   = Shader.PropertyToID("_ShadingToonyFactor");

    // ── Setup（カメラ・ポストプロセス・ライト） ───────────────────────────────

    public void Setup(Camera cam)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        if (enablePostProcessing)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing         = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality  = AntialiasingQuality.High;
            BuildVolume();
        }

        if (enableBackLight) BuildBackLight();
    }

    void BuildVolume()
    {
        if (_volume != null) return;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        if (tonemapACES)
        {
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
        }
        if (enableBloom)
        {
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(obsChromaSafe ? bloomIntensity * 0.4f : bloomIntensity);
            bloom.threshold.Override(bloomThreshold);
            bloom.scatter.Override(0.6f);
        }
        if (enableColorGrading)
        {
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(postExposure);
            ca.contrast.Override(contrast);
            ca.saturation.Override(saturation);
            ca.colorFilter.Override(colorFilter);

            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(temperature);
        }
        if (enableVignette && !obsChromaSafe)
        {
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(vignetteIntensity);
            vig.smoothness.Override(0.4f);
        }

        var go = new GameObject("PostProcessVolume");
        go.transform.SetParent(transform, false);
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 10;
        _volume.profile  = profile;
    }

    void BuildBackLight()
    {
        var go = new GameObject("BackRimLight");
        go.transform.SetParent(transform, false);
        // 後方やや上から差し込み、シルエットを背景から分離する
        go.transform.rotation = Quaternion.Euler(150f, 200f, 0f);
        var light = go.AddComponent<Light>();
        light.type      = LightType.Directional;
        light.color     = backLightColor;
        light.intensity = backLightIntensity;
        light.shadows   = LightShadows.None;   // 背景に影を落とさない（クロマキー保護）
    }

    // ── ApplyToAvatar（MToon 調整） ──────────────────────────────────────────

    public void ApplyToAvatar(GameObject avatarRoot)
    {
        if (avatarRoot == null || (!enableRimLight && !tuneShade)) return;

        foreach (var r in avatarRoot.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;

                if (enableRimLight && m.HasProperty(RimColorID))
                {
                    m.SetColor(RimColorID, rimColor);
                    if (m.HasProperty(RimLightMixID)) m.SetFloat(RimLightMixID, rimLightingMix);
                    if (m.HasProperty(RimFresnelID))  m.SetFloat(RimFresnelID, rimFresnelPower);
                }

                if (tuneShade && m.HasProperty(ShadeColorID))
                {
                    // 既存の影色に寒色寄りの色味を乗算して立体感を補強
                    Color s = m.GetColor(ShadeColorID);
                    m.SetColor(ShadeColorID, new Color(s.r * shadeTint.r, s.g * shadeTint.g, s.b * shadeTint.b, s.a));
                    if (m.HasProperty(ShadingToonyID)) m.SetFloat(ShadingToonyID, shadingToony);
                }
            }
        }
    }
}
