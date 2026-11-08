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
        readonly Dictionary<MatchPlayer, int> m_lastTypes = new Dictionary<MatchPlayer, int>();
        readonly Dictionary<MatchPlayer, float> m_lastKnocked = new Dictionary<MatchPlayer, float>();
        readonly Dictionary<MatchPlayer, MatchPlayer> m_lastKnockAttacker = new Dictionary<MatchPlayer, MatchPlayer>();
        readonly Dictionary<MatchPlayer, float> m_lastKoTime = new Dictionary<MatchPlayer, float>();
        readonly Dictionary<MatchPlayer, int> m_koWindowCount = new Dictionary<MatchPlayer, int>();
        readonly HashSet<MatchPlayer> m_damaged = new HashSet<MatchPlayer>();
        readonly Dictionary<TumikiBlock, ChainStamp> m_chainStamp = new Dictionary<TumikiBlock, ChainStamp>();
        readonly Dictionary<DarumaCharacter, ChainVictim> m_chainVictim = new Dictionary<DarumaCharacter, ChainVictim>();
        DarumaCharacter m_counterVictim;
        float m_counterTime = -100f;

        // 追加実績の判定に使う時間（秒）。仮の値はプレイテストで調整する
        const float OpportunistWindow = 3.0f;
        const float MultiKOWindow = 0.5f;
        const float ChainKOWindow = 0.6f;
        const float QuickWinSeconds = 30.0f;

        struct ChainStamp
        {
            public float Time;
            public int Depth;
            public IDarumaAttacker Owner;
        }

        struct ChainVictim
        {
            public float Time;
            public IDarumaAttacker Owner;
        }

        float Now => m_match != null ? m_match.Elapsed : Time.unscaledTime;

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
            DarumaHammer.SweepFinished += OnHammerSweepFinished;
            DarumaCharacter.AnyEliminated += OnEliminated;
            DarumaCharacter.AnyDamaged += OnAnyDamaged;
            SpecialGauge.Consumed += OnSpecialConsumed;
            AchievementEvents.PassiveTriggered += OnPassiveTriggered;
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
            DarumaHammer.SweepFinished -= OnHammerSweepFinished;
            DarumaCharacter.AnyEliminated -= OnEliminated;
            DarumaCharacter.AnyDamaged -= OnAnyDamaged;
            SpecialGauge.Consumed -= OnSpecialConsumed;
            AchievementEvents.PassiveTriggered -= OnPassiveTriggered;
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
            m_match.PlayerKnocked += OnPlayerKnocked;
            m_match.PlayerKO += OnPlayerKO;
        }

        void Unbind()
        {
            if (m_match != null)
            {
                m_match.Started -= OnMatchStarted;
                m_match.Finished -= OnMatchFinished;
                m_match.PlayerJoined -= OnPlayerJoined;
                m_match.PlayerKnocked -= OnPlayerKnocked;
                m_match.PlayerKO -= OnPlayerKO;
            }
            m_match = null;
            m_active = false;
            m_chain.Clear();
            m_chainStamp.Clear();
            m_chainVictim.Clear();
            m_lastStack.Clear();
            m_lastTypes.Clear();
            m_lastKnocked.Clear();
            m_lastKnockAttacker.Clear();
            m_lastKoTime.Clear();
            m_koWindowCount.Clear();
            m_damaged.Clear();
            m_hooked.Clear();
            m_counterVictim = null;
        }

        void OnMatchStarted()
        {
            m_active = false;
            m_chain.Clear();
            m_chainStamp.Clear();
            m_chainVictim.Clear();
            m_lastStack.Clear();
            m_lastTypes.Clear();
            m_lastKnocked.Clear();
            m_lastKnockAttacker.Clear();
            m_lastKoTime.Clear();
            m_koWindowCount.Clear();
            m_damaged.Clear();
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

            if (won)
            {
                // 一度も被弾せずに勝ったか（この試合で被害を受けた人を記録している）
                if (winner.Character != null && !m_damaged.Contains(winner)) AchievementEvents.RaiseNoDamageWin();
                // 試合開始から一定時間以内の勝利
                if (m_match != null && m_match.Elapsed <= QuickWinSeconds) AchievementEvents.RaiseQuickWin();
            }
            else if (winner != null && winner.IsCpu)
            {
                // 引き分け（winner == null）は敗北に含めない
                AchievementEvents.RaiseMatchLost();
            }
        }

        void RegisterPlayer(MatchPlayer player)
        {
            if (player == null || player.IsCpu) return;
            var data = player.CharacterData;
            if (data != null && !string.IsNullOrEmpty(data.id)) AchievementEvents.RaiseCharacterUsed(data.id);
            Hook(player.Character);
            if (player.Character != null && player.StackCount > 0)
            {
                AchievementEvents.RaiseStack(player.StackCount);
                int types = CountStackTypes(player);
                if (types > 0) AchievementEvents.RaiseBlockTypesInStack(types);
                m_lastStack[player] = player.StackCount;
                m_lastTypes[player] = types;
            }
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
                if (m_chain.Count > 4096)
                {
                    m_chain.Clear();
                    m_chainStamp.Clear();
                }
                m_chain[other] = depth + 1;
                m_chainStamp[other] = new ChainStamp
                {
                    Time = Time.unscaledTime,
                    Depth = depth + 1,
                    Owner = block.Owner,
                };
                AchievementEvents.RaiseChain(depth + 1);
                return;
            }

            if (info.Target is DarumaCharacter victim)
            {
                if (!mine) return;

                // 打ち返した積み木（ReturnCount > 0）が相手に当たった：カウンター撃破の判定用に覚えておく
                if (block.ReturnCount > 0 && !IsHuman(victim))
                {
                    m_counterVictim = victim;
                    m_counterTime = Time.unscaledTime;
                }

                // 連鎖で飛んだ積み木が相手に当たった：このあと脱落したら「ドミノ倒し」
                if (m_chainStamp.TryGetValue(block, out ChainStamp stamp)
                    && Time.unscaledTime - stamp.Time <= ChainKOWindow)
                {
                    // 自分の積み木で自分に当たった場合は数えない
                    if (!(block.Owner is DarumaCharacter owner) || owner != victim)
                    {
                        m_chainVictim[victim] = new ChainVictim
                        {
                            Time = Time.unscaledTime,
                            Owner = block.Owner,
                        };
                    }
                }
            }
        }

        void OnHammerHit(HammerHitInfo info)
        {
            // 飛んできた攻撃を打ち返した（ReturnCount が増えた）＝カウンター成功
            if (info.ReturnCount <= 0) return;
            if (!IsHumanAttacker(info.Owner)) return;
            AchievementEvents.RaiseCounter();
            // 同じ積み木を打ち返した回数が、そのままラリー数になる
            AchievementEvents.RaiseRally(info.ReturnCount);
        }

        // 薙ぎ払いが終わった。何にも当たっていなければ空振り
        void OnHammerSweepFinished(HammerSweepInfo info)
        {
            if (info.HitCount > 0) return;
            if (!IsHumanAttacker(info.Owner)) return;
            AchievementEvents.RaiseHammerMiss();
        }

        void OnEliminated(DarumaCharacter character)
        {
            if (character == null) return;
            bool byAttack = character.IsEliminatedByAttack;

            if (ReferenceEquals(character, m_counterVictim))
            {
                bool counted = byAttack && Time.unscaledTime - m_counterTime <= 0.6f;
                m_counterVictim = null;
                if (counted) AchievementEvents.RaiseCounterKO();
            }

            if (m_chainVictim.TryGetValue(character, out ChainVictim chain))
            {
                m_chainVictim.Remove(character);
                if (byAttack
                    && Time.unscaledTime - chain.Time <= ChainKOWindow
                    && IsHumanAttacker(chain.Owner))
                {
                    AchievementEvents.RaiseChainKO();
                }
            }
        }

        void OnAnyDamaged(DarumaCharacter character)
        {
            var player = FindPlayer(character);
            if (player != null) m_damaged.Add(player);
        }

        void OnPassiveTriggered(DarumaCharacter character)
        {
            if (IsHuman(character)) AchievementEvents.RaisePassiveActivated();
        }

        void OnPlayerKnocked(MatchPlayer victim, MatchPlayer attacker)
        {
            if (victim == null) return;
            m_damaged.Add(victim);
            if (attacker == null || attacker == victim) return;
            m_lastKnocked[victim] = Now;
            m_lastKnockAttacker[victim] = attacker;
        }

        void OnPlayerKO(MatchPlayer victim, MatchPlayer attacker)
        {
            if (victim != null) m_damaged.Add(victim);
            if (victim == null || attacker == null || victim == attacker) return;

            if (!attacker.IsCpu)
            {
                // 同じ攻撃・連鎖で続けて倒した人数（0.5秒以内）
                float now = Now;
                m_koWindowCount.TryGetValue(attacker, out int count);
                if (m_lastKoTime.TryGetValue(attacker, out float last) && now - last <= MultiKOWindow)
                    count++;
                else
                    count = 1;
                m_lastKoTime[attacker] = now;
                m_koWindowCount[attacker] = count;
                AchievementEvents.RaiseMultiKO(count);

                // 他の人に傷つけられた直後の相手を倒した
                if (m_lastKnocked.TryGetValue(victim, out float knockedAt)
                    && now - knockedAt <= OpportunistWindow)
                {
                    m_lastKnockAttacker.TryGetValue(victim, out MatchPlayer other);
                    if (other != null && other != attacker && other != victim)
                        AchievementEvents.RaiseOpportunistKO();
                }
            }

            m_lastKnocked.Remove(victim);
            m_lastKnockAttacker.Remove(victim);
        }

        MatchPlayer FindPlayer(DarumaCharacter character)
        {
            if (character == null || m_match == null) return null;
            foreach (var p in m_match.Players)
            {
                if (p != null && p.Character == character) return p;
            }
            return null;
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
                int types = CountStackTypes(p);
                m_lastStack.TryGetValue(p, out int last);
                m_lastTypes.TryGetValue(p, out int lastTypes);
                if (count == last && types == lastTypes) continue;
                m_lastStack[p] = count;
                m_lastTypes[p] = types;
                if (count > 0) AchievementEvents.RaiseStack(count);
                if (types > 0) AchievementEvents.RaiseBlockTypesInStack(types);
            }
        }

        // いま積んでいる積み木の色の種類数（赤・青・緑・黄）
        int CountStackTypes(MatchPlayer player)
        {
            var stack = player != null ? player.Stack : null;
            if (stack == null) return 0;

            var seen = new bool[4];
            int types = 0;
            int n = stack.Count;
            for (int i = 0; i < n; i++)
            {
                var block = stack.Get(i);
                if (block == null) continue;
                int color = (int)block.Color;
                if (color < 0 || color >= seen.Length || seen[color]) continue;
                seen[color] = true;
                types++;
            }
            return types;
        }
    }
}
