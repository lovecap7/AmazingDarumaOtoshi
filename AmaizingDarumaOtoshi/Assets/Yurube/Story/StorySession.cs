using System.Collections.Generic;
using UnityEngine;

namespace Yurube.Story
{
    // 章の進行（ストーリー全体のセーブ）。PlayerPrefs に保存する（GameSettings・実績と同じ方式）。
    public static class StoryProgress
    {
        const string KeyPrefix = "Story.Clear.";

        // 章をクリア済みか
        public static bool IsCleared(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return false;
            return PlayerPrefs.GetInt(KeyPrefix + chapterId, 0) == 1;
        }

        // 章が遊べるか（解放条件の章をクリア済みか）
        public static bool IsUnlocked(StoryChapter chapter)
        {
            if (chapter == null) return false;
            return string.IsNullOrEmpty(chapter.requiredChapter) || IsCleared(chapter.requiredChapter);
        }

        // 章クリアを記録する（新聞まで見終わったとき）
        public static void MarkCleared(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return;
            PlayerPrefs.SetInt(KeyPrefix + chapterId, 1);
            PlayerPrefs.Save();
            Debug.Log("[Story] 章クリアを保存: " + chapterId);
        }

        // 記録を消す（デバッグ用）
        public static void Clear(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return;
            PlayerPrefs.DeleteKey(KeyPrefix + chapterId);
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
        // 試合前の一言（matchStart）を出したか。リトライでは繰り返さない
        public static bool MatchStartPlayed { get; set; }
        // 敗北した回数（デバッグ表示用）
        public static int RetryCount { get; private set; }

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
            MatchStartPlayed = false;
            RetryCount = 0;
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
            MatchStartPlayed = false;
            RetryCount = 0;
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
