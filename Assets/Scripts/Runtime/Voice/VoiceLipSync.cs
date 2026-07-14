using UnityEngine;
using UniVRM10;
using VRMTracker.Platform;

namespace VRMTracker.Voice
{
    /// <summary>
    /// マイク入力を解析して口パク（リップシンク）と控えめな頭の動きを生成する MonoBehaviour。
    /// AvatarController の LateUpdate から、ボーン・表情の書き込みの後に Apply() が呼ばれる。
    /// </summary>
    public class VoiceLipSync : MonoBehaviour
    {
        // マイク感度（RMS レベルの倍率）
        [Range(0.5f, 4f)] [SerializeField] private float micGain = 1.6f;
        // 口を開き始めるレベルのしきい値
        [Range(0f, 0.1f)] [SerializeField] private float openThreshold = 0.012f;
        // 開閉のスムージング速度（大きいほど追従が速い）
        [Range(1f, 30f)] [SerializeField] private float smooth = 14f;
        // 頭の揺れの大きさ
        [Range(0f, 0.3f)] [SerializeField] private float headMotionAmount = 0.08f;

        private Vrm10Instance _vrm;
        private Transform _head;
        private AudioClip _clip;
        private string _device;
        private float[] _samples;
        // 解析ウィンドウのサンプル数
        private const int Win = 1024;
        private float _open;   // 現在の口の開き具合（スムージング済み）
        private float _vowel;  // 母音の明るさ（ゼロクロス率ベース、スムージング済み）
        private bool _mic;     // マイク録音中フラグ
        private bool _enabled; // リップシンク有効フラグ

        /// <summary>
        /// リップシンクの有効/無効。設定を PlayerPrefs に保存し、マイクの開始/停止を行う。
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                PlayerPrefs.SetInt(PrefKeys.VoiceOn, value ? 1 : 0);
                if (value) StartMic();
                else StopMic();
            }
        }

        private void Awake()
        {
            _samples = new float[Win];
            // 設定を復元するが、ここではまだマイクを開始しない（OnEnable / Initialize で開始）
            _enabled = PlayerPrefs.GetInt(PrefKeys.VoiceOn, 0) == 1;
        }

        private void OnEnable()
        {
            if (_enabled) StartMic();
        }

        private void OnDisable()
        {
            StopMic();
        }

        /// <summary>
        /// VRM インスタンスを登録し、頭ボーンを取得する。有効なら未起動のマイクを開始。
        /// </summary>
        public void Initialize(Vrm10Instance vrm)
        {
            _vrm = vrm;
            _head = vrm != null ? vrm.GetComponent<Animator>()?.GetBoneTransform(HumanBodyBones.Head) : null;
            if (_enabled && !_mic) StartMic();
        }

        /// <summary>
        /// 既定のマイクデバイスで録音を開始する。デバイスが無ければ無効化する。
        /// </summary>
        private void StartMic()
        {
            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[VoiceLipSync] no mic");
                _enabled = false;
                return;
            }
            _device = null;
            _clip = Microphone.Start(_device, true, 1, 44100);
            _mic = true;
        }

        /// <summary>
        /// マイク録音を停止し、口の状態をリセットする。
        /// </summary>
        private void StopMic()
        {
            if (_mic)
            {
                try { Microphone.End(_device); } catch { }
                _mic = false;
            }
            _open = 0;
            _vowel = 0;
            ClearMouth();
        }

        /// <summary>
        /// 口の表情ウェイトをすべて 0 に戻す。
        /// </summary>
        private void ClearMouth()
        {
            if (_vrm == null) return;
            try { SetW(ExpressionPreset.aa, 0); } catch { }
            try { SetW(ExpressionPreset.ih, 0); } catch { }
            try { SetW(ExpressionPreset.ou, 0); } catch { }
            try { SetW(ExpressionPreset.oh, 0); } catch { }
        }

        /// <summary>
        /// AvatarController の LateUpdate から、ボーン・表情の書き込み後に呼ばれる。
        /// マイク波形を解析し、口の開閉と母音の混合、頭の揺れを適用する。
        /// </summary>
        public void Apply()
        {
            if (!_enabled || !_mic || _clip == null || _vrm == null) return;

            // 直近 Win サンプル分を取得
            int pos = Microphone.GetPosition(_device) - Win;
            if (pos < 0) return;
            _clip.GetData(_samples, pos);

            // RMS（音量）とゼロクロス率（母音の明るさの指標）を計算
            float sumSq = 0f;
            int zc = 0;
            for (int i = 0; i < Win; i++)
            {
                float s = _samples[i];
                sumSq += s * s;
                if (i > 0 && ((_samples[i - 1] < 0f) != (s < 0f))) zc++;
            }
            float rms = Mathf.Sqrt(sumSq / Win);
            float zcr = (float)zc / Win;

            float level = rms * micGain;
            // 音量を口の開き具合にマッピング
            float openTarget = Mathf.Clamp01(Mathf.InverseLerp(openThreshold, 0.25f, level));
            // ゼロクロス率を 0..1 に正規化
            float znorm = Mathf.Clamp01(zcr / 0.15f);

            // 指数スムージングで滑らかに追従
            float t = 1f - Mathf.Exp(-smooth * Time.deltaTime);
            _open = Mathf.Lerp(_open, openTarget, t);
            _vowel = Mathf.Lerp(_vowel, znorm, t);

            // 母音の混合：暗い(ou) <-> 明るい(ih)、中間は aa
            float ou = Mathf.Clamp01(1f - _vowel * 2f);
            float ih = Mathf.Clamp01(_vowel * 2f - 1f);
            float aa = Mathf.Clamp01(1f - ou - ih);
            SetW(ExpressionPreset.aa, _open * aa);
            SetW(ExpressionPreset.ih, _open * ih);
            SetW(ExpressionPreset.ou, _open * ou);
            SetW(ExpressionPreset.oh, 0);

            // 頭の揺れ：この frame のボーン書き込みの上に控えめに加算
            if (_head != null)
            {
                float nod = Mathf.Sin(Time.time * 7f) * _open * headMotionAmount;
                float sway = Mathf.Sin(Time.time * 3f) * _open * headMotionAmount * 0.6f;
                _head.localRotation *= Quaternion.Euler(nod * 12f, sway * 10f, 0f);
            }
        }

        /// <summary>
        /// 指定プリセットの表情ウェイトを設定する（例外は握りつぶす）。
        /// </summary>
        private void SetW(ExpressionPreset preset, float w)
        {
            try { _vrm.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(preset), w); } catch { }
        }
    }
}
