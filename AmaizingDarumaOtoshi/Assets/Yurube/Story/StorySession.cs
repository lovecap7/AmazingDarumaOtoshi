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

        // 章をクリア済みか
        public static bool IsCleared(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return false;
            return PlayerPrefs.GetInt(KeyPrefix + chapterId, 0) == 1;
        }

        // 章が遊べるか（解放済みとして保存されているか、または解放条件の章をクリア済みか）
        public static bool IsUnlocked(StoryChapter chapter)
        {
            if (chapter == null) return false;
            if (PlayerPrefs.GetInt(UnlockKeyPrefix + chapter.id, 0) == 1) return true;
            return string.IsNullOrEmpty(chapter.requiredChapter) || IsCleared(chapter.requiredChapter);
        }

        public static bool IsUnlocked(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return false;
            if (PlayerPrefs.GetInt(UnlockKeyPrefix + chapterId, 0) == 1) return true;
            var chapter = StoryData.Load(chapterId);
            return chapter != null && IsUnlocked(chapter);
        }

        // 章の解放を保存する
        public static void MarkUnlocked(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return;
            PlayerPrefs.SetInt(UnlockKeyPrefix + chapterId, 1);
            PlayerPrefs.Save();
        }

        // 解放演出を再生済みか
        public static bool HasSeenUnlockAnim(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return true;
            return PlayerPrefs.GetInt(UnlockSeenKeyPrefix + chapterId, 0) == 1;
        }

        public static void MarkUnlockAnimSeen(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return;
            PlayerPrefs.SetInt(UnlockSeenKeyPrefix + chapterId, 1);
            PlayerPrefs.Save();
        }

        // 章クリアを記録する（新聞まで見終わったとき）。
        // 戻り値：このクリアで初めて解放された次の章ID（なければ null）
        public static string MarkCleared(string chapterId)
        {
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
            const int PlannedChapterSlots = 4;   // 序章＋第1章＋第2章＋最終章
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
            foreach (var chapter in StoryData.LoadAll())
            {
                if (chapter == null) continue;
                MarkUnlocked(chapter.id);
                MarkUnlockAnimSeen(chapter.id);
            }
            PlayerPrefs.Save();
            Debug.Log("[Story] 全章を解放しました（デバッグ）");
        }

        // 全章のセーブを初期化する（デバッグ用。序章のみ解放に戻る）
        public static void ResetAll()
        {
            foreach (var chapter in StoryData.LoadAll())
            {
                if (chapter != null) Clear(chapter.id);
            }
            PlayerPrefs.Save();
            Debug.Log("[Story] 章の進行を初期化しました（デバッグ）");
        }

        // 記録を消す（デバッグ用）
        public static void Clear(string chapterId)
        {
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
