// 志鷹さんの Assets/Shitaka から複製（由留部の作業用）
// 元のクラスと名前が衝突しないよう、名前空間 Yurube.Blocks に入れている
using UnityEngine;

namespace Yurube.Blocks
{
// 積み木に当たると爆発し、当たった相手以外の周囲の積み木も吹き飛ばす
public class BombBehavior : NormalBehavior
{
    readonly float radius; // 爆発の範囲(半径)

    public BombBehavior(float radius = 2.0f)
    {
        this.radius = radius;
    }

    public override bool OnBlockHit(BlockController self, BlockController other)
    {
        // 通常通り、当たった相手を飛ばす。戻り値(自分が壊れるか)もそのまま使う
        bool breakSelf = base.OnBlockHit(self, other);

        // 爆発の中心。自分が壊れる前に位置を取っておく
        Vector3 center = self.transform.position;

        foreach (var col in Physics.OverlapSphere(center, radius))
        {
            if (!col.CompareTag("Block")) continue;

            var block = col.GetComponent<BlockController>();
            if (block == null || block == self || block == other) continue; // 自分と当たった相手は除外

            // 爆発の中心から外側へ向かう向きに飛ばす(高さは無視して水平方向のみ)
            Vector3 away = block.transform.position - center;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) continue; // 真上や同じ位置など、向きが決まらない場合は飛ばさない

            block.Launch(away);
        }

        return breakSelf;
    }
}
}
