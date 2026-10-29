using UnityEngine;
using System.Collections.Generic;

// 必殺技：打ち出した1発が、指定した数に分裂して、全員が同じ積み木をホーミングする
// 分裂したもの同士は衝突しないので、当たっても壊れない
public class FunnelBehavior : HomingBehavior
{
    readonly BlockController ownerBlock;
    readonly FunnelTargetSelector funnelSelector; // 本体と分身の全員で共有する
    readonly List<BlockController> group;         // 本体と分身(セレクターと同じリスト)
    readonly bool isLeader;                       // 本体だけが分裂を行う
    readonly int count;                           // 分裂後の総数(本体を含む)
    readonly float spreadAngle;                   // 扇状に広がる全体の角度
    readonly float startTurnSpeed;
    readonly float guidanceDuration;

    bool split;

    // 本体用。プレイヤー側や生成側は、こちらを使う
    public FunnelBehavior(BlockController owner, int count = 3, float spreadAngle = 90f,
                          float searchRadius = 15f, float startTurnSpeed = 270f,
                          float guidanceDuration = 3f)
        : this(owner,
               new FunnelTargetSelector(new List<BlockController> { owner }, searchRadius),
               true, count, spreadAngle, startTurnSpeed, guidanceDuration)
    { }

    // 内部用。本体と分身の両方がここを通る。セレクターは外で1つだけ作り、全員で共有する
    FunnelBehavior(BlockController owner, FunnelTargetSelector selector, bool isLeader,
                   int count, float spreadAngle, float startTurnSpeed, float guidanceDuration)
        : base(owner, selector, startTurnSpeed, guidanceDuration)
    {
        this.ownerBlock = owner;
        this.funnelSelector = selector;
        this.group = selector.Group;
        this.isLeader = isLeader;
        this.count = count;
        this.spreadAngle = spreadAngle;
        this.startTurnSpeed = startTurnSpeed;
        this.guidanceDuration = guidanceDuration;
    }

    public override Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
    {
        // 本体は、最初の1回だけ分裂する(発射された直後)
        if (isLeader && !split)
        {
            split = true;
            Split(currentDirection);
            currentDirection = Rotate(currentDirection, FanOffset(0)); // 本体も扇の端の向きにする
        }
        return base.UpdateDirection(currentDirection, deltaTime); // 以降は普通のホーミング
    }

    void Split(Vector3 baseDir)
    {
        for (int i = 1; i < count; i++)
        {
            var go = UnityEngine.Object.Instantiate(ownerBlock.gameObject,
                         ownerBlock.transform.position, ownerBlock.transform.rotation);
            var clone = go.GetComponent<BlockController>();

            // 分身は、同じセレクターを共有する(=全員が同じ相手を狙う)
            clone.SetBehaviorInstance(new FunnelBehavior(clone, funnelSelector, false,
                count, spreadAngle, startTurnSpeed, guidanceDuration));

            // 仲間(本体と、先に作った分身)とは衝突しない。壁や他の積み木とは普通に衝突する
            var cloneCol = go.GetComponent<Collider>();
            foreach (var member in group)
                Physics.IgnoreCollision(cloneCol, member.GetComponent<Collider>());
            group.Add(clone);

            clone.DestroyWhenFlightEnds(); // 止まったら消える
            clone.Launch(Rotate(baseDir, FanOffset(i)), ownerBlock.CurrentSpeed);
        }
        Debug.Log($"[Funnel] {count}発に分裂", ownerBlock); // テスト用
    }

    // 番号iの発射角のずれ(度)。全体でspreadAngleの扇になる
    float FanOffset(int i)
    {
        if (count <= 1) return 0f;
        return -spreadAngle * 0.5f + spreadAngle * i / (count - 1);
    }

    static Vector3 Rotate(Vector3 dir, float deg) => Quaternion.AngleAxis(deg, Vector3.up) * dir;

    // 念のため：仲間同士で当たった場合も、何もせず壊れないようにする
    public override bool OnBlockHit(BlockController self, BlockController other)
    {
        if (group.Contains(other)) return false;
        return base.OnBlockHit(self, other);
    }
}