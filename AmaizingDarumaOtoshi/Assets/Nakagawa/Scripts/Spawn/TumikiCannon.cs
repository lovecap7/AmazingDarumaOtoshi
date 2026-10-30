using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 回転砲台。積み木を土台にしたおもちゃの大砲で、生成エリアの一種として TumikiSpawner の生成命令で積み木を撃ち出す
    //  ・砲身は回り続け(一定方向に回転 / 左右に首振り / 固定)、砲口の前の床に向きを示す点線のガイドが出る
    //  ・命令を受けると、砲身が膨らむ → 砲口が光る(ため) → ポンッと発射。複数個なら一定間隔で1個ずつ撃つ(回りながらなので扇状にばらまく)
    //  ・弾の持ち主は「なし」。周りのプレイヤーの有無にかかわらず発射する
    //  ・本体に飛んでいる積み木が当たると、壁と同じように跳ね返る(当たり判定は円柱)
    // このTransformの位置が土台の中心(床の高さ)
    [RequireComponent(typeof(Rigidbody))]
    public class TumikiCannon : TumikiSpawnSource, ITumikiReflector
    {
        public enum RotationMode { Spin, Swing, Fixed }

        [Header("回り方")]
        [SerializeField] RotationMode m_rotationMode = RotationMode.Spin;
        // 一定方向に回転するときの向き
        [SerializeField] bool m_clockwise = true;
        // 回転の速さ(度/秒)。首振りでは首を振る一番速いときの速さ
        [SerializeField] float m_rotationSpeed = 40.0f;
        // 首振りの片側の角度(配置した向きを中心に左右へ振る)
        [SerializeField, Range(0.0f, 180.0f)] float m_swingAngle = 60.0f;

        [Header("発射")]
        [SerializeField] float m_fireSpeed = 14.0f;
        // 最初の1発を撃つまでのため(砲身が膨らみ、砲口が光る)
        [SerializeField, Min(0.0f)] float m_chargeTime = 1.0f;
        // 2発目以降の間隔
        [SerializeField, Min(0.0f)] float m_fireInterval = 0.3f;

        [Header("排出量(1回の生成命令で割り振られる数)")]
        [SerializeField, Min(0)] int m_minAmount = 1;
        [SerializeField, Min(0)] int m_maxAmount = 4;

        [Header("当たり判定")]
        [SerializeField] float m_radius = 0.8f;
        [SerializeField] float m_height = 1.2f;

        [Header("見た目")]
        // 砲身ごと水平に回る部分
        [SerializeField] Transform m_turret;
        // ためで膨らむ部分
        [SerializeField] Transform m_barrel;
        // 砲口の光(ためで明るく大きくなる)
        [SerializeField] Renderer m_muzzleGlow;
        [SerializeField] Color m_chargeColor = GimmickPalette.Warning;
        [SerializeField] float m_inflateScale = 1.35f;
        // ためで光が点滅する速さ(1秒あたりの回数)
        [SerializeField] float m_chargeBlinkRate = 8.0f;
        [SerializeField] float m_muzzleGlowSize = 0.6f;
        // 撃った反動で砲身が縮む時間
        [SerializeField] float m_recoilTime = 0.15f;

        [Header("ガイド線")]
        // 点線(砲口から前へ並べる)
        [SerializeField] Renderer[] m_guideDashes;
        [SerializeField] Color m_guideColor = new Color(1.0f, 1.0f, 1.0f, 0.55f);
        [SerializeField] float m_dashLength = 0.35f;
        [SerializeField] float m_dashGap = 0.25f;
        [SerializeField] float m_dashWidth = 0.12f;

        [Header("エフェクト")]
        // 発射: 砲口から前へ火花と煙が噴き出す
        [SerializeField] GimmickBurstSettings m_fireEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 0.85f, 0.35f, 1.0f), ColorB = new Color(1.0f, 0.5f, 0.15f, 1.0f),
            Count = 26, Speed = new Vector2(3.0f, 7.0f), Size = new Vector2(0.06f, 0.16f), Lifetime = new Vector2(0.15f, 0.35f),
            Shape = GimmickBurstShape.Cone, ShapeSize = new Vector3(0.2f, 0, 0), ConeAngle = 25.0f, Stretch = true,
        };
        [SerializeField] GimmickBurstSettings m_fireSmokeEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.95f, 0.95f, 0.95f, 0.8f), ColorB = new Color(0.75f, 0.75f, 0.78f, 0.7f),
            Count = 12, Speed = new Vector2(0.8f, 2.0f), Size = new Vector2(0.25f, 0.5f), Lifetime = new Vector2(0.4f, 0.7f),
            Gravity = -0.05f, Shape = GimmickBurstShape.Cone, ShapeSize = new Vector3(0.25f, 0, 0), ConeAngle = 35.0f,
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.9f, 0.5f, 0.7f), ShockwaveRadius = 1.2f, ShockwaveTime = 0.25f,
        };
        // ため始め: 砲口に光の粒が集まる
        [SerializeField] GimmickBurstSettings m_chargeEffect = new GimmickBurstSettings
        {
            ColorA = GimmickPalette.Warning, ColorB = new Color(1.0f, 1.0f, 0.75f, 1.0f),
            Count = 12, Speed = new Vector2(0.3f, 0.8f), Size = new Vector2(0.05f, 0.1f), Lifetime = new Vector2(0.4f, 0.7f),
            Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.35f, 0, 0),
        };
        // 砲口の位置(m_turret 基準。発射のエフェクトを出す)
        [SerializeField] Vector3 m_muzzleOffset = new Vector3(0.0f, 1.2f, 0.95f);

        [Header("音")]
        [SerializeField] AudioClip m_chargeClip;
        [SerializeField, Range(0.0f, 1.0f)] float m_chargeVolume = 0.3f;
        [SerializeField] AudioClip m_fireClip;
        [SerializeField, Range(0.0f, 1.0f)] float m_fireVolume = 0.5f;

        // 弾を出す、本体の縁からの隙間
        const float kMuzzleGap = 0.1f;

        readonly Queue<TumikiColor> m_queue = new Queue<TumikiColor>();
        TumikiBlock m_prefab;
        bool m_charging;
        float m_chargeTimer;
        float m_shotTimer;
        float m_recoilTimer;
        float m_time;
        float m_baseYaw;
        float m_yaw;
        Vector3 m_barrelScale = Vector3.one;

        public override int MinAmount => m_minAmount;
        public override int MaxAmount => Mathf.Max(m_minAmount, m_maxAmount);
        public override int PendingCount => m_queue.Count;

        // 今、砲口が向いている水平方向
        public Vector3 AimDirection => Quaternion.Euler(0.0f, m_yaw, 0.0f) * Vector3.forward;
        // 砲口の位置(ワールド)
        Vector3 MuzzlePosition => transform.position + Quaternion.Euler(0.0f, m_yaw, 0.0f) * m_muzzleOffset;

        private void Awake()
        {
            GetComponent<Rigidbody>().isKinematic = true;
            m_baseYaw = transform.eulerAngles.y;
            m_yaw = m_baseYaw;
            if (m_barrel != null) m_barrelScale = m_barrel.localScale;
            ApplyShape();
        }

        // ステージの切り替えなどで無効になったら、撃ち残しは捨てる
        private void OnDisable()
        {
            m_queue.Clear();
            m_charging = false;
            m_time = 0.0f;
            m_yaw = m_baseYaw;
        }

        private void OnValidate()
        {
            m_radius = Mathf.Max(0.1f, m_radius);
            m_height = Mathf.Max(0.1f, m_height);
            m_maxAmount = Mathf.Max(m_minAmount, m_maxAmount);
            ApplyShape();
        }

        public override int Spawn(IReadOnlyList<TumikiColor> colors, TumikiBlock prefab, List<Vector3> reserved)
        {
            if (colors.Count == 0 || prefab == null) return 0;
            m_prefab = prefab;
            bool idle = m_queue.Count == 0;
            foreach (var c in colors) m_queue.Enqueue(c);
            if (idle) StartCharge();
            return colors.Count;
        }

        private void StartCharge()
        {
            m_charging = true;
            m_chargeTimer = 0.0f;
            GimmickBurst.Play(m_chargeEffect, MuzzlePosition);
            if (m_chargeTime > 0.0f)
            {
                AudioClip clip = GimmickSound.Or(m_chargeClip, GimmickSound.Charge);
                GimmickSound.Play(clip, m_chargeVolume, clip.length / m_chargeTime);
            }
        }

        private void Update()
        {
            // 止めている間は回らず、ためも発射もしない
            float dt = GimmickClock.IsHeld ? 0.0f : Time.deltaTime;
            m_time += dt;
            UpdateRotation(dt);
            UpdateFiring(dt);
            UpdateVisuals(dt);
        }

        private void UpdateRotation(float dt)
        {
            switch (m_rotationMode)
            {
                case RotationMode.Spin:
                    m_yaw = Mathf.Repeat(m_yaw + (m_clockwise ? m_rotationSpeed : -m_rotationSpeed) * dt, 360.0f);
                    break;
                case RotationMode.Swing:
                    // 一番速いときの速さが回転の速さになるように振る
                    float omega = m_swingAngle > 0.01f ? m_rotationSpeed / m_swingAngle : 0.0f;
                    m_yaw = m_baseYaw + m_swingAngle * Mathf.Sin(m_time * omega);
                    break;
                default:
                    m_yaw = m_baseYaw;
                    break;
            }
            if (m_turret != null) m_turret.rotation = Quaternion.Euler(0.0f, m_yaw, 0.0f);
        }

        private void UpdateFiring(float dt)
        {
            if (m_queue.Count == 0) return;
            if (m_charging)
            {
                m_chargeTimer += dt;
                if (m_chargeTimer < m_chargeTime) return;
                m_charging = false;
                Fire();
                m_shotTimer = m_fireInterval;
                return;
            }
            m_shotTimer -= dt;
            if (m_shotTimer > 0.0f) return;
            Fire();
            m_shotTimer = m_fireInterval;
        }

        // 砲口の前に積み木を出して、砲身の向きに飛ばす(持ち主なし)
        private void Fire()
        {
            if (m_queue.Count == 0 || m_prefab == null) return;
            TumikiColor color = m_queue.Dequeue();
            Vector3 dir = AimDirection;
            Vector3 pos = transform.position + dir * (m_radius + TumikiBlock.kRadius + kMuzzleGap)
                        + Vector3.up * (TumikiBlock.kHeight * 0.5f + 0.05f);
            TumikiBlock block = Instantiate(m_prefab, pos, Quaternion.LookRotation(dir));
            block.SetColor(color);
            block.Launch(dir, m_fireSpeed, null);
            m_recoilTimer = m_recoilTime;
            Quaternion aim = Quaternion.LookRotation(dir);
            GimmickBurst.Play(m_fireEffect, MuzzlePosition, aim);
            GimmickBurst.Play(m_fireSmokeEffect, MuzzlePosition, aim);
            GimmickSound.Play(GimmickSound.Or(m_fireClip, GimmickSound.Pop), m_fireVolume);
        }

        private void UpdateVisuals(float dt)
        {
            m_recoilTimer = Mathf.Max(0.0f, m_recoilTimer - dt);
            float charge = m_charging && m_chargeTime > 0.0f ? Mathf.Clamp01(m_chargeTimer / m_chargeTime) : 0.0f;
            float recoil = m_recoilTime > 0.0f ? m_recoilTimer / m_recoilTime : 0.0f;

            // 砲身: ためで膨らみ、撃つと一瞬縮んでから戻る
            if (m_barrel != null)
            {
                float s = Mathf.Lerp(1.0f, m_inflateScale, charge * charge) * (1.0f - 0.15f * Mathf.Sin(recoil * Mathf.PI));
                m_barrel.localScale = new Vector3(m_barrelScale.x * s, m_barrelScale.y * s, m_barrelScale.z);
            }

            // 砲口の光: ための間は点滅しながら明るく大きくなり、撃った瞬間に強く光る
            if (m_muzzleGlow != null)
            {
                float blink = m_charging ? Mathf.Lerp(0.6f, 1.0f, GimmickPalette.Blink(m_chargeTimer, m_chargeBlinkRate)) : 0.0f;
                float light = Mathf.Max(charge * blink, recoil);
                float size = m_muzzleGlowSize * Mathf.Lerp(0.3f, 1.0f, light);
                m_muzzleGlow.transform.localScale = new Vector3(size, 1.0f, size);
                EffectMeshes.SetColor(m_muzzleGlow, GimmickPalette.WithAlpha(EffectMeshes.Brighten(m_chargeColor, recoil * 0.6f), light));
            }
        }

        // 当たり判定・ガイド線を合わせる(エディタから設定を変えたときにも呼ぶ)
        public void ApplyShape()
        {
            var col = GetComponent<CapsuleCollider>();
            if (col != null)
            {
                col.direction = 1;
                col.radius = m_radius;
                col.height = Mathf.Max(m_height, m_radius * 2.0f);
                col.center = new Vector3(0.0f, col.height * 0.5f, 0.0f);
            }
            if (m_guideDashes == null) return;
            // 点線は砲身と一緒に回る(m_turret の子)。床から少し浮かせる
            float z = m_radius + TumikiBlock.kRadius * 2.0f + kMuzzleGap;
            for (int i = 0; i < m_guideDashes.Length; i++)
            {
                var r = m_guideDashes[i];
                if (r == null) continue;
                float fade = 1.0f - (float)i / m_guideDashes.Length;
                r.transform.localPosition = new Vector3(0.0f, 0.02f, z + m_dashLength * 0.5f);
                r.transform.localScale = new Vector3(m_dashWidth, 1.0f, m_dashLength);
                EffectMeshes.SetColor(r, GimmickPalette.WithAlpha(m_guideColor, m_guideColor.a * fade));
                z += m_dashLength + m_dashGap;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = m_chargeColor;
            Vector3 dir = Application.isPlaying ? AimDirection : transform.forward;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.3f, transform.position + Vector3.up * 0.3f + dir * (m_radius + 3.0f));
            if (m_rotationMode == RotationMode.Swing)
            {
                Gizmos.color = new Color(1, 1, 1, 0.5f);
                foreach (float s in new[] { -1.0f, 1.0f })
                {
                    Vector3 d = Quaternion.Euler(0.0f, s * m_swingAngle, 0.0f) * transform.forward;
                    Gizmos.DrawLine(transform.position, transform.position + d * (m_radius + 2.0f));
                }
            }
        }
    }
}
