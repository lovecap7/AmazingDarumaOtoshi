using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Nakagawa
{
    // ルーレット盤(Stage20)の進行役。一定間隔で「回転演出」を行う
    //  0. 回し始めに、盤の外の賭けの表でディーラーが色(赤・黒・緑の0)に賭ける(画面の右上の窓に映す。RouletteBet)
    //  1. ディーラーの手が台の外から現れ、軸の取っ手をひねって盤を回す
    //  2. 手が縁へ移り、球を外周のレール(盤と逆向き)へはじき入れる。手は台の外へ戻る
    //  3. 球はレールを高速で周回して減速し、「No more bets!」の後に内側へ落ちてポケットの上を数回跳ね、どれかのポケットに止まる
    //  4. 止まった瞬間にディーラーの賭けの判定(的中なら配当の積み木)、ポケットの色の出来事(雨など)、
    //     そのポケットを奈落にする(既に奈落なら隣)。盤はゆっくり止まり、球は台の外へ回収される
    // 球の動きは奈落でも通常のポケットでも同じ。回数を重ねるほど奈落が増え、次の回転までの間隔も短くなる
    // 開始のカウントダウン中は止まり、試合が終わったら新しく回さない。ネット対戦のクライアントでは何もしない
    public class RouletteGame : MonoBehaviour
    {
        enum Phase { Idle, HandIn, Twist, HandToRim, Flick, Orbit, Drop, Hop, Settled, Collect }

        [Header("部品")]
        [SerializeField] RouletteWheel m_wheel;
        [SerializeField] RouletteBall m_ball;
        [Tooltip("ディーラーの手(指先が +Z、手のひらが下を向く形)")]
        [SerializeField] Transform m_hand;
        [Tooltip("手に持った球の位置(手のローカル)")]
        [SerializeField] Vector3 m_handBallOffset = new Vector3(0.0f, -0.55f, 1.3f);
        [Tooltip("盤の外でディーラーが色に賭ける(画面の窓に映す)")]
        [SerializeField] RouletteBet m_bet;
        [Tooltip("ポケットの色(緑・赤・黒の順)ごとの出来事")]
        [SerializeField] RoulettePocketEvent[] m_events;
        [Tooltip("縁の電飾(角度の順に並べる)")]
        [SerializeField] Renderer[] m_rimLights;

        [Header("間隔")]
        [SerializeField] float m_firstDelay = 3.0f;
        [Tooltip("回し終わってから次に回すまでの時間(1回目 → 最短)")]
        [SerializeField] Vector2 m_interval = new Vector2(9.0f, 4.0f);
        [Tooltip("1回ごとに間隔を短くする時間")]
        [SerializeField] float m_intervalStep = 0.7f;

        [Header("盤")]
        [SerializeField] float m_spinSpeed = 30.0f;
        [SerializeField] float m_spinAccel = 30.0f;
        [SerializeField] float m_stopDecel = 9.0f;

        [Header("球")]
        [Tooltip("球が周回するレールの半径")]
        [SerializeField] float m_trackRadius = 13.9f;
        [Tooltip("周回の速さ(度/秒。負は盤と逆向き) 始め → 終わり")]
        [SerializeField] Vector2 m_orbitSpeed = new Vector2(-420.0f, -130.0f);
        [SerializeField] float m_orbitTime = 4.5f;
        [SerializeField] float m_dropTime = 1.0f;
        [SerializeField] Vector2Int m_hops = new Vector2Int(2, 4);
        [SerializeField] float m_hopTime = 0.42f;
        [SerializeField] float m_hopHeight = 1.1f;
        [Tooltip("止まってから回収するまで")]
        [SerializeField] float m_collectDelay = 2.8f;
        [SerializeField] float m_collectTime = 0.7f;

        [Header("手")]
        [SerializeField] float m_handInTime = 0.9f;
        [SerializeField] float m_twistTime = 0.9f;
        [SerializeField] float m_handToRimTime = 0.8f;
        [SerializeField] float m_flickTime = 0.25f;
        [SerializeField] float m_handOutTime = 0.9f;
        [Tooltip("手が出入りする方向(ワールドの角度。0 = 奥)の範囲")]
        [SerializeField] Vector2 m_handAngle = new Vector2(-60.0f, 60.0f);

        [Header("奈落")]
        [Tooltip("奈落にするまでの予告の時間")]
        [SerializeField] float m_abyssWarning = 1.8f;
        [Tooltip("奈落にできるポケットの数の上限(全部が穴にならないように)")]
        [SerializeField] int m_maxAbyss = 11;

        [Header("文字")]
        [SerializeField] string m_noMoreBets = "No more bets!";
        [SerializeField] string m_abyssLabel = "奈落!";
        [SerializeField] Color m_red = new Color(0.9f, 0.15f, 0.15f, 1.0f);
        [SerializeField] Color m_black = new Color(0.25f, 0.25f, 0.3f, 1.0f);
        [SerializeField] Color m_green = new Color(0.15f, 0.8f, 0.35f, 1.0f);

        [Header("エフェクト・音")]
        [SerializeField] GimmickBurstSettings m_flickEffect = new GimmickBurstSettings
        {
            ColorA = Color.white, ColorB = new Color(1.0f, 0.95f, 0.7f, 1.0f),
            Count = 16, Speed = new Vector2(3.0f, 7.0f), Size = new Vector2(0.05f, 0.12f), Lifetime = new Vector2(0.15f, 0.3f),
            Shape = GimmickBurstShape.Cone, ShapeSize = new Vector3(0.2f, 0, 0), ConeAngle = 25.0f, Stretch = true,
        };
        [SerializeField] GimmickBurstSettings m_hopEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 1.0f, 0.9f, 1.0f), ColorB = new Color(1.0f, 0.85f, 0.4f, 1.0f),
            Count = 10, Speed = new Vector2(2.0f, 4.5f), Size = new Vector2(0.04f, 0.1f), Lifetime = new Vector2(0.12f, 0.25f),
            Shape = GimmickBurstShape.Hemisphere, ShapeSize = new Vector3(0.1f, 0, 0), Stretch = true,
        };
        [SerializeField] GimmickBurstSettings m_resultEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = Color.white, ColorB = Color.white,
            Count = 40, Speed = new Vector2(3.0f, 8.0f), Size = new Vector2(0.1f, 0.22f), Lifetime = new Vector2(0.7f, 1.2f),
            Gravity = 0.5f, Shape = GimmickBurstShape.Hemisphere, ShapeSize = new Vector3(0.6f, 0, 0),
            Shockwave = true, ShockwaveColor = Color.white, ShockwaveRadius = 3.5f, ShockwaveTime = 0.45f,
        };
        [SerializeField] Color m_lightDim = new Color(1.0f, 0.75f, 0.35f, 0.25f);
        [SerializeField] Color m_lightBright = new Color(1.0f, 0.95f, 0.7f, 1.0f);
        [SerializeField, Range(0.0f, 1.0f)] float m_spinVolume = 0.25f;
        [SerializeField, Range(0.0f, 1.0f)] float m_rollVolume = 0.3f;

        Phase m_phase = Phase.Idle;
        float m_t;
        float m_idleTimer;
        int m_spins;
        bool m_needsReset;

        float m_handYaw;           // 手が出入りする方向(ワールドの角度)
        Vector3 m_handPos, m_handTargetPos;
        Quaternion m_handRot = Quaternion.identity, m_handTargetRot = Quaternion.identity;
        bool m_handVisible;
        float m_twistStartWheel;

        float m_ballAngle;         // 周回中の球の角度(ワールド)
        float m_ballSpeed;         // 周回中の角速度
        float m_ballRadius;        // 周回中の半径
        readonly List<float> m_hopAngles = new List<float>();   // 跳ねて着く角度(盤に固定した角度。連続した値)
        int m_hopIndex;
        float m_hopFrom;
        float m_settleAngle;       // 止まったポケットの角度(盤に固定)
        int m_result = -1;
        Vector3 m_collectFrom, m_collectTo;
        float m_resultFlash;

        AudioSource m_spinLoop, m_rollLoop;

        // 球がポケットに止まった(ポケットの位置)
        public event Action<int> BallSettled;
        // ポケットが奈落になることが決まった
        public event Action<int> AbyssChosen;

        public RouletteWheel Wheel => m_wheel;
        public RouletteBall Ball => m_ball;
        public int SpinCount => m_spins;
        public bool IsSpinning => m_phase != Phase.Idle;

        private void Awake()
        {
            m_spinLoop = GimmickSound.CreateLoop(transform, GimmickSound.Rumble);
            m_spinLoop.pitch = 0.6f;
            m_rollLoop = GimmickSound.CreateLoop(transform, GimmickSound.Rumble);
            m_rollLoop.pitch = 2.6f;
        }

        private void OnEnable()
        {
            m_needsReset = true;
        }

        private void OnDisable()
        {
            GimmickSound.SetLoopVolume(m_spinLoop, 0.0f);
            GimmickSound.SetLoopVolume(m_rollLoop, 0.0f);
            if (m_bet != null) m_bet.Clear();
            if (m_events != null) foreach (var e in m_events) if (e != null) e.Stop();
        }

        // 最初の状態に戻す(すべてのポケットを床に戻し、球と手を隠す)
        public void ResetGame()
        {
            m_needsReset = false;
            if (m_wheel != null) m_wheel.ResetBoard();
            if (m_bet != null) m_bet.Clear();
            if (m_events != null) foreach (var e in m_events) if (e != null) e.Stop();
            m_phase = Phase.Idle;
            m_idleTimer = m_firstDelay;
            m_spins = 0;
            m_result = -1;
            m_ball.SetVisible(false, false);
            m_handVisible = false;
            if (m_hand != null) m_hand.gameObject.SetActive(false);
        }

        //========================================
        // 進行(物理の1ステップごと)
        //========================================

        private void FixedUpdate()
        {
            if (m_needsReset) ResetGame();
            if (GimmickClock.IsNetClient || m_wheel == null || m_ball == null) return;
            if (GimmickClock.IsHeld) return;
            float dt = Time.fixedDeltaTime;
            m_t += dt;

            switch (m_phase)
            {
                case Phase.Idle: UpdateIdle(dt); break;
                case Phase.HandIn: UpdateHandIn(); break;
                case Phase.Twist: UpdateTwist(); break;
                case Phase.HandToRim: UpdateHandToRim(); break;
                case Phase.Flick: UpdateFlick(); break;
                case Phase.Orbit: UpdateOrbit(dt); break;
                case Phase.Drop: UpdateDrop(dt); break;
                case Phase.Hop: UpdateHop(); break;
                case Phase.Settled: UpdateSettled(); break;
                case Phase.Collect: UpdateCollect(); break;
            }
        }

        void SetPhase(Phase p)
        {
            m_phase = p;
            m_t = 0.0f;
        }

        void UpdateIdle(float dt)
        {
            var match = DarumaMatch.Current;
            if (match != null && match.IsFinished) return;
            m_idleTimer -= dt;
            if (m_idleTimer > 0.0f) return;

            // 回転演出を始める
            m_handYaw = Random.Range(m_handAngle.x, m_handAngle.y);
            m_handVisible = true;
            if (m_hand != null) m_hand.gameObject.SetActive(true);
            OffPose(out m_handPos, out m_handRot);
            m_handTargetPos = m_handPos;
            m_handTargetRot = m_handRot;
            if (m_hand != null) m_hand.SetPositionAndRotation(m_handPos, m_handRot);
            m_ball.SetVisible(true, false);
            m_ball.SetScale(1.0f);
            m_ball.Place(BallInHand(m_handPos, m_handRot));
            GimmickSound.Play(GimmickSound.Launch, 0.25f, 0.5f);
            // 盤の外でディーラーが色に賭ける
            if (m_bet != null) m_bet.PlaceRandom();
            SetPhase(Phase.HandIn);
        }

        void UpdateHandIn()
        {
            float t = Ratio(m_handInTime);
            OffPose(out var p0, out var r0);
            SpindlePose(0.0f, out var p1, out var r1);
            float e = Ease(t);
            SetHand(Vector3.Lerp(p0, p1, e) + Vector3.up * (2.0f * Mathf.Sin(e * Mathf.PI)), Quaternion.Slerp(r0, r1, e));
            HoldBall();
            if (t >= 1.0f)
            {
                m_twistStartWheel = m_wheel.Angle;
                m_wheel.SetTargetSpeed(m_spinSpeed, m_spinAccel);
                GimmickSound.Play(GimmickSound.Charge, 0.25f, 1.4f);
                SetPhase(Phase.Twist);
            }
        }

        void UpdateTwist()
        {
            float t = Ratio(m_twistTime);
            // 取っ手と一緒に手首をひねる
            SpindlePose(Mathf.DeltaAngle(m_twistStartWheel, m_wheel.Angle) + 25.0f * Mathf.Sin(t * Mathf.PI), out var p, out var r);
            SetHand(p + Vector3.down * (0.25f * Mathf.Sin(t * Mathf.PI)), r);
            HoldBall();
            if (t >= 1.0f) SetPhase(Phase.HandToRim);
        }

        void UpdateHandToRim()
        {
            float t = Ratio(m_handToRimTime);
            SpindlePose(Mathf.DeltaAngle(m_twistStartWheel, m_wheel.Angle), out var p0, out var r0);
            RimPose(0.0f, out var p1, out var r1);
            float e = Ease(t);
            SetHand(Vector3.Lerp(p0, p1, e) + Vector3.up * (1.5f * Mathf.Sin(e * Mathf.PI)), Quaternion.Slerp(r0, r1, e));
            HoldBall();
            if (t >= 1.0f) SetPhase(Phase.Flick);
        }

        void UpdateFlick()
        {
            float t = Ratio(m_flickTime);
            // 手首を返して球をはじく
            RimPose(-50.0f * t * t, out var p, out var r);
            SetHand(p, r);
            HoldBall();
            if (t < 1.0f) return;

            // レールに球を放つ
            m_ballAngle = m_handYaw;
            m_ballSpeed = m_orbitSpeed.x;
            m_ballRadius = m_trackRadius;
            Vector3 start = TrackPoint(m_ballAngle, m_ballRadius, 0.0f);
            m_ball.SetVisible(true, true);
            m_ball.Place(start);
            Vector3 tangent = RouletteMeshes.Point(1.0f, m_ballAngle + (m_ballSpeed < 0 ? -90.0f : 90.0f));
            GimmickBurst.Play(m_flickEffect, start, Quaternion.LookRotation(tangent));
            GimmickSound.Play(BilliardFx.Clack, 0.4f, 0.8f);
            GimmickSound.Play(GimmickSound.Launch, 0.35f, 1.2f);
            SetPhase(Phase.Orbit);
        }

        void UpdateOrbit(float dt)
        {
            float t = Ratio(m_orbitTime);
            UpdateHandOut();
            m_ballSpeed = Mathf.Lerp(m_orbitSpeed.x, m_orbitSpeed.y, t);
            m_ballAngle += m_ballSpeed * dt;
            // 速いうちは外側の縁に押し付けられ、遅くなると少し内側へ寄る(小さく揺れながら)
            m_ballRadius = m_trackRadius + Mathf.Lerp(0.35f, -0.2f, t) + 0.08f * Mathf.Sin(m_t * 23.0f);
            float bounce = Mathf.Abs(Mathf.Sin(m_t * 9.0f)) * 0.05f * (1.0f - t);
            m_ball.MoveTo(TrackPoint(m_ballAngle, m_ballRadius, bounce));
            if (t >= 1.0f)
            {
                BilliardFx.Popup(m_wheel.Center + Vector3.up * 4.5f, m_noMoreBets, Color.white, 1.4f, 1.8f);
                GimmickSound.Play(GimmickSound.Charge, 0.3f, 0.8f);
                SetPhase(Phase.Drop);
            }
        }

        void UpdateDrop(float dt)
        {
            float t = Ratio(m_dropTime);
            UpdateHandOut();
            m_ballSpeed = Mathf.Lerp(m_orbitSpeed.y, m_orbitSpeed.y * 0.45f, t);
            m_ballAngle += m_ballSpeed * dt;
            float r = Mathf.Lerp(m_trackRadius, m_wheel.PocketMidRadius, Ease(t));
            // 盤の縁を越えるところで小さく跳ねる
            float bump = 0.5f * Mathf.Sin(Mathf.Clamp01((t - 0.35f) / 0.4f) * Mathf.PI);
            m_ball.MoveTo(TrackPoint(m_ballAngle, r, Mathf.Max(0.0f, bump)));
            if (t < 1.0f) return;

            // ここから盤の上: 盤に対して球が進む向き(角度が減る向き)へ数回跳ねて、どれかのポケットに止まる
            float local = Mathf.Repeat(m_ballAngle - m_wheel.Angle, 360.0f);
            float w = m_wheel.PocketWidth;
            int p = m_wheel.PocketAtLocalAngle(local);
            float baseAngle = local + Mathf.DeltaAngle(local, p * w);
            int hops = Random.Range(m_hops.x, m_hops.y + 1);
            m_hopAngles.Clear();
            int cum = 0;
            for (int i = 0; i < hops; i++)
            {
                cum += i == hops - 1 ? Random.Range(0, 2) : Random.Range(1, 3);
                m_hopAngles.Add(baseAngle - cum * w);
            }
            m_hopIndex = 0;
            m_hopFrom = local;
            PlayHop(m_ball.Position, 1.0f);
            SetPhase(Phase.Hop);
        }

        void UpdateHop()
        {
            UpdateHandOut();
            float k = Mathf.Pow(0.75f, m_hopIndex);
            float duration = m_hopTime * k;
            float t = Ratio(duration);
            float to = m_hopAngles[m_hopIndex];
            float angle = Mathf.Lerp(m_hopFrom, to, t);
            float h = m_hopHeight * Mathf.Pow(0.6f, m_hopIndex) * 4.0f * t * (1.0f - t);
            m_ball.MoveTo(m_wheel.LocalPoint(m_wheel.PocketMidRadius, angle, m_ball.Radius + h));
            if (t < 1.0f) return;

            m_hopIndex++;
            m_hopFrom = to;
            if (m_hopIndex < m_hopAngles.Count)
            {
                PlayHop(m_ball.Position, k);
                m_t = 0.0f;
                return;
            }
            m_settleAngle = Mathf.Repeat(to, 360.0f);
            Settle(m_wheel.PocketAtLocalAngle(m_settleAngle));
        }

        void PlayHop(Vector3 pos, float strength)
        {
            GimmickBurst.Play(m_hopEffect, pos - Vector3.up * (m_ball.Radius * 0.8f), Mathf.Lerp(0.6f, 1.1f, strength));
            GimmickSound.Play(BilliardFx.Clack, 0.35f * strength + 0.1f, Random.Range(1.15f, 1.35f));
        }

        // 球がポケットに止まった
        void Settle(int pocket)
        {
            m_result = pocket;
            m_spins++;
            SetPhase(Phase.Settled);
            m_resultFlash = 1.0f;
            m_wheel.SetTargetSpeed(0.0f, m_stopDecel);

            var kind = m_wheel.Kind(pocket);
            Color color = KindColor(kind);
            Vector3 c = m_wheel.PocketWorldCenter(pocket, 0.1f);
            var fx = m_resultEffect.Clone();
            fx.ColorA = color;
            fx.ColorB = Color.Lerp(color, Color.white, 0.6f);
            fx.ShockwaveColor = GimmickPalette.WithAlpha(Color.Lerp(color, Color.white, 0.3f), 0.9f);
            GimmickBurst.Play(fx, c);
            GimmickSound.Play(BilliardFx.Clack, 0.5f, 0.9f);
            GimmickSound.Play(GimmickSound.Pop, 0.5f, 0.7f);
            string kindName = kind == RouletteWheel.PocketKind.Green ? "緑" : kind == RouletteWheel.PocketKind.Red ? "赤" : "黒";
            BilliardFx.Popup(c + Vector3.up * 2.5f, $"{m_wheel.Number(pocket)} {kindName}", Color.Lerp(color, Color.white, 0.2f), 1.6f, 2.2f);

            // ディーラーの賭けの判定(的中なら配当の積み木が降る)
            if (m_bet != null && m_bet.Resolve(kind, m_wheel)) BilliardFx.Popup(c, "ベット的中! 配当!", new Color(1.0f, 0.85f, 0.3f), 1.2f, 2.2f);

            // ポケットの色の出来事
            int index = (int)kind;
            if (m_events != null && index < m_events.Length && m_events[index] != null)
            {
                var ev = m_events[index];
                BilliardFx.Popup(m_wheel.Center + Vector3.up * 5.0f, ev.Label, Color.Lerp(ev.Color, Color.white, 0.2f), 1.3f, 2.0f);
                ev.Activate(this, pocket);
            }

            // 止まったポケットを奈落にする(既に奈落なら、一番近い床のポケット)
            int abyss = ChooseAbyss(pocket);
            if (abyss >= 0 && m_wheel.BeginAbyss(abyss, m_abyssWarning))
            {
                BilliardFx.Popup(m_wheel.PocketWorldCenter(abyss, 0.5f) + Vector3.up * 1.5f, m_abyssLabel, new Color(1.0f, 0.35f, 0.25f), 1.1f, 1.8f);
                AbyssChosen?.Invoke(abyss);
            }
            BallSettled?.Invoke(pocket);
        }

        int ChooseAbyss(int pocket)
        {
            int n = m_wheel.PocketCount;
            int count = m_wheel.AbyssCount;
            for (int i = 0; i < n; i++) if (m_wheel.IsWarning(i)) count++;
            if (count >= m_maxAbyss) return -1;
            for (int d = 0; d <= n / 2; d++)
            {
                // 近い順に、左右を交互に
                foreach (int s in d == 0 ? new[] { 0 } : (Random.value < 0.5f ? new[] { d, -d } : new[] { -d, d }))
                {
                    int i = ((pocket + s) % n + n) % n;
                    if (!m_wheel.IsAbyss(i) && !m_wheel.IsWarning(i)) return i;
                }
            }
            return -1;
        }

        void UpdateSettled()
        {
            // 球は止まったポケットに乗ったまま、盤と一緒に回る
            m_ball.MoveTo(m_wheel.LocalPoint(m_wheel.PocketMidRadius, m_settleAngle, m_ball.Radius));
            if (m_t < m_collectDelay) return;
            m_collectFrom = m_ball.Position;
            Vector3 dir = m_collectFrom - m_wheel.Center;
            dir.y = 0.0f;
            m_collectTo = m_wheel.Center + dir.normalized * 22.0f + Vector3.up * 7.0f;
            m_ball.SetVisible(true, false);
            GimmickSound.Play(GimmickSound.Pop, 0.35f, 1.3f);
            SetPhase(Phase.Collect);
        }

        void UpdateCollect()
        {
            float t = Ratio(m_collectTime);
            Vector3 p = Vector3.Lerp(m_collectFrom, m_collectTo, t) + Vector3.up * (4.0f * t * (1.0f - t) * 2.0f);
            m_ball.MoveTo(p);
            m_ball.SetScale(t < 0.7f ? 1.0f : Mathf.Lerp(1.0f, 0.0f, (t - 0.7f) / 0.3f));
            if (t < 1.0f) return;
            m_ball.SetVisible(false, false);
            m_ball.SetScale(1.0f);
            if (m_bet != null) m_bet.Clear();
            m_idleTimer = Mathf.Max(m_interval.y, m_interval.x - m_intervalStep * (m_spins - 1));
            SetPhase(Phase.Idle);
        }

        //========================================
        // 手
        //========================================

        // 台の外の上空
        void OffPose(out Vector3 pos, out Quaternion rot)
        {
            pos = m_wheel.Center + RouletteMeshes.Point(30.0f, m_handYaw, 11.0f);
            rot = Quaternion.LookRotation(-RouletteMeshes.Point(1.0f, m_handYaw)) * Quaternion.Euler(25.0f, 0.0f, 0.0f);
        }

        // 軸の取っ手の上(twist: 手首をひねった角度)
        void SpindlePose(float twist, out Vector3 pos, out Quaternion rot)
        {
            pos = m_wheel.Center + Vector3.up * 3.9f + RouletteMeshes.Point(1.1f, m_handYaw);
            rot = Quaternion.Euler(0.0f, m_handYaw + 180.0f + twist, 0.0f) * Quaternion.Euler(35.0f, 0.0f, 0.0f);
        }

        // 縁の上(球をはじく向き = 球が周回する向き。flick: 手首を返した角度)
        void RimPose(float flick, out Vector3 pos, out Quaternion rot)
        {
            float dirSign = m_orbitSpeed.x < 0.0f ? -1.0f : 1.0f;
            Vector3 tangent = RouletteMeshes.Point(1.0f, m_handYaw + 90.0f * dirSign);
            // 指先(球)がレールの真上に来るように、手首を少し後ろに置く
            pos = m_wheel.Center + RouletteMeshes.Point(m_trackRadius, m_handYaw, 1.5f) - tangent * 1.2f;
            rot = Quaternion.LookRotation(tangent) * Quaternion.Euler(20.0f, flick, 0.0f);
        }

        void UpdateHandOut()
        {
            if (!m_handVisible) return;
            float elapsed = m_phase == Phase.Orbit ? m_t : m_handOutTime;
            float t = Mathf.Clamp01(elapsed / m_handOutTime);
            RimPose(-50.0f, out var p0, out var r0);
            OffPose(out var p1, out var r1);
            float e = Ease(t);
            SetHand(Vector3.Lerp(p0, p1, e), Quaternion.Slerp(r0, r1, e));
            if (t >= 1.0f)
            {
                m_handVisible = false;
                if (m_hand != null) m_hand.gameObject.SetActive(false);
            }
        }

        void SetHand(Vector3 pos, Quaternion rot)
        {
            m_handTargetPos = pos;
            m_handTargetRot = rot;
        }

        Vector3 BallInHand(Vector3 pos, Quaternion rot) => pos + rot * m_handBallOffset;

        void HoldBall() => m_ball.MoveTo(BallInHand(m_handTargetPos, m_handTargetRot));

        //========================================
        // 見た目(毎フレーム)
        //========================================

        private void Update()
        {
            // 手は物理のステップで決めた位置へなめらかに寄せる
            if (m_hand != null && m_handVisible)
            {
                float k = 1.0f - Mathf.Exp(-30.0f * Time.deltaTime);
                m_handPos = Vector3.Lerp(m_handPos, m_handTargetPos, k);
                m_handRot = Quaternion.Slerp(m_handRot, m_handTargetRot, k);
                m_hand.SetPositionAndRotation(m_handPos, m_handRot);
            }

            UpdateLights();
            UpdateSounds();
        }

        // 縁の電飾: 周回中は球の位置を追って光り、止まった瞬間は当たりの色に点滅、ふだんはゆっくりまたたく
        void UpdateLights()
        {
            if (m_rimLights == null || m_rimLights.Length == 0) return;
            int n = m_rimLights.Length;
            bool chasing = m_phase == Phase.Orbit || m_phase == Phase.Drop || m_phase == Phase.Hop || m_phase == Phase.Flick;
            m_resultFlash = Mathf.Max(0.0f, m_resultFlash - Time.deltaTime * 0.6f);
            Color resultColor = m_result >= 0 ? KindColor(m_wheel.Kind(m_result)) : m_lightBright;
            float ballAngle = m_phase == Phase.Hop ? m_wheel.Angle + m_hopFrom : m_ballAngle;
            for (int i = 0; i < n; i++)
            {
                float a = i * 360.0f / n;
                float b;
                Color c;
                if (chasing)
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(a, ballAngle));
                    b = Mathf.Exp(-d * d / 400.0f);
                    c = Color.Lerp(m_lightDim, m_lightBright, b);
                }
                else if (m_resultFlash > 0.0f)
                {
                    float blink = GimmickPalette.Blink(Time.time + (i % 2) * 0.1f, 5.0f);
                    c = Color.Lerp(m_lightDim, GimmickPalette.WithAlpha(resultColor, 1.0f), m_resultFlash * blink);
                }
                else
                {
                    b = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.5f + i * 0.7f);
                    c = Color.Lerp(m_lightDim, m_lightBright, b * 0.35f);
                }
                EffectMeshes.SetColor(m_rimLights[i], c);
            }
        }

        void UpdateSounds()
        {
            float spin = Mathf.Clamp01(Mathf.Abs(m_wheel != null ? m_wheel.Speed : 0.0f) / Mathf.Max(1.0f, m_spinSpeed));
            GimmickSound.SetLoopVolume(m_spinLoop, m_spinVolume * spin);
            bool rolling = m_phase == Phase.Orbit || m_phase == Phase.Drop;
            float roll = rolling ? Mathf.Clamp01(Mathf.Abs(m_ballSpeed) / Mathf.Abs(m_orbitSpeed.x)) : 0.0f;
            if (m_rollLoop != null) m_rollLoop.pitch = Mathf.Lerp(1.6f, 2.8f, roll);
            GimmickSound.SetLoopVolume(m_rollLoop, m_rollVolume * roll);
        }

        //========================================
        // 共通
        //========================================

        Vector3 TrackPoint(float angle, float radius, float lift)
        {
            return m_wheel.Center + RouletteMeshes.Point(radius, angle, m_ball.Radius + lift);
        }

        public Color KindColor(RouletteWheel.PocketKind kind)
        {
            return kind == RouletteWheel.PocketKind.Green ? m_green : kind == RouletteWheel.PocketKind.Red ? m_red : m_black;
        }

        float Ratio(float length) => length > 0.0f ? Mathf.Clamp01(m_t / length) : 1.0f;
        static float Ease(float t) => t * t * (3.0f - 2.0f * t);
    }
}
