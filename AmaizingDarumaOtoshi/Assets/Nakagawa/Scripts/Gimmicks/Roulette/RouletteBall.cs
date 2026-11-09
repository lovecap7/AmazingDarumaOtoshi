using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // ルーレットの球(Stage20)。重い球として、動きはすべて RouletteGame が決める(動かない Rigidbody で動かす)
    //  ・プレイヤー・積み木に押されても動かない。速く転がっているときに当たったプレイヤー・落ちている積み木は、進む向きへはじく
    //  ・飛んでいる積み木は壁と同じように跳ね返る(ITumikiReflector)
    //  ・見た目は進む向きに転がり、速いと軌跡が出る
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class RouletteBall : MonoBehaviour, ITumikiReflector
    {
        [SerializeField, Min(0.1f)] float m_radius = 0.6f;
        [SerializeField] Transform m_visual;
        [SerializeField] TrailRenderer m_trail;
        [SerializeField] float m_trailSpeed = 6.0f;

        [Header("はじく")]
        [SerializeField] float m_minHitSpeed = 3.0f;
        [Tooltip("プレイヤーをはじく速さ = 球の速さ × この倍率")]
        [SerializeField] float m_characterPowerScale = 0.9f;
        [SerializeField] float m_characterMinPower = 8.0f;
        [SerializeField] float m_characterMaxPower = 20.0f;
        [SerializeField] float m_blockPowerScale = 0.8f;
        [SerializeField] float m_upPower = 3.5f;
        [SerializeField] float m_hitCooldown = 0.4f;
        [SerializeField] GimmickBurstSettings m_hitEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 0.95f, 0.75f, 1.0f), ColorB = new Color(1.0f, 0.75f, 0.3f, 1.0f),
            Count = 20, Speed = new Vector2(3.0f, 8.0f), Size = new Vector2(0.06f, 0.15f), Lifetime = new Vector2(0.15f, 0.35f),
            Shape = GimmickBurstShape.Hemisphere, ShapeSize = new Vector3(0.2f, 0, 0), Stretch = true,
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.95f, 0.75f, 0.8f), ShockwaveRadius = 1.4f, ShockwaveTime = 0.25f,
        };

        Rigidbody m_rb;
        SphereCollider m_collider;
        Vector3 m_velocity;
        Vector3 m_lastPosition;
        bool m_hasLast;
        Vector3 m_visualScale = Vector3.one;
        readonly Dictionary<Object, float> m_lastHit = new Dictionary<Object, float>();

        public float Radius => m_radius;
        public Vector3 Position => m_rb != null ? m_rb.position : transform.position;
        // 直近のステップの速度
        public Vector3 Velocity => m_velocity;
        public bool Visible { get; private set; } = true;
        // プレイヤー・積み木をはじいた(相手、位置、はじいた速さ)
        public event System.Action<IGimmickAffectable, Vector3, float> Knocked;

        private void Awake()
        {
            m_rb = GetComponent<Rigidbody>();
            m_rb.isKinematic = true;
            m_rb.interpolation = RigidbodyInterpolation.Interpolate;
            m_collider = GetComponent<SphereCollider>();
            m_collider.radius = m_radius;
            if (m_visual != null) m_visualScale = m_visual.localScale;
        }

        // 物理の1ステップで position へ動かす(FixedUpdate から呼ぶ)
        public void MoveTo(Vector3 position)
        {
            if (m_hasLast) m_velocity = (position - m_lastPosition) / Mathf.Max(0.0001f, Time.fixedDeltaTime);
            m_lastPosition = position;
            m_hasLast = true;
            m_rb.MovePosition(position);
        }

        // すぐにその位置へ置く(速度なし)
        public void Place(Vector3 position)
        {
            m_rb.position = position;
            transform.position = position;
            m_lastPosition = position;
            m_hasLast = true;
            m_velocity = Vector3.zero;
            if (m_trail != null) m_trail.Clear();
        }

        // 見せる・隠す。collide: 当たり判定を持たせるか(手に持っている間などは持たせない)
        public void SetVisible(bool visible, bool collide)
        {
            Visible = visible;
            if (m_visual != null) m_visual.gameObject.SetActive(visible);
            m_collider.enabled = visible && collide;
            if (m_trail != null)
            {
                m_trail.emitting = false;
                if (!visible) m_trail.Clear();
            }
            if (!visible) m_velocity = Vector3.zero;
        }

        // 大きさの倍率(現れる・消える動き)
        public void SetScale(float s)
        {
            if (m_visual != null) m_visual.localScale = m_visualScale * s;
        }

        private void Update()
        {
            if (!Visible) return;
            Vector3 h = new Vector3(m_velocity.x, 0.0f, m_velocity.z);
            float speed = h.magnitude;
            if (m_visual != null && speed > 0.01f)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, h / speed);
                m_visual.Rotate(axis, speed * Time.deltaTime / m_radius * Mathf.Rad2Deg, Space.World);
            }
            if (m_trail != null) m_trail.emitting = speed > m_trailSpeed && m_collider.enabled;
        }

        private void OnCollisionEnter(Collision collision) => HandleContact(collision);
        private void OnCollisionStay(Collision collision) => HandleContact(collision);

        void HandleContact(Collision collision)
        {
            if (!Visible || GimmickClock.IsNetClient) return;
            // 飛んでいる積み木は積み木の側で跳ね返る
            var block = collision.collider.GetComponentInParent<TumikiBlock>();
            if (block != null && block.CurrentState == TumikiBlock.State.Flying) return;

            IGimmickAffectable target = GimmickUtil.Find(collision.collider);
            if (target == null || !(target is Object key)) return;
            Vector3 vel = new Vector3(m_velocity.x, 0.0f, m_velocity.z);
            float speed = vel.magnitude;
            if (speed < m_minHitSpeed) return;
            Vector3 to = target.Rigidbody.position - transform.position;
            to.y = 0.0f;
            if (to.sqrMagnitude < 0.0001f) to = vel;
            to.Normalize();
            if (Vector3.Dot(vel / speed, to) < 0.1f) return;
            if (m_lastHit.TryGetValue(key, out float last) && Time.time - last < m_hitCooldown) return;
            m_lastHit[key] = Time.time;
            if (m_lastHit.Count > 32) m_lastHit.Clear();

            Vector3 dir = (to + vel / speed).normalized;
            bool isBlock = target is TumikiBlock;
            float power = isBlock ? speed * m_blockPowerScale : Mathf.Clamp(speed * m_characterPowerScale, m_characterMinPower, m_characterMaxPower);
            target.AddKnockback(dir * power + Vector3.up * (isBlock ? 0.5f : m_upPower));
            if (!isBlock) target.StartHitStop(0.08f, 0.15f, 0.12f);

            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            float k = Mathf.InverseLerp(m_minHitSpeed, 20.0f, speed);
            GimmickBurst.Play(m_hitEffect, point, Quaternion.LookRotation(dir), Mathf.Lerp(0.6f, 1.2f, k));
            GimmickSound.Play(BilliardFx.Strike, Mathf.Lerp(0.2f, 0.45f, k), Random.Range(1.1f, 1.25f));
            if (!isBlock) BilliardFx.Shake(Mathf.Lerp(0.08f, 0.3f, k), 0.2f);
            Knocked?.Invoke(target, point, power);
        }
    }
}
