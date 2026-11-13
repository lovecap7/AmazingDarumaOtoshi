using UnityEngine;

namespace Nakahira
{
    // 状態「必殺技」:ゲージが満タンで、撃つには敵に近づく必要があるキャラ(みだる・いわだる・へびだる・だるきんぐ)が、撃てる所まで近づく
    //  (だるきんぐ:敵 2 人が一直線に並ぶ位置へ回り込む。5 秒で決まらなければ 1 人狙い。2026-11-13)
    //  ・点数は SpecialStateScore(殴る・撃つより高く、逃げるより低い)
    //  ・近づく相手は CpuSpecial.ApproachTarget(みだる:道で一番近い敵・いわだる:一番積み木を積んでいる敵・
    //    へびだる:一番積み木が少ない敵。生存者・無敵でない)
    //  ・撃つのは CpuSpecial(撃ちどころが来たら、どの状態の最中でも撃つ)。この状態は近づくだけ
    //  ・SpecialGiveUpTime 秒たっても撃てなければ終わる(すぐまた入ってよい。点数しだいで入り直す)
    //  ・撃ったあとは敵についていく(点数 SpecialFollowScore)
    //    いわだる:カウントダウン(3 秒)の間、狙った敵に重なるほど近くへ
    //    へびだる:毒沼の間、反転している敵を追う。殴る間合いに入ったら殴るに引き継ぐ
    //  設計メモ 6.2.1
    public class CpuSpecialState : CpuState
    {
        readonly CpuSpecial m_special;
        readonly CpuPathFollower m_route = new CpuPathFollower();
        MatchPlayer m_target;
        float m_time;
        float m_thinkTimer;

        readonly CpuMeleeState m_melee;

        public CpuSpecialState(CpuSpecial special, CpuMeleeState melee)
        {
            m_special = special;
            m_melee = melee;
        }

        public override string Name => "必殺技";
        public MatchPlayer Target => m_target;
        public override CpuPathFollower Route => m_route;

        public override float Evaluate(CpuContext c)
        {
            if (m_special.IsFollowing)
            {
                // へびだるの毒沼の間:殴っている最中・敵が殴る間合いに入ったら、殴るに引き継ぐ
                // (殴るより低く、積むより高い点数。殴れない相手なら、この状態のまま追い続ける)
                if (m_special.IsSwampFollowing)
                {
                    CpuOpponent f = m_special.FollowTarget(c, m_target);
                    if (f == null) return 0.0f;
                    if (m_melee.Target != null || f.PathDistance <= c.Params.MeleeEnterDistance)
                        return c.Params.MeleeScore - c.Params.StateStickiness - 0.01f;
                }
                // 撃ったあと、敵についていく(いわだるのカウントダウン中・へびだるの毒沼の間)
                return c.Params.SpecialFollowScore;
            }
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
            bool following = m_special.IsFollowing;
            if (following)
            {
                // ついていく(全速で重なるほど近くへ)
                //  ・いわだる:撃ったときの敵(カウントダウンが終わった位置が円の中心になる)
                //  ・へびだる:反転している敵(いなければ一番段数が少ない敵)。考え直すのは ThinkInterval ごと
                m_thinkTimer -= c.DeltaTime;
                if (m_target == null || m_thinkTimer <= 0.0f || !IsValidTarget(c.Perception.Find(m_target)))
                {
                    m_thinkTimer = c.Params.ThinkInterval;
                    CpuOpponent f = m_special.FollowTarget(c, m_target);
                    if (f == null) return false;
                    if (f.Player != m_target)
                    {
                        m_target = f.Player;
                        m_route.SetDestination(f.Position, CpuPickupMotor.GoalTolerance);
                    }
                }
            }
            else
            {
                if (!m_special.WantsApproach(c)) return false;   // 撃った(ゲージが空になった)・積み木がなくなった
                m_special.TickApproach(c);   // だるきんぐ:2 人狙いを探した時間を数える(5 秒で 1 人狙い)
                m_time += c.DeltaTime;
                if (m_time > c.Params.SpecialGiveUpTime) return false;
                m_thinkTimer -= c.DeltaTime;
            }

            if (!following && (m_target == null || m_thinkTimer <= 0.0f || !IsValidTarget(c.Perception.Find(m_target))))
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
            // だるきんぐは敵 2 人が一直線に並ぶ位置へ回り込む(ほかのキャラは敵の所)
            m_route.MoveDestination(following ? o.Position : m_special.ApproachPoint(c, o));
            m_route.Update(pos, c.Params, c.DeltaTime, out _);
            m_route.IsOnFinalLeg(pos, out Vector3 next);
            CpuPathFollower.Walk(pos, next, c.Self.MoveSpeed, c.Params, ref cmd);
            return true;
        }

        // 近づく敵(キャラごとに CpuSpecial が決める。みだる:道で一番近い敵・いわだる:一番積み木を積んでいる敵)
        private CpuOpponent ChooseTarget(CpuContext c) => m_special.ApproachTarget(c, m_target);

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
