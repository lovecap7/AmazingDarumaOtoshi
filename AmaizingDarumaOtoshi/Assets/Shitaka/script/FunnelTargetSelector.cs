using UnityEngine;
using System.Collections.Generic;

// ファンネル用：グループ全員が、同じ1個の積み木を狙う。
// 最初に選ばれた相手を全員で共有し、その相手がいなくなったら選び直す
public class FunnelTargetSelector : IHomingTargetSelector
{
    readonly List<BlockController> group; // 同じ分裂グループ(本体+分身)。狙わない
    // readonly int index; // 全員が同じ相手を狙うため不要
    readonly float searchRadius;

    // グループで共有する、現在のターゲット。全員が同じSelectorインスタンスを使うので、ここに持てる
    Transform sharedTarget;

    // FunnelBehaviorが、同じグループのリストを参照するための公開プロパティ
    public List<BlockController> Group => group;

    // public FunnelTargetSelector(List<BlockController> group, int index, float searchRadius) // グループで1つのSelectorを共有するため変更
    public FunnelTargetSelector(List<BlockController> group, float searchRadius)
    {
        this.group = group;
        this.searchRadius = searchRadius;
    }

    public Transform SelectTarget(BlockController owner)
    {
        // すでに共有のターゲットが決まっていて、まだ存在するなら、そのまま使う
        if (sharedTarget != null) return sharedTarget;

        Vector3 pos = owner.transform.position;
        Transform nearest = null;
        float nearestSqr = float.MaxValue;

        foreach (var col in Physics.OverlapSphere(pos, searchRadius))
        {
            if (!col.CompareTag("Block")) continue;
            var block = col.GetComponent<BlockController>();
            if (block == null || group.Contains(block)) continue;

            float sqr = (col.transform.position - pos).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = col.transform;
            }
        }

        sharedTarget = nearest; // 見つからなければnullのまま。次に呼ばれたときに、もう一度探す
        return sharedTarget;
    }
}