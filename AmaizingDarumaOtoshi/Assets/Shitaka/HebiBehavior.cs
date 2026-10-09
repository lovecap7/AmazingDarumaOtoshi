using UnityEngine;

public class HebiBehavior : NormalBehavior
{
    // 曲がる方向
    public enum CurveSide {  Left, Right };

    CurveSide side;
    // 1秒あたりに曲がる角度
    float turnSpeed;

    public HebiBehavior(CurveSide side, float turnSpeed = 60.0f)
    {
        this.side = side;
        this.turnSpeed = turnSpeed;
    }

    public override Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
    {
        // 左なら-1、右なら+1。この後の回転角度を決める
        float sign = side == CurveSide.Left ? -1.0f : 1.0f;
        // このフレームぶんに曲げる角度。
        float angle = sign * turnSpeed * deltaTime;
        // Y軸(水平方向)にangle度だけ回転させた新しい方向を返す
        return Quaternion.Euler(0.0f, angle, 0.0f) * currentDirection;
    }
}
