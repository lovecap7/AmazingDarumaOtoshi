using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // 攻撃判定のある積み木が、他の積み木にぶつかったときの振る舞い
    // 積み木ごとに差し替えられる(TumikiBlock.ImpactBehavior)。新しい振る舞いはこのインターフェースを実装して作る
    public interface ITumikiImpactBehavior
    {
        // self: ぶつかった攻撃判定のある積み木 / other: ぶつかられた積み木(崩れる途中・積まれているものは除く)
        void OnHitBlock(TumikiBlock self, TumikiBlock other);
    }

    // 既定の振る舞い: 相手を飛ばして自分は崩れる(連鎖)
    public sealed class TumikiBreakImpact : ITumikiImpactBehavior
    {
        public static readonly TumikiBreakImpact Instance = new TumikiBreakImpact();

        public void OnHitBlock(TumikiBlock self, TumikiBlock other)
        {
            self.ComputeImpact(other.transform.position, out Vector3 dir, out float speed);
            self.GetImpactHitStop(out float duration, out float timeScale, out float shake);
            // 両方がヒットストップ。相手は明けてから飛び出し、自分は残りの速度でゆっくり進みながら耐えて、明けたら割れる
            other.StartHitStop(duration, timeScale, shake);
            other.Push(dir, speed, self.Owner, self.ReturnCount);
            self.Break(self.Direction * self.Speed - dir * speed, duration, shake);
        }
    }

    // 積み木
    // Loose  : ステージに落ちている。上に乗ると拾える
    // Stacked: プレイヤーに積まれている。当たり判定はプレイヤー側のコライダーが担当する
    // Flying : 地面を滑るように直進し、次第に減速する。壁で反射する。止まる速さを下回るとLooseに戻る
    //           ・攻撃判定あり(HarmlessSpeed以上): 他の積み木に当たると ImpactBehavior に従い(既定は連鎖)、キャラクターにダメージを与える
    //           ・攻撃判定なし(HarmlessSpeed未満): 積み木同士は弾き合い、キャラクターには跳ね返される
    [RequireComponent(typeof(Rigidbody), typeof(HitStop))]
    public class TumikiBlock : MonoBehaviour, IGimmickAffectable
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
        // 減速度・攻撃判定がなくなる速さ・止まる速さ(ScriptableObjectから毎回読む)
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

        // ギミックからの影響(IGimmickAffectable)
        Vector3 m_carryVelocity;      // 次のステップで上乗せする運搬速度
        Vector3 m_appliedCarry;       // 前のステップで上乗せした運搬速度
        Vector3 m_force;              // 次のステップで加える力(加速度)
        float m_carryYaw;             // 次のステップで足場ごと回る角度(度)
        float m_gravityScale = 1.0f;  // 次のステップの重力の倍率


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
        // 攻撃判定があるか(連鎖・ダメージが起きる速さで動いている)
        public bool IsHarmful => CurrentState == State.Flying && !m_isConsumed && Speed >= m_params.HarmlessSpeed;

        // 攻撃判定があるときに他の積み木にぶつかったときの振る舞い。積み木ごとに設定がなければ DefaultImpactBehavior を使う
        // nullを設定すると既定に戻る
        public ITumikiImpactBehavior ImpactBehavior
        {
            get => m_impactBehavior ?? DefaultImpactBehavior ?? TumikiBreakImpact.Instance;
            set => m_impactBehavior = value;
        }
        ITumikiImpactBehavior m_impactBehavior;

        // 全積み木の既定の振る舞い(デバッグでの切り替えや、ルールによる変更用)
        public static ITumikiImpactBehavior DefaultImpactBehavior { get; set; } = TumikiBreakImpact.Instance;

        // ドメインの再読み込みをしない設定でも、再生のたびに既定に戻す
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => DefaultImpactBehavior = TumikiBreakImpact.Instance;

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
            if (CurrentState == State.Flying) UpdateFlying(dt, timeScale);
            UpdateGimmickEffects();
        }

        private void UpdateFlying(float dt, float timeScale)
        {
            // 次第に減速し(HarmlessSpeedを下回ると攻撃判定がなくなる)、ほぼ止まったら落ちている積み木に戻る
            Speed = Mathf.Max(0.0f, Speed - m_params.Deceleration * dt);
            if (Speed < m_params.StopSpeed)
            {
                SetLoose();
                return;
            }

            // 水平に直進する(Y方向は重力に任せる)
            Vector3 v = Direction * (Speed * timeScale);
            v.y = m_rb.linearVelocity.y;
            m_rb.linearVelocity = v;
        }

        // ギミックからの影響を反映する。運搬は溜め込まない(要求をやめるとすぐ止まる)
        private void UpdateGimmickEffects()
        {
            Vector3 carry = m_carryVelocity;
            Vector3 force = m_force;
            float yaw = m_carryYaw;
            float gravityScale = m_gravityScale;
            m_carryVelocity = Vector3.zero;
            m_force = Vector3.zero;
            m_carryYaw = 0.0f;
            m_gravityScale = 1.0f;

            if (!CanBeAffected || m_rb.isKinematic)
            {
                m_appliedCarry = Vector3.zero;
                return;
            }

            // 足場ごと回る: 向きを回す(進む方向も下で同じだけ回す)
            Quaternion turn = Quaternion.Euler(0.0f, yaw, 0.0f);
            if (yaw != 0.0f) m_rb.rotation = turn * m_rb.rotation;

            if (CurrentState == State.Flying)
            {
                // 足場の回転・力は進む向き・速さそのものを変える(弾道が曲がり、速さに応じて攻撃判定も変わる)
                if (yaw != 0.0f || force != Vector3.zero)
                {
                    float dt = Time.fixedDeltaTime * m_hitStop.TimeScale;
                    Vector3 h = turn * Direction * Speed + new Vector3(force.x, 0.0f, force.z) * dt;
                    Speed = h.magnitude;
                    if (Speed > 0.0001f) Direction = h / Speed;
                    Vector3 fv = Direction * (Speed * m_hitStop.TimeScale);
                    fv.y = m_rb.linearVelocity.y + force.y * dt;
                    m_rb.linearVelocity = fv;
                }

                // 飛んでいる間は水平速度が毎ステップ上書きされるので、前のステップの分を取り除くのは上下方向だけ
                Vector3 v = m_rb.linearVelocity;
                v.y -= m_appliedCarry.y;
                m_rb.linearVelocity = v + carry;
                m_appliedCarry = carry;
            }
            else
            {
                // 落ちている積み木は床との摩擦で速度が削られるので、位置で運ぶ
                // (飛んでいる途中で止まった場合は、上乗せしていた速度を取り除く)
                if (m_appliedCarry != Vector3.zero) m_rb.linearVelocity -= m_appliedCarry;
                m_appliedCarry = Vector3.zero;
                // 滑っている場合は、進む方向も向きと同じだけ回す
                if (yaw != 0.0f)
                {
                    Vector3 v = m_rb.linearVelocity;
                    Vector3 h = turn * new Vector3(v.x, 0.0f, v.z);
                    m_rb.linearVelocity = new Vector3(h.x, v.y, h.z);
                }
                m_rb.position += carry * Time.fixedDeltaTime;
                // 力は物理に任せる(床との摩擦より強ければ動き出し、慣性が残る)
                if (force != Vector3.zero) m_rb.AddForce(force, ForceMode.Acceleration);
            }

            if (gravityScale < 1.0f && m_rb.useGravity)
            {
                m_rb.AddForce(Physics.gravity * (gravityScale - 1.0f), ForceMode.Acceleration);
            }
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
            SetEmission(m_fireAura, IsHarmful);
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

        // ぶつかられて押し出される。速さに応じて攻撃判定の有無が決まる。ほとんど動かないなら落ちている積み木のまま
        public void Push(Vector3 dir, float speed, IDarumaAttacker owner, int returnCount, DarumaCharacter alsoIgnore = null)
        {
            if (m_isConsumed) return;

            if (speed >= m_params.StopSpeed)
            {
                Launch(dir, speed, owner, alsoIgnore, returnCount);
                return;
            }

            SetLoose();
            if (alsoIgnore != null) IgnoreTemporarily(alsoIgnore.Body);
        }

        //========================================
        // ギミックからの影響(IGimmickAffectable)
        //========================================

        public Rigidbody Rigidbody => m_rb;
        // 積まれている間はキャラクターがまとめて影響を受ける
        public bool CanBeAffected => CurrentState != State.Stacked && !m_isConsumed && !m_isBreaking;

        public void AddCarryVelocity(Vector3 velocity)
        {
            m_carryVelocity += velocity;
        }

        public void AddCarryRotation(float yawDegrees)
        {
            m_carryYaw += yawDegrees;
        }

        public void AddForce(Vector3 acceleration)
        {
            m_force += acceleration;
        }

        // 水平方向は弾と同じ仕組みで押し出す(速ければ攻撃判定を持ち、最後の持ち主を引き継ぐ)
        public void AddKnockback(Vector3 velocity)
        {
            if (!CanBeAffected) return;
            Vector3 h = new Vector3(velocity.x, 0.0f, velocity.z);
            Push(h, h.magnitude, Owner, ReturnCount);
            if (velocity.y > 0.0f && !m_rb.isKinematic)
            {
                Vector3 v = m_rb.linearVelocity;
                v.y = Mathf.Max(v.y, velocity.y);
                m_rb.linearVelocity = v;
            }
        }

        public void RequestGravityScale(float scale)
        {
            m_gravityScale = Mathf.Min(m_gravityScale, scale);
        }

        // 水平方向の今の速度(落ちている積み木は物理の速度)
        public Vector3 HorizontalVelocity
        {
            get
            {
                if (CurrentState == State.Flying) return Direction * Speed;
                Vector3 v = m_rb.linearVelocity;
                v.y = 0.0f;
                return v;
            }
        }

        // 攻撃判定のない積み木同士が弾き合う(同じ重さの弾性衝突。どちらも壊れない)
        private void BounceWith(TumikiBlock other)
        {
            Vector3 n = other.transform.position - transform.position;
            n.y = 0.0f;
            if (n.sqrMagnitude < 0.0001f) return;
            n.Normalize();

            Vector3 v1 = HorizontalVelocity;
            // 落ちている積み木は止まっていたとみなす(衝突の通知時点では物理が既に少し押してしまっているため)
            Vector3 v2 = other.CurrentState == State.Flying ? other.HorizontalVelocity : Vector3.zero;
            // 近づいているときだけ(両方の積み木から通知が来ても1回だけ弾き合う)
            float approach = Vector3.Dot(v1 - v2, n);
            if (approach <= 0.0f) return;

            SetHorizontalVelocity(v1 - n * approach);
            Vector3 v2After = v2 + n * approach;
            other.Push(v2After, v2After.magnitude, Owner, 0);
        }

        // 攻撃判定のない積み木がキャラクターに当たったら、キャラクターを押さずに壁のように跳ね返る
        private void ReflectFrom(Vector3 targetPos)
        {
            Vector3 normal = transform.position - targetPos;
            normal.y = 0.0f;
            if (normal.sqrMagnitude < 0.0001f) return;
            normal.Normalize();
            if (Vector3.Dot(Direction, normal) < 0.0f)
            {
                Direction = Vector3.Reflect(Direction, normal).normalized;
                SetHorizontalVelocity(Direction * Speed);
            }
        }

        // 飛んでいる積み木の水平速度を変える(遅すぎれば止まって落ちている積み木に戻る)。振る舞いの差し替え用
        public void SetFlyingVelocity(Vector3 v)
        {
            if (CurrentState != State.Flying || m_isConsumed) return;
            SetHorizontalVelocity(v);
        }

        private void SetHorizontalVelocity(Vector3 v)
        {
            v.y = 0.0f;
            float speed = v.magnitude;
            if (speed < m_params.StopSpeed)
            {
                SetLoose();
                return;
            }
            Direction = v / speed;
            Speed = speed;
            Vector3 rv = Direction * (Speed * m_hitStop.TimeScale);
            rv.y = m_rb.linearVelocity.y;
            m_rb.linearVelocity = rv;
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

            // 他の積み木に当たった
            var other = collision.collider.GetComponentInParent<TumikiBlock>();
            if (other != null && other != this)
            {
                if (other.m_isConsumed || other.CurrentState == State.Stacked) return;

                if (!IsHarmful)
                {
                    // 攻撃判定のある相手なら、相手側の連鎖の処理に任せる
                    if (!other.IsHarmful) BounceWith(other);
                    return;
                }

                // 攻撃判定あり: 振る舞い(既定は相手を飛ばして自分は崩れる連鎖)に任せる
                if (requireApproach && !IsApproaching(other.transform.position)) return;
                ImpactBehavior.OnHitBlock(this, other);
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

                // 攻撃判定なし: キャラクターを押さずに跳ね返る
                if (!IsHarmful)
                {
                    ReflectFrom(hitPos);
                    return;
                }

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
