using UnityEngine;

namespace Nakahira
{
    // ヒットストップの状態(残り時間・時間の倍率)と、見た目の震え
    // 震えは見た目用の子オブジェクト(m_visual)だけを動かすので、当たり判定は動かない
    public class HitStop : MonoBehaviour
    {
        [SerializeField] Transform m_visual;

        float m_timer;
        float m_timeScale = 1.0f;
        float m_shake;

        public bool IsActive => m_timer > 0.0f;
        // ヒットストップ中はその倍率、それ以外は1
        public float TimeScale => IsActive ? m_timeScale : 1.0f;

        // 重なった場合は、長い方の残り時間・強い方の効果を採用する
        public void Begin(float duration, float timeScale, float shake)
        {
            if (duration <= 0.0f) return;
            if (!IsActive)
            {
                m_timeScale = timeScale;
                m_shake = shake;
            }
            else
            {
                m_timeScale = Mathf.Min(m_timeScale, timeScale);
                m_shake = Mathf.Max(m_shake, shake);
            }
            m_timer = Mathf.Max(m_timer, duration);
        }

        // 実時間で進める(スローの長さ自体はスローにしない)
        private void FixedUpdate()
        {
            if (m_timer > 0.0f) m_timer -= Time.fixedDeltaTime;
        }

        private void LateUpdate()
        {
            if (m_visual == null) return;
            if (IsActive)
            {
                // 主に横方向に細かく揺らす
                Vector3 offset = Random.insideUnitSphere * m_shake;
                offset.y *= 0.3f;
                m_visual.localPosition = offset;
            }
            else
            {
                m_visual.localPosition = Vector3.zero;
            }
        }
    }
}
