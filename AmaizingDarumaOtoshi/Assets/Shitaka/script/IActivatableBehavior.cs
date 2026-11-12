using UnityEngine;

// 任意のタイミングで発動する効果を持つ挙動が実装する(テレポートなど)
public interface IActivatableBehavior
{
    void Activate();
}