using UnityEngine;

// 積み木の動きのインターフェース
// いわだるやへびだるなど通常とは違う動きをするものを実装する際に使う
public interface IBlockBehavior
{
    // 壁に当たったときの処理。falseを返したら破壊、trueなら反射などの方向をnewDirectionに入れる
    bool OnWallHit(Vector3 currentDirection, Vector3 wallNormal, out Vector3 newDirection);

    // 他の積み木に当たったときの処理。自分が壊れるべきかを返す
    bool OnBlockHit(BlockController self, BlockController other);

    // 毎ステップ呼ばれる、方向を更新するための処理(曲がる動きなどに使う)
    Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime);
}
