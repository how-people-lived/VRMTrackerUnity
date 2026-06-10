using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;

/// UDP receiver that listens for TrackingFrames sent from the iPhone app.
/// Runs a background thread — thread-safe via frameLock.
public class NetworkReceiver : MonoBehaviour
{
    [Tooltip("UDP port to listen on (must match iOS UDPSender port)")]
    public int port = 12345;

    public bool IsReceiving => receiveThread != null && receiveThread.IsAlive;
    public string SenderIP  { get; private set; } = "—";
    public System.DateTime LastReceiveTime { get; private set; } = System.DateTime.MinValue;

    private UdpClient  udpClient;
    private Thread     receiveThread;
    private volatile   bool running;

    private TrackingFrame latestFrame;
    private readonly  object frameLock = new object();

    void OnEnable()
    {
        try
        {
            udpClient = new UdpClient(port);
            // Allow multiple sockets on same port (useful for testing)
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket,
                                             SocketOptionName.ReuseAddress, true);
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkReceiver] Cannot bind UDP port {port}: {e.Message}");
            return;
        }

        running       = true;
        receiveThread = new Thread(ReceiveLoop)
        {
            IsBackground = true,
            Name         = "UDPReceiver"
        };
        receiveThread.Start();
        Debug.Log($"[NetworkReceiver] Listening on UDP :{port}");
    }

    void OnDisable()
    {
        running = false;
        udpClient?.Close();
        receiveThread?.Join(500);
    }

    /// Returns the most recent frame (null if no frame received yet).
    public TrackingFrame GetFrame()
    {
        lock (frameLock) { return latestFrame; }
    }

    private void ReceiveLoop()
    {
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = udpClient.Receive(ref ep);
                SenderIP        = ep.Address.ToString();
                LastReceiveTime = System.DateTime.UtcNow;
                string json = Encoding.UTF8.GetString(data);
                var frame   = JsonConvert.DeserializeObject<TrackingFrame>(json);
                lock (frameLock) { latestFrame = frame; }
            }
            catch (SocketException) { break; }
            catch (Exception e)    { Debug.LogWarning($"[NetworkReceiver] {e.Message}"); }
        }
    }
}
