using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // ステージの背景(ゲームに干渉しない外の飾り)のルート。背景ごとにプレハブにして Assets/Nakagawa/Backdrops で管理する
    // (BackdropBuilder がコードの定義から作る。手で作ったプレハブでもよい)
    // ステージ(StageEntry)に紐づけると、そのステージの子として置かれ、ステージと一緒に表示が切り替わる
    //  ・当たり判定は持たない(念のため、実行時に子の当たり判定をすべて無効にする)
    //  ・Fit でステージの広さに合わせる: 背景全体をステージの中心へ動かし、BackdropAnchor の付いた小物を範囲の外側へ置き直し、
    //    床の下に支え(本の山など)を作る
    public class StageBackdrop : MonoBehaviour
    {
        // 床の下に置く支えの見た目
        public enum SupportStyle
        {
            None,       // 支えなし(床が宙に浮く)
            BookStack,  // 本を積み重ねた山
            Block,      // 1本の柱
            SlabStack,  // 板(まな板・座布団・風呂ふたなど)を積み重ねた山。表紙の色を板ごとに順に使う
        }

        [System.Serializable]
        public class SupportSettings
        {
            public SupportStyle Style = SupportStyle.None;
            [Tooltip("支えの下端の高さ(背景の机の天板の高さなど)")]
            public float BottomY = -10.5f;
            [Tooltip("床の縁から内側へ下げる幅(m)。落ちる物が支えに隠れないように")]
            [Min(0.0f)] public float Inset = 0.8f;
            [Tooltip("本1冊の厚さ(m) 最小/最大")]
            [Min(0.1f)] public float MinPieceHeight = 1.2f;
            [Min(0.1f)] public float MaxPieceHeight = 2.2f;
            [Tooltip("本1冊ごとに回す角度の最大(度)")]
            [Range(0.0f, 10.0f)] public float MaxYaw = 2.5f;
            [Tooltip("表紙の色(順に使う)")]
            public Material[] CoverMaterials;
            [Tooltip("本のページの色")]
            public Material PageMaterial;
        }

        const string kSupportRootName = "FloorSupports";

        [SerializeField] string m_id;
        [SerializeField] string m_title;
        [SerializeField, TextArea(2, 6)] string m_description;
        [SerializeField] SupportSettings m_supports = new SupportSettings();
        [Header("この背景に合う床・壁の見た目(StageBuilder がステージを作るときに使う。未設定なら既定)")]
        [SerializeField] Material m_floorMaterial;
        [SerializeField] Material m_wallMaterial;

        public string Id => m_id;
        public string Title => m_title;
        public string Description => m_description;
        public SupportSettings Supports => m_supports;
        public Material FloorMaterial => m_floorMaterial;
        public Material WallMaterial => m_wallMaterial;

        public void Set(string id, string title, string description, SupportSettings supports, Material floorMaterial, Material wallMaterial)
        {
            m_id = id;
            m_title = title;
            m_description = description;
            m_supports = supports ?? new SupportSettings();
            m_floorMaterial = floorMaterial;
            m_wallMaterial = wallMaterial;
        }

        private void Awake()
        {
            // ゲームに干渉しないよう、子に当たり判定があっても無効にする
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        //========================================
        // ステージに合わせる
        //========================================

        // stageRoot の床に合わせる(背景をステージの中心へ動かし、小物を外側へ置き直し、床の下に支えを作る)
        // center・size: ステージの範囲(StageArea)の中心と大きさ
        public void Fit(Transform stageRoot, Vector3 center, Vector2 size)
        {
            transform.position = new Vector3(center.x, transform.position.y, center.z);
            foreach (var anchor in GetComponentsInChildren<BackdropAnchor>(true)) anchor.Fit(center, size);
            BuildSupports(CollectFloors(stageRoot, center.y));
        }

        // ステージの床(動かない箱で、壁ではなく、上面が床の高さ floorY にある物)の範囲。背景の物は除く
        public static List<Bounds> CollectFloors(Transform stageRoot, float floorY)
        {
            var floors = new List<Bounds>();
            if (stageRoot == null) return floors;
            foreach (var box in stageRoot.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.isTrigger || box.attachedRigidbody != null) continue;
                if (box.GetComponentInParent<StageBackdrop>(true) != null) continue;
                if (box.GetComponentInParent<ReflectWall>(true) != null) continue;
                if (Vector3.Dot(box.transform.up, Vector3.up) < 0.999f) continue;
                Bounds b = WorldBounds(box);
                if (Mathf.Abs(b.max.y - floorY) > 0.05f) continue;
                floors.Add(b);
            }
            return floors;
        }

        // 当たり判定の範囲を Transform から求める(作った直後や無効なオブジェクトでも正しく求まるように)
        static Bounds WorldBounds(BoxCollider box)
        {
            Transform t = box.transform;
            Vector3 s = t.lossyScale;
            Vector3 size = Vector3.Scale(box.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
            return new Bounds(t.TransformPoint(box.center), size);
        }

        // 床の下の支えを作り直す
        public void BuildSupports(List<Bounds> floors)
        {
            Transform old = transform.Find(kSupportRootName);
            if (old != null) DestroySafe(old.gameObject);
            if (m_supports.Style == SupportStyle.None || floors.Count == 0) return;

            var root = new GameObject(kSupportRootName).transform;
            root.SetParent(transform, false);
            root.position = new Vector3(transform.position.x, 0.0f, transform.position.z);

            int bookIndex = 0;
            for (int i = 0; i < floors.Count; i++)
            {
                Bounds f = floors[i];
                float w = f.size.x - m_supports.Inset * 2.0f;
                float d = f.size.z - m_supports.Inset * 2.0f;
                float top = f.min.y;
                if (w <= 0.2f || d <= 0.2f || top <= m_supports.BottomY) continue;

                var floorRoot = new GameObject($"Support_{i}").transform;
                floorRoot.SetParent(root, false);
                floorRoot.position = new Vector3(f.center.x, 0.0f, f.center.z);

                if (m_supports.Style == SupportStyle.Block)
                {
                    AddBox(floorRoot, "Pillar", new Vector3(0, (top + m_supports.BottomY) * 0.5f, 0), new Vector3(w, top - m_supports.BottomY, d), 0.0f, Cover(bookIndex++));
                    continue;
                }

                // 床の位置で決まる乱数(作り直しても同じ見た目になる)
                var rng = new System.Random(Mathf.RoundToInt(f.center.x * 73.0f) ^ Mathf.RoundToInt(f.center.z * 151.0f) ^ (i * 977));
                float y = m_supports.BottomY;
                while (y < top - 0.05f)
                {
                    float h = Mathf.Lerp(m_supports.MinPieceHeight, m_supports.MaxPieceHeight, (float)rng.NextDouble());
                    // 残りが薄すぎるなら、この1冊で天井まで埋める
                    if (top - (y + h) < m_supports.MinPieceHeight * 0.5f) h = top - y;
                    float yaw = ((float)rng.NextDouble() * 2.0f - 1.0f) * m_supports.MaxYaw;
                    // 回しても床からはみ出さない大きさに縮める
                    float shrink = 1.0f - Mathf.Sin(Mathf.Abs(yaw) * Mathf.Deg2Rad) * Mathf.Max(w, d) / Mathf.Min(w, d);
                    float scale = Mathf.Clamp(shrink, 0.6f, 1.0f) * Mathf.Lerp(0.9f, 1.0f, (float)rng.NextDouble());
                    if (m_supports.Style == SupportStyle.SlabStack)
                    {
                        AddBox(floorRoot, $"Slab_{bookIndex}", new Vector3(0, y + h * 0.5f, 0), new Vector3(w * scale, h, d * scale), yaw, Cover(bookIndex));
                    }
                    else
                    {
                        AddBook(floorRoot, $"Book_{bookIndex}", y, h, w * scale, d * scale, yaw, Cover(bookIndex), rng.Next(4));
                    }
                    bookIndex++;
                    y += h;
                }
            }
        }

        Material Cover(int index)
        {
            var list = m_supports.CoverMaterials;
            if (list == null || list.Length == 0) return null;
            return list[index % list.Length];
        }

        // 本1冊: 上下の表紙・背表紙と、少し内側に下がったページ
        void AddBook(Transform parent, string name, float bottom, float height, float w, float d, float yaw, Material cover, int spineSide)
        {
            var book = new GameObject(name).transform;
            book.SetParent(parent, false);
            book.localPosition = new Vector3(0.0f, bottom, 0.0f);
            book.localRotation = Quaternion.Euler(0.0f, yaw, 0.0f);

            float coverT = Mathf.Min(0.12f, height * 0.12f);
            float overhang = Mathf.Min(0.15f, Mathf.Min(w, d) * 0.05f);
            AddBox(book, "CoverBottom", new Vector3(0, coverT * 0.5f, 0), new Vector3(w, coverT, d), 0.0f, cover);
            AddBox(book, "CoverTop", new Vector3(0, height - coverT * 0.5f, 0), new Vector3(w, coverT, d), 0.0f, cover);

            // 背表紙の側(0: 西 1: 東 2: 南 3: 北)はページを寄せ、背表紙を立てる
            // (同じ平面に同じ向きの面が重なるとちらつくので、ページは背表紙の内側まで、背表紙は上下の表紙の間だけにする)
            Vector3 dir = spineSide switch { 0 => Vector3.left, 1 => Vector3.right, 2 => Vector3.back, _ => Vector3.forward };
            bool alongX = spineSide <= 1;
            float inner = height - coverT * 2.0f;
            Vector3 pageSize = alongX
                ? new Vector3(w - overhang - coverT, inner, d - overhang * 2.0f)
                : new Vector3(w - overhang * 2.0f, inner, d - overhang - coverT);
            Vector3 pageCenter = new Vector3(0, height * 0.5f, 0) + dir * ((overhang - coverT) * 0.5f);
            AddBox(book, "Pages", pageCenter, pageSize, 0.0f, m_supports.PageMaterial != null ? m_supports.PageMaterial : cover);
            Vector3 spineSize = alongX ? new Vector3(coverT, inner, d) : new Vector3(w, inner, coverT);
            Vector3 spineCenter = new Vector3(0, height * 0.5f, 0) + dir * ((alongX ? w : d) * 0.5f - coverT * 0.5f);
            AddBox(book, "Spine", spineCenter, spineSize, 0.0f, cover);
        }

        static void AddBox(Transform parent, string name, Vector3 localPos, Vector3 size, float yaw, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0.0f, yaw, 0.0f);
            go.transform.localScale = size;
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
#if UNITY_EDITOR
            go.isStatic = true;
#endif
        }

        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
