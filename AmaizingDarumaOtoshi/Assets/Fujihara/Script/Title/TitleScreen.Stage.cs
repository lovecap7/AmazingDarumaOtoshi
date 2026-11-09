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
    Material letterMat;

    void BuildStage()
    {
        var blockMesh = ToyKit.RoundedBox(new Vector3(BlockW, BlockH, BlockD), 0.14f, 10);
        var buttonMesh = ToyKit.RoundedBox(ButtonSize, 0.16f, 10);
        letterMat = ToyKit.LetterMaterial(font);

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
            UseBlockModel(blocks[i], col % Palette.Length, new Vector3(BlockW, BlockH, BlockD), 0f);
        }

        // ---- ボタン（上から：はじめる、やめる） ----
        buttons = new Block[menuLabels.Length];
        var buttonColorIndex = new[] { 0, 4, 3, 5 };
        for (int i = 0; i < menuLabels.Length; i++)
        {
            int ci = buttonColorIndex[i % buttonColorIndex.Length];
            buttons[i] = MakeBlock(menuLabels[i], Palette[ci], buttonMesh, new Vector2(2.1f, 0.6f), ButtonSize.z);
            // ボタンは横長の楕円の積み木にする。文字が円柱の正面から浮かないよう、文字の幅は少し狭める
            UseBlockModel(buttons[i], ci, ButtonModelSize, ButtonModelTextWidth);
            var box = buttons[i].t.gameObject.AddComponent<BoxCollider>();   // マウスで選べるように
            box.size = ButtonSize;
            buttons[i].t.gameObject.SetActive(false);
        }

        var face = ToyKit.CreateHead();
        head = face.root; eyesNormal = face.eyesNormal; eyesHappy = face.eyesHappy; cheeks = face.cheeks;
        if (headModel != null) UseHeadModel();
        if (!showHead) head.gameObject.SetActive(false);
        hammer = ToyKit.CreateHammer();
        hammer.localScale = Vector3.one * hammerScale;

        subtitle = ToyKit.Text3D(font, new Material(font.material), subtitleText, null, new Vector2(11f, 0.5f), 4f);
        subtitle.color = Ink;
        subtitle.characterSpacing = 30f;
        subtitle.transform.position = new Vector3(logoX, logoRowY[logoRowY.Length - 1] - 0.98f, logoZ);
        subtitle.alpha = 0f;

        var fx = ToyKit.CreateFx();
        confetti = fx.confetti; puff = fx.puff; sparkle = fx.sparkle;
        shockwave = fx.shockwave; groundRing = fx.groundRing; shockMat = fx.shockMat; ringMat = fx.ringMat;

        BuildUI();
    }

    // 顔をモデル（headModel）に差し替える：スマイルの球と目・口は隠し、モデルを同じ場所に置く
    // 頭の位置（head）は球の中心なので、モデルの足元が球の下端（中心から HeadR 下）に来るようにずらす
    // （まばたき・ニコッは隠した目に効くだけなので、モデルの見た目は変わらない）
    void UseHeadModel() => AttachHeadModel(head);

    // ---- 部室の部員など、ほかの演出からも同じモデルを使うための窓口 ----
    public bool HasHeadModel => headModel != null;
    public Mesh BlockModelMesh => blockModelMesh;
    // ToyKit.Palette の番号の色の積み木マテリアル（なければ null）
    public Material BlockModelMaterial(int paletteIndex)
    {
        if (blockModelMaterials == null || blockModelMaterials.Length == 0) return null;
        return blockModelMaterials[((paletteIndex % blockModelMaterials.Length) + blockModelMaterials.Length) % blockModelMaterials.Length];
    }

    // スマイルの頭（ToyKit.CreateHead の root）をモデルに差し替える。球と目・口は隠し、モデルの足元を球の下端に合わせる。
    // 頭が拡大されていても、頭のローカル座標で headModelHeight の高さになる
    public void AttachHeadModel(Transform headRoot)
    {
        if (headModel == null || headRoot == null) return;
        foreach (Transform c in headRoot) c.gameObject.SetActive(false);
        var model = Instantiate(headModel, headRoot, false);
        model.name = "HeadModel";
        model.transform.localRotation = Quaternion.Euler(0f, headModelYaw, 0f);

        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        float parentScale = Mathf.Max(headRoot.lossyScale.y, 1e-4f);
        float sizeY = b.size.y / parentScale;   // 頭のローカル座標での高さ
        float s = sizeY > 0f ? headModelHeight / sizeY : 1f;
        model.transform.localScale = Vector3.one * s;
        float bottom = (b.min.y - headRoot.position.y) / parentScale * s;
        model.transform.localPosition = new Vector3(0f, -HeadR - bottom, 0f);
    }

    // ボタンをモデルにしたときの大きさ（横長の楕円の円柱）と文字の幅
    static readonly Vector3 ButtonModelSize = new Vector3(2.5f, 0.7f, 1.3f);
    const float ButtonModelTextWidth = 1.75f;

    // 積み木の本体をモデル（blockModelMesh）に差し替える。
    // size の大きさにそろえ、マテリアルは色ごとに複製する（光らせるため）。
    // 文字はモデルの正面に付け直す。textWidth > 0 なら文字の枠の幅も変える
    void UseBlockModel(Block b, int paletteIndex, Vector3 size, float textWidth)
    {
        if (blockModelMesh == null || blockModelMaterials == null || blockModelMaterials.Length == 0) return;
        var src = blockModelMaterials[paletteIndex % blockModelMaterials.Length];
        if (src == null) return;

        var body = b.t.Find("Body");
        body.GetComponent<MeshFilter>().sharedMesh = blockModelMesh;
        var bounds = blockModelMesh.bounds;
        body.localScale = new Vector3(size.x / bounds.size.x, size.y / bounds.size.y, size.z / bounds.size.z);
        body.localPosition = -Vector3.Scale(bounds.center, body.localScale);

        var text = b.t.GetComponentInChildren<TextMeshPro>();
        if (text != null)
        {
            var p = text.transform.localPosition;
            text.transform.localPosition = new Vector3(p.x, p.y, -size.z * 0.5f - 0.006f);
            if (textWidth > 0f) text.rectTransform.sizeDelta = new Vector2(textWidth, text.rectTransform.sizeDelta.y);
        }

        var mat = new Material(src);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", Color.black);
        body.GetComponent<MeshRenderer>().sharedMaterial = mat;
        Destroy(b.mat);
        b.mat = mat;
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

        var hint = UIText("Hint", uiRoot, "上下：選ぶ　A：決定", 32f, Ink);
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
