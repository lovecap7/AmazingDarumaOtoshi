using UnityEngine;

public class AfterimageBehavior:NormalBehavior
{
    readonly BlockController owner;
    readonly float delay;
    readonly float lifetime;
    readonly float alpha;
    readonly Material material;

    bool spawned;

    public AfterimageBehavior(BlockController owner, float delay = 0.25f, float lifetime = 1.5f,
                              float alpha = 0.4f, Material material = null)
    {
        this.owner = owner;
        this.delay = delay;
        this.lifetime = lifetime;
        this.alpha = alpha;
        this.material = material;

        // 飛行が終わったら、次の飛行でまた作れるようにする
        owner.OnFlightEnded += () => spawned = false;
    }

    public override Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
    {
        if (!spawned)
        {
            spawned = true;
            Afterimage.Create(owner, delay, lifetime, alpha, material);
        }
        return currentDirection;
    }
}
