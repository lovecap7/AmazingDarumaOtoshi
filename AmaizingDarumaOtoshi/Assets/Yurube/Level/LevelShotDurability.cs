using Nakagawa;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // いわだるのレベルの能力：撃った弾（積み木）の耐久値を、飛んでいる間だけ増やす（ほかの積み木に当たっても壊れずに進む回数が増える）。
    // 飛び終わったら（積まれた・止まった）、ふつうの耐久値に戻す。DarumaLevel がショットのときに付ける
    [RequireComponent(typeof(TumikiBlock))]
    public class LevelShotDurability : MonoBehaviour
    {
        TumikiBlock m_block;
        TumikiDurability m_durability;
        bool m_active;

        void Awake()
        {
            m_block = GetComponent<TumikiBlock>();
            m_durability = GetComponent<TumikiDurability>();
            m_block.StateChanged += OnStateChanged;
        }

        void OnDestroy()
        {
            if (m_block != null) m_block.StateChanged -= OnStateChanged;
        }

        public void Begin(int extra)
        {
            if (m_durability == null || m_durability.IsUnbreakable || extra <= 0) return;
            m_durability.SetMax(TumikiDurability.DefaultMax + extra, true);
            m_active = true;
        }

        void OnStateChanged(TumikiBlock block)
        {
            if (!m_active || block.CurrentState == TumikiBlock.State.Flying) return;
            m_active = false;
            // 積まれたら最大まで回復、落ちて止まったら残りはそのまま（ふつうの最大値を超えないように）
            m_durability.SetMax(TumikiDurability.DefaultMax, block.CurrentState == TumikiBlock.State.Stacked);
        }
    }
}
