using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // CPU の行動「積む」:近くの積み木を選び、落下しながら触れるようにジャンプして拾う
    //  ・狙うのは落ちている積み木と、攻撃判定のない(ゆっくり)動いている積み木
    //  ・相手の近く・途中に壁がある積み木は後回しにし、途中に奈落がある積み木は狙わない
    //  ・ジャンプの高さ・重力と移動速度から、落ちてくるときにちょうど積み木に届く距離で跳ぶ
    public class CpuCollect
    {
        // 狙う積み木の候補(距離と相手からのコストだけで並べたもの)
        struct Candidate
        {
            public TumikiBlock Block;
            public float Cost;
        }

        // 積み木の上下の速さがこれ未満なら、置かれている(落ちてくる途中ではない)とみなす
        const float kSettledSpeed = 0.5f;
        // ジャンプの前に積み木に触れて押してしまわないよう、最低でもこの距離からは跳ぶ(体の半径+積み木の半径+余裕)
        const float kMinJumpDistance = DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.1f;
        // 動いている積み木の位置を先読みする最大の時間(秒)
        const float kMaxLeadTime = 2.0f;
        // 空中で狙う位置の真上に止まるための減速を始める時間(秒)
        const float kAirBrakeTime = 0.1f;

        TumikiBlock m_target;
        Rigidbody m_targetRb;
        float m_thinkTimer;
        float m_chaseTime;
        // 狙いごとの腕前のぶれ(狙い直すたびに決め直す)
        float m_jumpDistanceScale = 1.0f;
        Vector3 m_aimOffset;
        // 諦めた積み木と、また狙ってよくなる時刻
        readonly Dictionary<TumikiBlock, float> m_givenUp = new Dictionary<TumikiBlock, float>();
        readonly List<Candidate> m_candidates = new List<Candidate>();
        readonly List<TumikiBlock> m_expired = new List<TumikiBlock>();

        // 狙っている積み木(なければnull)
        public TumikiBlock Target => m_target;
        // 着地を狙っている位置(デバッグ表示用)
        public Vector3 AimPoint { get; private set; }

        // 積み木を集める操作を cmd に書く。集める必要がない・拾える積み木がないときは false(cmd は変えない)
        // opponents: 相手の位置
        public bool Tick(DarumaCharacter self, CpuParams p, List<Vector3> opponents, float dt, ref DarumaCommand cmd)
        {
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

            // 長く追っても拾えない積み木は、しばらく諦める
            m_chaseTime += dt;
            if (m_chaseTime > p.GiveUpTime)
            {
                m_givenUp[m_target] = Time.time + p.GiveUpCooldown;
                ClearTarget();
                return false;
            }

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

            // 距離と相手からのコストで並べ、近い順に足場・壁を調べる(レイキャストの数を減らす)
            m_candidates.Clear();
            foreach (var block in TumikiBlock.All)
            {
                if (!IsCandidate(block)) continue;
                Vector3 bp = block.transform.position;
                float dist = FlatDistance(pos, bp);
                if (dist > p.SearchRadius) continue;
                if (!CanReachHeight(self, bp)) continue;
                m_candidates.Add(new Candidate { Block = block, Cost = dist + OpponentCost(bp, p, opponents) });
            }
            m_candidates.Sort((a, b) => a.Cost.CompareTo(b.Cost));

            TumikiBlock best = null;
            float bestCost = float.MaxValue;
            float currentCost = float.MaxValue;
            foreach (var c in m_candidates)
            {
                // 壁のコストは足されるだけなので、並べたコストが今の一番以上ならこれ以降は調べなくてよい
                // (ただし今狙っている積み木のコストは、乗り換えるか決めるのに必要)
                if (c.Cost >= bestCost && c.Block != m_target) continue;
                if (!TryGetPathCost(pos, c.Block.transform.position, p, out float pathCost)) continue;
                float cost = c.Cost + pathCost;
                if (c.Block == m_target) currentCost = cost;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = c.Block;
                }
            }

            // 今の狙いを続けられるなら、十分に良い積み木があるときだけ乗り換える(迷わないように)
            if (m_target != null && currentCost < float.MaxValue && bestCost > currentCost - p.SwitchMargin) return;
            if (best != m_target) SetTarget(best, p);
        }

        private void SetTarget(TumikiBlock block, CpuParams p)
        {
            m_target = block;
            m_targetRb = block != null ? block.GetComponent<Rigidbody>() : null;
            m_chaseTime = 0.0f;
            m_jumpDistanceScale = 1.0f + Random.Range(-p.JumpDistanceError, p.JumpDistanceError);
            Vector2 offset = Random.insideUnitCircle * p.AimError;
            m_aimOffset = new Vector3(offset.x, 0.0f, offset.y);
        }

        private void ClearTarget()
        {
            m_target = null;
            m_targetRb = null;
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

        // 積み木までの道のコスト。途中や積み木の下に奈落があれば false
        private static bool TryGetPathCost(Vector3 from, Vector3 blockPos, CpuParams p, out float cost)
        {
            cost = 0.0f;
            if (!CpuSensor.HasGround(blockPos, p.GroundProbeDepth)) return false;
            if (!CpuSensor.HasGroundAlong(from, blockPos, p.PathSampleStep, p.GroundProbeDepth)) return false;
            if (CpuSensor.HasWallBetween(from, blockPos)) cost += p.WallPenalty;
            return true;
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
        // 近づいて跳ぶ
        //========================================

        private void Steer(DarumaCharacter self, CpuParams p, ref DarumaCommand cmd)
        {
            Vector3 pos = self.transform.position;
            Vector3 blockPos = m_target.transform.position;
            float speed = Mathf.Max(0.1f, self.MoveSpeed);
            float reach = ReachTime(self, blockPos);

            // 動いている積み木は、届くころの位置を狙う(届く時間は予想した位置までの距離で求め直す)
            Vector3 predicted = blockPos;
            if (self.IsGrounded)
            {
                for (int i = 0; i < 2; i++)
                {
                    float lead = FlatDistance(pos, predicted) / speed + reach;
                    predicted = m_target.PredictPosition(Mathf.Min(lead, kMaxLeadTime));
                }
            }
            else
            {
                predicted = m_target.PredictPosition(reach * 0.5f);
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

            // 進む先が奈落なら止まる(拾えないまま時間が経てば諦めて別の積み木を探す)
            Vector3 ahead = pos + new Vector3(dir.x, 0.0f, dir.y) * (p.EdgeLookAhead + speed * 0.1f);
            cmd.Move = CpuSensor.HasGround(ahead, p.GroundProbeDepth) ? dir : Vector2.zero;
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
