using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

namespace VRMTracker.Comments
{
    /// YouTube Data API v3 を使ってライブ配信のチャットを取得する。
    /// 1) videos.list で動画 ID → activeLiveChatId を解決
    /// 2) liveChat/messages.list を pollingIntervalMillis 間隔でポーリング
    /// 取得したメッセージは onMessage(author, text) で通知する。
    /// ※ ユーザー自身の API キー（YouTube Data API v3 有効）が必要。
    public class YouTubeLiveChat : MonoBehaviour
    {
        const string Base = "https://www.googleapis.com/youtube/v3";

        public string StatusText  { get; private set; } = "未接続";

        Action<string, string> _onMessage;
        string _apiKey, _liveChatId, _pageToken;
        Coroutine _loop;

        /// 接続開始。videoUrlOrId は動画URLでもID(11文字)でも可。
        public void Connect(string videoUrlOrId, string apiKey, Action<string, string> onMessage)
        {
            Disconnect();
            _onMessage = onMessage;
            _apiKey    = (apiKey ?? "").Trim();
            string vid = ExtractVideoId(videoUrlOrId);

            if (string.IsNullOrEmpty(vid))    { StatusText = "動画URL/IDが不正"; return; }
            if (string.IsNullOrEmpty(_apiKey)){ StatusText = "APIキー未入力";   return; }

            _loop = StartCoroutine(Run(vid));
        }

        public void Disconnect()
        {
            if (_loop != null) StopCoroutine(_loop);
            _loop = null;
            _pageToken  = null;
            _liveChatId = null;
            StatusText  = "未接続";
        }

        IEnumerator Run(string videoId)
        {
            StatusText = "接続中…";

            // 1) liveChatId を解決
            yield return ResolveLiveChatId(videoId);
            if (string.IsNullOrEmpty(_liveChatId))
            {
                if (StatusText == "接続中…") StatusText = "ライブチャットが見つかりません";
                yield break;
            }

            StatusText  = "接続中";
            bool first = true;   // 初回バックログは流さない（洪水防止）

            // 2) ポーリング
            while (true)
            {
                string url = $"{Base}/liveChat/messages?liveChatId={_liveChatId}" +
                             $"&part=snippet,authorDetails&maxResults=200&key={_apiKey}" +
                             (string.IsNullOrEmpty(_pageToken) ? "" : $"&pageToken={_pageToken}");

                int waitMs = 4000;
                using (var req = UnityWebRequest.Get(url))
                {
                    yield return req.SendWebRequest();

                    if (req.result == UnityWebRequest.Result.Success)
                        waitMs = HandlePoll(req.downloadHandler.text, first);
                    else
                    {
                        StatusText = $"エラー: {req.responseCode}";
                        Debug.LogWarning($"[YouTube] {req.responseCode} {req.error}\n{req.downloadHandler?.text}");
                        waitMs = 6000;
                    }
                }
                first = false;
                yield return new WaitForSeconds(Mathf.Max(1f, waitMs / 1000f));
            }
        }

        // ポーリング結果を処理し、次回までの待機ミリ秒を返す
        int HandlePoll(string json, bool first)
        {
            int waitMs = 4000;
            try
            {
                var jo = JObject.Parse(json);
                _pageToken = (string)jo["nextPageToken"];
                waitMs = (int?)jo["pollingIntervalMillis"] ?? 4000;

                if (!first && jo["items"] is JArray items)
                {
                    foreach (var it in items)
                    {
                        string author = (string)it["authorDetails"]?["displayName"] ?? "";
                        string text   = (string)it["snippet"]?["displayMessage"]   ?? "";
                        if (!string.IsNullOrEmpty(text)) _onMessage?.Invoke(author, text);
                    }
                }
                StatusText = "接続中";
            }
            catch (Exception e)
            {
                StatusText = "解析エラー";
                Debug.LogWarning($"[YouTube] parse: {e.Message}");
            }
            return waitMs;
        }

        IEnumerator ResolveLiveChatId(string videoId)
        {
            string url = $"{Base}/videos?part=liveStreamingDetails&id={videoId}&key={_apiKey}";
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var jo = JObject.Parse(req.downloadHandler.text);
                        _liveChatId = (string)jo["items"]?[0]?["liveStreamingDetails"]?["activeLiveChatId"];
                    }
                    catch (Exception e) { Debug.LogWarning($"[YouTube] resolve parse: {e.Message}"); }
                }
                else
                {
                    StatusText = $"エラー: {req.responseCode}";
                    Debug.LogWarning($"[YouTube] resolve {req.responseCode} {req.error}\n{req.downloadHandler?.text}");
                }
            }
        }

        // URL / 短縮URL / 生ID から 11 文字の動画 ID を取り出す
        static string ExtractVideoId(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            var m = Regex.Match(s, @"(?:v=|youtu\.be/|live/|embed/)([A-Za-z0-9_\-]{11})");
            if (m.Success) return m.Groups[1].Value;
            if (Regex.IsMatch(s, @"^[A-Za-z0-9_\-]{11}$")) return s;
            return null;
        }
    }
}
