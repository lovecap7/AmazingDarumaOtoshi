using Nakahira;
using UnityEngine;

namespace Yurube.Lab
{
    // ラボのダミー。操作入力を持たない実キャラクターで、既定では無敵。
    // 攻撃の当たり・積み木の連鎖・演出を、CPUの動きに邪魔されずに見るための的。
    public class CharacterLabDummy : MonoBehaviour
    {
        const float kInvincibleTime = 999999.0f;

        DarumaCharacter m_character;
        Vector3 m_home;
        Quaternion m_homeRotation;
        bool m_invincible = true;

        public DarumaCharacter Character => m_character;
        public bool Invincible => m_invincible;

        public void Initialize(DarumaCharacter character, bool invincible)
        {
            m_character = character;
            m_home = transform.position;
            m_homeRotation = transform.rotation;
            SetInvincible(invincible);
        }

        public void SetInvincible(bool invincible)
        {
            m_invincible = invincible;
            if (m_character != null) m_character.SetInvincible(invincible ? kInvincibleTime : 0.0f);
        }

        public void ResetPosition()
        {
            if (m_character == null) return;
            var rb = m_character.Rigidbody;
            if (rb != null)
            {
                rb.position = m_home;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            m_character.transform.SetPositionAndRotation(m_home, m_homeRotation);
            SetInvincible(m_invincible);
        }

        private void FixedUpdate()
        {
            // 無敵は時間で切れるので、ラボ中は切れないように掛け直す
            if (m_invincible && m_character != null && !m_character.IsInvincible)
                m_character.SetInvincible(kInvincibleTime);
        }
    }
}
