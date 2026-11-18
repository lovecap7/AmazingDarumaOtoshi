using Nakahira;
using UnityEngine;
using Yurube.Achievements;

namespace Yurube
{
    // だるきんぐのパッシブ: 積み木は2個までしか積めない
    // このキャラクターのスタックの上限(DarumaStack.SetMaxCount)を2段にする。他のキャラクターは10段のまま。
    // 上限で拾うと、ほかのキャラの10段と同じく一番上(古い段)が後ろにこぼれて地面に残る
    // (2026-11-18 中平: 前はこぼれた段を消していた。ほかのキャラと同じにこぼすことにした)
    [RequireComponent(typeof(DarumaCharacter))]
    public class DaruKingPassive : MonoBehaviour
    {
        [Tooltip("積める段数の上限")]
        [SerializeField, Min(1)] int m_maxStack = 2;
        [Tooltip("上限でこぼしたときにConsoleに出す(テスト用)")]
        [SerializeField] bool m_logLimit = true;

        DarumaCharacter m_character;
        bool m_reportedPassive;

        // 積める段数の上限(CPU が上限を越えて拾わないための読み取り用。CPU 用(中平))
        public int MaxStack => m_maxStack;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
        }

        private void OnEnable()
        {
            if (m_character == null || m_character.Stack == null) return;
            m_character.Stack.SetMaxCount(m_maxStack);
            Trim();
            m_character.Stack.Changed += OnStackChanged;
            DarumaCharacter.AnyPickedUp += OnAnyPickedUp;
        }

        private void OnDisable()
        {
            if (m_character != null && m_character.Stack != null)
                m_character.Stack.Changed -= OnStackChanged;
            DarumaCharacter.AnyPickedUp -= OnAnyPickedUp;
        }

        void OnStackChanged()
        {
            Trim();
        }

        // 上限で拾って古い段がこぼれた(拾っても段が増えなかった)
        void OnAnyPickedUp(TumikiPickupInfo info)
        {
            if (info.Character != m_character || info.StackGrew) return;
            ReportLimit();
        }

        // ほかの経路(上限をあとから変えられた など)で上限を越えていたら、越えた分を後ろへこぼす
        void Trim()
        {
            if (m_character == null || m_character.Stack == null) return;
            if (SpecialNet.IsClient) return;   // ネット対戦用(suehara): 積み木を落とすのはホストだけ

            bool trimmed = false;
            while (m_character.Stack.Count > m_maxStack)
            {
                var block = m_character.Stack.RemoveAt(m_character.Stack.Count - 1);
                if (block != null)
                {
                    block.transform.position = transform.position - transform.forward * 0.9f + Vector3.up * (TumikiBlock.kHeight * 0.5f);
                    var rb = block.GetComponent<Rigidbody>();
                    if (rb != null) rb.linearVelocity = -transform.forward * 2.0f + Vector3.up * 1.0f;
                }
                trimmed = true;
            }
            if (trimmed) ReportLimit();
        }

        void ReportLimit()
        {
            if (m_logLimit)
                Debug.Log($"[だるきんぐ] 積み木は{m_maxStack}段まで。古い段をこぼした", this);
            if (!m_reportedPassive)
            {
                m_reportedPassive = true;
                AchievementEvents.RaisePassiveTriggered(m_character);
            }
        }
    }
}
