using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // あおだるの必殺技「疾風連撃」で、薙ぎ払うたびに前へ飛ぶ斬撃(suehara)
    // ・地面すれすれ(足元から Height まで)を、まっすぐ一定の速さで進む。ジャンプで足元が Height より上にあれば当たらない
    // ・当たったキャラクターは、薙ぎ払いと同じく最下段を弾かれる(首だけなら脱落)。1つの斬撃は同じ相手に1回だけ
    // ・当たった積み木(落ちている・飛んでいる。自分の弾も)は、すべて斬撃の進む向きへ弾き飛ばす。1つの斬撃は同じ積み木に1回だけ
    // ・壁(ReflectWall)に当たるか、Range 進むと消える
    // 当たり判定はホストだけ(ネット対戦のクライアントには届かない)
    public class AodaruGaleSlash : MonoBehaviour
    {
        const float kFadeTime = 0.15f;
        static readonly Color kColor = new Color(0.55f, 0.95f, 1.0f, 0.9f);
        static Material s_material;

        DarumaCharacter m_owner;
        Vector3 m_direction;
        float m_speed;
        float m_range;
        float m_width;
        float m_height;
        float m_depth;
        float m_traveled;
        float m_fade = -1.0f;   // 0以上なら消えている最中
        LineRenderer m_line;
        readonly HashSet<DarumaCharacter> m_hit = new HashSet<DarumaCharacter>();
        readonly HashSet<TumikiBlock> m_hitBlocks = new HashSet<TumikiBlock>();
        readonly Collider[] m_overlaps = new Collider[32];

        // from: 出す位置(足元の高さ) / direction: 進む向き
        public static AodaruGaleSlash Spawn(DarumaCharacter owner, Vector3 from, Vector3 direction,
            float speed, float range, float width, float height, float depth)
        {
            direction.y = 0.0f;
            if (direction.sqrMagnitude < 0.0001f) return null;
            var go = new GameObject("AodaruGaleSlash");
            var slash = go.AddComponent<AodaruGaleSlash>();
            slash.m_owner = owner;
            slash.m_direction = direction.normalized;
            slash.m_speed = Mathf.Max(0.1f, speed);
            slash.m_range = Mathf.Max(0.1f, range);
            slash.m_width = Mathf.Max(0.1f, width);
            slash.m_height = Mathf.Max(0.05f, height);
            slash.m_depth = Mathf.Max(0.05f, depth);
            go.transform.position = from;
            go.transform.rotation = Quaternion.LookRotation(slash.m_direction, Vector3.up);
            slash.BuildVisual();
            return slash;
        }

        void BuildVisual()
        {
            if (s_material == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) s_material = new Material(shader);
            }
            if (s_material == null) return;

            // 三日月の形の光の帯。当たる高さのいちばん上に、地面と平行に寝かせて置く(どの高さを越えればよいか見えるように)
            var visual = new GameObject("Visual");
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = Vector3.up * m_height;
            visual.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            m_line = visual.AddComponent<LineRenderer>();
            m_line.sharedMaterial = s_material;
            m_line.useWorldSpace = false;
            m_line.alignment = LineAlignment.TransformZ;
            m_line.numCapVertices = 6;
            m_line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_line.receiveShadows = false;

            const int points = 11;
            m_line.positionCount = points;
            float half = m_width * 0.5f;
            for (int i = 0; i < points; i++)
            {
                float u = (float)i / (points - 1) * 2.0f - 1.0f;   // -1〜1
                // 中央が前に出た三日月(寝かせたローカルでは y が前)
                float forward = (1.0f - u * u) * m_depth;
                m_line.SetPosition(i, new Vector3(u * half, forward, 0.0f));
            }
            var widths = new AnimationCurve(new Keyframe(0.0f, 0.05f), new Keyframe(0.5f, 1.0f), new Keyframe(1.0f, 0.05f));
            m_line.widthCurve = widths;
            m_line.widthMultiplier = m_depth + 0.15f;
            ApplyColor(1.0f);
        }

        void ApplyColor(float alpha)
        {
            if (m_line == null) return;
            Color c = kColor;
            c.a *= alpha;
            m_line.startColor = c;
            m_line.endColor = c;
        }

        void Update()
        {
            if (m_fade < 0.0f) return;
            m_fade += Time.deltaTime;
            ApplyColor(1.0f - Mathf.Clamp01(m_fade / kFadeTime));
            if (m_fade >= kFadeTime) Destroy(gameObject);
        }

        void FixedUpdate()
        {
            if (m_fade >= 0.0f) return;
            float step = m_speed * Time.fixedDeltaTime;
            Vector3 from = transform.position;
            Vector3 to = from + m_direction * step;

            // 進む間に壁があれば、そこで消える
            Vector3 boxCenter = from + Vector3.up * (m_height * 0.5f);
            Vector3 boxHalf = new Vector3(m_width * 0.5f, m_height * 0.5f, m_depth * 0.5f);
            if (Physics.BoxCast(boxCenter, boxHalf * 0.9f, m_direction, out RaycastHit wall, transform.rotation, step, ~0, QueryTriggerInteraction.Ignore)
                && wall.collider.GetComponentInParent<ReflectWall>() != null)
            {
                transform.position = from + m_direction * wall.distance;
                m_fade = 0.0f;
                return;
            }

            transform.position = to;
            m_traveled += step;
            // ネット対戦用(suehara): 当たり判定はホストだけ
            if (!SpecialNet.IsClient) HitTargets(from, to);
            if (m_traveled >= m_range) m_fade = 0.0f;
        }

        // このステップで通った範囲(前の位置から今の位置まで)にいるキャラクター・積み木に当てる
        void HitTargets(Vector3 from, Vector3 to)
        {
            if (m_owner == null) return;
            float length = Vector3.Distance(from, to) + m_depth;
            Vector3 center = (from + to) * 0.5f + Vector3.up * (m_height * 0.5f);
            Vector3 half = new Vector3(m_width * 0.5f, m_height * 0.5f, length * 0.5f);
            int count = Physics.OverlapBoxNonAlloc(center, half, m_overlaps, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                // 積まれていない積み木は、持ち主・状態に関係なく弾く(積まれている積み木は、その持ち主のキャラクターとして扱う)
                var block = m_overlaps[i].GetComponentInParent<TumikiBlock>();
                if (block != null && block.CurrentState != TumikiBlock.State.Stacked)
                {
                    HitBlock(block);
                    continue;
                }

                var other = m_overlaps[i].GetComponentInParent<DarumaCharacter>();
                if (other == null || other == m_owner || other.IsEliminated || m_hit.Contains(other)) continue;
                DarumaMatch match = DarumaMatch.Current;
                if (match != null && match.IsFriendly(m_owner, other)) continue;
                m_hit.Add(other);

                bool damaged = other.KnockBottom(m_direction, LaunchSpeed, m_owner, out _);
                if (damaged)
                {
                    m_owner.OnDealtDamage();
                    other.StartHitStop(0.08f, 0.1f, 0.1f);
                }
                Vector3 point = other.transform.position + Vector3.up * 0.25f;
                DarumaHammer.RaiseHit(new HammerHitInfo(m_owner.Hammer, m_owner, other, point, damaged, 0));
            }
        }

        // 積み木を斬撃の進む向きへ弾く。飛んでいる弾は、今の速さより遅くはしない
        void HitBlock(TumikiBlock block)
        {
            if (!m_hitBlocks.Add(block)) return;
            bool flying = block.CurrentState == TumikiBlock.State.Flying;
            float speed = flying ? Mathf.Max(block.Speed, LaunchSpeed) : LaunchSpeed;
            block.Launch(m_direction, speed, m_owner);
            DarumaHammer.RaiseHit(new HammerHitInfo(m_owner.Hammer, m_owner, block, block.transform.position, false, 0));
        }

        // 弾き飛ばす速さ(持ち主のハンマーの、薙ぎ払いで飛ばす速さ)
        float LaunchSpeed => m_owner.Hammer != null && m_owner.Hammer.Params != null ? m_owner.Hammer.Params.SweepLaunchSpeed : 30.0f;
    }
}
