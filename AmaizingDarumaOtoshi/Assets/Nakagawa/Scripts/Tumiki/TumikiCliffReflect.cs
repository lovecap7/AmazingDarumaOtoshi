using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 飛んでいる積み木を、壁のない床の縁(崖)で壁と同じように反射させる
    // 床の上を進んでいて、次のステップで底の外周が床の外へはみ出すなら、はみ出す側を壁とみなして跳ね返す
    // (積み木のプレハブは TumikiEdgeSlip が持つので、TumikiEdgeSlip が起動時に自動で付ける)
    [RequireComponent(typeof(TumikiBlock))]
    public class TumikiCliffReflect : MonoBehaviour
    {
        // 底の外周を調べる点の数
        [SerializeField, Min(8)] int m_rimSamples = 16;
        // 縁からどこまで手前で跳ね返るか(積み木の半径なら、外周が縁に触れたところで跳ね返る)
        [SerializeField] float m_edgeMargin = TumikiBlock.kRadius;
        // 中心から下をどこまで床として調べるか(打ち出した直後に空中にいても、真下に床があれば床の上とみなす)
        [SerializeField] float m_probeDepth = 3.0f;
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
            if (m_block.CurrentState != TumikiBlock.State.Flying || !m_block.CanBeAffected) return;
            if (m_block.Rigidbody == null || m_block.Rigidbody.isKinematic) return;

            Vector3 dir = m_block.Direction;
            float speed = m_block.Speed;
            if (dir.sqrMagnitude < 0.0001f || speed <= 0.0f) return;

            // 既に床の外にいる(縁から落ちている途中)なら何もしない
            Vector3 center = transform.position;
            if (!HasFloor(center)) return;

            // 次のステップの位置で、外周のうち床のない側を調べる
            Vector3 next = center + dir * (speed * Time.fixedDeltaTime);
            if (!TryGetOutward(next, out Vector3 outward)) return;

            // 縁に向かって進んでいるときだけ反射する(反射後は離れる向きになるので何度も反射しない)
            if (Vector3.Dot(dir, outward) <= 0.0f) return;
            Vector3 reflected = Vector3.Reflect(dir, outward).normalized;
            m_block.SetFlyingVelocity(reflected * speed);
        }

        // 外周の点のうち床がないものの向きの平均(床のない側)。すべて床の上ならfalse
        private bool TryGetOutward(Vector3 center, out Vector3 outward)
        {
            outward = Vector3.zero;
            int missing = 0;
            for (int i = 0; i < m_rimSamples; i++)
            {
                float a = i * Mathf.PI * 2.0f / m_rimSamples;
                Vector3 offset = new Vector3(Mathf.Cos(a), 0.0f, Mathf.Sin(a));
                if (HasFloor(center + offset * m_edgeMargin)) continue;
                outward += offset;
                missing++;
            }
            if (missing == 0) return false;
            // 床が細く両側とも外に出る場合などは向きが決まらないので、進む向きの逆に返す
            if (outward.sqrMagnitude < 0.0001f) outward = m_block.Direction;
            outward.Normalize();
            return true;
        }

        // 中心の高さから下へ調べ、動かない当たり判定(床・壁)があれば床とみなす
        private bool HasFloor(Vector3 xz)
        {
            Vector3 origin = new Vector3(xz.x, transform.position.y, xz.z);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_hits, m_probeDepth, m_floorMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = s_hits[i].collider;
                if (c == m_collider) continue;
                // 他の積み木・キャラクターなど動く物は床とみなさない
                Rigidbody rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;
                if (c.GetComponentInParent<TumikiBlock>() != null || c.GetComponentInParent<DarumaCharacter>() != null) continue;
                return true;
            }
            return false;
        }
    }
}
