using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 動きの部品「道をたどる」:目的地までのナビメッシュの道を一定間隔で求め直し、曲がり角を順にたどる
    public class CpuPathFollower
    {
        // 道の曲がり角にこの距離(m)まで近づいたら、次の曲がり角へ向かう
        const float kCornerReach = 0.35f;

        NavMeshPath m_path;           // MonoBehaviour の初期化中には作れないので、使うときに作る
        readonly Vector3[] m_corners = new Vector3[64];
        int m_count;
        int m_index;
        float m_repathTimer;
        Vector3 m_destination;
        float m_tolerance = 1.0f;
        CpuRidePlan m_ride;
        float m_lastPathTime = float.NegativeInfinity;

        // 移動床に乗って行く道なら、その乗り方(道は乗り場まで。乗り降りは CpuRide が行う)
        public bool HasRide => m_ride.IsValid;
        public CpuRidePlan Ride => m_ride;
        // 最後に道を求めた時刻(使われなくなった古い道で乗り降りしないように)
        public float LastPathTime => m_lastPathTime;

        // 今たどっている道(デバッグ表示用。0番は道を求めたときの位置)
        public int Count => m_count;
        public int Index => m_index;
        public Vector3 Corner(int i) => m_corners[i];
        public Vector3 Destination => m_destination;

        // 目的地を決める(道は次の Update で求める)。tolerance:目的地のこの距離(m)以内まで行ければよい
        public void SetDestination(Vector3 destination, float tolerance)
        {
            m_destination = destination;
            m_tolerance = tolerance;
            m_repathTimer = 0.0f;
        }

        // 動く目的地(動いている積み木など)を追う。道は決まった間隔で求め直す
        public void MoveDestination(Vector3 destination)
        {
            m_destination = destination;
        }

        public void Clear()
        {
            m_count = 0;
            m_index = 0;
            m_repathTimer = 0.0f;
            m_ride = default;
        }

        // 求め直す時期なら道を求め直す。求め直せたら true(remaining に道の長さ)
        // 道が見つからなければ、まっすぐ向かう(曲がり角なし)
        public bool Update(Vector3 pos, CpuParams p, float dt, out float remaining)
        {
            if (m_path == null) m_path = new NavMeshPath();
            remaining = 0.0f;
            m_repathTimer -= dt;
            if (m_repathTimer > 0.0f) return false;

            m_repathTimer = p.RepathInterval;
            m_lastPathTime = Time.time;
            m_count = 0;
            m_index = 0;
            if (!CpuNavMesh.TryFindPath(pos, m_destination, m_tolerance, m_path, out remaining, out m_ride)) return false;
            m_count = m_path.GetCornersNonAlloc(m_corners);
            m_index = Mathf.Min(1, m_count);
            return true;
        }

        // 曲がり角を進め、最後のまっすぐな区間(目的地まで曲がらずに行ける)なら true
        // そうでなければ next に次の曲がり角を入れる
        // 移動床に乗って行く道なら、目的地ではなく乗り場へ向かう(最後の区間ではない)
        public bool IsOnFinalLeg(Vector3 pos, out Vector3 next)
        {
            while (m_index < m_count - 1 && CpuContext.FlatDistance(pos, m_corners[m_index]) < kCornerReach) m_index++;
            if (m_ride.IsValid)
            {
                next = m_index < m_count ? m_corners[m_index] : m_ride.BoardSpot;
                return false;
            }
            if (m_index < m_count - 1)
            {
                next = m_corners[m_index];
                return false;
            }
            next = m_destination;
            return true;
        }

        // destination へ歩く。進む先(destination より手前)が奈落なら止まる
        public static void Walk(Vector3 pos, Vector3 destination, float speed, CpuParams p, ref DarumaCommand cmd)
        {
            Vector3 to = destination - pos;
            to.y = 0.0f;
            float dist = to.magnitude;
            if (dist < 0.001f)
            {
                cmd.Move = Vector2.zero;
                return;
            }
            Vector3 dir = to / dist;
            // 曲がり角の先は調べない(道は曲がり角で向きを変えるので、その先が奈落でも落ちない)
            float lookAhead = Mathf.Min(p.EdgeLookAhead + speed * 0.1f, dist);
            bool safe = CpuSensor.HasGround(pos + dir * lookAhead, p.GroundProbeDepth);
            cmd.Move = safe ? new Vector2(dir.x, dir.z) : Vector2.zero;
        }

        public void DrawGizmos(Vector3 from)
        {
            for (int i = m_index; i < m_count; i++)
            {
                Gizmos.DrawLine(from + Vector3.up * 0.1f, m_corners[i] + Vector3.up * 0.1f);
                from = m_corners[i];
            }
        }
    }
}
