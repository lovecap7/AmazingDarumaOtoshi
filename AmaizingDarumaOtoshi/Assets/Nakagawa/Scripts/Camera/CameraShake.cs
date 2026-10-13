using System;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // カメラを揺らす。積み木が他の積み木・キャラクターに当たったとき(速いほど強く)、ハンマーでキャラクターを叩いたとき、
    // キャラクターが攻撃で脱落したときに揺れる
    // 揺れは画面の上下左右への位置のずれ。MatchCamera が位置を決めた後に加える
    // 同時に複数の揺れが起きたときは強い方を使う(重ねて大きくなりすぎないように)
    [DefaultExecutionOrder(100)]
    public class CameraShake : MonoBehaviour
    {
        // 揺れの強さと長さ
        [Serializable]
        public struct ShakeSettings
        {
            [Tooltip("揺れ幅(m)")]
            [Min(0.0f)] public float Amplitude;
            [Tooltip("揺れる時間(秒)")]
            [Min(0.0f)] public float Duration;

            public ShakeSettings(float amplitude, float duration)
            {
                Amplitude = amplitude;
                Duration = duration;
            }
        }

        [SerializeField] bool m_enabled = true;

        [Header("積み木が当たったとき")]
        [Tooltip("他の積み木に当たったときに揺らす")]
        [SerializeField] bool m_shakeOnBlockHit = true;
        [Tooltip("キャラクターに当たったときに揺らす")]
        [SerializeField] bool m_shakeOnCharacterHit = true;
        [Tooltip("この速さ未満で当たったときは揺らさない")]
        [SerializeField, Min(0.0f)] float m_minSpeed = 6.0f;
        [Tooltip("この速さ以上で当たったときに最大の揺れになる")]
        [SerializeField, Min(0.0f)] float m_maxSpeed = 30.0f;
        [Tooltip("最小の速さで当たったときの揺れ")]
        [SerializeField] ShakeSettings m_weakHit = new ShakeSettings(0.05f, 0.12f);
        [Tooltip("最大の速さで当たったときの揺れ")]
        [SerializeField] ShakeSettings m_strongHit = new ShakeSettings(0.3f, 0.25f);
        [Tooltip("キャラクターに当たったときの揺れ幅の倍率")]
        [SerializeField, Min(0.0f)] float m_characterHitMultiplier = 1.3f;

        [Header("ハンマーが当たったとき")]
        [Tooltip("ハンマーでキャラクターを直接叩いてダメージを与えたときに揺らす")]
        [SerializeField] bool m_shakeOnHammerHitCharacter = true;
        [Tooltip("ハンマーでキャラクターを直接叩いたときの揺れ")]
        [SerializeField] ShakeSettings m_hammerHitCharacter = new ShakeSettings(0.25f, 0.2f);
        [Tooltip("ハンマーで積み木を打った(打ち返した)ときに揺らす")]
        [SerializeField] bool m_shakeOnHammerHitBlock = false;
        [Tooltip("ハンマーで積み木を打ったときの揺れ")]
        [SerializeField] ShakeSettings m_hammerHitBlock = new ShakeSettings(0.08f, 0.1f);

        [Header("脱落したとき")]
        [Tooltip("攻撃(積み木・ハンマー)で脱落したときの揺れ")]
        [SerializeField] ShakeSettings m_elimination = new ShakeSettings(0.6f, 0.45f);
        [Tooltip("奈落に落ちて脱落したときも揺らす")]
        [SerializeField] bool m_shakeOnFall = false;
        [Tooltip("奈落に落ちて脱落したときの揺れ")]
        [SerializeField] ShakeSettings m_fall = new ShakeSettings(0.2f, 0.25f);

        [Header("揺れ方")]
        [Tooltip("1秒あたりの揺れの細かさ")]
        [SerializeField, Min(0.0f)] float m_frequency = 25.0f;
        [Tooltip("時間とともに揺れが弱まる曲線の鋭さ(大きいほど早く収まる)")]
        [SerializeField, Min(0.1f)] float m_falloff = 2.0f;

        float m_amplitude;
        float m_duration;
        float m_time;
        float m_seed;
        Vector3 m_appliedOffset;
        Vector3 m_shakenPosition;

        // 今の揺れ幅
        public float CurrentAmplitude => m_duration > 0.0f && m_time < m_duration
            ? m_amplitude * Mathf.Pow(1.0f - m_time / m_duration, m_falloff)
            : 0.0f;

        private void Awake()
        {
            m_seed = UnityEngine.Random.value * 100.0f;
        }

        private void OnEnable()
        {
            TumikiBlock.Hit += OnTumikiHit;
            DarumaHammer.Hit += OnHammerHit;
            DarumaCharacter.AnyEliminated += OnEliminated;
        }

        private void OnDisable()
        {
            TumikiBlock.Hit -= OnTumikiHit;
            DarumaHammer.Hit -= OnHammerHit;
            DarumaCharacter.AnyEliminated -= OnEliminated;
            RemoveOffset();
        }

        // 揺らす(今の揺れより強ければ置き換える)。他の演出から呼んでもよい
        public void Shake(ShakeSettings settings)
        {
            if (!m_enabled || settings.Amplitude <= 0.0f || settings.Duration <= 0.0f) return;
            if (settings.Amplitude < CurrentAmplitude) return;
            m_amplitude = settings.Amplitude;
            m_duration = settings.Duration;
            m_time = 0.0f;
        }

        private void OnTumikiHit(TumikiHitInfo hit)
        {
            bool isCharacter = hit.Target is DarumaCharacter;
            if (isCharacter ? !m_shakeOnCharacterHit : !m_shakeOnBlockHit) return;
            if (hit.Speed < m_minSpeed) return;

            float t = Mathf.InverseLerp(m_minSpeed, m_maxSpeed, hit.Speed);
            var settings = new ShakeSettings(
                Mathf.Lerp(m_weakHit.Amplitude, m_strongHit.Amplitude, t),
                Mathf.Lerp(m_weakHit.Duration, m_strongHit.Duration, t));
            if (isCharacter) settings.Amplitude *= m_characterHitMultiplier;
            Shake(settings);
        }

        // 叩いて脱落させた場合は、先に来る脱落の強い揺れが優先される
        private void OnHammerHit(HammerHitInfo hit)
        {
            if (hit.Target is DarumaCharacter)
            {
                if (m_shakeOnHammerHitCharacter && hit.Damaged) Shake(m_hammerHitCharacter);
            }
            else if (m_shakeOnHammerHitBlock)
            {
                Shake(m_hammerHitBlock);
            }
        }

        private void OnEliminated(DarumaCharacter character)
        {
            if (character.IsEliminatedByAttack) Shake(m_elimination);
            else if (character.EliminationCause == EliminationCause.Fall && m_shakeOnFall) Shake(m_fall);
        }

        private void LateUpdate()
        {
            // 他から位置を決め直されていなければ、前のフレームのずれを取り除いてから揺らす
            RemoveOffset();

            float amplitude = CurrentAmplitude;
            m_time += Time.unscaledDeltaTime;
            if (amplitude <= 0.0f) return;

            float t = Time.unscaledTime * m_frequency;
            float x = Mathf.PerlinNoise(m_seed, t) * 2.0f - 1.0f;
            float y = Mathf.PerlinNoise(m_seed + 37.0f, t) * 2.0f - 1.0f;
            m_appliedOffset = (transform.right * x + transform.up * y) * amplitude;
            transform.position += m_appliedOffset;
            m_shakenPosition = transform.position;
        }

        private void RemoveOffset()
        {
            if (m_appliedOffset != Vector3.zero && transform.position == m_shakenPosition)
            {
                transform.position -= m_appliedOffset;
            }
            m_appliedOffset = Vector3.zero;
        }
    }
}
