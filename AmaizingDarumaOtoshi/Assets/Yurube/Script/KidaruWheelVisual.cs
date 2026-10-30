using Nakahira;
using UnityEngine;

namespace Yurube
{
    // きだる必殺技中だけ、だるまの頭を一番下に置き、積み木をその上に並べる。
    // 頭は移動速度に応じて車輪のように回転する。終了時は DarumaStack の通常配置へ戻す。
    [DefaultExecutionOrder(1000)]
    public class KidaruWheelVisual : MonoBehaviour
    {
        DarumaCharacter m_character;
        DarumaStack m_stack;
        Transform m_head;
        Quaternion m_headOriginalRotation;
        bool m_active;
        float m_rollAngle;

        private void Awake()
        {
            Resolve();
        }

        // DarumaStack はルートではなく Stack 子オブジェクトに付いているため、
        // DarumaCharacter.Stack から参照する。生成タイミングに備えて遅延解決もする。
        void Resolve()
        {
            if (m_character == null) m_character = GetComponent<DarumaCharacter>();
            if (m_stack == null && m_character != null) m_stack = m_character.Stack;

            if (m_head == null)
            {
                foreach (var t in GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Head")
                    {
                        m_head = t;
                        break;
                    }
                }
            }
        }

        public void Begin()
        {
            if (m_active) return;

            Resolve();
            m_active = true;
            m_rollAngle = 0.0f;
            if (m_head != null) m_headOriginalRotation = m_head.localRotation;
        }

        public void End()
        {
            if (!m_active) return;

            m_active = false;

            // DarumaStack は位置を毎LateUpdateで戻すが、回転は戻さないのでここで戻す
            if (m_head != null) m_head.localRotation = m_headOriginalRotation;
            if (m_stack != null)
            {
                for (int i = 0; i < m_stack.Count; i++)
                {
                    var block = m_stack.Get(i);
                    if (block != null) block.transform.localRotation = Quaternion.identity;
                }
            }
        }

        private void LateUpdate()
        {
            if (!m_active) return;
            if (m_character == null || m_stack == null) Resolve();
            if (m_character == null || m_stack == null) return;

            Vector3 rootPosition = m_character.transform.position;
            Quaternion rootRotation = m_character.transform.rotation;
            float radius = m_stack.HeadRadius;
            float height = TumikiBlock.kHeight;

            // 頭を一番下に置く
            if (m_head != null)
            {
                m_head.position = rootPosition + Vector3.up * radius;
                m_head.rotation = rootRotation * Quaternion.Euler(m_rollAngle, 0.0f, 0.0f);
            }

            // 積み木は頭の上に積む
            for (int i = 0; i < m_stack.Count; i++)
            {
                var block = m_stack.Get(i);
                if (block == null) continue;

                block.transform.position = rootPosition
                    + Vector3.up * (radius * 2.0f + height * (i + 0.5f));
                block.transform.rotation = rootRotation;
            }

            // 実際の移動速度に応じて頭を車輪のように転がす
            Vector3 velocity = m_character.Rigidbody != null
                ? m_character.Rigidbody.linearVelocity
                : Vector3.zero;
            float forwardSpeed = Vector3.Dot(
                new Vector3(velocity.x, 0.0f, velocity.z),
                m_character.transform.forward);
            m_rollAngle = Mathf.Repeat(
                m_rollAngle + forwardSpeed * Time.deltaTime / Mathf.Max(0.01f, radius) * Mathf.Rad2Deg,
                360.0f);
        }

        private void OnDisable()
        {
            End();
        }

        private void OnDestroy()
        {
            End();
        }
    }
}
