using Nakahira;
using UnityEngine;

namespace Yurube
{
    // へびだるのパッシブ: 股抜きショットで飛ばした自分の積み木が、左右に蛇行する
    // 「ショットでスタックが1段減った瞬間」だけを拾うので、
    // ハンマーでステージ上の積み木を飛ばしたときは発動しない
    [RequireComponent(typeof(DarumaCharacter))]
    public class HebiPassive : MonoBehaviour
    {
        [Tooltip("左右に切り返す間隔(秒)")]
        [SerializeField] float m_interval = 0.18f;
        [Tooltip("1秒あたりに曲がる角度(度/秒)")]
        [SerializeField] float m_turnSpeed = 200.0f;
        [Tooltip("ショットした積み木を探す距離(m)")]
        [SerializeField] float m_searchRadius = 3.0f;
        [Tooltip("ショット時にConsoleに出す(テスト用)")]
        [SerializeField] bool m_logSnake = true;

        DarumaCharacter m_character;
        DarumaHammer m_hammer;
        int m_lastCount;
        bool m_pendingShot;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_hammer = m_character != null && m_character.Hammer != null
                ? m_character.Hammer
                : GetComponentInChildren<DarumaHammer>();
        }

        private void OnEnable()
        {
            if (m_character == null || m_character.Stack == null) return;
            m_lastCount = m_character.Stack.Count;
            m_character.Stack.Changed += OnStackChanged;
        }

        private void OnDisable()
        {
            if (m_character != null && m_character.Stack != null)
                m_character.Stack.Changed -= OnStackChanged;
        }

        // ショットは「ハンマーがショット中」に「スタックが減る」ので、それだけで判別できる。
        // 被弾で減ったときはハンマーがショット中でない(または減った積み木の持ち主が自分でない)ため拾わない。
        void OnStackChanged()
        {
            if (m_character == null || m_character.Stack == null) return;
            int count = m_character.Stack.Count;
            bool shot = count < m_lastCount && m_hammer != null && m_hammer.IsShooting;
            m_lastCount = count;
            if (shot) m_pendingShot = true;
        }

        private void FixedUpdate()
        {
            if (!m_pendingShot) return;
            m_pendingShot = false;
            if (m_character == null) return;

            var block = FindShotBlock();
            if (block == null) return;

            var zigzag = block.GetComponent<TumikiZigzag>();
            if (zigzag == null) zigzag = block.gameObject.AddComponent<TumikiZigzag>();
            zigzag.Begin(m_character, m_interval, m_turnSpeed);

            if (m_logSnake) Debug.Log("[へびだる] ショットの積み木を蛇行させる", this);
        }

        // 自分の正面近くを飛んでいる積み木(ショットしたもの)を探す
        TumikiBlock FindShotBlock()
        {
            Vector3 origin = m_character.transform.position;
            Vector3 forward = m_character.transform.forward;
            float best = m_searchRadius;
            TumikiBlock found = null;

            foreach (var block in FindObjectsByType<TumikiBlock>())
            {
                if (block == null || block.CurrentState != TumikiBlock.State.Flying) continue;
                if (!ReferenceEquals(block.Owner, m_character)) continue;

                Vector3 delta = block.transform.position - origin;
                delta.y = 0.0f;
                float distance = delta.magnitude;
                if (distance > best) continue;
                // 正面から大きく外れたものは別の積み木なので除外
                if (distance > 0.01f && Vector3.Dot(delta / distance, forward) < 0.2f) continue;

                best = distance;
                found = block;
            }
            return found;
        }
    }
}
