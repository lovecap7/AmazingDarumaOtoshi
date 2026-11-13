using System.Collections;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using Yurube.Achievements;

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
        bool m_reflected;
        float m_reflectIdle;
        // 連鎖チュートリアル：相手に当てたあと、連鎖が起きるまでを受け付ける時間
        bool m_chained;
        float m_chainOpponentHitUntil = -1f;
        readonly Dictionary<MatchPlayer, bool> m_cpuIdle = new Dictionary<MatchPlayer, bool>();
        readonly Dictionary<MatchPlayer, DarumaCharacter> m_cpuBodies = new Dictionary<MatchPlayer, DarumaCharacter>();

        public void Setup(StoryChapter chapter, DialogueRunner dialogue, StoryMatchDirector director, DarumaMatch match)
        {
            m_chapter = chapter;
            m_dialogue = dialogue;
            m_director = director;
            m_match = match;
            m_player = director != null ? director.Player : null;
        }

        void OnEnable()
        {
            DarumaHammer.Hit += OnHammerHit;
            TumikiBlock.Hit += OnBlockHit;
            AchievementEvents.Chain += OnChain;
        }

        void OnDisable()
        {
            DarumaHammer.Hit -= OnHammerHit;
            TumikiBlock.Hit -= OnBlockHit;
            AchievementEvents.Chain -= OnChain;
        }

        void OnDestroy()
        {
            Unhook();
        }

        // プレイヤーが飛んできた積み木を打ち返した
        void OnHammerHit(HammerHitInfo info)
        {
            if (info.ReturnCount <= 0) return;
            if (m_player == null || m_player.Character == null) return;
            if (!ReferenceEquals(info.Owner, m_player.Character)) return;
            if (!m_reflected) Debug.Log("[Story] 打ち返し成功（チュートリアル）");
            m_reflected = true;
        }

        // プレイヤーの弾が相手（CPU）に当たった
        void OnBlockHit(TumikiHitInfo info)
        {
            if (!info.IsHarmful) return;
            if (m_player == null || m_player.Character == null) return;
            if (!ReferenceEquals(info.Block.Owner, m_player.Character)) return;
            if (!(info.Target is DarumaCharacter target) || target == m_player.Character) return;
            m_chainOpponentHitUntil = Time.unscaledTime + 1.2f;
        }

        // 相手に当てたあと、抜けた積み木が別の積み木に当たって連鎖した
        void OnChain(int length)
        {
            if (Time.unscaledTime <= m_chainOpponentHitUntil) m_chained = true;
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

            // 章データに「開始！」の文言がある章は、GO! の直後に出す
            // （序章など文言のない章は、これまでどおりチュートリアルの後に「練習試合、開始！」を出す）
            bool noticeShownAtStart = false;
            if (!StorySession.MatchStartPlayed && !string.IsNullOrEmpty(m_chapter.matchStartNotice))
            {
                yield return m_dialogue.PlaySmall("", m_chapter.matchStartNotice, m_chapter);
                noticeShownAtStart = true;
            }

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

                if (condition == "DialogueDone")
                {
                    StorySession.TutorialStep = i + 1;
                    yield return new WaitForSecondsRealtime(0.25f);
                    continue;
                }

                // このステップの間だけ動かすCPU（第1章の打ち返しは、いわだるだけ動かす）
                SetCpusForStep(step);
                if (condition == "Reflect")
                    Debug.Log("[Story] 打ち返しチュートリアル開始（いわだるの積み木を待つ）");

                // 実際にやってもらう。できるまで次の説明には進まない
                m_dialogue.ShowObjective(m_chapter, step.speaker, step.text);
                yield return WaitCondition(condition, Mathf.Max(1, step.count));
                // 操作をやり終えてから「済み」にする（途中で負けたら、このステップからやり直す）
                StorySession.TutorialStep = i + 1;
                m_dialogue.HideObjective();
                SetCpusIdle(true);
                yield return new WaitForSecondsRealtime(0.2f);
            }

            // 締めの一言 → CPU を動かして試合開始（リトライでは繰り返さない）
            if (!StorySession.MatchStartPlayed)
            {
                StorySession.MatchStartPlayed = true;
                var startLines = m_chapter.matchStart;
                if (startLines != null && startLines.Count > 0)
                {
                    m_director.SetStoryPaused(true);
                    yield return m_dialogue.PlayBig(m_chapter, startLines);
                }
                SetCpusIdle(false);
                m_director.SetStoryPaused(false);

                if (!noticeShownAtStart)
                {
                    string notice = string.IsNullOrEmpty(m_chapter.matchStartNotice)
                        ? "練習試合、開始！"
                        : m_chapter.matchStartNotice;
                    m_dialogue.StartCoroutine(m_dialogue.PlaySmall("", notice, m_chapter));
                }
            }
            else
            {
                // リトライ：前回すでに試合前の一言は見ている
                SetCpusIdle(false);
                m_director.SetStoryPaused(false);
            }
        }

        // ========================================
        // CPU を見学にする／戻す
        // ========================================

        void SetCpusForStep(TutorialStep step)
        {
            string who = step != null ? step.cpu : null;
            if (string.IsNullOrEmpty(who))
            {
                SetCpusIdle(true);
                return;
            }
            if (m_match == null) return;
            foreach (var p in m_match.Players)
            {
                if (p == null || !p.IsCpu) continue;
                bool active = who == "all" || (p.CharacterData != null && p.CharacterData.id == who);
                SetCpuIdle(p, !active);
            }
        }

        void SetCpusIdle(bool idle)
        {
            if (m_match == null) return;
            foreach (var p in m_match.Players)
            {
                if (p == null || !p.IsCpu) continue;
                SetCpuIdle(p, idle);
            }
        }

        void SetCpuIdle(MatchPlayer p, bool idle)
        {
            bool changed = !m_cpuIdle.TryGetValue(p, out bool was) || was != idle;
            m_cpuIdle[p] = idle;
            if (changed) m_cpuBodies.Remove(p);
            ApplyCpuState(p);
        }

        // 体が作り直された（復活した）ときも、いまの見学／稼働の状態を掛け直す
        void ApplyCpuState(MatchPlayer p)
        {
            if (p == null || !p.IsCpu) return;
            bool idle = m_cpuIdle.TryGetValue(p, out bool value) && value;
            if (p.Character != null && m_cpuBodies.TryGetValue(p, out var body) && body == p.Character) return;

            // 操作の供給元（DarumaCpu など）を止める
            if (p.CommandSource is MonoBehaviour behaviour) behaviour.enabled = !idle;
            if (p.Character == null) return;

            m_cpuBodies[p] = p.Character;
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

        void ApplyCpuStates()
        {
            if (m_match == null) return;
            foreach (var p in m_match.Players) ApplyCpuState(p);
        }

        IEnumerator WaitCondition(string condition, int count)
        {
            m_moved = false;
            m_jumped = false;
            m_attacked = false;
            m_reflected = false;
            m_reflectIdle = 0f;
            m_chained = false;
            m_chainOpponentHitUntil = -1f;
            m_startStack = m_player != null ? m_player.StackCount : 0;
            float assistSeconds = StorySettings.Instance != null ? StorySettings.Instance.ReflectAssistSeconds : 20f;
            var settings = StorySettings.Instance;
            float chainHintSeconds = settings != null ? settings.ChainHintSeconds : 30f;
            float chainAssistSeconds = settings != null ? settings.ChainAssistSeconds : 45f;
            float chainStart = Time.unscaledTime;
            bool chainHintShown = false;
            bool chainAssistDone = false;

            while (true)
            {
                ApplyCpuStates();
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

                    case "Reflect":
                        if (m_reflected) yield break;
                        // いわだるが積み木を飛ばしてこないときの詰まり防止
                        bool incoming = m_director != null && m_director.HasIncomingBlockToPlayer();
                        m_reflectIdle = incoming ? 0f : m_reflectIdle + Time.unscaledDeltaTime;
                        if (m_reflectIdle >= assistSeconds)
                        {
                            if (m_director != null) m_director.FireReflectAssist();
                            m_reflectIdle = 0f;
                        }
                        break;

                    case "Chain":
                        if (m_chained) yield break;
                        // 30秒で操作バナーを強調し、さらに45秒で連鎖しやすい位置に積み木を出す
                        float elapsed = Time.unscaledTime - chainStart;
                        if (!chainHintShown && elapsed >= chainHintSeconds)
                        {
                            chainHintShown = true;
                            if (m_dialogue != null) m_dialogue.SetObjectiveEmphasis(true);
                            if (m_director != null) m_director.NotifyChainHint();
                        }
                        if (!chainAssistDone && elapsed >= chainHintSeconds + chainAssistSeconds)
                        {
                            chainAssistDone = true;
                            if (m_director != null) m_director.SpawnChainAssist();
                        }
                        break;

                    default:
                        yield break;
                }
                yield return null;
            }
        }
    }
}
