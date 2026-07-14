using System;
using UnityEngine;
using VRMTracker.Network;

namespace VRMTracker.UI
{
    public static class ConnectionState
    {
        public const double LiveWindow = 1.0, BarWindow = 2.0, IdleWindow = 15.0;

        public static bool Received(NetworkReceiver r) => r != null && r.LastReceiveTime != DateTime.MinValue;

        public static double Elapsed(NetworkReceiver r) => Received(r) ? (DateTime.UtcNow - r.LastReceiveTime).TotalSeconds : double.MaxValue;

        public static bool IsLive(NetworkReceiver r) => Received(r) && Elapsed(r) < LiveWindow;

        public static bool BarActive(NetworkReceiver r) => Received(r) && Elapsed(r) < BarWindow;

        public static bool Waiting(NetworkReceiver r) => Received(r) && Elapsed(r) < IdleWindow;

        // ダークバー上で視認しやすい配色
        public static readonly Color BarIdle = new(0.62f, 0.62f, 0.68f),
                                     BarActiveColor = new(0.32f, 0.86f, 0.48f),
                                     BarWaiting = new(0.98f, 0.72f, 0.28f);

        public static readonly Color DotLive = new(0.30f, 0.80f, 0.40f),
                                     DotWaiting = new(0.95f, 0.65f, 0.20f),
                                     DotIdle = new(0.47f, 0.47f, 0.50f);
    }
}
