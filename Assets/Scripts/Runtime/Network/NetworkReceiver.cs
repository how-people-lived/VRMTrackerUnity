using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;

namespace VRMTracker.Network
{
    /// 複数プロトコルの UDP トラッキングを受信する。
    ///  - カスタム iPhone アプリ (JSON / 既定ポート 12345)
    ///  - iFacialMocap        (文字列 / 既定ポート 49983)
    ///  - FaceMotion3D        (文字列 / 既定ポート 49993)
    /// iFacialMocap と FaceMotion3D はほぼ同じ文字列書式（区切りと命名だけ違う）なので
    /// 統一パーサで自動判別して共通の TrackingFrame に変換する。
    /// 各ポートは個別スレッドで待ち受け、ソケット障害時は自動再接続する。
    public class NetworkReceiver : MonoBehaviour
    {
        public enum Kind { CustomJson, IFacialMocap, FaceMotion3D }

        [Tooltip("カスタム iPhone アプリ用ポート（JSON）")]
        public int port = TrackingProtocol.DefaultJsonPort;

        [Tooltip("iFacialMocap 用ポート（既定 49983）")]
        public int iFacialMocapPort = TrackingProtocol.DefaultIfmPort;

        [Tooltip("FaceMotion3D 用ポート（既定 49993）")]
        public int faceMotion3DPort = TrackingProtocol.DefaultFaceMotion3DPort;

        [Tooltip("iFacialMocap の受信を有効化する")]
        public bool listenIFacialMocap = true;

        [Tooltip("FaceMotion3D の受信を有効化する")]
        public bool listenFaceMotion3D = true;

        [Tooltip("ソケット障害後に再接続を試みるまでの待機秒数")]
        [SerializeField, Range(1f, 10f)] float reconnectDelaySec = 3f;

        public string   SenderIP        { get; private set; } = "—";
        public DateTime LastReceiveTime { get; private set; } = DateTime.MinValue;
        public int      ReconnectCount  { get; private set; } = 0;
        public string   ActiveSource    { get; private set; } = "—";   // 直近に受信したプロトコル名

        // 診断（パース成否に関わらず、生の受信状況を可視化）
        public long   RawPacketCount { get; private set; } = 0;
        public string LastRawFrom    { get; private set; } = "—";
        public string LastRawSource  { get; private set; } = "—";
        public string LastRawText    { get; private set; } = "";

        // 制御文字列（純ASCII）
        static readonly byte[] IfmHandshakeBytes = Encoding.ASCII.GetBytes(TrackingProtocol.IfmHandshake);
        static readonly byte[] Fm3dStartBytes    = Encoding.ASCII.GetBytes(TrackingProtocol.FaceMotion3DStart);

        private TrackingFrame   latestFrame;
        private readonly object frameLock = new object();
        private volatile bool   running;
        DateTime _lastConfirmUtc = DateTime.MinValue;   // iFacialMocap 確認送信のスロットル

        // 待ち受けソケット 1 本ぶんの状態
        private class Sock { public UdpClient client; public Thread thread; public int port; public Kind kind; }
        private readonly List<Sock> _socks = new List<Sock>();

        void OnEnable()
        {
            running = true;
            StartSock(port, Kind.CustomJson);
            if (listenIFacialMocap && iFacialMocapPort != port)
                StartSock(iFacialMocapPort, Kind.IFacialMocap);
            if (listenFaceMotion3D && faceMotion3DPort != port && faceMotion3DPort != iFacialMocapPort)
                StartSock(faceMotion3DPort, Kind.FaceMotion3D);
        }

        void OnDisable()
        {
            running = false;
            foreach (var s in _socks)
            {
                try { s.client?.Close(); } catch { }
                s.thread?.Join(300);
            }
            _socks.Clear();
        }

        /// Returns the most recent frame (null if no frame received yet).
        public TrackingFrame GetFrame()
        {
            lock (frameLock) { return latestFrame; }
        }

        // ── 送信（開始トリガ） ────────────────────────────────────────────────

        string _ifmTargetIp = "";   // iFacialMocap キープアライブ送信先（受信ループが定期送信）

        /// iFacialMocap の接続先を設定する（PC 発モード）。
        /// iFacialMocap はハンドシェイクを1回送っただけだと単発で止まるため、
        /// 実際の送信は受信ループが ~0.4秒ごとに継続して行う（同一スレッド・同一ソケット）。
        public void SendIFacialMocapHandshake(string iphoneIp)
        {
            if (string.IsNullOrWhiteSpace(iphoneIp)) return;
            _ifmTargetIp = iphoneIp.Trim();
            _lastConfirmUtc = DateTime.MinValue;   // すぐ送れるようにリセット
            Debug.Log($"[NetworkReceiver] iFacialMocap target → {_ifmTargetIp}:{iFacialMocapPort}（受信ループから継続送信）");
        }

        // 受信ループ内から呼ぶ iFacialMocap キープアライブ（~0.4秒スロットル・待受ソケットから送信）
        void SendIfmKeepAlive(Sock s)
        {
            if (string.IsNullOrEmpty(_ifmTargetIp)) return;
            var now = DateTime.UtcNow;
            if ((now - _lastConfirmUtc).TotalSeconds < 0.4) return;
            _lastConfirmUtc = now;
            try { s.client.Send(IfmHandshakeBytes, IfmHandshakeBytes.Length, _ifmTargetIp, iFacialMocapPort); }
            catch { }
        }

        /// FaceMotion3D に UDP ストリーミング開始を要求する。取りこぼし対策で数回送る。
        public void SendFaceMotion3DStart(string iphoneIp)
        {
            for (int i = 0; i < 3; i++) SendTo(iphoneIp, faceMotion3DPort, Fm3dStartBytes, Kind.FaceMotion3D);
        }

        // 現状 SendTo は FaceMotion3D 専用（唯一の呼び出しは SendFaceMotion3DStart）。
        // iFacialMocap は SendIfmKeepAlive が受信ループから継続送信するため経由しない。
        // トリガは「待受ソケット」から送る＝送信元ポートが待受ポートになるので、
        // 相手が『送信元ポートへ返信』方式でも『固定ポートへ返信』方式でも受け取れる。
        void SendTo(string iphoneIp, int destPort, byte[] payload, Kind kind)
        {
            if (string.IsNullOrWhiteSpace(iphoneIp)) return;
            var ip = iphoneIp.Trim();
            try
            {
                var sock = _socks.Find(x => x.kind == kind);
                if (sock != null && sock.client != null)
                    sock.client.Send(payload, payload.Length, ip, destPort);
                else { using var c = new UdpClient(); c.Send(payload, payload.Length, ip, destPort); }
                Debug.Log($"[NetworkReceiver] {SourceName(kind)} trigger → {ip}:{destPort}");
            }
            catch (Exception e) { Debug.LogWarning($"[NetworkReceiver] trigger failed: {e.Message}"); }
        }

        // ── 待ち受け ──────────────────────────────────────────────────────────

        void StartSock(int p, Kind kind)
        {
            var s = new Sock { port = p, kind = kind };
            if (!Bind(s)) return;
            s.thread = new Thread(() => Loop(s)) { IsBackground = true, Name = $"UDP{p}" };
            _socks.Add(s);
            s.thread.Start();
            Debug.Log($"[NetworkReceiver] Listening on UDP :{p} ({kind})");
        }

        bool Bind(Sock s)
        {
            try
            {
                s.client = new UdpClient(s.port);
                s.client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkReceiver] Cannot bind UDP port {s.port}: {e.Message}");
                return false;
            }
        }

        // iFacialMocap 受信タイムアウト(ms)。この間隔でループが起きてキープアライブを送れる。
        const int IfmTimeoutMs = 400;

        // iFacialMocap ソケットに受信タイムアウトを設定（Loop 初期化と再接続の両方から使用）。
        static void ApplyIfmTimeout(Sock s)
        {
            try { s.client.Client.ReceiveTimeout = IfmTimeoutMs; } catch { }
        }

        void Loop(Sock s)
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            bool ifm = s.kind == Kind.IFacialMocap;
            // iFacialMocap は受信タイムアウトを設定し、パケットが来なくても定期的に
            // キープアライブを送れるようにする（単発で止まるのを防ぐ・同一スレッドで安全）。
            if (ifm) ApplyIfmTimeout(s);

            while (running)
            {
                byte[] data;
                try
                {
                    data = s.client.Receive(ref ep);
                }
                catch (SocketException se)
                {
                    if (!running) break;

                    // iFacialMocap の受信タイムアウトは正常。キープアライブを送って継続。
                    if (ifm && (se.SocketErrorCode == SocketError.TimedOut || se.SocketErrorCode == SocketError.WouldBlock))
                    {
                        SendIfmKeepAlive(s);
                        continue;
                    }

                    // 実際のソケット障害 → 再接続
                    ReconnectCount++;
                    Debug.LogWarning($"[NetworkReceiver] Socket :{s.port} error — reconnecting in {reconnectDelaySec}s (attempt {ReconnectCount})");
                    int steps = Mathf.RoundToInt(reconnectDelaySec * 10f);
                    for (int i = 0; i < steps && running; i++) Thread.Sleep(100);
                    if (!running) break;
                    try
                    {
                        var old = s.client;
                        if (Bind(s)) { if (ifm) ApplyIfmTimeout(s); old?.Close(); }
                    }
                    catch (Exception ex) { Debug.LogError($"[NetworkReceiver] Reconnect :{s.port} failed: {ex.Message}"); }
                    continue;
                }
                catch (Exception e) { Debug.LogWarning($"[NetworkReceiver] {e.Message}"); continue; }

                try
                {
                    string text = Encoding.UTF8.GetString(data);

                    // 診断: パース前に生の受信を記録（つながらない切り分け用）
                    RawPacketCount++;
                    LastRawFrom   = ep.Address.ToString();
                    LastRawSource = SourceName(s.kind);
                    LastRawText   = text.Length > 160 ? text.Substring(0, 160) : text;

                    var frame = s.kind == Kind.CustomJson ? ParseJson(text) : ParseTrackingString(text);
                    if (frame != null)
                    {
                        SenderIP        = ep.Address.ToString();
                        LastReceiveTime = DateTime.UtcNow;
                        ActiveSource    = SourceName(s.kind);
                        lock (frameLock) { latestFrame = frame; }
                        if (ifm) _ifmTargetIp = ep.Address.ToString();   // 実際の送信元へ追従
                    }

                    if (ifm) SendIfmKeepAlive(s);   // 受信有無に関わらず継続送信
                }
                catch (Exception e) { Debug.LogWarning($"[NetworkReceiver] parse: {e.Message}"); }
            }
        }

        static string SourceName(Kind k) => k switch
        {
            Kind.IFacialMocap => "iFacialMocap",
            Kind.FaceMotion3D => "FaceMotion3D",
            _                 => "カスタム",
        };

        // ── パーサ ────────────────────────────────────────────────────────────

        static TrackingFrame ParseJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int i = 0;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '{') return null;   // JSON でなければ無視
            return JsonConvert.DeserializeObject<TrackingFrame>(json);
        }

        /// iFacialMocap / FaceMotion3D 共通の文字列フォーマットを TrackingFrame.face に変換する。
        ///  ・ブレンドシェイプ: "name&value"（FaceMotion3D）または "name-value"（iFacialMocap）を '|' 区切り
        ///  ・'=' 以降: "head#v1,v2,v3,..."（度数、カンマ/'#' どちらの区切りも許容）→ 頭部回転
        ///  ・名前: Left/Right（FaceMotion3D）と _L/_R（iFacialMocap）の両方を _L/_R に正規化
        ///  ・値域: 0..100 基準（負値・100超もあり得るため Clamp01）
        static TrackingFrame ParseTrackingString(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;

            var face = new FaceData { blendShapes = new Dictionary<string, float>() };

            int eq = s.IndexOf('=');
            string bsPart = eq >= 0 ? s.Substring(0, eq)  : s;
            string trPart = eq >= 0 ? s.Substring(eq + 1) : "";

            foreach (var tok in bsPart.Split('|'))
            {
                if (tok.Length == 0) continue;
                int sep = tok.IndexOf('&');            // FaceMotion3D
                if (sep < 0) sep = tok.LastIndexOf('-'); // iFacialMocap（値は非負なので最後の '-'）
                if (sep <= 0) continue;
                string name = tok.Substring(0, sep);
                if (!TryF(tok.Substring(sep + 1), out float v)) continue;
                face.blendShapes[Normalize(name)] = Mathf.Clamp01(v / TrackingProtocol.BlendScale);
            }

            foreach (var seg in trPart.Split('|'))
            {
                int h = seg.IndexOf('#');
                if (h < 0 || seg.Substring(0, h) != "head") continue;
                var vals = seg.Substring(h + 1).Split(new[] { ',', '#' }, StringSplitOptions.RemoveEmptyEntries);
                if (vals.Length >= 3 && TryF(vals[0], out float rx) && TryF(vals[1], out float ry) && TryF(vals[2], out float rz))
                {
                    var q = Quaternion.Euler(rx, ry, rz);
                    face.headRotation = new QuatData { x = q.x, y = q.y, z = q.z, w = q.w };
                }
            }

            return new TrackingFrame { face = face };
        }

        // ブレンドシェイプ名を FaceMapper が使う _L/_R 形式へ正規化。
        static string Normalize(string n)
        {
            if (n.EndsWith("Left"))  return n.Substring(0, n.Length - 4) + "_L";
            if (n.EndsWith("Right")) return n.Substring(0, n.Length - 5) + "_R";
            return n;   // 既に _L/_R もしくは左右なしはそのまま
        }

        static bool TryF(string str, out float f) =>
            float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out f);
    }
}
