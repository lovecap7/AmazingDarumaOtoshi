using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Nakahira;

namespace Nakagawa
{
    // 床どうしが重なっている所で、他の床にめり込んでいる床の側面(継ぎ目)に積み木が引っかかって止まらないようにする
    // 物理の接触を書き換えられる当たり判定(Register で登録した積み木)と床の接触のうち、
    // 横向きで、接触点が別の床の中に埋まっているもの(外から見えない側面)を無視する
    // (床の上面や、外に出ている崖の側面との接触はそのまま残す)
    public static class FloorSeamFilter
    {
        // 横向きとみなす接触の法線の上下成分
        const float kSideNormalY = 0.7f;
        // 接触点が別の床の「中」にあるとみなす余裕(横は外側へ少し広げ、辺どうしで接しているだけの床も継ぎ目とみなす。上は床の上面より少し上まで)
        const float kInsideInset = -0.03f;
        const float kTopTolerance = 0.05f;
        // 継ぎ目とみなす、触れている床の上面から接触点までの深さの上限(積み木1段の高さ)
        const float kMaxSeamDepth = TumikiBlock.kHeight;
        // 床の一覧を作り直す間隔(秒)。ステージの切り替え・シーンの読み込みでもすぐに作り直す
        const float kRebuildInterval = 1.0f;

        // 床の箱(物理の計算中に別スレッドから読むので、Unityの物を使わず値だけ持つ)
        struct FloorBox
        {
            public Matrix4x4 WorldToLocal; // 箱の中心を原点・箱の向きに合わせた座標へ(大きさは実寸のまま)
            public Vector3 HalfSize;       // 各辺の長さの半分(実寸)
            public float Top;              // 上面の高さ
        }

        // 多角形の柱の床(六角形の氷のタイルなど。RegisterPrism で登録)。上から見た形(凸多角形の組・ワールドの XZ)と上面・底面の高さ
        struct PrismFloor
        {
            public Vector2[][] Polygons;
            public float Top, Bottom;
            public bool Active;
        }

        static readonly List<PrismFloor> s_prisms = new List<PrismFloor>();
        static readonly Dictionary<EntityId, int> s_prismIndex = new Dictionary<EntityId, int>();

        static readonly List<FloorBox> s_floors = new List<FloorBox>();
        static readonly Dictionary<EntityId, int> s_floorIndex = new Dictionary<EntityId, int>();
        static bool s_dirty = true;
        static float s_lastBuildTime = float.NegativeInfinity;
        static bool s_hooked;
        // 動く床(せり上がり塀・移動床など。RegisterMoving で登録)。位置が毎ステップ変わるので、物理の更新の前に毎回写し直す
        static readonly List<BoxCollider> s_moving = new List<BoxCollider>();
        static int s_staticCount;
        static float s_movingStep = -1.0f;
        static int s_movingFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_floors.Clear();
            s_floorIndex.Clear();
            s_dirty = true;
            s_lastBuildTime = float.NegativeInfinity;
            s_moving.Clear();
            s_staticCount = 0;
            s_movingStep = -1.0f;
            s_movingFrame = -1;
            s_prisms.Clear();
            s_prismIndex.Clear();
            if (s_hooked)
            {
                Physics.ContactModifyEvent -= OnContactModify;
                Physics.ContactModifyEventCCD -= OnContactModify;
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                s_hooked = false;
            }
        }

        // 積み木の当たり判定を、継ぎ目の接触を無視する対象にする
        public static void Register(Collider collider)
        {
            if (collider == null) return;
            collider.hasModifiableContacts = true;
            if (s_hooked) return;
            Physics.ContactModifyEvent += OnContactModify;
            Physics.ContactModifyEventCCD += OnContactModify;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            s_hooked = true;
        }

        // 床の配置が変わった(ステージの切り替えなど)。次の物理の更新の前に作り直す
        public static void MarkDirty() => s_dirty = true;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => s_dirty = true;
        static void OnSceneUnloaded(Scene scene) => s_dirty = true;

        // 動く床の箱を、継ぎ目を調べる床に加える(上面が水平な箱。隣り合った塀の上や、床に横付けした移動床の継ぎ目で引っかからないように)
        // 壁としての側面(外に出ている面)との接触はそのまま残る
        public static void RegisterMoving(BoxCollider box)
        {
            if (box != null && !s_moving.Contains(box)) s_moving.Add(box);
        }

        public static void UnregisterMoving(BoxCollider box)
        {
            s_moving.Remove(box);
        }

        // 多角形の柱の床(メッシュの当たり判定。動かない物)を、継ぎ目を調べる床に加える
        // polygons: 上から見た形(凸多角形の組。ワールドの X と Z)。top / bottom: 上面・底面の高さ
        // 隣り合った柱の床どうし・箱の床との継ぎ目(外から見えない側面)に、積み木が引っかからないようにする
        public static void RegisterPrism(Collider collider, IReadOnlyList<Vector2[]> polygons, float top, float bottom)
        {
            if (collider == null || polygons == null) return;
            var floor = new PrismFloor { Polygons = new Vector2[polygons.Count][], Top = top, Bottom = bottom, Active = true };
            for (int i = 0; i < polygons.Count; i++) floor.Polygons[i] = polygons[i];
            EntityId id = collider.GetEntityId();
            if (s_prismIndex.TryGetValue(id, out int index)) s_prisms[index] = floor;
            else
            {
                s_prismIndex[id] = s_prisms.Count;
                s_prisms.Add(floor);
            }
        }

        // 柱の床を一時的に外す・戻す(崩れて落ちた氷のタイルなど)
        public static void SetPrismActive(Collider collider, bool active)
        {
            if (collider == null || !s_prismIndex.TryGetValue(collider.GetEntityId(), out int index)) return;
            var floor = s_prisms[index];
            floor.Active = active;
            s_prisms[index] = floor;
        }

        public static void UnregisterPrism(Collider collider)
        {
            if (collider == null) return;
            SetPrismActive(collider, false);
        }

        // 物理の更新の前(FixedUpdate)にメインスレッドから呼ぶ。必要なら床の一覧を作り直す(動く床は毎回写し直す)
        public static void UpdateFloors()
        {
            bool rebuild = s_dirty || Time.time - s_lastBuildTime >= kRebuildInterval;
            // 動く床は物理の1ステップに1回だけ写す(積み木ごとに呼ばれるので)
            if (!rebuild && s_movingStep == Time.fixedTime && s_movingFrame == Time.frameCount) return;
            s_movingStep = Time.fixedTime;
            s_movingFrame = Time.frameCount;
            if (rebuild)
            {
                s_dirty = false;
                s_lastBuildTime = Time.time;

                s_floors.Clear();
                s_floorIndex.Clear();
                foreach (var box in Object.FindObjectsByType<BoxCollider>())
                {
                    if (IsFloor(box)) AddBox(box);
                }
                s_staticCount = s_floors.Count;
            }
            UpdateMovingFloors();
        }

        static void UpdateMovingFloors()
        {
            if (s_floors.Count > s_staticCount)
            {
                // 前回写した動く床を外す
                foreach (var box in s_moving) if (box != null) s_floorIndex.Remove(box.GetEntityId());
                s_floors.RemoveRange(s_staticCount, s_floors.Count - s_staticCount);
            }
            for (int i = s_moving.Count - 1; i >= 0; i--)
            {
                var box = s_moving[i];
                if (box == null) { s_moving.RemoveAt(i); continue; }
                if (!box.enabled || box.isTrigger || !box.gameObject.activeInHierarchy) continue;
                if (Vector3.Dot(box.transform.up, Vector3.up) <= 0.999f) continue;
                AddBox(box);
            }
        }

        static void AddBox(BoxCollider box)
        {
            Transform t = box.transform;
            Vector3 scale = t.lossyScale;
            Vector3 half = Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 0.5f;
            Vector3 center = t.TransformPoint(box.center);
            Quaternion rotation = t.rotation;
            // 動く物は見た目(補間した Transform)ではなく、物理の位置で調べる
            var rb = box.attachedRigidbody;
            if (rb != null)
            {
                Transform rt = rb.transform;
                Vector3 local = rt.InverseTransformPoint(center);
                Quaternion localRot = Quaternion.Inverse(rt.rotation) * rotation;
                center = rb.position + rb.rotation * Vector3.Scale(local, rt.lossyScale);
                rotation = rb.rotation * localRot;
            }
            var worldToLocal = Matrix4x4.TRS(center, rotation, Vector3.one).inverse;
            s_floorIndex[box.GetEntityId()] = s_floors.Count;
            s_floors.Add(new FloorBox { WorldToLocal = worldToLocal, HalfSize = half, Top = center.y + half.y });
        }

        // 床とみなす当たり判定: 動かない箱で、壁(ReflectWall)ではなく、上面が水平
        static bool IsFloor(BoxCollider box)
        {
            if (!box.enabled || box.isTrigger || !box.gameObject.activeInHierarchy) return false;
            if (box.attachedRigidbody != null) return false;
            if (box.GetComponentInParent<ReflectWall>() != null) return false;
            return Vector3.Dot(box.transform.up, Vector3.up) > 0.999f;
        }

        // 物理の計算中に呼ばれる(別スレッドの場合がある)。値だけを読み、Unityの物には触れない
        static void OnContactModify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            for (int p = 0; p < pairs.Length; p++)
            {
                var pair = pairs[p];
                int floor = -1, prism = -1;
                float top;
                if (s_floorIndex.TryGetValue(pair.otherColliderEntityId, out floor)
                    || s_floorIndex.TryGetValue(pair.colliderEntityId, out floor)) top = s_floors[floor].Top;
                else if (s_prismIndex.TryGetValue(pair.otherColliderEntityId, out prism)
                    || s_prismIndex.TryGetValue(pair.colliderEntityId, out prism))
                {
                    floor = -1;
                    if (!s_prisms[prism].Active) continue;
                    top = s_prisms[prism].Top;
                }
                else continue;

                for (int i = 0; i < pair.contactCount; i++)
                {
                    if (Mathf.Abs(pair.GetNormal(i).y) >= kSideNormalY) continue;
                    Vector3 point = pair.GetPoint(i);
                    // 触れている箱の上面が接触点より十分高い(扇風機の台など背の高い物の側面)なら継ぎ目ではない
                    if (top - point.y > kMaxSeamDepth) continue;
                    if (IsInsideOtherFloor(point, floor, prism)) pair.IgnoreContact(i);
                }
                pairs[p] = pair;
            }
        }

        // 点が凸多角形の中(辺から margin だけ外まで含む)か。多角形の向き(時計回り・反時計回り)は問わない
        static bool InPolygon(Vector2[] poly, Vector2 p, float margin)
        {
            int n = poly.Length;
            if (n < 3) return false;
            // 向き(面積の符号)
            float area = 0.0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }
            float sign = area >= 0.0f ? 1.0f : -1.0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 0.0001f) continue;
                // 内側が正になる、辺からの距離
                float dist = sign * (d.x * (p.y - a.y) - d.y * (p.x - a.x)) / len;
                if (dist < -margin) return false;
            }
            return true;
        }

        // 接触点が、触れている床以外の床の中に埋まっているか・その床の面に接しているか(外から見えない側面か)
        // exclude / excludePrism: 触れている箱の床・柱の床の番号(なければ -1)
        static bool IsInsideOtherFloor(Vector3 point, int exclude, int excludePrism = -1)
        {
            var xz = new Vector2(point.x, point.z);
            for (int i = 0; i < s_prisms.Count; i++)
            {
                if (i == excludePrism) continue;
                PrismFloor f = s_prisms[i];
                if (!f.Active || point.y > f.Top + kTopTolerance || point.y < f.Bottom) continue;
                foreach (var poly in f.Polygons) if (InPolygon(poly, xz, -kInsideInset)) return true;
            }
            for (int i = 0; i < s_floors.Count; i++)
            {
                if (i == exclude) continue;
                FloorBox f = s_floors[i];
                Vector3 l = f.WorldToLocal.MultiplyPoint3x4(point);
                if (Mathf.Abs(l.x) >= f.HalfSize.x - kInsideInset) continue;
                if (Mathf.Abs(l.z) >= f.HalfSize.z - kInsideInset) continue;
                if (l.y > f.HalfSize.y + kTopTolerance || l.y < -f.HalfSize.y) continue;
                return true;
            }
            return false;
        }
    }
}
