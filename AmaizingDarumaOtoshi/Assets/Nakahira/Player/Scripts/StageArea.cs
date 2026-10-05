using UnityEngine;

namespace Nakahira
{
    // ステージの範囲(XZ)。幽霊の移動範囲や、脱落位置の補正に使う
    // このTransformの位置が範囲の中心、Yが床の高さ
    public class StageArea : MonoBehaviour
    {
        [SerializeField] Vector2 m_size = new Vector2(20.0f, 20.0f);

        public float FloorY => transform.position.y;

        // 範囲内に収めた位置を返す(Yは床の高さ)
        public Vector3 Clamp(Vector3 pos, float margin = 0.0f)
        {
            Vector3 c = transform.position;
            float hx = Mathf.Max(0.0f, m_size.x * 0.5f - margin);
            float hz = Mathf.Max(0.0f, m_size.y * 0.5f - margin);
            return new Vector3(
                Mathf.Clamp(pos.x, c.x - hx, c.x + hx),
                FloorY,
                Mathf.Clamp(pos.z, c.z - hz, c.z + hz));
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.6f, 0.4f, 1.0f, 0.8f);
            Gizmos.DrawWireCube(transform.position, new Vector3(m_size.x, 0.05f, m_size.y));
        }
    }
}
