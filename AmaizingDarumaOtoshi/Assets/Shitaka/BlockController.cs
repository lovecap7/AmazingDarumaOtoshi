using System;
using UnityEngine;

public class BlockController : MonoBehaviour 
{
    [SerializeField] float speed = 2.0f;

    public bool isFlying {  get; private set; } // 現在飛行中かどうか trueの場合は攻撃判定が発生する

    // 開始時点で飛行中だったかを記録する
    // (衝突処理の途中でisFlyingが書き換わっても、判定がブレないようにするため)
    bool wasFlyingThisStep;

    bool hasHit; // 一度攻撃したら以降の衝突は無視する

    Rigidbody rb;
    BlockInfo info;
    Vector3 direction;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        info = rb.GetComponent<BlockInfo>();
    }

    private void Start()
    {
        
    }

    // 積み木を飛ばす処理
    public void Launch(Vector3 dir, bool useBurst = false)
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
            direction = Vector3.Reflect(direction, collision.contacts[0].normal);
        }
        else if(collision.gameObject.CompareTag("Block"))
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
                other.Launch(other.transform.position - transform.position, true);
            }
            Debug.Log($"攻撃ヒット 色:{info.Color}");
            Break(); // 他のブロックを打ち出した自分は破壊される
        }
    }

    void Break()
    {
        Destroy(gameObject);
    }
}
