using UnityEngine;

public class HomingBehavior : NormalBehavior
{
    readonly BlockController owner;
    readonly IHomingTargetSelector selector; // ターゲットの選び方(差し替え可能)
    readonly float turnSpeed;                // 1秒あたりに向きを変えられる最大角度
    Transform target;

    // テスト用：ターゲット選択のログを出すかどうか
    static bool debugLog = true;

    public HomingBehavior(BlockController owner, IHomingTargetSelector selector, float turnSpeed = 90.0f)
    {
        this.owner = owner;
        this.selector = selector;
        this.turnSpeed = turnSpeed;
    }

    // ターゲットを設定する。変わったときだけログを出す
    void SetTarget(Transform newTarget)
    {
        // 同じターゲット(どちらもnullの場合を含む)なら何もしない
        if (newTarget == target) return;

        target = newTarget;

        if (!debugLog) return;

        if (target != null)
        {
            float dist = Vector3.Distance(owner.transform.position, target.position);
            // 第2引数にownerを渡すと、コンソールのログをクリックしたときにヒエラルキー上で該当の積み木が光る
            Debug.Log($"[Homing] {owner.name} のターゲット: {target.name} (距離 {dist:F2})", owner);
        }
        else
        {
            Debug.Log($"[Homing] {owner.name} のターゲットなし", owner);
        }
    }

    public override Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
    {
        // ターゲット未取得、または破壊されて消えた場合は選び直す
        // if (target == null) target = selector.SelectTarget(owner); // ログ出力のためSetTargetに変更
        if (target == null) SetTarget(selector.SelectTarget(owner));
        if (target == null) return currentDirection; // 見つからなければ直進

        // 水平方向のみで追尾する
        Vector3 toTarget = target.position - owner.transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return currentDirection;

        // 1ステップで曲がれる角度に制限しながらターゲットの方向へ回す
        float maxRadians = turnSpeed * Mathf.Deg2Rad * deltaTime;
        return Vector3.RotateTowards(currentDirection, toTarget.normalized, maxRadians, 0f);
    }
}
