// 志鷹さんの Assets/Shitaka から複製（由留部の作業用）
// 元のクラスと名前が衝突しないよう、名前空間 Yurube.Blocks に入れている
using UnityEngine;

namespace Yurube.Blocks
{
// 色の定義と判定
public enum BlockColor {Red,Blue,Green,Yellow,All }
public static class BlockDefinition
{
    // 2つの色が一致しているか判定
    public static bool IsMatch(BlockColor color1, BlockColor color2)
    {
        // 両方の色のタグが一致または片方が全ての色かで判断する
        return color1 == BlockColor.All || color2 == BlockColor.All || color1 == color2;
    }
}
}
