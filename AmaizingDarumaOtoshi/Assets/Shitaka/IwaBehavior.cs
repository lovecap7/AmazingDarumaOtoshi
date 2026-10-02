using UnityEngine;

public class IwaBehavior :IBlockBehavior
{
    public bool OnWallHit(Vector3 currentDirection, Vector3 wallNormal, out Vector3 newDirection)
    {
        newDirection = Vector3.Reflect(currentDirection, wallNormal);
        newDirection.y = 0f;
        newDirection.Normalize();
        return true; // 壊れず反射する
    }

    public bool OnBlockHit(BlockController self, BlockController other)
    {
        other.Launch(self.Direction); // 相手を飛ばす
        return true; // 自分は壊れる
    }

    public Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
        => currentDirection; // 方向は変化しない
}
