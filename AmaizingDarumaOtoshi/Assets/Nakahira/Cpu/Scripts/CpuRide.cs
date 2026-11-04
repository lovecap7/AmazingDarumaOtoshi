using Nakagawa;
using UnityEngine;

namespace Nakahira
{
    // 移動床の乗り降り。状態が出した操作のうち、移動だけを置き換える(攻撃・ショットはそのまま)
    //  ・乗り場で待つ:状態の道が移動床に乗って行く道で、乗り場に着いたら、床がその地点に止まるまで待つ
    //  ・乗る:床が止まっていて、乗りきるまで止まっていそうなら床の真ん中へ歩く
    //  ・乗っている:床が動いている間は真ん中に立っている(降りない)
    //  ・降りる:床が止まったとき、今の道がこの床に乗って行く道でなければ(=降りて歩いて行ける)、止まっている地点の乗り場へ降りる
    //           (道を持たない状態(撃つなど)も降りる。乗ったまま運ばれないように)
    // ワープゲートも同じ層で扱う:状態の道がゲートに入って行く道なら、入り口に着いたらゲートの真ん中へ歩いて入る
    // 状態の道(CpuState.Route)を見るだけで、状態の中身は変えない
    public class CpuRide
    {
        public enum Action { None, WaitAtDock, Board, Riding, Alight, WaitAtGate, EnterGate, LeaveGate }

        // 乗り場に着いたとみなす距離(m)
        const float kDockReach = 0.8f;
        // 床の真ん中からこの距離(m)以内なら、真ん中に立っているとみなす
        const float kCenterReach = 0.35f;
        // 真ん中へ寄るときに、この距離(m)で入力を最大にする
        const float kCenterFullInputDistance = 1.0f;
        // 乗りきるまでの時間に足す余裕(秒)
        const float kBoardMargin = 0.3f;
        // この時間(秒)より前に求めた道は、もう使われていないとみなす
        const float kRouteStaleTime = 1.0f;
        // 乗り場に立っているとみなす距離(m)
        const float kSpotReach = 0.25f;
        // 床から出たあと、乗り場までこの距離(m)未満なら降りる動きを続ける
        const float kAlightKeepDistance = 1.5f;

        // ゲートの中にいるとみなす、ゲートの半径に足す距離(m)
        const float kGateInsideMargin = 0.1f;
        // ゲートの中にいたあと、入り直すまで待つ時間(秒。ゲートの反応しない時間より長く)
        const float kGateRearmWait = 0.6f;
        // ゲートに入ってからこの時間(秒)送られなければ、出直す
        const float kGateGiveUpTime = 1.5f;

        // 降りる先の乗り場
        Vector3 m_alightSpot;
        // 入ると決めたゲートと、中に入った時刻
        WarpGateGimmick m_enteringGate;
        float m_enterTime;
        // 最後にゲート(どれでも)の中にいた時刻
        float m_lastInsideTime = float.NegativeInfinity;

        public Action Current { get; private set; }

        public void Apply(CpuContext c, CpuPathFollower route, ref DarumaCommand cmd)
        {
            Action before = Current;
            Current = Action.None;
            if (route != null && Time.time - route.LastPathTime > kRouteStaleTime) route = null;
            Vector3 pos = c.Position;
            if (CpuWarpGates.IsInsideGate(pos, kGateInsideMargin)) m_lastInsideTime = Time.time;

            // 移動床の上
            if (CpuMovingFloors.TryGetFloorUnder(pos, out MovingFloorGimmick floor))
            {
                int stop = floor.StoppedAt;
                // 動いている間・この床に乗って行く途中なら、真ん中に立っている
                if (stop < 0 || (route != null && route.HasRide && route.Ride.Floor == floor))
                {
                    Current = Action.Riding;
                    cmd.Move = ToCenter(pos, floor.Position);
                    cmd.Jump = false;
                    return;
                }
                // 止まった:乗り場へまっすぐ降りる(降りたら状態の操作に戻る)
                // 状態の道に任せると、床と島の継ぎ目で縁の確かめに止められて、乗ったまま運ばれることがある
                if (CpuMovingFloors.TryGetDock(floor, stop, out Vector3 dock))
                {
                    Current = Action.Alight;
                    m_alightSpot = dock;
                    cmd.Move = ToPoint(pos, dock);
                    cmd.Jump = false;
                }
                return;
            }

            // 床から出ても、乗り場(島の縁から少し内側)に着くまでは降りる動きを続ける(縁ぎわで止まって落とされないように)
            // (はじかれて離れたら続けない。縁の確かめをしない動きなので)
            float toSpot = CpuContext.FlatDistance(pos, m_alightSpot);
            if (before == Action.Alight && toSpot > kSpotReach && toSpot < kAlightKeepDistance)
            {
                Current = Action.Alight;
                cmd.Move = ToPoint(pos, m_alightSpot);
                cmd.Jump = false;
                return;
            }

            // ワープゲートに入る
            if (route != null && route.HasWarp)
            {
                ApplyWarp(c, route.Warp, ref cmd);
                return;
            }
            m_enteringGate = null;

            // 乗り場で床を待つ・乗る
            if (route == null || !route.HasRide) return;
            CpuRidePlan plan = route.Ride;
            if (plan.Floor == null || !plan.Floor.isActiveAndEnabled) return;
            if (CpuContext.FlatDistance(pos, plan.BoardSpot) > kDockReach) return;

            MovingFloorGimmick f = plan.Floor;
            float speed = Mathf.Max(0.1f, c.Self.MoveSpeed);
            float boardTime = CpuContext.FlatDistance(pos, f.Position) / speed + kBoardMargin;
            // 乗り始めたら、床が止まっている間は乗りきる(時間の見込みが少し外れても引き返さない)
            bool docked = f.StoppedAt == plan.Board;
            if (docked && (before == Action.Board || f.WaitRemaining > boardTime))
            {
                Current = Action.Board;
                // 床は止まっていて乗り場のすぐ隣にあるので、縁の確かめはしない(床と島の継ぎ目で止まらないように)
                cmd.Move = ToPoint(pos, f.Position);
                cmd.Jump = false;
                return;
            }
            // 乗り場(島の縁から少し内側)に立って待つ
            Current = Action.WaitAtDock;
            cmd.Move = CpuContext.FlatDistance(pos, plan.BoardSpot) > kSpotReach ? ToPoint(pos, plan.BoardSpot) * 0.5f : Vector2.zero;
            cmd.Jump = false;
        }

        // 入り口に着いたら、ゲートの真ん中へ歩いて入る(ゲートが送ってくれる)
        //  ・ゲートの中にいるのに入ると決めていない(ワープで出てきたところ)なら、まず入り口へ出る
        //  ・ゲートの中にいたばかりなら、入り口で少し待つ(ゲートは出てきたものに、離れてしばらくは反応しないため)
        //  ・入ったのにしばらく送られなければ(出口がふさがっているなど)、いったん入り口へ出る
        private void ApplyWarp(CpuContext c, CpuWarpPlan plan, ref DarumaCommand cmd)
        {
            WarpGateGimmick gate = plan.Gate;
            if (gate == null || !gate.isActiveAndEnabled) return;
            Vector3 pos = c.Position;
            float toCenter = CpuContext.FlatDistance(pos, gate.transform.position);
            bool inside = toCenter <= gate.Radius + kGateInsideMargin;

            if (m_enteringGate != gate || !inside) m_enterTime = Time.time;
            if (m_enteringGate == gate && inside && Time.time - m_enterTime > kGateGiveUpTime)
            {
                // 入ったまま送られない:入り口へ出直す
                m_enteringGate = null;
            }

            if (inside && m_enteringGate != gate)
            {
                Current = Action.LeaveGate;
                cmd.Move = ToPoint(pos, plan.EntrySpot);
                cmd.Jump = false;
                return;
            }

            if (m_enteringGate != gate)
            {
                // 入り口に着くまでは状態の道のまま歩く
                if (CpuContext.FlatDistance(pos, plan.EntrySpot) > kDockReach) return;
                if (Time.time - m_lastInsideTime < kGateRearmWait)
                {
                    Current = Action.WaitAtGate;
                    cmd.Move = Vector2.zero;
                    cmd.Jump = false;
                    return;
                }
                m_enteringGate = gate;
                m_enterTime = Time.time;
            }

            Current = Action.EnterGate;
            // ゲートは床の上にあり、入り口のすぐ隣なので縁の確かめはしない
            cmd.Move = ToPoint(pos, gate.transform.position);
            cmd.Jump = false;
        }

        // point へまっすぐ向かう最大の入力
        private static Vector2 ToPoint(Vector3 pos, Vector3 point)
        {
            Vector3 to = point - pos;
            to.y = 0.0f;
            float dist = to.magnitude;
            if (dist < 0.01f) return Vector2.zero;
            return new Vector2(to.x, to.z) / dist;
        }

        // 床の真ん中へ寄る入力(着いていれば 0)
        private static Vector2 ToCenter(Vector3 pos, Vector3 center)
        {
            Vector3 to = center - pos;
            to.y = 0.0f;
            float dist = to.magnitude;
            if (dist < kCenterReach) return Vector2.zero;
            Vector3 dir = to / dist;
            return new Vector2(dir.x, dir.z) * Mathf.Clamp01(dist / kCenterFullInputDistance);
        }
    }
}
