using System;
using System.Collections;
using System.Collections.Generic;
using Nakahira;
using Unity.Netcode;
using UnityEngine;

// ネット対戦の試合をまとめる。シーンに置いた NetworkObject に付ける(NetStages の NetMatch)。
//  ・接続したら、シーンの Match(最初は非表示の DarumaMatch)に NetMatchDriver を付けて表示する
//  ・ホスト      : 接続した人ごとに NetPlayer を出す(1人分は NetMatchPlayer)。ルール・時間・試合の始まりと終わり・お知らせを配る
//  ・クライアント: 届いたルール・時間・結果・お知らせを DarumaMatch に入れる(HUD・カメラなどはいつもどおり DarumaMatch を読む)
// 試合を始めるのはホスト(StartMatch。NetSessionMenu の「試合を はじめる」)。開始待ちの間に選んだルールは SetRules で配る。それまでは開始待ちで、自由に動ける
// 試合が終わったらローカル(MatchResultRelay)と同じく、結果を ResultData に入れて全員でリザルト画面へ移り、
// ホストの決定でこのシーンに戻る(NetSceneFlow / NetResultFlow)。シーンを読み直すので、次の試合はまた開始待ちから
public class NetMatch : NetworkBehaviour
{
    public struct RulesData : INetworkSerializable, IEquatable<RulesData>
    {
        public byte Mode, Stocks;
        public float StockTime, TimeLength, GhostTime;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Mode);
            s.SerializeValue(ref Stocks);
            s.SerializeValue(ref StockTime);
            s.SerializeValue(ref TimeLength);
            s.SerializeValue(ref GhostTime);
        }

        public bool Equals(RulesData o) => Mode == o.Mode && Stocks == o.Stocks && StockTime == o.StockTime && TimeLength == o.TimeLength && GhostTime == o.GhostTime;
    }

    public struct ClockData : INetworkSerializable, IEquatable<ClockData>
    {
        public bool Started;
        public float Elapsed, CountdownLeft, CountdownLength;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Started);
            s.SerializeValue(ref Elapsed);
            s.SerializeValue(ref CountdownLeft);
            s.SerializeValue(ref CountdownLength);
        }

        public bool Equals(ClockData o) => Started == o.Started && Elapsed == o.Elapsed && CountdownLeft == o.CountdownLeft && CountdownLength == o.CountdownLength;
    }

    // 届いた時間と自分で進めた時間がこれ以上ずれたら合わせる(秒)
    const float kClockSnap = 0.15f;
    // 時間を送る細かさ(秒)。間はクライアントが自分で進める
    const float kClockStep = 0.05f;

    // シーンの Match(最初は非表示にしておく)。未設定ならシーンから探す
    [SerializeField] DarumaMatch m_match;
    // 接続した人ごとに出す1人分(NetPlayer)
    [SerializeField] NetworkObject m_playerPrefab;
    [SerializeField, Min(1)] int m_maxPlayers = 4;

    readonly NetworkVariable<RulesData> m_rules = new NetworkVariable<RulesData>();
    readonly NetworkVariable<ClockData> m_clock = new NetworkVariable<ClockData>();
    // ホスト: 接続した人ごとの1人分
    readonly Dictionary<ulong, NetMatchPlayer> m_players = new Dictionary<ulong, NetMatchPlayer>();
    // クライアント: 届いた時間の間を自分で進めた値
    float m_elapsed, m_countdown;

    public static NetMatch Instance { get; private set; }
    public DarumaMatch Match => m_match;
    // ホストが試合を始めたか(false なら開始待ち)
    public bool IsStarted => m_clock.Value.Started;
    public MatchSetup.MatchRules Rules => ToRules(m_rules.Value);
    public RulesData RulesValue => m_rules.Value;
    public int PlayerCount => m_match != null ? m_match.Players.Count : 0;

    void Awake()
    {
        Instance = this;
        if (m_match == null) m_match = FindFirstObjectByType<DarumaMatch>(FindObjectsInactive.Include);
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        if (m_match == null)
        {
            Debug.LogError("[NetMatch] DarumaMatch が見つかりません");
            return;
        }

        // リザルトのあと・切断したときに戻るシーン
        NetSceneFlow.ReturnScene = gameObject.scene.name;

        // DarumaMatch は Awake でこれを探すので、表示する前に付ける
        var driver = m_match.GetComponent<NetMatchDriver>();
        if (driver == null) driver = m_match.gameObject.AddComponent<NetMatchDriver>();
        driver.IsServer = IsServer;
        m_match.gameObject.SetActive(true);
        AttachMatchExtras();

        if (IsServer)
        {
            m_rules.Value = FromRules(m_match.Rules);
            m_match.Started += OnStarted;
            m_match.Finished += OnFinished;
            m_match.PlayerKnocked += OnKnocked;
            m_match.PlayerKO += OnKO;
            m_match.PlayerRespawnAppearing += OnRespawnAppearing;
            m_match.PlayerRespawned += OnRespawned;
            m_match.PlayerOut += OnOut;
            m_match.Scored += OnScored;

            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            if (NetworkManager.SceneManager != null) NetworkManager.SceneManager.OnLoadComplete += OnClientLoaded;
            // つながったままこのシーンへ移ってきたとき(接続画面・リザルトから)、ほかの人はまだシーンを読んでいる途中。
            // いま1人分(NetPlayer)を出すと、その人の端末では読み込み中の前のシーンと一緒に消えて操作できなくなる。
            // なので自分(ホスト)の分だけすぐ出し、ほかの人は読み終えたとき(OnClientLoaded)に出す
            foreach (var id in NetworkManager.ConnectedClientsIds)
            {
                if (id == NetworkManager.LocalClientId) OnClientConnected(id);
            }
            return;
        }

        m_match.SetNetRules(ToRules(m_rules.Value));
        m_rules.OnValueChanged += OnRulesChanged;
    }

    // 中川さんの演出(輪郭・ハンマーの火花・復活の出現・開始の演出・王冠)は、シーンを読んだときに表示中の試合へ自動で付く。
    // この Match は接続するまで非表示なので付かない。表示したあとに、ローカルと同じものを付ける
    // (王冠・開始の演出は、ネット対戦では自分で止まる設定になっている)
    void AttachMatchExtras()
    {
        var go = m_match.gameObject;
        if (Nakagawa.PlayerOutlines.AutoAttach && go.GetComponent<Nakagawa.PlayerOutlines>() == null) go.AddComponent<Nakagawa.PlayerOutlines>();
        if (Nakagawa.HammerHitEffect.AutoAttach && go.GetComponent<Nakagawa.HammerHitEffect>() == null) go.AddComponent<Nakagawa.HammerHitEffect>();
        if (Nakagawa.RespawnAppearEffect.AutoAttach && go.GetComponent<Nakagawa.RespawnAppearEffect>() == null) go.AddComponent<Nakagawa.RespawnAppearEffect>();
        if (Nakagawa.StartCountdownCinematic.AutoAttach && go.GetComponent<Nakagawa.StartCountdownCinematic>() == null) go.AddComponent<Nakagawa.StartCountdownCinematic>();
        if (Nakagawa.MatchCrown.AutoAttach && FindAnyObjectByType<Nakagawa.MatchCrown>() == null) go.AddComponent<Nakagawa.MatchCrown>();
    }

    public override void OnNetworkDespawn()
    {
        m_rules.OnValueChanged -= OnRulesChanged;
        if (NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            if (NetworkManager.SceneManager != null) NetworkManager.SceneManager.OnLoadComplete -= OnClientLoaded;
        }
        m_players.Clear();
        if (m_match == null) return;
        m_match.Started -= OnStarted;
        m_match.Finished -= OnFinished;
        m_match.PlayerKnocked -= OnKnocked;
        m_match.PlayerKO -= OnKO;
        m_match.PlayerRespawnAppearing -= OnRespawnAppearing;
        m_match.PlayerRespawned -= OnRespawned;
        m_match.PlayerOut -= OnOut;
        m_match.Scored -= OnScored;
    }

    // ホスト: このルールで試合を始める(試合中・試合後ならやり直す)
    public void StartMatch(MatchSetup.MatchRules rules)
    {
        if (!IsServer || m_match == null) return;
        m_rules.Value = FromRules(rules);
        m_match.SetNetRules(rules);
        m_match.BeginNetMatch();
    }

    // ホスト: 開始待ちの間に選んでいるルールを配る(さんかした人の表示に出す。試合はまだ始めない)
    public void SetRules(MatchSetup.MatchRules rules)
    {
        if (!IsServer || m_match == null || IsStarted) return;
        m_rules.Value = FromRules(rules);
        m_match.SetNetRules(rules);
    }

    private void Update()
    {
        if (!IsSpawned || m_match == null) return;
        if (IsServer) ServerUpdateClock();
        else ClientUpdateClock();
    }

    //========================================
    // 接続した人
    //========================================

    void OnClientConnected(ulong clientId)
    {
        if (!IsServer || m_players.ContainsKey(clientId)) return;
        if (m_players.Count >= m_maxPlayers)
        {
            Debug.LogWarning($"[NetMatch] 定員({m_maxPlayers}人)なので、クライアント {clientId} は見るだけになります");
            return;
        }
        if (m_playerPrefab == null)
        {
            Debug.LogError("[NetMatch] Player Prefab(NetPlayer)が未設定です");
            return;
        }

        NetworkObject obj = Instantiate(m_playerPrefab);
        var player = obj.GetComponent<NetMatchPlayer>();
        player.Index = FreeIndex();
        m_players[clientId] = player;
        obj.SpawnAsPlayerObject(clientId, true);
    }

    // ほかの人がこのシーンを読み終えた(接続画面・リザルトから一緒に移ってきたとき)
    void OnClientLoaded(ulong clientId, string sceneName, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (sceneName != gameObject.scene.name) return;
        if (!NetworkManager.ConnectedClients.ContainsKey(clientId)) return;
        OnClientConnected(clientId);
    }

    // 1人分(NetPlayer)は NGO が消し、そのとき NetMatchPlayer が試合から外す
    void OnClientDisconnected(ulong clientId) => m_players.Remove(clientId);

    // 空いている一番小さい番号(抜けた人の番号は次に来た人が使う)
    int FreeIndex()
    {
        for (int i = 0; ; i++)
        {
            bool used = false;
            foreach (var p in m_players.Values) if (p != null && p.Index == i) used = true;
            if (!used) return i;
        }
    }

    //========================================
    // ルール・時間
    //========================================

    void OnRulesChanged(RulesData prev, RulesData next) => m_match.SetNetRules(ToRules(next));

    void ServerUpdateClock()
    {
        var c = new ClockData
        {
            Started = !m_match.IsNetWaiting,
            Elapsed = Step(m_match.Elapsed),
            CountdownLeft = Step(m_match.CountdownLeft),
            CountdownLength = m_match.CountdownLength,
        };
        if (!m_clock.Value.Equals(c)) m_clock.Value = c;
    }

    static float Step(float t) => Mathf.Ceil(t / kClockStep) * kClockStep;

    // 届いた時間の間は自分で進め、大きくずれたら合わせる(届くのはとびとびなので、そのまま使うとカウントダウンがカクつく)
    void ClientUpdateClock()
    {
        var c = m_clock.Value;
        float dt = Time.deltaTime;
        if (m_countdown > 0.0f) m_countdown = Mathf.Max(0.0f, m_countdown - dt);
        else if (c.Started && !m_match.IsFinished) m_elapsed += dt;
        if (Mathf.Abs(c.CountdownLeft - m_countdown) > kClockSnap) m_countdown = c.CountdownLeft;
        if (Mathf.Abs(c.Elapsed - m_elapsed) > kClockSnap) m_elapsed = c.Elapsed;
        m_match.NetClientSetClock(c.Started, m_elapsed, m_countdown, c.CountdownLength);
    }

    public static RulesData FromRules(MatchSetup.MatchRules r) => new RulesData
    {
        Mode = (byte)r.mode,
        Stocks = (byte)Mathf.Clamp(r.stocks, 1, 99),
        StockTime = r.stockTimeLimit,
        TimeLength = r.timeLength,
        GhostTime = r.ghostTimeLimit,
    };

    static MatchSetup.MatchRules ToRules(RulesData d) => new MatchSetup.MatchRules
    {
        mode = (MatchSetup.MatchMode)d.Mode,
        stocks = Mathf.Max(1, (int)d.Stocks),
        stockTimeLimit = d.StockTime,
        timeLength = d.TimeLength > 0.0f ? d.TimeLength : 120.0f,
        ghostTimeLimit = d.GhostTime,
    };

    //========================================
    // 試合の始まり・終わり・お知らせ(ホスト → クライアント)
    //========================================

    void OnStarted() => StartedRpc();

    [Rpc(SendTo.NotServer)]
    void StartedRpc()
    {
        if (m_match != null) m_match.NetClientStarted();
    }

    void OnFinished(DarumaMatch.MatchResult result)
    {
        int n = result.Ranking.Count;
        var order = new byte[n];
        var ranks = new byte[n];
        // 積み木の色(リザルトのタワーに使う。クライアントは積み木を持っていないのでホストのものを送る)
        var counts = new byte[n];
        var colors = new List<byte>();
        for (int i = 0; i < n; i++)
        {
            var p = result.Ranking[i];
            order[i] = (byte)p.Index;
            ranks[i] = (byte)p.Rank;
            var stack = p.Stack;
            int count = stack != null ? Mathf.Min(stack.Count, byte.MaxValue) : 0;
            counts[i] = (byte)count;
            for (int b = 0; b < count; b++) colors.Add((byte)stack.Get(b).Color);
        }
        FinishedRpc((byte)result.Reason, result.Winner != null ? (sbyte)result.Winner.Index : (sbyte)-1, order, ranks, counts, colors.ToArray());

        // DarumaMatch が自分でシーンを読み直さないようにする(リザルト画面が元に戻す)
        DarumaMatch.AutoReturnToSelect = false;
        // ステージの切り替え(左右キー)も、試合が終わったあとはシーンを読み直してしまうので止める
        var switcher = FindAnyObjectByType<Nakagawa.StageSwitcher>();
        if (switcher != null) switcher.enabled = false;
        ResultData.Capture(result, m_match.Mode);
        StartCoroutine(GoToResult(switcher));
    }

    [Rpc(SendTo.NotServer)]
    void FinishedRpc(byte reason, sbyte winner, byte[] order, byte[] ranks, byte[] counts, byte[] colors)
    {
        if (m_match == null) return;
        var result = new DarumaMatch.MatchResult { Reason = (DarumaMatch.FinishReason)reason };
        var blockColors = new Dictionary<MatchPlayer, List<Color>>();
        int next = 0;
        for (int i = 0; i < order.Length; i++)
        {
            var list = new List<Color>();
            for (int b = 0; b < counts[i] && next < colors.Length; b++) list.Add(TumikiColorUtil.ToUnityColor((TumikiColor)colors[next++]));
            var p = FindPlayer(order[i]);
            if (p == null) continue;
            p.Rank = ranks[i];
            result.Ranking.Add(p);
            blockColors[p] = list;
            if (order[i] == winner) result.Winner = p;
        }
        m_match.NetClientFinish(result);

        // ResultData.Ranking は result.Ranking と同じ順(null は入らない)
        ResultData.Capture(result, m_match.Mode);
        for (int i = 0; i < result.Ranking.Count && i < ResultData.Ranking.Count; i++)
        {
            ResultData.Ranking[i].blockColors.Clear();
            ResultData.Ranking[i].blockColors.AddRange(blockColors[result.Ranking[i]]);
        }
        DarumaMatch.AutoReturnToSelect = false;
    }

    // ホスト: 「しあい しゅうりょう！」を見せてから、全員でリザルト画面へ(MatchResultRelay と同じ待ち時間)
    // ネット対戦では MatchSettings の「リザルト(表彰)」を使わない設定でも、リザルト画面を通って戻る(その場でやり直す作りはない)
    IEnumerator GoToResult(Nakagawa.StageSwitcher switcher)
    {
        var settings = MatchSettings.Instance;
        yield return new WaitForSecondsRealtime(settings != null ? settings.resultSceneDelay : 2.0f);
        // 戻ってきたら同じステージから始める(クライアントはホストのステージに合わせる)
        if (switcher != null && switcher.Current != null && switcher.Current.Info != null) Nakagawa.StageSwitcher.RequestStage(switcher.Current.Info.Id);
        NetSceneFlow.Load(settings != null ? settings.ResultSceneFor(m_match.Mode) : "ResultTowerScene");
    }

    void OnKnocked(MatchPlayer victim, MatchPlayer attacker) => Send(DarumaMatch.NetEvent.Knocked, victim, attacker);
    void OnKO(MatchPlayer victim, MatchPlayer attacker) => Send(DarumaMatch.NetEvent.KO, victim, attacker);
    void OnRespawnAppearing(MatchPlayer p, Vector3 pos, float seconds) => Send(DarumaMatch.NetEvent.RespawnAppearing, p, null, pos, seconds);
    void OnRespawned(MatchPlayer p) => Send(DarumaMatch.NetEvent.Respawned, p, null);
    void OnOut(MatchPlayer p) => Send(DarumaMatch.NetEvent.Out, p, null);
    void OnScored(MatchPlayer p, int points, DarumaMatch.ScoreReason reason) => Send(DarumaMatch.NetEvent.Scored, p, null, default, 0.0f, points, reason);

    void Send(DarumaMatch.NetEvent e, MatchPlayer a, MatchPlayer b, Vector3 pos = default, float seconds = 0.0f, int points = 0, DarumaMatch.ScoreReason reason = default)
    {
        EventRpc((byte)e, IndexOf(a), IndexOf(b), pos, seconds, points, (byte)reason);
    }

    static sbyte IndexOf(MatchPlayer p) => p != null ? (sbyte)p.Index : (sbyte)-1;

    [Rpc(SendTo.NotServer)]
    void EventRpc(byte e, sbyte a, sbyte b, Vector3 pos, float seconds, int points, byte reason)
    {
        if (m_match == null) return;
        var pa = FindPlayer(a);
        if (pa == null) return;
        m_match.NetClientRaise((DarumaMatch.NetEvent)e, pa, FindPlayer(b), pos, seconds, points, (DarumaMatch.ScoreReason)reason);
    }

    MatchPlayer FindPlayer(int index)
    {
        if (index < 0 || m_match == null) return null;
        foreach (var p in m_match.Players) if (p != null && p.Index == index) return p;
        return null;
    }
}
