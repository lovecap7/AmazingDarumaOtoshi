using UnityEngine;

namespace Yurube.Achievements
{
    // メニューの「じっせき」（Nakagawa さんの AchievementBook）に、このシステムの実績を並べる係。
    // AchievementBook は static な Entries を開いたときに読むだけなので、起動時と変化したときにここで入れ直す。
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

        // 実績の一覧を、メニューの「じっせき」へ入れ直す
        public static void Sync()
        {
            // 実績ラボのデバッグ用データはメニューに出さない（ふだんのデータのときだけ）
            if (AchievementManager.StorageKey != AchievementManager.NormalStorageKey) return;

            var entries = AchievementBook.Entries;
            entries.Clear();
            foreach (var data in AchievementManager.All)
            {
                entries.Add(new AchievementBook.Entry(data.Name, data.Description, data.IsUnlocked));
            }
        }
    }
}
