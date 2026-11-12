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
        bool m_stageMode;
        float m_matchStartTime;
        float m_nextIncomingCheck;
        bool m_incomingBlock;

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
            m_stageMode = m_chapter.cutscene != null && !string.IsNullOrEmpty(m_chapter.cutscene.type);

            PauseMenu.StoryMode = true;
            AchievementManager.StoryMode = true;
            MatchSetup.StoryNames = true;

            // 章の設定で CPU の必殺技を止められる（チュートリアルでは止める）
            if (m_chapter.rules != null && !m_chapter.rules.cpuSpecial) CpuSpecial.Disabled = true;
            SpecialGaugeHUD.Suppressed = false;
            // 序章は覚醒イベントまでゲージを見せない。第1章以降とリトライ後は最初から見せる
            bool gaugeFromStart = m_chapter.rules == null || m_chapter.rules.gaugeFromStart;
            SpecialGaugeHUD.Visible = gaugeFromStart || StorySession.AwakeningDone;
            // 覚醒前は必殺技を撃てない（「初めて出せた」の瞬間を守る）
            SpecialInput.StoryLocked = m_chapter.rules != null && m_chapter.rules.awakening && !StorySession.AwakeningDone;

            m_dialogue = DialogueRunner.Create();
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
            IwaBlock.WallBroken -= OnIwaWallBroken;
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
            MatchSetup.StoryNames = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        void Update()
        {
            if (m_player == null || m_player.Character == null || m_finishedHandled) return;
            if (m_match != null && m_match.IsFinished) return;
            // 会話中（止めている間）は出さない。閉じたあとに改めて判定する
            if (m_dialogue != null && (m_dialogue.IsShowing || (m_match != null && m_match.IsPaused))) return;

            TryStartAwakening();
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
                if (m_stageMode) yield return m_movie.PlayStageIntro(m_chapter);
                else yield return m_movie.PlayOpening();
                yield return m_dialogue.PlayBig(m_chapter, m_chapter.before);
                StorySession.MarkIntroPlayed();
                m_dialogue.SetDimAlpha(0.62f);
                yield return m_movie.FadeToBlack(0.4f);
                // 暗転のあと、すぐにステージとカウントダウンを見せず、少し間を置く
                yield return new WaitForSecondsRealtime(0.45f);
                m_movie.Hide();
                yield return new WaitForSecondsRealtime(1.0f);
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
            // 同じ掛け合いは一度だけ（リトライでも繰り返さない）
            string eventKey = m_chapter.id + ".taunt." + speaker;
            if (string.IsNullOrEmpty(speaker) || StorySession.HasEvent(eventKey)) return;

            foreach (var line in m_chapter.cpuTaunts)
            {
                if (line == null || line.speaker != speaker) continue;
                StorySession.MarkEvent(eventKey);
                m_dialogue.StartCoroutine(m_dialogue.PlaySmall(line.speaker, line.text, m_chapter));
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
        // 詰まり防止：プレイヤーへ向かう積み木が来ないときに、いわだるの積み木を飛ばす
        //========================================

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

        // プレイヤーが脱落した（4人戦ではCPU同士の試合が続いてしまうので、ここでストーリーの敗北にする）
        void OnPlayerOut(MatchPlayer player)
        {
            if (m_finishedHandled || player == null || player != m_player) return;
            m_finishedHandled = true;
            // その場で試合を止めて、敗北の台詞 → リトライへ
            if (m_dialogue != null) m_dialogue.HideObjective();
            SetStoryPaused(true);
            // 試合は止めるが、敗北の台詞の音は鳴らす
            AudioListener.pause = false;
            SpecialGaugeHUD.Suppressed = true;
            SpecialInput.StoryLocked = true;
            StartCoroutine(Defeat());
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
            // 試合が終わったらゲージは出さない（リザルト・新聞・黒画面の上に出ないように）
            SpecialGaugeHUD.Suppressed = true;
            SpecialInput.StoryLocked = true;
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
            if (m_stageMode) m_movie.ShowStageConversation(true);
            else m_movie.ShowConversation(true);
            yield return m_movie.FadeIn(0.6f);
            yield return m_dialogue.PlayBig(m_chapter, m_chapter.after);

            // だるま新聞 → 章クリア保存
            yield return m_dialogue.PlayNewspaper(m_chapter.newspaper);

            // 新聞まで見終わったら、章クリアの実績を解除する
            AchievementManager.ReportStoryCleared(m_chapter.id);
            if (AchievementManager.PendingCount > 0)
            {
                var clearCanvas = ToyKit.CreateOverlayCanvas("StoryClearAchievements", 45);
                var clearPresenter = AchievementResultPresenter.Create(clearCanvas, PresentationSettings.DefaultFont);
                yield return clearPresenter.Play();
                Destroy(clearCanvas.gameObject);
            }

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
