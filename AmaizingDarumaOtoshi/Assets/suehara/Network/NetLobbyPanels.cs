using System.Collections.Generic;
using Nakahira;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ネット対戦のロビーの表示(NetLobbyUI が NetSessionMenu といっしょに自動で付ける)。
//  ・開始待ちの間: 画面下の各プレイヤーの枠(中川さんの MatchHud。ストック・点数)を隠し、同じ見た目の枠で全員の状態を出す
//      名札(1P)・顔とキャラ名・状態(準備中・準備ＯＫ・観戦・選んでいる・試合中・待機)・選んだステージ・ホストの印
//      観戦の人も並べる(MatchHud は試合に出る人だけ)
//  ・試合中      : いつもの MatchHud。試合中に入ってきて待っている人(待機)には、試合の上に待機画面(全員の状態)を重ねる
//                  Start のメニューで「観戦する」を選ぶと、待機画面を閉じて試合を見られる
public class NetLobbyPanels : MonoBehaviour
{
    static readonly Vector2 kPanelSize = new Vector2(300.0f, 128.0f);
    const float kSpacing = 24.0f;
    const float kBottom = 120.0f;
    const int kSortingOrder = 16;          // MatchHud(15)の上、メニュー(50)の下
    const int kWaitingSortingOrder = 40;   // 待機画面は開始のカウントダウン(30)などより上、メニュー(50)の下

    class Panel
    {
        public NetMatchPlayer Player;
        public RectTransform Root;
        public Outline Outline;
        public Image Tag, Portrait, StatusBg;
        public TextMeshProUGUI Label, Name, Status, Stage, Host;
        public RectTransform HostMark;
        public int ShownCharacter = -2;
    }

    TMP_FontAsset m_font;
    Material m_letterMat, m_popMat;
    RectTransform m_canvas, m_waitingCanvas, m_lobby, m_waiting, m_waitingList;
    TextMeshProUGUI m_waitingTitle, m_waitingHint;
    readonly List<Panel> m_lobbyPanels = new List<Panel>();
    readonly List<Panel> m_waitingPanels = new List<Panel>();
    readonly List<NetMatchPlayer> m_players = new List<NetMatchPlayer>();
    GameObject m_hud;

    void Start()
    {
        m_font = PresentationSettings.DefaultFont;
        m_letterMat = ToyKit.LetterMaterial(m_font);
        m_popMat = ToyKit.PopMaterial(m_letterMat);
        m_canvas = ToyKit.CreateOverlayCanvas("NetLobbyPanels", kSortingOrder);
        m_lobby = Layer("Lobby", m_canvas);

        // 待機画面(試合中に入ってきた人)
        m_waitingCanvas = ToyKit.CreateOverlayCanvas("NetLobbyWaiting", kWaitingSortingOrder);
        m_waiting = Layer("Waiting", m_waitingCanvas);
        var dim = ToyKit.UIImage("Dim", m_waiting, new Color(0.05f, 0.04f, 0.04f, 0.88f));
        ToyKit.Stretch(dim);
        m_waitingTitle = Text("Title", m_waiting, "いま 試合中です", 72.0f, Color.white, m_popMat);
        ToyKit.Anchor(m_waitingTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, 260.0f), new Vector2(1600.0f, 100.0f));
        var sub = Text("Sub", m_waiting, "試合が おわったら ロビーで さんか できます", 40.0f, new Color(1.0f, 1.0f, 1.0f, 0.85f), m_letterMat);
        ToyKit.Anchor(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, 175.0f), new Vector2(1600.0f, 60.0f));
        m_waitingList = Layer("List", m_waiting);
        m_waitingHint = Text("Hint", m_waiting, "Start：メニュー(観戦する・へやから ぬける)", 34.0f, new Color(1.0f, 0.82f, 0.25f), m_letterMat);
        ToyKit.Anchor(m_waitingHint.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(0.0f, 60.0f), new Vector2(1600.0f, 50.0f));
        m_waiting.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        SetHudVisible(true);
        if (m_canvas != null) Destroy(m_canvas.gameObject);
        if (m_waitingCanvas != null) Destroy(m_waitingCanvas.gameObject);
    }

    void LateUpdate()
    {
        var net = NetMatch.Instance;
        bool connected = net != null && net.IsSpawned;
        bool lobby = connected && !net.IsStarted;
        var local = NetMatchPlayer.Local;
        bool waiting = connected && net.IsStarted && local != null && local.Status == NetMatchPlayer.NetStatus.Waiting;

        // いる人(番号順)
        m_players.Clear();
        foreach (var p in NetMatchPlayer.All) if (p != null && p.IsSpawned) m_players.Add(p);
        m_players.Sort((a, b) => a.Index.CompareTo(b.Index));

        // 開始待ちの間は MatchHud(ストック・点数)を隠し、ロビーの枠を出す
        SetHudVisible(!lobby);
        m_lobby.gameObject.SetActive(lobby);
        if (lobby) SyncPanels(m_lobbyPanels, m_lobby, new Vector2(0.5f, 0.0f), kBottom);

        // 試合中に入ってきた人は、待機画面
        m_waiting.gameObject.SetActive(waiting && !NetSessionMenu.IsOpen);
        if (waiting) SyncPanels(m_waitingPanels, m_waitingList, new Vector2(0.5f, 0.5f), -10.0f);
    }

    // MatchHud の表示(試合の Match の子の「MatchHud」)を出す・隠す
    void SetHudVisible(bool visible)
    {
        if (m_hud == null)
        {
            var match = DarumaMatch.Current;
            var t = match != null ? match.transform.Find("MatchHud") : null;
            m_hud = t != null ? t.gameObject : null;
        }
        if (m_hud != null && m_hud.activeSelf != visible) m_hud.SetActive(visible);
    }

    // 全員分の枠をそろえて横に並べ、中身を新しくする
    void SyncPanels(List<Panel> panels, RectTransform parent, Vector2 anchor, float y)
    {
        bool changed = panels.Count != m_players.Count;
        for (int i = 0; !changed && i < panels.Count; i++) changed = panels[i].Player != m_players[i];
        if (changed)
        {
            foreach (var p in panels) if (p.Root != null) Destroy(p.Root.gameObject);
            panels.Clear();
            foreach (var player in m_players) panels.Add(CreatePanel(player, parent));
            float total = panels.Count * kPanelSize.x + (panels.Count - 1) * kSpacing;
            for (int i = 0; i < panels.Count; i++)
            {
                float x = -total * 0.5f + kPanelSize.x * 0.5f + i * (kPanelSize.x + kSpacing);
                ToyKit.Anchor(panels[i].Root, anchor, new Vector2(x, y), kPanelSize);
            }
        }
        foreach (var p in panels) UpdatePanel(p);
    }

    void UpdatePanel(Panel p)
    {
        var player = p.Player;
        var list = NetCharacterList.Instance;
        Color color = player.Color;
        p.Outline.effectColor = color;
        p.Tag.color = color;
        p.Label.text = player.Label;

        // 顔とキャラ名(変わったときだけ)
        if (p.ShownCharacter != player.CharacterIndex)
        {
            p.ShownCharacter = player.CharacterIndex;
            var data = list != null ? list.Character(player.CharacterIndex) : null;
            p.Name.text = data != null ? data.displayName : "";
            p.Portrait.sprite = data != null ? data.portrait : null;
            p.Portrait.enabled = p.Portrait.sprite != null;
        }

        // 状態
        StatusLook(player, out string status, out Color statusColor);
        p.Status.text = status;
        p.StatusBg.color = statusColor;

        // 選んだステージ(観戦・待機の人は出さない)
        var stage = list != null ? list.Stage(player.StageVote) : null;
        bool showStage = player.Status != NetMatchPlayer.NetStatus.Spectating && player.Status != NetMatchPlayer.NetStatus.Waiting;
        p.Stage.text = !showStage ? "" : stage != null ? $"ステージ：{stage.displayName}" : "ステージ：おまかせ";

        // ホストの印
        p.HostMark.gameObject.SetActive(player.IsHostPlayer);
    }

    static void StatusLook(NetMatchPlayer player, out string text, out Color color)
    {
        switch (player.Status)
        {
            case NetMatchPlayer.NetStatus.Ready: text = "準備ＯＫ！"; color = ToyKit.Palette[3]; break;
            case NetMatchPlayer.NetStatus.Spectating: text = "観戦"; color = new Color(0.45f, 0.55f, 0.85f); break;
            case NetMatchPlayer.NetStatus.Waiting: text = "待機中"; color = new Color(0.55f, 0.55f, 0.58f); break;
            case NetMatchPlayer.NetStatus.Playing: text = "試合中"; color = ToyKit.Palette[0]; break;
            case NetMatchPlayer.NetStatus.Picking: text = "えらんでいます"; color = ToyKit.Palette[2]; break;
            default:
                // ホストは準備完了が要らない(試合を はじめる人)
                if (player.IsHostPlayer) { text = "準備ＯＫ！"; color = ToyKit.Palette[3]; }
                else { text = "準備中"; color = new Color(0.45f, 0.42f, 0.4f); }
                break;
        }
    }

    // 枠1枚: MatchHud と同じ見た目(左に名札とキャラ、右に状態、下に選んだステージ、右上にホストの印)
    Panel CreatePanel(NetMatchPlayer player, RectTransform parent)
    {
        var p = new Panel { Player = player };
        p.Root = ToyKit.UIImage($"Panel_{player.Label}", parent, new Color(0.08f, 0.06f, 0.05f, 0.72f));
        p.Outline = p.Root.gameObject.AddComponent<Outline>();

        var tag = ToyKit.UIImage("Tag", p.Root, player.Color);
        ToyKit.Anchor(tag, new Vector2(0.0f, 1.0f), new Vector2(52.0f, -24.0f), new Vector2(88.0f, 36.0f));
        tag.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
        p.Tag = tag.GetComponent<Image>();
        p.Label = Text("Label", tag, player.Label, 26.0f, Color.white, m_letterMat);
        ToyKit.Stretch(p.Label.rectTransform);

        var face = ToyKit.UIImage("Portrait", p.Root, Color.white);
        p.Portrait = face.GetComponent<Image>();
        p.Portrait.preserveAspect = true;
        ToyKit.Anchor(face, new Vector2(0.0f, 0.0f), new Vector2(52.0f, 30.0f), new Vector2(48.0f, 48.0f));
        p.Name = Text("Name", p.Root, "", 18.0f, Color.white, m_letterMat);
        ToyKit.Anchor(p.Name.rectTransform, new Vector2(0.0f, 1.0f), new Vector2(56.0f, -56.0f), new Vector2(100.0f, 24.0f));
        p.Name.enableAutoSizing = true;
        p.Name.fontSizeMin = 10.0f;
        p.Name.fontSizeMax = 18.0f;

        // 状態(色つきの札)
        var statusBg = ToyKit.UIRounded("Status", p.Root, Color.gray, 14.0f);
        ToyKit.Anchor(statusBg, new Vector2(1.0f, 0.5f), new Vector2(-88.0f, 8.0f), new Vector2(160.0f, 52.0f));
        statusBg.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
        p.StatusBg = statusBg.GetComponent<Image>();
        p.Status = Text("Text", statusBg, "", 28.0f, Color.white, m_letterMat);
        p.Status.enableAutoSizing = true;
        p.Status.fontSizeMin = 16.0f;
        p.Status.fontSizeMax = 28.0f;
        ToyKit.Stretch(p.Status.rectTransform);

        // 選んだステージ
        p.Stage = Text("Stage", p.Root, "", 18.0f, new Color(1.0f, 1.0f, 1.0f, 0.85f), m_letterMat);
        p.Stage.enableAutoSizing = true;
        p.Stage.fontSizeMin = 10.0f;
        p.Stage.fontSizeMax = 18.0f;
        ToyKit.Anchor(p.Stage.rectTransform, new Vector2(1.0f, 0.0f), new Vector2(-88.0f, 18.0f), new Vector2(170.0f, 26.0f));

        // ホストの印(右上)
        p.HostMark = ToyKit.UIRounded("Host", p.Root, ToyKit.Palette[1], 10.0f);
        ToyKit.Anchor(p.HostMark, new Vector2(1.0f, 1.0f), new Vector2(-46.0f, 0.0f), new Vector2(84.0f, 32.0f));
        p.HostMark.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
        p.Host = Text("Text", p.HostMark, "ホスト", 20.0f, Color.white, m_letterMat);
        ToyKit.Stretch(p.Host.rectTransform);
        return p;
    }

    static RectTransform Layer(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        ToyKit.Stretch(rt);
        return rt;
    }

    TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color, Material mat)
    {
        var t = ToyKit.UIText(name, parent, m_font, text, size, color);
        if (mat != null) t.fontSharedMaterial = mat;
        t.alignment = TextAlignmentOptions.Center;
        return t;
    }
}
