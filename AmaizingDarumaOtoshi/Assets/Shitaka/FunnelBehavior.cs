using UnityEngine;
using System.Collections.Generic;

// 必殺技：打ち出した1発が、元の積み木の左右に分身を出し、全員が同じ積み木をホーミングする
// 分裂したもの同士は衝突するが、当たっても壊れない
public class FunnelBehavior : HomingBehavior
{
    readonly BlockController ownerBlock;
    readonly FunnelTargetSelector funnelSelector; // 本体と分身の全員で共有する
    readonly List<BlockController> group;         // 本体と分身(セレクターと同じリスト)
    readonly bool isLeader;                       // 本体だけが分裂を行う
    readonly int count;                           // 分裂後の総数(本体を含む)
    readonly float spawnSpacing;                  // 分身を並べる間隔(積み木の幅に対する倍率)
    readonly float startTurnSpeed;
    readonly float guidanceDuration;

    bool split;

    // 本体用。プレイヤー側や生成側は、こちらを使う
    public FunnelBehavior(BlockController owner, int count = 3, float spawnSpacing = 1.2f,
                          float searchRadius = 15f, float startTurnSpeed = 270f,
                          float guidanceDuration = 3f)
        : this(owner,
               new FunnelTargetSelector(new List<BlockController> { owner }, searchRadius),
               true, count, spawnSpacing, startTurnSpeed, guidanceDuration)
    { }

    // 内部用。本体と分身の両方がここを通る。セレクターは外で1つだけ作り、全員で共有する
    FunnelBehavior(BlockController owner, FunnelTargetSelector selector, bool isLeader,
                   int count, float spawnSpacing, float startTurnSpeed, float guidanceDuration)
        : base(owner, selector, startTurnSpeed, guidanceDuration)
    {
        this.ownerBlock = owner;
        this.funnelSelector = selector;
        this.group = selector.Group;
        this.isLeader = isLeader;
        this.count = count;
        this.spawnSpacing = spawnSpacing;
        this.startTurnSpeed = startTurnSpeed;
        this.guidanceDuration = guidanceDuration;
    }

    public override Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
    {
        // 本体は、最初の1回だけ分裂する(発射された直後)
        if (isLeader && !split)
        {
            split = true;
            Transform target = Split(currentDirection);

            // 本体も、自分の位置からターゲットへまっすぐ向ける(旋回で遠回りしないようにする)
            currentDirection = DirectionToTarget(ownerBlock.transform.position, target, currentDirection);
        }
        return base.UpdateDirection(currentDirection, deltaTime); // 以降は普通のホーミング
    }

    // 分身を作って発射する。全員が狙うターゲットを返す(見つからなければnull)
    Transform Split(Vector3 baseDir)
    {
        Vector3 origin = ownerBlock.transform.position;

        // 進行方向に対して右向きの単位ベクトル(分身を左右に並べるために使う)
        Vector3 right = Vector3.Cross(Vector3.up, baseDir).normalized;

        // 積み木の幅(分身どうしや本体と重ならない間隔を決めるために使う)
        Bounds b = ownerBlock.GetComponent<Collider>().bounds;
        float width = Mathf.Max(b.size.x, b.size.z);

        // 全員が狙うターゲットを、ここで先に決めておく(共有されるので、本体も同じ相手を使う)
        Transform target = funnelSelector.SelectTarget(ownerBlock);

        for (int i = 1; i < count; i++)
        {
            // 右、左、右、左…の順に、本体から外側へ並べる
            //  i=1:右1つ目 / i=2:左1つ目 / i=3:右2つ目 / i=4:左2つ目 …
            float side = (i % 2 == 1) ? 1f : -1f;
            int rank = (i + 1) / 2;
            float offset = side * rank * spawnSpacing * width;
            Vector3 pos = origin + right * offset;

            // 壁の向こうに出さない。間に壁があれば、壁の手前に寄せる
            if (Physics.Linecast(origin, pos, out var hit) && hit.collider.CompareTag("Wall"))
            {
                pos = hit.point - (pos - origin).normalized * (width * 0.5f);
            }

            var go = UnityEngine.Object.Instantiate(ownerBlock.gameObject, pos,
                         ownerBlock.transform.rotation);
            var clone = go.GetComponent<BlockController>();

            // 分身は、同じセレクターを共有する(=全員が同じ相手を狙う)
            clone.SetBehaviorInstance(new FunnelBehavior(clone, funnelSelector, false,
                count, spawnSpacing, startTurnSpeed, guidanceDuration));

            // 仲間(本体と、先に作った分身)とは衝突しない。壁や他の積み木とは普通に衝突する
            //var cloneCol = go.GetComponent<Collider>();
            //foreach (var member in group)
            //    Physics.IgnoreCollision(cloneCol, member.GetComponent<Collider>());
            group.Add(clone);

            // 分身自身の位置から、ターゲットへまっすぐ向かう(最短)。ターゲットがなければ打ち出した向き
            Vector3 dir = DirectionToTarget(pos, target, baseDir);

            clone.DestroyWhenFlightEnds(); // 止まったら消える
            clone.Launch(dir, ownerBlock.CurrentSpeed);
        }
        Debug.Log($"[Funnel] {count}発に分裂", ownerBlock); // テスト用
        return target;
    }

    // fromからターゲットへの水平方向の向きを返す。ターゲットがない、または真上などで向きが決まらない場合はfallback
    static Vector3 DirectionToTarget(Vector3 from, Transform target, Vector3 fallback)
    {
        if (target == null) return fallback;
        Vector3 to = target.position - from;
        to.y = 0f;
        return (to.sqrMagnitude > 0.0001f) ? to.normalized : fallback;
    }

    // 念のため：仲間同士で当たった場合も、何もせず壊れないようにする
    public override bool OnBlockHit(BlockController self, BlockController other)
    {
        if (group.Contains(other)) return false;
        return base.OnBlockHit(self, other);
    }
}