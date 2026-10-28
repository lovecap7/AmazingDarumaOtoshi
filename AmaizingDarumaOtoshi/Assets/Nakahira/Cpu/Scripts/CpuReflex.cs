using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // とっさの反応(割り込み層):自分に当たる弾(攻撃判定のある積み木)に反応する。どの状態の最中でも割り込む
    //  反応の種類(タイミングが難しい順。落ち着いているほど難しい方を選ぶ):
    //   ・ジャンプ乗り:落ちてくる途中で弾に触れるように跳ぶ(被弾せずに積める)
    //   ・打ち返し  :ハンマーが弾の方向を通る瞬間に、弾が届く距離にあるように振る。
    //                 弾がハンマーの届く角度の外なら、必要なだけ向きを変える(狙いは考えない)
    //   ・ジャンプ避け:弾が通り過ぎる間、足が弾より上にあるように跳ぶ
    //  弾ごとに一度だけ「反応するか・どれで反応するか」を決める。確率は集中度(疲れ)が高いほど下がる
    //  反応すると決めたら集中度を足す。反応の最中は、状態が出したジャンプ・薙ぎ払いを止める
    public class CpuReflex
    {
        public enum Action { None, Catch, Return, Dodge }

        // 当たるか調べるときの時間の刻み(秒)
        const float kSampleStep = 0.01f;
        // 体と積み木が触れる、中心同士の水平の距離
        const float kContact = DarumaCharacter.kBodyRadius + TumikiBlock.kRadius;
        // 打ち返し:弾が正面からこの角度(度)以内なら、向きを変えずに振る
        const float kReturnMaxBearing = 105.0f;
        // 打ち返し:向きを変えるときは、弾を正面からこの角度(度)に置く(必要最小限の回転)
        const float kReturnTurnBearing = 95.0f;
        // 打ち返し:向きを変えるのにかかる時間の見積もり(秒)
        const float kTurnTime = 0.12f;
        // 打ち返し:ハンマーが当たる高さ(足元からの積み木の中心の高さ)の範囲
        const float kReturnMinHeight = -0.35f;
        const float kReturnMaxHeight = 0.85f;
        // ジャンプ:弾の上面より、この高さ(m)だけ余裕を見る
        const float kJumpMargin = 0.05f;
        // 反応したあと、この時間(秒)は反応の最中として扱う(空中で弾の通り道に留まる)
        const float kAfterAction = 0.4f;
        // 向きを変えるための、ごく小さい移動入力
        const float kTurnInput = 0.05f;

        // 当たる弾の予想(時間は今からの秒数)
        struct Threat
        {
            public float TReach;    // ハンマーが届く距離に入る時刻(届かなければ負)
            public float TIn;       // 体に触れ始める時刻
            public float TOut;      // 体から離れる時刻
            public float Top;       // 足元から見た弾の上面の高さ
            public float Center;    // 足元から見た弾の中心の高さ
        }

        TumikiBlock m_block;
        Action m_action;
        float m_noticeTime;
        float m_timingError;
        bool m_executed;
        float m_executedTime;
        // 反応するか決めた弾(反応しないと決めた弾も含む。弾ごとに一度だけ決める)
        readonly HashSet<TumikiBlock> m_decided = new HashSet<TumikiBlock>();
        readonly List<TumikiBlock> m_forget = new List<TumikiBlock>();

        public Action Current => m_block != null ? m_action : Action.None;
        public TumikiBlock Block => m_block;

        public void Reset()
        {
            m_block = null;
            m_decided.Clear();
        }

        // 状態が出した操作に、とっさの反応を割り込ませる(毎フレーム、状態の Tick のあとに呼ぶ)
        public void Apply(CpuContext c, ref DarumaCommand cmd)
        {
            ForgetPassedBlocks();
            NoticeNewThreat(c);
            if (m_block == null) return;

            if (!m_block.isActiveAndEnabled || (m_executed && Time.time - m_executedTime > kAfterAction))
            {
                m_block = null;
                return;
            }
            bool threat = TryPredictHit(c, m_block, out Threat t);
            if (!m_executed && !threat)
            {
                // もう当たらない(向きが変わった・止まった・ほかの物に当たった)
                m_block = null;
                return;
            }

            // 反応の最中は、状態のジャンプ・薙ぎ払いを止める(跳んでしまうと反応できない)
            if (!m_executed)
            {
                cmd.Jump = false;
                cmd.Sweep = false;
            }
            bool canAct = Time.time >= m_noticeTime + c.Params.ReflexReaction;

            switch (m_action)
            {
                case Action.Catch:
                case Action.Dodge:
                    if (!m_executed)
                    {
                        // ジャンプ乗りは弾の通り道に留まる(動くと触れる時刻がずれる)
                        if (m_action == Action.Catch) cmd.Move = Vector2.zero;
                        float jumpIn = (m_action == Action.Catch ? CatchJumpTime(c, t) : DodgeJumpTime(c, t)) + m_timingError;
                        if (canAct && jumpIn <= c.DeltaTime * 0.5f && c.Self.IsGrounded)
                        {
                            cmd.Jump = true;
                            Execute();
                        }
                    }
                    else if (!c.Self.IsGrounded)
                    {
                        // 空中では弾の通り道に留まる
                        cmd.Move = Vector2.zero;
                    }
                    break;

                case Action.Return:
                    if (!m_executed && threat) StepReturn(c, t, canAct, ref cmd);
                    break;
            }
        }

        //========================================
        // 弾に気づいて、反応を決める
        //========================================

        // まだ決めていない弾のうち、一番早く当たる弾について、反応するか・どれで反応するかを決める
        private void NoticeNewThreat(CpuContext c)
        {
            if (m_block != null) return;
            TumikiBlock first = null;
            Threat firstThreat = default;
            foreach (var block in TumikiBlock.All)
            {
                if (block == null || m_decided.Contains(block)) continue;
                if (!TryPredictHit(c, block, out Threat t)) continue;
                if (first == null || t.TIn < firstThreat.TIn)
                {
                    first = block;
                    firstThreat = t;
                }
            }
            if (first == null) return;

            m_decided.Add(first);
            CpuParams p = c.Params;
            float alert = c.Alertness;
            // 疲れているほど気づけない
            if (Random.value >= p.ReactChance * alert) return;

            // 難しい順に、落ち着いているほど選びやすい。間に合わない反応は飛ばす
            float reaction = p.ReflexReaction;
            Action action = Action.None;
            if (Random.value < p.CatchChance * alert && CanCatch(c, firstThreat, reaction)) action = Action.Catch;
            else if (Random.value < p.ReturnChance * alert && CanReturn(c, first, firstThreat, reaction)) action = Action.Return;
            else if (CanDodge(c, firstThreat, reaction)) action = Action.Dodge;
            if (action == Action.None) return;

            m_block = first;
            m_action = action;
            m_noticeTime = Time.time;
            m_timingError = Random.Range(-p.ReflexTimingError, p.ReflexTimingError);
            m_executed = false;
            c.AddLoad(p.AttentionOnProjectile);
        }

        // 攻撃判定がなくなった弾は忘れる(また攻撃判定を持ったら、改めて決める)
        private void ForgetPassedBlocks()
        {
            m_forget.Clear();
            foreach (var block in m_decided)
            {
                if (block == null || !block.IsHarmful) m_forget.Add(block);
            }
            foreach (var block in m_forget) m_decided.Remove(block);
        }

        // 弾がこのまま(減速しながら直進して)自分に当たるか。自分は今の位置に留まるとみなす
        private static bool TryPredictHit(CpuContext c, TumikiBlock block, out Threat t)
        {
            t = default;
            if (!block.IsHarmful || block.IsIgnoring(c.Self.Body)) return false;
            if (!c.Self.IsGrounded) return false;

            Vector3 bp0 = block.transform.position;
            t.Center = bp0.y - c.Position.y;
            t.Top = CpuBlockSearch.BlockTopFromFeet(c.Self, bp0);
            // 頭より上・足より下を通る弾は当たらない
            if (t.Top < 0.0f || t.Top - TumikiBlock.kHeight > c.Self.Stack.Height) return false;

            float horizon = Mathf.Min(c.Params.ReflexHorizon, block.HarmfulTimeLeft);
            float reach = c.Self.Hammer.BlockHitDistance;
            t.TReach = -1.0f;
            t.TIn = -1.0f;
            for (float time = 0.0f; time <= horizon; time += kSampleStep)
            {
                float d = CpuContext.FlatDistance(block.PredictPosition(time), c.Position);
                if (t.TReach < 0.0f && d <= reach) t.TReach = time;
                if (d <= kContact)
                {
                    if (t.TIn < 0.0f) t.TIn = time;
                    t.TOut = time;
                }
                else if (t.TIn >= 0.0f) break;
            }
            // もう触れている弾には間に合わない
            return t.TIn > 0.0f;
        }

        //========================================
        // ジャンプ乗り・ジャンプ避け
        //========================================

        // 落ちてくる途中で、弾が体を通り過ぎている間に足が弾の上面より下に来るように跳ぶ時刻(今からの秒数)
        private static float CatchJumpTime(CpuContext c, Threat t)
        {
            return (t.TIn + t.TOut) * 0.5f - FallTime(c.Self.JumpParams, t.Top + kJumpMargin) - InputLatency;
        }

        // 弾が体を通り過ぎる間、足が弾の上面より上にあるように跳ぶ時刻(今からの秒数)
        private static float DodgeJumpTime(CpuContext c, Threat t)
        {
            return t.TIn - RiseTime(c.Self.JumpParams, t.Top + kJumpMargin) - InputLatency;
        }

        // 操作命令が物理に反映されるまでの遅れ(秒)。命令は次の物理ステップで反映される
        private static float InputLatency => Time.fixedDeltaTime;

        private static bool CanCatch(CpuContext c, Threat t, float reaction)
        {
            JumpParams jump = c.Self.JumpParams;
            if (t.Top + kJumpMargin >= jump.Height) return false;
            float jumpIn = CatchJumpTime(c, t);
            // 跳ぶのが間に合い、上がっている途中に当たらない(弾が来る前に弾の上面より上に上がれる)
            return jumpIn >= reaction && jumpIn + RiseTime(jump, t.Top + kJumpMargin) <= t.TIn;
        }

        private static bool CanDodge(CpuContext c, Threat t, float reaction)
        {
            JumpParams jump = c.Self.JumpParams;
            float h = t.Top + kJumpMargin;
            if (h >= jump.Height) return false;
            float jumpIn = DodgeJumpTime(c, t);
            // 跳ぶのが間に合い、弾が通り過ぎるまで落ちてこない
            return jumpIn >= reaction && t.TOut <= jumpIn + FallTime(jump, h);
        }

        // 跳んでから、上がる途中で足が高さ h に届くまでの時間(秒)
        // 物理は刻みで進むので、計算上の時間を次の刻みに切り上げる
        private static float RiseTime(JumpParams jump, float h)
        {
            float g = jump.Gravity;
            float v0 = jump.JumpSpeed;
            float disc = v0 * v0 - 2.0f * g * h;
            float time = disc < 0.0f ? jump.TimeToApex : (v0 - Mathf.Sqrt(disc)) / g;
            float step = Time.fixedDeltaTime;
            return Mathf.Ceil(time / step) * step + step;
        }

        // 跳んでから、落ちてくる途中で足が高さ h まで下がるまでの時間(秒)
        private static float FallTime(JumpParams jump, float h)
        {
            float fallGravity = jump.Gravity * jump.FallGravityMultiplier;
            return jump.TimeToApex + Mathf.Sqrt(2.0f * Mathf.Max(0.0f, jump.Height - h) / fallGravity);
        }

        //========================================
        // 打ち返し
        //========================================

        private static bool CanReturn(CpuContext c, TumikiBlock block, Threat t, float reaction)
        {
            if (t.TReach < 0.0f || c.Self.Hammer.IsBusy) return false;
            if (t.Center < kReturnMinHeight || t.Center > kReturnMaxHeight) return false;
            float bearing = Bearing(c, block.transform.position);
            float turn = Mathf.Abs(bearing) > kReturnMaxBearing ? kTurnTime : 0.0f;
            float aimBearing = turn > 0.0f ? Mathf.Sign(bearing) * kReturnTurnBearing : bearing;
            return SwingStartIn(c, t, aimBearing) >= reaction + turn;
        }

        private void StepReturn(CpuContext c, Threat t, bool canAct, ref DarumaCommand cmd)
        {
            float bearing = Bearing(c, m_block.transform.position);
            // ハンマーの届く角度の外なら、必要なだけ向きを変える
            if (Mathf.Abs(bearing) > kReturnMaxBearing)
            {
                Vector3 toBlock = m_block.transform.position - c.Position;
                toBlock.y = 0.0f;
                Vector3 facing = Quaternion.AngleAxis(-Mathf.Sign(bearing) * kReturnTurnBearing, Vector3.up) * toBlock.normalized;
                cmd.Move = new Vector2(facing.x, facing.z) * kTurnInput;
                return;
            }
            cmd.Move = Vector2.zero;

            float startIn = SwingStartIn(c, t, bearing) + m_timingError;
            if (canAct && startIn <= c.DeltaTime * 0.5f && c.Self.IsGrounded && !c.Self.Hammer.IsBusy)
            {
                cmd.Sweep = true;
                Execute();
            }
        }

        // ハンマーが弾の方向(bearing)を通る瞬間に、弾がハンマーの届く距離にあるように振り始める時刻(今からの秒数)
        private static float SwingStartIn(CpuContext c, Threat t, float bearing)
        {
            DarumaHammer hammer = c.Self.Hammer;
            float start = hammer.SweepStartYaw;
            float end = hammer.SweepEndYaw;
            float cross = Mathf.Clamp01((start - bearing) / (start - end)) * hammer.SweepDuration;
            // 届く距離に入ってから体に触れるまでの間の真ん中で当てる
            float hitTime = (t.TReach + t.TIn) * 0.5f;
            return hitTime - cross - InputLatency;
        }

        // 自分の正面から見た位置の角度(度。右が +)
        private static float Bearing(CpuContext c, Vector3 position)
        {
            Vector3 forward = c.Self.transform.forward;
            forward.y = 0.0f;
            Vector3 to = position - c.Position;
            to.y = 0.0f;
            return Vector3.SignedAngle(forward, to, Vector3.up);
        }

        private void Execute()
        {
            m_executed = true;
            m_executedTime = Time.time;
        }
    }
}
