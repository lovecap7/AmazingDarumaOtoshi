using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // プレイヤーが脱落した瞬間に、一瞬スローにしてカメラを脱落した場所へ寄せ、その場所にエフェクトを出す演出(MatchCamera と同じカメラに付ける)
    // 流れ(実時間): 寄る → スローのまま止める → 通常の速さに戻しながら引く
    // 時間の速さ(Time.timeScale)は演出の前の値に戻す。演出中に他(試合の進行など)が時間の速さを変えたら、その値を優先して手を引く
    [RequireComponent(typeof(MatchCamera))]
    [DefaultExecutionOrder(-50)]
    public class EliminationCinematic : MonoBehaviour
    {
        [SerializeField] bool m_enabled = true;

        [Header("演出する脱落")]
        [Tooltip("積み木・ハンマーの攻撃で脱落したとき")]
        [SerializeField] bool m_onAttack = true;
        [Tooltip("奈落に落ちて脱落したとき(落ちた先は床の下なので、既定ではオフ)")]
        [SerializeField] bool m_onFall = false;
        [Tooltip("それ以外(原因不明)で脱落したとき")]
        [SerializeField] bool m_onOther = true;

        [Header("スロー")]
        [Tooltip("スロー中の時間の速さ")]
        [SerializeField, Range(0.01f, 1.0f)] float m_slowTimeScale = 0.15f;
        [Tooltip("スローのまま止める時間(実時間・秒)")]
        [SerializeField, Min(0.0f)] float m_slowDuration = 0.7f;
        [Tooltip("通常の速さに戻すまでの時間(実時間・秒)")]
        [SerializeField, Min(0.0f)] float m_recoverDuration = 0.35f;

        [Header("クローズアップ")]
        [Tooltip("脱落した場所からカメラまでの距離(m)")]
        [SerializeField, Min(0.5f)] float m_closeDistance = 9.0f;
        [Tooltip("寄ったときの視野角")]
        [SerializeField, Range(5.0f, 90.0f)] float m_closeFov = 26.0f;
        [Tooltip("脱落した場所から見る点までの高さ(m)")]
        [SerializeField] float m_focusHeight = 0.8f;
        [Tooltip("寄るのにかける時間(実時間・秒)")]
        [SerializeField, Min(0.01f)] float m_zoomInTime = 0.12f;
        [Tooltip("引くのにかける時間(実時間・秒。スローが終わってから)")]
        [SerializeField, Min(0.01f)] float m_zoomOutTime = 0.4f;

        [Header("エフェクト")]
        [Tooltip("脱落した場所に出すエフェクト(キャラクターはすぐに消えるので、寄った先で見せるもの)。未設定なら出さない")]
        [SerializeField] GameObject m_effectPrefab;
        [Tooltip("エフェクトを消すまでの時間(ゲーム内の時間・秒。スロー中はゆっくり進む)")]
        [SerializeField, Min(0.1f)] float m_effectLifetime = 3.0f;

        MatchCamera m_camera;
        bool m_playing;
        float m_time;
        Vector3 m_focus;

        // 時間の速さを管理しているか(他が変えたら手を引く)
        bool m_ownsTime;
        float m_baseTimeScale = 1.0f;
        float m_baseFixedDeltaTime = 0.02f;
        float m_appliedTimeScale = -1.0f;

        public bool IsPlaying => m_playing;

        private void Awake()
        {
            m_camera = GetComponent<MatchCamera>();
        }

        private void OnEnable()
        {
            DarumaCharacter.AnyEliminated += OnEliminated;
        }

        private void OnDisable()
        {
            DarumaCharacter.AnyEliminated -= OnEliminated;
            Stop();
        }

        private void OnEliminated(DarumaCharacter character)
        {
            if (!m_enabled || !ShouldPlay(character.EliminationCause)) return;
            Vector3 position = character.transform.position;
            if (m_effectPrefab != null) Destroy(Instantiate(m_effectPrefab, position, Quaternion.identity), m_effectLifetime);
            Play(position + Vector3.up * m_focusHeight);
        }

        private bool ShouldPlay(EliminationCause cause)
        {
            switch (cause)
            {
                case EliminationCause.Projectile:
                case EliminationCause.Hammer:
                    return m_onAttack;
                case EliminationCause.Fall:
                    return m_onFall;
                default:
                    return m_onOther;
            }
        }

        // 指定した点で演出を始める(演出中なら新しい点でやり直す)。他の演出から呼んでもよい
        public void Play(Vector3 focus)
        {
            if (!m_playing)
            {
                m_baseTimeScale = Time.timeScale;
                m_baseFixedDeltaTime = Time.fixedDeltaTime;
                // 止まっている(ポーズ中など)ときは時間には触れない
                m_ownsTime = m_baseTimeScale > 0.0f;
            }
            m_focus = focus;
            m_time = 0.0f;
            m_playing = true;
            Apply();
        }

        private void Update()
        {
            if (!m_playing) return;
            m_time += Time.unscaledDeltaTime;
            Apply();
        }

        private void Apply()
        {
            // 時間の速さ
            if (m_ownsTime && m_appliedTimeScale >= 0.0f && !Mathf.Approximately(Time.timeScale, m_appliedTimeScale))
            {
                // 他が時間の速さを変えた: そちらを優先し、物理の刻みだけ元に戻す
                m_ownsTime = false;
                Time.fixedDeltaTime = m_baseFixedDeltaTime;
            }
            float rate = m_time < m_slowDuration
                ? m_slowTimeScale
                : Mathf.Lerp(m_slowTimeScale, 1.0f, Smooth((m_time - m_slowDuration) / Mathf.Max(0.0001f, m_recoverDuration)));
            if (m_ownsTime)
            {
                Time.timeScale = m_baseTimeScale * rate;
                // 物理の刻みも同じ割合で細かくして、スロー中も動きがなめらかになるようにする
                Time.fixedDeltaTime = m_baseFixedDeltaTime * rate;
                m_appliedTimeScale = Time.timeScale;
            }

            // クローズアップ
            float weight = m_time < m_slowDuration
                ? Smooth(m_time / m_zoomInTime)
                : 1.0f - Smooth((m_time - m_slowDuration) / m_zoomOutTime);
            Vector3 position = m_focus - m_camera.BaseRotation * Vector3.forward * m_closeDistance;
            m_camera.SetOverride(position, m_closeFov, weight);

            if (m_time >= m_slowDuration + Mathf.Max(m_recoverDuration, m_zoomOutTime)) Stop();
        }

        private void Stop()
        {
            if (!m_playing) return;
            m_playing = false;
            if (m_ownsTime)
            {
                Time.timeScale = m_baseTimeScale;
                Time.fixedDeltaTime = m_baseFixedDeltaTime;
            }
            m_ownsTime = false;
            m_appliedTimeScale = -1.0f;
            if (m_camera != null) m_camera.ClearOverride();
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3.0f - 2.0f * t);
        }
    }
}
