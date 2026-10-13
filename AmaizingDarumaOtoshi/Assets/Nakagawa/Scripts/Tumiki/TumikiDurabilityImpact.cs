using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 耐久度つき: 当てるたびに当てた積み木の耐久度(TumikiDurability)が1減り、0になったら崩れる(既定の連鎖)
    // 耐久度が残っている間は「耐えたときの振る舞い」(跳ね返りなど)をする。壊れない積み木(最大値0)は常に耐える
    // TumikiDurability が付いていない積み木は1回で崩れる
    [CreateAssetMenu(fileName = "TumikiImpact_Durability", menuName = "Nakagawa/Tumiki Impact/Durability")]
    public class TumikiDurabilityImpact : TumikiImpactAsset
    {
        [Tooltip("耐久度が残っているときの振る舞い(未設定なら既定の跳ね返り)")]
        [SerializeField] TumikiImpactAsset m_onSurvive;

        TumikiBounceImpact m_fallback;

        public override void OnHitBlock(TumikiBlock self, TumikiBlock other)
        {
            // 離れていく相手には当たらない(同じ衝突で何度も耐久度が減らないように)
            if (TumikiBounceImpact.Approach(self, other, out _, out _, out _) <= 0.0f) return;

            var durability = self.GetComponent<TumikiDurability>();
            if (durability == null || durability.Damage())
            {
                TumikiBreakImpact.Instance.OnHitBlock(self, other);
                return;
            }
            Survive.OnHitBlock(self, other);
        }

        ITumikiImpactBehavior Survive
        {
            get
            {
                if (m_onSurvive != null) return m_onSurvive;
                if (m_fallback == null) m_fallback = CreateInstance<TumikiBounceImpact>();
                return m_fallback;
            }
        }
    }
}
