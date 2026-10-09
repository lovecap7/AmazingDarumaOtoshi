using System;
using UnityEngine;

public class BlockController : MonoBehaviour 
{
    [SerializeField] float speed = 2.0f;
    [SerializeField] float deceleration = 0.2f;          // 減速量(単位/秒)。大きいほど早く止まる
    [SerializeField] float stopThreshold = 0.05f;         // この速度を下回ったら停止とみなす

    float currentSpeed; // 現在の速度(毎ステップ減衰する)

    // 現在の水平速度を外部(Behavior)から参照するための公開プロパティ
    public float CurrentSpeed => currentSpeed;

    // テスト用：インスペクターで挙動を選べるようにする
    public enum BehaviorType { Normal, Iwa,HebiLeft, HebiRight, UnBreak }
    [SerializeField] BehaviorType behaviorType = BehaviorType.Normal;
    // テスト用：へびだるの曲がる強さ(度/秒)。大きいほど急カーブになる
    [SerializeField] float hebiTurnSpeed = 60.0f;

    public bool isFlying {  get; private set; } // 現在飛行中かどうか trueの場合は攻撃判定が発生する

    // 開始時点で飛行中だったかを記録する
    // (衝突処理の途中でisFlyingが書き換わっても、判定がブレないようにするため)
    bool wasFlyingThisStep;

    bool hasHit; // 一度攻撃したら以降の衝突は無視する

    Rigidbody rb;
    BlockInfo info;
    Vector3 direction;

    // 直前の衝突の接触法線(相手から自分へ向かう向き)。IBlockBehavior実装クラスから参照する
    Vector3 lastContactNormal;
    public Vector3 LastContactNormal => lastContactNormal;

    // 同じ相手への連続ヒットを防ぐための記録
    BlockController lastHitTarget;
    float ignoreUntil;
    [SerializeField] float rehitIgnoreTime = 0.2f; // 同じ相手を無視する時間(秒)

    // キャラクターごとの挙動を差し替えるための参照。デフォルトはNormalBehavior
    //IBlockBehavior behavior = new NormalBehavior();
    IBlockBehavior behavior;

    // 現在の飛行方向。IBlockBehavior実装クラス(別スクリプト)から参照するための公開プロパティ
    public Vector3 Direction => direction;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        info = rb.GetComponent<BlockInfo>();

        // インスペクターで選んだ種類に応じてbehaviorを生成する
        behavior = CreateBehavior(behaviorType);
    }

    private void Start()
    {
        
    }

    // テスト用：選択された種類からbehaviorを生成する
    IBlockBehavior CreateBehavior(BehaviorType type)
    {
        switch (type)
        {
            case BehaviorType.Iwa: return new IwaBehavior();
            case BehaviorType.HebiLeft: return new HebiBehavior(HebiBehavior.CurveSide.Left, hebiTurnSpeed);
            case BehaviorType.HebiRight: return new HebiBehavior(HebiBehavior.CurveSide.Right, hebiTurnSpeed);
            case BehaviorType.UnBreak: return new UnBreakBehavior();
            case BehaviorType.Normal:
            default: return new NormalBehavior();
        }
    }

    // behaviorを外部(生成担当側)から差し替えるための窓口
    public void SetBehavior(IBlockBehavior newBehavior)
    {
        behavior = newBehavior;
    }

    // 積み木を飛ばす処理
    public void Launch(Vector3 dir, float startSpeed = -1.0f)
    {
        direction = new Vector3(dir.x,0.0f,dir.z).normalized;
        //指定がなければ設定速度、指定があれば speed を上限にして採用
        currentSpeed = (startSpeed < 0f) ? speed : Mathf.Min(startSpeed, speed);
        isFlying = true;
    }

    // 衝突で弾かれたときなど、飛行中に方向と速度を変更する
    // newDir: 新しい進行方向 / speedRatio: 現在速度に掛ける倍率(1で維持、小さいほど減速)
    public void Bounce(Vector3 newDir, float speedRatio)
    {
        Vector3 flat = new Vector3(newDir.x, 0f, newDir.z);
        if (flat.sqrMagnitude < 0.0001f) return; // 水平方向がなければ何もしない
        direction = flat.normalized;
        currentSpeed *= speedRatio;
    }

    private void FixedUpdate()
    {
        // 更新の最初で状態を確定させる
        wasFlyingThisStep = isFlying;
        // 飛んでいなけば動かさない
        if (!isFlying)
        {
            return;
        }

        // 速度を減衰させる(0未満にならないようにする)
        currentSpeed *= Mathf.Exp(-deceleration * Time.fixedDeltaTime);

        // 十分遅くなったら停止して、飛行状態を終了する
        if (currentSpeed <= stopThreshold)
        {
            StopFlying();
            return;
        }

        // behaviorに方向の更新を委譲する(曲がるキャラ等はここで方向が変化する)
        direction = behavior.UpdateDirection(direction, Time.fixedDeltaTime);

        // Y方向(重力)は維持して、水平方向だけ上書きする
        Vector3 v = direction * currentSpeed;
        v.y = rb.linearVelocity.y;
        rb.linearVelocity = v;
    }

    void StopFlying()
    {
        isFlying = false;
        hasHit = false;
        currentSpeed = 0.0f;

        // 水平方向の速度だけ止める(Yは重力のまま)
        Vector3 v = rb.linearVelocity;
        v.x = 0.0f;
        v.z = 0.0f;
        rb.linearVelocity = v;
    }

    private void OnCollisionEnter(Collision collision)
    {
        // このステップの開始時点で飛んでいないなら、
        // 衝突処理の途中でisFlyingがtrueになっていても攻撃側として扱わない
        if (!wasFlyingThisStep || hasHit)
        {
            return;
        }
       

        // 壁に当たったら積み木を反射させる
        if (collision.gameObject.CompareTag("Wall"))
        {
            // direction = Vector3.Reflect(direction, collision.contacts[0].normal); // behaviorに委譲したため不要

            hasHit = true; // 壁ヒットでも二重判定を防ぐため統一して立てる
            if (behavior.OnWallHit(direction, collision.contacts[0].normal, out var newDirection))
            {
                direction = newDirection; // 反射するキャラ(Normal等)
                hasHit = false; // 反射の場合は引き続き攻撃判定を持たせるため戻す
            }
            else
            {
                Break(); // 壁で砕けるキャラ(いわだる等)
            }
        }
        else if (collision.gameObject.CompareTag("Block"))
        {
            // 衝突相手の高さを取得
            float halfHeight = collision.collider.bounds.extents.y;
            // 自分と相手の中心位置の高さの差を計算
            float dy = Mathf.Abs(collision.collider.bounds.center.y - GetComponent<Collider>().bounds.center.y);

            // 高さが半ブロック以上ずれている相手(上に乗っているもの等)は攻撃対象にしない
            if (dy > halfHeight)
            {
                return;
            }

            var other = collision.gameObject.GetComponent<BlockController>();
            if (other != null)
            {
                // 弾かれた直後に同じ相手へ再衝突しても、一定時間は無視する
                if (other == lastHitTarget && Time.time < ignoreUntil)
                {
                    return;
                }
                lastHitTarget = other;
                ignoreUntil = Time.time + rehitIgnoreTime;

                // 接触法線を保存する(behavior側で反射方向の計算に使う)
                lastContactNormal = collision.GetContact(0).normal;

                hasHit = true;
                // other.Launch(other.transform.position - transform.position); // behaviorに委譲したため不要

                Debug.Log($"攻撃ヒット 色:{info.Color}");

                if (behavior.OnBlockHit(this, other))
                {
                    Break(); // 自分が壊れるキャラ(Normal等)
                }
                else
                {
                    // 自分が壊れないキャラ(いわだる等)は、次の積み木にも攻撃できるよう判定を戻す
                    hasHit = false;
                }
            }
        }
    }

    public void Break()
    {
        Destroy(gameObject);
    }
}
