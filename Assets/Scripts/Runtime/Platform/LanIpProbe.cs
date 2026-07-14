namespace VRMTracker.Platform
{
    /// このMacのLAN IPv4アドレスを取得する静的ヘルパ。
    public static class LanIpProbe
    {
        /// iPhone アプリが UDP 送信すべき宛先 = このMacのLAN IPv4アドレスを返す。
        public static string GetLocalIPAddress()
        {
            // ルーティング先のローカル端点を取得（実際には送信しない）
            try
            {
                using var socket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Dgram,
                    System.Net.Sockets.ProtocolType.Udp);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is System.Net.IPEndPoint ep
                    && !System.Net.IPAddress.IsLoopback(ep.Address))
                    return ep.Address.ToString();
            }
            catch { /* オフライン時はインターフェース列挙にフォールバック */ }

            // フォールバック: 稼働中インターフェースのプライベートIPv4を探す
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                            && !System.Net.IPAddress.IsLoopback(ua.Address))
                            return ua.Address.ToString();
                }
            }
            catch { }
            return "—";
        }
    }
}
