using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ネット対戦の試合のシーン(NetStages)で、つないでいる間に出す表示とメニュー。NetLobbyUI が自動で付ける。
//  ・右上: いまの試合の設定(モード・ストック・時間)・へやばんごう・人数(見るだけ。クリックはしない)
//  ・Start(+ / キーボードは Tab)でメニューを開く。見た目は試合のポーズ画面(PauseMenu)と同じ
//      ホスト(開始待ち): 試合を はじめる / キャラクター / モード / ストック / 時間 / へやを かいさん / とじる
//      ホスト(試合中) ・さんか: キャラクター / へやを かいさん(ぬける) / とじる
//    キャラクター: 自分のキャラを選ぶ画面を開く(だれでも。開始待ちの間だけ。選ぶとその場で体が入れ替わる。NetMatchPlayer.RequestCharacter)
//    ↑↓：えらぶ　←→：かえる　A：決定　B / Start：とじる。かいさん・ぬけるは「はい / いいえ」で確かめる
//  ・メニューを開いている間は、キャラクターの操作(NetPadKeyboardInput)とステージの切り替え(左右キー)を止める
// ホストが変えた設定はすぐ NetMatch に入れるので、さんかした人の右上にも同じ設定が出る。
[DefaultExecutionOrder(100)]   // キャラクターが入力を読んだあとに閉じる(とじるの A でジャンプしないように)
public class NetSessionMenu : MonoBehaviour
{
    // 時間制限の選択肢(秒。0 = なし。タイムはなしを選べないので 120 秒)
    static readonly float[] kTimeLimits = { 0.0f, 60.0f, 120.0f, 180.0f };
    const int kMaxStocks = 5;
    const int kSortingOrder = 50;   // 試合の HUD(15)・カウントダウン(30)・名札(20)より上

    static readonly Color kPanelColor = new Color(0.08f, 0.06f, 0.05f, 0.92f);
    static readonly Color kInfoColor = new Color(0.08f, 0.06f, 0.05f, 0.78f);
    static readonly Color kItemColor = new Color(1.0f, 1.0f, 1.0f, 0.12f);
    static readonly Color kSelectedColor = new Color(1.0f, 0.82f, 0.25f, 1.0f);
    static readonly Color kDimColor = new Color(0.0f, 0.0f, 0.0f, 0.55f);
    static readonly Color kPale = new Color(1.0f, 1.0f, 1.0f, 0.7f);
    static readonly string[] kConfirmNames = { "はい", "いいえ" };

    // 選んだルール(リザルトから戻ってシーンを読み直しても覚えておく)
    static MatchSetup.MatchMode s_mode = MatchSetup.MatchMode.Stock;
    static int s_stocks = 3;
    static int s_timeIndex;

    // メニューを開いているか(開いている間はキャラクターを動かさない)
    public static bool IsOpen { get; private set; }

    enum Item { Start, Character, Mode, Stocks, Time, Leave, Close }

    NetworkManager m_nm;
    TMP_FontAsset m_font;
    Material m_letterMat, m_popMat;

    // ---- 右上の表示 ----
    RectTransform m_canvas, m_info;
    Image m_infoTag;
    TextMeshProUGUI m_infoTagText, m_infoRoom, m_infoRules, m_infoState, m_infoHint;

    // ---- メニュー ----
    RectTransform m_menu, m_panel, m_confirm;
    CanvasGroup m_menuGroup;
    Image m_menuTag;
    TextMeshProUGUI m_menuTagText, m_confirmText;
    readonly List<Item> m_items = new List<Item>();
    readonly List<Image> m_itemImages = new List<Image>();
    readonly List<TextMeshProUGUI> m_itemTexts = new List<TextMeshProUGUI>();
    readonly List<TextMeshProUGUI> m_itemValues = new List<TextMeshProUGUI>();
    Image[] m_confirmItems;
    TextMeshProUGUI[] m_confirmTexts;
    TextMeshProUGUI m_menuHint;
    int m_index, m_confirmIndex;
    bool m_confirming;
    int m_openFrame;
    float m_openTime;

    // メニューを開いている間止めているもの(ステージの切り替え・開発用の表示)
    // OnGUI で描く表示はどの UI よりも上に出てしまうので、開発用のものはメニューを開いている間だけ止める
    // (Yurube さんの必殺技のゲージ・カットインも OnGUI だが、止めると必殺技が止まるのでそのまま)
    static readonly HashSet<string> kHideWhileOpen = new HashSet<string>
    {
        "StageSwitcher", "TumikiImpactModeToggle", "SpawnPointDebugView", "StageCameraTuner", "CharacterLabUI",
    };
    readonly List<MonoBehaviour> m_hidden = new List<MonoBehaviour>();

    // ---- キャラクターを選ぶ画面 ----
    const int kPickColumns = 5;
    class Card { public Image bg, icon; public TextMeshProUGUI letter, name; public RectTransform now; }
    RectTransform m_picker;
    readonly List<Card> m_cards = new List<Card>();
    TextMeshProUGUI m_pickName, m_pickDesc;
    bool m_picking;
    int m_pickIndex;
    // 最後に NetMatch に入れたルール(変わったときだけ入れ直す)
    NetMatch.RulesData? m_sentRules;

    bool IsHost => m_nm != null && m_nm.IsServer;
    bool Connected => m_nm != null && m_nm.IsListening && (m_nm.IsServer || m_nm.IsConnectedClient);
    static NetMatch Net => NetMatch.Instance != null && NetMatch.Instance.IsSpawned ? NetMatch.Instance : null;
    // ホストがルールを変えられるのは開始待ちのときだけ
    bool CanEditRules => IsHost && Net != null && !Net.IsStarted;

    void Start()
    {
        m_font = PresentationSettings.DefaultFont;
        m_letterMat = ToyKit.LetterMaterial(m_font);
        m_popMat = ToyKit.PopMaterial(m_letterMat);
        Build();
    }

    void OnDestroy()
    {
        if (IsOpen) RestoreHidden();
        IsOpen = false;
        if (m_canvas != null) Destroy(m_canvas.gameObject);
    }

    void Update()
    {
        m_nm = NetworkManager.Singleton;
        bool connected = Connected;
        m_canvas.gameObject.SetActive(connected);
        if (!connected)
        {
            if (IsOpen) Close(false);
            return;
        }

        if (CanEditRules) SendRules();
        UpdateInfo();

        if (!IsOpen)
        {
            // 画面の切り替え中・リザルトへ移る前は開かない
            if (!NetSceneFlow.IsBusy && !Fader.IsBusy && PressedToggle()) Open();
        }
        else
        {
            if (Time.frameCount != m_openFrame) UpdateMenu();
            if (IsOpen) Animate();
        }
    }

    //========================================
    // ルール
    //========================================

    // タイムは時間制限なしを選べない
    static float TimeLimit => s_mode == MatchSetup.MatchMode.Time && kTimeLimits[s_timeIndex] <= 0.0f ? 120.0f : kTimeLimits[s_timeIndex];

    static MatchSetup.MatchRules BuildRules() => new MatchSetup.MatchRules
    {
        mode = s_mode,
        stocks = s_stocks,
        stockTimeLimit = TimeLimit,
        timeLength = TimeLimit,
        ghostTimeLimit = TimeLimit,
    };

    // ホスト: 選んでいるルールを NetMatch に入れる(さんかした人の表示にも出る)
    void SendRules()
    {
        var rules = BuildRules();
        var data = NetMatch.FromRules(rules);
        if (m_sentRules.HasValue && m_sentRules.Value.Equals(data) && Net.RulesValue.Equals(data)) return;
        Net.SetRules(rules);
        m_sentRules = data;
    }

    static string TimeLabel(float seconds) => seconds > 0.0f ? $"{seconds:0}秒" : "なし";

    static string RulesText(MatchSetup.MatchRules r)
    {
        string time = "時間 " + TimeLabel(r.TimeLimit);
        switch (r.mode)
        {
            case MatchSetup.MatchMode.Stock: return $"ストック {r.stocks}機 ・ {time}";
            case MatchSetup.MatchMode.Time: return $"タイム {TimeLabel(r.TimeLimit)}";
            default: return $"{r.ModeName} ・ {time}";
        }
    }

    //========================================
    // 右上の表示
    //========================================

    void UpdateInfo()
    {
        var net = Net;
        bool host = IsHost;
        m_infoTag.color = host ? ToyKit.Palette[1] : ToyKit.Palette[3];
        m_infoTagText.text = host ? "ホスト" : "さんか";
        int room = host ? NetRoom.Current : NetRoom.JoinedRoom;
        m_infoRoom.text = room > 0 ? $"へやばんごう {room}" : "";
        m_infoRules.text = net != null ? RulesText(net.Rules) : "";
        string phase = net == null ? "" : !net.IsStarted ? "開始待ち" : net.Match != null && net.Match.IsFinished ? "試合終了" : "試合中";
        m_infoState.text = net != null ? $"{phase} ・ {net.PlayerCount}人" : "";
        m_infoHint.text = net != null && !net.IsStarted
            ? (host ? "Start：メニュー(試合を はじめる・設定)" : "ホストが はじめるのを まっています　Start：メニュー")
            : "Start：メニュー";
        // 試合中はじゃまにならないように薄くする
        var g = m_info.GetComponent<CanvasGroup>();
        g.alpha = Mathf.MoveTowards(g.alpha, net != null && net.IsStarted && !IsOpen ? 0.55f : 1.0f, Time.unscaledDeltaTime * 3.0f);
    }

    //========================================
    // メニュー: 開く・閉じる
    //========================================

    void Open()
    {
        IsOpen = true;
        m_openFrame = Time.frameCount;
        m_openTime = Time.unscaledTime;
        m_confirming = false;
        m_picking = false;
        RebuildItems();
        m_index = 0;   // 開いたら一番上(ホストの開始待ちなら「試合を はじめる」)から
        m_menuTag.color = IsHost ? ToyKit.Palette[1] : ToyKit.Palette[3];
        m_menuTagText.text = IsHost ? "ホスト" : "さんか";
        m_menu.gameObject.SetActive(true);
        // メニューの左右でステージが切り替わらないように・開発用の表示がメニューの上に出ないようにする
        m_hidden.Clear();
        foreach (var b in FindObjectsByType<MonoBehaviour>())
        {
            if (b.enabled && kHideWhileOpen.Contains(b.GetType().Name))
            {
                b.enabled = false;
                m_hidden.Add(b);
            }
        }
        MenuSounds.Decide();
    }

    void Close(bool sound = true)
    {
        IsOpen = false;
        m_picking = false;
        m_info.gameObject.SetActive(true);
        m_menu.gameObject.SetActive(false);
        RestoreHidden();
        if (sound) MenuSounds.Cancel();
    }

    void RestoreHidden()
    {
        // ステージの切り替えは、試合が終わったら NetMatch が止めているので、そのままにする
        bool finished = Net != null && Net.Match != null && Net.Match.IsFinished;
        foreach (var b in m_hidden)
        {
            if (b == null) continue;
            if (finished && b is Nakagawa.StageSwitcher) continue;
            b.enabled = true;
        }
        m_hidden.Clear();
    }

    // いまの状態で出す項目
    void RebuildItems()
    {
        var items = new List<Item>();
        if (CanEditRules) items.Add(Item.Start);
        // キャラクターはだれでも(試合中は薄くして選べない)
        if (NetCharacterList.Instance != null) items.Add(Item.Character);
        if (CanEditRules)
        {
            items.Add(Item.Mode);
            // ストックの行はいつも出しておく(モードで板の大きさが変わらないように)。ストック以外では薄くして選べない
            items.Add(Item.Stocks);
            items.Add(Item.Time);
        }
        items.Add(Item.Leave);
        items.Add(Item.Close);
        bool same = items.Count == m_items.Count;
        for (int i = 0; same && i < items.Count; i++) same = items[i] == m_items[i];
        if (same) return;

        Item current = m_index < m_items.Count ? m_items[m_index] : Item.Close;
        foreach (var img in m_itemImages) Destroy(img.gameObject);
        m_itemImages.Clear();
        m_itemTexts.Clear();
        m_itemValues.Clear();
        m_items.Clear();
        m_items.AddRange(items);

        // 項目の数に合わせてパネルの高さを決める(上に「メニュー」、下に操作の説明)
        const float top = 190.0f, step = 88.0f, bottom = 100.0f;
        float height = top + step * items.Count + bottom - 12.0f;
        m_panel.sizeDelta = new Vector2(680.0f, height);
        for (int i = 0; i < items.Count; i++)
        {
            var item = ToyKit.UIRounded($"Item{i}", m_panel, kItemColor, 20.0f);
            ToyKit.Anchor(item, new Vector2(0.5f, 1.0f), new Vector2(0.0f, -top - i * step + 6.0f), new Vector2(560.0f, 74.0f));
            m_itemImages.Add(item.GetComponent<Image>());
            // 設定の行は左に名前・右に「＜ 値 ＞」(キャラクターは右にいまのキャラ)、ほかは真ん中に名前
            bool value = IsValue(items[i]) || items[i] == Item.Character;
            var t = Text("Text", item, "", 34.0f, Color.white, m_letterMat);
            ToyKit.Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(36.0f, 0.0f);
            t.rectTransform.offsetMax = new Vector2(-36.0f, 0.0f);
            if (value) t.alignment = TextAlignmentOptions.Left;
            m_itemTexts.Add(t);
            var v = Text("Value", item, "", 34.0f, Color.white, m_letterMat);
            ToyKit.Stretch(v.rectTransform);
            v.rectTransform.offsetMin = new Vector2(36.0f, 0.0f);
            v.rectTransform.offsetMax = new Vector2(-36.0f, 0.0f);
            v.alignment = TextAlignmentOptions.Right;
            v.gameObject.SetActive(value);
            m_itemValues.Add(v);
        }
        m_index = Mathf.Max(0, m_items.IndexOf(current));
        m_confirm.SetAsLastSibling();
        m_picker.SetAsLastSibling();
    }

    //========================================
    // メニュー: 操作
    //========================================

    void UpdateMenu()
    {
        // 試合が始まった・終わったなどで項目が変わったら作り直す
        RebuildItems();
        if (m_picking)
        {
            UpdatePicker();
            return;
        }
        if (m_confirming)
        {
            if (Pressed(Dir.Left) || Pressed(Dir.Right) || Pressed(Dir.Up) || Pressed(Dir.Down)) { m_confirmIndex = 1 - m_confirmIndex; MenuSounds.Cursor(); }
            if (PressedSubmit())
            {
                if (m_confirmIndex == 0) Leave();
                else { m_confirming = false; MenuSounds.Cancel(); }
            }
            else if (PressedCancel()) { m_confirming = false; MenuSounds.Cancel(); }
            return;
        }

        if (Pressed(Dir.Up)) { Move(-1); MenuSounds.Cursor(); }
        if (Pressed(Dir.Down)) { Move(1); MenuSounds.Cursor(); }
        // モードを変えて、いまの行が選べなくなったとき(念のため)
        if (!Selectable(m_items[m_index])) Move(1);
        if (PressedToggle() || PressedCancel()) { Close(); return; }

        Item item = m_items[m_index];
        int change = Pressed(Dir.Left) ? -1 : Pressed(Dir.Right) ? 1 : 0;
        if (PressedSubmit() && IsValue(item)) change = 1;   // A でも次の値へ
        if (change != 0 && IsValue(item))
        {
            ChangeValue(item, change);
            MenuSounds.Cursor();
            return;
        }
        if (!PressedSubmit()) return;

        switch (item)
        {
            case Item.Start:
                MenuSounds.Decide();
                Net.StartMatch(BuildRules());
                Close(false);
                break;
            case Item.Character:
                OpenPicker();
                break;
            case Item.Leave:
                m_confirming = true;
                m_confirmIndex = 1;   // 最初は「いいえ」
                m_confirmText.text = IsHost
                    ? "へやを かいさんしますか？\n<size=70%>みんなの せつぞくが 切れます</size>"
                    : "へやから ぬけますか？";
                MenuSounds.Decide();
                break;
            default:
                Close();
                break;
        }
    }

    static bool IsValue(Item item) => item == Item.Mode || item == Item.Stocks || item == Item.Time;

    // ストックの行はストックのときだけ、キャラクターは開始待ちの間だけ選べる
    static bool Selectable(Item item)
    {
        if (item == Item.Stocks) return s_mode == MatchSetup.MatchMode.Stock;
        if (item == Item.Character) return CanPickCharacter;
        return true;
    }

    // 自分のキャラを変えられるか(開始待ちの間で、自分の1人分がいる)
    static bool CanPickCharacter => Net != null && !Net.IsStarted && NetMatchPlayer.Local != null && NetCharacterList.Instance != null;

    // 選べる行だけをたどる
    void Move(int delta)
    {
        for (int n = 0; n < m_items.Count; n++)
        {
            m_index = (m_index + m_items.Count + delta) % m_items.Count;
            if (Selectable(m_items[m_index])) return;
        }
    }

    void ChangeValue(Item item, int delta)
    {
        switch (item)
        {
            case Item.Mode:
                s_mode = (MatchSetup.MatchMode)(((int)s_mode + 3 + delta) % 3);
                break;
            case Item.Stocks:
                s_stocks = (s_stocks - 1 + kMaxStocks + delta) % kMaxStocks + 1;
                break;
            case Item.Time:
                s_timeIndex = (s_timeIndex + kTimeLimits.Length + delta) % kTimeLimits.Length;
                // タイムは「なし」を選べないので飛ばす
                if (s_mode == MatchSetup.MatchMode.Time && kTimeLimits[s_timeIndex] <= 0.0f) s_timeIndex = (s_timeIndex + kTimeLimits.Length + delta) % kTimeLimits.Length;
                break;
        }
    }

    // かいさん(ホスト)・ぬける(さんか)。切れたら NetLobbyUI が接続画面(または最初の画面)に戻す
    void Leave()
    {
        MenuSounds.Decide();
        Close(false);
        if (m_nm != null) m_nm.Shutdown();
    }

    string ItemLabel(Item item)
    {
        switch (item)
        {
            case Item.Start: return "試合を はじめる";
            case Item.Character: return "キャラクター";
            case Item.Mode: return "モード";
            case Item.Stocks: return "ストック";
            case Item.Time: return "時間";
            case Item.Leave: return IsHost ? "へやを かいさん" : "へやから ぬける";
            default: return "とじる";
        }
    }

    // 設定の行の右に出す「＜ 値 ＞」
    static string ItemValue(Item item)
    {
        switch (item)
        {
            case Item.Mode: return $"＜ {new MatchSetup.MatchRules { mode = s_mode }.ModeName} ＞";
            case Item.Stocks: return Selectable(item) ? $"＜ {s_stocks}機 ＞" : "ー";
            case Item.Time: return $"＜ {TimeLabel(TimeLimit)} ＞";
            case Item.Character: return CanPickCharacter ? CurrentCharacterName : "試合中は 変えられません";
            default: return "";
        }
    }

    // 自分のいまのキャラの名前
    static string CurrentCharacterName
    {
        get
        {
            var local = NetMatchPlayer.Local;
            var list = NetCharacterList.Instance;
            var data = local != null && list != null ? list.Character(local.CharacterIndex) : null;
            return data != null ? data.displayName : "だるま";
        }
    }

    //========================================
    // キャラクターを選ぶ画面
    //========================================

    void OpenPicker()
    {
        var list = NetCharacterList.Instance;
        var local = NetMatchPlayer.Local;
        if (list == null || local == null || list.Count == 0) return;
        m_picking = true;
        // いまのキャラから(なければ選べる最初のキャラ)
        m_pickIndex = local.CharacterIndex < list.Count ? local.CharacterIndex : 0;
        if (!list.IsAvailable(m_pickIndex)) MovePick(1, 0);
        m_picker.gameObject.SetActive(true);
        MenuSounds.Decide();
    }

    void ClosePicker(bool sound = true)
    {
        m_picking = false;
        m_picker.gameObject.SetActive(false);
        m_info.gameObject.SetActive(true);
        if (sound) MenuSounds.Cancel();
    }

    void UpdatePicker()
    {
        // 試合が始まったら選べない
        if (!CanPickCharacter)
        {
            ClosePicker(false);
            return;
        }
        if (PressedToggle()) { Close(); return; }
        if (PressedCancel()) { ClosePicker(); return; }

        int dx = Pressed(Dir.Left) ? -1 : Pressed(Dir.Right) ? 1 : 0;
        int dy = Pressed(Dir.Up) ? -1 : Pressed(Dir.Down) ? 1 : 0;
        if (dx != 0 || dy != 0)
        {
            MovePick(dx, dy);
            MenuSounds.Cursor();
        }
        if (!PressedSubmit()) return;

        var list = NetCharacterList.Instance;
        if (!list.IsAvailable(m_pickIndex)) { MenuSounds.Cancel(); return; }
        NetMatchPlayer.Local.RequestCharacter(m_pickIndex);
        MenuSounds.Decide();
        ClosePicker(false);
    }

    // 選べるキャラだけをたどる(左右は並びの順、上下は1段ずつ)
    void MovePick(int dx, int dy)
    {
        var list = NetCharacterList.Instance;
        int n = list.Count;
        for (int tries = 0; tries < n; tries++)
        {
            if (dy != 0)
            {
                int rows = (n + kPickColumns - 1) / kPickColumns;
                int col = m_pickIndex % kPickColumns;
                int row = ((m_pickIndex / kPickColumns) + rows + dy) % rows;
                m_pickIndex = Mathf.Min(row * kPickColumns + col, n - 1);
            }
            else m_pickIndex = (m_pickIndex + n + (dx != 0 ? dx : 1)) % n;
            if (list.IsAvailable(m_pickIndex)) return;
        }
    }

    void AnimatePicker(float pulse)
    {
        var list = NetCharacterList.Instance;
        var local = NetMatchPlayer.Local;
        if (list == null) return;
        for (int i = 0; i < m_cards.Count; i++)
        {
            var c = m_cards[i];
            bool on = i == m_pickIndex;
            bool available = list.IsAvailable(i);
            c.bg.color = on ? kSelectedColor : kItemColor;
            c.bg.rectTransform.localScale = Vector3.one * (on ? pulse : 1.0f);
            var data = list.Character(i);
            c.name.text = available && data != null ? data.displayName : "？？？";
            c.name.color = on ? ToyKit.Ink : available ? Color.white : new Color(1.0f, 1.0f, 1.0f, 0.3f);
            c.icon.color = available ? Color.white : new Color(0.1f, 0.1f, 0.1f, 0.8f);
            c.now.gameObject.SetActive(local != null && local.CharacterIndex == i);
        }
        var sel = list.Character(m_pickIndex);
        m_pickName.text = sel != null && list.IsAvailable(m_pickIndex) ? sel.displayName : "？？？";
        m_pickDesc.text = sel != null && list.IsAvailable(m_pickIndex) ? Description(sel) : "まだ 選べません";
    }

    static string Description(CharacterData c)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(c.passiveName) || !string.IsNullOrEmpty(c.passiveText)) lines.Add($"パッシブ：{c.passiveName}　{c.passiveText}");
        if (!string.IsNullOrEmpty(c.specialName) || !string.IsNullOrEmpty(c.specialText)) lines.Add($"必殺技：{c.specialName}　{c.specialText}");
        return lines.Count > 0 ? string.Join("\n", lines) : "";
    }

    void BuildPicker()
    {
        m_picker = ToyKit.UIRounded("CharacterPicker", m_menu, kPanelColor, 28.0f);
        ToyKit.Anchor(m_picker, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1320.0f, 860.0f));
        m_picker.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;

        var title = Text("Title", m_picker, "キャラクター", 60.0f, Color.white, m_popMat);
        ToyKit.Anchor(title.rectTransform, new Vector2(0.5f, 1.0f), new Vector2(0.0f, -60.0f), new Vector2(800.0f, 80.0f));

        var list = NetCharacterList.Instance;
        int n = list != null ? list.Count : 0;
        const float cardW = 220.0f, cardH = 230.0f, gapX = 240.0f, gapY = 250.0f;
        for (int i = 0; i < n; i++)
        {
            int col = i % kPickColumns, row = i / kPickColumns;
            var card = ToyKit.UIRounded($"Card{i}", m_picker, kItemColor, 20.0f);
            ToyKit.Anchor(card, new Vector2(0.5f, 1.0f), new Vector2((col - (kPickColumns - 1) * 0.5f) * gapX, -250.0f - row * gapY), new Vector2(cardW, cardH));
            var data = list.Character(i);

            var iconRt = ToyKit.UIImage("Icon", card, Color.white);
            ToyKit.Anchor(iconRt, new Vector2(0.5f, 1.0f), new Vector2(0.0f, -88.0f), new Vector2(150.0f, 150.0f));
            var icon = iconRt.GetComponent<Image>();
            icon.preserveAspect = true;
            TextMeshProUGUI letter = null;
            if (data != null && data.portrait != null) icon.sprite = data.portrait;
            else
            {
                // 顔の画像が無いキャラは、色と頭文字で仮に出す(キャラクターセレクトと同じ)
                icon.color = data != null ? data.color : Color.gray;
                letter = Text("Letter", iconRt, data != null && !string.IsNullOrEmpty(data.displayName) ? data.displayName.Substring(0, 1) : "?", 80.0f, Color.white, m_popMat);
                ToyKit.Stretch(letter.rectTransform);
            }

            var name = Text("Name", card, "", 32.0f, Color.white, m_letterMat);
            name.enableAutoSizing = true; name.fontSizeMin = 20.0f; name.fontSizeMax = 32.0f;
            ToyKit.Anchor(name.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(0.0f, 34.0f), new Vector2(cardW - 16.0f, 44.0f));

            var now = ToyKit.UIRounded("Now", card, ToyKit.Palette[1], 12.0f);
            ToyKit.Anchor(now, new Vector2(1.0f, 1.0f), new Vector2(-30.0f, -6.0f), new Vector2(84.0f, 40.0f));
            now.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
            var nowText = Text("Label", now, "いま", 26.0f, Color.white, m_popMat);
            ToyKit.Stretch(nowText.rectTransform);

            m_cards.Add(new Card { bg = card.GetComponent<Image>(), icon = icon, letter = letter, name = name, now = now });
        }

        int rows = (n + kPickColumns - 1) / kPickColumns;
        float descTop = -250.0f - (rows - 1) * gapY - cardH * 0.5f - 30.0f;
        m_pickName = Text("SelName", m_picker, "", 44.0f, kSelectedColor, m_letterMat);
        ToyKit.Anchor(m_pickName.rectTransform, new Vector2(0.5f, 1.0f), new Vector2(0.0f, descTop - 30.0f), new Vector2(1200.0f, 56.0f));
        m_pickDesc = Text("SelDesc", m_picker, "", 28.0f, kPale, m_letterMat);
        m_pickDesc.enableAutoSizing = true; m_pickDesc.fontSizeMin = 20.0f; m_pickDesc.fontSizeMax = 28.0f;
        ToyKit.Anchor(m_pickDesc.rectTransform, new Vector2(0.5f, 1.0f), new Vector2(0.0f, descTop - 100.0f), new Vector2(1200.0f, 80.0f));

        var hint = Text("Hint", m_picker, "←→↑↓：選ぶ　A：決定(すぐに 入れ替わります)　B：もどる", 28.0f, kSelectedColor, null);
        hint.enableAutoSizing = true; hint.fontSizeMin = 20.0f; hint.fontSizeMax = 28.0f;
        ToyKit.Anchor(hint.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(0.0f, 40.0f), new Vector2(1240.0f, 40.0f));

        m_picker.gameObject.SetActive(false);
    }

    //========================================
    // 入力(この画面を選んでいるときだけ。同じ PC で複数の画面を動かすときに裏の画面が動かないように)
    //========================================

    enum Dir { Up, Down, Left, Right }

    static bool Pressed(Dir dir)
    {
        if (!Application.isFocused) return false;
        foreach (var g in Gamepad.all)
        {
            var pad = dir == Dir.Up ? g.dpad.up : dir == Dir.Down ? g.dpad.down : dir == Dir.Left ? g.dpad.left : g.dpad.right;
            var stick = dir == Dir.Up ? g.leftStick.up : dir == Dir.Down ? g.leftStick.down : dir == Dir.Left ? g.leftStick.left : g.leftStick.right;
            if (pad.wasPressedThisFrame || stick.wasPressedThisFrame) return true;
        }
        var k = Keyboard.current;
        if (k == null) return false;
        switch (dir)
        {
            case Dir.Up: return k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame;
            case Dir.Down: return k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame;
            case Dir.Left: return k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame;
            default: return k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame;
        }
    }

    static bool PressedSubmit()
    {
        if (!Application.isFocused) return false;
        foreach (var g in Gamepad.all) if (g.buttonSouth.wasPressedThisFrame) return true;
        var k = Keyboard.current;
        return k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame);
    }

    static bool PressedCancel()
    {
        if (!Application.isFocused) return false;
        foreach (var g in Gamepad.all) if (g.buttonEast.wasPressedThisFrame) return true;
        var k = Keyboard.current;
        return k != null && (k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame);
    }

    // Start(+)・キーボードは Tab(試合のポーズと同じ)
    static bool PressedToggle()
    {
        if (!Application.isFocused) return false;
        foreach (var g in Gamepad.all) if (g.startButton.wasPressedThisFrame) return true;
        var k = Keyboard.current;
        return k != null && k.tabKey.wasPressedThisFrame;
    }

    //========================================
    // 見た目
    //========================================

    void Animate()
    {
        float t = Time.unscaledTime - m_openTime;
        // 開いたときに少し弾んで出る(ポーズ画面と同じ)
        float pop = Mathf.Clamp01(t / 0.2f);
        m_menuGroup.alpha = pop;
        m_panel.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1.0f, ToyKit.EaseOutBack(pop));

        float pulse = 1.0f + 0.04f * Mathf.Sin(t * 8.0f);
        for (int i = 0; i < m_items.Count; i++)
        {
            bool on = i == m_index;
            m_itemImages[i].color = on ? kSelectedColor : kItemColor;
            m_itemTexts[i].color = m_itemValues[i].color = on ? ToyKit.Ink : Selectable(m_items[i]) ? Color.white : new Color(1.0f, 1.0f, 1.0f, 0.3f);
            m_itemTexts[i].text = ItemLabel(m_items[i]);
            m_itemValues[i].text = ItemValue(m_items[i]);
            m_itemImages[i].rectTransform.localScale = Vector3.one * (on && !m_confirming ? pulse : 1.0f);
        }
        m_menuHint.text = m_items.Count > 0 && IsValue(m_items[m_index])
            ? "↑↓：選ぶ　←→ / A：変える　B / Start：とじる"
            : "↑↓：選ぶ　A：決定　B / Start：とじる";

        // キャラクターを選んでいる間は、メニューの板を隠して選ぶ画面だけを出す
        // 右上の表示も、選ぶ画面に重ならないように隠す
        m_panel.gameObject.SetActive(!m_picking);
        m_info.gameObject.SetActive(!m_picking);
        if (m_picking) AnimatePicker(pulse);

        m_confirm.gameObject.SetActive(m_confirming);
        for (int i = 0; i < m_confirmItems.Length; i++)
        {
            bool on = i == m_confirmIndex;
            m_confirmItems[i].color = on ? kSelectedColor : kItemColor;
            m_confirmTexts[i].color = on ? ToyKit.Ink : Color.white;
            m_confirmItems[i].rectTransform.localScale = Vector3.one * (on ? pulse : 1.0f);
        }
    }

    void Build()
    {
        m_canvas = ToyKit.CreateOverlayCanvas("NetSessionMenu", kSortingOrder);

        // ---- 右上: いまの設定 ----
        m_info = ToyKit.UIRounded("Info", m_canvas, kInfoColor, 24.0f);
        m_info.gameObject.AddComponent<CanvasGroup>();
        m_info.anchorMin = m_info.anchorMax = new Vector2(1.0f, 1.0f);
        m_info.pivot = new Vector2(1.0f, 1.0f);
        m_info.anchoredPosition = new Vector2(-24.0f, -24.0f);
        m_info.sizeDelta = new Vector2(540.0f, 214.0f);
        m_info.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;

        var tag = ToyKit.UIRounded("Tag", m_info, ToyKit.Palette[1], 16.0f);
        ToyKit.Anchor(tag, new Vector2(0.0f, 1.0f), new Vector2(92.0f, -40.0f), new Vector2(140.0f, 52.0f));
        tag.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
        m_infoTag = tag.GetComponent<Image>();
        m_infoTagText = Text("Label", tag, "", 32.0f, Color.white, m_popMat);
        ToyKit.Stretch(m_infoTagText.rectTransform);

        m_infoRoom = Text("Room", m_info, "", 30.0f, Color.white, m_letterMat);
        m_infoRoom.alignment = TextAlignmentOptions.Right;
        ToyKit.Anchor(m_infoRoom.rectTransform, new Vector2(1.0f, 1.0f), new Vector2(-150.0f, -40.0f), new Vector2(260.0f, 52.0f));

        m_infoRules = Text("Rules", m_info, "", 34.0f, Color.white, m_letterMat);
        m_infoRules.alignment = TextAlignmentOptions.Left;
        ToyKit.Anchor(m_infoRules.rectTransform, new Vector2(0.0f, 1.0f), new Vector2(270.0f, -98.0f), new Vector2(500.0f, 48.0f));

        m_infoState = Text("State", m_info, "", 28.0f, kPale, m_letterMat);
        m_infoState.alignment = TextAlignmentOptions.Left;
        ToyKit.Anchor(m_infoState.rectTransform, new Vector2(0.0f, 1.0f), new Vector2(270.0f, -140.0f), new Vector2(500.0f, 38.0f));

        // 操作の案内(縁取りなしで読みやすく)
        m_infoHint = Text("Hint", m_info, "", 26.0f, kSelectedColor, null);
        m_infoHint.alignment = TextAlignmentOptions.Left;
        m_infoHint.enableAutoSizing = true; m_infoHint.fontSizeMin = 18.0f; m_infoHint.fontSizeMax = 26.0f;
        ToyKit.Anchor(m_infoHint.rectTransform, new Vector2(0.0f, 1.0f), new Vector2(270.0f, -182.0f), new Vector2(500.0f, 36.0f));

        // ---- メニュー(ポーズ画面と同じ形: 暗くして、中央にパネル。上に名札と「メニュー」) ----
        m_menu = new GameObject("Menu", typeof(RectTransform)).GetComponent<RectTransform>();
        m_menu.SetParent(m_canvas, false);
        ToyKit.Stretch(m_menu);
        m_menuGroup = m_menu.gameObject.AddComponent<CanvasGroup>();
        m_menuGroup.blocksRaycasts = false;
        var dim = ToyKit.UIImage("Dim", m_menu, kDimColor);
        ToyKit.Stretch(dim);

        m_panel = ToyKit.UIRounded("Panel", m_menu, kPanelColor, 28.0f);
        ToyKit.Anchor(m_panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680.0f, 600.0f));
        m_panel.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;

        var mtag = ToyKit.UIRounded("Tag", m_panel, ToyKit.Palette[1], 18.0f);
        ToyKit.Anchor(mtag, new Vector2(0.5f, 1.0f), Vector2.zero, new Vector2(170.0f, 72.0f));
        mtag.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
        m_menuTag = mtag.GetComponent<Image>();
        m_menuTagText = Text("Label", mtag, "", 44.0f, Color.white, m_popMat);
        ToyKit.Stretch(m_menuTagText.rectTransform);

        var title = Text("Title", m_panel, "メニュー", 64.0f, Color.white, m_popMat);
        ToyKit.Anchor(title.rectTransform, new Vector2(0.5f, 1.0f), new Vector2(0.0f, -90.0f), new Vector2(600.0f, 80.0f));

        // 操作の案内(縁取りなしで読みやすく)
        m_menuHint = Text("Hint", m_panel, "", 28.0f, kSelectedColor, null);
        m_menuHint.enableAutoSizing = true; m_menuHint.fontSizeMin = 20.0f; m_menuHint.fontSizeMax = 28.0f;
        ToyKit.Anchor(m_menuHint.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(0.0f, 40.0f), new Vector2(640.0f, 40.0f));

        // 確かめる(かいさん・ぬける)
        m_confirm = ToyKit.UIRounded("Confirm", m_panel, new Color(0.14f, 0.11f, 0.09f, 0.98f), 24.0f);
        ToyKit.Anchor(m_confirm, new Vector2(0.5f, 0.5f), new Vector2(0.0f, -30.0f), new Vector2(600.0f, 300.0f));
        m_confirm.gameObject.AddComponent<Outline>().effectColor = kSelectedColor;
        m_confirmText = Text("Message", m_confirm, "", 38.0f, Color.white, m_letterMat);
        ToyKit.Anchor(m_confirmText.rectTransform, new Vector2(0.5f, 1.0f), new Vector2(0.0f, -85.0f), new Vector2(560.0f, 130.0f));
        m_confirmItems = new Image[kConfirmNames.Length];
        m_confirmTexts = new TextMeshProUGUI[kConfirmNames.Length];
        for (int i = 0; i < kConfirmNames.Length; i++)
        {
            var item = ToyKit.UIRounded($"Choice{i}", m_confirm, kItemColor, 20.0f);
            ToyKit.Anchor(item, new Vector2(0.5f, 0.0f), new Vector2((i - 0.5f) * 220.0f, 60.0f), new Vector2(190.0f, 70.0f));
            m_confirmItems[i] = item.GetComponent<Image>();
            m_confirmTexts[i] = Text("Text", item, kConfirmNames[i], 34.0f, Color.white, m_letterMat);
            ToyKit.Stretch(m_confirmTexts[i].rectTransform);
        }
        m_confirm.gameObject.SetActive(false);
        BuildPicker();
        m_menu.gameObject.SetActive(false);
        m_canvas.gameObject.SetActive(false);
    }

    TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color, Material mat)
    {
        var t = ToyKit.UIText(name, parent, m_font, text, size, color);
        if (mat != null) t.fontSharedMaterial = mat;
        return t;
    }
}
