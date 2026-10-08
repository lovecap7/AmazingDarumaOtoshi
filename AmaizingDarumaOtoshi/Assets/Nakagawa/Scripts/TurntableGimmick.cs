using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 回転台。地面に埋め込んだ円形のターンテーブルが、時計回り/反時計回りに一定速度で回り続ける
    // 動かないRigidbody(キネマティック)で回すので、プレイヤーや積み木が乗っても速度は変わらない
    // 上に乗っているもの(円の中で、表面から一定の高さまで)には、台の回転分の動きと向きの回転を加える
    // (積み木は向きと一緒に進む方向も回る。プレイヤーは向き・移動方向とも入力のまま)
    // 台には当たり判定を付けない(床の上に置く前提。縁に引っかかって積み木が縁に沿って滑るのを防ぐ)
    // このTransformの位置が台の表面の中心
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(Rigidbody))]
    public class TurntableGimmick : MonoBehaviour
    {
        public enum Direction { Clockwise, CounterClockwise }

        [SerializeField] Direction m_direction = Direction.Clockwise;
        // 回転の速さ(度/秒)
        [SerializeField] float m_speed = 45.0f;
        [SerializeField] float m_radius = 3.0f;
        // 表面からこの高さまでにいるものを乗っているとみなす
        [SerializeField] float m_rideHeight = 0.6f;

        [Header("見た目")]
        // 台の本体(半径に合わせて大きさを変える。中心から下に伸びる)
        [SerializeField] Transform m_disc;
        [SerializeField] float m_thickness = 0.5f;
        // 表面からの出っ張り(床との重なりのちらつき防止)
        [SerializeField] float m_surfaceOffset = 0.02f;
        // 表面の線(回っているのが分かるように。長さを直径に合わせる)
        [SerializeField] Transform[] m_lines;

        Rigidbody m_rb;
        Quaternion m_baseRotation;
        float m_angle;
        readonly List<IGimmickAffectable> m_targets = new List<IGimmickAffectable>();

        public float Radius => m_radius;
        // 上から見て時計回りが正(UnityのY軸回転と同じ)
        float SignedSpeed => m_direction == Direction.Clockwise ? m_speed : -m_speed;

        private void Awake()
        {
            m_rb = GetComponent<Rigidbody>();
            m_rb.isKinematic = true;
            m_rb.interpolation = RigidbodyInterpolation.Interpolate;
            m_baseRotation = transform.rotation;
            ApplyShape();
        }

        private void OnValidate()
        {
            m_radius = Mathf.Max(0.1f, m_radius);
            m_speed = Mathf.Max(0.0f, m_speed);
            ApplyShape();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float delta = SignedSpeed * dt;
            m_angle = Mathf.Repeat(m_angle + delta, 360.0f);
            m_rb.MoveRotation(m_baseRotation * Quaternion.Euler(0.0f, m_angle, 0.0f));
            Carry(delta, dt);
        }

        // 乗っているものを、このステップで台が回る分だけ動かす(中心からの距離は変わらない)
        private void Carry(float deltaAngle, float dt)
        {
            Vector3 center = m_rb.position;
            Vector3 half = new Vector3(m_radius, m_rideHeight * 0.5f, m_radius);
            GimmickUtil.OverlapBox(center + Vector3.up * (m_rideHeight * 0.5f), half, Quaternion.identity, m_targets);

            Quaternion step = Quaternion.Euler(0.0f, deltaAngle, 0.0f);
            foreach (var target in m_targets)
            {
                Vector3 offset = target.Rigidbody.position - center;
                offset.y = 0.0f;
                if (offset.sqrMagnitude > m_radius * m_radius) continue;
                target.AddCarryVelocity((step * offset - offset) / dt);
                target.AddCarryRotation(deltaAngle);
            }
        }

        // 半径・厚さに合わせて見た目と当たり判定の大きさを変える(エディタから設定を変えたときにも呼ぶ)
        public void ApplyShape()
        {
            float diameter = m_radius * 2.0f;
            if (m_disc != null)
            {
                // Unityの円柱は高さ2なので、Yは厚さの半分
                m_disc.localScale = new Vector3(diameter, m_thickness * 0.5f, diameter);
                m_disc.localPosition = new Vector3(0.0f, m_surfaceOffset - m_thickness * 0.5f, 0.0f);
            }
            if (m_lines == null) return;
            foreach (var line in m_lines)
            {
                if (line == null) continue;
                Vector3 s = line.localScale;
                line.localScale = new Vector3(diameter * 0.92f, s.y, s.z);
                Vector3 p = line.localPosition;
                line.localPosition = new Vector3(p.x, m_surfaceOffset + s.y * 0.5f, p.z);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1.0f, 0.6f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * (m_rideHeight * 0.5f),
                new Vector3(m_radius * 2.0f, m_rideHeight, m_radius * 2.0f));
        }
    }
}
