using System;
using System.Collections.Generic;

// Mirrors iOS TrackingFrame / FaceData structure.
// JSON-serialized via Newtonsoft.Json (supports Dictionary<string,float>).

[Serializable]
public class TrackingFrame
{
    public double timestamp;
    public FaceData face;
    public HandData leftHand;
    public HandData rightHand;
    public BodyData body;
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

[Serializable]
public class JointPoint
{
    public float x;
    public float y;
    public float confidence;
}
