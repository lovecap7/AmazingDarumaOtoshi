using UnityEngine;

namespace Yurube.Story.Vfx
{
    // ワールド／前面パーティクルプレハブのルートに付ける実行時コントローラ。
    //  ・tint / scale / intensity / unscaledTime をマテリアル複製なしで反映（毎フレームGCゼロ）
    //  ・非ループ再生は全パーティクル・トレイル消滅後に自動破棄
    //  ・ループ再生は Stop() で新規発生を止め、残粒子消滅後に自動破棄
    public class StoryVfxInstance : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [Header("基本設定（プレハブ上で調整可）")]
        [SerializeField] string m_key = "";
        [SerializeField] bool m_looping;
        [SerializeField] bool m_useUnscaledTime = true;
        [SerializeField, Min(0.05f)] float m_defaultScale = 1f;
        [SerializeField] Color m_defaultTint = Color.white;
        [SerializeField, Range(0f, 2f)] float m_intensity = 1f;
        [SerializeField, Min(0f)] float m_autoDestroyGraceSeconds = 0.25f;

        ParticleSystem[] m_systems;
        ParticleSystemRenderer[] m_renderers;
        TrailRenderer[] m_trails;
        float[] m_baseRates;
        Color[] m_baseMaterialColors;
        MaterialPropertyBlock m_mpb;

        Color m_appliedTint = Color.white;
        float m_appliedScale = 1f;
        float m_appliedIntensity = 1f;
        bool m_isLoopingRuntime;
        bool m_stopping;
        bool m_paused;
        bool m_initialized;
        float m_elapsedUnscaled;
        float m_stopElapsedUnscaled;
        float m_maxTrailTime;

        public string Key => m_key;
        public bool IsLooping => m_isLoopingRuntime;
        public bool IsStopping => m_stopping;
        public bool IsPaused => m_paused;

        public void ConfigureDefaults(string key, bool looping, bool useUnscaledTime, float defaultScale = 1f)
        {
            m_key = key ?? "";
            m_looping = looping;
            m_useUnscaledTime = useUnscaledTime;
            m_defaultScale = Mathf.Max(0.05f, defaultScale);
        }

        void Awake()
        {
            CacheComponents();
        }

        void CacheComponents()
        {
            if (m_initialized) return;
            m_initialized = true;

            m_systems = GetComponentsInChildren<ParticleSystem>(true);
            m_renderers = GetComponentsInChildren<ParticleSystemRenderer>(true);
            m_trails = GetComponentsInChildren<TrailRenderer>(true);
            m_mpb = new MaterialPropertyBlock();

            m_baseRates = new float[m_systems.Length];
            for (int i = 0; i < m_systems.Length; i++)
            {
                m_baseRates[i] = m_systems[i].emission.rateOverTimeMultiplier;
            }

            m_baseMaterialColors = new Color[m_renderers.Length];
            for (int i = 0; i < m_renderers.Length; i++)
            {
                var mat = m_renderers[i] != null ? m_renderers[i].sharedMaterial : null;
                if (mat != null && mat.HasProperty(BaseColorId))
                    m_baseMaterialColors[i] = mat.GetColor(BaseColorId);
                else if (mat != null && mat.HasProperty(ColorId))
                    m_baseMaterialColors[i] = mat.GetColor(ColorId);
                else
                    m_baseMaterialColors[i] = Color.white;
            }

            m_maxTrailTime = 0f;
            for (int i = 0; i < m_trails.Length; i++)
            {
                if (m_trails[i] != null)
                    m_maxTrailTime = Mathf.Max(m_maxTrailTime, m_trails[i].time);
            }

            m_isLoopingRuntime = m_looping;
            m_appliedTint = m_defaultTint;
            m_appliedScale = m_defaultScale;
            m_appliedIntensity = m_intensity;
        }

        public void Play(Color? tint = null, float scale = 1f, bool? loopOverride = null, bool? unscaledOverride = null, float intensity = 1f)
        {
            CacheComponents();

            m_stopping = false;
            m_paused = false;
            m_elapsedUnscaled = 0f;
            m_stopElapsedUnscaled = -1f;
            m_isLoopingRuntime = loopOverride ?? m_looping;
            bool useUnscaled = unscaledOverride ?? m_useUnscaledTime;

            m_appliedTint = (tint ?? Color.white) * m_defaultTint;
            m_appliedScale = Mathf.Max(0.05f, m_defaultScale * scale);
            m_appliedIntensity = Mathf.Clamp(intensity * m_intensity, 0f, 3f);

            transform.localScale = Vector3.one * m_appliedScale;

            for (int i = 0; i < m_systems.Length; i++)
            {
                var ps = m_systems[i];
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = m_isLoopingRuntime;
                main.useUnscaledTime = useUnscaled;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            for (int i = 0; i < m_trails.Length; i++)
            {
                var tr = m_trails[i];
                if (tr == null) continue;
                tr.Clear();
                tr.emitting = true;
                tr.widthMultiplier = Mathf.Max(0.02f, m_appliedScale * 0.24f);
            }

            ApplyVisualParams();

            for (int i = 0; i < m_systems.Length; i++)
            {
                if (m_systems[i] != null) m_systems[i].Play(false);
            }
        }

        public void SetTint(Color tint)
        {
            CacheComponents();
            m_appliedTint = tint * m_defaultTint;
            ApplyVisualParams();
        }

        public void SetScale(float scale)
        {
            CacheComponents();
            m_appliedScale = Mathf.Max(0.05f, m_defaultScale * scale);
            transform.localScale = Vector3.one * m_appliedScale;
        }

        // 闇落ちオーラなど、ループ演出の強さを実行中に 0〜1（最大3）で滑らかに変える
        public void SetIntensity(float intensity)
        {
            CacheComponents();
            m_appliedIntensity = Mathf.Clamp(intensity, 0f, 3f);
            ApplyVisualParams();
        }

        public void SetPaused(bool paused)
        {
            CacheComponents();
            if (m_paused == paused) return;
            m_paused = paused;
            for (int i = 0; i < m_systems.Length; i++)
            {
                var ps = m_systems[i];
                if (ps == null) continue;
                if (paused) ps.Pause(false);
                else if (!m_stopping || ps.particleCount > 0) ps.Play(false);
            }
        }

        public void Stop(bool clearImmediate = false)
        {
            CacheComponents();
            m_stopping = true;
            m_paused = false;
            m_stopElapsedUnscaled = 0f;

            var behavior = clearImmediate
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting;

            for (int i = 0; i < m_systems.Length; i++)
            {
                if (m_systems[i] != null) m_systems[i].Stop(false, behavior);
            }

            for (int i = 0; i < m_trails.Length; i++)
            {
                if (m_trails[i] == null) continue;
                m_trails[i].emitting = false;
                if (clearImmediate) m_trails[i].Clear();
            }

            if (clearImmediate)
            {
                Destroy(gameObject);
            }
        }

        void ApplyVisualParams()
        {
            float alphaFactor = Mathf.Clamp01(m_appliedIntensity);
            for (int i = 0; i < m_renderers.Length; i++)
            {
                var r = m_renderers[i];
                if (r == null) continue;
                Color baseCol = m_baseMaterialColors[i];
                Color finalCol = new Color(
                    baseCol.r * m_appliedTint.r,
                    baseCol.g * m_appliedTint.g,
                    baseCol.b * m_appliedTint.b,
                    baseCol.a * m_appliedTint.a * alphaFactor);
                r.GetPropertyBlock(m_mpb);
                m_mpb.SetColor(BaseColorId, finalCol);
                m_mpb.SetColor(ColorId, finalCol);
                r.SetPropertyBlock(m_mpb);
            }

            for (int i = 0; i < m_systems.Length; i++)
            {
                var ps = m_systems[i];
                if (ps == null) continue;
                var emission = ps.emission;
                if (m_baseRates[i] > 0f)
                {
                    emission.rateOverTimeMultiplier = m_baseRates[i] * m_appliedIntensity;
                }
            }

            for (int i = 0; i < m_trails.Length; i++)
            {
                var tr = m_trails[i];
                if (tr == null) continue;
                Color trailTint = new Color(m_appliedTint.r, m_appliedTint.g, m_appliedTint.b, m_appliedTint.a * alphaFactor);
                tr.GetPropertyBlock(m_mpb);
                m_mpb.SetColor(BaseColorId, trailTint);
                m_mpb.SetColor(ColorId, trailTint);
                tr.SetPropertyBlock(m_mpb);
            }
        }

        void LateUpdate()
        {
            if (!m_initialized || m_paused) return;

            float dt = Time.unscaledDeltaTime;
            m_elapsedUnscaled += dt;
            if (m_stopElapsedUnscaled >= 0f) m_stopElapsedUnscaled += dt;

            if (m_isLoopingRuntime && !m_stopping) return;

            // 生成直後の猶予時間は消さない
            if (m_elapsedUnscaled < m_autoDestroyGraceSeconds) return;

            bool anyAlive = false;
            for (int i = 0; i < m_systems.Length; i++)
            {
                if (m_systems[i] != null && m_systems[i].IsAlive(false))
                {
                    anyAlive = true;
                    break;
                }
            }

            if (!anyAlive && m_maxTrailTime > 0f)
            {
                float refElapsed = m_stopElapsedUnscaled >= 0f ? m_stopElapsedUnscaled : m_elapsedUnscaled;
                if (refElapsed < m_maxTrailTime + 0.1f)
                    anyAlive = true;
            }

            if (!anyAlive)
            {
                Destroy(gameObject);
            }
        }
    }
}
