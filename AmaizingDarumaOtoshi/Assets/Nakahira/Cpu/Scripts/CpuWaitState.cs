using System.Collections.Generic;
using Nakagawa;
using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 状態「待つ」:盤面に拾える積み木がなく、攻めに行ける(有利な)相手もいないとき、積み木の供給を待つ
    //  ・供給の予告(TumikiSpawnWarning。積み木が出る少し前に床に出る印)が出たら、その近くへ先回りする
    //    (予告の上には乗らない。積み木やキャラクターが乗っていると生成が取りやめになるため)
    //  ・予告がなければ、供給エリア(TumikiSpawnArea)のすぐ外で、近くの敵が少ない所で待つ
    //    候補:各エリアの外周 8 方向 × 縁から 1/3/5m の点(エリアの中にキャラクターがいると供給が止まるので、中は選ばない)
    //    点数:近くの敵の重み(近いほど・危険な相手ほど重い)+ 縁からの距離 + 歩く道の長さ。一番小さい点を選ぶ
    //    今の場所より十分良い点があるときだけ移る(迷わないように)。敵が近づけば自然に場所を変える
    //  ・待つ場所に着いたら、周りを少しうろうろする/近くの相手の方を向く(棒立ちにならないように)
    //  ・積み木が出れば積むが、有利な相手がいれば攻めに行くが、点数で選ばれて引き継ぐ
    public class CpuWaitState : CpuState
    {
        // 予告の近くで止まる距離(m。予告の上に乗らず、出てきたらすぐ跳んで拾える距離)
        const float kWarningStandOff = 1.3f;
        // 待つ場所の候補:エリアの縁(体の半径 + この距離 m だけ外)から、さらに外へずらす距離(m)
        const float kAreaMargin = 1.0f;
        static readonly float[] kSpotOffsets = { 0.0f, 2.0f, 4.0f };
        // 候補を作る向きの数(エリアの中心から)
        const int kSpotDirections = 8;
        // 縁を探すとき、エリアの中心から外へ進む刻み(m)と、最大の距離(m)
        const float kOutsideStep = 0.25f;
        const float kOutsideMaxDistance = 20.0f;
        // 候補をナビメッシュ上に寄せる範囲(m)
        const float kSampleRadius = 1.0f;
        // 候補がバンパーなどの危険な床からこの距離(m)以内なら使わない
        const float kHazardClearance = DarumaCharacter.kBodyRadius + 0.3f;
        // 待つ場所にこの距離(m)まで着いたら着いたとみなす
        const float kArriveDistance = 0.6f;
        // 1回選び直すときに、道の長さを調べる候補の最大数(重くならないように)
        const int kMaxPathQueries = 6;
        // 供給エリアを探し直す間隔(秒。ステージの切り替えで変わる)
        const float kAreaScanInterval = 1.0f;
        // 道が供給エリアを通るか調べる刻み(m)
        const float kCrossCheckStep = 0.5f;
        // うろうろ先を探す回数
        const int kWanderAttempts = 6;
        // 相手の方を向くための、ごく小さい移動入力
        const float kTurnInput = 0.05f;

        struct Spot
        {
            public Vector3 Position;
            public float Offset;     // エリアの縁からの距離(m)
            public float Cost;       // 道の長さを除いた点数(小さいほど良い)
        }

        readonly CpuPathFollower m_route = new CpuPathFollower();
        readonly List<TumikiSpawnArea> m_areas = new List<TumikiSpawnArea>();
        readonly List<Spot> m_spots = new List<Spot>();         // エリアから作った候補(エリアを探し直すたびに作り直す)
        readonly List<Spot> m_ranked = new List<Spot>();        // 点数を付けた候補(選び直すたびに作り直す)
        NavMeshPath m_queryPath;   // MonoBehaviour の初期化中には作れないので、使うときに作る
        readonly Vector3[] m_corners = new Vector3[32];
        float m_areaScanTime = -100.0f;
        float m_thinkTimer;

        bool m_hasGoal;
        bool m_goalIsWarning;
        Vector3 m_goal;
        float m_stopDistance;
        float m_goalCost;          // 今の待つ場所の点数(移るか決めるのに使う)

        // 待つ場所に着いたか(着いたあとは、うろうろする範囲の中にいる間は着いたまま)
        bool m_arrived;
        // うろうろ
        bool m_wandering;
        Vector3 m_wanderTarget;
        float m_nextWanderTime;

        public override string Name => "待つ";
        public override CpuPathFollower Route => m_route;

        public override float Evaluate(CpuContext c)
        {
            return CpuBlockSearch.NothingToCollect(c) ? c.Params.WaitScore : 0.0f;
        }

        public override void Enter(CpuContext c)
        {
            m_thinkTimer = 0.0f;
            m_hasGoal = false;
            m_wandering = false;
            m_route.Clear();
        }

        public override void Exit(CpuContext c)
        {
            m_hasGoal = false;
            m_wandering = false;
            m_route.Clear();
        }

        public override bool Tick(CpuContext c, ref DarumaCommand cmd)
        {
            if (!CpuBlockSearch.NothingToCollect(c)) return false;

            m_thinkTimer -= c.DeltaTime;
            if (m_thinkTimer <= 0.0f)
            {
                m_thinkTimer = c.Params.ThinkInterval;
                ChooseGoal(c);
            }
            if (!m_hasGoal) return true;

            Vector3 pos = c.Position;
            // 着くまでは待つ場所まで行く。着いたあとは、うろうろする範囲の中にいる間は着いたままとみなす
            float dist = CpuContext.FlatDistance(pos, m_goal);
            if (dist <= m_stopDistance) m_arrived = true;
            else if (dist > m_stopDistance + c.Params.WaitWanderRadius + 0.5f) m_arrived = false;
            if (!m_arrived)
            {
                // 待つ場所へ向かう
                m_wandering = false;
                m_route.Update(pos, c.Params, c.DeltaTime, out _);
                m_route.IsOnFinalLeg(pos, out Vector3 next);
                CpuPathFollower.Walk(pos, next, c.Self.MoveSpeed, c.Params, ref cmd);
                return true;
            }

            // 着いた:予告の前ではじっと待つ(出たらすぐ拾う)。エリアの外で待つときは、うろうろする
            if (m_goalIsWarning)
            {
                FaceNearestOpponent(c, ref cmd);
                return true;
            }
            Idle(c, ref cmd);
            return true;
        }

        //========================================
        // 待つ場所を決める
        //========================================

        private void ChooseGoal(CpuContext c)
        {
            if (m_queryPath == null) m_queryPath = new NavMeshPath();
            if (TryChooseWarning(c, out Vector3 warning))
            {
                SetGoal(warning, kWarningStandOff, true, 0.0f);
                return;
            }
            if (TryChooseSpot(c, out Vector3 spot, out float cost))
            {
                SetGoal(spot, kArriveDistance, false, cost);
                return;
            }
            m_hasGoal = false;
        }

        private void SetGoal(Vector3 goal, float stopDistance, bool isWarning, float cost)
        {
            bool moved = !m_hasGoal || CpuContext.FlatDistance(goal, m_goal) > 0.5f;
            if (moved)
            {
                m_route.SetDestination(goal, stopDistance);
                m_arrived = false;
                m_wandering = false;
                m_nextWanderTime = Time.time;
            }
            m_goal = goal;
            m_stopDistance = stopDistance;
            m_goalIsWarning = isWarning;
            m_goalCost = cost;
            m_hasGoal = true;
        }

        // 一番近い(道の長さ)、行ってよい予告
        private bool TryChooseWarning(CpuContext c, out Vector3 best)
        {
            best = Vector3.zero;
            float bestLength = float.MaxValue;
            foreach (var w in TumikiSpawnWarning.Active)
            {
                if (w == null || !w.isActiveAndEnabled) continue;
                Vector3 wp = w.Position;
                if (!CpuBlockSearch.CanReachHeight(c.Self, wp)) continue;
                if (CpuBlockSearch.ShouldYield(c, wp)) continue;
                if (!CpuNavMesh.TryFindPath(c.Position, wp, CpuPickupMotor.GoalTolerance, m_queryPath, out float length)) continue;
                if (!CpuBlockSearch.AvoidsThreats(c, m_queryPath, c.Params.CollectThreatClearance)) continue;
                if (length < bestLength)
                {
                    bestLength = length;
                    best = wp;
                }
            }
            return bestLength < float.MaxValue;
        }

        // 供給エリアの外の候補のうち、点数(敵の重み + 縁からの距離 + 道の長さ)が一番小さい点
        // 今の待つ場所より WaitSwitchMargin 以上良くなければ、今の場所のまま
        private bool TryChooseSpot(CpuContext c, out Vector3 best, out float bestCost)
        {
            ScanAreas();
            CpuParams p = c.Params;
            Vector3 pos = c.Position;

            // 道の長さを除いた点数を付けて、小さい順に並べる(道の長さの下限として、まっすぐの距離を足しておく)
            m_ranked.Clear();
            foreach (var s in m_spots)
            {
                Spot r = s;
                r.Cost = Danger(c, s.Position) + s.Offset * p.WaitSupplyCost;
                m_ranked.Add(r);
            }
            m_ranked.Sort((a, b) => (a.Cost + CpuContext.FlatDistance(pos, a.Position) * p.WaitTravelCost)
                .CompareTo(b.Cost + CpuContext.FlatDistance(pos, b.Position) * p.WaitTravelCost));

            best = Vector3.zero;
            bestCost = float.MaxValue;
            int queries = 0;
            foreach (var s in m_ranked)
            {
                float lowerBound = s.Cost + CpuContext.FlatDistance(pos, s.Position) * p.WaitTravelCost;
                if (lowerBound >= bestCost || queries >= kMaxPathQueries) break;
                queries++;
                if (!CpuNavMesh.TryFindPath(pos, s.Position, kArriveDistance, m_queryPath, out float length)) continue;
                if (!CpuBlockSearch.AvoidsThreats(c, m_queryPath, p.CollectThreatClearance)) continue;
                float cost = s.Cost + length * p.WaitTravelCost;
                // 道が供給エリアの中を通るなら減点(通っている間はそのエリアの供給が止まる)
                if (CrossesArea(m_queryPath)) cost += p.WaitCrossAreaCost;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = s.Position;
                }
            }
            if (bestCost == float.MaxValue) return false;

            // 今の待つ場所(エリアの外で待っているとき)の点数を付け直し、十分良くなければ移らない
            if (m_hasGoal && !m_goalIsWarning && CpuContext.FlatDistance(best, m_goal) > 0.5f)
            {
                float current = CurrentGoalCost(c);
                if (current < float.MaxValue && bestCost > current - p.WaitSwitchMargin)
                {
                    best = m_goal;
                    bestCost = current;
                }
            }
            return true;
        }

        // 今の待つ場所の点数(候補の中から同じ点を探す。見つからなければ無限大)
        private float CurrentGoalCost(CpuContext c)
        {
            foreach (var s in m_ranked)
            {
                if (CpuContext.FlatDistance(s.Position, m_goal) > 0.1f) continue;
                return s.Cost + CpuContext.FlatDistance(c.Position, m_goal) * c.Params.WaitTravelCost;
            }
            return float.MaxValue;
        }

        // point の近くの敵の重み(近いほど重い。WaitDangerRadius より遠い相手は数えない)
        // 幽霊・自分より段数が多い相手(自分を倒せる)を重く、少ない相手を軽く数える
        private static float Danger(CpuContext c, Vector3 point)
        {
            CpuParams p = c.Params;
            float sum = 0.0f;
            foreach (var o in c.Perception.Opponents)
            {
                float d = CpuContext.FlatDistance(o.Position, point);
                if (d >= p.WaitDangerRadius) continue;
                float weight;
                if (o.IsGhost) weight = p.WaitGhostWeight;
                else if (o.StackDiff < 0) weight = p.WaitStrongerWeight;
                else if (o.StackDiff == 0) weight = p.WaitEqualWeight;
                else weight = p.WaitWeakerWeight;
                sum += weight * (1.0f - d / p.WaitDangerRadius);
            }
            return sum;
        }

        //========================================
        // 供給エリアと候補
        //========================================

        private void ScanAreas()
        {
            if (Time.time - m_areaScanTime < kAreaScanInterval) return;
            m_areaScanTime = Time.time;
            m_areas.Clear();
            foreach (var area in Object.FindObjectsByType<TumikiSpawnArea>())
            {
                if (area != null && area.isActiveAndEnabled) m_areas.Add(area);
            }
            BuildSpots();
        }

        // 各エリアの外周 kSpotDirections 方向 × 縁からの距離 kSpotOffsets の点(床があり、どのエリアの中でもない点)
        private void BuildSpots()
        {
            m_spots.Clear();
            float inset = -(DarumaCharacter.kBodyRadius + kAreaMargin);
            foreach (var area in m_areas)
            {
                Vector3 center = area.transform.position;
                for (int i = 0; i < kSpotDirections; i++)
                {
                    float angle = i * 360.0f / kSpotDirections;
                    Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * area.transform.forward;
                    dir.y = 0.0f;
                    dir.Normalize();
                    if (!TryFindEdge(area, center, dir, inset, out float edge)) continue;
                    foreach (float offset in kSpotOffsets)
                    {
                        Vector3 p = center + dir * (edge + offset);
                        if (!NavMesh.SamplePosition(p, out NavMeshHit hit, kSampleRadius, NavMesh.AllAreas)) continue;
                        Vector3 sp = hit.position;
                        if (IsInsideAnyArea(sp, inset)) continue;
                        if (!CpuSensor.HasGround(sp, 1.0f)) continue;
                        if (CpuNavMesh.IsNearHazard(sp, kHazardClearance)) continue;
                        m_spots.Add(new Spot { Position = sp, Offset = kAreaMargin + offset });
                    }
                }
            }
        }

        // エリアの中心から dir へ進み、エリアの外(縁から inset だけ外)に出る距離
        private static bool TryFindEdge(TumikiSpawnArea area, Vector3 center, Vector3 dir, float inset, out float edge)
        {
            for (float d = 0.0f; d <= kOutsideMaxDistance; d += kOutsideStep)
            {
                if (area.Contains(center + dir * d, inset)) continue;
                edge = d;
                return true;
            }
            edge = 0.0f;
            return false;
        }

        // 道の途中(kCrossCheckStep m ごと)に、供給エリアの中(体が少しでもかかる)を通る所があるか
        private bool CrossesArea(NavMeshPath path)
        {
            int count = path.GetCornersNonAlloc(m_corners);
            float inset = -DarumaCharacter.kBodyRadius;
            for (int i = 1; i < count; i++)
            {
                Vector3 a = m_corners[i - 1];
                Vector3 b = m_corners[i];
                float len = CpuContext.FlatDistance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / kCrossCheckStep));
                for (int k = 0; k <= steps; k++)
                {
                    if (IsInsideAnyArea(Vector3.Lerp(a, b, k / (float)steps), inset)) return true;
                }
            }
            return false;
        }

        private bool IsInsideAnyArea(Vector3 pos, float inset)
        {
            foreach (var area in m_areas)
            {
                if (area != null && area.Contains(pos, inset)) return true;
            }
            return false;
        }

        //========================================
        // 着いたあと
        //========================================

        // 待つ場所の周りを、ときどきゆっくり歩く。歩いていないときは近くの相手の方を向く
        private void Idle(CpuContext c, ref DarumaCommand cmd)
        {
            CpuParams p = c.Params;
            Vector3 pos = c.Position;
            if (m_wandering)
            {
                if (CpuContext.FlatDistance(pos, m_wanderTarget) <= 0.3f)
                {
                    m_wandering = false;
                    m_nextWanderTime = Time.time + Random.Range(p.WaitWanderIntervalMin, p.WaitWanderIntervalMax);
                }
                else
                {
                    CpuPathFollower.Walk(pos, m_wanderTarget, c.Self.MoveSpeed, p, ref cmd);
                    cmd.Move *= p.WaitWanderInput;
                    return;
                }
            }
            if (Time.time >= m_nextWanderTime)
            {
                if (TryPickWanderTarget(c, out m_wanderTarget)) m_wandering = true;
                else m_nextWanderTime = Time.time + p.WaitWanderIntervalMin;
            }
            if (!m_wandering) FaceNearestOpponent(c, ref cmd);
        }

        // 待つ場所から WaitWanderRadius 以内の、床があってエリアの外の点
        private bool TryPickWanderTarget(CpuContext c, out Vector3 target)
        {
            float inset = -(DarumaCharacter.kBodyRadius + 0.3f);
            for (int i = 0; i < kWanderAttempts; i++)
            {
                Vector2 r = Random.insideUnitCircle * c.Params.WaitWanderRadius;
                Vector3 p = m_goal + new Vector3(r.x, 0.0f, r.y);
                if (CpuContext.FlatDistance(p, c.Position) < 0.5f) continue;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 0.5f, NavMesh.AllAreas)) continue;
                p = hit.position;
                if (IsInsideAnyArea(p, inset)) continue;
                if (!CpuSensor.HasGround(p, 1.0f)) continue;
                if (CpuNavMesh.IsNearHazard(p, kHazardClearance)) continue;
                target = p;
                return true;
            }
            target = Vector3.zero;
            return false;
        }

        // 一番近い相手(WaitDangerRadius 以内)の方を向く
        private static void FaceNearestOpponent(CpuContext c, ref DarumaCommand cmd)
        {
            CpuOpponent nearest = null;
            float best = c.Params.WaitDangerRadius;
            foreach (var o in c.Perception.Opponents)
            {
                float d = o.FlatDistance;
                if (d < best)
                {
                    best = d;
                    nearest = o;
                }
            }
            if (nearest == null) return;
            Vector3 to = nearest.Position - c.Position;
            to.y = 0.0f;
            // もうおおむね向いていれば動かさない(小さな入力でも少しずつ進んでしまうため)
            if (to.sqrMagnitude < 0.0001f || Vector3.Angle(c.Self.transform.forward, to) < 15.0f) return;
            to.Normalize();
            cmd.Move = new Vector2(to.x, to.z) * kTurnInput;
        }

        public override void DrawGizmos(CpuContext c)
        {
            if (!m_hasGoal) return;
            Gizmos.color = new Color(0.4f, 0.8f, 1.0f);
            m_route.DrawGizmos(c.Position);
            Gizmos.DrawWireSphere(m_goal, m_stopDistance);
            if (m_wandering) Gizmos.DrawLine(c.Position, m_wanderTarget);
        }
    }
}
