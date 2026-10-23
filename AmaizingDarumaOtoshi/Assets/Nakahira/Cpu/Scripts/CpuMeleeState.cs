using UnityEngine;

namespace Nakahira
{
    // 状態「殴る」:近くの相手に近づいて薙ぎ払いで叩く
    //  入る  :無敵でない生存者が道の距離 MeleeEnterDistance 以内にいて、段数の差が −FleeStackGap より大きい
    //  中身  :間合いまで近づいて振る(振る間隔をあける)/相手の振りはジャンプでかわして着地後すぐ反撃/
    //          回り込む(間合いの中で相手を右側に置くように横へ動きながら振る/振られたら跳びながら相手の背中側へ回る)/
    //          すぐそばの安全な積み木は拾う/逃げた相手は少しだけ追う
    //  焦り  :集中度の割合が高いほど、ハンマーを連打する・届かない/当たらない時にも振る・意味もなく跳ぶ・かわすのが遅れる
    //  終わる:差が開いて不利になった・相手が離れた(MeleeChaseDistance・MeleeChaseTime)・相手が幽霊/復活待ち/無敵になった
    public class CpuMeleeState : CpuState
    {
        // 相手の動きを先読みする時間(秒)
        const float kLeadTime = 0.15f;
        // 正面からこの角度(度)以内に相手がいれば振る(ハンマーは左右 110° まで届く。横へ動きながら右の相手を叩けるように)
        const float kSwingAngle = 100.0f;
        // 相手の振り始めから、この時間(秒)以内ならかわす(それより後に気づいても間に合わない)
        const float kDodgeWindow = 0.15f;
        // 間合いの中で向きを合わせるための、ごく小さい移動入力(止まっていると向きが変わらないため)
        const float kTurnInput = 0.05f;
        // 拾う積み木と相手の間合いの間に空ける余裕(m)
        const float kPickupSafety = 0.5f;
        // 跳んでからこの時間(秒)は、接地して見えても空中として扱う(物理が進むまで接地のままに見えるため)
        const float kJustJumpedTime = 0.2f;

        // 回り込んで目指す、相手の正面からの角度(度。相手のハンマーは正面の左右 110° までしか届かない)
        const float kBackTargetAngle = 150.0f;
        // 相手の段数が減ったのが、自分が振ってからこの時間(秒)以内なら、自分の一撃で減ったとみなす
        const float kKnockAttributionTime = 0.4f;
        // 振る前に向き直すとき、相手を正面から右へこの角度(度)に置く(ハンマーは右から振るので早く当たる)
        const float kSwingBearing = 45.0f;
        // 横へ動くとき、間合いからのずれを前後に直す強さ
        const float kStrafeRadialGain = 2.0f;
        // 横へ動く先に床があるか確かめる距離(m)
        const float kStrafeLookAhead = 0.8f;

        readonly CpuPickupMotor m_pickup = new CpuPickupMotor();
        readonly CpuPathFollower m_route = new CpuPathFollower();
        MatchPlayer m_target;
        float m_nextSwingTime;
        float m_chaseTime;
        bool m_lastTargetSweeping;
        float m_targetSwingStart = -1.0f;
        // 最後に振った時刻と、相手の段数(自分の一撃で 1段 → 0段にしたかを見る)
        float m_lastSwingTime = -1.0f;
        int m_lastTargetStack = -1;
        // かわすジャンプをする時刻(しなければ負)と、跳んだ時刻
        float m_dodgeTime = -1.0f;
        float m_dodgeJumpTime = -1.0f;
        // かわしたあと、着地したらすぐ反撃する
        bool m_counterReady;
        // 次の1回を、横へ動きながら振るか(振るたびに StrafeChance で決め直す)
        bool m_circling;
        // 次の1回を、焦って雑に振るか(振るたびに焦りの強さで決め直す)
        // 雑に振る番は、相手が跳んでいても・自分が跳んでいても・届かなくても・向きが合わなくても振ってしまう
        bool m_sloppy;
        // かわすジャンプの最中に、相手の背中側へ回り込むか
        bool m_dodgeCircle;

        public override string Name => "殴る";
        public MatchPlayer Target => m_target;

        public override float Evaluate(CpuContext c)
        {
            // 殴っている最中は、続けられる間は続けたい
            if (m_target != null && CanContinue(c, c.Perception.Find(m_target))) return c.Params.MeleeScore;
            return ChooseTarget(c) != null ? c.Params.MeleeScore : 0.0f;
        }

        public override float Threshold(CpuContext c) => c.Params.MeleeThreshold;

        public override void Enter(CpuContext c)
        {
            CpuOpponent o = ChooseTarget(c);
            m_target = o != null ? o.Player : null;
            m_chaseTime = 0.0f;
            m_dodgeTime = -1.0f;
            m_counterReady = false;
            m_lastTargetSweeping = false;
            m_lastTargetStack = o != null ? o.StackCount : -1;
            m_dodgeCircle = false;
            RollCircling(c);
            m_pickup.Clear();
            m_route.Clear();
        }

        public override void Exit(CpuContext c)
        {
            m_target = null;
            m_pickup.Clear();
            m_route.Clear();
        }

        public override bool Tick(CpuContext c, ref DarumaCommand cmd)
        {
            CpuOpponent o = m_target != null ? c.Perception.Find(m_target) : null;
            if (!CanContinue(c, o)) return false;

            // 自分が相手を 1段 → 0段(首だけ)にした瞬間:あと一撃に夢中になる(集中度を足す)
            if (m_lastTargetStack >= 1 && o.StackCount == 0 && Time.time - m_lastSwingTime < kKnockAttributionTime)
            {
                c.AddLoad(c.Params.AttentionOnKnockToZero);
            }
            m_lastTargetStack = o.StackCount;

            CpuParams p = c.Params;
            DarumaCharacter self = c.Self;
            float reach = self.Hammer.CharacterHitDistance;
            float range = reach * p.MeleeRangeRatio;
            float dist = o.FlatDistance;

            // 追っている時間(間合いの外にいる時間)
            m_chaseTime = dist > reach ? m_chaseTime + c.DeltaTime : 0.0f;

            UpdateDodge(c, o, ref cmd);

            // 焦り:意味もなく跳んでしまう(疲れは足さない)
            float panic = Panic(c);
            if (!cmd.Jump && self.IsGrounded && Time.time - m_dodgeJumpTime >= kJustJumpedTime
                && Random.value < panic * p.PanicJumpRate * c.DeltaTime)
            {
                cmd.Jump = true;
                m_dodgeJumpTime = Time.time;
            }

            // 跳んだ直後は、物理が進むまで接地のままに見えるので、少しの間は空中として扱う
            bool justJumped = Time.time - m_dodgeJumpTime < kJustJumpedTime;
            if (cmd.Jump || !self.IsGrounded || justJumped)
            {
                // 雑に振る番なら、空中でも振ってしまう(空中で振っても当たらない)
                if (m_sloppy) TrySwing(c, o, reach, false, ref cmd);
                bool jump = cmd.Jump;
                bool sweep = cmd.Sweep;
                // かわしている最中:相手の背中側へ回り込む/すぐそばの安全な積み木の上に降りる/相手の方を向く
                if (m_dodgeCircle && MoveToBack(c, o, range, ref cmd)) { }
                else if (TryPickupNearby(c, o, ref cmd)) { }
                else Face(c, o.Position, ref cmd);
                cmd.Jump |= jump;
                cmd.Sweep |= sweep;
                return true;
            }
            m_dodgeCircle = false;

            // 着地した:かわしたあとなら、空振りした相手にすぐ反撃する
            bool counter = m_counterReady;
            m_counterReady = false;

            if (TrySwing(c, o, reach, counter, ref cmd)) return true;
            // 反撃したいのに向きが合っていなければ、次のフレームで振れるよう向き直す
            if (counter && dist <= reach)
            {
                m_counterReady = true;
                Face(c, o.Position, ref cmd);
                return true;
            }

            // 振れる時なのに向きが合わず振れなかった:相手を右前に置く向きへ向き直す(右の相手にはすぐ当たる)
            if (Time.time >= m_nextSwingTime && !self.Hammer.IsBusy && dist <= reach)
            {
                Face(c, c.Position + Quaternion.AngleAxis(-kSwingBearing, Vector3.up) * (o.Position - c.Position), ref cmd);
                return true;
            }

            // 相手が振っていなければ、すぐそばの安全な積み木を拾う
            if (!o.IsSweeping && TryPickupNearby(c, o, ref cmd)) return true;

            // 回り込む:間合いの中で、相手を自分の右側に置くように横へ動き続ける(振ればすぐ当たる。相手は向き直り続ける)
            if (m_circling && dist <= reach && Strafe(c, o, range, ref cmd)) return true;

            // 間合いまで近づく(相手の動きを先読みする)。間合いの中では向きだけ合わせる
            Vector3 predicted = o.Position + o.Velocity * kLeadTime;
            if (dist > range) ApproachTo(c, predicted, ref cmd);
            else Face(c, predicted, ref cmd);
            return true;
        }

        //========================================
        // 回り込む
        //========================================

        // 次の1回を、横へ動きながら振るか・焦って雑に振るか決める
        private void RollCircling(CpuContext c)
        {
            m_circling = Random.value < c.Params.StrafeChance;
            m_sloppy = Random.value < Panic(c);
        }

        // 焦り(0〜1)。集中度の割合が PanicStart を超えると上がり始め、割合 1 で最大
        private static float Panic(CpuContext c)
        {
            float start = c.Params.PanicStart;
            return Mathf.Clamp01((c.LoadRatio - start) / Mathf.Max(0.01f, 1.0f - start));
        }

        // 相手の正面から見た自分の角度(度。相手の右が +、左が −。相手のハンマーは右から左へ振る)
        private static float BearingFromOpponent(CpuContext c, CpuOpponent o, out Vector3 forward)
        {
            forward = o.Character.transform.forward;
            forward.y = 0.0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 rel = c.Position - o.Position;
            rel.y = 0.0f;
            return Vector3.SignedAngle(forward, rel, Vector3.up);
        }

        // 回る向き:今いる側から背中へ回る。正面付近なら相手の左側(ハンマーが最後に来る側)から回る
        private static float CircleSide(float bearing)
        {
            return bearing > 5.0f ? 1.0f : -1.0f;
        }

        // 間合い(range)を保ちながら、相手を自分の右側に置くように横へ動く(進む向きの右に相手がいる)
        // 自分のハンマーは右から左へ振るので、右にいる相手にはすぐ当たる(相手がかわす間がない)
        // 横に床がなければ反対向きに動く。どちらにも動けなければ false
        private bool Strafe(CpuContext c, CpuOpponent o, float range, ref DarumaCommand cmd)
        {
            Vector3 toOpponent = o.Position - c.Position;
            toOpponent.y = 0.0f;
            float dist = toOpponent.magnitude;
            if (dist < 0.01f) return false;
            toOpponent /= dist;

            // 進む向き(相手が右に来る向き)と、間合いを保つための前後の補正
            Vector3 tangent = Vector3.Cross(toOpponent, Vector3.up);
            Vector3 radial = toOpponent * Mathf.Clamp((dist - range) * kStrafeRadialGain, -1.0f, 1.0f);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Vector3 dir = (tangent + radial).normalized;
                if (CpuSensor.HasGround(c.Position + dir * kStrafeLookAhead, c.Params.GroundProbeDepth))
                {
                    cmd.Move = new Vector2(dir.x, dir.z);
                    return true;
                }
                // こちら側は床がない(奈落ぎわ):反対向きに回る
                tangent = -tangent;
            }
            return false;
        }

        // 相手の背中側(自分の間合いの距離)へまっすぐ向かう(かわすジャンプの最中に使う)
        private bool MoveToBack(CpuContext c, CpuOpponent o, float range, ref DarumaCommand cmd)
        {
            float bearing = BearingFromOpponent(c, o, out Vector3 forward);
            float side = CircleSide(bearing);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Vector3 point = o.Position + Quaternion.AngleAxis(side * kBackTargetAngle, Vector3.up) * forward * range;
                if (CpuSensor.HasGround(point, c.Params.GroundProbeDepth))
                {
                    Vector3 to = point - c.Position;
                    to.y = 0.0f;
                    if (to.sqrMagnitude > 0.0001f)
                    {
                        to.Normalize();
                        cmd.Move = new Vector2(to.x, to.z);
                    }
                    return true;
                }
                side = -side;
            }
            return false;
        }

        //========================================
        // 相手を選ぶ・続ける
        //========================================

        // 殴り合いを始められる相手のうち、攻めの点数が一番高い相手(いなければ null)
        private CpuOpponent ChooseTarget(CpuContext c)
        {
            CpuOpponent best = null;
            float bestScore = float.MinValue;
            foreach (var o in c.Perception.Opponents)
            {
                if (o.IsGhost || o.IsInvincible) continue;
                if (o.PathDistance > c.Params.MeleeEnterDistance) continue;
                if (o.StackDiff <= -c.Params.FleeStackGap) continue;
                float score = CpuAttackTarget.Score(c, o, o.PathDistance);
                if (o.Player == m_target) score += c.Params.StateStickiness;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }
            return best;
        }

        private bool CanContinue(CpuContext c, CpuOpponent o)
        {
            if (o == null || o.IsGhost || o.IsInvincible || o.Character == null) return false;
            CpuParams p = c.Params;
            if (o.StackDiff <= -p.FleeStackGap) return false;                 // 不利になった(逃げる)
            if (o.PathDistance > p.MeleeChaseDistance) return false;          // 離れた
            return m_chaseTime <= p.MeleeChaseTime;                           // 追っても間合いに入れない
        }

        //========================================
        // 振る・かわす
        //========================================

        private bool TrySwing(CpuContext c, CpuOpponent o, float reach, bool counter, ref DarumaCommand cmd)
        {
            CpuParams p = c.Params;
            if (c.Self.Hammer.IsBusy) return false;
            if (!counter && Time.time < m_nextSwingTime) return false;
            // 落ち着いていれば、相手が跳んでいる間(当たらない)は振らない。焦って雑に振る番なら振ってしまう
            if (o.IsAirborne && !m_sloppy) return false;

            // 相手の少し先の位置が、間合いの中・正面にあるか(雑に振る番は、届かなくても・向きが合わなくても振る)
            Vector3 predicted = o.Position + o.Velocity * kLeadTime;
            Vector3 to = predicted - c.Position;
            to.y = 0.0f;
            float panic = Panic(c);
            float reachLimit = reach - 0.1f + (m_sloppy ? panic * p.PanicReachSlack : 0.0f);
            if (to.magnitude > reachLimit) return false;
            Vector3 forward = c.Self.transform.forward;
            forward.y = 0.0f;
            if (!m_sloppy && Vector3.Angle(forward, to) > kSwingAngle) return false;

            cmd.Sweep = true;
            m_lastSwingTime = Time.time;
            Face(c, predicted, ref cmd);
            // 振る間隔:焦っているほど短く(連打)
            float calm = Random.Range(p.SwingIntervalMin, p.SwingIntervalMax);
            float mash = Random.Range(0.0f, p.PanicSwingInterval);
            m_nextSwingTime = Time.time + c.Self.Hammer.SweepDuration + Mathf.Lerp(calm, mash, panic);
            // 次の1回を回り込むか・雑に振るか決め直す
            RollCircling(c);
            return true;
        }

        // 相手が振り始め、自分が相手の間合いにいたら、反応の遅れのあとにジャンプでかわす
        private void UpdateDodge(CpuContext c, CpuOpponent o, ref DarumaCommand cmd)
        {
            bool startedSwing = o.IsSweeping && !m_lastTargetSweeping;
            m_lastTargetSweeping = o.IsSweeping;
            if (startedSwing)
            {
                m_targetSwingStart = Time.time;
                bool inReach = o.FlatDistance <= o.SweepHitDistance;
                // 疲れているほど(集中度の割合が高いほど)気づけない
                if (inReach && c.Self.IsGrounded && Random.value < c.Params.DodgeChance * c.Alertness)
                {
                    // 焦っているほど、跳ぶのが遅れる(間に合わないこともある)
                    float jitter = Random.Range(0.0f, c.Params.PanicDodgeJitter) * Panic(c);
                    m_dodgeTime = Time.time + c.Params.DodgeReaction + jitter;
                }
            }

            if (m_dodgeTime < 0.0f || Time.time < m_dodgeTime) return;
            // 相手が振り終わってからでは跳ばない(焦って遅れたジャンプは、かわせなくても跳ぶ)
            if (Time.time - m_targetSwingStart > kDodgeWindow + c.Params.DodgeReaction + c.Params.PanicDodgeJitter || !c.Self.IsGrounded)
            {
                m_dodgeTime = -1.0f;
                return;
            }
            cmd.Jump = true;
            m_dodgeTime = -1.0f;
            m_dodgeJumpTime = Time.time;
            m_counterReady = true;
            c.AddLoad(c.Params.AttentionOnDodge);
            // 跳びながら相手の背中側へ回り込むか
            m_dodgeCircle = Random.value < c.Params.CircleChance;
        }

        //========================================
        // 動き
        //========================================

        // すぐそば(MeleePickupRadius)で、相手の間合いの外にある積み木を拾う
        private bool TryPickupNearby(CpuContext c, CpuOpponent o, ref DarumaCommand cmd)
        {
            if (c.StackCount >= c.Params.CollectUntil) return false;
            if (m_pickup.Target != null && !IsSafeNearby(c, o, m_pickup.Target)) m_pickup.Clear();
            if (m_pickup.Target == null)
            {
                TumikiBlock best = null;
                float bestDist = c.Params.MeleePickupRadius;
                foreach (var block in TumikiBlock.All)
                {
                    if (!IsSafeNearby(c, o, block)) continue;
                    float d = CpuContext.FlatDistance(c.Position, block.transform.position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = block;
                    }
                }
                if (best == null) return false;
                m_pickup.SetTarget(best, c.Params);
            }
            bool jump = cmd.Jump;
            bool running = m_pickup.Tick(c, ref cmd) == CpuPickupMotor.Status.Running;
            cmd.Jump |= jump;   // かわすジャンプは消さない
            if (!running) m_pickup.Clear();
            return running;
        }

        private bool IsSafeNearby(CpuContext c, CpuOpponent o, TumikiBlock block)
        {
            if (!CpuBlockSearch.IsCollectable(block) || c.IsGivenUp(block)) return false;
            Vector3 bp = block.transform.position;
            if (CpuContext.FlatDistance(c.Position, bp) > c.Params.MeleePickupRadius) return false;
            if (!CpuBlockSearch.CanReachHeight(c.Self, bp)) return false;
            if (!CpuSensor.HasGround(bp, c.Params.GroundProbeDepth)) return false;
            return CpuContext.FlatDistance(o.Position, bp) > o.SweepHitDistance + kPickupSafety;
        }

        private void ApproachTo(CpuContext c, Vector3 destination, ref DarumaCommand cmd)
        {
            Vector3 pos = c.Position;
            m_route.MoveDestination(destination);
            m_route.Update(pos, c.Params, c.DeltaTime, out _);
            m_route.IsOnFinalLeg(pos, out Vector3 next);
            CpuPathFollower.Walk(pos, next, c.Self.MoveSpeed, c.Params, ref cmd);
        }

        // 相手の方を向く(止まっていると向きが変わらないので、ごく小さく動く)
        private static void Face(CpuContext c, Vector3 target, ref DarumaCommand cmd)
        {
            Vector3 to = target - c.Position;
            to.y = 0.0f;
            if (to.sqrMagnitude < 0.0001f) return;
            to.Normalize();
            cmd.Move = new Vector2(to.x, to.z) * kTurnInput;
        }

        public override void DrawGizmos(CpuContext c)
        {
            CpuOpponent o = m_target != null ? c.Perception.Find(m_target) : null;
            if (o == null) return;
            Gizmos.color = new Color(1.0f, 0.5f, 0.0f);
            Gizmos.DrawLine(c.Position + Vector3.up, o.Position + Vector3.up);
            Gizmos.DrawWireSphere(c.Position, c.Self.Hammer.CharacterHitDistance);
            if (m_pickup.Target != null) m_pickup.DrawGizmos(c.Self);
        }
    }
}
