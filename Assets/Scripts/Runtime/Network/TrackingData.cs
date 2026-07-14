using System;
using System.Collections.Generic;

// Mirrors iOS TrackingFrame / FaceData structure. JSON-serialized via Newtonsoft.Json.
// 注: 現行の各パーサ（iFacialMocap / FaceMotion3D 文字列）は face のみを埋め、消費側も
// frame.face（blendShapes / headRotation）しか読まない。leftHand/rightHand/body/body3D と
// headPosition はカスタム JSON アプリ用の契約フィールドで、現状は無消費だが JSON 契約の
// 一部なので「不活性だが load-bearing」。フィールド名の変更・削除は互換を壊すため不可。

namespace VRMTracker.Network
{
    [Serializable]
    public class TrackingFrame
    {
        public double timestamp;
        public FaceData face;
        // ↓ カスタム JSON アプリ用の契約フィールド（現状は無消費）
        public HandData leftHand;
        public HandData rightHand;
        public BodyData body;       // Vision 2D body pose (legacy fallback)
        public Body3DData body3D;   // Vision 3D body pose (iOS 17+)
    }

    [Serializable]
    public class FaceData
    {
        /// ARKit blend shape key → 0‑1 coefficient
        public Dictionary<string, float> blendShapes;
        /// Quaternion sent as {x,y,z,w} object from iOS
        public QuatData headRotation;
        /// Position sent as {x,y,z} object from iOS (reserved)
        public Vec3Data headPosition;
    }

    [Serializable]
    public class QuatData
    {
        public float x, y, z, w;
    }

    [Serializable]
    public class Vec3Data
    {
        public float x, y, z;
    }

    [Serializable]
    public class HandData
    {
        public Dictionary<string, JointPoint> joints;
    }

    [Serializable]
    public class BodyData
    {
        public Dictionary<string, JointPoint> joints;
    }

    /// Vision VNDetectHumanBodyPose3DRequest joints (iOS 17+).
    /// Positions are in Vision's 3D model space (metres) relative to the body root.
    /// Any field may be null if that joint was not detected this frame.
    [Serializable]
    public class Body3DData
    {
        public Vec3Data leftShoulder;
        public Vec3Data leftElbow;
        public Vec3Data leftWrist;
        public Vec3Data rightShoulder;
        public Vec3Data rightElbow;
        public Vec3Data rightWrist;
        public Vec3Data root;
        public Vec3Data spine;
    }

    [Serializable]
    public class JointPoint
    {
        public float x;
        public float y;
        public float confidence;
    }
}
