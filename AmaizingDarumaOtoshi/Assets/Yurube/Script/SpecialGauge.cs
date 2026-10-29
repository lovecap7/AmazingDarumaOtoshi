using Nakahira;
using UnityEngine;

namespace Yurube
{
    // 全キャラ共通の必殺技ゲージ。積み木が増えた分だけ溜まる。
    // 必殺技を持つキャラクターのプレハブに付け、必殺技側から IsFull / Consume を読む。
    [RequireComponent(typeof(DarumaCharacter))]
    public class SpecialGauge : MonoBehaviour
    {
        [Tooltip("満タンになるまでに積む積み木の数")]
        [SerializeField, Min(1)] int m_blocksToFill = 5;

        DarumaCharacter m_character;
        int m_blocks;
        int m_lastCount;

        public int Blocks => m_blocks;
        public int BlocksToFill => m_blocksToFill;
        public bool IsFull => m_blocks >= m_blocksToFill;
        public float Normalized => Mathf.Clamp01((float)m_blocks / m_blocksToFill);

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
        }

        private void OnEnable()
        {
            if (m_character == null || m_character.Stack == null) return;
            // 最初から積まれている場合もその分を数える
            m_lastCount = 0;
            Add(m_character.Stack.Count);
            m_character.Stack.Changed += OnStackChanged;
        }

        private void OnDisable()
        {
            if (m_character != null && m_character.Stack != null)
                m_character.Stack.Changed -= OnStackChanged;
        }

        void OnStackChanged()
        {
            if (m_character == null || m_character.Stack == null) return;
            int count = m_character.Stack.Count;
            int delta = count - m_lastCount;
            m_lastCount = count;
            if (delta > 0) Add(delta);
        }

        public void Add(int blocks)
        {
            if (blocks <= 0) return;
            m_blocks = Mathf.Min(m_blocksToFill, m_blocks + blocks);
        }

        public void Fill()
        {
            m_blocks = m_blocksToFill;
        }

        public void Consume()
        {
            m_blocks = 0;
        }
    }
}
