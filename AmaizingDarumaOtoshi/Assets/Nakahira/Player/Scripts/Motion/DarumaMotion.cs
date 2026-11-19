using UnityEngine;

namespace Nakahira
{
    // 生存キャラクターの体の動き(見た目だけ。当たり判定・移動・攻撃の届く範囲は変えない)
    // 積み木の段・頭・ハンマーに、高さに応じたずれを渡す(DarumaStack.Motion・DarumaHammer.BodyMotion)
    // 積み木は傾けない。だるま落としのように、上の段ほど大きく平行にずらして階段の形にする
    //  ・歩く   : 1 歩ごとにぴょこっと跳ね、よちよち左右にずれ、進む向きへ少し前にずれる
    //  ・ゆらゆら: 動き出し・止まる・曲がる・ハンマーの反動で上が置いていかれて揺れる(高いタワーほど上が揺れる)
    //  ・跳ぶ   : 跳んだ瞬間に上の段ほど浮いて段の間に隙間が開き、落ちるときに詰まる
    //  ・着地   : 上の段から順に 1 段ずつトトトッと跳ねる
    //  ・拾う   : 段の間に一瞬だけ隙間が開く
    // 積み木は硬いので、大きさは変えない(伸び縮みしない)
    // DarumaCharacter が Awake で自分に付ける。調整値は JumpParams.Motion(空なら動かない)
    // ネット対戦のクライアント(DarumaCharacter が止まっている)では動かさない
    [DisallowMultipleComponent]
    public class DarumaMotion : MonoBehaviour, IDarumaBodyMotion
    {
        // 1 フレームで進める時間の上限(重いフレームで揺れが暴れないように)
        const float kMaxStep = 1.0f / 30.0f;

        DarumaCharacter m_character;
        DarumaStack m_stack;
        DarumaHammer m_hammer;
        DarumaMotionParams m_params;
        bool m_active;

        // 歩き
        float m_stepPhase;     // 1 で 1 歩
        float m_walkBlend;     // 0 止まっている〜1 歩いている
        Vector3 m_lean;        // 前のめり(ワールドの水平。長さは高さ 1m あたりのずれ = tan(角度))
        Vector3 m_leanVelocity;

        // ゆらゆら(基準の高さでの、上のずれ。ワールドの水平)
        Vector3 m_sway;
        Vector3 m_swayVelocity;
        Vector3 m_lastHorizontal;
        bool m_hasLastHorizontal;
        float m_idleTime;

        // ジャンプ・着地・拾う
        float m_gap;           // 段と段の間の隙間(m。1 段ごと)
        bool m_wasGrounded = true;
        float m_airTime;
        float m_landTime = -1.0f; // 着地してからの時間(負なら着地の動きなし)
        bool m_gapByJump;      // 今の隙間がジャンプで開いたものか(落ち始めるまで開いたまま)

        // ハンマー
        bool m_wasSweeping;
        bool m_wasShooting;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            if (m_character != null)
            {
                m_stack = m_character.Stack;
                m_hammer = m_character.Hammer;
            }
            m_idleTime = Random.value * 10.0f;
        }

        private void OnEnable()
        {
            if (m_stack != null) m_stack.Motion = this;
            if (m_hammer != null) m_hammer.BodyMotion = this;
            DarumaCharacter.AnyPickedUp += OnPickedUp;
        }

        private void OnDisable()
        {
            if (m_stack != null && ReferenceEquals(m_stack.Motion, this)) m_stack.Motion = null;
            if (m_hammer != null && ReferenceEquals(m_hammer.BodyMotion, this)) m_hammer.BodyMotion = null;
            DarumaCharacter.AnyPickedUp -= OnPickedUp;
        }

        private void OnPickedUp(TumikiPickupInfo info)
        {
            if (info.Character != m_character || m_params == null) return;
            if (m_gap < m_params.PickupGap)
            {
                m_gap = m_params.PickupGap;
                m_gapByJump = false;
            }
        }

        private void Update()
        {
            m_params = m_character != null && m_character.JumpParams != null ? m_character.JumpParams.Motion : null;
            // ネット対戦のクライアントでは DarumaCharacter が止まっている(位置はホストから届く)
            bool active = m_params != null && m_character.enabled;
            if (!active)
            {
                if (m_active) ResetState();
                m_active = false;
                return;
            }
            m_active = true;

            // ヒットストップ中は体の動きもスローにする。ポーズ中(0)は止める
            float dt = Mathf.Min(Time.deltaTime, kMaxStep) * m_character.LocalTimeScale;
            if (dt <= 0.0f) return;

            UpdateWalk(dt);
            UpdateSway(dt);
            UpdateJump(dt);
            UpdateHammerKick();
        }

        private void ResetState()
        {
            m_walkBlend = 0.0f;
            m_lean = m_leanVelocity = Vector3.zero;
            m_sway = m_swayVelocity = Vector3.zero;
            m_hasLastHorizontal = false;
            m_gap = 0.0f;
            m_landTime = -1.0f;
        }

        // 本来の水平の速度(ヒットストップ・ギミックの速さの倍率を掛ける前)
        private Vector3 Horizontal => m_character.HorizontalVelocity;

        private void UpdateWalk(float dt)
        {
            // 自分で歩いている速さ(動く床・扇風機などで流されている分は入れない)
            Vector3 h = m_character.WalkVelocity;
            float speed = h.magnitude;
            bool walking = m_character.IsGrounded && speed > 0.3f;
            m_walkBlend = Mathf.MoveTowards(m_walkBlend, walking ? 1.0f : 0.0f, dt / m_params.WalkBlendTime);

            // 歩いた距離で 1 歩ずつ進める。止まったら、今の歩の終わり(足が着いた所)まで進めて止める
            if (walking) m_stepPhase += speed / m_params.StepLength * dt;
            else
            {
                float end = Mathf.Ceil(m_stepPhase);
                m_stepPhase = Mathf.MoveTowards(m_stepPhase, end, dt * 4.0f);
            }
            m_stepPhase %= 2.0f;

            // 前傾(進む向きへ。速さに比例)
            float maxSpeed = Mathf.Max(0.1f, m_character.MoveSpeed);
            Vector3 leanTarget = m_character.IsGrounded && speed > 0.01f
                ? h / speed * (Mathf.Tan(m_params.LeanAngle * Mathf.Deg2Rad) * Mathf.Clamp01(speed / maxSpeed))
                : Vector3.zero;
            m_lean = Vector3.SmoothDamp(m_lean, leanTarget, ref m_leanVelocity, m_params.LeanSmoothTime, Mathf.Infinity, dt);
        }

        private void UpdateSway(float dt)
        {
            // 体の加速度(上が置いていかれる向きに揺らす)
            Vector3 h = Horizontal;
            Vector3 accel = Vector3.zero;
            if (m_hasLastHorizontal) accel = (h - m_lastHorizontal) / dt;
            m_lastHorizontal = h;
            m_hasLastHorizontal = true;
            // ワープなどで速度が飛んだときに暴れないように抑える
            accel = Vector3.ClampMagnitude(accel, 80.0f);

            // ばね(減衰つき)。基準の高さでの上のずれ
            float omega = 2.0f * Mathf.PI * m_params.SwayFrequency;
            float zeta = m_params.SwayDamping;
            Vector3 force = -omega * omega * m_sway - 2.0f * zeta * omega * m_swayVelocity - accel * m_params.SwayInertia;
            m_swayVelocity += force * dt;
            m_sway += m_swayVelocity * dt;
            // 基準の高さで積み木の半径の 2 倍を超えるような揺れは止める(どのみち横のずれの上限で切られる)
            float limit = m_params.MaxHorizontalOffset * 2.0f;
            if (m_sway.magnitude > limit)
            {
                m_sway = m_sway.normalized * limit;
                m_swayVelocity = Vector3.ProjectOnPlane(m_swayVelocity, m_sway.normalized);
            }

            m_idleTime += dt;
        }

        private void UpdateJump(float dt)
        {
            bool grounded = m_character.IsGrounded;
            float vy = m_character.Rigidbody.linearVelocity.y / m_character.LocalTimeScale;

            // 跳んだ瞬間(地上から上向きに離れた)
            if (m_wasGrounded && !grounded && vy > 0.1f)
            {
                m_gapByJump = true;
            }
            // 着地
            if (!m_wasGrounded && grounded)
            {
                if (m_airTime >= m_params.LandMinAirTime) m_landTime = 0.0f;
                m_gapByJump = false;
            }
            if (grounded) m_airTime = 0.0f;
            else m_airTime += dt;
            m_wasGrounded = grounded;

            // 隙間:跳んでから上っている間は開き、落ち始めたら詰まる。着地したら残りもすぐ詰める
            if (m_gapByJump && !grounded && vy > 0.0f)
            {
                m_gap = Mathf.MoveTowards(m_gap, m_params.JumpGap, m_params.JumpGap / m_params.GapOpenTime * dt);
            }
            else
            {
                if (m_gapByJump && vy <= 0.0f) m_gapByJump = false;
                float closeTime = grounded ? m_params.GapCloseTime * 0.3f : m_params.GapCloseTime;
                float rate = Mathf.Max(m_params.JumpGap, m_params.PickupGap) / closeTime;
                m_gap = Mathf.MoveTowards(m_gap, 0.0f, rate * dt);
            }

            if (m_landTime >= 0.0f)
            {
                m_landTime += dt;
                int top = m_stack != null ? m_stack.Count : 0;
                float end = top * m_params.LandInterval + m_params.LandDuration;
                if (m_landTime > end) m_landTime = -1.0f;
            }
        }

        // ハンマーを振り始めた反動で上を揺らす
        private void UpdateHammerKick()
        {
            if (m_hammer == null) return;
            bool sweeping = m_hammer.IsSweeping;
            bool shooting = m_hammer.IsShooting;
            // 薙ぎ払いは右から左へ振るので、反動で体の上は右へ。ショットは後ろから前へ振り下ろすので後ろへ
            if (sweeping && !m_wasSweeping) m_swayVelocity += transform.right * m_params.SweepKick;
            if (shooting && !m_wasShooting) m_swayVelocity -= transform.forward * m_params.ShotKick;
            m_wasSweeping = sweeping;
            m_wasShooting = shooting;
        }

        //========================================
        // 見た目のずれ(IDarumaBodyMotion)
        //========================================

        // 段・頭・ハンマーは自分の高さでのずれだけを受け取り、傾かない。
        // 段ごとに高さが違うので、だるま落としのように 1 段ずつ平行にずれた階段の形になる
        public Vector3 GetOffset(float height)
        {
            if (!m_active || m_params == null) return Vector3.zero;
            Vector3 side = HorizontalAt(height);
            return new Vector3(side.x, VerticalAt(height), side.z);
        }

        // この高さでの横のずれ(体のローカル)
        private Vector3 HorizontalAt(float height)
        {
            float h = Mathf.Max(0.0f, height);

            // 前のめりとよちよち(高さに比例して上の段ほど大きくずれる)
            Vector3 lean = transform.InverseTransformDirection(m_lean);
            float waddle = Mathf.Tan(m_params.WaddleAngle * Mathf.Deg2Rad) * Mathf.Sin(m_stepPhase * Mathf.PI) * m_walkBlend;
            Vector3 shear = new Vector3(lean.x + waddle, 0.0f, lean.z);

            // ゆらゆら(高さのべき乗。低いタワーはほとんど揺れない)
            float k = Mathf.Pow(h / m_params.SwayReferenceHeight, m_params.SwayHeightExponent);
            Vector3 sway = transform.InverseTransformDirection(m_sway);
            sway.y = 0.0f;
            // 止まっているときのかすかな揺れ(左右にゆっくり)
            float idle = m_params.IdleSway * (1.0f - m_walkBlend) * Mathf.Sin(m_idleTime * 2.0f * Mathf.PI / m_params.IdleSwayPeriod);
            sway.x += idle;

            Vector3 raw = shear * h + sway * k;

            // 上限:どの段も積み木の半径(初期値)より横にずれない。上限に近づくほどゆるやかに頭打ちにする
            float max = m_params.MaxHorizontalOffset;
            float m = raw.magnitude;
            if (m > 0.0001f)
            {
                if (max <= 0.0f) return Vector3.zero;
                raw *= max * (float)System.Math.Tanh(m / max) / m;
            }
            return raw;
        }

        // この高さでの上下のずれ
        private float VerticalAt(float height)
        {
            // 何段目か(0 が一番下。頭は段の数)
            int index = Mathf.Max(0, Mathf.FloorToInt(height / TumikiBlock.kHeight));
            int top = m_stack != null ? m_stack.Count : 0;
            index = Mathf.Min(index, top);

            // 足踏み:タワーごと跳ね、跳ねている間は段の間がかすかに空く
            float hop = Mathf.Abs(Mathf.Sin(m_stepPhase * Mathf.PI)) * m_walkBlend;
            float y = m_params.HopHeight * hop + m_params.HopGap * hop * index;

            // ジャンプ・拾う:段の間の隙間(全部足して上限まで)
            float gap = m_gap;
            if (top > 0 && m_params.MaxJumpSpread > 0.0f) gap = Mathf.Min(gap, m_params.MaxJumpSpread / top);
            y += gap * index;

            // 着地:上の段から順に 1 段ずつ跳ねる
            if (m_landTime >= 0.0f)
            {
                float local = m_landTime - (top - index) * m_params.LandInterval;
                if (local >= 0.0f && local < m_params.LandDuration)
                {
                    y += m_params.LandBounce * Mathf.Sin(local / m_params.LandDuration * Mathf.PI);
                }
            }
            return y;
        }
    }
}
