using UnityEngine;

namespace Nakagawa
{
    // 背景(StageBackdrop)の小物を、ステージの広さに合わせて外側へ置き直すための目印(背景の子に付ける)
    // ステージの中心から Direction の向きに進み、ステージの範囲の縁からさらに Margin だけ離れた所へ動かす
    // (高さ・向き・大きさは変えない)。ステージが大きくなっても小物が床と重ならない
    public class BackdropAnchor : MonoBehaviour
    {
        // ステージの中心から見た向き(XZ。x: 東、y: 北)
        [SerializeField] Vector2 m_direction = Vector2.up;
        // ステージの範囲の縁から離す距離(m)
        [SerializeField, Min(0.0f)] float m_margin = 6.0f;

        public Vector2 Direction => m_direction;
        public float Margin => m_margin;

        public void Set(Vector2 direction, float margin)
        {
            m_direction = direction;
            m_margin = margin;
        }

        // 中心 center・大きさ size(XZ)のステージの外側の位置へ動かす
        public void Fit(Vector3 center, Vector2 size)
        {
            Vector2 d = m_direction.sqrMagnitude > 0.0001f ? m_direction.normalized : Vector2.up;
            // 中心から d の向きに進んで範囲の縁に着くまでの距離
            float tx = Mathf.Abs(d.x) > 0.0001f ? size.x * 0.5f / Mathf.Abs(d.x) : float.PositiveInfinity;
            float tz = Mathf.Abs(d.y) > 0.0001f ? size.y * 0.5f / Mathf.Abs(d.y) : float.PositiveInfinity;
            float distance = Mathf.Min(tx, tz) + m_margin;
            Vector3 p = transform.position;
            transform.position = new Vector3(center.x + d.x * distance, p.y, center.z + d.y * distance);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1.0f, 0.7f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 1.0f);
        }
    }
}
