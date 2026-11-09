using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 重りの雨(黒のポケット): 止まったポケットの側の盤の上に、空から黒い重りが落ちてくる
    //  ・落ちる場所に影が出て、だんだん濃く大きくなる(予告) → 重りがドスンと落ちる
    //  ・落ちた所の周りのプレイヤー・積み木を外向きに吹き飛ばす(穴の近くだと落ちる危険)。重りは少し残ってから床に沈んで消える
    //  ・床のない所(奈落)に落ちた重りは、そのまま下へ落ちていく
    // 重りの見た目は m_template(無効にした見本)を複製して使う(当たり判定なし)
    public class RouletteWeightRain : RoulettePocketEvent
    {
        [SerializeField] GameObject m_template;
        [SerializeField, Min(1)] int m_count = 4;
        [SerializeField] float m_spread = 55.0f;
        [SerializeField] Vector2 m_radius = new Vector2(3.8f, 11.5f);
        [Tooltip("影が出てから落ちるまで")]
        [SerializeField] float m_warningTime = 1.5f;
        [Tooltip("落ちてくる時間(予告の最後のこの時間で落ちる)")]
        [SerializeField] float m_fallTime = 0.45f;
        [SerializeField] float m_interval = 0.25f;
        [SerializeField] float m_height = 14.0f;
        [Header("吹き飛ばし")]
        [SerializeField] float m_impactRadius = 2.8f;
        [SerializeField] float m_power = 15.0f;
        [SerializeField] float m_upPower = 4.0f;
        [SerializeField] float m_stayTime = 0.9f;
        [SerializeField] float m_sinkTime = 0.5f;
        [SerializeField] GimmickBurstSettings m_impactEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.55f, 0.5f, 0.45f, 0.9f), ColorB = new Color(0.3f, 0.28f, 0.26f, 0.8f),
            Count = 30, Speed = new Vector2(2.0f, 6.0f), Size = new Vector2(0.15f, 0.4f), Lifetime = new Vector2(0.4f, 0.8f),
            Gravity = 0.6f, Shape = GimmickBurstShape.Ring, ShapeSize = new Vector3(0.9f, 0, 0),
            Shockwave = true, ShockwaveColor = new Color(0.2f, 0.2f, 0.25f, 0.85f), ShockwaveRadius = 3.2f, ShockwaveTime = 0.3f,
        };

        class Weight
        {
            public Vector3 Point;
            public float Timer;        // 落ちるまでの残り時間
            public float After;        // 落ちてからの時間
            public bool Landed, Falling;
            public float FallSpeed;
            public Transform Body;
            public MeshRenderer Shadow;
            public Vector3 Scale;
        }

        readonly List<Weight> m_weights = new List<Weight>();
        readonly List<IGimmickAffectable> m_targets = new List<IGimmickAffectable>();

        public override void Activate(RouletteGame game, int pocket)
        {
            var wheel = game.Wheel;
            Rain(wheel, wheel.Angle + wheel.PocketLocalAngle(pocket), m_spread, m_count);
        }

        public void Rain(RouletteWheel wheel, float centerDeg, float spread, int count)
        {
            if (m_template == null) return;
            for (int i = 0; i < count; i++)
            {
                var w = new Weight
                {
                    Point = RandomPoint(wheel, centerDeg, spread, m_radius.x, m_radius.y),
                    Timer = m_warningTime + i * m_interval,
                };
                var body = Instantiate(m_template, transform);
                body.SetActive(true);
                w.Body = body.transform;
                w.Scale = w.Body.localScale;
                w.Body.position = w.Point + Vector3.up * m_height;
                w.Body.rotation = Quaternion.Euler(0.0f, Random.Range(0.0f, 360.0f), 0.0f);
                w.Shadow = EffectMeshes.Create("WeightShadow", transform, EffectMeshes.Disc, false);
                w.Shadow.transform.position = w.Point + Vector3.up * 0.05f;
                m_weights.Add(w);
            }
        }

        public override void Stop()
        {
            foreach (var w in m_weights)
            {
                if (w.Body != null) Destroy(w.Body.gameObject);
                if (w.Shadow != null) Destroy(w.Shadow.gameObject);
            }
            m_weights.Clear();
        }

        private void Update()
        {
            if (m_weights.Count == 0 || GimmickClock.IsNetClient) return;
            float dt = GimmickClock.IsHeld ? 0.0f : Time.deltaTime;
            for (int i = m_weights.Count - 1; i >= 0; i--)
            {
                var w = m_weights[i];
                if (!w.Landed && !w.Falling)
                {
                    w.Timer -= dt;
                    float total = m_warningTime;
                    float t = Mathf.Clamp01(1.0f - w.Timer / total);
                    // 影: だんだん濃く大きく
                    w.Shadow.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.3f, t);
                    EffectMeshes.SetColor(w.Shadow, new Color(0, 0, 0, Mathf.Lerp(0.15f, 0.7f, t)));
                    // 最後の m_fallTime で落ちる
                    float f = Mathf.Clamp01(1.0f - w.Timer / m_fallTime);
                    w.Body.position = w.Point + Vector3.up * (m_height * (1.0f - f * f));
                    if (w.Timer <= 0.0f) Land(w);
                    continue;
                }
                if (w.Falling)
                {
                    // 奈落に落ちていく
                    w.FallSpeed += 30.0f * dt;
                    w.Body.position += Vector3.down * (w.FallSpeed * dt);
                    if (w.Body.position.y < -20.0f) Remove(i);
                    continue;
                }
                w.After += dt;
                if (w.After > m_stayTime)
                {
                    float s = Mathf.Clamp01((w.After - m_stayTime) / m_sinkTime);
                    w.Body.localScale = w.Scale * (1.0f - s);
                    w.Body.position = w.Point + Vector3.down * (s * 0.8f);
                    if (s >= 1.0f) Remove(i);
                }
            }
        }

        void Land(Weight w)
        {
            Destroy(w.Shadow.gameObject);
            w.Shadow = null;
            // 床がなければ奈落へ落ちていく
            bool floor = Physics.Raycast(w.Point + Vector3.up * 0.5f, Vector3.down, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (!floor)
            {
                w.Falling = true;
                w.FallSpeed = 20.0f;
                return;
            }
            w.Landed = true;
            w.Body.position = w.Point;

            // 周りを外向きに吹き飛ばす
            GimmickUtil.OverlapBox(w.Point + Vector3.up * 1.5f, new Vector3(m_impactRadius, 1.5f, m_impactRadius), Quaternion.identity, m_targets);
            foreach (var t in m_targets)
            {
                Vector3 d = t.Rigidbody.position - w.Point;
                d.y = 0.0f;
                float dist = d.magnitude;
                if (dist > m_impactRadius) continue;
                Vector3 dir = dist > 0.01f ? d / dist : Vector3.forward;
                float k = Mathf.Lerp(1.0f, 0.55f, dist / m_impactRadius);
                bool isBlock = t is TumikiBlock;
                t.AddKnockback(dir * (m_power * k * (isBlock ? 0.6f : 1.0f)) + Vector3.up * m_upPower);
                if (!isBlock) t.StartHitStop(0.1f, 0.1f, 0.15f);
            }
            GimmickBurst.Play(m_impactEffect, w.Point + Vector3.up * 0.1f);
            GimmickSound.Play(BilliardFx.Drop, 0.6f, Random.Range(0.6f, 0.75f));
            GimmickSound.Play(GimmickSound.Rumble, 0.35f, 1.6f);
            BilliardFx.Shake(0.35f, 0.3f);
        }

        void Remove(int i)
        {
            var w = m_weights[i];
            if (w.Body != null) Destroy(w.Body.gameObject);
            if (w.Shadow != null) Destroy(w.Shadow.gameObject);
            m_weights.RemoveAt(i);
        }
    }
}
