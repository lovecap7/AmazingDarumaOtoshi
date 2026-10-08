using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 扇風機。指定した二点間を往復しながら、指定方向に常に風を吹かせる
    // 風の範囲(扇風機の前方の直方体)にいるプレイヤー・積み木に風下への力を加える。遠いほど弱くなる
    // 力は今の速度に加算されるので、元の移動方向・速さは残ったまま流され、範囲を出ても慣性が残る
    // (飛んでいる積み木は弾道が曲がる。落ちている積み木は床との摩擦より強いときだけ動く)
    // このTransformの位置が足元、向きは風の方向に自動で合わせる
    // ギミックの要求は受ける側より先に処理する(同じステップで反映させる)
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(Rigidbody))]
    public class FanGimmick : MonoBehaviour
    {
        [Header("移動")]
        // 往復する二点(扇風機の子にしないこと。開始時の位置を使う)
        [SerializeField] Transform m_pointA;
        [SerializeField] Transform m_pointB;
        [SerializeField] float m_moveSpeed = 2.0f;
        // 端で止まる時間
        [SerializeField] float m_waitTime = 0.5f;

        [Header("風")]
        // 風を吹かせる方向(ワールド空間。水平成分だけを使う)
        [SerializeField] Vector3 m_windDirection = Vector3.forward;
        // 風の根元での力の強さ(加速度 m/s²)
        [SerializeField] float m_windForce = 15.0f;
        // 風の届く距離
        [SerializeField] float m_windLength = 10.0f;
        // 風の範囲の幅・高さ
        [SerializeField] Vector2 m_windSize = new Vector2(3.0f, 2.0f);
        // 風の先端での強さの割合(1なら減衰しない)
        [SerializeField, Range(0.0f, 1.0f)] float m_endStrength = 0.3f;

        [Header("見た目")]
        [SerializeField] Transform m_blades;
        [SerializeField] float m_bladeSpeed = 900.0f;
        [SerializeField] ParticleSystem m_windParticles;
        // 風の粒子が流れる速さ
        [SerializeField] float m_particleSpeed = 8.0f;

        // 本体の前面から風の範囲が始まる距離
        const float kWindStartOffset = 0.4f;

        Rigidbody m_rb;
        Vector3 m_a, m_b;
        float m_t;          // 0=A, 1=B
        float m_moveSign = 1.0f;
        float m_waitTimer;
        readonly List<IGimmickAffectable> m_targets = new List<IGimmickAffectable>();

        public Vector3 WindDirection
        {
            get
            {
                Vector3 d = m_windDirection;
                d.y = 0.0f;
                return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
            }
        }

        private void Awake()
        {
            m_rb = GetComponent<Rigidbody>();
            m_rb.isKinematic = true;
            m_rb.interpolation = RigidbodyInterpolation.Interpolate;
            m_a = m_pointA != null ? m_pointA.position : transform.position;
            m_b = m_pointB != null ? m_pointB.position : transform.position;
            transform.SetPositionAndRotation(m_a, Quaternion.LookRotation(WindDirection));
            ApplyParticleSettings();
        }

        private void OnValidate()
        {
            m_windLength = Mathf.Max(0.1f, m_windLength);
            m_windSize = Vector2.Max(m_windSize, new Vector2(0.1f, 0.1f));
            transform.rotation = Quaternion.LookRotation(WindDirection);
            ApplyParticleSettings();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            UpdateMove(dt);
            Blow();
        }

        private void Update()
        {
            if (m_blades != null) m_blades.Rotate(0.0f, 0.0f, m_bladeSpeed * Time.deltaTime, Space.Self);
        }

        // 二点間を一定の速さで往復し、端で少し止まる
        private void UpdateMove(float dt)
        {
            if (m_waitTimer > 0.0f)
            {
                m_waitTimer -= dt;
                return;
            }

            float length = Vector3.Distance(m_a, m_b);
            if (length < 0.001f) return;

            m_t += m_moveSign * m_moveSpeed * dt / length;
            if (m_t >= 1.0f || m_t <= 0.0f)
            {
                m_t = Mathf.Clamp01(m_t);
                m_moveSign = -m_moveSign;
                m_waitTimer = m_waitTime;
            }
            m_rb.MovePosition(Vector3.Lerp(m_a, m_b, m_t));
        }

        // 風の範囲にいるものに風下への力を加える
        private void Blow()
        {
            Vector3 dir = WindDirection;
            Quaternion rot = Quaternion.LookRotation(dir);
            Vector3 origin = m_rb.position + dir * kWindStartOffset;
            Vector3 center = origin + dir * (m_windLength * 0.5f) + Vector3.up * (m_windSize.y * 0.5f);
            Vector3 half = new Vector3(m_windSize.x, m_windSize.y, m_windLength) * 0.5f;

            GimmickUtil.OverlapBox(center, half, rot, m_targets);
            foreach (var target in m_targets)
            {
                float distance = Vector3.Dot(target.Rigidbody.position - origin, dir);
                float strength = Mathf.Lerp(1.0f, m_endStrength, Mathf.Clamp01(distance / m_windLength));
                target.AddForce(dir * (m_windForce * strength));
            }
        }

        // 風の粒子を範囲に合わせる(根元から先端まで流れる。エディタから設定を変えたときにも呼ぶ)
        public void ApplyParticleSettings()
        {
            if (m_windParticles == null) return;

            float speed = Mathf.Max(1.0f, m_particleSpeed);
            var main = m_windParticles.main;
            main.startSpeed = speed;
            main.startLifetime = m_windLength / speed;

            var shape = m_windParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(m_windSize.x, m_windSize.y, 0.1f);
            shape.position = Vector3.zero;

            m_windParticles.transform.localPosition = new Vector3(0.0f, m_windSize.y * 0.5f, kWindStartOffset);
            m_windParticles.transform.localRotation = Quaternion.identity;
        }

        private void OnDrawGizmos()
        {
            Vector3 dir = WindDirection;
            Vector3 pos = transform.position;
            Gizmos.color = new Color(0.4f, 0.8f, 1.0f, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(pos + dir * (kWindStartOffset + m_windLength * 0.5f) + Vector3.up * (m_windSize.y * 0.5f),
                Quaternion.LookRotation(dir), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(m_windSize.x, m_windSize.y, m_windLength));
            Gizmos.matrix = Matrix4x4.identity;

            if (m_pointA != null && m_pointB != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(m_pointA.position, m_pointB.position);
                Gizmos.DrawWireSphere(m_pointA.position, 0.2f);
                Gizmos.DrawWireSphere(m_pointB.position, 0.2f);
            }
        }
    }
}
