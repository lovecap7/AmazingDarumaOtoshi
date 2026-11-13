using UnityEngine;

// 赤(N極)と青(S極)の積み木が、異極は引き合い、同極は反発する。
// 相手の積み木は、通常の挙動でも色が赤か青なら磁力の対象になる
public class MagnetBehavior : NormalBehavior
{
    readonly BlockController owner;
    readonly BlockInfo info;
    readonly float range;           // 磁力が届く距離
    readonly float attractStrength; // 引き合う力の強さ
    readonly float repelStrength;   // 反発する力の強さ(最も近いときの値)
    readonly float minDistance;     // これより近づいても、反発の強さは増えない(無限大を防ぐ)
    readonly Collider myCollider; // 同じ高さかどうかを調べるために、自分の高さの基準として保持する

    // public MagnetBehavior(BlockController owner, float range = 3f, float strength = 4f) // 反発を別パラメータにするため変更
    public MagnetBehavior(BlockController owner, float range = 3f, float attractStrength = 4f,
                          float repelStrength = 8f, float minDistance = 0.5f)
    {
        this.owner = owner;
        this.info = owner.GetComponent<BlockInfo>();
        this.myCollider = owner.GetComponent<Collider>(); // 追加
        this.range = range;
        this.attractStrength = attractStrength;
        this.repelStrength = repelStrength;
        this.minDistance = minDistance;
    }

    // 赤=+1(N極)、青=-1(S極)、それ以外は0(磁力なし)
    static int Pole(BlockColor c)
    {
        switch (c)
        {
            case BlockColor.Red: return 1;
            case BlockColor.Blue: return -1;
            default: return 0;
        }
    }

    public override Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
    {
        int myPole = Pole(info.Color); // 毎回読むので、SetColorで色が変わっても追従する
        if (myPole == 0) return currentDirection;

        Vector3 myPos = owner.transform.position;
        Vector3 force = Vector3.zero;

        foreach (var col in Physics.OverlapSphere(myPos, range))
        {
            if (!col.CompareTag("Block")) continue;

            var other = col.GetComponent<BlockController>();
            if (other == null || other == owner) continue;

            // 同じ高さにある積み木だけを対象にする
            // (積み上げられた上の段の積み木に反応しないようにするため。
            float dy = Mathf.Abs(col.bounds.center.y - myCollider.bounds.center.y);
            if (dy > col.bounds.extents.y) continue;

            // 相手の挙動は問わない(Magnet設定がなくても、色が赤か青なら対象にする)
            // if (!(other.Behavior is MagnetBehavior)) continue; // 相手の挙動を問わない仕様に変更

            var otherInfo = other.GetComponent<BlockInfo>();
            if (otherInfo == null) continue;
            int otherPole = Pole(otherInfo.Color);
            if (otherPole == 0) continue;

            Vector3 toOther = other.transform.position - myPos;
            toOther.y = 0f;
            float dist = toOther.magnitude;
            if (dist < 0.0001f) continue;

            Vector3 dirToOther = toOther / dist;

            if (myPole == otherPole)
            {
                // 同極：反発。距離の2乗に反比例して、近いほど急に強くなる。
                // rangeの端で0、minDistanceで1になるよう正規化している
                float near = Mathf.Max(dist, minDistance);
                float inv = 1.0f / (near * near) - 1.0f / (range * range);
                float norm = 1.0f / (minDistance * minDistance) - 1.0f / (range * range);
                float repelFalloff = Mathf.Clamp01(inv / norm);

                force -= dirToOther * (repelFalloff * repelStrength);
            }
            else
            {
                // 異極：引き合う。今までどおり、距離に対して直線的に弱まる
                float falloff = 1f - dist / range;
                force += dirToOther * (falloff * attractStrength);
            }
        }

        if (force.sqrMagnitude < 0.0001f) return currentDirection;

        Vector3 newDir = currentDirection + force * deltaTime;
        newDir.y = 0f;
        return newDir.normalized;
    }
}