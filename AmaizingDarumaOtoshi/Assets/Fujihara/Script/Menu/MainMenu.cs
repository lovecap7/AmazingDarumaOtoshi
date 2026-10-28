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

// メインメニュー。タイトルと同じおもちゃの世界で見せる。
// ・左に「たたかう／サウンド／ビデオ／クレジット／やめる」の積み木が縦に積まれ、てっぺんにスマイル
//   ハンマーが選択中の積み木の横で構える（カーソル）
// ・右の看板に、選択中の項目の設定や説明が暗いプレビューで出ている
// ・たたかう／サウンド／ビデオ：ハンマーで叩き出すと積み木が看板の上に乗り、上の山はストンと1段落ちる
//   看板にスポットライトが当たって操作できるようになる。左の山は暗くなる。キャンセルで元に戻る
//   たたかう（battleStyle = Board のとき）：看板で「ローカル／オンライン」を選ぶ
// ・たたかう（battleStyle = KnockTwice のとき）：画面外へ叩き飛ばすと、右から「ローカル」「オンライン」の積み木が
//   飛んできて山の横に積まれる。選んだ方をもう一度叩き飛ばしてシーンへ。B で2つは飛んでいき、たたかうが戻ってくる
// ・クレジット：背景をボカしてスタッフロール ・やめる：画面外へ叩き飛ばして終了
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

    public enum BattleStyle { KnockTwice, Board }
    [Tooltip("たたかうの選び方。KnockTwice：叩き飛ばすと「ローカル／オンライン」の積み木が飛んできて、もう一度叩く　Board：看板のボタンで選ぶ")]
    [SerializeField] BattleStyle battleStyle = BattleStyle.KnockTwice;

    [Header("項目（上から。並びは固定：たたかう／サウンド／ビデオ／クレジット／やめる）")]
    [SerializeField] string[] labels = { "たたかう", "サウンド", "ビデオ", "クレジット", "やめる" };
    [Tooltip("看板の説明（たたかう・サウンド・ビデオは看板に操作画面が出るので使わない）")]
    [SerializeField] string[] descriptions =
    {
        "",
        "",
        "",
        "このゲームを\nつくった人たち",
        "ゲームをおわります",
    };
    [TextArea(5, 20)]
    [SerializeField] string creditsText =
        "<size=150%>アメイジング だるま落とし</size>\n\n\n<size=110%>プログラム</size>\nFujihara\nInoue\nShitaka\nNakahira\n\n\n<size=110%>フォント</size>\nYuji Syuku (SIL Open Font License)\n\n\n\n<size=120%>Thank you for playing!</size>";

    [Header("大きさ・配置")]
    [SerializeField] float stackX = -2.9f;
    [SerializeField] float hammerScale = 0.75f;
    [SerializeField] Vector3 boardCenter = new Vector3(2.9f, 2.35f, 1.0f);
    [SerializeField] Vector2 boardSize = new Vector2(6.4f, 3.9f);

    const int Battle = 0, Sound = 1, Video = 2, Credits = 3, Quit = 4;
    static readonly Vector3 BlockSize = new Vector3(2.6f, 0.72f, 1.1f);
    const float BoardDepth = 0.3f;
    const float CanvasScale = 0.005f;   // 看板の UI：1ピクセル = 5mm
    const float FlyTime = 0.6f, HangTime = 0.05f, DropTime = 0.12f, ReturnTime = 0.55f;
    const float IdleYaw = -32f, CursorOffset = 0.3f, CursorScale = 1.06f;

    enum Phase { Browse, Hitting, Flying, Panel, Returning, Credits, Leaving, Choose, ChoiceLeaving, ChoiceBack }
    enum Content { Battle, Sound, Video, Description }

    Phase phase;
    float sim;

    // ---- 舞台 ----
    Camera cam;
    ToyKit.Post post;
    ToyKit.Fx fx;
    ToyKit.Face face;
    Transform hammer, board;
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
    float camFov = 40f, shake;
    static readonly Vector3 BrowsePos = new Vector3(-0.3f, 2.5f, -10.5f), BrowseLook = new Vector3(-0.3f, 2.2f, 0.5f);
    static readonly Vector3 PanelPos = new Vector3(1.9f, 3.0f, -8.2f), PanelLook = new Vector3(2.4f, 2.9f, 1f);
    static readonly Vector3 ChoicePos = new Vector3(2.1f, 2.05f, -7.6f), ChoiceLook = new Vector3(2.1f, 1.7f, 1f);   // ローカル／オンラインの積み木と看板を正面から

    // ---- UI ----
    RectTransform overlay;
    Image fade;
    TextMeshProUGUI hintText;
    CanvasGroup titleGroup;
    CreditRoll credits;
    CanvasGroup battleGroup, soundGroup, videoGroup, descGroup;
    TextMeshProUGUI descText, boardTitle, battleNote;
    Image boardDim;
    Selectable battleFirst, soundFirst, videoFirst;
    bool starting;                  // たたかうでローカル／オンラインを決めた（シーン移動まで操作しない）
    float noteTime = -100f;         // 「じゅんびちゅう」を出した時刻

    // ---- たたかう（KnockTwice）：ローカル／オンラインの積み木 ----
    static readonly string[] ChoiceLabels = { "ローカル", "オンライン" };
    static readonly string[] ChoiceDescriptions = { "この画面で\nみんなで あそぶ", "ネットで\nだれかと あそぶ" };
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
        camPos = BrowsePos; camLook = BrowseLook;
        hammerPos = ToyKit.HammerPivot(Contact(cursor), hammerScale);
        StartCoroutine(FadeIn());
    }

    void OnDestroy() => Time.timeScale = 1f;

    // ================= 組み立て =================
    void BuildStage()
    {
        var blockMesh = ToyKit.RoundedBox(new Vector3(ToyKit.BlockW, ToyKit.BlockH, ToyKit.BlockD), 0.14f, 10);
        menuMesh = ToyKit.RoundedBox(BlockSize, 0.16f, 10);
        letterMat = ToyKit.LetterMaterial(font);

        cam = ToyKit.CreateCamera();
        ToyKit.CreateLights();
        post = ToyKit.CreatePostProcess(0.35f);
        ToyKit.CreateEnvironment(blockMesh);
        fx = ToyKit.CreateFx();

        // 積み木（上から）。マウスで選べるようにコライダーを付ける
        int n = labels.Length;
        blocks = new Transform[n];
        blockMats = new Material[n];
        blockColors = new Color[n];
        offsetX = new float[n];
        scaleNow = new float[n];
        var colors = new[] { ToyKit.Palette[0], ToyKit.Palette[1], ToyKit.Palette[2], ToyKit.Palette[3], ToyKit.Palette[4], ToyKit.Palette[5] };
        for (int i = 0; i < n; i++)
        {
            blockColors[i] = colors[i % colors.Length];
            blocks[i] = ToyKit.CreateBlock(labels[i], blockColors[i], menuMesh, new Vector2(2.3f, 0.6f), BlockSize.z, font, letterMat, out blockMats[i]);
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
            choiceBlocks[i] = ToyKit.CreateBlock(ChoiceLabels[i], choiceColors[i], menuMesh, new Vector2(2.3f, 0.6f), BlockSize.z, font, letterMat, out choiceMats[i]);
            choiceBlocks[i].gameObject.AddComponent<BoxCollider>().size = BlockSize;
            choiceBlocks[i].gameObject.SetActive(false);
            choiceScale[i] = 1f;
        }

        face = ToyKit.CreateHead();
        hammer = ToyKit.CreateHammer();
        hammer.localScale = Vector3.one * hammerScale;

        BuildBoard();
        BuildOverlay();
    }

    // 右の看板（世界の中に立つ板。表面に設定画面の UI を貼る）
    void BuildBoard()
    {
        board = new GameObject("Board").transform;
        board.position = boardCenter;
        var body = new GameObject("Body");
        body.transform.SetParent(board, false);
        body.AddComponent<MeshFilter>().sharedMesh = ToyKit.RoundedBox(new Vector3(boardSize.x, boardSize.y, BoardDepth), 0.18f, 10);
        body.AddComponent<MeshRenderer>().sharedMaterial = ToyKit.Lit(new Color(1f, 0.97f, 0.92f), 0.4f);

        float feetH = boardCenter.y - boardSize.y * 0.5f;
        var wood = ToyKit.Lit(new Color(0.96f, 0.86f, 0.66f), 0.45f);
        foreach (float x in new[] { -boardSize.x * 0.35f, boardSize.x * 0.35f })
        {
            var leg = ToyKit.Primitive(PrimitiveType.Cylinder, "Leg", board);
            leg.localScale = new Vector3(0.18f, feetH * 0.5f, 0.18f);
            leg.localPosition = new Vector3(x, -boardSize.y * 0.5f - feetH * 0.5f, 0f);
            leg.GetComponent<Renderer>().sharedMaterial = wood;
        }

        // 決定したときに看板を照らすスポットライト
        boardLight = new GameObject("BoardLight").AddComponent<Light>();
        boardLight.type = LightType.Spot;
        boardLight.color = new Color(1f, 0.95f, 0.85f);
        boardLight.spotAngle = 60f;
        boardLight.innerSpotAngle = 35f;
        boardLight.range = 20f;
        boardLight.intensity = 0f;
        boardLight.transform.position = boardCenter + new Vector3(-1f, 4.5f, -5f);
        boardLight.transform.LookAt(boardCenter);

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

        boardTitle = ToyKit.UIText("Title", root, font, "", 64f, ToyKit.Ink);
        ToyKit.Anchor(boardTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1000f, 100f));

        battleGroup = BuildBattlePanel(root);
        soundGroup = BuildSoundPanel(root);
        videoGroup = BuildVideoPanel(root);

        var desc = new GameObject("Description", typeof(RectTransform));
        desc.transform.SetParent(root, false);
        ToyKit.Stretch((RectTransform)desc.transform);
        descGroup = desc.AddComponent<CanvasGroup>();
        descText = ToyKit.UIText("Text", desc.transform, font, "", 70f, ToyKit.Ink);
        descText.textWrappingMode = TextWrappingModes.Normal;
        ToyKit.Anchor(descText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1100f, 520f));

        // プレビュー中に暗くする幕（最前面）
        boardDim = ToyKit.UIImage("Dim", root, new Color(0.2f, 0.15f, 0.15f, 0.5f), uiSprite).GetComponent<Image>();
        ToyKit.Stretch(boardDim.rectTransform);
    }

    // たたかう：ローカル／オンラインの大きなボタン2つ
    CanvasGroup BuildBattlePanel(Transform root)
    {
        var group = Group("Battle", root);
        var names = new[] { "ローカル", "オンライン" };
        var subs = new[] { "この画面で みんなで あそぶ", "ネットで だれかと あそぶ" };
        var colors = new[] { ToyKit.Palette[0], ToyKit.Palette[4] };
        var buttons = new Button[2];
        for (int i = 0; i < 2; i++)
        {
            // 色の付いたカード（いつも見える）＋ 選んだときに光る板（Tint）
            var card = ToyKit.UIRounded("Card_" + names[i], group.transform, Color.Lerp(colors[i], Color.white, 0.55f), 36f);
            ToyKit.Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f - i * 250f), new Vector2(1100f, 210f));
            var hl = ToyKit.UIRounded("Highlight", card, Color.white, 36f);
            ToyKit.Stretch(hl);
            hl.GetComponent<Image>().raycastTarget = true;

            var label = ToyKit.UIText("Label", card, font, names[i], 84f, Color.Lerp(colors[i], ToyKit.Ink, 0.55f));
            label.fontStyle = FontStyles.Bold;
            ToyKit.Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 26f), new Vector2(1000f, 110f));
            var sub = ToyKit.UIText("Sub", card, font, subs[i], 40f, ToyKit.Ink);
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
        battleNote = ToyKit.UIText("Note", group.transform, font, "オンラインは じゅんびちゅう！", 46f, new Color(0.85f, 0.25f, 0.2f));
        battleNote.fontStyle = FontStyles.Bold;
        ToyKit.Anchor(battleNote.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 45f), new Vector2(1100f, 70f));
        battleNote.alpha = 0f;
        return group;
    }

    void ChooseBattle(int index)
    {
        bool knockTwice = battleStyle == BattleStyle.KnockTwice;
        if (knockTwice ? phase != Phase.Choose : phase != Phase.Panel || outIdx != Battle || starting) return;
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
        ToyKit.Emit(fx.confetti, contact, Quaternion.Euler(-30f, 90f, 0f), 45);
        ToyKit.Emit(fx.sparkle, contact, Quaternion.Euler(0f, 90f, 0f), 20);
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
                ToyKit.Emit(fx.puff, slot - Vector3.up * BH * 0.5f, Quaternion.Euler(-90f, 0f, 0f), 8);
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

            b.position = pos;
            b.rotation = rot;
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
        ToyKit.Emit(fx.confetti, boardCenter + new Vector3(0f, 0.5f, -0.5f), Quaternion.Euler(-60f, 0f, 0f), 50);
        ToyKit.Emit(fx.sparkle, boardCenter + new Vector3(0f, 0f, -0.5f), Quaternion.Euler(0f, 180f, 0f), 20);
        yield return new WaitForSecondsRealtime(0.45f);
        Fader.Load(scene);
    }

    CanvasGroup BuildSoundPanel(Transform root)
    {
        var group = Group("Sound", root);
        var names = new[] { "マスター", "BGM", "効果音" };
        var colors = new[] { ToyKit.Palette[1], ToyKit.Palette[4], ToyKit.Palette[3] };
        var sliders = new Slider[3];
        var meters = new RectTransform[3];
        var values = new TMP_Text[3];
        for (int i = 0; i < 3; i++)
        {
            var row = Row(group.transform, 150f - i * 150f, names[i]);

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
        Chain(sliders);
        soundFirst = sliders[0];
        group.gameObject.AddComponent<SoundSettingsPanel>().Setup(sliders[0], sliders[1], sliders[2], values[0], values[1], values[2]);
        // 目盛りは SoundSettingsPanel が 0〜10 に設定してから作る
        for (int i = 0; i < 3; i++)
        {
            meters[i].gameObject.AddComponent<VolumeMeter>().Setup(sliders[i], meters[i], colors[i], new Color(0.88f, 0.82f, 0.74f));
            sliders[i].onValueChanged.AddListener(_ => MenuSounds.Cursor());
        }
        return group;
    }

    CanvasGroup BuildVideoPanel(Transform root)
    {
        var group = Group("Video", root);
        var names = new[] { "画面モード", "解像度", "垂直同期" };
        var sels = new OptionSelector[3];
        for (int i = 0; i < 3; i++)
        {
            var row = Row(group.transform, 150f - i * 150f, names[i]);
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
        Chain(sels);
        videoFirst = sels[0];
        group.gameObject.AddComponent<VideoSettingsPanel>().Setup(sels[0], sels[1], sels[2]);
        return group;
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

        hintText = ToyKit.UIText("Hint", overlay, font, "", 32f, ToyKit.Ink);
        hintText.alignment = TextAlignmentOptions.Right;
        ToyKit.Anchor(hintText.rectTransform, new Vector2(1f, 0f), new Vector2(-70f, 40f), new Vector2(900f, 50f));
        hintText.rectTransform.pivot = new Vector2(1f, 0f);

        // クレジット（背景をボカした上に流す）
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

    IEnumerator FadeIn()
    {
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
            case Phase.Browse:
            {
                int hover = mouse != null && (mouseMoved || click) ? BlockUnderMouse(mouse.position.ReadValue()) : -1;
                if (hover >= 0 && hover != cursor) { cursor = hover; MenuSounds.Cursor(); }

                if (MenuInput.Up()) { cursor = (cursor + labels.Length - 1) % labels.Length; MenuSounds.Cursor(); }
                else if (MenuInput.Down()) { cursor = (cursor + 1) % labels.Length; MenuSounds.Cursor(); }
                else if (MenuInput.Submit() || (click && hover >= 0))
                {
                    // たたかう（KnockTwice）は叩き飛ばした瞬間に撃墜の音を鳴らすので、決定の音は重ねない
                    if (cursor != Battle || battleStyle != BattleStyle.KnockTwice) MenuSounds.Decide();
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
                if (!credits.IsBusy) phase = Phase.Browse;
                break;

            case Phase.Choose:
            {
                if (!ChoicesReady) break;
                int hover = mouse != null && (mouseMoved || click) ? ChoiceUnderMouse(mouse.position.ReadValue()) : -1;
                if (hover >= 0 && hover != choiceCursor) { choiceCursor = hover; MenuSounds.Cursor(); }

                if (MenuInput.Up() || MenuInput.Down()) { choiceCursor = 1 - choiceCursor; MenuSounds.Cursor(); }
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
        ToyKit.Emit(fx.confetti, contact, Quaternion.Euler(-30f, 90f, 0f), 35);
        ToyKit.Emit(fx.sparkle, contact, Quaternion.Euler(0f, 90f, 0f), 18);
        shake = Mathf.Max(shake, 0.5f);
        post.chroma.intensity.value = 0.4f;
        SlowMo(0.15f, 0.07f);

        switch (index)
        {
            case Battle when battleStyle == BattleStyle.KnockTwice:
                // 画面外へ叩き飛ばし（撃墜の音）、代わりにローカル／オンラインの積み木が飛んでくる
                MenuSounds.Play(MatchSounds.Instance.KO);
                outIdx = index;
                impactTime = sim;
                phase = Phase.Leaving;
                StartCoroutine(BringChoices());
                break;

            case Battle:
            case Sound:
            case Video:
                outIdx = index;
                impactTime = sim;
                phase = Phase.Flying;
                break;

            case Quit:
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

            case Credits:
                wobbleIdx = index;
                wobbleTime = sim;
                yield return new WaitForSecondsRealtime(0.35f);
                credits.Play();
                phase = Phase.Credits;
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

        bool panel = phase == Phase.Panel;
        bool choosing = phase == Phase.Choose || phase == Phase.ChoiceLeaving;
        dim = Mathf.MoveTowards(dim, panel || phase == Phase.Credits ? 1f : choosing ? 0.6f : 0f, dt * 3f);
        panelLit = Mathf.MoveTowards(panelLit, panel ? 1f : 0f, dt * 3f);

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

            b.position = pos;
            b.rotation = rot;
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
        ToyKit.Emit(fx.puff, Dock() - Vector3.up * BH * 0.5f, Quaternion.Euler(-90f, 0f, 0f), 8);
        ToyKit.Emit(fx.sparkle, Dock(), Quaternion.Euler(-90f, 0f, 0f), 14);
        shake = Mathf.Max(shake, 0.25f);

        var first = outIdx == Battle ? battleFirst : outIdx == Sound ? soundFirst : videoFirst;
        if (EventSystem.current != null && first != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
    }

    void UpdateHead()
    {
        var f = face;
        float y = labels.Length * BH + ToyKit.HeadR;
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
        f.root.position = new Vector3(stackX, y, 0f);
        f.root.rotation = headRot;
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
        if (!swinging && phase != Phase.Leaving && !(phase == Phase.ChoiceLeaving && choiceOut >= 0))
            hammerPos = Vector3.Lerp(hammerPos, ToyKit.HammerPivot(choosing ? ChoiceContact(choiceCursor) : Contact(cursor), hammerScale), 1f - Mathf.Exp(-12f * Time.deltaTime));
        hammer.position = hammerPos;
        hammer.rotation = Quaternion.Euler(0f, hammerYaw, 0f);
    }

    void UpdateBoard(float dt)
    {
        // 何を映すか：操作中はその項目、それ以外はカーソルの項目のプレビュー
        int item = (phase == Phase.Panel || phase == Phase.Flying || phase == Phase.Returning) && outIdx >= 0 ? outIdx : cursor;
        bool choosing = phase == Phase.Choose || phase == Phase.ChoiceLeaving || phase == Phase.ChoiceBack;
        Content content = item == Battle && battleStyle == BattleStyle.Board ? Content.Battle
                        : item == Sound ? Content.Sound : item == Video ? Content.Video : Content.Description;
        if (content == Content.Description)
        {
            if (choosing)
            {
                // ローカル／オンラインを選んでいる間：選んでいる方の説明（遊べないときはお知らせも）
                string note = sim - noteTime < 2f ? "\n<color=#D94033><size=65%>オンラインは じゅんびちゅう！</size></color>" : "";
                descText.text = ChoiceDescriptions[choiceCursor] + note;
            }
            else descText.text = item == Battle ? "ローカル か オンライン で\nたたかう" : descriptions[Mathf.Clamp(item, 0, descriptions.Length - 1)];
        }
        boardTitle.text = outIdx >= 0 && phase == Phase.Panel ? "" : choosing ? ChoiceLabels[choiceCursor] : labels[item];

        Fade(battleGroup, content == Content.Battle, dt);
        Fade(soundGroup, content == Content.Sound, dt);
        Fade(videoGroup, content == Content.Video, dt);
        Fade(descGroup, content == Content.Description, dt);
        battleGroup.interactable = battleGroup.blocksRaycasts = phase == Phase.Panel && outIdx == Battle && !starting;
        soundGroup.interactable = soundGroup.blocksRaycasts = phase == Phase.Panel && outIdx == Sound;
        videoGroup.interactable = videoGroup.blocksRaycasts = phase == Phase.Panel && outIdx == Video;
        // 「じゅんびちゅう」は2秒出して消える
        float nt = sim - noteTime;
        battleNote.alpha = nt < 1.6f ? 1f : Mathf.Clamp01(1f - (nt - 1.6f) / 0.4f);

        boardDim.color = new Color(0.2f, 0.15f, 0.15f, 0.5f * (1f - panelLit));
        boardLight.intensity = panelLit * 35f;

        float p = (sim - boardPunch) / 0.35f;
        board.localScale = Vector3.one * (1f + ToyKit.Punch(p, 0.05f));
    }

    static void Fade(CanvasGroup g, bool on, float dt) => g.alpha = Mathf.MoveTowards(g.alpha, on ? 1f : 0f, dt * 6f);

    void UpdateCamera(float dt)
    {
        bool focusBoard = phase == Phase.Panel || phase == Phase.Flying;
        // ローカル／オンラインを選んでいる間は、2つの積み木と看板に寄る
        bool focusChoice = phase == Phase.Choose || phase == Phase.ChoiceLeaving;
        Vector3 pos = focusChoice ? ChoicePos : focusBoard ? PanelPos : BrowsePos;
        Vector3 look = focusChoice ? ChoiceLook : focusBoard ? PanelLook : BrowseLook;
        float k = 1f - Mathf.Exp(-3.5f * dt);
        camPos = Vector3.Lerp(camPos, pos, k);
        camLook = Vector3.Lerp(camLook, look, k);
        camFov = Mathf.Lerp(camFov, focusChoice || focusBoard ? 38f : 40f, k);

        float tt = Time.unscaledTime;
        Vector3 drift = new Vector3(Mathf.PerlinNoise(tt * 0.21f, 1.3f) - 0.5f, Mathf.PerlinNoise(2.7f, tt * 0.17f) - 0.5f, 0f) * 0.1f;
        shake = Mathf.Max(0f, shake - dt * 2.2f);
        Vector3 jolt = UnityEngine.Random.insideUnitSphere * 0.08f * shake * shake;
        cam.transform.position = camPos + drift + jolt;
        cam.transform.LookAt(camLook + drift * 0.5f);
        cam.fieldOfView = camFov;

        // クレジット中は全体を大きくボカす
        bool blurAll = phase == Phase.Credits || credits.IsBusy;
        Vector3 subject = focusChoice ? ChoiceSlot(0) : focusBoard ? boardCenter : new Vector3(stackX * 0.4f, 2f, 0.5f);
        float focus = blurAll ? 0.6f : Vector3.Distance(camPos, subject);
        post.dof.focusDistance.value = Mathf.Lerp(post.dof.focusDistance.value, focus, k * 2f);
        post.dof.aperture.value = Mathf.Lerp(post.dof.aperture.value, blurAll ? 1f : focusBoard || focusChoice ? 2f : 3.2f, k * 2f);
    }

    void UpdateUI()
    {
        titleGroup.alpha = 1f - dim;
        hintText.text = phase == Phase.Panel && outIdx == Battle || phase == Phase.Choose ? "上下：えらぶ　A：けってい　B：もどる"
                      : phase == Phase.Panel ? "上下：えらぶ　左右：かえる　B：もどる"
                      : phase == Phase.Credits ? "" : "上下：えらぶ　A：けってい";
    }

    // ================= 位置 =================
    Vector3 Slot(int i) => new Vector3(stackX, (labels.Length - 1 - i) * BH + BH * 0.5f, 0f);

    // 看板の上（左寄り）に乗る位置
    Vector3 Dock() => new Vector3(boardCenter.x - boardSize.x * 0.5f + BlockSize.x * 0.65f, boardCenter.y + boardSize.y * 0.5f + BH * 0.5f, boardCenter.z);

    // 積み木の左面（ハンマーが当たる位置）
    Vector3 Contact(int i)
    {
        var s = Slot(i);
        return new Vector3(s.x + CursorOffset - BlockSize.x * CursorScale * 0.5f, s.y, s.z);
    }
}
