namespace Yurube
{
    // 必殺技を持つキャラクターの共通インターフェース。
    // ラボやUIから「そのキャラの必殺技を発動する」ために使う。
    public interface ISpecialAbility
    {
        bool TryActivate();
    }
}
