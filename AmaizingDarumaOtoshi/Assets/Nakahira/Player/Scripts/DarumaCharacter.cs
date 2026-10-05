using System;
using UnityEngine;

namespace Nakahira
{
    // 生存キャラクター(だるま)。移動・ジャンプ・積み木の取得・被弾・脱落を担当する
    // 操作は IDarumaCommandSource(同じGameObjectに付いていれば自動取得、SetCommandSourceで差し替え可)
    // または ApplyCommand で渡す
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class DarumaCharacter : MonoBehaviour, IDarumaAttacker
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

        [Header("参照")]
        [SerializeField] DarumaStack m_stack;
        [SerializeField] DarumaHammer m_hammer;

        // 胴体の半径
        public const float kBodyRadius = 0.45f;

        Rigidbody m_rb;
        CapsuleCollider m_body;
        IDarumaCommandSource m_commandSource;

        Vector3 m_moveDirection; // ワールド空間の移動方向(XZ)
        bool m_jumpRequested;
        bool m_isGrounded;
        bool m_isEliminated;
        float m_invincibleTimer;
        readonly RaycastHit[] m_groundHits = new RaycastHit[8];

        public DarumaStack Stack => m_stack;
        public Collider Body => m_body;
        public Rigidbody Rigidbody => m_rb;
        public bool IsGrounded => m_isGrounded;
        public bool IsInvincible => m_invincibleTimer > 0.0f;
        public float MoveSpeed => Mathf.Max(m_minMoveSpeed, m_baseMoveSpeed - m_stack.Count * m_speedLossPerBlock);

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
            if (command.Shot) m_hammer.TryShot();
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
            UpdateMove();
            UpdateGravity();
            UpdateJump();
        }

        // 上昇中は通常の重力、落下中は倍率を掛けた重力
        private void UpdateGravity()
        {
            Vector3 v = m_rb.linearVelocity;
            float gravity = m_jumpParams.Gravity;
            if (v.y < 0.0f) gravity *= m_jumpParams.FallGravityMultiplier;
            v.y -= gravity * Time.fixedDeltaTime;
            m_rb.linearVelocity = v;
        }

        private void UpdateMove()
        {
            Vector3 moveDir = m_moveDirection;

            Vector3 v = moveDir * MoveSpeed;
            v.y = m_rb.linearVelocity.y;
            m_rb.linearVelocity = v;

            // ハンマーを振っている間は向きを固定(打ち返しの方向がブレないように)
            if (moveDir.sqrMagnitude > 0.0001f && !m_hammer.IsBusy)
            {
                Quaternion target = Quaternion.LookRotation(moveDir, Vector3.up);
                m_rb.MoveRotation(Quaternion.Slerp(m_rb.rotation, target, m_turnSpeed * Time.fixedDeltaTime));
            }
        }

        private void UpdateJump()
        {
            if (!m_jumpRequested) return;
            m_jumpRequested = false;
            if (!m_isGrounded) return;

            Vector3 v = m_rb.linearVelocity;
            v.y = m_jumpParams.JumpSpeed;
            m_rb.linearVelocity = v;
        }

        private bool CheckGrounded()
        {
            if (m_rb.linearVelocity.y > 0.1f) return false;

            Vector3 origin = m_rb.position + Vector3.up * (kBodyRadius + 0.05f);
            int count = Physics.SphereCastNonAlloc(origin, kBodyRadius * 0.9f, Vector3.down, m_groundHits,
                0.15f, m_groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (m_groundHits[i].collider.attachedRigidbody != m_rb) return true;
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
            if (block == null || block.CurrentState != TumikiBlock.State.Loose) return;

            // 上から乗ったときだけ拾う
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y > 0.7f)
                {
                    Pickup(block);
                    return;
                }
            }
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

            // 当たった段が抜けて、当たった角度に応じた速さで飛ぶ(連鎖)
            TumikiBlock block = m_stack.RemoveAt(index);
            projectile.ComputeImpact(block.transform.position, out Vector3 dir, out float speed);
            block.Push(dir, speed, projectile.Owner, projectile.ReturnCount, this);
            return true;
        }

        // 脱落。とりあえずキャラクターを消すだけ(演出は今後)
        public void Eliminate()
        {
            if (m_isEliminated) return;
            m_isEliminated = true;            Eliminated?.Invoke(this);
            Destroy(gameObject);
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
