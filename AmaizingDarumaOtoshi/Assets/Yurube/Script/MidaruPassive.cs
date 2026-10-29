using Nakahira;
using UnityEngine;

namespace Yurube
{
    // みだるのパッシブ。自分が飛ばした緑色の積み木が相手を追いかける。
    // 積み木が飛び始めた直後はキャラクターのすぐ前にいるので、近くの緑の積み木を拾って
    // TumikiHoming を付ける(積み木側のスクリプトやハンマーには手を入れない)。
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(DarumaCharacter))]
    public class MidaruPassive : MonoBehaviour
    {
        [Tooltip("ホーミングさせる緑の積み木を探す範囲(m)")]
        [SerializeField] float m_searchRadius = 3.0f;
        [Tooltip("1秒あたりに曲がれる角度(度/秒)")]
        [SerializeField] float m_turnSpeed = 270.0f;
        [Tooltip("追いかける時間(秒)")]
        [SerializeField] float m_guidanceDuration = 1.5f;
        [Tooltip("ホーミングの開始・終了をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logHoming = true;

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
                if (block.Color != TumikiColor.Green) continue;
                if (block.CurrentState != TumikiBlock.State.Flying) continue;
                // 自分が飛ばした積み木だけ(他の人の緑はそのまま)
                if (!ReferenceEquals(block.Owner, m_character)) continue;

                var homing = block.GetComponent<TumikiHoming>();
                // この発射でまだホーミングしていなければ付ける(2回目以降の発射でも付く)
                if (homing != null && homing.ConsumedLaunch) continue;
                if (homing == null) homing = block.gameObject.AddComponent<TumikiHoming>();
                homing.Begin(m_character, m_turnSpeed, m_guidanceDuration, m_logHoming);
            }
        }
    }
}
