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
            return p.TargetStackWeight * stack + p.TargetDistanceWeight * near + p.TargetRuleWeight * RuleScore(o.Player);
        }

        // ルールで目立つ度合い(0〜1)。ストック:残りストックが多いほど / タイム:持ち点が多いほど / ゴースト:なし
        private static float RuleScore(MatchPlayer player)
        {
            var match = DarumaMatch.Current;
            if (match == null || player == null) return 0.0f;
            switch (match.Mode)
            {
                case MatchSetup.MatchMode.Stock:
                {
                    int max = 0;
                    foreach (var p in match.Players) max = Mathf.Max(max, p.Stocks);
                    return max > 0 ? Mathf.Clamp01((float)player.Stocks / max) : 0.0f;
                }
                case MatchSetup.MatchMode.Time:
                {
                    int min = int.MaxValue, max = int.MinValue;
                    foreach (var p in match.Players)
                    {
                        min = Mathf.Min(min, p.Score);
                        max = Mathf.Max(max, p.Score);
                    }
                    return max > min ? (float)(player.Score - min) / (max - min) : 0.0f;
                }
                default:
                    return 0.0f;
            }
        }
    }
}
