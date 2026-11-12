namespace Yurube
{
    // 必殺技を持つキャラクターの共通インターフェース。
    // ラボやUIから「そのキャラの必殺技を発動する」ために使う。
    public interface ISpecialAbility
    {
        bool TryActivate();
        // 必殺技の効果が続いている間 true(この間は必殺技ゲージが溜まらない。中平 2026-11-12)
        bool IsActive { get; }
    }
}
