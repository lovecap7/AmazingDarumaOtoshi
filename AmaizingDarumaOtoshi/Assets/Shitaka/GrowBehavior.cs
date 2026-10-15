using UnityEngine;

// 壁に当たるたびに、XとZだけ大きくなる積み木(高さは変えない)
public class GrowBehavior:NormalBehavior
{
    readonly BlockController owner;
    readonly Collider col; // 拡大前のコライダーの大きさを調べるために保持する
    readonly float growthRate;   // 1回の壁ヒットで掛ける倍率(1.2なら20%拡大)
    readonly float maxScaleX;    // Xの上限(初期サイズに対する倍率ではなく実際の値)
    readonly float maxScaleZ;    // Zの上限

    public GrowBehavior(BlockController owner, float growthRate = 1.2f, float maxMultiplier = 3f)
    {
        this.owner = owner;
        this.col = owner.GetComponent<Collider>();
        this.growthRate = growthRate;

        // 初期サイズを基準に、何倍まで大きくなれるかを上限にする
        Vector3 initial = owner.transform.localScale;
        maxScaleX = initial.x * maxMultiplier;
        maxScaleZ = initial.z * maxMultiplier;
    }

    // 壁に当たったら通常通り反射したうえで、大きくなる
    public override bool OnWallHit(Vector3 currentDirection, Vector3 wallNormal, out Vector3 newDirection)
    {
        bool result = base.OnWallHit(currentDirection, wallNormal, out newDirection);
        Grow(wallNormal);
        return result;
    }

    void Grow(Vector3 wallNormal)
    {
        Transform t = owner.transform;
        Vector3 before = t.localScale;

        // 拡大前の、壁の法線方向に向いた半径(おおよそ)
        Vector3 ext = col.bounds.extents;
        float halfAlongNormal = Mathf.Abs(wallNormal.x) * ext.x + Mathf.Abs(wallNormal.z) * ext.z;

        // XとZだけ拡大する(Yは変えない)。上限を超えないようにする
        Vector3 s = before;
        s.x = Mathf.Min(s.x * growthRate, maxScaleX);
        s.z = Mathf.Min(s.z * growthRate, maxScaleZ);
        t.localScale = s;

        // 法線方向に実際に何倍になったか(XZの増え方を、法線の向きで重みづけする)
        float ratio = Mathf.Abs(wallNormal.x) * (s.x / before.x)
                    + Mathf.Abs(wallNormal.z) * (s.z / before.z) - 1f;

        // 増えた半径ぶん+少しの余裕を持たせて、壁から離れる向きへ押し出す
        float push = halfAlongNormal * ratio + 0.01f;
        if (ratio > 0f) t.position += wallNormal * push;
    }
}
