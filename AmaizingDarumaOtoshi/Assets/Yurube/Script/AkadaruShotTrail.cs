using Nakahira;
using UnityEngine;

namespace Yurube
{
    // あかだるの弾速を目に見える形にする演出。
    // ・太く長い残光トレイル
    // ・弾のまわりに燃える火の粉(パーティクル)
    // ・弾が照らす点光源
    // どれも弾速が速いほど大きく・明るくなる。
    [RequireComponent(typeof(TumikiBlock))]
    public class AkadaruShotTrail : MonoBehaviour
    {
        [Tooltip("トレイルの寿命(秒) 遅い→速い")]
        [SerializeField] float m_minTime = 1.15f;
        [SerializeField] float m_maxTime = 2.55f;
        [Tooltip("トレイルの太さ 遅い→速い")]
        [SerializeField] float m_minWidth = 1.12f;
        [SerializeField] float m_maxWidth = 2.55f;
        [Tooltip("火の粉の量(個/秒) 遅い→速い")]
        [SerializeField] float m_minCometRate = 130.0f;
        [SerializeField] float m_maxCometRate = 290.0f;
        [Tooltip("光の強さ 遅い→速い")]
        [SerializeField] float m_minLight = 11.5f;
        [SerializeField] float m_maxLight = 26.0f;

        TumikiBlock m_block;
        TrailRenderer m_trail;
        ParticleSystem m_comet;
        Light m_light;
        // 積み木が元々持っている煙・炎のエフェクト(あかだるの弾の間だけ消す)
        ParticleSystem[] m_blockEffects;
        float m_minSpeed = 10.0f;
        float m_maxSpeed = 60.0f;

        // この発射で演出を付けたか(同じ積み木を何度も拾い直さないためのフラグ)
        public bool ConsumedLaunch { get; private set; }

        private void Awake()
        {
            m_block = GetComponent<TumikiBlock>();
            enabled = false;
        }

        // あかだるのパッシブから、飛び始めた積み木に対して呼ばれる
        public void Begin(Material material, float minSpeed, float maxSpeed, Vector3 muzzlePosition)
        {
            m_minSpeed = Mathf.Max(0.01f, minSpeed);
            m_maxSpeed = Mathf.Max(m_minSpeed + 0.01f, maxSpeed);
            ConsumedLaunch = true;

            CreateEffect(material);
            HideBlockEffects();
            UpdateEffect();
            enabled = true;

            // 撃った瞬間だけマズルフラッシュ(打ち返しでは出さない)
            if (m_block != null && m_block.ReturnCount == 0)
            {
                AkadaruShotFlash.Spawn(muzzlePosition, m_block.Direction, m_block.Speed,
                    m_minSpeed, m_maxSpeed, material);
            }
        }

        private void Update()
        {
            if (m_block == null || m_trail == null) return;
            if (m_block.CurrentState != TumikiBlock.State.Flying)
            {
                // 落ちたら元の煙・炎を戻す(次に誰が投げても普通の積み木に戻る)
                StopEffect(true);
                return;
            }

            m_trail.transform.position = transform.position;
            UpdateEffect();
        }

        private void OnDestroy()
        {
            // 積み木ごと消えるときは、元のエフェクトを戻さない
            StopEffect(false);
        }

        // 積み木が元々持っている煙・炎を、あかだるの弾の間だけ消す。
        // (共有プレハブは触らず、この積み木のインスタンスだけ止める)
        private void HideBlockEffects()
        {
            if (m_blockEffects != null) return;

            var effects = GetComponentsInChildren<ParticleSystem>(true);
            if (effects == null || effects.Length == 0) return;

            m_blockEffects = effects;
            foreach (var ps in m_blockEffects)
            {
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.gameObject.SetActive(false);
            }
        }

        private void RestoreBlockEffects()
        {
            if (m_blockEffects == null) return;

            foreach (var ps in m_blockEffects)
            {
                if (ps == null) continue;
                ps.gameObject.SetActive(true);
                ps.Play();
            }
            m_blockEffects = null;
        }

        private void CreateEffect(Material material)
        {
            if (m_trail != null) return;

            var go = new GameObject("AkadaruShotFX");
            go.transform.position = transform.position;

            m_trail = go.AddComponent<TrailRenderer>();
            if (material != null) m_trail.sharedMaterial = material;
            m_trail.time = m_minTime;
            m_trail.minVertexDistance = 0.05f;
            m_trail.autodestruct = false;
            m_trail.emitting = true;
            m_trail.alignment = LineAlignment.View;
            m_trail.textureMode = LineTextureMode.Stretch;
            m_trail.numCapVertices = 2;
            m_trail.numCornerVertices = 2;
            m_trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_trail.receiveShadows = false;

            // 弾のあとを追いかける火の粉(ワールド空間に残るので尾になる)
            m_comet = go.AddComponent<ParticleSystem>();
            m_comet.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = m_comet.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = 0.0f;
            main.startSize = 0.4f;
            main.startColor = new Color(1.0f, 0.5f, 0.15f, 1.0f);
            main.maxParticles = 256;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = m_comet.emission;
            emission.enabled = true;
            emission.rateOverTime = m_minCometRate;

            var shape = m_comet.shape;
            shape.enabled = false;

            var colorOverLifetime = m_comet.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.0f, 0.8f, 0.4f), 0.0f),
                    new GradientColorKey(new Color(1.0f, 0.25f, 0.05f), 1.0f)
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(0.7f, 0.3f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = m_comet.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.EaseInOut(0.0f, 1.0f, 1.0f, 0.4f));

            var renderer = m_comet.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            m_comet.Play();

            // 弾がまわりを照らす
            m_light = go.AddComponent<Light>();
            m_light.type = LightType.Point;
            m_light.color = new Color(1.0f, 0.55f, 0.2f);
            m_light.range = 4.0f;
            m_light.intensity = m_minLight;
            m_light.shadows = LightShadows.None;
        }

        // 速度に応じて長さ・太さ・色・光を変える
        private void UpdateEffect()
        {
            if (m_trail == null || m_block == null) return;

            float t = Mathf.InverseLerp(m_minSpeed, m_maxSpeed, m_block.Speed);

            m_trail.time = Mathf.Lerp(m_minTime, m_maxTime, t);
            m_trail.startWidth = Mathf.Lerp(m_minWidth, m_maxWidth, t);
            m_trail.endWidth = 0.0f;
            m_trail.startColor = Color.Lerp(new Color(1.0f, 0.35f, 0.10f, 0.9f), new Color(1.0f, 0.95f, 0.80f, 1.0f), t);
            m_trail.endColor = new Color(1.0f, 0.30f, 0.05f, 0.0f);

            if (m_comet != null)
            {
                var main = m_comet.main;
                main.startSize = Mathf.Lerp(0.35f, 0.90f, t);
                main.startColor = Color.Lerp(new Color(1.0f, 0.45f, 0.10f, 1.0f), new Color(1.0f, 0.95f, 0.75f, 1.0f), t);
                var emission = m_comet.emission;
                emission.rateOverTime = Mathf.Lerp(m_minCometRate, m_maxCometRate, t);
            }

            if (m_light != null)
            {
                m_light.intensity = Mathf.Lerp(m_minLight, m_maxLight, t);
                m_light.range = Mathf.Lerp(3.5f, 7.0f, t);
            }
        }

        private void StopEffect(bool restoreBlockEffects)
        {
            ConsumedLaunch = false;
            enabled = false;

            if (restoreBlockEffects) RestoreBlockEffects();

            if (m_trail == null) return;

            m_trail.emitting = false;
            if (m_comet != null)
            {
                var emission = m_comet.emission;
                emission.rateOverTime = 0.0f;
            }
            if (m_light != null) m_light.enabled = false;

            Destroy(m_trail.gameObject, Mathf.Max(m_trail.time, 0.4f) + 0.3f);
            m_trail = null;
            m_comet = null;
            m_light = null;
        }
    }
}
