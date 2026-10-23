using System;
using UnityEngine;

namespace Nakagawa
{
    // 一回だけ噴き出す粒子の形
    public enum GimmickBurstShape
    {
        // 全方向に飛び散る
        Sphere,
        // 上半分に飛び散る
        Hemisphere,
        // 床に平らな円の縁から外へ広がる
        Ring,
        // 床に平らな四角の辺から出る(大きさは ShapeSize の X・Z)
        BoxEdge,
        // 向き(Play に渡す向きの前方)へ円錐状に飛ぶ
        Cone,
    }

    // ギミックの効果が発動したときに一回だけ出すエフェクトの設定(インスペクターから編集する)
    //  ・粒子の噴き出し + 床に広がる衝撃波の輪(任意)
    [Serializable]
    public class GimmickBurstSettings
    {
        public bool Enabled = true;
        // 未設定なら演出用のマテリアル(Additive なら加算、そうでなければ半透明)
        public Material Material;
        public bool Additive = true;
        public Color ColorA = Color.white;
        public Color ColorB = Color.white;
        [Min(0)] public int Count = 16;
        public Vector2 Speed = new Vector2(1.0f, 3.0f);
        public Vector2 Size = new Vector2(0.08f, 0.2f);
        public Vector2 Lifetime = new Vector2(0.3f, 0.6f);
        // 負なら上へ昇る
        public float Gravity = 0.0f;
        public GimmickBurstShape Shape = GimmickBurstShape.Sphere;
        // 形の大きさ(球・円・円錐は X が半径。四角は X・Z が辺の長さ)
        public Vector3 ShapeSize = new Vector3(0.3f, 0.0f, 0.3f);
        [Range(0.0f, 90.0f)] public float ConeAngle = 25.0f;
        // 進む向きに引き伸ばす(勢いの線に見せる)
        public bool Stretch = false;

        [Header("衝撃波の輪")]
        public bool Shockwave = false;
        public Color ShockwaveColor = Color.white;
        public float ShockwaveRadius = 1.5f;
        public float ShockwaveTime = 0.3f;

        // 大きさなどをその場で合わせて使うための複製
        public GimmickBurstSettings Clone() => (GimmickBurstSettings)MemberwiseClone();
    }

    public static class GimmickBurst
    {
        // position に一回だけエフェクトを出す(終わったら自動で消える)
        // rotation: 円錐の向き・四角の向き。scale: 形の大きさ・速さ・衝撃波の半径に掛ける倍率
        // tint: 色に掛ける色(ワープゲートのグループの色など。null なら設定の色のまま)
        public static void Play(GimmickBurstSettings s, Vector3 position, Quaternion rotation, float scale = 1.0f, Color? tint = null)
        {
            if (s == null || !s.Enabled) return;
            Color k = tint ?? Color.white;
            if (s.Count > 0) PlayParticles(s, position, rotation, scale, k);
            if (s.Shockwave) GimmickShockwave.Create(position, s.ShockwaveColor * k, s.ShockwaveRadius * scale, s.ShockwaveTime);
        }

        public static void Play(GimmickBurstSettings s, Vector3 position, float scale = 1.0f)
        {
            Play(s, position, Quaternion.identity, scale);
        }

        static void PlayParticles(GimmickBurstSettings s, Vector3 position, Quaternion rotation, float scale, Color tint)
        {
            var go = new GameObject("GimmickBurst");
            go.transform.position = position;
            // 半球・円は床に平らに(上向きに)する
            bool flat = s.Shape == GimmickBurstShape.Hemisphere || s.Shape == GimmickBurstShape.Ring;
            go.transform.rotation = flat ? rotation * Quaternion.Euler(-90.0f, 0.0f, 0.0f) : rotation;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(s.Lifetime.x, s.Lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(s.Speed.x * scale, s.Speed.y * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(s.Size.x, s.Size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(s.ColorA * tint, s.ColorB * tint);
            main.gravityModifier = s.Gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(1, s.Count);
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = ps.emission;
            emission.rateOverTime = 0.0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, (short)Mathf.Min(s.Count, short.MaxValue)) });

            var shape = ps.shape;
            shape.radius = Mathf.Max(0.001f, s.ShapeSize.x * scale);
            switch (s.Shape)
            {
                case GimmickBurstShape.Sphere: shape.shapeType = ParticleSystemShapeType.Sphere; break;
                case GimmickBurstShape.Hemisphere: shape.shapeType = ParticleSystemShapeType.Hemisphere; break;
                case GimmickBurstShape.Ring:
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radiusThickness = 0.0f;
                    break;
                case GimmickBurstShape.BoxEdge:
                    // 箱の粒子は形の前方(Z)へ飛ぶので、前方が上を向くように寝かせる(X・Z の四角の辺から上へ噴き出す)
                    shape.shapeType = ParticleSystemShapeType.BoxEdge;
                    shape.rotation = new Vector3(-90.0f, 0.0f, 0.0f);
                    shape.scale = new Vector3(s.ShapeSize.x * scale, s.ShapeSize.z * scale, Mathf.Max(0.01f, s.ShapeSize.y * scale));
                    shape.randomDirectionAmount = 0.35f;
                    break;
                default:
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = s.ConeAngle;
                    break;
            }

            // 現れてから透明になって消える
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
                new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0.0f, 1.0f) });
            col.color = gradient;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 0.2f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = s.Material != null ? s.Material : s.Additive ? EffectMeshes.AdditiveMaterial : EffectMeshes.AlphaMaterial;
            if (s.Stretch)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.08f;
                renderer.lengthScale = 2.0f;
            }

            ps.Play();
        }
    }

    // 床に平らに広がって消える光の輪(GimmickBurst の衝撃波)
    public class GimmickShockwave : MonoBehaviour
    {
        Renderer m_renderer;
        Color m_color;
        float m_radius;
        float m_duration;
        float m_time;

        public static GimmickShockwave Create(Vector3 position, Color color, float radius, float duration)
        {
            var go = new GameObject("GimmickShockwave");
            go.transform.position = position + Vector3.up * 0.03f;
            var w = go.AddComponent<GimmickShockwave>();
            w.m_renderer = EffectMeshes.Create("Ring", go.transform, EffectMeshes.Ring, true);
            w.m_color = color;
            w.m_radius = Mathf.Max(0.05f, radius);
            w.m_duration = Mathf.Max(0.02f, duration);
            w.Apply();
            return w;
        }

        private void Update()
        {
            m_time += Time.deltaTime;
            if (m_time >= m_duration)
            {
                Destroy(gameObject);
                return;
            }
            Apply();
        }

        // 素早く広がり、広がるにつれて薄くなる
        private void Apply()
        {
            float t = Mathf.Clamp01(m_time / m_duration);
            float r = m_radius * Mathf.Lerp(0.15f, 1.0f, 1.0f - (1.0f - t) * (1.0f - t));
            m_renderer.transform.localScale = new Vector3(r, 1.0f, r);
            EffectMeshes.SetColor(m_renderer, GimmickPalette.WithAlpha(m_color, m_color.a * (1.0f - t)));
        }
    }
}
