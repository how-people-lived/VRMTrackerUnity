using System;
using System.Collections.Generic;
using UnityEngine;
using UniVRM10;
using VRMTracker.Network;

namespace VRMTracker.Avatar
{
    /// 顔（頭・首）を iPhone のフェイストラッキングで駆動し、それ以外は自然な立ちポーズで固定する。
    ///
    /// 立ちポーズは Unity の **マッスル空間 (HumanPose)** で生成する。FromToRotation で腕方向だけ
    /// 合わせる旧方式は前腕のロールが破綻して不自然になりやすいが、マッスル空間なら解剖学的に
    /// 妥当な姿勢が保証される。読み込み時に一度ポーズを作ってボーンのローカル回転を控え、
    /// 毎フレームそれを再適用する（頭・首だけ表情で上書き）。
    public class BoneMapper : MonoBehaviour
    {
        private Vrm10Instance vrm;
        private Animator      animator;
        private readonly Dictionary<HumanBodyBones, Quaternion> restRotations = new();
        private readonly Dictionary<HumanBodyBones, Quaternion> idleLocalRot  = new();

        [Header("自然な立ちポーズ（マッスル空間 -1..1）")]
        [Tooltip("上腕の上げ下げ。負で下げる（-1で体側に密着／0で水平）")]
        [Range(-1f, 0f)] [SerializeField] float armDown   = -0.50f;
        [Tooltip("上腕の前後。+で後方へ／-で前方へ。腕を体の真横に揃えるため少し後ろへ")]
        [Range(-0.6f, 0.6f)] [SerializeField] float armFront = 0.15f;
        [Tooltip("肘の曲げ。0=触れない(Tポーズの真っ直ぐを維持)。値を入れると前方に曲がる")]
        [Range(-0.5f, 0.5f)] [SerializeField] float elbow   = 0f;
        [Tooltip("指の曲げ。負で軽く握る")]
        [Range(-1f, 0.3f)] [SerializeField] float fingerCurl = -0.15f;
        [Tooltip("親指の曲げ")]
        [Range(-1f, 0.3f)] [SerializeField] float thumbCurl  = -0.10f;

        [Header("スムージング")]
        [Tooltip("頭部回転の補間速度。大きいほど追従が速い（8=適度、20=ほぼ遅延なし）")]
        [Range(1f, 30f)] [SerializeField] float headSmoothSpeed = 8f;

        Quaternion _smoothHead = Quaternion.identity;
        Quaternion _smoothNeck = Quaternion.identity;
        bool _smoothInit;

        // 立ちポーズで固定するボーン（腕・手・指）。頭/首/胴/脚は触らない。
        static readonly HumanBodyBones[] DrivenBones =
        {
            HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftThumbProximal,  HumanBodyBones.LeftThumbIntermediate,  HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.LeftIndexProximal,  HumanBodyBones.LeftIndexIntermediate,  HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal,   HumanBodyBones.LeftRingIntermediate,   HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
            HumanBodyBones.RightThumbProximal,  HumanBodyBones.RightThumbIntermediate,  HumanBodyBones.RightThumbDistal,
            HumanBodyBones.RightIndexProximal,  HumanBodyBones.RightIndexIntermediate,  HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal,   HumanBodyBones.RightRingIntermediate,   HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
        };

        // ── Initialize ────────────────────────────────────────────────────────

        public void Initialize(Vrm10Instance instance)
        {
            vrm      = instance;
            animator = instance.GetComponent<Animator>();
            CaptureRest();
            BuildIdlePose();
        }

        // ── 実行時に立ちポーズを調整（設定パネルから） ───────────────────────────
        public float ArmDown       { get => armDown;        set { armDown = value;        BuildIdlePose(); } }
        public float ArmFront      { get => armFront;       set { armFront = value;       BuildIdlePose(); } }
        public float Elbow         { get => elbow;          set { elbow = value;          BuildIdlePose(); } }
        public float FingerCurl    { get => fingerCurl;     set { fingerCurl = value;     BuildIdlePose(); } }
        public float HeadSmoothSpeed { get => headSmoothSpeed; set => headSmoothSpeed = Mathf.Clamp(value, 1f, 30f); }

        void CaptureRest()
        {
            if (animator == null) return;
            foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (b == HumanBodyBones.LastBone) continue;
                var t = animator.GetBoneTransform(b);
                if (t != null) restRotations[b] = t.localRotation;
            }
        }

        /// マッスル空間で自然な立ちポーズを生成し、駆動ボーンのローカル回転を控える。
        void BuildIdlePose()
        {
            idleLocalRot.Clear();
            if (animator == null || animator.avatar == null || !animator.isHuman) return;

            using var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);

            // 復元用に現在（Tポーズ）の値を退避
            var savedMuscles = (float[])pose.muscles.Clone();
            var savedPos = pose.bodyPosition;
            var savedRot = pose.bodyRotation;

            void SetMuscle(string name, float v)
            {
                int i = Array.IndexOf(HumanTrait.MuscleName, name);
                if (i >= 0) pose.muscles[i] = v;
            }

            // 腕を下ろす（左右対称）
            SetMuscle("Left Arm Down-Up",  armDown);
            SetMuscle("Right Arm Down-Up", armDown);
            SetMuscle("Left Arm Front-Back",  armFront);
            SetMuscle("Right Arm Front-Back", armFront);
            // 肘マッスルは 0 が「真っ直ぐ」ではないため、明示指定時のみ触る（既定は触れず真っ直ぐ維持）
            if (Mathf.Abs(elbow) > 0.001f)
            {
                SetMuscle("Left Forearm Stretch",  elbow);
                SetMuscle("Right Forearm Stretch", elbow);
            }

            // 指を軽く曲げる
            foreach (var side in new[] { "Left", "Right" })
                foreach (var finger in new[] { "Index", "Middle", "Ring", "Little" })
                    for (int n = 1; n <= 3; n++)
                        SetMuscle($"{side} {finger} {n} Stretched", fingerCurl);
            foreach (var side in new[] { "Left", "Right" })
                for (int n = 1; n <= 3; n++)
                    SetMuscle($"{side} Thumb {n} Stretched", thumbCurl);

            handler.SetHumanPose(ref pose);

            // 生成された姿勢のローカル回転を控える
            foreach (var b in DrivenBones)
            {
                var t = animator.GetBoneTransform(b);
                if (t != null) idleLocalRot[b] = t.localRotation;
            }

            // ボディを元（Tポーズ）に戻す。腕・指は毎フレーム idleLocalRot で再適用する。
            Array.Copy(savedMuscles, pose.muscles, savedMuscles.Length);
            pose.bodyPosition = savedPos;
            pose.bodyRotation = savedRot;
            handler.SetHumanPose(ref pose);
        }

        // ── 顔（頭・首）トラッキング ─────────────────────────────────────────────

        public void Apply(FaceData face)
        {
            if (vrm == null || animator == null) return;
            if (face?.headRotation == null) return;

            var q  = face.headRotation;
            var eu = YXZEuler(new Quaternion(q.x, q.y, q.z, q.w));

            var targetHead = RestRot(HumanBodyBones.Head) * FromYXZ(eu.x * 0.60f, -eu.y * 0.60f, eu.z * 0.60f);
            var targetNeck = RestRot(HumanBodyBones.Neck) * FromYXZ(eu.x * 0.25f, -eu.y * 0.25f, eu.z * 0.25f);

            // 初回フレームは補間なし（ゼロ位置からのポップを防ぐ）
            if (!_smoothInit) { _smoothHead = targetHead; _smoothNeck = targetNeck; _smoothInit = true; }

            // フレームレート非依存の指数補間
            float t = 1f - Mathf.Exp(-headSmoothSpeed * Time.deltaTime);
            _smoothHead = Quaternion.Slerp(_smoothHead, targetHead, t);
            _smoothNeck = Quaternion.Slerp(_smoothNeck, targetNeck, t);

            SetBone(HumanBodyBones.Head, _smoothHead);
            SetBone(HumanBodyBones.Neck, _smoothNeck);
        }

        /// トラッキング信号消失時に頭部をニュートラルへ滑らかに戻す。
        public void ReturnHeadToNeutral()
        {
            if (vrm == null || animator == null) return;
            var neutralHead = RestRot(HumanBodyBones.Head);
            var neutralNeck = RestRot(HumanBodyBones.Neck);
            // 速度を半分に落として自然な収束感を出す
            float t = 1f - Mathf.Exp(-headSmoothSpeed * 0.5f * Time.deltaTime);
            _smoothHead = Quaternion.Slerp(_smoothHead, neutralHead, t);
            _smoothNeck = Quaternion.Slerp(_smoothNeck, neutralNeck, t);
            SetBone(HumanBodyBones.Head, _smoothHead);
            SetBone(HumanBodyBones.Neck, _smoothNeck);
        }

        // ── 自然姿勢を毎フレーム維持（腕・指のみ） ───────────────────────────────

        /// モーション再生中は true にして腕・指のアイドル固定を止める（モーションに body を明け渡す）。
        public bool SuspendIdle { get; set; }

        void LateUpdate()
        {
            if (SuspendIdle) return;
            if (vrm == null || animator == null || idleLocalRot.Count == 0) return;
            foreach (var b in DrivenBones)
            {
                if (!idleLocalRot.TryGetValue(b, out var rot)) continue;
                var t = animator.GetBoneTransform(b);
                if (t != null) t.localRotation = rot;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        Quaternion RestRot(HumanBodyBones b) =>
            restRotations.TryGetValue(b, out var r) ? r : Quaternion.identity;

        void SetBone(HumanBodyBones b, Quaternion rot)
        {
            var t = animator.GetBoneTransform(b);
            if (t != null) t.localRotation = rot;
        }

        static Vector3 YXZEuler(Quaternion q)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            float m12   = 2f * (y * z - w * x);
            float pitch = Mathf.Asin(Mathf.Clamp(-m12, -1f, 1f));
            float yaw, roll;
            if (Mathf.Abs(m12) < 0.9999f)
            {
                yaw  = Mathf.Atan2(2f * (x * z + w * y), 1f - 2f * (x * x + y * y));
                roll = Mathf.Atan2(2f * (x * y + w * z), 1f - 2f * (x * x + z * z));
            }
            else
            {
                yaw  = Mathf.Atan2(-(2f * (x * z - w * y)), 1f - 2f * (y * y + z * z));
                roll = 0f;
            }
            return new Vector3(pitch, yaw, roll);
        }

        static Quaternion FromYXZ(float x, float y, float z)
        {
            const float toDeg = Mathf.Rad2Deg;
            return Quaternion.AngleAxis(y * toDeg, Vector3.up)
                 * Quaternion.AngleAxis(x * toDeg, Vector3.right)
                 * Quaternion.AngleAxis(z * toDeg, Vector3.forward);
        }
    }
}
