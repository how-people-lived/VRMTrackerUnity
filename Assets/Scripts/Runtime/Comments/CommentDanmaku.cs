using System.Collections.Generic;
using UnityEngine;
using VRMTracker.CameraControl;

namespace VRMTracker.Comments
{
    /// YouTube コメントを 3D テキスト（TextMesh）としてアバターの後ろに流す。
    /// ・各コメントはカメラのビューポート右端から左へスクロール
    /// ・奥行きをアバターの少し後ろに置くことで、アバターに隠れて流れる
    /// ・カメラへ正対（ビルボード）するのでオービットしても読める
    public class CommentDanmaku : MonoBehaviour
    {
        [SerializeField] float speed        = 0.13f;   // ビューポート/秒（右→左）
        [SerializeField] float depthBehind  = 0.6f;    // アバター中心からの奥行き(m)
        [SerializeField] float charSize     = 0.0085f; // TextMesh characterSize
        [SerializeField] int   fontSize     = 84;
        [SerializeField] int   maxActive    = 40;      // 同時表示上限
        [SerializeField] float spawnGap     = 0.25f;   // 連続スポーンの最小間隔(秒)

        Camera           _cam;
        CameraController _camCtrl;
        Font             _font;

        class Item { public Transform tr; public float vx, vy, depth; }
        readonly List<Item> _active   = new List<Item>();
        readonly Queue<string> _queue = new Queue<string>();

        static readonly float[] Lanes = { 0.16f, 0.28f, 0.40f, 0.52f, 0.64f, 0.76f, 0.88f };
        int   _lane;
        float _spawnTimer;

        public void Setup(Camera cam, CameraController camCtrl, Font font)
        {
            _cam = cam; _camCtrl = camCtrl; _font = font;
        }

        /// コメントを投入（メインスレッドから呼ぶ）
        public void Enqueue(string author, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string line = string.IsNullOrEmpty(author) ? text : $"{author}: {text}";
            if (line.Length > 80) line = line.Substring(0, 80) + "…";
            _queue.Enqueue(line);
        }

        void Update()
        {
            if (_cam == null) return;

            // スポーン（間隔と上限を守る）
            _spawnTimer -= Time.deltaTime;
            if (_spawnTimer <= 0f && _queue.Count > 0 && _active.Count < maxActive)
            {
                Spawn(_queue.Dequeue());
                _spawnTimer = spawnGap;
            }

            float depth = (_camCtrl != null ? _camCtrl.Distance : 2.2f) + depthBehind;

            // 移動・ビルボード・回収
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var it = _active[i];
                it.vx   -= speed * Time.deltaTime;
                it.depth = depth;
                if (it.vx < -0.2f)
                {
                    Destroy(it.tr.gameObject);
                    _active.RemoveAt(i);
                    continue;
                }
                it.tr.position = _cam.ViewportToWorldPoint(new Vector3(it.vx, it.vy, it.depth));
                it.tr.rotation = _cam.transform.rotation;   // カメラへ正対
            }
        }

        void Spawn(string line)
        {
            var go = new GameObject("comment");
            go.transform.SetParent(transform, false);

            var tm = go.AddComponent<TextMesh>();
            tm.text          = line;
            tm.font          = _font;
            tm.fontSize      = fontSize;
            tm.characterSize = charSize;
            tm.anchor        = TextAnchor.MiddleLeft;
            tm.alignment     = TextAlignment.Left;
            tm.color         = Color.white;
            tm.richText      = false;
            if (_font != null) go.GetComponent<MeshRenderer>().sharedMaterial = _font.material;

            float vy = Lanes[_lane % Lanes.Length];
            _lane++;

            _active.Add(new Item { tr = go.transform, vx = 1.08f, vy = vy });   // depth は Update で毎フレーム再計算
        }

        /// 全コメントを消去
        public void Clear()
        {
            foreach (var it in _active) if (it.tr != null) Destroy(it.tr.gameObject);
            _active.Clear();
            _queue.Clear();
        }
    }
}
