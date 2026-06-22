using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

/// Drives head + neck from iPhone face tracking, and holds the rest of the body in a
/// natural relaxed standing pose (arms lowered to the sides, fingers softly curled).
///
/// Body / arm / hand tracking has been removed — this app focuses on facial tracking.
/// The loaded VRM rests in a T-pose; the idle pose below rotates the arms down so the
/// avatar looks natural while standing still.
public class BoneMapper : MonoBehaviour
{
    private Vrm10Instance vrm;
    private Animator      animator;
    private Transform     avatarRoot;
    private readonly Dictionary<HumanBodyBones, Quaternion> restRotations = new();

    // 腕の基準（Initialize 時のワールド空間レスト方向 + レスト回転）— 自然姿勢の計算に使う
    private Vector3    leftUpperArmRef,  rightUpperArmRef;
    private Vector3    leftForearmRef,   rightForearmRef;
    private Quaternion leftUpperArmInitRot,  rightUpperArmInitRot;
    private Quaternion leftForearmInitRot,   rightForearmInitRot;

    [Header("Idle Pose（自然な立ち姿）")]
    [Tooltip("上腕を水平(Tポーズ)から下ろす角度。90で真下")]
    [Range(0f, 90f)]   [SerializeField] float upperArmDownAngle    = 72f;
    [Tooltip("腕を体の前方へ少し傾ける角度。アバターが前後逆ならマイナスに")]
    [Range(-30f, 30f)] [SerializeField] float upperArmForwardAngle = 7f;
    [Tooltip("肘の前方への曲げ角度")]
    [Range(0f, 45f)]   [SerializeField] float elbowBendAngle       = 10f;
    [Tooltip("指の軽い曲げ（度）")]
    [Range(0f, 40f)]   [SerializeField] float fingerCurl           = 12f;
    [Tooltip("親指の軽い曲げ（度）")]
    [Range(0f, 40f)]   [SerializeField] float thumbCurl            = 8f;

    // ── Initialize ────────────────────────────────────────────────────────

    public void Initialize(Vrm10Instance instance)
    {
        vrm        = instance;
        animator   = instance.GetComponent<Animator>();
        avatarRoot = instance.transform;
        CaptureRest();
        CaptureArmRef(HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm,
                      ref leftUpperArmRef,  ref leftUpperArmInitRot);
        CaptureArmRef(HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftHand,
                      ref leftForearmRef,   ref leftForearmInitRot);
        CaptureArmRef(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                      ref rightUpperArmRef, ref rightUpperArmInitRot);
        CaptureArmRef(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                      ref rightForearmRef,  ref rightForearmInitRot);
        // 接続前でも自然姿勢を見せておく
        ApplyIdlePose();
    }

    void CaptureArmRef(HumanBodyBones from, HumanBodyBones to,
                       ref Vector3 dir, ref Quaternion initRot)
    {
        var tFrom = animator?.GetBoneTransform(from);
        var tTo   = animator?.GetBoneTransform(to);
        if (tFrom == null || tTo == null) return;
        dir     = (tTo.position - tFrom.position).normalized;
        initRot = tFrom.rotation;
    }

    // ── 顔（頭・首）トラッキング ─────────────────────────────────────────────

    /// 表情・頭部の向きを適用する。腕・指は LateUpdate の自然姿勢が担当。
    public void Apply(FaceData face)
    {
        if (vrm == null || animator == null) return;
        if (face?.headRotation == null) return;

        var q  = face.headRotation;
        var eu = YXZEuler(new Quaternion(q.x, q.y, q.z, q.w));
        SetBone(HumanBodyBones.Head,
                RestRot(HumanBodyBones.Head) *
                FromYXZ(eu.x * 0.60f, -eu.y * 0.60f, eu.z * 0.60f));
        SetBone(HumanBodyBones.Neck,
                RestRot(HumanBodyBones.Neck) *
                FromYXZ(eu.x * 0.25f, -eu.y * 0.25f, eu.z * 0.25f));
    }

    // ── 自然姿勢（毎フレーム維持） ───────────────────────────────────────────

    void LateUpdate()
    {
        if (vrm == null || animator == null) return;
        ApplyIdlePose();
    }

    void ApplyIdlePose()
    {
        ApplyIdleArm(leftUpperArmRef,  leftUpperArmInitRot,  HumanBodyBones.LeftUpperArm,  isForearm: false);
        ApplyIdleArm(leftForearmRef,   leftForearmInitRot,   HumanBodyBones.LeftLowerArm,  isForearm: true);
        ApplyIdleArm(rightUpperArmRef, rightUpperArmInitRot, HumanBodyBones.RightUpperArm, isForearm: false);
        ApplyIdleArm(rightForearmRef,  rightForearmInitRot,  HumanBodyBones.RightLowerArm, isForearm: true);
        ApplyIdleFingers();
    }

    void ApplyIdleArm(Vector3 refDir, Quaternion initRot, HumanBodyBones bone, bool isForearm)
    {
        var bt = animator.GetBoneTransform(bone);
        if (bt == null || refDir.sqrMagnitude < 1e-6f) return;

        // Tポーズ時の水平方向（左右の符号を保持） → これを基準に下ろす
        Vector3 outward = new Vector3(refDir.x, 0f, refDir.z);
        outward = outward.sqrMagnitude < 1e-6f ? Vector3.right : outward.normalized;

        float downDeg = upperArmDownAngle;
        float fwdDeg  = upperArmForwardAngle + (isForearm ? elbowBendAngle : 0f);

        float d = downDeg * Mathf.Deg2Rad;
        float f = fwdDeg  * Mathf.Deg2Rad;
        Vector3 dir = outward * Mathf.Cos(d) + Vector3.down * Mathf.Sin(d)
                    + Vector3.forward * Mathf.Sin(f);
        dir.Normalize();

        bt.rotation = Quaternion.FromToRotation(refDir, dir) * initRot;
    }

    // 親指以外の指の骨（近位・中位・遠位）
    static readonly HumanBodyBones[] CurlFingers =
    {
        HumanBodyBones.LeftIndexProximal,  HumanBodyBones.LeftIndexIntermediate,  HumanBodyBones.LeftIndexDistal,
        HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
        HumanBodyBones.LeftRingProximal,   HumanBodyBones.LeftRingIntermediate,   HumanBodyBones.LeftRingDistal,
        HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
        HumanBodyBones.RightIndexProximal,  HumanBodyBones.RightIndexIntermediate,  HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightRingProximal,   HumanBodyBones.RightRingIntermediate,   HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
    };

    static readonly HumanBodyBones[] ThumbBones =
    {
        HumanBodyBones.LeftThumbProximal,  HumanBodyBones.LeftThumbIntermediate,  HumanBodyBones.LeftThumbDistal,
        HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
    };

    void ApplyIdleFingers()
    {
        foreach (var bone in CurlFingers)
        {
            bool isLeft = bone.ToString().StartsWith("Left");
            float deg   = bone.ToString().EndsWith("Distal") ? fingerCurl * 0.7f : fingerCurl;
            SetFingerCurl(bone, deg, isLeft);
        }
        foreach (var bone in ThumbBones)
        {
            bool isLeft = bone.ToString().StartsWith("Left");
            SetFingerCurl(bone, thumbCurl, isLeft);
        }
    }

    void SetFingerCurl(HumanBodyBones bone, float degrees, bool isLeft)
    {
        var t = animator.GetBoneTransform(bone);
        if (t == null) return;
        restRotations.TryGetValue(bone, out Quaternion rest);
        // VRM 標準: 指の curl 軸は local Z（左手 +Z、右手 -Z）
        t.localRotation = rest * Quaternion.AngleAxis(degrees, isLeft ? Vector3.forward : Vector3.back);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    void CaptureRest()
    {
        if (animator == null) return;
        foreach (HumanBodyBones b in System.Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (b == HumanBodyBones.LastBone) continue;
            var t = animator.GetBoneTransform(b);
            if (t != null) restRotations[b] = t.localRotation;
        }
    }

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
