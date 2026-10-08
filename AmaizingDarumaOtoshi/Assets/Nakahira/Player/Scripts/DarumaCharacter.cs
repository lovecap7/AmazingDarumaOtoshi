using System;
using UnityEngine;

namespace Nakahira
{
    // ギミック(風・バンパー・回転台・重力エリア等)から影響を受けるもの
    // ギミックは影響を毎ステップ要求し直す。要求は溜めておき、受ける側の次のFixedUpdateでまとめて反映する
    // (新しいギミックはこれらの組み合わせで作れるので、キャラクター・積み木側を変更する必要はない)
    public interface IGimmickAffectable
    {
        Rigidbody Rigidbody { get; }
        // 影響を受けられる状態か(積まれている積み木・脱落済みなどはfalse)
        bool CanBeAffected { get; }
        // 足場ごと運ぶ(回転台など)。今の移動方向・速さはそのままに、このステップだけ足場の動きを加算する
        // 溜め込まれず、要求をやめると足場の動きの分だけ止まる
        void AddCarryVelocity(Vector3 velocity);
        // 足場ごと回す(回転台など)。このステップで足場が水平に回った角度(度。上から見て時計回りが正)
        // 向きをどう扱うかは受ける側が決める(積み木は向きと進む方向が回る。プレイヤーは入力で向きを決めるので回らない)
        void AddCarryRotation(float yawDegrees);
        // 力を加える(加速度 m/s²。風など)。今の速度に加算され続け、慣性が残る
        void AddForce(Vector3 acceleration);
        // はじく(瞬間的に速度を与える)
        void AddKnockback(Vector3 velocity);
        // このステップの重力の倍率。複数のギミックから要求されたら一番小さい値を使う
        void RequestGravityScale(float scale);
    }

    // 生存キャラクター(だるま)。移動・ジャンプ・積み木の取得・被弾・脱落を担当する
    // 操作は IDarumaCommandSource(同じGameObjectに付いていれば自動取得、SetCommandSourceで差し替え可)
    // または ApplyCommand で渡す
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(HitStop))]
    public class DarumaCharacter : MonoBehaviour, IDarumaAttacker, IGimmickAffectable
    {
        [Header("移動")]
        [SerializeField] float m_baseMoveSpeed = 6.0f;
        // 1段積むごとに遅くなる量
        [SerializeField] float m_speedLossPerBlock = 0.3f;
        [SerializeField] float m_minMoveSpeed = 2.5f;
        [SerializeField] float m_turnSpeed = 15.0f;

        [Header("ジャンプ")]
        // 高さ・滞空・落下(ScriptableObjectから毎回読む)。重力もここから計算したキャラクター専用のものを使う
        [SerializeField] JumpParams m_jumpParams;
        [SerializeField] LayerMask m_groundMask = ~0;

        [Header("脱落")]
        // これより下に落ちたら奈落に落ちたとみなして脱落
        [SerializeField] float m_killY = -5.0f;
        // 無敵中の点滅間隔
        [SerializeField] float m_blinkInterval = 0.1f;

        [Header("ギミック")]
        // はじかれた・力で押された速度が減衰する速さ(大きいほどすぐ止まる。一定の力を受け続けると 力/この値 の速さで流される)
        [SerializeField] float m_knockbackDamping = 5.0f;

        [Header("参照")]
        [SerializeField] DarumaStack m_stack;
        [SerializeField] DarumaHammer m_hammer;

        // 胴体の半径
        public const float kBodyRadius = 0.45f;

        Rigidbody m_rb;
        CapsuleCollider m_body;
        HitStop m_hitStop;
        IDarumaCommandSource m_commandSource;
        // 前のステップでRigidbodyの速度に掛けた時間の倍率(本来の速度を復元するのに使う)
        float m_appliedTimeScale = 1.0f;

        Vector3 m_moveDirection; // ワールド空間の移動方向(XZ)
        bool m_jumpRequested;
        bool m_isGrounded;
        // 物理計算の直前に空中で落下中だったか(衝突の通知時点では落下速度が打ち消されているため記録しておく)
        bool m_isFallingInAir;
        bool m_isEliminated;
        float m_invincibleTimer;
        readonly RaycastHit[] m_groundHits = new RaycastHit[8];

        // ギミックからの影響(IGimmickAffectable)
        Vector3 m_carryVelocity;      // 次のステップで上乗せする運搬速度
        Vector3 m_appliedExternal;    // 前のステップで上乗せした速度(本来の速度を復元するのに使う)
        Vector3 m_knockback;          // はじかれた・力で押された水平方向の速度(減衰する)
        Vector3 m_force;              // 次のステップで加える力(加速度)
        float m_knockbackUp;          // はじかれた上向きの速度(次のステップで1回だけ反映)
        float m_gravityScale = 1.0f;  // 次のステップの重力の倍率

        public DarumaStack Stack => m_stack;
        public Collider Body => m_body;
        public Rigidbody Rigidbody => m_rb;
        public bool IsGrounded => m_isGrounded;
        // 股抜きショットを始められるか。地上にいて、先に押されたジャンプが実行待ちでないこと
        public bool CanStartShot => m_isGrounded && !m_jumpRequested;
        public bool IsInvincible => m_invincibleTimer > 0.0f;
        public float MoveSpeed => Mathf.Max(m_minMoveSpeed, m_baseMoveSpeed - m_stack.Count * m_speedLossPerBlock);
        // ヒットストップ中は1未満(移動・落下・ハンマーがスローになる)
        public float LocalTimeScale => Mathf.Max(0.01f, m_hitStop.TimeScale);

        // 脱落した(この直後にGameObjectは破棄される)
        public event Action<DarumaCharacter> Eliminated;
        // 他の生存者にダメージを与えた
        public event Action<DarumaCharacter> DealtDamage;

        private void Awake()
        {
            m_rb = GetComponent<Rigidbody>();
            // 重力はJumpParamsから計算して自前で掛ける
            m_rb.useGravity = false;
            m_body = GetComponent<CapsuleCollider>();
            m_hitStop = GetComponent<HitStop>();
            m_commandSource = GetComponent<IDarumaCommandSource>();
            m_stack.Changed += UpdateBodyCollider;
            UpdateBodyCollider();
        }

        private void Update()
        {
            if (m_commandSource != null)
            {
                ApplyCommand(m_commandSource.ReadCommand());
            }
            UpdateInvincible();
        }

        // 操作命令の供給元を差し替える(人間⇔AIの切り替え、回線切断時のAI引き継ぎ等)。nullで操作なし
        public void SetCommandSource(IDarumaCommandSource source)
        {
            m_commandSource = source;
            // 前の供給元の入力が残らないようにする
            m_moveDirection = Vector3.zero;
            m_jumpRequested = false;
        }

        // 操作命令を受け取る(将来ネットワークから受け取る場合もここを使う)
        public void ApplyCommand(in DarumaCommand command)
        {
            Vector2 move = Vector2.ClampMagnitude(command.Move, 1.0f);
            m_moveDirection = new Vector3(move.x, 0.0f, move.y);
            if (command.Jump) m_jumpRequested = true;
            if (command.Sweep) m_hammer.TrySweep();
            if (command.Shot && m_hammer.TryShot()) FaceMoveDirection();
        }

        // 振り向きの途中でも、スティックの方向へ即座に向きを合わせる(ニュートラルなら今の向きのまま)
        private void FaceMoveDirection()
        {
            if (m_moveDirection.sqrMagnitude <= 0.0001f) return;
            Quaternion target = Quaternion.LookRotation(m_moveDirection, Vector3.up);
            m_rb.rotation = target;
            transform.rotation = target;
        }

        private void FixedUpdate()
        {
            // 奈落に落ちた
            if (m_rb.position.y < m_killY)
            {
                Eliminate();
                return;
            }

            m_isGrounded = CheckGrounded();

            // 本来の速度で計算し、最後に時間の倍率を掛けてRigidbodyに渡す(ヒットストップ中はスローになる)
            float timeScale = LocalTimeScale;
            float dt = Time.fixedDeltaTime * timeScale;
            Vector3 v = m_rb.linearVelocity / m_appliedTimeScale - m_appliedExternal;

            UpdateMove(ref v, dt);
            UpdateGravity(ref v, dt);
            UpdateJump(ref v);
            Vector3 external = UpdateExternal(ref v, dt);

            m_rb.linearVelocity = (v + external) * timeScale;
            m_appliedTimeScale = timeScale;

            // 地上でも自前の重力で速度は負になるので、空中にいることも条件にする
            m_isFallingInAir = !m_isGrounded && v.y < 0.0f;
        }

        // 上昇中は通常の重力、落下中は倍率を掛けた重力
        private void UpdateGravity(ref Vector3 v, float dt)
        {
            float gravity = m_jumpParams.Gravity * m_gravityScale;
            if (v.y < 0.0f) gravity *= m_jumpParams.FallGravityMultiplier;
            v.y -= gravity * dt;
            m_gravityScale = 1.0f;
        }

        // ギミックからの影響を反映し、このステップだけ上乗せする速度を返す
        // (移動入力で毎ステップ上書きされる水平速度とは別に持ち、次のステップの最初に取り除く)
        private Vector3 UpdateExternal(ref Vector3 v, float dt)
        {
            if (m_knockbackUp > 0.0f)
            {
                v.y = Mathf.Max(v.y, m_knockbackUp);
                m_isGrounded = false;
                m_knockbackUp = 0.0f;
            }

            // 力は水平方向を慣性として溜め(移動入力とは別に持つ)、上下方向は今の速度に加える
            m_knockback += new Vector3(m_force.x, 0.0f, m_force.z) * dt;
            v.y += m_force.y * dt;
            m_force = Vector3.zero;

            Vector3 external = m_carryVelocity + m_knockback;
            m_carryVelocity = Vector3.zero;
            m_knockback *= Mathf.Exp(-m_knockbackDamping * dt);
            if (m_knockback.sqrMagnitude < 0.01f) m_knockback = Vector3.zero;

            m_appliedExternal = external;
            return external;
        }

        private void UpdateMove(ref Vector3 v, float dt)
        {
            Vector3 moveDir = m_moveDirection;
            Vector3 horizontal = moveDir * MoveSpeed;
            v.x = horizontal.x;
            v.z = horizontal.z;

            // ハンマーを振っている間は向きを固定(打ち返しの方向がブレないように)
            // ただし股抜きショットは弾が出るまで向きを変えて狙える
            bool canTurn = !m_hammer.IsBusy || m_hammer.IsAimingShot;
            if (moveDir.sqrMagnitude > 0.0001f && !m_hammer.IsBusy)
            {
                Quaternion target = Quaternion.LookRotation(moveDir, Vector3.up);
                m_rb.MoveRotation(Quaternion.Slerp(m_rb.rotation, target, m_turnSpeed * dt));
            }
        }

        private void UpdateJump(ref Vector3 v)
        {
            if (!m_jumpRequested) return;
            // ヒットストップ中に押されたジャンプは、明けてから反映する
            if (m_hitStop.IsActive) return;
            m_jumpRequested = false;
            if (!m_isGrounded) return;
            // 股抜きショットを振っている間はジャンプできない(空中で弾が出ないように)
            if (m_hammer.IsShooting) return;

            v.y = m_jumpParams.JumpSpeed;
            // 次の接地判定を待たずに空中扱いにする(直後のショットを防ぐ)
            m_isGrounded = false;
        }

        private bool CheckGrounded()
        {
            if (m_rb.linearVelocity.y > 0.1f) return false;

            Vector3 origin = m_rb.position + Vector3.up * (kBodyRadius + 0.05f);
            int count = Physics.SphereCastNonAlloc(origin, kBodyRadius * 0.9f, Vector3.down, m_groundHits,
                0.15f, m_groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider col = m_groundHits[i].collider;
                if (col.attachedRigidbody == m_rb) continue;
                // 積み木は地面扱いしない(真下に来た積み木で接地扱いになると、踏んで拾う判定が外れるため)
                if (col.GetComponentInParent<TumikiBlock>() != null) continue;
                return true;
            }
            return false;
        }

        // タワーの高さに合わせて当たり判定を伸縮
        private void UpdateBodyCollider()
        {
            float height = m_stack.Height;
            m_body.radius = kBodyRadius;
            m_body.height = height;
            m_body.center = new Vector3(0.0f, height * 0.5f, 0.0f);
        }

        //========================================
        // 積み木を拾う(上に乗る)
        //========================================

        private void OnCollisionEnter(Collision collision)
        {
            var block = collision.collider.GetComponentInParent<TumikiBlock>();
            if (block != null) TryPickupFromAbove(block);
        }

        // 空中で落下中に触れた積み木を拾う(踏んだ扱い)。飛んでいる積み木もどんな速さでも拾え、被弾しない
        // 速い積み木でも拾いやすいよう、当たった場所は問わない
        // 衝突はキャラクター側・積み木側のどちらからも通知されるため、両方から呼ばれても1回だけ拾う
        public bool TryPickupFromAbove(TumikiBlock block)
        {
            if (m_isEliminated || !m_isFallingInAir) return false;
            if (block.CurrentState == TumikiBlock.State.Stacked) return false;

            Pickup(block);
            // 同じステップで複数の積み木に触れても1つだけ拾う
            m_isFallingInAir = false;
            return true;
        }

        private void Pickup(TumikiBlock block)
        {
            // 足元を積み木の底に合わせる
            Vector3 p = block.transform.position;
            p.y -= TumikiBlock.kHeight * 0.5f;
            Vector3 delta = p - m_rb.position;
            m_rb.position = p;
            transform.position = p;
            // 頭と既存の段は見た目上その場に残す(めり込んでから迫り上がるのを防ぐ)
            m_stack.KeepVisualsInPlace(delta);
            Vector3 v = m_rb.linearVelocity;
            v.y = 0.0f;
            m_rb.linearVelocity = v;

            TumikiBlock overflow = m_stack.AddBottom(block);
            if (overflow != null)
            {
                // 一番古い段(頭の下)が後ろにこぼれる
                overflow.transform.position -= transform.forward * (kBodyRadius + TumikiBlock.kRadius + 0.1f);
                var rb = overflow.GetComponent<Rigidbody>();
                rb.linearVelocity = -transform.forward * 2.0f + Vector3.up * 1.0f;
            }
        }

        //========================================
        // 攻撃を与える
        //========================================

        public void OnDealtDamage()
        {
            DealtDamage?.Invoke(this);
        }

        public void StartHitStop(float duration, float timeScale, float shake)
        {
            m_hitStop.Begin(duration, timeScale, shake);
        }

        //========================================
        // 攻撃を受ける
        //========================================

        // ハンマーで最下段を弾き飛ばされた。ダメージを受けたらtrue
        // knocked: 弾になった積み木(首だけで脱落した場合や無敵中はnull)
        public bool KnockBottom(Vector3 dir, float speed, IDarumaAttacker attacker, out TumikiBlock knocked)
        {
            knocked = null;
            if (IsInvincible || m_isEliminated) return false;

            dir.y = 0.0f;
            dir.Normalize();

            if (m_stack.Count == 0)
            {
                // 首だけ
                Eliminate();
                return true;
            }

            knocked = m_stack.RemoveAt(0);
            knocked.transform.position = m_rb.position
                + dir * (kBodyRadius + TumikiBlock.kRadius + 0.05f)
                + Vector3.up * (TumikiBlock.kHeight * 0.5f);
            knocked.Launch(dir, speed, attacker, this);
            return true;
        }

        // 飛んできた積み木が当たった。当てた積み木を消滅させるならtrue
        // damaged: ダメージを受けたか(無敵中はfalse)
        public bool ReceiveProjectile(TumikiBlock projectile, Vector3 hitPoint, out bool damaged)
        {
            damaged = false;
            if (m_isEliminated) return false;
            // 無敵中は当たった積み木が崩れるだけ
            if (IsInvincible) return true;

            damaged = true;
            int index = m_stack.IndexFromHeight(hitPoint.y - m_rb.position.y);
            if (index >= m_stack.Count)
            {
                // 頭に当たった
                Eliminate();
                return true;
            }

            // 手ごたえ: 自分と抜けた段も、当たった積み木の速さに応じてヒットストップ
            projectile.GetImpactHitStop(out float duration, out float timeScale, out float shake);
            StartHitStop(duration, timeScale, shake);

            // 当たった段が抜けて、当たった角度に応じた速さで飛ぶ(連鎖)。ヒットストップが明けてから飛び出す
            TumikiBlock block = m_stack.RemoveAt(index);
            block.StartHitStop(duration, timeScale, shake);
            projectile.ComputeImpact(block.transform.position, out Vector3 dir, out float speed);
            block.Push(dir, speed, projectile.Owner, projectile.ReturnCount, this);
            return true;
        }

        // 脱落。とりあえずキャラクターを消すだけ(演出は今後)
        public void Eliminate()
        {
            if (m_isEliminated) return;
            m_isEliminated = true; Eliminated?.Invoke(this);
            Destroy(gameObject);
        }

        //========================================
        // ギミックからの影響(IGimmickAffectable)
        //========================================

        public bool CanBeAffected => !m_isEliminated;

        public void AddCarryVelocity(Vector3 velocity)
        {
            m_carryVelocity += velocity;
        }

        // 向きは移動入力で決める(止まっているときに照準が流れないように)ので、足場が回っても向きは変えない
        public void AddCarryRotation(float yawDegrees)
        {
        }

        public void AddForce(Vector3 acceleration)
        {
            m_force += acceleration;
        }

        public void AddKnockback(Vector3 velocity)
        {
            m_knockback += new Vector3(velocity.x, 0.0f, velocity.z);
            m_knockbackUp = Mathf.Max(m_knockbackUp, velocity.y);
        }

        public void RequestGravityScale(float scale)
        {
            m_gravityScale = Mathf.Min(m_gravityScale, scale);
        }

        //========================================
        // 無敵
        //========================================

        public void SetInvincible(float seconds)
        {
            m_invincibleTimer = seconds;
        }

        private void UpdateInvincible()
        {
            if (m_invincibleTimer <= 0.0f) return;

            m_invincibleTimer -= Time.deltaTime;
            bool visible = m_invincibleTimer <= 0.0f
                || Mathf.FloorToInt(m_invincibleTimer / m_blinkInterval) % 2 == 0;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                r.enabled = visible;
            }
        }
    }
}
