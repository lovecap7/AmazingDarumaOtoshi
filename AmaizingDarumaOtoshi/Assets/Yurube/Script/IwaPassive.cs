using Nakahira;
using UnityEngine;

namespace Yurube
{
    // いわだるのパッシブ。自分が飛ばした積み木を「岩の積み木」にする。
    // 積み木が飛び始めた直後はキャラクターのすぐ前にいるので、近くの積み木を拾って
    // IwaBlock を付ける(積み木側のスクリプトやハンマーには手を入れない)。
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(DarumaCharacter))]
    public class IwaPassive : MonoBehaviour
    {
        [Tooltip("岩化する積み木を探す範囲(m)")]
        [SerializeField] float m_searchRadius = 3.0f;
        [Tooltip("岩化・壁での破壊をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logIwa = true;

        DarumaCharacter m_character;
        readonly Collider[] m_overlaps = new Collider[32];

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
        }

        private void FixedUpdate()
        {
            if (m_character == null || !m_character.CanBeAffected) return;

            Vector3 center = m_character.transform.position + Vector3.up * 0.5f;
            int count = Physics.OverlapSphereNonAlloc(center, m_searchRadius, m_overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var block = m_overlaps[i].GetComponentInParent<TumikiBlock>();
                if (block == null) continue;
                if (block.CurrentState != TumikiBlock.State.Flying) continue;
                // 自分が飛ばした積み木だけ(他の人の積み木はそのまま)
                if (!ReferenceEquals(block.Owner, m_character)) continue;

                var iwa = block.GetComponent<IwaBlock>();
                // この発射でまだ岩化していなければ付ける(2回目以降の発射でも付く)
                if (iwa != null && iwa.ConsumedLaunch) continue;
                if (iwa == null) iwa = block.gameObject.AddComponent<IwaBlock>();
                iwa.Begin(m_logIwa);
            }
        }
    }
}
