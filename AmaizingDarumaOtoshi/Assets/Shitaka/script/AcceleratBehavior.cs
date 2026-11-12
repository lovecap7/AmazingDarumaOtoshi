using UnityEngine;

public class AcceleratBehavior:NormalBehavior
{
    readonly BlockController owner;
    readonly float accelRate;   // 壁ヒットごとに速度に掛ける倍率(1.3なら30%加速)
    readonly float maxSpeed;    // 速度の上限(壁抜けを防ぐため必ず設定する)
    readonly int maxBlockHits;  // 何回積み木に当たったら壊れるか

    int blockHits;
    BlockController lastTarget;

    public AcceleratBehavior(BlockController owner, float accelRate = 1.3f,
                         float maxSpeed = 12f, int maxBlockHits = 3)
    {
        this.owner = owner;
        this.accelRate = accelRate;
        this.maxSpeed = maxSpeed;
        this.maxBlockHits = maxBlockHits;
    }

    // 壁に当たったら通常通り反射して、加速する
    public override bool OnWallHit(Vector3 currentDirection, Vector3 wallNormal, out Vector3 newDirection)
    {
        bool result = base.OnWallHit(currentDirection, wallNormal, out newDirection);
        owner.BoostSpeed(accelRate, maxSpeed);
        return result;
    }

    public override bool OnBlockHit(BlockController self, BlockController other)
    {
        // 加速しているので、飛ばした直後の同じ積み木に追いついて、何度もぶつかることがある。
        // その場合は、新しい1回として数えない
        if (other == lastTarget && other.isFlying) return false;
        lastTarget = other;

        // 相手は通常通り飛ばす(攻撃力や速度の引き継ぎも、NormalBehaviorと同じ)。
        // base の戻り値(自分が壊れるか)は使わない
        base.OnBlockHit(self, other);

        blockHits++;
        return blockHits >= maxBlockHits; // 規定の回数に達したら壊れる
    }
}
