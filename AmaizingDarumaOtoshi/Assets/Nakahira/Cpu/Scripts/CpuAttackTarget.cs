using UnityEngine;

namespace Nakahira
{
    // 攻め(殴る・撃つ・弾く)で狙う相手の選び方(共通)
    // 相手ごとに点数を付け、一番高い相手を狙う:段数が少ないほど・近いほど・ルールで目立つ(ストック・持ち点が多い)ほど高い
    // 幽霊・無敵中の相手は狙わない
    public static class CpuAttackTarget
    {
        // 段数の点を付けるときの最大の段数
        const float kMaxStack = 10.0f;

        // 攻めの相手としての点数(高いほど狙う)。狙えない相手は負
        public static float Score(CpuContext c, CpuOpponent o, float distance)
        {
            if (o.IsGhost || o.IsInvincible || o.Character == null) return -1.0f;
            CpuParams p = c.Params;
            float stack = 1.0f - Mathf.Clamp01(o.StackCount / kMaxStack);
            float near = 1.0f - Mathf.Clamp01(distance / Mathf.Max(0.01f, p.TargetDistanceRange));
            return p.TargetStackWeight * stack + p.TargetDistanceWeight * near + p.TargetRuleWeight * RuleScore(c.Player, o.Player)
                - p.TargetCrowdPenalty * CrowdCount(c, o);
        }

        // その相手がもう狙われているか:そばにいる、その相手の敵の生存者(自分を除く)の数(2026-11-20)
        // 弱い相手に全員で群がらないように、もう囲まれている相手は点を下げる(ほかの相手と戦いに行く)
        private static int CrowdCount(CpuContext c, CpuOpponent o)
        {
            CpuParams p = c.Params;
            var match = DarumaMatch.Current;
            if (p.TargetCrowdPenalty <= 0.0f || p.TargetCrowdRadius <= 0.0f || match == null || o.Player == null) return 0;
            float r2 = p.TargetCrowdRadius * p.TargetCrowdRadius;
            int count = 0;
            foreach (var other in match.Players)
            {
                if (other == null || other == c.Player || other == o.Player) continue;
                if (other.Character == null || match.AreTeammates(other, o.Player)) continue;
                Vector3 d = other.Character.transform.position - o.Position;
                d.y = 0.0f;
                if (d.sqrMagnitude <= r2) count++;
            }
            return count;
        }

        // ルールで目立つ度合い(0〜1)。ストック:残りストックが多いほど / タイム:持ち点が多いほど / ゴースト:なし
        // チーム戦では、勝ち負けと同じくチームの合計で比べる。比べるのは敵(自分のチーム以外)の人だけ
        private static float RuleScore(MatchPlayer self, MatchPlayer player)
        {
            var match = DarumaMatch.Current;
            if (match == null || player == null) return 0.0f;
            bool teams = match.Rules.teams;   // 直接再生のチーム戦も見る
            switch (match.Mode)
            {
                case MatchSetup.MatchMode.Stock:
                {
                    int max = 0;
                    foreach (var p in match.Players)
                    {
                        if (teams && (p == self || match.AreTeammates(self, p))) continue;
                        max = Mathf.Max(max, RuleValue(match, p, true, teams));
                    }
                    return max > 0 ? Mathf.Clamp01((float)RuleValue(match, player, true, teams) / max) : 0.0f;
                }
                case MatchSetup.MatchMode.Time:
                {
                    int min = int.MaxValue, max = int.MinValue;
                    foreach (var p in match.Players)
                    {
                        if (teams && (p == self || match.AreTeammates(self, p))) continue;
                        int v = RuleValue(match, p, false, teams);
                        min = Mathf.Min(min, v);
                        max = Mathf.Max(max, v);
                    }
                    return max > min ? (float)(RuleValue(match, player, false, teams) - min) / (max - min) : 0.0f;
                }
                default:
                    return 0.0f;
            }
        }

        // その人の残りストック(stocks)か持ち点。チーム戦ではその人のチームの合計
        private static int RuleValue(DarumaMatch match, MatchPlayer player, bool stocks, bool teams)
        {
            if (!teams) return stocks ? player.Stocks : player.Score;
            int sum = 0;
            foreach (var p in match.Players)
            {
                if (p != player && !match.AreTeammates(player, p)) continue;
                sum += stocks ? Mathf.Max(0, p.Stocks) : p.Score;
            }
            return sum;
        }
    }
}
