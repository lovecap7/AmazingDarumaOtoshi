using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 拾いに行く積み木を探すための共通の問い合わせ(積む・逃げるの状態が使う)
    public static class CpuBlockSearch
    {
        // 拾いに行く候補
        public struct Candidate
        {
            public TumikiBlock Block;
            public float Distance;      // まっすぐの距離(道の長さはこれ以上になる)
            public float ExtraCost;
            public float LowerBound => Distance + ExtraCost;
        }

        // 1回選び直すときに道を求める積み木の最大数(近い順。重くならないように)
        public const int MaxPathQueries = 12;
        // 触れると危ないギミック(バンパー)からこの距離(m)以内の積み木は、拾いに行くと触れてしまうので狙わない(体の半径+余裕)
        const float kHazardClearance = DarumaCharacter.kBodyRadius + 0.3f;

        // 取り合いで、積み木までの距離の差がこれ(m)未満なら同じ距離とみなす
        const float kTieDistance = 0.3f;

        static readonly Vector3[] s_corners = new Vector3[64];

        // 拾える積み木か(落ちている・攻撃判定なしで動いている積み木)
        public static bool IsCollectable(TumikiBlock block)
        {
            if (block == null || !block.isActiveAndEnabled || !block.CanBeAffected) return false;
            return !(block.CurrentState == TumikiBlock.State.Flying && block.IsHarmful);
        }

        // 拾いに行く候補(拾える・諦めていない・探す範囲内・跳んで届く高さ)を集め、まっすぐの距離 + extraCost の小さい順に並べる
        // extraCost が無限大の積み木は除く
        public static void Gather(CpuContext c, List<Candidate> results, Func<TumikiBlock, float> extraCost)
        {
            results.Clear();
            Vector3 pos = c.Position;
            foreach (var block in TumikiBlock.All)
            {
                if (!IsCollectable(block) || c.IsGivenUp(block)) continue;
                Vector3 bp = block.transform.position;
                float dist = CpuContext.FlatDistance(pos, bp);
                if (dist > c.Params.SearchRadius) continue;
                if (!CanReachHeight(c.Self, bp)) continue;
                float extra = extraCost != null ? extraCost(block) : 0.0f;
                if (float.IsPositiveInfinity(extra)) continue;
                results.Add(new Candidate { Block = block, Distance = dist, ExtraCost = extra });
            }
            results.Sort((a, b) => a.LowerBound.CompareTo(b.LowerBound));
        }

        // 積み木まで歩いて行ける道の長さ。積み木の下が奈落・バンパーのすぐそば・歩いて行けないときは false
        public static bool TryGetPathLength(Vector3 from, Vector3 blockPos, CpuParams p, NavMeshPath path, out float length)
        {
            length = 0.0f;
            if (!CpuSensor.HasGround(blockPos, p.GroundProbeDepth)) return false;
            if (CpuNavMesh.IsNearHazard(blockPos, kHazardClearance)) return false;
            return CpuNavMesh.TryFindPath(from, blockPos, CpuPickupMotor.GoalTolerance, path, out length);
        }

        // 取り合いで譲るべき積み木か。積み木から半径 ContestRadius 以内にいる生存者それぞれと段数を比べる
        //  ・有利(差 ≥ AdvantageMargin)な相手には譲らない(強気に取りに行く)
        //  ・不利(差 < 0)な相手がいれば譲る
        //  ・同じ段数(差 0〜AdvantageMargin 未満)なら、積み木に近い方が取る(ほぼ同じ距離ならプレイヤー番号の小さい方が取る)
        //    (同じ段数の2人が同時に譲り合って、近づいては離れるのを繰り返さないように)
        public static bool ShouldYield(CpuContext c, Vector3 blockPos)
        {
            float myDistance = CpuContext.FlatDistance(c.Position, blockPos);
            int myIndex = c.Player != null ? c.Player.Index : int.MaxValue;
            foreach (var o in c.Perception.Opponents)
            {
                if (o.IsGhost) continue;
                float theirDistance = CpuContext.FlatDistance(o.Position, blockPos);
                if (theirDistance > c.Params.ContestRadius) continue;
                if (o.StackDiff >= c.Params.AdvantageMargin) continue;
                if (o.StackDiff < 0) return true;

                // 同じ段数:近い方が取る
                if (theirDistance < myDistance - kTieDistance) return true;
                if (myDistance < theirDistance - kTieDistance) continue;
                if (o.Player.Index < myIndex) return true;
            }
            return false;
        }

        // 危険な相手(近づかれたら逃げる相手):幽霊、または段数が FleeStackGap 以上多い生存者
        public static bool IsThreat(CpuContext c, CpuOpponent o)
        {
            return o.IsGhost || o.StackDiff <= -c.Params.FleeStackGap;
        }

        // 道が危険な相手から clearance(m)より離れたところだけを通るか(積み木を取りに行って、逃げることにならないように)
        // 今すでに clearance より近くにいる相手からは、今より近づかなければよい
        public static bool AvoidsThreats(CpuContext c, NavMeshPath path, float clearance)
        {
            int count = path.GetCornersNonAlloc(s_corners);
            foreach (var o in c.Perception.Opponents)
            {
                if (!IsThreat(c, o)) continue;
                float limit = Mathf.Min(clearance, o.FlatDistance - 0.1f);
                for (int i = 1; i < count; i++)
                {
                    if (FlatDistanceToSegment(o.Position, s_corners[i - 1], s_corners[i]) < limit) return false;
                }
            }
            return true;
        }

        // 点 p から線分 a-b までの水平の距離
        private static float FlatDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var p2 = new Vector2(p.x, p.z);
            var a2 = new Vector2(a.x, a.z);
            Vector2 ab = new Vector2(b.x, b.z) - a2;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / ab.sqrMagnitude) : 0.0f;
            return Vector2.Distance(p2, a2 + ab * t);
        }

        // 幽霊の近くにある積み木ほど大きいコスト(幽霊は一番の脅威なので、近くの積み木は後回しにする)
        public static float GhostCost(CpuContext c, Vector3 blockPos)
        {
            CpuParams p = c.Params;
            if (p.GhostAvoidRadius <= 0.0f) return 0.0f;
            float cost = 0.0f;
            foreach (var o in c.Perception.Opponents)
            {
                if (!o.IsGhost) continue;
                float d = CpuContext.FlatDistance(o.Position, blockPos);
                if (d < p.GhostAvoidRadius) cost += p.GhostAvoidPenalty * (1.0f - d / p.GhostAvoidRadius);
            }
            return cost;
        }

        // ジャンプで積み木の上面より上まで跳べるか(高い所にある積み木は狙わない)
        public static bool CanReachHeight(DarumaCharacter self, Vector3 blockPos)
        {
            return BlockTopFromFeet(self, blockPos) < self.JumpParams.Height;
        }

        // 足元から見た積み木の上面の高さ
        public static float BlockTopFromFeet(DarumaCharacter self, Vector3 blockPos)
        {
            return blockPos.y + TumikiBlock.kHeight * 0.5f - self.transform.position.y;
        }
    }
}
