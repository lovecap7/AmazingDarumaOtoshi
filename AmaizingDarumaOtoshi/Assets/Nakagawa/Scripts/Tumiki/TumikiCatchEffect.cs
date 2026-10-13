using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // プレイヤーが攻撃判定のある(飛んできた)積み木に上から乗って止めたときの成功エフェクト(シーンに1つ置く)
    // エフェクトのプレハブを設定すればそれを出し、未設定なら下の設定で光の粒が弾けるエフェクトを作って出す
    public class TumikiCatchEffect : MonoBehaviour
    {
        [Tooltip("攻撃判定のある積み木に乗ったときだけ出す(オフなら拾うたびに出す)")]
        [SerializeField] bool m_onlyHarmful = true;
        [Tooltip("出すエフェクト(未設定なら既定のエフェクトを作る)")]
        [SerializeField] GameObject m_effectPrefab;
        [Tooltip("プレハブのエフェクトを消すまでの時間(秒)")]
        [SerializeField, Min(0.1f)] float m_prefabLifetime = 3.0f;
        [Tooltip("積み木の位置からずらす高さ")]
        [SerializeField] float m_heightOffset = 0.4f;

        [Header("既定のエフェクト")]
        [SerializeField] Material m_particleMaterial;
        [SerializeField] Color m_color = new Color(1.0f, 0.85f, 0.25f, 1.0f);
        [Tooltip("飛び散る光の粒の数")]
        [SerializeField, Min(1)] int m_sparkCount = 28;
        [SerializeField, Min(0.0f)] float m_sparkSpeed = 6.0f;
        [SerializeField, Min(0.01f)] float m_sparkSize = 0.22f;
        [SerializeField, Min(0.05f)] float m_lifetime = 0.55f;
        [Tooltip("中心で一瞬光る大きさ(0なら光らない)")]
        [SerializeField, Min(0.0f)] float m_flashSize = 2.2f;

        [Header("カメラの揺れ")]
        [SerializeField] bool m_shakeCamera = false;
        [SerializeField] CameraShake.ShakeSettings m_shake = new CameraShake.ShakeSettings(0.12f, 0.15f);

        private void OnEnable()
        {
            DarumaCharacter.AnyPickedUp += OnPickedUp;
        }

        private void OnDisable()
        {
            DarumaCharacter.AnyPickedUp -= OnPickedUp;
        }

        private void OnPickedUp(TumikiPickupInfo info)
        {
            if (m_onlyHarmful && !info.WasHarmful) return;
            Vector3 pos = info.Block != null ? info.Block.transform.position : info.Character.transform.position;
            Play(pos + Vector3.up * m_heightOffset);
        }

        // エフェクトを出す(他の演出から呼んでもよい)
        public void Play(Vector3 position)
        {
            if (m_effectPrefab != null)
            {
                Destroy(Instantiate(m_effectPrefab, position, Quaternion.identity), m_prefabLifetime);
            }
            else
            {
                var root = new GameObject("TumikiCatchEffect");
                root.transform.position = position;
                CreateSparks(root.transform);
                if (m_flashSize > 0.0f) CreateFlash(root.transform);
                Destroy(root, m_lifetime + 0.5f);
            }

            if (m_shakeCamera && Camera.main != null)
            {
                var shake = Camera.main.GetComponent<CameraShake>();
                if (shake != null) shake.Shake(m_shake);
            }
        }

        // 全方向(やや上向き)に弾けて、小さくなりながら消える光の粒
        private void CreateSparks(Transform parent)
        {
            var ps = CreateParticleSystem("Sparks", parent);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(m_lifetime * 0.6f, m_lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(m_sparkSpeed * 0.5f, m_sparkSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(m_sparkSize * 0.6f, m_sparkSize);
            main.gravityModifier = 0.6f;

            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, (short)m_sparkCount) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.2f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 0.0f));

            ps.Play();
        }

        // 中心で一瞬大きく光って消える
        private void CreateFlash(Transform parent)
        {
            var ps = CreateParticleSystem("Flash", parent);
            var main = ps.main;
            main.startLifetime = m_lifetime * 0.4f;
            main.startSpeed = 0.0f;
            main.startSize = m_flashSize;

            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 1) });

            var shape = ps.shape;
            shape.enabled = false;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.EaseInOut(0.0f, 0.4f, 1.0f, 1.0f));

            ps.Play();
        }

        private ParticleSystem CreateParticleSystem(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = m_lifetime;
            main.startColor = m_color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0.0f;

            // だんだん透明になる
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
                new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 0.5f), new GradientAlphaKey(0.0f, 1.0f) });
            color.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (m_particleMaterial != null) renderer.sharedMaterial = m_particleMaterial;
            return ps;
        }
    }
}
