using Nakahira;
using UnityEngine;

namespace Yurube
{
    // へびだるの「蛇行」を付ける積み木側のコンポーネント。
    // 飛んでいる間だけ、一定間隔で左右に切り返して軌道をS字にする。
    // SetFlyingVelocity で向きを書き換えるので、反射・当たり判定・連鎖も更新後の向きで計算される。
    [RequireComponent(typeof(TumikiBlock))]
    public class TumikiZigzag : MonoBehaviour
    {
        [Tooltip("左右に切り返す間隔(秒)")]
        [SerializeField] float m_interval = 0.18f;
        [Tooltip("1秒あたりに曲がる角度(度/秒)")]
        [SerializeField] float m_turnSpeed = 200.0f;

        TumikiBlock m_block;
        DarumaCharacter m_shooter;
        float m_timer;
        int m_side = 1;
        bool m_active;

        private void Awake()
        {
            m_block = GetComponent<TumikiBlock>();
            enabled = false;
        }

        // へびだるのショット時に呼ぶ。撃った本人の積み木の間だけ蛇行させる
        public void Begin(DarumaCharacter shooter, float interval, float turnSpeed)
        {
            m_shooter = shooter;
            m_interval = Mathf.Max(0.01f, interval);
            m_turnSpeed = turnSpeed;
            m_timer = 0.0f;
            m_side = 1;
            m_sharp = false;
            m_active = true;
            enabled = true;
        }

        // 2026-11-24 レベル用(DarumaLevel): カクカクのジグザグ(稲妻のような折れ線)。
        // 撃った向きから左右へ angle 度ずつ、interval 秒ごとに一気に向きを切り替える。
        // 最初の1本は半分の長さにして、撃った向きの線を中心に左右へ振れるようにする
        public void BeginSharp(DarumaCharacter shooter, float interval, float angle)
        {
            m_shooter = shooter;
            m_angle = Mathf.Clamp(angle, 0.0f, 85.0f);
            // 弾を速くする分(下)だけ切り返しを早くして、折れ線の形(1本の長さと横の振れ幅)は速くする前と同じにする
            float cos = Mathf.Max(0.2f, Mathf.Cos(m_angle * Mathf.Deg2Rad));
            m_interval = Mathf.Max(0.01f, interval * cos);
            m_timer = m_interval * 0.5f;
            m_side = 1;
            m_sharp = true;
            m_base = m_block.Direction;
            m_base.y = 0.0f;
            m_origin = transform.position;
            // 左右の振れ幅(m)：速くする前の速さで interval 秒進んだ折れ線1本の、横の幅の半分
            m_amplitude = 0.5f * m_block.Speed * Mathf.Max(0.01f, interval) * Mathf.Sin(m_angle * Mathf.Deg2Rad);
            m_active = true;
            enabled = true;

            // 斜めに進むぶん、前へ進む速さが cos(角度) 倍に落ちるので、その分だけ弾を速くして、前へ進む速さを撃ったときのままにする
            ApplySharp(1.0f / Mathf.Max(0.2f, Mathf.Cos(m_angle * Mathf.Deg2Rad)));

            // 速くなると、1回の物理のステップで積み木やキャラクターをすり抜けやすいので、ジグザグの間だけ連続した当たり判定にする
            m_rb = GetComponent<Rigidbody>();
            if (m_rb != null && !m_changedDetection)
            {
                m_oldDetection = m_rb.collisionDetectionMode;
                m_rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                m_changedDetection = true;
            }
        }

        HitStop m_hitStop;
        Rigidbody m_rb;
        CollisionDetectionMode m_oldDetection;
        bool m_changedDetection;
        bool m_sharp;
        float m_angle;
        Vector3 m_base;       // ジグザグの中心の向き(撃った向き)
        Vector3 m_origin;     // ジグザグの中心の線が通る点(撃った位置。壁で跳ね返ったら跳ね返った位置)
        float m_amplitude;    // 中心の線から左右へ振れる幅(m)
        Vector3 m_lastSet;    // 最後にこちらで決めた向き(壁で跳ね返ったかを見る)

        // 向きだけを切り替える(速さは今の速さ × speedScale)
        void ApplySharp(float speedScale = 1.0f)
        {
            if (m_base.sqrMagnitude < 0.0001f) return;
            Vector3 dir = (Quaternion.AngleAxis(m_side * m_angle, Vector3.up) * m_base.normalized).normalized;
            m_lastSet = dir;
            m_block.SetFlyingVelocity(dir * (m_block.Speed * speedScale));
        }

        void EndSharp()
        {
            if (m_changedDetection && m_rb != null) m_rb.collisionDetectionMode = m_oldDetection;
            m_changedDetection = false;
            m_sharp = false;
        }

        void SharpStep()
        {
            Vector3 now = m_block.Direction;
            now.y = 0.0f;
            // 壁などで向きが大きく変わっていたら(跳ね返った)、今の向きを新しい中心にして、そこから振り直す
            // (物理のわずかなずれは無視する。拾うと中心が少しずつ回って、弾が後ろへ戻ってしまうため)
            if (now.sqrMagnitude > 0.0001f && Vector3.Angle(now, m_lastSet) > 30.0f)
            {
                m_base = now;
                m_origin = transform.position;
                m_side = 1;
                ApplySharp();
                return;
            }
            // 中心の線からの横のずれで切り返す(時間で切り返すと、速い弾は物理の1ステップで進む量が大きく、
            // 折れ線の長さが左右でそろわずに片側へずれていくため。横のずれで決めると、いつも中心の線の左右に振れる)
            Vector3 right = Vector3.Cross(Vector3.up, m_base.normalized);
            float lateral = Vector3.Dot(transform.position - m_origin, right);
            // 角度が + のとき右へ進む(Quaternion.AngleAxis は上から見て時計回り)
            bool turn = (m_side > 0 && lateral >= m_amplitude) || (m_side < 0 && lateral <= -m_amplitude);
            if (!turn) return;
            m_side = -m_side;
            ApplySharp();
        }

        private void FixedUpdate()
        {
            if (!m_active) return;

            // 飛んでいない・持ち主が変わった(他の人に打ち返された)なら蛇行をやめる
            if (m_block == null
                || m_block.CurrentState != TumikiBlock.State.Flying
                || !ReferenceEquals(m_block.Owner, m_shooter))
            {
                EndSharp();
                m_active = false;
                enabled = false;
                return;
            }

            if (m_sharp) { SharpStep(); return; }

            m_timer += Time.fixedDeltaTime;
            if (m_timer >= m_interval)
            {
                m_timer -= m_interval;
                m_side = -m_side;
            }

            Vector3 direction = m_block.Direction;
            if (direction.sqrMagnitude < 0.0001f) return;

            float angle = m_side * m_turnSpeed * Time.fixedDeltaTime;
            Vector3 turned = (Quaternion.AngleAxis(angle, Vector3.up) * direction).normalized;
            m_block.SetFlyingVelocity(turned * m_block.Speed);
        }
    }
}
