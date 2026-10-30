using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nakahira
{
    // 試合の管理。プレイヤーの登録、キャラクター/幽霊の生成、モードのルールでの勝ち負け、結果の表示を行う
    // 始め方は2通り。どちらも同じ試合の進め方(モード・得点・復活・終わり方)で、出る人の決め方だけが違う
    //  ・セレクトから: Fujihara のセレクト画面で決めた内容(MatchSetup)があるとき。決めた人数・キャラ・入力機器で全員を出し、
    //                  結果を見せてセレクトへ戻る(ボタンを押した人の途中参加はしない)
    //  ・直接再生    : ステージのシーンをそのまま再生したとき。同じGameObjectの PlayerInputManager で、ボタンを押した人が
    //                  途中から参加する(DarumaPlayer)。ルールはインスペクターの「直接再生」で決める(既定はゴースト)。
    //                  結果を見せたら、同じシーンをやり直す
    //
    // ■ モード
    //   ストック：やられるとストックが1つ減り、少ししてから首だけで復活。0 になったら脱落。最後の1人が勝ち
    //             時間制限ありなら、時間切れでストックが一番多い人の勝ち(同じストックなら積み木が多い人)
    //   タイム　：やられても少ししてから復活。崩す +1・崩される -1・顔を飛ばす +1・相手なしで落ちる -1(MatchSetup.Rules)。
    //             時間切れで点が一番多い人の勝ち
    //   ゴースト：やられると幽霊になり、生き残っている相手を叩くと復活(出現ポイントのどれかにランダムで)。生き残りが1人になったら終わり
    //             時間制限ありなら、時間切れで生き残っている中で積み木が一番多い人の勝ち
    //   同点(同じ数)のときは引き分け。
    //
    // ■ ゲーム画面の UI(HUD)を作るとき
    //   DarumaMatch.Current から読む。値を毎フレーム読んでも、イベントで受け取ってもよい。
    //     Players            ：出ている人(番号順)。1人分の中身は MatchPlayer を参照
    //     Rules / Mode       ：モードとルール
    //     HasTimeLimit / TimeLeft / Elapsed：時間
    //     IsFinished / Result：結果(勝者・理由・順位)
    //   イベント
    //     Started                  ：試合が始まった(全員が出たあと。直接再生では、最初からいる人(ダミー)が出たあと)
    //     CountdownFinished        ：開始のカウントダウンが終わって動けるようになった(カウントダウンが無ければ来ない)
    //                                カウントダウン中は IsCountingDown / CountdownLeft で読む。操作・時間・積み木の生成は止まる
    //     PlayerJoined(人)         ：途中から参加した(直接再生)
    //     PlayerKnocked(被害, 相手)：積み木を崩された(相手が分かったときだけ)
    //     PlayerKO(被害, 相手)     ：顔を飛ばされた / 落ちた(相手は分からなければ null)
    //     PlayerRespawnAppearing(人, 位置, 秒)：復活の出現演出が始まった(この秒数のあとに位置へ出て PlayerRespawned が来る)
    //     PlayerRespawned(人)      ：復活した(ストック・タイムの復活、幽霊からの復活)
    //     PlayerOut(人)            ：脱落した(ストックが無くなった)
    //     Scored(人, 点, 理由)      ：点が動いた(タイム)
    //     Finished(結果)            ：試合が終わった
    //   1人分の積み木・ストック・点数の変化は MatchPlayer のイベント(StackChanged など)でも受け取れる。
    [DefaultExecutionOrder(-50)]   // HUD などより先にプレイヤーを出す
    public class DarumaMatch : MonoBehaviour
    {
        public enum ScoreReason { Knock, Knocked, KO, SelfOut }
        public enum FinishReason { LastOneStanding, TimeUp }

        public class MatchResult
        {
            public FinishReason Reason;
            public MatchPlayer Winner;                  // null = 引き分け
            public List<MatchPlayer> Ranking = new List<MatchPlayer>();   // 1位から順(同じ順位の人もいる。Rank を見る)
            public bool IsDraw => Winner == null;
        }

        [Header("ステージ")]
        // 出現・復活ポイント。他のキャラクターすべてから最も遠い地点が選ばれる
        [SerializeField] Transform[] m_spawnPoints;
        [SerializeField] StageArea m_stageArea;

        [Header("直接再生:キャラクター(未設定なら MatchSettings のもの)")]
        [SerializeField] DarumaCharacter m_characterPrefab;
        [SerializeField] DarumaGhost m_ghostPrefab;
        // 復活したあとの無敵時間
        [SerializeField] float m_reviveInvincibleTime = 1.5f;

        [Header("直接再生:ルール")]
        [SerializeField] MatchSetup.MatchMode m_directMode = MatchSetup.MatchMode.Ghost;
        // ストックのストック数
        [SerializeField, Min(1)] int m_directStocks = 3;
        // 時間制限(秒)。0 = なし(タイムでは 0 のとき 120 秒)
        [SerializeField, Min(0.0f)] float m_directTimeLimit = 0.0f;
        [SerializeField] int m_maxPlayers = 4;

        [Header("直接再生:デバッグ")]
        // 開始時に置く操作なしのダミープレイヤーの数
        [SerializeField] int m_dummyCount = 1;
        // ダミーに最初から積んでおく積み木
        [SerializeField] TumikiBlock m_tumikiPrefab;
        [SerializeField] int m_dummyStartBlocks = 5;
        // 開始時に置く CPU(DarumaCpu)の数と、その考え方・腕前
        [SerializeField] int m_cpuCount = 0;
        [SerializeField] CpuParams m_cpuParams;

        [Header("表示")]
        // 試合の仮の表示(名札・残り時間・結果)を付ける
        [SerializeField] bool m_addDebugView = true;
        // 復活するとき、出る位置で光る演出(PlayerRespawnAppearing)を見せてからキャラクターを出すまでの時間(0 ならすぐ出す)
        [SerializeField, Min(0.0f)] float m_respawnAppearTime = 0.8f;

        [Header("開始のカウントダウン")]
        // カウントダウンの秒数(3 なら 3, 2, 1。0 ならすぐ始める)。シーンのフェードが明けてから数え始める
        [SerializeField, Min(0.0f)] float m_startCountdown = 3.0f;
        // 直接再生でもカウントダウンする(オフならセレクトから始めた試合だけ)
        [SerializeField] bool m_countdownInDirectPlay = false;

        public static DarumaMatch Current { get; private set; }

        // false にすると、試合が終わってもセレクトへ戻らない(自動テスト用)
        public static bool AutoReturnToSelect = true;
        // 試合中の速さ(自動テスト用。ふだんは 1)
        public static float PlaySpeed = 1f;

        const string SelectSceneName = "SelectScene";
        static readonly Color[] PlayerColors =
        {
            new Color(0.93f, 0.27f, 0.25f), new Color(0.22f, 0.55f, 0.95f), new Color(0.98f, 0.76f, 0.12f), new Color(0.30f, 0.74f, 0.35f),
            new Color(0.96f, 0.55f, 0.15f), new Color(0.40f, 0.85f, 0.85f), new Color(0.85f, 0.45f, 0.80f), new Color(0.55f, 0.45f, 0.35f),
        };
        static readonly Color CpuColor = new Color(0.55f, 0.55f, 0.58f);

        public static Color ColorFor(MatchSetup.Entry e) => e.isCpu ? CpuColor : PlayerColors[e.playerIndex % PlayerColors.Length];

        // 直接再生の試合をやり直す直前(ステージをまとめたシーンで、今のステージを覚えておく等)
        public static event Action BeforeRestart;

        // ---- 読むもの ----
        // セレクト画面で決めた内容で始めた試合か(false なら直接再生)
        public bool IsConfigured { get; private set; }
        public bool IsFinished => Result != null;
        public StageArea StageArea => m_stageArea;
        // 出現・復活ポイント(デバッグ表示などで読む)
        public IReadOnlyList<Transform> SpawnPoints => m_spawnPoints ?? Array.Empty<Transform>();
        public MatchSetup.MatchRules Rules => m_rules ?? MatchSetup.Rules;
        public MatchSetup.MatchMode Mode => Rules.mode;
        public IReadOnlyList<MatchPlayer> Players => m_players;
        public bool HasTimeLimit => m_timeLimit > 0f;
        public float TimeLimit => m_timeLimit;
        public float TimeLeft => HasTimeLimit ? Mathf.Max(0f, m_timeLimit - Elapsed) : -1f;
        public float Elapsed { get; private set; }
        public MatchResult Result { get; private set; }
        // 復活の出現演出の時間
        public float RespawnAppearTime => m_respawnAppearTime;
        // 開始のカウントダウン中か(フェードが明けるのを待っている間も含む)。この間は操作・時間・積み木の生成が止まる
        public bool IsCountingDown => m_countdownLeft > 0f;
        // カウントダウンの残り(秒)と、全体の秒数
        public float CountdownLeft => Mathf.Max(0f, m_countdownLeft);
        public float CountdownLength => m_countdownLength;
        // ネット対戦用(suehara): ネット対戦の試合か(同じ GameObject に INetMatchDriver がある)
        public bool IsNetworked => m_net != null;
        // ネット対戦のクライアントか(試合を進めず、ホストから届いた内容を見せるだけ)
        public bool IsNetClient => m_net != null && !m_net.IsServer;
        // ネット対戦で、ホストが試合を始める前か。この間は時間が進まず、やられても少ししてから復活し、試合は終わらない
        public bool IsNetWaiting => m_net != null && !m_netStarted;

        // ---- お知らせ ----
        public event Action Started;
        public event Action CountdownFinished;
        public event Action<MatchPlayer> PlayerJoined;
        public event Action<MatchPlayer, MatchPlayer> PlayerKnocked;
        public event Action<MatchPlayer, MatchPlayer> PlayerKO;
        public event Action<MatchPlayer, Vector3, float> PlayerRespawnAppearing;
        public event Action<MatchPlayer> PlayerRespawned;
        public event Action<MatchPlayer> PlayerOut;
        public event Action<MatchPlayer, int, ScoreReason> Scored;
        public event Action<MatchResult> Finished;

        // 崩した / 顔を飛ばした、の記録(同じフレームの「やられた」と「当てた」を組にする)
        class Hit
        {
            public MatchPlayer victim, attacker;
            public bool head;
        }

        // 復活の出現演出中(時間が来たら pos に出す)
        class Appearing
        {
            public MatchPlayer player;
            public Vector3 pos;
            public float timeLeft;
        }

        MatchSettings m_settings;
        MatchSetup.MatchRules m_rules;
        PlayerInputManager m_inputManager;
        float m_timeLimit;
        readonly List<MatchPlayer> m_players = new List<MatchPlayer>();
        readonly Dictionary<DarumaCharacter, MatchPlayer> m_owners = new Dictionary<DarumaCharacter, MatchPlayer>();
        readonly List<Hit> m_hits = new List<Hit>();
        readonly List<Appearing> m_appearing = new List<Appearing>();
        int m_outCount, m_lastOutFrame = -1;
        bool m_lastOnePending, m_timeUpFinish;
        float m_finishTime = -1f;
        bool m_leaving;
        float m_countdownLeft, m_countdownLength;
        // ネット対戦用(suehara)
        INetMatchDriver m_net;
        bool m_netStarted;

        private void Awake()
        {
            Current = this;
            // 定員オーバーで参加したプレイヤーは Register で弾かれて破棄される
            m_inputManager = GetComponent<PlayerInputManager>();

            // 部品・時間の設定(直接再生で MatchSettings が無くても、シーンに設定した部品で遊べるようにする)
            m_settings = MatchSettings.Instance;
            if (m_settings == null) m_settings = ScriptableObject.CreateInstance<MatchSettings>();

            // ネット対戦用(suehara): 接続した人が出るので、セレクトの内容もボタンを押した人の途中参加も使わない
            m_net = GetComponent<INetMatchDriver>();

            // セレクト画面で決めた内容があれば、その内容で試合をする(ボタンを押した人の途中参加は止める)
            IsConfigured = m_net == null && MatchSetup.Players.Count > 0;
            m_rules = IsConfigured ? MatchSetup.Rules : DirectRules();
            m_timeLimit = Rules.TimeLimit;
            if ((IsConfigured || m_net != null) && m_inputManager != null)
            {
                m_inputManager.DisableJoining();
                m_inputManager.enabled = false;
            }
            if (m_addDebugView && GetComponent<MatchDebugView>() == null) gameObject.AddComponent<MatchDebugView>();
        }

        // 直接再生のルール(インスペクターの設定から作る)
        MatchSetup.MatchRules DirectRules()
        {
            var rules = new MatchSetup.MatchRules
            {
                mode = m_directMode,
                stocks = m_directStocks,
                stockTimeLimit = m_directTimeLimit,
                ghostTimeLimit = m_directTimeLimit,
                timeLength = m_directTimeLimit > 0.0f ? m_directTimeLimit : 120.0f,
            };
            return rules;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            // 試合の速さ・終わったときの停止を元に戻す
            if (IsConfigured || Result != null) Time.timeScale = 1f;
        }

        private void Start()
        {
            // ネット対戦用(suehara): 出る人は接続してから AddNetPlayer で足す
            if (m_net != null) return;
            if (IsConfigured) StartConfigured();
            else StartDirect();
        }

        private void Update()
        {
            // ネット対戦用(suehara): クライアントは試合を進めない(時間・結果はホストから届く)
            if (IsNetClient) return;
            UpdateMatch();
        }

        private void LateUpdate()
        {
            if (IsNetClient) return;
            LateUpdateMatch();
        }

        //========================================
        // 共通(カメラ・ステージ切り替えなどから使う)
        //========================================

        // 参加している全員の、いま動かしている体(生存キャラクター・幽霊)
        public void GetBodies(List<Transform> results)
        {
            results.Clear();
            foreach (var p in m_players)
            {
                if (p != null && p.Body != null) results.Add(p.Body);
            }
        }

        // 全員の体(キャラクター・幽霊)を消す。消えるのはフレームの終わりなので、次のフレームで SpawnAllAtStart を呼ぶ
        // (ステージの切り替えなどで、全員を出し直すときに使う)
        public void ClearBodies()
        {
            foreach (var p in m_players)
            {
                if (p.Character != null)
                {
                    m_owners.Remove(p.Character);
                    Destroy(p.Character.gameObject);
                }
                if (p.Ghost != null) Destroy(p.Ghost.gameObject);
            }
            m_hits.Clear();
            // 出現演出中の人も SpawnAllAtStart で出し直す
            m_appearing.Clear();
        }

        // 全員(脱落した人を除く)を出現ポイントに出し直す
        public void SpawnAllAtStart()
        {
            for (int i = 0; i < m_players.Count; i++)
            {
                var p = m_players[i];
                if (p.State == MatchPlayer.PlayerState.Out) continue;
                SpawnMatchCharacter(p, InitialSpawnPoint(i), false);
            }
        }

        //========================================
        // 直接再生:参加
        //========================================

        // 最初からいるダミー(操作なし)を出して、試合を始める。人はボタンを押して途中から参加する(Register)
        private void StartDirect()
        {
            for (int i = 0; i < m_dummyCount && m_players.Count < m_maxPlayers; i++)
            {
                var entry = new MatchSetup.Entry
                {
                    playerIndex = m_players.Count,
                    isCpu = true,
                    deviceName = "Dummy",
                    cpuNumber = m_dummyCount > 1 ? i + 1 : 0,
                };
                var p = new GameObject().AddComponent<MatchPlayer>();
                p.Setup(entry, ColorFor(entry), null, Mode == MatchSetup.MatchMode.Stock ? Mathf.Max(1, Rules.stocks) : -1);
                p.name += "_Dummy";
                m_players.Add(p);
                SpawnMatchCharacter(p, InitialSpawnPoint(m_players.Count - 1), false);
                StackDebugBlocks(p.Character);
            }
            for (int i = 0; i < m_cpuCount && m_players.Count < m_maxPlayers; i++)
            {
                var entry = new MatchSetup.Entry
                {
                    playerIndex = m_players.Count,
                    isCpu = true,
                    deviceName = "CPU",
                    cpuNumber = m_dummyCount + m_cpuCount > 1 ? m_dummyCount + i + 1 : 0,
                };
                var go = new GameObject();
                var cpu = go.AddComponent<DarumaCpu>();
                cpu.Params = m_cpuParams;
                var p = go.AddComponent<MatchPlayer>();
                p.Setup(entry, ColorFor(entry), cpu, Mode == MatchSetup.MatchMode.Stock ? Mathf.Max(1, Rules.stocks) : -1);
                m_players.Add(p);
                SpawnMatchCharacter(p, InitialSpawnPoint(m_players.Count - 1), false);
            }
            Time.timeScale = PlaySpeed;
            if (m_countdownInDirectPlay) BeginCountdown();
            Started?.Invoke();
        }

        // ボタンを押した人(PlayerInputManager が出した DarumaPlayer)を試合に参加させ、キャラクターを出す。参加できなければfalse
        // 参加した人も、セレクトから出た人と同じ MatchPlayer として扱う(同じ GameObject に付ける)
        public bool Register(DarumaPlayer player)
        {
            if (IsConfigured || IsFinished || m_players.Count >= m_maxPlayers) return false;

            var input = player.GetComponent<PlayerInput>();
            InputDevice device = input != null && input.devices.Count > 0 ? input.devices[0] : null;
            int humans = 0;
            foreach (var other in m_players) if (!other.IsCpu) humans++;
            var entry = new MatchSetup.Entry
            {
                playerIndex = humans,
                isCpu = false,
                deviceId = device != null ? device.deviceId : -1,
                deviceName = device != null ? device.displayName : "",
            };

            var p = player.gameObject.AddComponent<MatchPlayer>();
            p.Setup(entry, ColorFor(entry), player.GetComponent<IDarumaCommandSource>(), Mode == MatchSetup.MatchMode.Stock ? Mathf.Max(1, Rules.stocks) : -1);
            player.OnRegistered(this, p);
            m_players.Add(p);
            SpawnMatchCharacter(p, FarthestSpawnPoint(p), false);
            Debug.Log($"[DarumaMatch] {p.Label} が参加 ({m_players.Count}/{m_maxPlayers})");
            PlayerJoined?.Invoke(p);
            return true;
        }

        private void StackDebugBlocks(DarumaCharacter character)
        {
            if (m_tumikiPrefab == null || character == null) return;
            for (int i = 0; i < m_dummyStartBlocks; i++)
            {
                TumikiBlock block = Instantiate(m_tumikiPrefab, character.transform.position, character.transform.rotation);
                block.SetColor((TumikiColor)(i % 4));
                character.Stack.AddBottom(block);
            }
        }

        // ステージの中心を向く向き
        private Quaternion FacingCenter(Vector3 pos)
        {
            Vector3 toCenter = (m_stageArea != null ? m_stageArea.transform.position : Vector3.zero) - pos;
            toCenter.y = 0.0f;
            return toCenter.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toCenter) : Quaternion.identity;
        }

        //========================================
        // セレクトから:始める
        //========================================

        private void StartConfigured()
        {
            if (m_settings == null || m_settings.characterPrefab == null)
            {
                Debug.LogError("[DarumaMatch] Resources/MatchSettings が無いか、characterPrefab が未設定です");
                enabled = false;
                return;
            }

            // プレイヤー番号の小さい順に出す
            foreach (var e in MatchSetup.Players)
            {
                var p = CreatePlayer(e);
                m_players.Add(p);
                SpawnMatchCharacter(p, InitialSpawnPoint(m_players.Count - 1), false);
                Debug.Log($"[DarumaMatch] {p.Label} {(e.character != null ? e.character.displayName : "-")} ({(e.isCpu ? "CPU Lv" + e.cpuLevel : e.deviceName + " #" + e.deviceId)})");
            }
            Debug.Log("[DarumaMatch] " + MatchSetup.Describe());
            Time.timeScale = PlaySpeed;
            BeginCountdown();
            Started?.Invoke();
        }

        //========================================
        // 開始のカウントダウン(共通)
        //========================================

        // カウントダウンを始める(出ている全員の操作を止める。あとから出たキャラクターも SpawnMatchCharacter で止める)
        void BeginCountdown()
        {
            if (m_startCountdown <= 0f) return;
            m_countdownLength = m_startCountdown;
            m_countdownLeft = m_startCountdown;
            foreach (var p in m_players) if (p.Character != null) p.Character.SetCommandSource(null);
        }

        // フェードが明けてから数える。終わったら全員を動けるようにする。カウントダウン中なら true
        bool UpdateCountdown()
        {
            if (m_countdownLeft <= 0f) return false;
            if (Fader.IsBusy) return true;
            m_countdownLeft -= Time.deltaTime;
            if (m_countdownLeft > 0f) return true;

            m_countdownLeft = 0f;
            foreach (var p in m_players) if (p.Character != null) p.Character.SetCommandSource(p.CommandSource);
            CountdownFinished?.Invoke();
            return false;
        }

        MatchPlayer CreatePlayer(MatchSetup.Entry e)
        {
            GameObject go;
            IDarumaCommandSource source = null;
            var device = e.Device;
            if (e.isCpu)
            {
                go = new GameObject();
                var cpu = go.AddComponent<CpuDarumaInput>();
                cpu.level = e.cpuLevel;
                source = cpu;
            }
            else if (device is Gamepad && m_settings.gamepadInputPrefab != null)
            {
                // そのコントローラーと組にして、本編と同じ操作設定(DarumaInputHandler)で動かす
                var input = PlayerInput.Instantiate(m_settings.gamepadInputPrefab, playerIndex: e.playerIndex, controlScheme: "Gamepad", pairWithDevice: device);
                go = input.gameObject;
                source = go.GetComponent<IDarumaCommandSource>();
            }
            else if (device is Keyboard)
            {
                go = new GameObject();
                source = go.AddComponent<KeyboardDarumaInput>();
            }
            else
            {
                // コントローラーが抜けているなど:操作なしで出す(人数と番号はセレクトのまま)
                Debug.LogWarning($"[DarumaMatch] {e.DisplayLabel} の入力機器({e.deviceName} #{e.deviceId})が見つからないので、操作なしで出します");
                go = new GameObject();
            }

            var p = go.AddComponent<MatchPlayer>();
            p.Setup(e, ColorFor(e), source, Mode == MatchSetup.MatchMode.Stock ? Mathf.Max(1, Rules.stocks) : -1);
            return p;
        }

        void SpawnMatchCharacter(MatchPlayer p, Vector3 pos, bool invincible)
        {
            var c = Instantiate(CharacterPrefab(p), pos, FacingCenter(pos));
            // ネット対戦用(suehara): 全員に配る
            m_net?.OnBodySpawned(c.gameObject);
            if (invincible) c.SetInvincible(IsConfigured ? m_settings.respawnInvincibleTime : m_reviveInvincibleTime);

            m_owners[c] = p;
            c.Eliminated += OnMatchCharacterEliminated;
            c.DealtDamage += OnDealtDamage;
            int last = 0;
            c.Stack.Changed += () =>
            {
                // 積み木が減った:崩された(だれに崩されたかは、このあと同じフレームの DealtDamage で分かる)
                int n = c.Stack.Count;
                if (n < last) m_hits.Add(new Hit { victim = p });
                last = n;
            };
            p.SetCharacter(c);
            // カウントダウン中に出し直した(ステージの切り替えなど)キャラクターも、終わるまで動かさない
            if (IsCountingDown) c.SetCommandSource(null);
        }

        DarumaCharacter CharacterPrefab(MatchPlayer p)
        {
            var data = p.CharacterData;
            if (data != null && data.prefab != null)
            {
                var custom = data.prefab.GetComponent<DarumaCharacter>();
                if (custom != null) return custom;
            }
            // 直接再生ではシーンに設定したキャラクターを優先する
            if (!IsConfigured && m_characterPrefab != null) return m_characterPrefab;
            return m_settings.characterPrefab;
        }

        DarumaGhost GhostPrefab => !IsConfigured && m_ghostPrefab != null ? m_ghostPrefab : m_settings.ghostPrefab;

        //========================================
        // 毎フレーム(共通)
        //========================================

        void UpdateMatch()
        {
            if (Result != null) { UpdateResult(); return; }
            // カウントダウン中は時間を進めない
            if (UpdateCountdown()) return;

            // ネット対戦用(suehara): 開始待ちの間は時間を進めない
            if (!IsNetWaiting) Elapsed += Time.deltaTime;

            // 復活待ち
            foreach (var p in m_players)
            {
                if (p.State != MatchPlayer.PlayerState.Respawning || IsAppearing(p)) continue;
                p.RespawnTimeLeft -= Time.deltaTime;
                if (p.RespawnTimeLeft > 0f) continue;
                Respawn(p, FarthestSpawnPoint(p));
            }
            UpdateAppearing();

            if (HasTimeLimit && Elapsed >= m_timeLimit) Finish(FinishReason.TimeUp);
            else if (IsConfigured && AutoReturnToSelect && PressedBack()) BackToSelect();   // テスト用:途中でもセレクトへ戻れる
        }

        // 復活させる。出現演出の時間があれば、その間は出る位置で光る演出を見せてから出す
        void Respawn(MatchPlayer p, Vector3 pos)
        {
            if (m_respawnAppearTime <= 0f)
            {
                RespawnNow(p, pos);
                return;
            }
            m_appearing.Add(new Appearing { player = p, pos = pos, timeLeft = m_respawnAppearTime });
            PlayerRespawnAppearing?.Invoke(p, pos, m_respawnAppearTime);
        }

        void RespawnNow(MatchPlayer p, Vector3 pos)
        {
            SpawnMatchCharacter(p, pos, true);
            PlayerRespawned?.Invoke(p);
        }

        // 出現演出が終わった人を出す
        void UpdateAppearing()
        {
            for (int i = 0; i < m_appearing.Count;)
            {
                var a = m_appearing[i];
                a.timeLeft -= Time.deltaTime;
                if (a.timeLeft > 0f) { i++; continue; }
                m_appearing.RemoveAt(i);
                if (a.player != null && a.player.State != MatchPlayer.PlayerState.Out) RespawnNow(a.player, a.pos);
            }
        }

        // 復活の出現演出中か(まだキャラクターは出ていない)
        public bool IsAppearing(MatchPlayer p)
        {
            foreach (var a in m_appearing) if (a.player == p) return true;
            return false;
        }

        void LateUpdateMatch()
        {
            ProcessHits();
            // 生き残りが1人になったあと、同じフレームで幽霊が復活していることがある(幽霊が生存者を倒した → 復活)。
            // 復活を優先し、フレームの最後にもう一度数えてから終わる
            if (m_lastOnePending && Result == null)
            {
                m_lastOnePending = false;
                if (CountLeft() <= 1) Finish(FinishReason.LastOneStanding);
            }
        }

        // 同じフレームの「やられた」と「当てた」を組にしてから、点数とお知らせを出す
        // (当てた側の DealtDamage は、やられた側の処理が終わってから届くので、フレームの最後にまとめて行う)
        void ProcessHits()
        {
            if (m_hits.Count == 0) return;
            var hits = m_hits.ToArray();
            m_hits.Clear();
            foreach (var h in hits)
            {
                if (h.head)
                {
                    if (h.attacker != null)
                    {
                        h.attacker.CountKO();
                        AddScore(h.attacker, Rules.headPoints, ScoreReason.KO);
                    }
                    else
                    {
                        // 相手なしでやられた(自分から場外に落ちた など)
                        h.victim.CountSelfOut();
                        AddScore(h.victim, Rules.selfOutPoints, ScoreReason.SelfOut);
                    }
                    PlayerKO?.Invoke(h.victim, h.attacker);
                }
                else if (h.attacker != null)
                {
                    // 相手が分からない減り方(自分で撃った・こぼれた等)は数えない
                    h.attacker.CountKnock();
                    h.victim.CountKnocked();
                    AddScore(h.attacker, Rules.knockPoints, ScoreReason.Knock);
                    AddScore(h.victim, Rules.knockedPoints, ScoreReason.Knocked);
                    PlayerKnocked?.Invoke(h.victim, h.attacker);
                }
            }
        }

        void AddScore(MatchPlayer p, int points, ScoreReason reason)
        {
            if (Mode != MatchSetup.MatchMode.Time || Result != null || points == 0) return;
            p.AddScore(points);
            Scored?.Invoke(p, points, reason);
        }

        //========================================
        // 当てた・やられた(共通)
        //========================================

        // ハンマー・弾・連鎖で生存者にダメージを与えた(このすぐ前に、やられた側の記録が入っている)
        void OnDealtDamage(DarumaCharacter attackerCharacter)
        {
            if (m_owners.TryGetValue(attackerCharacter, out var attacker)) Attribute(attacker);
        }

        // いちばん新しい「相手がまだ分からないやられ」を、当てた人のものにする
        void Attribute(MatchPlayer attacker)
        {
            for (int i = m_hits.Count - 1; i >= 0; i--)
            {
                var h = m_hits[i];
                if (h.attacker != null || h.victim == attacker) continue;
                h.attacker = attacker;
                return;
            }
        }

        // 脱落した順番。同じフレームでやられた人は同じ順番(相打ち)にして、順位を同じにする(最後の2人が相打ちなら引き分け)
        int NextOutOrder()
        {
            if (Time.frameCount != m_lastOutFrame)
            {
                m_lastOutFrame = Time.frameCount;
                m_outCount++;
            }
            return m_outCount;
        }

        // 首だけで叩かれた・頭に当たった・落ちた(この直後にキャラクターは消える)
        void OnMatchCharacterEliminated(DarumaCharacter c)
        {
            if (!m_owners.TryGetValue(c, out var p)) return;
            m_owners.Remove(c);
            if (Result != null) return;
            // ネット対戦用(suehara): 開始待ちの間は、モードにかかわらず少ししてから復活するだけ
            if (IsNetWaiting)
            {
                p.SetRespawning(m_settings.respawnDelay);
                return;
            }

            p.CountFall();
            m_hits.Add(new Hit { victim = p, head = true });
            Vector3 pos = c.transform.position;

            switch (Mode)
            {
                case MatchSetup.MatchMode.Ghost:
                    p.OutOrder = NextOutOrder();
                    SpawnMatchGhost(p, pos);
                    break;
                case MatchSetup.MatchMode.Stock:
                    p.LoseStock();
                    if (p.Stocks > 0) p.SetRespawning(m_settings.respawnDelay);
                    else
                    {
                        p.SetOut(NextOutOrder());
                        PlayerOut?.Invoke(p);
                    }
                    break;
                default:
                    p.SetRespawning(m_settings.respawnDelay);
                    break;
            }

            CheckLastOne();
        }

        void SpawnMatchGhost(MatchPlayer p, Vector3 pos)
        {
            if (GhostPrefab == null) { p.SetOut(p.OutOrder); return; }
            if (m_stageArea != null) pos = m_stageArea.Clamp(pos, 0.5f);
            var ghost = Instantiate(GhostPrefab, pos, Quaternion.identity);
            // ネット対戦用(suehara): 全員に配る
            m_net?.OnBodySpawned(ghost.gameObject);
            ghost.Init(m_stageArea);
            ghost.ReviveRequested += g =>
            {
                // 幽霊が生存者にダメージを与えた(幽霊には DealtDamage が無いので、ここで当てた人を記録する)
                Attribute(p);
                if (Result != null) return;
                // その場で復活すると、直接殴った相手(近くにいる幽霊など)にすぐ殴り返されるので、出現ポイントのどれかに出す(仮)
                Destroy(g.gameObject);
                Respawn(p, RandomSpawnPoint());
            };
            p.SetGhost(ghost);
        }

        //========================================
        // 試合の終わり(共通)
        //========================================

        // 生き残りが1人以下になったら終わり(ストック・ゴースト)
        void CheckLastOne()
        {
            if (Mode == MatchSetup.MatchMode.Time || m_players.Count < 2) return;
            if (CountLeft() <= 1) m_lastOnePending = true;   // 最後の一撃の「当てた人」と幽霊の復活が済んでから終わる(LateUpdate)
        }

        // まだ戦っている人数(ゴースト:生存者、ストック:脱落していない人)
        int CountLeft()
        {
            int left = 0;
            foreach (var p in m_players)
            {
                // 幽霊から復活する途中(出現演出中)の人は生存者として数える
                bool inGame = Mode == MatchSetup.MatchMode.Ghost ? p.IsAlive || IsAppearing(p) : p.State != MatchPlayer.PlayerState.Out;
                if (inGame) left++;
            }
            return left;
        }

        void Finish(FinishReason reason)
        {
            // ネット対戦用(suehara): 開始待ちの間は終わらない
            if (Result != null || IsNetWaiting) return;
            ProcessHits();   // このフレームの点数を先に入れる
            m_timeUpFinish = reason == FinishReason.TimeUp;

            foreach (var p in m_players) p.StopControl();
            Time.timeScale = 0f;   // その場で止める(セレクトへ戻るときに戻す)

            var result = new MatchResult { Reason = reason };
            result.Ranking.AddRange(m_players);
            result.Ranking.Sort((a, b) => RankKey(b).CompareTo(RankKey(a)));
            for (int i = 0; i < result.Ranking.Count; i++)
            {
                var p = result.Ranking[i];
                p.Rank = i > 0 && RankKey(p) == RankKey(result.Ranking[i - 1]) ? result.Ranking[i - 1].Rank : i + 1;
            }
            // 1位が1人だけなら勝者、2人以上なら引き分け
            if (result.Ranking.Count > 0 && (result.Ranking.Count == 1 || result.Ranking[1].Rank != 1)) result.Winner = result.Ranking[0];

            Result = result;
            m_finishTime = Time.unscaledTime;
            Debug.Log($"[DarumaMatch] 試合終了({(reason == FinishReason.TimeUp ? "時間切れ" : "最後の1人")}):{(result.Winner != null ? result.Winner.Label + " の勝ち" : "引き分け")}");
            Finished?.Invoke(result);
        }

        // 順位の比べ方(大きいほど上)
        double RankKey(MatchPlayer p)
        {
            bool timeUp = m_timeUpFinish;
            switch (Mode)
            {
                case MatchSetup.MatchMode.Time:
                    return p.Score;
                case MatchSetup.MatchMode.Stock:
                    // 残っている人が上(時間切れならストックが多い順。同じストックなら積み木が多い順)。脱落した人は後に脱落した人ほど上
                    if (p.State != MatchPlayer.PlayerState.Out) return 1000 + (timeUp ? p.Stocks * 100 + p.StackCount : 0);
                    return p.OutOrder;
                default:
                    // 生き残っている人が上(時間切れなら積み木が多い順)。幽霊は後にやられた人ほど上
                    if (p.IsAlive || IsAppearing(p)) return 1000 + (timeUp ? p.StackCount : 0);
                    return p.OutOrder;
            }
        }

        // 結果を見せたら、セレクトから:セレクトへ戻る / 直接再生:同じシーンをやり直す
        void UpdateResult()
        {
            if (m_leaving || !AutoReturnToSelect) return;
            float t = Time.unscaledTime - m_finishTime;
            if (t > m_settings.resultTime || (t > 1f && PressedStart()))
            {
                if (m_net != null) BeginNetMatch();   // ネット対戦用(suehara): シーンを読み直さずにもう一度
                else if (IsConfigured) BackToSelect();
                else Restart();
            }
        }

        // 同じシーンを読み直して、試合をやり直す(直接再生。参加していた人はボタンを押して参加し直す)
        public void Restart()
        {
            // ネット対戦用(suehara): シーンは全員で共有しているので読み直さず、その場でやり直す
            if (m_net != null)
            {
                BeginNetMatch();
                return;
            }
            if (m_leaving) return;
            m_leaving = true;
            BeforeRestart?.Invoke();
            Time.timeScale = 1f;
            var scene = gameObject.scene;
            if (scene.buildIndex >= 0)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(scene.buildIndex);
                return;
            }
#if UNITY_EDITOR
            // ビルド設定に登録していないシーンはエディタからだけ読み直せる
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
            Debug.LogWarning("[DarumaMatch] ビルド設定にないシーンは読み直せません");
#endif
        }

        public void BackToSelect()
        {
            if (m_leaving) return;
            m_leaving = true;
            Time.timeScale = 1f;
            Fader.Load(SelectSceneName);
        }

        //========================================
        // ネット対戦用(suehara)。ローカルの試合では使わない
        //  ・ホスト      : 試合はここでいつもどおり進める。出る人は接続した人(AddNetPlayer)。
        //                  始め方・やり直しは BeginNetMatch(シーンは読み直さない)。それまでは開始待ち(IsNetWaiting)
        //  ・クライアント: 試合を進めない。ホストから届いた人・時間・結果を NetClient〜 で入れ、お知らせを出す
        //                  (HUD・カメラ・カウントダウンの表示は、いつもどおりここを読めばよい)
        //========================================

        // ネット対戦のクライアントに届けるお知らせの種類
        internal enum NetEvent : byte { Knocked, KO, RespawnAppearing, Respawned, Out, Scored }

        // 試合のルールを決める(ホストは試合を始める前に、クライアントはホストから届いたら)
        public void SetNetRules(MatchSetup.MatchRules rules)
        {
            m_rules = rules;
            m_timeLimit = rules.TimeLimit;
        }

        // ホスト: 接続した人を参加させ、キャラクターを出す
        public void AddNetPlayer(MatchPlayer p, MatchSetup.Entry entry, IDarumaCommandSource source)
        {
            if (m_net == null || m_players.Contains(p)) return;
            p.Setup(entry, ColorFor(entry), source, Mode == MatchSetup.MatchMode.Stock ? Mathf.Max(1, Rules.stocks) : -1);
            m_players.Add(p);
            m_players.Sort((a, b) => a.Index.CompareTo(b.Index));
            SpawnMatchCharacter(p, FarthestSpawnPoint(p), false);
            Debug.Log($"[DarumaMatch] {p.Label} が接続して参加 ({m_players.Count}人)");
            PlayerJoined?.Invoke(p);
        }

        // ホスト: 切断した人を試合から外し、体を消す
        public void RemoveNetPlayer(MatchPlayer p)
        {
            if (!m_players.Remove(p)) return;
            if (p.Character != null)
            {
                m_owners.Remove(p.Character);
                Destroy(p.Character.gameObject);
            }
            if (p.Ghost != null) Destroy(p.Ghost.gameObject);
            m_appearing.RemoveAll(a => a.player == p);
            m_hits.RemoveAll(h => h.victim == p || h.attacker == p);
            Debug.Log($"[DarumaMatch] {p.Label} が切断 ({m_players.Count}人)");
            // 試合中に相手がいなくなったら、残った人の勝ちで終わる(タイムは時間まで続ける)
            if (!IsNetWaiting && Mode != MatchSetup.MatchMode.Time && m_players.Count == 1) m_lastOnePending = true;
            else CheckLastOne();
        }

        // ホスト: 試合を始める(やり直す)。全員を最初の状態に戻して出現ポイントに出し直し、カウントダウンから始める
        public void BeginNetMatch()
        {
            if (m_net == null || !m_net.IsServer) return;
            Time.timeScale = 1f;
            ClearBodies();
            foreach (var block in FindObjectsByType<TumikiBlock>()) Destroy(block.gameObject);

            Result = null;
            Elapsed = 0f;
            m_outCount = 0;
            m_lastOutFrame = -1;
            m_lastOnePending = false;
            m_timeUpFinish = false;
            m_leaving = false;
            m_timeLimit = Rules.TimeLimit;
            int stocks = Mode == MatchSetup.MatchMode.Stock ? Mathf.Max(1, Rules.stocks) : -1;
            foreach (var p in m_players) p.ResetForMatch(stocks);

            m_netStarted = true;
            SpawnAllAtStart();
            BeginCountdown();
            Debug.Log($"[DarumaMatch] ネット対戦の試合開始 ({m_players.Count}人 / {Rules.ModeName})");
            Started?.Invoke();
        }

        // クライアント: 人が増えた・減った
        internal void NetClientAddPlayer(MatchPlayer p)
        {
            if (m_players.Contains(p)) return;
            m_players.Add(p);
            m_players.Sort((a, b) => a.Index.CompareTo(b.Index));
            PlayerJoined?.Invoke(p);
        }

        internal void NetClientRemovePlayer(MatchPlayer p) => m_players.Remove(p);

        // クライアント: 時間とカウントダウン
        internal void NetClientSetClock(bool started, float elapsed, float countdownLeft, float countdownLength)
        {
            bool wasCounting = IsCountingDown;
            m_netStarted = started;
            Elapsed = elapsed;
            m_countdownLeft = countdownLeft;
            m_countdownLength = countdownLength;
            if (wasCounting && !IsCountingDown) CountdownFinished?.Invoke();
        }

        // クライアント: 試合が始まった(やり直しを含む)
        internal void NetClientStarted()
        {
            Result = null;
            Started?.Invoke();
        }

        // クライアント: 試合が終わった(順位は result に入れて届く)
        internal void NetClientFinish(MatchResult result)
        {
            Result = result;
            m_finishTime = Time.unscaledTime;
            Finished?.Invoke(result);
        }

        // クライアント: ホストで出たお知らせを、同じように出す
        internal void NetClientRaise(NetEvent e, MatchPlayer a, MatchPlayer b, Vector3 pos, float seconds, int points, ScoreReason reason)
        {
            switch (e)
            {
                case NetEvent.Knocked: PlayerKnocked?.Invoke(a, b); break;
                case NetEvent.KO: PlayerKO?.Invoke(a, b); break;
                case NetEvent.RespawnAppearing: PlayerRespawnAppearing?.Invoke(a, pos, seconds); break;
                case NetEvent.Respawned: PlayerRespawned?.Invoke(a); break;
                case NetEvent.Out: PlayerOut?.Invoke(a); break;
                case NetEvent.Scored: Scored?.Invoke(a, points, reason); break;
            }
        }

        //========================================
        // 出現ポイント(共通)
        //========================================

        // 最初は 1P から順に出現ポイント 1, 2, … に出す
        Vector3 InitialSpawnPoint(int order)
        {
            if (m_spawnPoints == null || m_spawnPoints.Length == 0) return Vector3.zero;
            return m_spawnPoints[order % m_spawnPoints.Length].position;
        }

        // 幽霊からの復活:出現ポイントからランダムに選ぶ
        Vector3 RandomSpawnPoint()
        {
            if (m_spawnPoints == null || m_spawnPoints.Length == 0) return Vector3.zero;
            return m_spawnPoints[UnityEngine.Random.Range(0, m_spawnPoints.Length)].position;
        }

        // 復活:ほかのキャラクター(生存者・幽霊・出現演出中の位置)すべてから一番遠い出現ポイント
        Vector3 FarthestSpawnPoint(MatchPlayer self)
        {
            if (m_spawnPoints == null || m_spawnPoints.Length == 0) return Vector3.zero;
            Vector3 best = m_spawnPoints[0].position;
            float bestScore = float.MinValue;
            foreach (var point in m_spawnPoints)
            {
                float nearest = float.MaxValue;
                foreach (var p in m_players)
                {
                    if (p == self || p.Body == null) continue;
                    nearest = Mathf.Min(nearest, Vector3.Distance(point.position, p.Body.position));
                }
                foreach (var a in m_appearing)
                {
                    if (a.player != self) nearest = Mathf.Min(nearest, Vector3.Distance(point.position, a.pos));
                }
                if (nearest > bestScore) { bestScore = nearest; best = point.position; }
            }
            return best;
        }

        //========================================
        // 入力
        //========================================

        static bool PressedStart()
        {
            if (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)) return true;
            foreach (var g in Gamepad.all) if (g.startButton.wasPressedThisFrame || g.buttonSouth.wasPressedThisFrame) return true;
            return false;
        }

        static bool PressedBack()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
            foreach (var g in Gamepad.all) if (g.selectButton.wasPressedThisFrame) return true;
            return false;
        }
    }
}
