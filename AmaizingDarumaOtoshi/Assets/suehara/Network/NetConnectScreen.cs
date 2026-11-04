using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ネット対戦(LAN)の入り口。メニューの「たたかう → オンライン」から来る(NetConnectScene)。
// 見た目はセレクト画面(MatchSelect)に合わせている(部室の背景は同じ GameObject に付けた TitleClubRoomDressing)。
//  ・はじめ  : 「ホスト」と「さんか」のカードから選ぶ(B でメニューへ)
//  ・ホスト  : へやを開いて、へやばんごう(4けた。NetRoom)・IP アドレスと集まった人を出す。A / Start で全員そろって試合のシーン(NetStages)へ
//  ・さんか  : へやばんごう(LAN の中で NetRoom が探す)か IP アドレスをキーボードで入れてつなぐ。つながったらホストが始めるのを待つ
//              (ホストが始めると、NGO のシーン管理で自動的に試合のシーンへ移る)
//  ・つながらなかった・切れたときは理由を出して、この画面に残る
// 試合のシーンで切断したとき(NetLobbyUI・NetSceneFlow)も、この画面に戻ってくる。
public class NetConnectScreen : MonoBehaviour
{
    const string kAddressKey = "NetLobby.Address";   // NetLobbyUI と同じ(前回入れたアドレス)
    const int kMaxAddressLength = 15;
    const int kRoomDigits = 4;
    const float kSearchTime = 5.0f;   // へやを探す時間(秒。これで見つからなければあきらめる)

    [Header("素材(セレクト画面と同じもの)")]
    [SerializeField] TMP_FontAsset m_font;
    [Tooltip("角丸の画像(UI/Skin/UISprite)")]
    [SerializeField] Sprite m_uiSprite;
    [Tooltip("ネット対戦で使う NetworkManager(NetStages のものと同じ設定のプレハブ)")]
    [SerializeField] NetworkManager m_networkManagerPrefab;

    [Header("シーン")]
    [SerializeField] string m_menuScene = "MenuScene";
    [Tooltip("ホストが始めたときに全員で移る試合のシーン")]
    [SerializeField] string m_matchScene = "NetStages";

    [Header("通信")]
    [SerializeField] ushort m_port = 7777;
    [Tooltip("さんかするとき、1秒ごとに試す回数(この回数でつながらなければあきらめる)")]
    [SerializeField, Min(1)] int m_connectAttempts = 10;
    [Tooltip("試合に出られる人数(NetMatch の定員と同じにする。それより多い人は見るだけ)")]
    [SerializeField, Min(1)] int m_maxPlayers = 4;

    // この画面のシーン名(一度ここを通ったら入る。試合のシーンで切断したときの戻り先)
    public static string SceneName { get; private set; }
    // 次にこの画面を開いたときに出すお知らせ(試合のシーンで切断したときなど)
    public static string Message { get; set; }

    static readonly Color[] kPlayerColors =
    {
        new Color(0.93f, 0.27f, 0.25f),   // 1P 赤
        new Color(0.22f, 0.55f, 0.95f),   // 2P 青
        new Color(0.98f, 0.76f, 0.12f),   // 3P 黄
        new Color(0.30f, 0.74f, 0.35f),   // 4P 緑
    };
    static readonly Color kError = new Color(0.85f, 0.25f, 0.2f);
    static readonly Color kGood = new Color(0.2f, 0.6f, 0.3f);

    enum Screen { Choose, Host, Join }
    // さんかの画面の中の状態
    enum JoinState { Input, Searching, Connecting, Connected }

    Screen m_screen;
    JoinState m_joinState;
    int m_choice;                 // はじめの画面で選んでいるカード(0 = ホスト, 1 = さんか)
    int m_screenFrame = -1;       // 画面を切り替えたフレーム(同じボタンで続けて操作しない)
    bool m_leaving;               // シーンを移るところ(もう操作しない)
    bool m_starting;              // ホスト: 試合のシーンへ移るところ
    bool m_wasConnected;          // さんか: 一度つながった(切れたときのお知らせを分ける)

    NetworkManager m_nm;
    string m_address;             // さんか: 入れているもの(へやばんごう か IP アドレス)
    string m_target;              // さんか: つなぎに行っている IP アドレス
    NetRoom.Search m_search;      // さんか: へやばんごうで探している途中
    string m_message;
    Color m_messageColor = kError;
    string[] m_localAddresses = Array.Empty<string>();

    // ---- 画面の部品 ----
    RectTransform m_canvas;
    RectTransform[] m_roots;
    CanvasGroup[] m_groups;
    Material m_letterMat, m_popMat;
    Card[] m_cards;
    TextMeshProUGUI m_chooseMessage, m_hint;
    // ホスト
    TextMeshProUGUI m_hostRoom, m_hostAddress, m_hostCount;
    Chip[] m_chips;
    Button m_startButton;
    // さんか
    TextMeshProUGUI m_joinLabel, m_addressText, m_joinStatus;
    RectTransform m_addressBox;
    Button m_connectButton;

    // ---- 背景 ----
    Camera m_cam;
    readonly List<Transform> m_floaters = new List<Transform>();

    class Card { public RectTransform rect; public Image bg; public TextMeshProUGUI title, desc; public Color color; }
    class Chip { public Image bg; public TextMeshProUGUI label, state; }
    class Button { public RectTransform rect; public Image bg; public TextMeshProUGUI text; public Color color; }

    UnityTransport Transport => m_nm != null ? m_nm.GetComponent<UnityTransport>() : null;

    //========================================
    // 始まり・終わり
    //========================================

    void Start()
    {
        SceneName = gameObject.scene.name;
        m_address = PlayerPrefs.GetString(kAddressKey, "127.0.0.1");
        m_localAddresses = LocalAddresses();
        m_letterMat = ToyKit.LetterMaterial(m_font);
        m_popMat = ToyKit.PopMaterial(m_letterMat);

        BuildBackground();
        BuildUI();
        SetupNetworkManager();

        // つないだままこのシーンを読み直したとき(参加した直後にホストと同じシーンへそろえられたときなど)は、続きの画面から
        if (m_nm != null && m_nm.IsServer) GoTo(Screen.Host);
        else if (m_nm != null && m_nm.IsClient)
        {
            m_wasConnected = m_nm.IsConnectedClient;
            m_joinState = m_wasConnected ? JoinState.Connected : JoinState.Connecting;
            GoTo(Screen.Join);
        }
        else GoTo(Screen.Choose);

        if (!string.IsNullOrEmpty(Message)) { SetMessage(Message, kError); Message = null; }
        SnapScreens();

        var k = Keyboard.current;
        if (k != null) k.onTextInput += OnTextInput;
    }

    void OnDestroy()
    {
        var k = Keyboard.current;
        if (k != null) k.onTextInput -= OnTextInput;
        StopSearch();
        Unsubscribe();
    }

    // 動いている NetworkManager を使う。無ければ(止まっているものしかなければ)プレハブから作る
    void SetupNetworkManager()
    {
        var running = NetworkManager.Singleton;
        // 止まっているものはすぐ消す(Singleton が空いて、新しく作るものが Singleton になるように)
        foreach (var nm in FindObjectsByType<NetworkManager>(FindObjectsInactive.Include))
        {
            if (!nm.IsListening) DestroyImmediate(nm.gameObject);
        }
        if (running != null && running.IsListening) m_nm = running;
        else if (m_networkManagerPrefab != null)
        {
            m_nm = Instantiate(m_networkManagerPrefab);
            m_nm.name = m_networkManagerPrefab.name;
            m_nm.SetSingleton();
        }
        if (m_nm == null)
        {
            Debug.LogError("[NetConnectScreen] NetworkManager のプレハブが設定されていません", this);
            return;
        }
        m_nm.OnClientConnectedCallback += OnConnected;
        m_nm.OnClientDisconnectCallback += OnDisconnected;
        m_nm.OnClientStopped += OnStopped;
        m_nm.OnServerStopped += OnStopped;
    }

    void Unsubscribe()
    {
        if (m_nm == null) return;
        m_nm.OnClientConnectedCallback -= OnConnected;
        m_nm.OnClientDisconnectCallback -= OnDisconnected;
        m_nm.OnClientStopped -= OnStopped;
        m_nm.OnServerStopped -= OnStopped;
    }

    //========================================
    // つなぐ・切る
    //========================================

    bool CanStart => m_nm != null && !m_nm.IsListening && !m_nm.ShutdownInProgress;

    void StartHost()
    {
        if (!CanStart) return;
        // 同じ LAN の他の PC から来られるように、すべてのネットワークで待つ
        Transport.SetConnectionData("127.0.0.1", m_port, "0.0.0.0");
        if (m_nm.StartHost())
        {
            NetRoom.Open(m_port);
            SetMessage(null, kError);
            m_localAddresses = LocalAddresses();
            GoTo(Screen.Host);
            return;
        }
        MenuSounds.Cancel();
        SetMessage($"へやを ひらけませんでした(ポート {m_port} を ほかのアプリが 使っているかもしれません)", kError);
    }

    // さんか: 入れたもの(へやばんごう か IP アドレス)でつなぐ
    void Submit()
    {
        if (!CanStart) return;
        string input = m_address.Trim();
        m_address = input;
        // 「.」が入っていれば IP アドレス、数字だけならへやばんごう
        if (input.Contains("."))
        {
            if (!IPAddress.TryParse(input, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            {
                MenuSounds.Cancel();
                SetMessage("IP アドレスの 形が ちがいます(れい：192.168.1.10)", kError);
                return;
            }
            PlayerPrefs.SetString(kAddressKey, input);
            MenuSounds.Decide();
            StartClient(input, m_port);
            return;
        }
        if (input.Length != kRoomDigits)
        {
            MenuSounds.Cancel();
            SetMessage($"へやばんごうは {kRoomDigits}けたの 数字です", kError);
            return;
        }
        PlayerPrefs.SetString(kAddressKey, input);
        MenuSounds.Decide();
        SetMessage(null, kError);
        try
        {
            m_search = new NetRoom.Search(int.Parse(input));
            m_joinState = JoinState.Searching;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[NetConnectScreen] へやを探せませんでした: " + e.Message);
            SetMessage("へやを 探せませんでした。IP アドレスで つないでね", kError);
        }
    }

    // へやを探している途中: 見つかったらつなぐ。しばらく見つからなければあきらめる
    void UpdateSearch(float dt)
    {
        if (m_search == null) return;
        m_search.Update(dt);
        if (m_search.IsFound)
        {
            string address = m_search.Address;
            ushort port = m_search.GamePort;
            StopSearch();
            StartClient(address, port);
            return;
        }
        if (m_search.Elapsed < kSearchTime) return;
        StopSearch();
        m_joinState = JoinState.Input;
        MenuSounds.Cancel();
        SetMessage($"へやばんごう {m_address} が 見つかりませんでした。\n番号と、同じ LAN(Wi-Fi)に いるかを たしかめてね(IP アドレスでも つなげます)", kError);
    }

    void StopSearch()
    {
        m_search?.Dispose();
        m_search = null;
    }

    void StartClient(string address, ushort port)
    {
        if (!CanStart) return;
        m_target = address;
        Transport.SetConnectionData(address, port);
        Transport.MaxConnectAttempts = m_connectAttempts;
        m_wasConnected = false;
        if (!m_nm.StartClient())
        {
            m_joinState = JoinState.Input;
            MenuSounds.Cancel();
            SetMessage("さんかを 始められませんでした", kError);
            return;
        }
        SetMessage(null, kError);
        m_joinState = JoinState.Connecting;
    }

    void Stop()
    {
        if (m_nm != null && m_nm.IsListening) m_nm.Shutdown();
    }

    void OnConnected(ulong clientId)
    {
        if (m_nm.IsServer)
        {
            if (clientId != m_nm.LocalClientId) MenuSounds.Decide();
            return;
        }
        if (clientId != m_nm.LocalClientId) return;
        m_wasConnected = true;
        m_joinState = JoinState.Connected;
        MenuSounds.Decide();
        SetMessage(null, kError);
    }

    // クライアント: ホストから切れた(つながらなかったときも来る)
    void OnDisconnected(ulong clientId)
    {
        if (m_nm.IsServer) return;
        if (!m_wasConnected)
        {
            SetMessage($"{m_target} に つながりませんでした。\nホストの IP アドレス・同じ LAN(Wi-Fi)にいるか・ファイアウォールを たしかめてね", kError);
            return;
        }
        // ホストが理由を付けて切ったときはそれも出す
        string reason = m_nm.DisconnectReason;
        SetMessage(string.IsNullOrEmpty(reason) ? "ホストとの 接続が 切れました" : $"ホストとの 接続が 切れました({reason})", kError);
    }

    // 止まった: ホストははじめの画面へ、さんかは入力に戻る(お知らせはそのまま)
    void OnStopped(bool wasHost)
    {
        // メニューへ戻るところなら何もしない。試合のシーンへ移る前に止まったときは、この画面で続ける
        if (m_leaving && !m_starting) return;
        m_leaving = m_starting = false;
        if (m_screen == Screen.Host) GoTo(Screen.Choose);
        m_joinState = JoinState.Input;
    }

    void BackToMenu()
    {
        m_leaving = true;
        MenuSounds.Cancel();
        Unsubscribe();
        // メニューではネット対戦を使わないので、NetworkManager ごと片付ける(次に来たら作り直す)
        if (m_nm != null)
        {
            if (m_nm.IsListening) m_nm.Shutdown();
            Destroy(m_nm.gameObject);
            m_nm = null;
        }
        Fader.Load(m_menuScene);
    }

    void StartMatch()
    {
        m_leaving = true;
        m_starting = true;
        MenuSounds.Decide();
        NetSceneFlow.Load(m_matchScene);
    }

    //========================================
    // 毎フレーム
    //========================================

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (!m_leaving && !Fader.IsBusy && !NetSceneFlow.IsBusy && m_screenFrame != Time.frameCount)
        {
            if (m_screen == Screen.Choose) UpdateChoose();
            else if (m_screen == Screen.Host) UpdateHost();
            else UpdateJoin();
        }

        // 画面の切り替え(はじめ → ホスト / さんか)。横にすべって切り替わる(セレクト画面と同じ)
        float k = 1.0f - Mathf.Exp(-10.0f * dt);
        for (int i = 0; i < m_roots.Length; i++)
        {
            m_roots[i].anchoredPosition = Vector2.Lerp(m_roots[i].anchoredPosition, new Vector2(ScreenOffset(i) * 2200.0f, 0.0f), k);
            m_groups[i].alpha = Mathf.MoveTowards(m_groups[i].alpha, i == (int)m_screen ? 1.0f : 0.0f, dt * 4.0f);
        }

        if (!m_leaving && m_joinState == JoinState.Searching) UpdateSearch(dt);
        UpdateCards(dt);
        UpdateHostView(dt);
        UpdateJoinView(dt);
        UpdateBackground();
        m_hint.text = Hint();
    }

    // はじめの画面を真ん中に、ホストは右、さんかはさらに右
    float ScreenOffset(int i) => i - (int)m_screen;

    void GoTo(Screen screen)
    {
        m_screen = screen;
        m_screenFrame = Time.frameCount;
    }

    void SnapScreens()
    {
        for (int i = 0; i < m_roots.Length; i++)
        {
            m_roots[i].anchoredPosition = new Vector2(ScreenOffset(i) * 2200.0f, 0.0f);
            m_groups[i].alpha = i == (int)m_screen ? 1.0f : 0.0f;
        }
    }

    void SetMessage(string text, Color color)
    {
        m_message = text;
        m_messageColor = color;
    }

    void UpdateChoose()
    {
        if (MenuInput.Left() || MenuInput.Right()) { m_choice = 1 - m_choice; MenuSounds.Cursor(); }
        else if (MenuInput.Submit() || MenuInput.Start())
        {
            if (m_choice == 0) { MenuSounds.Decide(); StartHost(); }
            else
            {
                MenuSounds.Decide();
                SetMessage(null, kError);
                m_joinState = JoinState.Input;
                GoTo(Screen.Join);
            }
        }
        else if (MenuInput.Cancel()) BackToMenu();
    }

    void UpdateHost()
    {
        if (m_nm == null || !m_nm.IsServer) return;
        if (MenuInput.Submit() || MenuInput.Start()) StartMatch();
        else if (MenuInput.Cancel()) { MenuSounds.Cancel(); Stop(); }
    }

    // さんか: キーボードで IP アドレスを入れる(文字は OnTextInput で受け取る)
    void UpdateJoin()
    {
        var k = Keyboard.current;
        bool submit = (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame));
        bool cancel = (k != null && k.escapeKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
        foreach (var g in Gamepad.all)
        {
            submit |= g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame;
            cancel |= g.buttonEast.wasPressedThisFrame;
        }

        if (m_joinState == JoinState.Input)
        {
            // 貼り付け(Ctrl + V)
            if (k != null && (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed) && k.vKey.wasPressedThisFrame) Paste(GUIUtility.systemCopyBuffer);
            if (submit) Submit();
            else if (cancel) { MenuSounds.Cancel(); SetMessage(null, kError); GoTo(Screen.Choose); }
            return;
        }
        // 探している途中: B でやめる
        if (m_joinState == JoinState.Searching)
        {
            if (cancel) { MenuSounds.Cancel(); StopSearch(); m_joinState = JoinState.Input; }
            return;
        }
        // つないでいる途中・つながったあと: B でやめる
        if (cancel) { MenuSounds.Cancel(); SetMessage(null, kError); Stop(); }
    }

    void OnTextInput(char c)
    {
        if (m_leaving || m_screen != Screen.Join || m_joinState != JoinState.Input) return;
        if (c == '\b')
        {
            if (m_address.Length > 0) { m_address = m_address.Substring(0, m_address.Length - 1); MenuSounds.Cursor(); }
            return;
        }
        if (!IsAddressChar(c) || m_address.Length >= kMaxAddressLength) return;
        m_address += c;
        MenuSounds.Cursor();
    }

    void Paste(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var sb = new System.Text.StringBuilder();
        foreach (char c in text.Trim())
        {
            if (IsAddressChar(c) && sb.Length < kMaxAddressLength) sb.Append(c);
        }
        if (sb.Length == 0) return;
        m_address = sb.ToString();
        MenuSounds.Cursor();
    }

    static bool IsAddressChar(char c) => (c >= '0' && c <= '9') || c == '.';

    string Hint()
    {
        switch (m_screen)
        {
            case Screen.Choose: return "←→：えらぶ　A / Start：決定　B：メニューへ";
            case Screen.Host: return "A / Start：はじめる　B：へやを とじる";
            default:
                return m_joinState == JoinState.Input
                    ? "キーボード：へやばんごう か IP アドレスを入れる(Ctrl + V で はりつけ)　Enter / A：つなぐ　Esc / B：もどる"
                    : "B：やめる";
        }
    }

    //========================================
    // 見た目の更新
    //========================================

    void UpdateCards(float dt)
    {
        // 選んでいるカードは色を付けて大きく(セレクト画面のモードのカードと同じ)
        float k = 1.0f - Mathf.Exp(-14.0f * dt);
        for (int i = 0; i < m_cards.Length; i++)
        {
            var c = m_cards[i];
            bool sel = i == m_choice;
            c.bg.color = Color.Lerp(c.bg.color, sel ? c.color : Color.white, k);
            c.rect.localScale = Vector3.Lerp(c.rect.localScale, Vector3.one * (sel ? 1.08f : 0.94f), k);
            c.title.color = sel ? Color.white : ToyKit.Ink;
            c.desc.color = sel ? Color.white : new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.75f);
        }
        m_chooseMessage.text = m_message ?? "";
        m_chooseMessage.color = m_messageColor;
    }

    void UpdateHostView(float dt)
    {
        bool hosting = m_nm != null && m_nm.IsServer;
        int room = NetRoom.Current;
        m_hostRoom.text = room > 0 && NetRoom.IsAnswering ? room.ToString() : "----";
        string ip = m_localAddresses.Length > 0 ? string.Join(" / ", m_localAddresses) : "(見つかりません)";
        m_hostAddress.text = room > 0 && !NetRoom.IsAnswering
            ? $"へやばんごうが 使えません。IP アドレスで さんかしてね：{ip}"
            : $"見つからないときは IP アドレス：{ip}";

        int count = hosting ? m_nm.ConnectedClientsIds.Count : 0;
        for (int i = 0; i < m_chips.Length; i++)
        {
            bool joined = i < count;
            var chip = m_chips[i];
            chip.bg.color = Color.Lerp(chip.bg.color, joined ? kPlayerColors[i] : new Color(0.85f, 0.83f, 0.8f), 1.0f - Mathf.Exp(-12.0f * dt));
            chip.state.text = !joined ? "まっています" : i == 0 ? "ホスト(あなた)" : "さんか";
            chip.label.color = chip.state.color = joined ? Color.white : new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.5f);
        }
        m_hostCount.text = count > m_maxPlayers
            ? $"{count}人 あつまっています({m_maxPlayers + 1}人目からは 見るだけ)"
            : $"{count}人 あつまっています({m_maxPlayers}人まで)";
        Pulse(m_startButton, hosting);
    }

    void UpdateJoinView(float dt)
    {
        bool input = m_joinState == JoinState.Input;
        // 入力中だけカーソルを点滅させる
        bool caret = input && Mathf.Repeat(Time.unscaledTime, 1.0f) < 0.55f;
        m_addressText.text = (m_address.Length > 0 ? m_address : (caret ? "" : " ")) + (input ? (caret ? "<color=#E8735A>|</color>" : "<alpha=#00>|") : "");
        m_addressBox.localScale = Vector3.Lerp(m_addressBox.localScale, Vector3.one * (input ? 1.0f : 0.94f), 1.0f - Mathf.Exp(-12.0f * dt));

        string status;
        Color color;
        string dots = new string('・', 1 + (int)(Time.unscaledTime * 3.0f) % 3);
        if (m_joinState == JoinState.Searching)
        {
            status = $"へやばんごう {m_address} を さがしています" + dots;
            color = ToyKit.Ink;
        }
        else if (m_joinState == JoinState.Connecting)
        {
            status = $"{m_target} に つないでいます" + dots;
            color = ToyKit.Ink;
        }
        else if (m_joinState == JoinState.Connected)
        {
            status = "つながりました！\nホストが はじめるのを まっています";
            color = kGood;
        }
        else
        {
            status = m_message ?? "";
            color = m_messageColor;
        }
        m_joinStatus.text = status;
        m_joinStatus.color = color;
        m_joinLabel.text = m_joinState == JoinState.Input ? "ホストの へやばんごうを 入れてね" : "ホストの へや";
        Pulse(m_connectButton, input);
    }

    // 押せるボタンはゆっくり大きくなったり小さくなったりする。押せないときは灰色
    void Pulse(Button b, bool active)
    {
        float s = active ? 1.0f + Mathf.Sin(Time.unscaledTime * 4.0f) * 0.03f : 0.94f;
        b.rect.localScale = Vector3.one * s;
        b.bg.color = active ? b.color : new Color(0.75f, 0.72f, 0.7f);
    }

    //========================================
    // 組み立て
    //========================================

    // 背景はセレクト画面と同じ(このあと TitleClubRoomDressing が部室に差し替える)
    void BuildBackground()
    {
        var blockMesh = ToyKit.RoundedBox(new Vector3(ToyKit.BlockW, ToyKit.BlockH, ToyKit.BlockD), 0.14f, 10);
        m_cam = ToyKit.CreateCamera();
        m_cam.fieldOfView = 45.0f;
        ToyKit.CreateLights();
        var post = ToyKit.CreatePostProcess(0.35f);
        post.dof.focusDistance.value = 3.0f;      // 背景はボカして UI を読みやすく
        post.dof.aperture.value = 2.0f;
        ToyKit.CreateEnvironment(blockMesh);

        // 宙に浮いてゆっくり回る積み木(にぎやかし)
        for (int i = 0; i < 9; i++)
        {
            var b = new GameObject("Floater").transform;
            b.gameObject.AddComponent<MeshFilter>().sharedMesh = blockMesh;
            b.gameObject.AddComponent<MeshRenderer>().sharedMaterial = ToyKit.Lit(ToyKit.Palette[i % ToyKit.Palette.Length], 0.55f);
            b.position = new Vector3(-7.0f + i * 1.75f, 1.5f + (i % 3) * 1.6f, 4.0f + (i % 2) * 2.0f);
            b.rotation = UnityEngine.Random.rotation;
            m_floaters.Add(b);
        }
        UpdateBackground();
    }

    void UpdateBackground()
    {
        float t = Time.unscaledTime;
        foreach (var b in m_floaters)
        {
            if (b != null) b.Rotate(new Vector3(0.3f, 0.7f, 0.2f), 12.0f * Time.unscaledDeltaTime);
        }
        m_cam.transform.position = new Vector3(Mathf.Sin(t * 0.13f) * 0.3f, 3.0f + Mathf.Sin(t * 0.17f) * 0.1f, -10.0f);
        m_cam.transform.LookAt(new Vector3(0.0f, 3.0f, 5.0f));
        m_cam.fieldOfView = ToyKit.FitFov(45.0f, m_cam.aspect);   // どの縦横比でも左右の範囲は同じ
    }

    void BuildUI()
    {
        m_canvas = ToyKit.CreateOverlayCanvas("UI", 10);
        m_roots = new RectTransform[3];
        m_groups = new CanvasGroup[3];
        for (int i = 0; i < 3; i++)
        {
            m_roots[i] = Layer(((Screen)i).ToString(), m_canvas);
            m_groups[i] = m_roots[i].gameObject.AddComponent<CanvasGroup>();
        }

        // ---- はじめ: ホスト / さんか ----
        var choose = m_roots[(int)Screen.Choose];
        Header(choose, "オンライン", ToyKit.Palette[4]);
        m_cards = new[]
        {
            MakeCard(choose, "ホスト", ToyKit.Palette[1], new Vector2(-330.0f, 10.0f), "へやを つくって\nみんなが くるのを まつ\n\n(へやばんごうを\nさんかする人に つたえる)"),
            MakeCard(choose, "さんか", ToyKit.Palette[3], new Vector2(330.0f, 10.0f), "ホストの へやに\nはいる\n\n(ホストの へやばんごうを\n入れる)"),
        };
        m_chooseMessage = ToyKit.UIText("Message", choose, m_font, "", 36.0f, kError);
        m_chooseMessage.textWrappingMode = TextWrappingModes.Normal;
        ToyKit.Anchor(m_chooseMessage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, -400.0f), new Vector2(1600.0f, 110.0f));

        // ---- ホスト: IP アドレスと集まった人 ----
        var host = m_roots[(int)Screen.Host];
        Header(host, "ホスト", ToyKit.Palette[1]);
        Plate(host, new Vector2(0.0f, 40.0f), new Vector2(1400.0f, 640.0f));
        var hl = ToyKit.UIText("Label", host, m_font, "へやばんごう(さんかする人に つたえてね)", 40.0f, ToyKit.Ink);
        ToyKit.Anchor(hl.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, 310.0f), new Vector2(1300.0f, 60.0f));
        m_hostRoom = ToyKit.UIText("Room", host, m_font, "", 140.0f, ToyKit.Palette[4]);
        m_hostRoom.fontSharedMaterial = m_popMat;
        m_hostRoom.characterSpacing = 30.0f;
        ToyKit.Anchor(m_hostRoom.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, 190.0f), new Vector2(1300.0f, 170.0f));
        m_hostAddress = ToyKit.UIText("Address", host, m_font, "", 30.0f, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.7f));
        m_hostAddress.enableAutoSizing = true; m_hostAddress.fontSizeMin = 20.0f; m_hostAddress.fontSizeMax = 30.0f;
        ToyKit.Anchor(m_hostAddress.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, 85.0f), new Vector2(1300.0f, 46.0f));
        m_chips = new Chip[m_maxPlayers];
        for (int i = 0; i < m_chips.Length; i++)
        {
            float x = (i - (m_chips.Length - 1) * 0.5f) * 320.0f;
            m_chips[i] = MakeChip(host, (i + 1) + "P", new Vector2(x, -40.0f));
        }
        m_hostCount = ToyKit.UIText("Count", host, m_font, "", 38.0f, ToyKit.Ink);
        ToyKit.Anchor(m_hostCount.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, -190.0f), new Vector2(1300.0f, 60.0f));
        m_startButton = MakeButton(host, "A / Start：はじめる", ToyKit.Palette[0], new Vector2(0.0f, -380.0f));

        // ---- さんか: IP アドレスを入れる ----
        var join = m_roots[(int)Screen.Join];
        Header(join, "さんか", ToyKit.Palette[3]);
        Plate(join, new Vector2(0.0f, 40.0f), new Vector2(1400.0f, 640.0f));
        m_joinLabel = ToyKit.UIText("Label", join, m_font, "", 48.0f, ToyKit.Ink);
        ToyKit.Anchor(m_joinLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, 280.0f), new Vector2(1300.0f, 70.0f));
        m_addressBox = ToyKit.UIImage("AddressBox", join, Color.white, m_uiSprite);
        m_addressBox.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        ToyKit.Anchor(m_addressBox, new Vector2(0.5f, 0.5f), new Vector2(0.0f, 130.0f), new Vector2(1000.0f, 170.0f));
        var ol = m_addressBox.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(4.0f, -4.0f);
        m_addressText = ToyKit.UIText("Text", m_addressBox, m_font, "", 100.0f, ToyKit.Ink);
        ToyKit.Stretch(m_addressText.rectTransform);
        var example = ToyKit.UIText("Example", join, m_font, "4けたの 数字(ホストの 画面に 出ています)。IP アドレスでも つなげます", 30.0f, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.6f));
        ToyKit.Anchor(example.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, 10.0f), new Vector2(1300.0f, 50.0f));
        m_joinStatus = ToyKit.UIText("Status", join, m_font, "", 42.0f, ToyKit.Ink);
        m_joinStatus.textWrappingMode = TextWrappingModes.Normal;
        m_joinStatus.enableAutoSizing = true; m_joinStatus.fontSizeMin = 26.0f; m_joinStatus.fontSizeMax = 42.0f;
        ToyKit.Anchor(m_joinStatus.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, -150.0f), new Vector2(1300.0f, 200.0f));
        m_connectButton = MakeButton(join, "Enter / A：つなぐ", ToyKit.Palette[3], new Vector2(0.0f, -380.0f));

        m_hint = ToyKit.UIText("Hint", m_canvas, m_font, "", 26.0f, ToyKit.Ink);
        m_hint.alignment = TextAlignmentOptions.Right;
        m_hint.rectTransform.pivot = new Vector2(1.0f, 0.0f);
        ToyKit.Anchor(m_hint.rectTransform, new Vector2(1.0f, 0.0f), new Vector2(-40.0f, 14.0f), new Vector2(1840.0f, 40.0f));
    }

    RectTransform Layer(string name, Transform parent)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent, false);
        ToyKit.Stretch(r);
        return r;
    }

    // 左上の見出し(セレクト画面と同じ形)
    void Header(Transform parent, string text, Color accent)
    {
        var bar = ToyKit.UIImage("HeaderBar", parent, accent, m_uiSprite);
        bar.anchorMin = bar.anchorMax = new Vector2(0.0f, 1.0f);
        bar.pivot = new Vector2(0.0f, 1.0f);
        bar.anchoredPosition = new Vector2(-30.0f, -30.0f);
        bar.sizeDelta = new Vector2(760.0f, 110.0f);
        var t = ToyKit.UIText("Header", bar, m_font, text, 66.0f, Color.white);
        t.fontSharedMaterial = m_popMat;
        t.alignment = TextAlignmentOptions.Left;
        ToyKit.Anchor(t.rectTransform, new Vector2(0.0f, 0.5f), new Vector2(400.0f, 0.0f), new Vector2(700.0f, 110.0f));
    }

    // 文字の後ろに敷く薄い板(背景の部室に文字が紛れないように)
    void Plate(Transform parent, Vector2 pos, Vector2 size)
    {
        var plate = ToyKit.UIImage("Plate", parent, new Color(1.0f, 0.98f, 0.94f, 0.85f), m_uiSprite);
        plate.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        ToyKit.Anchor(plate, new Vector2(0.5f, 0.5f), pos, size);
        var ol = plate.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3.0f, -3.0f);
    }

    // はじめの画面のカード(セレクト画面のモードのカードと同じ形)
    Card MakeCard(Transform parent, string title, Color color, Vector2 pos, string desc)
    {
        var c = new Card { color = color };
        var size = new Vector2(560.0f, 620.0f);
        c.rect = ToyKit.UIImage("Card_" + title, parent, Color.white, m_uiSprite);
        ToyKit.Anchor(c.rect, new Vector2(0.5f, 0.5f), pos, size);
        c.bg = c.rect.GetComponent<Image>();
        c.bg.pixelsPerUnitMultiplier = 0.3f;
        var ol = c.rect.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(4.0f, -4.0f);

        c.title = ToyKit.UIText("Title", c.rect, m_font, title, 110.0f, ToyKit.Ink);
        c.title.fontSharedMaterial = m_letterMat;
        ToyKit.Anchor(c.title.rectTransform, new Vector2(0.5f, 1.0f), new Vector2(0.0f, -170.0f), new Vector2(size.x - 40.0f, 140.0f));

        c.desc = ToyKit.UIText("Desc", c.rect, m_font, desc, 34.0f, ToyKit.Ink);
        c.desc.enableAutoSizing = true; c.desc.fontSizeMin = 20.0f; c.desc.fontSizeMax = 34.0f;
        c.desc.lineSpacing = 10.0f;
        ToyKit.Anchor(c.desc.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(0.0f, 160.0f), new Vector2(size.x - 50.0f, 260.0f));
        return c;
    }

    // ホストの画面の、集まった人の札(1P〜4P)
    Chip MakeChip(Transform parent, string label, Vector2 pos)
    {
        var r = ToyKit.UIImage("Chip" + label, parent, Color.white, m_uiSprite);
        r.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), pos, new Vector2(280.0f, 180.0f));
        var ol = r.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3.0f, -3.0f);
        var chip = new Chip { bg = r.GetComponent<Image>() };
        chip.label = ToyKit.UIText("Label", r, m_font, label, 72.0f, Color.white);
        chip.label.fontSharedMaterial = m_letterMat;
        ToyKit.Anchor(chip.label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, 25.0f), new Vector2(260.0f, 100.0f));
        chip.state = ToyKit.UIText("State", r, m_font, "", 28.0f, Color.white);
        ToyKit.Anchor(chip.state.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, -50.0f), new Vector2(260.0f, 44.0f));
        return chip;
    }

    // 下の大きなボタン(押せるときはゆれる)
    Button MakeButton(Transform parent, string text, Color color, Vector2 pos)
    {
        var r = ToyKit.UIImage("Button", parent, color, m_uiSprite);
        r.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), pos, new Vector2(760.0f, 120.0f));
        var ol = r.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3.0f, -3.0f);
        var t = ToyKit.UIText("Text", r, m_font, text, 56.0f, Color.white);
        t.fontSharedMaterial = m_letterMat;
        ToyKit.Stretch(t.rectTransform);
        return new Button { rect = r, bg = r.GetComponent<Image>(), text = t, color = color };
    }

    // この PC の LAN の IP アドレス(さんかする人に伝える)
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
            Debug.LogWarning("[NetConnectScreen] IP アドレスを調べられませんでした: " + e.Message);
        }
        return list.ToArray();
    }
}
