using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UniGLTF;
using UniVRM10;
using VRMTracker.Platform;

namespace VRMTracker.Motions
{
    /// VRM Animation（.vrma）モーションの読み込み・再生。
    /// UniVRM の Vrm10Runtime.VrmAnimation による自動リターゲットを利用する
    /// （公式サンプル SimpleVrma と同じ方式）。
    /// StreamingAssets/Motions/*.vrma を自動スキャンし、ファイル選択でも読み込める。
    /// ※ .vrma は VRM コンソーシアムが無償・再配布可のサンプルを配布している。
    public class MotionPlayer : MonoBehaviour
    {
        Vrm10Instance       _vrm;
        RuntimeGltfInstance _current;   // 再生中の vrma インスタンス
        Action<string>      _onStatus;
        bool                _loading;

        public bool   IsPlaying   { get; private set; }
        public bool   Loop        { get; set; } = true;
        public string CurrentName { get; private set; } = "";

        public void Setup(Action<string> onStatus) => _onStatus = onStatus;

        public void SetAvatar(Vrm10Instance vrm)
        {
            Stop();
            _vrm = vrm;
        }

        /// StreamingAssets/Motions 内の .vrma を列挙（表示名, フルパス）。
        public List<(string name, string path)> ScanFolder()
        {
            var list = new List<(string, string)>();
            try
            {
                var dir = Path.Combine(Application.streamingAssetsPath, "Motions");
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.vrma"))
                        list.Add((Path.GetFileNameWithoutExtension(f), f));
            }
            catch { }
            return list;
        }

        /// ファイル選択ダイアログから .vrma を選んで再生。
        public async void PickAndPlay()
        {
            var path = await NativeFileDialog.PickVrma();
            if (!string.IsNullOrEmpty(path)) PlayFile(path);
        }

        /// 指定パスの .vrma を読み込んで再生。
        public async void PlayFile(string path)
        {
            if (_vrm == null) { _onStatus?.Invoke("先に VRM を読み込んでください"); return; }
            if (_loading || string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            _loading = true;
            _onStatus?.Invoke($"モーション読込中: {Path.GetFileName(path)} …");
            try
            {
                StopInternal();
                using var data   = new AutoGltfFileParser(path).Parse();
                using var loader = new VrmAnimationImporter(data);
                var instance = await loader.LoadAsync(new ImmediateCaller());

                var vrma = instance.GetComponent<Vrm10AnimationInstance>();
                if (vrma == null)
                {
                    _onStatus?.Invoke("モーションの読込に失敗しました");
                    if (instance != null) Destroy(instance.gameObject);
                    _loading = false;
                    return;
                }

                _current = instance;
                if (vrma.BoxMan != null) vrma.BoxMan.enabled = false;   // 可視化用ボックスマンは隠す
                _vrm.Runtime.VrmAnimation = vrma;                       // ← 自動リターゲット開始

                var anim = vrma.GetComponent<Animation>();
                if (anim != null)
                {
                    anim.wrapMode = Loop ? WrapMode.Loop : WrapMode.Once;
                    anim.Play();
                }

                IsPlaying   = true;
                CurrentName = Path.GetFileNameWithoutExtension(path);
                _onStatus?.Invoke($"▶ モーション再生: {CurrentName}");
            }
            catch (Exception e)
            {
                _onStatus?.Invoke($"モーションエラー: {e.Message}");
                Debug.LogError($"[MotionPlayer] {e}");
            }
            _loading = false;
        }

        public void Stop()
        {
            StopInternal();
            IsPlaying   = false;
            CurrentName = "";
        }

        void StopInternal()
        {
            if (_vrm != null && _vrm.Runtime != null) _vrm.Runtime.VrmAnimation = null;
            if (_current != null) { Destroy(_current.gameObject); _current = null; }
        }

        void OnDisable() => StopInternal();
    }
}
