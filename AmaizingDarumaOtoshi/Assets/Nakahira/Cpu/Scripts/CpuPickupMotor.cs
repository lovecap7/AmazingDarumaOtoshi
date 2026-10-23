using UnityEngine;

namespace Nakahira
{
    // 動きの部品「跳んで拾う」:決めた積み木へ道をたどって近づき、落ちてくるときにちょうど触れる距離で跳ぶ
    //  ・跳ぶのは道の最後のまっすぐな区間だけ(奈落の向こうへは跳ばない)
    //  ・動いている積み木は届くころの位置を狙う(飛んでいる積み木は減速を、運ばれている積み木は測った速度を使う)
    //  ・近づけないまま時間が経ったら諦める(遠回りの道でも、進んでいる間は諦めない)
    public class CpuPickupMotor
    {
        public enum Status
        {
            Running,    // 追っている
            Lost,       // 拾った・ほかの人に拾われた・壊れたなどで、もう拾えない
            GaveUp,     // 近づけないので諦めた(しばらく狙わない)
        }

        // 道の終わりが積み木からこの距離(m)以内なら、歩いて行けるとみなす(積み木が床の縁にあっても拾えるように)
        public const float GoalTolerance = 1.0f;

        // 積み木の上下の速さがこれ未満なら、置かれている(落ちてくる途中ではない)とみなす
        const float kSettledSpeed = 0.5f;
        // ジャンプの前に積み木に触れて押してしまわないよう、最低でもこの距離からは跳ぶ(体の半径+積み木の半径+余裕)
        const float kMinJumpDistance = DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.1f;
        // 飛んでいる積み木の位置を先読みする最大の時間(秒)
        const float kMaxLeadTime = 2.0f;
        // 落ちている積み木(回転台などで運ばれている)の位置を、測った速度で先読みする最大の時間(秒)
        // (運ばれる向きは変わっていくので、長く先読みしすぎない)
        const float kMaxCarriedLeadTime = 0.6f;
        // 測った速度をなめらかにする速さ(大きいほど今の速度にすぐ合わせる)
        const float kVelocitySmoothing = 15.0f;
        // 空中で狙う位置の真上に止まるための減速を始める時間(秒)
        const float kAirBrakeTime = 0.1f;
        // 積み木までの残りの道がこの長さ(m)以上縮んだら、近づいているとみなす(諦めるまでの時間を数え直す)
        const float kProgressDistance = 0.5f;
        // 跳んでからこの時間(秒)は「拾うジャンプの最中」とみなす(上がって落ちるまで)
        const float kJumpDuration = 0.5f;
        // 中継点にこの距離(m)まで近づいたら、積み木へ向かう
        const float kWaypointReach = 1.0f;
        // 中継点の位置がこの距離(m)以上変わったら、道を求め直す
        const float kWaypointMoved = 0.5f;

        readonly CpuPathFollower m_route = new CpuPathFollower();
        TumikiBlock m_target;
        Rigidbody m_targetRb;
        // 近づけていない時間(残りの道が縮むたびに 0 に戻す)
        float m_chaseTime;
        // 狙ってから一番近づいたときの、残りの道の長さ
        float m_closestRemaining;
        // 狙っている積み木の、位置の変化から測った水平の速度(ギミックで運ばれている分も含む)
        Vector3 m_targetVelocity;
        Vector3 m_targetLastPos;
        float m_targetLastSampleTime = -1.0f;
        // 狙いごとの腕前のぶれ(狙い直すたびに決め直す)
        float m_jumpDistanceScale = 1.0f;
        Vector3 m_aimOffset;
        float m_jumpTime = -1.0f;
        // 積み木の前に寄る中継点(危険な相手のそばを避けて遠回りするとき)
        bool m_hasWaypoint;
        Vector3 m_waypoint;

        // 狙っている積み木(なければnull)
        public TumikiBlock Target => m_target;
        public bool HasWaypoint => m_hasWaypoint;
        public Vector3 Waypoint => m_waypoint;
        // 着地を狙っている位置・次に向かう曲がり角(デバッグ表示用)
        public Vector3 AimPoint { get; private set; }
        public CpuPathFollower Route => m_route;

        // 積み木を拾うために跳んで、まだ空中にいる
        public bool IsJumping(DarumaCharacter self) => m_target != null && !self.IsGrounded && Time.time - m_jumpTime < kJumpDuration;

        public void SetTarget(TumikiBlock block, CpuParams p)
        {
            if (block == m_target) return;
            m_target = block;
            m_targetRb = block != null ? block.GetComponent<Rigidbody>() : null;
            m_chaseTime = 0.0f;
            m_closestRemaining = float.MaxValue;
            m_targetVelocity = Vector3.zero;
            m_targetLastSampleTime = -1.0f;
            m_jumpDistanceScale = 1.0f + Random.Range(-p.JumpDistanceError, p.JumpDistanceError);
            Vector2 offset = Random.insideUnitCircle * p.AimError;
            m_aimOffset = new Vector3(offset.x, 0.0f, offset.y);
            m_hasWaypoint = false;
            if (block != null) m_route.SetDestination(block.transform.position, GoalTolerance);
            else m_route.Clear();
        }

        // 積み木の前に中継点へ寄る(has が false なら寄らずに積み木へ向かう)
        public void SetWaypoint(bool has, Vector3 waypoint)
        {
            if (m_target == null) return;
            if (has == m_hasWaypoint && (!has || CpuContext.FlatDistance(waypoint, m_waypoint) < kWaypointMoved)) return;
            m_hasWaypoint = has;
            m_waypoint = waypoint;
            m_route.SetDestination(has ? waypoint : m_target.transform.position, GoalTolerance);
            // 向かう先が変わったので、近づいているかは測り直す
            m_closestRemaining = float.MaxValue;
        }

        public void Clear()
        {
            m_target = null;
            m_targetRb = null;
            m_hasWaypoint = false;
            m_route.Clear();
        }

        // 狙っている積み木を拾う操作を cmd に書く
        public Status Tick(CpuContext c, ref DarumaCommand cmd)
        {
            if (m_target == null) return Status.Lost;
            if (!CpuBlockSearch.IsCollectable(m_target))
            {
                Clear();
                return Status.Lost;
            }

            CpuParams p = c.Params;
            m_chaseTime += c.DeltaTime;
            if (m_chaseTime > p.GiveUpTime)
            {
                c.GiveUp(m_target);
                Clear();
                return Status.GaveUp;
            }

            // 中継点に着いたら、積み木へ向かう
            if (m_hasWaypoint && CpuContext.FlatDistance(c.Position, m_waypoint) < kWaypointReach) SetWaypoint(false, Vector3.zero);

            m_route.MoveDestination(m_hasWaypoint ? m_waypoint : m_target.transform.position);
            if (m_route.Update(c.Position, p, c.DeltaTime, out float remaining) && remaining < m_closestRemaining - kProgressDistance)
            {
                m_closestRemaining = remaining;
                m_chaseTime = 0.0f;
            }

            MeasureTargetVelocity();
            Steer(c, ref cmd);
            return Status.Running;
        }

        private void Steer(CpuContext c, ref DarumaCommand cmd)
        {
            DarumaCharacter self = c.Self;
            CpuParams p = c.Params;
            Vector3 pos = c.Position;
            float speed = Mathf.Max(0.1f, self.MoveSpeed);

            // 道の曲がり角を順にたどる。最後の曲がり角(積み木のそば)の手前までは、そこへ向かって歩くだけ
            // 中継点へ向かっている間は、中継点まで歩くだけ(跳ばない)
            bool finalLeg = m_route.IsOnFinalLeg(pos, out Vector3 corner);
            if (!finalLeg || m_hasWaypoint)
            {
                AimPoint = corner;
                CpuPathFollower.Walk(pos, corner, speed, p, ref cmd);
                return;
            }

            // 最後のまっすぐな区間:積み木を狙って跳ぶ
            Vector3 blockPos = m_target.transform.position;
            float reach = ReachTime(self, blockPos);

            // 動いている積み木は、届くころの位置を狙う(届く時間は予想した位置までの距離で求め直す)
            Vector3 predicted = blockPos;
            if (self.IsGrounded)
            {
                for (int i = 0; i < 2; i++)
                {
                    float lead = CpuContext.FlatDistance(pos, predicted) / speed + reach;
                    predicted = PredictTarget(lead);
                }
            }
            else
            {
                predicted = PredictTarget(reach * 0.5f);
            }
            Vector3 aim = predicted + m_aimOffset;
            AimPoint = aim;

            Vector3 to = aim - pos;
            to.y = 0.0f;
            float dist = to.magnitude;
            Vector2 dir = dist > 0.001f ? new Vector2(to.x, to.z) / dist : Vector2.zero;

            if (!self.IsGrounded)
            {
                // 空中:狙う位置の真上で止まるよう、近づくほどゆっくりにする(真上から落ちれば拾える)
                cmd.Move = dir * Mathf.Clamp01(dist / (speed * kAirBrakeTime));
                return;
            }

            bool settled = m_targetRb == null || Mathf.Abs(m_targetRb.linearVelocity.y) < kSettledSpeed;
            // 跳んでから落ちてきて積み木に届くまでに進める距離
            float jumpDistance = Mathf.Max(speed * reach * m_jumpDistanceScale, kMinJumpDistance);
            if (settled && dist <= jumpDistance)
            {
                cmd.Jump = true;
                cmd.Move = dir;
                m_jumpTime = Time.time;
                return;
            }

            // 積み木が落ちてくる途中なら、触れない距離で待つ
            if (!settled && dist <= kMinJumpDistance + 0.3f)
            {
                cmd.Move = Vector2.zero;
                return;
            }

            CpuPathFollower.Walk(pos, aim, speed, p, ref cmd);
        }

        // 狙っている積み木の速度を、物理の1ステップごとの位置の変化から測る
        // (回転台・風などで運ばれている分は Rigidbody の速度に表れないことがあるため)
        private void MeasureTargetVelocity()
        {
            float now = Time.fixedTime;
            if (now == m_targetLastSampleTime) return;   // 物理が進んでいない(位置が変わらない)フレームは測らない
            Vector3 pos = m_target.transform.position;
            if (m_targetLastSampleTime >= 0.0f)
            {
                Vector3 v = (pos - m_targetLastPos) / (now - m_targetLastSampleTime);
                v.y = 0.0f;
                float t = 1.0f - Mathf.Exp(-kVelocitySmoothing * (now - m_targetLastSampleTime));
                m_targetVelocity = Vector3.Lerp(m_targetVelocity, v, t);
            }
            m_targetLastPos = pos;
            m_targetLastSampleTime = now;
        }

        // 狙っている積み木の seconds 秒後の位置の予想
        // 飛んでいる積み木は減速して止まるところまで、落ちている積み木は測った速度で(運ばれている分)
        private Vector3 PredictTarget(float seconds)
        {
            if (m_target.CurrentState == TumikiBlock.State.Flying)
            {
                return m_target.PredictPosition(Mathf.Min(seconds, kMaxLeadTime));
            }
            return m_target.transform.position + m_targetVelocity * Mathf.Min(seconds, kMaxCarriedLeadTime);
        }

        // 跳んでから、落ちてくる途中で積み木の上面の高さに届くまでの時間(秒)
        private static float ReachTime(DarumaCharacter self, Vector3 blockPos)
        {
            JumpParams jump = self.JumpParams;
            float fall = Mathf.Max(0.0f, jump.Height - CpuBlockSearch.BlockTopFromFeet(self, blockPos));
            float fallGravity = jump.Gravity * jump.FallGravityMultiplier;
            return jump.TimeToApex + Mathf.Sqrt(2.0f * fall / fallGravity);
        }

        public void DrawGizmos(DarumaCharacter self)
        {
            if (m_target == null) return;
            m_route.DrawGizmos(self.transform.position);
            Gizmos.DrawWireSphere(m_target.transform.position, TumikiBlock.kRadius);
            if (m_hasWaypoint) Gizmos.DrawWireCube(m_waypoint, Vector3.one * 0.4f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(AimPoint, 0.15f);
        }
    }
}
