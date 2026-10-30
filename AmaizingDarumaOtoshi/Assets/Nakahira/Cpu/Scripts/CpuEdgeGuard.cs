using UnityEngine;

namespace Nakahira
{
    // 縁で踏ん張る:自分で動いていないのに縁の外へ流されている(扇風機の風・押されたあとの勢いなど)ときは、逆向きに入力して押し返す
    // 道をたどる動き(CpuPathFollower.Walk)は、進む先が奈落なら止まるだけなので、止まったあとに流されると落ちてしまう
    // 自分で縁の方へ歩いている(入力と流れる向きがほぼ同じ)ときは何もしない(乗り場など縁の近くへ行けるように)
    public static class CpuEdgeGuard
    {
        // これより遅ければ流されていないとみなす(m/s)
        const float kMinSpeed = 0.3f;
        // 流れる向きの先、体の半径にこの距離(m)と速さの分を足した所に床がなければ踏ん張る
        const float kProbeMargin = 0.2f;
        const float kProbeTimePerSpeed = 0.1f;
        // 入力の向きと流れる向きがこれ以上そろっていれば、自分で歩いているとみなす(cos)
        const float kSelfMoveDot = 0.7f;

        public static void Apply(CpuContext c, ref DarumaCommand cmd)
        {
            DarumaCharacter self = c.Self;
            if (!self.IsGrounded || self.Rigidbody == null) return;
            Vector3 v = self.Rigidbody.linearVelocity;
            v.y = 0.0f;
            float speed = v.magnitude;
            if (speed < kMinSpeed) return;
            Vector3 dir = v / speed;

            // 自分で流れる向きへ歩いているなら任せる
            Vector3 move = new Vector3(cmd.Move.x, 0.0f, cmd.Move.y);
            if (move.magnitude > 0.1f && Vector3.Dot(move.normalized, dir) > kSelfMoveDot) return;

            float probe = DarumaCharacter.kBodyRadius + kProbeMargin + speed * kProbeTimePerSpeed;
            if (CpuSensor.HasGround(c.Position + dir * probe, c.Params.GroundProbeDepth)) return;
            cmd.Move = new Vector2(-dir.x, -dir.z);
            cmd.Jump = false;
        }
    }
}
