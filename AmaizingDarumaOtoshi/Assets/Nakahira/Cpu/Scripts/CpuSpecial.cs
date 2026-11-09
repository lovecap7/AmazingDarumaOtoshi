using UnityEngine;
using Yurube;

namespace Nakahira
{
    // CPU が必殺技を撃つ(1人の CPU につき1つ)
    // ゲージが満タンで、そのキャラの発動条件(ShouldActivate)を満たしたら撃つ
    // 今はどのキャラも「満タンになったらすぐ撃つ」。キャラごとの撃ちどころはあとで ShouldActivate に足す
    public class CpuSpecial
    {
        DarumaCharacter m_self;
        ISpecialAbility m_ability;
        SpecialGauge m_gauge;

        // 撃った回数(デバッグ・テスト用)
        public int ActivatedCount { get; private set; }
        // ゲージが満タンか(デバッグ表示用)
        public bool IsReady => m_gauge != null && m_gauge.IsFull;

        public void Reset()
        {
            m_self = null;
            m_ability = null;
            m_gauge = null;
        }

        // 撃てたら true
        public bool Apply(CpuContext c)
        {
            Bind(c.Self);
            if (m_ability == null || m_gauge == null || !m_gauge.IsFull) return false;
            // 時間が止まっている・遅くなっている間(ほかの必殺技のカットイン・ヒットストップ・やられ演出)は撃たない
            // (カットインは始まったときの時間の速さを覚えて終わりに戻すので、重なると止まったままになる)
            if (Time.timeScale < DarumaMatch.PlaySpeed) return false;
            if (!ShouldActivate(c)) return false;
            if (!m_ability.TryActivate()) return false;
            ActivatedCount++;
            return true;
        }

        // 体が変わったら必殺技とゲージを取り直す(パッシブがあとから付けることもあるので、見つかるまで探す)
        private void Bind(DarumaCharacter self)
        {
            if (self != m_self)
            {
                m_self = self;
                m_ability = null;
                m_gauge = null;
            }
            if (m_self == null) return;
            if (m_ability == null) m_ability = m_self.GetComponent<ISpecialAbility>();
            if (m_gauge == null) m_gauge = m_self.GetComponent<SpecialGauge>();
        }

        // キャラごとの発動条件(ゲージが満タンのときだけ呼ばれる)
        private bool ShouldActivate(CpuContext c)
        {
            switch (m_ability)
            {
                case IwadaruSpecial _:  return true;   // 隕石
                case HebidaruSpecial _: return true;   // 毒沼
                case AkadaruSpecial _:  return true;   // トロン(ビーム)
                case DaruKingSpecial _: return true;   // エクスカリバー
                case MidaruSpecial _:   return true;   // ファンネル
                case KidaruSpecial _:   return true;   // 黄金化
                case AodaruSpecial _:   return true;   // 六腕
                default:                return true;
            }
        }
    }
}
