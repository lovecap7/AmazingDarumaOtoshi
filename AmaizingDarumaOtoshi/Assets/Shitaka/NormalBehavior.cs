using UnityEngine;
using UnityEngine.UIElements;

// 基本の積み木の動き
public class NormalBehavior:IBlockBehavior
{
    // 壁に当たった時の処理
    public virtual bool OnWallHit(Vector3 currentDirection, Vector3 wallNormal, out Vector3 newDirection)
    {
        newDirection = Vector3.Reflect(currentDirection, wallNormal);
        return true; // 壊れず反射する
    }

    // 他の積み木に当たった時の処理
    public virtual bool OnBlockHit(BlockController self, BlockController other)
    {
        other.Launch(other.transform.position - self.transform.position); // 相手を飛ばす
        return true; // 自分は壊れる
    }

    public virtual Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
        => currentDirection; // 方向は変化しない   
}
