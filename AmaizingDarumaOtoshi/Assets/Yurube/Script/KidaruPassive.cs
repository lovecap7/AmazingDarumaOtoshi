using Nakahira;
using UnityEngine;

namespace Yurube
{
    // きだるのパッシブ: 黄色い積み木が積まれると、同じ黄色い積み木がもう1個増える
    // 拾ったときだけでなく、デバッグや他の経路で積まれたときも反応するよう、
    // スタックの変化(黄色の数が増えたか)で判定する
    // 増えた分も普通の積み木なので、段数・移動速度・崩され方などは他の積み木と同じ扱いになる
    [RequireComponent(typeof(DarumaCharacter))]
    public class KidaruPassive : MonoBehaviour
    {
        [Tooltip("増やす積み木のプレハブ(Tumiki)")]
        [SerializeField] GameObject m_blockPrefab;
        [Tooltip("増やしたときにConsoleに出す(テスト用)")]
        [SerializeField] bool m_logExtra = true;

        DarumaCharacter m_character;
        int m_lastYellow;
        bool m_adding;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
        }

        private void OnEnable()
        {
            if (m_character == null || m_character.Stack == null) return;
            m_lastYellow = CountYellow();
            m_character.Stack.Changed += OnStackChanged;
        }

        private void OnDisable()
        {
            if (m_character != null && m_character.Stack != null)
                m_character.Stack.Changed -= OnStackChanged;
        }

        void OnStackChanged()
        {
            if (m_character == null) return;
            int yellow = CountYellow();
            bool gained = yellow > m_lastYellow;
            m_lastYellow = yellow;
            if (!gained || m_adding) return;

            var stack = m_character.Stack;
            if (m_blockPrefab == null || stack == null || stack.Count >= stack.MaxCount) return;

            m_adding = true;
            var go = Instantiate(m_blockPrefab, m_character.transform.position, m_character.transform.rotation);
            var block = go.GetComponent<TumikiBlock>();
            if (block == null)
            {
                Destroy(go);
                m_adding = false;
                return;
            }

            block.SetColor(TumikiColor.Yellow);
            var overflow = stack.AddBottom(block);
            if (overflow != null) Destroy(overflow.gameObject);
            m_adding = false;
            m_lastYellow = CountYellow();

            if (m_logExtra) Debug.Log($"[きだる] 黄色を積んだので1個追加 (合計 {stack.Count}段)");
        }

        int CountYellow()
        {
            var stack = m_character != null ? m_character.Stack : null;
            return stack != null ? stack.CountColor(TumikiColor.Yellow) : 0;
        }
    }
}
