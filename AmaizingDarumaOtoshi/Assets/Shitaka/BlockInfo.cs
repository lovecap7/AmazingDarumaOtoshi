using NUnit.Framework;
using System;
using UnityEngine;

public class BlockInfo: MonoBehaviour
{
    [SerializeField] BlockColor color;
    public BlockColor Color => color;

    // 色が変わったときに通知する
    public event Action<BlockColor> OnColorChanged;

    // 色を指定する 生成担当が、生成した直後・打ち出す前に呼ぶ想定
    public void SetColor(BlockColor newColor)
    {
        if (color == newColor) return;
        color = newColor;
        OnColorChanged?.Invoke(color);
    }

    [SerializeField] int attackPower = 1;
    public int AttackPower => attackPower;
}
