using UnityEngine;

// 発射した瞬間から弧を描いて飛び、戻り先(または発射位置)へ戻ってくる積み木。
// 戻るまでは、他の積み木に当たっても壊れず、自分の軌道も変わらない
public class BoomerangBehavior : NormalBehavior
{
    readonly BlockController owner;
    readonly Transform returnTarget;  // 戻る先。nullなら発射した位置に戻る
    readonly float radius;            // 弧の半径(いちばん遠くなる距離は、およそ半径の2倍)
    readonly float side;              // 曲がる向き(-1:左 / +1:右)
    readonly float arriveDistance;    // 戻り先にこの距離まで近づいたら「戻った」とみなす

    const float OutwardAngle = 180f;  // この角度だけ曲がったら、戻り先を向き始める

    bool initialized;
    Vector3 returnPoint; // returnTargetがない場合の戻り先(発射した位置)
    float turnedAngle;   // ここまでに曲がった合計の角度(度)
    bool returned;       // 戻ったかどうか

    public BoomerangBehavior(BlockController owner, Transform returnTarget = null,
                             float radius = 1.5f, bool curveLeft = true,
                             float arriveDistance = 0.8f)
    {
        this.owner = owner;
        this.returnTarget = returnTarget;
        this.radius = radius;
        this.side = curveLeft ? -1f : 1f;
        this.arriveDistance = arriveDistance;

        // 飛行が終わって止まったら、次の飛行に備えて状態をリセットする
        owner.OnFlightEnded += ResetState;
    }

    void ResetState()
    {
        initialized = false;
        turnedAngle = 0f;
        returned = false;
    }

    public override Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
    {
        // 最初の1回だけ、発射した位置を記録する
        if (!initialized)
        {
            initialized = true;
            returnPoint = owner.transform.position;
        }

        // 戻ったあとは、向きを変えず直進する
        if (returned) return currentDirection;

        // 1ステップで曲がれる角度。半径を一定に保つため、現在の速さに比例させる
        // (角速度 = 速さ ÷ 半径)
        float maxDeg = owner.CurrentSpeed / radius * Mathf.Rad2Deg * deltaTime;

        float step;
        if (turnedAngle < OutwardAngle)
        {
            // 行き：発射の瞬間から、一定の曲率で曲がり続ける
            step = side * maxDeg;
        }
        else
        {
            // 帰り：折り返したあとは、戻り先へ向かって曲がる
            Vector3 goal = (returnTarget != null) ? returnTarget.position : returnPoint;
            Vector3 toGoal = goal - owner.transform.position;
            toGoal.y = 0f;

            // 十分近づいたら「戻った」とみなして、旋回をやめる
            if (toGoal.magnitude <= arriveDistance)
            {
                returned = true;
                return currentDirection;
            }

            float angle = Vector3.SignedAngle(currentDirection, toGoal, Vector3.up);

            // 戻り先がほぼ真後ろにあるときは、曲がる向きが急に逆転しないよう、行きと同じ側に回す
            if (Mathf.Abs(angle) > 170f) angle = side * Mathf.Abs(angle);

            step = Mathf.Clamp(angle, -maxDeg, maxDeg);
        }

        turnedAngle += Mathf.Abs(step);
        return Quaternion.AngleAxis(step, Vector3.up) * currentDirection;
    }

    public override bool OnBlockHit(BlockController self, BlockController other)
    {
        // 戻ったあとは、通常の積み木と同じ挙動に戻る
        if (returned) return base.OnBlockHit(self, other);

        // 戻るまでは、相手だけを飛ばして、自分は壊れず軌道も変えない
        self.KnockOff(other, other.transform.position - self.transform.position, self.CurrentSpeed);
        return false;
    }
}