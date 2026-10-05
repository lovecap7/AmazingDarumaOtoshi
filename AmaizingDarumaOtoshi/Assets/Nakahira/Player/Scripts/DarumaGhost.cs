using System;
using UnityEngine;

namespace Nakahira
{
    // 幽霊キャラクター。脱落したプレイヤーが操作する
    // ・浮いているので奈落の上も移動できる(ステージ範囲内に制限)。壁は通れない
    // ・ジャンプ・積み木を積む・股抜きショットはできない
    // ・当たり判定を持たないので、攻撃を受けず、生存者や積み木もすり抜ける
    // ・遅いハンマーで薙ぎ払いができ、生存者にダメージを与えると復活を要求する
    public class DarumaGhost : MonoBehaviour, IDarumaAttacker
    {
        // 移動速度など(ScriptableObjectから毎回読む)。ハンマーの性能はハンマー側のHammerParams
        [SerializeField] GhostParams m_params;
        [SerializeField] float m_turnSpeed = 15.0f;
        // 壁との当たり判定に使う半径
        [SerializeField] float m_radius = 0.45f;
        [SerializeField] DarumaHammer m_hammer;

        IDarumaCommandSource m_commandSource;
        StageArea m_stageArea;
        Vector3 m_moveDirection;
        bool m_reviveRequested;
        readonly RaycastHit[] m_wallHits = new RaycastHit[8];

        public Collider Body => null;

        // 生存者にダメージを与えたので復活したい
        public event Action<DarumaGhost> ReviveRequested;

        public void Init(StageArea stageArea)
        {
            m_stageArea = stageArea;
        }

        public void SetCommandSource(IDarumaCommandSource source)
        {
            m_commandSource = source;
            m_moveDirection = Vector3.zero;
        }

        private void Update()
        {
            if (m_commandSource != null)
            {
                DarumaCommand command = m_commandSource.ReadCommand();
                Vector2 move = Vector2.ClampMagnitude(command.Move, 1.0f);
                m_moveDirection = new Vector3(move.x, 0.0f, move.y);
                if (command.Sweep) m_hammer.TrySweep();
            }

            UpdateMove();
        }

        private void UpdateMove()
        {
            Vector3 delta = m_moveDirection * (m_params.MoveSpeed * Time.deltaTime);
            delta = SlideAlongWalls(delta);

            Vector3 pos = transform.position + delta;
            if (m_stageArea != null) pos = m_stageArea.Clamp(pos, m_radius);
            transform.position = pos;

            // ハンマーを振っている間は向きを固定
            if (m_moveDirection.sqrMagnitude > 0.0001f && !m_hammer.IsBusy)
            {
                Quaternion target = Quaternion.LookRotation(m_moveDirection, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, m_turnSpeed * Time.deltaTime);
            }
        }

        // 壁(ReflectWall)にぶつかる移動は壁に沿って滑らせる
        private Vector3 SlideAlongWalls(Vector3 delta)
        {
            for (int iter = 0; iter < 2; iter++)
            {
                float dist = delta.magnitude;
                if (dist < 0.0001f) break;

                Vector3 dir = delta / dist;
                Vector3 origin = transform.position + Vector3.up * (m_radius + 0.05f);
                int count = Physics.SphereCastNonAlloc(origin, m_radius, dir, m_wallHits, dist, ~0, QueryTriggerInteraction.Ignore);

                float nearest = float.MaxValue;
                Vector3 normal = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = m_wallHits[i];
                    if (hit.collider.GetComponentInParent<ReflectWall>() == null) continue;
                    if (hit.distance < nearest)
                    {
                        nearest = hit.distance;
                        normal = hit.normal;
                    }
                }
                if (nearest == float.MaxValue) break;

                normal.y = 0.0f;
                normal.Normalize();
                // 壁の手前まで進み、残りは壁に沿った成分だけにする
                Vector3 toWall = dir * Mathf.Max(0.0f, nearest - 0.01f);
                Vector3 rest = delta - toWall;
                rest -= normal * Vector3.Dot(rest, normal);
                transform.position += toWall;
                delta = rest;
            }
            return delta;
        }

        public void OnDealtDamage()
        {
            if (m_reviveRequested) return;
            m_reviveRequested = true;
            ReviveRequested?.Invoke(this);
        }
    }
}
