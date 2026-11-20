using UnityEngine;

namespace Nakahira
{
    // 背中を見せた相手を叩く(2026-11-20。設計メモ 12)
    // どの状態の最中でも(積む・必殺技を撃ちに行く・麻雀の牌を取りに行く・攻めに行く・逃げる…)、
    // ハンマーが届く所に背を向けた(相手のハンマーが届かない角度に自分がいる)相手がいれば振る。
    // 向きも動きも変えない(今の動きのまま振るだけ)。殴るの最中は、殴るが自分で振るので除く
    //  ・自分も相手も地面にいるとき(地上で振ると空中の相手には当たらない)
    //  ・幽霊・無敵中の相手は叩かない。味方は相手に入らない(CpuPerception)
    //  ・振れるか(ハンマーが空いている・股抜きショットを押していない)
    public class CpuBackSwing
    {
        // 相手の動きを先読みする時間(秒。殴ると同じ)
        const float kLeadTime = 0.15f;
        // 届く距離から引く余裕(m。殴ると同じ)
        const float kReachSlack = 0.1f;

        float m_nextRollTime;
        // 前のフレームで振ると決めたか(本当に振り始めたかを次のフレームで数える)
        bool m_pending;

        // 本当に振り始めた回数と、最後に振り始めた時刻(テスト用)
        public int SwingCount { get; private set; }
        public float LastSwingTime { get; private set; } = -1.0f;

        public void Apply(CpuContext c, ref DarumaCommand cmd)
        {
            CpuParams p = c.Params;
            DarumaCharacter self = c.Self;
            if (m_pending && self.Hammer.IsSweeping)
            {
                SwingCount++;
                LastSwingTime = Time.time;
            }
            m_pending = false;
            if (p.BackSwingChance <= 0.0f || cmd.Sweep || cmd.Shot) return;
            if (self.Hammer.IsBusy || !self.IsGrounded) return;

            CpuOpponent target = FindTarget(c);
            if (target == null) return;

            // 振るかどうかは、一度外れたら少し待ってから決め直す(毎フレーム引き直すと、確率が意味をなさないため)
            if (Time.time < m_nextRollTime) return;
            if (Random.value >= p.BackSwingChance)
            {
                m_nextRollTime = Time.time + p.BackSwingRetryInterval;
                return;
            }
            cmd.Sweep = true;
            m_pending = true;
        }

        // ハンマーが届く所(正面から左右 kSwingAngle 以内)にいて、背を向けている相手(一番近い相手)
        private static CpuOpponent FindTarget(CpuContext c)
        {
            float reach = c.Self.Hammer.CharacterHitDistance - kReachSlack;
            Vector3 forward = c.Self.transform.forward;
            forward.y = 0.0f;
            CpuOpponent best = null;
            float bestDistance = float.MaxValue;
            foreach (var o in c.Perception.Opponents)
            {
                if (o.IsGhost || o.IsInvincible || o.Character == null || o.IsAirborne) continue;
                Vector3 to = o.Position + o.Velocity * kLeadTime - c.Position;
                to.y = 0.0f;
                float d = to.magnitude;
                if (d > reach || d >= bestDistance) continue;
                if (Vector3.Angle(forward, to) > CpuMeleeState.kSwingAngle) continue;
                if (!CpuMeleeState.IsBehind(c, o)) continue;
                best = o;
                bestDistance = d;
            }
            return best;
        }
    }
}
