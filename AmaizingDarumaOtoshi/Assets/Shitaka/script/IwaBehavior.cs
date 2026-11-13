using UnityEngine;

public class IwaBehavior :IBlockBehavior
{
    // 壁に当たったら反射せず破壊される
    public bool OnWallHit(Vector3 currentDirection, Vector3 wallNormal, out Vector3 newDirection)
    {
        newDirection = currentDirection;
        return false; 
    }

    // 他の積み木に当たったら、自分は壊れず相手だけ破壊する
    public bool OnBlockHit(BlockController self, BlockController other)
    {
        other.Break(); // 相手を直接破壊する(Launchで飛ばすのではなく即破壊)
        return false; // 自分は壊れない
    }

    // 方向は変化しない(通常と同じ直進)
    public Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
        => currentDirection; // 方向は変化しない
}
