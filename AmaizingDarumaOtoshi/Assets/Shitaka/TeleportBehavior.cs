using System.Collections;
using UnityEngine;

// 任意のタイミングで、進行方向へテレポートする積み木。
// ボタンを押した瞬間に、ワープ先へ残像を出し、積み木は発光(予備動作)してからワープする。
// ステージの外へ出ないよう、壁・重なり・地面の3つを確認する。速度と向きは維持される。
// 壁や積み木に当たったときの挙動は、NormalBehaviorと同じ
public class TeleportBehavior : NormalBehavior, IActivatableBehavior
{
    readonly BlockController owner;
    readonly Rigidbody rb;
    readonly Collider col;
    readonly Renderer rend;
    readonly float distance;          // テレポートする距離
    readonly int maxUses;             // 1回の飛行で使える回数
    readonly float wallMargin;        // 壁との間に空ける余白
    readonly float groundCheckDepth;  // 着地点の下に、地面があるか調べる深さ
    readonly float windUpTime;        // 予備動作(発光)の長さ(秒)
    readonly float ghostLifetime;     // ワープ先の残像が消えるまでの時間(秒)
    readonly float ghostAlpha;        // 残像の最初の濃さ
    readonly Material ghostMaterial;  // 残像用の半透明マテリアル(nullなら本体のマテリアルを複製する)
    readonly float ghostExpand;       // 残像が消えるまでに、積み木の何倍の大きさまで広がるか
    readonly float trailSpacing;      // 軌跡の残像を並べる間隔(積み木の幅に対する倍率)

    const int MaxTrailGhosts = 12;    // 軌跡の残像の最大数(長距離でも増えすぎないようにする)
    const float MinDistance = 0.5f;   // これより短くしか動けないなら、テレポートしない
    const int SearchSteps = 8;        // 距離を縮めて探し直す回数
    static readonly Color FlashColor = Color.white; // 予備動作で混ぜる色

    int used;
    bool winding; // 予備動作中か

    public TeleportBehavior(BlockController owner, float distance = 4f, int maxUses = 1,
                            float wallMargin = 0.1f, float groundCheckDepth = 1.5f,
                            float windUpTime = 0.15f, float ghostLifetime = 0.3f,
                            float ghostAlpha = 0.5f, Material ghostMaterial = null,
                            float ghostExpand = 1.6f, float trailSpacing = 1.2f)
    {
        this.owner = owner;
        this.rb = owner.GetComponent<Rigidbody>();
        this.col = owner.GetComponent<Collider>();
        this.rend = owner.GetComponent<Renderer>();
        this.distance = distance;
        this.maxUses = maxUses;
        this.wallMargin = wallMargin;
        this.groundCheckDepth = groundCheckDepth;
        this.windUpTime = windUpTime;
        this.ghostLifetime = ghostLifetime;
        this.ghostAlpha = ghostAlpha;
        this.ghostMaterial = ghostMaterial;
        this.ghostExpand = ghostExpand;
        this.trailSpacing = trailSpacing;

        // 飛行が終わったら、次の飛行でまた使えるようにする
        owner.OnFlightEnded += () => used = 0;
    }

    // プレイヤー側のボタン入力から呼ばれる
    public void Activate()
    {
        if (winding || !owner.isFlying || used >= maxUses) return;

        // 押した時点で、ワープ先を決める。行ける場所がないなら、光らせずに何もしない
        if (!TryFindDestination(out float d, out Vector3 dir))
        {
            Debug.Log("[Teleport] 移動できる場所がない", owner); // テスト用
            return;
        }

        Vector3 dest = rb.position + dir * d;
        owner.StartCoroutine(WindUpAndTeleport(dest, dir));
    }

    // ワープ先に残像を出しながら発光して、予備動作が終わったら、その残像の場所へワープする
    IEnumerator WindUpAndTeleport(Vector3 dest, Vector3 dir)
    {
        winding = true;
        Color original = (rend != null) ? rend.material.color : Color.white;

        // 発光の開始と同時に、ワープ先へ残像を出す(色は発光前の本体の色)
        FadingGhost.Create(owner, dest, ghostLifetime, ghostAlpha, original, ghostMaterial, ghostExpand);

        // あわせて、今の位置からワープ先までの軌跡にも残像を並べる
        SpawnTrail(rb.position, dest, original);

        float t = 0f;
        while (t < windUpTime)
        {
            // 予備動作の途中で止まったら、ワープせず(回数も消費せず)、色を戻して終わる
            if (!owner.isFlying)
            {
                RestoreColor(original);
                winding = false;
                yield break;
            }

            t += Time.deltaTime;
            if (rend != null)
                rend.material.color = Color.Lerp(original, FlashColor, t / windUpTime);
            yield return null;
        }

        // 積み木は予備動作のあいだも進んでいるが、行き先は残像の場所のまま。
        // 着地できる状態か確認してから、位置だけを動かす(向きと速さは変えないので、そのまま維持される)
        if (owner.isFlying && CanLandAt(dest, dir))
        {
            rb.position = dest;
            owner.transform.position = dest;
            used++;

            Debug.Log($"[Teleport] ワープ ({used}/{maxUses}回目)", owner); // テスト用
        }
        else
        {
            Debug.Log("[Teleport] ワープ先に着地できず、中止", owner); // テスト用
        }

        RestoreColor(original);
        winding = false;
    }

    void RestoreColor(Color original)
    {
        if (rend != null) rend.material.color = original;
    }

    // 予備動作のあいだに状況が変わっていても、安全に着地できるか確認する
    bool CanLandAt(Vector3 dest, Vector3 dir)
    {
        Vector3 toDest = dest - rb.position;
        toDest.y = 0f;

        // すでに行き先を追い越していたら、後ろへは戻らない
        if (Vector3.Dot(toDest, dir) <= 0f) return false;

        // 今の位置から行き先までの間に、壁がないか(予備動作中に反射して、向きが変わった場合の対策)
        float dist = toDest.magnitude;
        foreach (var h in rb.SweepTestAll(toDest.normalized, dist, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.CompareTag("Wall")) return false;
        }

        // 行き先に、壁や他の積み木が入り込んでいないか、地面があるか
        Bounds b = col.bounds;
        Vector3 center = b.center + (dest - rb.position);
        Vector3 half = b.extents * 0.95f;
        return IsFree(center, half) && HasGround(center, b.extents.y);
    }

    // ワープできる距離と向きを探す。①壁の手前まで ②着地点が空いている ③下に地面がある、を満たす一番遠い場所
    bool TryFindDestination(out float foundDistance, out Vector3 dir)
    {
        foundDistance = 0f;
        dir = owner.Direction;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return false;
        dir.Normalize();

        // ① 進行方向へスイープして、最初の壁の手前までに距離を制限する
        float maxDist = distance;
        foreach (var h in rb.SweepTestAll(dir, distance + wallMargin, QueryTriggerInteraction.Ignore))
        {
            if (!h.collider.CompareTag("Wall")) continue;
            maxDist = Mathf.Min(maxDist, Mathf.Max(0f, h.distance - wallMargin));
        }

        // ②③ 着地点が空いていて、下に地面がある場所を、遠い方から探す
        Bounds b = col.bounds;
        Vector3 half = b.extents * 0.95f; // 床や隣の積み木に、かすって引っかからないよう少し小さくする

        for (int i = 0; i <= SearchSteps; i++)
        {
            float d = maxDist * (1f - (float)i / SearchSteps);
            if (d < MinDistance) break;

            Vector3 center = b.center + dir * d;
            if (!IsFree(center, half) || !HasGround(center, b.extents.y)) continue;

            foundDistance = d;
            return true;
        }
        return false;
    }

    // 着地点に、壁や他の積み木が重なっていないか
    bool IsFree(Vector3 center, Vector3 half)
    {
        foreach (var c in Physics.OverlapBox(center, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.attachedRigidbody == rb) continue; // 自分自身は除く
            if (c.CompareTag("Wall") || c.CompareTag("Block")) return false;
        }
        return true;
    }

    // 着地点の下に、地面があるか(ステージの端から落ちないようにする)
    bool HasGround(Vector3 center, float halfHeight)
    {
        float reach = halfHeight + groundCheckDepth;
        foreach (var h in Physics.RaycastAll(center, Vector3.down, reach, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.attachedRigidbody == rb) continue; // 自分自身は除く
            return true;
        }
        return false;
    }

    // 出発点からワープ先までの直線上に、積み木の幅ごとに残像を並べる。ワープ先に近いほど濃い
    void SpawnTrail(Vector3 start, Vector3 dest, Color color)
    {
        Vector3 span = dest - start;
        float dist = span.magnitude;

        Bounds b = col.bounds;
        float spacing = Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z) * trailSpacing);
        int count = Mathf.Min(MaxTrailGhosts, Mathf.FloorToInt(dist / spacing));

        // i=0は積み木自身の位置、i=countはワープ先(別に出しているので除く)
        for (int i = 1; i < count; i++)
        {
            float t = (float)i / count;
            float alpha = Mathf.Lerp(ghostAlpha * 0.2f, ghostAlpha, t); // ワープ先に近いほど濃くする
            FadingGhost.Create(owner, start + span * t, ghostLifetime, alpha, color, ghostMaterial, 1f);
        }
    }

    // 予備動作中(ボタンを押してからワープするまで)は、他の積み木に当たっても壊れない
    public override bool OnBlockHit(BlockController self, BlockController other)
    {
        if (winding)
        {
            base.OnBlockHit(self, other); // 当たった相手だけ、通常通り飛ばす(戻り値は使わない)
            return false;                 // 自分は壊れない
        }
        return base.OnBlockHit(self, other); // 予備動作中でなければ、通常と同じ
    }
}