using UnityEngine;

// 色の定義と判定
public enum BlockColor {Red,Blue,Green,Yellow,All }
public static class BlockColorUtil
{
    // 2つの色が一致しているか判定
    public static bool IsMatch(BlockColor color1, BlockColor color2)
    {
        return color1 == BlockColor.All || color2 == BlockColor.All || color1 == color2;
    }
}
