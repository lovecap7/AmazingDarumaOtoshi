using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // あかだるの必殺技「トロン」の当たり判定。
    // 表示中の TrailRenderer の全点を読み、折れ曲がり(壁反射)も含めて
    // 短い判定ボックスを何個も並べる。エフェクトが消えたら判定も消える。
    public class TronProjectile : MonoBehaviour
    {
        const int MaxSegments = 24;
        const float MaxSegmentLength = 3.0f;
        const float CornerAngle = 12.0f;

        class Segment
        {
            public GameObject gameObject;
            public BoxCollider box;
        }

        TumikiBlock m_block;
        DarumaCharacter m_owner;
        AkadaruShotTrail m_shotTrail;
        GameObject m_triggerRoot;
        Segment[] m_segments;
        readonly Vector3[] m_positions = new Vector3[2048];
        readonly List<Vector3> m_anchors = new List<Vector3>(MaxSegments + 2);
        readonly Dictionary<DarumaCharacter, float> m_lastHitTimes = new Dictionary<DarumaCharacter, float>();
        float m_duration = 1.2f;
        float m_elapsed;
        float m_ownerGraceUntil;
        float m_hitInterval = 0.4f;
        bool m_finished;
        bool m_log;

        private void Awake()
        {
            m_block = GetComponent<TumikiBlock>();
        }

        public void Begin(DarumaCharacter owner, float duration, float ownerGrace, float hitInterval, bool log)
        {
            m_owner = owner;
            m_duration = Mathf.Max(0.1f, duration);
            m_ownerGraceUntil = Time.time + Mathf.Max(0.0f, ownerGrace);
            m_hitInterval = Mathf.Max(0.05f, hitInterval);
            m_log = log;
            m_elapsed = 0.0f;
            m_finished = false;
            CreateTrigger();
        }

        void CreateTrigger()
        {
            m_triggerRoot = new GameObject("TronHitbox");
            var body = m_triggerRoot.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // 複合コライダーのトリガーイベントは Rigidbody 側にも届くため、ここでも受ける
            var rootHit = m_triggerRoot.AddComponent<TronTrailHit>();
            rootHit.projectile = this;

            m_segments = new Segment[MaxSegments];
            for (int i = 0; i < MaxSegments; i++)
            {
                var go = new GameObject($"TronHitboxSegment{i}");
                go.transform.SetParent(m_triggerRoot.transform, false);

                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(1.0f, 1.0f, 1.0f);
                box.center = Vector3.zero;
                box.enabled = false;

                var hit = go.AddComponent<TronTrailHit>();
                hit.projectile = this;

                var view = go.AddComponent<TronHitboxView>();
                view.Initialize(box);

                go.SetActive(false);
                m_segments[i] = new Segment { gameObject = go, box = box };
            }
        }

        private void FixedUpdate()
        {
            if (m_finished) return;

            m_elapsed += Time.fixedDeltaTime;
            if (m_elapsed >= m_duration)
            {
                Cleanup();
                return;
            }

            if (m_shotTrail == null) m_shotTrail = GetComponent<AkadaruShotTrail>();
            UpdateSegments();
        }

        void UpdateSegments()
        {
            if (m_segments == null) return;

            if (m_shotTrail == null)
            {
                DisableSegments(0);
                return;
            }

            int count = m_shotTrail.CopyTrailPositions(m_positions);
            float width = m_shotTrail.TrailWidth;
            if (count < 2 || width <= 0.0f)
            {
                DisableSegments(0);
                return;
            }

            BuildAnchors(count);

            int used = 0;
            for (int i = 0; i + 1 < m_anchors.Count && used < MaxSegments; i++)
            {
                Vector3 a = m_anchors[i];
                Vector3 b = m_anchors[i + 1];
                Vector3 delta = b - a;
                float length = delta.magnitude;
                if (length < 0.05f) continue;

                var segment = m_segments[used];
                if (segment.gameObject == null) continue;
                if (!segment.gameObject.activeSelf) segment.gameObject.SetActive(true);

                segment.box.enabled = true;
                segment.gameObject.transform.SetPositionAndRotation(
                    (a + b) * 0.5f,
                    Quaternion.LookRotation(delta / length, Vector3.up));
                segment.box.size = new Vector3(width, width, length);
                segment.box.center = Vector3.zero;
                used++;
            }

            DisableSegments(used);
        }

        // トレイルの点を、屈折点と一定間隔で間引いて折れ線にする
        void BuildAnchors(int count)
        {
            m_anchors.Clear();
            m_anchors.Add(m_positions[0]);
            Vector3 lastAnchor = m_positions[0];

            for (int i = 1; i < count - 1; i++)
            {
                Vector3 prev = m_positions[i] - m_positions[i - 1];
                Vector3 next = m_positions[i + 1] - m_positions[i];
                if (prev.sqrMagnitude < 0.000001f || next.sqrMagnitude < 0.000001f) continue;

                bool corner = Vector3.Angle(prev, next) >= CornerAngle;
                bool far = Vector3.Distance(lastAnchor, m_positions[i]) >= MaxSegmentLength;
                if (corner || far)
                {
                    m_anchors.Add(m_positions[i]);
                    lastAnchor = m_positions[i];
                }

                if (m_anchors.Count >= MaxSegments) break;
            }

            m_anchors.Add(m_positions[count - 1]);
        }

        void DisableSegments(int from)
        {
            if (m_segments == null) return;
            for (int i = from; i < m_segments.Length; i++)
            {
                var segment = m_segments[i];
                if (segment == null) continue;
                if (segment.box != null) segment.box.enabled = false;
                if (segment.gameObject != null && segment.gameObject.activeSelf) segment.gameObject.SetActive(false);
            }
        }

        // 残像のトリガーから呼ばれる
        public void OnTrailHit(Collider other)
        {
            if (m_finished || other == null) return;

            // 積み木に触れたら、その積み木を壊す(必殺技の積み木自身は除く)
            var block = other.GetComponentInParent<TumikiBlock>();
            if (block != null)
            {
                if (block != m_block) BreakBlock(block);
                return;
            }

            // キャラクターに触れたら、通常の積み木と同じく積み木を1個失わせる
            var target = other.GetComponentInParent<DarumaCharacter>();
            if (target == null) return;

            // 発動直後は自分自身を守る
            if (target == m_owner && Time.time < m_ownerGraceUntil) return;

            if (target.IsInvincible || !target.CanBeAffected) return;

            // 同じ相手でも、一定間隔を空ければ何度でも当たる
            if (m_lastHitTimes.TryGetValue(target, out float last) && Time.time - last < m_hitInterval) return;
            m_lastHitTimes[target] = Time.time;
            HitCharacter(target);
        }

        void HitCharacter(DarumaCharacter target)
        {
            if (target == null || target.Stack == null) return;

            // 積み木を持っていなければ即死、あれば通常の飛び道具と同じく1個壊す
            if (target.Stack.Count == 0)
            {
                if (m_log) Debug.Log("[あかだる] トロン命中 → 積み木なしで即死", this);
                target.Eliminate(EliminationCause.Projectile);
                return;
            }

            if (m_log) Debug.Log("[あかだる] トロン命中 → 積み木を1個破壊", this);
            TumikiBlock block = target.Stack.RemoveAt(0);
            if (block != null) block.Break(Vector3.zero, 0.0f, 0.0f);
        }

        void BreakBlock(TumikiBlock block)
        {
            if (block == null || block == m_block) return;

            // 積まれている積み木なら、先にスタックから外す
            var stack = block.GetComponentInParent<DarumaStack>();
            if (stack != null)
            {
                for (int i = 0; i < stack.Count; i++)
                {
                    if (stack.Get(i) == block)
                    {
                        stack.RemoveAt(i);
                        break;
                    }
                }
            }

            Vector3 velocity = m_block != null ? m_block.Direction * 3.0f : Vector3.zero;
            block.Break(velocity, 0.0f, 0.0f);
        }

        void Cleanup()
        {
            m_finished = true;
            if (m_triggerRoot != null) Destroy(m_triggerRoot);
            m_triggerRoot = null;

            if (m_block != null) Destroy(m_block.gameObject);
            else Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (m_triggerRoot != null) Destroy(m_triggerRoot);
        }
    }
}
