using System;
using UnityEngine;

public class BlockController : MonoBehaviour 
{
    [SerializeField] float speed = 2.0f;
    [SerializeField] float deceleration = 0.2f;          // 減速量(単位/秒)。大きいほど早く止まる
    [SerializeField] float stopThreshold = 0.5f;         // この速度を下回ったら停止とみなす

    float currentSpeed; // 現在の速度(毎ステップ減衰する)

    // 現在の水平速度を外部(Behavior)から参照するための公開プロパティ
    public float CurrentSpeed => currentSpeed;

    // 現在のbehaviorを外部から参照するための公開プロパティ(MagnetBehaviorが相手の挙動を判定するのに使う)
    public IBlockBehavior Behavior => behavior;

    // 飛行が終わって止まったときに通知する(FadeBehaviorなどが元の状態に戻すために購読する)
    public event Action OnFlightEnded;

    // テスト用：インスペクターで挙動を選べるようにする
    public enum BehaviorType { Normal, Iwa,HebiLeft, HebiRight, UnBreak,Homing,Ziguzagu,Grow,Bomb, Magnet,Fade, Boomerang, Gravity, Accelerat, Funnel }
    [SerializeField] BehaviorType behaviorType = BehaviorType.Normal;
    [SerializeField] float hebiTurnSpeed = 60.0f; // テスト用：へびだるの曲がる強さ(度/秒)。大きいほど急カーブになる
    [SerializeField] float homingStartTurnSpeed = 270.0f;  // テスト用：ホーミング発射直後の旋回速度(度/秒)
    [SerializeField] float homingGuidanceDuration = 1.5f; // テスト用：誘導が切れるまでの時間(秒)
    [SerializeField] float ziguzagAngle = 40.0f;      // テスト用：ジグザグの傾き(度)
    [SerializeField] float ziguzagInterval = 0.4f;  // テスト用：向きを切り替える間隔(秒)
    [SerializeField] float growRate = 1.2f;        // テスト用：壁ヒットごとの拡大倍率
    [SerializeField] float growMaxMultiplier = 3.0f; // テスト用：初期サイズに対する最大倍率
    [SerializeField] float explosionRadius = 2.0f; // テスト用：爆発の範囲(半径)
    [SerializeField] float magnetRange = 10.0f;     // テスト用：磁力が届く距離
    [SerializeField] float magnetStrength = 20.0f;  // テスト用：磁力の強さ
    [SerializeField] float magnetRepelStrength = 20.0f;  // テスト用：反発する力の強さ(最も近いとき)
    [SerializeField] float magnetMinDistance = 0.5f;    // テスト用：反発が最大になる距離
    [SerializeField] float fadeDelay = 0.25f;     // テスト用：薄くなり始めるまでの時間(秒)
    [SerializeField] float fadeDuration = 0.8f;  // テスト用：薄くなりきるまでの時間(秒)
    [SerializeField] float fadeMinAlpha = 0.12f; // テスト用：最も薄いときのアルファ値
    [SerializeField] Transform boomerangReturnTarget;    // テスト用：戻る先。未設定なら発射した位置に戻る
    [SerializeField] float boomerangRadius = 5.0f;          // テスト用：弧の半径
    [SerializeField] bool boomerangCurveLeft = true;        // テスト用：左に曲がるか(falseなら右)
    [SerializeField] float boomerangArriveDistance = 1.0f;  // テスト用：戻ったとみなす距離
    [SerializeField] float gravityRadius = 10.0f;      // テスト用：井戸の範囲(半径)
    [SerializeField] float gravityStrength = 12.0f;   // テスト用：引き寄せる強さ(加速度)
    [SerializeField] float gravityDuration = 2.5f;    // テスト用：井戸が残る時間(秒)
    [SerializeField] float gravityDeadZone = 0.4f;    // テスト用：中心で止まる距離
    [SerializeField] float gravityEdgeRatio = 0.4f; // テスト用：範囲の端での強さ(中心を1とした割合)
    [SerializeField] float accelRate = 1.3f;       // テスト用：壁ヒットごとの加速倍率
    [SerializeField] float accelMaxSpeed = 12.0f;  // テスト用：速度の上限
    [SerializeField] int accelMaxBlockHits = 2;    // テスト用：積み木に何回当たったら壊れるか

    int funnelCount = 3;               // テスト用：分裂後の総数。本来はプレイヤーが管理している積み木の数を渡す
    float funnelSpread = 90.0f;        // テスト用：扇状に広がる角度
    float funnelSearchRadius = 15.0f;  // テスト用：狙う相手を探す範囲
    float funnelTurnSpeed = 270.0f;    // テスト用：発射直後の旋回速度(度/秒)
    float funnelDuration = 3.0f;       // テスト用：誘導が切れるまでの時間(秒)

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

        // 高速で動いても壁や他の積み木に深くめり込まないよう、連続衝突判定にする
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

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
            case BehaviorType.Homing: return new HomingBehavior(this, new OtherBlockTargetSelector(), homingStartTurnSpeed, homingGuidanceDuration);
            case BehaviorType.Ziguzagu: return new ZiguzagBehavior(ziguzagAngle, ziguzagInterval);
            case BehaviorType.Grow: return new GrowBehavior(this, growRate, growMaxMultiplier);
            case BehaviorType.Bomb: return new BombBehavior(explosionRadius);
            case BehaviorType.Magnet: return new MagnetBehavior(this, magnetRange, magnetStrength, magnetRepelStrength, magnetMinDistance);
            case BehaviorType.Fade: return new FadeBehavior(this, fadeDelay, fadeDuration, fadeMinAlpha);
            case BehaviorType.Boomerang: return new BoomerangBehavior(this, boomerangReturnTarget, boomerangRadius, boomerangCurveLeft, boomerangArriveDistance);
            case BehaviorType.Gravity: return new GravityWellBehavior(this, gravityRadius, gravityStrength, gravityDuration, gravityDeadZone, gravityEdgeRatio);
            case BehaviorType.Accelerat: return new AcceleratBehavior(this, accelRate, accelMaxSpeed, accelMaxBlockHits);
            case BehaviorType.Funnel: return new FunnelBehavior(this, funnelCount, funnelSpread, funnelSearchRadius, funnelTurnSpeed, funnelDuration);
            case BehaviorType.Normal:
            default: return new NormalBehavior();
        }
    }

    // 生成担当が、積み木を生成した直後に種類を指定するための窓口
    public void SetBehaviorType(BehaviorType type)
    {
        behaviorType = type;
        behavior = CreateBehavior(type);
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
        Vector3 flat = new Vector3(newDir.x, 0.0f, newDir.z);
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

    // 挙動のインスタンスを直接差し替える(FunnelBehaviorが分身に渡すためなど)
    public void SetBehaviorInstance(IBlockBehavior newBehavior)
    {
        behavior = newBehavior;
    }

    // 飛行が終わって止まったら、この積み木を消す(分身が場に残り続けないようにする)
    public void DestroyWhenFlightEnds()
    {
        OnFlightEnded += () => Destroy(gameObject);
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
        OnFlightEnded?.Invoke(); 
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
            // 接触法線を水平方向だけにする
            // (壁の縁や傾きでYが混ざると、反射後の水平速度が落ちて止まったように見えるため)
            Vector3 wallNormal = collision.GetContact(0).normal;
            wallNormal.y = 0.0f;
            if (wallNormal.sqrMagnitude < 0.0001f)
            {
                // めり込み等で法線が上下を向いたときは、壁の軸のうち進行方向と最も向かい合う向きを法線の代わりにする
                wallNormal = FallbackWallNormal(collision.transform, direction);
            }
            wallNormal.Normalize();

            // すでに壁から離れる向きに動いているなら反射しない
            // (同じ壁との接触イベントが続けて来たとき、2回反射して壁の方へ戻ってしまうのを防ぐ)
            if (Vector3.Dot(direction, wallNormal) >= 0f) return;

            hasHit = true; // 壁ヒットでも二重判定を防ぐため統一して立てる
            if (behavior.OnWallHit(direction, wallNormal, out var newDirection))
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

    // 壁の水平な軸(前後左右)のうち、進行方向に最も向かい合う向きを返す
    static Vector3 FallbackWallNormal(Transform wall, Vector3 dir)
    {
        Vector3[] axes = { wall.right, -wall.right, wall.forward, -wall.forward };
        Vector3 best = Vector3.zero;
        float bestDot = float.MaxValue;

        foreach (var a in axes)
        {
            Vector3 flat = new Vector3(a.x, 0.0f, a.z).normalized;
            float d = Vector3.Dot(dir, flat);
            if (d < bestDot)
            {
                bestDot = d;
                best = flat;
            }
        }
        return best;
    }

    // 攻撃力の数だけ、当たった積み木(target)とその真上に積み上がっている積み木を飛ばす
    public void KnockOff(BlockController target, Vector3 dir, float startSpeed = -1.0f)
    {
        int count = Mathf.Max(1, info.AttackPower);
        int launched = 0;
        BlockController current = target;

        while (current != null && launched < count)
        {
            // 飛ばす前に、真上の積み木を見つけておく
            BlockController above = current.FindBlockAbove(this);
            current.Launch(dir, startSpeed);
            launched++;
            current = above;
        }

        // テスト用：確認できたら削除してよい
        Debug.Log($"[KnockOff] 攻撃力 {count} → {launched}個飛ばした", this);
    }

    // 自分の真上に積まれている積み木を1個探す(なければnull)
    BlockController FindBlockAbove(BlockController exclude)
    {
        Bounds b = GetComponent<Collider>().bounds;

        // 真上1個ぶんの位置に、少し小さめの箱を置いて、重なっている積み木を探す
        Vector3 center = b.center + Vector3.up * b.size.y;
        Vector3 halfExtents = b.extents * 0.8f;

        foreach (var c in Physics.OverlapBox(center, halfExtents))
        {
            if (!c.CompareTag("Block")) continue;

            var block = c.GetComponent<BlockController>();
            if (block == null || block == this || block == exclude) continue;
            if (block.isFlying) continue; // すでに飛んでいる積み木は対象にしない

            return block;
        }
        return null;
    }

    // 速度を倍率で上げる(上限つき)。壁で加速する挙動などから呼ぶ
    public void BoostSpeed(float ratio, float maxSpeed)
    {
        currentSpeed = Mathf.Min(currentSpeed * ratio, maxSpeed);
    }
}
