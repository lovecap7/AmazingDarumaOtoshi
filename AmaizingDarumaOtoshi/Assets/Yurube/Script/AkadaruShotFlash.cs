using UnityEngine;

namespace Yurube
{
    // あかだるの発射時に出るマズルフラッシュ＆衝撃波。
    // 弾速が速いほど、粒の数・大きさ・広がる速さ・光の強さが増える。
    // 1回の発射ごとに使い捨てで、再生が終わったら自分で消える。
    public class AkadaruShotFlash : MonoBehaviour
    {
        float m_destroyAt;
        Light m_light;
        float m_lightIntensity = 1.0f;

        // speed: 発射時の弾速 / material: パーティクル用マテリアル(FireParticle など)
        public static void Spawn(Vector3 position, Vector3 direction, float speed,
                                 float minSpeed, float maxSpeed, Material material)
        {
            float t = Mathf.InverseLerp(minSpeed, maxSpeed, speed);

            var root = new GameObject("AkadaruMuzzleFlash");
            // 非アクティブの間に設定する(再生中の設定変更を避ける)
            root.SetActive(false);
            root.transform.position = position;
            root.transform.rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction.normalized)
                : Quaternion.identity;

            var core = CreateBurst(root.transform, "Core", t, material, false);
            var wave = CreateBurst(root.transform, "Wave", t, material, true);

            // 発射の瞬間、まわりを一瞬照らす
            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1.0f, 0.65f, 0.30f);
            light.range = Mathf.Lerp(4.0f, 9.0f, t);
            light.intensity = Mathf.Lerp(5.0f, 14.0f, t);
            light.shadows = LightShadows.None;

            root.SetActive(true);
            core.Play();
            wave.Play();

            var flash = root.AddComponent<AkadaruShotFlash>();
            flash.m_light = light;
            flash.m_lightIntensity = light.intensity;
            flash.m_destroyAt = Time.time + 0.8f;
        }

        // ring: true なら衝撃波の輪、false なら前方への火の粉
        private static ParticleSystem CreateBurst(Transform parent, string name, float t, Material material, bool ring)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            main.stopAction = ParticleSystemStopAction.None;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1.0f, 0.35f, 0.08f), new Color(1.0f, 0.98f, 0.80f));

            if (ring)
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.45f);
                main.startSpeed = 0.0f;
                main.startSize = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0.18f, 0.30f, t), Mathf.Lerp(0.30f, 0.50f, t));

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.25f;
                shape.radiusThickness = 0.0f;   // 輪のフチから出す
                shape.arc = 360.0f;

                // 中心軸(Z)から外へ広がる → 輪が大きくなる
                // (x/y/z/radial は同じカーブモードで揃えないと Unity がエラーを出す)
                var velocity = ps.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.x = new ParticleSystem.MinMaxCurve(0.0f, 0.0f);
                velocity.y = new ParticleSystem.MinMaxCurve(0.0f, 0.0f);
                velocity.z = new ParticleSystem.MinMaxCurve(0.0f, 0.0f);
                velocity.radial = new ParticleSystem.MinMaxCurve(Mathf.Lerp(5.0f, 14.0f, t), Mathf.Lerp(8.0f, 20.0f, t));
            }
            else
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(Mathf.Lerp(6.0f, 12.0f, t), Mathf.Lerp(14.0f, 30.0f, t));
                main.startSize = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0.30f, 0.50f, t), Mathf.Lerp(0.50f, 0.90f, t));

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 35.0f;
                shape.radius = 0.10f;
            }

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0.0f;
            int count = Mathf.RoundToInt(ring
                ? Mathf.Lerp(28.0f, 64.0f, t)
                : Mathf.Lerp(24.0f, 60.0f, t));
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, (short)count) });

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.0f, 0.85f, 0.50f), 0.0f),
                    new GradientColorKey(new Color(1.0f, 0.25f, 0.05f), 1.0f)
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(0.8f, 0.3f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.EaseInOut(0.0f, 1.0f, 1.0f, 0.15f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return ps;
        }

        private void Update()
        {
            float remaining = Mathf.Clamp01((m_destroyAt - Time.time) / 0.8f);
            if (m_light != null) m_light.intensity = m_lightIntensity * remaining;
            if (Time.time >= m_destroyAt) Destroy(gameObject);
        }
    }
}
