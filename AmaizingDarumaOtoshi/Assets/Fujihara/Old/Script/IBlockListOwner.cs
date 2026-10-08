// MenuBlock のマウス操作を受け取る側（メニュー・タイトル）
public interface IBlockListOwner
{
    void OnBlockHover(int index);
    void OnBlockClick(int index);
}
