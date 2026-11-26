using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // みだるの必殺技「マシンガン」の弾(suehara)。積み木ではないので、ハンマーなどで打ち返せない
    // ・まっすぐ一定の速さで進む
    // ・敵に当たると最下段を1段崩して消える(首だけなら脱落)。味方・幽霊・撃った本人はすり抜ける
    //   みだるのパッシブ「当てた相手を遅くする」も、レベルに関係なくかかる(Lv1 でも Lv2 の強さ)
    // ・積み木(落ちている・飛んでいる。だれの弾でも)に当たると、弾の進む向きへ弾き飛ばして、そのまま進む
    // ・壁・ギミックなど、ほかの物に当たると消える。射程は無限(range が 0 以下のとき。念のため kMaxTravel 進んだら消す)
    // 当たり判定はホストだけ(ネット対戦のクライアントには弾が届かない)
    public class MidaruBullet : MonoBehaviour
    {
        // 見やすいように大きめ(見た目と当たり判定は同じ大きさ。砲口の高さ 0.3m なので床をこすらない大きさ)
        const float kRadius = 0.28f;
        // 射程無限のときも、ステージの外へ飛んでいった弾が残り続けないように、この距離で消す(m)
        const float kMaxTravel = 300.0f;
        static readonly Color kColor = new Color(1.0f, 0.95f, 0.45f);
        static readonly Color kTrailColor = new Color(1.0f, 0.75f, 0.25f, 0.8f);
        static Material s_trailMaterial;
        static readonly RaycastHit[] s_hits = new RaycastHit[32];

        DarumaCharacter m_owner;
        Vector3 m_direction;
        float m_speed;
        float m_range;
        float m_traveled;
        readonly HashSet<TumikiBlock> m_hitBlocks = new HashSet<TumikiBlock>();

        public static MidaruBullet Fire(DarumaCharacter owner, Vector3 position, Vector3 direction, float speed, float range)
        {
            direction.y = 0.0f;
            if (direction.sqrMagnitude < 0.0001f) return null;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "MidaruBullet";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * (kRadius * 2.0f);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", kColor);
                r.SetPropertyBlock(mpb);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            AddTrail(go);

            var bullet = go.AddComponent<MidaruBullet>();
            bullet.m_owner = owner;
            bullet.m_direction = direction.normalized;
            bullet.m_speed = Mathf.Max(1.0f, speed);
            bullet.m_range = range > 0.0f ? Mathf.Max(0.5f, range) : kMaxTravel;
            return bullet;
        }

        static void AddTrail(GameObject go)
        {
            if (s_trailMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) return;
                s_trailMaterial = new Material(shader);
            }
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = s_trailMaterial;
            trail.time = 0.12f;
            trail.startWidth = kRadius * 1.6f;
            trail.endWidth = 0.0f;
            trail.startColor = kTrailColor;
            Color end = kTrailColor;
            end.a = 0.0f;
            trail.endColor = end;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
        }

        void FixedUpdate()
        {
            float step = m_speed * Time.fixedDeltaTime;
            Vector3 from = transform.position;

            // ネット対戦用(suehara): 当たり判定はホストだけ(クライアントは進むだけ)
            if (!SpecialNet.IsClient && CheckHits(from, step)) return;

            transform.position = from + m_direction * step;
            m_traveled += step;
            if (m_traveled >= m_range) Destroy(gameObject);
        }

        // このステップで通る間に当たった物を、近い順に処理する。弾が消えたら true
        bool CheckHits(Vector3 from, float step)
        {
            int count = Physics.SphereCastNonAlloc(from, kRadius, m_direction, s_hits, step, ~0, QueryTriggerInteraction.Ignore);
            if (count <= 0) return false;
            System.Array.Sort(s_hits, 0, count, HitDistanceComparer.Instance);

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = s_hits[i];
                Collider col = hit.collider;
                if (col == null) continue;
                if (m_owner != null && col.transform.IsChildOf(m_owner.transform)) continue;

                var block = col.GetComponentInParent<TumikiBlock>();
                if (block != null && block.CurrentState != TumikiBlock.State.Stacked)
                {
                    KnockBlock(block);
                    continue;
                }

                var character = col.GetComponentInParent<DarumaCharacter>();
                if (character != null)
                {
                    if (character == m_owner || character.IsEliminated) continue;
                    DarumaMatch match = DarumaMatch.Current;
                    if (m_owner != null && match != null && match.IsFriendly(m_owner, character)) continue;
                    HitCharacter(character);
                    Destroy(gameObject);
                    return true;
                }
                if (col.GetComponentInParent<DarumaGhost>() != null) continue;

                // 撃ち出した瞬間に足元の床に触れているだけなら無視する
                if (hit.distance <= 0.0f && hit.normal.y > 0.5f) continue;
                // 壁・ギミックなど
                transform.position = hit.distance > 0.0f ? hit.point : from;
                Destroy(gameObject);
                return true;
            }
            return false;
        }

        float LaunchSpeed => m_owner != null && m_owner.Hammer != null && m_owner.Hammer.Params != null
            ? m_owner.Hammer.Params.SweepLaunchSpeed
            : 30.0f;

        // 積み木を弾の進む向きへ弾く(1つの弾で同じ積み木は1回だけ)。飛んでいる弾は今の速さより遅くしない
        void KnockBlock(TumikiBlock block)
        {
            if (!m_hitBlocks.Add(block)) return;
            bool flying = block.CurrentState == TumikiBlock.State.Flying;
            float speed = flying ? Mathf.Max(block.Speed, LaunchSpeed) : LaunchSpeed;
            block.Launch(m_direction, speed, m_owner);
            if (m_owner != null)
                DarumaHammer.RaiseHit(new HammerHitInfo(m_owner.Hammer, m_owner, block, block.transform.position, false, 0));
        }

        void HitCharacter(DarumaCharacter character)
        {
            bool damaged = character.KnockBottom(m_direction, LaunchSpeed, m_owner, out _);
            if (damaged)
            {
                character.StartHitStop(0.06f, 0.1f, 0.08f);
                if (m_owner != null) m_owner.OnDealtDamage();
            }
            // みだるのパッシブ「当てた相手を遅くする」を、レベルに関係なく(Lv1 でも Lv2 の強さで)かける
            if (m_owner != null) DarumaLevel.ApplySlowAtLeastOneStep(m_owner, character);
            // 薙ぎ払いと同じ知らせを出す(揺れ・音・みだるのパッシブ「当てた相手を遅くする」が働く)
            if (m_owner != null)
            {
                Vector3 point = character.transform.position + Vector3.up * 0.25f;
                DarumaHammer.RaiseHit(new HammerHitInfo(m_owner.Hammer, m_owner, character, point, damaged, 0));
            }
        }

        sealed class HitDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new HitDistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
