using System.Collections.Generic;
using UnityEngine;

namespace Yurube.Achievements
{
    // 実績の報酬（キャラクターの解放）。
    // 20個の実績をすべて解除すると、報酬キャラクター「だるきんぐ」がキャラクターセレクトで選べるようになる。
    // 解放状態はセーブに別持ちせず、実績の解除状況から毎回求める（実績を消せば元に戻る）。
    // 「全実績」の数は固定値ではなく、いま定義されている実績の総数を見る。
    public static class AchievementRewards
    {
        // 報酬キャラクターの CharacterData.id（セレクトの一覧と同じ名前）
        public const string RewardCharacterId = "daruking";

        public static string RewardCharacterName => "だるきんぐ";

        // 報酬キャラクターが解放済みか（全実績解除）
        public static bool IsRewardUnlocked => AchievementManager.AllUnlocked;

        public static int UnlockedCount => AchievementManager.UnlockedCount;
        public static int TotalCount => AchievementManager.All.Count;

        // そのキャラクターがいま選べるか。報酬キャラだけ、全実績解除まで選べない
        public static bool IsAvailable(CharacterData character)
        {
            if (character == null) return false;
            if (character.id != RewardCharacterId) return true;
            return IsRewardUnlocked;
        }

        // 選べるキャラクターだけの一覧（キャラクターセレクト用）
        public static List<CharacterData> AvailableCharacters(IReadOnlyList<CharacterData> source)
        {
            var list = new List<CharacterData>();
            if (source == null) return list;
            for (int i = 0; i < source.Count; i++)
            {
                if (IsAvailable(source[i])) list.Add(source[i]);
            }
            return list;
        }

        // おまかせ（ランダム）用：選べる中から1人
        public static CharacterData RandomAvailable(IReadOnlyList<CharacterData> source)
        {
            var list = AvailableCharacters(source);
            if (list.Count == 0) return null;
            return list[Random.Range(0, list.Count)];
        }
    }
}
