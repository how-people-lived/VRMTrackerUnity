using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

public class BoneMapper : MonoBehaviour
{
    private Vrm10Instance vrm;
    private Animator      animator;
    private readonly Dictionary<HumanBodyBones, Quaternion> restRotations = new();

    // 腕の基準方向（Initialize 時のワールド空間、T/A ポーズに依存しない）
    private Vector3    leftUpperArmRef,  rightUpperArmRef;
    private Vector3    leftForearmRef,   rightForearmRef;
    private Quaternion leftUpperArmInitRot,  rightUpperArmInitRot;
    private Quaternion leftForearmInitRot,   rightForearmInitRot;

    private bool _bodyLogged, _handLogged;

    // ── Initialize ────────────────────────────────────────────────────────

    public void Initialize(Vrm10Instance instance)
    {
        vrm      = instance;
        animator = instance.GetComponent<Animator>();
        CaptureRest();
        CaptureArmRef(HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm,
                      ref leftUpperArmRef,  ref leftUpperArmInitRot);
        CaptureArmRef(HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftHand,
                      ref leftForearmRef,   ref leftForearmInitRot);
        CaptureArmRef(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                      ref rightUpperArmRef, ref rightUpperArmInitRot);
        CaptureArmRef(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                      ref rightForearmRef,  ref rightForearmInitRot);
    }

    void CaptureArmRef(HumanBodyBones from, HumanBodyBones to,
                       ref Vector3 dir, ref Quaternion initRot)
    {
        var tFrom = animator?.GetBoneTransform(from);
        var tTo   = animator?.GetBoneTransform(to);
        if (tFrom == null || tTo == null) return;
        dir    = (tTo.position - tFrom.position).normalized;
        initRot = tFrom.rotation;
    }

    // ── Apply ─────────────────────────────────────────────────────────────

    public void Apply(FaceData face,
                      HandData leftHand  = null,
                      HandData rightHand = null,
                      BodyData body      = null)
    {
        if (vrm == null || animator == null) return;

        // 頭
        if (face?.headRotation != null)
        {
            var q  = face.headRotation;
            var eu = YXZEuler(new Quaternion(q.x, q.y, q.z, q.w));
            SetBone(HumanBodyBones.Head,
                    RestRot(HumanBodyBones.Head) *
                    FromYXZ(eu.x * 0.60f, -eu.y * 0.60f, eu.z * 0.60f));
            SetBone(HumanBodyBones.Neck,
                    RestRot(HumanBodyBones.Neck) *
                    FromYXZ(eu.x * 0.25f, -eu.y * 0.25f, eu.z * 0.25f));
        }

        // 腕（Body pose）
        if (body?.joints != null)
        {
            if (!_bodyLogged) { _bodyLogged = true;
                Debug.Log("[BoneMapper] body joints: " + string.Join(", ", body.joints.Keys)); }
            ApplyArms(body);
        }

        // 指（Hand pose）
        if (leftHand?.joints  != null) ApplyFingers(leftHand,  isLeft: true);
        if (rightHand?.joints != null) ApplyFingers(rightHand, isLeft: false);
    }

    // ── 腕 ───────────────────────────────────────────────────────────────

    void ApplyArms(BodyData body)
    {
        // Vision body joint 名は VNHumanBodyPoseObservation.JointName.rawValue.rawValue
        ApplyArmSeg(body, "left_shoulder_1_joint",  "left_forearm_joint",
                    leftUpperArmRef,  leftUpperArmInitRot,  HumanBodyBones.LeftUpperArm);
        ApplyArmSeg(body, "left_forearm_joint",      "left_hand_joint",
                    leftForearmRef,   leftForearmInitRot,   HumanBodyBones.LeftLowerArm);
        ApplyArmSeg(body, "right_shoulder_1_joint", "right_forearm_joint",
                    rightUpperArmRef, rightUpperArmInitRot, HumanBodyBones.RightUpperArm);
        ApplyArmSeg(body, "right_forearm_joint",     "right_hand_joint",
                    rightForearmRef,  rightForearmInitRot,  HumanBodyBones.RightLowerArm);
    }

    void ApplyArmSeg(BodyData body,
                     string fromKey, string toKey,
                     Vector3 refDir, Quaternion initRot, HumanBodyBones bone)
    {
        if (!body.joints.TryGetValue(fromKey, out var f)) return;
        if (!body.joints.TryGetValue(toKey,   out var t)) return;
        if (f.confidence < 0.3f || t.confidence < 0.3f) return;

        // Vision 座標: x 右増加, y 上増加 → そのままワールド XY 平面の方向として使用
        float dx = t.x - f.x;
        float dy = t.y - f.y;
        if (dx * dx + dy * dy < 1e-6f) return;

        var desired = new Vector3(dx, dy, 0f).normalized;
        var delta   = Quaternion.FromToRotation(refDir, desired);

        var bt = animator.GetBoneTransform(bone);
        if (bt != null) bt.rotation = delta * initRot;
    }

    // ── 指 ───────────────────────────────────────────────────────────────

    // VNRecognizedPointKey の実際の rawValue（Vision の内部キー形式 VNHLK*）
    static readonly (string mcp, string pip, string dip, string tip,
                     HumanBodyBones lp, HumanBodyBones li, HumanBodyBones ld)[] FingerDefs =
    {
        ("VNHLKIMCP", "VNHLKIPIP", "VNHLKIDIP", "VNHLKITIP",
         HumanBodyBones.LeftIndexProximal,    HumanBodyBones.LeftIndexIntermediate,    HumanBodyBones.LeftIndexDistal),
        ("VNHLKMMCP", "VNHLKMPIP", "VNHLKMDIP", "VNHLKMTIP",
         HumanBodyBones.LeftMiddleProximal,   HumanBodyBones.LeftMiddleIntermediate,   HumanBodyBones.LeftMiddleDistal),
        ("VNHLKRMCP", "VNHLKRPIP", "VNHLKRDIP", "VNHLKRTIP",
         HumanBodyBones.LeftRingProximal,     HumanBodyBones.LeftRingIntermediate,     HumanBodyBones.LeftRingDistal),
        ("VNHLKPMCP", "VNHLKPPIP", "VNHLKPDIP", "VNHLKPTIP",
         HumanBodyBones.LeftLittleProximal,   HumanBodyBones.LeftLittleIntermediate,   HumanBodyBones.LeftLittleDistal),
    };

    void ApplyFingers(HandData hand, bool isLeft)
    {
        if (!_handLogged) { _handLogged = true;
            Debug.Log("[BoneMapper] hand joints: " + string.Join(", ", hand.joints.Keys)); }

        if (!hand.joints.TryGetValue("VNHLKW", out var wrist)) return;

        foreach (var (mcp, pip, dip, tip, lp, li, ld) in FingerDefs)
        {
            if (!hand.joints.TryGetValue(mcp, out var jMcp)) continue;
            if (!hand.joints.TryGetValue(tip, out var jTip)) continue;

            float curl = CalcCurl(jTip, wrist, jMcp);

            // 左右で骨を切り替え
            var bProx = isLeft ? lp : MirrorBone(lp);
            var bIntr = isLeft ? li : MirrorBone(li);
            var bDist = isLeft ? ld : MirrorBone(ld);

            SetFingerCurl(bProx, curl * 80f,  isLeft);
            SetFingerCurl(bIntr, curl * 72f,  isLeft);
            SetFingerCurl(bDist, curl * 50f,  isLeft);
        }

        // 親指
        if (hand.joints.TryGetValue("VNHLKTCMC", out var tCmc) &&
            hand.joints.TryGetValue("VNHLKTTIP", out var tTip))
        {
            float curl = CalcCurl(tTip, wrist, tCmc);
            var bPx = isLeft ? HumanBodyBones.LeftThumbProximal    : HumanBodyBones.RightThumbProximal;
            var bIn = isLeft ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate;
            var bDi = isLeft ? HumanBodyBones.LeftThumbDistal       : HumanBodyBones.RightThumbDistal;
            SetFingerCurl(bPx, curl * 50f, isLeft);
            SetFingerCurl(bIn, curl * 40f, isLeft);
            SetFingerCurl(bDi, curl * 30f, isLeft);
        }
    }

    static float CalcCurl(JointPoint tip, JointPoint wrist, JointPoint mcp)
    {
        float tw = Vector2.Distance(new Vector2(tip.x, tip.y), new Vector2(wrist.x, wrist.y));
        float mw = Vector2.Distance(new Vector2(mcp.x, mcp.y), new Vector2(wrist.x, wrist.y));
        if (mw < 1e-5f) return 0f;
        return Mathf.Clamp01(1f - tw / (mw * 3.2f));
    }

    void SetFingerCurl(HumanBodyBones bone, float degrees, bool isLeft)
    {
        var t = animator.GetBoneTransform(bone);
        if (t == null) return;
        restRotations.TryGetValue(bone, out Quaternion rest);
        // VRM 標準: 指の curl 軸は local Z（左手 +Z、右手 -Z）
        t.localRotation = rest * Quaternion.AngleAxis(degrees, isLeft ? Vector3.forward : Vector3.back);
    }

    // 左→右への骨ミラーリング
    static HumanBodyBones MirrorBone(HumanBodyBones b) => b switch
    {
        HumanBodyBones.LeftIndexProximal    => HumanBodyBones.RightIndexProximal,
        HumanBodyBones.LeftIndexIntermediate=> HumanBodyBones.RightIndexIntermediate,
        HumanBodyBones.LeftIndexDistal      => HumanBodyBones.RightIndexDistal,
        HumanBodyBones.LeftMiddleProximal   => HumanBodyBones.RightMiddleProximal,
        HumanBodyBones.LeftMiddleIntermediate=> HumanBodyBones.RightMiddleIntermediate,
        HumanBodyBones.LeftMiddleDistal     => HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.LeftRingProximal     => HumanBodyBones.RightRingProximal,
        HumanBodyBones.LeftRingIntermediate => HumanBodyBones.RightRingIntermediate,
        HumanBodyBones.LeftRingDistal       => HumanBodyBones.RightRingDistal,
        HumanBodyBones.LeftLittleProximal   => HumanBodyBones.RightLittleProximal,
        HumanBodyBones.LeftLittleIntermediate=> HumanBodyBones.RightLittleIntermediate,
        HumanBodyBones.LeftLittleDistal     => HumanBodyBones.RightLittleDistal,
        _ => b,
    };

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
