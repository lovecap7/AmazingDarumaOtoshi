using System;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

// ネット対戦(LAN)の試合のシーン(NetStages)の接続まわり。
//  ・つないでいないとき: 画面の中央に「ホストになる」と「参加する(ホストの IP アドレスを入れる)」(NetStages を直接開いたときの試験用)
//  ・つないでいる間の表示とメニュー(試合の設定・開始・かいさん)は NetSessionMenu(ここで自動で付ける)
//    (試合が終わったら全員でリザルト画面へ移り、ホストの決定でこのシーンに戻って開始待ちになる)
//  ・参加する: つながらなかった・切れたときは理由を出す
//  ・切断したら(自分で切った・ホストがいなくなった・つながらなかった)シーンを読み直して最初の状態に戻す
//    (メニューから接続画面 NetConnectScene を通って来たときは、接続画面に戻る。この画面の接続パネルは NetStages を直接開いたときの試験用)
// コマンドライン: -nethost でホストとして、-netclient <IPアドレス> で参加して始める(ビルドでの試験用)
public class NetLobbyUI : MonoBehaviour
{
    const string kAddressKey = "NetLobby.Address";

    [SerializeField] ushort m_port = 7777;
    // 参加するとき、1秒ごとに試す回数(この回数でつながらなければあきらめる)
    [SerializeField, Min(1)] int m_connectAttempts = 10;

    // 前回の切断の理由(シーンを読み直しても出す)
    static string s_message;

    NetworkManager m_nm;
    string m_address;
    bool m_wasConnected;
    bool m_reload;

    UnityTransport Transport => m_nm != null ? m_nm.GetComponent<UnityTransport>() : null;

    // 前回の切断の理由を出す(NetSceneFlow がリザルト画面で切れたときに使う)
    public static void SetMessage(string message) => s_message = message;

    void Start()
    {
        if (GetComponent<NetSessionMenu>() == null) gameObject.AddComponent<NetSessionMenu>();
        if (GetComponent<NetLobbyPanels>() == null) gameObject.AddComponent<NetLobbyPanels>();
        m_nm = NetworkManager.Singleton;
        m_address = PlayerPrefs.GetString(kAddressKey, "127.0.0.1");
        if (m_nm == null) return;
        // つないだままリザルトから戻ってきたら、このシーンの NetworkManager は使わないので消す
        // (動いている NetworkManager が DontDestroyOnLoad に残っている)
        foreach (var nm in FindObjectsByType<NetworkManager>())
        {
            if (nm != m_nm && !nm.IsListening) Destroy(nm.gameObject);
        }
        // 接続画面から来る途中で切れていたら(シーンを移っている間に止まったなど)、接続画面に戻る
        if (!m_nm.IsListening && !string.IsNullOrEmpty(NetConnectScreen.SceneName))
        {
            m_reload = true;
            return;
        }
        m_wasConnected = m_nm.IsConnectedClient && !m_nm.IsServer;
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
        NetMatchPlayer.ResetChoices();   // 部屋に入ったときは、あかだる・ステージおまかせから
        // 同じ LAN の他の PC から来られるように、すべてのネットワークで待つ
        Transport.SetConnectionData("127.0.0.1", m_port, "0.0.0.0");
        if (m_nm.StartHost()) NetRoom.Open(m_port);
        else s_message = $"ホストを始められませんでした(ポート {m_port} を他のアプリが使っているかもしれません)";
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
        NetMatchPlayer.ResetChoices();   // 部屋に入ったときは、あかだる・ステージおまかせから
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
        // 切れた理由(エラーコードなど)は画面に出さない
        s_message = "ホストとの接続が切れました";
    }

    // 止まったら、次のフレームでシーンを読み直して最初の状態に戻す(試合・積み木・キャラクターを残さない)
    void OnStopped(bool wasHost) => m_reload = true;

    void Reload()
    {
        m_reload = false;
        Time.timeScale = 1.0f;
        // NetworkManager がシーンの外(DontDestroyOnLoad)に移っていたら、読み直したシーンのものと二重にならないように消す
        if (m_nm != null && m_nm.gameObject.scene != gameObject.scene) Destroy(m_nm.gameObject);
        // 接続画面(NetConnectScene)から来たときは、接続画面に戻って理由を出す
        if (!string.IsNullOrEmpty(NetConnectScreen.SceneName))
        {
            NetConnectScreen.Message = s_message;
            s_message = null;
            Fader.Load(NetConnectScreen.SceneName);
            return;
        }
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

        // つないでいる間の表示とメニュー(試合の設定・開始・かいさん)は NetSessionMenu
        if (!m_nm.IsClient && !m_nm.IsServer) ConnectPanel(width, height, button, label, field);
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

}
