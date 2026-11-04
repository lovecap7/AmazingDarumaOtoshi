using System.Collections.Generic;
using Nakagawa;
using UnityEngine;

namespace Nakahira
{
    // 幽霊になった CPU の頭。生存者にハンマーでダメージを与えて、早く復活することだけを考える
    // 幽霊は浮いているので奈落・床のギミックを無視してまっすぐ進める(ステージの範囲内。壁(ReflectWall)には沿って滑る)
    //  ・攻撃:振ればハンマーが当たる相手がいれば、何よりも先に振る(毎フレーム確かめる)
    //  ・近づく:一番早く追いつけそうな相手へ、動きを先読みして回り込む。ワープゲートが近道なら入る
    //  ・弾いて当てる:落ちている積み木を弾いて相手に当てる方が早そうなら、積み木の向こう側へ回って弾く
    //    (幽霊が弾いた積み木が生存者に当たっても復活できる)
    // 詳しい考え方は Assets/Nakahira/Cpu/CPU設計メモ.md の「幽霊」
    public class CpuGhost
    {
        public enum Action { None, Approach, Warp, Bat }

        // ハンマーが角度 b(度。正面から右が +)を通るまでの時間の計算で、振り始めの 1 ステップ分の遅れ(秒)
        const float kSwingDelay = 0.02f;
        // 相手の高さがこれ以上ずれていたら(ジャンプ中など)当たらないとみなす(m。ハンマーの当たる高さの幅より少し小さく)
        const float kHitHeight = 0.5f;
        // 積み木を弾く位置:積み木から、弾く向きと反対へこの距離(m)
        const float kBatStandDistance = 1.3f;
        // 弾く位置からこの距離(m)以内なら、弾く向きへ入力 kBatCreepInput でゆっくり進んで向きをそろえる
        const float kBatCreepDistance = 0.5f;
        const float kBatCreepInput = 0.2f;
        // 弾いた積み木の速さが、この速さ(m/秒)を下回ると当たらなくなる(TumikiParams.HarmlessSpeed)
        const float kHarmlessSpeed = 10.0f;
        // 弾いた積み木の減速(m/秒²。TumikiParams.Deceleration)
        const float kBlockDeceleration = 5.0f;
        // 積み木の通り道に床があるか確かめる間隔(m)と、積み木の高さから下に探す深さ(m)
        const float kGroundCheckStep = 1.0f;
        const float kGroundCheckDepth = 1.5f;
        // 着いたとみなす距離(m)
        const float kArriveDistance = 0.25f;
        // 引っかかりの判定:この時間(秒)のあいだ、思った距離の 3 割も進めなければ横へよける
        const float kStuckCheckTime = 0.3f;
        const float kDetourTime = 0.7f;
        // ゲートをよけ始める、ゲートの半径に足す距離(m)
        const float kGateAvoidDistance = 0.8f;
        // ワープしたとみなす、1 フレームでの移動距離(m)
        const float kTeleportJump = 2.0f;
        // ゲートを調べ直す間隔(秒)
        const float kGateRefreshInterval = 2.0f;
        // あきらめた積み木を使わない時間(秒)
        const float kBatBlacklistTime = 3.0f;
        // ワープで出てきてから、次にワープを考えるまでの時間(秒。出口のゲートは離れるまで反応しないので)
        const float kWarpRearmTime = 1.0f;
        // ゲートの中にこの時間(秒)いても送られなければ(出口がふさがっているなど)、あきらめる
        const float kWarpGiveUpTime = 1.5f;

        DarumaCharacter m_target;
        TumikiBlock m_batBlock;
        WarpGateGimmick m_warpGate;
        float m_thinkTimer;
        float m_batTime;
        float m_noWarpUntil;
        float m_warpInsideTime;
        Vector3 m_lastPos;
        bool m_hasLastPos;
        Vector3 m_lastMoveDir;
        Vector3 m_velocity;
        bool m_batCreep;
        // 振ると決めてから振るまでの残り(反応の遅れ)と、次の 1 回が空振りの回か
        bool m_swingPending;
        float m_swingDelay;
        bool m_earlySwing;

        // 引っかかり
        float m_stuckTimer;
        float m_stuckMoved;
        float m_stuckExpected;
        float m_detourTimer;
        Vector3 m_detourDir;
        int m_detourSide = 1;

        readonly List<WarpGateGimmick> m_gates = new List<WarpGateGimmick>();
        float m_gateRefreshTimer;
        readonly List<DarumaCharacter> m_survivors = new List<DarumaCharacter>();
        readonly Dictionary<TumikiBlock, float> m_blacklist = new Dictionary<TumikiBlock, float>();
        readonly List<TumikiBlock> m_expired = new List<TumikiBlock>();

        public Action Current { get; private set; }
        public DarumaCharacter Target => m_target;
        public TumikiBlock BatBlock => m_batBlock;

        // 幽霊になった・復活した:最初から考え直す
        public void Reset()
        {
            Current = Action.None;
            m_target = null;
            m_batBlock = null;
            m_warpGate = null;
            m_thinkTimer = 0.0f;
            m_hasLastPos = false;
            m_detourTimer = 0.0f;
            m_stuckTimer = 0.0f;
            m_stuckMoved = 0.0f;
            m_stuckExpected = 0.0f;
            m_gateRefreshTimer = 0.0f;
            m_noWarpUntil = 0.0f;
            m_warpInsideTime = 0.0f;
            m_swingPending = false;
            m_earlySwing = false;
            m_blacklist.Clear();
        }

        public DarumaCommand Tick(DarumaGhost ghost, MatchPlayer self, CpuParams p, float dt)
        {
            var cmd = new DarumaCommand();
            CollectSurvivors(self);
            RefreshGates(dt);
            ForgetBlacklist();

            Vector3 pos = ghost.transform.position;
            // ワープで送られた:入ろうとしていたゲートは終わり
            if (m_hasLastPos && CpuContext.FlatDistance(pos, m_lastPos) > kTeleportJump)
            {
                m_warpGate = null;
                m_noWarpUntil = Time.time + kWarpRearmTime;
                m_thinkTimer = 0.0f;
            }
            // ゲートの中にいるのに送られない:あきらめて、しばらくワープを使わない
            if (Current == Action.Warp && m_warpGate != null
                && CpuContext.FlatDistance(pos, m_warpGate.transform.position) <= m_warpGate.WarpRadius)
            {
                m_warpInsideTime += dt;
                if (m_warpInsideTime > kWarpGiveUpTime)
                {
                    m_warpGate = null;
                    m_noWarpUntil = Time.time + kWarpRearmTime;
                    m_thinkTimer = 0.0f;
                }
            }
            else m_warpInsideTime = 0.0f;
            UpdateStuck(ghost, pos, dt);
            // 自分の今の動き(振っている間も同じように動き続けるとみなす。ワープした直後は 0)
            m_velocity = m_hasLastPos && dt > 0.0001f && CpuContext.FlatDistance(pos, m_lastPos) <= kTeleportJump ? (pos - m_lastPos) / dt : Vector3.zero;
            m_velocity.y = 0.0f;
            m_lastPos = pos;
            m_hasLastPos = true;

            if (m_survivors.Count == 0)
            {
                Current = Action.None;
                return cmd;
            }

            m_thinkTimer -= dt;
            if (m_thinkTimer <= 0.0f || !IsValidTarget(m_target)
                || (Current == Action.Bat && !IsUsableBlock(m_batBlock))
                || (Current == Action.Warp && (m_warpGate == null || !m_warpGate.isActiveAndEnabled)))
            {
                m_thinkTimer = p.ThinkInterval;
                Decide(ghost, p);
            }

            Vector3 moveTo = pos;
            bool exact = false;
            m_batCreep = false;
            switch (Current)
            {
                case Action.Bat:
                    m_batTime += dt;
                    if (m_batTime > p.GhostBatGiveUpTime)
                    {
                        m_blacklist[m_batBlock] = Time.time + kBatBlacklistTime;
                        m_thinkTimer = 0.0f;
                    }
                    if (TickBat(ghost, p, ref cmd, out moveTo)) return cmd;
                    exact = true;
                    break;
                case Action.Warp:
                    moveTo = m_warpGate.transform.position;
                    exact = true;
                    break;
                case Action.Approach:
                    moveTo = InterceptPoint(ghost, p, m_target);
                    break;
            }

            cmd.Move = Steer(ghost, pos, moveTo, exact);
            // 攻撃は何よりも先(弾いて当てる途中でも、近くの相手に当たるなら振る)
            if (CanHitAnyone(ghost, p)) cmd.Sweep = true;
            return cmd;
        }

        //========================================
        // 決める
        //========================================

        private void Decide(DarumaGhost ghost, CpuParams p)
        {
            Vector3 pos = ghost.transform.position;
            float speed = Mathf.Max(0.1f, ghost.MoveSpeed);

            // 一番早く追いつけそうな相手(今の相手にはおまけ)
            DarumaCharacter best = null;
            float bestTime = float.MaxValue;
            WarpGateGimmick bestGate = null;
            foreach (var s in m_survivors)
            {
                float time = CatchTime(pos, speed, s, p, out WarpGateGimmick gate);
                if (s.IsInvincible) time += p.GhostInvinciblePenalty;
                if (s == m_target) time -= p.GhostTargetStickiness;
                if (time < bestTime)
                {
                    best = s;
                    bestTime = time;
                    bestGate = gate;
                }
            }
            m_target = best;

            // 積み木を弾いて当てる方が早いか
            if (TryFindBat(ghost, p, out TumikiBlock block, out DarumaCharacter batTarget, out float batTime)
                && batTime * p.GhostBatPenalty < bestTime)
            {
                if (Current != Action.Bat || m_batBlock != block) m_batTime = 0.0f;
                Current = Action.Bat;
                m_batBlock = block;
                m_target = batTarget;
                m_warpGate = null;
                return;
            }
            m_batBlock = null;

            if (bestGate != null)
            {
                Current = Action.Warp;
                m_warpGate = bestGate;
                return;
            }
            Current = Action.Approach;
            m_warpGate = null;
        }

        // s に追いつくまでの見込みの時間(秒)。ワープゲートが近道なら gate に入る
        private float CatchTime(Vector3 pos, float speed, DarumaCharacter s, CpuParams p, out WarpGateGimmick gate)
        {
            gate = null;
            Vector3 sp = s.transform.position;
            float reach = s.IsInvincible ? 0.0f : 1.5f;
            float closing = ClosingSpeed(pos, speed, s, p);
            float direct = Mathf.Max(0.0f, CpuContext.FlatDistance(pos, sp) - reach) / closing;
            float best = direct;

            // ワープゲート:入るまで + ワープの手間 + 出口から(行き先が抽選なら出口ごとの平均)
            if (Time.time < m_noWarpUntil) return best;
            foreach (var g in m_gates)
            {
                if (g == null || !g.isActiveAndEnabled) continue;
                float sum = 0.0f;
                int exits = 0;
                foreach (var e in m_gates)
                {
                    if (e == g || e == null || !e.isActiveAndEnabled || e.Group != g.Group) continue;
                    sum += Mathf.Max(0.0f, CpuContext.FlatDistance(e.transform.position, sp) - reach) / closing;
                    exits++;
                }
                if (exits == 0) continue;
                float time = CpuContext.FlatDistance(pos, g.transform.position) / speed + p.GhostWarpCost + sum / exits;
                if (time < best)
                {
                    best = time;
                    gate = g;
                }
            }
            return best;
        }

        // s へ近づく速さの見込み(m/秒)。相手が今こちらから離れていく速さと、相手の最高速度を混ぜて逃げる速さとする
        private static float ClosingSpeed(Vector3 pos, float speed, DarumaCharacter s, CpuParams p)
        {
            Vector3 away = s.transform.position - pos;
            away.y = 0.0f;
            float awaySpeed = away.sqrMagnitude > 0.0001f ? Mathf.Max(0.0f, Vector3.Dot(s.HorizontalVelocity, away.normalized)) : 0.0f;
            float escape = Mathf.Lerp(awaySpeed, s.MoveSpeed, p.GhostEscapeWeight);
            return Mathf.Max(p.GhostMinClosingSpeed, speed - escape);
        }

        // 回り込む先:相手の今の動きのまま、こちらが着くころにいそうな場所(先読みは GhostMaxLeadTime まで)
        private static Vector3 InterceptPoint(DarumaGhost ghost, CpuParams p, DarumaCharacter s)
        {
            Vector3 pos = ghost.transform.position;
            Vector3 sp = s.transform.position;
            Vector3 v = s.HorizontalVelocity;
            v.y = 0.0f;
            float speed = Mathf.Max(0.1f, ghost.MoveSpeed);
            float lead = 0.0f;
            // 2 回繰り返して、着くまでの時間と先読みの位置をそろえる
            for (int i = 0; i < 2; i++)
            {
                lead = Mathf.Min(CpuContext.FlatDistance(pos, sp + v * lead) / speed, p.GhostMaxLeadTime);
            }
            return sp + v * lead;
        }

        //========================================
        // 攻撃
        //========================================

        // 今振るか:振れば当たる相手に気づいたら、反応の遅れ(GhostSwingReaction)のあとに振る
        // 空振りする回(GhostEarlySwingChance)は、届く距離に GhostEarlySwingSlack を足して見てしまう
        private bool CanHitAnyone(DarumaGhost ghost, CpuParams p)
        {
            DarumaHammer hammer = ghost.Hammer;
            if (hammer == null || hammer.IsBusy) return false;
            if (m_swingPending)
            {
                m_swingDelay -= Time.deltaTime;
                if (m_swingDelay > 0.0f) return false;
                OnSwing(p);
                return true;
            }

            float slack = m_earlySwing ? p.GhostEarlySwingSlack : 0.0f;
            bool hit = false;
            foreach (var s in m_survivors)
            {
                if (s.IsInvincible) continue;
                if (WillHit(ghost, hammer, p, s, m_velocity, slack))
                {
                    hit = true;
                    break;
                }
            }
            if (!hit) return false;

            float delay = Random.Range(p.GhostSwingReactionMin, Mathf.Max(p.GhostSwingReactionMin, p.GhostSwingReactionMax));
            if (delay > 0.0f)
            {
                m_swingPending = true;
                m_swingDelay = delay;
                return false;
            }
            OnSwing(p);
            return true;
        }

        // 振った:次の 1 回が空振りの回か決め直す
        private void OnSwing(CpuParams p)
        {
            m_swingPending = false;
            m_earlySwing = Random.value < p.GhostEarlySwingChance;
        }

        // 今振ると、ハンマーが相手の角度を通るときに相手が届く距離にいるか
        // (相手も自分も今の動きのまま進むとみなす。振っている間も追いかけ続けるので、逃げる相手にも届く。向きは振っている間変わらない)
        private static bool WillHit(DarumaGhost ghost, DarumaHammer hammer, CpuParams p, DarumaCharacter s, Vector3 selfVelocity, float slack)
        {
            Transform t = ghost.transform;
            Vector3 sp = s.transform.position;
            if (Mathf.Abs(sp.y - t.position.y) > kHitHeight) return false;
            // 自分から見た相手の動き
            Vector3 v = s.HorizontalVelocity - selfVelocity;
            v.y = 0.0f;
            float start = hammer.SweepStartYaw;
            float end = hammer.SweepEndYaw;
            float duration = hammer.SweepDuration / ghost.LocalTimeScale;
            float reach = hammer.CharacterHitDistance * p.GhostHitMargin;

            // 相手の角度 → ハンマーがそこを通る時間 → その時の相手の位置、を 2 回繰り返す
            float time = 0.0f;
            Vector3 local = Vector3.zero;
            for (int i = 0; i < 2; i++)
            {
                local = t.InverseTransformPoint(sp + v * time);
                float bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                if (bearing > start || bearing < end) return false;
                time = kSwingDelay + duration * (start - bearing) / (start - end);
            }
            // 時間がたつほど相手の動きは変わりやすいので、遠い角度ほど届く距離を控えめにする
            float margin = Mathf.Lerp(1.0f, 0.85f, time / Mathf.Max(0.01f, duration));
            if (new Vector2(local.x, local.z).magnitude > reach * margin + slack) return false;
            // 空中にいる相手は、ハンマーが通るまでに着地していなければ当たらない(少し先までなら高さの差で判断済み)
            if (!s.IsGrounded && time > 0.15f) return false;
            return true;
        }

        //========================================
        // 積み木を弾いて当てる
        //========================================

        // 弾いて当てるのに使える積み木と相手、見込みの時間(弾く位置へ行く + 振る + 積み木が飛ぶ)
        private bool TryFindBat(DarumaGhost ghost, CpuParams p, out TumikiBlock bestBlock, out DarumaCharacter bestTarget, out float bestTime)
        {
            bestBlock = null;
            bestTarget = null;
            bestTime = float.MaxValue;
            if (ghost.Hammer == null) return false;
            Vector3 pos = ghost.transform.position;
            float speed = Mathf.Max(0.1f, ghost.MoveSpeed);
            float launch = ghost.Hammer.SweepLaunchSpeed;

            foreach (var block in TumikiBlock.All)
            {
                if (!IsUsableBlock(block) || m_blacklist.ContainsKey(block)) continue;
                Vector3 b = block.transform.position;
                if (CpuContext.FlatDistance(pos, b) > p.GhostBatSearch) continue;
                // ハンマーが当たる高さにある積み木だけ(高い段の上などは届かない)
                if (Mathf.Abs(b.y - pos.y - TumikiBlock.kHeight * 0.5f) > kHitHeight) continue;

                foreach (var s in m_survivors)
                {
                    if (s.IsInvincible) continue;
                    if (!TryAimBat(ghost, p, block, s, launch, out Vector3 stand, out float flight)) continue;
                    float time = CpuContext.FlatDistance(pos, stand) / speed + ghost.Hammer.SweepDuration * 0.5f + flight;
                    if (block == m_batBlock && s == m_target) time -= p.GhostTargetStickiness;
                    if (time < bestTime)
                    {
                        bestTime = time;
                        bestBlock = block;
                        bestTarget = s;
                    }
                }
            }
            return bestBlock != null;
        }

        // block を弾いて s に当てる位置(stand)と、積み木が飛ぶ時間。届かない・通り道に床がないなら false
        private static bool TryAimBat(DarumaGhost ghost, CpuParams p, TumikiBlock block, DarumaCharacter s, float launch, out Vector3 stand, out float flight)
        {
            stand = Vector3.zero;
            flight = 0.0f;
            Vector3 b = block.transform.position;
            Vector3 sp = s.transform.position;
            Vector3 v = s.HorizontalVelocity;
            v.y = 0.0f;

            // 飛ぶ時間ぶん相手の動きを先読みする(長く読むほど外れるので半分まで)
            Vector3 aim = sp;
            for (int i = 0; i < 2; i++)
            {
                if (!TryFlightTime(CpuContext.FlatDistance(b, aim), launch, p, out flight)) return false;
                aim = sp + v * Mathf.Min(flight, p.GhostMaxLeadTime) * 0.5f;
            }
            Vector3 dir = aim - b;
            dir.y = 0.0f;
            if (dir.sqrMagnitude < 0.01f) return false;
            dir.Normalize();
            if (!HasGroundAlong(b, aim)) return false;

            stand = b - dir * kBatStandDistance;
            stand.y = ghost.transform.position.y;
            return true;
        }

        // 弾いた積み木が distance m 飛ぶ時間(秒)。当たらなくなる速さに落ちるまでに届かなければ false
        private static bool TryFlightTime(float distance, float launch, CpuParams p, out float time)
        {
            time = 0.0f;
            if (distance > p.GhostBatRange) return false;
            // distance = launch・t - decel・t² / 2
            float disc = launch * launch - 2.0f * kBlockDeceleration * distance;
            if (disc < 0.0f) return false;
            time = (launch - Mathf.Sqrt(disc)) / kBlockDeceleration;
            return launch - kBlockDeceleration * time >= kHarmlessSpeed;
        }

        // 積み木の通り道に床があるか(床がないと積み木が奈落へ落ちる)
        private static bool HasGroundAlong(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0.0f;
            float length = d.magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / kGroundCheckStep));
            for (int i = 1; i <= steps; i++)
            {
                Vector3 point = from + d * ((float)i / steps) + Vector3.up * 0.5f;
                if (!Physics.Raycast(point, Vector3.down, 0.5f + kGroundCheckDepth, ~0, QueryTriggerInteraction.Ignore)) return false;
            }
            return true;
        }

        // 弾く位置へ回り、弾く向きが相手へ向いたら振る。振ったら true(cmd は振る操作だけ)
        private bool TickBat(DarumaGhost ghost, CpuParams p, ref DarumaCommand cmd, out Vector3 moveTo)
        {
            Vector3 pos = ghost.transform.position;
            moveTo = pos;
            DarumaHammer hammer = ghost.Hammer;
            if (!TryAimBat(ghost, p, m_batBlock, m_target, hammer.SweepLaunchSpeed, out Vector3 stand, out _))
            {
                m_thinkTimer = 0.0f;
                return false;
            }
            moveTo = stand;
            // 弾く位置のそばでは、相手へ弾く向きにゆっくり進む(止まると幽霊の向きが変わらず、積み木が正面に来ないため)
            m_batCreep = CpuContext.FlatDistance(pos, stand) < kBatCreepDistance;
            if (m_batCreep)
            {
                Vector3 want = m_batBlock.transform.position - stand;
                want.y = 0.0f;
                if (want.sqrMagnitude > 0.0001f) moveTo = pos + want.normalized;
            }
            if (hammer.IsBusy) return false;

            // 弾く向きは幽霊から積み木への向き(正面から ±MaxLaunchAngle まで)。それが積み木から弾く位置の逆、つまり相手への向きにそろったら振る
            Vector3 b = m_batBlock.transform.position;
            Vector3 local = ghost.transform.InverseTransformPoint(b);
            float bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float dist = new Vector2(local.x, local.z).magnitude;
            if (Mathf.Abs(bearing) > hammer.MaxLaunchAngle || dist > hammer.BlockHitDistance * p.GhostHitMargin) return false;
            Vector3 launchDir = b - pos;
            launchDir.y = 0.0f;
            Vector3 wantDir = b - stand;
            wantDir.y = 0.0f;
            if (Vector3.Angle(launchDir, wantDir) > p.GhostBatAimTolerance) return false;

            cmd.Sweep = true;
            // 振ったら、次は近づく(積み木が当たれば復活する)
            m_thinkTimer = hammer.SweepDuration;
            m_blacklist[m_batBlock] = Time.time + kBatBlacklistTime;
            return true;
        }

        //========================================
        // 動き
        //========================================

        // to へ向かう入力。exact なら着く手前で緩める。引っかかっていたら横へよけ、入らないゲートはよける
        private Vector2 Steer(DarumaGhost ghost, Vector3 pos, Vector3 to, bool exact)
        {
            Vector3 d = to - pos;
            d.y = 0.0f;
            float dist = d.magnitude;
            if (dist < (exact ? kArriveDistance * 0.5f : 0.05f)) return Vector2.zero;
            Vector3 dir = d / dist;
            float strength = exact ? Mathf.Clamp01(dist / 1.0f) : 1.0f;
            // 弾く位置のすぐそばでは、積み木の方へ少しずつ寄って向きをそろえる
            if (Current == Action.Bat && m_batCreep) strength = kBatCreepInput;

            if (m_detourTimer > 0.0f)
            {
                dir = (m_detourDir * 0.8f + dir * 0.2f).normalized;
                strength = 1.0f;
            }
            dir = AvoidGates(pos, dir);
            m_stuckExpected += strength * ghost.MoveSpeed * Time.deltaTime * ghost.LocalTimeScale;
            m_lastMoveDir = dir;
            return new Vector2(dir.x, dir.z) * strength;
        }

        // 入るつもりのないゲートに近づいたら、ゲートへ向かう成分を消す(中に入ってしまったら外へ出る)
        private Vector3 AvoidGates(Vector3 pos, Vector3 dir)
        {
            foreach (var g in m_gates)
            {
                if (g == null || g == m_warpGate || !g.isActiveAndEnabled) continue;
                Vector3 to = g.transform.position - pos;
                to.y = 0.0f;
                float dist = to.magnitude;
                float r = g.WarpRadius + 0.45f;
                if (dist > r + kGateAvoidDistance || dist < 0.001f) continue;
                Vector3 n = to / dist;
                float inward = Vector3.Dot(dir, n);
                if (dist < r)
                {
                    dir = -n;
                    continue;
                }
                if (inward <= 0.0f) continue;
                Vector3 side = dir - n * inward;
                if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(Vector3.up, n);
                float w = Mathf.Clamp01((r + kGateAvoidDistance - dist) / kGateAvoidDistance);
                dir = Vector3.Lerp(dir, side.normalized, w).normalized;
            }
            return dir;
        }

        // 壁に沿って滑っても進めないときは、しばらく横へよける(よけても進めなければ反対側へ)
        private void UpdateStuck(DarumaGhost ghost, Vector3 pos, float dt)
        {
            if (m_detourTimer > 0.0f) m_detourTimer -= dt;
            if (!m_hasLastPos) return;
            m_stuckMoved += CpuContext.FlatDistance(pos, m_lastPos);
            m_stuckTimer += dt;
            if (m_stuckTimer < kStuckCheckTime) return;

            bool stuck = m_stuckExpected > 0.3f && m_stuckMoved < m_stuckExpected * 0.3f;
            if (stuck && m_lastMoveDir.sqrMagnitude > 0.01f)
            {
                if (m_detourTimer > 0.0f) m_detourSide = -m_detourSide;
                m_detourDir = Vector3.Cross(Vector3.up, m_lastMoveDir).normalized * m_detourSide;
                m_detourTimer = kDetourTime;
            }
            m_stuckTimer = 0.0f;
            m_stuckMoved = 0.0f;
            m_stuckExpected = 0.0f;
        }

        //========================================
        // 下調べ
        //========================================

        private void CollectSurvivors(MatchPlayer self)
        {
            m_survivors.Clear();
            DarumaMatch match = DarumaMatch.Current;
            if (match == null) return;
            foreach (var mp in match.Players)
            {
                if (mp == null || mp == self || !mp.IsAlive) continue;
                if (IsValidTarget(mp.Character)) m_survivors.Add(mp.Character);
            }
        }

        private static bool IsValidTarget(DarumaCharacter c)
        {
            return c != null && c.isActiveAndEnabled && c.CanBeAffected;
        }

        private static bool IsUsableBlock(TumikiBlock block)
        {
            return block != null && block.isActiveAndEnabled && block.CurrentState == TumikiBlock.State.Loose && block.CanBeAffected;
        }

        // プレイヤー(幽霊)を送るワープゲート
        private void RefreshGates(float dt)
        {
            m_gateRefreshTimer -= dt;
            if (m_gateRefreshTimer > 0.0f) return;
            m_gateRefreshTimer = kGateRefreshInterval;
            m_gates.Clear();
            m_gates.AddRange(CpuWarpGates.PlayerGates());
        }

        private void ForgetBlacklist()
        {
            if (m_blacklist.Count == 0) return;
            m_expired.Clear();
            foreach (var kv in m_blacklist)
            {
                if (kv.Key == null || kv.Value < Time.time) m_expired.Add(kv.Key);
            }
            foreach (var b in m_expired) m_blacklist.Remove(b);
        }

        public void DrawGizmos(DarumaGhost ghost)
        {
            if (ghost == null) return;
            Vector3 pos = ghost.transform.position + Vector3.up * 0.5f;
            if (m_target != null)
            {
                Gizmos.color = new Color(0.6f, 0.8f, 1.0f);
                Gizmos.DrawLine(pos, m_target.transform.position + Vector3.up * 0.5f);
            }
            if (Current == Action.Bat && m_batBlock != null)
            {
                Gizmos.color = new Color(1.0f, 0.8f, 0.2f);
                Gizmos.DrawWireSphere(m_batBlock.transform.position, 0.6f);
            }
            if (Current == Action.Warp && m_warpGate != null)
            {
                Gizmos.color = new Color(0.9f, 0.4f, 0.9f);
                Gizmos.DrawLine(pos, m_warpGate.transform.position);
            }
        }
    }
}
