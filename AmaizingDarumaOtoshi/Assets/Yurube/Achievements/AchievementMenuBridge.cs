using UnityEngine;

namespace Yurube.Achievements
{
    // メニューの「じっせき」（Nakagawa さんの AchievementBook）に、このシステムの実績を並べる係。
    // AchievementBook は static な Entries を開いたときに読むだけなので、起動時と変化したときにここで入れ直す。
    // メニューでは「ポエム（ひとこと）」と「解除条件」の両方を見せる（ゲーム中の通知は解除条件だけ）。
    // メニュー側のコードは変更していない。
    public static class AchievementMenuBridge
    {
        static bool s_installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            AchievementManager.Initialize();
            Sync();
            if (s_installed) return;   // ドメインの再読み込みをしない設定でも二重に登録しない
            s_installed = true;
            AchievementManager.Changed += Sync;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_installed = false;
        }

        // 実績の一覧と、収集ダッシュボード用の集計を入れ直す
        public static void Sync()
        {
            // 実績ラボのデバッグ用データはメニューに出さない（ふだんのデータのときだけ）
            if (AchievementManager.StorageKey != AchievementManager.NormalStorageKey) return;

            var entries = AchievementBook.Entries;
            var summaries = AchievementBook.Summaries;
            entries.Clear();
            summaries.Clear();
            AchievementBook.BarSprite = AchievementVisuals.BarSprite();

            // 通常レアリティの集計（特別実績は含めない）
            foreach (var rarity in AchievementRarityInfo.All)
            {
                int total = 0, unlocked = 0;
                foreach (var data in AchievementManager.All)
                {
                    if (data.IsSpecial || data.Rarity != rarity) continue;
                    total++;
                    if (data.IsUnlocked) unlocked++;
                }
                summaries.Add(new AchievementBook.RaritySummary(
                    AchievementRarityInfo.NameOf(rarity),
                    AchievementRarityInfo.ColorOf(rarity), total, unlocked,
                    AchievementRarityIcons.IconOf(rarity)));
            }

            // 特別実績（レアリティ別コンプリートなど）は別枠で集計
            int specialTotal = 0, specialUnlocked = 0;
            foreach (var data in AchievementManager.All)
            {
                if (!data.IsSpecial) continue;
                specialTotal++;
                if (data.IsUnlocked) specialUnlocked++;
            }
            summaries.Add(new AchievementBook.RaritySummary(
                AchievementRarityInfo.SpecialName,
                AchievementRarityInfo.SpecialColor, specialTotal, specialUnlocked,
                AchievementRarityIcons.SpecialIcon()));

            foreach (var data in AchievementManager.All)
            {
                // 一覧には名前だけを出し、解除条件とポエムは右の詳細カードに出す
                var entry = new AchievementBook.Entry(data.Name, data.ConditionText, data.IsUnlocked);
                entry.id = data.Id;
                entry.number = data.Number;
                entry.secret = data.IsSecret;
                entry.special = data.IsSpecial;
                entry.rarityRank = RarityRank(data);
                entry.unlockedAt = data.UnlockedAt;
                entry.obtainedText = data.UnlockedText;
                entry.poem = data.Poem;
                entry.condition = data.ConditionText;
                entry.progress = data.Progress;
                entry.target = data.Target;
                entry.hasProgress = data.HasProgress;
                // アイコン：レアリティ色の下地に、白いアイコン（トロフィー／王冠）を重ねる。
                // 未取得は同じ場所に鍵を出す（AchievementBook 側で灰色の下地に切り替える）
                entry.color = data.DisplayRarityColor;
                entry.glyph = AchievementRarityIcons.SilhouetteOf(data.Rarity, data.IsSpecial);
                entry.lockGlyph = AchievementVisuals.LockIcon();
                entries.Add(entry);
            }
        }

        // レアリティ順の並び：銅→銀→金→プラチナ→ダイヤモンド→特別
        static int RarityRank(AchievementData data)
        {
            if (data.IsSpecial) return 5;
            return (int)data.Rarity;
        }
    }
}
