using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nakagawa
{
    // ギミックの範囲にいる相手から出す粒子の設定(インスペクターから編集する)
    [Serializable]
    public class GimmickParticleSettings
    {
        // 未設定なら演出用の加算マテリアル
        public Material Material;
        public Color ColorA = Color.white;
        public Color ColorB = Color.white;
        [Min(0.0f)] public float Rate = 25.0f;
        [Min(0.01f)] public float Lifetime = 0.6f;
        public Vector2 Speed = new Vector2(0.5f, 1.5f);
        public Vector2 Size = new Vector2(0.08f, 0.2f);
        // 負なら上へ昇る
        public float Gravity = 0.0f;
        // 足元からの高さ
        public float Height = 0.1f;
        // 出す範囲(円錐の角度・底の半径)
        [Range(0.0f, 90.0f)] public float ConeAngle = 20.0f;
        [Min(0.0f)] public float Radius = 0.45f;
        // 進む向きに引き伸ばす(勢いの線に見せる)
        public bool Stretch = false;
    }

    public static class GimmickParticles
    {
        // 相手に付ける粒子を作る(すぐ出し始める)
        public static ParticleSystem Create(string name, Transform parent, GimmickParticleSettings s)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.up * s.Height;
            go.transform.rotation = Quaternion.Euler(-90.0f, 0.0f, 0.0f);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(s.Lifetime * 0.6f, s.Lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(s.Speed.x, s.Speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(s.Size.x, s.Size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(s.ColorA, s.ColorB);
            main.gravityModifier = s.Gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.rateOverTime = s.Rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = s.ConeAngle;
            shape.radius = s.Radius;

            // 現れてから透明になって消える
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
                new[] { new GradientAlphaKey(0.0f, 0.0f), new GradientAlphaKey(1.0f, 0.15f), new GradientAlphaKey(0.0f, 1.0f) });
            col.color = gradient;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 0.3f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = s.Material != null ? s.Material : EffectMeshes.AdditiveMaterial;
            if (s.Stretch)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.15f;
                renderer.lengthScale = 2.0f;
            }

            ps.Play();
            return ps;
        }

        // 出し終えて、出ている粒子が自然に消えるまで残してから消す
        public static void Stop(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.transform.SetParent(null, true);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(ps.gameObject, ps.main.startLifetime.constantMax + 0.1f);
        }
    }

    // 範囲にいる相手ごとの粒子(範囲に入ったら付け、出たら止める)
    // 毎ステップ Begin → 範囲にいる相手を Touch → End の順に呼ぶ
    public class GimmickTargetEffects
    {
        readonly string m_name;
        readonly Func<GimmickParticleSettings> m_settings;
        readonly Dictionary<IGimmickAffectable, ParticleSystem> m_effects = new Dictionary<IGimmickAffectable, ParticleSystem>();
        readonly HashSet<IGimmickAffectable> m_touched = new HashSet<IGimmickAffectable>();
        readonly List<IGimmickAffectable> m_exited = new List<IGimmickAffectable>();

        // settings: 粒子を作るたびに読む(再生中にインスペクターで変えても次に作る粒子から反映される)
        public GimmickTargetEffects(string name, Func<GimmickParticleSettings> settings)
        {
            m_name = name;
            m_settings = settings;
        }

        public void Begin() => m_touched.Clear();

        public void Touch(IGimmickAffectable target)
        {
            if (!m_touched.Add(target) || m_effects.ContainsKey(target)) return;
            m_effects[target] = GimmickParticles.Create(m_name, target.Rigidbody.transform, m_settings());
        }

        public void End()
        {
            m_exited.Clear();
            foreach (var pair in m_effects)
            {
                if (!m_touched.Contains(pair.Key)) m_exited.Add(pair.Key);
            }
            foreach (var key in m_exited)
            {
                GimmickParticles.Stop(m_effects[key]);
                m_effects.Remove(key);
            }
        }

        public void Clear()
        {
            foreach (var ps in m_effects.Values) GimmickParticles.Stop(ps);
            m_effects.Clear();
            m_touched.Clear();
        }
    }
}
