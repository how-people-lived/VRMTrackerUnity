using System.Collections.Generic;
using UnityEngine;
using UniVRM10;
using VRMTracker.Network;

namespace VRMTracker.Avatar
{
    /// Maps ARKit 52 blend shape coefficients to VRM 1.0 expressions.
    /// Mirrors VRMBlendShapeMapper.swift — same mapping table and accumulation logic.
    public class FaceMapper : MonoBehaviour
    {
        [SerializeField, Range(0f, 0.5f), Tooltip("この値未満のブレンドシェイプを無視して安静時の口開きを防ぐ")]
        float deadzone = 0.15f;

        private Vrm10Instance vrm;
        private bool initialized;

        // (ARKit blend shape key, VRM 1.0 expression preset, additive scale)
        // ARKit uses _L/_R suffix for lateralized shapes, no suffix for bilateral ones.
        private static readonly (string arkit, ExpressionPreset preset, float scale)[] Mapping =
        {
            // ── Blink / eye-wide / squint ──────────────────────────────────────
            ("eyeBlink_L",          ExpressionPreset.blinkLeft,  1.00f),
            ("eyeBlink_R",          ExpressionPreset.blinkRight, 1.00f),
            ("eyeWide_L",           ExpressionPreset.surprised,  0.40f),
            ("eyeWide_R",           ExpressionPreset.surprised,  0.40f),
            ("eyeSquint_L",         ExpressionPreset.relaxed,    0.40f),
            ("eyeSquint_R",         ExpressionPreset.relaxed,    0.40f),
            // ── LookAt (ARKit In/Out = eye-relative → character-relative) ──────
            ("eyeLookUp_L",         ExpressionPreset.lookUp,     0.80f),
            ("eyeLookUp_R",         ExpressionPreset.lookUp,     0.80f),
            ("eyeLookDown_L",       ExpressionPreset.lookDown,   0.80f),
            ("eyeLookDown_R",       ExpressionPreset.lookDown,   0.80f),
            ("eyeLookIn_L",         ExpressionPreset.lookRight,  0.70f),
            ("eyeLookOut_R",        ExpressionPreset.lookRight,  0.70f),
            ("eyeLookOut_L",        ExpressionPreset.lookLeft,   0.70f),
            ("eyeLookIn_R",         ExpressionPreset.lookLeft,   0.70f),
            // ── Brow ───────────────────────────────────────────────────────────
            ("browInnerUp",         ExpressionPreset.surprised,  0.70f),
            ("browDown_L",          ExpressionPreset.angry,      0.50f),
            ("browDown_R",          ExpressionPreset.angry,      0.50f),
            ("browOuterUp_L",       ExpressionPreset.surprised,  0.30f),
            ("browOuterUp_R",       ExpressionPreset.surprised,  0.30f),
            // ── Jaw / mouth primary ────────────────────────────────────────────
            ("jawOpen",             ExpressionPreset.aa,         1.00f),
            ("mouthFunnel",         ExpressionPreset.oh,         0.70f),
            ("mouthPucker",         ExpressionPreset.ou,         0.90f),
            ("mouthSmile_L",        ExpressionPreset.happy,      0.60f),
            ("mouthSmile_R",        ExpressionPreset.happy,      0.60f),
            ("mouthFrown_L",        ExpressionPreset.sad,        0.60f),
            ("mouthFrown_R",        ExpressionPreset.sad,        0.60f),
            ("mouthDimple_L",       ExpressionPreset.happy,      0.20f),
            ("mouthDimple_R",       ExpressionPreset.happy,      0.20f),
            // ── Mouth detail ───────────────────────────────────────────────────
            ("mouthLowerDown_L",    ExpressionPreset.aa,         0.30f),
            ("mouthLowerDown_R",    ExpressionPreset.aa,         0.30f),
            ("mouthUpperUp_L",      ExpressionPreset.ih,         0.30f),
            ("mouthUpperUp_R",      ExpressionPreset.ih,         0.30f),
            ("mouthStretch_L",      ExpressionPreset.ih,         0.25f),
            ("mouthStretch_R",      ExpressionPreset.ih,         0.25f),
            ("mouthPress_L",        ExpressionPreset.relaxed,    0.20f),
            ("mouthPress_R",        ExpressionPreset.relaxed,    0.20f),
            ("mouthRollLower",      ExpressionPreset.oh,         0.30f),
            ("mouthRollUpper",      ExpressionPreset.ou,         0.30f),
            ("mouthShrugLower",     ExpressionPreset.sad,        0.25f),
            ("mouthShrugUpper",     ExpressionPreset.surprised,  0.20f),
            // ── Cheek / nose ───────────────────────────────────────────────────
            ("cheekPuff",           ExpressionPreset.surprised,  0.35f),
            ("cheekSquint_L",       ExpressionPreset.happy,      0.25f),
            ("cheekSquint_R",       ExpressionPreset.happy,      0.25f),
            ("noseSneer_L",         ExpressionPreset.angry,      0.30f),
            ("noseSneer_R",         ExpressionPreset.angry,      0.30f),
        };

        public void Initialize(Vrm10Instance instance)
        {
            vrm         = instance;
            initialized = true;
        }

        /// トラッキング信号消失時に全表情をゼロに戻す。
        public void ResetToNeutral() => ClearAllExpressions();

        // custom 以外の全プリセットを 0 にする（ResetToNeutral と Apply の共通処理）。
        void ClearAllExpressions()
        {
            if (!initialized || vrm == null) return;
            foreach (ExpressionPreset preset in System.Enum.GetValues(typeof(ExpressionPreset)))
            {
                if (preset == ExpressionPreset.custom) continue;
                try { vrm.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(preset), 0f); }
                catch { }
            }
        }

        bool _loggedOnce = false;

        public void Apply(FaceData face)
        {
            if (!initialized || vrm == null || face?.blendShapes == null) return;

            // 1. 全プリセットを 0 に
            ClearAllExpressions();

            // 2. Accumulate (additive, clamped to [0,1])
            // deadzone以下はゼロ、超えた分を[0,1]にリマップして安静時の誤検知を防ぐ
            var weights = new Dictionary<ExpressionPreset, float>();
            float dz = Mathf.Clamp(deadzone, 0f, 0.99f);
            foreach (var (key, preset, scale) in Mapping)
            {
                if (!face.blendShapes.TryGetValue(key, out float v)) continue;
                v = Mathf.Clamp01(v);
                v = v <= dz ? 0f : (v - dz) / (1f - dz);
                if (v <= 0f) continue;
                weights.TryGetValue(preset, out float cur);
                weights[preset] = Mathf.Min(1f, cur + v * scale);
            }

            // 3. Apply accumulated weights
            foreach (var kv in weights)
            {
                try { vrm.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(kv.Key), kv.Value); }
                catch { }
            }

            // 受信できているブレンドシェイプをログに出力（初回のみ）
            if (!_loggedOnce && face.blendShapes.Count > 0)
            {
                _loggedOnce = true;
                var sb = new System.Text.StringBuilder("[FaceMapper] blendShapes received: ");
                foreach (var kv in face.blendShapes)
                    if (kv.Value > 0.01f) sb.Append($"{kv.Key}={kv.Value:F2} ");
                Debug.Log(sb.ToString());
            }
        }
    }
}
