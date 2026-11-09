using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 積み木の雨(赤のポケット): 止まったポケットの側の盤の上に、空から積み木が降ってくる
    //  ・落ちる場所に先に光る輪(予告)が出て、少し後に真上から積み木が落ちてくる(落ちている積み木として着地する)
    //  ・盤面の積み木の上限(TumikiSpawner)を超える分は降らせない
    public class RouletteTumikiRain : RoulettePocketEvent
    {
        [SerializeField] TumikiBlock m_tumikiPrefab;
        [Tooltip("盤面の上限を調べる TumikiSpawner(未設定なら上限なし)")]
        [SerializeField] TumikiSpawner m_spawner;
        [SerializeField, Min(0)] int m_overLimit = 6;
        [SerializeField, Min(1)] int m_count = 9;
        [Tooltip("ポケットの方向を中心に、左右へ広げる角度")]
        [SerializeField] float m_spread = 55.0f;
        [SerializeField] Vector2 m_radius = new Vector2(3.8f, 11.5f);
        [Tooltip("予告から落とし始めるまでの時間")]
        [SerializeField] float m_warningTime = 0.8f;
        [Tooltip("1個ずつずらす時間")]
        [SerializeField] float m_interval = 0.12f;
        [SerializeField] float m_dropHeight = 9.0f;
        [SerializeField] GimmickBurstSettings m_dropEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 0.95f, 0.6f, 1.0f), ColorB = Color.white,
            Count = 10, Speed = new Vector2(0.8f, 2.0f), Size = new Vector2(0.06f, 0.14f), Lifetime = new Vector2(0.3f, 0.5f),
            Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.4f, 0, 0),
        };

        class Drop
        {
            public Vector3 Point;
            public float Timer;
            public MeshRenderer Ring;
        }

        readonly List<Drop> m_drops = new List<Drop>();
        float m_lastSound;

        public override void Activate(RouletteGame game, int pocket)
        {
            var wheel = game.Wheel;
            float angle = wheel.Angle + wheel.PocketLocalAngle(pocket);
            Rain(wheel, angle, m_spread, m_count);
        }

        // ワールドの角度 centerDeg ± spread の盤の上に count 個降らせる
        public void Rain(RouletteWheel wheel, float centerDeg, float spread, int count)
        {
            if (m_tumikiPrefab == null) return;
            for (int i = 0; i < count; i++)
            {
                var d = new Drop
                {
                    Point = RandomPoint(wheel, centerDeg, spread, m_radius.x, m_radius.y),
                    Timer = m_warningTime + i * m_interval,
                };
                d.Ring = EffectMeshes.Create("TumikiRainRing", transform, EffectMeshes.Ring, true);
                d.Ring.transform.position = d.Point + Vector3.up * 0.04f;
                m_drops.Add(d);
            }
        }

        public override void Stop()
        {
            foreach (var d in m_drops) if (d.Ring != null) Destroy(d.Ring.gameObject);
            m_drops.Clear();
        }

        private void Update()
        {
            if (m_drops.Count == 0 || GimmickClock.IsNetClient) return;
            float dt = GimmickClock.IsHeld ? 0.0f : Time.deltaTime;
            for (int i = m_drops.Count - 1; i >= 0; i--)
            {
                var d = m_drops[i];
                d.Timer -= dt;
                float t = Mathf.Clamp01(1.0f - d.Timer / Mathf.Max(0.01f, m_warningTime));
                d.Ring.transform.localScale = Vector3.one * Mathf.Lerp(1.4f, 0.6f, t);
                EffectMeshes.SetColor(d.Ring, GimmickPalette.WithAlpha(Color, Mathf.Lerp(0.3f, 0.9f, GimmickPalette.Blink(Time.time, 6.0f))));
                if (d.Timer > 0.0f) continue;
                Destroy(d.Ring.gameObject);
                m_drops.RemoveAt(i);
                Spawn(d.Point);
            }
        }

        void Spawn(Vector3 point)
        {
            if (m_spawner != null && TumikiSpawner.CountBlocksOnBoard() >= m_spawner.MaxBlocksOnBoard + m_overLimit) return;
            Vector3 pos = point + Vector3.up * m_dropHeight;
            TumikiBlock block = Instantiate(m_tumikiPrefab, pos, Quaternion.Euler(0.0f, Random.Range(0.0f, 360.0f), 0.0f));
            block.SetColor((TumikiColor)Random.Range(0, 4));
            if (block.Rigidbody != null && !block.Rigidbody.isKinematic) block.Rigidbody.linearVelocity = Vector3.down * 6.0f;
            GimmickBurst.Play(m_dropEffect, pos);
            if (Time.time - m_lastSound > 0.1f)
            {
                m_lastSound = Time.time;
                GimmickSound.Play(GimmickSound.Pop, 0.3f, Random.Range(1.1f, 1.3f));
            }
        }
    }
}
