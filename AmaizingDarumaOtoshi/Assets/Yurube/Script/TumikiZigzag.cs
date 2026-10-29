using Nakahira;
using UnityEngine;

namespace Yurube
{
    // へびだるの「蛇行」を付ける積み木側のコンポーネント。
    // 飛んでいる間だけ、一定間隔で左右に切り返して軌道をS字にする。
    // SetFlyingVelocity で向きを書き換えるので、反射・当たり判定・連鎖も更新後の向きで計算される。
    [RequireComponent(typeof(TumikiBlock))]
    public class TumikiZigzag : MonoBehaviour
    {
        [Tooltip("左右に切り返す間隔(秒)")]
        [SerializeField] float m_interval = 0.18f;
        [Tooltip("1秒あたりに曲がる角度(度/秒)")]
        [SerializeField] float m_turnSpeed = 200.0f;

        TumikiBlock m_block;
        DarumaCharacter m_shooter;
        float m_timer;
        int m_side = 1;
        bool m_active;

        private void Awake()
        {
            m_block = GetComponent<TumikiBlock>();
            enabled = false;
        }

        // へびだるのショット時に呼ぶ。撃った本人の積み木の間だけ蛇行させる
        public void Begin(DarumaCharacter shooter, float interval, float turnSpeed)
        {
            m_shooter = shooter;
            m_interval = Mathf.Max(0.01f, interval);
            m_turnSpeed = turnSpeed;
            m_timer = 0.0f;
            m_side = 1;
            m_active = true;
            enabled = true;
        }

        private void FixedUpdate()
        {
            if (!m_active) return;

            // 飛んでいない・持ち主が変わった(他の人に打ち返された)なら蛇行をやめる
            if (m_block == null
                || m_block.CurrentState != TumikiBlock.State.Flying
                || !ReferenceEquals(m_block.Owner, m_shooter))
            {
                m_active = false;
                enabled = false;
                return;
            }

            m_timer += Time.fixedDeltaTime;
            if (m_timer >= m_interval)
            {
                m_timer -= m_interval;
                m_side = -m_side;
            }

            Vector3 direction = m_block.Direction;
            if (direction.sqrMagnitude < 0.0001f) return;

            float angle = m_side * m_turnSpeed * Time.fixedDeltaTime;
            Vector3 turned = (Quaternion.AngleAxis(angle, Vector3.up) * direction).normalized;
            m_block.SetFlyingVelocity(turned * m_block.Speed);
        }
    }
}
