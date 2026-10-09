using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 床の縁に引っかかった積み木を、縁の外へ滑らせて自然に落とす(積み木のプレハブに付ける)
    // 積み木は倒れないよう回転を固定しているため、中心が床の外に出ても底の一部が床に乗っていると傾かずに止まってしまう
    // (床どうしが重なる角や、壁のないステージの端で起きる)
    // 中心の真下に床がなく、底の外周の一部だけが支えられているとき、支えられていない側へ加速させる
    [RequireComponent(typeof(TumikiBlock))]
    public class TumikiEdgeSlip : MonoBehaviour
    {
        // 縁の外へ押し出す加速度(床との摩擦に勝てる強さ)
        [SerializeField] float m_slipAcceleration = 25.0f;
        // 底の外周を調べる点の数
        [SerializeField, Min(4)] int m_rimSamples = 8;
        // 底より下をどこまで床として調べるか
        [SerializeField] float m_probeDepth = 0.15f;
        [SerializeField] LayerMask m_floorMask = Physics.DefaultRaycastLayers;

        static readonly RaycastHit[] s_hits = new RaycastHit[8];

        TumikiBlock m_block;
        Collider m_collider;

        private void Awake()
        {
            m_block = GetComponent<TumikiBlock>();
            m_collider = GetComponent<Collider>();
        }

        private void FixedUpdate()
        {
            if (!m_block.CanBeAffected || m_block.CurrentState == TumikiBlock.State.Stacked) return;
            if (m_block.Rigidbody == null || m_block.Rigidbody.isKinematic) return;
            if (!TryGetSlipDirection(out Vector3 dir)) return;

            // 落ちている積み木は物理の力で、飛んでいる積み木は進む向きに外向きの力が加わる(ギミックと同じ仕組み)
            m_block.AddForce(dir * m_slipAcceleration);
        }

        // 中心の真下に床がなく、外周の一部が支えられていれば、支えのない側の向きを返す
        private bool TryGetSlipDirection(out Vector3 dir)
        {
            dir = Vector3.zero;
            Vector3 center = transform.position;
            float bottom = center.y - TumikiBlock.kHeight * 0.5f;
            if (HasFloor(center, bottom)) return false;

            Vector3 supported = Vector3.zero;
            int count = 0;
            float r = TumikiBlock.kRadius * 0.95f;
            for (int i = 0; i < m_rimSamples; i++)
            {
                float a = i * Mathf.PI * 2.0f / m_rimSamples;
                Vector3 offset = new Vector3(Mathf.Cos(a), 0.0f, Mathf.Sin(a)) * r;
                if (!HasFloor(center + offset, bottom)) continue;
                supported += offset;
                count++;
            }
            // どこも支えられていなければ普通に落ちている
            if (count == 0 || supported.sqrMagnitude < 0.0001f) return false;

            dir = -supported.normalized;
            return true;
        }

        // 底の少し上から下へ調べ、自分以外の当たり判定があれば床とみなす
        private bool HasFloor(Vector3 xz, float bottom)
        {
            Vector3 origin = new Vector3(xz.x, bottom + 0.05f, xz.z);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_hits, 0.05f + m_probeDepth, m_floorMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (s_hits[i].collider != m_collider) return true;
            }
            return false;
        }
    }
}
