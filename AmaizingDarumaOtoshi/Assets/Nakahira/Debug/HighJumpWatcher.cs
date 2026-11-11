using System.Collections.Generic;
using UnityEngine;

namespace Nakahira.DebugTools
{
    // 高く飛び上がる不具合の調査用。試合中の全キャラクターを見張り、
    // ジャンプの初速より速く上がった・最後に接地した高さからジャンプより高く上がったら Console に出す
    // (Unity MCP から HighJumpWatcher.Begin() で付ける)
    public class HighJumpWatcher : MonoBehaviour
    {
        public static HighJumpWatcher Instance { get; private set; }

        // ジャンプの高さにこれを足した高さを超えたら記録する(m)
        public float HeightMargin = 0.7f;
        public float MaxVyOverJump { get; private set; } = float.NegativeInfinity;
        public float MaxRise { get; private set; }
        public int Count { get; private set; }
        public int Steps { get; private set; }

        readonly Dictionary<DarumaCharacter, float> m_groundY = new Dictionary<DarumaCharacter, float>();
        readonly HashSet<DarumaCharacter> m_reported = new HashSet<DarumaCharacter>();

        public static HighJumpWatcher Begin()
        {
            if (Instance != null) Destroy(Instance.gameObject);
            var go = new GameObject("HighJumpWatcher");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<HighJumpWatcher>();
            return Instance;
        }

        public string Summary => $"見張ったステップ {Steps} / 上向き速度 - ジャンプ初速 の最大 {MaxVyOverJump:F2}m/s"
            + $" / 接地した高さからの最大の上昇 {MaxRise:F2}m / ジャンプより高く上がった回数 {Count}";

        private void FixedUpdate()
        {
            Steps++;
            foreach (var ch in FindObjectsByType<DarumaCharacter>(FindObjectsSortMode.None))
            {
                if (ch.Rigidbody == null || ch.JumpParams == null) continue;
                float y = ch.Rigidbody.position.y;
                if (ch.IsGrounded || !m_groundY.ContainsKey(ch))
                {
                    m_groundY[ch] = y;
                    m_reported.Remove(ch);
                }
                float vyOver = ch.Rigidbody.linearVelocity.y - ch.JumpParams.JumpSpeed;
                if (vyOver > MaxVyOverJump) MaxVyOverJump = vyOver;
                float rise = y - m_groundY[ch];
                if (rise > MaxRise) MaxRise = rise;
                if (rise > ch.JumpParams.Height + HeightMargin && m_reported.Add(ch))
                {
                    Count++;
                    if (Count <= 20)
                    {
                        Debug.LogWarning($"[HighJumpWatcher] {ch.name} 接地した高さから {rise:F2}m 上昇 y={y:F2}"
                            + $" vy={ch.Rigidbody.linearVelocity.y:F2} scale={ch.LocalTimeScale:F2}");
                    }
                }
            }
        }
    }
}
