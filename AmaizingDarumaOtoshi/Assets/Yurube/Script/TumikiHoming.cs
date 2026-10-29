using Nakahira;
using UnityEngine;

namespace Yurube
{
    // みだるのパッシブで緑の積み木に付くホーミング。
    // 積み木の向きは TumikiBlock.SetFlyingVelocity で書き換えるので、反射・当たり判定・
    // 連鎖の計算も更新後の向き(Direction)で行われる。
    [RequireComponent(typeof(TumikiBlock))]
    public class TumikiHoming : MonoBehaviour
    {
        [Tooltip("1秒あたりに曲がれる角度(度/秒)")]
        [SerializeField] float m_turnSpeed = 270.0f;
        [Tooltip("追いかける時間(秒)")]
        [SerializeField] float m_guidanceDuration = 1.5f;
        [Tooltip("ターゲットを探す距離(m)")]
        [SerializeField] float m_targetSearchRadius = 30.0f;
        [Tooltip("ホーミングの開始・終了をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logHoming = true;

        TumikiBlock m_block;
        DarumaCharacter m_shooter;
        DarumaCharacter m_target;
        float m_elapsed;
        // 何も設定されていないときは動かさない(Begin で false になる)
        bool m_finished = true;
        bool m_loggedStart;

        // この発射でホーミングを始めたか(同じ積み木を何度も拾い直さないためのフラグ)
        public bool ConsumedLaunch { get; private set; }
        // 今ホーミング中か
        public bool IsHoming => enabled && !m_finished;

        private void Awake()
        {
            m_block = GetComponent<TumikiBlock>();
            // 落ちてLooseに戻ったら「この発射は終わり」にする。
            // (ホーミング終了で enabled=false になっても、着地を拾えるように購読は切らない)
            if (m_block != null) m_block.StateChanged += OnBlockStateChanged;
            enabled = false;
        }

        private void OnBlockStateChanged(TumikiBlock block)
        {
            if (block.CurrentState != TumikiBlock.State.Flying) ConsumedLaunch = false;
        }

        // みだるのパッシブから、飛び始めた緑の積み木に対して呼ばれる
        public void Begin(DarumaCharacter shooter, float turnSpeed, float guidanceDuration, bool log)
        {
            m_shooter = shooter;
            m_turnSpeed = turnSpeed;
            m_guidanceDuration = guidanceDuration;
            m_logHoming = log;

            m_target = null;
            m_elapsed = 0.0f;
            m_finished = false;
            m_loggedStart = false;
            ConsumedLaunch = true;
            enabled = true;
        }

        private void FixedUpdate()
        {
            if (m_block == null)
            {
                enabled = false;
                return;
            }

            // 飛んでいない(落ちた・積まれた・消えた)ならホーミング終了
            if (m_block.CurrentState != TumikiBlock.State.Flying)
            {
                ConsumedLaunch = false;
                enabled = false;
                return;
            }

            m_elapsed += Time.fixedDeltaTime;
            if (m_elapsed >= m_guidanceDuration)
            {
                Finish();
                return;
            }

            if (m_target == null) m_target = FindTarget();
            if (m_target == null) return;

            if (!m_loggedStart)
            {
                m_loggedStart = true;
                if (m_logHoming)
                {
                    Debug.Log($"[みだる] 緑の積み木がホーミング開始 → {m_target.name}", this);
                }
            }

            Vector3 toTarget = m_target.transform.position - transform.position;
            toTarget.y = 0.0f;
            if (toTarget.sqrMagnitude < 0.0001f) return;

            // 少しずつ相手の方向へ曲げる(曲がり切れないうちは円を描いて追いかける)
            float maxRadians = m_turnSpeed * Mathf.Deg2Rad * Time.fixedDeltaTime;
            Vector3 direction = Vector3.RotateTowards(m_block.Direction, toTarget.normalized, maxRadians, 0.0f).normalized;
            m_block.SetFlyingVelocity(direction * m_block.Speed);
        }

        private void Finish()
        {
            if (!m_finished && m_logHoming && m_loggedStart)
            {
                Debug.Log("[みだる] 緑の積み木のホーミング終了", this);
            }
            m_finished = true;
            enabled = false;
        }

        // 一番近い、自分以外の生きているキャラクターを狙う
        private DarumaCharacter FindTarget()
        {
            DarumaCharacter nearest = null;
            float nearestSqr = m_targetSearchRadius * m_targetSearchRadius;

            foreach (var character in FindObjectsByType<DarumaCharacter>(FindObjectsInactive.Exclude))
            {
                if (character == null || character == m_shooter) continue;

                float sqr = (character.transform.position - transform.position).sqrMagnitude;
                if (sqr >= nearestSqr) continue;

                nearestSqr = sqr;
                nearest = character;
            }
            return nearest;
        }
    }
}
