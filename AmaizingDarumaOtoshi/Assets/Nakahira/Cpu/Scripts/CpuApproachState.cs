using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 状態「攻めに行く」:盤面に拾える積み木がない(または積み終わった)とき、相手を倒しに近づく
    //  ・積むが優先(拾える積み木があるうちは選ばれない)
    //  ・狙う相手は、自分が有利な(段数の差 ≥ AdvantageMargin)相手のうち、攻めの点数(CpuAttackTarget)が一番高い相手
    //    有利な相手がいなければ選ばれない(待つ状態で積み木の供給を待つ)
    //  ・道(ナビメッシュ)をたどって近づくだけ。近づけば、殴る(3m)・撃つ(8m)が点数で選ばれて引き継ぐ
    //  ・道が危険な相手のそばを通るなら、その相手は狙わない(近づいて逃げる、を繰り返さないように)
    public class CpuApproachState : CpuState
    {
        readonly CpuPathFollower m_route = new CpuPathFollower();
        NavMeshPath m_queryPath;   // MonoBehaviour の初期化中には作れないので、使うときに作る
        MatchPlayer m_target;
        float m_thinkTimer;

        public override string Name => "攻めに行く";
        public MatchPlayer Target => m_target;

        public override float Evaluate(CpuContext c)
        {
            if (!ShouldApproach(c)) return 0.0f;
            // 満タン近く(遊ぶ)でも点数は同じ(遊ぶ側では積むの点数が下がるので、積むには勝つ)。
            // 今の状態に足す StateStickiness 込みでも、殴る・撃つより低くなるようにしておく(近づいたら必ず引き継ぐ)
            float score = c.Params.ApproachScore;
            if (m_target != null && IsValidTarget(c, c.Perception.Find(m_target))) return score;
            return ChooseTarget(c) != null ? score : 0.0f;
        }

        public override void Enter(CpuContext c)
        {
            m_target = null;
            m_thinkTimer = 0.0f;
            m_route.Clear();
        }

        public override void Exit(CpuContext c)
        {
            m_target = null;
            m_route.Clear();
        }

        public override bool Tick(CpuContext c, ref DarumaCommand cmd)
        {
            if (!ShouldApproach(c)) return false;

            m_thinkTimer -= c.DeltaTime;
            if (m_thinkTimer <= 0.0f || m_target == null)
            {
                m_thinkTimer = c.Params.ThinkInterval;
                CpuOpponent best = ChooseTarget(c);
                if (best == null) return false;
                if (best.Player != m_target)
                {
                    m_target = best.Player;
                    m_route.SetDestination(best.Position, CpuPickupMotor.GoalTolerance);
                }
            }
            CpuOpponent o = c.Perception.Find(m_target);
            if (!IsValidTarget(c, o)) return false;

            // 相手の位置へ道をたどる(殴る・撃つの距離に入れば、そちらが選ばれる)
            Vector3 pos = c.Position;
            m_route.MoveDestination(o.Position);
            m_route.Update(pos, c.Params, c.DeltaTime, out _);
            m_route.IsOnFinalLeg(pos, out Vector3 next);
            CpuPathFollower.Walk(pos, next, c.Self.MoveSpeed, c.Params, ref cmd);
            return true;
        }

        // 攻めに行く場面か:盤面に拾える積み木がない、または積み終わった(積むが選ばれない)、または満タン近く(遊ぶ)
        private static bool ShouldApproach(CpuContext c) => c.IsPlayful || CpuBlockSearch.NothingToCollect(c);

        // 狙える相手のうち、攻めの点数が一番高い相手(歩いて行けて、道が危険な相手のそばを通らない)
        private CpuOpponent ChooseTarget(CpuContext c)
        {
            if (m_queryPath == null) m_queryPath = new NavMeshPath();
            CpuOpponent best = null;
            float bestScore = float.MinValue;
            foreach (var o in c.Perception.Opponents)
            {
                if (!IsValidTarget(c, o)) continue;
                if (!CpuNavMesh.TryFindPath(c.Position, o.Position, CpuPickupMotor.GoalTolerance, m_queryPath, out float length)) continue;
                if (!CpuBlockSearch.AvoidsThreats(c, m_queryPath, c.Params.CollectThreatClearance)) continue;
                float score = CpuAttackTarget.Score(c, o, length);
                if (o.Player == m_target) score += c.Params.StateStickiness;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }
            return best;
        }

        // 狙ってよい相手か(生存者・無敵でない・自分が有利な相手)
        // 有利でない相手には攻めに行かない(積み木の供給を待つ。首だけどうしで突っ込み合わないように)
        // ただし満タン近く(遊ぶ)なら、同じ段数の相手にも攻めに行く(満タンどうしで止まらないように)
        // 殴る距離(MeleeEnterDistance)まで近づいた相手は、攻めに行くの相手ではない
        // (攻めに行くを終えて殴る・撃つに引き継ぐ。近づいたまま押し合わないように)
        private static bool IsValidTarget(CpuContext c, CpuOpponent o)
        {
            if (o == null || o.IsGhost || o.IsInvincible || o.Character == null) return false;
            if (o.PathDistance <= c.Params.MeleeEnterDistance) return false;
            return o.StackDiff >= (c.IsPlayful ? 0 : c.Params.AdvantageMargin);
        }

        public override void DrawGizmos(CpuContext c)
        {
            CpuOpponent o = m_target != null ? c.Perception.Find(m_target) : null;
            if (o == null) return;
            Gizmos.color = new Color(1.0f, 0.3f, 0.3f);
            m_route.DrawGizmos(c.Position);
            Gizmos.DrawWireSphere(o.Position + Vector3.up, 0.6f);
        }
    }
}
