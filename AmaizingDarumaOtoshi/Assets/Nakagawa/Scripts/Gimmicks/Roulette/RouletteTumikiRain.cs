using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 積み木の雨(赤のポケット): 止まったポケットの側の盤の上に、空から積み木が降ってくる
    //  ・落ちる場所に先に光る輪(予告)が出て、少し後に真上から積み木が落ちてくる(落ちている積み木として着地する)
    //  ・盤面の積み木の上限(TumikiSpawner)を超える分は降らせない
    public class RouletteTumikiRain : RoulettePocketEvent, INetGimmickState
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
            using var relay = GimmickNet.Relay();   // ネット対戦用(suehara)
            float dt = GimmickClock.IsHeld ? 0.0f : Time.deltaTime;
            for (int i = m_drops.Count - 1; i >= 0; i--)
            {
                var d = m_drops[i];
                d.Timer -= dt;
                UpdateRing(d.Ring, RingProgress(d));
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

        float RingProgress(Drop d) => Mathf.Clamp01(1.0f - d.Timer / Mathf.Max(0.01f, m_warningTime));

        void UpdateRing(MeshRenderer ring, float t)
        {
            ring.transform.localScale = Vector3.one * Mathf.Lerp(1.4f, 0.6f, t);
            EffectMeshes.SetColor(ring, GimmickPalette.WithAlpha(Color, Mathf.Lerp(0.3f, 0.9f, GimmickPalette.Blink(Time.time, 6.0f))));
        }

        //========================================
        // ネット対戦用(suehara): 落ちる場所の輪をホストから受け取る(降ってくる積み木はネットワークに乗っているので別に届く)
        //========================================

        const int kMaxNetDrops = 32;

        public void WriteNetState(GimmickNetState s)
        {
            int n = Mathf.Min(m_drops.Count, kMaxNetDrops);
            s.AddInt(n);
            for (int i = 0; i < n; i++)
            {
                s.AddVec3(m_drops[i].Point);
                s.Add(RingProgress(m_drops[i]));
            }
        }

        public void ReadNetState(GimmickNetState s)
        {
            int n = Mathf.Clamp(s.Int(), 0, kMaxNetDrops);
            if (!s.Ok) return;
            while (m_drops.Count > n)
            {
                var last = m_drops[m_drops.Count - 1];
                if (last.Ring != null) Destroy(last.Ring.gameObject);
                m_drops.RemoveAt(m_drops.Count - 1);
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 point = s.Vec3();
                float t = s.Float();
                if (!s.Ok) return;
                if (i >= m_drops.Count) m_drops.Add(new Drop { Ring = EffectMeshes.Create("TumikiRainRing", transform, EffectMeshes.Ring, true) });
                var d = m_drops[i];
                d.Point = point;
                d.Ring.transform.position = point + Vector3.up * 0.04f;
                UpdateRing(d.Ring, t);
            }
        }
    }
}
