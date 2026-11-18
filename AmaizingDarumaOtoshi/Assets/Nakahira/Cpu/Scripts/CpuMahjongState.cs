using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 状態「牌を集める」(Stage22「こたつの全自動卓」の牌あつめ。2026-11-18)
    //  ・場に牌があれば取りに行く。触れれば卓が拾う
    //    気づくのは床に着いてから HazardReaction + MahjongNoticeExtra 秒後(つよい 0.7・ふつう 0.9・よわい 1.2 秒)。
    //    降る予告・落ちている途中の牌は狙わない(人が見える前から落ちる所で待っているように見えたため。中平 2026-11-18)
    //  ・点数は MahjongScore(逃げる以外のどの状態より高い。中平が決めた 2026-11-18)。
    //    ただし必殺技を撃ちに近づきたいとき(状態「必殺技」が選ばれる場面)は、そちらを先にする
    //  ・危険な相手(幽霊・段数が FleeStackGap 以上多い生存者。積むと同じ)のそばを通る道の牌は狙わない(遠回りはしない)
    //  ・手持ちが上限なら選ばれない。消えるまでに着けない牌・ふたが開いている間の中央の牌は狙わない
    //  ・攻撃を受けると手持ちから1枚落とす(卓の仕様)。落とした牌は 0.7 秒拾えないが、拾えるようになれば同じように取りに行く
    public class CpuMahjongState : CpuState
    {
        // 1回選び直すときに道を求める牌の最大数(重くならないように)
        const int kMaxQueries = 4;
        // 消えるまでの残りが、着くまでの見込みよりこの秒数以上なければ狙わない
        const float kLifeMargin = 0.5f;
        // 落ちる所へ向かって待つとき、ここまで近づいたら止まる(m)
        const float kWaitDistance = 0.3f;

        readonly CpuPathFollower m_route = new CpuPathFollower();
        readonly List<Nakagawa.MahjongTable.CpuFieldTile> m_tiles = new List<Nakagawa.MahjongTable.CpuFieldTile>();
        readonly System.Func<CpuContext, bool> m_specialWants;
        NavMeshPath m_queryPath;
        object m_target;
        Vector3 m_targetPos;
        float m_thinkTimer;
        // 選び直した時刻(Evaluate と Tick で同じフレームに何度も道を求めないように)
        float m_lastChoose = float.NegativeInfinity;
        bool m_lastFound;

        public override string Name => "牌を集める";
        public override CpuPathFollower Route => m_target != null ? m_route : null;

        // specialWants:必殺技を撃ちに近づきたいか(そのときは状態「必殺技」より低い点数にする)
        public CpuMahjongState(System.Func<CpuContext, bool> specialWants)
        {
            m_specialWants = specialWants;
        }

        public override float Evaluate(CpuContext c)
        {
            if (!Choose(c)) return 0.0f;
            if (m_specialWants != null && m_specialWants(c)) return c.Params.SpecialStateScore - c.Params.StateStickiness - 0.01f;
            return c.Params.MahjongScore;
        }

        public override void Enter(CpuContext c)
        {
            m_thinkTimer = 0.0f;
            m_route.Clear();
        }

        public override void Exit(CpuContext c)
        {
            m_target = null;
            m_route.Clear();
        }

        public override bool Tick(CpuContext c, ref DarumaCommand cmd)
        {
            m_thinkTimer -= c.DeltaTime;
            if (m_thinkTimer <= 0.0f || m_target == null || !TargetStillThere(c))
            {
                m_thinkTimer = c.Params.ThinkInterval;
                if (!Choose(c, true)) return false;
            }

            Vector3 pos = c.Position;
            Vector3 goal = m_targetPos;
            goal.y = pos.y;
            if (CpuContext.FlatDistance(pos, goal) < kWaitDistance)
            {
                cmd.Move = Vector2.zero;
                return true;
            }
            m_route.MoveDestination(goal);
            m_route.Update(pos, c.Params, c.DeltaTime, out _);
            m_route.IsOnFinalLeg(pos, out Vector3 next);
            CpuPathFollower.Walk(pos, next, c.Self.MoveSpeed, c.Params, ref cmd);
            return true;
        }

        // 狙っている牌がまだ場にあれば、位置を読み直して true
        private bool TargetStillThere(CpuContext c)
        {
            var table = CpuMahjong.Table;
            if (table == null) return false;
            table.GetCpuFieldTiles(m_tiles);
            foreach (var t in m_tiles)
            {
                if (t.Key != m_target) continue;
                m_targetPos = t.Position;
                return true;
            }
            return false;
        }

        // 取りに行く牌を選ぶ。force でなければ、同じフレームに選び直したばかりならその結果を使う
        private bool Choose(CpuContext c, bool force = false)
        {
            if (!force && Time.time - m_lastChoose < 0.1f) return m_lastFound;
            m_lastChoose = Time.time;
            m_lastFound = ChooseTile(c);
            return m_lastFound;
        }

        private bool ChooseTile(CpuContext c)
        {
            var table = CpuMahjong.Table;
            if (table == null || c.Player == null) { m_target = null; return false; }
            if (table.CpuHandCount(c.Player) >= table.CpuMaxHand) { m_target = null; return false; }
            table.GetCpuFieldTiles(m_tiles);
            if (m_tiles.Count == 0) { m_target = null; return false; }
            if (m_queryPath == null) m_queryPath = new NavMeshPath();

            Vector3 pos = c.Position;
            float speed = Mathf.Max(0.5f, c.Self.MoveSpeed);
            bool lidOpen = table.CpuLidOpen;
            float noticeDelay = c.Params.HazardReaction + c.Params.MahjongNoticeExtra;
            // 近い(まっすぐの距離)順に調べる
            m_tiles.Sort((a, b) => CpuContext.FlatDistance(pos, a.Position).CompareTo(CpuContext.FlatDistance(pos, b.Position)));

            object best = null;
            Vector3 bestPos = Vector3.zero;
            float bestCost = float.MaxValue;
            float currentCost = float.MaxValue;
            Vector3 currentPos = Vector3.zero;
            int queries = 0;
            foreach (var t in m_tiles)
            {
                bool isCurrent = t.Key == m_target;
                float straight = CpuContext.FlatDistance(pos, t.Position);
                if (!isCurrent && (straight >= bestCost || queries >= kMaxQueries)) continue;
                // 床に着いてから気づく(降る予告・落ちている途中の牌は狙わない。人は落ちきるまで見えにくいので)
                if (t.SinceLanded < noticeDelay) continue;
                if (t.TimeLeft < straight / speed + kLifeMargin) continue;
                if (lidOpen && CpuMahjong.IsInLid(table, t.Position, DarumaCharacter.kBodyRadius)) continue;
                Vector3 goal = new Vector3(t.Position.x, table.CpuCenter.y, t.Position.z);
                queries++;
                if (!CpuNavMesh.TryFindPath(pos, goal, CpuPickupMotor.GoalTolerance, m_queryPath, out float length)) continue;
                if (t.TimeLeft < length / speed + kLifeMargin) continue;
                // 危険な相手のそばを通る道の牌は狙わない(取りに行って逃げる、を繰り返さないように。積むと同じ)
                if (!CpuBlockSearch.AvoidsThreats(c, m_queryPath, c.Params.CollectThreatClearance)) continue;
                if (isCurrent)
                {
                    currentCost = length;
                    currentPos = t.Position;
                }
                if (length < bestCost)
                {
                    bestCost = length;
                    best = t.Key;
                    bestPos = t.Position;
                }
            }

            // 今の狙いを続けられるなら、十分に近い牌があるときだけ乗り換える(迷わないように)
            if (m_target != null && currentCost < float.MaxValue && bestCost > currentCost - c.Params.SwitchMargin)
            {
                m_targetPos = currentPos;
                return true;
            }
            if (best == null)
            {
                m_target = null;
                return false;
            }
            if (best != m_target) m_route.SetDestination(bestPos, CpuPickupMotor.GoalTolerance);
            m_target = best;
            m_targetPos = bestPos;
            return true;
        }

        public override void DrawGizmos(CpuContext c)
        {
            if (m_target == null) return;
            Gizmos.color = new Color(1.0f, 0.95f, 0.6f);
            m_route.DrawGizmos(c.Position);
            Gizmos.DrawWireCube(m_targetPos + Vector3.up * 0.4f, new Vector3(0.6f, 0.8f, 0.4f));
        }
    }
}
