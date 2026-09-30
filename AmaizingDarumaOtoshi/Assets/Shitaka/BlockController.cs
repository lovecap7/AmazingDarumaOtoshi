using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(BlockInfo))] // RigidbodyやBlockInfoがオブジェクトに設定されていない場合は自動で追加する
public class BlockController : MonoBehaviour 
{
    [SerializeField] float speed = 2.0f;

    public bool isFlying {  get; private set; } // 現在飛行中かどうか trueの場合は攻撃判定が発生する

    // 開始時点で飛行中だったかを記録する
    // (衝突処理の途中でisFlyingが書き換わっても、判定がブレないようにするため)
    bool wasFlyingThisStep;

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
        rb.linearVelocity = direction * speed;
    }

    private void OnCollisionEnter(Collision collision)
    {
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
            var other = collision.gameObject.GetComponent<BlockController>(); // 相手が飛行中かを確認するためBlockControlerを取得
            if (other != null)
            {
                // どちらの挙動がいいかは検討中
                //other.Launch(direction); // 相手を自分と同じ方向へ飛ばす
                other.Launch(other.transform.position - transform.position); // 自分から相手への向きへ飛ばす

            }
            Debug.Log($"攻撃ヒット 色:{info.Color}");
            Break(); // 打ち出した自分は破壊される
        }
    }

    void Break()
    {
        Destroy(gameObject);
    }
}
