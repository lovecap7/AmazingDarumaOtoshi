using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 入れ替え: 台の上のプレイヤーの位置をぐるっと入れ替える
    //  ・予告: プレイヤーの足元に輪が出て点滅しながら縮み、光の粒が昇る
    //  ・入れ替え: 全員(生きているキャラクター)を、別の人がいた位置へ同時にワープさせる(自分の位置に戻る人はいない)
    //    1人しかいないときは、台の上のランダムな的玉と入れ替える
    public class BilliardShuffleGimmick : BilliardPocketGimmick, INetGimmickState
    {
        [SerializeField] float m_warningTime = 1.3f;
        [Tooltip("入れ替えた後、少しのあいだ動きを止める時間(ワープの余韻)")]
        [SerializeField] float m_freezeTime = 0.25f;
        [SerializeField] GimmickBurstSettings m_warpEffect = new GimmickBurstSettings
        {
            ColorA = new Color(0.55f, 0.75f, 1.0f, 1.0f), ColorB = Color.white,
            Count = 30, Speed = new Vector2(1.5f, 4.5f), Size = new Vector2(0.06f, 0.16f), Lifetime = new Vector2(0.3f, 0.6f),
            Gravity = -0.4f, Shape = GimmickBurstShape.Ring, ShapeSize = new Vector3(0.8f, 0, 0),
            Shockwave = true, ShockwaveColor = new Color(0.6f, 0.8f, 1.0f, 0.9f), ShockwaveRadius = 2.0f, ShockwaveTime = 0.3f,
        };
        [SerializeField] GimmickBurstSettings m_warningEffect = new GimmickBurstSettings
        {
            ColorA = new Color(0.55f, 0.75f, 1.0f, 1.0f), ColorB = Color.white,
            Count = 10, Speed = new Vector2(0.8f, 2.0f), Size = new Vector2(0.05f, 0.12f), Lifetime = new Vector2(0.4f, 0.8f),
            Gravity = -0.6f, Shape = GimmickBurstShape.Ring, ShapeSize = new Vector3(0.7f, 0, 0),
        };

        class Target
        {
            public DarumaCharacter Character;
            public MeshRenderer Ring, Column;
        }

        BilliardTable m_table;
        float m_time = -1.0f;
        float m_nextSparkle;
        readonly List<Target> m_targets = new List<Target>();
        readonly List<DarumaCharacter> m_frozen = new List<DarumaCharacter>();
        float m_freezeTimer;

        public override bool IsActive => m_time >= 0.0f || m_frozen.Count > 0;

        public override void Activate(BilliardTable table, Transform pocket, BilliardBall ball)
        {
            if (m_time >= 0.0f) return;   // 予告中にもう一度入っても、1回だけ入れ替える
            m_table = table;
            m_time = 0.0f;
            m_nextSparkle = 0.0f;
            BuildTargets();
            GimmickSound.Play(GimmickSound.Charge, 0.3f, GimmickSound.Charge.length / Mathf.Max(0.1f, m_warningTime));
        }

        public override void Stop()
        {
            m_time = -1.0f;
            ClearTargets();
            Unfreeze();
        }

        void BuildTargets()
        {
            ClearTargets();
            // ネット対戦用(suehara): クライアントのキャラクターは表示専用(DarumaCharacter は止めてある)なので、出ている人を全員選ぶ
            bool client = GimmickClock.IsNetClient;
            foreach (var c in FindObjectsByType<DarumaCharacter>())
            {
                if (c == null || (client ? !c.gameObject.activeInHierarchy : !c.CanBeAffected)) continue;
                var t = new Target { Character = c };
                t.Ring = EffectMeshes.Create("ShuffleRing", transform, EffectMeshes.Ring, true);
                t.Column = EffectMeshes.Create("ShuffleColumn", transform, EffectMeshes.Tube, true);
                m_targets.Add(t);
            }
        }

        void ClearTargets()
        {
            foreach (var t in m_targets)
            {
                if (t.Ring != null) Destroy(t.Ring.gameObject);
                if (t.Column != null) Destroy(t.Column.gameObject);
            }
            m_targets.Clear();
        }

        private void Update()
        {
            if (m_frozen.Count > 0)
            {
                m_freezeTimer -= Time.deltaTime;
                if (m_freezeTimer <= 0.0f) Unfreeze();
            }
            if (m_time < 0.0f) return;
            // ネット対戦用(suehara): ホストは光の粒・入れ替えの演出を送り、クライアントは自分では出さない(予告の輪だけ自分で出す)
            using var relay = GimmickNet.RelayOrMute();
            if (!GimmickClock.IsHeld) m_time += Time.deltaTime;
            float t = m_warningTime > 0.0f ? Mathf.Clamp01(m_time / m_warningTime) : 1.0f;

            // 予告: 足元の輪が縮みながら速く点滅し、光の柱が伸びる
            float blink = GimmickPalette.Blink(m_time, Mathf.Lerp(2.0f, 10.0f, t));
            bool sparkle = m_time >= m_nextSparkle;
            if (sparkle) m_nextSparkle = m_time + 0.25f;
            foreach (var target in m_targets)
            {
                if (target.Character == null) continue;
                Vector3 p = target.Character.transform.position;
                target.Ring.transform.position = p + Vector3.up * 0.05f;
                target.Ring.transform.localScale = Vector3.one * Mathf.Lerp(2.0f, 0.9f, t);
                EffectMeshes.SetColor(target.Ring, GimmickPalette.WithAlpha(Color, Mathf.Lerp(0.4f, 1.0f, blink)));
                target.Column.transform.position = p;
                target.Column.transform.localScale = new Vector3(0.8f, Mathf.Lerp(0.5f, 3.5f, t), 0.8f);
                EffectMeshes.SetColor(target.Column, GimmickPalette.WithAlpha(Color, 0.45f * t));
                if (sparkle) GimmickBurst.Play(m_warningEffect, p + Vector3.up * 0.1f);
            }

            if (t >= 1.0f)
            {
                // ネット対戦用(suehara): 入れ替えはホストだけ(クライアントは予告を片付けるだけ)
                if (GimmickClock.IsNetClient)
                {
                    m_time = -1.0f;
                    ClearTargets();
                }
                else Swap();
            }
        }

        //========================================
        // ネット対戦用(suehara): 予告の経過時間をホストから受け取る(輪・光の柱はクライアントの Update が出す)
        //========================================

        public void WriteNetState(GimmickNetState s)
        {
            s.AddBool(m_time >= 0.0f);
            s.Add(Mathf.Max(0.0f, m_time));
        }

        public void ReadNetState(GimmickNetState s)
        {
            bool active = s.Bool();
            float time = s.Float();
            if (!s.Ok) return;
            if (!active)
            {
                if (m_time >= 0.0f)
                {
                    m_time = -1.0f;
                    ClearTargets();
                }
                return;
            }
            if (m_time < 0.0f) BuildTargets();
            m_time = time;
        }

        void Swap()
        {
            m_time = -1.0f;
            var chars = new List<DarumaCharacter>();
            foreach (var t in m_targets)
            {
                if (t.Character != null && t.Character.CanBeAffected) chars.Add(t.Character);
            }
            ClearTargets();

            if (chars.Count >= 2)
            {
                // 並びを混ぜてから1つずつずらす(自分の位置に戻る人はいない)
                for (int i = chars.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (chars[i], chars[j]) = (chars[j], chars[i]);
                }
                var positions = new List<Vector3>();
                foreach (var c in chars) positions.Add(c.transform.position);
                for (int i = 0; i < chars.Count; i++)
                {
                    Vector3 from = positions[i];
                    Vector3 to = positions[(i + 1) % chars.Count];
                    Warp(chars[i], from, to);
                }
            }
            else if (chars.Count == 1 && m_table != null)
            {
                // 1人なら、ランダムな的玉と入れ替える
                var balls = new List<BilliardBall>();
                foreach (var b in m_table.ObjectBalls) if (b != null && b.InPlay) balls.Add(b);
                if (balls.Count > 0)
                {
                    var ball = balls[Random.Range(0, balls.Count)];
                    Vector3 cp = chars[0].transform.position;
                    Vector3 bp = new Vector3(ball.Position.x, cp.y, ball.Position.z);
                    Warp(chars[0], cp, bp);
                    ball.PlaceAt(new Vector3(cp.x, m_table.FloorY, cp.z), true);
                }
            }
            GimmickSound.Play(GimmickSound.Warp, 0.55f);
            BilliardFx.Shake(0.15f, 0.2f);
        }

        void Warp(DarumaCharacter c, Vector3 from, Vector3 to)
        {
            GimmickBurst.Play(m_warpEffect, from + Vector3.up * 0.1f);
            c.Teleport(to, 0.0f);
            GimmickBurst.Play(m_warpEffect, to + Vector3.up * 0.1f, 1.2f);
            if (m_freezeTime > 0.0f)
            {
                c.SetSuspended(true);
                m_frozen.Add(c);
                m_freezeTimer = m_freezeTime;
            }
        }

        void Unfreeze()
        {
            foreach (var c in m_frozen) if (c != null) c.SetSuspended(false);
            m_frozen.Clear();
        }
    }
}
