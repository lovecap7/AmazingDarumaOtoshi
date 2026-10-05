using NUnit.Framework;
using UnityEngine;

public class BlockInfo: MonoBehaviour
{
    [SerializeField] BlockColor color;
    public BlockColor Color => color;

    [SerializeField] int attackPower = 1;
    public int AttackPower => attackPower;
}
