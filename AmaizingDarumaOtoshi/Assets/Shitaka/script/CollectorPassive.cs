using System;
using System.Collections.Generic;
using UnityEngine;

// 本体の一定範囲内にある積み木を、本体へ引き寄せて回収するパッシブ。
// 本体が止まっている間だけ働く。
public class CollectorPassive : MonoBehaviour
{
    BlockController owner;
    Collider myCollider;
    float range;
    float strength;         // 引き寄せる加速度
    float edgeRatio;        // 範囲の端での強さ(本体のすぐ近くを1とした割合)
    float collectDistance;  // この距離まで近づいたら回収する
    float flyingPullRatio;  // 飛んでいる積み木への引力の割合(0で影響なし、1で地面と同じ)

    // 回収されたときに通知する。プレイヤー側が購読して、手持ちの積み木に加える
    public event Action<BlockController> OnCollected;

    public static CollectorPassive Attach(BlockController owner, float range, float strength,
                                       float edgeRatio, float collectDistance, float flyingPullRatio)
    {
        // SetBehaviorTypeで作り直されたときに、古いものが残らないようにする
        foreach (var old in owner.GetComponents<CollectorPassive>())
        {
            old.enabled = false;
            Destroy(old);
        }

        var p = owner.gameObject.AddComponent<CollectorPassive>();
        p.owner = owner;
        p.myCollider = owner.GetComponent<Collider>();
        p.range = range;
        p.strength = strength;
        p.edgeRatio = edgeRatio;
        p.collectDistance = collectDistance;
        p.flyingPullRatio = flyingPullRatio;
        return p;
    }

    float debugNextLogTime; // テスト用:ログを出す間隔の管理

    void FixedUpdate()
    {
        // テスト用:1秒に1回だけログを出す
        bool log = Time.time >= debugNextLogTime;
        if (log) debugNextLogTime = Time.time + 1f;

        if (owner == null)
        {
            if (log) Debug.Log("[Collector] ownerがnull", this);
            return;
        }

        // 本体が飛んでいる間は働かない
        if (owner.isFlying)
        {
            if (log) Debug.Log("[Collector] 本体が飛行中のため停止", this);
            return;
        }

        Vector3 center = transform.position;
        var handled = new HashSet<BlockController>();
        var cols = Physics.OverlapSphere(center, range);

        if (log) Debug.Log($"[Collector] 範囲内のコライダー数: {cols.Length} range={range} strength={strength}", this);

        foreach (var col in cols)
        {
            if (!col.CompareTag("Block")) continue;

            var block = col.GetComponent<BlockController>();
            if (block == null || block == owner) continue;
            if (!handled.Add(block)) continue;

            // 他のコレクターとは引き合わない(お互いに引き合って震えるのを防ぐ)
            if (block.Behavior is CollectorBehavior) continue;

            // 同じ高さの積み木だけを対象にする(積み上げの上の段は無視する)
            float dyDiff = Mathf.Abs(col.bounds.center.y - myCollider.bounds.center.y);
            if (dyDiff > col.bounds.extents.y)
            {
                if (log) Debug.Log($"[Collector] 高さ判定で除外: {col.name} dy={dyDiff} 許容={col.bounds.extents.y}", this);
                continue;
            }

            Vector3 toCenter = center - col.transform.position;
            toCenter.y = 0f;
            float dist = toCenter.magnitude;
            if (dist < 0.0001f) continue;
            Vector3 dir = toCenter / dist;
            float falloff = Falloff(dist);

            if (log) Debug.Log($"[Collector] 対象: {col.name} dist={dist:F2} falloff={falloff:F2} rb={(col.attachedRigidbody != null)} flying={block.isFlying}", this);

            if (block.isFlying)
            {
                PullFlyingBlock(block, dir, falloff);
                continue; // 飛んでいる積み木は回収しない
            }

            // 十分近づいたら回収する
            if (dist <= collectDistance)
            {
                Collect(block);
                continue;
            }

            var rb = col.attachedRigidbody;
            if (rb == null) continue;

            // 質量に関係なく同じ加速度で動かす
            rb.AddForce(dir * (strength * falloff), ForceMode.Acceleration);
        }
    }

    // 近いほど強く、範囲の端でedgeRatioになる
    float Falloff(float dist) => Mathf.Lerp(edgeRatio, 1f, 1f - Mathf.Clamp01(dist / range));

    // 飛んでいる積み木の向きを、弱く本体へ寄せる(加速はしない)
    void PullFlyingBlock(BlockController block, Vector3 dirToCenter, float falloff)
    {
        float cs = block.CurrentSpeed;
        if (cs <= 0.0001f) return;

        Vector3 vel = block.Direction * cs
                    + dirToCenter * (strength * falloff * flyingPullRatio) * Time.fixedDeltaTime;
        vel.y = 0f;
        if (vel.sqrMagnitude < 0.0001f) return;

        float newSpeed = Mathf.Min(vel.magnitude, cs);
        block.Bounce(vel, newSpeed / cs);
    }

    void Collect(BlockController block)
    {
        OnCollected?.Invoke(block);
        Destroy(block.gameObject); // 手持ちに戻った扱い。残したい場合はここを変える
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, range);
    }
}