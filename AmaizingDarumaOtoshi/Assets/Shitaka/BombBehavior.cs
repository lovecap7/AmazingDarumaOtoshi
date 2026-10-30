using System.Collections;
using UnityEngine;

// 打ち出されてから一定時間後に爆発し、周囲の積み木を吹き飛ばす。
// 爆発するまでは、積み木に当たっても壊れない。爆発が近づくにつれて点滅が速くなる
// (旧仕様の「積み木に当たると爆発」は、OnBlockHit内でExplode()を呼べば再現できる)
public class BombBehavior : NormalBehavior
{
    readonly BlockController owner;
    readonly Renderer rend;
    readonly float radius;         // 爆発の範囲(半径)
    readonly float fuseTime;       // 打ち出されてから爆発するまでの時間(秒)
    readonly float blinkDuration;  // 爆発のこの秒数前から点滅を始める

    const float MinBlinkFreq = 2f;   // 点滅の速さ(回/秒)。点滅の開始時
    const float MaxBlinkFreq = 14f;  // 点滅の速さ(回/秒)。爆発の直前
    static readonly Color BlinkColor = Color.white; // 点滅で混ぜる色

    bool fuseLit; // 導火線に点火したか

    public BombBehavior(BlockController owner, float radius = 2.0f,
                        float fuseTime = 2.5f, float blinkDuration = 1.5f)
    {
        this.owner = owner;
        this.rend = owner.GetComponent<Renderer>();
        this.radius = radius;
        this.fuseTime = fuseTime;
        this.blinkDuration = blinkDuration;
    }

    public override Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
    {
        // 打ち出された最初の1回だけ点火する
        if (!fuseLit)
        {
            fuseLit = true;
            owner.StartCoroutine(Fuse());
        }
        return currentDirection;
    }

    // 爆発するまでは壊れない。当たった相手だけ、通常通り飛ばす
    public override bool OnBlockHit(BlockController self, BlockController other)
    {
        base.OnBlockHit(self, other); // 戻り値(自分が壊れるか)は使わない
        return false;
    }

    // 導火線：時間を数えて、爆発が近づくと点滅を速くして、時間が来たら爆発する
    IEnumerator Fuse()
    {
        float elapsed = 0f;
        float phase = 0f; // 点滅の位相。速さが変わっても、色が飛ばないよう積算する
        Color original = (rend != null) ? rend.material.color : Color.white;

        while (elapsed < fuseTime)
        {
            elapsed += Time.deltaTime;
            float remaining = fuseTime - elapsed;

            if (rend != null && remaining < blinkDuration)
            {
                // 0(点滅の開始)〜1(爆発の直前)
                float t = 1f - remaining / blinkDuration;
                float freq = Mathf.Lerp(MinBlinkFreq, MaxBlinkFreq, t);
                phase += freq * Time.deltaTime * Mathf.PI * 2f;

                float pulse = (Mathf.Sin(phase) + 1f) * 0.5f; // 0〜1を往復する
                rend.material.color = Color.Lerp(original, BlinkColor, pulse);
            }
            yield return null;
        }

        Explode();
    }

    // 範囲内の積み木を、中心から外側へ飛ばして、自分は壊れる
    void Explode()
    {
        Vector3 center = owner.transform.position;

        foreach (var col in Physics.OverlapSphere(center, radius))
        {
            if (!col.CompareTag("Block")) continue;

            var block = col.GetComponent<BlockController>();
            if (block == null || block == owner) continue; // 自分は除外

            // 爆発の中心から外側へ向かう向きに飛ばす(高さは無視して水平方向のみ)
            Vector3 away = block.transform.position - center;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) continue; // 真上や同じ位置など、向きが決まらない場合は飛ばさない

            block.Launch(away);
        }

        Debug.Log("[Bomb] 爆発", owner); // テスト用
        owner.Break();
    }
}