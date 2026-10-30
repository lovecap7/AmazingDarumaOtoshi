using System.Collections.Generic;
using Nakagawa;
using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 移動床に乗って行く道(乗る床・乗る地点・降りる地点)
    public struct CpuRidePlan
    {
        public MovingFloorGimmick Floor;
        // 乗る地点・降りる地点の番号(移動床の地点)
        public int Board;
        public int Alight;
        // 乗る地点のそばの、床の上(ナビメッシュ)の乗り場
        public Vector3 BoardSpot;

        public bool IsValid => Floor != null;
    }

    // CPU が移動床に乗って移動するための下調べ
    // 移動床はナビメッシュに焼き込まない(動くので)。代わりに、各地点のそばの床の上に「乗り場」を置き、
    // 歩いて行けない場所へは「乗り場まで歩く → 床に乗る → 別の地点の乗り場で降りる → 歩く」道を求める
    // 乗り場はナビメッシュを作ったとき(CpuNavMesh)に作り直す
    public static class CpuMovingFloors
    {
        // 乗り場とみなす、床の縁からの最大の距離(m)。ナビメッシュは床の縁から体の半径だけ内側にある
        const float kDockGap = 1.2f;
        // 乗り場と地点の高さの差の上限(m)
        const float kDockHeight = 0.5f;
        // 乗り場まで歩けるとみなす距離(m)
        const float kDockTolerance = 0.5f;
        // 乗って運ばれる距離 1m あたりの道の長さとしての重さ(床は歩くより遅いので、少し重く)
        const float kRideDistanceCost = 1.5f;
        // 床を待つ・乗り降りする手間(道の長さ m として)
        const float kRideBaseCost = 4.0f;
        // 床の上にいるとみなす高さの範囲(床の上面から m)と、縁からはみ出してもよい距離(m)
        const float kOnFloorBelow = 0.5f;
        const float kOnFloorAbove = 3.0f;   // ジャンプしても床の上とみなす
        const float kOnFloorMargin = 0.1f;

        struct Dock
        {
            public MovingFloorGimmick Floor;
            public int Stop;
            public Vector3 Spot;
        }

        static readonly List<MovingFloorGimmick> s_floors = new List<MovingFloorGimmick>();
        static readonly List<Dock> s_docks = new List<Dock>();
        static float[] s_fromLength = new float[0];
        static float[] s_toLength = new float[0];
        static NavMeshPath s_path;   // MonoBehaviour の初期化中には作れないので、使うときに作る

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_floors.Clear();
            s_docks.Clear();
        }

        // ナビメッシュを作った直後に呼ぶ:今のステージの移動床と乗り場を調べ直す
        internal static void Build()
        {
            s_floors.Clear();
            s_docks.Clear();
            foreach (var floor in Object.FindObjectsByType<MovingFloorGimmick>())
            {
                if (floor.Points.Count < 2) continue;
                s_floors.Add(floor);
                Vector2 size = floor.Size;
                float search = Mathf.Max(size.x, size.y) * 0.5f + kDockGap;
                for (int i = 0; i < floor.Points.Count; i++)
                {
                    Vector3 stop = floor.Points[i];
                    if (!NavMesh.SamplePosition(stop, out NavMeshHit hit, search, NavMesh.AllAreas)) continue;
                    if (Mathf.Abs(hit.position.y - stop.y) > kDockHeight) continue;
                    if (RectDistance(floor, stop, hit.position) > kDockGap) continue;
                    s_docks.Add(new Dock { Floor = floor, Stop = i, Spot = hit.position });
                }
            }
            if (s_fromLength.Length < s_docks.Count)
            {
                s_fromLength = new float[s_docks.Count];
                s_toLength = new float[s_docks.Count];
            }
        }

        // pos が移動床の上なら、その床
        public static bool TryGetFloorUnder(Vector3 pos, out MovingFloorGimmick floor)
        {
            foreach (var f in s_floors)
            {
                if (f == null || !f.isActiveAndEnabled) continue;
                Vector3 local = Quaternion.Inverse(f.transform.rotation) * (pos - f.Position);
                Vector2 half = f.Size * 0.5f;
                if (Mathf.Abs(local.x) > half.x + kOnFloorMargin || Mathf.Abs(local.z) > half.y + kOnFloorMargin) continue;
                if (local.y < -kOnFloorBelow || local.y > kOnFloorAbove) continue;
                floor = f;
                return true;
            }
            floor = null;
            return false;
        }

        // 床の上にいるときに道を求める起点:今止まっている(動いていれば次に止まる)地点の乗り場
        public static bool TryGetCurrentDock(MovingFloorGimmick floor, out Vector3 spot)
        {
            int stop = floor.StoppedAt >= 0 ? floor.StoppedAt : floor.NextStop;
            return TryGetDock(floor, stop, out spot);
        }

        public static bool TryGetDock(MovingFloorGimmick floor, int stop, out Vector3 spot)
        {
            foreach (var d in s_docks)
            {
                if (d.Floor == floor && d.Stop == stop)
                {
                    spot = d.Spot;
                    return true;
                }
            }
            spot = Vector3.zero;
            return false;
        }

        // 歩いて行けない to へ、移動床に乗って行く道を求める(from から歩いて行けることは確かめ済みでないこと)
        // 道(曲がり角)は乗り場までの分だけ path に入る。length は乗る手間も含めた道の長さ
        internal static bool TryFindRide(Vector3 from, Vector3 to, float goalTolerance, NavMeshPath path, out float length, out CpuRidePlan plan)
        {
            length = 0.0f;
            plan = default;
            int count = s_docks.Count;
            if (count == 0) return false;
            if (s_path == null) s_path = new NavMeshPath();

            // 歩いて行ける乗り場
            bool anyFrom = false;
            for (int i = 0; i < count; i++)
            {
                s_fromLength[i] = -1.0f;
                if (!s_docks[i].Floor.isActiveAndEnabled) continue;
                if (!CpuNavMesh.TryFindDirect(from, s_docks[i].Spot, kDockTolerance, s_path, out float l)) continue;
                s_fromLength[i] = l;
                anyFrom = true;
            }
            if (!anyFrom) return false;

            // 降りてから歩いて行ける乗り場(歩いて行ける乗り場と同じ床のものだけ調べる)
            for (int i = 0; i < count; i++)
            {
                s_toLength[i] = -1.0f;
                if (s_fromLength[i] >= 0.0f || !HasReachableDock(s_docks[i].Floor)) continue;
                if (!CpuNavMesh.TryFindDirect(s_docks[i].Spot, to, goalTolerance, s_path, out float l)) continue;
                s_toLength[i] = l;
            }

            int bestBoard = -1, bestAlight = -1;
            float bestCost = float.MaxValue;
            for (int a = 0; a < count; a++)
            {
                if (s_fromLength[a] < 0.0f) continue;
                for (int b = 0; b < count; b++)
                {
                    if (s_toLength[b] < 0.0f || s_docks[b].Floor != s_docks[a].Floor || s_docks[b].Stop == s_docks[a].Stop) continue;
                    float ride = RideDistance(s_docks[a].Floor, s_docks[a].Stop, s_docks[b].Stop);
                    if (ride < 0.0f) continue;
                    float cost = s_fromLength[a] + s_toLength[b] + ride * kRideDistanceCost + kRideBaseCost;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestBoard = a;
                        bestAlight = b;
                    }
                }
            }
            if (bestBoard < 0) return false;

            Dock board = s_docks[bestBoard];
            if (!CpuNavMesh.TryFindDirect(from, board.Spot, kDockTolerance, path, out _)) return false;
            length = bestCost;
            plan = new CpuRidePlan { Floor = board.Floor, Board = board.Stop, Alight = s_docks[bestAlight].Stop, BoardSpot = board.Spot };
            return true;
        }

        private static bool HasReachableDock(MovingFloorGimmick floor)
        {
            for (int i = 0; i < s_docks.Count; i++)
            {
                if (s_docks[i].Floor == floor && s_fromLength[i] >= 0.0f) return true;
            }
            return false;
        }

        // 地点 from から地点 to まで運ばれる距離(m)。その順には行かない(片道で戻る)なら -1
        private static float RideDistance(MovingFloorGimmick floor, int from, int to)
        {
            var pts = floor.Points;
            int n = pts.Count;
            float d = 0.0f;
            switch (floor.Mode)
            {
                case MovingFloorGimmick.LoopMode.Loop:
                    for (int i = from; i != to; i = (i + 1) % n) d += Vector3.Distance(pts[i], pts[(i + 1) % n]);
                    return d;
                case MovingFloorGimmick.LoopMode.PingPong:
                    for (int i = Mathf.Min(from, to); i < Mathf.Max(from, to); i++) d += Vector3.Distance(pts[i], pts[i + 1]);
                    return d;
                default:
                    if (to < from) return -1.0f;
                    for (int i = from; i < to; i++) d += Vector3.Distance(pts[i], pts[i + 1]);
                    return d;
            }
        }

        // 床が地点 stop にあるときの、床の縁(長方形)から pos までの水平の距離
        private static float RectDistance(MovingFloorGimmick floor, Vector3 stop, Vector3 pos)
        {
            Vector3 local = Quaternion.Inverse(floor.transform.rotation) * (pos - stop);
            Vector2 half = floor.Size * 0.5f;
            float dx = Mathf.Max(0.0f, Mathf.Abs(local.x) - half.x);
            float dz = Mathf.Max(0.0f, Mathf.Abs(local.z) - half.y);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static void DrawGizmos()
        {
            Gizmos.color = new Color(0.9f, 0.7f, 0.4f);
            foreach (var d in s_docks) Gizmos.DrawWireCube(d.Spot + Vector3.up * 0.1f, new Vector3(0.4f, 0.2f, 0.4f));
        }
    }
}
