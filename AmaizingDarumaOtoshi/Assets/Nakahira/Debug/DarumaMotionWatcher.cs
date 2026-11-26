using System.Collections.Generic;
using UnityEngine;

namespace Nakahira.DebugTools
{
    // 体の動き(DarumaMotion)の確かめ用。毎フレーム(描画の直前)、生存キャラクターの段・頭・ハンマーが
    // 体の真ん中(当たり判定の軸)から横にどれだけずれているか、段の間の隙間がどれだけ開いたかを記録する
    // (Unity MCP から DarumaMotionWatcher.Begin() で付ける。結果は Summary)
    [DefaultExecutionOrder(10000)]
    public class DarumaMotionWatcher : MonoBehaviour
    {
        public static DarumaMotionWatcher Instance { get; private set; }

        public int Frames { get; private set; }
        // 段数ごとの、一番上(頭)の横のずれの最大(m)
        readonly Dictionary<int, float> m_maxHeadSide = new Dictionary<int, float>();
        public float MaxBlockSide { get; private set; }
        public float MaxHeadRise { get; private set; }
        public float MaxHammerShift { get; private set; }
        public int Jumps { get; private set; }
        public int Landings { get; private set; }
        public float MaxGhostShift { get; private set; }
        readonly Dictionary<DarumaCharacter, bool> m_grounded = new Dictionary<DarumaCharacter, bool>();

        // ジャンプののけぞり(2026-11-25)
        // 頭・一番上の段の傾きの最大(度)、隣り合う段の中心の距離から積み木の高さを引いた最小(m。負なら重なっている)
        public float MaxHeadTilt { get; private set; }
        public float MaxTopBlockTilt { get; private set; }
        public float MinLevelSpacing { get; private set; } = float.MaxValue;
        // ジャンプの段階が変わった時刻(最初のキャラクターだけ)
        readonly System.Text.StringBuilder m_phaseLog = new System.Text.StringBuilder();
        string m_lastPhase;
        public string PhaseLog => m_phaseLog.ToString();
        // 段の隙間・傾きの時間ごとの記録(最初のキャラクターだけ。TraceInterval 秒ごと)
        // 1 行 = 時刻 地上/空中 | 下から順の隙間(cm) | 下から順の傾き(度)
        public float TraceInterval = 0.0333f;
        readonly System.Text.StringBuilder m_trace = new System.Text.StringBuilder();
        float m_nextTrace;
        public string Trace => m_trace.ToString();

        public static DarumaMotionWatcher Begin()
        {
            if (Instance != null) Destroy(Instance.gameObject);
            var go = new GameObject("DarumaMotionWatcher");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DarumaMotionWatcher>();
            return Instance;
        }

        public string Summary
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"フレーム {Frames} / 段の横ずれ最大 {MaxBlockSide:F3}m / 頭が本来より上がった最大 {MaxHeadRise:F3}m")
                  .Append($" / ハンマーの支点のずれ最大 {MaxHammerShift:F3}m / 幽霊の上下最大 {MaxGhostShift:F3}m / ジャンプ {Jumps} / 着地 {Landings}");
                var keys = new List<int>(m_maxHeadSide.Keys);
                keys.Sort();
                sb.Append(" / 段数ごとの頭の横ずれ最大:");
                foreach (int k in keys) sb.Append($" {k}段={m_maxHeadSide[k]:F3}");
                sb.Append($" / 頭の傾き最大 {MaxHeadTilt:F1}度 / 一番上の段の傾き最大 {MaxTopBlockTilt:F1}度")
                  .Append($" / 隣り合う段の隙間の最小 {(MinLevelSpacing == float.MaxValue ? 0.0f : MinLevelSpacing):F3}m");
                return sb.ToString();
            }
        }

        // 段階を記録するキャラクター(最初に見つけた 1 人)
        DarumaCharacter m_first;
        private DarumaCharacter FindFirstCharacter()
        {
            if (m_first == null || !m_first.isActiveAndEnabled)
            {
                foreach (var c in FindObjectsByType<DarumaCharacter>())
                {
                    if (c.isActiveAndEnabled) { m_first = c; break; }
                }
            }
            return m_first;
        }

        private void LateUpdate()
        {
            Frames++;
            foreach (var c in FindObjectsByType<DarumaCharacter>())
            {
                if (!c.isActiveAndEnabled) continue;
                // 体の動きで足したずれだけを見る(拾ったときに段・頭が寄ってくる前からの動きは数えない)
                var motion = c.GetComponent<DarumaMotion>();
                if (motion == null) continue;
                DarumaStack s = c.Stack;
                for (int i = 0; i < s.Count; i++)
                {
                    Vector3 o = motion.GetOffset(TumikiBlock.kHeight * (i + 0.5f));
                    MaxBlockSide = Mathf.Max(MaxBlockSide, new Vector2(o.x, o.z).magnitude);
                }
                {
                    Vector3 o = motion.GetOffset(s.Count * TumikiBlock.kHeight + s.HeadRadius);
                    float side = new Vector2(o.x, o.z).magnitude;
                    m_maxHeadSide.TryGetValue(s.Count, out float old);
                    m_maxHeadSide[s.Count] = Mathf.Max(old, side);
                    MaxHeadRise = Mathf.Max(MaxHeadRise, o.y);
                }
                if (c.Hammer != null)
                {
                    Vector3 o = motion.GetOffset(0.6f);
                    MaxHammerShift = Mathf.Max(MaxHammerShift, o.magnitude);
                }
                // のけぞり:実際に置かれた段・頭の傾きと、隣り合う段の間隔
                for (int i = 0; i < s.Count; i++)
                {
                    Transform t = s.Get(i).transform;
                    float tilt = Quaternion.Angle(Quaternion.identity, t.localRotation);
                    if (i == s.Count - 1) MaxTopBlockTilt = Mathf.Max(MaxTopBlockTilt, tilt);
                    if (i > 0)
                    {
                        float d = Vector3.Distance(t.position, s.Get(i - 1).transform.position) - TumikiBlock.kHeight;
                        MinLevelSpacing = Mathf.Min(MinLevelSpacing, d);
                    }
                }
                if (s.Head != null)
                {
                    float headTilt = Vector3.Angle(s.Head.up, c.transform.up);
                    MaxHeadTilt = Mathf.Max(MaxHeadTilt, headTilt);
                }
                if (m_phaseLog.Length < 4000 && c == FindFirstCharacter())
                {
                    string phase = motion.JumpPhaseName;
                    if (phase != m_lastPhase)
                    {
                        m_phaseLog.Append($"{Time.time:F2}:{phase}{(c.IsGrounded ? "(地上)" : "(空中)")} ");
                        m_lastPhase = phase;
                    }
                }

                if (c == FindFirstCharacter() && Time.time >= m_nextTrace && m_trace.Length < 20000)
                {
                    m_nextTrace = Time.time + TraceInterval;
                    m_trace.Append($"{Time.time:F2} {(c.IsGrounded ? "地" : "空")} |");
                    for (int i = 1; i < s.Count; i++)
                    {
                        float gap = Vector3.Dot(s.Get(i).transform.position - s.Get(i - 1).transform.position, c.transform.up) - TumikiBlock.kHeight;
                        m_trace.Append($" {gap * 100.0f:F0}");
                    }
                    m_trace.Append(" |");
                    for (int i = 0; i < s.Count; i++)
                        m_trace.Append($" {Quaternion.Angle(Quaternion.identity, s.Get(i).transform.localRotation):F0}");
                    m_trace.Append('\n');
                }

                bool g = c.IsGrounded;
                if (m_grounded.TryGetValue(c, out bool was))
                {
                    if (was && !g) Jumps++;
                    if (!was && g) Landings++;
                }
                m_grounded[c] = g;
            }
            foreach (var gh in DarumaGhost.Active)
            {
                if (gh == null || gh.Visual == null) continue;
                foreach (Transform t in gh.Visual)
                {
                    if (t.name != "Head") continue;
                    MaxGhostShift = Mathf.Max(MaxGhostShift, Mathf.Abs(t.localPosition.y - 0.75f));
                }
            }
        }
    }
}
