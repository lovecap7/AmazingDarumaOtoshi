using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 積み木噴出: 玉が入ったポケットから、台の中央へ向かって積み木が扇状に噴き出す
    //  ・ポケットが光って膨らむ(ため) → ポンポンと1個ずつ撃ち出す(持ち主なし。速いうちは当たると危ない)
    //  ・盤面の積み木の上限(TumikiSpawner)を超える分は撃たない
    //  ・撃ち出した積み木は着地すると普通の落ちている積み木になるので、噴き出したポケットの周りが取り合いになる
    public class BilliardFountainGimmick : BilliardPocketGimmick, INetGimmickState
    {
        [SerializeField] TumikiBlock m_tumikiPrefab;
        [Tooltip("盤面の上限を調べる TumikiSpawner(未設定なら上限なし)")]
        [SerializeField] TumikiSpawner m_spawner;
        [Tooltip("盤面の上限に対して、この数までは超えて撃ってよい")]
        [SerializeField, Min(0)] int m_overLimit = 4;
        [SerializeField, Min(1)] int m_count = 7;
        [SerializeField] float m_chargeTime = 0.8f;
        [SerializeField] float m_interval = 0.12f;
        [SerializeField] Vector2 m_speed = new Vector2(8.0f, 13.0f);
        [Tooltip("中央への向きを中心に、左右へばらまく角度")]
        [SerializeField] float m_spread = 35.0f;
        [Tooltip("撃ち出す位置の、ポケットの中心からの距離(穴の外の床の上になるように)")]
        [SerializeField] float m_muzzleDistance = 2.5f;
        [Tooltip("撃ち出す積み木の色(順に使う。空ならランダム)")]
        [SerializeField] TumikiColor[] m_colors;

        [Header("エフェクト")]
        [SerializeField] GimmickBurstSettings m_chargeEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 0.9f, 0.3f, 1.0f), ColorB = Color.white,
            Count = 20, Speed = new Vector2(0.4f, 1.2f), Size = new Vector2(0.06f, 0.14f), Lifetime = new Vector2(0.5f, 0.8f),
            Gravity = -0.8f, Shape = GimmickBurstShape.Ring, ShapeSize = new Vector3(1.2f, 0, 0),
        };
        [SerializeField] GimmickBurstSettings m_fireEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 0.9f, 0.4f, 1.0f), ColorB = new Color(1.0f, 0.6f, 0.2f, 1.0f),
            Count = 16, Speed = new Vector2(3.0f, 7.0f), Size = new Vector2(0.06f, 0.15f), Lifetime = new Vector2(0.15f, 0.35f),
            Shape = GimmickBurstShape.Cone, ShapeSize = new Vector3(0.4f, 0, 0), ConeAngle = 30.0f, Stretch = true,
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.9f, 0.5f, 0.7f), ShockwaveRadius = 1.4f, ShockwaveTime = 0.2f,
        };
        [SerializeField, Range(0.0f, 1.0f)] float m_fireVolume = 0.45f;

        class Shot
        {
            public Transform Pocket;
            public Vector3 Origin, Center;
            public int Left;
            public float Timer;
            public bool Charging;
            public int Index;
            public MeshRenderer Glow;
            public float Charge;   // ネット対戦用(suehara): ための進み(0〜1)
        }

        readonly List<Shot> m_shots = new List<Shot>();
        BilliardTable m_table;

        public override bool IsActive => m_shots.Count > 0;

        public override void Activate(BilliardTable table, Transform pocket, BilliardBall ball)
        {
            if (m_tumikiPrefab == null) return;
            m_table = table;
            Vector3 origin = pocket != null ? pocket.position : ball.Position;
            origin.y = table.FloorY;
            var shot = new Shot
            {
                Pocket = pocket, Origin = origin, Center = table.Center,
                Left = m_count, Timer = m_chargeTime, Charging = true,
            };
            shot.Glow = EffectMeshes.Create("FountainGlow", transform, EffectMeshes.Disc, true);
            shot.Glow.transform.position = origin + Vector3.up * 0.05f;
            m_shots.Add(shot);
            GimmickBurst.Play(m_chargeEffect, origin + Vector3.up * 0.1f);
            AudioClip clip = GimmickSound.Charge;
            GimmickSound.Play(clip, 0.3f, clip.length / Mathf.Max(0.1f, m_chargeTime));
        }

        public override void Stop()
        {
            foreach (var s in m_shots) if (s.Glow != null) Destroy(s.Glow.gameObject);
            m_shots.Clear();
        }

        private void Update()
        {
            if (m_shots.Count == 0 || GimmickClock.IsNetClient) return;
            using var relay = GimmickNet.Relay();   // ネット対戦用(suehara)
            float dt = GimmickClock.IsHeld ? 0.0f : Time.deltaTime;
            for (int i = m_shots.Count - 1; i >= 0; i--)
            {
                var s = m_shots[i];
                s.Timer -= dt;
                // ポケットの光: ための間は膨らみながら点滅、撃っている間は明るく
                s.Charge = s.Charging && m_chargeTime > 0.0f ? 1.0f - Mathf.Clamp01(s.Timer / m_chargeTime) : 1.0f;
                UpdateGlow(s);

                if (s.Timer > 0.0f) continue;
                s.Charging = false;
                if (s.Left <= 0 || !Fire(s))
                {
                    Destroy(s.Glow.gameObject);
                    m_shots.RemoveAt(i);
                    continue;
                }
                s.Left--;
                s.Timer = m_interval;
            }
        }

        bool Fire(Shot s)
        {
            if (m_spawner != null && TumikiSpawner.CountBlocksOnBoard() >= m_spawner.MaxBlocksOnBoard + m_overLimit) return false;

            Vector3 toCenter = s.Center - s.Origin;
            toCenter.y = 0.0f;
            if (toCenter.sqrMagnitude < 0.01f) toCenter = Vector3.forward;
            // 左右に順番にばらまく(端から端へ)
            float k = m_count > 1 ? (float)s.Index / (m_count - 1) : 0.5f;
            float angle = Mathf.Lerp(-m_spread, m_spread, k) + Random.Range(-4.0f, 4.0f);
            Vector3 dir = Quaternion.Euler(0.0f, angle, 0.0f) * toCenter.normalized;
            s.Index++;

            // ポケットの縁(床の上)から撃ち出す
            Vector3 pos = s.Origin + dir * m_muzzleDistance + Vector3.up * (TumikiBlock.kHeight * 0.5f + 0.4f);
            TumikiBlock block = Instantiate(m_tumikiPrefab, pos, Quaternion.LookRotation(dir));
            block.SetColor(PickColor(s.Index));
            block.Launch(dir, Random.Range(m_speed.x, m_speed.y), null);
            var rb = block.Rigidbody;
            if (rb != null) rb.linearVelocity += Vector3.up * 3.0f;

            Quaternion aim = Quaternion.LookRotation(dir);
            GimmickBurst.Play(m_fireEffect, pos, aim);
            GimmickSound.Play(GimmickSound.Pop, m_fireVolume, Random.Range(0.9f, 1.15f));
            return true;
        }

        TumikiColor PickColor(int index)
        {
            if (m_colors != null && m_colors.Length > 0) return m_colors[index % m_colors.Length];
            return (TumikiColor)Random.Range(0, 4);
        }

        void UpdateGlow(Shot s)
        {
            float blink = GimmickPalette.Blink(Time.time, s.Charging ? 8.0f : 14.0f);
            s.Glow.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 2.0f, s.Charge);
            EffectMeshes.SetColor(s.Glow, GimmickPalette.WithAlpha(Color, Mathf.Lerp(0.4f, 0.9f, blink) * s.Charge));
        }

        //========================================
        // ネット対戦用(suehara): ポケットの光をホストから受け取る(撃ち出す積み木はネットワークに乗っているので別に届く)
        //========================================

        const int kMaxNetShots = 4;

        public void WriteNetState(GimmickNetState s)
        {
            int n = Mathf.Min(m_shots.Count, kMaxNetShots);
            s.AddInt(n);
            for (int i = 0; i < n; i++)
            {
                s.AddVec3(m_shots[i].Origin);
                s.Add(m_shots[i].Charge);
                s.AddBool(m_shots[i].Charging);
            }
        }

        public void ReadNetState(GimmickNetState s)
        {
            int n = Mathf.Clamp(s.Int(), 0, kMaxNetShots);
            while (m_shots.Count > n)
            {
                var last = m_shots[m_shots.Count - 1];
                if (last.Glow != null) Destroy(last.Glow.gameObject);
                m_shots.RemoveAt(m_shots.Count - 1);
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 origin = s.Vec3();
                float charge = s.Float();
                bool charging = s.Bool();
                if (!s.Ok) return;
                if (i >= m_shots.Count)
                {
                    var shot = new Shot { Glow = EffectMeshes.Create("FountainGlow", transform, EffectMeshes.Disc, true) };
                    m_shots.Add(shot);
                }
                var sh = m_shots[i];
                sh.Origin = origin;
                sh.Charge = charge;
                sh.Charging = charging;
                sh.Glow.transform.position = origin + Vector3.up * 0.05f;
                UpdateGlow(sh);
            }
        }
    }
}
