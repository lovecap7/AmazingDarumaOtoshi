using System;
using UnityEngine;

public class BlockInfo : MonoBehaviour
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

    [SerializeField] int attackPower = 1; // 基本の攻撃力
    int attackBonus;                      // 打ち返しなどで一時的に上乗せされる分(飛行が終わるとリセットされる)

    // 実際に使われる攻撃力(基本+上乗せ)
    public int AttackPower => attackPower + attackBonus;

    // 攻撃力を上乗せする
    public void AddAttackBonus(int amount)
    {
        attackBonus += amount;
    }

    // 上乗せ分を元に戻す
    public void ResetAttackBonus()
    {
        attackBonus = 0;
    }
}