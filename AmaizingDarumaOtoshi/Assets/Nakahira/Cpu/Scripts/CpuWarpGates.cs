using System.Collections.Generic;
using Nakagawa;
using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // ワープゲートに入って行く道(入るゲートと、その手前の入り口)
    public struct CpuWarpPlan
    {
        public WarpGateGimmick Gate;
        // ゲートのそばの、床の上(ナビメッシュ)の入り口。ここからゲートの真ん中へ歩いて入る
        public Vector3 EntrySpot;

        public bool IsValid => Gate != null;
    }

    // CPU がワープゲートを使って移動するための下調べ
    // ゲートの中はナビメッシュで歩かない場所にしてある(ふだんの道で間違って入らないように。CpuNavMesh)
    // 代わりに、ゲートのそばに「入り口」を置き、歩いて行けない場所へは「入り口まで歩く → ゲートに入る → 出口から歩く」道を求める
    //  ・出口が 1 つ(2 つで 1 組のゲート)なら行き先は決まっている
    //  ・出口が複数(行き先は抽選)なら、出口ごとの残りの道の平均で比べる(着いた所から行けない出口は外れの手間で数える)。
    //    外れの島に出たら、そこからまた道を求め直す
    public static class CpuWarpGates
    {
        // 入り口を探す範囲(m)
        const float kEntrySearch = 3.0f;
        // 入り口まで歩けるとみなす距離(m)
        const float kEntryTolerance = 0.5f;
        // ゲートに入る手間(吸い込まれて出てくるまで。道の長さ m として)
        const float kWarpCost = 3.0f;
        // 抽選で目的地へ行けない出口に出たときの手間(道の長さ m として。もう一度ワープし直す分)
        const float kWrongExitCost = 20.0f;
        // ゲートの中を歩かない場所にするとき、ワープ範囲に足す余裕(m)
        public const float kKeepOutMargin = 0.2f;

        class Gate
        {
            public WarpGateGimmick Gimmick;
            public Vector3 Entry;
            public bool HasEntry;
            public readonly List<Gate> Exits = new List<Gate>();
        }

        static readonly List<Gate> s_gates = new List<Gate>();
        static float[] s_fromLength = new float[0];
        static float[] s_toLength = new float[0];
        static NavMeshPath s_path;   // MonoBehaviour の初期化中には作れないので、使うときに作る

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_gates.Clear();
        }

        // ナビメッシュに歩かない場所として加えるゲート(プレイヤーを送るもの)
        public static IEnumerable<WarpGateGimmick> PlayerGates()
        {
            foreach (var g in Object.FindObjectsByType<WarpGateGimmick>())
            {
                if (g.AcceptsPlayers) yield return g;
            }
        }

        // ナビメッシュを作った直後に呼ぶ:今のステージのゲートと入り口・出口を調べ直す
        internal static void Build()
        {
            s_gates.Clear();
            foreach (var g in PlayerGates())
            {
                var gate = new Gate { Gimmick = g };
                if (NavMesh.SamplePosition(g.transform.position, out NavMeshHit hit, kEntrySearch, NavMesh.AllAreas)
                    && Mathf.Abs(hit.position.y - g.transform.position.y) < 0.5f)
                {
                    gate.Entry = hit.position;
                    gate.HasEntry = true;
                }
                s_gates.Add(gate);
            }
            // 出口:同じグループの自分以外で、プレイヤーを受け付けるゲート
            foreach (var a in s_gates)
            {
                foreach (var b in s_gates)
                {
                    if (a != b && a.Gimmick.Group == b.Gimmick.Group) a.Exits.Add(b);
                }
            }
            if (s_fromLength.Length < s_gates.Count)
            {
                s_fromLength = new float[s_gates.Count];
                s_toLength = new float[s_gates.Count];
            }
        }

        // 歩いて行けない to へ、ワープゲートに入って行く道を求める
        // 道(曲がり角)は入り口までの分だけ path に入る。length はワープの手間・抽選の外れも含めた道の長さ
        internal static bool TryFindWarp(Vector3 from, Vector3 to, float goalTolerance, NavMeshPath path, out float length, out CpuWarpPlan plan)
        {
            length = 0.0f;
            plan = default;
            int count = s_gates.Count;
            if (count == 0) return false;
            if (s_path == null) s_path = new NavMeshPath();

            // 歩いて入れるゲート
            bool anyFrom = false;
            for (int i = 0; i < count; i++)
            {
                s_fromLength[i] = -1.0f;
                Gate g = s_gates[i];
                if (!g.HasEntry || g.Exits.Count == 0 || !g.Gimmick.isActiveAndEnabled) continue;
                if (!CpuNavMesh.TryFindDirect(from, g.Entry, kEntryTolerance, s_path, out float l)) continue;
                s_fromLength[i] = l;
                anyFrom = true;
            }
            if (!anyFrom) return false;

            // 出口から目的地まで歩けるか(出口のゲートの真ん中から)
            for (int i = 0; i < count; i++)
            {
                s_toLength[i] = -1.0f;
                if (!s_gates[i].Gimmick.isActiveAndEnabled) continue;
                if (CpuNavMesh.TryFindDirect(s_gates[i].Gimmick.transform.position, to, goalTolerance, s_path, out float l)) s_toLength[i] = l;
            }

            int best = -1;
            float bestCost = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (s_fromLength[i] < 0.0f) continue;
                Gate g = s_gates[i];
                // 出口ごとの残りの道の平均(行けない出口は外れの手間)。どの出口からも行けなければ使わない
                float sum = 0.0f;
                bool anyReach = false;
                foreach (var exit in g.Exits)
                {
                    float l = s_toLength[s_gates.IndexOf(exit)];
                    if (l >= 0.0f)
                    {
                        sum += l;
                        anyReach = true;
                    }
                    else sum += kWrongExitCost;
                }
                if (!anyReach) continue;
                float cost = s_fromLength[i] + kWarpCost + sum / g.Exits.Count;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = i;
                }
            }
            if (best < 0) return false;

            Gate chosen = s_gates[best];
            if (!CpuNavMesh.TryFindDirect(from, chosen.Entry, kEntryTolerance, path, out _)) return false;
            length = bestCost;
            plan = new CpuWarpPlan { Gate = chosen.Gimmick, EntrySpot = chosen.Entry };
            return true;
        }

        // pos がどれかのゲートの中(半径 + margin 以内)か
        public static bool IsInsideGate(Vector3 pos, float margin)
        {
            foreach (var g in s_gates)
            {
                if (g.Gimmick == null || !g.Gimmick.isActiveAndEnabled) continue;
                Vector3 d = pos - g.Gimmick.transform.position;
                if (Mathf.Abs(d.y) > 1.5f) continue;
                d.y = 0.0f;
                float r = g.Gimmick.Radius + margin;
                if (d.sqrMagnitude <= r * r) return true;
            }
            return false;
        }

        public static void DrawGizmos()
        {
            Gizmos.color = new Color(0.9f, 0.4f, 0.9f);
            foreach (var g in s_gates)
            {
                if (g.HasEntry) Gizmos.DrawWireCube(g.Entry + Vector3.up * 0.1f, new Vector3(0.4f, 0.2f, 0.4f));
            }
        }
    }
}
