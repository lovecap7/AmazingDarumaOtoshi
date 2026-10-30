using UnityEngine;

namespace Nakahira
{
    // ジャンプでかわすときに、跳びながら動く向きを選ぶ(真上に跳ぶだけだと機械的に見えるため)
    // 候補の向きを順に試し、跳んでいる間に通る所と着地点に床がある向きを使う(奈落・バンパーへは跳ばない)
    public static class CpuDodgeMove
    {
        // 着地点がバンパーなどの危険な床からこの距離(m)以内なら使わない
        const float kHazardClearance = DarumaCharacter.kBodyRadius + 0.3f;
        // 着地点の先にも床があることを確かめる距離(m)
        const float kLandingMargin = DarumaCharacter.kBodyRadius + 0.5f;

        // 跳んでから着地するまでの時間(秒。同じ高さに降りるとき)
        public static float AirTime(JumpParams jump)
        {
            float fall = jump.TimeToApex / Mathf.Sqrt(Mathf.Max(jump.FallGravityMultiplier, 0.01f));
            return jump.TimeToApex + fall;
        }

        // 動きながら跳ぶか決める(DodgeMoveChance。外れたら真上に跳ぶ)
        public static bool Roll(CpuContext c) => Random.value < c.Params.DodgeMoveChance;

        // 移動入力 move(大きさ 0〜1)で跳んだとき、途中と着地点に床があるか
        public static bool IsSafe(CpuContext c, Vector2 move)
        {
            if (move.sqrMagnitude < 0.0001f) return false;
            Vector3 dir = new Vector3(move.x, 0.0f, move.y);
            float distance = c.Self.MoveSpeed * AirTime(c.Self.JumpParams);
            Vector3 pos = c.Position;
            float depth = c.Params.GroundProbeDepth;
            Vector3 land = pos + dir * distance;
            if (!CpuSensor.HasGround(pos + dir * (distance * 0.5f), depth)) return false;
            if (!CpuSensor.HasGround(land, depth)) return false;
            // 着地してから少し滑る分も床があること(縁ぎりぎりに降りて落ちないように)
            if (!CpuSensor.HasGround(land + dir.normalized * kLandingMargin, depth)) return false;
            // 移動床の上には跳ばない(着くころには動いているかもしれない)
            if (CpuMovingFloors.TryGetFloorUnder(land, out _)) return false;
            return !CpuNavMesh.IsNearHazard(land, kHazardClearance);
        }

        // 候補のうち、最初に安全な向きを返す(どれも危なければ false。真上に跳ぶ)
        public static bool TryPick(CpuContext c, out Vector2 move, params Vector2[] candidates)
        {
            foreach (var m in candidates)
            {
                if (!IsSafe(c, m)) continue;
                move = m;
                return true;
            }
            move = Vector2.zero;
            return false;
        }

        // 水平の向き(大きさは捨てる)を、大きさ scale の移動入力にする
        public static Vector2 ToMove(Vector3 dir, float scale)
        {
            dir.y = 0.0f;
            if (dir.sqrMagnitude < 0.0001f) return Vector2.zero;
            dir.Normalize();
            return new Vector2(dir.x, dir.z) * scale;
        }
    }
}
