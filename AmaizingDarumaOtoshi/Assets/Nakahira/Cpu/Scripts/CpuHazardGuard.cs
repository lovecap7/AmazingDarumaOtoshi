using UnityEngine;

namespace Nakahira
{
    // 危ない場所(隕石の予告円・ビーム・エクスカリバーの予定の線)から出る・入らない。状態の操作のあとに割り込む(1人の CPU につき1つ)
    //  ・中にいる:外へ出られる一番近い向きへ走る(途中に床がない向きは選ばない)
    //  ・外にいる:進む先が危ない場所なら、中へ入る向きの分を消して縁に沿って動く(沿えなければ止まる)
    // 気づいている危ない場所だけ(CpuHazards。出てから HazardReaction 秒たったもの)
    // 毒沼で移動が反転していても直さない(人と同じく、出ようとして中心へ走ってしまう。2026-11-09 決定)
    public class CpuHazardGuard
    {
        // 出る向きの候補の数
        const int kDirections = 16;
        // 出る向きを調べる刻み(m)と、調べる最大の距離(m)
        const float kMarchStep = 0.5f;
        const float kMarchMax = 16.0f;
        // 出る向きを選び直す間隔(秒)
        const float kRethinkInterval = 0.1f;
        // 今の向きに近い(45°以内)向きは、この距離(m)だけ短いとみなす(向きがふらつかないように)
        const float kKeepBonus = 1.0f;
        // 出たとみなすのは、離れておく距離よりさらにこの距離(m)外へ出てから(出入りを繰り返さないように)
        const float kExitMargin = 0.3f;
        // 進む先を調べる距離(m)。移動速度 × 0.1 秒ぶんを足して使う
        const float kProbeDistance = 0.6f;
        // 縁に沿う向きの大きさがこれより小さければ止まる
        const float kMinSlide = 0.2f;

        bool m_escaping;
        Vector3 m_escapeDir;
        float m_rethinkTimer;

        public enum Action { None, Escape, Slide }
        // 今の動き(デバッグ表示用)
        public Action Current { get; private set; }

        public void Reset()
        {
            m_escaping = false;
            m_rethinkTimer = 0.0f;
            Current = Action.None;
        }

        // riding:移動床・ワープゲートの乗り降り中(縁に沿う動きはせず、中から出るのも隕石だけにする)
        public void Apply(CpuContext c, bool riding, ref DarumaCommand cmd)
        {
            Current = Action.None;
            CpuHazards hazards = c.Hazards;
            if (!hazards.Any)
            {
                m_escaping = false;
                return;
            }

            Vector3 pos = c.Position;
            bool inside = hazards.TryGetOutward(pos, m_escaping ? kExitMargin : 0.0f, out Vector3 outward, out CpuHazardKind kind);
            if (inside && (!riding || CpuHazards.IsLethal(kind)))
            {
                if (!m_escaping) m_rethinkTimer = 0.0f;
                m_escaping = true;
                m_rethinkTimer -= c.DeltaTime;
                if (m_rethinkTimer <= 0.0f)
                {
                    m_rethinkTimer = kRethinkInterval;
                    m_escapeDir = ChooseEscape(c, outward);
                }
                if (m_escapeDir.sqrMagnitude > 0.0f)
                {
                    cmd.Move = new Vector2(m_escapeDir.x, m_escapeDir.z);
                    Current = Action.Escape;
                }
                return;
            }
            m_escaping = false;
            if (riding) return;

            // 外にいる:進む先が危ない場所なら縁に沿う
            Vector3 move = new Vector3(cmd.Move.x, 0.0f, cmd.Move.y);
            float amount = move.magnitude;
            if (amount < 0.1f) return;
            Vector3 dir = move / amount;
            float probe = kProbeDistance + c.Self.MoveSpeed * 0.1f;
            if (!hazards.TryGetOutward(pos + dir * probe, 0.0f, out Vector3 n, out _)) return;

            float into = Vector3.Dot(dir, n);
            Vector3 slide = into < 0.0f ? dir - n * into : dir;
            if (slide.magnitude >= kMinSlide)
            {
                slide.Normalize();
                if (!hazards.IsInside(pos + slide * probe))
                {
                    cmd.Move = new Vector2(slide.x, slide.z) * amount;
                    Current = Action.Slide;
                    return;
                }
            }
            cmd.Move = Vector2.zero;
            Current = Action.Slide;
        }

        // 外へ出られる一番近い向き。途中(1m 先まで含む)に床がない向きは選ばない
        // どの向きも床が続かなければ、今いる危ない場所の外側の向き(床があれば)
        private Vector3 ChooseEscape(CpuContext c, Vector3 fallback)
        {
            Vector3 pos = c.Position;
            CpuHazards hazards = c.Hazards;
            float depth = c.Params.GroundProbeDepth;
            float bestCost = float.MaxValue;
            Vector3 best = Vector3.zero;
            for (int k = 0; k < kDirections; k++)
            {
                float angle = k * Mathf.PI * 2.0f / kDirections;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
                float cost = m_escapeDir.sqrMagnitude > 0.0f && Vector3.Dot(dir, m_escapeDir) > 0.7f ? -kKeepBonus : 0.0f;
                if (cost + kMarchStep >= bestCost) continue;

                float exit = -1.0f;
                for (float t = kMarchStep; t <= kMarchMax; t += kMarchStep)
                {
                    Vector3 p = pos + dir * t;
                    if (!CpuSensor.HasGround(p, depth)) break;
                    if (CpuNavMesh.IsNearHazard(p, DarumaCharacter.kBodyRadius)) break;
                    if (hazards.IsInside(p, kExitMargin)) continue;
                    // 出た先の 1m 先にも床がある(縁ぎりぎりで止まって落ちないように)
                    if (!CpuSensor.HasGround(p + dir * 1.0f, depth)) break;
                    exit = t;
                    break;
                }
                if (exit < 0.0f) continue;
                cost += exit;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = dir;
                }
            }
            if (best.sqrMagnitude > 0.0f) return best;
            return CpuSensor.HasGround(pos + fallback * kProbeDistance, depth) ? fallback : Vector3.zero;
        }
    }
}
