using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // 積み木
    // Loose  : ステージに落ちている。上に乗ると拾える
    // Stacked: プレイヤーに積まれている。当たり判定はプレイヤー側のコライダーが担当する
    // Flying : 弾として地面を滑るように直進し、次第に減速する。壁で反射し、他の積み木に当たると連鎖する
    //           一定速度を下回るとLooseに戻る(無害になり、拾える)
    [RequireComponent(typeof(Rigidbody))]
    public class TumikiBlock : MonoBehaviour
    {
        public enum State { Loose, Stacked, Flying }

        // 積み木1段の高さ
        public const float kHeight = 0.5f;
        // 積み木の半径
        public const float kRadius = 0.5f;

        [SerializeField] TumikiColor m_color = TumikiColor.Red;
        // これより下に落ちたら奈落に落ちたとみなして消滅
        [SerializeField] float m_killY = -10.0f;
        // 発射直後、撃った本人などと当たらないようにする時間
        [SerializeField] float m_ignoreTime = 0.3f;
        // 減速度・無害になる速さ(ScriptableObjectから毎回読む)
        [SerializeField] TumikiParams m_params;

        [Header("割れる演出")]
        [SerializeField] float m_shatterDuration = 0.3f;
        [SerializeField] int m_shatterPieces = 8;
        // 破片が放射状に広がる速さ
        [SerializeField] float m_shatterSpread = 3.0f;

        Rigidbody m_rb;
        Collider m_collider;
        bool m_isConsumed; // 連鎖で崩れて消滅済み

        // 一時的に衝突を無視している相手
        readonly List<Collider> m_ignored = new List<Collider>();
        float m_ignoreTimer;

        public State CurrentState { get; private set; } = State.Loose;
        public TumikiColor Color => m_color;
        public Vector3 Direction { get; private set; }
        public float Speed { get; private set; }
        // この弾を最後に撃った(打ち返した)キャラクター。連鎖した先の積み木にも引き継がれる
        public IDarumaAttacker Owner { get; private set; }
        // ハンマーで打ち返された回数(打ち返しの弾速に使う)。連鎖した先にも引き継がれる
        public int ReturnCount { get; private set; }

        private void Awake()
        {
            m_rb = GetComponent<Rigidbody>();
            m_collider = GetComponent<Collider>();
            ApplyColor();
            if (CurrentState == State.Loose)
            {
                EnablePhysics();
            }
        }

        private void OnValidate()
        {
            ApplyColor();
        }

        private void FixedUpdate()
        {
            // 奈落に落ちた
            if (transform.position.y < m_killY)
            {
                Consume();
                return;
            }

            UpdateIgnore();

            if (CurrentState != State.Flying) return;

            // 次第に減速し、遅くなったら落ちている積み木に戻る(残りの勢いは物理に任せる)
            Speed = Mathf.Max(0.0f, Speed - m_params.Deceleration * Time.fixedDeltaTime);
            if (Speed < m_params.HarmlessSpeed)
            {
                SetLoose();
                return;
            }

            // 水平に直進する(Y方向は重力に任せる)
            Vector3 v = Direction * Speed;
            v.y = m_rb.linearVelocity.y;
            m_rb.linearVelocity = v;
        }

        public void SetColor(TumikiColor color)
        {
            m_color = color;
            ApplyColor();
        }

        // プレイヤーに積まれる
        public void SetStacked(Transform parent)
        {
            RestoreIgnore();
            CurrentState = State.Stacked;
            if (!m_rb.isKinematic)
            {
                m_rb.linearVelocity = Vector3.zero;
            }
            m_rb.isKinematic = true;
            m_rb.interpolation = RigidbodyInterpolation.None;
            m_collider.enabled = false;
            transform.SetParent(parent, true);
            transform.localRotation = Quaternion.identity;
        }

        // ステージに落ちている状態にする
        public void SetLoose()
        {
            CurrentState = State.Loose;
            EnablePhysics();
        }

        // ぶつかられて押し出される。弾になるほど速くなければ、落ちている積み木のまま押されるだけ
        public void Push(Vector3 dir, float speed, IDarumaAttacker owner, int returnCount, DarumaCharacter alsoIgnore = null)
        {
            if (m_isConsumed) return;

            if (speed >= m_params.HarmlessSpeed)
            {
                Launch(dir, speed, owner, alsoIgnore, returnCount);
                return;
            }

            dir.y = 0.0f;
            SetLoose();
            if (alsoIgnore != null) IgnoreTemporarily(alsoIgnore.Body);
            m_rb.linearVelocity = dir.normalized * speed;
        }

        // 弾として飛ばす
        // alsoIgnore: 撃った本人以外に一時的に当たらないようにしたい相手(弾き出された元のキャラクター等)
        public void Launch(Vector3 dir, float speed, IDarumaAttacker owner, DarumaCharacter alsoIgnore = null, int returnCount = 0)
        {
            if (m_isConsumed) return;

            dir.y = 0.0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = Direction.sqrMagnitude > 0.0001f ? Direction : Vector3.forward;
            }

            EnablePhysics();
            CurrentState = State.Flying;
            Direction = dir.normalized;
            Speed = speed;
            Owner = owner;
            ReturnCount = returnCount;

            RestoreIgnore();
            if (DarumaAttackerUtil.IsAlive(owner)) IgnoreTemporarily(owner.Body);
            if (alsoIgnore != null) IgnoreTemporarily(alsoIgnore.Body);

            m_rb.linearVelocity = Direction * Speed;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (CurrentState != State.Flying || m_isConsumed) return;

            // 壁で反射
            if (collision.collider.GetComponentInParent<ReflectWall>() != null)
            {
                Vector3 normal = collision.GetContact(0).normal;
                normal.y = 0.0f;
                if (normal.sqrMagnitude > 0.0001f && Vector3.Dot(Direction, normal) < 0.0f)
                {
                    Direction = Vector3.Reflect(Direction, normal.normalized).normalized;
                }
                return;
            }

            // 他の積み木に当たったら、相手を飛ばして自分は崩れる(連鎖)
            var other = collision.collider.GetComponentInParent<TumikiBlock>();
            if (other != null && other != this)
            {
                if (other.m_isConsumed || other.CurrentState == State.Stacked) return;

                ComputeImpact(other.transform.position, out Vector3 dir, out float speed);
                other.Push(dir, speed, Owner, ReturnCount);
                // 相手に渡した残りの速度で進みながら割れる
                Shatter(Direction * Speed - dir * speed);
                return;
            }

            // 生存キャラクター(のタワー・頭)に当たった
            var character = collision.collider.GetComponentInParent<DarumaCharacter>();
            if (character != null)
            {
                // キャラクターは動かない相手として、ぶつかった方向の成分を失った残りの速度で割れる
                Vector3 hitPos = character.transform.position;
                hitPos.y = transform.position.y;
                ComputeImpact(hitPos, out Vector3 dir, out float speed);
                Vector3 remaining = Direction * Speed - dir * speed;

                bool consume = character.ReceiveProjectile(this, collision.GetContact(0).point, out bool damaged);
                // 弾の持ち主に手柄を通知(幽霊なら復活する)
                if (damaged && DarumaAttackerUtil.IsAlive(Owner)) Owner.OnDealtDamage();
                if (consume) Shatter(remaining);
            }
        }

        // 割れて飛び散りながら消える
        public void Shatter(Vector3 velocity)
        {
            if (m_isConsumed) return;

            var r = GetComponentInChildren<Renderer>();
            if (r != null)
            {
                TumikiShatter.Spawn(transform.position, transform.rotation, velocity,
                    r.sharedMaterial, TumikiColorUtil.ToUnityColor(m_color),
                    m_shatterDuration, m_shatterPieces, m_shatterSpread);
            }
            Consume();
        }

        // ビリヤードのように、ぶつかった相手は中心同士を結ぶ方向へ、自分の速度のその方向の成分で飛ぶ
        // (正面衝突ならほぼ全速度、かすっただけなら遅く横へ)
        public void ComputeImpact(Vector3 targetPos, out Vector3 dir, out float speed)
        {
            dir = targetPos - transform.position;
            dir.y = 0.0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Direction;
            dir.Normalize();
            speed = Speed * Mathf.Max(0.0f, Vector3.Dot(Direction, dir));
        }

        // 崩れて消滅する
        public void Consume()
        {
            if (m_isConsumed) return;
            m_isConsumed = true;
            RestoreIgnore();
            Destroy(gameObject);
        }

        private void EnablePhysics()
        {
            transform.SetParent(null, true);
            m_collider.enabled = true;
            m_rb.isKinematic = false;
            m_rb.useGravity = true;
            m_rb.interpolation = RigidbodyInterpolation.Interpolate;
            m_rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // 倒れないように回転は固定(上に乗れるように常に立てておく)
            m_rb.constraints = RigidbodyConstraints.FreezeRotation;
            // 無敵点滅中に外れた場合でも見えるようにする
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;
        }

        private void IgnoreTemporarily(Collider col)
        {
            if (col == null) return;
            Physics.IgnoreCollision(m_collider, col, true);
            m_ignored.Add(col);
            m_ignoreTimer = m_ignoreTime;
        }

        private void UpdateIgnore()
        {
            if (m_ignored.Count == 0) return;
            m_ignoreTimer -= Time.fixedDeltaTime;
            if (m_ignoreTimer <= 0.0f)
            {
                RestoreIgnore();
            }
        }

        private void RestoreIgnore()
        {
            foreach (var col in m_ignored)
            {
                if (col != null) Physics.IgnoreCollision(m_collider, col, false);
            }
            m_ignored.Clear();
        }

        private void ApplyColor()
        {
            var r = GetComponentInChildren<Renderer>();
            if (r == null) return;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", TumikiColorUtil.ToUnityColor(m_color));
            r.SetPropertyBlock(mpb);
        }
    }
}
