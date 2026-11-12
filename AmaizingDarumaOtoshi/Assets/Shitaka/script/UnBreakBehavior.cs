using UnityEngine;
using UnityEngine.UIElements;
public class UnBreakBehavior : IBlockBehavior
{
    // ★追加: 弾かれた後の速度の割合(1で勢い維持、小さいほど弱くなる)
    const float BounceSpeedRatio = 0.8f;
    // 壁に当たった時の処理
    public virtual bool OnWallHit(Vector3 currentDirection, Vector3 wallNormal, out Vector3 newDirection)
    {
        newDirection = Vector3.Reflect(currentDirection, wallNormal);
        return true; // 壊れず反射する
    }

    // 他の積み木に当たった時の処理
    public virtual bool OnBlockHit(BlockController self, BlockController other)
    {
        // 自分の現在速度を相手に渡す(遅ければ相手も遅く飛ぶ)
        other.Launch(other.transform.position - self.transform.position, self.CurrentSpeed); // 相手を飛ばす
        // 自分は接触面で反射して弾かれる
        Vector3 reflected = Vector3.Reflect(self.Direction, self.LastContactNormal);
        self.Bounce(reflected, BounceSpeedRatio);
        return false; //  自分は壊れない
    }
       

    public virtual Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
        => currentDirection; // 方向は変化しない   
}
