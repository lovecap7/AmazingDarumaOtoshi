using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// メニューの「じっせき」。ずかん（CharacterBook）と同じように開いて見るだけの画面。
// B / Esc / 右クリック：とじる　上下：えらぶ（実績があるとき）
// いまは実績の中身が無いので、見出しと空のカードだけを出す。
// 実績を増やすときは Entries に足す（MainMenu 側の変更はいらない）。カードに名前・説明が並ぶ。
[RequireComponent(typeof(CanvasGroup))]
public class AchievementBook : MonoBehaviour
{
    [System.Serializable]
    public class Texts
    {
        public string title = "じっせき";
        [Tooltip("まだ取っていない実績の名前の代わりに出す字")]
        public string locked = "？？？";
        [Tooltip("じっせきを開いている間、下の帯に出す説明")]
        [TextArea(1, 3)] public string help = "これまでに集めた実績が見られます。";
    }

    // 実績1つ分
    public class Entry
    {
        public string name;
        public string description;
        public bool unlocked;
        // レアリティの色。行の下地に使う（未設定なら白＝今までの見た目）
        public Color rarityColor = Color.white;
        // レアリティの帯の画像（光沢つき。未設定なら帯は出さない）
        public Sprite raritySprite;
        public Entry(string name, string description, bool unlocked) { this.name = name; this.description = description; this.unlocked = unlocked; }
    }

    // 並べる実績（どこからでも足せる。開いたときに並べ直す）
    public static readonly List<Entry> Entries = new List<Entry>();

    const float CardW = 1700f, CardH = 640f, RowH = 92f, RowGap = 10f;
    const float TopSpace = 150f, BottomSpace = 250f;   // 見出しの高さ、MainMenu の下の帯の高さ
    // 選択中の行の色。金（黄色）のレアリティと紛れないよう、青系にする
    static readonly Color SelectedRowColor = new Color(0.58f, 0.80f, 0.96f);

    TMP_FontAsset font;
    Material letterMat;
    Sprite uiSprite;
    Texts texts;

    CanvasGroup group;
    RectTransform card, listContent;
    readonly List<Image> rowBgs = new List<Image>();
    // 行ごとの下地の色（レアリティの色を混ぜたもの）
    readonly List<Color> rowBaseColors = new List<Color>();

    int index;
    bool open;
    float fade, punch;
    int openedFrame = -1, closedFrame = -1;

    // 開いている、またはとじたフレーム（同じボタン入力でメニューが反応しないように）
    public bool IsBusy => open || fade > 0f || closedFrame == Time.frameCount;
    public bool HasEntries => Entries.Count > 0;

    public void Setup(TMP_FontAsset font, Material letterMat, Sprite uiSprite, Texts texts)
    {
        this.font = font;
        this.letterMat = letterMat;
        this.uiSprite = uiSprite;
        this.texts = texts ?? new Texts();
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
        punch = 1f;
        BuildRows();
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
        punch = Mathf.MoveTowards(punch, 0f, dt / 0.25f);
        Layout();
        if (!open || Time.frameCount == openedFrame) return;

        int n = rowBgs.Count;
        // 押しっぱなしのくり返しは端で止まる。押し直したときだけ反対の端へ回る
        if (n > 0 && MenuInput.UpRepeat() && MenuInput.StepRepeat(ref index, n, MenuInput.Dir.Up, -1)) MenuSounds.Cursor();
        else if (n > 0 && MenuInput.DownRepeat() && MenuInput.StepRepeat(ref index, n, MenuInput.Dir.Down, 1)) MenuSounds.Cursor();
        else if (MenuInput.Cancel()) { Close(); return; }
        UpdateRows(dt);
    }

    void Apply()
    {
        group.alpha = fade;
        group.blocksRaycasts = fade > 0f;
        group.interactable = false;
    }

    // 見出しの下から、下の帯の上までに収まるようにカードを縮めて置く
    void Layout()
    {
        float h = ((RectTransform)transform).rect.height;
        float top = h * 0.5f - TopSpace - 10f, bottom = -h * 0.5f + BottomSpace + 10f;
        float s = Mathf.Min(1f, (top - bottom) / CardH);
        card.anchoredPosition = new Vector2(0f, (top + bottom) * 0.5f);
        card.localScale = Vector3.one * s * (1f + 0.03f * punch * punch);
    }

    void UpdateRows(float dt)
    {
        float k = 1f - Mathf.Exp(-14f * dt);
        for (int i = 0; i < rowBgs.Count; i++)
        {
            // 選んでいる行は青系（黄色は金のレアリティと紛れるので使わない）、それ以外はレアリティの色を混ぜた下地
            Color rowColor = i == index ? SelectedRowColor
                : i < rowBaseColors.Count ? rowBaseColors[i] : new Color(1f, 0.98f, 0.94f, 0.92f);
            rowBgs[i].color = Color.Lerp(rowBgs[i].color, rowColor, k);
        }
        // 選んでいる行が見えるように送る
        float view = CardH - 60f;
        float rowTop = index * (RowH + RowGap);
        float scroll = listContent.anchoredPosition.y;
        float target = Mathf.Clamp(scroll, rowTop + RowH - view, rowTop);
        target = Mathf.Clamp(target, 0f, Mathf.Max(0f, rowBgs.Count * (RowH + RowGap) - RowGap - view));
        listContent.anchoredPosition = new Vector2(0f, Mathf.Lerp(scroll, target, k));
    }

    // ---------------- 組み立て ----------------
    void Build()
    {
        var root = (RectTransform)transform;
        ToyKit.Stretch(root);

        // 背景を薄く覆う（後ろは MainMenu がボカす）
        var dim = ToyKit.UIImage("Dim", root, new Color(ToyKit.Horizon.r, ToyKit.Horizon.g, ToyKit.Horizon.b, 0.55f));
        ToyKit.Stretch(dim);

        // 見出し（左上。ずかんと同じ形）
        var bar = ToyKit.UIImage("HeaderBar", root, ToyKit.Palette[2], uiSprite);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 1f);
        bar.anchoredPosition = new Vector2(-30f, -30f);
        bar.sizeDelta = new Vector2(560f, 110f);
        var ht = ToyKit.UIText("Header", bar, font, texts.title, 66f, Color.white);
        if (letterMat != null) ht.fontSharedMaterial = letterMat;
        ht.alignment = TextAlignmentOptions.Left;
        ToyKit.Anchor(ht.rectTransform, new Vector2(0f, 0.5f), new Vector2(300f, 0f), new Vector2(500f, 110f));

        // 実績を並べるカード（いまは空）
        card = ToyKit.UIImage("Card", root, new Color(1f, 0.98f, 0.94f, 0.96f), uiSprite);
        card.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        ToyKit.Anchor(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW, CardH));
        var ol = card.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3f, -3f);

        var view = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
        view.SetParent(card, false);
        ToyKit.Stretch(view);
        view.offsetMin = new Vector2(30f, 30f);
        view.offsetMax = new Vector2(-30f, -30f);
        view.gameObject.AddComponent<RectMask2D>();

        listContent = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        listContent.SetParent(view, false);
        listContent.anchorMin = new Vector2(0f, 1f);
        listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.anchoredPosition = Vector2.zero;
    }

    // Entries から行を作り直す
    void BuildRows()
    {
        foreach (Transform c in listContent) Destroy(c.gameObject);
        rowBgs.Clear();
        rowBaseColors.Clear();
        listContent.anchoredPosition = Vector2.zero;
        listContent.sizeDelta = new Vector2(0f, Entries.Count * (RowH + RowGap));
        for (int i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            var holder = new GameObject("Row" + i, typeof(RectTransform)).GetComponent<RectTransform>();
            holder.SetParent(listContent, false);
            holder.anchorMin = new Vector2(0f, 1f); holder.anchorMax = new Vector2(1f, 1f);
            holder.pivot = new Vector2(0.5f, 1f);
            holder.sizeDelta = new Vector2(0f, RowH);
            holder.anchoredPosition = new Vector2(0f, -i * (RowH + RowGap));

            // レアリティは文字ではなく色で示す（下地に色を混ぜ、左に光沢のある帯を出す）
            Color cream = new Color(1f, 0.98f, 0.94f, 0.92f);
            Color baseColor = Color.Lerp(cream, e.rarityColor, 0.45f);
            var bg = ToyKit.UIImage("Bg", holder, baseColor, uiSprite);
            ToyKit.Stretch(bg);
            bg.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
            rowBgs.Add(bg.GetComponent<Image>());
            rowBaseColors.Add(baseColor);

            if (e.raritySprite != null)
            {
                var strip = ToyKit.UIImage("Rarity", holder, Color.white, e.raritySprite);
                var stripImage = strip.GetComponent<Image>();
                stripImage.type = Image.Type.Simple;
                stripImage.pixelsPerUnitMultiplier = 1f;
                strip.anchorMin = new Vector2(0f, 0f);
                strip.anchorMax = new Vector2(0f, 1f);
                strip.pivot = new Vector2(0f, 0.5f);
                strip.sizeDelta = new Vector2(14f, -12f);
                strip.anchoredPosition = new Vector2(6f, 0f);
            }

            var name = ToyKit.UIText("Name", holder, font, e.unlocked ? e.name : texts.locked, 40f, ToyKit.Ink);
            name.fontStyle = FontStyles.Bold;
            name.alignment = TextAlignmentOptions.Left;
            ToyKit.Anchor(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(30f + 250f, 0f), new Vector2(500f, RowH - 10f));
            var desc = ToyKit.UIText("Desc", holder, font, e.unlocked ? e.description : "", 32f,
                                     new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.8f));
            desc.alignment = TextAlignmentOptions.Left;
            desc.enableAutoSizing = true; desc.fontSizeMin = 18f; desc.fontSizeMax = 32f;
            ToyKit.Anchor(desc.rectTransform, new Vector2(0f, 0.5f), new Vector2(560f + 500f, 0f), new Vector2(1000f, RowH - 10f));
        }
    }
}
