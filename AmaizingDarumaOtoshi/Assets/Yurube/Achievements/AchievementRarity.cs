using UnityEngine;

namespace Yurube.Achievements
{
    // 実績のレアリティ（銅・銀・金・プラチナ）。
    // 難易度・希少性を表すだけで、解除条件そのものには影響しない。
    // 銀（Silver）はいまは該当なし。将来の実績追加に備えて定義だけ持つ。
    public enum AchievementRarity
    {
        Bronze,     // 銅：初級。基本操作や初めての経験
        Silver,     // 銀：中級（いまは未使用）
        Gold,       // 金：上級。意識した立ち回りや高い技術
        Platinum,   // プラチナ：熟練者向け。難しい条件
    }

    public static class AchievementRarityInfo
    {
        public static readonly AchievementRarity[] All =
        {
            AchievementRarity.Bronze,
            AchievementRarity.Silver,
            AchievementRarity.Gold,
            AchievementRarity.Platinum,
        };

        public static string NameOf(AchievementRarity rarity)
        {
            switch (rarity)
            {
                case AchievementRarity.Bronze: return "銅";
                case AchievementRarity.Silver: return "銀";
                case AchievementRarity.Gold: return "金";
                default: return "プラチナ";
            }
        }

        public static string EnglishNameOf(AchievementRarity rarity)
        {
            switch (rarity)
            {
                case AchievementRarity.Bronze: return "Bronze";
                case AchievementRarity.Silver: return "Silver";
                case AchievementRarity.Gold: return "Gold";
                default: return "Platinum";
            }
        }

        // レアリティごとの色（銅・銀・金・プラチナを見分けるため）
        public static Color ColorOf(AchievementRarity rarity)
        {
            switch (rarity)
            {
                case AchievementRarity.Bronze: return new Color(0.78f, 0.48f, 0.20f);
                case AchievementRarity.Silver: return new Color(0.72f, 0.74f, 0.78f);
                case AchievementRarity.Gold: return new Color(0.96f, 0.72f, 0.12f);
                // プラチナ：白銀に青みを足したアイスブルー（銀の灰色と区別できるように）
                default: return new Color(0.74f, 0.83f, 0.97f);
            }
        }
    }
}
