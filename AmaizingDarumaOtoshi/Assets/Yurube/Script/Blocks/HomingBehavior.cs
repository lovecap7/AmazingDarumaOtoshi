// 志鷹さんの Assets/Shitaka から複製（由留部の作業用）
// 元のクラスと名前が衝突しないよう、名前空間 Yurube.Blocks に入れている
using UnityEngine;

namespace Yurube.Blocks
{
public class HomingBehavior : NormalBehavior
{
    readonly BlockController owner;
    readonly IHomingTargetSelector selector; // ターゲットの選び方(差し替え可能)
    // readonly float turnSpeed;                // 1秒あたりに向きを変えられる最大角度
    readonly float startTurnSpeed;  // 発射直後の旋回速度(度/秒)
    readonly float guidanceDuration; // 誘導が完全に切れるまでの時間(秒)
    Transform target;
    float elapsed;           // 発射してからの経過時間(飛行中だけ加算される)
    bool guidanceEnded;      // 誘導終了のログを一度だけ出すためのフラグ

    // テスト用：ターゲット選択のログを出すかどうか
    static bool debugLog = true;

    public HomingBehavior(BlockController owner, IHomingTargetSelector selector,
                          float startTurnSpeed = 270f, float guidanceDuration = 1.5f)
    {
        this.owner = owner;
        this.selector = selector;
        this.startTurnSpeed = startTurnSpeed;
        this.guidanceDuration = guidanceDuration;
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
        elapsed += deltaTime;

        // 誘導時間が過ぎたら、以降はターゲットを探さず直進する
        if (elapsed >= guidanceDuration)
        {
            if (!guidanceEnded)
            {
                guidanceEnded = true;
                if (debugLog) Debug.Log($"[Homing] {owner.name} 誘導終了", owner);
            }
            return currentDirection;
        }

        // ターゲット未取得、または破壊されて消えた場合は選び直す
        // if (target == null) target = selector.SelectTarget(owner); // ログ出力のためSetTargetに変更
        if (target == null) SetTarget(selector.SelectTarget(owner));
        if (target == null) return currentDirection; // 見つからなければ直進

        // 水平方向のみで追尾する
        Vector3 toTarget = target.position - owner.transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return currentDirection;

        // 経過時間に応じて旋回速度を startTurnSpeed → 0 へ直線的に減らす
        float t = elapsed / guidanceDuration; // 0(発射直後)〜1(誘導終了)
        float currentTurnSpeed = Mathf.Lerp(startTurnSpeed, 0.0f, t);

        // 1ステップで曲がれる角度に制限しながらターゲットの方向へ回す
        float maxRadians = currentTurnSpeed * Mathf.Deg2Rad * deltaTime;
        return Vector3.RotateTowards(currentDirection, toTarget.normalized, maxRadians, 0.0f);
    }
}
}
