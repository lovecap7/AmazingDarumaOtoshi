using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 低重力エリア。直方体の範囲内の重力を弱める
    // 範囲を紫色の半透明の箱で表示し、範囲内のプレイヤー・積み木から紫の粒子を出す
    // このTransformの位置が範囲の底面の中心
    [DefaultExecutionOrder(-10)]
    public class LowGravityArea : MonoBehaviour
    {
        [SerializeField] Vector3 m_size = new Vector3(8.0f, 5.0f, 8.0f);
        // 範囲内の重力の倍率
        [SerializeField, Range(0.0f, 1.0f)] float m_gravityScale = 0.3f;

        [Header("見た目")]
        // 範囲を表す箱(大きさを範囲に合わせる。当たり判定なし)
        [SerializeField] Transform m_volume;

        [Header("エフェクト")]
        [SerializeField] Material m_effectMaterial;
        [SerializeField] Color m_effectColorA = new Color(0.75f, 0.35f, 1.0f, 1.0f);
        [SerializeField] Color m_effectColorB = new Color(1.0f, 0.6f, 1.0f, 1.0f);
        // 1秒あたりに出す粒子の数
        [SerializeField] float m_effectRate = 25.0f;
        [SerializeField] float m_effectLifetime = 1.0f;

        readonly List<IGimmickAffectable> m_targets = new List<IGimmickAffectable>();
        // 範囲内にいる相手ごとのエフェクト
        readonly Dictionary<IGimmickAffectable, ParticleSystem> m_effects = new Dictionary<IGimmickAffectable, ParticleSystem>();
        readonly List<IGimmickAffectable> m_exited = new List<IGimmickAffectable>();

        public Vector3 Size => m_size;
        public float GravityScale => m_gravityScale;

        private void Awake()
        {
            ApplyShape();
        }

        private void OnValidate()
        {
            m_size = Vector3.Max(m_size, new Vector3(0.1f, 0.1f, 0.1f));
            ApplyShape();
        }

        private void FixedUpdate()
        {
            Vector3 center = transform.position + transform.rotation * new Vector3(0.0f, m_size.y * 0.5f, 0.0f);
            GimmickUtil.OverlapBox(center, m_size * 0.5f, transform.rotation, m_targets);

            foreach (var target in m_targets)
            {
                target.RequestGravityScale(m_gravityScale);
                if (!m_effects.ContainsKey(target)) m_effects[target] = CreateEffect(target.Rigidbody.transform);
            }
            RemoveExitedEffects();
        }

        private void OnDisable()
        {
            foreach (var ps in m_effects.Values) StopEffect(ps);
            m_effects.Clear();
        }

        // 範囲から出た(積まれた・破棄された)相手のエフェクトを止める
        private void RemoveExitedEffects()
        {
            m_exited.Clear();
            foreach (var pair in m_effects)
            {
                if (!m_targets.Contains(pair.Key)) m_exited.Add(pair.Key);
            }
            foreach (var key in m_exited)
            {
                StopEffect(m_effects[key]);
                m_effects.Remove(key);
            }
        }

        private void StopEffect(ParticleSystem ps)
        {
            if (ps == null) return;
            // 出ている粒子は自然に消えるまで残す
            ps.transform.SetParent(null, true);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(ps.gameObject, m_effectLifetime + 0.1f);
        }

        // 足元からふわふわ立ち上る紫の粒子
        private ParticleSystem CreateEffect(Transform parent)
        {
            var go = new GameObject("LowGravityEffect");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.rotation = Quaternion.Euler(-90.0f, 0.0f, 0.0f);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(m_effectLifetime * 0.6f, m_effectLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(m_effectColorA, m_effectColorB);
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.rateOverTime = m_effectRate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 15.0f;
            shape.radius = 0.45f;

            // だんだん透明になって消える
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
            if (m_effectMaterial != null) renderer.sharedMaterial = m_effectMaterial;

            ps.Play();
            return ps;
        }

        // 範囲の大きさに合わせて箱の見た目を変える(エディタから設定を変えたときにも呼ぶ)
        public void ApplyShape()
        {
            if (m_volume == null) return;
            m_volume.localPosition = new Vector3(0.0f, m_size.y * 0.5f, 0.0f);
            m_volume.localRotation = Quaternion.identity;
            m_volume.localScale = m_size;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.7f, 0.3f, 1.0f, 0.9f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0.0f, m_size.y * 0.5f, 0.0f), m_size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
