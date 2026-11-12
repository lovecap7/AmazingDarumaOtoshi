using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Yurube;

namespace Nakahira.DebugTools
{
    // 必殺技ゲージの調整用。試合中に、だれが何でどれだけ溜めたか・満タンまでの時間・撃った回数を記録する
    // (Unity MCP から SpecialGaugeWatcher.Begin() で付ける。結果は Summary)
    public class SpecialGaugeWatcher : MonoBehaviour
    {
        public static SpecialGaugeWatcher Instance { get; private set; }

        class Record
        {
            public string Name;
            public readonly float[] ByReason = new float[System.Enum.GetValues(typeof(SpecialGauge.GainReason)).Length];
            public readonly List<float> FillTimes = new List<float>();
            public float EmptySince;
            public bool WasFull;
            public int Consumed;
        }

        readonly Dictionary<string, Record> m_records = new Dictionary<string, Record>();
        float m_startTime;

        public static SpecialGaugeWatcher Begin()
        {
            if (Instance != null) Destroy(Instance.gameObject);
            var go = new GameObject("SpecialGaugeWatcher");
            Instance = go.AddComponent<SpecialGaugeWatcher>();
            return Instance;
        }

        private void OnEnable()
        {
            m_startTime = Time.time;
            SpecialGauge.Gained += OnGained;
            SpecialGauge.Consumed += OnConsumed;
        }

        private void OnDisable()
        {
            SpecialGauge.Gained -= OnGained;
            SpecialGauge.Consumed -= OnConsumed;
            if (Instance == this) Instance = null;
        }

        Record Get(SpecialGauge g)
        {
            var match = DarumaMatch.Current;
            var p = match != null ? match.PlayerOf(g.Character) : null;
            string key = p != null ? $"{p.Label}({(p.CharacterData != null ? p.CharacterData.displayName : g.name)})" : g.name;
            if (!m_records.TryGetValue(key, out var r))
            {
                r = new Record { Name = key, EmptySince = Time.time };
                m_records[key] = r;
            }
            return r;
        }

        void OnGained(SpecialGauge g, float amount, SpecialGauge.GainReason reason)
        {
            var r = Get(g);
            r.ByReason[(int)reason] += amount;
            if (g.IsFull && !r.WasFull)
            {
                r.WasFull = true;
                r.FillTimes.Add(Time.time - r.EmptySince);
            }
        }

        void OnConsumed(SpecialGauge g)
        {
            var r = Get(g);
            r.Consumed++;
            r.WasFull = false;
            r.EmptySince = Time.time;
        }

        public string Summary
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine($"経過 {Time.time - m_startTime:F0} 秒");
                var names = System.Enum.GetNames(typeof(SpecialGauge.GainReason));
                foreach (var r in m_records.Values)
                {
                    sb.Append(r.Name).Append(": ");
                    for (int i = 0; i < names.Length; i++) if (r.ByReason[i] > 0.0f) sb.Append($"{names[i]} {r.ByReason[i]:F0} / ");
                    sb.Append($"満タン {r.FillTimes.Count} 回");
                    if (r.FillTimes.Count > 0)
                    {
                        float sum = 0.0f;
                        foreach (var t in r.FillTimes) sum += t;
                        sb.Append($"(平均 {sum / r.FillTimes.Count:F0} 秒)");
                    }
                    sb.AppendLine($" / 撃った {r.Consumed} 回");
                }
                return sb.ToString();
            }
        }
    }
}
