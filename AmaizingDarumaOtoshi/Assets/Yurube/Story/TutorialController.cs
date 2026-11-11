using System.Collections;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube.Story
{
    // 対戦中のチュートリアル。章データ（tutorial）のステップを上から順に進める。
    //  ・各ステップの頭で試合を止め、大きいウィンドウ説明を読む（Aで次へ／Bでスキップ）
    //  ・説明のあと動かして、指定の操作をするまで次の説明には進まない
    //    操作のあいだは、画面の上に「いま何をすればいいか」を出しておく
    //  ・チュートリアルのあいだ CPU は止めて、攻撃してこないようにする（当たり判定も消す）
    //  ・最後の一言のあと CPU を動かして試合開始
    // 到達したステップは StorySession に覚えておき、リトライ時は続きから
    // 条件（condition）：MoveAndJump / Stack / Attack / DialogueDone
    public class TutorialController : MonoBehaviour
    {
        StoryChapter m_chapter;
        DialogueRunner m_dialogue;
        StoryMatchDirector m_director;
        DarumaMatch m_match;
        MatchPlayer m_player;

        DarumaCharacter m_hooked;
        bool m_moved, m_jumped, m_attacked;
        int m_startStack;
        bool m_cpusIdle;

        public void Setup(StoryChapter chapter, DialogueRunner dialogue, StoryMatchDirector director, DarumaMatch match)
        {
            m_chapter = chapter;
            m_dialogue = dialogue;
            m_director = director;
            m_match = match;
            m_player = director != null ? director.Player : null;
        }

        void OnDestroy()
        {
            Unhook();
        }

        void Hook()
        {
            var character = m_player != null ? m_player.Character : null;
            if (character == m_hooked) return;
            Unhook();
            m_hooked = character;
            if (m_hooked != null) m_hooked.CommandReceived += OnCommand;
        }

        void Unhook()
        {
            if (m_hooked != null) m_hooked.CommandReceived -= OnCommand;
            m_hooked = null;
        }

        void OnCommand(Nakahira.DarumaCommand command)
        {
            if (command.Jump) m_jumped = true;
            if (command.Sweep || command.Shot) m_attacked = true;
        }

        public IEnumerator Run()
        {
            if (m_chapter == null || m_player == null || m_director == null) yield break;

            // チュートリアルのあいだ CPU は見学（動かない・当たらない）
            SetCpusIdle(true);

            // 開始のカウントダウンが終わるまで待つ（会話のあと）
            while (m_match != null && m_match.IsCountingDown) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);

            var steps = m_chapter.tutorial;
            for (int i = StorySession.TutorialStep; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step == null) { StorySession.TutorialStep = i + 1; continue; }
                string condition = string.IsNullOrEmpty(step.condition) ? "DialogueDone" : step.condition;

                // 説明：試合を止めて、大きいウィンドウで読む
                m_director.SetStoryPaused(true);
                var line = new StoryLine { speaker = step.speaker, text = step.text };
                yield return m_dialogue.PlayBig(m_chapter, new List<StoryLine> { line });
                m_director.SetStoryPaused(false);
                StorySession.TutorialStep = i + 1;

                if (condition == "DialogueDone")
                {
                    yield return new WaitForSecondsRealtime(0.25f);
                    continue;
                }

                // 実際にやってもらう。できるまで次の説明には進まない
                m_dialogue.ShowObjective(m_chapter, step.speaker, step.text);
                yield return WaitCondition(condition, Mathf.Max(1, step.count));
                m_dialogue.HideObjective();
                yield return new WaitForSecondsRealtime(0.2f);
            }

            // 締めの一言 → CPU を動かして試合開始
            m_director.SetStoryPaused(true);
            var startLines = m_chapter.matchStart != null && m_chapter.matchStart.Count > 0
                ? m_chapter.matchStart
                : new List<StoryLine> { new StoryLine { speaker = "midaru", text = "では……一試合、やりましょう" } };
            yield return m_dialogue.PlayBig(m_chapter, startLines);
            SetCpusIdle(false);
            m_director.SetStoryPaused(false);

            m_dialogue.StartCoroutine(m_dialogue.PlaySmall("", "練習試合、開始！", m_chapter));
        }

        // ========================================
        // CPU を見学にする／戻す
        // ========================================

        void SetCpusIdle(bool idle)
        {
            if (m_match == null) return;
            if (m_cpusIdle == idle) return;
            m_cpusIdle = idle;

            foreach (var p in m_match.Players)
            {
                if (p == null || !p.IsCpu) continue;

                // 操作の供給元（DarumaCpu など）を止める
                if (p.CommandSource is MonoBehaviour behaviour) behaviour.enabled = !idle;

                if (p.Character == null) continue;
                if (idle)
                {
                    p.Character.SetCommandSource(null);
                    p.Character.SetSuspended(true);   // 当たり判定も消えるので、攻撃が当たらない
                }
                else
                {
                    p.Character.SetSuspended(false);
                    p.Character.SetCommandSource(p.CommandSource);
                }
            }
        }

        IEnumerator WaitCondition(string condition, int count)
        {
            m_moved = false;
            m_jumped = false;
            m_attacked = false;
            m_startStack = m_player != null ? m_player.StackCount : 0;

            while (true)
            {
                Hook();
                var character = m_player != null ? m_player.Character : null;
                if (character == null) { yield return null; continue; }

                switch (condition)
                {
                    case "MoveAndJump":
                        if (character.HorizontalVelocity.magnitude > 2f) m_moved = true;
                        if (m_moved && m_jumped) yield break;
                        break;

                    case "Stack":
                        if (m_player.StackCount >= m_startStack + count) yield break;
                        break;

                    case "Attack":
                        if (m_attacked) yield break;
                        break;

                    default:
                        yield break;
                }
                yield return null;
            }
        }
    }
}
