using System.Collections.Generic;
using UnityEngine;

namespace Yurube.Story
{
    // 章の進行（ストーリー全体のセーブ）。PlayerPrefs に保存する（GameSettings・実績と同じ方式）。
    public static class StoryProgress
    {
        const string KeyPrefix = "Story.Clear.";
        const string UnlockKeyPrefix = "Story.Unlock.";
        const string UnlockSeenKeyPrefix = "Story.UnlockSeen.";
        // 最終章のエンディング到達フラグ（隠しストーリー解放判定用）
        public const string EndingTrueKey = "Story.Ending.True";
        public const string EndingBadKey = "Story.Ending.Bad";
        // トゥルーエンド報酬：だるきんぐ解放フラグ
        public const string DaruKingUnlockKey = "Story.UnlockChar.daruking";
        // 旧 chapter3（旧最終章）→ final への1回限りの移行済みフラグ
        const string MigrationKeyV3Chapter3 = "Story.Migration.v3Chapter3ToFinal";

        static bool s_migrated;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetMigrationCache() => s_migrated = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BootstrapMigration() => EnsureMigrated();

        // 旧最終章（旧 chapter3）のセーブを1回だけ final へ移行する。
        //  ・旧最終章クリア済み → Story.Clear.final / Story.Unlock.final / Story.UnlockSeen.final を立てる
        //  ・旧最終章未クリア → final は立てず、新第3章クリアまでロックのまま
        //  ・旧 chapter3 側のキーはすべて消し、新第3章は未クリア扱い（第2章クリア済みなら requiredChapter で解放）
        public static void EnsureMigrated()
        {
            if (s_migrated) return;
            s_migrated = true;

            if (PlayerPrefs.GetInt(MigrationKeyV3Chapter3, 0) == 1) return;

            string oldClearKey = KeyPrefix + "chapter3";
            string oldUnlockKey = UnlockKeyPrefix + "chapter3";
            string oldUnlockSeenKey = UnlockSeenKeyPrefix + "chapter3";

            bool oldFinalCleared = PlayerPrefs.GetInt(oldClearKey, 0) == 1;
            if (oldFinalCleared)
            {
                PlayerPrefs.SetInt(KeyPrefix + StoryLauncher.FinalId, 1);
                PlayerPrefs.SetInt(UnlockKeyPrefix + StoryLauncher.FinalId, 1);
                PlayerPrefs.SetInt(UnlockSeenKeyPrefix + StoryLauncher.FinalId, 1);
            }

            PlayerPrefs.DeleteKey(oldClearKey);
            PlayerPrefs.DeleteKey(oldUnlockKey);
            PlayerPrefs.DeleteKey(oldUnlockSeenKey);
            PlayerPrefs.SetInt(MigrationKeyV3Chapter3, 1);
            PlayerPrefs.Save();

            if (oldFinalCleared)
                Debug.Log("[Story] 旧最終章（chapter3）のクリア記録を final へ移行しました");
        }

        // 章をクリア済みか
        public static bool IsCleared(string chapterId)
        {
            EnsureMigrated();
            if (string.IsNullOrEmpty(chapterId)) return false;
            return PlayerPrefs.GetInt(KeyPrefix + chapterId, 0) == 1;
        }

        // 章が遊べるか（解放済みまたはクリア済みとして保存されているか、または解放条件の章をクリア済みか）
        public static bool IsUnlocked(StoryChapter chapter)
        {
            EnsureMigrated();
            if (chapter == null) return false;
            if (PlayerPrefs.GetInt(UnlockKeyPrefix + chapter.id, 0) == 1) return true;
            if (IsCleared(chapter.id)) return true;
            return string.IsNullOrEmpty(chapter.requiredChapter) || IsCleared(chapter.requiredChapter);
        }

        public static bool IsUnlocked(string chapterId)
        {
            EnsureMigrated();
            if (string.IsNullOrEmpty(chapterId)) return false;
            if (PlayerPrefs.GetInt(UnlockKeyPrefix + chapterId, 0) == 1) return true;
            var chapter = StoryData.Load(chapterId);
            return chapter != null && IsUnlocked(chapter);
        }

        // 章の解放を保存する
        public static void MarkUnlocked(string chapterId)
        {
            EnsureMigrated();
            if (string.IsNullOrEmpty(chapterId)) return;
            PlayerPrefs.SetInt(UnlockKeyPrefix + chapterId, 1);
            PlayerPrefs.Save();
        }

        // 解放演出を再生済みか
        public static bool HasSeenUnlockAnim(string chapterId)
        {
            EnsureMigrated();
            if (string.IsNullOrEmpty(chapterId)) return true;
            return PlayerPrefs.GetInt(UnlockSeenKeyPrefix + chapterId, 0) == 1;
        }

        public static void MarkUnlockAnimSeen(string chapterId)
        {
            EnsureMigrated();
            if (string.IsNullOrEmpty(chapterId)) return;
            PlayerPrefs.SetInt(UnlockSeenKeyPrefix + chapterId, 1);
            PlayerPrefs.Save();
        }

        // 章クリアを記録する（新聞まで見終わったとき）。
        // 戻り値：このクリアで初めて解放された次の章ID（なければ null）
        public static string MarkCleared(string chapterId)
        {
            EnsureMigrated();
            if (string.IsNullOrEmpty(chapterId)) return null;
            string newlyUnlockedId = null;

            // クリア前に未解放だった章のうち、この章のクリアで解放されるものを探す
            var all = StoryData.LoadAll();
            var wasUnlocked = new HashSet<string>();
            foreach (var c in all)
            {
                if (c != null && IsUnlocked(c)) wasUnlocked.Add(c.id);
            }

            PlayerPrefs.SetInt(KeyPrefix + chapterId, 1);
            MarkUnlocked(chapterId);

            foreach (var c in all)
            {
                if (c == null || c.requiredChapter != chapterId) continue;
                MarkUnlocked(c.id);
                if (!wasUnlocked.Contains(c.id) && !HasSeenUnlockAnim(c.id) && newlyUnlockedId == null)
                    newlyUnlockedId = c.id;
            }

            PlayerPrefs.Save();
            Debug.Log("[Story] 章クリアを保存: " + chapterId + (newlyUnlockedId != null ? "（新規解放: " + newlyUnlockedId + "）" : ""));
            return newlyUnlockedId;
        }

        // 全章クリア済みか（最終章クリア判定用）
        public static bool IsAllCleared()
        {
            EnsureMigrated();
            var all = StoryData.LoadAll();
            if (all.Count == 0) return false;
            foreach (var c in all)
            {
                if (c != null && !IsCleared(c.id)) return false;
            }
            return true;
        }

        // 物語が途中か（1章以上クリアしていて、まだクリアしていない章がある）。
        // メニューの初期カーソルを「ものがたり」にする判定に使う。全章クリア後は false。
        public static bool HasStoryInProgress()
        {
            EnsureMigrated();
            const int PlannedChapterSlots = 7;   // プロローグ＋序章＋第1章＋第2章＋第3章＋第4章＋最終章
            int chapterCount = 0;
            bool anyCleared = false;
            bool anyUncleared = false;
            foreach (var chapter in StoryData.LoadAll())
            {
                if (chapter == null) continue;
                chapterCount++;
                if (IsCleared(chapter.id)) anyCleared = true;
                else anyUncleared = true;
            }
            if (!anyCleared) return false;
            if (anyUncleared) return true;
            return chapterCount < PlannedChapterSlots;
        }

        // 全章を解放する（デバッグ用）
        public static void UnlockAll()
        {
            EnsureMigrated();
            foreach (var chapter in StoryData.LoadAll())
            {
                if (chapter == null) continue;
                MarkUnlocked(chapter.id);
                MarkUnlockAnimSeen(chapter.id);
            }
            PlayerPrefs.Save();
            Debug.Log("[Story] 全章を解放しました（デバッグ）");
        }

        // エンディング到達の記録（トゥルー／バッド。どちらも到達済みなら両方 1）
        public static bool IsTrueEndingReached()
        {
            EnsureMigrated();
            return PlayerPrefs.GetInt(EndingTrueKey, 0) == 1;
        }

        public static bool IsBadEndingReached()
        {
            EnsureMigrated();
            return PlayerPrefs.GetInt(EndingBadKey, 0) == 1;
        }

        public static void MarkEndingReached(bool isTrueEnding)
        {
            EnsureMigrated();
            string key = isTrueEnding ? EndingTrueKey : EndingBadKey;
            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
            Debug.Log("[Story] エンディング到達を保存: " + key);
        }

        // だるきんぐがプレイアブルとして解放済みか（トゥルーエンド到達で解放）
        public static bool IsDaruKingUnlocked()
        {
            EnsureMigrated();
            return PlayerPrefs.GetInt(DaruKingUnlockKey, 0) == 1 || PlayerPrefs.GetInt(EndingTrueKey, 0) == 1;
        }

        // だるきんぐを解放して保存する。今回初めて解放したときだけ true（再プレイ時の通知重複を防ぐ）
        public static bool UnlockDaruKing()
        {
            EnsureMigrated();
            if (PlayerPrefs.GetInt(DaruKingUnlockKey, 0) == 1) return false;
            PlayerPrefs.SetInt(DaruKingUnlockKey, 1);
            PlayerPrefs.Save();
            Debug.Log("[Story] キャラクター解放を保存: daruking");
            return true;
        }

        // 全章のセーブを初期化する（デバッグ用。序章のみ解放に戻る）
        public static void ResetAll()
        {
            EnsureMigrated();
            foreach (var chapter in StoryData.LoadAll())
            {
                if (chapter != null) Clear(chapter.id);
            }
            PlayerPrefs.DeleteKey(EndingTrueKey);
            PlayerPrefs.DeleteKey(EndingBadKey);
            PlayerPrefs.DeleteKey(DaruKingUnlockKey);
            PlayerPrefs.Save();
            Debug.Log("[Story] 章の進行を初期化しました（デバッグ）");
        }

        // 記録を消す（デバッグ用）
        public static void Clear(string chapterId)
        {
            EnsureMigrated();
            if (string.IsNullOrEmpty(chapterId)) return;
            PlayerPrefs.DeleteKey(KeyPrefix + chapterId);
            PlayerPrefs.DeleteKey(UnlockKeyPrefix + chapterId);
            PlayerPrefs.DeleteKey(UnlockSeenKeyPrefix + chapterId);
            PlayerPrefs.Save();
        }
    }

    // いま遊んでいる章の状態。シーンを読み直しても消えない（static）ので、
    // 敗北→リトライのときに「会話（前）を飛ばす」「チュートリアルの続きから」に使う。
    public static class StorySession
    {
        public static StoryChapter Chapter { get; private set; }
        public static bool IsRunning => Chapter != null;

        // 会話（前）を表示済みか（リトライでは出さない）
        public static bool IntroPlayed { get; private set; }
        // 完了したチュートリアルのステップ数（この番号から再開する）
        public static int TutorialStep { get; set; }
        // 初回限定の掛け合い（黄色い積み木）を出したか
        public static bool YellowExtraPlayed { get; set; }
        // 必殺技の覚醒イベントを終えたか（リトライでも保持する）
        public static bool AwakeningDone { get; set; }
        // 「追い詰められたら撃つ」イベントの掛け合いを一度見たか（リトライでは補助だけ）
        public static bool LastStandPlayed { get; set; }
        // 試合前の一言（matchStart）を出したか。リトライでは繰り返さない
        public static bool MatchStartPlayed { get; set; }
        // 敗北した回数（デバッグ表示用）
        public static int RetryCount { get; private set; }
        // 章クリア後にメニューへ戻った際、自動で章選択画面を開くフラグ
        public static bool ReturnToChapterSelect { get; set; }
        // 章クリアで新しく解放された章ID（章選択で解放演出を再生するため）
        public static string PendingUnlockChapterId { get; set; }

        public static bool ConsumeReturnToChapterSelect()
        {
            bool v = ReturnToChapterSelect;
            ReturnToChapterSelect = false;
            return v;
        }

        public static string ConsumePendingUnlockChapter()
        {
            string id = PendingUnlockChapterId;
            PendingUnlockChapterId = null;
            return id;
        }

        // 初回だけの演出を覚えておく（章ID + 種類）
        static readonly HashSet<string> s_events = new HashSet<string>();

        public static bool HasEvent(string key)
        {
            return !string.IsNullOrEmpty(key) && s_events.Contains(key);
        }

        public static void MarkEvent(string key)
        {
            if (!string.IsNullOrEmpty(key)) s_events.Add(key);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Chapter = null;
            IntroPlayed = false;
            TutorialStep = 0;
            YellowExtraPlayed = false;
            AwakeningDone = false;
            LastStandPlayed = false;
            MatchStartPlayed = false;
            RetryCount = 0;
            ReturnToChapterSelect = false;
            PendingUnlockChapterId = null;
            s_events.Clear();
        }

        // 章を始める（最初から）
        public static void Begin(StoryChapter chapter)
        {
            Chapter = chapter;
            IntroPlayed = false;
            TutorialStep = 0;
            YellowExtraPlayed = false;
            AwakeningDone = false;
            LastStandPlayed = false;
            MatchStartPlayed = false;
            RetryCount = 0;
            ReturnToChapterSelect = false;
            PendingUnlockChapterId = null;
            s_events.Clear();
        }

        public static void MarkIntroPlayed() => IntroPlayed = true;

        public static void CountRetry()
        {
            RetryCount++;
            Debug.Log("[Story] リトライ " + RetryCount + " 回目（チュートリアルは " + TutorialStep + " 番から）");
        }

        public static void End() => Chapter = null;
    }
}
