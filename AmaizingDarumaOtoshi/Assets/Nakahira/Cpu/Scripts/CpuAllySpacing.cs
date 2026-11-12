using UnityEngine;

namespace Nakahira
{
    // チーム戦:歩いているとき、近くの味方から離れる向きへ歩く向きをずらす(設計メモ 9)
    // 味方はいないものとして動く(同じ敵を追う)ので、放っておくと重なって、撃ち合い・打ち返しが味方に当たりやすい
    //  ・AllySpacing(3m)以内の味方ごとに、離れる向きを「近いほど強く」足す。重みは AllySpacingWeight
    //  ・歩いているときだけ(止まっている・向き直りだけのときは動かさない。殴る・撃つ狙いを崩さないため)
    //  ・速さは変えず、向きだけ変える。床の縁・危ない場所はこのあと(縁で踏ん張る・危ない場所から出る)が直す
    public static class CpuAllySpacing
    {
        // これより小さいスティックの倒し方は「歩いていない」とみなす(向き直りだけの小さな入力など)
        const float kMinMove = 0.3f;

        public static void Apply(CpuContext c, ref DarumaCommand cmd)
        {
            CpuParams p = c.Params;
            if (p.AllySpacing <= 0.0f || p.AllySpacingWeight <= 0.0f) return;
            var allies = c.Perception.Allies;
            if (allies.Count == 0) return;

            Vector2 move = cmd.Move;
            float speed = move.magnitude;
            if (speed < kMinMove) return;

            Vector3 pos = c.Position;
            Vector3 push = Vector3.zero;
            foreach (var ally in allies)
            {
                if (ally == null) continue;
                Vector3 away = pos - ally.transform.position;
                away.y = 0.0f;
                float d = away.magnitude;
                if (d >= p.AllySpacing) continue;
                // ぴったり重なっているときは、歩いている向きの右へ逃げる
                Vector3 dir = d > 0.0001f ? away / d : new Vector3(move.y, 0.0f, -move.x) / speed;
                push += dir * (1.0f - d / p.AllySpacing);
            }
            if (push.sqrMagnitude < 0.0001f) return;

            Vector3 want = new Vector3(move.x, 0.0f, move.y) / speed + push * p.AllySpacingWeight;
            want.y = 0.0f;
            if (want.sqrMagnitude < 0.0001f) return;
            want = want.normalized * speed;
            cmd.Move = new Vector2(want.x, want.z);
        }
    }
}
