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
// ・サウンド／ビデオ：ハンマーで叩き出すと積み木が看板の上に乗り、上の山はストンと1段落ちる
//   看板にスポットライトが当たって操作できるようになる。左の山は暗くなる。キャンセルで元に戻る
// ・クレジット：背景をボカしてスタッフロール ・やめる：画面外へ叩き飛ばして終了 ・たたかう：準備中
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
    [Tooltip("「たたかう」で移動するシーン（キャラクターセレクト）")]
    [SerializeField] string battleSceneName = "SelectScene";

    [Header("項目（上から。並びは固定：たたかう／サウンド／ビデオ／クレジット／やめる）")]
    [SerializeField] string[] labels = { "たたかう", "サウンド", "ビデオ", "クレジット", "やめる" };
    [SerializeField] string[] descriptions =
    {
        "だるまを積んで、飛ばして、\n最後の一人を落とせ！",
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

    enum Phase { Browse, Hitting, Flying, Panel, Returning, Credits, Leaving }
    enum Content { Sound, Video, Description }

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
    Material letterMat, popMat;

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

    // ---- UI ----
    RectTransform overlay;
    Image fade;
    TextMeshProUGUI hintText;
    CanvasGroup titleGroup;
    CreditRoll credits;
    CanvasGroup soundGroup, videoGroup, descGroup;
    TextMeshProUGUI descText, boardTitle;
    Image boardDim;
    Selectable soundFirst, videoFirst;
    readonly List<GameObject> pops = new List<GameObject>();

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
        var menuMesh = ToyKit.RoundedBox(BlockSize, 0.16f, 10);
        letterMat = ToyKit.LetterMaterial(font);
        popMat = ToyKit.PopMaterial(letterMat);

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

    CanvasGroup BuildSoundPanel(Transform root)
    {
        var group = Group("Sound", root);
        var names = new[] { "マスター", "BGM", "効果音" };
        var sliders = new Slider[3];
        var values = new TMP_Text[3];
        for (int i = 0; i < 3; i++)
        {
            var row = Row(group.transform, 150f - i * 150f, names[i]);
            var sg = new GameObject("Slider", typeof(RectTransform));
            sg.transform.SetParent(row.transform, false);
            ToyKit.Anchor((RectTransform)sg.transform, new Vector2(0.5f, 0.5f), new Vector2(70f, 0f), new Vector2(560f, 44f));
            var bg = ToyKit.UIImage("Background", sg.transform, new Color(0.85f, 0.78f, 0.7f), uiSprite); ToyKit.Stretch(bg);
            var fa = new GameObject("Fill Area", typeof(RectTransform)).GetComponent<RectTransform>();
            fa.SetParent(sg.transform, false); ToyKit.Stretch(fa); fa.offsetMin = new Vector2(12f, 0f); fa.offsetMax = new Vector2(-12f, 0f);
            var fill = ToyKit.UIImage("Fill", fa, ToyKit.Palette[1], uiSprite); ToyKit.Stretch(fill); fill.offsetMin = new Vector2(-12f, 0f); fill.offsetMax = new Vector2(12f, 0f);
            var ha = new GameObject("Handle Area", typeof(RectTransform)).GetComponent<RectTransform>();
            ha.SetParent(sg.transform, false); ToyKit.Stretch(ha); ha.offsetMin = new Vector2(24f, 0f); ha.offsetMax = new Vector2(-24f, 0f);
            var handle = ToyKit.UIImage("Handle", ha, ToyKit.Palette[0], knobSprite);
            handle.GetComponent<Image>().type = Image.Type.Simple;
            handle.anchorMin = new Vector2(0f, 0f); handle.anchorMax = new Vector2(0f, 1f); handle.sizeDelta = new Vector2(64f, 30f);

            var s = sg.AddComponent<Slider>();
            s.fillRect = fill; s.handleRect = handle; s.direction = Slider.Direction.LeftToRight;
            Tint(s, row.GetComponent<Image>());
            sliders[i] = s;

            values[i] = ToyKit.UIText("Value", row.transform, font, "8", 54f, ToyKit.Ink);
            ToyKit.Anchor(values[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(440f, 0f), new Vector2(120f, 100f));
        }
        Chain(sliders);
        soundFirst = sliders[0];
        group.gameObject.AddComponent<SoundSettingsPanel>().Setup(sliders[0], sliders[1], sliders[2], values[0], values[1], values[2]);
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
            var prev = Arrow(row.transform, "＜", -60f);
            var next = Arrow(row.transform, "＞", 460f);
            var val = ToyKit.UIText("Value", row.transform, font, "-", 48f, ToyKit.Ink);
            ToyKit.Anchor(val.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(200f, 0f), new Vector2(420f, 100f));
            os.Bind(val, prev, next);
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
        var r = ToyKit.UIImage("Row_" + label, parent, Color.white, uiSprite);
        ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(1100f, 120f));
        var l = ToyKit.UIText("Label", r, font, label, 54f, ToyKit.Ink);
        l.alignment = TextAlignmentOptions.Left;
        ToyKit.Anchor(l.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-360f, 0f), new Vector2(320f, 100f));
        return r.gameObject;
    }

    Button Arrow(Transform parent, string s, float x)
    {
        var a = ToyKit.UIImage(s == "＜" ? "Prev" : "Next", parent, ToyKit.Palette[1], uiSprite);
        ToyKit.Anchor(a, new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(84f, 84f));
        var img = a.GetComponent<Image>();
        img.raycastTarget = true;
        var t = ToyKit.UIText("Text", a, font, s, 48f, ToyKit.Ink);
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

        var title = ToyKit.UIText("Title", overlay, font, "MENU", 90f, ToyKit.Ink);
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
                if (hover >= 0 && hover != cursor) cursor = hover;

                if (MenuInput.Up()) cursor = (cursor + labels.Length - 1) % labels.Length;
                else if (MenuInput.Down()) cursor = (cursor + 1) % labels.Length;
                else if (MenuInput.Submit() || (click && hover >= 0)) Hit(cursor);
                break;
            }

            case Phase.Panel:
            {
                // B / Esc / 右クリック、または左の山をクリックで戻る
                if (MenuInput.Cancel() || (click && BlockUnderMouse(mouse.position.ReadValue()) >= 0)) Return();
                break;
            }

            case Phase.Credits:
                if (!credits.IsBusy) phase = Phase.Browse;
                break;
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
            case Sound:
            case Video:
                outIdx = index;
                impactTime = sim;
                phase = Phase.Flying;
                Pop("カコーン！", contact + new Vector3(-0.3f, 1.1f, -0.3f), 90f, ToyKit.Palette[1], -8f);
                break;

            case Quit:
                outIdx = index;
                impactTime = sim;
                phase = Phase.Leaving;
                Pop("カコーン！", contact + new Vector3(-0.3f, 1.1f, -0.3f), 90f, ToyKit.Palette[1], -8f);
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
                Pop("コンッ", contact + new Vector3(-0.3f, 1.0f, -0.3f), 70f, ToyKit.Palette[3], -8f);
                yield return new WaitForSecondsRealtime(0.35f);
                credits.Play();
                phase = Phase.Credits;
                break;

            default:
                // たたかう：画面外へ叩き飛ばしてキャラクターセレクトへ
                outIdx = index;
                impactTime = sim;
                phase = Phase.Leaving;
                Pop("カコーン！", contact + new Vector3(-0.3f, 1.1f, -0.3f), 90f, ToyKit.Palette[1], -8f);
                yield return new WaitForSecondsRealtime(0.45f);
                Fader.Load(battleSceneName);
                break;
        }
    }

    void Return()
    {
        phase = Phase.Returning;
        returnTime = sim;
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
        dim = Mathf.MoveTowards(dim, panel || phase == Phase.Credits ? 1f : 0f, dt * 3f);
        panelLit = Mathf.MoveTowards(panelLit, panel ? 1f : 0f, dt * 3f);

        UpdateBlocks();
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
                if (phase == Phase.Leaving)
                {
                    // やめる：画面の外へ吹き飛ぶ
                    float f = (sim - impactTime) / 0.6f;
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

        var first = outIdx == Sound ? soundFirst : videoFirst;
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
        if (!swinging && phase != Phase.Leaving)
            hammerPos = Vector3.Lerp(hammerPos, ToyKit.HammerPivot(Contact(cursor), hammerScale), 1f - Mathf.Exp(-12f * Time.deltaTime));
        hammer.position = hammerPos;
        hammer.rotation = Quaternion.Euler(0f, hammerYaw, 0f);
    }

    void UpdateBoard(float dt)
    {
        // 何を映すか：操作中はその項目、それ以外はカーソルの項目のプレビュー
        int item = (phase == Phase.Panel || phase == Phase.Flying || phase == Phase.Returning) && outIdx >= 0 ? outIdx : cursor;
        Content content = item == Sound ? Content.Sound : item == Video ? Content.Video : Content.Description;
        if (content == Content.Description) descText.text = descriptions[Mathf.Clamp(item, 0, descriptions.Length - 1)];
        boardTitle.text = outIdx >= 0 && phase == Phase.Panel ? "" : labels[item];

        Fade(soundGroup, content == Content.Sound, dt);
        Fade(videoGroup, content == Content.Video, dt);
        Fade(descGroup, content == Content.Description, dt);
        soundGroup.interactable = soundGroup.blocksRaycasts = phase == Phase.Panel && outIdx == Sound;
        videoGroup.interactable = videoGroup.blocksRaycasts = phase == Phase.Panel && outIdx == Video;

        boardDim.color = new Color(0.2f, 0.15f, 0.15f, 0.5f * (1f - panelLit));
        boardLight.intensity = panelLit * 35f;

        float p = (sim - boardPunch) / 0.35f;
        board.localScale = Vector3.one * (1f + ToyKit.Punch(p, 0.05f));
    }

    static void Fade(CanvasGroup g, bool on, float dt) => g.alpha = Mathf.MoveTowards(g.alpha, on ? 1f : 0f, dt * 6f);

    void UpdateCamera(float dt)
    {
        bool focusBoard = phase == Phase.Panel || phase == Phase.Flying;
        Vector3 pos = focusBoard ? PanelPos : BrowsePos;
        Vector3 look = focusBoard ? PanelLook : BrowseLook;
        float k = 1f - Mathf.Exp(-3.5f * dt);
        camPos = Vector3.Lerp(camPos, pos, k);
        camLook = Vector3.Lerp(camLook, look, k);
        camFov = Mathf.Lerp(camFov, focusBoard ? 38f : 40f, k);

        float tt = Time.unscaledTime;
        Vector3 drift = new Vector3(Mathf.PerlinNoise(tt * 0.21f, 1.3f) - 0.5f, Mathf.PerlinNoise(2.7f, tt * 0.17f) - 0.5f, 0f) * 0.1f;
        shake = Mathf.Max(0f, shake - dt * 2.2f);
        Vector3 jolt = UnityEngine.Random.insideUnitSphere * 0.08f * shake * shake;
        cam.transform.position = camPos + drift + jolt;
        cam.transform.LookAt(camLook + drift * 0.5f);
        cam.fieldOfView = camFov;

        // クレジット中は全体を大きくボカす
        bool blurAll = phase == Phase.Credits || credits.IsBusy;
        float focus = blurAll ? 0.6f : Vector3.Distance(camPos, focusBoard ? boardCenter : new Vector3(stackX * 0.4f, 2f, 0.5f));
        post.dof.focusDistance.value = Mathf.Lerp(post.dof.focusDistance.value, focus, k * 2f);
        post.dof.aperture.value = Mathf.Lerp(post.dof.aperture.value, blurAll ? 1f : focusBoard ? 2f : 3.2f, k * 2f);
    }

    void UpdateUI()
    {
        titleGroup.alpha = 1f - dim;
        hintText.text = phase == Phase.Panel ? "上下：えらぶ　左右：かえる　B / Esc：もどる"
                      : phase == Phase.Credits ? "" : "上下：えらぶ　A / Enter：けってい";
    }

    void Pop(string text, Vector3 world, float size, Color color, float angle)
    {
        var local = ToyKit.WorldToCanvas(cam, overlay, world);
        var go = ToyKit.Pop(this, overlay, font, popMat, text, local, size, color, angle, 0.45f, pops);
        if (go != null) go.transform.SetSiblingIndex(fade.transform.GetSiblingIndex());
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
