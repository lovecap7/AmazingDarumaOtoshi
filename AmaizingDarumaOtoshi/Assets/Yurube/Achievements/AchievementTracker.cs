using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yurube.Achievements
{
    // ゲーム本編の出来事を実績システムへ流す係。
    // シーンに置かなくても、再生すると自動で1つだけ作られる（DontDestroyOnLoad）。
    // 試合・積み木・ハンマー・必殺技の「もともとあるお知らせ」を聞いているだけで、
    // ゲームの動きには一切触らない。
    //
    //  試合の始まり/終わり … DarumaMatch.Started / Finished
    //  攻撃の命中          … DarumaCharacter.DealtDamage（実際にダメージを与えたとき）
    //  打ち返し            … DarumaHammer.Hit（ReturnCount > 0）
    //  カウンター撃破      … 打ち返した積み木が当たった相手が直後に脱落したら
    //  連鎖                … TumikiBlock.Hit（積み木→積み木の当たり）を数えて長さを出す
    //  積み上げ            … 各プレイヤーの積み木の数を毎フレーム見る
    //  必殺技              … SpecialGauge.Consumed
    [DefaultExecutionOrder(-1000)]
    public class AchievementTracker : MonoBehaviour
    {
        static AchievementTracker s_instance;

        DarumaMatch m_match;
        bool m_active;
        readonly HashSet<DarumaCharacter> m_hooked = new HashSet<DarumaCharacter>();
        readonly Dictionary<TumikiBlock, int> m_chain = new Dictionary<TumikiBlock, int>();
        readonly Dictionary<MatchPlayer, int> m_lastStack = new Dictionary<MatchPlayer, int>();
        DarumaCharacter m_counterVictim;
        float m_counterTime = -100f;

        // シーンに置かなくても動くように、再生のたびに1つ作る
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (s_instance != null) return;
            var go = new GameObject("AchievementTracker");
            DontDestroyOnLoad(go);
            go.AddComponent<AchievementTracker>();
        }

        void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_instance = this;
            AchievementManager.Initialize();
            TumikiBlock.Hit += OnBlockHit;
            DarumaHammer.Hit += OnHammerHit;
            DarumaCharacter.AnyEliminated += OnEliminated;
            SpecialGauge.Consumed += OnSpecialConsumed;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void Start()
        {
            // DarumaMatch より先に動く（DefaultExecutionOrder）ので、試合が始まる前に受け取れる
            Bind(DarumaMatch.Current);
        }

        void OnDestroy()
        {
            TumikiBlock.Hit -= OnBlockHit;
            DarumaHammer.Hit -= OnHammerHit;
            DarumaCharacter.AnyEliminated -= OnEliminated;
            SpecialGauge.Consumed -= OnSpecialConsumed;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Unbind();
            if (s_instance == this) s_instance = null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Bind(DarumaMatch.Current);
        }

        void Update()
        {
            // シーン読み込みの知らせを取りこぼしても拾えるように、毎フレーム確かめる
            if (DarumaMatch.Current != m_match) Bind(DarumaMatch.Current);
            if (!m_active || m_match == null) return;
            HookCharacters();
            CheckStacks();
        }

        //========================================
        // 試合とのつなぎ
        //========================================

        void Bind(DarumaMatch match)
        {
            if (match == m_match) return;
            Unbind();
            m_match = match;
            if (m_match == null) return;
            m_match.Started += OnMatchStarted;
            m_match.Finished += OnMatchFinished;
            m_match.PlayerJoined += OnPlayerJoined;
        }

        void Unbind()
        {
            if (m_match != null)
            {
                m_match.Started -= OnMatchStarted;
                m_match.Finished -= OnMatchFinished;
                m_match.PlayerJoined -= OnPlayerJoined;
            }
            m_match = null;
            m_active = false;
            m_chain.Clear();
            m_lastStack.Clear();
            m_hooked.Clear();
            m_counterVictim = null;
        }

        void OnMatchStarted()
        {
            m_active = false;
            m_chain.Clear();
            m_lastStack.Clear();
            m_hooked.Clear();
            m_counterVictim = null;
            // ネット対戦のクライアントは試合を進めないので数えない（ホスト側で数える）
            if (m_match == null || m_match.IsNetClient) return;
            // 人が1人もいない試合（直接再生のダミーだけ等）は数えない
            if (!HasHumanPlayer()) return;

            m_active = true;
            AchievementEvents.RaiseMatchStarted();
            foreach (var p in m_match.Players) RegisterPlayer(p);
        }

        void OnPlayerJoined(MatchPlayer player)
        {
            if (!m_active) return;
            RegisterPlayer(player);
        }

        void OnMatchFinished(DarumaMatch.MatchResult result)
        {
            if (!m_active) return;
            m_active = false;
            m_counterVictim = null;

            var winner = result != null ? result.Winner : null;
            bool won = winner != null && !winner.IsCpu;
            // 積み木1個以下で勝ったか。幽霊（体が無い）は対象外
            bool lowStackWin = won && winner.Character != null && winner.StackCount <= 1;
            AchievementEvents.RaiseMatchFinished(won, lowStackWin);
        }

        void RegisterPlayer(MatchPlayer player)
        {
            if (player == null || player.IsCpu) return;
            var data = player.CharacterData;
            if (data != null && !string.IsNullOrEmpty(data.id)) AchievementEvents.RaiseCharacterUsed(data.id);
            Hook(player.Character);
            if (player.Character != null && player.StackCount > 0) AchievementEvents.RaiseStack(player.StackCount);
        }

        bool HasHumanPlayer()
        {
            foreach (var p in m_match.Players)
            {
                if (p != null && !p.IsCpu) return true;
            }
            return false;
        }

        //========================================
        // キャラクター（人間の分だけ）
        //========================================

        void HookCharacters()
        {
            foreach (var p in m_match.Players)
            {
                if (p == null || p.IsCpu) continue;
                Hook(p.Character);
            }
        }

        void Hook(DarumaCharacter character)
        {
            if (character == null || m_hooked.Contains(character)) return;
            m_hooked.Add(character);
            character.DealtDamage += OnDealtDamage;
        }

        void OnDealtDamage(DarumaCharacter attacker)
        {
            if (!IsHuman(attacker)) return;
            AchievementEvents.RaiseHit(1);
        }

        bool IsHuman(DarumaCharacter character)
        {
            if (character == null || m_match == null) return false;
            foreach (var p in m_match.Players)
            {
                if (p != null && p.Character == character) return !p.IsCpu;
            }
            return false;
        }

        bool IsHumanAttacker(IDarumaAttacker attacker)
        {
            return attacker is DarumaCharacter character && IsHuman(character);
        }

        //========================================
        // 積み木・ハンマー
        //========================================

        void OnBlockHit(TumikiHitInfo info)
        {
            var block = info.Block;
            if (block == null || !info.IsHarmful) return;
            bool mine = IsHumanAttacker(block.Owner);

            // 積み木 → 積み木：連鎖。当てた積み木の深さ + 1 が相手の深さになる
            if (info.Target is TumikiBlock other)
            {
                if (!mine) return;
                m_chain.TryGetValue(block, out int depth);
                if (m_chain.Count > 4096) m_chain.Clear();
                m_chain[other] = depth + 1;
                AchievementEvents.RaiseChain(depth + 1);
                return;
            }

            // 打ち返した積み木（ReturnCount > 0）が相手に当たった：撃破の判定用に覚えておく
            if (info.Target is DarumaCharacter victim && block.ReturnCount > 0 && mine && !IsHuman(victim))
            {
                m_counterVictim = victim;
                m_counterTime = Time.unscaledTime;
            }
        }

        void OnHammerHit(HammerHitInfo info)
        {
            // 飛んできた攻撃を打ち返した（ReturnCount が増えた）＝カウンター成功
            if (info.ReturnCount <= 0) return;
            if (!IsHumanAttacker(info.Owner)) return;
            AchievementEvents.RaiseCounter();
        }

        void OnEliminated(DarumaCharacter character)
        {
            if (character == null || m_counterVictim == null) return;
            if (!ReferenceEquals(character, m_counterVictim)) return;
            if (!character.IsEliminatedByAttack) return;
            if (Time.unscaledTime - m_counterTime > 0.6f) return;
            m_counterVictim = null;
            AchievementEvents.RaiseCounterKO();
        }

        void OnSpecialConsumed(SpecialGauge gauge)
        {
            if (gauge == null) return;
            if (!IsHuman(gauge.GetComponent<DarumaCharacter>())) return;
            AchievementEvents.RaiseSpecial();
        }

        //========================================
        // 積み木の数
        //========================================

        void CheckStacks()
        {
            foreach (var p in m_match.Players)
            {
                if (p == null || p.IsCpu) continue;
                int count = p.StackCount;
                m_lastStack.TryGetValue(p, out int last);
                if (count == last) continue;
                m_lastStack[p] = count;
                if (count > 0) AchievementEvents.RaiseStack(count);
            }
        }
    }
}
