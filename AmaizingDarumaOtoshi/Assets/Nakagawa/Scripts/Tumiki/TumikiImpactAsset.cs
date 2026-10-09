using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 積み木同士がぶつかったときの振る舞い(ITumikiImpactBehavior)を、アセットとして設定できるようにする基底クラス
    // 新しい振る舞いはこのクラスを継承して作り、TumikiImpactModeToggle などに登録する
    public abstract class TumikiImpactAsset : ScriptableObject, ITumikiImpactBehavior
    {
        // 切り替え時の表示名
        [SerializeField] string m_displayName;

        public string DisplayName => string.IsNullOrEmpty(m_displayName) ? name : m_displayName;

        public abstract void OnHitBlock(TumikiBlock self, TumikiBlock other);
    }
}
