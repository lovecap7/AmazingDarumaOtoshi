using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // あかだるの必殺技(ハンマーの嵐。suehara)。あおだるの「六腕」をもとにしたもの
    // ・あかだるのハンマーのモデルを大きくした浮遊ハンマーを、体の周りで回す
    // ・ハンマー1本ずつにカプセルの当たり判定があり、触れた敵は通常の薙ぎ払いと同じく最下段を崩される(首だけなら脱落)
    // ・当たった積み木(落ちている・飛んでいる。自分の弾も)はすべて外へ弾き飛ばす。ハンマーとハンマーの間はすり抜ける
    // ・同じ敵は HitInterval 秒に1回だけ殴る
    // 当たり判定はホストだけ(ネット対戦のクライアントは見た目だけ)
    public class AkadaruHammerStorm : MonoBehaviour
    {
        const float kIntroTime = 0.25f;
        const float kOutroTime = 0.35f;
        // 同じ積み木を続けて弾かない間隔(秒。同じ一瞬に何本ものハンマーが当たって何度も弾かないように)
        const float kBlockRepeatGuard = 0.15f;
        // 頭(先端)を狙う高さ(m)。落ちている積み木の中心くらい
        const float kTipHeight = 0.35f;
        // 当たり判定を柄のどこから始めるか(ハンマーの長さに対する割合。体のすぐそばは判定なし)
        const float kHitStartRatio = 0.15f;
        // 柄を軸に頭をひねる角度(度)
        const float kHeadRoll = 90.0f;
        // 当たり判定の大きさ(大きくする前の、手持ちのハンマーでの値。m)
        //  柄の太さ(半径) / 頭(柄に直角な樽)の長さの半分と太さ(半径)
        const float kHandleRadius = 0.1f;
        const float kHeadHalfLength = 0.3f;
        const float kHeadRadius = 0.22f;
        // 1回の物理更新でハンマーの頭がこれ以上動くときは、途中の位置でも当たりを調べる(m。速く回してもすり抜けないように)
        const float kMaxSubstepMove = 0.2f;

        // 浮遊ハンマーの並び(衛星のように、等間隔に並んだまま全部が同じ向き・同じ速さで回る)
        const int kHammerCount = 5;
        // 支点が回る半径と高さ(m)
        const float kOrbitRadius = 1.05f;
        const float kPivotHeight = 0.68f;

        static readonly Color kTrailStart = new Color(1.0f, 0.55f, 0.25f, 0.9f);
        static readonly Color kTrailEnd = new Color(0.9f, 0.1f, 0.05f, 0.0f);

        DarumaCharacter m_character;
        float m_duration;
        float m_hitInterval;
        float m_knockSpeed;
        float m_orbitSpeed;  // 回る速さ(度/秒。上から見て時計回りが正)
        float m_scale;
        float m_armLength;   // 支点から頭の中心までの長さ(大きくした後)
        float m_start;
        Material m_trailMaterial;

        readonly List<Transform> m_pivots = new List<Transform>(5);
        readonly Collider[] m_overlaps = new Collider[64];
        readonly Dictionary<DarumaCharacter, float> m_lastCharacterHit = new Dictionary<DarumaCharacter, float>();
        readonly Dictionary<TumikiBlock, float> m_lastBlockHit = new Dictionary<TumikiBlock, float>();

        // 回るハンマーが届く距離(体の中心から水平に。CPU の撃ちどころ用)
        public static float ReachFor(DarumaCharacter character, float scale)
        {
            float arm = character != null && character.Hammer != null ? character.Hammer.ArmLength : 1.2f;
            return kOrbitRadius + arm * scale + kHeadRadius * scale;
        }

        public void Initialize(DarumaCharacter character, float duration, float hitInterval, float knockSpeed, float scale, float orbitSpeed)
        {
            m_character = character;
            m_duration = Mathf.Max(0.1f, duration);
            m_hitInterval = Mathf.Max(0.05f, hitInterval);
            m_knockSpeed = Mathf.Max(1.0f, knockSpeed);
            m_orbitSpeed = orbitSpeed;
            m_scale = Mathf.Max(0.1f, scale);
            m_start = Time.time;

            float arm = character.Hammer != null ? character.Hammer.ArmLength : 1.2f;
            m_armLength = arm * m_scale;

            transform.position = character.transform.position;
            transform.rotation = Quaternion.identity;

            var shader = Shader.Find("Sprites/Default");
            if (shader != null) m_trailMaterial = new Material(shader);
            BuildHammers();
        }

        // 手持ちのハンマーの見た目(モデル)を複製して、大きくした浮遊ハンマーを作る
        void BuildHammers()
        {
            Transform source = m_character.Hammer != null ? m_character.Hammer.transform : null;
            if (source == null) return;

            for (int i = 0; i < kHammerCount; i++)
            {
                var pivot = new GameObject($"StormHammer{i}").transform;
                pivot.SetParent(transform, false);
                pivot.localScale = Vector3.one * 0.01f;

                for (int c = 0; c < source.childCount; c++)
                {
                    Transform part = source.GetChild(c);
                    var clone = Instantiate(part.gameObject, pivot, false);
                    clone.name = part.name;
                    clone.transform.localPosition = part.localPosition * m_scale;
                    clone.transform.localRotation = part.localRotation;
                    clone.transform.localScale = part.localScale * m_scale;
                    // 見た目だけにする(物理で物を押したり、ほかの判定に拾われたりしないように)
                    foreach (var col in clone.GetComponentsInChildren<Collider>(true)) Destroy(col);
                    foreach (var rb in clone.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
                    // 無敵の点滅中に複製すると、表示が消えたまま写されるので必ず表示する
                    foreach (var r in clone.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                }

                if (m_trailMaterial != null)
                {
                    var trailObject = new GameObject("Trail");
                    trailObject.transform.SetParent(pivot, false);
                    trailObject.transform.localPosition = new Vector3(0.0f, 0.0f, m_armLength / m_scale);
                    var trail = trailObject.AddComponent<TrailRenderer>();
                    trail.sharedMaterial = m_trailMaterial;
                    trail.time = 0.25f;
                    trail.minVertexDistance = 0.05f;
                    trail.startWidth = 0.6f;
                    trail.endWidth = 0.0f;
                    trail.startColor = kTrailStart;
                    trail.endColor = kTrailEnd;
                    trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    trail.receiveShadows = false;
                }

                m_pivots.Add(pivot);
            }
        }

        float Elapsed => Time.time - m_start;
        float Total => kIntroTime + m_duration + kOutroTime;

        // i 本目のハンマーの、体の中心から見た支点の位置と向き(大きさは appear)
        void Pose(int i, float t, float appear, out Vector3 localPivot, out Quaternion localRotation)
        {
            float yaw = 360.0f * i / kHammerCount + m_orbitSpeed * t;
            float rad = yaw * Mathf.Deg2Rad;
            float radius = kOrbitRadius * (0.35f + 0.65f * Mathf.Clamp01(appear));
            float height = kPivotHeight;
            localPivot = new Vector3(Mathf.Sin(rad) * radius, height, Mathf.Cos(rad) * radius);
            // 外向きに伸ばし、頭が積み木の高さを通るよう少し下へ傾ける
            float drop = Mathf.Clamp((height - kTipHeight) / Mathf.Max(0.01f, m_armLength), -1.0f, 1.0f);
            float pitch = Mathf.Asin(drop) * Mathf.Rad2Deg;
            // 柄を軸に頭をひねり、叩く面を回る向きにそろえる(通常の薙ぎ払いと同じ。柄の向き=当たり判定は変わらない)
            localRotation = Quaternion.Euler(0.0f, yaw, 0.0f) * Quaternion.Euler(pitch, 0.0f, 0.0f) * Quaternion.Euler(0.0f, 0.0f, kHeadRoll);
        }

        // 0=出ていない, 1=出ている
        float Appear(float t)
        {
            if (t < kIntroTime)
            {
                float u = t / kIntroTime;
                return Mathf.Clamp(EaseOutBack(u), 0.0f, 1.15f);
            }
            if (t < kIntroTime + m_duration) return 1.0f;
            float v = Mathf.Clamp01((t - kIntroTime - m_duration) / kOutroTime);
            return 1.0f - v * v;
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1.0f;
            float x = t - 1.0f;
            return 1.0f + c3 * x * x * x + c1 * x * x;
        }

        void Update()
        {
            if (m_character == null || Elapsed >= Total)
            {
                Destroy(gameObject);
                return;
            }

            transform.position = m_character.transform.position;
            float t = Elapsed;
            float appear = Appear(t);
            for (int i = 0; i < m_pivots.Count; i++)
            {
                Pose(i, t, appear, out Vector3 p, out Quaternion r);
                m_pivots[i].localPosition = p;
                m_pivots[i].localRotation = r;
                m_pivots[i].localScale = Vector3.one * Mathf.Max(0.01f, appear);
            }
        }

        void FixedUpdate()
        {
            if (m_character == null || !m_character.CanBeAffected) return;
            // ネット対戦用(suehara): 当たり判定はホストだけ
            if (SpecialNet.IsClient) return;

            float t = Elapsed;
            // 消え始めたら当たらない
            if (t > kIntroTime + m_duration) return;
            Vector3 center = m_character.Rigidbody != null ? m_character.Rigidbody.position : m_character.transform.position;

            // 前の物理更新からの間に頭が大きく動いたときは、途中の位置でも調べる(速く回っても弾や敵をすり抜けないように)
            float dt = Time.fixedDeltaTime;
            float headMove = Mathf.Abs(m_orbitSpeed) * Mathf.Deg2Rad * (kOrbitRadius + m_armLength) * dt;
            int steps = Mathf.Clamp(Mathf.CeilToInt(headMove / kMaxSubstepMove), 1, 8);
            for (int s = 1; s <= steps; s++)
            {
                float ts = Mathf.Max(0.0f, t - dt * (1.0f - (float)s / steps));
                float appear = Mathf.Max(0.01f, Appear(ts));
                for (int i = 0; i < kHammerCount; i++) HitHammer(i, ts, appear, center);
            }
        }

        // i 本目のハンマーの当たり判定(柄と、柄に直角な頭の2つのカプセル)
        void HitHammer(int i, float t, float appear, Vector3 center)
        {
            Pose(i, t, appear, out Vector3 localPivot, out Quaternion localRotation);
            Vector3 pivot = center + localPivot;
            Vector3 dir = localRotation * Vector3.forward;
            // 頭は手持ちのハンマーでは縦(Y)向き。ひねったあとの向きがそのまま頭の向きになる
            Vector3 headAxis = localRotation * Vector3.up;
            float size = m_scale * appear;
            float arm = m_armLength * appear;
            Vector3 head = pivot + dir * arm;

            Overlap(pivot + dir * (arm * kHitStartRatio), head, kHandleRadius * size, center, dir);
            Vector3 half = headAxis * (kHeadHalfLength * size);
            Overlap(head - half, head + half, kHeadRadius * size, center, dir);
        }

        void Overlap(Vector3 from, Vector3 to, float radius, Vector3 center, Vector3 dir)
        {
            int count = Physics.OverlapCapsuleNonAlloc(from, to, radius, m_overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < count; k++) HitCollider(m_overlaps[k], center, dir);
        }

        void HitCollider(Collider col, Vector3 center, Vector3 hammerDir)
        {
            if (col == null) return;
            var block = col.GetComponentInParent<TumikiBlock>();
            if (block != null && block.CurrentState != TumikiBlock.State.Stacked)
            {
                HitBlock(block, center, hammerDir);
                return;
            }
            // 積まれている積み木はその持ち主のキャラクターとして扱う
            var other = col.GetComponentInParent<DarumaCharacter>();
            if (other != null) HitCharacter(other, center, hammerDir);
        }

        Vector3 Outward(Vector3 center, Vector3 target, Vector3 fallback)
        {
            Vector3 d = target - center;
            d.y = 0.0f;
            if (d.sqrMagnitude < 0.0001f) d = new Vector3(fallback.x, 0.0f, fallback.z);
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
        }

        void HitBlock(TumikiBlock block, Vector3 center, Vector3 hammerDir)
        {
            // 落ちている積み木も飛んでいる弾も、持ち主(自分の弾も)・向きに関係なく、当たったものはすべてはじく
            bool flying = block.CurrentState == TumikiBlock.State.Flying;
            if (m_lastBlockHit.TryGetValue(block, out float last) && Time.time - last < kBlockRepeatGuard) return;
            m_lastBlockHit[block] = Time.time;

            // 飛んでくる弾は、今の速さより遅くはしない
            float speed = flying ? Mathf.Max(block.Speed, m_knockSpeed) : SweepLaunchSpeed;
            Vector3 dir = Outward(center, block.transform.position, hammerDir);
            block.Launch(dir, speed, m_character);
            DarumaHammer.RaiseHit(new HammerHitInfo(m_character.Hammer, m_character, block, block.transform.position, false, 0));
        }

        void HitCharacter(DarumaCharacter other, Vector3 center, Vector3 hammerDir)
        {
            if (other == m_character || other.IsEliminated) return;
            DarumaMatch match = DarumaMatch.Current;
            if (match != null && match.IsFriendly(m_character, other)) return;
            if (m_lastCharacterHit.TryGetValue(other, out float last) && Time.time - last < m_hitInterval) return;
            m_lastCharacterHit[other] = Time.time;

            Vector3 dir = Outward(center, other.transform.position, hammerDir);
            bool damaged = other.KnockBottom(dir, SweepLaunchSpeed, m_character, out _);
            if (damaged)
            {
                m_character.OnDealtDamage();
                other.StartHitStop(0.08f, 0.1f, 0.1f);
            }
            Vector3 point = other.transform.position + Vector3.up * 0.25f;
            DarumaHammer.RaiseHit(new HammerHitInfo(m_character.Hammer, m_character, other, point, damaged, 0));
        }

        float SweepLaunchSpeed => m_character.Hammer != null && m_character.Hammer.Params != null
            ? m_character.Hammer.Params.SweepLaunchSpeed
            : 30.0f;

        void OnDestroy()
        {
            if (m_trailMaterial != null) Destroy(m_trailMaterial);
        }
    }
}
