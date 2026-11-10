using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nakagawa
{
    // 台の外から出てきて手玉を突く、手とキュー(見た目だけ。当たり判定なし)
    // 動きの流れ(BilliardTable が Play で始める):
    //  1. 出てくる  : 台の外の上空から、手玉の後ろへ手(ブリッジ)とキューが降りてくる
    //  2. 狙う      : 狙う向きの左右に少し揺れながら向きが定まる。手玉から当てる点まで点線と、当たる位置の輪が出る
    //  3. 素振り    : キューを前後に数回小さく動かす
    //  4. 引く      : 大きく引いてため、キューの先が光る
    //  5. 突く      : 一気に突き出し、手玉に当たった瞬間に BilliardTable へ知らせる(火花・チョークの粉・衝撃波・音・揺れ)
    //  6. 下がる    : 少し残心してから、台の外へ戻って消える
    // 開始のカウントダウン中は止まる
    public class BilliardCue : MonoBehaviour, INetGimmickState
    {
        enum Phase { Idle, Enter, Aim, Practice, PullBack, Strike, Follow, Exit }

        [Header("見た目")]
        [Tooltip("出ていないときに隠す見た目のルート(手・キュー)")]
        [SerializeField] GameObject m_visualRoot;
        [Tooltip("キューの先(原点)から後ろ(-Z)へ伸びるキューと、後ろの手。突く動きで前後に動く")]
        [SerializeField] Transform m_cuePivot;
        [Tooltip("台の上に置く手(ブリッジ)。キューはこの上を滑る")]
        [SerializeField] Transform m_bridge;
        [Tooltip("キューの先の光")]
        [SerializeField] Renderer m_tipGlow;
        [SerializeField] Color m_glowColor = new Color(0.45f, 0.75f, 1.0f, 1.0f);

        [Header("位置")]
        [Tooltip("ブリッジの手を置く、手玉の縁からの距離")]
        [SerializeField] float m_bridgeDistance = 2.4f;
        [Tooltip("構えたときの、キューの先と手玉の縁の隙間")]
        [SerializeField] float m_restGap = 0.5f;
        [Tooltip("キューの後ろを持ち上げる角度")]
        [SerializeField] float m_elevation = 7.0f;
        [Tooltip("出てくる位置(手玉から後ろへの距離・高さ)")]
        [SerializeField] float m_enterDistance = 26.0f;
        [SerializeField] float m_enterHeight = 9.0f;

        [Header("時間")]
        [SerializeField] float m_enterTime = 0.9f;
        [SerializeField] float m_aimTime = 1.3f;
        [Tooltip("狙うときに左右へ揺れる角度")]
        [SerializeField] float m_aimWobble = 14.0f;
        [SerializeField, Min(0)] int m_practiceStrokes = 2;
        [SerializeField] float m_practiceStrokeTime = 0.5f;
        [SerializeField] float m_practiceStrokeLength = 1.1f;
        [SerializeField] float m_pullBackTime = 0.5f;
        [SerializeField] float m_pullBackLength = 2.6f;
        [SerializeField] float m_strikeTime = 0.06f;
        [SerializeField] float m_followTime = 0.45f;
        [SerializeField] float m_exitTime = 0.8f;

        [Header("狙いのガイド")]
        [SerializeField] Color m_guideColor = new Color(1.0f, 1.0f, 1.0f, 0.7f);
        [SerializeField, Min(1)] int m_guideDashes = 16;
        [SerializeField] float m_dashWidth = 0.16f;
        [SerializeField] float m_dashLength = 0.45f;

        [Header("エフェクト")]
        [SerializeField] GimmickBurstSettings m_strikeEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 1.0f, 0.9f, 1.0f), ColorB = new Color(0.6f, 0.85f, 1.0f, 1.0f),
            Count = 30, Speed = new Vector2(4.0f, 10.0f), Size = new Vector2(0.06f, 0.16f), Lifetime = new Vector2(0.12f, 0.3f),
            Shape = GimmickBurstShape.Cone, ShapeSize = new Vector3(0.2f, 0, 0), ConeAngle = 30.0f, Stretch = true,
        };
        [Tooltip("チョークの粉(青い粉が舞う)")]
        [SerializeField] GimmickBurstSettings m_chalkEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.35f, 0.6f, 0.95f, 0.85f), ColorB = new Color(0.75f, 0.88f, 1.0f, 0.7f),
            Count = 18, Speed = new Vector2(0.6f, 2.0f), Size = new Vector2(0.15f, 0.35f), Lifetime = new Vector2(0.4f, 0.8f),
            Gravity = -0.05f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.3f, 0, 0),
            Shockwave = true, ShockwaveColor = new Color(0.7f, 0.9f, 1.0f, 0.8f), ShockwaveRadius = 2.2f, ShockwaveTime = 0.3f,
        };
        [Tooltip("ため始め: キューの先に光の粒が集まる")]
        [SerializeField] GimmickBurstSettings m_chargeEffect = new GimmickBurstSettings
        {
            ColorA = new Color(0.5f, 0.8f, 1.0f, 1.0f), ColorB = Color.white,
            Count = 14, Speed = new Vector2(0.3f, 0.9f), Size = new Vector2(0.05f, 0.12f), Lifetime = new Vector2(0.35f, 0.6f),
            Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.4f, 0, 0),
        };
        [SerializeField, Range(0.0f, 1.0f)] float m_strikeVolume = 0.7f;
        [SerializeField, Range(0.0f, 1.0f)] float m_whooshVolume = 0.2f;

        Phase m_phase = Phase.Idle;
        float m_time;
        BilliardBall m_ball;
        Vector3 m_dir = Vector3.forward;
        float m_power;
        Vector3 m_aimPoint;
        Action<Vector3, float> m_onStrike;
        Vector3 m_anchor;          // 手玉の位置(床の高さ)。突くまでは手玉を追う
        float m_offset;            // キューの先と手玉の縁の隙間(突く動き)
        float m_enter;             // 0: 台の外 〜 1: 構えた位置
        float m_wobbleSeed;
        float m_exitFromOffset;

        Transform m_guideRoot;
        readonly List<MeshRenderer> m_dashes = new List<MeshRenderer>();
        MeshRenderer m_ghostRing;
        float m_guideAlpha;

        // ネット対戦用(suehara): クライアントへ送る見た目(左右の揺れ・先の光・手玉の半径)
        float m_netYaw, m_netGlow;
        float m_netRadius = 0.9f;
        float BallRadius => m_ball != null ? m_ball.Radius : m_netRadius;

        // 出ている間(出てくる〜下がりきるまで) true
        public bool IsBusy => m_phase != Phase.Idle;
        // 手玉を突いた(向き、速さ)
        public event Action<Vector3, float> Struck;

        private void Awake()
        {
            BuildGuide();
            Hide();
        }

        private void OnDisable()
        {
            Cancel(true);
        }

        // 突く動きを始める。dir: 突く向き、power: 手玉の速さ、aimPoint: 手玉が当たる位置(ガイドの輪)
        public void Play(BilliardBall ball, Vector3 dir, float power, Vector3 aimPoint, Action<Vector3, float> onStrike)
        {
            if (ball == null) return;
            dir.y = 0.0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            m_ball = ball;
            m_dir = dir.normalized;
            m_power = power;
            m_aimPoint = aimPoint;
            m_onStrike = onStrike;
            m_anchor = Flat(ball.Position, ball.Table != null ? ball.Table.FloorY : 0.0f);
            m_offset = m_restGap;
            m_enter = 0.0f;
            m_guideAlpha = 0.0f;
            m_wobbleSeed = UnityEngine.Random.value * 10.0f;
            SetPhase(Phase.Enter);
            if (m_visualRoot != null) m_visualRoot.SetActive(true);
            GimmickSound.Play(GimmickSound.Launch, m_whooshVolume, 0.55f);
            ApplyPose(0.0f);
        }

        // やめる。immediate: すぐに消す / false: 台の外へ下がってから消える
        public void Cancel(bool immediate)
        {
            if (m_phase == Phase.Idle) return;
            if (immediate)
            {
                Hide();
                return;
            }
            if (m_phase == Phase.Exit) return;
            m_onStrike = null;
            m_exitFromOffset = m_offset;
            SetPhase(Phase.Exit);
        }

        void Hide()
        {
            m_phase = Phase.Idle;
            m_ball = null;
            m_onStrike = null;
            if (m_visualRoot != null) m_visualRoot.SetActive(false);
            m_guideAlpha = 0.0f;
            UpdateGuide();
        }

        void SetPhase(Phase phase)
        {
            m_phase = phase;
            m_time = 0.0f;
        }

        private void Update()
        {
            if (m_phase == Phase.Idle) return;
            // ネット対戦用(suehara): クライアントはホストから受け取った見た目を出すだけ(ReadNetState)
            if (GimmickClock.IsNetClient) return;
            using var relay = GimmickNet.Relay();
            float dt = GimmickClock.IsHeld ? 0.0f : Time.deltaTime;
            m_time += dt;

            // 突くまでは手玉を追う(プレイヤーに押されて動いても構え直す)
            bool tracking = m_phase <= Phase.Strike;
            if (tracking && m_ball != null && m_ball.InPlay)
            {
                m_anchor = Flat(m_ball.Position, m_anchor.y);
            }
            else if (tracking && (m_ball == null || !m_ball.InPlay))
            {
                // 突く前に手玉が落ちた
                Cancel(false);
            }

            float yawOffset = 0.0f;
            float glow = 0.0f;
            switch (m_phase)
            {
                case Phase.Enter:
                {
                    float t = Ratio(m_enterTime);
                    m_enter = Ease(t);
                    yawOffset = m_aimWobble * (1.0f - m_enter);
                    if (t >= 1.0f) SetPhase(Phase.Aim);
                    break;
                }
                case Phase.Aim:
                {
                    float t = Ratio(m_aimTime);
                    m_enter = 1.0f;
                    // 左右に揺れながら狙いが定まる
                    yawOffset = m_aimWobble * (1.0f - t) * Mathf.Sin(t * Mathf.PI * 2.5f + m_wobbleSeed);
                    m_guideAlpha = Mathf.Clamp01(t * 2.0f);
                    if (t >= 1.0f) SetPhase(m_practiceStrokes > 0 ? Phase.Practice : Phase.PullBack);
                    break;
                }
                case Phase.Practice:
                {
                    float total = m_practiceStrokes * m_practiceStrokeTime;
                    float t = Ratio(total);
                    float s = Mathf.Sin(t * m_practiceStrokes * Mathf.PI);
                    m_offset = m_restGap + m_practiceStrokeLength * s * s;
                    m_guideAlpha = 1.0f;
                    if (t >= 1.0f)
                    {
                        SetPhase(Phase.PullBack);
                        GimmickBurst.Play(m_chargeEffect, TipPosition());
                        AudioClip clip = GimmickSound.Charge;
                        GimmickSound.Play(clip, 0.25f, clip.length / Mathf.Max(0.05f, m_pullBackTime));
                    }
                    break;
                }
                case Phase.PullBack:
                {
                    float t = Ratio(m_pullBackTime);
                    float e = 1.0f - (1.0f - t) * (1.0f - t);
                    m_offset = m_restGap + m_pullBackLength * e;
                    glow = t;
                    m_guideAlpha = 1.0f;
                    if (t >= 1.0f) SetPhase(Phase.Strike);
                    break;
                }
                case Phase.Strike:
                {
                    float t = Ratio(m_strikeTime);
                    m_offset = Mathf.Lerp(m_restGap + m_pullBackLength, -0.15f, t * t);
                    glow = 1.0f;
                    m_guideAlpha = 1.0f - t;
                    if (t >= 1.0f)
                    {
                        Hit();
                        SetPhase(Phase.Follow);
                    }
                    break;
                }
                case Phase.Follow:
                {
                    float t = Ratio(m_followTime);
                    m_offset = Mathf.Lerp(-0.15f, m_restGap, t);
                    glow = 1.0f - t;
                    m_guideAlpha = 0.0f;
                    if (t >= 1.0f)
                    {
                        m_exitFromOffset = m_offset;
                        SetPhase(Phase.Exit);
                    }
                    break;
                }
                case Phase.Exit:
                {
                    float t = Ratio(m_exitTime);
                    m_enter = 1.0f - Ease(t);
                    m_offset = Mathf.Lerp(m_exitFromOffset, m_exitFromOffset + 3.0f, t);
                    m_guideAlpha = Mathf.Max(0.0f, m_guideAlpha - dt * 4.0f);
                    if (t >= 1.0f)
                    {
                        Hide();
                        return;
                    }
                    break;
                }
            }

            m_netYaw = yawOffset;
            m_netGlow = glow;
            ApplyPose(yawOffset);
            UpdateGlow(glow);
            UpdateGuide();
        }

        // 手玉に当たった瞬間
        void Hit()
        {
            float r = m_ball != null ? m_ball.Radius : 0.9f;
            Vector3 contact = m_anchor + Vector3.up * r - m_dir * r;
            Quaternion fwd = Quaternion.LookRotation(m_dir);
            GimmickBurst.Play(m_strikeEffect, contact, fwd);
            GimmickBurst.Play(m_chalkEffect, contact, fwd);
            GimmickSound.Play(BilliardFx.Strike, m_strikeVolume);
            BilliardFx.Shake(0.2f, 0.2f);
            m_onStrike?.Invoke(m_dir, m_power);
            m_onStrike = null;
            Struck?.Invoke(m_dir, m_power);
        }

        void ApplyPose(float yawOffset)
        {
            float r = BallRadius;
            Vector3 dir = Quaternion.Euler(0.0f, yawOffset, 0.0f) * m_dir;
            Vector3 away = -dir * m_enterDistance + Vector3.up * m_enterHeight;
            transform.position = m_anchor + away * (1.0f - m_enter);
            transform.rotation = Quaternion.LookRotation(dir);
            if (m_bridge != null) m_bridge.localPosition = new Vector3(0.0f, 0.0f, -(r + m_bridgeDistance));
            if (m_cuePivot != null)
            {
                m_cuePivot.localPosition = new Vector3(0.0f, r, -(r + m_offset));
                m_cuePivot.localRotation = Quaternion.Euler(m_elevation, 0.0f, 0.0f);
            }
        }

        Vector3 TipPosition()
        {
            return m_cuePivot != null ? m_cuePivot.position : transform.position;
        }

        void UpdateGlow(float glow)
        {
            if (m_tipGlow == null) return;
            float blink = Mathf.Lerp(0.7f, 1.0f, GimmickPalette.Blink(Time.time, 10.0f));
            float a = Mathf.Clamp01(glow) * blink;
            m_tipGlow.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 0.75f, a);
            EffectMeshes.SetColor(m_tipGlow, GimmickPalette.WithAlpha(m_glowColor, a));
            m_tipGlow.enabled = a > 0.01f;
        }

        //========================================
        // 狙いのガイド(手玉から当てる点までの点線と、当たる位置の輪)
        //========================================

        void BuildGuide()
        {
            m_guideRoot = new GameObject("CueGuide").transform;
            m_guideRoot.SetParent(transform.parent != null ? transform.parent : transform, false);
            for (int i = 0; i < m_guideDashes; i++)
            {
                var dash = EffectMeshes.Create($"Dash_{i}", m_guideRoot, EffectMeshes.Disc, true);
                m_dashes.Add(dash);
            }
            m_ghostRing = EffectMeshes.Create("GhostRing", m_guideRoot, EffectMeshes.Ring, true);
        }

        void UpdateGuide()
        {
            if (m_guideRoot == null) return;
            bool show = m_guideAlpha > 0.01f && (m_ball != null || GimmickClock.IsNetClient) && m_phase != Phase.Idle;
            m_guideRoot.gameObject.SetActive(show);
            if (!show) return;

            float r = BallRadius;
            m_guideRoot.position = m_anchor + Vector3.up * 0.03f;
            m_guideRoot.rotation = Quaternion.LookRotation(m_dir);
            float length = Vector3.Dot(Flat(m_aimPoint, m_anchor.y) - m_anchor, m_dir);
            length = Mathf.Max(r * 2.0f, length);
            // 点線は手玉の縁から当たる位置の手前まで。手前から奥へ流れて見えるよう、時間でずらす
            float start = r + 0.2f, end = Mathf.Max(start + 0.1f, length - r);
            float step = (end - start) / m_dashes.Count;
            float flow = Mathf.Repeat(Time.time * 2.5f, 1.0f);
            for (int i = 0; i < m_dashes.Count; i++)
            {
                float z = start + step * (i + flow);
                var d = m_dashes[i];
                d.transform.localPosition = new Vector3(0.0f, 0.0f, z);
                d.transform.localScale = new Vector3(m_dashWidth, 1.0f, Mathf.Min(m_dashLength, step * 0.45f));
                float fade = 1.0f - (float)i / m_dashes.Count * 0.5f;
                EffectMeshes.SetColor(d, GimmickPalette.WithAlpha(m_guideColor, m_guideColor.a * m_guideAlpha * fade));
            }
            m_ghostRing.transform.localPosition = new Vector3(0.0f, 0.0f, length);
            float pulse = 1.0f + 0.08f * Mathf.Sin(Time.time * 12.0f);
            m_ghostRing.transform.localScale = Vector3.one * (r * pulse);
            EffectMeshes.SetColor(m_ghostRing, GimmickPalette.WithAlpha(m_guideColor, m_guideColor.a * m_guideAlpha));
        }

        float Ratio(float length) => length > 0.0f ? Mathf.Clamp01(m_time / length) : 1.0f;
        static float Ease(float t) => t * t * (3.0f - 2.0f * t);
        static Vector3 Flat(Vector3 v, float y) => new Vector3(v.x, y, v.z);

        //========================================
        // ネット対戦用(suehara): 構え・突く動き・狙いのガイドをホストから受け取る(GimmickNet)
        //========================================

        public void WriteNetState(GimmickNetState s)
        {
            s.AddBool(m_phase != Phase.Idle);
            s.AddVec3(m_anchor);
            s.AddVec3(m_dir);
            s.AddVec3(m_aimPoint);
            s.Add(m_enter);
            s.Add(m_offset);
            s.Add(m_netYaw);
            s.Add(m_netGlow);
            s.Add(m_guideAlpha);
            s.Add(BallRadius);
        }

        public void ReadNetState(GimmickNetState s)
        {
            bool busy = s.Bool();
            Vector3 anchor = s.Vec3();
            Vector3 dir = s.Vec3();
            Vector3 aim = s.Vec3();
            float enter = s.Float();
            float offset = s.Float();
            float yaw = s.Float();
            float glow = s.Float();
            float guide = s.Float();
            float radius = s.Float();
            if (!s.Ok) return;

            if (!busy)
            {
                if (m_phase != Phase.Idle) Hide();
                return;
            }
            if (m_phase == Phase.Idle && m_visualRoot != null) m_visualRoot.SetActive(true);
            m_phase = Phase.Aim;   // 出ている間ならどれでもよい(クライアントでは動きを進めない)
            m_anchor = anchor;
            dir.y = 0.0f;
            m_dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            m_aimPoint = aim;
            m_enter = enter;
            m_offset = offset;
            m_guideAlpha = guide;
            m_netRadius = radius;
            ApplyPose(yaw);
            UpdateGlow(glow);
            UpdateGuide();
        }
    }
}
