using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // ビリヤードの玉(手玉・的玉)。床の上を転がり、クッション(壁)・ほかの玉で跳ね返る
    //  ・速く転がってきてプレイヤー・落ちている積み木に当たると、進む向きへ強くはじく(積み木は弾になって飛ぶ)
    //  ・攻撃判定のある積み木が当たると、積み木は壁と同じように跳ね返り(ITumikiReflector)、玉は押されて転がる
    //  ・ポケット(床の穴)に落ちたら台(BilliardTable)に知らせる。台が消す・復活させる
    // 物理の回転は止め(Rigidbody の freezeRotation)、見た目(m_visual)だけを進む向きに転がす
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class BilliardBall : MonoBehaviour, ITumikiReflector
    {
        public enum Kind { Cue, Object }

        [SerializeField] Kind m_kind = Kind.Object;
        [Tooltip("玉の種類(ポケットに入ったときに発動するギミックの番号。BilliardTable のギミックの並び順)")]
        [SerializeField, Min(0)] int m_type;
        [Tooltip("番号(見た目・説明用)")]
        [SerializeField] int m_number;
        [Tooltip("玉の色(エフェクトの色)")]
        [SerializeField] Color m_color = Color.white;
        [SerializeField, Min(0.1f)] float m_radius = 0.9f;
        [Tooltip("転がる見た目(玉の中心に置いた子)")]
        [SerializeField] Transform m_visual;
        [SerializeField] TrailRenderer m_trail;

        [Header("動き")]
        [SerializeField] float m_maxSpeed = 34.0f;
        [Tooltip("この速さより遅くなったら止める")]
        [SerializeField] float m_stopSpeed = 0.2f;
        [Tooltip("転がる抵抗(Rigidbody の linearDamping)")]
        [SerializeField] float m_rollingDrag = 0.4f;
        [Tooltip("軌跡を出す速さ")]
        [SerializeField] float m_trailSpeed = 7.0f;

        [Header("はじく")]
        [Tooltip("この速さ以上で当たったときだけプレイヤー・積み木をはじく")]
        [SerializeField] float m_minHitSpeed = 2.5f;
        [Tooltip("プレイヤーをはじく速さ = 玉の速さ × この倍率")]
        [SerializeField] float m_characterPowerScale = 1.05f;
        [SerializeField] float m_characterMaxPower = 22.0f;
        [Tooltip("落ちている積み木をはじく速さ = 玉の速さ × この倍率")]
        [SerializeField] float m_blockPowerScale = 0.9f;
        [SerializeField] float m_upPower = 3.0f;
        [Tooltip("はじいた後に玉に残る速さの割合")]
        [SerializeField, Range(0.0f, 1.0f)] float m_speedKeep = 0.6f;
        [Tooltip("同じ相手を続けてはじかない時間")]
        [SerializeField] float m_hitCooldown = 0.35f;
        [Tooltip("攻撃判定のある積み木が当たったときに玉が押される速さ")]
        [SerializeField] float m_tumikiPush = 5.0f;

        [Header("エフェクト")]
        [SerializeField] GimmickBurstSettings m_clackEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 1.0f, 0.95f, 1.0f), ColorB = new Color(1.0f, 0.9f, 0.6f, 1.0f),
            Count = 12, Speed = new Vector2(2.0f, 5.0f), Size = new Vector2(0.05f, 0.12f), Lifetime = new Vector2(0.12f, 0.25f),
            Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.1f, 0, 0), Stretch = true,
        };
        [SerializeField] GimmickBurstSettings m_hitEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 0.95f, 0.7f, 1.0f), ColorB = new Color(1.0f, 0.6f, 0.25f, 1.0f),
            Count = 22, Speed = new Vector2(3.0f, 8.0f), Size = new Vector2(0.06f, 0.16f), Lifetime = new Vector2(0.15f, 0.35f),
            Shape = GimmickBurstShape.Hemisphere, ShapeSize = new Vector3(0.2f, 0, 0), Stretch = true,
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.95f, 0.7f, 0.8f), ShockwaveRadius = 1.6f, ShockwaveTime = 0.25f,
        };
        [SerializeField, Range(0.0f, 1.0f)] float m_clackVolume = 0.35f;

        Rigidbody m_rb;
        SphereCollider m_collider;
        Vector3 m_prevVelocity;
        Vector3 m_visualScale = Vector3.one;
        readonly Dictionary<Object, float> m_lastHit = new Dictionary<Object, float>();

        // 消える・現れるの動き
        enum Anim { None, Sink, Appear }
        Anim m_anim;
        float m_animTime;
        float m_animLength;
        Vector3 m_animFrom, m_animTo;

        public Kind BallKind => m_kind;
        public int Type => m_type;
        public int Number => m_number;
        public Color Color => m_color;
        public float Radius => m_radius;
        public bool IsCue => m_kind == Kind.Cue;
        // 台の上で転がっている(ポケットに落ちて消えている間・現れる途中は false)
        public bool InPlay { get; private set; } = true;
        public BilliardTable Table { get; set; }
        public Rigidbody Rigidbody => m_rb;
        public Vector3 Position => m_rb != null ? m_rb.position : transform.position;
        public Vector3 Velocity => m_rb != null && !m_rb.isKinematic ? m_rb.linearVelocity : Vector3.zero;
        public float Speed => Flat(Velocity).magnitude;

        // 玉が他の玉に当たった(当たった相手、当たった位置、強さ)。演出・拡張用
        public event System.Action<BilliardBall, BilliardBall, Vector3, float> BallHit;
        // 玉がプレイヤー・積み木をはじいた(はじいた相手、位置、はじいた速さ)
        public event System.Action<BilliardBall, IGimmickAffectable, Vector3, float> Knocked;

        // 玉どうしが当たったとき、どちらが音を鳴らすかを決める通し番号
        static int s_nextOrder;
        int m_order;

        private void Awake()
        {
            m_order = ++s_nextOrder;
            m_rb = GetComponent<Rigidbody>();
            m_collider = GetComponent<SphereCollider>();
            m_rb.freezeRotation = true;
            m_rb.linearDamping = m_rollingDrag;
            m_rb.interpolation = RigidbodyInterpolation.Interpolate;
            m_rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (m_visual != null) m_visualScale = m_visual.localScale;
        }

        private void OnEnable()
        {
            // ネット対戦のクライアントでは動かさない(結果はホストから届く想定)
            if (GimmickClock.IsNetClient) m_rb.isKinematic = true;
        }

        private void OnValidate()
        {
            m_radius = Mathf.Max(0.1f, m_radius);
            var col = GetComponent<SphereCollider>();
            if (col != null) col.radius = m_radius;
        }

        //========================================
        // 台から使う
        //========================================

        // 手玉を突く(水平の速度を与える)
        public void Strike(Vector3 velocity)
        {
            if (!InPlay || m_rb.isKinematic) return;
            velocity.y = 0.0f;
            m_rb.WakeUp();
            m_rb.linearVelocity = Vector3.ClampMagnitude(velocity, m_maxSpeed);
            m_prevVelocity = m_rb.linearVelocity;
        }

        // 外から速さを足す(ギミックなど)
        public void AddVelocity(Vector3 velocity)
        {
            if (!InPlay || m_rb.isKinematic) return;
            velocity.y = 0.0f;
            m_rb.WakeUp();
            m_rb.linearVelocity = Vector3.ClampMagnitude(m_rb.linearVelocity + velocity, m_maxSpeed);
        }

        // ポケットに吸い込まれて消える(当たり判定はすぐ消す)
        public void Sink(Vector3 pocketPosition, float duration)
        {
            InPlay = false;
            m_collider.enabled = false;
            if (!m_rb.isKinematic)
            {
                m_rb.linearVelocity = Vector3.zero;
                m_rb.isKinematic = true;
            }
            m_anim = Anim.Sink;
            m_animTime = 0.0f;
            m_animLength = Mathf.Max(0.01f, duration);
            m_animFrom = transform.position;
            m_animTo = pocketPosition + Vector3.down * (m_radius * 2.0f);
            if (m_trail != null) m_trail.emitting = false;
        }

        // すぐに消す(ステージの切り替えなど)
        public void HideImmediately()
        {
            InPlay = false;
            m_anim = Anim.None;
            m_collider.enabled = false;
            if (!m_rb.isKinematic) m_rb.linearVelocity = Vector3.zero;
            m_rb.isKinematic = true;
            SetVisible(false);
        }

        // floorPosition(床の上の点)に置き直す。appear: 小さい所からポンと現れる
        public void PlaceAt(Vector3 floorPosition, bool appear)
        {
            Vector3 pos = new Vector3(floorPosition.x, floorPosition.y + m_radius, floorPosition.z);
            m_anim = appear ? Anim.Appear : Anim.None;
            m_animTime = 0.0f;
            m_animLength = 0.35f;
            transform.position = pos;
            m_rb.position = pos;
            SetVisible(true);
            if (m_visual != null) m_visual.localScale = appear ? Vector3.zero : m_visualScale;
            m_collider.enabled = true;
            bool client = GimmickClock.IsNetClient;
            m_rb.isKinematic = client;
            if (!client)
            {
                m_rb.linearVelocity = Vector3.zero;
                m_rb.angularVelocity = Vector3.zero;
            }
            m_prevVelocity = Vector3.zero;
            m_lastHit.Clear();
            if (m_trail != null)
            {
                m_trail.Clear();
                m_trail.emitting = false;
            }
            InPlay = true;
        }

        void SetVisible(bool visible)
        {
            if (m_visual != null) m_visual.gameObject.SetActive(visible);
            if (m_trail != null && !visible) m_trail.Clear();
        }

        //========================================
        // 動き
        //========================================

        private void FixedUpdate()
        {
            if (!InPlay || m_rb.isKinematic) return;
            Vector3 v = m_rb.linearVelocity;
            Vector3 h = Flat(v);
            if (h.magnitude > m_maxSpeed)
            {
                h = h.normalized * m_maxSpeed;
                v = new Vector3(h.x, v.y, h.z);
                m_rb.linearVelocity = v;
            }
            // ほぼ止まったら止める(いつまでもじわじわ転がらないように)
            else if (h.magnitude < m_stopSpeed && Mathf.Abs(v.y) < 0.05f && h.sqrMagnitude > 0.0f)
            {
                m_rb.linearVelocity = new Vector3(0.0f, v.y, 0.0f);
            }
            m_prevVelocity = m_rb.linearVelocity;

            if (Table != null && m_rb.position.y < Table.PocketedY) Table.NotifyFell(this);
        }

        private void Update()
        {
            UpdateAnim();
            if (!InPlay) return;

            // 見た目を進む向きに転がす
            Vector3 h = Flat(Velocity);
            float speed = h.magnitude;
            if (m_visual != null && speed > 0.01f)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, h / speed);
                float angle = speed * Time.deltaTime / m_radius * Mathf.Rad2Deg;
                m_visual.Rotate(axis, angle, Space.World);
            }
            if (m_trail != null) m_trail.emitting = speed > m_trailSpeed;
        }

        void UpdateAnim()
        {
            if (m_anim == Anim.None) return;
            m_animTime += Time.deltaTime;
            float t = Mathf.Clamp01(m_animTime / m_animLength);
            if (m_anim == Anim.Sink)
            {
                // ポケットの中心へ寄りながら沈み、小さくなる
                float e = t * t;
                transform.position = Vector3.Lerp(m_animFrom, m_animTo, e);
                if (m_visual != null) m_visual.localScale = m_visualScale * Mathf.Lerp(1.0f, 0.3f, e);
                if (t >= 1.0f)
                {
                    m_anim = Anim.None;
                    SetVisible(false);
                    if (m_visual != null) m_visual.localScale = m_visualScale;
                }
            }
            else
            {
                // 小さい所から少し膨らんで元の大きさに
                float s = ToyKit.EaseOutBack(t);
                if (m_visual != null) m_visual.localScale = m_visualScale * s;
                if (t >= 1.0f)
                {
                    m_anim = Anim.None;
                    if (m_visual != null) m_visual.localScale = m_visualScale;
                }
            }
        }

        //========================================
        // ぶつかったとき
        //========================================

        private void OnCollisionEnter(Collision collision) => HandleContact(collision, true);
        private void OnCollisionStay(Collision collision) => HandleContact(collision, false);

        void HandleContact(Collision collision, bool enter)
        {
            if (!InPlay || m_rb.isKinematic || GimmickClock.IsNetClient) return;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;

            // ほかの玉: 音と火花だけ(跳ね返りは物理に任せる)
            var other = collision.collider.GetComponentInParent<BilliardBall>();
            if (other != null)
            {
                if (!enter) return;
                float strength = collision.relativeVelocity.magnitude;
                // 2つの玉のうち片方だけが鳴らす
                if (strength > 1.0f && m_order < other.m_order)
                {
                    float k = Mathf.InverseLerp(1.0f, 20.0f, strength);
                    GimmickBurst.Play(m_clackEffect, point, Mathf.Lerp(0.5f, 1.4f, k));
                    GimmickSound.Play(BilliardFx.Clack, m_clackVolume * Mathf.Lerp(0.35f, 1.0f, k), Random.Range(0.95f, 1.08f));
                    BallHit?.Invoke(this, other, point, strength);
                }
                return;
            }

            // 飛んでいる積み木: 積み木は跳ね返る。攻撃判定があれば玉が押される
            var block = collision.collider.GetComponentInParent<TumikiBlock>();
            if (block != null && block.CurrentState == TumikiBlock.State.Flying)
            {
                if (!enter || !Ready(block)) return;
                if (block.IsHarmful)
                {
                    Vector3 dir = Flat(transform.position - block.transform.position);
                    if (dir.sqrMagnitude < 0.0001f) dir = Flat(block.Direction);
                    AddVelocity(dir.normalized * m_tumikiPush);
                    GimmickBurst.Play(m_clackEffect, point, 0.8f);
                    GimmickSound.Play(BilliardFx.Clack, m_clackVolume * 0.6f, 1.2f);
                }
                return;
            }

            // プレイヤー・落ちている積み木: 速く当たったらはじく
            IGimmickAffectable target = GimmickUtil.Find(collision.collider);
            if (target == null || !(target is Object key)) return;
            Vector3 vel = Flat(m_prevVelocity);
            float speed = vel.magnitude;
            if (speed < m_minHitSpeed) return;
            Vector3 to = Flat(target.Rigidbody.position - transform.position);
            if (to.sqrMagnitude < 0.0001f) to = vel;
            to.Normalize();
            // 相手に向かって転がっているときだけ
            if (Vector3.Dot(vel / speed, to) < 0.2f) return;
            if (!Ready(key)) return;

            Vector3 dirHit = (to + vel / speed).normalized;
            bool isBlock = target is TumikiBlock;
            float power = isBlock ? speed * m_blockPowerScale : Mathf.Min(speed * m_characterPowerScale, m_characterMaxPower);
            target.AddKnockback(dirHit * power + Vector3.up * (isBlock ? 0.5f : m_upPower));
            if (!isBlock) target.StartHitStop(0.08f, 0.15f, 0.12f);
            m_rb.linearVelocity = new Vector3(vel.x * m_speedKeep, m_rb.linearVelocity.y, vel.z * m_speedKeep);

            float k2 = Mathf.InverseLerp(m_minHitSpeed, 25.0f, speed);
            GimmickBurst.Play(m_hitEffect, point, Quaternion.LookRotation(dirHit), Mathf.Lerp(0.6f, 1.3f, k2));
            GimmickSound.Play(BilliardFx.Strike, Mathf.Lerp(0.2f, 0.5f, k2), Random.Range(0.75f, 0.85f));
            if (!isBlock) BilliardFx.Shake(Mathf.Lerp(0.08f, 0.35f, k2), 0.25f);
            Knocked?.Invoke(this, target, point, power);
        }

        bool Ready(Object key)
        {
            if (m_lastHit.TryGetValue(key, out float last) && Time.time - last < m_hitCooldown) return false;
            m_lastHit[key] = Time.time;
            if (m_lastHit.Count > 32) CleanUp();
            return true;
        }

        void CleanUp()
        {
            var dead = new List<Object>();
            foreach (var pair in m_lastHit)
            {
                if (pair.Key == null || Time.time - pair.Value > 5.0f) dead.Add(pair.Key);
            }
            foreach (var key in dead) m_lastHit.Remove(key);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0.0f, v.z);
    }
}
