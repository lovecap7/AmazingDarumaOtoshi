using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Nakahira
{
    // CPU が奈落・壁を迂回する道を求めるためのナビメッシュ
    // ステージごとに焼き込まず、最初に道を聞かれたときにシーンの当たり判定から作る(積み木・キャラクター・トリガーは除く)
    // 道を求めるだけで NavMeshAgent は使わない(動きは DarumaCharacter の物理のまま)
    // ステージの床の配置が変わったら MarkDirty を呼ぶと、次に道を聞かれたときに作り直す
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

        static NavMeshDataInstance s_instance;
        static bool s_dirty = true;
        static readonly List<NavMeshBuildSource> s_sources = new List<NavMeshBuildSource>();
        static readonly List<NavMeshBuildMarkup> s_markups = new List<NavMeshBuildMarkup>();
        static readonly Vector3[] s_corners = new Vector3[64];

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

        // from から to まで歩いて行ける道を求める。to の近く(水平に goalTolerance m 以内)まで行けなければ false
        // 道(曲がり角)は path に入る。length は道の長さ(水平)
        public static bool TryFindPath(Vector3 from, Vector3 to, float goalTolerance, NavMeshPath path, out float length)
        {
            length = 0.0f;
            EnsureBuilt();
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, kStartSampleRadius, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out NavMeshHit goal, goalTolerance, NavMesh.AllAreas)) return false;
            if (FlatDistance(goal.position, to) > goalTolerance) return false;
            if (!NavMesh.CalculatePath(start.position, goal.position, NavMesh.AllAreas, path)) return false;
            if (path.status != NavMeshPathStatus.PathComplete) return false;

            int count = path.GetCornersNonAlloc(s_corners);
            for (int i = 1; i < count; i++) length += FlatDistance(s_corners[i - 1], s_corners[i]);
            return true;
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
            // 動くもの(積み木・キャラクター)と、通り抜けられるトリガーは足場・壁にしない
            s_sources.RemoveAll(source =>
            {
                var col = source.component as Collider;
                if (col == null) return false;
                return col.isTrigger
                    || col.GetComponentInParent<TumikiBlock>() != null
                    || col.GetComponentInParent<DarumaCharacter>() != null;
            });

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            // 体の半径ぶん床の縁から離れた道になる(奈落ぎわを歩かない)
            settings.agentRadius = DarumaCharacter.kBodyRadius;
            settings.agentHeight = kAgentHeight;
            settings.agentClimb = kAgentClimb;
            settings.agentSlope = kAgentSlope;

            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, s_sources, bounds, Vector3.zero, Quaternion.identity);
            if (data != null) s_instance = NavMesh.AddNavMeshData(data);
            s_sources.Clear();
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
