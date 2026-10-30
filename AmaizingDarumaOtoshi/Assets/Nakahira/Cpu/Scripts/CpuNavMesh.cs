using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Nakahira
{
    // CPU が奈落・壁を迂回する道を求めるためのナビメッシュ
    // ステージごとに焼き込まず、最初に道を聞かれたときにシーンの当たり判定から作る(積み木・キャラクター・トリガーは除く)
    // バンパーの周りは余裕を持って歩けないエリアにする(触れるとはじかれて落ちるため)
    // 道を求めるだけで NavMeshAgent は使わない(動きは DarumaCharacter の物理のまま)
    // ステージの床の配置が変わったら MarkDirty を呼ぶと、次に道を聞かれたときに作り直す
    // 動くギミックは焼き込まない:せり上がり塀・扇風機は当たり判定がある間だけ切り抜き(CpuGimmickObstacle)、
    // 移動床は乗り場から乗って行く道として使う(CpuMovingFloors / CpuRide)
    public static class CpuNavMesh
    {
        // ナビメッシュを作る範囲(ワールドの原点を中心とした立方体の一辺 m)
        const float kBuildSize = 300.0f;
        // 段差として登れる高さ(これより高い段は登れない。積み木の高さより低くする)
        const float kAgentClimb = 0.3f;
        const float kAgentHeight = 2.0f;
        const float kAgentSlope = 40.0f;
        // 道の始点を探す範囲(ジャンプ中でも足元の床が見つかるように、ジャンプの高さより大きくする)
        const float kStartSampleRadius = 2.5f;
        // 触れると危ないギミック(バンパー)の周りに、体の半径に加えて空ける距離(m)
        const float kHazardMargin = 0.5f;
        // ナビメッシュの「歩けない」エリア
        const int kNotWalkableArea = 1;

        // 触れると危ないギミックの範囲(水平の円)
        struct Hazard
        {
            public Vector3 Center;
            public float Radius;
        }

        static NavMeshDataInstance s_instance;
        static bool s_dirty = true;
        static readonly List<NavMeshBuildSource> s_sources = new List<NavMeshBuildSource>();
        static readonly List<NavMeshBuildMarkup> s_markups = new List<NavMeshBuildMarkup>();
        static readonly Vector3[] s_corners = new Vector3[64];
        static readonly List<Hazard> s_hazards = new List<Hazard>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Remove();
            s_dirty = true;
            // シーンを読み直したら(試合のやり直しなど)作り直す
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => MarkDirty();

        // 床の配置が変わった(ステージの切り替えなど)。次に道を聞かれたときに作り直す
        public static void MarkDirty()
        {
            s_dirty = true;
        }

        // from から to まで行ける道を求める。to の近く(水平に goalTolerance m 以内)まで行けなければ false
        // 道(曲がり角)は path に入る。length は道の長さ(水平)
        // 歩いて行けなければ、移動床に乗って行く道を探す(そのときの path は乗り場までの道。length は乗る手間も含む)
        public static bool TryFindPath(Vector3 from, Vector3 to, float goalTolerance, NavMeshPath path, out float length)
        {
            return TryFindPath(from, to, goalTolerance, path, out length, out _);
        }

        // ride:移動床に乗って行くなら、その乗り方(歩いて行けるなら無効)
        public static bool TryFindPath(Vector3 from, Vector3 to, float goalTolerance, NavMeshPath path, out float length, out CpuRidePlan ride)
        {
            ride = default;
            EnsureBuilt();
            // 移動床の上にいるなら、今止まっている(動いていれば次に止まる)地点の乗り場から道を求める
            // (床はナビメッシュに無いので。床が動いている間は降りない:CpuRide)
            float extra = 0.0f;
            if (CpuMovingFloors.TryGetFloorUnder(from, out var floor) && CpuMovingFloors.TryGetCurrentDock(floor, out Vector3 dock))
            {
                extra = FlatDistance(from, dock);
                from = dock;
            }
            if (TryFindDirect(from, to, goalTolerance, path, out length) || CpuMovingFloors.TryFindRide(from, to, goalTolerance, path, out length, out ride))
            {
                length += extra;
                return true;
            }
            return false;
        }

        // from から to まで歩いて行ける道を求める(移動床は使わない)
        internal static bool TryFindDirect(Vector3 from, Vector3 to, float goalTolerance, NavMeshPath path, out float length)
        {
            length = 0.0f;
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, kStartSampleRadius, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out NavMeshHit goal, goalTolerance, NavMesh.AllAreas)) return false;
            if (FlatDistance(goal.position, to) > goalTolerance) return false;
            if (!NavMesh.CalculatePath(start.position, goal.position, NavMesh.AllAreas, path)) return false;
            if (path.status != NavMeshPathStatus.PathComplete) return false;

            int count = path.GetCornersNonAlloc(s_corners);
            for (int i = 1; i < count; i++) length += FlatDistance(s_corners[i - 1], s_corners[i]);
            return true;
        }

        // pos(水平)が、触れると危ないギミックから clearance m 以内か(体の半径は含めないので呼ぶ側で足す)
        public static bool IsNearHazard(Vector3 pos, float clearance)
        {
            EnsureBuilt();
            foreach (var hazard in s_hazards)
            {
                if (FlatDistance(pos, hazard.Center) < hazard.Radius + clearance) return true;
            }
            return false;
        }

        private static void EnsureBuilt()
        {
            if (!s_dirty && s_instance.valid) return;
            s_dirty = false;
            Build();
        }

        private static void Build()
        {
            Remove();

            var bounds = new Bounds(Vector3.zero, Vector3.one * kBuildSize);
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, s_markups, s_sources);
            // 動くもの(積み木・キャラクター・動くギミック)と、通り抜けられるトリガーは足場・壁にしない
            // (せり上がり塀・扇風機は CpuGimmickObstacle で切り抜き、移動床は CpuMovingFloors で乗り場から使う)
            s_sources.RemoveAll(source =>
            {
                var col = source.component as Collider;
                if (col == null) return false;
                return col.isTrigger
                    || col.GetComponentInParent<TumikiBlock>() != null
                    || col.GetComponentInParent<DarumaCharacter>() != null
                    || IsDynamicGimmick(col);
            });
            AddHazards();

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            // 体の半径ぶん床の縁から離れた道になる(奈落ぎわを歩かない)
            settings.agentRadius = DarumaCharacter.kBodyRadius;
            settings.agentHeight = kAgentHeight;
            settings.agentClimb = kAgentClimb;
            settings.agentSlope = kAgentSlope;

            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, s_sources, bounds, Vector3.zero, Quaternion.identity);
            if (data != null) s_instance = NavMesh.AddNavMeshData(data);
            s_sources.Clear();

            AttachObstacles();
            CpuMovingFloors.Build();
        }

        private static bool IsDynamicGimmick(Collider col)
        {
            return col.GetComponentInParent<Nakagawa.RisingWallGimmick>() != null
                || col.GetComponentInParent<Nakagawa.FanGimmick>() != null
                || col.GetComponentInParent<Nakagawa.MovingFloorGimmick>() != null;
        }

        // せり上がり塀・扇風機の本体を、当たり判定がある間だけナビメッシュから切り抜く
        // (せり上がり塀は床に戻っている間は当たり判定が無効なので、無効なものも含めて付けておく)
        private static void AttachObstacles()
        {
            foreach (var wall in Object.FindObjectsByType<Nakagawa.RisingWallGimmick>()) AttachObstacles(wall);
            foreach (var fan in Object.FindObjectsByType<Nakagawa.FanGimmick>()) AttachObstacles(fan);
        }

        private static void AttachObstacles(Component gimmick)
        {
            foreach (var box in gimmick.GetComponentsInChildren<BoxCollider>(true))
            {
                if (!box.isTrigger) CpuGimmickObstacle.Attach(box);
            }
        }

        // 触れると危ないギミック(バンパー:触れるとはじかれて奈落に落ちることがある)を覚え、
        // その周りを余裕を持って歩けないエリアにする(体の半径ぶんだけだと、道がぎりぎりを通って触れてしまう)
        private static void AddHazards()
        {
            s_hazards.Clear();
            foreach (var bumper in Object.FindObjectsByType<Nakagawa.BumperGimmick>())
            {
                foreach (var col in bumper.GetComponentsInChildren<Collider>())
                {
                    if (col.isTrigger) continue;
                    Bounds b = col.bounds;
                    s_hazards.Add(new Hazard { Center = b.center, Radius = Mathf.Max(b.extents.x, b.extents.z) });
                    s_sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        size = b.size + new Vector3(kHazardMargin * 2.0f, kAgentHeight, kHazardMargin * 2.0f),
                        transform = Matrix4x4.Translate(b.center),
                        area = kNotWalkableArea,
                    });
                }
            }
        }

        private static void Remove()
        {
            if (s_instance.valid) s_instance.Remove();
            s_instance = default;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0.0f;
            b.y = 0.0f;
            return Vector3.Distance(a, b);
        }
    }
}
