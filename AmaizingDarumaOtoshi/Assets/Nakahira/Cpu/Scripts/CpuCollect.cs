using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // CPU の行動「積む」:近くの積み木を選び、落下しながら触れるようにジャンプして拾う
    //  ・狙うのは落ちている積み木と、攻撃判定のない(ゆっくり)動いている積み木
    //  ・奈落・壁・バンパーはナビメッシュ(CpuNavMesh)の道で迂回する。近さは歩く道の長さで比べ、歩いて行けない積み木は狙わない
    //  ・相手の近くにある積み木は後回しにする
    //  ・ジャンプの高さ・重力と移動速度から、落ちてくるときにちょうど積み木に届く距離で跳ぶ
    //    (跳ぶのは道の最後のまっすぐな区間だけ。奈落の向こうへは跳ばない)
    public class CpuCollect
    {
        // 狙う積み木の候補
        struct Candidate
        {
            public TumikiBlock Block;
            public float Distance;      // まっすぐの距離(道の長さはこれ以上になる)
            public float OpponentCost;
            public float LowerBound => Distance + OpponentCost;
        }

        // 積み木の上下の速さがこれ未満なら、置かれている(落ちてくる途中ではない)とみなす
        const float kSettledSpeed = 0.5f;
        // ジャンプの前に積み木に触れて押してしまわないよう、最低でもこの距離からは跳ぶ(体の半径+積み木の半径+余裕)
        const float kMinJumpDistance = DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.1f;
        // 動いている積み木の位置を先読みする最大の時間(秒)
        const float kMaxLeadTime = 2.0f;
        // 空中で狙う位置の真上に止まるための減速を始める時間(秒)
        const float kAirBrakeTime = 0.1f;
        // 道の終わりが積み木からこの距離(m)以内なら、歩いて行けるとみなす(積み木が床の縁にあっても拾えるように)
        const float kGoalTolerance = 1.0f;
        // 道の曲がり角にこの距離(m)まで近づいたら、次の曲がり角へ向かう
        const float kCornerReach = 0.35f;
        // 1回選び直すときに道を求める積み木の最大数(近い順。重くならないように)
        const int kMaxPathQueries = 12;
        // 積み木までの残りの道がこの長さ(m)以上縮んだら、近づいているとみなす(諦めるまでの時間を数え直す)
        const float kProgressDistance = 0.5f;
        // 落ちている積み木(回転台などで運ばれている)の位置を、測った速度で先読みする最大の時間(秒)
        // (運ばれる向きは変わっていくので、長く先読みしすぎない)
        const float kMaxCarriedLeadTime = 0.6f;
        // 測った速度をなめらかにする速さ(大きいほど今の速度にすぐ合わせる)
        const float kVelocitySmoothing = 15.0f;
        // 触れると危ないギミック(バンパー)からこの距離(m)以内の積み木は、拾いに行くと触れてしまうので狙わない(体の半径+余裕)
        const float kHazardClearance = DarumaCharacter.kBodyRadius + 0.3f;

        TumikiBlock m_target;
        Rigidbody m_targetRb;
        float m_thinkTimer;
        // 近づけていない時間(残りの道が縮むたびに 0 に戻す)
        float m_chaseTime;
        // 狙ってから一番近づいたときの、残りの道の長さ
        float m_closestRemaining;
        float m_repathTimer;
        // 狙っている積み木の、位置の変化から測った水平の速度(ギミックで運ばれている分も含む)
        Vector3 m_targetVelocity;
        Vector3 m_targetLastPos;
        float m_targetLastSampleTime = -1.0f;
        // 狙いごとの腕前のぶれ(狙い直すたびに決め直す)
        float m_jumpDistanceScale = 1.0f;
        Vector3 m_aimOffset;
        // 諦めた積み木と、また狙ってよくなる時刻
        readonly Dictionary<TumikiBlock, float> m_givenUp = new Dictionary<TumikiBlock, float>();
        readonly List<Candidate> m_candidates = new List<Candidate>();
        readonly List<TumikiBlock> m_expired = new List<TumikiBlock>();

        // 狙っている積み木までの道(曲がり角。0番は道を求めたときの位置)
        NavMeshPath m_path;           // MonoBehaviour の初期化中には作れないので、使うときに作る
        NavMeshPath m_queryPath;      // 候補を比べるとき用
        readonly Vector3[] m_corners = new Vector3[64];
        int m_cornerCount;
        int m_cornerIndex;

        // 狙っている積み木(なければnull)
        public TumikiBlock Target => m_target;
        // 着地を狙っている位置(デバッグ表示用)
        public Vector3 AimPoint { get; private set; }
        // 今たどっている道(デバッグ表示用)
        public int RouteCount => m_cornerCount;
        public int RouteIndex => m_cornerIndex;
        public Vector3 RouteCorner(int i) => m_corners[i];

        // 積み木を集める操作を cmd に書く。集める必要がない・拾える積み木がないときは false(cmd は変えない)
        // opponents: 相手の位置
        public bool Tick(DarumaCharacter self, CpuParams p, List<Vector3> opponents, float dt, ref DarumaCommand cmd)
        {
            if (m_path == null)
            {
                m_path = new NavMeshPath();
                m_queryPath = new NavMeshPath();
            }

            if (self.Stack.Count >= p.CollectUntil)
            {
                ClearTarget();
                return false;
            }

            // 拾った・崩れた・撃たれたなどで狙えなくなったら、すぐ選び直す
            if (m_target != null && !IsCandidate(m_target))
            {
                ClearTarget();
                m_thinkTimer = 0.0f;
            }

            m_thinkTimer -= dt;
            if (m_thinkTimer <= 0.0f)
            {
                m_thinkTimer = p.ThinkInterval;
                ChooseTarget(self, p, opponents);
            }
            if (m_target == null) return false;

            // 近づけないまま時間が経った積み木は、しばらく諦める(道を進んでいる間は諦めない)
            m_chaseTime += dt;
            if (m_chaseTime > p.GiveUpTime)
            {
                m_givenUp[m_target] = Time.time + p.GiveUpCooldown;
                ClearTarget();
                return false;
            }

            m_repathTimer -= dt;
            if (m_repathTimer <= 0.0f) UpdateRoute(self, p);

            MeasureTargetVelocity();
            Steer(self, p, ref cmd);
            return true;
        }

        //========================================
        // 狙う積み木を選ぶ
        //========================================

        private void ChooseTarget(DarumaCharacter self, CpuParams p, List<Vector3> opponents)
        {
            ForgetExpiredGiveUps();
            Vector3 pos = self.transform.position;

            // まっすぐの距離(道の長さの下限)で並べ、近い順に道を求める(道を求める回数を減らす)
            m_candidates.Clear();
            foreach (var block in TumikiBlock.All)
            {
                if (!IsCandidate(block)) continue;
                Vector3 bp = block.transform.position;
                float dist = FlatDistance(pos, bp);
                if (dist > p.SearchRadius) continue;
                if (!CanReachHeight(self, bp)) continue;
                m_candidates.Add(new Candidate { Block = block, Distance = dist, OpponentCost = OpponentCost(bp, p, opponents) });
            }
            m_candidates.Sort((a, b) => a.LowerBound.CompareTo(b.LowerBound));

            TumikiBlock best = null;
            float bestCost = float.MaxValue;
            float currentCost = float.MaxValue;
            int queries = 0;
            foreach (var c in m_candidates)
            {
                bool isCurrent = c.Block == m_target;
                // 道の長さはまっすぐの距離以上なので、下限が今の一番以上ならこれ以降は調べなくてよい
                // (ただし今狙っている積み木のコストは、乗り換えるか決めるのに必要)
                if (!isCurrent && (c.LowerBound >= bestCost || queries >= kMaxPathQueries)) continue;
                queries++;
                if (!TryGetPathLength(pos, c.Block.transform.position, p, m_queryPath, out float length)) continue;
                float cost = length + c.OpponentCost;
                if (isCurrent) currentCost = cost;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = c.Block;
                }
            }

            // 今の狙いを続けられるなら、十分に良い積み木があるときだけ乗り換える(迷わないように)
            if (m_target != null && currentCost < float.MaxValue && bestCost > currentCost - p.SwitchMargin) return;
            if (best != m_target) SetTarget(best, self, p);
        }

        private void SetTarget(TumikiBlock block, DarumaCharacter self, CpuParams p)
        {
            m_target = block;
            m_targetRb = block != null ? block.GetComponent<Rigidbody>() : null;
            m_chaseTime = 0.0f;
            m_closestRemaining = float.MaxValue;
            m_targetVelocity = Vector3.zero;
            m_targetLastSampleTime = -1.0f;
            m_jumpDistanceScale = 1.0f + Random.Range(-p.JumpDistanceError, p.JumpDistanceError);
            Vector2 offset = Random.insideUnitCircle * p.AimError;
            m_aimOffset = new Vector3(offset.x, 0.0f, offset.y);
            if (block != null) UpdateRoute(self, p);
        }

        private void ClearTarget()
        {
            m_target = null;
            m_targetRb = null;
            m_cornerCount = 0;
        }

        // 拾いに行ってよい積み木か(落ちている・攻撃判定なしで動いている積み木。諦めたものは除く)
        private bool IsCandidate(TumikiBlock block)
        {
            if (block == null || !block.isActiveAndEnabled || !block.CanBeAffected) return false;
            if (block.CurrentState == TumikiBlock.State.Flying && block.IsHarmful) return false;
            return !m_givenUp.ContainsKey(block);
        }

        // 相手の近くにある積み木ほどコストを足す
        private static float OpponentCost(Vector3 blockPos, CpuParams p, List<Vector3> opponents)
        {
            if (p.OpponentAvoidRadius <= 0.0f) return 0.0f;
            float cost = 0.0f;
            foreach (var o in opponents)
            {
                float d = FlatDistance(o, blockPos);
                if (d < p.OpponentAvoidRadius) cost += p.OpponentPenalty * (1.0f - d / p.OpponentAvoidRadius);
            }
            return cost;
        }

        // 積み木まで歩いて行ける道の長さ。積み木の下が奈落・バンパーのすぐそば・歩いて行けないときは false
        private static bool TryGetPathLength(Vector3 from, Vector3 blockPos, CpuParams p, NavMeshPath path, out float length)
        {
            length = 0.0f;
            if (!CpuSensor.HasGround(blockPos, p.GroundProbeDepth)) return false;
            if (CpuNavMesh.IsNearHazard(blockPos, kHazardClearance)) return false;
            return CpuNavMesh.TryFindPath(from, blockPos, kGoalTolerance, path, out length);
        }

        private void ForgetExpiredGiveUps()
        {
            m_expired.Clear();
            foreach (var pair in m_givenUp)
            {
                if (pair.Key == null || Time.time >= pair.Value) m_expired.Add(pair.Key);
            }
            foreach (var block in m_expired) m_givenUp.Remove(block);
        }

        //========================================
        // 道をたどって近づき、跳ぶ
        //========================================

        // 今の位置から狙っている積み木までの道を求め直す(道が見つからなければ、まっすぐ向かう)
        private void UpdateRoute(DarumaCharacter self, CpuParams p)
        {
            m_repathTimer = p.RepathInterval;
            m_cornerCount = 0;
            m_cornerIndex = 0;
            if (m_target == null) return;
            if (!CpuNavMesh.TryFindPath(self.transform.position, m_target.transform.position, kGoalTolerance, m_path, out float remaining)) return;
            m_cornerCount = m_path.GetCornersNonAlloc(m_corners);
            m_cornerIndex = Mathf.Min(1, m_cornerCount);

            // 遠回りの道でも、進んでいる間は諦めない
            if (remaining < m_closestRemaining - kProgressDistance)
            {
                m_closestRemaining = remaining;
                m_chaseTime = 0.0f;
            }
        }

        private void Steer(DarumaCharacter self, CpuParams p, ref DarumaCommand cmd)
        {
            Vector3 pos = self.transform.position;
            float speed = Mathf.Max(0.1f, self.MoveSpeed);

            // 道の曲がり角を順にたどる。最後の曲がり角(積み木のそば)の手前までは、そこへ向かって歩くだけ
            while (m_cornerIndex < m_cornerCount - 1 && FlatDistance(pos, m_corners[m_cornerIndex]) < kCornerReach) m_cornerIndex++;
            if (m_cornerIndex < m_cornerCount - 1)
            {
                AimPoint = m_corners[m_cornerIndex];
                Walk(pos, m_corners[m_cornerIndex], speed, p, ref cmd);
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
                    float lead = FlatDistance(pos, predicted) / speed + reach;
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
                return;
            }

            // 積み木が落ちてくる途中なら、触れない距離で待つ
            if (!settled && dist <= kMinJumpDistance + 0.3f)
            {
                cmd.Move = Vector2.zero;
                return;
            }

            Walk(pos, aim, speed, p, ref cmd);
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

        // destination へ歩く。進む先(destination より手前)が奈落なら止まる
        // (拾えないまま時間が経てば諦めて別の積み木を探す)
        private static void Walk(Vector3 pos, Vector3 destination, float speed, CpuParams p, ref DarumaCommand cmd)
        {
            Vector3 to = destination - pos;
            to.y = 0.0f;
            float dist = to.magnitude;
            if (dist < 0.001f)
            {
                cmd.Move = Vector2.zero;
                return;
            }
            Vector3 dir = to / dist;
            // 曲がり角の先は調べない(道は曲がり角で向きを変えるので、その先が奈落でも落ちない)
            float lookAhead = Mathf.Min(p.EdgeLookAhead + speed * 0.1f, dist);
            bool safe = CpuSensor.HasGround(pos + dir * lookAhead, p.GroundProbeDepth);
            cmd.Move = safe ? new Vector2(dir.x, dir.z) : Vector2.zero;
        }

        // 跳んでから、落ちてくる途中で積み木の上面の高さに届くまでの時間(秒)
        private static float ReachTime(DarumaCharacter self, Vector3 blockPos)
        {
            JumpParams jump = self.JumpParams;
            float fall = Mathf.Max(0.0f, jump.Height - BlockTopFromFeet(self, blockPos));
            float fallGravity = jump.Gravity * jump.FallGravityMultiplier;
            return jump.TimeToApex + Mathf.Sqrt(2.0f * fall / fallGravity);
        }

        // ジャンプで積み木の上面より上まで跳べるか(高い所にある積み木は狙わない)
        private static bool CanReachHeight(DarumaCharacter self, Vector3 blockPos)
        {
            return BlockTopFromFeet(self, blockPos) < self.JumpParams.Height;
        }

        // 足元から見た積み木の上面の高さ
        private static float BlockTopFromFeet(DarumaCharacter self, Vector3 blockPos)
        {
            return blockPos.y + TumikiBlock.kHeight * 0.5f - self.transform.position.y;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0.0f;
            b.y = 0.0f;
            return Vector3.Distance(a, b);
        }
    }
}
