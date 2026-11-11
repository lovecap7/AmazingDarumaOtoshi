using System.Collections;
using System.Collections.Generic;
using Nakagawa;
using Nakahira;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yurube;
using Yurube.Achievements;

namespace Yurube.Story
{
    // ストーリーの試合を進行する係。ストーリーの試合シーンに自動で付く（シーンに置く必要はない）。
    //  ・会話（前）→ チュートリアル → 勝敗で分岐
    //  ・会話中は試合を止める（DarumaMatch.Pause）。ただしポーズ画面は出さない（PauseMenu.SuppressForStory）
    //  ・勝利：実績カード → 会話（後）→ だるま新聞 → 章クリア保存 → メニューへ
    //  ・敗北：1行の台詞 → 同じシーンを読み直して再開（会話（前）は出さない・チュートリアルは続きから）
    // 試合のルール自体は変えない（上に重ねるレイヤー）。実績の解除判定はふつうに動く。
    [DefaultExecutionOrder(-50)]
    public class StoryMatchDirector : MonoBehaviour
    {
        const string MenuSceneName = "MenuScene";

        DarumaMatch m_match;
        DialogueRunner m_dialogue;
        StoryMovie m_movie;
        TutorialController m_tutorial;
        MatchPlayer m_player;
        StoryChapter m_chapter;

        int m_yellowCount;
        DarumaCharacter m_yellowCharacter;
        bool m_finishedHandled;
        bool m_leavingForRetry;
        bool m_koRunning;
        readonly HashSet<string> m_taunted = new HashSet<string>();

        // チュートリアルから使う
        public MatchPlayer Player => m_player;
        public DarumaMatch Match => m_match;

        // ストーリーの試合シーンが読み込まれたら、自動で付ける
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!StorySession.IsRunning) return;
            var match = DarumaMatch.Current;
            if (match == null || match.gameObject.scene != scene) return;
            if (match.GetComponent<StoryMatchDirector>() == null) match.gameObject.AddComponent<StoryMatchDirector>();
        }

        // ドメインの再読み込みをしない設定でも、前の再生のフラグが残らないようにする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            PauseMenu.SuppressForStory = false;
            SpecialInput.Suspended = false;
            SpecialGaugeHUD.Suppressed = false;
            CpuSpecial.Disabled = false;
            DarumaMatch.AutoReturnToSelect = true;
        }

        void Start()
        {
            m_match = DarumaMatch.Current;
            m_chapter = StorySession.Chapter;
            if (m_match == null || m_chapter == null) { enabled = false; return; }

            // 章の設定で CPU の必殺技を止められる（チュートリアルでは止める）
            if (m_chapter.rules != null && !m_chapter.rules.cpuSpecial) CpuSpecial.Disabled = true;
            SpecialGaugeHUD.Suppressed = false;

            m_dialogue = DialogueRunner.Create();
            m_movie = StoryMovie.Create(m_chapter);
            m_match.Finished += OnFinished;
            m_match.PlayerKO += OnPlayerKO;
            StartCoroutine(RunStory());
        }

        void OnDestroy()
        {
            if (m_match != null)
            {
                m_match.Finished -= OnFinished;
                m_match.PlayerKO -= OnPlayerKO;
            }
            if (m_movie != null) Destroy(m_movie.gameObject);

            // ポーズメニューからタイトルへ戻った等：ストーリーを中断し、次の試合に影響を残さない
            if (!m_leavingForRetry && StorySession.IsRunning)
            {
                StorySession.End();
                DarumaMatch.AutoReturnToSelect = true;
            }

            PauseMenu.SuppressForStory = false;
            SpecialInput.Suspended = false;
            SpecialGaugeHUD.Suppressed = false;
            CpuSpecial.Disabled = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        void Update()
        {
            if (m_player == null || m_player.Character == null || m_finishedHandled) return;
            if (m_match != null && m_match.IsFinished) return;
            // 会話中（止めている間）は出さない。閉じたあとに改めて判定する
            if (m_dialogue != null && (m_dialogue.IsShowing || (m_match != null && m_match.IsPaused))) return;

            // きだるの黄色い積み木（初回だけ）：黄色が2個増えたら「あっ、増えた！」
            if (!StorySession.YellowExtraPlayed && m_chapter.yellowExtra != null && m_chapter.yellowExtra.Count > 0)
            {
                // 復活すると体が作り直されるので、数え直す
                if (m_player.Character != m_yellowCharacter)
                {
                    m_yellowCharacter = m_player.Character;
                    m_yellowCount = m_yellowCharacter.Stack.CountColor(TumikiColor.Yellow);
                }
                int yellow = m_player.Character.Stack.CountColor(TumikiColor.Yellow);
                if (yellow >= m_yellowCount + 2)
                {
                    StorySession.YellowExtraPlayed = true;
                    StartCoroutine(PlayLines(m_chapter.yellowExtra));
                }
                m_yellowCount = Mathf.Max(m_yellowCount, yellow);
            }
        }

        //========================================
        // 止める・動かす
        //========================================

        // ストーリーの会話用に試合を止める／動かす。
        // DarumaMatch.Pause はポーズ画面も開くので、会話中は PauseMenu に「出さない」と伝える。
        public void SetStoryPaused(bool paused)
        {
            if (m_match == null) return;
            if (paused)
            {
                if (m_match.IsPaused) return;
                if (!m_match.CanPause) return;
                PauseMenu.SuppressForStory = true;
                SpecialInput.Suspended = true;
                if (!m_match.Pause())
                {
                    PauseMenu.SuppressForStory = false;
                    SpecialInput.Suspended = false;
                }
            }
            else
            {
                if (m_match.IsPaused) m_match.Resume();
                PauseMenu.SuppressForStory = false;
                SpecialInput.Suspended = false;
            }
        }

        // 会話中はカウントダウンの数字を隠す（会話の後ろに残らないように）
        void SetCountdownHidden(bool hidden)
        {
            if (m_match == null) return;
            var view = m_match.GetComponent<StartCountdownView>();
            if (view != null) view.SetHidden(hidden);
        }

        //========================================
        // 進行
        //========================================

        IEnumerator RunStory()
        {
            // 試合の準備（プレイヤーが出るまで待つ）
            while (m_match != null && m_player == null)
            {
                foreach (var p in m_match.Players)
                {
                    if (p != null && !p.IsCpu) { m_player = p; break; }
                }
                yield return null;
            }

            // HUD・カウントダウン・ポーズ画面が作られるのを待つ
            yield return null;

            if (m_player != null && m_player.Character != null)
            {
                m_yellowCount = m_player.Character.Stack.CountColor(TumikiColor.Yellow);
            }
            HookTaunts();

            // 会話（前）：フェーダーが明けて、止められるようになってから
            if (!StorySession.IntroPlayed)
            {
                while (m_match == null || !m_match.CanPause) yield return null;
                SetCountdownHidden(true);
                SetStoryPaused(true);
                // 背景を部室のムービーにして、オープニング → 会話
                m_dialogue.SetDimAlpha(0.3f);
                yield return m_movie.PlayOpening();
                yield return m_dialogue.PlayBig(m_chapter, m_chapter.before);
                StorySession.MarkIntroPlayed();
                m_dialogue.SetDimAlpha(0.62f);
                yield return m_movie.FadeToBlack(0.4f);
                m_movie.Hide();
                SetCountdownHidden(false);
                SetStoryPaused(false);
            }

            // チュートリアル（到達したステップから）
            if (m_match == null) yield break;
            m_tutorial = gameObject.AddComponent<TutorialController>();
            m_tutorial.Setup(m_chapter, m_dialogue, this, m_match);
            yield return m_tutorial.Run();
        }

        void HookTaunts()
        {
            if (m_match == null || m_chapter.cpuTaunts == null) return;
            foreach (var p in m_match.Players)
            {
                if (p == null || !p.IsCpu || p.Character == null) continue;
                var character = p.Character;
                character.DealtDamage += attacker => OnCpuAttack(character, attacker);
            }
        }

        // CPU が攻撃したときの掛け合い（同じ台詞は一度だけ・数秒で自動的に閉じる）
        void OnCpuAttack(DarumaCharacter cpu, DarumaCharacter attacker)
        {
            if (m_finishedHandled || attacker != cpu) return;
            if (m_match == null || m_player == null || m_player.Character == null) return;
            if (attacker == m_player.Character) return;

            // どの CPU かは、キャラクターの持ち主から話者IDを決める
            string speaker = null;
            foreach (var p in m_match.Players)
            {
                if (p != null && p.Character == cpu && p.CharacterData != null) { speaker = p.CharacterData.id; break; }
            }
            if (string.IsNullOrEmpty(speaker) || m_taunted.Contains(speaker)) return;

            foreach (var line in m_chapter.cpuTaunts)
            {
                if (line == null || line.speaker != speaker) continue;
                m_taunted.Add(speaker);
                m_dialogue.StartCoroutine(m_dialogue.PlaySmall(line.speaker, line.text, m_chapter));
                break;
            }
        }

        IEnumerator PlayLines(List<StoryLine> lines)
        {
            foreach (var line in lines)
            {
                if (line == null) continue;
                yield return m_dialogue.PlaySmall(line.speaker, line.text, m_chapter);
            }
        }

        //========================================
        // やられたとき
        //========================================

        // ストックが残っているときは、赤い閃光と「やられた！」を出して少し止める
        void OnPlayerKO(MatchPlayer victim, MatchPlayer attacker)
        {
            if (m_finishedHandled || victim == null || victim != m_player) return;
            if (m_player.Stocks <= 0) return;   // 最後のストックは敗北の演出で扱う
            if (m_koRunning) return;
            StartCoroutine(KoRoutine());
        }

        IEnumerator KoRoutine()
        {
            m_koRunning = true;
            SetStoryPaused(true);
            yield return m_dialogue.PlayKoFlash("やられた！", $"のこり {Mathf.Max(0, m_player.Stocks)} ストック");
            SetStoryPaused(false);
            m_koRunning = false;
        }

        //========================================
        // 勝敗
        //========================================

        void OnFinished(DarumaMatch.MatchResult result)
        {
            if (m_finishedHandled) return;
            m_finishedHandled = true;
            bool won = result != null && result.Winner != null && !result.Winner.IsCpu;

            // 念のため：会話用に止めていたら解く
            if (m_match != null && m_match.IsPaused)
            {
                PauseMenu.SuppressForStory = false;
                SpecialInput.Suspended = false;
                m_match.Resume();
            }
            Time.timeScale = 1f;   // 試合終了で止まっている時間を戻す
            StartCoroutine(won ? Victory() : Defeat());
        }

        IEnumerator Victory()
        {
            // 実績カード（解除が無ければ何も出ない）
            var canvas = ToyKit.CreateOverlayCanvas("StoryAchievements", 45);
            var presenter = AchievementResultPresenter.Create(canvas, PresentationSettings.DefaultFont);
            yield return presenter.Play();

            // 会話（後）：部室の夕方のムービーを背景に
            m_dialogue.SetDimAlpha(0.3f);
            m_movie.SetBlack();
            m_movie.ShowConversation(true);
            yield return m_movie.FadeIn(0.6f);
            yield return m_dialogue.PlayBig(m_chapter, m_chapter.after);

            // だるま新聞 → 章クリア保存
            yield return m_dialogue.PlayNewspaper(m_chapter.newspaper);
            m_movie.Hide();
            StoryProgress.MarkCleared(m_chapter.id);

            StorySession.End();
            DarumaMatch.AutoReturnToSelect = true;
            m_leavingForRetry = true;
            Fader.Load(MenuSceneName);
        }

        IEnumerator Defeat()
        {
            // 最後のストックを落としたときも、ひと目で分かるように赤い閃光を出す
            yield return m_dialogue.PlayKoFlash("やられた…", "");

            // 1行だけ台詞を出して、同じシーンを読み直す
            yield return m_dialogue.PlayBig(m_chapter, m_chapter.defeat);
            StorySession.CountRetry();

            DarumaMatch.AutoReturnToSelect = false;
            m_leavingForRetry = true;
            Nakagawa.StageSwitcher.RequestStage(m_chapter.stageId);
            Fader.Load(SceneManager.GetActiveScene().name);
        }
    }
}
