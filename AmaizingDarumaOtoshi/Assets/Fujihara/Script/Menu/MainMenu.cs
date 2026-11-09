using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// メインメニュー。タイトルと同じおもちゃの世界で見せる（MenuScene は TitleClubRoomDressing で部室にしている）。
// ・始まり（dropIn）：項目の積み木が下（やめる）から順に上から降ってきて積み上がり、最後に顔が乗る（A で飛ばせる）
// ・左に「たたかう／サウンド／ビデオ／クレジット／やめる」の積み木が縦に積まれ、てっぺんにスマイル
//   ハンマーが選択中の積み木の横で構える（カーソル）
// ・右の看板に、選択中の項目の設定や説明が暗いプレビューで出ている
// ・たたかう／サウンド／ビデオ：ハンマーで叩き出すと積み木が看板の上に乗り、上の山はストンと1段落ちる
//   看板にスポットライトが当たって操作できるようになる。左の山は暗くなる。キャンセルで元に戻る
//   たたかう（battleStyle = Board のとき）：看板で「ローカル／オンライン」を選ぶ
// ・たたかう（battleStyle = KnockTwice のとき）：画面外へ叩き飛ばすと、右から「ローカル」「オンライン」の積み木が
//   飛んできて山の横に積まれる。選んだ方をもう一度叩き飛ばしてシーンへ。B で2つは飛んでいき、たたかうが戻ってくる
// ・クレジット：背景をボカしてスタッフロール ・やめる：画面外へ叩き飛ばして終了
// ・項目の並び・数・名前は items（ItemKind ごとに動きが決まる）。せってい＝サウンド＋ビデオを1枚の看板に、じっせき＝AchievementBook
// 共通の部品は ToyKit。
public class MainMenu : MonoBehaviour
{
    [Header("素材")]
    [SerializeField] TMP_FontAsset font;
    [Tooltip("角丸の画像（UI/Skin/UISprite）")]
    [SerializeField] Sprite uiSprite;
    [Tooltip("丸い画像（UI/Skin/Knob）")]
    [SerializeField] Sprite knobSprite;

    [Header("シーン")]
    [Tooltip("「たたかう」→「ローカル」で移動するシーン（キャラクターセレクト）")]
    [SerializeField] string battleSceneName = "SelectScene";
    [Tooltip("「たたかう」→「オンライン」で移動するシーン。空欄（または Build Settings に無い）なら「じゅんびちゅう」と出す")]
    [SerializeField] string onlineSceneName = "";

    [Tooltip("始まったとき、項目の積み木を下から順に上から降らせて積む（A で飛ばせる）")]
    [SerializeField] bool dropIn = true;

    public enum BattleStyle { KnockTwice, Board }
    [Tooltip("たたかうの選び方。KnockTwice：叩き飛ばすと「ローカル／オンライン」の積み木が飛んできて、もう一度叩く　Board：看板のボタンで選ぶ")]
    [SerializeField] BattleStyle battleStyle = BattleStyle.KnockTwice;

    // 項目の種類。並び順・数は items で自由に変えられる（同じ種類を2つ置かない）
    // Settings：サウンドとビデオを1枚の看板にまとめたもの　Sound／Video：別々の看板（前の形）
    // Book：キャラのずかん（roster が無ければクレジット）　Achievements：じっせき　Credits：クレジット
    public enum ItemKind { Battle, Settings, Book, Achievements, Quit, Sound, Video, Credits, None = 99 }

    [Serializable]
    public class MenuItem
    {
        public ItemKind kind;
        [Tooltip("積み木に書く名前")]
        public string label;
        [Tooltip("看板の説明（たたかう・せってい・サウンド・ビデオは看板に操作画面が出るので使わない）")]
        [TextArea(1, 3)] public string description;
        [Tooltip("下の帯に出す説明")]
        [TextArea(1, 3)] public string help;
        [Tooltip("ハンマーの位置のずらし（再生中に変えてもすぐ反映される。x：右　y：上　z：奥）")]
        public Vector3 hammerOffset;
        [Tooltip("看板を操作している間も、看板の見出しに名前を出す")]
        public bool titleInPanel = true;
        public MenuItem() { }
        public MenuItem(ItemKind kind, string label, string description, string help, float hammerY)
        {
            this.kind = kind; this.label = label; this.description = description; this.help = help;
            hammerOffset = new Vector3(0f, hammerY, 0f);
        }
    }

    [Header("項目（上から）")]
    [Tooltip("メニューの積み木（上から）。種類ごとの動きは ItemKind で決まる。看板と積み木は始まったときに作るので、再生中に変えたら再生し直す")]
    [SerializeField] MenuItem[] items =
    {
        new MenuItem(ItemKind.Battle, "たたかう", "だるまを積んで、飛ばして、\n最後の一人を落とせ！",
                     "だるまを叩き合って、相手の頭を落とせば勝ち！\nローカルかオンラインで遊べます。", -0.4f),
        new MenuItem(ItemKind.Settings, "せってい", "", "音量と画面の設定を変更します。", -0.3f),
        new MenuItem(ItemKind.Book, "ずかん", "キャラクターの\n性能を見る", "キャラクターの名前・パッシブ・必殺技が見られます。", -0.2f),
        new MenuItem(ItemKind.Achievements, "じっせき", "これまでの\nきろくを 見る", "集めた実績が見られます。", -0.1f),
        new MenuItem(ItemKind.Quit, "やめる", "ゲームをおわります", "ゲームを終了します。", 0f),
    };
    [Tooltip("ずかんに並べるキャラクター（セレクト画面と同じ SelectRoster）。未設定ならずかんの代わりにクレジットを流す")]
    [SerializeField] SelectRoster roster;
    [Tooltip("カメラの構図を、下の帯（説明＋操作ガイド）より上の範囲に収める割合。1：構図まるごと帯の上に収める（少し引いて上へ寄せる）　0：帯を気にせず画面いっぱい")]
    [SerializeField, Range(0f, 1f)] float fitAboveBand = 1f;

    // 看板の行1つ分：左に出す名前と、選んでいるとき下の帯に出す説明
    [Serializable]
    public class RowText
    {
        public string label;
        [TextArea(1, 3)] public string help;
        public RowText() { }
        public RowText(string label, string help) { this.label = label; this.help = help; }
    }

    // 画面に出すそのほかの文字（項目の名前・説明・クレジットは上の欄）
    [Serializable]
    public class MenuTexts
    {
        [Header("たたかう：ローカル／オンライン（上から ローカル、オンライン）")]
        [Tooltip("積み木・看板のボタンに書く名前")]
        public string[] choiceLabels = { "ローカル", "オンライン" };
        [Tooltip("選んでいるとき看板に出す短い説明（KnockTwice のとき）／看板のボタンの下の小さい字（Board のとき）")]
        [TextArea(1, 3)] public string[] choiceBoardTexts = { "この画面で\nみんなで あそぶ", "ネットで\nだれかと あそぶ" };
        [Tooltip("選んでいるとき下の帯に出す説明")]
        [TextArea(1, 3)] public string[] choiceHelps =
        {
            "1台のパソコンにコントローラーをつないで、最大4人で遊びます。CPU も参加できます。",
            "インターネットで、ほかのプレイヤーと対戦します。",
        };
        [Tooltip("たたかうにカーソルがあるとき看板に出す説明")]
        [TextArea(1, 3)] public string battleBoardText = "ローカル か オンライン で\nたたかう";
        [Tooltip("オンラインがまだ遊べないときのお知らせ")]
        public string onlineNotReady = "オンラインは じゅんびちゅう！";

        [Header("サウンド・ビデオの行（上から）")]
        public RowText[] soundRows =
        {
            new RowText("マスター", "ゲーム全体の音量です。"),
            new RowText("BGM", "音楽の音量です。"),
            new RowText("効果音", "叩く音や撃墜の音などの音量です。"),
        };
        public RowText[] videoRows =
        {
            new RowText("画面モード", "フルスクリーンかウィンドウかを切り替えます。"),
            new RowText("解像度", "画面の解像度を変更します。"),
            new RowText("垂直同期", "オンにすると、画面のちらつき（ティアリング）を抑えます。"),
        };

        [Header("右下の操作ガイド（キーの字と説明）")]
        public string keySelect = "上下";
        public string guideSelect = "選ぶ";
        public string keyChange = "左右";
        public string guideChange = "変える";
        public string keyDecide = "A";
        public string guideDecide = "決定";
        public string keyBack = "B";
        public string guideBack = "戻る";

        [Header("ずかん（クレジットの項目で開く）")]
        public CharacterBook.Texts book = new CharacterBook.Texts();

        [Header("じっせき")]
        public AchievementBook.Texts achievements = new AchievementBook.Texts();

        [Header("看板の飾り")]
        [Tooltip("得点板の色帯の両はしの札（左、右）。showScoreNumbers をオンにしたときだけ出す")]
        public string[] scoreNumbers = { "0", "1" };
        [Tooltip("得点板の色帯の両はしに番号の札を出す")]
        public bool showScoreNumbers = false;
        [Tooltip("ノートの右上のメモ")]
        public string notebookMemo = "だるま部　れんしゅうノート";
    }
    [Tooltip("ローカル／オンライン・看板の行・操作ガイド・看板の飾りの文字（▶ で開く）。看板と積み木の文字は始まったときに作るので、再生中に変えたら再生し直す")]
    [SerializeField] MenuTexts texts = new MenuTexts();

    // 配列の i 番目（足りなければ空欄）
    static string At(string[] a, int i) => a != null && i >= 0 && i < a.Length ? a[i] : "";
    static RowText RowAt(RowText[] a, int i) => a != null && i >= 0 && i < a.Length && a[i] != null ? a[i] : new RowText("", "");

    [TextArea(5, 20)]
    [SerializeField] string creditsText =
        "<size=150%>アメイジング だるま落とし</size>\n\n\n<size=110%>プログラム</size>\nFujihara\nInoue\nShitaka\nNakahira\n\n\n<size=110%>フォント</size>\nYuji Syuku (SIL Open Font License)\n\n\n\n<size=120%>Thank you for playing!</size>";

    [Header("大きさ・配置")]
    [SerializeField] float stackX = -2.9f;
    [SerializeField] float hammerScale = 0.75f;
    [SerializeField] Vector3 boardCenter = new Vector3(2.9f, 2.35f, 1.0f);
    [SerializeField] Vector2 boardSize = new Vector2(6.4f, 3.9f);

    // 右の看板の見た目（中身の UI・位置・大きさはどれも同じ）
    public enum BoardLook { ScoreBoard, Notebook, Whiteboard }
    [Tooltip("ScoreBoard：卓上のめくり式の得点板　Notebook：ブックスタンドに立てた部活ノート　Whiteboard：前のホワイトボード\n" +
             "得点板とノートは、映す項目が変わるたびにページがめくれる")]
    [SerializeField] BoardLook boardLook = BoardLook.ScoreBoard;

    [Header("看板のページめくり（得点板・ノート）")]
    [Tooltip("めくる時間（秒）。長いほど目にやさしい")]
    [SerializeField, Range(0.15f, 1.5f)] float flipTime = 0.5f;
    [Tooltip("めくってからこの秒数のうちにまた中身が変わったら、めくらずに中身だけをふわっと入れ替える（連打・長押しでチカチカしないように）")]
    [SerializeField, Range(0f, 2f)] float flipCooldown = 0.6f;
    [Tooltip("めくる紙の曲がりの半径（看板の高さに対する割合）。大きいほど硬い紙に見える（ふつうの紙より少し硬め：0.15）")]
    [SerializeField, Range(0.05f, 0.3f)] float paperCurl = 0.15f;
    PaperFlipPage flipPaper;

    [Header("カメラ（再生中に変えてもすぐ反映される）")]
    [Tooltip("ふだん：左の山と右の看板")]
    [SerializeField] Shot browseShot = new Shot { look = new Vector3(-0.55f, 2.8f, 0.5f), distance = 11f, width = 13.6f };
    [Tooltip("サウンド・ビデオを操作しているとき：看板と、上に乗った積み木")]
    [SerializeField] Shot panelShot = new Shot { look = new Vector3(2.9f, 2.9f, 1f), distance = 10f, width = 12.5f };
    [Tooltip("たたかうで、ローカル／オンラインを選んでいるとき：2つの積み木（下まで）と看板")]
    [SerializeField] Shot choiceShot = new Shot { look = new Vector3(2.5f, 2.1f, 0.3f), distance = 10f, width = 13.5f };
    [Tooltip("見下ろす具合（距離 1 あたりのカメラの高さ。0 で見る点と同じ高さから水平に見る）")]
    [SerializeField, Range(-0.3f, 0.5f)] float camRise = 0.03f;

    [Tooltip("部屋を斜めから見る角度。カメラを右へ回り込ませて左を向かせ、積み木・看板・ハンマーも一緒に回してカメラの正面に向ける")]
    [SerializeField, Range(-45f, 45f)] float viewYaw = 18f;

    // 項目 i の種類（範囲外は None）
    ItemKind Kind(int i) => items != null && i >= 0 && i < items.Length && items[i] != null ? items[i].kind : ItemKind.None;
    MenuItem ItemAt(int i) => items != null && i >= 0 && i < items.Length && items[i] != null ? items[i] : null;
    int ItemCount => items != null ? items.Length : 0;
    // 看板に操作画面が出る（叩くと看板の上に乗る）種類
    static bool IsPanelKind(ItemKind k) => k == ItemKind.Battle || k == ItemKind.Settings || k == ItemKind.Sound || k == ItemKind.Video;

    static readonly Vector3 BlockSize = new Vector3(2.6f, 0.72f, 1.1f);
    const float BoardDepth = 0.3f;
    const float CanvasScale = 0.005f;   // 看板の UI：1ピクセル = 5mm
    const float FlyTime = 0.6f, HangTime = 0.05f, DropTime = 0.12f, ReturnTime = 0.55f;
    const float IdleYaw = -32f, CursorOffset = 0.3f, CursorScale = 1.06f;

    enum Phase { Intro, Browse, Hitting, Flying, Panel, Returning, Credits, Leaving, Choose, ChoiceLeaving, ChoiceBack }

    // ---- 始まり：積み木が上から降ってくる ----
    const float DropFall = 0.38f, DropHeight = 9f, DropInterval = 0.16f;
    float[] dropAt;                 // それぞれの積み木が落ち始める時刻（sim）
    bool[] dropLanded;
    float headDropAt = -100f;
    bool headDropLanded = true;
    enum Content { Battle, Sound, Video, Description, Settings }

    Phase phase;
    float sim;

    // ---- 舞台 ----
    Camera cam;
    ToyKit.Post post;
    ToyKit.Fx fx;
    ToyKit.Face face;
    Transform hammer, board;
    // メニューの物（積み木・顔・ハンマー・看板）とカメラを入れる空間。原点を中心に viewYaw だけ回してある
    // 中の位置はすべてこの空間での位置（前と同じ数字のまま）。部屋（TitleClubRoomDressing）は回さない
    Transform space;
    Light boardLight;
    Transform[] blocks;
    Material[] blockMats;
    Color[] blockColors;
    Material letterMat;

    // ---- 積み木の状態 ----
    int cursor;
    float[] offsetX, scaleNow;
    int outIdx = -1;                    // 叩き出されている項目
    float impactTime = -100f, returnTime = -100f, dockTime = -100f;
    int wobbleIdx = -1;
    float wobbleTime = -100f;
    float dim, panelLit;
    float boardPunch = -100f;

    // ---- 看板のページめくり（得点板・ノート） ----
    Transform flipPage;                 // めくれていくページ（上の綴じ目が軸）
    float flipStart = -100f;
    int flipDir = 1;                    // 1：前のページが後ろへめくれて次が出る（下へ）　-1：後ろからページが戻ってくる（上へ）
    int navDir = 1;                     // 最後にカーソルを動かした向き（1：下　-1：上）
    int shownKey = -1;                  // いま映している中身（変わったらめくる）
    CanvasGroup boardContent;           // 看板の中身（ページが戻ってくる間は隠す）
    float boardTop;                     // 看板のいちばん上（板の上端からの高さ。積み木が乗る）

    // ---- ハンマー ----
    bool swinging;
    float hammerYaw = IdleYaw;
    float recoverStart = -100f;
    Vector3 hammerPos;

    // ---- 顔 ----
    float nextBlink = 2f;
    Quaternion headRot = Quaternion.identity;

    // ---- カメラ ----
    Vector3 camPos, camLook;
    float camDist, camWidth, shake;

    // 構図（メニューの空間で）：見る点・距離・横に映す幅（見る点の奥行きで）
    // 横の幅で決めるので、画面の縦横比が変わっても左右の収まりは変わらない（画角は縦横比から求める）
    [Serializable]
    public struct Shot
    {
        [Tooltip("カメラが見る点（画面の真ん中に来る）。x：右　y：上　z：奥")]
        public Vector3 look;
        [Tooltip("見る点からカメラまでの距離。近いほど遠近感が強く、遠いほど正面から見たように平たくなる")]
        public float distance;
        [Tooltip("見る点の奥行きで、画面の左右に映す幅。小さいほど寄る")]
        public float width;
    }

    // ---- UI ----
    RectTransform overlay;
    Image fade;
    // 下の帯：上が説明の帯（暗い板に緑のふち）、下が操作ガイドの段（右下に、ボタンの形でそろえる）
    const float GuideBarH = 80f, HelpBandH = 170f;   // 1080 の画面での高さ
    CanvasGroup helpGroup;
    TextMeshProUGUI helpText;
    RectTransform guideRow;
    string guideKey;
    // 看板の中の行（音量の行・ビデオの行・ローカル／オンラインのボタン）→ 下の帯に出す説明
    readonly Dictionary<GameObject, string> rowHelps = new Dictionary<GameObject, string>();
    CanvasGroup titleGroup;
    CreditRoll credits;
    CharacterBook book;
    AchievementBook achievements;
    CanvasGroup battleGroup, soundGroup, videoGroup, settingsGroup, descGroup;
    TextMeshProUGUI descText, boardTitle, battleNote;
    Image boardDim;
    Selectable battleFirst, soundFirst, videoFirst, settingsFirst;
    bool starting;                  // たたかうでローカル／オンラインを決めた（シーン移動まで操作しない）
    float noteTime = -100f;         // 「じゅんびちゅう」を出した時刻

    // ---- たたかう（KnockTwice）：ローカル／オンラインの積み木 ----
    static readonly Vector3 ChoiceBase = new Vector3(1.6f, 0f, -1.2f);   // 2つの積み木を積む場所（山の右。間にハンマーが入る）
    const float ChoiceFlyIn = 0.5f;
    Transform[] choiceBlocks;
    Material[] choiceMats;
    Color[] choiceColors;
    float[] choiceArrive, choiceOffset, choiceScale;
    bool[] choiceLanded;
    int choiceCursor;
    int choiceOut = -1;                 // 叩き飛ばした方
    float choiceImpact = -100f, choiceAway = -100f, choiceWobble = -100f;
    int choiceWobbleIdx = -1;
    bool battleOut, battleBack;         // たたかうが画面外にいる／戻ってくるところ
    float battleBackTime = -100f;
    Mesh menuMesh;
    GameObject panelSelected;   // 看板の中でいま選んでいる行（行を移ったときの音用）

    float BH => BlockSize.y;

    void Start()
    {
        BuildStage();
        camLook = browseShot.look; camDist = browseShot.distance; camWidth = browseShot.width;
        camPos = CamPosition();
        hammerPos = HammerSpot(cursor);

        int n = blocks.Length;
        dropAt = new float[n];
        dropLanded = new bool[n];
        if (dropIn)
        {
            // 下の積み木（やめる）から順に、上から降ってきて積み上がる。最後に顔
            phase = Phase.Intro;
            for (int i = 0; i < n; i++) dropAt[i] = 0.55f + (n - 1 - i) * DropInterval;
            headDropAt = 0.55f + n * DropInterval + 0.1f;
            headDropLanded = false;
            hammerPos = OffscreenHammer();
        }
        else
        {
            phase = Phase.Browse;
            for (int i = 0; i < n; i++) { dropAt[i] = -100f; dropLanded[i] = true; }
        }
        StartCoroutine(FadeIn());
    }

    // 始まりの間、ハンマーは画面の左の外で待つ
    Vector3 OffscreenHammer() => HammerSpot(cursor) + new Vector3(-8f, 0f, 0f);

    // 項目 i の横で構えるハンマーの位置（Inspector の「ハンマーの位置のずらし」を足す）
    Vector3 HammerSpot(int i)
    {
        var it = ItemAt(i);
        return ToyKit.HammerPivot(Contact(i), hammerScale) + (it != null ? it.hammerOffset : Vector3.zero);
    }

    // 降ってくる高さ（0 で着地）
    float DropOffset(float at)
    {
        if (sim < at) return DropHeight;
        float u = Mathf.Clamp01((sim - at) / DropFall);
        return DropHeight * (1f - u * u);
    }

    float DropSquash(float at) => ToyKit.Squash((sim - at - DropFall) / 0.3f, 0.18f);

    // 着地の音と土けむり。全部（顔も）着地したらメニューを操作できる
    void UpdateIntro()
    {
        if (phase != Phase.Intro) return;
        for (int i = 0; i < blocks.Length; i++)
        {
            if (dropLanded[i] || sim < dropAt[i] + DropFall) continue;
            dropLanded[i] = true;
            Emit(fx.puff, Slot(i) - Vector3.up * BH * 0.5f, Quaternion.Euler(-90f, 0f, 0f), 6);
            shake = Mathf.Max(shake, 0.2f);
            MenuSounds.Play(MatchSounds.Instance.BlockHitBlock, 0.7f);
        }
        if (!headDropLanded && sim >= headDropAt + DropFall)
        {
            headDropLanded = true;
            Emit(fx.sparkle, new Vector3(stackX, ItemCount * BH + ToyKit.HeadR, 0f), Quaternion.Euler(-90f, 0f, 0f), 14);
            shake = Mathf.Max(shake, 0.3f);
            MenuSounds.Play(MatchSounds.Instance.BlockHitBlock, 0.9f);
        }
        if (headDropLanded && sim >= headDropAt + DropFall + 0.3f) phase = Phase.Browse;
    }

    // A などで始まりを飛ばす：全部その場に積み上がった状態にする
    void SkipIntro()
    {
        for (int i = 0; i < blocks.Length; i++) { dropAt[i] = -100f; dropLanded[i] = true; }
        headDropAt = -100f;
        headDropLanded = true;
        phase = Phase.Browse;
    }

    void OnDestroy() => Time.timeScale = 1f;

    // ================= 組み立て =================
    void BuildStage()
    {
        var blockMesh = ToyKit.RoundedBox(new Vector3(ToyKit.BlockW, ToyKit.BlockH, ToyKit.BlockD), 0.14f, 10);
        menuMesh = ToyKit.RoundedBox(BlockSize, 0.16f, 10);
        letterMat = ToyKit.LetterMaterial(font);
        space = new GameObject("MenuSpace").transform;
        space.rotation = Quaternion.Euler(0f, -viewYaw, 0f);

        cam = ToyKit.CreateCamera();
        ToyKit.CreateLights();
        post = ToyKit.CreatePostProcess(0.35f);
        ToyKit.CreateEnvironment(blockMesh);
        fx = ToyKit.CreateFx();

        // 積み木（上から）。マウスで選べるようにコライダーを付ける
        int n = ItemCount;
        blocks = new Transform[n];
        blockMats = new Material[n];
        blockColors = new Color[n];
        offsetX = new float[n];
        scaleNow = new float[n];
        var colors = new[] { ToyKit.Palette[0], ToyKit.Palette[1], ToyKit.Palette[2], ToyKit.Palette[3], ToyKit.Palette[4], ToyKit.Palette[5] };
        for (int i = 0; i < n; i++)
        {
            blockColors[i] = colors[i % colors.Length];
            blocks[i] = ToyKit.CreateBlock(ItemAt(i) != null ? ItemAt(i).label : "", blockColors[i], menuMesh, new Vector2(2.3f, 0.6f), BlockSize.z, font, letterMat, out blockMats[i]);
            blocks[i].SetParent(space, false);
            blocks[i].gameObject.AddComponent<BoxCollider>().size = BlockSize;
            scaleNow[i] = 1f;
        }

        // ローカル／オンラインの積み木（たたかうを叩き飛ばすと飛んでくる。最初は隠しておく）
        choiceBlocks = new Transform[2];
        choiceMats = new Material[2];
        choiceColors = new[] { ToyKit.Palette[0], ToyKit.Palette[5] };
        choiceArrive = new float[2];
        choiceOffset = new float[2];
        choiceScale = new float[2];
        choiceLanded = new bool[2];
        for (int i = 0; i < 2; i++)
        {
            choiceBlocks[i] = ToyKit.CreateBlock(At(texts.choiceLabels, i), choiceColors[i], menuMesh, new Vector2(2.3f, 0.6f), BlockSize.z, font, letterMat, out choiceMats[i]);
            choiceBlocks[i].SetParent(space, false);
            choiceBlocks[i].gameObject.AddComponent<BoxCollider>().size = BlockSize;
            choiceBlocks[i].gameObject.SetActive(false);
            choiceScale[i] = 1f;
        }

        face = ToyKit.CreateHead();
        face.root.SetParent(space, false);
        hammer = ToyKit.CreateHammer();
        hammer.SetParent(space, false);
        hammer.localScale = Vector3.one * hammerScale;

        BuildBoard();
        BuildOverlay();
    }

    // 右の看板（世界の中に立つ物。表面に設定画面の UI を貼る）。どの見た目でも、表の面は z = -BoardDepth / 2
    void BuildBoard()
    {
        board = new GameObject("Board").transform;
        board.SetParent(space, false);
        board.localPosition = boardCenter;
        if (boardLook == BoardLook.ScoreBoard) BuildScoreBoard();
        else if (boardLook == BoardLook.Notebook) BuildNotebook();
        else BuildWhiteboard();

        // 決定したときに看板を照らすスポットライト
        boardLight = new GameObject("BoardLight").AddComponent<Light>();
        boardLight.type = LightType.Spot;
        boardLight.color = new Color(1f, 0.95f, 0.85f);
        boardLight.spotAngle = 60f;
        boardLight.innerSpotAngle = 35f;
        boardLight.range = 20f;
        boardLight.intensity = 0f;
        boardLight.transform.SetParent(space, false);
        boardLight.transform.localPosition = boardCenter + new Vector3(-1f, 4.5f, -5f);
        boardLight.transform.localRotation = Quaternion.LookRotation(new Vector3(1f, -4.5f, 5f));

        // 表面の UI
        var canvasGo = new GameObject("BoardUI", typeof(RectTransform));
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        canvasGo.AddComponent<GraphicRaycaster>();
        var root = (RectTransform)canvasGo.transform;
        root.SetParent(board, false);
        root.sizeDelta = new Vector2(boardSize.x - 0.2f, boardSize.y - 0.2f) / CanvasScale;
        root.localScale = Vector3.one * CanvasScale;
        root.localPosition = new Vector3(0f, 0f, -BoardDepth * 0.5f - 0.01f);
        DecorateSurface(root);
        var surface = root;

        // 中身（見出し・操作画面・説明）はまとめて1つに入れる（ページが戻ってくる間だけ隠す）
        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(surface, false);
        root = (RectTransform)contentGo.transform;
        ToyKit.Stretch(root);
        boardContent = contentGo.AddComponent<CanvasGroup>();

        boardTitle = ToyKit.UIText("Title", root, font, "", 64f, ToyKit.Ink);
        ToyKit.Anchor(boardTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1000f, 100f));
        if (boardLook == BoardLook.ScoreBoard) boardTitle.color = Color.white;   // 赤い色帯の上

        battleGroup = BuildBattlePanel(root);
        soundGroup = BuildSoundPanel(root);
        videoGroup = BuildVideoPanel(root);
        settingsGroup = BuildSettingsPanel(root);

        var desc = new GameObject("Description", typeof(RectTransform));
        desc.transform.SetParent(root, false);
        ToyKit.Stretch((RectTransform)desc.transform);
        descGroup = desc.AddComponent<CanvasGroup>();
        descText = ToyKit.UIText("Text", desc.transform, font, "", 70f, ToyKit.Ink);
        descText.textWrappingMode = TextWrappingModes.Normal;
        ToyKit.Anchor(descText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1100f, 520f));

        // プレビュー中に暗くする幕（最前面）
        boardDim = ToyKit.UIImage("Dim", surface, new Color(0.2f, 0.15f, 0.15f, 0.5f), uiSprite).GetComponent<Image>();
        ToyKit.Stretch(boardDim.rectTransform);
    }

    // ---- 看板の見た目 ----
    Transform Part(Mesh mesh, string name, Material mat, Vector3 pos, Transform parent = null)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent != null ? parent : board, false);
        t.localPosition = pos;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return t;
    }

    Transform Prim(PrimitiveType type, string name, Material mat, Vector3 pos, Vector3 scale, Vector3 euler = default)
    {
        var t = ToyKit.Primitive(type, name, board);
        t.localPosition = pos;
        t.localScale = scale;
        t.localRotation = Quaternion.Euler(euler);
        t.GetComponent<Renderer>().sharedMaterial = mat;
        return t;
    }

    // 机の上面（y = 0）の、看板から見た高さ
    float TableY => -boardCenter.y;

    // 前のホワイトボード（白い板に脚2本）
    void BuildWhiteboard()
    {
        Part(ToyKit.RoundedBox(new Vector3(boardSize.x, boardSize.y, BoardDepth), 0.18f, 10), "Body", ToyKit.Lit(new Color(1f, 0.97f, 0.92f), 0.4f), Vector3.zero);
        float feetH = boardCenter.y - boardSize.y * 0.5f;
        var wood = ToyKit.Lit(new Color(0.96f, 0.86f, 0.66f), 0.45f);
        foreach (float x in new[] { -boardSize.x * 0.35f, boardSize.x * 0.35f })
            Prim(PrimitiveType.Cylinder, "Leg", wood, new Vector3(x, -boardSize.y * 0.5f - feetH * 0.5f, 0f), new Vector3(0.18f, feetH * 0.5f, 0.18f));
        boardTop = 0f;
    }

    // 卓上のめくり式の得点板：緑の台と2本の柱、上の横棒にリングでカードを吊るす。後ろにめくったカードが重なっている
    void BuildScoreBoard()
    {
        float w = boardSize.x, h = boardSize.y, front = -BoardDepth * 0.5f;
        float barY = h * 0.5f + 0.2f;
        var frame = ToyKit.Lit(new Color(0.16f, 0.4f, 0.31f), 0.35f);
        var metal = ToyKit.Lit(new Color(0.78f, 0.8f, 0.84f), 0.8f);
        var card = ToyKit.Lit(new Color(1f, 0.98f, 0.93f), 0.25f);

        // 台と柱と横棒
        Part(ToyKit.RoundedBox(new Vector3(w + 1.2f, 0.22f, 1.7f), 0.08f, 6), "Base", frame, new Vector3(0f, TableY + 0.11f, 0.25f));
        float postH = barY + 0.25f - TableY;
        foreach (float x in new[] { -w * 0.5f - 0.35f, w * 0.5f + 0.35f })
            Part(ToyKit.RoundedBox(new Vector3(0.26f, postH, 0.26f), 0.06f, 4), "Post", frame, new Vector3(x, TableY + postH * 0.5f, 0f));
        Prim(PrimitiveType.Cylinder, "Bar", metal, new Vector3(0f, barY, 0f), new Vector3(0.12f, w * 0.5f + 0.4f, 0.12f), new Vector3(0f, 0f, 90f));

        // カード（表）と、後ろにめくったカード
        var cardMesh = ToyKit.RoundedBox(new Vector3(w, h, 0.06f), 0.08f, 6);
        Part(cardMesh, "Card", card, new Vector3(0f, 0f, front + 0.03f));
        var back = ToyKit.Lit(new Color(0.93f, 0.9f, 0.84f), 0.25f);
        for (int i = 0; i < 2; i++)
        {
            var hinge = new GameObject("BackHinge").transform;
            hinge.SetParent(board, false);
            hinge.localPosition = new Vector3(0f, barY, 0.1f + i * 0.08f);
            hinge.localRotation = Quaternion.Euler(-6f - i * 5f, 0f, 0f);
            Part(cardMesh, "BackCard", back, new Vector3(0f, -barY, 0f), hinge);
        }

        // 横棒に通したリング
        for (int i = 0; i < 6; i++)
        {
            float x = (i - 2.5f) * (w / 6.5f);
            Prim(PrimitiveType.Cylinder, "Ring", metal, new Vector3(x, barY, 0f), new Vector3(0.3f, 0.025f, 0.3f), new Vector3(0f, 0f, 90f));
        }

        flipPage = MakeFlipPage(cardMesh, card, barY, front - 0.04f);
        boardTop = barY + 0.12f - h * 0.5f;
    }

    // ブックスタンドに立てた部活ノート（上がリング綴じ）。少し後ろにもたれている
    void BuildNotebook()
    {
        float w = boardSize.x, h = boardSize.y, front = -BoardDepth * 0.5f;
        board.localRotation = Quaternion.Euler(8f, 0f, 0f);
        var wood = ToyKit.Lit(new Color(0.8f, 0.6f, 0.4f), 0.4f);
        var cover = ToyKit.Lit(new Color(0.24f, 0.42f, 0.72f), 0.35f);
        var paper = ToyKit.Lit(new Color(1f, 0.99f, 0.96f), 0.15f);
        var metal = ToyKit.Lit(new Color(0.35f, 0.36f, 0.4f), 0.7f);

        // 表紙（後ろ）と紙の束
        Part(ToyKit.RoundedBox(new Vector3(w + 0.16f, h + 0.12f, 0.06f), 0.06f, 4), "Cover", cover, new Vector3(0f, -0.04f, front + 0.16f));
        var pageMesh = ToyKit.RoundedBox(new Vector3(w, h, 0.12f), 0.03f, 4);
        Part(pageMesh, "Pages", paper, new Vector3(0f, 0f, front + 0.06f));

        // 上のリング（紙を貫く輪）
        float ringY = h * 0.5f;
        for (int i = 0; i < 17; i++)
        {
            float x = (i - 8f) * (w / 17.5f);
            Prim(PrimitiveType.Cylinder, "Ring", metal, new Vector3(x, ringY, front + 0.08f), new Vector3(0.24f, 0.012f, 0.24f), new Vector3(0f, 0f, 90f));
        }

        // ブックスタンド：ノートの下を受ける棚と、机までの脚。後ろに支えの板
        // （板ごと傾けているので、机の上の物は傾きを戻して置く）
        var stand = new GameObject("Stand").transform;
        stand.SetParent(board, false);
        stand.localPosition = new Vector3(0f, -h * 0.5f - 0.1f, front + 0.05f);
        stand.rotation = space.rotation;
        Part(ToyKit.RoundedBox(new Vector3(w + 0.4f, 0.16f, 0.55f), 0.05f, 4), "Ledge", wood, new Vector3(0f, 0f, -0.1f), stand);
        Part(ToyKit.RoundedBox(new Vector3(w + 0.4f, 0.1f, 0.18f), 0.04f, 4), "Lip", wood, new Vector3(0f, 0.11f, -0.32f), stand);
        float legH = stand.position.y;   // 棚から机まで
        foreach (float x in new[] { -w * 0.4f, w * 0.4f })
            Part(ToyKit.RoundedBox(new Vector3(0.16f, legH, 0.16f), 0.04f, 4), "Leg", wood, new Vector3(x, -legH * 0.5f, 0f), stand);
        // 支えの板：ノートの裏の真ん中あたりから、後ろの机まで
        Vector3 a = board.TransformPoint(new Vector3(0f, 0f, front + 0.22f));
        Vector3 b = a + space.forward * 1.3f;
        b.y = 0f;
        var prop = Part(ToyKit.RoundedBox(new Vector3(w * 0.4f, 0.08f, (a - b).magnitude), 0.03f, 3), "Prop", wood, Vector3.zero, stand);
        prop.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(a - b));

        flipPage = MakeFlipPage(ToyKit.RoundedBox(new Vector3(w, h, 0.02f), 0.02f, 3), paper, ringY, front - 0.03f);
        boardTop = 0.14f;
    }

    // めくれていくページ：綴じ目（y = hingeY）から垂れ下がる紙。下の端を持って真上に持ち上げ、後ろへ送る（PaperFlipPage）。
    // ふだんは隠しておく。mesh は前の形（硬い板が回る）で使っていたもので、今は使わない
    Transform MakeFlipPage(Mesh mesh, Material mat, float hingeY, float z)
    {
        var hinge = new GameObject("FlipHinge").transform;
        hinge.SetParent(board, false);
        hinge.localPosition = new Vector3(0f, hingeY, z);
        flipPaper = PaperFlipPage.Create(hinge, boardSize.x, boardSize.y, hingeY - boardSize.y * 0.5f, mat);
        flipPaper.curl = paperCurl;
        hinge.gameObject.SetActive(false);
        return hinge;
    }

    // 表面の飾り（中身の UI より後ろ）：得点板は上の色帯、ノートは罫線と赤い余白線
    void DecorateSurface(RectTransform root)
    {
        if (boardLook == BoardLook.ScoreBoard)
        {
            var band = ToyKit.UIImage("Band", root, new Color(0.93f, 0.33f, 0.27f));
            band.anchorMin = new Vector2(0f, 1f); band.anchorMax = new Vector2(1f, 1f); band.pivot = new Vector2(0.5f, 1f);
            band.sizeDelta = new Vector2(-40f, 130f);
            band.anchoredPosition = new Vector2(0f, -10f);
            // 色帯の両はしの、めくり式の番号（showScoreNumbers のときだけ）
            if (texts.showScoreNumbers)
            foreach (float side in new[] { -1f, 1f })
            {
                var num = ToyKit.UIRounded("Num", band, new Color(1f, 0.98f, 0.93f), 14f);
                ToyKit.Anchor(num, new Vector2(side < 0f ? 0f : 1f, 0.5f), new Vector2(side * -70f, 0f), new Vector2(80f, 100f));
                var t = ToyKit.UIText("Text", num, font, At(texts.scoreNumbers, side < 0f ? 0 : 1), 70f, ToyKit.Ink);
                t.fontStyle = FontStyles.Bold;
                ToyKit.Stretch(t.rectTransform);
            }
        }
        else if (boardLook == BoardLook.Notebook)
        {
            var line = new Color(0.55f, 0.7f, 0.9f, 0.55f);
            for (float y = -150f; y > -root.sizeDelta.y; y -= 90f)
            {
                var l = ToyKit.UIImage("Rule", root, line);
                l.anchorMin = new Vector2(0f, 1f); l.anchorMax = new Vector2(1f, 1f);
                l.sizeDelta = new Vector2(0f, 3f);
                l.anchoredPosition = new Vector2(0f, y);
            }
            var margin = ToyKit.UIImage("Margin", root, new Color(0.9f, 0.4f, 0.4f, 0.6f));
            margin.anchorMin = new Vector2(0f, 0f); margin.anchorMax = new Vector2(0f, 1f);
            margin.sizeDelta = new Vector2(3f, 0f);
            margin.anchoredPosition = new Vector2(70f, 0f);
            var memo = ToyKit.UIText("Memo", root, font, texts.notebookMemo, 30f, new Color(0.45f, 0.45f, 0.5f));
            memo.alignment = TextAlignmentOptions.Right;
            ToyKit.Anchor(memo.rectTransform, new Vector2(1f, 1f), new Vector2(-230f, -40f), new Vector2(420f, 50f));
        }
    }

    // たたかう：ローカル／オンラインの大きなボタン2つ
    CanvasGroup BuildBattlePanel(Transform root)
    {
        var group = Group("Battle", root);
        var colors = new[] { ToyKit.Palette[0], ToyKit.Palette[4] };
        var buttons = new Button[2];
        for (int i = 0; i < 2; i++)
        {
            string name = At(texts.choiceLabels, i), subText = At(texts.choiceBoardTexts, i).Replace("\n", " ");
            // 色の付いたカード（いつも見える）＋ 選んだときに光る板（Tint）
            var card = ToyKit.UIRounded("Card_" + i, group.transform, Color.Lerp(colors[i], Color.white, 0.55f), 36f);
            rowHelps[card.gameObject] = At(texts.choiceHelps, i);
            ToyKit.Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f - i * 250f), new Vector2(1100f, 210f));
            var hl = ToyKit.UIRounded("Highlight", card, Color.white, 36f);
            ToyKit.Stretch(hl);
            hl.GetComponent<Image>().raycastTarget = true;

            var label = ToyKit.UIText("Label", card, font, name, 84f, Color.Lerp(colors[i], ToyKit.Ink, 0.55f));
            label.fontStyle = FontStyles.Bold;
            ToyKit.Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 26f), new Vector2(1000f, 110f));
            var sub = ToyKit.UIText("Sub", card, font, subText, 40f, ToyKit.Ink);
            ToyKit.Anchor(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -58f), new Vector2(1000f, 60f));

            var b = card.gameObject.AddComponent<Button>();
            Tint(b, hl.GetComponent<Image>());
            int index = i;
            b.onClick.AddListener(() => ChooseBattle(index));
            buttons[i] = b;
        }
        Chain(buttons);
        battleFirst = buttons[0];

        // オンラインがまだ遊べないときのお知らせ
        battleNote = ToyKit.UIText("Note", group.transform, font, texts.onlineNotReady, 46f, new Color(0.85f, 0.25f, 0.2f));
        battleNote.fontStyle = FontStyles.Bold;
        ToyKit.Anchor(battleNote.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 45f), new Vector2(1100f, 70f));
        battleNote.alpha = 0f;
        return group;
    }

    void ChooseBattle(int index)
    {
        bool knockTwice = battleStyle == BattleStyle.KnockTwice;
        if (knockTwice ? phase != Phase.Choose : phase != Phase.Panel || Kind(outIdx) != ItemKind.Battle || starting) return;
        bool online = index == 1;
        string scene = online ? onlineSceneName : battleSceneName;
        if (string.IsNullOrEmpty(scene) || !Application.CanStreamedLevelBeLoaded(scene))
        {
            // まだ遊べない：お知らせを出して、看板（KnockTwice ならその積み木）がぷるっと揺れる
            MenuSounds.Cancel();
            noteTime = sim;
            boardPunch = sim;
            if (knockTwice) { choiceWobbleIdx = index; choiceWobble = sim; }
            return;
        }
        if (knockTwice) StartCoroutine(KnockChoice(index, scene));
        else StartCoroutine(StartBattle(scene));
    }

    // ================= たたかう（KnockTwice） =================
    // たたかうを叩き飛ばしたあと：右から「オンライン」（下）→「ローカル」（上）の順に飛んできて積まれる
    IEnumerator BringChoices()
    {
        battleOut = true;
        yield return new WaitForSecondsRealtime(0.35f);
        for (int i = 0; i < 2; i++)
        {
            choiceArrive[i] = sim + (i == 1 ? 0f : 0.18f);
            choiceLanded[i] = false;
            choiceOffset[i] = 0f;
            choiceScale[i] = 1f;
            choiceBlocks[i].gameObject.SetActive(true);
        }
        choiceOut = -1;
        choiceCursor = 0;
        choiceAway = -100f;
        phase = Phase.Choose;
    }

    bool ChoicesReady => phase == Phase.Choose && choiceLanded[0] && choiceLanded[1];

    // 選んだ方をもう一度叩き飛ばして、そのシーンへ
    IEnumerator KnockChoice(int index, string scene)
    {
        phase = Phase.ChoiceLeaving;
        yield return Swing(0.22f, -82f);
        MenuSounds.Play(MatchSounds.Instance.KO);   // 叩き飛ばした瞬間に撃墜の音
        var contact = ChoiceContact(index);
        Emit(fx.confetti, contact, Quaternion.Euler(-30f, 90f, 0f), 45);
        Emit(fx.sparkle, contact, Quaternion.Euler(0f, 90f, 0f), 20);
        shake = Mathf.Max(shake, 0.6f);
        post.chroma.intensity.value = 0.5f;
        SlowMo(0.15f, 0.08f);
        choiceOut = index;
        choiceImpact = sim;
        yield return new WaitForSecondsRealtime(0.5f);
        Fader.Load(scene);
    }

    // B：2つは右へ飛んでいき、たたかうが戻ってくる
    IEnumerator CancelChoices()
    {
        phase = Phase.ChoiceBack;
        MenuSounds.Cancel();
        choiceAway = sim;
        yield return new WaitForSecondsRealtime(0.35f);
        foreach (var b in choiceBlocks) b.gameObject.SetActive(false);
        battleBack = true;
        battleBackTime = sim;
    }

    void UpdateChoices()
    {
        float follow = 1f - Mathf.Exp(-16f * Time.deltaTime);
        for (int i = 0; i < 2; i++)
        {
            var b = choiceBlocks[i];
            if (!b.gameObject.activeSelf) continue;
            bool isCursor = ChoicesReady && i == choiceCursor || phase == Phase.ChoiceLeaving && i == choiceCursor && choiceOut < 0;
            choiceOffset[i] = Mathf.Lerp(choiceOffset[i], isCursor ? CursorOffset : 0f, follow);
            choiceScale[i] = Mathf.Lerp(choiceScale[i], isCursor ? CursorScale : 1f, follow);

            Vector3 slot = ChoiceSlot(i);
            Vector3 pos = slot + Vector3.right * choiceOffset[i];
            Quaternion rot = Quaternion.identity;
            Vector3 s = Vector3.one * choiceScale[i];

            // 飛んでくる
            float t = Mathf.Clamp01((sim - choiceArrive[i]) / ChoiceFlyIn);
            if (t < 1f)
            {
                float u = 1f - Mathf.Pow(1f - t, 2.2f);
                Vector3 from = slot + new Vector3(13f, 3f, -1f);
                pos = ToyKit.Bezier(from, from + new Vector3(-5f, 2f, 0f), slot + new Vector3(1.5f, 1.6f, 0f), slot, u);
                rot = Quaternion.AngleAxis(540f * (1f - u), Vector3.forward);
            }
            else if (!choiceLanded[i])
            {
                // ドスン
                choiceLanded[i] = true;
                Emit(fx.puff, slot - Vector3.up * BH * 0.5f, Quaternion.Euler(-90f, 0f, 0f), 8);
                shake = Mathf.Max(shake, 0.3f);
                MenuSounds.Play(MatchSounds.Instance.BlockHitBlock, 0.7f);
            }
            if (t >= 1f) s = Vector3.Scale(s, ToyKit.SquashVec(ToyKit.Squash((sim - choiceArrive[i] - ChoiceFlyIn) / 0.35f, 0.18f)));

            // 下（オンライン）が叩き出されたら、上はストンと落ちる
            if (choiceOut == 1 && i == 0)
            {
                float d = Mathf.Clamp01((sim - choiceImpact - HangTime) / DropTime);
                pos.y -= BH * d * d;
            }
            // まだ遊べない：ぷるぷる
            if (i == choiceWobbleIdx)
            {
                float w = (sim - choiceWobble) / 0.45f;
                if (w > 0f && w < 1f)
                {
                    float a = Mathf.Sin(w * Mathf.PI * 4f) * (1f - w);
                    pos.x += 0.25f * a;
                    rot = Quaternion.Euler(0f, 0f, -6f * a);
                }
            }
            // 叩き飛ばされた／B で帰っていく：画面の外へ
            float f = i == choiceOut ? (sim - choiceImpact) / 0.6f : (sim - choiceAway - (i == 0 ? 0f : 0.08f)) / 0.6f;
            if ((i == choiceOut || phase == Phase.ChoiceBack) && f > 0f)
            {
                f = Mathf.Min(f, 2f);
                pos = slot + new Vector3(14f * f * f + 6f * f, 2f * f, -1.5f * f);
                rot = Quaternion.Euler(0f, 0f, -540f * f);
            }

            b.localPosition = pos;
            b.localRotation = rot;
            b.localScale = s;
            float glow = isCursor ? 0.08f + 0.05f * Mathf.Sin(Time.unscaledTime * 5f) : 0f;
            choiceMats[i].SetColor("_EmissionColor", Color.white * glow);
        }
    }

    // 上がローカル（0）、下がオンライン（1）
    Vector3 ChoiceSlot(int i) => ChoiceBase + new Vector3(0f, (1 - i) * BH + BH * 0.5f, 0f);

    Vector3 ChoiceContact(int i)
    {
        var s = ChoiceSlot(i);
        return new Vector3(s.x + CursorOffset - BlockSize.x * CursorScale * 0.5f, s.y, s.z);
    }

    int ChoiceUnderMouse(Vector2 screen)
    {
        if (!Physics.Raycast(cam.ScreenPointToRay(screen), out var hit, 100f)) return -1;
        for (int i = 0; i < choiceBlocks.Length; i++)
            if (hit.transform == choiceBlocks[i]) return i;
        return -1;
    }

    IEnumerator StartBattle(string scene)
    {
        starting = true;
        MenuSounds.Play(MatchSounds.Instance.KO);
        boardPunch = sim;
        shake = Mathf.Max(shake, 0.5f);
        Emit(fx.confetti, boardCenter + new Vector3(0f, 0.5f, -0.5f), Quaternion.Euler(-60f, 0f, 0f), 50);
        Emit(fx.sparkle, boardCenter + new Vector3(0f, 0f, -0.5f), Quaternion.Euler(0f, 180f, 0f), 20);
        yield return new WaitForSecondsRealtime(0.45f);
        Fader.Load(scene);
    }

    bool HasItem(ItemKind k) => items != null && Array.Exists(items, it => it != null && it.kind == k);

    // サウンドだけの看板（項目に Sound があるときだけ中身を作る。同じ設定を2か所に作ると表示がずれるため）
    CanvasGroup BuildSoundPanel(Transform root)
    {
        var group = Group("Sound", root);
        if (!HasItem(ItemKind.Sound)) return group;
        var sliders = SoundRows(group.transform, 150f, 150f);
        Chain(sliders);
        soundFirst = sliders[0];
        return group;
    }

    // ビデオだけの看板（項目に Video があるときだけ）
    CanvasGroup BuildVideoPanel(Transform root)
    {
        var group = Group("Video", root);
        if (!HasItem(ItemKind.Video)) return group;
        var sels = VideoRows(group.transform, 150f, 150f);
        Chain(sels);
        videoFirst = sels[0];
        return group;
    }

    // せってい：サウンド3行とビデオ3行を1枚の看板にまとめる（6行が収まるように少し縮める）
    const float SettingsScale = 0.8f, SettingsStep = 115f, SettingsCenterY = -65f;
    CanvasGroup BuildSettingsPanel(Transform root)
    {
        var group = Group("Settings", root);
        if (!HasItem(ItemKind.Settings)) return group;
        var rows = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
        rows.SetParent(group.transform, false);
        ToyKit.Anchor(rows, new Vector2(0.5f, 0.5f), new Vector2(0f, SettingsCenterY), new Vector2(1100f, SettingsStep * 6f));
        rows.localScale = Vector3.one * SettingsScale;

        var soundHost = new GameObject("SoundRows", typeof(RectTransform)).GetComponent<RectTransform>();
        soundHost.SetParent(rows, false); ToyKit.Stretch(soundHost);
        var videoHost = new GameObject("VideoRows", typeof(RectTransform)).GetComponent<RectTransform>();
        videoHost.SetParent(rows, false); ToyKit.Stretch(videoHost);

        float top = SettingsStep * 2.5f + 15f;   // サウンドとビデオの間を少しあける
        var sliders = SoundRows(soundHost, top, SettingsStep);
        var sels = VideoRows(videoHost, -SettingsStep * 0.5f - 15f, SettingsStep);
        var all = new Selectable[sliders.Length + sels.Length];
        sliders.CopyTo(all, 0);
        sels.CopyTo(all, sliders.Length);
        Chain(all);
        settingsFirst = all[0];
        return group;
    }

    // 音量の3行（parent に SoundSettingsPanel を付ける）。y0：いちばん上の行の高さ　step：行の間隔
    Slider[] SoundRows(Transform parent, float y0, float step)
    {
        var colors = new[] { ToyKit.Palette[1], ToyKit.Palette[4], ToyKit.Palette[3] };
        var sliders = new Slider[3];
        var meters = new RectTransform[3];
        var values = new TMP_Text[3];
        for (int i = 0; i < 3; i++)
        {
            var rt = RowAt(texts.soundRows, i);
            var row = Row(parent, y0 - i * step, rt.label);
            rowHelps[row] = rt.help;

            // 目盛りの積み木を並べる場所。スライダーの当たり判定も同じ広さ
            var sg = new GameObject("Slider", typeof(RectTransform));
            sg.transform.SetParent(row.transform, false);
            var area = (RectTransform)sg.transform;
            ToyKit.Anchor(area, new Vector2(0.5f, 0.5f), new Vector2(85f, 0f), new Vector2(510f, 80f));
            var hit = ToyKit.UIImage("Hit", area, Color.clear); ToyKit.Stretch(hit);
            hit.GetComponent<Image>().raycastTarget = true;
            var ha = new GameObject("Handle Area", typeof(RectTransform)).GetComponent<RectTransform>();
            ha.SetParent(area, false); ToyKit.Stretch(ha); ha.offsetMin = new Vector2(23f, 0f); ha.offsetMax = new Vector2(-23f, 0f);
            var handle = ToyKit.UIImage("Handle", ha, Color.clear);
            handle.anchorMin = new Vector2(0f, 0f); handle.anchorMax = new Vector2(0f, 1f); handle.sizeDelta = new Vector2(46f, 0f);

            var s = sg.AddComponent<Slider>();
            s.handleRect = handle; s.direction = Slider.Direction.LeftToRight;
            Tint(s, row.GetComponent<Image>());
            sliders[i] = s;
            meters[i] = area;

            values[i] = ToyKit.UIText("Value", row.transform, font, "8", 56f, Color.Lerp(colors[i], ToyKit.Ink, 0.35f));
            values[i].fontStyle = FontStyles.Bold;
            ToyKit.Anchor(values[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(445f, 0f), new Vector2(120f, 100f));
        }
        parent.gameObject.AddComponent<SoundSettingsPanel>().Setup(sliders[0], sliders[1], sliders[2], values[0], values[1], values[2]);
        // 目盛りは SoundSettingsPanel が 0〜10 に設定してから作る
        for (int i = 0; i < 3; i++)
        {
            meters[i].gameObject.AddComponent<VolumeMeter>().Setup(sliders[i], meters[i], colors[i], new Color(0.88f, 0.82f, 0.74f));
            sliders[i].onValueChanged.AddListener(_ => MenuSounds.Cursor());
        }
        return sliders;
    }

    // 画面の3行（parent に VideoSettingsPanel を付ける）
    OptionSelector[] VideoRows(Transform parent, float y0, float step)
    {
        var sels = new OptionSelector[3];
        for (int i = 0; i < 3; i++)
        {
            var rt = RowAt(texts.videoRows, i);
            var row = Row(parent, y0 - i * step, rt.label);
            rowHelps[row] = rt.help;
            row.GetComponent<Image>().raycastTarget = true;
            var os = row.AddComponent<OptionSelector>();
            Tint(os, row.GetComponent<Image>());
            // ＜ [ 値 ] ＞ ：値は角丸の板の上、その下に何番目かの丸
            var pill = ToyKit.UIRounded("ValuePill", row.transform, new Color(0.98f, 0.94f, 0.87f), 36f);
            ToyKit.Anchor(pill, new Vector2(0.5f, 0.5f), new Vector2(200f, 0f), new Vector2(400f, 92f));
            var val = ToyKit.UIText("Value", pill, font, "-", 44f, ToyKit.Ink);
            ToyKit.Anchor(val.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 9f), new Vector2(380f, 64f));
            var dots = new GameObject("Dots", typeof(RectTransform)).GetComponent<RectTransform>();
            dots.SetParent(pill, false);
            ToyKit.Anchor(dots, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(380f, 14f));
            var prev = Arrow(row.transform, "＜", -50f);
            var next = Arrow(row.transform, "＞", 450f);
            os.Bind(val, prev, next);
            os.onValueChanged.AddListener(_ => MenuSounds.Cursor());
            row.AddComponent<OptionSelectorLook>().Setup(os, pill, (RectTransform)prev.transform, (RectTransform)next.transform, dots,
                                                         ToyKit.Palette[1], new Color(0.82f, 0.75f, 0.66f));
            sels[i] = os;
        }
        parent.gameObject.AddComponent<VideoSettingsPanel>().Setup(sels[0], sels[1], sels[2]);
        return sels;
    }

    CanvasGroup Group(string name, Transform root)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(root, false);
        ToyKit.Stretch((RectTransform)go.transform);
        var g = go.AddComponent<CanvasGroup>();
        g.alpha = 0f;
        g.interactable = false;
        return g;
    }

    GameObject Row(Transform parent, float y, string label)
    {
        var r = ToyKit.UIRounded("Row_" + label, parent, Color.white, 30f);
        ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(1100f, 120f));
        var l = ToyKit.UIText("Label", r, font, label, 54f, ToyKit.Ink);
        l.alignment = TextAlignmentOptions.Left;
        ToyKit.Anchor(l.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-360f, 0f), new Vector2(320f, 100f));
        return r.gameObject;
    }

    Button Arrow(Transform parent, string s, float x)
    {
        // 丸い矢印ボタン（下に影の丸を敷いて、少し浮いて見せる）
        var shadow = ToyKit.UIRounded(s == "＜" ? "PrevShadow" : "NextShadow", parent, new Color(0.75f, 0.42f, 0.1f), 40f);
        ToyKit.Anchor(shadow, new Vector2(0.5f, 0.5f), new Vector2(x, -5f), new Vector2(80f, 80f));
        var a = ToyKit.UIRounded(s == "＜" ? "Prev" : "Next", parent, ToyKit.Palette[1], 40f);
        ToyKit.Anchor(a, new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(80f, 80f));
        var img = a.GetComponent<Image>();
        img.raycastTarget = true;
        var t = ToyKit.UIText("Text", a, font, s, 46f, Color.white);   // ◀▶ はフォントに無いので全角の＜＞
        t.fontStyle = FontStyles.Bold;
        ToyKit.Stretch(t.rectTransform);
        var b = a.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
        return b;
    }

    static void Tint(Selectable sel, Graphic g)
    {
        sel.targetGraphic = g;
        var cb = sel.colors;
        cb.normalColor = new Color(1f, 1f, 1f, 0f);
        cb.highlightedColor = new Color(1f, 0.85f, 0.45f, 0.35f);
        cb.selectedColor = new Color(1f, 0.8f, 0.3f, 0.6f);
        cb.pressedColor = new Color(1f, 0.75f, 0.25f, 0.7f);
        cb.disabledColor = new Color(1f, 1f, 1f, 0f);
        cb.fadeDuration = 0.08f;
        sel.colors = cb;
    }

    static void Chain(Selectable[] arr)
    {
        for (int i = 0; i < arr.Length; i++)
        {
            var nav = new Navigation { mode = Navigation.Mode.Explicit };
            nav.selectOnUp = i > 0 ? arr[i - 1] : null;
            nav.selectOnDown = i < arr.Length - 1 ? arr[i + 1] : null;
            arr[i].navigation = nav;
        }
    }

    void BuildOverlay()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        overlay = ToyKit.CreateOverlayCanvas("UI", 10);

        // 左上の見出し（今は出さない。文字を入れれば出る）
        var title = ToyKit.UIText("Title", overlay, font, "", 90f, ToyKit.Ink);
        title.characterSpacing = 20f;
        ToyKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(330f, -90f), new Vector2(500f, 120f));
        titleGroup = title.gameObject.AddComponent<CanvasGroup>();

        // ずかん（4番目の項目で開く。下の帯より後ろに置いて、帯の説明と操作ガイドは見えるようにする）
        var bookGo = new GameObject("Book", typeof(RectTransform));
        bookGo.transform.SetParent(overlay, false);
        book = bookGo.AddComponent<CharacterBook>();
        book.Setup(font, letterMat, uiSprite, knobSprite, roster, texts.book);

        // じっせき（ずかんと同じように開く。中身はまだ無い）
        var achGo = new GameObject("Achievements", typeof(RectTransform));
        achGo.transform.SetParent(overlay, false);
        achievements = achGo.AddComponent<AchievementBook>();
        achievements.Setup(font, letterMat, uiSprite, texts.achievements);

        BuildHelpBand();

        // クレジット（背景をボカした上に流す。ずかんにキャラがいないときだけ使う）
        var cr = ToyKit.UIImage("Credits", overlay, new Color(ToyKit.Horizon.r, ToyKit.Horizon.g, ToyKit.Horizon.b, 0.55f));
        ToyKit.Stretch(cr);
        cr.gameObject.AddComponent<CanvasGroup>();
        var content = ToyKit.UIText("Content", cr, font, creditsText, 52f, ToyKit.Ink);
        content.alignment = TextAlignmentOptions.Top;
        content.lineSpacing = 20f;
        ToyKit.Anchor(content.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 100f));
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        credits = cr.gameObject.AddComponent<CreditRoll>();
        credits.Setup(content.rectTransform);

        fade = ToyKit.UIImage("Fade", overlay, Color.white).GetComponent<Image>();
        ToyKit.Stretch(fade.rectTransform);
    }

    // 画面の下の帯：説明の帯（上）と操作ガイドの段（下。ガイドは右下にそろえる）
    void BuildHelpBand()
    {
        var root = new GameObject("HelpBand", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(overlay, false);
        BottomStrip(root, GuideBarH + HelpBandH, 0f);
        helpGroup = root.gameObject.AddComponent<CanvasGroup>();
        helpGroup.alpha = 0f;
        helpGroup.blocksRaycasts = false;
        helpGroup.interactable = false;

        var bar = ToyKit.UIImage("GuideBar", root, new Color(0.34f, 0.24f, 0.08f, 0.92f));
        BottomStrip(bar, GuideBarH, 0f);
        var band = ToyKit.UIImage("Band", root, new Color(0.11f, 0.11f, 0.11f, 0.9f));
        BottomStrip(band, HelpBandH, GuideBarH);
        var ol = band.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(0.36f, 0.62f, 0.3f);
        ol.effectDistance = new Vector2(4f, -4f);

        helpText = ToyKit.UIText("Text", band, font, "", 36f, Color.white);
        helpText.alignment = TextAlignmentOptions.TopLeft;
        helpText.textWrappingMode = TextWrappingModes.Normal;
        ToyKit.Stretch(helpText.rectTransform);
        helpText.rectTransform.offsetMin = new Vector2(300f, 18f);
        helpText.rectTransform.offsetMax = new Vector2(-300f, -24f);

        guideRow = new GameObject("Guides", typeof(RectTransform)).GetComponent<RectTransform>();
        guideRow.SetParent(bar, false);
        ToyKit.Stretch(guideRow);
        guideRow.offsetMax = new Vector2(-40f, 0f);
    }

    // 画面の下の端にそって横いっぱいに広げる（下から bottom の高さに、高さ h）
    static void BottomStrip(RectTransform r, float h, float bottom)
    {
        r.anchorMin = new Vector2(0f, 0f);
        r.anchorMax = new Vector2(1f, 0f);
        r.pivot = new Vector2(0.5f, 0f);
        r.sizeDelta = new Vector2(0f, h);
        r.anchoredPosition = new Vector2(0f, bottom);
    }

    // 操作ガイド：「キー, 説明, キー, 説明…」を、右下から左へボタンの形で並べる（変わったときだけ作り直す）
    void SetGuides(params string[] pairs)
    {
        string key = string.Join("|", pairs);
        if (key == guideKey) return;
        guideKey = key;
        foreach (Transform c in guideRow) Destroy(c.gameObject);

        float x = 0f;
        for (int i = pairs.Length - 2; i >= 0; i -= 2)
        {
            string k = pairs[i], label = pairs[i + 1];
            var chip = ToyKit.UIRounded("Guide_" + label, guideRow, new Color(0.12f, 0.12f, 0.12f, 0.95f), 14f);
            chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(1f, 0.5f);

            // キー：A・B は丸、上下・左右は横長
            float keyW = k.Length == 1 ? 44f : 76f;
            var keyBg = ToyKit.UIRounded("Key", chip, new Color(0.95f, 0.93f, 0.88f), 22f);
            keyBg.anchorMin = keyBg.anchorMax = keyBg.pivot = new Vector2(0f, 0.5f);
            keyBg.anchoredPosition = new Vector2(12f, 0f);
            keyBg.sizeDelta = new Vector2(keyW, 44f);
            var kt = ToyKit.UIText("Text", keyBg, font, k, 28f, new Color(0.12f, 0.12f, 0.12f));
            kt.fontStyle = FontStyles.Bold;
            ToyKit.Stretch(kt.rectTransform);

            var lt = ToyKit.UIText("Label", chip, font, label, 32f, Color.white);
            float labelW = lt.GetPreferredValues(label).x;
            lt.alignment = TextAlignmentOptions.Left;
            lt.rectTransform.anchorMin = lt.rectTransform.anchorMax = lt.rectTransform.pivot = new Vector2(0f, 0.5f);
            lt.rectTransform.anchoredPosition = new Vector2(12f + keyW + 12f, 0f);
            lt.rectTransform.sizeDelta = new Vector2(labelW + 4f, 50f);

            float w = 12f + keyW + 12f + labelW + 22f;
            chip.sizeDelta = new Vector2(w, 58f);
            chip.anchoredPosition = new Vector2(-x, 0f);
            x += w + 16f;
        }
    }

    // 下の帯の説明：操作中は選んでいる行、ローカル／オンラインを選んでいるときはその方、ふだんはカーソルの項目
    string HelpText()
    {
        bool choosing = phase == Phase.Choose || phase == Phase.ChoiceLeaving || phase == Phase.ChoiceBack;
        if (choosing)
        {
            string note = sim - noteTime < 2f ? "　<color=#E8735A>" + texts.onlineNotReady + "</color>" : "";
            return At(texts.choiceHelps, choiceCursor) + note;
        }
        if (phase == Phase.Panel && outIdx >= 0 && EventSystem.current != null)
        {
            // 選んでいる行（スライダーは行の子）の説明
            for (var t = EventSystem.current.currentSelectedGameObject != null ? EventSystem.current.currentSelectedGameObject.transform : null; t != null; t = t.parent)
                if (rowHelps.TryGetValue(t.gameObject, out var h)) return h;
        }
        int item = (phase == Phase.Panel || phase == Phase.Flying || phase == Phase.Returning) && outIdx >= 0 ? outIdx : cursor;
        var it = ItemAt(item);
        return it != null ? it.help : "";
    }

    IEnumerator FadeIn()
    {
        // シーン遷移の演出（Fader）から来たときは、その演出が明けるので白いフェードは重ねない
        if (Fader.IsBusy) { fade.color = new Color(1f, 1f, 1f, 0f); yield break; }
        for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime)
        {
            fade.color = new Color(1f, 1f, 1f, 1f - t / 0.8f);
            yield return null;
        }
        fade.color = new Color(1f, 1f, 1f, 0f);
    }

    // ================= 入力 =================
    void HandleInput()
    {
        var mouse = Mouse.current;
        bool mouseMoved = mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0f;
        bool click = MenuInput.AnyClick();

        switch (phase)
        {
            case Phase.Intro:
                if (MenuInput.Submit() || MenuInput.Cancel() || click) SkipIntro();
                break;

            case Phase.Browse:
            {
                int hover = mouse != null && (mouseMoved || click) ? BlockUnderMouse(mouse.position.ReadValue()) : -1;
                if (hover >= 0 && hover != cursor) { navDir = hover > cursor ? 1 : -1; cursor = hover; MenuSounds.Cursor(); }

                int n = ItemCount;
                // 押しっぱなしのくり返しは端で止まる。押し直したときだけ反対の端へ回る
                if (MenuInput.UpRepeat() && MenuInput.StepRepeat(ref cursor, n, MenuInput.Dir.Up, -1)) { navDir = -1; MenuSounds.Cursor(); }
                else if (MenuInput.DownRepeat() && MenuInput.StepRepeat(ref cursor, n, MenuInput.Dir.Down, 1)) { navDir = 1; MenuSounds.Cursor(); }
                else if (MenuInput.Submit() || (click && hover >= 0))
                {
                    // たたかう（KnockTwice）は叩き飛ばした瞬間に撃墜の音を鳴らすので、決定の音は重ねない
                    if (Kind(cursor) != ItemKind.Battle || battleStyle != BattleStyle.KnockTwice) MenuSounds.Decide();
                    Hit(cursor);
                }
                break;
            }

            case Phase.Panel:
            {
                if (starting) break;   // たたかうを決めて、シーンを移るところ
                // 看板の中で行を移ったらカーソルの音
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected != null && panelSelected != null && selected != panelSelected) MenuSounds.Cursor();
                if (selected != null) panelSelected = selected;

                // B / Esc / 右クリック、または左の山をクリックで戻る
                if (MenuInput.Cancel() || (click && BlockUnderMouse(mouse.position.ReadValue()) >= 0)) { MenuSounds.Cancel(); Return(); }
                break;
            }

            case Phase.Credits:
                if (!credits.IsBusy && !book.IsBusy && !achievements.IsBusy) phase = Phase.Browse;
                break;

            case Phase.Choose:
            {
                if (!ChoicesReady) break;
                int hover = mouse != null && (mouseMoved || click) ? ChoiceUnderMouse(mouse.position.ReadValue()) : -1;
                if (hover >= 0 && hover != choiceCursor) { choiceCursor = hover; navDir = choiceCursor == 1 ? 1 : -1; MenuSounds.Cursor(); }

                if (MenuInput.Up() || MenuInput.Down()) { choiceCursor = 1 - choiceCursor; navDir = choiceCursor == 1 ? 1 : -1; MenuSounds.Cursor(); }
                else if (MenuInput.Submit() || (click && hover >= 0)) ChooseBattle(choiceCursor);
                else if (MenuInput.Cancel() || (click && BlockUnderMouse(mouse.position.ReadValue()) >= 0)) StartCoroutine(CancelChoices());
                break;
            }
        }
    }

    int BlockUnderMouse(Vector2 screen)
    {
        if (!Physics.Raycast(cam.ScreenPointToRay(screen), out var hit, 100f)) return -1;
        for (int i = 0; i < blocks.Length; i++)
            if (hit.transform == blocks[i]) return i;
        return -1;
    }

    // ================= 叩く =================
    void Hit(int index)
    {
        phase = Phase.Hitting;
        StartCoroutine(HitRoutine(index));
    }

    IEnumerator HitRoutine(int index)
    {
        yield return Swing(0.22f, -82f);

        var contact = Contact(index);
        Emit(fx.confetti, contact, Quaternion.Euler(-30f, 90f, 0f), 35);
        Emit(fx.sparkle, contact, Quaternion.Euler(0f, 90f, 0f), 18);
        shake = Mathf.Max(shake, 0.5f);
        post.chroma.intensity.value = 0.4f;
        SlowMo(0.15f, 0.07f);

        switch (Kind(index))
        {
            case ItemKind.Battle when battleStyle == BattleStyle.KnockTwice:
                // 画面外へ叩き飛ばし（撃墜の音）、代わりにローカル／オンラインの積み木が飛んでくる
                MenuSounds.Play(MatchSounds.Instance.KO);
                outIdx = index;
                impactTime = sim;
                phase = Phase.Leaving;
                StartCoroutine(BringChoices());
                break;

            case ItemKind.Battle:
            case ItemKind.Settings:
            case ItemKind.Sound:
            case ItemKind.Video:
                outIdx = index;
                impactTime = sim;
                phase = Phase.Flying;
                break;

            case ItemKind.Quit:
                outIdx = index;
                impactTime = sim;
                phase = Phase.Leaving;
                yield return new WaitForSecondsRealtime(0.5f);
                for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
                {
                    fade.color = new Color(1f, 1f, 1f, t / 0.5f);
                    yield return null;
                }
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                break;

            case ItemKind.Book:
            case ItemKind.Credits:
            case ItemKind.Achievements:
            {
                var kind = Kind(index);
                wobbleIdx = index;
                wobbleTime = sim;
                yield return new WaitForSecondsRealtime(0.35f);
                if (kind == ItemKind.Achievements) achievements.Open();     // じっせき
                else if (kind == ItemKind.Book && book.HasEntries) book.Open();   // キャラのずかん
                else credits.Play();
                phase = Phase.Credits;
                break;
            }

            default:
                phase = Phase.Browse;
                break;
        }
    }

    void Return()
    {
        phase = Phase.Returning;
        returnTime = sim;
        panelSelected = null;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        GameSettings.Save();
    }

    IEnumerator Swing(float windup, float windupYaw)
    {
        swinging = true;
        float from = hammerYaw;
        for (float t = 0f; t < windup; t += Time.deltaTime)
        {
            float u = t / windup;
            hammerYaw = Mathf.Lerp(from, windupYaw, 1f - (1f - u) * (1f - u));
            yield return null;
        }
        const float strike = 0.07f;
        for (float t = 0f; t < strike; t += Time.deltaTime)
        {
            float u = t / strike;
            hammerYaw = Mathf.Lerp(windupYaw, 0f, u * u);
            yield return null;
        }
        hammerYaw = 0f;
        swinging = false;
        recoverStart = sim;
    }

    void SlowMo(float scale, float holdReal) => StartCoroutine(SlowRoutine(scale, holdReal));

    IEnumerator SlowRoutine(float scale, float holdReal)
    {
        Time.timeScale = scale;
        yield return new WaitForSecondsRealtime(holdReal);
        for (float t = 0f; t < 0.08f; t += Time.unscaledDeltaTime)
        {
            Time.timeScale = Mathf.Lerp(scale, 1f, t / 0.08f);
            yield return null;
        }
        Time.timeScale = 1f;
    }

    // ================= 毎フレーム =================
    void Update()
    {
        sim += Time.deltaTime;
        float dt = Time.unscaledDeltaTime;

        HandleInput();
        UpdateIntro();

        bool panel = phase == Phase.Panel;
        bool choosing = phase == Phase.Choose || phase == Phase.ChoiceLeaving;
        dim = Mathf.MoveTowards(dim, panel || phase == Phase.Credits ? 1f : choosing ? 0.6f : 0f, dt * 3f);
        // 看板を照らす：サウンド・ビデオの操作中と、たたかうでローカル／オンラインを選んでいる間（看板に選んでいる方の説明が出る）
        panelLit = Mathf.MoveTowards(panelLit, panel || choosing ? 1f : 0f, dt * 3f);

        UpdateBlocks();
        UpdateChoices();
        UpdateHead();
        UpdateHammer();
        UpdateBoard(dt);
        UpdateCamera(dt);
        UpdateUI();

        post.chroma.intensity.value = Mathf.MoveTowards(post.chroma.intensity.value, 0f, dt * 1.2f);
    }

    // 叩き出された積み木より上の山が落ちている量（0〜1段）
    float DropAmount()
    {
        if (outIdx < 0) return 0f;
        // たたかうが戻ってくる：先に上の山が持ち上がって隙間を空ける
        if (battleBack) return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((sim - battleBackTime) / 0.2f));
        if (phase == Phase.Returning)
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((sim - returnTime) / (ReturnTime * 0.35f)));   // 先に持ち上がって隙間を空ける
        float d = Mathf.Clamp01((sim - impactTime - HangTime) / DropTime);
        return d * d;
    }

    void UpdateBlocks()
    {
        int n = blocks.Length;
        float follow = 1f - Mathf.Exp(-16f * Time.deltaTime);
        float drop = DropAmount();
        float dropSquash = phase != Phase.Returning ? ToyKit.Squash((sim - impactTime - HangTime - DropTime) / 0.3f, 0.14f) : 0f;

        for (int i = 0; i < n; i++)
        {
            var b = blocks[i];
            bool isCursor = (phase == Phase.Browse || phase == Phase.Hitting) && i == cursor;
            offsetX[i] = Mathf.Lerp(offsetX[i], isCursor ? CursorOffset : 0f, follow);
            scaleNow[i] = Mathf.Lerp(scaleNow[i], isCursor ? CursorScale : 1f, follow);

            Vector3 pos = Slot(i) + Vector3.right * offsetX[i];
            Quaternion rot = Quaternion.identity;
            Vector3 s = Vector3.one * scaleNow[i];
            // 始まり：上から降ってきて、着地でつぶれる
            pos.y += DropOffset(dropAt[i]);
            s = Vector3.Scale(s, ToyKit.SquashVec(DropSquash(dropAt[i])));

            if (outIdx >= 0 && i < outIdx)
            {
                pos.y -= BH * drop;
                s = Vector3.Scale(s, ToyKit.SquashVec(dropSquash));
            }

            if (i == outIdx)
            {
                Vector3 p0 = Slot(i), p3 = Dock();
                Vector3 p1 = p0 + new Vector3(2.6f, 1.4f, -1.8f), p2 = p3 + new Vector3(0f, 1.8f, -1.2f);
                if (battleBack)
                {
                    // たたかう：画面の外から、くるっと回って元の段へ戻る
                    float r = Mathf.Clamp01((sim - battleBackTime - 0.05f) / 0.45f);
                    float f = 1f - (1f - Mathf.Pow(1f - r, 2.2f));
                    pos = p0 + new Vector3(14f * f * f + 6f * f, 2f * f, -1.5f * f);
                    rot = Quaternion.Euler(0f, 0f, 540f * f);
                    if (r >= 1f)
                    {
                        battleBack = battleOut = false;
                        outIdx = -1;
                        phase = Phase.Browse;
                        shake = Mathf.Max(shake, 0.25f);
                    }
                }
                else if (phase == Phase.Leaving || battleOut)
                {
                    // やめる・たたかう：画面の外へ吹き飛ぶ
                    float f = Mathf.Min((sim - impactTime) / 0.6f, 2f);
                    pos = p0 + new Vector3(14f * f * f + 6f * f, 2f * f, -1.5f * f);
                    rot = Quaternion.Euler(0f, 0f, -540f * f);
                }
                else if (phase == Phase.Returning)
                {
                    float r = Mathf.Clamp01((sim - returnTime) / ReturnTime);
                    float u = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 0.2f) / 0.8f));
                    pos = ToyKit.Bezier(p0, p1, p2, p3, u);
                    rot = Quaternion.AngleAxis(-360f * (1f - u), Vector3.forward);
                    if (r >= 1f) { outIdx = -1; phase = Phase.Browse; }
                }
                else
                {
                    float t = Mathf.Clamp01((sim - impactTime) / FlyTime);
                    float u = 1f - Mathf.Pow(1f - t, 2.2f);
                    pos = ToyKit.Bezier(p0, p1, p2, p3, u);
                    rot = Quaternion.AngleAxis(-360f * u, Vector3.forward);
                    if (t >= 1f && phase == Phase.Flying) Docked();
                    s = ToyKit.SquashVec(ToyKit.Squash((sim - dockTime) / 0.35f, 0.2f));
                }
            }
            else if (i == wobbleIdx)
            {
                float w = (sim - wobbleTime) / 0.45f;
                if (w > 0f && w < 1f)
                {
                    float a = Mathf.Sin(w * Mathf.PI * 4f) * (1f - w);
                    pos.x += 0.25f * a;
                    rot = Quaternion.Euler(0f, 0f, -6f * a);
                }
            }

            b.localPosition = pos;
            b.localRotation = rot;
            b.localScale = s;

            // 看板を操作している間、左の山は暗くなる（看板に乗った積み木は明るいまま）
            float d = i == outIdx ? 0f : dim;
            blockMats[i].SetColor("_BaseColor", Color.Lerp(blockColors[i], blockColors[i] * 0.45f, d));
            float glow = isCursor ? 0.08f + 0.05f * Mathf.Sin(Time.unscaledTime * 5f) : 0f;
            blockMats[i].SetColor("_EmissionColor", Color.white * glow);
        }
    }

    // 積み木が看板に着地した
    void Docked()
    {
        phase = Phase.Panel;
        dockTime = sim;
        boardPunch = sim;
        Emit(fx.puff, Dock() - Vector3.up * BH * 0.5f, Quaternion.Euler(-90f, 0f, 0f), 8);
        Emit(fx.sparkle, Dock(), Quaternion.Euler(-90f, 0f, 0f), 14);
        shake = Mathf.Max(shake, 0.25f);

        var k = Kind(outIdx);
        var first = k == ItemKind.Battle ? battleFirst : k == ItemKind.Settings ? settingsFirst : k == ItemKind.Sound ? soundFirst : videoFirst;
        if (EventSystem.current != null && first != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
    }

    void UpdateHead()
    {
        var f = face;
        float y = ItemCount * BH + ToyKit.HeadR;
        if (outIdx >= 0) y -= BH * DropAmount();

        // 看板を操作している間は看板の方を見てニコッ。ときどきまばたき
        bool happy = phase == Phase.Panel || phase == Phase.Credits;
        f.SetHappy(happy);
        var target = phase == Phase.Panel ? Quaternion.Euler(-5f, -35f, 0f) : Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.7f) * 8f, 0f);
        headRot = Quaternion.Slerp(headRot, target, 1f - Mathf.Exp(-6f * Time.deltaTime));

        if (Time.time > nextBlink)
        {
            float b = (Time.time - nextBlink) / 0.14f;
            f.eyesNormal.localScale = new Vector3(1f, Mathf.Abs(1f - 2f * Mathf.Clamp01(b)) * 0.9f + 0.1f, 1f);
            if (b >= 1f) { f.eyesNormal.localScale = Vector3.one; nextBlink = Time.time + UnityEngine.Random.Range(2.5f, 5f); }
        }

        Vector3 s = Vector3.one;
        if (outIdx >= 0 && phase != Phase.Returning) s = ToyKit.SquashVec(ToyKit.Squash((sim - impactTime - HangTime - DropTime) / 0.35f, 0.2f));
        // 始まり：顔は最後に降ってきて、ぽよんとつぶれる
        y += DropOffset(headDropAt);
        s = Vector3.Scale(s, ToyKit.SquashVec(ToyKit.Squash((sim - headDropAt - DropFall) / 0.45f, 0.3f)));
        f.root.localPosition = new Vector3(stackX, y, 0f);
        f.root.localRotation = headRot;
        f.root.localScale = s;
        Color c = new Color(0.99f, 0.85f, 0.18f);
        f.body.SetColor("_BaseColor", Color.Lerp(c, c * 0.5f, dim));
    }

    void UpdateHammer()
    {
        if (!swinging)
        {
            float t = Mathf.Clamp01((sim - recoverStart) / 0.35f);
            float sway = Mathf.Sin(Time.unscaledTime * 1.7f) * 3f;
            hammerYaw = Mathf.LerpUnclamped(14f, IdleYaw + sway, ToyKit.EaseOutBack(t));
        }
        // ローカル／オンラインを選んでいる間は、その積み木の横で構える
        bool choosing = phase == Phase.Choose || phase == Phase.ChoiceLeaving;
        if (phase == Phase.Intro) hammerPos = OffscreenHammer();
        else if (!swinging && phase != Phase.Leaving && !(phase == Phase.ChoiceLeaving && choiceOut >= 0))
            hammerPos = Vector3.Lerp(hammerPos, choosing ? ToyKit.HammerPivot(ChoiceContact(choiceCursor), hammerScale) : HammerSpot(cursor), 1f - Mathf.Exp(-12f * Time.deltaTime));
        hammer.localPosition = hammerPos;
        // どの段（ローカル／オンラインも）にいても、一番下（やめる）にいるときと同じ角度で見えるように、
        // カメラから見た方向の違いの分だけ傾ける（上の段ほど、少し上から見下ろした向きに起こす）
        Vector3 refPos = HammerSpot(ItemCount - 1);
        var tilt = Quaternion.FromToRotation(refPos - camPos, hammerPos - camPos);
        hammer.localRotation = tilt * Quaternion.Euler(0f, hammerYaw, 0f);
    }

    void UpdateBoard(float dt)
    {
        // 何を映すか：操作中はその項目、それ以外はカーソルの項目のプレビュー
        int item = (phase == Phase.Panel || phase == Phase.Flying || phase == Phase.Returning) && outIdx >= 0 ? outIdx : cursor;
        bool choosing = phase == Phase.Choose || phase == Phase.ChoiceLeaving || phase == Phase.ChoiceBack;
        var kind = Kind(item);
        var it = ItemAt(item);
        Content content = kind == ItemKind.Battle && battleStyle == BattleStyle.Board ? Content.Battle
                        : kind == ItemKind.Settings ? Content.Settings
                        : kind == ItemKind.Sound ? Content.Sound : kind == ItemKind.Video ? Content.Video : Content.Description;
        if (content == Content.Description)
        {
            if (choosing)
            {
                // ローカル／オンラインを選んでいる間：選んでいる方の説明（遊べないときはお知らせも）
                string note = sim - noteTime < 2f ? "\n<color=#D94033><size=65%>" + texts.onlineNotReady + "</size></color>" : "";
                descText.text = At(texts.choiceBoardTexts, choiceCursor) + note;
            }
            else descText.text = kind == ItemKind.Battle ? texts.battleBoardText : it != null ? it.description : "";
        }
        boardTitle.text = outIdx >= 0 && phase == Phase.Panel && (it == null || !it.titleInPanel) ? "" : choosing ? At(texts.choiceLabels, choiceCursor) : it != null ? it.label : "";

        Fade(battleGroup, content == Content.Battle, dt);
        Fade(soundGroup, content == Content.Sound, dt);
        Fade(videoGroup, content == Content.Video, dt);
        Fade(settingsGroup, content == Content.Settings, dt);
        Fade(descGroup, content == Content.Description, dt);
        var outKind = Kind(outIdx);
        battleGroup.interactable = battleGroup.blocksRaycasts = phase == Phase.Panel && outKind == ItemKind.Battle && !starting;
        soundGroup.interactable = soundGroup.blocksRaycasts = phase == Phase.Panel && outKind == ItemKind.Sound;
        videoGroup.interactable = videoGroup.blocksRaycasts = phase == Phase.Panel && outKind == ItemKind.Video;
        settingsGroup.interactable = settingsGroup.blocksRaycasts = phase == Phase.Panel && outKind == ItemKind.Settings;
        // 「じゅんびちゅう」は2秒出して消える
        float nt = sim - noteTime;
        battleNote.alpha = nt < 1.6f ? 1f : Mathf.Clamp01(1f - (nt - 1.6f) / 0.4f);

        boardDim.color = new Color(0.2f, 0.15f, 0.15f, 0.5f * (1f - panelLit));
        boardLight.intensity = panelLit * 35f;

        float p = (sim - boardPunch) / 0.35f;
        board.localScale = Vector3.one * (1f + ToyKit.Punch(p, 0.05f));

        UpdateFlip((int)content * 1000 + item * 10 + (choosing ? choiceCursor + 1 : 0));
    }

    // 映す中身が変わったらページをめくる（得点板・ノート）。めくる向きはカーソルを動かした向きにそろえる
    //   下へ（次へ）：いまのページの下の端を持って真上に持ち上げ、後ろへ送る。下から次の中身が出てくる
    //   上へ（前へ）：後ろに送ってあったページが戻ってきて、下ろして垂れ下がる。下りきってから中身が出る
    // 目にやさしくするための工夫：
    //   ・めくるページも看板と同じだけ暗くする（暗い看板の上を明るいページが横切ってチカッとしないように）
    //   ・ページは向こうへ倒れるほど少し暗くなる（光の当たり方。まぶしい面が大きく動かない）
    //   ・めくった直後（flipCooldown 以内）にまた変わったときは、めくらずに中身だけをふわっと入れ替える（連打・長押し）
    //   ・中身はぱっと出さず、短く溶け込ませる
    float contentFadeStart = -100f;
    void UpdateFlip(int key)
    {
        if (key != shownKey)
        {
            float since = sim - flipStart;
            bool recent = since >= 0f && since < flipCooldown;
            if (shownKey >= 0 && flipPage != null && !recent)
            {
                flipStart = sim;
                flipDir = navDir >= 0 ? 1 : -1;
            }
            else if (shownKey >= 0) contentFadeStart = sim;   // めくらずに中身だけ入れ替える
            shownKey = key;
        }

        // 中身の出方：入れ替えたときは短く溶け込ませる
        float contentAlpha = Mathf.Clamp01((sim - contentFadeStart) / ContentFade);
        if (flipPage != null)
        {
            float u = (sim - flipStart) / flipTime;
            bool flipping = u >= 0f && u < 1f;
            flipPage.gameObject.SetActive(flipping);
            if (flipping && flipPaper != null)
            {
                // 次へ：持ち上げて後ろへ送る　前へ：後ろから戻ってきて、下ろして垂れ下がる
                float p = flipDir >= 0 ? u : 1f - u;
                if (flipPaper.curl != paperCurl) flipPaper.curl = paperCurl;
                flipPaper.SetProgress(p);
                TintFlipPage(p);
            }
            if (u >= 0f)
            {
                // 次へ：持ち上がるページの下から中身が溶け込む　前へ：ページが着いてから中身が溶け込む
                float start = flipDir < 0 ? 1f : 0.15f;
                contentAlpha = Mathf.Min(contentAlpha, Mathf.Clamp01((u - start) * flipTime / ContentFade));
            }
        }
        if (boardContent != null) boardContent.alpha = contentAlpha;
    }

    const float ContentFade = 0.15f;

    // めくるページの色：看板の暗幕（boardDim）と同じだけ暗くし、めくれていくほど少し暗くする
    void TintFlipPage(float progress)
    {
        Color c = flipPaper.BaseColor * Mathf.Lerp(1f, 0.8f, progress);
        Color dimColor = new Color(0.2f, 0.15f, 0.15f);
        c = Color.Lerp(c, dimColor, 0.5f * (1f - panelLit));
        c.a = 1f;
        flipPaper.SetColor(c);
    }

    // 粒を出す（位置と向きはメニューの空間で）
    void Emit(ParticleSystem ps, Vector3 pos, Quaternion rot, int count) => ToyKit.Emit(ps, space.TransformPoint(pos), space.rotation * rot, count);

    Vector3 CamPosition() => camLook + new Vector3(0f, camDist * camRise, -camDist);

    static void Fade(CanvasGroup g, bool on, float dt) => g.alpha = Mathf.MoveTowards(g.alpha, on ? 1f : 0f, dt * 6f);

    void UpdateCamera(float dt)
    {
        bool focusBoard = phase == Phase.Panel || phase == Phase.Flying;
        // ローカル／オンラインを選んでいる間は、2つの積み木と看板に寄る
        bool focusChoice = phase == Phase.Choose || phase == Phase.ChoiceLeaving;
        Shot shot = focusChoice ? choiceShot : focusBoard ? panelShot : browseShot;
        float k = 1f - Mathf.Exp(-3.5f * dt);
        camLook = Vector3.Lerp(camLook, shot.look, k);
        camDist = Mathf.Lerp(camDist, shot.distance, k);
        camWidth = Mathf.Lerp(camWidth, shot.width, k);
        camPos = CamPosition();

        // 揺れは距離に合わせて大きくし、画面の上ではいつも同じくらいに見せる
        float tt = Time.unscaledTime;
        float far = camDist / 11f;
        Vector3 drift = new Vector3(Mathf.PerlinNoise(tt * 0.21f, 1.3f) - 0.5f, Mathf.PerlinNoise(2.7f, tt * 0.17f) - 0.5f, 0f) * 0.1f;
        shake = Mathf.Max(0f, shake - dt * 2.2f);
        Vector3 jolt = UnityEngine.Random.insideUnitSphere * 0.08f * shake * shake;
        // カメラもメニューの空間に置く（部屋から見ると、右に回り込んで左を向いている）
        cam.transform.position = space.TransformPoint(camPos + (drift + jolt) * far);
        // 下の帯のぶん、構図まるごと帯より上の範囲に収める：少し引いて（幅を広げて）、帯の高さの半分だけ下を向ける
        // （構図の数字は、帯が無いときに画面いっぱいで見える形のまま調整できる）
        float aspect = Mathf.Max(0.1f, cam.aspect);
        var canvasComp = overlay.GetComponentInParent<Canvas>();
        float bandShare = Mathf.Clamp01((GuideBarH + HelpBandH) * (canvasComp != null ? canvasComp.scaleFactor : 1f) / Mathf.Max(1f, Screen.height)) * fitAboveBand;
        float width = camWidth / Mathf.Max(0.3f, 1f - bandShare);
        float lift = 0.5f * bandShare * width / aspect;
        cam.transform.LookAt(space.TransformPoint(camLook + drift * 0.5f + Vector3.down * lift));
        float halfW = width * 0.5f / camDist;
        cam.fieldOfView = 2f * Mathf.Atan(halfW / Mathf.Max(0.1f, cam.aspect)) * Mathf.Rad2Deg;

        // クレジット中は全体を大きくボカす
        bool blurAll = phase == Phase.Credits || credits.IsBusy || book.IsBusy || achievements.IsBusy;
        Vector3 subject = focusChoice ? ChoiceSlot(0) : focusBoard ? boardCenter : new Vector3(stackX * 0.4f, 2f, 0.5f);
        float focus = blurAll ? 0.6f : Vector3.Distance(camPos, subject);
        post.dof.focusDistance.value = Mathf.Lerp(post.dof.focusDistance.value, focus, k * 2f);
        post.dof.aperture.value = Mathf.Lerp(post.dof.aperture.value, blurAll ? 1f : focusBoard || focusChoice ? 2f : 3.2f, k * 2f);
    }

    void UpdateUI()
    {
        titleGroup.alpha = 1f - dim;

        // 下の帯は、始まりの演出と（流れる）クレジットの間は隠す。ずかんの間は出す
        bool achOpen = achievements.IsBusy;
        bool bookOpen = book.IsBusy || achOpen;
        bool hide = phase == Phase.Intro || credits.IsBusy || phase == Phase.Credits && !bookOpen;
        helpGroup.alpha = Mathf.MoveTowards(helpGroup.alpha, hide ? 0f : 1f, Time.unscaledDeltaTime * 4f);
        var g = texts;
        helpText.text = achOpen ? g.achievements.help : bookOpen ? g.book.help : HelpText();
        if (achOpen && !achievements.HasEntries) SetGuides(g.keyBack, g.guideBack);
        else if (achOpen) SetGuides(g.keySelect, "選ぶ", "左右", "絞る", "L/R", achievements.SortLabel, "Y", achievements.DirectionLabel, g.keyBack, g.guideBack);
        else if (bookOpen) SetGuides(g.keySelect, g.guideSelect, g.keyBack, g.guideBack);
        else if (phase == Phase.Panel && Kind(outIdx) == ItemKind.Battle || phase == Phase.Choose) SetGuides(g.keySelect, g.guideSelect, g.keyDecide, g.guideDecide, g.keyBack, g.guideBack);
        else if (phase == Phase.Panel) SetGuides(g.keySelect, g.guideSelect, g.keyChange, g.guideChange, g.keyBack, g.guideBack);
        else if (!hide) SetGuides(g.keySelect, g.guideSelect, g.keyDecide, g.guideDecide);
    }

    // ================= 位置 =================
    Vector3 Slot(int i) => new Vector3(stackX, (ItemCount - 1 - i) * BH + BH * 0.5f, 0f);

    // 看板の上（左寄り）に乗る位置（得点板は横棒の上、ノートはリングの上。ノートは傾いているので傾きに沿わせる）
    Vector3 Dock()
    {
        var top = new Vector3(-boardSize.x * 0.5f + BlockSize.x * 0.65f, boardSize.y * 0.5f + boardTop, 0f);
        Vector3 p = board != null ? board.localPosition + board.localRotation * top : boardCenter + top;
        return p + Vector3.up * BH * 0.5f;
    }

    // 積み木の左面（ハンマーが当たる位置）
    Vector3 Contact(int i)
    {
        var s = Slot(i);
        return new Vector3(s.x + CursorOffset - BlockSize.x * CursorScale * 0.5f, s.y, s.z);
    }
}
