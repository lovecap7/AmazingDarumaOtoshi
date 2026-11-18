using System.Collections.Generic;
using UnityEngine;

namespace Nakahira.DebugTools
{
    // 割れる氷(Stage21)で、CPU がどのタイルから落ちたかの調査用(2026-11-18)
    // CPU のキャラクターが床より下(FallY)に落ちたら、少し前(Lookback 秒前)に立っていたタイルと、その時のタイルの状態を記録する
    //  ・点滅中(Warning)・落ち始め(Falling)なら「タイルと一緒に落ちた」
    //  ・落ちたあと(Gone)・戻る途中(Reviving)なら「穴へ歩いて入った」
    //  ・タイルがなければ「縁から落ちた・飛ばされた」
    // (Unity MCP から CpuIceFallWatcher.Begin() で付ける。結果は Summary)
    public class CpuIceFallWatcher : MonoBehaviour
    {
        public static CpuIceFallWatcher Instance { get; private set; }

        public float FallY = -1.5f;
        public float Lookback = 0.5f;

        readonly List<string> m_log = new List<string>();
        public IReadOnlyList<string> Log => m_log;
        public int WithTile { get; private set; }
        public int IntoHole { get; private set; }
        public int Other { get; private set; }

        struct Sample
        {
            public float Time;
            public Vector3 Pos;
            public int Tile;
            public Nakagawa.IceFloorGimmick.TileState State;
        }

        class Track
        {
            public readonly List<Sample> Samples = new List<Sample>();
            public bool Fallen;
        }

        readonly Dictionary<DarumaCpu, Track> m_tracks = new Dictionary<DarumaCpu, Track>();
        Nakagawa.IceFloorGimmick m_ice;

        public static CpuIceFallWatcher Begin()
        {
            if (Instance != null) Destroy(Instance.gameObject);
            var go = new GameObject("CpuIceFallWatcher");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CpuIceFallWatcher>();
            return Instance;
        }

        public string Summary => $"タイルと一緒に落ちた {WithTile} 回 / 穴へ歩いて入った {IntoHole} 回 / そのほか {Other} 回"
            + (m_log.Count > 0 ? "\n" + string.Join("\n", m_log) : "");

        private void Update()
        {
            if (m_ice == null) m_ice = FindFirstObjectByType<Nakagawa.IceFloorGimmick>();
            if (m_ice == null) return;
            foreach (var cpu in FindObjectsByType<DarumaCpu>())
            {
                var player = cpu.GetComponent<MatchPlayer>();
                DarumaCharacter c = player != null ? player.Character : null;
                if (!m_tracks.TryGetValue(cpu, out var track)) m_tracks[cpu] = track = new Track();
                if (c == null)
                {
                    track.Samples.Clear();
                    track.Fallen = false;
                    continue;
                }
                Vector3 pos = c.transform.position;
                if (pos.y < FallY)
                {
                    if (!track.Fallen && track.Samples.Count > 0)
                    {
                        track.Fallen = true;
                        Sample s = track.Samples[0];
                        string kind;
                        if (s.Tile < 0) { kind = "そのほか"; Other++; }
                        else if (s.State == Nakagawa.IceFloorGimmick.TileState.Warning || s.State == Nakagawa.IceFloorGimmick.TileState.Falling) { kind = "タイルと一緒"; WithTile++; }
                        else if (s.State == Nakagawa.IceFloorGimmick.TileState.Intact) { kind = "そのほか(無事なタイルから)"; Other++; }
                        else { kind = "穴へ"; IntoHole++; }
                        if (m_log.Count < 40) m_log.Add($"{Time.time:F1} {player.Label} 状態={cpu.StateName} {kind} タイル{s.Tile}={s.State} 位置=({s.Pos.x:F1}, {s.Pos.z:F1})");
                    }
                    continue;
                }
                track.Fallen = false;
                int tile = TileAt(pos, out var state);
                track.Samples.Add(new Sample { Time = Time.time, Pos = pos, Tile = tile, State = state });
                while (track.Samples.Count > 1 && Time.time - track.Samples[1].Time >= Lookback) track.Samples.RemoveAt(0);
            }
        }

        // 位置の真下にあるタイル(元の位置で、中心が一番近いもの。内接円の 1.2 倍まで)
        private int TileAt(Vector3 pos, out Nakagawa.IceFloorGimmick.TileState state)
        {
            state = Nakagawa.IceFloorGimmick.TileState.Intact;
            int best = -1;
            float bestDist = float.MaxValue;
            var tiles = m_ice.Tiles;
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (t.Solid || t.Root == null || t.Points == null || t.Points.Length < 3) continue;
                Vector2 center = CpuIceHoles.Centroid(t.Points);
                Vector3 top = CpuIceHoles.BaseWorldPosition(t);
                float d = new Vector2(pos.x - top.x - center.x, pos.z - top.z - center.y).magnitude;
                if (d < bestDist && d < CpuIceHoles.InnerRadius(t.Points, center) * 1.2f)
                {
                    bestDist = d;
                    best = i;
                    state = t.State;
                }
            }
            return best;
        }
    }
}
