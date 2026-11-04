using Nakahira;
using UnityEngine;

namespace Yurube
{
    // へびだるの毒沼デバフ。
    // 1キャラにつき1個だけ付け、残り時間は「長い方」、速度倍率は「低い方」を採用する。
    // 毎フレームの再付与でタイマーや速度低下が累積しない。
    public class HebiPoisonDebuff : MonoBehaviour
    {
        DarumaCharacter m_target;
        float m_remaining;
        float m_slowMultiplier = 1.0f;
        bool m_active;

        public static void Apply(DarumaCharacter target, float duration, float slowMultiplier)
        {
            if (target == null) return;

            var debuff = target.GetComponent<HebiPoisonDebuff>();
            if (debuff == null) debuff = target.gameObject.AddComponent<HebiPoisonDebuff>();
            debuff.ApplyInternal(duration, slowMultiplier);
        }

        public static void Clear(DarumaCharacter target)
        {
            if (target == null) return;
            var debuff = target.GetComponent<HebiPoisonDebuff>();
            if (debuff != null) debuff.End();
        }

        private void Awake()
        {
            m_target = GetComponent<DarumaCharacter>();
        }

        void ApplyInternal(float duration, float slowMultiplier)
        {
            if (m_target == null) m_target = GetComponent<DarumaCharacter>();
            if (m_target == null) return;

            m_remaining = Mathf.Max(m_remaining, Mathf.Max(0.0f, duration));
            m_slowMultiplier = Mathf.Min(m_slowMultiplier, Mathf.Clamp01(slowMultiplier));
            m_active = true;

            m_target.MovementInverted = true;
            m_target.MoveSpeedMultiplier = Mathf.Min(m_target.MoveSpeedMultiplier, m_slowMultiplier);
            enabled = true;
        }

        private void Update()
        {
            if (!m_active) return;

            m_remaining -= Time.deltaTime;
            if (m_remaining <= 0.0f) End();
        }

        private void OnDisable()
        {
            Restore();
            m_active = false;
        }

        private void OnDestroy()
        {
            Restore();
        }

        void End()
        {
            if (!m_active) return;
            Restore();
            m_active = false;
            // 同じフレームで毒沼に再入しても取りこぼさないよう、コンポーネントは残して再利用する
        }

        void Restore()
        {
            if (m_target == null)
            {
                m_slowMultiplier = 1.0f;
                return;
            }
            m_target.MovementInverted = false;
            m_target.MoveSpeedMultiplier = 1.0f;
            m_slowMultiplier = 1.0f;
        }
    }
}
