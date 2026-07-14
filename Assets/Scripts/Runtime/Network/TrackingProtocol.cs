namespace VRMTracker.Network
{
    /// <summary>
    /// Shared constants for the tracking network protocols (custom JSON + iFacialMocap + FaceMotion3D).
    /// Wire strings/ports are fixed by the external apps — do not change.
    /// </summary>
    public static class TrackingProtocol
    {
        /// <summary>iFacialMocap handshake string (sent UTF-8 to the sender to start streaming).</summary>
        public const string IfmHandshake = "iFacialMocap_sahuasouryya9218sauhuiayeta91555dy3719";

        /// <summary>Default UDP port for the custom JSON tracking stream.</summary>
        public const int DefaultJsonPort = 12345;

        /// <summary>Default UDP port for the iFacialMocap stream.</summary>
        public const int DefaultIfmPort = 49983;

        /// <summary>FaceMotion3D の制御ポート（ここへトリガを送る／データもここに返る）。</summary>
        public const int DefaultFaceMotion3DPort = 49993;

        /// <summary>FaceMotion3D UDP ストリーミング開始トリガ文字列。iOS の 49993 へ送る。</summary>
        public const string FaceMotion3DStart = "FACEMOTION3D_OtherStreaming";

        /// <summary>iFacialMocap / FaceMotion3D のブレンドシェイプ値は 0-100 基準。これで割って 0-1 に。</summary>
        public const float BlendScale = 100f;
    }
}
