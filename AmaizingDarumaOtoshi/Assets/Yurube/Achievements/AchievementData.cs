using System;
using UnityEngine;

namespace Yurube.Achievements
{
    // 実績1つ分の「いまの状態」。決まった内容（AchievementDefinition）と、
    // 進捗・解除済みか・解除した日時を持つ。AchievementManager だけが書き換える。
    public class AchievementData
    {
        public AchievementData(AchievementDefinition definition)
        {
            Definition = definition;
        }

        public readonly AchievementDefinition Definition;

        // いまの進捗（目標まで。目標を超えた分は目標で止める）
        public int Progress { get; internal set; }
        // 解除済みか
        public bool IsUnlocked { get; internal set; }
        // 解除した日時（未解除なら DateTime.MinValue）
        public DateTime UnlockedAt { get; internal set; } = DateTime.MinValue;

        public string Id => Definition.id;
        public string Name => Definition.name;
        // ひとこと（ポエム）。メニューの「じっせき」だけで見せる
        public string Poem => Definition.poem;
        // 解除条件。ゲーム中の通知にも出す
        public string ConditionText => Definition.condition;
        public AchievementCategory Category => Definition.category;
        public string CategoryName => Definition.CategoryName;
        // レアリティ（表示だけに使う）
        public AchievementRarity Rarity => Definition.rarity;
        public string RarityName => AchievementRarityInfo.NameOf(Definition.rarity);
        public string RarityEnglishName => AchievementRarityInfo.EnglishNameOf(Definition.rarity);
        public Color RarityColor => AchievementRarityInfo.ColorOf(Definition.rarity);
        public Sprite Icon => Definition.icon;
        // 解除に必要な数（CharacterUsedDistinct は全キャラクター数に置き換わることがある）
        public int Target => Definition.target;

        public float Ratio => Target <= 0 ? 0f : Mathf.Clamp01((float)Progress / Target);
        // 数を出す実績か（1回きりの実績は進捗バーを出さない）
        public bool HasProgress => Target > 1;
        public string ProgressText => HasProgress ? Progress + " / " + Target : IsUnlocked ? "達成" : "未達成";

        public string UnlockedText => IsUnlocked && UnlockedAt > DateTime.MinValue
            ? UnlockedAt.ToString("yyyy/MM/dd HH:mm")
            : "";
    }
}
