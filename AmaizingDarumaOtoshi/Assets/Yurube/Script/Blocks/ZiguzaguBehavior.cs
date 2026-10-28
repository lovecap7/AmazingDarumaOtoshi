// 志鷹さんの Assets/Shitaka から複製（由留部の作業用）
// 元のクラスと名前が衝突しないよう、名前空間 Yurube.Blocks に入れている
using UnityEngine;

namespace Yurube.Blocks
{
public class ZiguzagBehavior : NormalBehavior
{
    readonly float angle;     // 中心線からの傾き(度)。大きいほど振れ幅が大きい
    readonly float interval;  // 向きを切り替える間隔(秒)
    readonly float firstSign; // 最初に曲がる向き(-1:左 / +1:右)

    float elapsed;
    float currentOffset; // 直前のステップで中心線に加えた傾き(符号つき・度)

    public ZiguzagBehavior(float angle = 40.0f, float interval = 0.4f, bool startLeft = true)
    {
        this.angle = angle;
        this.interval = interval;
        this.firstSign = startLeft ? -1.0f : 1.0f;

        // 最初の1区間だけ半分の長さにして、中心線を軸に左右対称に振れるようにする
        // (これがないと、片側だけに寄ったジグザグになる)
        elapsed = interval * 0.5f;
    }

    public override Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
    {
        // 前ステップで加えた傾きを外して、中心線(ジグザグ前)の向きを取り出す
        Vector3 baseDir = Quaternion.Euler(0f, -currentOffset, 0f) * currentDirection;

        elapsed += deltaTime;

        // intervalごとに区間番号が増え、偶数・奇数で左右を交互に切り替える
        int phase = Mathf.FloorToInt(elapsed / interval);
        float sign = (phase % 2 == 0) ? firstSign : -firstSign;

        // 今回の傾きを中心線に加えて、進む向きとする
        currentOffset = sign * angle;
        return Quaternion.Euler(0f, currentOffset, 0f) * baseDir;
    }

    // 壁で反射すると、回転の向きが鏡に映したように逆になる。
    // 保持している傾きの符号も反転しておかないと、反射後の中心線がずれてしまう
    public override bool OnWallHit(Vector3 currentDirection, Vector3 wallNormal, out Vector3 newDirection)
    {
        bool result = base.OnWallHit(currentDirection, wallNormal, out newDirection);
        currentOffset = -currentOffset;
        return result;
    }
}
}
