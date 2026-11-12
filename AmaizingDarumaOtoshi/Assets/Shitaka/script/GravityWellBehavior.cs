using UnityEngine;

// 壁か積み木に当たると、その地点に重力井戸を作る。井戸を作った積み木自身は壊れる
public class GravityWellBehavior : NormalBehavior
{
    readonly BlockController owner;
    readonly float radius;
    readonly float strength;
    readonly float duration;
    readonly float deadZone;
    readonly float edgeRatio;
    readonly float burstImmunity;

    public GravityWellBehavior(BlockController owner, float radius = 3f, float strength = 12f,
                               float duration = 2.5f, float deadZone = 0.4f, float edgeRatio = 0.4f, float burstImmunity = 0.5f)
    {
        this.owner = owner;
        this.radius = radius;
        this.strength = strength;
        this.duration = duration;
        this.deadZone = deadZone;
        this.edgeRatio = edgeRatio;
        this.burstImmunity = burstImmunity;
    }

    // 壁に当たったら、井戸を作って自分は壊れる(falseを返すとBlockController側でBreak()される)
    public override bool OnWallHit(Vector3 currentDirection, Vector3 wallNormal, out Vector3 newDirection)
    {
        newDirection = currentDirection;
        CreateWell();
        return false;
    }

    // 積み木に当たったら、井戸を作って自分は壊れる
    public override bool OnBlockHit(BlockController self, BlockController other)
    {
        CreateWell();
        return true;
    }

    void CreateWell()
    {
        GravityWell.Create(owner.transform.position, radius, strength, duration, deadZone, edgeRatio, burstImmunity);
    }
}