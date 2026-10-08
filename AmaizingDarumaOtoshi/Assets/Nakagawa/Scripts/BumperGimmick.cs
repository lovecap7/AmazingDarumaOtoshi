using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // バンパー。触れたプレイヤー・積み木を中心から外向きに一定の強さではじく円盤
    // 子のコライダーの衝突を受け取るため、このGameObjectに動かないRigidbodyを付ける
    [RequireComponent(typeof(Rigidbody))]
    public class BumperGimmick : MonoBehaviour
    {
        [Header("はじく強さ")]
        // プレイヤーをはじく水平の速さ
        [SerializeField] float m_characterPower = 12.0f;
        // 積み木をはじく水平の速さ(積み木は弾と同じく直進し、速いと攻撃判定を持つ)
        [SerializeField] float m_blockPower = 8.0f;
        // 上向きに浮かせる速さ
        [SerializeField] float m_upPower = 2.0f;
        // 同じ相手を続けてはじかない時間
        [SerializeField] float m_cooldown = 0.2f;

        [Header("見た目")]
        // はじいたときに膨らむ部分
        [SerializeField] Transform m_visual;
        [SerializeField] Renderer m_renderer;
        [SerializeField] Color m_flashColor = Color.white;
        [SerializeField] float m_punchScale = 1.2f;
        [SerializeField] float m_punchTime = 0.15f;

        readonly Dictionary<IGimmickAffectable, float> m_lastHitTime = new Dictionary<IGimmickAffectable, float>();
        Vector3 m_visualScale = Vector3.one;
        Color m_baseColor = Color.white;
        float m_punchTimer;
        MaterialPropertyBlock m_mpb;

        private void Awake()
        {
            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            if (m_visual != null) m_visualScale = m_visual.localScale;
            if (m_renderer != null && m_renderer.sharedMaterial != null && m_renderer.sharedMaterial.HasProperty("_BaseColor"))
            {
                m_baseColor = m_renderer.sharedMaterial.GetColor("_BaseColor");
            }
            m_mpb = new MaterialPropertyBlock();
        }

        private void OnCollisionEnter(Collision collision)
        {
            Bounce(collision);
        }

        // 押し付けたまま触れ続けている場合もはじく
        private void OnCollisionStay(Collision collision)
        {
            Bounce(collision);
        }

        private void Bounce(Collision collision)
        {
            IGimmickAffectable target = GimmickUtil.Find(collision.collider);
            if (target == null) return;
            if (m_lastHitTime.TryGetValue(target, out float last) && Time.time - last < m_cooldown) return;
            m_lastHitTime[target] = Time.time;

            // 中心から相手へ向かう水平方向にはじく(真上に乗った場合は当たった面の向き)
            Vector3 dir = target.Rigidbody.position - transform.position;
            dir.y = 0.0f;
            if (dir.sqrMagnitude < 0.0001f) dir = -collision.GetContact(0).normal;
            dir.y = 0.0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();

            float power = target is TumikiBlock ? m_blockPower : m_characterPower;
            target.AddKnockback(dir * power + Vector3.up * m_upPower);
            m_punchTimer = m_punchTime;
        }

        private void Update()
        {
            if (m_punchTimer <= 0.0f && m_lastHitTime.Count > 0) CleanUp();

            float t = m_punchTime > 0.0f ? Mathf.Clamp01(m_punchTimer / m_punchTime) : 0.0f;
            m_punchTimer = Mathf.Max(0.0f, m_punchTimer - Time.deltaTime);

            // 一瞬膨らんで光る
            if (m_visual != null)
            {
                float s = Mathf.Lerp(1.0f, m_punchScale, Mathf.Sin(t * Mathf.PI));
                m_visual.localScale = new Vector3(m_visualScale.x * s, m_visualScale.y, m_visualScale.z * s);
            }
            if (m_renderer != null)
            {
                m_renderer.GetPropertyBlock(m_mpb);
                m_mpb.SetColor("_BaseColor", Color.Lerp(m_baseColor, m_flashColor, t));
                m_renderer.SetPropertyBlock(m_mpb);
            }
        }

        // 破棄された相手の記録を消す
        private void CleanUp()
        {
            List<IGimmickAffectable> dead = null;
            foreach (var key in m_lastHitTime.Keys)
            {
                if (GimmickUtil.IsAlive(key)) continue;
                (dead ??= new List<IGimmickAffectable>()).Add(key);
            }
            if (dead == null) return;
            foreach (var key in dead) m_lastHitTime.Remove(key);
        }
    }
}
