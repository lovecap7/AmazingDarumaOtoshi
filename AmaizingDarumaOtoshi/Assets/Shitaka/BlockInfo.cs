using NUnit.Framework;
using UnityEngine;

public class BlockInfo: MonoBehaviour
{
    [SerializeField] BlockColor color;
    public BlockColor Color => color;
}
