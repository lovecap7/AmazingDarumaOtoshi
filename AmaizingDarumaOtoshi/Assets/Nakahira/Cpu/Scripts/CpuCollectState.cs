using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 状態「積む」:近くの積み木を選んで拾う
    //  ・近さは歩く道の長さで比べ、歩いて行けない積み木は狙わない
    //  ・取り合い:狙う積み木の近く(ContestRadius)に、自分が有利でない生存者がいれば譲って別の積み木を狙う
    //    (有利なら強気にそのまま取りに行く)
    //  ・幽霊の近くの積み木は後回しにする
    //  ・道が危険な相手(逃げる理由になる相手)のそばを通るなら、中継点を経由して遠回りする。遠回りでも行けなければ狙わない
    public class CpuCollectState : CpuState
    {
        // 1回選び直すときに、遠回りの道を探す積み木の最大数(重くならないように)
        const int kMaxDetourBlocks = 4;

        readonly CpuPickupMotor m_pickup = new CpuPickupMotor();
        readonly CpuDetourFinder m_detour = new CpuDetourFinder();
        readonly List<CpuBlockSearch.Candidate> m_candidates = new List<CpuBlockSearch.Candidate>();
        NavMeshPath m_queryPath;
        float m_thinkTimer;
        CpuContext m_context;   // 候補のコストを求めるときに使う(毎回ラムダを作らないように)
        readonly System.Func<TumikiBlock, float> m_extraCost;

        public override string Name => "積む";
        public CpuPickupMotor Pickup => m_pickup;

        public CpuCollectState()
        {
            m_extraCost = ExtraCost;
        }

        public override float Evaluate(CpuContext c)
        {
            if (c.StackCount >= c.Params.CollectUntil) return 0.0f;
            // 拾える積み木がなくても、ほかにやることがなければ積む(待つ)
            if (!CpuBlockSearch.HasAnyCollectable()) return c.Params.CollectScore * 0.1f;
            // 満タン近くなら、攻めに行ける相手がいればそちらを選ぶ(遊ぶ)
            return c.IsPlayful ? c.Params.PlayfulCollectScore : c.Params.CollectScore;
        }

        public override void Enter(CpuContext c)
        {
            m_thinkTimer = 0.0f;
            m_pickup.Clear();
        }

        public override void Exit(CpuContext c)
        {
            m_pickup.Clear();
        }

        public override bool Tick(CpuContext c, ref DarumaCommand cmd)
        {
            if (c.StackCount >= c.Params.CollectUntil) return false;

            m_thinkTimer -= c.DeltaTime;
            if (m_thinkTimer <= 0.0f)
            {
                m_thinkTimer = c.Params.ThinkInterval;
                ChooseTarget(c);
            }
            if (m_pickup.Target == null) return true;

            // 拾った・諦めたら、すぐ次を選ぶ
            if (m_pickup.Tick(c, ref cmd) != CpuPickupMotor.Status.Running) m_thinkTimer = 0.0f;
            return true;
        }

        private void ChooseTarget(CpuContext c)
        {
            if (m_queryPath == null) m_queryPath = new NavMeshPath();
            // 取り合いで譲ることになった積み木は、しばらく狙わない(相手が少し離れても、すぐ戻ってこないように)
            if (m_pickup.Target != null && CpuBlockSearch.ShouldYield(c, m_pickup.Target.transform.position))
            {
                c.GiveUp(m_pickup.Target, c.Params.YieldCooldown);
            }
            m_context = c;
            CpuBlockSearch.Gather(c, m_candidates, m_extraCost);
            m_context = null;

            TumikiBlock current = m_pickup.Target;
            TumikiBlock best = null;
            float bestCost = float.MaxValue;
            bool bestHasWaypoint = false;
            Vector3 bestWaypoint = Vector3.zero;
            float currentCost = float.MaxValue;
            bool currentHasWaypoint = false;
            Vector3 currentWaypoint = Vector3.zero;
            int queries = 0;
            int detours = 0;
            Vector3 pos = c.Position;
            m_detour.Reset();
            foreach (var cand in m_candidates)
            {
                bool isCurrent = cand.Block == current;
                // 道の長さはまっすぐの距離以上なので、下限が今の一番以上ならこれ以降は調べなくてよい
                // (ただし今狙っている積み木のコストは、乗り換えるか決めるのに必要)
                if (!isCurrent && (cand.LowerBound >= bestCost || queries >= CpuBlockSearch.MaxPathQueries)) continue;
                queries++;
                Vector3 bp = cand.Block.transform.position;
                if (!CpuBlockSearch.TryGetPathLength(pos, bp, c.Params, m_queryPath, out float length)) continue;

                // 一番短い道が危険な相手のそばを通るなら、中継点を経由して遠回りする道を探す
                // (取りに行って逃げる、を繰り返さないように。遠回りでも行けなければ狙わない)
                bool hasWaypoint = false;
                Vector3 waypoint = Vector3.zero;
                if (!CpuBlockSearch.AvoidsThreats(c, m_queryPath, c.Params.CollectThreatClearance))
                {
                    if (!isCurrent && detours >= kMaxDetourBlocks) continue;
                    detours++;
                    float limit = isCurrent ? float.MaxValue : bestCost - cand.ExtraCost;
                    if (!m_detour.TryFind(c, bp, limit, m_pickup.HasWaypoint, m_pickup.Waypoint, out length, out waypoint)) continue;
                    hasWaypoint = true;
                }

                float cost = length + cand.ExtraCost;
                if (isCurrent)
                {
                    currentCost = cost;
                    currentHasWaypoint = hasWaypoint;
                    currentWaypoint = waypoint;
                }
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = cand.Block;
                    bestHasWaypoint = hasWaypoint;
                    bestWaypoint = waypoint;
                }
            }

            // 今の狙いを続けられるなら、十分に良い積み木があるときだけ乗り換える(迷わないように)
            // (取り合いで譲ることになった積み木は候補から外れるので、ここで乗り換わる)
            if (current != null && currentCost < float.MaxValue && bestCost > currentCost - c.Params.SwitchMargin)
            {
                m_pickup.SetWaypoint(currentHasWaypoint, currentWaypoint);
                return;
            }
            if (best == null)
            {
                m_pickup.Clear();
                return;
            }
            m_pickup.SetTarget(best, c.Params);
            m_pickup.SetWaypoint(bestHasWaypoint, bestWaypoint);
        }

        // 候補の追加のコスト。取り合いで譲るべき積み木は除く(無限大)
        private float ExtraCost(TumikiBlock block)
        {
            Vector3 bp = block.transform.position;
            if (CpuBlockSearch.ShouldYield(m_context, bp)) return float.PositiveInfinity;
            return CpuBlockSearch.GhostCost(m_context, bp);
        }

        public override void DrawGizmos(CpuContext c)
        {
            Gizmos.color = Color.cyan;
            m_pickup.DrawGizmos(c.Self);
        }
    }
}
