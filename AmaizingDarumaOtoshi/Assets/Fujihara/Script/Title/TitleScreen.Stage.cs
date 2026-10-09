using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// タイトル画面の「舞台づくり」部分。共通の部品は ToyKit。
public partial class TitleScreen
{
    const float BlockW = ToyKit.BlockW, BlockH = ToyKit.BlockH, BlockD = ToyKit.BlockD;
    const float HeadR = ToyKit.HeadR;
    const float HammerLength = ToyKit.HammerLength;
    const float HammerHeadHalf = ToyKit.HammerHeadHalf;
    static readonly Vector3 ButtonSize = new Vector3(2.4f, 0.7f, 1.1f);
    static Color[] Palette => ToyKit.Palette;
    static Color Ink => ToyKit.Ink;

    class Block
    {
        public Transform t;
        public Material mat;
        public float phase;
        public float flash;
    }

    // ---- 舞台の参照 ----
    Camera cam;
    Bloom bloom;
    ChromaticAberration chroma;
    DepthOfField dof;
    LensDistortion lens;
    Block[] blocks;         // 文字の積み木（叩かれる順＝下から）
    Block[] buttons;        // はじめる・やめる
    Transform head, eyesNormal, eyesHappy, cheeks;
    Transform hammer;
    ParticleSystem confetti, puff, sparkle;
    Transform shockwave, groundRing;
    Material shockMat, ringMat;
    TextMeshPro subtitle;

    // ---- UI ----
    RectTransform uiRoot, barTop, barBottom;
    Image fade;
    CanvasGroup skipGroup, hintGroup, speedGroup;
    SpeedLines speedLines;
    Material letterMat, popMat;

    void BuildStage()
    {
        var blockMesh = ToyKit.RoundedBox(new Vector3(BlockW, BlockH, BlockD), 0.14f, 10);
        var buttonMesh = ToyKit.RoundedBox(ButtonSize, 0.16f, 10);
        letterMat = ToyKit.LetterMaterial(font);
        popMat = ToyKit.PopMaterial(letterMat);

        cam = ToyKit.CreateCamera();
        ToyKit.CreateLights();
        var post = ToyKit.CreatePostProcess(BloomBase);
        bloom = post.bloom; chroma = post.chroma; dof = post.dof; lens = post.lens;
        ToyKit.CreateEnvironment(blockMesh);

        // ---- 文字の積み木（下から「アメイジングだるま落とし」の順に積む） ----
        string all = lineTop + lineBottom;
        blocks = new Block[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            int col = i < lineTop.Length ? i : i - lineTop.Length;
            blocks[i] = MakeBlock(all[i].ToString(), Palette[col % Palette.Length], blockMesh, new Vector2(1.05f, 0.68f), BlockD);
        }

        // ---- ボタン（上から：はじめる、やめる） ----
        buttons = new Block[menuLabels.Length];
        var buttonColors = new[] { Palette[0], Palette[4], Palette[3], Palette[5] };
        for (int i = 0; i < menuLabels.Length; i++)
        {
            buttons[i] = MakeBlock(menuLabels[i], buttonColors[i % buttonColors.Length], buttonMesh, new Vector2(2.1f, 0.6f), ButtonSize.z);
            var box = buttons[i].t.gameObject.AddComponent<BoxCollider>();   // マウスで選べるように
            box.size = ButtonSize;
            buttons[i].t.gameObject.SetActive(false);
        }

        var face = ToyKit.CreateHead();
        head = face.root; eyesNormal = face.eyesNormal; eyesHappy = face.eyesHappy; cheeks = face.cheeks;
        hammer = ToyKit.CreateHammer();
        hammer.localScale = Vector3.one * hammerScale;

        subtitle = ToyKit.Text3D(font, new Material(font.material), subtitleText, null, new Vector2(11f, 0.5f), 4f);
        subtitle.color = Ink;
        subtitle.characterSpacing = 30f;
        subtitle.transform.position = new Vector3(0f, logoRowY[logoRowY.Length - 1] - 0.98f, logoZ);
        subtitle.alpha = 0f;

        var fx = ToyKit.CreateFx();
        confetti = fx.confetti; puff = fx.puff; sparkle = fx.sparkle;
        shockwave = fx.shockwave; groundRing = fx.groundRing; shockMat = fx.shockMat; ringMat = fx.ringMat;

        BuildUI();
    }

    Block MakeBlock(string label, Color color, Mesh mesh, Vector2 textBox, float depth)
    {
        var t = ToyKit.CreateBlock(label, color, mesh, textBox, depth, font, letterMat, out var mat);
        return new Block { t = t, mat = mat, phase = Random.value * 10f };
    }

    // ---------------- UI ----------------
    void BuildUI()
    {
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        uiRoot = ToyKit.CreateOverlayCanvas("UI", 10);

        // 集中線
        var sl = new GameObject("SpeedLines", typeof(RectTransform));
        sl.transform.SetParent(uiRoot, false);
        ToyKit.Stretch((RectTransform)sl.transform);
        speedLines = sl.AddComponent<SpeedLines>();
        speedLines.color = new Color(1f, 1f, 1f, 0.85f);
        speedLines.raycastTarget = false;
        speedGroup = sl.AddComponent<CanvasGroup>();
        speedGroup.alpha = 0f;

        // 映画の黒帯
        barTop = ToyKit.UIImage("BarTop", uiRoot, Color.black);
        barTop.anchorMin = new Vector2(0f, 1f); barTop.anchorMax = Vector2.one; barTop.pivot = new Vector2(0.5f, 1f);
        barBottom = ToyKit.UIImage("BarBottom", uiRoot, Color.black);
        barBottom.anchorMin = Vector2.zero; barBottom.anchorMax = new Vector2(1f, 0f); barBottom.pivot = new Vector2(0.5f, 0f);
        SetLetterbox(1f);

        var skip = UIText("Skip", uiRoot, "A　スキップ", 26f, new Color(1f, 1f, 1f, 0.7f));
        Anchor(skip.rectTransform, new Vector2(1f, 0f), new Vector2(-60f, 22f), new Vector2(500f, 50f));
        skip.rectTransform.pivot = new Vector2(1f, 0f);
        skip.alignment = TextAlignmentOptions.Right;
        skipGroup = skip.gameObject.AddComponent<CanvasGroup>();
        skipGroup.alpha = 0f;

        var hint = UIText("Hint", uiRoot, "上下：えらぶ　A：けってい", 32f, Ink);
        Anchor(hint.rectTransform, new Vector2(1f, 0f), new Vector2(-70f, 40f), new Vector2(700f, 50f));
        hint.rectTransform.pivot = new Vector2(1f, 0f);
        hint.alignment = TextAlignmentOptions.Right;
        hintGroup = hint.gameObject.AddComponent<CanvasGroup>();
        hintGroup.alpha = 0f;

        fade = ToyKit.UIImage("Fade", uiRoot, Color.white).GetComponent<Image>();
        ToyKit.Stretch(fade.rectTransform);
    }

    void SetLetterbox(float amount)
    {
        barTop.sizeDelta = new Vector2(0f, LetterboxHeight * amount);
        barBottom.sizeDelta = new Vector2(0f, LetterboxHeight * amount);
    }

    TextMeshProUGUI UIText(string name, Transform parent, string text, float size, Color c) =>
        ToyKit.UIText(name, parent, font, text, size, c);

    static void Anchor(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size) => ToyKit.Anchor(r, anchor, pos, size);
}
