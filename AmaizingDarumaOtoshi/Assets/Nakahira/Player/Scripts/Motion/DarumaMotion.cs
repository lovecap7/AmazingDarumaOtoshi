using UnityEngine;

namespace Nakahira
{
    // 生存キャラクターの体の動き(見た目だけ。当たり判定・移動・攻撃の届く範囲は変えない)
    // 積み木の段・頭・ハンマーに、高さに応じたずれを渡す(DarumaStack.Motion・DarumaHammer.BodyMotion)
    // ふだんは積み木を傾けない。だるま落としのように、上の段ほど大きく平行にずらして階段の形にする
    //  ・歩く   : 1 歩ごとにぴょこっと跳ね、よちよち左右にずれ、進む向きへ少し前にずれる
    //  ・ゆらゆら: 動き出し・止まる・曲がる・ハンマーの反動で上が置いていかれて揺れる(高いタワーほど上が揺れる)
    //  ・跳ぶ・落ちる・着地: 段ごとに簡単な物理で計算する。体が急に押し上げられる・止まると、積み木はばねのように
    //    ぶつかり合って跳ね、ふわっと浮いてから落ちてきて、下の段から順にコツンと乗る。離れる・ぶつかるたびにランダムな向きに傾く
    //  ・拾う   : 段の間に一瞬だけ隙間が開く
    // 積み木は硬いので、大きさは変えない(伸び縮みしない)
    // DarumaCharacter が Awake で自分に付ける。調整値は JumpParams.Motion(空なら動かない)
    // ネット対戦のクライアント(DarumaCharacter が止まっている)では動かさない
    [DisallowMultipleComponent]
    public class DarumaMotion : MonoBehaviour, IDarumaBodyMotion
    {
        // 1 フレームで進める時間の上限(重いフレームで揺れが暴れないように)
        const float kMaxStep = 1.0f / 30.0f;
        // 積み木の物理を細かく分けて計算する 1 回の時間の上限(秒。硬いばねが暴れないように)
        const float kSubStep = 1.0f / 600.0f;
        // 1 ステップで体が受ける上下の力の上限(m/秒。ワープなどで暴れないように)
        const float kMaxImpulse = 60.0f;
        // 拾ったときの隙間が詰まるまでの時間(秒)
        const float kPickupGapCloseTime = 0.1f;

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

        // 拾う
        float m_pickupGap;     // 段と段の間の隙間(m。1 段ごと)

        // ジャンプ・着地(段ごとの簡単な物理。体から見た動きで計算する)
        // 段(レベル)は 0 が一番下の段(体に付いていて動かない)、段の数が頭
        float[] m_y = new float[0];            // 休んでいるときの位置からの上下のずれ(m。計算の値。ぶつかって少しめり込む)
        float[] m_vy = new float[0];           // 上下の速さ(m/秒)
        float[] m_force = new float[0];
        Vector2[] m_tilt = new Vector2[0];     // 傾き(度。x: 左右の軸まわり・y: 前後の軸まわり)
        Vector2[] m_tiltVel = new Vector2[0];
        Vector2[] m_torque = new Vector2[0];
        bool[] m_touching = new bool[0];       // 下の段と触れているか
        float[] m_shown = new float[0];        // 見た目の上下のずれ(めり込まないように直した値)
        int m_levels;                          // 今の段の数 + 1(頭)
        bool m_moving;
        // 体が受けた上下の力(FixedUpdate でためて、Update で積み木に渡す。速さの変化 + 重力 m/秒)
        float m_pendingImpulse;
        float m_lastVerticalVelocity;
        bool m_hasLastVertical;
        System.Random m_random;

        // ハンマー
        int m_lastSweepCount;
        bool m_wasShooting;

        // 調査用(DarumaMotionWatcher など):積み木が動いているか
        public string JumpPhaseName => m_moving ? "Moving" : "None";

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            if (m_character != null)
            {
                m_stack = m_character.Stack;
                m_hammer = m_character.Hammer;
            }
            m_idleTime = Random.value * 10.0f;
            m_random = new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
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
            if (m_pickupGap < m_params.PickupGap) m_pickupGap = m_params.PickupGap;
        }

        // 体が受けた上下の力をためる(キャラの速度は FixedUpdate で変わるので、同じ間隔で見る)
        // 地面に立っている間は重力の分だけ上へ押されていて、宙にいる間は 0、跳んだ・着地した瞬間に大きく押される
        private void FixedUpdate()
        {
            if (!m_active || m_character == null || m_character.JumpParams == null)
            {
                m_hasLastVertical = false;
                return;
            }
            float vy = m_character.VerticalVelocity;
            if (m_hasLastVertical)
            {
                var jump = m_character.JumpParams;
                float gravity = jump.Gravity * (vy < 0.0f ? jump.FallGravityMultiplier : 1.0f);
                float dt = Time.fixedDeltaTime * m_character.LocalTimeScale;
                float impulse = vy - m_lastVerticalVelocity + gravity * dt;
                m_pendingImpulse += Mathf.Clamp(impulse, -kMaxImpulse, kMaxImpulse);
            }
            m_lastVerticalVelocity = vy;
            m_hasLastVertical = true;
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
            UpdatePickup(dt);
            UpdateJump(dt);
            UpdateHammerKick();
        }

        private void ResetState()
        {
            m_walkBlend = 0.0f;
            m_lean = m_leanVelocity = Vector3.zero;
            m_sway = m_swayVelocity = Vector3.zero;
            m_hasLastHorizontal = false;
            m_pickupGap = 0.0f;
            System.Array.Clear(m_y, 0, m_y.Length);
            System.Array.Clear(m_vy, 0, m_vy.Length);
            System.Array.Clear(m_tilt, 0, m_tilt.Length);
            System.Array.Clear(m_tiltVel, 0, m_tiltVel.Length);
            System.Array.Clear(m_shown, 0, m_shown.Length);
            for (int i = 0; i < m_touching.Length; i++) m_touching[i] = true;
            m_moving = false;
            m_pendingImpulse = 0.0f;
            m_hasLastVertical = false;
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

        private void UpdatePickup(float dt)
        {
            float rate = Mathf.Max(0.0001f, m_params.PickupGap) / kPickupGapCloseTime;
            m_pickupGap = Mathf.MoveTowards(m_pickupGap, 0.0f, rate * dt);
        }

        //========================================
        // ジャンプ・落ちる・着地
        //========================================

        // 体から見た積み木の動きを計算する。体が上へ押されると、積み木は置いていかれて下の段へ押し付けられ、
        // ばねのように跳ね返って浮き上がる。宙では BlockPull で下へ引き戻され、下の段に落ちてコツンと乗る
        private void UpdateJump(float dt)
        {
            int count = m_stack != null ? m_stack.Count : 0;
            EnsureLevels(count + 1);

            // たまった体の上下の力を、体から見た積み木の速さの変化として渡す(一番下の段は体に付いている)
            float kick = -m_pendingImpulse * m_params.BodyInertia;
            m_pendingImpulse = 0.0f;
            for (int i = 1; i < m_levels; i++) m_vy[i] += kick;

            float w = 2.0f * Mathf.PI * m_params.ContactFrequency;
            float k = w * w;
            float c = 2.0f * m_params.ContactDamping * w;
            float tw = 2.0f * Mathf.PI * m_params.TiltFrequency;
            float tk = tw * tw;
            float tc = 2.0f * m_params.TiltDamping * tw;

            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / kSubStep));
            float h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                for (int i = 1; i < m_levels; i++)
                {
                    m_force[i] = -m_params.BlockPull;
                    m_torque[i] = Vector2.zero;
                }
                for (int i = 1; i < m_levels; i++)
                {
                    // 下の段との隙間(負ならめり込んでいる)。傾いた積み木は角が出るので、そのぶん離れる
                    float gap = m_y[i] - m_y[i - 1] - TiltClearance(i - 1, i);
                    float rel = m_vy[i] - m_vy[i - 1];
                    bool touching = gap < 0.0f;
                    if (touching)
                    {
                        // 押すだけで引っぱらない
                        float f = Mathf.Max(0.0f, -k * gap - c * rel);
                        m_force[i] += f;
                        if (i > 1) m_force[i - 1] -= f;
                        // 乗っている間は、下の段と同じ傾きに戻る
                        m_torque[i] += -tk * (m_tilt[i] - m_tilt[i - 1]) - tc * (m_tiltVel[i] - m_tiltVel[i - 1]);
                    }
                    // 離れた・ぶつかった瞬間に、その勢いに応じてランダムな向きに傾く
                    if (touching != m_touching[i])
                    {
                        m_touching[i] = touching;
                        float speed = Mathf.Abs(rel);
                        if (speed > m_params.TiltKickMinSpeed) KickTilt(i, speed);
                    }
                }
                float maxTilt = m_params.MaxTilt;
                for (int i = 1; i < m_levels; i++)
                {
                    m_vy[i] += m_force[i] * h;
                    m_y[i] += m_vy[i] * h;
                    m_tiltVel[i] += m_torque[i] * h;
                    m_tilt[i] += m_tiltVel[i] * h;
                    float t = m_tilt[i].magnitude;
                    if (t > maxTilt)
                    {
                        Vector2 dir = m_tilt[i] / t;
                        m_tilt[i] = dir * maxTilt;
                        float outward = Vector2.Dot(m_tiltVel[i], dir);
                        if (outward > 0.0f) m_tiltVel[i] -= dir * outward;
                    }
                    // 下の段からこれ以上は離れない
                    float limit = m_y[i - 1] + TiltClearance(i - 1, i) + m_params.MaxGap;
                    if (m_y[i] > limit)
                    {
                        m_y[i] = limit;
                        if (m_vy[i] > m_vy[i - 1]) m_vy[i] = m_vy[i - 1];
                    }
                }
            }

            // 見た目:めり込んでいる分は見せない(下の段の上に乗せる)
            m_moving = false;
            m_shown[0] = 0.0f;
            for (int i = 1; i < m_levels; i++)
            {
                m_shown[i] = Mathf.Max(m_y[i], m_shown[i - 1] + TiltClearance(i - 1, i));
                if (m_shown[i] > 0.001f || m_tilt[i].sqrMagnitude > 0.01f) m_moving = true;
            }
        }

        private void KickTilt(int level, float speed)
        {
            double a = m_random.NextDouble() * 2.0 * System.Math.PI;
            Vector2 dir = new Vector2((float)System.Math.Cos(a), (float)System.Math.Sin(a));
            m_tiltVel[level] += dir * (m_params.TiltKick * speed);
        }

        // 傾いた積み木の角が出るぶん、下の段と上の段の間を空ける量(m)。頭は球なので出ない
        private float TiltClearance(int lower, int upper) => Corner(lower) + Corner(upper);

        private float Corner(int level)
        {
            if (level <= 0 || level >= m_levels - 1) return 0.0f;
            float rad = m_tilt[level].magnitude * Mathf.Deg2Rad;
            float half = TumikiBlock.kHeight * 0.5f;
            return half * Mathf.Cos(rad) + TumikiBlock.kRadius * Mathf.Sin(rad) - half;
        }

        // 段の数が増えたら配列をのばす(増えた段は 1 つ下の段と同じ動きにする)
        private void EnsureLevels(int levels)
        {
            m_levels = levels;
            if (m_y.Length >= levels) return;
            int old = m_y.Length;
            System.Array.Resize(ref m_y, levels);
            System.Array.Resize(ref m_vy, levels);
            System.Array.Resize(ref m_force, levels);
            System.Array.Resize(ref m_tilt, levels);
            System.Array.Resize(ref m_tiltVel, levels);
            System.Array.Resize(ref m_torque, levels);
            System.Array.Resize(ref m_touching, levels);
            System.Array.Resize(ref m_shown, levels);
            for (int i = old; i < levels; i++)
            {
                m_touching[i] = true;
                if (i == 0) continue;
                m_y[i] = m_y[i - 1];
                m_vy[i] = m_vy[i - 1];
                m_tilt[i] = m_tilt[i - 1];
            }
        }

        // ハンマーを振り始めた反動で上を揺らす
        private void UpdateHammerKick()
        {
            if (m_hammer == null) return;
            int sweeps = m_hammer.SweepCount;
            bool shooting = m_hammer.IsShooting;
            // 薙ぎ払いは右から左へ振るので、反動で体の上は右へ(切り返しは左へ)。ショットは後ろから前へ振り下ろすので後ろへ
            if (sweeps != m_lastSweepCount)
            {
                float side = m_hammer.IsReverseSweep ? -1.0f : 1.0f;
                m_swayVelocity += transform.right * (m_params.SweepKick * side);
            }
            if (shooting && !m_wasShooting) m_swayVelocity -= transform.forward * m_params.ShotKick;
            m_lastSweepCount = sweeps;
            m_wasShooting = shooting;
        }

        //========================================
        // 見た目のずれ(IDarumaBodyMotion)
        //========================================

        // 何段目か(0 が一番下。頭は段の数)
        private int LevelAt(float height)
        {
            int index = Mathf.Max(0, Mathf.FloorToInt(height / TumikiBlock.kHeight));
            return Mathf.Min(index, m_levels - 1);
        }

        // 段・頭・ハンマーは自分の高さでのずれを受け取る。
        // 段ごとに高さが違うので、だるま落としのように 1 段ずつ平行にずれた階段の形になる
        public Vector3 GetOffset(float height)
        {
            if (!m_active || m_params == null || m_levels <= 0) return Vector3.zero;
            int level = LevelAt(height);
            Vector3 side = HorizontalAt(height);
            return new Vector3(side.x, VerticalAt(level) + m_shown[level], side.z);
        }

        // ジャンプ・着地での傾き(段・頭だけ)
        public Quaternion GetTilt(float height)
        {
            if (!m_active || m_params == null || m_levels <= 0) return Quaternion.identity;
            Vector2 t = m_tilt[LevelAt(height)];
            if (t.x == 0.0f && t.y == 0.0f) return Quaternion.identity;
            return Quaternion.AngleAxis(t.x, Vector3.right) * Quaternion.AngleAxis(t.y, Vector3.forward);
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

        // 上下のずれ(歩く・拾う)
        private float VerticalAt(int level)
        {
            // 足踏み:タワーごと跳ね、跳ねている間は段の間がかすかに空く
            float hop = Mathf.Abs(Mathf.Sin(m_stepPhase * Mathf.PI)) * m_walkBlend;
            float y = m_params.HopHeight * hop + m_params.HopGap * hop * level;

            // 拾う:段の間の隙間
            y += m_pickupGap * level;
            return y;
        }
    }
}
