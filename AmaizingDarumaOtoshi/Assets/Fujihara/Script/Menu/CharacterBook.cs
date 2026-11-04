using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// メニューの「ずかん」。キャラクターを左の一覧から選ぶと、右に名前・パッシブ・必殺技が出る（ハンマー性能は今は性能差が無いので出さない）。
// 上下：えらぶ　B / Esc / 右クリック：とじる。一覧はクリックでも選べる
// 中身は CharacterData（SelectRoster.characters の順）。MainMenu が組み立てて Open() で開く
[RequireComponent(typeof(CanvasGroup))]
public class CharacterBook : MonoBehaviour
{
    // 画面に出す文字（MainMenu の Inspector の Texts → Book で書き換えられる）
    [System.Serializable]
    public class Texts
    {
        public string title = "ずかん";
        public string passiveTitle = "パッシブ";
        public string specialTitle = "ひっさつわざ";
        [Tooltip("名前や説明が空欄のときに出す字")]
        public string unset = "（未設定）";
        [Tooltip("ずかんを開いている間、下の帯に出す説明")]
        [TextArea(1, 3)] public string help = "キャラクターの名前・パッシブ・必殺技が見られます。";
    }

    const float ListW = 500f, RowH = 76f, RowGap = 10f, ViewH = 640f;
    const float CardW = 1240f, CardH = 640f;

    TMP_FontAsset font;
    Material letterMat;
    Sprite uiSprite, knobSprite;
    List<CharacterData> characters;
    Texts texts;

    CanvasGroup group;
    RectTransform listView, listContent;
    readonly List<RectTransform> rows = new List<RectTransform>();
    readonly List<Image> rowBgs = new List<Image>();
    RectTransform card, charPage;
    Image portrait, portraitImage;
    TextMeshProUGUI portraitLetter, nameText, passiveName, passiveText, specialName, specialText;

    int index, shownIndex = -1;
    bool open;
    float fade, punch;
    int openedFrame = -1, closedFrame = -1;

    // 開いている、またはとじたフレーム（同じボタン入力でメニューが反応しないように）
    public bool IsBusy => open || fade > 0f || closedFrame == Time.frameCount;
    public bool HasEntries => characters != null && characters.Count > 0;
    int EntryCount => characters != null ? characters.Count : 0;

    public void Setup(TMP_FontAsset font, Material letterMat, Sprite uiSprite, Sprite knobSprite, SelectRoster roster, Texts texts)
    {
        this.font = font;
        this.letterMat = letterMat;
        this.uiSprite = uiSprite;
        this.knobSprite = knobSprite;
        this.texts = texts ?? new Texts();
        characters = roster != null ? roster.characters.FindAll(c => c != null) : new List<CharacterData>();
        group = GetComponent<CanvasGroup>();
        Build();
        Apply();
    }

    public void Open()
    {
        if (open) return;
        open = true;
        openedFrame = Time.frameCount;
        index = 0;
        shownIndex = -1;
        MenuSounds.Decide();
    }

    void Close()
    {
        open = false;
        closedFrame = Time.frameCount;
        MenuSounds.Cancel();
    }

    void Update()
    {
        if (group == null) return;
        float dt = Time.unscaledDeltaTime;
        fade = Mathf.MoveTowards(fade, open ? 1f : 0f, dt / 0.25f);
        Apply();
        if (!open) return;

        if (Time.frameCount != openedFrame)
        {
            int n = EntryCount;
            if (MenuInput.Up()) { index = (index + n - 1) % n; MenuSounds.Cursor(); }
            else if (MenuInput.Down()) { index = (index + 1) % n; MenuSounds.Cursor(); }
            else if (MenuInput.Cancel()) { Close(); return; }
            else if (MenuInput.AnyClick())
            {
                // 一覧の行をクリックで選ぶ
                for (int i = 0; i < rows.Count; i++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(rows[i], UnityEngine.InputSystem.Mouse.current.position.ReadValue(), null))
                    {
                        if (i != index) { index = i; MenuSounds.Cursor(); }
                        break;
                    }
            }
        }

        if (index != shownIndex) { Show(index); shownIndex = index; punch = 1f; }
        UpdateList(dt);
        punch = Mathf.MoveTowards(punch, 0f, dt / 0.25f);
        Layout();
    }

    // 見出しの下から、下の帯（MainMenu の説明＋操作ガイド）の上までに収まるように、一覧とカードを縮めて置く
    const float TopSpace = 150f, BottomSpace = 250f;   // 見出しの高さ、下の帯の高さ（MainMenu の GuideBarH + HelpBandH）
    void Layout()
    {
        float h = ((RectTransform)transform).rect.height;
        float top = h * 0.5f - TopSpace - 10f, bottom = -h * 0.5f + BottomSpace + 10f;
        float s = Mathf.Min(1f, (top - bottom) / ViewH);
        float cy = (top + bottom) * 0.5f;
        listView.anchoredPosition = new Vector2(-640f, cy);
        listView.localScale = Vector3.one * s;
        card.anchoredPosition = new Vector2(280f, cy);
        card.localScale = Vector3.one * s * (1f + 0.03f * punch * punch);
    }

    void Apply()
    {
        group.alpha = fade;
        group.blocksRaycasts = fade > 0f;
        group.interactable = false;
    }

    // 選んでいる行は色を付けて少し右へ。見えないところにあれば一覧を送る
    void UpdateList(float dt)
    {
        float k = 1f - Mathf.Exp(-14f * dt);
        for (int i = 0; i < rows.Count; i++)
        {
            bool sel = i == index;
            rowBgs[i].color = Color.Lerp(rowBgs[i].color, sel ? ToyKit.Palette[4] : new Color(1f, 0.98f, 0.94f, 0.92f), k);
            var p = rows[i].anchoredPosition;
            rows[i].anchoredPosition = new Vector2(Mathf.Lerp(p.x, sel ? 24f : 0f, k), p.y);
        }
        float rowTop = index * (RowH + RowGap);
        float scroll = listContent.anchoredPosition.y;
        float target = Mathf.Clamp(scroll, rowTop + RowH - ViewH + 10f, rowTop - 10f);
        target = Mathf.Clamp(target, 0f, Mathf.Max(0f, EntryCount * (RowH + RowGap) - RowGap - ViewH));
        listContent.anchoredPosition = new Vector2(0f, Mathf.Lerp(scroll, target, k));
    }

    // ---------------- 中身を出す ----------------
    void Show(int i)
    {
        var c = characters[i];
        portrait.color = c.color;
        portraitImage.sprite = c.portrait;
        portraitImage.enabled = c.portrait != null;
        portraitLetter.text = c.portrait != null ? "" : Initial(c.displayName);
        nameText.text = c.displayName;

        passiveName.text = Or(c.passiveName);
        passiveText.text = c.passiveText;

        specialName.text = Or(c.specialName);
        specialText.text = c.specialText;
    }

    string Or(string s) => string.IsNullOrEmpty(s) ? texts.unset : s;

    static string Initial(string name) => string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1);

    // ---------------- 組み立て ----------------
    void Build()
    {
        var root = (RectTransform)transform;
        ToyKit.Stretch(root);

        // 背景を薄く覆う（後ろは MainMenu がボカす）
        var dim = ToyKit.UIImage("Dim", root, new Color(ToyKit.Horizon.r, ToyKit.Horizon.g, ToyKit.Horizon.b, 0.55f));
        ToyKit.Stretch(dim);

        // 見出し（左上）
        var bar = ToyKit.UIImage("HeaderBar", root, ToyKit.Palette[4], uiSprite);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 1f);
        bar.anchoredPosition = new Vector2(-30f, -30f);
        bar.sizeDelta = new Vector2(560f, 110f);
        var ht = ToyKit.UIText("Header", bar, font, texts.title, 66f, Color.white);
        ht.fontSharedMaterial = letterMat;
        ht.alignment = TextAlignmentOptions.Left;
        ToyKit.Anchor(ht.rectTransform, new Vector2(0f, 0.5f), new Vector2(300f, 0f), new Vector2(500f, 110f));

        BuildList(root);
        BuildCard(root);
    }

    void BuildList(RectTransform root)
    {
        var view = listView = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
        view.SetParent(root, false);
        ToyKit.Anchor(view, new Vector2(0.5f, 0.5f), new Vector2(-640f, 45f), new Vector2(ListW + 40f, ViewH));
        view.gameObject.AddComponent<RectMask2D>();

        listContent = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        listContent.SetParent(view, false);
        listContent.anchorMin = new Vector2(0f, 1f);
        listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.sizeDelta = new Vector2(0f, EntryCount * (RowH + RowGap));
        listContent.anchoredPosition = Vector2.zero;

        for (int i = 0; i < EntryCount; i++)
        {
            var c = characters[i];
            var holder = new GameObject("Row" + i, typeof(RectTransform)).GetComponent<RectTransform>();
            holder.SetParent(listContent, false);
            holder.anchorMin = holder.anchorMax = new Vector2(0f, 1f);
            holder.pivot = new Vector2(0f, 1f);
            holder.sizeDelta = new Vector2(ListW, RowH);
            holder.anchoredPosition = new Vector2(0f, -i * (RowH + RowGap));

            var bg = ToyKit.UIImage("Bg", holder, Color.white, uiSprite);
            ToyKit.Stretch(bg);
            bg.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
            var ol = bg.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(2f, -2f);

            var icon = ToyKit.UIImage("Icon", holder, c.color, uiSprite);
            ToyKit.Anchor(icon, new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(60f, 60f));
            if (c.portrait != null)
            {
                var img = ToyKit.UIImage("Image", icon, Color.white).GetComponent<Image>();
                img.sprite = c.portrait;
                img.preserveAspect = true;
                ToyKit.Stretch(img.rectTransform);
            }
            else
            {
                var l = ToyKit.UIText("Letter", icon, font, Initial(c.displayName), 36f, Color.white);
                l.fontSharedMaterial = letterMat;
                ToyKit.Stretch(l.rectTransform);
            }

            var name = ToyKit.UIText("Name", holder, font, c.displayName, 40f, ToyKit.Ink);
            name.alignment = TextAlignmentOptions.Left;
            name.enableAutoSizing = true; name.fontSizeMin = 20f; name.fontSizeMax = 40f;
            ToyKit.Anchor(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(96f + 180f, 0f), new Vector2(360f, RowH - 10f));

            rows.Add(holder);
            rowBgs.Add(bg.GetComponent<Image>());
        }
    }

    void BuildCard(RectTransform root)
    {
        card = ToyKit.UIImage("Card", root, new Color(1f, 0.98f, 0.94f, 0.96f), uiSprite);
        card.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        ToyKit.Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(280f, 45f), new Vector2(CardW, CardH));
        var ol = card.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3f, -3f);
        var center = new Vector2(0.5f, 0.5f);

        charPage = Page("Character", card);

        // 左：顔と名前
        portrait = ToyKit.UIImage("Portrait", charPage, Color.white, uiSprite).GetComponent<Image>();
        ToyKit.Anchor(portrait.rectTransform, center, new Vector2(-470f, 150f), new Vector2(240f, 240f));
        var po = portrait.gameObject.AddComponent<Outline>(); po.effectColor = ToyKit.Ink; po.effectDistance = new Vector2(3f, -3f);
        portraitImage = ToyKit.UIImage("Image", portrait.rectTransform, Color.white).GetComponent<Image>();
        portraitImage.preserveAspect = true;
        ToyKit.Stretch(portraitImage.rectTransform);
        portraitLetter = ToyKit.UIText("Letter", portrait.rectTransform, font, "", 150f, Color.white);
        portraitLetter.fontSharedMaterial = letterMat;
        ToyKit.Stretch(portraitLetter.rectTransform);
        nameText = ToyKit.UIText("Name", charPage, font, "", 60f, ToyKit.Ink);
        nameText.fontStyle = FontStyles.Bold;
        nameText.enableAutoSizing = true; nameText.fontSizeMin = 28f; nameText.fontSizeMax = 60f;
        ToyKit.Anchor(nameText.rectTransform, center, new Vector2(-470f, -20f), new Vector2(300f, 80f));

        // 右：パッシブ（上半分）と必殺技（下半分）。読みやすいように大きめの字で
        const float x0 = -300f;          // 右の列の左はし
        const float textX = x0 + 10f, textW = 890f;    // 文字は右の列いっぱい
        const float wideW = textW;
        var faded = new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.85f);

        SectionTitle(charPage, texts.passiveTitle, new Vector2(x0 + 100f, 268f));
        passiveName = Text(charPage, 60f, ToyKit.Ink, new Vector2(textX, 198f), new Vector2(textW, 74f));
        passiveName.fontStyle = FontStyles.Bold;
        passiveText = Text(charPage, 36f, faded, new Vector2(textX, 90f), new Vector2(textW, 130f));
        passiveText.textWrappingMode = TextWrappingModes.Normal;
        passiveText.alignment = TextAlignmentOptions.TopLeft;

        Divider(charPage, x0, 2f);
        SectionTitle(charPage, texts.specialTitle, new Vector2(x0 + 100f, -42f));
        specialName = Text(charPage, 60f, ToyKit.Ink, new Vector2(x0 + 10f, -112f), new Vector2(wideW, 74f));
        specialName.fontStyle = FontStyles.Bold;
        specialText = Text(charPage, 36f, faded, new Vector2(x0 + 10f, -222f), new Vector2(wideW, 130f));
        specialText.textWrappingMode = TextWrappingModes.Normal;
        specialText.alignment = TextAlignmentOptions.TopLeft;
    }

    static RectTransform Page(string name, Transform parent)
    {
        var page = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        page.SetParent(parent, false);
        ToyKit.Stretch(page);
        return page;
    }

    // 左寄せの文字（pos は中心）
    TextMeshProUGUI Text(Transform parent, float size, Color color, Vector2 leftCenter, Vector2 boxSize)
    {
        var t = ToyKit.UIText("Text", parent, font, "", size, color);
        t.alignment = TextAlignmentOptions.Left;
        t.enableAutoSizing = true; t.fontSizeMin = size * 0.5f; t.fontSizeMax = size;
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        t.rectTransform.pivot = new Vector2(0f, 0.5f);
        t.rectTransform.anchoredPosition = leftCenter;
        t.rectTransform.sizeDelta = boxSize;
        return t;
    }

    // 項目名の札（パッシブ／ハンマー／ひっさつわざ）
    void SectionTitle(Transform parent, string text, Vector2 pos)
    {
        var chip = ToyKit.UIRounded("Section_" + text, parent, ToyKit.Palette[1], 20f);
        ToyKit.Anchor(chip, new Vector2(0.5f, 0.5f), pos, new Vector2(190f, 46f));
        var t = ToyKit.UIText("Text", chip, font, text, 30f, Color.white);
        t.fontStyle = FontStyles.Bold;
        t.enableAutoSizing = true; t.fontSizeMin = 16f; t.fontSizeMax = 30f;
        ToyKit.Stretch(t.rectTransform);
    }

    // 区切りの線（右の列の幅）
    void Divider(Transform parent, float x0, float y)
    {
        var line = ToyKit.UIImage("Divider", parent, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.18f));
        line.anchorMin = line.anchorMax = new Vector2(0.5f, 0.5f);
        line.pivot = new Vector2(0f, 0.5f);
        line.anchoredPosition = new Vector2(x0, y);
        line.sizeDelta = new Vector2(CardW * 0.5f - x0 - 30f, 3f);
    }
}
