using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // 幽霊の体の動き(見た目だけ)。ゆっくり上下に揺れて浮いている感じを出す。ハンマーも一緒に揺れる
    // 見た目の子(頭・しっぽなど。Visual の直下)とハンマーの支点(DarumaHammer.BodyMotion)を同じだけ上下させる
    // Visual そのものはヒットストップの震え(HitStop)と出現の演出の大きさ(GhostAppearEffect)が使っているので動かさない
    // DarumaGhost が Awake で自分に付ける。調整値は GhostParams の「浮いている揺れ」
    // ネット対戦のクライアント(DarumaGhost が止まっている)では動かさない
    [DisallowMultipleComponent]
    public class DarumaGhostMotion : MonoBehaviour, IDarumaBodyMotion
    {
        struct Part
        {
            public Transform Transform;
            public Vector3 Rest;
            public Vector3 Written;
        }

        DarumaGhost m_ghost;
        readonly List<Part> m_parts = new List<Part>();
        float m_time;
        float m_offset;

        private void Awake()
        {
            m_ghost = GetComponent<DarumaGhost>();
            // 幽霊どうしで揺れがそろわないように、始まりをずらす
            m_time = Random.value * 10.0f;
        }

        private void Start()
        {
            if (m_ghost == null || m_ghost.Visual == null) return;
            Transform hammer = m_ghost.Hammer != null ? m_ghost.Hammer.transform : null;
            foreach (Transform child in m_ghost.Visual)
            {
                if (child == hammer) continue;
                m_parts.Add(new Part { Transform = child, Rest = child.localPosition, Written = child.localPosition });
            }
        }

        private void OnEnable()
        {
            if (m_ghost != null && m_ghost.Hammer != null) m_ghost.Hammer.BodyMotion = this;
        }

        private void OnDisable()
        {
            if (m_ghost != null && m_ghost.Hammer != null && ReferenceEquals(m_ghost.Hammer.BodyMotion, this)) m_ghost.Hammer.BodyMotion = null;
            m_offset = 0.0f;
            Apply();
        }

        private void Update()
        {
            GhostParams p = m_ghost != null ? m_ghost.Params : null;
            // ネット対戦のクライアントでは DarumaGhost が止まっている
            if (p == null || !m_ghost.enabled || p.FloatAmplitude <= 0.0f)
            {
                m_offset = 0.0f;
                return;
            }
            // ヒットストップ中はスロー、ポーズ中は止まる
            m_time += Time.deltaTime * m_ghost.LocalTimeScale;
            m_offset = p.FloatAmplitude * Mathf.Sin(m_time * 2.0f * Mathf.PI / p.FloatPeriod);
        }

        private void LateUpdate()
        {
            Apply();
        }

        // 見た目の子を、本来の位置 + 揺れに置く(外から位置を変えられていたら、そこを本来の位置にする)
        private void Apply()
        {
            Vector3 offset = new Vector3(0.0f, m_offset, 0.0f);
            for (int i = 0; i < m_parts.Count; i++)
            {
                Part part = m_parts[i];
                if (part.Transform == null) continue;
                if (part.Transform.localPosition != part.Written) part.Rest = part.Transform.localPosition - (part.Written - part.Rest);
                part.Transform.localPosition = part.Rest + offset;
                part.Written = part.Transform.localPosition;
                m_parts[i] = part;
            }
        }

        // ハンマーの支点(IDarumaBodyMotion)
        public Vector3 GetOffset(float height)
        {
            return new Vector3(0.0f, m_offset, 0.0f);
        }

        // 幽霊は傾けない
        public Quaternion GetTilt(float height) => Quaternion.identity;
    }
}
