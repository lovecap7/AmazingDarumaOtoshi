using UnityEngine;

namespace Yurube.Achievements
{
    // 実績のレアリティ（銅・銀・金・プラチナ・ダイヤモンド）。
    // 難易度・希少性を表すだけで、解除条件そのものには影響しない。
    // ダイヤモンドが最高位で、ほかのすべての実績を解除すると手に入る（AchievementCatalog のメタ実績）。
    public enum AchievementRarity
    {
        Bronze,     // 銅：初級。基本操作や初めての経験
        Silver,     // 銀：中級。何度か遊んで身につく立ち回り
        Gold,       // 金：上級。意識した立ち回りや高い技術
        Platinum,   // プラチナ：熟練者向け。難しい条件
        Diamond,    // ダイヤモンド：最高位。すべての実績を解除
    }

    // 実績UIのレアリティカラーは、すべてここで管理する。
    // 画面ごとに色を書かず、必ずこのクラスから取る（カードの下地・外枠・光彩・通知・メニューの帯）。
    public static class AchievementRarityInfo
    {
        public static readonly AchievementRarity[] All =
        {
            AchievementRarity.Bronze,
            AchievementRarity.Silver,
            AchievementRarity.Gold,
            AchievementRarity.Platinum,
            AchievementRarity.Diamond,
        };

        // レアリティ別の集計には入れない「特別実績」（レアリティ制覇など）の表示
        public const string SpecialName = "特別";
        public const string SpecialEnglishName = "Special";
        public static readonly Color SpecialColor = new Color32(0xD9, 0x8A, 0x4E, 0xFF); // #D98A4E

        public static string NameOf(AchievementRarity rarity)
        {
            switch (rarity)
            {
                case AchievementRarity.Bronze: return "銅";
                case AchievementRarity.Silver: return "銀";
                case AchievementRarity.Gold: return "金";
                case AchievementRarity.Platinum: return "プラチナ";
                default: return "ダイヤモンド";
            }
        }

        public static string EnglishNameOf(AchievementRarity rarity)
        {
            switch (rarity)
            {
                case AchievementRarity.Bronze: return "Bronze";
                case AchievementRarity.Silver: return "Silver";
                case AchievementRarity.Gold: return "Gold";
                case AchievementRarity.Platinum: return "Platinum";
                default: return "Diamond";
            }
        }

        // 一覧や説明に出す、レアリティの意味
        public static string DescriptionOf(AchievementRarity rarity)
        {
            switch (rarity)
            {
                case AchievementRarity.Bronze: return "基本的な実績を達成";
                case AchievementRarity.Silver: return "中級実績を達成";
                case AchievementRarity.Gold: return "上級実績を達成";
                case AchievementRarity.Platinum: return "高難易度実績を達成";
                default: return "すべての実績を解除";
            }
        }

        // 最高レアリティか（ダイヤモンドだけ特別扱いするとき用）
        public static bool IsHighest(AchievementRarity rarity) => rarity == AchievementRarity.Diamond;

        // ---- 実績カードの下地（左上が明るく、右下が暗い斜めグラデーション） ----

        public static Color GradientStartOf(AchievementRarity rarity)
        {
            switch (rarity)
            {
                case AchievementRarity.Bronze: return new Color32(0xC1, 0x8A, 0x60, 0xFF);   // #C18A60
                case AchievementRarity.Silver: return new Color32(0xDC, 0xE2, 0xEA, 0xFF);   // #DCE2EA
                case AchievementRarity.Gold: return new Color32(0xF2, 0xD1, 0x6B, 0xFF);     // #F2D16B
                case AchievementRarity.Platinum: return new Color32(0xA6, 0xE3, 0xF0, 0xFF); // #A6E3F0
                default: return new Color32(0xC5, 0xA4, 0xFF, 0xFF);                          // #C5A4FF
            }
        }

        public static Color GradientEndOf(AchievementRarity rarity)
        {
            switch (rarity)
            {
                case AchievementRarity.Bronze: return new Color32(0x87, 0x50, 0x2F, 0xFF);   // #87502F
                case AchievementRarity.Silver: return new Color32(0x87, 0x95, 0xA7, 0xFF);   // #8795A7
                case AchievementRarity.Gold: return new Color32(0xAD, 0x7C, 0x1F, 0xFF);     // #AD7C1F
                case AchievementRarity.Platinum: return new Color32(0x42, 0x8F, 0xAF, 0xFF); // #428FAF
                default: return new Color32(0x68, 0x40, 0xA8, 0xFF);                          // #6840A8
            }
        }

        // レアリティの代表色（通知・メニューの帯・ラボの見本など、単色が要るところ用）。
        // カードの下地は GradientStartOf / GradientEndOf を使う。
        // 単色の見本は、いちばん明るい開始色にして、通知や小さい帯でも沈まないようにする。
        public static Color ColorOf(AchievementRarity rarity) => GradientStartOf(rarity);

        // カードの外側に敷く細い暗い縁。明るい外枠とカードの下地を分ける
        public static Color EdgeOf(AchievementRarity rarity, bool unlocked)
        {
            Color c = Color.Lerp(GradientEndOf(rarity), Color.black, 0.28f);
            c.a = 0.55f;
            return unlocked ? c : Muted(c);
        }

        // カードの外枠。レアリティカラーを明るくした色
        public static Color BorderOf(AchievementRarity rarity, bool unlocked)
        {
            Color c = Color.Lerp(GradientStartOf(rarity), Color.white, 0.18f);
            c.a = 0.9f;
            return unlocked ? c : Muted(c);
        }

        // 選択・ホバー時に外へ出す光彩の色（下地の真ん中あたりを少し明るくした色）
        public static Color GlowOf(AchievementRarity rarity, bool unlocked)
        {
            Color c = Color.Lerp(GradientStartOf(rarity), GradientEndOf(rarity), 0.4f);
            c = Color.Lerp(c, Color.white, 0.22f);
            c.a = 1f;
            return unlocked ? c : Muted(c);
        }

        // 未解除用。色相はそのままに、彩度と明度だけを少し落とす（グレーや水色にはしない）
        public static Color Muted(Color c)
        {
            float l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            Color desaturated = Color.Lerp(c, new Color(l, l, l, c.a), 0.35f);
            return new Color(desaturated.r * 0.82f, desaturated.g * 0.82f, desaturated.b * 0.82f, c.a);
        }
    }
}
