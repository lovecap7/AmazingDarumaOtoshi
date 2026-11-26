using UnityEngine;

namespace Nakahira.DebugTools
{
    // ハンマーの横振り(薙ぎ払い・切り返し)の確かめ用。物理のステップごとに、ハンマーの角度と状態を記録する
    // (Unity MCP から HammerSwingWatcher.Begin(キャラ) で付ける。結果は Trace。PressAt で決めた時刻に横振りを押す)
    public class HammerSwingWatcher : MonoBehaviour
    {
        public static HammerSwingWatcher Instance { get; private set; }

        DarumaHammer m_hammer;
        ScriptedCommandSource m_source;
        float m_start;
        float[] m_pressTimes = new float[0];
        int m_nextPress;
        readonly System.Text.StringBuilder m_trace = new System.Text.StringBuilder();
        public string Trace => m_trace.ToString();
        // 角度を記録する時間(秒)。当たりはずっと記録する
        public float RecordSeconds = 3.0f;

        public static HammerSwingWatcher Begin(DarumaCharacter character, float[] pressTimes)
        {
            if (Instance != null) Destroy(Instance.gameObject);
            var go = new GameObject("HammerSwingWatcher");
            Instance = go.AddComponent<HammerSwingWatcher>();
            Instance.m_hammer = character.Hammer;
            Instance.m_source = ScriptedCommandSource.Attach(character);
            Instance.m_start = Time.time;
            Instance.m_pressTimes = pressTimes;
            return Instance;
        }

        private void OnEnable() => DarumaHammer.Hit += OnHit;
        private void OnDisable() => DarumaHammer.Hit -= OnHit;

        // 当たった瞬間のハンマーの角度・向き・相手の角度を残す
        private void OnHit(HammerHitInfo info)
        {
            if (info.Hammer != m_hammer) return;
            Vector3 local = info.Hammer.transform.parent.InverseTransformPoint(info.Target.transform.position);
            float bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            m_trace.Append($"[当たり {info.Target.name} 角度{bearing:F0} ハンマー{m_hammer.SweepYaw:F0} {(m_hammer.IsReverseSweep ? "左→右" : "右→左")}]\n");
        }

        private void Update()
        {
            if (m_source == null) return;
            float t = Time.time - m_start;
            while (m_nextPress < m_pressTimes.Length && t >= m_pressTimes[m_nextPress])
            {
                m_source.Sweep();
                m_trace.Append($"[{t:F2} 押す] ");
                m_nextPress++;
            }
        }

        private void FixedUpdate()
        {
            if (m_hammer == null || m_trace.Length > 20000) return;
            float t = Time.time - m_start;
            if (t > RecordSeconds) return;
            string state = m_hammer.IsSweeping ? (m_hammer.IsReverseSweep ? "左→右" : "右→左") : m_hammer.IsBusy ? "他" : "待";
            m_trace.Append($"{t:F2} {state} {m_hammer.SweepYaw:F0} 回{m_hammer.SweepCount} ");
            Vector3 e = m_hammer.transform.localEulerAngles;
            m_trace.Append($"姿勢({e.x:F0},{e.y:F0},{e.z:F0})\n");
        }
    }
}
