using System.Collections;
using UnityEngine;
using Yurube.Achievements;

namespace Yurube.Story
{
    // 試合のない章（「プロローグ：八年前の槌音」など）を再生する係。
    //  ・AllStages へ遷移せず、MenuScene 上で「テロップ → 会話（演出・SEつき）→ だるま新聞 → クリア保存 → 章選択へ戻る」を行う。
    //  ・再プレイ時にテロップで B スキップした場合は、現行章と同様に会話パートもスキップして新聞へ進む。
    public class StoryDramaDirector : MonoBehaviour
    {
        const string MenuSceneName = "MenuScene";

        static StoryDramaDirector s_current;

        StoryChapter m_chapter;
        DialogueRunner m_dialogue;
        StoryMovie m_movie;
        bool m_prevFog;
        bool m_hasPrevFog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_current = null;

        public static void StartChapter(StoryChapter chapter)
        {
            if (chapter == null || s_current != null) return;

            var mainMenu = FindAnyObjectByType<MainMenu>();
            if (mainMenu != null) mainMenu.enabled = false;

            var go = new GameObject("StoryDramaDirector");
            var director = go.AddComponent<StoryDramaDirector>();
            s_current = director;
            director.Begin(chapter);
        }

        void Begin(StoryChapter chapter)
        {
            m_chapter = chapter;
            StorySession.Begin(chapter);
            m_prevFog = RenderSettings.fog;
            m_hasPrevFog = true;
            RenderSettings.fog = false;
            StartCoroutine(Run());
        }

        void OnDestroy()
        {
            if (s_current == this) s_current = null;
            PreludeSfx.StopAll();
            if (m_hasPrevFog) RenderSettings.fog = m_prevFog;
            if (m_dialogue != null)
            {
                m_dialogue.StoryLineStarted -= OnStoryLineStarted;
                m_dialogue.StoryLineTextCompleted -= OnStoryLineTextCompleted;
                m_dialogue.DialogueSkipped -= OnDialogueSkipped;
            }
            if (m_movie != null)
            {
                m_movie.CleanupVfxAfterDialogue(clearImmediate: true);
                Destroy(m_movie.gameObject);
            }
            if (m_dialogue != null) Destroy(m_dialogue.gameObject);
        }

        void OnStoryLineStarted(StoryLine line)
        {
            if (m_movie != null) m_movie.OnDialogueLine(line);
            if (line != null && !string.IsNullOrEmpty(line.se)) PreludeSfx.Play(line.se, this);
        }

        void OnStoryLineTextCompleted(StoryLine line)
        {
            if (m_movie != null) m_movie.OnDialogueLineTextCompleted(line);
        }

        void OnDialogueSkipped()
        {
            if (m_movie != null) m_movie.OnDialogueSkipped();
        }

        IEnumerator Run()
        {
            m_dialogue = DialogueRunner.Create();
            m_dialogue.StoryLineStarted += OnStoryLineStarted;
            m_dialogue.StoryLineTextCompleted += OnStoryLineTextCompleted;
            m_dialogue.DialogueSkipped += OnDialogueSkipped;

            m_movie = StoryMovie.Create(m_chapter);
            m_movie.SetBlack();
            m_movie.Show();

            // 1. テロップ（A：つぎへ / B：スキップ）
            yield return m_movie.PlayPreludeIntro(m_chapter);

            // 2. 演出つき会話（章クリア済みの再プレイでテロップを B スキップしたら、会話もスキップする）
            bool skipBefore = StoryProgress.IsCleared(m_chapter.id) && m_movie.WasSkipped;
            if (!skipBefore && m_chapter.before != null && m_chapter.before.Count > 0)
            {
                // 背景のカットシーンが見えやすいよう、会話ウィンドウを画面最下部に小さく配置し、背景暗幕も最小限にする
                m_dialogue.SetCompactBottomLayout(true);
                m_dialogue.SetDimAlpha(0.05f);
                yield return m_dialogue.PlayBig(m_chapter, m_chapter.before);
            }

            PreludeSfx.StopAll();
            m_dialogue.SetDimAlpha(0.55f);
            // 会話の終了・スキップ後に暗転や保留中エフェクトが残らないよう整理してから新聞へ遷移する
            if (m_dialogue.WasSkipped)
            {
                m_movie.CleanupVfxAfterDialogue(clearImmediate: true);
            }
            yield return m_movie.FadeToBlack(0.35f);
            m_movie.HideViewKeepBlack();

            // 3. だるま新聞（号外）
            yield return m_dialogue.PlayNewspaper(m_chapter.newspaper);

            // 4. クリア保存（プロローグは新規実績なしだが、将来の互換性のため通知は通常どおり呼ぶ）
            AchievementManager.ReportStoryCleared(m_chapter.id);
            string newlyUnlocked = StoryProgress.MarkCleared(m_chapter.id);
            if (!string.IsNullOrEmpty(newlyUnlocked))
                StorySession.PendingUnlockChapterId = newlyUnlocked;

            // 5. 黒い画面を残したままメニュー（章選択）へ戻る
            m_dialogue.HideHint();
            m_movie.HideHint();
            m_movie.ShowBlackCover();

            StorySession.End();
            StorySession.ReturnToChapterSelect = true;
            Fader.Load(MenuSceneName);
        }
    }
}
