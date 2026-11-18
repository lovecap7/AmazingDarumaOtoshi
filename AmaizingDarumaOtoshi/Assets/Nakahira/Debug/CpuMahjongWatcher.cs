using System.Collections.Generic;
using UnityEngine;

namespace Nakahira.DebugTools
{
    // 牌あつめの卓(Stage22)で CPU がどう動いたかの調査用(2026-11-18)
    //  ・拾った牌の数・落とした牌の数(手持ちの増減。役で 3〜4 枚消えたものは「役」)
    //  ・拾える牌が場にある間の、状態ごとの時間
    //  ・ふたが開いている間に中央の穴へ落ちた回数(発動した人は除く)・巨大な牌の下敷きになった回数
    // (Unity MCP から CpuMahjongWatcher.Begin() で付ける。結果は Summary)
    public class CpuMahjongWatcher : MonoBehaviour
    {
        public static CpuMahjongWatcher Instance { get; private set; }

        public int Picked { get; private set; }
        public int Dropped { get; private set; }
        public int Yaku { get; private set; }
        public int HoleFalls { get; private set; }
        public int Crushed { get; private set; }
        public int Flows { get; private set; }
        readonly Dictionary<string, float> m_stateTime = new Dictionary<string, float>();
        readonly Dictionary<MatchPlayer, int> m_hands = new Dictionary<MatchPlayer, int>();
        readonly HashSet<DarumaCharacter> m_fallen = new HashSet<DarumaCharacter>();
        readonly List<string> m_log = new List<string>();
        readonly List<Nakagawa.MahjongTable.CpuFieldTile> m_tiles = new List<Nakagawa.MahjongTable.CpuFieldTile>();
        bool m_wasOpen;
        bool m_wasPon;

        public static CpuMahjongWatcher Begin()
        {
            if (Instance != null) Destroy(Instance.gameObject);
            var go = new GameObject("CpuMahjongWatcher");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CpuMahjongWatcher>();
            return Instance;
        }

        private void OnEnable() => DarumaCharacter.AnyEliminated += OnEliminated;
        private void OnDisable() => DarumaCharacter.AnyEliminated -= OnEliminated;

        public string Summary
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"拾った {Picked} 枚 / 落とした {Dropped} 枚 / 役 {Yaku} 回 / ツモ {Flows} 回 / 中央の穴へ落ちた {HoleFalls} 回 / 下敷き {Crushed} 回");
                sb.Append("\n牌がある間の状態:");
                foreach (var pair in m_stateTime) sb.Append($" {pair.Key}={pair.Value:F0}秒");
                if (m_pickDelays.Count > 0)
                {
                    m_pickDelays.Sort();
                    float sum = 0.0f;
                    foreach (float d in m_pickDelays) sum += d;
                    sb.Append($"\n床に着いてから拾われるまで: 平均 {sum / m_pickDelays.Count:F2}秒・最短 {m_pickDelays[0]:F2}秒・中央 {m_pickDelays[m_pickDelays.Count / 2]:F2}秒({m_pickDelays.Count} 枚)");
                }
                if (m_scatter.Count > 0)
                {
                    m_scatter.Sort();
                    float sum = 0.0f;
                    foreach (float d in m_scatter) sum += d;
                    sb.Append($"\n落とした牌が止まった所までの距離: 平均 {sum / m_scatter.Count:F1}m・最短 {m_scatter[0]:F1}m・最長 {m_scatter[m_scatter.Count - 1]:F1}m({m_scatter.Count} 枚)");
                }
                foreach (var line in m_log) sb.Append("\n" + line);
                return sb.ToString();
            }
        }

        private void Update()
        {
            var table = CpuMahjong.Table;
            var match = DarumaMatch.Current;
            if (table == null || match == null) return;

            bool open = table.CpuLidOpen;
            bool wasOpen = m_wasOpen;
            if (open && !m_wasOpen) Flows++;
            m_wasOpen = open;
            bool pon = table.TryGetCpuPonDrop(out Vector3 ponCenter, out Vector2 ponHalf);
            if (pon && !m_wasPon && m_log.Count < 30) m_log.Add($"{Time.time:F1} ポンの予告 発動={(table.CpuFlowImmune != null ? table.CpuFlowImmune.Label : "?")} 大きさの半分=({ponHalf.x:F1}, {ponHalf.y:F1})");
            m_wasPon = pon;

            table.GetCpuFieldTiles(m_tiles);
            bool anyPickable = false;
            foreach (var t in m_tiles) if (t.Pickable) anyPickable = true;
            TrackTiles(table);

            foreach (var p in match.Players)
            {
                if (p == null) continue;
                var cpu = p.GetComponent<DarumaCpu>();
                if (cpu == null) continue;
                int hand = table.CpuHandCount(p);
                if (m_hands.TryGetValue(p, out int last))
                {
                    int diff = hand - last;
                    if (diff > 0) Picked += diff;
                    else if (diff <= -3) Yaku++;
                    else if (diff < 0) Dropped -= diff;
                }
                m_hands[p] = hand;

                DarumaCharacter c = p.Character;
                if (c == null) continue;
                if (anyPickable && Time.deltaTime > 0.0f)
                {
                    string state = cpu.StateName;
                    m_stateTime.TryGetValue(state, out float s);
                    m_stateTime[state] = s + Time.deltaTime;
                }
                // ふたが開いている間に中央の穴へ落ちた
                Vector3 pos = c.transform.position;
                if (open && pos.y < table.CpuCenter.y - 1.0f && CpuMahjong.IsInLid(table, pos, 0.5f) && m_fallen.Add(c))
                {
                    HoleFalls++;
                    bool immune = table.CpuFlowImmune == p;
                    if (m_log.Count < 30) m_log.Add($"{Time.time:F1} {p.Label} 中央の穴へ落ちた 段数={c.Stack.Count} 速さ={c.MoveSpeed:F1} 状態={cpu.StateName} 発動した人={immune} 開いた時の中央からの距離={(m_openDistance.TryGetValue(c, out float od) ? od : -1):F1}");
                }
                if (open && !wasOpen)
                {
                    Vector3 d = pos - table.CpuCenter;
                    m_openDistance[c] = new Vector2(d.x, d.z).magnitude;
                }
                m_lastInfo[c] = $"{p.Label} 状態={cpu.StateName} 危ない場所={cpu.HazardAction} 反応={cpu.ReflexAction} 位置=({pos.x:F1}, {pos.z:F1})";
            }
        }

        readonly Dictionary<DarumaCharacter, float> m_openDistance = new Dictionary<DarumaCharacter, float>();
        readonly Dictionary<DarumaCharacter, string> m_lastInfo = new Dictionary<DarumaCharacter, string>();

        class TileTrack
        {
            public Vector3 First;
            public bool Dropped;    // 落とした牌(空から降った牌ではない)
            public bool Measured;   // 止まった所までの距離を測った
            public Vector3 Last;
            public float SinceLanded;
            public float TimeLeft;
            public bool Seen;
        }

        readonly Dictionary<object, TileTrack> m_tracks = new Dictionary<object, TileTrack>();
        readonly List<object> m_gone = new List<object>();
        readonly List<float> m_pickDelays = new List<float>();
        readonly List<float> m_scatter = new List<float>();

        // 牌ごとに、拾われるまでの時間と、落とした牌の飛んだ距離を測る
        private void TrackTiles(Nakagawa.MahjongTable table)
        {
            foreach (var tr in m_tracks.Values) tr.Seen = false;
            foreach (var t in m_tiles)
            {
                if (!m_tracks.TryGetValue(t.Key, out var tr))
                {
                    tr = new TileTrack { First = t.Position, Dropped = t.Position.y > table.CpuCenter.y + 0.6f };
                    m_tracks[t.Key] = tr;
                }
                tr.Seen = true;
                // 床に着いて 0.8 秒(滑り終わるころ)たったら、落とした所からの距離を測る
                if (tr.Dropped && !tr.Measured && t.SinceLanded >= 0.8f)
                {
                    tr.Measured = true;
                    m_scatter.Add(new Vector2(t.Position.x - tr.First.x, t.Position.z - tr.First.z).magnitude);
                }
                tr.Last = t.Position;
                tr.SinceLanded = t.SinceLanded;
                tr.TimeLeft = t.TimeLeft;
            }
            m_gone.Clear();
            foreach (var pair in m_tracks) if (!pair.Value.Seen) m_gone.Add(pair.Key);
            foreach (var key in m_gone)
            {
                var tr = m_tracks[key];
                m_tracks.Remove(key);
                // 床にあって、寿命の前に消えた = 拾われた(ふたが開いて穴へ落ちたものは除く)
                if (tr.SinceLanded >= 0.0f && tr.TimeLeft > 0.3f && !table.CpuLidOpen) m_pickDelays.Add(tr.SinceLanded);
            }
        }

        private void OnEliminated(DarumaCharacter c)
        {
            m_fallen.Remove(c);
            var table = CpuMahjong.Table;
            if (table == null) return;
            // 巨大な牌が着地した瞬間に、並びの下にいた
            if (!table.TryGetCpuPonDrop(out Vector3 center, out Vector2 half)) return;
            // 下敷きは、真下向きに吹き飛ぶ「その他」の脱落(殴られて脱落したものと分ける)
            if (c.EliminationCause != EliminationCause.Other || c.EliminationDirection.y > -0.9f) return;
            Vector3 local = c.transform.position - center;
            if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.y)
            {
                Crushed++;
                if (m_log.Count < 30) m_log.Add($"{Time.time:F1} 巨大な牌の下敷き 直前: {(m_lastInfo.TryGetValue(c, out string info) ? info : c.name)} 牌の中心=({center.x:F1}, {center.z:F1})");
            }
        }
    }
}
