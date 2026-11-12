using UnityEngine;

// 周囲の積み木を本体に引き寄せるパッシブ
// 実際には積み木ではなく本体に引き寄せるようにする
public class CollectorBehavior:NormalBehavior
{
    public CollectorPassive Passive { get; }

    public CollectorBehavior(BlockController owner, float range = 5f, float strength = 12f,
                          float edgeRatio = 0.4f, float collectDistance = 0.8f,
                          float flyingPullRatio = 0.3f)
    {
        Passive = CollectorPassive.Attach(owner, range, strength, edgeRatio, collectDistance, flyingPullRatio);
    }
}
