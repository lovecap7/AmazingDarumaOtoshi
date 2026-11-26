using Nakahira;
using UnityEngine;

namespace Yurube
{
    // みだるの必殺技「マシンガン」で置く固定砲台(suehara)
    // ・置いた場所から動かない。壊れない(当たり判定がなく、物も人もすり抜ける)
    // ・砲身は一番近い敵へ自動で向き、TurnSpeed(度/秒)までの速さで回って、その向きへ弾(MidaruBullet)を一定の間隔で撃つ
    //   (ダッシュしている相手に、近くではギリギリ追いつけない速さ。歩いている相手には追いつく)。敵がいなければ今の向きのまま撃つ
    // ・弾は積み木ではないので打ち返せない。積み木は弾き飛ばす
    // ・みだるは置いたあと自由に動ける(砲台は勝手に撃ち続ける)
    // ・みだるがやられても、時間まで撃ち続ける
    // 撃つのはホストだけ(ネット対戦のクライアントは砲台の見た目だけ。弾は届かない)
    public class MidaruMachineGun : MonoBehaviour
    {
        const float kIntroTime = 0.25f;
        const float kOutroTime = 0.3f;
        // 砲口までの距離(砲台の中心から。m)
        const float kMuzzleDistance = 0.9f;
        // 砲台を置く位置(みだるの中心から前へ。m。台座の半径 + 体の半径)
        public const float HoldDistance = 0.95f;
        // 狙う敵を選び直す間隔(秒)
        const float kRetargetInterval = 0.1f;
        static readonly Color kBodyColor = new Color(0.25f, 0.75f, 0.35f);
        static readonly Color kBarrelColor = new Color(0.15f, 0.2f, 0.18f);

        DarumaCharacter m_owner;
        float m_duration;
        float m_interval;
        float m_speed;
        float m_range;
        float m_turnSpeed;
        float m_start;
        float m_fireTimer;
        Vector3 m_direction;
        Transform m_turret;
        // 今狙っている敵
        DarumaCharacter m_target;
        float m_retargetTimer;

        public bool IsFiring => Elapsed < kIntroTime + m_duration;
        float Elapsed => Time.time - m_start;

        // turnSpeed: 砲身が回る速さの上限(度/秒) / range: 弾が届く距離(m。0 以下なら無限)
        public static MidaruMachineGun Place(DarumaCharacter owner, Vector3 position, Vector3 direction, float duration,
            float shotsPerSecond, float speed, float range, float turnSpeed)
        {
            var go = new GameObject("MidaruMachineGun");
            go.transform.position = position;
            var gun = go.AddComponent<MidaruMachineGun>();
            gun.m_owner = owner;
            gun.m_duration = Mathf.Max(0.1f, duration);
            gun.m_interval = 1.0f / Mathf.Max(0.1f, shotsPerSecond);
            gun.m_speed = Mathf.Max(1.0f, speed);
            gun.m_range = range > 0.0f ? Mathf.Max(0.5f, range) : 0.0f;   // 0 = 射程無限
            gun.m_turnSpeed = Mathf.Max(1.0f, turnSpeed);
            gun.m_start = Time.time;
            direction.y = 0.0f;
            gun.m_direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            gun.BuildVisual();
            return gun;
        }

        // 台座・胴・砲身を基本形状で作る(当たり判定は外す)
        void BuildVisual()
        {
            var mpbBody = new MaterialPropertyBlock();
            mpbBody.SetColor("_BaseColor", kBodyColor);
            var mpbBarrel = new MaterialPropertyBlock();
            mpbBarrel.SetColor("_BaseColor", kBarrelColor);

            Part(PrimitiveType.Cylinder, transform, new Vector3(0.0f, 0.1f, 0.0f), Quaternion.identity, new Vector3(0.9f, 0.1f, 0.9f), mpbBarrel);
            m_turret = new GameObject("Turret").transform;
            m_turret.SetParent(transform, false);
            m_turret.localPosition = new Vector3(0.0f, TumikiBlock.kHeight * 0.5f + 0.05f, 0.0f);
            Part(PrimitiveType.Sphere, m_turret, Vector3.zero, Quaternion.identity, new Vector3(0.65f, 0.5f, 0.65f), mpbBody);
            // 砲身(前 = +Z に伸ばす)
            Part(PrimitiveType.Cylinder, m_turret, new Vector3(0.0f, 0.0f, 0.5f), Quaternion.Euler(90.0f, 0.0f, 0.0f), new Vector3(0.22f, 0.3f, 0.22f), mpbBarrel);
            Part(PrimitiveType.Cylinder, m_turret, new Vector3(0.12f, 0.0f, 0.45f), Quaternion.Euler(90.0f, 0.0f, 0.0f), new Vector3(0.12f, 0.27f, 0.12f), mpbBarrel);
            Part(PrimitiveType.Cylinder, m_turret, new Vector3(-0.12f, 0.0f, 0.45f), Quaternion.Euler(90.0f, 0.0f, 0.0f), new Vector3(0.12f, 0.27f, 0.12f), mpbBarrel);
            m_turret.rotation = Quaternion.LookRotation(m_direction, Vector3.up);
            transform.localScale = Vector3.one * 0.01f;
        }

        static void Part(PrimitiveType type, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, MaterialPropertyBlock mpb)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.SetPropertyBlock(mpb);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        void Update()
        {
            float t = Elapsed;
            if (t >= kIntroTime + m_duration + kOutroTime)
            {
                Destroy(gameObject);
                return;
            }
            // 出るときは膨らみ、消えるときは縮む
            float scale = t < kIntroTime ? t / kIntroTime
                : t < kIntroTime + m_duration ? 1.0f
                : 1.0f - (t - kIntroTime - m_duration) / kOutroTime;
            transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            if (m_turret != null) m_turret.rotation = Quaternion.LookRotation(m_direction, Vector3.up);
        }

        // 一番近い敵の方へ、回る速さの上限までで向ける(敵がいなければ今の向きのまま)
        void UpdateDirection(float dt)
        {
            m_retargetTimer -= dt;
            if (m_retargetTimer <= 0.0f || !IsTargetable(m_target))
            {
                m_retargetTimer = kRetargetInterval;
                m_target = FindNearestEnemy();
            }
            if (m_target == null) return;
            Vector3 to = m_target.transform.position - transform.position;
            to.y = 0.0f;
            if (to.sqrMagnitude < 0.0001f) return;
            m_direction = Vector3.RotateTowards(m_direction, to.normalized, m_turnSpeed * Mathf.Deg2Rad * dt, 0.0f);
            m_direction.y = 0.0f;
            m_direction.Normalize();
        }

        // 狙える敵か(みだる本人・味方・脱落した人は狙わない)
        bool IsTargetable(DarumaCharacter c)
        {
            if (c == null || c == m_owner || c.IsEliminated || !c.gameObject.activeInHierarchy) return false;
            if (m_owner != null && DarumaMatch.AreFriends(m_owner, c)) return false;
            return true;
        }

        DarumaCharacter FindNearestEnemy()
        {
            DarumaCharacter best = null;
            float bestSqr = float.MaxValue;
            foreach (var c in FindObjectsByType<DarumaCharacter>(FindObjectsInactive.Exclude))
            {
                if (!IsTargetable(c)) continue;
                Vector3 d = c.transform.position - transform.position;
                d.y = 0.0f;
                float sqr = d.sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = c; }
            }
            return best;
        }

        void FixedUpdate()
        {
            if (!IsFiring) return;
            UpdateDirection(Time.fixedDeltaTime);
            if (SpecialNet.IsClient) return;   // ネット対戦用(suehara): 撃つのはホストだけ
            if (Elapsed < kIntroTime) return;

            m_fireTimer -= Time.fixedDeltaTime;
            if (m_fireTimer > 0.0f) return;
            m_fireTimer += m_interval;
            Fire();
        }

        void Fire()
        {
            Vector3 muzzle = transform.position + Vector3.up * (TumikiBlock.kHeight * 0.5f + 0.05f) + m_direction * kMuzzleDistance;
            DarumaCharacter owner = m_owner != null && !m_owner.IsEliminated ? m_owner : null;
            MidaruBullet.Fire(owner, muzzle, m_direction, m_speed, m_range);
        }
    }
}
