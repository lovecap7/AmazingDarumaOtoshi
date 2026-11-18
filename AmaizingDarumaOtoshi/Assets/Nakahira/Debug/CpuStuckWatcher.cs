using System.Collections.Generic;
using UnityEngine;

namespace Nakahira.DebugTools
{
    // CPU が「小刻みに向きを変えながら、その場から動けない」状態になっていないかの調査用(2026-11-18。Stage21 の割れる氷で起きていた)
    // 毎フレーム、CPU のキャラクターの位置と向きを Window 秒ぶん覚え、
    //  ・その間に動いた距離(はじめと今の差)が MaxMove m より小さい
    //  ・その間に向きが合わせて MinTurn 度以上変わった
    // を満たしたら「引っかかり」として 1 回数える(状態「待つ」の間は数えない。抜けるまで数え直さない)
    // (Unity MCP から CpuStuckWatcher.Begin() で付ける。結果は Summary)
    public class CpuStuckWatcher : MonoBehaviour
    {
        public static CpuStuckWatcher Instance { get; private set; }

        public float Window = 2.0f;
        public float MaxMove = 0.6f;
        public float MinTurn = 270.0f;

        public int StuckCount { get; private set; }
        public float StuckTime { get; private set; }
        public float WatchedTime { get; private set; }
        readonly List<string> m_log = new List<string>();
        public IReadOnlyList<string> Log => m_log;

        struct Sample
        {
            public float Time;
            public Vector3 Pos;
            public float Yaw;
        }

        class Track
        {
            public readonly List<Sample> Samples = new List<Sample>();
            public bool Stuck;
        }

        readonly Dictionary<DarumaCpu, Track> m_tracks = new Dictionary<DarumaCpu, Track>();

        public static CpuStuckWatcher Begin()
        {
            if (Instance != null) Destroy(Instance.gameObject);
            var go = new GameObject("CpuStuckWatcher");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CpuStuckWatcher>();
            return Instance;
        }

        public string Summary => $"見張った時間 {WatchedTime:F0}秒(CPU のべ) / 引っかかり {StuckCount} 回 / 引っかかっていた時間 {StuckTime:F1}秒"
            + (m_log.Count > 0 ? "\n" + string.Join("\n", m_log) : "");

        private void Update()
        {
            if (Time.deltaTime <= 0.0f) return;
            foreach (var cpu in FindObjectsByType<DarumaCpu>())
            {
                var player = cpu.GetComponent<MatchPlayer>();
                DarumaCharacter c = player != null ? player.Character : null;
                if (!m_tracks.TryGetValue(cpu, out var track)) m_tracks[cpu] = track = new Track();
                if (c == null || cpu.StateName == "待つ")
                {
                    track.Samples.Clear();
                    track.Stuck = false;
                    continue;
                }
                WatchedTime += Time.deltaTime;
                Vector3 pos = c.transform.position;
                track.Samples.Add(new Sample { Time = Time.time, Pos = pos, Yaw = c.transform.eulerAngles.y });
                while (track.Samples.Count > 0 && Time.time - track.Samples[0].Time > Window) track.Samples.RemoveAt(0);
                if (track.Samples.Count < 2 || Time.time - track.Samples[0].Time < Window * 0.9f) continue;

                Vector3 a = track.Samples[0].Pos;
                float move = new Vector2(pos.x - a.x, pos.z - a.z).magnitude;
                float turn = 0.0f;
                for (int i = 1; i < track.Samples.Count; i++) turn += Mathf.Abs(Mathf.DeltaAngle(track.Samples[i - 1].Yaw, track.Samples[i].Yaw));
                bool stuck = move < MaxMove && turn >= MinTurn;
                if (stuck)
                {
                    StuckTime += Time.deltaTime;
                    if (!track.Stuck)
                    {
                        StuckCount++;
                        if (m_log.Count < 40) m_log.Add($"{Time.time:F1} {player.Label} 状態={cpu.StateName} 位置=({pos.x:F1}, {pos.y:F1}, {pos.z:F1}) {Window}秒で {move:F2}m・向き {turn:F0}°");
                    }
                }
                track.Stuck = stuck;
            }
        }
    }
}
