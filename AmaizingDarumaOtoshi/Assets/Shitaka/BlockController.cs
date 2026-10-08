using System;
using UnityEngine;

public class BlockController : MonoBehaviour 
{
    [SerializeField] float speed = 2.0f;

    // テスト用：インスペクターで挙動を選べるようにする
    public enum BehaviorType { Normal, Iwa,HebiLeft, HebiRight }
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
    public void Launch(Vector3 dir)
    {
        direction = new Vector3(dir.x,0.0f,dir.z).normalized;
        isFlying = true;
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

        // behaviorに方向の更新を委譲する(曲がるキャラ等はここで方向が変化する)
        direction = behavior.UpdateDirection(direction, Time.fixedDeltaTime);

        // Y方向(重力)は維持して、水平方向だけ上書きする
        Vector3 v = direction * speed;
        v.y = rb.linearVelocity.y;
        rb.linearVelocity = v;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!wasFlyingThisStep || hasHit) return;
        // このステップの開始時点で飛んでいないなら、
        // 衝突処理の途中でisFlyingがtrueになっていても攻撃側として扱わない
        if (!wasFlyingThisStep)
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
