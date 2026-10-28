// 志鷹さんの Assets/Shitaka から複製（由留部の作業用）
// 元のクラスと名前が衝突しないよう、名前空間 Yurube.Blocks に入れている
using UnityEngine;

namespace Yurube.Blocks
{
// 暫定仕様：自分以外の積み木のうち、一番近いものを狙う
// 正式仕様(敵の積み木のみ等)が決まったら、別のSelectorクラスに差し替える
public class OtherBlockTargetSelector : IHomingTargetSelector
{
    public Transform SelectTarget(BlockController owner)
    {
        Transform nearest = null;
        float nearestSqr = float.MaxValue;

        // シーン内の「Block」タグが付いた全オブジェクトを順に調べ、一番近いものを探す
        foreach (var go in GameObject.FindGameObjectsWithTag("Block"))
        {
            if (go == owner.gameObject) continue; // 自分自身は除外

            float sqr = (go.transform.position - owner.transform.position).sqrMagnitude;
            // これまでの最短より近ければ、候補を更新する
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = go.transform;
            }
        }
        return nearest;
    }
}
}
