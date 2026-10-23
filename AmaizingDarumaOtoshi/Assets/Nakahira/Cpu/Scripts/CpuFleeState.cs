using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 状態「逃げる」:追ってくる相手(追っ手)から離れ、逃げ道の途中にある積み木を拾う
    //  入る:・道の距離 FleeEnterDistance 以内の生存者との段数の差が −FleeStackGap 以下
    //        ・幽霊がまっすぐの距離 FleeEnterDistance 以内にいる(一番の脅威なので、やりたさも高い)
    //        ・1段 → 0段になった瞬間だけ、FleeEnterDistance 以内の生存者との差が −1 以下(ZeroStackCheck)
    //  終わる:追っ手がいなくなったら(FleeExitDistance より遠くなった・生存者の追っ手との差が 0 以上になった)
    public class CpuFleeState : CpuState
    {
        // 逃げ先の候補:自分の周りの方向の数と距離(m)
        const int kPointDirections = 12;
        static readonly float[] kPointRadii = { 4.0f, 7.0f, 10.0f };
        // 逃げ先の候補をナビメッシュ上に寄せる範囲(m)
        const float kPointSampleRadius = 1.0f;
        // 逃げ先を選ぶとき、道の長さ 1m あたりに引く点
        const float kPathCostWeight = 0.3f;
        // 逃げ先は、追っ手から見て今の自分よりこの距離(m)以上遠い点だけ(近づく向きに逃げない)
        const float kPointAwayMargin = 0.5f;
        // 今の逃げ先を、これだけ(点)良い逃げ先が見つかるまで変えない(迷わないように)
        const float kPointKeepMargin = 1.0f;
        // 逃げ先にこの距離(m)まで着いたら止まる(次に選び直すまで)
        const float kArriveDistance = 0.5f;

        readonly List<MatchPlayer> m_pursuers = new List<MatchPlayer>();
        readonly CpuPickupMotor m_pickup = new CpuPickupMotor();
        readonly CpuPathFollower m_route = new CpuPathFollower();
        readonly List<CpuBlockSearch.Candidate> m_candidates = new List<CpuBlockSearch.Candidate>();
        NavMeshPath m_queryPath;
        Vector3 m_point;
        bool m_hasPoint;
        float m_thinkTimer;
        CpuContext m_context;   // 候補のコストを求めるときに使う(毎回ラムダを作らないように)
        readonly System.Func<TumikiBlock, float> m_extraCost;

        public CpuFleeState()
        {
            m_extraCost = ExtraCost;
        }

        public override string Name => "逃げる";

        // 1段 → 0段になった瞬間だけ true(決める側が立てる)
        public bool ZeroStackCheck { get; set; }
        // 追っ手(デバッグ表示用)
        public IReadOnlyList<MatchPlayer> Pursuers => m_pursuers;

        public override float Evaluate(CpuContext c)
        {
            float urgency = 0.0f;
            foreach (var o in c.Perception.Opponents) urgency = Mathf.Max(urgency, EnterUrgency(c, o));
            // 逃げている最中は、追っ手がいる間は続けたい
            if (m_pursuers.Count > 0) urgency = Mathf.Max(urgency, c.Params.StackThreatUrgency);
            return urgency;
        }

        // 幽霊から逃げるときは、疲れていても気づきやすい(一番の脅威なので、しきい値が高い)
        public override float Threshold(CpuContext c)
        {
            foreach (var o in c.Perception.Opponents)
            {
                if (o.IsGhost && EnterUrgency(c, o) > 0.0f) return c.Params.GhostFleeThreshold;
            }
            return c.Params.FleeThreshold;
        }

        public override void Enter(CpuContext c)
        {
            m_pursuers.Clear();
            AddNewPursuers(c);
            m_thinkTimer = 0.0f;
            m_hasPoint = false;
            m_pickup.Clear();
            m_route.Clear();
        }

        public override void Exit(CpuContext c)
        {
            m_pursuers.Clear();
            m_hasPoint = false;
            m_pickup.Clear();
            m_route.Clear();
        }

        public override bool Tick(CpuContext c, ref DarumaCommand cmd)
        {
            UpdatePursuers(c);
            if (m_pursuers.Count == 0) return false;

            m_thinkTimer -= c.DeltaTime;
            if (m_thinkTimer <= 0.0f)
            {
                m_thinkTimer = c.Params.ThinkInterval;
                if (m_queryPath == null) m_queryPath = new NavMeshPath();
                ChooseBlock(c);
                if (m_pickup.Target == null) ChoosePoint(c);
            }

            // 逃げ道の途中の積み木を拾う
            if (m_pickup.Target != null)
            {
                if (m_pickup.Tick(c, ref cmd) == CpuPickupMotor.Status.Running) return true;
                m_thinkTimer = 0.0f;
            }

            if (m_hasPoint) RunToPoint(c, ref cmd);
            return true;
        }

        //========================================
        // 追っ手
        //========================================

        // 逃げに入りたい強さ(この相手が逃げる理由にならなければ 0)
        private float EnterUrgency(CpuContext c, CpuOpponent o)
        {
            CpuParams p = c.Params;
            if (o.ThreatDistance > p.FleeEnterDistance) return 0.0f;
            if (o.IsGhost) return p.GhostThreatUrgency;
            if (o.StackDiff <= -p.FleeStackGap) return p.StackThreatUrgency;
            if (ZeroStackCheck && o.StackDiff < 0) return p.StackThreatUrgency;
            return 0.0f;
        }

        private void AddNewPursuers(CpuContext c)
        {
            foreach (var o in c.Perception.Opponents)
            {
                if (EnterUrgency(c, o) > 0.0f && !m_pursuers.Contains(o.Player)) m_pursuers.Add(o.Player);
            }
        }

        // 逃げ切った・追いついた追っ手を外し、新しく近づいてきた追っ手を加える
        private void UpdatePursuers(CpuContext c)
        {
            CpuParams p = c.Params;
            m_pursuers.RemoveAll(player =>
            {
                CpuOpponent o = c.Perception.Find(player);
                if (o == null) return true;                                     // 復活待ち・脱落
                if (o.ThreatDistance > p.FleeExitDistance) return true;         // 逃げ切った
                return !o.IsGhost && o.StackDiff >= 0;                          // 積み木を拾って追いついた
            });
            AddNewPursuers(c);
        }

        //========================================
        // 逃げ道の途中の積み木
        //========================================

        // 拾ってよい積み木(寄り道しても追っ手に近づかず、追っ手より余裕を持って着ける)のうち、道が一番短いもの
        private void ChooseBlock(CpuContext c)
        {
            // 取り合いで譲ることになった積み木は、しばらく狙わない
            TumikiBlock current = m_pickup.Target;
            if (current != null && CpuBlockSearch.ShouldYield(c, current.transform.position)) c.GiveUp(current, c.Params.YieldCooldown);

            // 今狙っている積み木がまだ安全なら続ける
            if (current != null && CpuBlockSearch.IsCollectable(current) && !c.IsGivenUp(current)
                && CpuBlockSearch.TryGetPathLength(c.Position, current.transform.position, c.Params, m_queryPath, out float currentLength)
                && IsSafeDetour(c, current.transform.position, currentLength))
            {
                return;
            }

            m_context = c;
            CpuBlockSearch.Gather(c, m_candidates, m_extraCost);
            m_context = null;

            TumikiBlock best = null;
            float bestLength = float.MaxValue;
            int queries = 0;
            foreach (var cand in m_candidates)
            {
                if (cand.LowerBound >= bestLength || queries >= CpuBlockSearch.MaxPathQueries) break;
                Vector3 bp = cand.Block.transform.position;
                // 道を求める前に、まっすぐの距離で分かる条件を調べる(道の長さはまっすぐの距離以上)
                if (!IsSafeDetour(c, bp, cand.Distance)) continue;
                queries++;
                if (!CpuBlockSearch.TryGetPathLength(c.Position, bp, c.Params, m_queryPath, out float length)) continue;
                if (!IsSafeDetour(c, bp, length)) continue;
                if (length < bestLength)
                {
                    bestLength = length;
                    best = cand.Block;
                }
            }

            if (best == null) m_pickup.Clear();
            else m_pickup.SetTarget(best, c.Params);
        }

        // 寄り道してよい積み木か:どの追っ手から見ても今の自分より遠く(追っ手に近づかない)、
        // 自分が道の長さ length で着くまでに、追っ手が(まっすぐ来ても)FleeSafetyMargin 以上遅れる
        private bool IsSafeDetour(CpuContext c, Vector3 blockPos, float length)
        {
            foreach (var player in m_pursuers)
            {
                CpuOpponent o = c.Perception.Find(player);
                if (o == null) continue;
                float d = CpuContext.FlatDistance(o.Position, blockPos);
                if (d < o.FlatDistance) return false;
                if (d - length < c.Params.FleeSafetyMargin) return false;
            }
            return true;
        }

        // 候補の追加のコスト。取り合いで譲るべき積み木は除く(無限大)
        private float ExtraCost(TumikiBlock block)
        {
            return CpuBlockSearch.ShouldYield(m_context, block.transform.position) ? float.PositiveInfinity : 0.0f;
        }

        //========================================
        // 逃げ先
        //========================================

        // 自分の周りの点から、追っ手から遠く、道が短い点を選ぶ(追っ手に近づく向きの点は避ける)
        private void ChoosePoint(CpuContext c)
        {
            Vector3 pos = c.Position;
            bool bestValid = false;
            float bestScore = float.MinValue;
            Vector3 best = pos;

            for (int r = 0; r < kPointRadii.Length; r++)
            {
                for (int k = 0; k < kPointDirections; k++)
                {
                    float angle = (k + 0.5f * r) * Mathf.PI * 2.0f / kPointDirections;
                    Vector3 sample = pos + new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * kPointRadii[r];
                    if (!NavMesh.SamplePosition(sample, out NavMeshHit hit, kPointSampleRadius, NavMesh.AllAreas)) continue;
                    if (!ScorePoint(c, hit.position, out float score, out bool valid)) continue;
                    if ((valid && !bestValid) || (valid == bestValid && score > bestScore))
                    {
                        bestValid = valid;
                        bestScore = score;
                        best = hit.position;
                    }
                }
            }
            if (bestScore == float.MinValue) return;

            // 今の逃げ先がまだ良ければ続ける(迷わないように)
            if (m_hasPoint && ScorePoint(c, m_point, out float currentScore, out bool currentValid)
                && (currentValid || !bestValid) && currentScore >= bestScore - kPointKeepMargin)
            {
                return;
            }
            m_point = best;
            m_hasPoint = true;
            m_route.SetDestination(best, kArriveDistance);
        }

        // 逃げ先の点の良さ。歩いて行けなければ false
        // valid:どの追っ手から見ても、今の自分より遠い点か
        private bool ScorePoint(CpuContext c, Vector3 point, out float score, out bool valid)
        {
            score = 0.0f;
            valid = true;
            if (!CpuNavMesh.TryFindPath(c.Position, point, kArriveDistance, m_queryPath, out float length)) return false;
            float nearest = float.MaxValue;
            foreach (var player in m_pursuers)
            {
                CpuOpponent o = c.Perception.Find(player);
                if (o == null) continue;
                float d = CpuContext.FlatDistance(o.Position, point);
                nearest = Mathf.Min(nearest, d);
                if (d < o.FlatDistance + kPointAwayMargin) valid = false;
            }
            score = nearest - kPathCostWeight * length;
            return true;
        }

        private void RunToPoint(CpuContext c, ref DarumaCommand cmd)
        {
            Vector3 pos = c.Position;
            m_route.Update(pos, c.Params, c.DeltaTime, out _);
            if (!m_route.IsOnFinalLeg(pos, out Vector3 next))
            {
                CpuPathFollower.Walk(pos, next, c.Self.MoveSpeed, c.Params, ref cmd);
                return;
            }
            if (CpuContext.FlatDistance(pos, m_point) < kArriveDistance)
            {
                cmd.Move = Vector2.zero;
                return;
            }
            CpuPathFollower.Walk(pos, m_point, c.Self.MoveSpeed, c.Params, ref cmd);
        }

        public override void DrawGizmos(CpuContext c)
        {
            // 追っ手を赤い線で結ぶ
            Gizmos.color = Color.red;
            foreach (var player in m_pursuers)
            {
                CpuOpponent o = c.Perception.Find(player);
                if (o != null) Gizmos.DrawLine(c.Position + Vector3.up, o.Position + Vector3.up);
            }
            Gizmos.color = Color.magenta;
            if (m_pickup.Target != null)
            {
                m_pickup.DrawGizmos(c.Self);
                return;
            }
            if (!m_hasPoint) return;
            m_route.DrawGizmos(c.Position);
            Gizmos.DrawWireSphere(m_point, 0.3f);
        }
    }
}
