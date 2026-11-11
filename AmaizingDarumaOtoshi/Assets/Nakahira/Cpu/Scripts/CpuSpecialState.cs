using UnityEngine;

namespace Nakahira
{
    // 状態「必殺技」:ゲージが満タンで、撃つには敵に近づく必要があるキャラ(今はみだる)が、撃てる所まで近づく
    //  ・点数は SpecialStateScore(殴る・撃つより高く、逃げるより低い)
    //  ・近づく相手は、道で一番近い敵(生存者・無敵でない)
    //  ・撃つのは CpuSpecial(撃ちどころが来たら、どの状態の最中でも向き直って撃つ)。この状態は近づくだけ
    //  ・SpecialGiveUpTime 秒たっても撃てなければ終わる(すぐまた入ってよい。点数しだいで入り直す)
    //  設計メモ 6.2.1
    public class CpuSpecialState : CpuState
    {
        readonly CpuSpecial m_special;
        readonly CpuPathFollower m_route = new CpuPathFollower();
        MatchPlayer m_target;
        float m_time;
        float m_thinkTimer;

        public CpuSpecialState(CpuSpecial special)
        {
            m_special = special;
        }

        public override string Name => "必殺技";
        public MatchPlayer Target => m_target;
        public override CpuPathFollower Route => m_route;

        public override float Evaluate(CpuContext c)
        {
            if (!m_special.WantsApproach(c)) return 0.0f;
            return ChooseTarget(c) != null ? c.Params.SpecialStateScore : 0.0f;
        }

        public override void Enter(CpuContext c)
        {
            m_target = null;
            m_time = 0.0f;
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
            if (!m_special.WantsApproach(c)) return false;   // 撃った(ゲージが空になった)・積み木がなくなった
            m_time += c.DeltaTime;
            if (m_time > c.Params.SpecialGiveUpTime) return false;

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
            if (!IsValidTarget(o)) return false;

            // 撃ちどころ(射程内・間に壁なし)なら、CpuSpecial が向き直って撃つ。それまでは相手へ道をたどる
            Vector3 pos = c.Position;
            m_route.MoveDestination(o.Position);
            m_route.Update(pos, c.Params, c.DeltaTime, out _);
            m_route.IsOnFinalLeg(pos, out Vector3 next);
            CpuPathFollower.Walk(pos, next, c.Self.MoveSpeed, c.Params, ref cmd);
            return true;
        }

        // 道で一番近い敵(歩いて行けない敵は選ばない)
        private CpuOpponent ChooseTarget(CpuContext c)
        {
            CpuOpponent best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (var o in c.Perception.Opponents)
            {
                if (!IsValidTarget(o)) continue;
                float d = o.PathDistance;
                if (o.Player == m_target) d -= 1.0f;   // 今の相手を少し優先(ころころ変えない)
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = o;
                }
            }
            return best;
        }

        private static bool IsValidTarget(CpuOpponent o)
        {
            if (o == null || o.IsGhost || o.IsInvincible || o.Character == null) return false;
            return !float.IsPositiveInfinity(o.PathDistance);
        }

        public override void DrawGizmos(CpuContext c)
        {
            CpuOpponent o = m_target != null ? c.Perception.Find(m_target) : null;
            if (o == null) return;
            Gizmos.color = new Color(1.0f, 0.6f, 1.0f);
            m_route.DrawGizmos(c.Position);
            Gizmos.DrawWireSphere(o.Position + Vector3.up, 0.6f);
        }
    }
}
