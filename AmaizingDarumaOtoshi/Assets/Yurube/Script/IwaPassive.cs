using Nakahira;
using UnityEngine;
using Yurube.Achievements;

namespace Yurube
{
    // いわだるのパッシブ。自分が飛ばした積み木を「岩の積み木」にする。
    // 積み木が飛び始めた直後はキャラクターのすぐ前にいるので、近くの積み木を拾って
    // IwaBlock を付ける(積み木側のスクリプトやハンマーには手を入れない)。
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(DarumaCharacter))]
    public class IwaPassive : MonoBehaviour, INetSpecial
    {
        [Tooltip("岩化する積み木を探す範囲(m)")]
        [SerializeField] float m_searchRadius = 3.0f;
        [Tooltip("岩化・壁での破壊をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logIwa = true;

        DarumaCharacter m_character;
        bool m_reportedPassive;
        readonly Collider[] m_overlaps = new Collider[32];

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
        }

        private void FixedUpdate()
        {
            if (m_character == null || !m_character.CanBeAffected) return;
            // ネット対戦用(suehara): クライアントはホストからの知らせ(NetReceive)で見た目だけ付ける
            if (SpecialNet.IsClient) return;

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
                // ネット対戦用(suehara): クライアントでも同じ岩の見た目を付ける
                Vector3 dir = block.Direction;
                SpecialNet.Send(m_character, this, kNetIwa, new[] { dir.x, dir.z, block.Speed }, new[] { block });
                if (!m_reportedPassive)
                {
                    m_reportedPassive = true;
                    AchievementEvents.RaisePassiveTriggered(m_character);
                }
            }
        }

        //========================================
        // ネット対戦用(suehara): クライアントで、ホストと同じ岩の見た目を付ける(壊す・壊れるはホストが決める)
        //========================================

        const int kNetIwa = 0;

        public void NetReceive(int ev, float[] v, TumikiBlock[] blocks)
        {
            if (ev != kNetIwa || v.Length < 3 || blocks.Length < 1 || blocks[0] == null) return;
            var block = blocks[0];
            SpecialNet.MarkLaunched(block, new Vector3(v[0], 0.0f, v[1]), v[2]);

            var iwa = block.GetComponent<IwaBlock>();
            if (iwa == null) iwa = block.gameObject.AddComponent<IwaBlock>();
            iwa.BeginNetVisual();
        }
    }
}
