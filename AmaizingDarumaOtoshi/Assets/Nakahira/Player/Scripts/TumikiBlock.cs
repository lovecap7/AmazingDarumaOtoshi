using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // 積み木
    // Loose  : ステージに落ちている。上に乗ると拾える
    // Stacked: プレイヤーに積まれている。当たり判定はプレイヤー側のコライダーが担当する
    // Flying : 弾として地面を滑るように直進し、次第に減速する。壁で反射し、他の積み木に当たると連鎖する
    //           一定速度を下回るとLooseに戻る(無害になり、拾える)
    [RequireComponent(typeof(Rigidbody), typeof(HitStop))]
    public class TumikiBlock : MonoBehaviour
    {
        public enum State { Loose, Stacked, Flying }

        // 積み木1段の高さ
        public const float kHeight = 0.5f;
        // 積み木の半径
        public const float kRadius = 0.5f;
        // 触れたまま弾かれたとき、相手に向かって進んでいるとみなす向きの一致度(cos約70°)
        const float kApproachDot = 0.35f;

        [SerializeField] TumikiColor m_color = TumikiColor.Red;
        // これより下に落ちたら奈落に落ちたとみなして消滅
        [SerializeField] float m_killY = -10.0f;
        // 発射直後、撃った本人などと当たらないようにする時間
        [SerializeField] float m_ignoreTime = 0.3f;
        // 減速度・無害になる速さ(ScriptableObjectから毎回読む)
        [SerializeField] TumikiParams m_params;

        [Header("エフェクト")]
        // 動いている間に出る煙のトレイル
        [SerializeField] ParticleSystem m_smokeTrail;
        // 攻撃判定がある(飛んでいる)間に纏う炎のオーラ
        [SerializeField] ParticleSystem m_fireAura;
        // これより速く動いているときに煙を出す
        [SerializeField] float m_smokeMinSpeed = 1.0f;

        [Header("割れる演出")]
        [SerializeField] float m_shatterDuration = 0.3f;
        [SerializeField] int m_shatterPieces = 8;
        // 破片が放射状に広がる速さ
        [SerializeField] float m_shatterSpread = 3.0f;

        Rigidbody m_rb;
        Collider m_collider;
        HitStop m_hitStop;
        bool m_isConsumed; // 連鎖で崩れて消滅済み(崩れる途中も含む)

        // 崩れる途中(ヒットストップが明けるまで、当たり判定なしで残りの速度で進む)
        bool m_isBreaking;
        Vector3 m_breakVelocity;

        // ヒットストップ中に押された(弾にならない速さの)ときは、明けてから動き出す
        bool m_hasPendingPush;
        Vector3 m_pendingPushVelocity;

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
            m_hitStop = GetComponent<HitStop>();
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
            if (m_isBreaking)
            {
                UpdateBreaking();
                return;
            }

            // 奈落に落ちた
            if (transform.position.y < m_killY)
            {
                Consume();
                return;
            }

            // ヒットストップ中はスロー(弾かれた直後はほぼその場に留まり、明けると本来の速さで飛び出す)
            float timeScale = m_hitStop.TimeScale;
            float dt = Time.fixedDeltaTime * timeScale;

            UpdateIgnore(dt);

            if (m_hasPendingPush && !m_hitStop.IsActive)
            {
                m_hasPendingPush = false;
                if (CurrentState == State.Loose) m_rb.linearVelocity = m_pendingPushVelocity;
            }

            if (CurrentState != State.Flying) return;

            // 次第に減速し、遅くなったら落ちている積み木に戻る(残りの勢いは物理に任せる)
            Speed = Mathf.Max(0.0f, Speed - m_params.Deceleration * dt);
            if (Speed < m_params.HarmlessSpeed)
            {
                SetLoose();
                return;
            }

            // 水平に直進する(Y方向は重力に任せる)
            Vector3 v = Direction * (Speed * timeScale);
            v.y = m_rb.linearVelocity.y;
            m_rb.linearVelocity = v;
        }

        private void Update()
        {
            UpdateEffects();
        }

        // 煙: 動いている間 / 炎: 攻撃判定がある(飛んでいる)間
        private void UpdateEffects()
        {
            bool moving = false;
            if (m_isBreaking)
            {
                moving = m_breakVelocity.magnitude * m_hitStop.TimeScale > m_smokeMinSpeed;
            }
            else if (CurrentState != State.Stacked)
            {
                Vector3 v = m_rb.linearVelocity;
                v.y = 0.0f;
                moving = v.magnitude > m_smokeMinSpeed;
            }
            SetEmission(m_smokeTrail, moving);
            SetEmission(m_fireAura, CurrentState == State.Flying && !m_isBreaking);
        }

        private static void SetEmission(ParticleSystem ps, bool enabled)
        {
            if (ps == null) return;
            var emission = ps.emission;
            if (emission.enabled != enabled) emission.enabled = enabled;
        }

        // 消えるときに、出ている煙・炎がその場で消えないよう切り離して自然に消えるまで残す
        private void DetachEffects()
        {
            foreach (var ps in new[] { m_smokeTrail, m_fireAura })
            {
                if (ps == null) continue;
                ps.transform.SetParent(null, true);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(ps.gameObject, ps.main.startLifetime.constantMax + 0.1f);
            }
            m_smokeTrail = null;
            m_fireAura = null;
        }

        // 手ごたえ: この積み木だけスローになり、見た目が震える
        public void StartHitStop(float duration, float timeScale, float shake)
        {
            m_hitStop.Begin(duration, timeScale, shake);
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
            Vector3 velocity = dir.normalized * speed;
            if (m_hitStop.IsActive)
            {
                // ヒットストップが明けてから動き出す
                m_hasPendingPush = true;
                m_pendingPushVelocity = velocity;
                m_rb.linearVelocity = Vector3.zero;
            }
            else
            {
                m_rb.linearVelocity = velocity;
            }
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
            HandleContact(collision, false);
        }

        // 壁・積み木・キャラクターに触れたまま弾かれた場合はEnterが来ないので、Stayでも処理する
        private void OnCollisionStay(Collision collision)
        {
            HandleContact(collision, true);
        }

        // requireApproach: 相手に向かって進んでいるときだけ連鎖・被弾させる
        // (Stayでは、横を並んで滑っているだけの相手まで当たり扱いにしないため)
        private void HandleContact(Collision collision, bool requireApproach)
        {
            if (CurrentState != State.Flying || m_isConsumed) return;

            // 壁で反射
            if (TryReflect(collision)) return;

            // 他の積み木に当たったら、相手を飛ばして自分は崩れる(連鎖)
            var other = collision.collider.GetComponentInParent<TumikiBlock>();
            if (other != null && other != this)
            {
                if (other.m_isConsumed || other.CurrentState == State.Stacked) return;
                if (requireApproach && !IsApproaching(other.transform.position)) return;

                ComputeImpact(other.transform.position, out Vector3 dir, out float speed);
                GetImpactHitStop(out float duration, out float timeScale, out float shake);
                // 両方がヒットストップ。相手は明けてから飛び出し、自分は残りの速度でゆっくり進みながら耐えて、明けたら割れる
                other.StartHitStop(duration, timeScale, shake);
                other.Push(dir, speed, Owner, ReturnCount);
                Break(Direction * Speed - dir * speed, duration, shake);
                return;
            }

            // 生存キャラクター(のタワー・頭)に当たった
            var character = collision.collider.GetComponentInParent<DarumaCharacter>();
            if (character != null)
            {
                // 空中で落下中のキャラクターに触れたら(踏まれたら)被弾ではなく拾われる
                if (character.TryPickupFromAbove(this)) return;

                Vector3 hitPos = character.transform.position;
                hitPos.y = transform.position.y;
                if (requireApproach && !IsApproaching(hitPos)) return;

                // キャラクターは動かない相手として、ぶつかった方向の成分を失った残りの速度で割れる
                ComputeImpact(hitPos, out Vector3 dir, out float speed);
                Vector3 remaining = Direction * Speed - dir * speed;
                GetImpactHitStop(out float duration, out _, out float shake);

                // ダメージを受けたキャラクターと抜けた段のヒットストップはキャラクター側で行う
                bool consume = character.ReceiveProjectile(this, collision.GetContact(0).point, out bool damaged);
                // 弾の持ち主に手柄を通知(幽霊なら復活する)
                if (damaged && DarumaAttackerUtil.IsAlive(Owner)) Owner.OnDealtDamage();
                if (consume) Break(remaining, duration, shake);
            }
        }

        // 今の速さでぶつかったときのヒットストップ(速いほど長く強い)
        public void GetImpactHitStop(out float duration, out float timeScale, out float shake)
        {
            m_params.GetImpactHitStop(Speed, out duration, out shake);
            timeScale = m_params.ImpactTimeScale;
        }

        // 進行方向と相手への向きのなす角が約70°以内なら「相手に向かって進んでいる」
        private bool IsApproaching(Vector3 targetPos)
        {
            Vector3 to = targetPos - transform.position;
            to.y = 0.0f;
            if (to.sqrMagnitude < 0.0001f) return true;
            return Vector3.Dot(Direction, to.normalized) > kApproachDot;
        }

        // 壁(ReflectWall)なら、壁に向かって進んでいるときだけ反射する。壁ならtrue
        // (反射後は壁から離れる向きになるので、接触が続いても何度も反射しない)
        private bool TryReflect(Collision collision)
        {
            if (collision.collider.GetComponentInParent<ReflectWall>() == null) return false;

            Vector3 normal = collision.GetContact(0).normal;
            normal.y = 0.0f;
            if (normal.sqrMagnitude > 0.0001f && Vector3.Dot(Direction, normal) < 0.0f)
            {
                Direction = Vector3.Reflect(Direction, normal.normalized).normalized;
            }
            return true;
        }

        // 崩れる: すぐには消えず、当たり判定を消して残りの速度でゆっくり進みながら震え、
        // ヒットストップが明けたら割れて飛び散りながら消える
        public void Break(Vector3 velocity, float hitStopDuration, float shake)
        {
            if (m_isConsumed) return;
            m_isConsumed = true;

            RestoreIgnore();
            m_collider.enabled = false;
            if (!m_rb.isKinematic) m_rb.linearVelocity = Vector3.zero;
            m_rb.isKinematic = true;

            m_isBreaking = true;
            m_breakVelocity = velocity;
            m_hitStop.Begin(hitStopDuration, m_params.ImpactTimeScale, shake);
        }

        private void UpdateBreaking()
        {
            m_rb.MovePosition(m_rb.position + m_breakVelocity * (Time.fixedDeltaTime * m_hitStop.TimeScale));
            if (m_hitStop.IsActive) return;

            var r = GetComponentInChildren<Renderer>();
            if (r != null)
            {
                TumikiShatter.Spawn(transform.position, transform.rotation, m_breakVelocity,
                    r.sharedMaterial, TumikiColorUtil.ToUnityColor(m_color),
                    m_shatterDuration, m_shatterPieces, m_shatterSpread);
            }
            DetachEffects();
            Destroy(gameObject);
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
            DetachEffects();
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

        // 今当たらないようにしている相手(撃った本人など)に、さらに長く当たらないようにする
        public void ExtendIgnore(float time)
        {
            m_ignoreTimer = Mathf.Max(m_ignoreTimer, time);
        }

        private void UpdateIgnore(float dt)
        {
            if (m_ignored.Count == 0) return;
            m_ignoreTimer -= dt;
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
