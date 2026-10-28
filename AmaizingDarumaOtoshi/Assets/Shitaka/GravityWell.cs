using UnityEngine;

// 着弾地点に生成され、一定時間、周囲の止まっている積み木を中心へ引き寄せる
public class GravityWell : MonoBehaviour
{
    float radius;
    float strength;   // 引き寄せる加速度
    float duration;   // 井戸が残る時間(秒)
    float deadZone;   // 中心にこの距離まで近づいたら、引き寄せるのをやめて止める
    float elapsed;
    float edgeRatio;  // 範囲の端での強さ(中心を1としたときの割合)。0.4なら端でも中心の40%

    public static GravityWell Create(Vector3 position, float radius, float strength,
                                     float duration, float deadZone, float edgeRatio = 0.4f)
    {
        var go = new GameObject("GravityWell");
        go.transform.position = position;

        var well = go.AddComponent<GravityWell>();
        well.radius = radius;
        well.strength = strength;
        well.duration = duration;
        well.deadZone = deadZone;
        well.edgeRatio = edgeRatio;
        return well;
    }

    void FixedUpdate()
    {
        elapsed += Time.fixedDeltaTime;
        if (elapsed >= duration)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 center = transform.position;

        foreach (var col in Physics.OverlapSphere(center, radius))
        {
            if (!col.CompareTag("Block")) continue;

            var block = col.GetComponent<BlockController>();
            if (block == null || block.isFlying) continue; // 飛んでいる積み木は対象外

            var rb = col.attachedRigidbody;
            if (rb == null) continue;

            // 井戸と同じ高さの積み木だけを対象にする(積み上げの上の段は無視する)
            if (Mathf.Abs(col.bounds.center.y - center.y) > col.bounds.extents.y) continue;

            Vector3 toCenter = center - col.transform.position;
            toCenter.y = 0f;
            float dist = toCenter.magnitude;

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

            // 中心で1、範囲の端でedgeRatioになるように、直線的に変化させる
            float falloff = Mathf.Lerp(edgeRatio, 1f, 1f - dist / radius);

            // 質量に関係なく同じ加速度で動かすため、ForceMode.Accelerationを使う
            rb.AddForce(toCenter / dist * (strength * falloff), ForceMode.Acceleration);
        }
    }

    // テスト用：シーンビューで、井戸の範囲を確認するための表示
    void OnDrawGizmos()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}