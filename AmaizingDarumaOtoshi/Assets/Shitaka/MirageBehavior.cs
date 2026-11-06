using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MirageBehavior : NormalBehavior, IActivatableBehavior
{
    readonly BlockController owner;
    readonly Rigidbody rb;
    readonly Collider col;
    readonly int cloneCount;   // 作る分身の数
    readonly float lifetime;   // 分身が消えるまでの時間(秒)
    readonly float spacing;    // 分身を並べる間隔(積み木の幅に対する倍率)
    readonly float cloneTint;  // 分身をどれだけ暗くするか(0〜1。0.15なら明るさが15%下がる)

    // 本体(0番目)と、作った分身。当たり判定を無視する相手と、入れ替えの相手を管理する
    readonly List<BlockController> group = new List<BlockController>();

    bool spawned;
    int nextSwap; // 次に入れ替える分身の順番

    const float MinSpacingRatio = 1.0f; // 本体に近づけて探すときでも、最低このくらい(積み木の幅に対する倍率)は離す
    const int SpawnSearchSteps = 3;     // 出現位置を探し直す回数

    public MirageBehavior(BlockController owner, int cloneCount = 2, float lifetime = 4f, float spacing = 1.5f, float cloneTint = 0.15f)
    {
        this.owner = owner;
        this.rb = owner.GetComponent<Rigidbody>();
        this.col = owner.GetComponent<Collider>();
        this.cloneCount = cloneCount;
        this.lifetime = lifetime;
        this.spacing = spacing;
        this.cloneTint = cloneTint;
    }

    public override Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
    {
        // 打ち出された最初の1回だけ、分身を作る
        if (!spawned)
        {
            spawned = true;
            SpawnClones(currentDirection);
        }
        return currentDirection; // 向きは変えない(本体は通常の動き)
    }

    void SpawnClones(Vector3 dir)
    {
        Vector3 origin = owner.transform.position;
        Bounds b = col.bounds;

        // 進行方向に対して右向きの単位ベクトル(分身を左右に並べるために使う)
        Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
        float width = Mathf.Max(b.size.x, b.size.z);
        Vector3 half = b.extents * 0.95f; // 床や隣の積み木に、かすって引っかからないよう少し小さくする

        group.Clear();
        group.Add(owner);

        for (int i = 0; i < cloneCount; i++)
        {
            // 右、左、右、左…の順に、本体から外側へ並べる
            float sign = (i % 2 == 0) ? 1f : -1f;
            int rank = i / 2 + 1;

            if (!TryFindSpawnPosition(origin, b.center, right * sign, rank * spacing * width,
                                      width, half, out Vector3 pos))
                continue; // 空いている場所がなければ、この分身は作らない

            var go = UnityEngine.Object.Instantiate(owner.gameObject, pos, owner.transform.rotation);
            var clone = go.GetComponent<BlockController>();

            // 分身は通常の動きにする(分裂と入れ替えは本体だけの能力)
            clone.SetBehaviorInstance(new NormalBehavior());
            // 本体と見分けられるよう、分身の色をわずかに暗くする
            TintClone(go);

            // 本体・他の分身とは当たらない。それ以外(壁や他の積み木)とは普通に当たる
            var cloneCol = go.GetComponent<Collider>();
            foreach (var member in group)
                Physics.IgnoreCollision(cloneCol, member.GetComponent<Collider>());
            group.Add(clone);

            clone.DestroyWhenFlightEnds();                  // 止まったら消える
            clone.StartCoroutine(Expire(clone, lifetime));  // 時間が来たら消える
            clone.Launch(dir, owner.CurrentSpeed);          // 本体と同じ向き・速さで飛ばす
        }

        Debug.Log($"[Clone] 分身を{group.Count - 1}体作成", owner); // テスト用
    }

    // 本体から横へ離れた、分身を置ける場所を探す。壁や他の積み木に重なるなら、本体に近づけて探し直す
    bool TryFindSpawnPosition(Vector3 origin, Vector3 boundsCenter, Vector3 sideDir,
                              float fullOffset, float width, Vector3 half, out Vector3 pos)
    {
        float minOffset = Mathf.Min(fullOffset, width * MinSpacingRatio);

        for (int i = 0; i < SpawnSearchSteps; i++)
        {
            float offset = Mathf.Lerp(fullOffset, minOffset, i / (float)(SpawnSearchSteps - 1));
            pos = origin + sideDir * offset;

            // 本体との間に壁があれば、壁の向こう側に出てしまうので、この距離は使わない
            if (Physics.Linecast(origin, pos, out var hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.CompareTag("Wall"))
                continue;

            if (IsSpawnSpotFree(boundsCenter + sideDir * offset, half)) return true;
        }

        pos = origin;
        return false;
    }

    // 出現位置に、壁や他の積み木が重なっていないか
    bool IsSpawnSpotFree(Vector3 center, Vector3 half)
    {
        foreach (var c in Physics.OverlapBox(center, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.attachedRigidbody == rb) continue; // 本体は除く
            if (c.CompareTag("Wall")) return false;

            if (c.CompareTag("Block"))
            {
                var block = c.GetComponent<BlockController>();
                if (block != null && group.Contains(block)) continue; // 先に作った分身は除く
                return false;
            }
        }
        return true;
    }

    // 時間が来たら、分身を消す(分身自身のコルーチンなので、本体が先に壊れても動き続ける)
    static IEnumerator Expire(BlockController clone, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (clone != null) clone.Break();
    }

    // プレイヤー側のボタン入力から呼ばれる：本体と分身1体の位置を入れ替える
    public void Activate()
    {
        if (!owner.isFlying) return;

        BlockController target = PickSwapTarget();
        if (target == null)
        {
            Debug.Log("[Clone] 入れ替えできる分身がいない", owner); // テスト用
            return;
        }

        var targetRb = target.GetComponent<Rigidbody>();
        Vector3 mainPos = rb.position;
        Vector3 clonePos = targetRb.position;

        // 位置だけを入れ替える。それぞれの向きと速さ(currentSpeed)は変えない
        rb.position = clonePos;
        owner.transform.position = clonePos;
        targetRb.position = mainPos;
        target.transform.position = mainPos;

        Debug.Log("[Clone] 本体と分身の位置を入れ替え", owner); // テスト用
    }

    // 生きている分身から、次に入れ替える1体を順番に選ぶ(いなければnull)
    BlockController PickSwapTarget()
    {
        int n = group.Count - 1; // 分身の数(0番目は本体)
        if (n <= 0) return null;

        for (int k = 0; k < n; k++)
        {
            int idx = 1 + (nextSwap + k) % n;
            if (group[idx] == null) continue; // すでに消えた分身は飛ばす

            nextSwap = (nextSwap + k + 1) % n;
            return group[idx];
        }
        return null;
    }

    // 分身の色の明るさを少し下げる。色相はそのままなので、赤は赤のまま見える
    void TintClone(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null || cloneTint <= 0f) return;

        // 本体とマテリアルを共有しないよう、複製してから色を変える
        var mat = new Material(r.sharedMaterial);
        Color.RGBToHSV(mat.color, out float h, out float s, out float v);
        Color c = Color.HSVToRGB(h, s, v * (1f - cloneTint));
        c.a = mat.color.a;
        mat.color = c;
        r.material = mat;
    }
}
