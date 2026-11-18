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
    //  ・勝利：リザルト → 実績カード → 会話（後）→ だるま新聞 → 章クリア保存 → メニューへ
    //  ・敗北：1行の台詞 → 同じシーンを読み直して再開（会話（前）は出さない・チュートリアルは続きから）
    //  ・序章：チュートリアルのあと、ストック1か一定時間で必殺技の覚醒イベントを1回だけ出す
    // 試合のルール自体は変えない（上に重ねるレイヤー）。実績の解除判定はふつうに動く。
    [DefaultExecutionOrder(-50)]
    public class StoryMatchDirector : MonoBehaviour
    {
        const string MenuSceneName = "MenuScene";

        [Header("序章ゲージチュートリアル")]
        [Tooltip("手順1（積んだ説明）のあと、時間経過の説明を出すまでの秒数（仮の初期値は15秒）")]
        [SerializeField, Min(1f)] float m_gaugeTimeHintSeconds = 15f;

        DarumaMatch m_match;
        DialogueRunner m_dialogue;
        StoryMovie m_movie;
        TutorialController m_tutorial;
        MatchPlayer m_player;
        StoryChapter m_chapter;

        bool m_finishedHandled;
        bool m_leavingForRetry;
        bool m_koRunning;
        bool m_matchRunning;
        bool m_awakeningRunning;
        bool m_lastStandRunning;
        bool m_lastStandHandled;
        bool m_stageMode;
        bool m_venueMode;
        bool m_finalMode;
        float m_matchStartTime;
        float m_nextIncomingCheck;
        bool m_incomingBlock;
        // いま出している小窓の台詞の世代。新しいイベントが来たら、前の台詞の続きは打ち切る
        int m_smallGen;
        // プレイヤーの弾が相手に当たってから、連鎖が起きるまでを受け付ける時間
        float m_chainOpponentHitUntil = -1f;

        // 序章の必殺技ゲージチュートリアル（手順0〜4）
        bool m_gaugeHooked;
        bool m_gaugeStackHintDone;
        bool m_gaugeHitHintDone;
        bool m_gaugeTimeHintDone;
        bool m_gaugeFullTriggered;
        float m_gaugeStackHintAt = -1f;

        bool IsPrologueGaugeTutorial => m_chapter != null && m_chapter.rules != null && m_chapter.rules.awakening;
        float GaugeTimeHintSeconds
        {
            get
            {
                var s = StorySettings.Instance;
                return s != null ? s.GaugeTimeHintSeconds : Mathf.Max(1f, m_gaugeTimeHintSeconds);
            }
        }

        // チュートリアルから使う
        public MatchPlayer Player => m_player;
        public DarumaMatch Match => m_match;

        // 覚醒イベントの保険の秒数（StorySettings が無ければ90秒）
        static float AwakeningSeconds => StorySettings.Instance != null ? StorySettings.Instance.AwakeningSeconds : 90f;

        // チュートリアルCPUの積み木の上限（Inspector の StorySettings が優先。0 なら章データ）
        int CpuBlockLimit
        {
            get
            {
                var settings = StorySettings.Instance;
                if (settings != null && settings.OverrideCpuBlockLimit > 0) return settings.OverrideCpuBlockLimit;
                if (settings != null && settings.TutorialCpuBlockLimit > 0) return settings.TutorialCpuBlockLimit;
                return m_chapter != null && m_chapter.rules != null ? m_chapter.rules.cpuBlockLimit : 0;
            }
        }

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
            PauseMenu.StoryMode = false;
            SpecialInput.Suspended = false;
            SpecialInput.StoryLocked = false;
            SpecialGaugeHUD.Suppressed = false;
            SpecialGaugeHUD.Visible = true;
            CpuSpecial.Disabled = false;
            DarumaMatch.AutoReturnToSelect = true;
            AchievementManager.StoryMode = false;
            MatchSetup.StoryNames = false;
        }

        void Start()
        {
            m_match = DarumaMatch.Current;
            m_chapter = StorySession.Chapter;
            if (m_match == null || m_chapter == null) { enabled = false; return; }
            m_venueMode = m_chapter.cutscene != null && m_chapter.cutscene.type == "venue";
            m_stageMode = m_chapter.cutscene != null && m_chapter.cutscene.type == "stage";
            m_finalMode = m_chapter.cutscene != null && m_chapter.cutscene.type == "final";
            m_chainOpponentHitUntil = -1f;

            PauseMenu.StoryMode = true;
            AchievementManager.StoryMode = true;
            MatchSetup.StoryNames = true;

            // 章の設定で CPU の必殺技を止められる（チュートリアルでは止める）
            if (m_chapter.rules != null && !m_chapter.rules.cpuSpecial) CpuSpecial.Disabled = true;
            SpecialGaugeHUD.Suppressed = false;
            // ゲージを見せる章は最初から見せる（第1章は見せ場まで隠す）
            bool gaugeFromStart = m_chapter.rules == null || m_chapter.rules.gaugeFromStart;
            SpecialGaugeHUD.Visible = gaugeFromStart || StorySession.AwakeningDone;
            // 序章では操作チュートリアル中はゲージを溜めず、練習試合開始後に溜める。満タンになるまで撃てない
            SpecialGauge.StoryGainScale = IsPrologueGaugeTutorial ? 0.0f : -1.0f;
            SpecialInput.StoryLocked = (IsPrologueGaugeTutorial || !gaugeFromStart) && !StorySession.AwakeningDone;

            m_dialogue = DialogueRunner.Create();
            m_dialogue.StoryLineStarted += OnStoryLineStarted;
            m_movie = StoryMovie.Create(m_chapter);
            // シーンが明けた瞬間にチュートリアルステージが見えないよう、黒いムービー画面で覆っておく
            m_movie.SetBlack();
            m_movie.Show();
            SpecialGaugeHUD.Suppressed = true;
            m_match.Finished += OnFinished;
            m_match.PlayerKO += OnPlayerKO;
            m_match.PlayerRespawned += OnPlayerRespawned;
            m_match.PlayerOut += OnPlayerOut;
            HookYellowExtra();
            HookReflectEvents();
            HookChainEvents();
            StartCoroutine(RunStory());
        }

        void OnDestroy()
        {
            if (m_match != null)
            {
                m_match.Finished -= OnFinished;
                m_match.PlayerKO -= OnPlayerKO;
                m_match.PlayerRespawned -= OnPlayerRespawned;
                m_match.PlayerOut -= OnPlayerOut;
            }
            KidaruPassive.ExtraAdded -= OnYellowExtra;
            DarumaHammer.Hit -= OnHammerHit;
            DarumaHammer.ShotFired -= OnCpuShot;
            IwaBlock.WallBroken -= OnIwaWallBroken;
            TumikiBlock.Hit -= OnBlockHitForChain;
            AchievementEvents.Chain -= OnChainForStory;
            UnhookPrologueGauge();
            SpecialGauge.StoryGainScale = -1.0f;
            if (m_dialogue != null) m_dialogue.StoryLineStarted -= OnStoryLineStarted;
            if (m_movie != null) Destroy(m_movie.gameObject);

            // ポーズメニューからタイトルへ戻った等：ストーリーを中断し、次の試合に影響を残さない
            if (!m_leavingForRetry && StorySession.IsRunning)
            {
                StorySession.End();
                DarumaMatch.AutoReturnToSelect = true;
            }

            PauseMenu.SuppressForStory = false;
            PauseMenu.StoryMode = false;
            SpecialInput.Suspended = false;
            SpecialInput.StoryLocked = false;
            SpecialGaugeHUD.Suppressed = false;
            SpecialGaugeHUD.Visible = true;
            CpuSpecial.Disabled = false;
            AchievementManager.StoryMode = false;
            // リトライ中は、次のシーンの Awake より前に名前表示を消さない
            // （消すと HUD が「1P」「CPU」で作られてしまう）
            if (!StorySession.IsRunning) MatchSetup.StoryNames = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        void Update()
        {
            if (m_player == null || m_player.Character == null || m_finishedHandled) return;
            if (m_match != null && m_match.IsFinished) return;
            // 会話中（止めている間）は出さない。閉じたあとに改めて判定する
            if (m_dialogue != null && (m_dialogue.IsShowing || (m_match != null && m_match.IsPaused))) return;

            if (IsPrologueGaugeTutorial)
                UpdatePrologueGaugeTimeHint();
            else
                TryStartAwakening();
            TryStartSpecialEvent();
        }

        //========================================
        // 必殺技の覚醒イベント
        //========================================

        void TryStartAwakening()
        {
            if (!m_matchRunning || m_awakeningRunning) return;
            if (m_chapter == null || m_chapter.rules == null || !m_chapter.rules.awakening) return;
            if (StorySession.AwakeningDone || m_koRunning) return;

            bool lowStock = m_player.Stocks == 1;
            bool timeout = Time.unscaledTime - m_matchStartTime >= AwakeningSeconds;
            if (!lowStock && !timeout) return;
            StartCoroutine(Awakening());
        }

        IEnumerator Awakening()
        {
            m_awakeningRunning = true;
            SetStoryPaused(true);

            var gauge = m_player != null && m_player.Character != null
                ? m_player.Character.GetComponent<SpecialGauge>() : null;
            if (gauge != null) gauge.Fill();
            // ゲージを見せて、撃てる状態にする
            SpecialGaugeHUD.Visible = true;
            SpecialInput.StoryLocked = false;

            // 短い間を置いてから掛け合い（試合は止まっている）
            yield return new WaitForSecondsRealtime(0.45f);
            if (m_chapter.awakening != null)
            {
                foreach (var line in m_chapter.awakening)
                {
                    if (line == null) continue;
                    yield return m_dialogue.PlaySmall(line.speaker, line.text, m_chapter, 2.6f);
                }
            }

            // ここからは動かして、必殺技を実際に1回使ってもらう
            SetStoryPaused(false);
            m_dialogue.ShowObjective(m_chapter, "kidaru", "[Y]で必殺技！");

            bool used = false;
            System.Action<SpecialGauge> onConsumed = g =>
            {
                if (g == gauge) used = true;
            };
            SpecialGauge.Consumed += onConsumed;
            try
            {
                while (!used && !m_finishedHandled)
                {
                    if (m_player == null || m_player.Character == null) break;
                    yield return null;
                }
            }
            finally
            {
                SpecialGauge.Consumed -= onConsumed;
            }

            m_dialogue.HideObjective();
            if (used)
            {
                StorySession.AwakeningDone = true;
                Debug.Log("[Story] 必殺技の覚醒イベント完了");
            }
            m_awakeningRunning = false;
        }

        //========================================
        // 「追い詰められたら撃つ」必殺技イベント（第2章以降）
        //========================================

        void TryStartSpecialEvent()
        {
            var ev = m_chapter != null ? m_chapter.specialEvent : null;
            if (ev == null || !ev.enabled || m_finishedHandled || m_lastStandRunning || m_lastStandHandled) return;
            if (!m_matchRunning || m_koRunning) return;
            if (m_player == null || m_player.Character == null) return;

            var settings = StorySettings.Instance;
            int threshold = Mathf.Max(1, ev.stockThreshold);
            bool timeTrigger = ev.trigger == "time";
            bool lowStock = !timeTrigger && m_player.Stocks <= threshold;
            bool fallback = ev.timeFallback || (settings != null && settings.SpecialFallbackEnabled);
            float fallbackSeconds = ev.timeSeconds > 0f
                ? ev.timeSeconds
                : settings != null ? settings.SpecialFallbackSeconds : 90f;
            bool timeout = (timeTrigger || fallback)
                && Time.unscaledTime - m_matchStartTime >= Mathf.Max(1f, fallbackSeconds);
            if (!lowStock && !timeout) return;

            StartCoroutine(SpecialEvent(ev));
        }

        IEnumerator SpecialEvent(StorySpecialEvent ev)
        {
            m_lastStandRunning = true;
            m_lastStandHandled = true;
            bool replay = StorySession.LastStandPlayed;
            var settings = StorySettings.Instance;

            if (replay && !(ev.replayAssist && (settings == null || settings.SpecialReplayAssist)))
            {
                m_lastStandRunning = false;
                yield break;
            }

            // あかだるの必殺技は積み木1個以上が必要。ストック1直後は0個のことが多いので補助する
            EnsurePlayerHasBlock("必殺技イベントの補助");

            var gauge = m_player != null && m_player.Character != null
                ? m_player.Character.GetComponent<SpecialGauge>() : null;
            if (gauge != null) gauge.Fill();
            SpecialGaugeHUD.Visible = true;
            SpecialInput.StoryLocked = false;

            // 初回だけ、試合を止めて掛け合い（約3秒ずつ）。リトライは台詞なしの補助だけ
            if (!replay)
            {
                if (ev.pause) SetStoryPaused(true);
                yield return new WaitForSecondsRealtime(0.35f);
                if (ev.lines != null)
                {
                    foreach (var line in ev.lines)
                    {
                        if (line == null) continue;
                        yield return m_dialogue.PlaySmall(line.speaker, line.text, m_chapter, 3.0f);
                    }
                }
                if (ev.pause) SetStoryPaused(false);
            }

            float promptSeconds = ev.promptSeconds > 0f
                ? ev.promptSeconds
                : settings != null ? settings.SpecialPromptSeconds : 10f;
            string prompt = string.IsNullOrEmpty(ev.prompt) ? "[L／Y]で必殺技！" : ev.prompt;
            m_dialogue.ShowObjective(m_chapter, "", prompt);

            bool used = false;
            System.Action<SpecialGauge> onConsumed = g =>
            {
                if (g == gauge) used = true;
            };
            SpecialGauge.Consumed += onConsumed;
            try
            {
                float until = Time.unscaledTime + Mathf.Max(1f, promptSeconds);
                while (!used && !m_finishedHandled && Time.unscaledTime < until)
                {
                    yield return null;
                }
            }
            finally
            {
                SpecialGauge.Consumed -= onConsumed;
            }

            m_dialogue.HideObjective();
            if (!replay) StorySession.LastStandPlayed = true;
            Debug.Log(replay
                ? "[Story] 必殺技イベント（リトライ補助）: ゲージ満タン＋プロンプト"
                : "[Story] 必殺技イベント: ストック1で掛け合い→プロンプト" + (used ? "（使用）" : "（時間切れ）"));
            m_lastStandRunning = false;
        }

        // 必殺技・連鎖の補助用。積み木が0個なら1個渡す
        void EnsurePlayerHasBlock(string reason)
        {
            if (m_player == null || m_player.Character == null || m_player.Character.Stack == null) return;
            if (m_player.Character.Stack.Count > 0) return;

            Vector3 pos = m_player.Character.transform.position;
            TumikiBlock block = FindLooseBlock(pos);
            if (block == null) block = CreateAssistBlock(pos + m_player.Character.transform.forward * 1.2f);
            if (block == null) return;
            m_player.Character.Stack.AddBottom(block);
            Debug.Log("[Story] " + reason + "：積み木を1個渡しました");
        }

        // 「あっ、増えた！」：黄色の追加分が積まれたとき（チュートリアル後・初回だけ）
        void HookYellowExtra()
        {
            if (m_chapter == null || m_chapter.yellowExtra == null || m_chapter.yellowExtra.Count == 0) return;
            KidaruPassive.ExtraAdded += OnYellowExtra;
        }

        void OnYellowExtra(DarumaCharacter character)
        {
            if (m_finishedHandled || StorySession.YellowExtraPlayed) return;
            if (!m_matchRunning || m_awakeningRunning || m_dialogue == null || m_dialogue.IsShowing) return;
            if (m_player == null || character != m_player.Character) return;
            if (m_match != null && m_match.IsPaused) return;

            StorySession.YellowExtraPlayed = true;
            StartCoroutine(PlayLines(m_chapter.yellowExtra));
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

        // ストーリーのカットシーン用。無敵の点滅などで体のRendererが切れていても、
        // カットシーンのあいだは全員の見た目を出しておく（ハンマーだけになるのを防ぐ）
        void EnsureStoryBodiesVisible()
        {
            if (m_match == null) return;
            foreach (var p in m_match.Players)
            {
                if (p == null || p.Character == null) continue;
                if (!p.Character.gameObject.activeSelf) p.Character.gameObject.SetActive(true);
                // 体の見た目（Visual / Head）が非アクティブになっていても戻す
                var visual = p.Character.transform.Find("Visual");
                if (visual != null && !visual.gameObject.activeSelf) visual.gameObject.SetActive(true);
                var stack = p.Character.transform.Find("Visual/Stack");
                if (stack != null && !stack.gameObject.activeSelf) stack.gameObject.SetActive(true);
                var head = p.Character.Stack != null ? p.Character.Stack.Head : null;
                if (head == null) head = p.Character.transform.Find("Visual/Stack/Head");
                if (head != null && !head.gameObject.activeSelf) head.gameObject.SetActive(true);
                var renderers = p.Character.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    if (r != null) r.enabled = true;
                }
                Debug.Log($"[Story] カットシーン表示: {p.Label} Head={(head != null)} Renderers={renderers.Length}");
            }
        }

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

            HookTaunts();
            ApplyCpuBlockLimit(CpuBlockLimit);

            // 会話（前）：フェーダーが明けて、止められるようになってから
            if (!StorySession.IntroPlayed)
            {
                while (m_match == null || !m_match.CanPause) yield return null;
                SetCountdownHidden(true);
                SetStoryPaused(true);
                // 背景をムービーにして、カットシーン → 会話
                m_dialogue.SetDimAlpha(0.3f);
                // ムービー中は OnGUI のゲージが映像の上に出てしまうので止める
                SpecialGaugeHUD.Suppressed = true;
                // カットシーンで体が消えないよう、出ている全員の見た目を有効にしておく
                EnsureStoryBodiesVisible();
                if (m_stageMode) yield return m_movie.PlayStageIntro(m_chapter);
                else if (m_venueMode) yield return m_movie.PlayVenueIntro(m_chapter);
                else if (m_finalMode) yield return m_movie.PlayFinalIntro(m_chapter);
                else yield return m_movie.PlayOpening();
                // 章クリア済みの再プレイで B を押したら、カットシーンだけでなく会話（前）も飛ばす
                bool skipBefore = StoryProgress.IsCleared(m_chapter.id) && m_movie.WasSkipped;
                if (!skipBefore)
                {
                    if (m_finalMode) yield return PlayFinalBefore();
                    else yield return m_dialogue.PlayBig(m_chapter, m_chapter.before);
                    m_movie.EndConversation(false);
                }
                StorySession.MarkIntroPlayed();
                m_dialogue.SetDimAlpha(0.62f);
                yield return m_movie.FadeToBlack(0.4f);
                // 暗転のあと、すぐにステージとカウントダウンを見せず、少し間を置く
                yield return new WaitForSecondsRealtime(0.45f);
                // ここで映像画面を消すと、黒幕も一緒に消えてステージが一瞬映る。
                // 黒い画面だけ残し、最後にフェードでステージを見せる
                m_movie.HideViewKeepBlack();
                // GO! の瞬間に会話の操作ヒントが残らないようにする
                m_dialogue.HideHint();
                m_movie.HideHint();
                yield return new WaitForSecondsRealtime(1.0f);
                yield return m_movie.FadeOutBlack(0.35f);
                SpecialGaugeHUD.Suppressed = false;
                SetCountdownHidden(false);
                SetStoryPaused(false);
            }
            else
            {
                // リトライ：オープニングは出さない。黒いムービー画面だけ片づける
                m_movie.Hide();
                SpecialGaugeHUD.Suppressed = false;
            }

            // チュートリアル（到達したステップから）
            if (m_match == null) yield break;
            m_tutorial = gameObject.AddComponent<TutorialController>();
            m_tutorial.Setup(m_chapter, m_dialogue, this, m_match);
            yield return m_tutorial.Run();

            // ここから試合開始。覚醒イベントの保険の秒数はここから数える
            m_matchRunning = true;
            m_matchStartTime = Time.unscaledTime;

            if (IsPrologueGaugeTutorial)
            {
                SpecialGauge.StoryGainScale = 1.0f;
                if (!StorySession.AwakeningDone)
                    StartCoroutine(StartPrologueGaugeTutorial());
            }
        }

        //========================================
        // 序章：必殺技ゲージのチュートリアル（手順0〜4）
        //========================================

        void HookPrologueGauge()
        {
            if (m_gaugeHooked) return;
            m_gaugeHooked = true;
            SpecialGauge.Gained += OnPrologueGaugeGained;
        }

        void UnhookPrologueGauge()
        {
            if (!m_gaugeHooked) return;
            m_gaugeHooked = false;
            SpecialGauge.Gained -= OnPrologueGaugeGained;
        }

        IEnumerator StartPrologueGaugeTutorial()
        {
            HookPrologueGauge();

            // 手順0：「練習試合、開始！」の小窓が消えたあと、あかだるの一言を出す
            yield return new WaitForSecondsRealtime(2.2f);
            float waitUntil = Time.unscaledTime + 2.0f;
            while (m_dialogue != null && m_dialogue.IsShowing && Time.unscaledTime < waitUntil) yield return null;

            if (m_finishedHandled || m_gaugeFullTriggered || m_awakeningRunning) yield break;
            if (!m_gaugeStackHintDone && !m_gaugeHitHintDone && m_dialogue != null)
            {
                yield return m_dialogue.PlaySmall("akadaru", "新入り相手に必殺技は使わん！", m_chapter, 2.8f);
            }
        }

        void OnPrologueGaugeGained(SpecialGauge gauge, float added, SpecialGauge.GainReason reason)
        {
            if (m_finishedHandled || !m_matchRunning || StorySession.AwakeningDone) return;
            if (gauge == null || m_player == null || gauge.Character != m_player.Character) return;

            // 手順4（満タン）は、手順1〜3の途中でも最優先で実行する
            if (gauge.IsFull && !m_gaugeFullTriggered)
            {
                m_gaugeFullTriggered = true;
                StartCoroutine(PrologueFullSpecialPrompt(gauge));
                return;
            }

            if (m_gaugeFullTriggered || m_awakeningRunning) return;

            // 手順1：積み木を積んだとき
            if (!m_gaugeStackHintDone && reason == SpecialGauge.GainReason.StackBlock)
            {
                m_gaugeStackHintDone = true;
                m_gaugeStackHintAt = Time.unscaledTime;
                if (m_dialogue != null)
                {
                    m_dialogue.StartCoroutine(m_dialogue.PlaySmall("midaru", "積むと10pt入ります。100ptで必殺技です。", m_chapter, 3.2f));
                }
                return;
            }

            // 手順2：ハンマーで直接殴ってダメージを与えたとき
            if (!m_gaugeHitHintDone && reason == SpecialGauge.GainReason.DirectHit)
            {
                m_gaugeHitHintDone = true;
                if (m_dialogue != null)
                {
                    m_dialogue.StartCoroutine(m_dialogue.PlaySmall("midaru", "ハンマーで直接殴ると15pt。……データ通りです。", m_chapter, 3.2f));
                }
            }
        }

        // 手順3：手順1のあと一定時間（既定15秒）が経ったら時間経過の説明を出す
        void UpdatePrologueGaugeTimeHint()
        {
            if (!m_matchRunning || StorySession.AwakeningDone) return;
            if (m_gaugeTimeHintDone || m_gaugeFullTriggered || m_awakeningRunning) return;
            if (!m_gaugeStackHintDone || m_gaugeStackHintAt < 0f) return;
            if (Time.unscaledTime - m_gaugeStackHintAt < GaugeTimeHintSeconds) return;

            m_gaugeTimeHintDone = true;
            if (m_dialogue != null)
            {
                m_dialogue.StartCoroutine(m_dialogue.PlaySmall("midaru", "近くに敵がいる間や、動いている間も少しずつ溜まります。戦うこと自体が、溜める行為です。", m_chapter, 3.8f));
            }
        }

        // 手順4：満タンになった瞬間にゲームを止め、[Y]で必殺技を発動させる（そのまま黄金化演出へ）
        IEnumerator PrologueFullSpecialPrompt(SpecialGauge gauge)
        {
            m_awakeningRunning = true;
            UnhookPrologueGauge();

            SetStoryPaused(true);
            if (m_dialogue != null)
            {
                m_dialogue.CloseSmallWindow();
                m_dialogue.ShowObjective(m_chapter, "kidaru", "[Y]で必殺技！");
            }
            SpecialInput.StoryLocked = false;

            // 同じフレームの押下で暴発しないよう 1 フレーム待つ
            yield return null;

            while (!m_finishedHandled)
            {
                if (m_player == null || m_player.Character == null) break;
                if (SpecialInput.WasPressedIgnoringPause(m_player.Character))
                {
                    SetStoryPaused(false);
                    var special = m_player.Character.GetComponent<KidaruSpecial>();
                    if (special != null && special.TryActivate())
                    {
                        StorySession.AwakeningDone = true;
                        Debug.Log("[Story] 序章ゲージチュートリアル完了（黄金化発動）");
                    }
                    break;
                }
                yield return null;
            }

            if (m_dialogue != null)
            {
                m_dialogue.HideObjective();
            }
            if (!m_finishedHandled && m_match != null && m_match.IsPaused) SetStoryPaused(false);
            m_awakeningRunning = false;
        }

        // 章のチュートリアル用に CPU の積み木の上限を下げる（0 なら何もしない）
        public void ApplyCpuBlockLimit(int limit)
        {
            if (limit <= 0 || m_match == null) return;
            foreach (var p in m_match.Players)
            {
                if (p == null || !p.IsCpu || p.Character == null) continue;
                p.Character.Stack.SetMaxCount(limit);
            }
        }

        // 復活で体が作り直されたら、CPUの上限をかけ直す
        void OnPlayerRespawned(MatchPlayer p)
        {
            if (p == null || !p.IsCpu) return;
            ApplyCpuBlockLimit(CpuBlockLimit);
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
            // 第2章のへびだるのように「攻撃したとき」で出したい章は、ショットでも拾う
            if (m_chapter.cpuTauntOnShot) DarumaHammer.ShotFired += OnCpuShot;
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
            PlayCpuTaunt(speaker);
        }

        // CPU が股抜きショットを撃った（初回だけ台詞）
        void OnCpuShot(HammerHitInfo info)
        {
            if (m_finishedHandled || m_match == null) return;
            if (!(info.Owner is DarumaCharacter character)) return;
            var player = m_match.PlayerOf(character);
            if (player == null || !player.IsCpu || player.CharacterData == null) return;
            PlayCpuTaunt(player.CharacterData.id);
        }

        void PlayCpuTaunt(string speaker)
        {
            if (m_chapter == null || m_dialogue == null || string.IsNullOrEmpty(speaker)) return;
            // 同じ掛け合いは一度だけ（リトライでも繰り返さない）
            string eventKey = m_chapter.id + ".taunt." + speaker;
            if (StorySession.HasEvent(eventKey)) return;

            foreach (var line in m_chapter.cpuTaunts)
            {
                if (line == null || line.speaker != speaker) continue;
                StorySession.MarkEvent(eventKey);
                StartCoroutine(PlayLines(new List<StoryLine> { line }));
                break;
            }
        }

        //========================================
        // 初回だけの掛け合い（第1章）
        //========================================

        void HookReflectEvents()
        {
            DarumaHammer.Hit += OnHammerHit;
            IwaBlock.WallBroken += OnIwaWallBroken;
        }

        // プレイヤーが飛んできた積み木を打ち返した（初回だけ台詞）
        void OnHammerHit(HammerHitInfo info)
        {
            if (m_finishedHandled || info.ReturnCount <= 0) return;
            if (m_player == null || m_player.Character == null) return;
            if (!ReferenceEquals(info.Owner, m_player.Character)) return;
            NotifyReflectSuccess();
        }

        public void NotifyReflectSuccess()
        {
            if (m_chapter == null || m_dialogue == null) return;
            string key = m_chapter.id + ".reflect";
            if (StorySession.HasEvent(key)) return;
            StorySession.MarkEvent(key);
            if (m_chapter.reflectFirst != null && m_chapter.reflectFirst.Count > 0)
                StartCoroutine(PlayLines(m_chapter.reflectFirst));
        }

        // いわだるの積み木が壁で砕けた（初回だけ台詞）
        void OnIwaWallBroken(IDarumaAttacker owner)
        {
            if (m_finishedHandled || m_chapter == null) return;
            var iwa = FindCpu("iwadaru");
            if (iwa == null || iwa.Character == null || !ReferenceEquals(owner, iwa.Character)) return;

            string key = m_chapter.id + ".wall";
            if (StorySession.HasEvent(key)) return;
            StorySession.MarkEvent(key);
            if (m_chapter.wallBreakFirst != null && m_chapter.wallBreakFirst.Count > 0)
                StartCoroutine(PlayLines(m_chapter.wallBreakFirst));
        }

        MatchPlayer FindCpu(string characterId)
        {
            if (m_match == null || string.IsNullOrEmpty(characterId)) return null;
            foreach (var p in m_match.Players)
            {
                if (p != null && p.IsCpu && p.CharacterData != null && p.CharacterData.id == characterId) return p;
            }
            return null;
        }

        //========================================
        // 連鎖（第2章）
        //========================================

        void HookChainEvents()
        {
            TumikiBlock.Hit += OnBlockHitForChain;
            AchievementEvents.Chain += OnChainForStory;
        }

        void OnStoryLineStarted(StoryLine line)
        {
            // 会話の1行ごとにショットIDの切り替えや話者へのカメラ寄せを行う
            if (m_movie != null) m_movie.OnDialogueLine(line);
        }

        // プレイヤーの弾が相手（CPU）に当たった。このあと抜けた積み木が連鎖すれば「相手の積み木を崩した」
        void OnBlockHitForChain(TumikiHitInfo info)
        {
            if (m_finishedHandled || !info.IsHarmful) return;
            if (m_player == null || m_player.Character == null) return;
            if (!ReferenceEquals(info.Block.Owner, m_player.Character)) return;
            if (!(info.Target is DarumaCharacter target) || target == m_player.Character) return;
            m_chainOpponentHitUntil = Time.unscaledTime + 1.2f;
        }

        void OnChainForStory(int length)
        {
            if (m_finishedHandled || m_chapter == null || m_player == null || m_player.Character == null) return;
            if (Time.unscaledTime > m_chainOpponentHitUntil) return;
            m_chainOpponentHitUntil = -1f;

            string key = m_chapter.id + ".chainFirst";
            if (StorySession.HasEvent(key)) return;
            StorySession.MarkEvent(key);
            if (m_chapter.chainFirst != null && m_chapter.chainFirst.Count > 0)
                StartCoroutine(PlayLines(m_chapter.chainFirst));
        }

        // 連鎖が起きないときのヒント（TutorialController から呼ぶ）
        public void NotifyChainHint()
        {
            if (m_chapter == null || m_dialogue == null) return;
            string key = m_chapter.id + ".chainHint";
            if (StorySession.HasEvent(key)) return;
            StorySession.MarkEvent(key);
            if (m_chapter.chainHint != null && m_chapter.chainHint.Count > 0)
                StartCoroutine(PlayLines(m_chapter.chainHint));
            else
                StartCoroutine(PlayLines(new List<StoryLine>
                {
                    new StoryLine { speaker = "midaru", text = "相手の積み木が積み上がっているところを狙おう" }
                }));
        }

        // 連鎖が起きにくいとき、相手の奥に積み木を2個出して「当てる→連鎖」を作りやすくする
        public void SpawnChainAssist()
        {
            if (m_finishedHandled || m_player == null || m_player.Character == null) return;
            MatchPlayer target = FindCpu("hebidaru");
            if (target == null || target.Character == null)
            {
                foreach (var p in m_match.Players)
                {
                    if (p != null && p.IsCpu && p.Character != null) { target = p; break; }
                }
            }
            if (target == null || target.Character == null) return;
            EnsureCpuHasBlock(target);
            EnsurePlayerHasBlock("連鎖の補助");

            Vector3 from = m_player.Character.transform.position;
            Vector3 to = target.Character.transform.position;
            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = m_player.Character.transform.forward;
            dir.Normalize();

            float distance = Vector3.Distance(from, to);
            float near = Mathf.Clamp(distance * 0.35f, 1.1f, 1.8f);
            float far = Mathf.Clamp(distance * 0.65f, 2.0f, 2.8f);
            if (distance < 3.0f) { near = Mathf.Max(0.8f, distance * 0.3f); far = Mathf.Max(1.4f, distance * 0.55f); }

            // 相手の奥（プレイヤーから見て向こう側）に、手前→奥の順で2個置く。
            // 相手に当てて抜けた積み木が、そのまま奥の2個へ連鎖する
            Vector3 pA = to + dir * near;
            Vector3 pB = to + dir * far;
            SpawnAssistBlock(pA);
            SpawnAssistBlock(pB);
            Debug.Log("[Story] 連鎖の補助：相手の奥に積み木を2個出しました", this);
        }

        // 相手に積み木が無いと「抜けた1個」が発生しないので、1個だけ足しておく
        void EnsureCpuHasBlock(MatchPlayer cpu)
        {
            if (cpu == null || cpu.Character == null || cpu.Character.Stack == null) return;
            if (cpu.Character.Stack.Count > 0) return;
            TumikiBlock block = CreateAssistBlock(cpu.Character.transform.position + Vector3.up * 0.5f);
            if (block == null) block = FindLooseBlock(cpu.Character.transform.position);
            if (block != null) cpu.Character.Stack.AddBottom(block);
        }

        void SpawnAssistBlock(Vector3 pos)
        {
            // 床の上に置く（穴の上なら相手の位置に寄せる）
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y + TumikiBlock.kHeight * 0.5f + 0.02f;
            TumikiBlock block = CreateAssistBlock(pos);
            if (block == null) block = FindLooseBlock(pos);
            if (block == null) return;
            block.transform.position = pos;
            block.SetLoose();
        }

        static TumikiBlock CreateAssistBlock(Vector3 pos)
        {
            var spawner = UnityEngine.Object.FindAnyObjectByType<TumikiSpawner>();
            if (spawner == null || spawner.Prefab == null) return null;
            var block = Instantiate(spawner.Prefab, pos, Quaternion.identity);
            block.SetColor(TumikiColor.Red);
            return block;
        }

        // プレイヤーへ向かっている攻撃判定のある積み木があるか（0.1秒ごとに数える）
        public bool HasIncomingBlockToPlayer()
        {
            if (Time.unscaledTime < m_nextIncomingCheck) return m_incomingBlock;
            m_nextIncomingCheck = Time.unscaledTime + 0.1f;
            m_incomingBlock = ScanIncomingBlock();
            return m_incomingBlock;
        }

        bool ScanIncomingBlock()
        {
            if (m_player == null || m_player.Character == null) return true;   // 判定できないときは補助しない
            Vector3 playerPos = m_player.Character.transform.position;
            var blocks = UnityEngine.Object.FindObjectsByType<TumikiBlock>();
            foreach (var block in blocks)
            {
                if (block == null || block.CurrentState != TumikiBlock.State.Flying) continue;
                if (!block.IsHarmful) continue;
                if (ReferenceEquals(block.Owner, m_player.Character)) continue;

                Vector3 to = playerPos - block.transform.position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance < 0.5f || distance > 25f) continue;
                if (Vector3.Dot(block.Direction, to / distance) > 0.35f) return true;
            }
            return false;
        }

        // いわだるの積み木（無ければ転がっている積み木）を、いわだるの前からプレイヤーへ飛ばす。
        // 積み木の生成・発射は既存の仕組み（DarumaStack.RemoveAt / TumikiBlock.Launch）をそのまま使う。
        public void FireReflectAssist()
        {
            if (m_player == null || m_player.Character == null) return;
            var iwa = FindCpu("iwadaru");
            if (iwa == null || iwa.Character == null)
            {
                FireNeutralAssist();
                return;
            }

            Vector3 from = iwa.Character.transform.position;
            Vector3 to = m_player.Character.transform.position;
            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = iwa.Character.transform.forward;
            dir.Normalize();

            TumikiBlock block = null;
            if (iwa.Character.Stack.Count > 0) block = iwa.Character.Stack.RemoveAt(0);
            else block = FindLooseBlock(from);
            if (block == null) return;

            Vector3 pos = from + dir * (DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.2f)
                + Vector3.up * (TumikiBlock.kHeight * 0.5f);
            block.transform.position = pos;
            block.transform.rotation = Quaternion.LookRotation(dir);
            block.Launch(dir, AssistSpeed, iwa.Character);
            Debug.Log("[Story] 打ち返しの補助：いわだるの積み木をプレイヤーへ飛ばしました", this);
        }

        // いわだるが落ちている（復活待ちなど）ときの保険。転がっている積み木を player の前方から飛ばす
        void FireNeutralAssist()
        {
            Vector3 playerPos = m_player.Character.transform.position;
            Vector3 forward = m_player.Character.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 from = playerPos + forward * 8f;
            var block = FindLooseBlock(from);
            if (block == null) return;
            Vector3 dir = playerPos - from;
            dir.y = 0f;
            dir.Normalize();
            block.transform.position = from + Vector3.up * (TumikiBlock.kHeight * 0.5f);
            block.transform.rotation = Quaternion.LookRotation(dir);
            block.Launch(dir, AssistSpeed, null);
            Debug.Log("[Story] 打ち返しの補助（いわだる不在）：積み木をプレイヤーへ飛ばしました", this);
        }

        float AssistSpeed
        {
            get
            {
                var settings = StorySettings.Instance;
                return settings != null ? settings.ReflectAssistSpeed : 18f;
            }
        }

        static TumikiBlock FindLooseBlock(Vector3 near)
        {
            var blocks = UnityEngine.Object.FindObjectsByType<TumikiBlock>();
            TumikiBlock best = null;
            float bestDistance = float.MaxValue;
            foreach (var block in blocks)
            {
                if (block == null || block.CurrentState != TumikiBlock.State.Loose) continue;
                float distance = (block.transform.position - near).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = block; }
            }
            return best;
        }

        // 出しかけの小窓の台詞を止める（試合終了・敗北のときに、台詞が残らないように）
        void StopEventLines()
        {
            m_smallGen++;
            if (m_dialogue != null) m_dialogue.CloseSmallWindow();
        }

        IEnumerator PlayLines(List<StoryLine> lines)
        {
            if (lines == null || lines.Count == 0) yield break;
            // 新しいイベント（脱落など）が来たら、前の台詞の続きは出さない
            int gen = ++m_smallGen;
            foreach (var line in lines)
            {
                if (line == null) continue;
                if (gen != m_smallGen) yield break;
                yield return m_dialogue.PlaySmall(line.speaker, line.text, m_chapter);
            }
        }

        //========================================
        // 最終章：控室 → 決勝の舞台の会話（前）
        //========================================

        // 章データの before を scene（room / stage）で分け、場面に合わせて背景を切り替える
        IEnumerator PlayFinalBefore()
        {
            var roomLines = new List<StoryLine>();
            var stageLines = new List<StoryLine>();
            if (m_chapter.before != null)
            {
                foreach (var line in m_chapter.before)
                {
                    if (line == null) continue;
                    if (line.scene == "stage") stageLines.Add(line);
                    else roomLines.Add(line);
                }
            }

            if (roomLines.Count > 0)
            {
                m_movie.ShowFinalConversation(false);
                yield return m_dialogue.PlayBig(m_chapter, roomLines);
            }
            if (stageLines.Count > 0)
            {
                if (roomLines.Count > 0) yield return m_movie.SwitchFinalScene("stage", 0.4f);
                else m_movie.ShowFinalConversation(true);
                yield return m_dialogue.PlayBig(m_chapter, stageLines);
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

        // プレイヤーが脱落した（4人戦ではCPU同士の試合が続いてしまうので、ここでストーリーの敗北にする）
        void OnPlayerOut(MatchPlayer player)
        {
            if (m_finishedHandled || player == null) return;
            if (player != m_player)
            {
                // CPU の脱落（最終章）。試合は続くので、台詞だけ出す
                OnCpuOut(player);
                return;
            }
            m_finishedHandled = true;
            // その場で試合を止めて、敗北の台詞 → リトライへ
            StopEventLines();
            UnhookPrologueGauge();
            if (m_dialogue != null)
            {
                m_dialogue.HideObjective();
            }
            SetStoryPaused(true);
            // 試合は止めるが、敗北の台詞の音は鳴らす
            AudioListener.pause = false;
            SpecialGaugeHUD.Suppressed = true;
            SpecialInput.StoryLocked = true;
            StartCoroutine(Defeat());
        }

        // CPU が脱落した（最終章）。そのキャラの台詞と、残り2人になったときの掛け合い
        void OnCpuOut(MatchPlayer player)
        {
            if (m_chapter == null || m_dialogue == null || player == null || player.CharacterData == null) return;
            string id = player.CharacterData.id;
            var lines = new List<StoryLine>();

            string key = m_chapter.id + ".out." + id;
            if (!StorySession.HasEvent(key) && m_chapter.outLines != null)
            {
                foreach (var entry in m_chapter.outLines)
                {
                    if (entry == null || entry.character != id) continue;
                    StorySession.MarkEvent(key);
                    if (entry.lines != null) lines.AddRange(entry.lines);
                    break;
                }
            }

            // 残りが「操作キャラ＋決勝の相手」の2人になったときだけ、2人きりの掛け合いを出す
            // （だるきんぐが先に落ちたときは残り2人でも出さない）
            string lastKey = m_chapter.id + ".lastTwo";
            bool hasLastTwo = m_chapter.lastTwoLines != null && m_chapter.lastTwoLines.Count > 0;
            if (hasLastTwo && !StorySession.HasEvent(lastKey) && IsLastTwoLeft())
            {
                StorySession.MarkEvent(lastKey);
                lines.AddRange(m_chapter.lastTwoLines);
            }

            if (lines.Count > 0)
            {
                Debug.Log("[Story] 脱落の掛け合い: " + id + "（" + lines.Count + "行）");
                StartCoroutine(PlayLines(lines));
            }
        }

        // 残っているのが自分と lastTwoCharacter だけか
        bool IsLastTwoLeft()
        {
            if (m_match == null || m_player == null || m_chapter == null) return false;
            if (m_player.State == MatchPlayer.PlayerState.Out) return false;

            MatchPlayer other = null;
            int alive = 0;
            foreach (var p in m_match.Players)
            {
                if (p == null || p.State == MatchPlayer.PlayerState.Out) continue;
                alive++;
                if (p != m_player) other = p;
            }
            if (alive != 2 || other == null || other.CharacterData == null) return false;

            string id = !string.IsNullOrEmpty(m_chapter.lastTwoCharacter)
                ? m_chapter.lastTwoCharacter
                : (m_chapter.cpu != null && m_chapter.cpu.Count > 0 ? m_chapter.cpu[0].character : null);
            return !string.IsNullOrEmpty(id) && other.CharacterData.id == id;
        }

        //========================================
        // 勝敗
        //========================================

        void OnFinished(DarumaMatch.MatchResult result)
        {
            if (m_finishedHandled) return;
            m_finishedHandled = true;
            // 出しかけの小窓の台詞は止める（リザルトに重ねない）
            StopEventLines();
            UnhookPrologueGauge();
            bool won = result != null && result.Winner != null && !result.Winner.IsCpu;

            // 念のため：会話用に止めていたら解く
            if (m_match != null && m_match.IsPaused)
            {
                PauseMenu.SuppressForStory = false;
                SpecialInput.Suspended = false;
                m_match.Resume();
            }
            Time.timeScale = 1f;   // 試合終了で止まっている時間を戻す
            // 試合が終わったらゲージは出さない（リザルト・新聞・黒画面の上に出ないように）
            SpecialGaugeHUD.Suppressed = true;
            SpecialInput.StoryLocked = true;
            if (m_dialogue != null) m_dialogue.HideObjective();
            StartCoroutine(won ? Victory() : Defeat());
        }

        IEnumerator Victory()
        {
            // リザルトの文字を少し見せてから消す（実績カードと重ならないように）
            var debugView = m_match != null ? m_match.GetComponent<MatchDebugView>() : null;
            if (debugView != null)
            {
                // 「しあいしゅうりょう！」を少し長めに見せてから実績へ
                yield return new WaitForSecondsRealtime(2.0f);
                debugView.SetResultVisible(false);
                yield return new WaitForSecondsRealtime(0.2f);
            }

            // 実績カード（解除が無ければ何も出ない）
            var canvas = ToyKit.CreateOverlayCanvas("StoryAchievements", 45);
            var presenter = AchievementResultPresenter.Create(canvas, PresentationSettings.DefaultFont);
            yield return presenter.Play();

            // 会話（後）：部室（第1章以降はステージ）の夕方のムービーを背景に
            m_dialogue.SetDimAlpha(0.3f);
            m_movie.SetBlack();
            if (m_finalMode) m_movie.ShowFinalConversation(true, true);
            else if (m_stageMode) m_movie.ShowStageConversation(true);
            else if (m_venueMode) m_movie.ShowVenueConversation(true);
            else m_movie.ShowConversation(true);
            yield return m_movie.FadeIn(0.6f);
            yield return m_dialogue.PlayBig(m_chapter, m_chapter.after, OnAfterLine);
            m_movie.EndConversation(true);
            // 横断幕が掲げられたのを少し見せる
            if (!string.IsNullOrEmpty(m_chapter.bannerText)) yield return new WaitForSecondsRealtime(1.4f);

            // だるま新聞
            yield return m_dialogue.PlayNewspaper(m_chapter.newspaper);

            // 新聞まで見終わったら、章クリアの実績とセーブを記録する。
            // このあとのエピローグを途中で終了しても、進行が失われないようにする
            AchievementManager.ReportStoryCleared(m_chapter.id);
            StoryProgress.MarkCleared(m_chapter.id);
            if (AchievementManager.PendingCount > 0)
            {
                var clearCanvas = ToyKit.CreateOverlayCanvas("StoryClearAchievements", 45);
                var clearPresenter = AchievementResultPresenter.Create(clearCanvas, PresentationSettings.DefaultFont);
                yield return clearPresenter.Play();
                Destroy(clearCanvas.gameObject);
            }

            // エピローグ（最終章。データがあるときだけ）
            if (m_chapter.epilogue != null && m_chapter.epilogue.enabled)
            {
                yield return m_movie.PlayEpilogue(m_chapter, m_dialogue);
            }

            // メニューへ切り替わるまで映像画面（黒）を残す（HUD付きの対戦画面を一瞬見せない）
            m_dialogue.HideHint();
            m_movie.HideHint();
            if (m_finalMode) m_movie.ShowBlackCover();
            else m_movie.SetBlack();

            StorySession.End();
            DarumaMatch.AutoReturnToSelect = true;
            m_leavingForRetry = true;
            Fader.Load(MenuSceneName);
        }

        // 会話（後）の最後のト書きに合わせて、横断幕を掲げる
        void OnAfterLine(int index, StoryLine line)
        {
            if (m_chapter == null || string.IsNullOrEmpty(m_chapter.bannerText)) return;
            if (m_chapter.after == null || m_chapter.after.Count == 0) return;
            if (index != m_chapter.after.Count - 1) return;
            m_movie.ShowBanner(m_chapter.bannerText, m_chapter.bannerSeconds);
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
            // 次のシーンの Awake 前に、名前表示（1P/CPU ではなく だるま名）を戻しておく
            MatchSetup.StoryNames = true;
            Nakagawa.StageSwitcher.RequestStage(m_chapter.stageId);
            Fader.Load(SceneManager.GetActiveScene().name);
        }
    }
}
