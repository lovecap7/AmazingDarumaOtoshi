using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 危険な相手のそばを通らずに積み木まで行く道を、中継点を経由して探す
    // (一番短い道が危険な相手のそばを通るとき、橋が2本あれば反対側から回る、など)
    // 中継点の候補は自分の周りの点。「自分 → 中継点」「中継点 → 積み木」の両方の道が危険な相手から離れていればよい
    public class CpuDetourFinder
    {
        // 中継点の候補:自分の周りの方向の数と距離(m)
        const int kDirections = 12;
        static readonly float[] kRadii = { 4.0f, 8.0f, 12.0f };
        // 中継点の候補をナビメッシュ上に寄せる範囲(m)
        const float kSampleRadius = 1.5f;
        // 中継点に着いたとみなす距離(m)
        const float kWaypointTolerance = 0.5f;
        // 積み木1つにつき、中継点から積み木までの道を求める最大の回数(近い中継点から)
        const int kMaxQueriesPerBlock = 6;

        struct Waypoint
        {
            public Vector3 Position;
            public float Length;    // 自分から中継点までの(安全な)道の長さ
        }

        readonly List<Waypoint> m_waypoints = new List<Waypoint>();
        readonly List<KeyValuePair<int, float>> m_order = new List<KeyValuePair<int, float>>();
        NavMeshPath m_path;
        bool m_ready;

        // 選び直すたびに呼ぶ(中継点の候補は、必要になったときに1回だけ求める)
        public void Reset()
        {
            m_ready = false;
        }

        // 中継点を経由して積み木まで安全に行ける道を探す。lengthLimit より短い道が見つかれば true
        // preferred:今使っている中継点があれば渡す(候補に加えて、なるべく同じ中継点を使い続ける)
        public bool TryFind(CpuContext c, Vector3 blockPos, float lengthLimit, bool hasPreferred, Vector3 preferred,
            out float length, out Vector3 waypoint)
        {
            length = lengthLimit;
            waypoint = Vector3.zero;
            EnsureWaypoints(c, hasPreferred, preferred);

            // 「自分 → 中継点」の長さ + 中継点から積み木までのまっすぐの距離(道の長さの下限)の小さい順に調べる
            m_order.Clear();
            for (int i = 0; i < m_waypoints.Count; i++)
            {
                m_order.Add(new KeyValuePair<int, float>(i, m_waypoints[i].Length + CpuContext.FlatDistance(m_waypoints[i].Position, blockPos)));
            }
            m_order.Sort((a, b) => a.Value.CompareTo(b.Value));

            bool found = false;
            int queries = 0;
            foreach (var pair in m_order)
            {
                if (pair.Value >= length || queries >= kMaxQueriesPerBlock) break;
                queries++;
                Waypoint w = m_waypoints[pair.Key];
                if (!CpuNavMesh.TryFindPath(w.Position, blockPos, CpuPickupMotor.GoalTolerance, m_path, out float rest)) continue;
                if (!CpuBlockSearch.AvoidsThreats(c, m_path, c.Params.CollectThreatClearance)) continue;
                float total = w.Length + rest;
                if (total >= length) continue;
                length = total;
                waypoint = w.Position;
                found = true;
            }
            return found;
        }

        // 中継点の候補(自分から危険な相手のそばを通らずに行ける点)を求める
        private void EnsureWaypoints(CpuContext c, bool hasPreferred, Vector3 preferred)
        {
            if (m_ready) return;
            m_ready = true;
            if (m_path == null) m_path = new NavMeshPath();
            m_waypoints.Clear();

            Vector3 pos = c.Position;
            if (hasPreferred) TryAddWaypoint(c, pos, preferred);
            for (int r = 0; r < kRadii.Length; r++)
            {
                for (int k = 0; k < kDirections; k++)
                {
                    float angle = (k + 0.5f * r) * Mathf.PI * 2.0f / kDirections;
                    Vector3 sample = pos + new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * kRadii[r];
                    if (!NavMesh.SamplePosition(sample, out NavMeshHit hit, kSampleRadius, NavMesh.AllAreas)) continue;
                    TryAddWaypoint(c, pos, hit.position);
                }
            }
        }

        private void TryAddWaypoint(CpuContext c, Vector3 from, Vector3 point)
        {
            if (!CpuNavMesh.TryFindPath(from, point, kWaypointTolerance, m_path, out float length)) return;
            if (!CpuBlockSearch.AvoidsThreats(c, m_path, c.Params.CollectThreatClearance)) return;
            m_waypoints.Add(new Waypoint { Position = point, Length = length });
        }
    }
}
