using Nakahira;
using UnityEngine;

namespace Yurube
{
    // だるきんぐのパッシブ: 積み木は2個までしか積めない
    // 共有の DarumaStack は変えず、このキャラクターだけスタックの変化を見て、
    // 2段を超えたら一番上(古い段)を落とす。他のキャラクターは10段のまま
    [RequireComponent(typeof(DarumaCharacter))]
    public class DaruKingPassive : MonoBehaviour
    {
        [Tooltip("積める段数の上限")]
        [SerializeField, Min(1)] int m_maxStack = 2;
        [Tooltip("上限を超えて落としたときにConsoleに出す(テスト用)")]
        [SerializeField] bool m_logLimit = true;

        DarumaCharacter m_character;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
        }

        private void OnEnable()
        {
            if (m_character == null || m_character.Stack == null) return;
            Trim();
            m_character.Stack.Changed += OnStackChanged;
        }

        private void OnDisable()
        {
            if (m_character != null && m_character.Stack != null)
                m_character.Stack.Changed -= OnStackChanged;
        }

        void OnStackChanged()
        {
            Trim();
        }

        void Trim()
        {
            if (m_character == null || m_character.Stack == null) return;

            bool trimmed = false;
            while (m_character.Stack.Count > m_maxStack)
            {
                // 一番上(古い段)を落とす。地面に残すと拾い直して繰り返すので消す
                var block = m_character.Stack.RemoveAt(m_character.Stack.Count - 1);
                if (block != null)
                {
                    // 消えるまでの1フレームだけ、拾い直されないようにする
                    block.gameObject.SetActive(false);
                    Destroy(block.gameObject);
                }
                trimmed = true;
            }

            if (trimmed && m_logLimit)
                Debug.Log($"[だるきんぐ] 積み木は{m_maxStack}段まで。超えた分を落とした", this);
        }
    }
}
