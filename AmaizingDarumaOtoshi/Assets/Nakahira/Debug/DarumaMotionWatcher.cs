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
                return sb.ToString();
            }
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
