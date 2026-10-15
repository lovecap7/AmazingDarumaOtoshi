using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 飛んでいる積み木が、床の穴(溝)の向こう側の崖の側面に当たったとき、壁と同じように反射させる
    // 床すれすれを飛んで穴を渡ると、ほとんど落ちないうちに向こう側の床の上端の角に触れる。
    // TumikiBlock は上端付近の接触を「床どうしの継ぎ目」とみなして反射しないため、側面に押し付けられたまま摩擦で宙に止まってしまう。
    // 真下(と手前側)に床がない = 穴の上にいるなら継ぎ目ではないので、壁と同じように反射する
    // (積み木のプレハブは TumikiEdgeSlip が持つので、TumikiEdgeSlip が起動時に自動で付ける)
    [RequireComponent(typeof(TumikiBlock))]
    public class TumikiGapWallReflect : MonoBehaviour
    {
        // 法線の上下成分がこれ未満の面を横向きの側面とみなす
        [SerializeField, Range(0.0f, 1.0f)] float m_sideNormalY = 0.5f;
        // 中心に加えて、当たった面から手前へこれだけ離れた位置も床を調べる(細い継ぎ目の上にいるだけなら床の上とみなす)
        [SerializeField, Min(0.0f)] float m_backProbe = 0.3f;
        // 底からどこまで下を床として調べるか
        [SerializeField, Min(0.01f)] float m_probeDepth = 0.3f;
        [SerializeField] LayerMask m_floorMask = Physics.DefaultRaycastLayers;

        static readonly RaycastHit[] s_hits = new RaycastHit[8];

        TumikiBlock m_block;
        Collider m_collider;

        private void Awake()
        {
            m_block = GetComponent<TumikiBlock>();
            m_collider = GetComponent<Collider>();
        }

        private void OnCollisionEnter(Collision collision) => HandleContact(collision);

        // 側面に触れたまま押し付けられている場合もあるので、Stay でも処理する
        private void OnCollisionStay(Collision collision) => HandleContact(collision);

        private void HandleContact(Collision collision)
        {
            if (m_block.CurrentState != TumikiBlock.State.Flying || !m_block.CanBeAffected) return;
            if (!IsStaticSurface(collision.collider)) return;

            Vector3 dir = m_block.Direction;
            for (int i = 0; i < collision.contactCount; i++)
            {
                Vector3 normal = collision.GetContact(i).normal;
                if (Mathf.Abs(normal.y) >= m_sideNormalY) continue;
                normal.y = 0.0f;
                if (normal.sqrMagnitude < 0.0001f) continue;
                normal.Normalize();
                // 面に向かって進んでいるときだけ(反射後は離れる向きになるので何度も反射しない)
                if (Vector3.Dot(dir, normal) >= 0.0f) continue;
                // 床の上を滑っていて継ぎ目に触れただけなら、TumikiBlock の扱い(床として無視)に任せる
                if (IsOverFloor(normal)) return;

                m_block.SetFlyingVelocity(Vector3.Reflect(dir, normal).normalized * m_block.Speed);
                return;
            }
        }

        // 動かない当たり判定(床など)か。壁(ITumikiReflector)は TumikiBlock が反射させ、動く物はそれぞれの処理に任せる
        private static bool IsStaticSurface(Collider col)
        {
            if (col.attachedRigidbody != null) return false;
            if (col.GetComponentInParent<ITumikiReflector>() != null) return false;
            return col.GetComponentInParent<TumikiBlock>() == null && col.GetComponentInParent<DarumaCharacter>() == null;
        }

        // 中心と、当たった面から手前側の位置のどちらかの真下に床があるか
        private bool IsOverFloor(Vector3 wallNormal)
        {
            Vector3 center = transform.position;
            return HasFloor(center) || HasFloor(center + wallNormal * m_backProbe);
        }

        private bool HasFloor(Vector3 xz)
        {
            Vector3 origin = new Vector3(xz.x, transform.position.y, xz.z);
            float length = TumikiBlock.kHeight * 0.5f + m_probeDepth;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_hits, length, m_floorMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = s_hits[i].collider;
                if (c == m_collider) continue;
                if (IsStaticSurface(c)) return true;
            }
            return false;
        }
    }
}
