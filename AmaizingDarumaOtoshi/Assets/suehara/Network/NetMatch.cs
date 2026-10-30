using System;
using System.Collections.Generic;
using Nakahira;
using Unity.Netcode;
using UnityEngine;

// ネット対戦の試合をまとめる。シーンに置いた NetworkObject に付ける(NetStages の NetMatch)。
//  ・接続したら、シーンの Match(最初は非表示の DarumaMatch)に NetMatchDriver を付けて表示する
//  ・ホスト      : 接続した人ごとに NetPlayer を出す(1人分は NetMatchPlayer)。ルール・時間・試合の始まりと終わり・お知らせを配る
//  ・クライアント: 届いたルール・時間・結果・お知らせを DarumaMatch に入れる(HUD・カメラなどはいつもどおり DarumaMatch を読む)
// 試合を始める・やり直すのはホスト(StartMatch。テスト用 UI の「試合開始」)。それまでは開始待ちで、自由に動ける
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

        // DarumaMatch は Awake でこれを探すので、表示する前に付ける
        var driver = m_match.GetComponent<NetMatchDriver>();
        if (driver == null) driver = m_match.gameObject.AddComponent<NetMatchDriver>();
        driver.IsServer = IsServer;
        m_match.gameObject.SetActive(true);

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
            foreach (var id in NetworkManager.ConnectedClientsIds) OnClientConnected(id);
            return;
        }

        m_match.SetNetRules(ToRules(m_rules.Value));
        m_rules.OnValueChanged += OnRulesChanged;
    }

    public override void OnNetworkDespawn()
    {
        m_rules.OnValueChanged -= OnRulesChanged;
        if (NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
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

    static RulesData FromRules(MatchSetup.MatchRules r) => new RulesData
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
        for (int i = 0; i < n; i++)
        {
            order[i] = (byte)result.Ranking[i].Index;
            ranks[i] = (byte)result.Ranking[i].Rank;
        }
        FinishedRpc((byte)result.Reason, result.Winner != null ? (sbyte)result.Winner.Index : (sbyte)-1, order, ranks);
    }

    [Rpc(SendTo.NotServer)]
    void FinishedRpc(byte reason, sbyte winner, byte[] order, byte[] ranks)
    {
        if (m_match == null) return;
        var result = new DarumaMatch.MatchResult { Reason = (DarumaMatch.FinishReason)reason };
        for (int i = 0; i < order.Length; i++)
        {
            var p = FindPlayer(order[i]);
            if (p == null) continue;
            p.Rank = ranks[i];
            result.Ranking.Add(p);
            if (order[i] == winner) result.Winner = p;
        }
        m_match.NetClientFinish(result);
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
