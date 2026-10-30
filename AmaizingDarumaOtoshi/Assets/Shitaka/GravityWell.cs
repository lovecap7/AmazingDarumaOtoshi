using System.Collections.Generic;
using UnityEngine;

// 着弾地点に生成され、一定時間、周囲の積み木(止まっているものも飛んでいるものも)を中心へ引き寄せる。
// 時間が終わったら、中心に集まった積み木を一斉に外側へ射出する
public class GravityWell : MonoBehaviour
{
    float radius;
    float strength;       // 引き寄せる加速度
    float duration;       // 井戸が残る時間(秒)
    float deadZone;       // 中心にこの距離まで近づいたら、引き寄せるのをやめて止める
    float elapsed;
    float edgeRatio;      // 範囲の端での強さ(中心を1としたときの割合)。0.4なら端でも中心の40%
    float burstImmunity;  // 射出した積み木同士が、互いを攻撃として扱わない時間(秒)

    public static GravityWell Create(Vector3 position, float radius, float strength,
                                     float duration, float deadZone, float edgeRatio = 0.4f,
                                     float burstImmunity = 0.5f)
    {
        var go = new GameObject("GravityWell");
        go.transform.position = position;

        var well = go.AddComponent<GravityWell>();
        well.radius = radius;
        well.strength = strength;
        well.duration = duration;
        well.deadZone = deadZone;
        well.edgeRatio = edgeRatio;
        well.burstImmunity = burstImmunity;
        return well;
    }

    void FixedUpdate()
    {
        elapsed += Time.fixedDeltaTime;
        if (elapsed >= duration)
        {
            Burst(); // 時間が来たら、集まった積み木を一斉に射出する
            Destroy(gameObject);
            return;
        }

        Vector3 center = transform.position;

        foreach (var col in Physics.OverlapSphere(center, radius))
        {
            if (!TryGetBlock(col, center, out var block)) continue;

            Vector3 toCenter = center - col.transform.position;
            toCenter.y = 0f;
            float dist = toCenter.magnitude;

            if (block.isFlying)
            {
                // 飛んでいる積み木：中心に着いたら止める。そのあとは止まっている積み木と同じ扱いになる
                if (dist <= deadZone)
                {
                    block.Bounce(block.Direction, 0f); // 速度が0になり、BlockController側で飛行が終わる
                    continue;
                }
                PullFlyingBlock(block, toCenter / dist, Falloff(dist));
                continue;
            }

            var rb = col.attachedRigidbody;
            if (rb == null) continue;

            // 中心に十分近いときは、引き寄せずに水平方向の動きを弱めて、その場に留める
            // (これがないと、中心を行き来して震え続ける)
            if (dist <= deadZone)
            {
                Vector3 v = rb.linearVelocity;
                v.x *= 0.5f;
                v.z *= 0.5f;
                rb.linearVelocity = v;
                continue;
            }

            // 質量に関係なく同じ加速度で動かすため、ForceMode.Accelerationを使う
            rb.AddForce(toCenter / dist * (strength * Falloff(dist)), ForceMode.Acceleration);
        }
    }

    // 井戸の影響を受ける積み木か調べる(Blockタグ、BlockControllerあり、井戸と同じ高さ)
    bool TryGetBlock(Collider col, Vector3 center, out BlockController block)
    {
        block = null;
        if (!col.CompareTag("Block")) return false;

        // 井戸と同じ高さの積み木だけを対象にする(積み上げの上の段は無視する)
        if (Mathf.Abs(col.bounds.center.y - center.y) > col.bounds.extents.y) return false;

        block = col.GetComponent<BlockController>();
        return block != null;
    }

    // 中心で1、範囲の端でedgeRatioになるように、直線的に変化させる
    float Falloff(float dist) => Mathf.Lerp(edgeRatio, 1f, 1f - dist / radius);

    // 飛んでいる積み木を中心へ引き寄せる。
    // BlockControllerは毎ステップ速度を上書きするので、AddForceではなく、向きと速さを直接変える
    void PullFlyingBlock(BlockController block, Vector3 dirToCenter, float falloff)
    {
        float cs = block.CurrentSpeed;
        if (cs <= 0.0001f) return;

        // 今の速度ベクトルに、中心へ向かう加速度を足す
        Vector3 vel = block.Direction * cs + dirToCenter * (strength * falloff) * Time.fixedDeltaTime;
        vel.y = 0f;
        if (vel.sqrMagnitude < 0.0001f) return;

        // 速さは今の速さ以下に抑える(加速はせず、向きだけを中心へ寄せる)
        float newSpeed = Mathf.Min(vel.magnitude, cs);
        block.Bounce(vel, newSpeed / cs);
    }

    // 井戸の中にいる止まっている積み木を、中心から外側へ、等間隔の角度で一斉に射出する
    void Burst()
    {
        Vector3 center = transform.position;

        var blocks = new List<BlockController>();
        foreach (var col in Physics.OverlapSphere(center, radius))
        {
            if (!TryGetBlock(col, center, out var block) || block.isFlying) continue;
            if (!blocks.Contains(block)) blocks.Add(block);
        }
        if (blocks.Count == 0) return;

        // 中心から見た角度の順に並べる。この順に角度を割り当てると、射出した積み木の軌道が交差しにくい
        blocks.Sort((a, b) => AngleOf(a, center).CompareTo(AngleOf(b, center)));
        float startAngle = AngleOf(blocks[0], center);
        float step = 360f / blocks.Count;

        // 射出する仲間全員を、お互いを攻撃として扱わない相手として登録する
        // (中心では積み木同士が触れているので、このままだと射出の瞬間に壊れてしまう)
        var group = new HashSet<BlockController>(blocks);

        for (int i = 0; i < blocks.Count; i++)
        {
            float rad = (startAngle + step * i) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

            blocks[i].SetHitImmunity(group, burstImmunity);
            blocks[i].Launch(dir);
        }

        Debug.Log($"[GravityWell] {blocks.Count}個を一斉に射出", this); // テスト用
    }

    // 中心から見た、積み木の角度(度)
    static float AngleOf(BlockController block, Vector3 center)
    {
        Vector3 p = block.transform.position - center;
        return Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg;
    }

    // テスト用：シーンビューで、井戸の範囲を確認するための表示
    void OnDrawGizmos()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}