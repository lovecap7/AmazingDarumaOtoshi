using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

// ネット対戦(LAN)の接続画面。NetStages に置く(テスト用の NetTestUI の置き換え)。
//  ・つないでいないとき: 画面の中央に「ホストになる」と「参加する(ホストの IP アドレスを入れる)」
//  ・ホスト  : 自分の IP アドレス(参加する人に伝える)・人数と、試合のルールを選んで「試合開始」(試合中・試合後は「やり直し」)
//  ・参加する: つながるまでの表示。つながらなかった・切れたときは理由を出す
//  ・切断したら(自分で切った・ホストがいなくなった・つながらなかった)シーンを読み直して最初の状態に戻す
// コマンドライン: -nethost でホストとして、-netclient <IPアドレス> で参加して始める(ビルドでの試験用)
public class NetLobbyUI : MonoBehaviour
{
    const string kAddressKey = "NetLobby.Address";
    // 時間制限の選択肢(秒。0 = なし。タイムはなしを選べないので 120 秒)
    static readonly float[] kTimeLimits = { 0.0f, 60.0f, 120.0f, 180.0f };

    [SerializeField] ushort m_port = 7777;
    // 参加するとき、1秒ごとに試す回数(この回数でつながらなければあきらめる)
    [SerializeField, Min(1)] int m_connectAttempts = 10;

    // 前回の切断の理由(シーンを読み直しても出す)
    static string s_message;

    NetworkManager m_nm;
    string m_address;
    string[] m_localAddresses = Array.Empty<string>();
    bool m_wasConnected;
    bool m_reload;

    MatchSetup.MatchMode m_mode = MatchSetup.MatchMode.Stock;
    int m_stocks = 3;
    int m_timeIndex;

    UnityTransport Transport => m_nm != null ? m_nm.GetComponent<UnityTransport>() : null;

    void Start()
    {
        m_nm = NetworkManager.Singleton;
        m_address = PlayerPrefs.GetString(kAddressKey, "127.0.0.1");
        m_localAddresses = LocalAddresses();
        if (m_nm == null) return;
        m_nm.OnClientConnectedCallback += OnConnected;
        m_nm.OnClientDisconnectCallback += OnDisconnected;
        m_nm.OnClientStopped += OnStopped;
        m_nm.OnServerStopped += OnStopped;
        StartFromCommandLine();
    }

    void OnDestroy()
    {
        if (m_nm == null) return;
        m_nm.OnClientConnectedCallback -= OnConnected;
        m_nm.OnClientDisconnectCallback -= OnDisconnected;
        m_nm.OnClientStopped -= OnStopped;
        m_nm.OnServerStopped -= OnStopped;
    }

    void Update()
    {
        if (m_reload) Reload();
    }

    //========================================
    // つなぐ・切る
    //========================================

    void StartHost()
    {
        s_message = null;
        // 同じ LAN の他の PC から来られるように、すべてのネットワークで待つ
        Transport.SetConnectionData("127.0.0.1", m_port, "0.0.0.0");
        if (!m_nm.StartHost()) s_message = $"ホストを始められませんでした(ポート {m_port} を他のアプリが使っているかもしれません)";
    }

    void StartClient(string address)
    {
        address = address.Trim();
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            s_message = "IP アドレスの形が正しくありません(例: 192.168.1.10)";
            return;
        }
        s_message = null;
        m_address = address;
        PlayerPrefs.SetString(kAddressKey, address);
        Transport.SetConnectionData(address, m_port);
        Transport.MaxConnectAttempts = m_connectAttempts;
        if (!m_nm.StartClient()) s_message = "参加を始められませんでした";
    }

    void OnConnected(ulong clientId)
    {
        if (!m_nm.IsServer && clientId == m_nm.LocalClientId) m_wasConnected = true;
    }

    // クライアント: ホストから切れた(つながらなかったときも来る)
    void OnDisconnected(ulong clientId)
    {
        if (m_nm.IsServer) return;
        if (!m_wasConnected)
        {
            s_message = $"{m_address} につながりませんでした。ホストの IP アドレス・同じ LAN(Wi-Fi)にいるか・ファイアウォールを確かめてください";
            return;
        }
        // ホストが理由を付けて切ったとき(定員など)はそれも出す
        string reason = m_nm.DisconnectReason;
        s_message = "ホストとの接続が切れました";
        if (!string.IsNullOrEmpty(reason)) s_message += $"\n({reason})";
    }

    // 止まったら、次のフレームでシーンを読み直して最初の状態に戻す(試合・積み木・キャラクターを残さない)
    void OnStopped(bool wasHost) => m_reload = true;

    void Reload()
    {
        m_reload = false;
        Time.timeScale = 1.0f;
        // NetworkManager がシーンの外(DontDestroyOnLoad)に移っていたら、読み直したシーンのものと二重にならないように消す
        if (m_nm != null && m_nm.gameObject.scene != gameObject.scene) Destroy(m_nm.gameObject);
        SceneManager.LoadScene(gameObject.scene.name);
    }

    void StartFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-nethost") StartHost();
            else if (args[i] == "-netclient" && i + 1 < args.Length) StartClient(args[i + 1]);
        }
    }

    // この PC の LAN の IP アドレス(参加する人に伝える)
    static string[] LocalAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var a in ni.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(a.Address.ToString());
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[NetLobbyUI] IP アドレスを調べられませんでした: " + e.Message);
        }
        return list.ToArray();
    }

    //========================================
    // 画面
    //========================================

    void OnGUI()
    {
        if (m_nm == null) return;
        // 1920x1080 を基準に、画面の大きさに合わせて拡大する
        float scale = Mathf.Max(0.5f, Screen.height / 1080.0f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1.0f));
        float width = Screen.width / scale, height = Screen.height / scale;

        var button = new GUIStyle(GUI.skin.button) { fontSize = 26 };
        var label = new GUIStyle(GUI.skin.label) { fontSize = 24, wordWrap = true };
        var field = new GUIStyle(GUI.skin.textField) { fontSize = 30 };

        if (!m_nm.IsClient && !m_nm.IsServer) ConnectPanel(width, height, button, label, field);
        else SessionPanel(button, label);
    }

    // つないでいないとき: 画面の中央
    void ConnectPanel(float width, float height, GUIStyle button, GUIStyle label, GUIStyle field)
    {
        var rect = new Rect((width - 640.0f) * 0.5f, (height - 520.0f) * 0.5f, 640.0f, 520.0f);
        GUI.Box(rect, "");
        GUILayout.BeginArea(new Rect(rect.x + 24, rect.y + 20, rect.width - 48, rect.height - 40));
        GUILayout.Label("ネット対戦(LAN)", new GUIStyle(label) { fontSize = 34, fontStyle = FontStyle.Bold });
        GUILayout.Space(10);
        if (GUILayout.Button("ホストになる", button, GUILayout.Height(64))) StartHost();
        GUILayout.Space(20);
        GUILayout.Label("参加する: ホストの画面に出ている IP アドレス", label);
        GUILayout.BeginHorizontal();
        m_address = GUILayout.TextField(m_address, 15, field, GUILayout.Height(52));
        if (GUILayout.Button("参加", button, GUILayout.Width(140), GUILayout.Height(52))) StartClient(m_address);
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(s_message))
        {
            GUILayout.Space(16);
            GUILayout.Label(s_message, new GUIStyle(label) { normal = { textColor = new Color(1.0f, 0.75f, 0.4f) } });
        }
        GUILayout.EndArea();
    }

    // つないでいるとき: 左上(ステージ名の表示の下)
    void SessionPanel(GUIStyle button, GUIStyle label)
    {
        GUILayout.BeginArea(new Rect(10, 60, 420, 700));
        GUILayout.BeginVertical(GUI.skin.box);
        if (m_nm.IsServer)
        {
            GUILayout.Label($"ホスト  {m_nm.ConnectedClientsIds.Count}人", label);
            GUILayout.Label(m_localAddresses.Length > 0 ? "IP: " + string.Join(" / ", m_localAddresses) : "IP: (見つかりません)", label);
        }
        else
        {
            GUILayout.Label(m_nm.IsConnectedClient ? $"参加中  {m_address}" : $"{m_address} につないでいます…", label);
        }
        MatchGUI(button, label);
        if (GUILayout.Button(m_nm.IsServer ? "ホストをやめる" : "抜ける", button, GUILayout.Height(44))) m_nm.Shutdown();
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void MatchGUI(GUIStyle button, GUIStyle label)
    {
        var net = NetMatch.Instance;
        if (net == null || !net.IsSpawned || net.Match == null) return;

        string phase = !net.IsStarted ? "開始待ち" : net.Match.IsFinished ? "試合終了" : "試合中";
        GUILayout.Label($"{phase}  {net.Rules.ModeName}  {net.PlayerCount}人", label);
        if (!m_nm.IsServer) return;

        if (GUILayout.Button("モード: " + ModeName(m_mode), button)) m_mode = (MatchSetup.MatchMode)(((int)m_mode + 1) % 3);
        if (m_mode == MatchSetup.MatchMode.Stock && GUILayout.Button($"ストック: {m_stocks}", button)) m_stocks = m_stocks % 5 + 1;
        if (GUILayout.Button("時間: " + TimeLabel(TimeLimit), button)) m_timeIndex = (m_timeIndex + 1) % kTimeLimits.Length;
        if (GUILayout.Button(net.IsStarted ? "やり直し" : "試合開始", button, GUILayout.Height(56))) net.StartMatch(BuildRules());
        GUILayout.Space(8);
    }

    // タイムは時間制限なしを選べない
    float TimeLimit => m_mode == MatchSetup.MatchMode.Time && kTimeLimits[m_timeIndex] <= 0.0f ? 120.0f : kTimeLimits[m_timeIndex];

    MatchSetup.MatchRules BuildRules() => new MatchSetup.MatchRules
    {
        mode = m_mode,
        stocks = m_stocks,
        stockTimeLimit = TimeLimit,
        timeLength = TimeLimit,
        ghostTimeLimit = TimeLimit,
    };

    static string ModeName(MatchSetup.MatchMode mode) => new MatchSetup.MatchRules { mode = mode }.ModeName;
    static string TimeLabel(float seconds) => seconds > 0.0f ? $"{seconds:0}秒" : "なし";
}
