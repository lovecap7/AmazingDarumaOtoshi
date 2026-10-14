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

        static readonly List<FloorBox> s_floors = new List<FloorBox>();
        static readonly Dictionary<EntityId, int> s_floorIndex = new Dictionary<EntityId, int>();
        static bool s_dirty = true;
        static float s_lastBuildTime = float.NegativeInfinity;
        static bool s_hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_floors.Clear();
            s_floorIndex.Clear();
            s_dirty = true;
            s_lastBuildTime = float.NegativeInfinity;
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

        // 物理の更新の前(FixedUpdate)にメインスレッドから呼ぶ。必要なら床の一覧を作り直す
        public static void UpdateFloors()
        {
            if (!s_dirty && Time.time - s_lastBuildTime < kRebuildInterval) return;
            s_dirty = false;
            s_lastBuildTime = Time.time;

            s_floors.Clear();
            s_floorIndex.Clear();
            foreach (var box in Object.FindObjectsByType<BoxCollider>())
            {
                if (!IsFloor(box)) continue;
                Transform t = box.transform;
                Vector3 scale = t.lossyScale;
                Vector3 half = Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 0.5f;
                Vector3 center = t.TransformPoint(box.center);
                var worldToLocal = Matrix4x4.TRS(center, t.rotation, Vector3.one).inverse;
                s_floorIndex[box.GetEntityId()] = s_floors.Count;
                s_floors.Add(new FloorBox { WorldToLocal = worldToLocal, HalfSize = half, Top = center.y + half.y });
            }
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
                int floor;
                if (!s_floorIndex.TryGetValue(pair.otherColliderEntityId, out floor)
                    && !s_floorIndex.TryGetValue(pair.colliderEntityId, out floor)) continue;

                float top = s_floors[floor].Top;
                for (int i = 0; i < pair.contactCount; i++)
                {
                    if (Mathf.Abs(pair.GetNormal(i).y) >= kSideNormalY) continue;
                    Vector3 point = pair.GetPoint(i);
                    // 触れている箱の上面が接触点より十分高い(扇風機の台など背の高い物の側面)なら継ぎ目ではない
                    if (top - point.y > kMaxSeamDepth) continue;
                    if (IsInsideOtherFloor(point, floor)) pair.IgnoreContact(i);
                }
                pairs[p] = pair;
            }
        }

        // 接触点が、触れている床以外の床の中に埋まっているか・その床の面に接しているか(外から見えない側面か)
        static bool IsInsideOtherFloor(Vector3 point, int exclude)
        {
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
