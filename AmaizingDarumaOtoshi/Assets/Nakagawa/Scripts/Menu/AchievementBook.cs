using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// メニューの「じっせき」。ずかん（CharacterBook）と同じ形にそろえた、開いて見るだけの画面。
//   左上：見出し　上の帯：達成率・取得状況・並び替え・レアリティ別の取得数
//   左：実績の一覧（レアリティ色のアイコンと実績名だけ）　右：選んでいる実績の詳細カード
// 条件やポエムは右のカードにまとめる（一覧の行には出さない）。
// 上下：えらぶ　←→：しぼる　Q/E：ならべる　X：昇順・降順　B/Esc/右クリック：とじる　マウス：クリックでえらぶ
[RequireComponent(typeof(CanvasGroup))]
public class AchievementBook : MonoBehaviour
{
    // 画面に出す文字（MainMenu の Inspector の Texts → Achievements で書き換えられる）
    [System.Serializable]
    public class Texts
    {
        public string title = "じっせき";
        [Tooltip("シークレット実績の名前の代わりに出す字")]
        public string locked = "？？？";
        [Tooltip("じっせきを開いている間、下の帯に出す説明")]
        [TextArea(1, 3)] public string help = "実績の名前・解除条件・ポエムが見られます。";
    }

    // 実績1つ分（AchievementMenuBridge が組み立てる）
    public class Entry
    {
        public string name;
        public string description;
        public bool unlocked;

        // 詳細・並び替え用
        public string id;
        public int number;
        public bool secret;
        public bool special;
        public int rarityRank;
        public DateTime unlockedAt;
        public string obtainedText;
        public string poem;
        public string condition;
        public int progress;
        public int target;
        public bool hasProgress;

        // アイコン：レアリティ色の下地に、白いアイコン（トロフィーなど）を重ねる
        public Color color = Color.white;
        public Sprite glyph;
        // 未取得のときにアイコンの代わりに出す鍵
        public Sprite lockGlyph;

        public Entry(string name, string description, bool unlocked) { this.name = name; this.description = description; this.unlocked = unlocked; }
    }

    // レアリティ別の集計（特別実績は別枠）
    public class RaritySummary
    {
        public string label;
        public Color color;
        public Sprite icon;
        public int total;
        public int unlocked;

        public RaritySummary(string label, Color color, int total, int unlocked, Sprite icon = null)
        {
            this.label = label;
            this.color = color;
            this.total = total;
            this.unlocked = unlocked;
            this.icon = icon;
        }
    }

    // 並べる実績（どこからでも足せる。開いたときに並べ直す）
    public static readonly List<Entry> Entries = new List<Entry>();
    public static readonly List<RaritySummary> Summaries = new List<RaritySummary>();
    // 進捗バー用のきれいな角丸バー（ブリッジから渡す。無ければ uiSprite で代用）
    public static Sprite BarSprite;

    enum Filter { All, Unlocked, Locked }
    enum SortMode { Acquired, Rarity, Number, Name }

    // 寸法は、ずかん（CharacterBook）とそろえる
    const float ListW = 500f, RowH = 76f, RowGap = 10f;
    const float CardW = 1240f, CardH = 620f;
    const float StripH = 150f, StripGap = 16f;
    const float ContentH = StripH + StripGap + CardH;
    const float ListX = -640f, CardX = 280f;
    // 画面の上端から中身まで（帯は見出しと同じ高さから始める）、下端は MainMenu の帯のぶん空ける
    const float TopMargin = 30f, BottomMargin = 260f;

    // 帯は2段。上段に達成率と絞り込み・並び替え、下段にレアリティ別の取得数
    const float StripRowY = 34f;
    const float PercentX = 24f, PercentW = 144f, GaugeX = 176f, GaugeW = 200f;
    const float FilterX = 388f, FilterW = 148f, FilterGap = 10f;
    const float SortX = 866f, SortW = 160f, DirX = 1036f, DirW = 110f;
    const float SummaryLabelW = 140f, SummaryGap = 20f, SummaryPitchMax = 172f;
    const float SummaryAreaW = CardW - (24f + SummaryLabelW + SummaryGap) - 24f;

    const string SecretCondition = "条件は非公開です";
    const string PoemHiddenText = "解除すると読めます";
    const string EmptyText = "条件に合う実績がありません";
    const string LockedDateText = "—";

    // 画面の色は、ずかんの青（Palette[4]）と、カードのクリーム色で統一する
    static readonly Color AccentColor = ToyKit.Palette[4];
    static readonly Color CardColor = new Color(1f, 0.98f, 0.94f, 0.96f);
    static readonly Color RowColor = new Color(1f, 0.98f, 0.94f, 0.92f);
    static readonly Color RowHoverColor = Color.Lerp(RowColor, ToyKit.Palette[4], 0.20f);
    static readonly Color PillColor = new Color(1f, 1f, 1f, 0.92f);
    static readonly Color LockedPillColor = new Color(0.65f, 0.65f, 0.68f);
    static readonly Color LockedTileColor = new Color(0.74f, 0.74f, 0.76f);
    // 薄くて読みづらかったので、小さい字も濃いめにする
    static readonly Color Faded = new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.85f);
    static readonly Color FadedInk = new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.65f);
    static readonly Color GlyphEdge = new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.85f);

    class SummaryView
    {
        public Image icon;
        public TextMeshProUGUI label;
    }

    TMP_FontAsset font;
    TMP_FontAsset bodyFont;
    Material letterMat;
    Sprite uiSprite;
    Texts texts;

    static TMP_FontAsset s_bodyFont;

    // 見出し・名前は手書き風の font、小さい字と本文は読みやすい本文フォント。無ければ font で代用する
    TMP_FontAsset BodyFont => bodyFont != null ? bodyFont : font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetBodyFont() => s_bodyFont = null;

    static TMP_FontAsset LoadBodyFont()
    {
        if (s_bodyFont != null) return s_bodyFont;
        var source = Resources.Load<Font>("AchievementBodyFont");
        if (source != null) s_bodyFont = TMP_FontAsset.CreateFontAsset(source);
        return s_bodyFont;
    }

    CanvasGroup group;
    RectTransform content, strip, card, listView, listContent;
    RectTransform scrollTrack, scrollThumb;
    readonly List<RectTransform> rows = new List<RectTransform>();
    readonly List<Image> rowBgs = new List<Image>();

    // 上の帯
    TextMeshProUGUI percentText, sortText, dirText, summaryLabel;
    Image gaugeFill;
    readonly TextMeshProUGUI[] filterTexts = new TextMeshProUGUI[3];
    readonly Image[] filterBgs = new Image[3];
    readonly RectTransform[] filterRects = new RectTransform[3];
    RectTransform sortRect, dirRect, summaryRoot;
    readonly List<SummaryView> summaryViews = new List<SummaryView>();
    readonly List<RaritySummary> visibleSummaries = new List<RaritySummary>();
    float m_summaryPitch = 100f, m_summaryIcon = 26f;

    // 右の詳細カード
    Image tile, glyph;
    Image statusBg;
    TextMeshProUGUI nameText, statusText, dateText, numberText;
    TextMeshProUGUI conditionText, poemText, progressLabel;
    RectTransform progressBar;
    Image progressFill;

    Filter m_filter = Filter.All;
    SortMode m_sort = SortMode.Acquired;
    bool m_ascending = true;
    readonly List<Entry> m_view = new List<Entry>();

    int index, shownIndex = -1;
    int hoverIndex = -1;
    bool pointerMode;   // 直近の操作がマウスか（キーボード操作でホバー表示を消す）
    bool open;
    float fade, punch;
    int openedFrame = -1, closedFrame = -1;

    // 開いている、またはとじたフレーム（同じボタン入力でメニューが反応しないように）
    public bool IsBusy => open || fade > 0f || closedFrame == Time.frameCount;
    public bool HasEntries => Entries.Count > 0;

    public void Setup(TMP_FontAsset font, Material letterMat, Sprite uiSprite, Texts texts)
    {
        this.font = font;
        this.bodyFont = LoadBodyFont();
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
        shownIndex = -1;
        hoverIndex = -1;
        pointerMode = false;
        punch = 1f;
        m_filter = Filter.All;
        m_sort = SortMode.Acquired;
        m_ascending = true;
        RebuildView();
        MenuSounds.Decide();
    }

    void Close()
    {
        open = false;
        hoverIndex = -1;
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

        // 取得状況・並び替え（キーボード／コントローラー）
        bool viewChanged = false;
        if (MenuInput.Left()) { StepFilter(-1); viewChanged = true; }
        else if (MenuInput.Right()) { StepFilter(1); viewChanged = true; }
        if (MenuInput.SortPrev()) { StepSort(-1); viewChanged = true; }
        else if (MenuInput.SortNext()) { StepSort(1); viewChanged = true; }
        if (MenuInput.ToggleSortDirection()) { ToggleSortDirection(); viewChanged = true; }
        if (viewChanged)
        {
            pointerMode = false;
            hoverIndex = -1;
            UpdateRows(dt);
            return;
        }

        int n = rows.Count;
        // 押しっぱなしのくり返しは端で止まる。押し直したときだけ反対の端へ回る
        bool navigated = false;
        if (n > 0 && MenuInput.UpRepeat() && MenuInput.StepRepeat(ref index, n, MenuInput.Dir.Up, -1)) { navigated = true; MenuSounds.Cursor(); }
        else if (n > 0 && MenuInput.DownRepeat() && MenuInput.StepRepeat(ref index, n, MenuInput.Dir.Down, 1)) { navigated = true; MenuSounds.Cursor(); }

        if (navigated)
        {
            // キーボード・コントローラーに切り替えたら、マウスのホバーは消す
            pointerMode = false;
            hoverIndex = -1;
        }
        else if (MenuInput.Cancel())
        {
            Close();
            return;
        }
        else
        {
            HandlePointer();
        }

        if (index != shownIndex) { shownIndex = index; RefreshDetail(); }
        UpdateRows(dt);
    }

    // マウスのホバーとクリック
    void HandlePointer()
    {
        var mouse = Mouse.current;
        if (mouse == null)
        {
            hoverIndex = -1;
            return;
        }

        Vector2 screen = mouse.position.ReadValue();
        bool moved = mouse.delta.ReadValue().sqrMagnitude > 0.01f;
        bool click = MenuInput.AnyClick();
        if (moved || click) pointerMode = true;

        if (pointerMode && click)
        {
            // 帯の操作を先に見る
            if (Clicked(filterRects[0])) { SetFilter(Filter.All); return; }
            if (Clicked(filterRects[1])) { SetFilter(Filter.Unlocked); return; }
            if (Clicked(filterRects[2])) { SetFilter(Filter.Locked); return; }
            if (Clicked(sortRect)) { StepSort(1); return; }
            if (Clicked(dirRect)) { ToggleSortDirection(); return; }
        }

        int hover = -1;
        if (pointerMode && listView != null &&
            RectTransformUtility.RectangleContainsScreenPoint(listView, screen, null))
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(rows[i], screen, null))
                {
                    hover = i;
                    break;
                }
            }
        }

        if (pointerMode && click && hover >= 0)
        {
            if (hover != index) { index = hover; MenuSounds.Cursor(); }
            hoverIndex = hover;
            return;
        }
        hoverIndex = pointerMode ? hover : -1;
    }

    bool Clicked(RectTransform rect)
    {
        return rect != null && Mouse.current != null &&
               RectTransformUtility.RectangleContainsScreenPoint(rect, Mouse.current.position.ReadValue(), null);
    }

    void Apply()
    {
        group.alpha = fade;
        group.blocksRaycasts = fade > 0f;
        group.interactable = false;
    }

    // 見出しの下から、下の帯の上までに収まるように、帯・一覧・カードをまとめて縮めて置く
    void Layout()
    {
        float h = ((RectTransform)transform).rect.height;
        float top = h * 0.5f - TopMargin, bottom = -h * 0.5f + BottomMargin;
        float s = Mathf.Min(1f, (top - bottom) / ContentH);
        content.anchoredPosition = new Vector2(0f, (top + bottom) * 0.5f);
        content.localScale = Vector3.one * s * (1f + 0.03f * punch * punch);
    }

    // 選んでいる行に色を付けて少し右へ。見えないところにあれば一覧を送る
    void UpdateRows(float dt)
    {
        float k = 1f - Mathf.Exp(-14f * dt);
        for (int i = 0; i < rows.Count; i++)
        {
            bool selected = i == index;
            bool hovered = !selected && i == hoverIndex;
            Color target = selected ? AccentColor : hovered ? RowHoverColor : RowColor;
            rowBgs[i].color = Color.Lerp(rowBgs[i].color, target, k);

            Vector2 p = rows[i].anchoredPosition;
            rows[i].anchoredPosition = new Vector2(Mathf.Lerp(p.x, selected ? 24f : 0f, k), p.y);
        }

        // 選んでいる行が見えるように送る
        float view = listView != null ? listView.rect.height : 0f;
        if (view <= 1f) view = CardH;
        float rowTop = index * (RowH + RowGap);
        float scroll = listContent.anchoredPosition.y;
        float target2 = Mathf.Clamp(scroll, rowTop + RowH - view + 10f, rowTop - 10f);
        target2 = Mathf.Clamp(target2, 0f, Mathf.Max(0f, rows.Count * (RowH + RowGap) - RowGap - view));
        listContent.anchoredPosition = new Vector2(0f, Mathf.Lerp(scroll, target2, k));
        RefreshScrollbar();
    }

    void RefreshScrollbar()
    {
        if (scrollTrack == null || scrollThumb == null) return;
        float contentH = listContent.rect.height;
        float view = listView != null ? listView.rect.height : 1f;
        bool show = contentH > view + 1f;
        scrollTrack.gameObject.SetActive(show);
        if (!show) return;

        float ratio = Mathf.Clamp01(view / contentH);
        float trackH = scrollTrack.rect.height;
        float thumbH = Mathf.Max(24f, trackH * ratio);
        scrollThumb.sizeDelta = new Vector2(4f, thumbH);
        float maxScroll = Mathf.Max(0.01f, contentH - view);
        float scroll = Mathf.Clamp(-listContent.anchoredPosition.y, 0f, maxScroll);
        float y = -(scroll / maxScroll) * (trackH - thumbH);
        scrollThumb.anchoredPosition = new Vector2(0f, y);
    }

    // ---------------- 上の帯 ----------------
    // 帯の中の部品は、帯の左はしからの距離で置く（y は帯の中心から）
    static void Place(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(w, h);
    }

    void BuildStrip()
    {
        strip = ToyKit.UIRounded("Strip", content, CardColor, 26f);
        strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 1f);
        strip.pivot = new Vector2(0.5f, 1f);
        strip.anchoredPosition = new Vector2(CardX, 0f);
        strip.sizeDelta = new Vector2(CardW, StripH);
        var stripOutline = strip.gameObject.AddComponent<Outline>();
        stripOutline.effectColor = ToyKit.Ink;
        stripOutline.effectDistance = new Vector2(2f, -2f);

        // 上段：達成率とゲージ
        percentText = ToyKit.UIText("Percent", strip, BodyFont, "0%", 46f, AccentColor);
        percentText.fontStyle = FontStyles.Bold;
        percentText.alignment = TextAlignmentOptions.Left;
        Place(percentText.rectTransform, PercentX, StripRowY, PercentW, 56f);

        var gaugeBg = ToyKit.UIRounded("Gauge", strip, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.16f), 9f);
        Place(gaugeBg, GaugeX, StripRowY, GaugeW, 18f);
        var fill = ToyKit.UIImage("Fill", gaugeBg, AccentColor, BarSprite != null ? BarSprite : uiSprite);
        ToyKit.Stretch(fill);
        gaugeFill = fill.GetComponent<Image>();
        gaugeFill.type = Image.Type.Sliced;
        gaugeFill.pixelsPerUnitMultiplier = 1f;
        gaugeFill.enabled = false;

        // 上段：取得状況（すべて／取得済み／未取得）。押せることが分かるように縁を付ける
        string[] labels = { "すべて", "取得済み", "未取得" };
        for (int i = 0; i < 3; i++)
        {
            var pill = ToyKit.UIRounded("Filter" + i, strip, PillColor, 22f);
            Place(pill, FilterX + i * (FilterW + FilterGap), StripRowY, FilterW, 46f);
            AddPillOutline(pill.gameObject);
            filterRects[i] = pill;
            filterBgs[i] = pill.GetComponent<Image>();
            var t = ToyKit.UIText("Text", pill, BodyFont, labels[i], 20f, Faded);
            t.alignment = TextAlignmentOptions.Center;
            ToyKit.Stretch(t.rectTransform);
            filterTexts[i] = t;
        }

        // 上段：並び替えと、昇順／降順
        var sortPill = ToyKit.UIRounded("Sort", strip, PillColor, 22f);
        Place(sortPill, SortX, StripRowY, SortW, 46f);
        AddPillOutline(sortPill.gameObject);
        sortRect = sortPill;
        sortText = ToyKit.UIText("Text", sortPill, BodyFont, "", 20f, Faded);
        sortText.alignment = TextAlignmentOptions.Center;
        ToyKit.Stretch(sortText.rectTransform);

        var dirPill = ToyKit.UIRounded("Direction", strip, PillColor, 22f);
        Place(dirPill, DirX, StripRowY, DirW, 46f);
        AddPillOutline(dirPill.gameObject);
        dirRect = dirPill;
        dirText = ToyKit.UIText("Text", dirPill, BodyFont, "", 20f, Faded);
        dirText.alignment = TextAlignmentOptions.Center;
        ToyKit.Stretch(dirText.rectTransform);

        // 下段：レアリティ別の取得数（アイコンと「3/9」だけ。文字では書かない）
        summaryLabel = ToyKit.UIText("SummaryLabel", strip, BodyFont, "レアリティ別", 20f, Faded);
        summaryLabel.alignment = TextAlignmentOptions.Left;
        Place(summaryLabel.rectTransform, 24f, -StripRowY, SummaryLabelW, 30f);

        summaryRoot = new GameObject("Rarity", typeof(RectTransform)).GetComponent<RectTransform>();
        summaryRoot.SetParent(strip, false);
        summaryRoot.anchorMin = summaryRoot.anchorMax = new Vector2(0f, 0.5f);
        summaryRoot.pivot = new Vector2(0f, 0.5f);
        summaryRoot.anchoredPosition = new Vector2(24f + SummaryLabelW + SummaryGap, -StripRowY);
        summaryRoot.sizeDelta = new Vector2(0f, 56f);
    }

    // 帯のボタン（ピル）に、押せることが分かる細い縁を付ける
    static void AddPillOutline(GameObject go)
    {
        var ol = go.AddComponent<Outline>();
        ol.effectColor = new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.8f);
        ol.effectDistance = new Vector2(2f, -2f);
    }

    // 帯に出すレアリティ（実績が1つ以上あるものだけ）
    void CollectSummaries()
    {
        visibleSummaries.Clear();
        foreach (var s in Summaries)
        {
            if (s == null || s.total <= 0) continue;
            visibleSummaries.Add(s);
        }
    }

    void EnsureSummaryViews()
    {
        if (summaryViews.Count == visibleSummaries.Count) return;
        foreach (var v in summaryViews)
            if (v.icon != null) Destroy(v.icon.transform.parent.gameObject);
        summaryViews.Clear();

        for (int i = 0; i < visibleSummaries.Count; i++)
        {
            var root = new GameObject("Summary" + i, typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(summaryRoot, false);
            root.anchorMin = root.anchorMax = new Vector2(0f, 0.5f);
            root.pivot = new Vector2(0f, 0.5f);
            root.anchoredPosition = new Vector2(i * m_summaryPitch, 0f);
            root.sizeDelta = new Vector2(m_summaryPitch - 6f, 56f);

            var icon = ToyKit.UIImage("Icon", root, Color.white, visibleSummaries[i].icon);
            var iconImage = icon.GetComponent<Image>();
            iconImage.type = Image.Type.Simple;
            iconImage.preserveAspect = true;
            ToyKit.Anchor(icon, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(m_summaryIcon, m_summaryIcon));
            icon.pivot = new Vector2(0f, 0.5f);

            var label = ToyKit.UIText("Count", root, BodyFont, "", 20f, Faded);
            label.alignment = TextAlignmentOptions.Left;
            ToyKit.Anchor(label.rectTransform, new Vector2(0f, 0.5f),
                          new Vector2(m_summaryIcon + 10f, 0f), new Vector2(m_summaryPitch - m_summaryIcon - 12f, 30f));
            label.rectTransform.pivot = new Vector2(0f, 0.5f);

            summaryViews.Add(new SummaryView { icon = iconImage, label = label });
        }
    }

    void RefreshDashboard()
    {
        int total = Entries.Count, unlocked = 0;
        foreach (var e in Entries) if (e.unlocked) unlocked++;
        float rate = total > 0 ? (float)unlocked / total : 0f;
        if (percentText != null) percentText.text = Mathf.RoundToInt(rate * 100f) + "%";
        if (gaugeFill != null)
        {
            // 9スライスの角丸を保ったまま、幅だけを達成率に合わせる
            var rt = gaugeFill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(Mathf.Max(0.001f, rate), 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            gaugeFill.enabled = rate > 0.001f;
        }

        string[] texts = { "すべて " + total, "取得済み " + unlocked, "未取得 " + (total - unlocked) };
        for (int i = 0; i < filterBgs.Length; i++)
        {
            bool on = (int)m_filter == i;
            if (filterBgs[i] != null) filterBgs[i].color = on ? AccentColor : PillColor;
            if (filterTexts[i] != null)
            {
                filterTexts[i].text = texts[i];
                filterTexts[i].color = on ? Color.white : Faded;
                filterTexts[i].fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        if (sortText != null) sortText.text = SortName(m_sort);
        if (dirText != null) dirText.text = m_ascending ? "昇順 ↑" : "降順 ↓";

        // 下段のレアリティ別：数のぶんだけ幅を分けて、全体を真ん中にそろえる
        CollectSummaries();
        int count = visibleSummaries.Count;
        m_summaryPitch = Mathf.Min(SummaryPitchMax, SummaryAreaW / Mathf.Max(1, count));
        m_summaryIcon = Mathf.Clamp(m_summaryPitch * 0.30f, 26f, 40f);
        EnsureSummaryViews();

        // 「レアリティ別」の右の余りに、レアリティのまとまりを真ん中そろえで置く
        float blockW = count * m_summaryPitch;
        float areaX = 24f + SummaryLabelW + SummaryGap;
        float startX = areaX + Mathf.Max(0f, (SummaryAreaW - blockW) * 0.5f);
        if (summaryLabel != null) summaryLabel.gameObject.SetActive(count > 0);
        if (summaryRoot != null)
        {
            summaryRoot.anchoredPosition = new Vector2(startX, -StripRowY);
            summaryRoot.sizeDelta = new Vector2(blockW, 56f);
        }

        for (int i = 0; i < summaryViews.Count && i < visibleSummaries.Count; i++)
        {
            var s = visibleSummaries[i];
            var view = summaryViews[i];
            if (view.icon != null)
            {
                var root = view.icon.transform.parent as RectTransform;
                if (root != null)
                {
                    root.anchoredPosition = new Vector2(i * m_summaryPitch, 0f);
                    root.sizeDelta = new Vector2(m_summaryPitch - 6f, 56f);
                }
                view.icon.rectTransform.anchoredPosition = new Vector2(0f, 0f);
                view.icon.rectTransform.sizeDelta = new Vector2(m_summaryIcon, m_summaryIcon);
                view.icon.sprite = s.icon;
                view.icon.enabled = true;
                view.icon.color = s.icon != null ? Color.white : s.color;
            }
            if (view.label != null)
            {
                view.label.rectTransform.anchoredPosition = new Vector2(m_summaryIcon + 10f, 0f);
                view.label.rectTransform.sizeDelta = new Vector2(m_summaryPitch - m_summaryIcon - 12f, 30f);
                view.label.text = s.unlocked + "/" + s.total;
            }
        }
    }

    static string SortName(SortMode sort)
    {
        switch (sort)
        {
            case SortMode.Rarity: return "レアリティ";
            case SortMode.Number: return "番号順";
            case SortMode.Name: return "名前順";
            default: return "獲得順";
        }
    }

    void StepFilter(int delta)
    {
        const int count = 3;
        SetFilter((Filter)(((int)m_filter + delta + count) % count));
    }

    void SetFilter(Filter filter)
    {
        if (m_filter == filter) return;
        m_filter = filter;
        RebuildView();
        MenuSounds.Cursor();
    }

    void StepSort(int delta)
    {
        const int count = 4;
        m_sort = (SortMode)(((int)m_sort + delta + count) % count);
        RebuildView();
        MenuSounds.Cursor();
    }

    void ToggleSortDirection()
    {
        m_ascending = !m_ascending;
        RebuildView();
        MenuSounds.Cursor();
    }

    void RebuildView()
    {
        Entry keep = (index >= 0 && index < m_view.Count) ? m_view[index] : null;
        m_view.Clear();
        foreach (var e in Entries)
        {
            if (m_filter == Filter.Unlocked && !e.unlocked) continue;
            if (m_filter == Filter.Locked && e.unlocked) continue;
            m_view.Add(e);
        }
        m_view.Sort(CompareEntries);
        index = keep != null ? m_view.IndexOf(keep) : -1;
        if (index < 0) index = 0;
        if (m_view.Count == 0) index = 0;
        BuildRows();
        RefreshDashboard();
        shownIndex = index;
        RefreshDetail();
    }

    int CompareEntries(Entry a, Entry b)
    {
        int cmp;
        switch (m_sort)
        {
            case SortMode.Acquired:
                // 未解除は後ろに固定。解除済みの中だけ日時で並べる
                if (a.unlocked != b.unlocked) return a.unlocked ? -1 : 1;
                cmp = a.unlocked ? a.unlockedAt.CompareTo(b.unlockedAt) : a.number.CompareTo(b.number);
                break;
            case SortMode.Rarity:
                cmp = a.rarityRank.CompareTo(b.rarityRank);
                if (cmp == 0) cmp = a.number.CompareTo(b.number);
                break;
            case SortMode.Number:
                cmp = a.number.CompareTo(b.number);
                break;
            default:
                cmp = string.Compare(a.name, b.name, StringComparison.CurrentCulture);
                if (cmp == 0) cmp = a.number.CompareTo(b.number);
                break;
        }
        return m_ascending ? cmp : -cmp;
    }

    // ---------------- 組み立て ----------------
    void Build()
    {
        var root = (RectTransform)transform;
        ToyKit.Stretch(root);

        // 背景を薄く覆う（後ろは MainMenu がボカす）
        var dim = ToyKit.UIImage("Dim", root, new Color(ToyKit.Horizon.r, ToyKit.Horizon.g, ToyKit.Horizon.b, 0.55f));
        ToyKit.Stretch(dim);

        // 見出し（ずかんと同じ形）
        var bar = ToyKit.UIImage("HeaderBar", root, AccentColor, uiSprite);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 1f);
        bar.anchoredPosition = new Vector2(-30f, -30f);
        bar.sizeDelta = new Vector2(560f, 110f);
        var header = ToyKit.UIText("Header", bar, font, texts.title, 66f, Color.white);
        if (letterMat != null) header.fontSharedMaterial = letterMat;
        header.alignment = TextAlignmentOptions.Left;
        ToyKit.Anchor(header.rectTransform, new Vector2(0f, 0.5f), new Vector2(300f, 0f), new Vector2(500f, 110f));

        // 帯・一覧・カードはまとめて縮められるように、1つの入れ物に置く
        content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(root, false);
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 0.5f);
        content.sizeDelta = new Vector2(1920f, ContentH);

        BuildStrip();
        BuildList();
        BuildCard();
    }

    void BuildList()
    {
        listView = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
        listView.SetParent(content, false);
        listView.anchorMin = listView.anchorMax = new Vector2(0.5f, 1f);
        listView.pivot = new Vector2(0.5f, 1f);
        listView.anchoredPosition = new Vector2(ListX, -(StripH + StripGap));
        listView.sizeDelta = new Vector2(ListW + 40f, CardH);
        listView.gameObject.AddComponent<RectMask2D>();

        listContent = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        listContent.SetParent(listView, false);
        listContent.anchorMin = new Vector2(0f, 1f);
        listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.anchoredPosition = Vector2.zero;

        // 一覧の右の余白に、スクロール位置を細く出す
        var track = ToyKit.UIRounded("ScrollTrack", listView, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.14f), 2f);
        track.anchorMin = track.anchorMax = new Vector2(1f, 0.5f);
        track.pivot = new Vector2(1f, 0.5f);
        track.anchoredPosition = new Vector2(-10f, 0f);
        track.sizeDelta = new Vector2(4f, CardH - 20f);
        scrollTrack = track;

        var thumb = ToyKit.UIRounded("Thumb", track, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.5f), 2f);
        thumb.anchorMin = thumb.anchorMax = new Vector2(0.5f, 1f);
        thumb.pivot = new Vector2(0.5f, 1f);
        thumb.sizeDelta = new Vector2(4f, 80f);
        thumb.anchoredPosition = Vector2.zero;
        scrollThumb = thumb;
    }

    void BuildCard()
    {
        card = ToyKit.UIImage("Card", content, CardColor, uiSprite);
        card.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 1f);
        card.pivot = new Vector2(0.5f, 1f);
        card.anchoredPosition = new Vector2(CardX, -(StripH + StripGap));
        card.sizeDelta = new Vector2(CardW, CardH);
        var ol = card.gameObject.AddComponent<Outline>();
        ol.effectColor = ToyKit.Ink;
        ol.effectDistance = new Vector2(3f, -3f);

        var page = new GameObject("Detail", typeof(RectTransform)).GetComponent<RectTransform>();
        page.SetParent(card, false);
        ToyKit.Stretch(page);
        var center = new Vector2(0.5f, 0.5f);

        // 実績番号（カードの右上に小さく）
        numberText = ToyKit.UIText("Number", page, BodyFont, "", 22f, Faded);
        numberText.alignment = TextAlignmentOptions.Right;
        ToyKit.Anchor(numberText.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -28f), new Vector2(300f, 32f));
        numberText.rectTransform.pivot = new Vector2(1f, 1f);

        // 左：アイコンと名前、取得状況
        var tileRect = ToyKit.UIRounded("Tile", page, Color.white, 26f);
        ToyKit.Anchor(tileRect, center, new Vector2(-470f, 150f), new Vector2(240f, 240f));
        tile = tileRect.GetComponent<Image>();
        var tileOutline = tileRect.gameObject.AddComponent<Outline>();
        tileOutline.effectColor = ToyKit.Ink;
        tileOutline.effectDistance = new Vector2(3f, -3f);

        var glyphRect = ToyKit.UIImage("Glyph", tileRect, Color.white, null);
        glyphRect.anchorMin = glyphRect.anchorMax = center;
        glyphRect.sizeDelta = new Vector2(160f, 160f);
        glyph = glyphRect.GetComponent<Image>();
        glyph.type = Image.Type.Simple;
        glyph.preserveAspect = true;
        glyph.enabled = false;
        // 下地が明るい色でも白いアイコンが沈まないように、こげ茶の縁取りを付ける
        var glyphOutline = glyphRect.gameObject.AddComponent<Outline>();
        glyphOutline.effectColor = GlyphEdge;
        glyphOutline.effectDistance = new Vector2(2.5f, -2.5f);

        nameText = ToyKit.UIText("Name", page, font, "", 50f, ToyKit.Ink);
        nameText.fontStyle = FontStyles.Bold;
        nameText.enableAutoSizing = true;
        nameText.fontSizeMin = 24f;
        nameText.fontSizeMax = 50f;
        ToyKit.Anchor(nameText.rectTransform, center, new Vector2(-470f, -34f), new Vector2(290f, 66f));

        var statusRect = ToyKit.UIRounded("Status", page, AccentColor, 19f);
        ToyKit.Anchor(statusRect, center, new Vector2(-470f, -104f), new Vector2(160f, 38f));
        statusBg = statusRect.GetComponent<Image>();
        statusText = ToyKit.UIText("Text", statusRect, BodyFont, "", 22f, Color.white);
        statusText.alignment = TextAlignmentOptions.Center;
        ToyKit.Stretch(statusText.rectTransform);

        // 獲得日時（薄くて見づらいと言われたので、ほかの小さい字と同じ濃さに）
        dateText = ToyKit.UIText("Date", page, BodyFont, "", 22f, Faded);
        dateText.alignment = TextAlignmentOptions.Center;
        ToyKit.Anchor(dateText.rectTransform, center, new Vector2(-470f, -150f), new Vector2(300f, 30f));

        // 何回で達成かの進捗（1回きりの実績では出さない）
        progressLabel = ToyKit.UIText("ProgressLabel", page, BodyFont, "", 22f, Faded);
        progressLabel.alignment = TextAlignmentOptions.Center;
        ToyKit.Anchor(progressLabel.rectTransform, center, new Vector2(-470f, -206f), new Vector2(300f, 28f));

        var progressBg = ToyKit.UIRounded("ProgressBg", page, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.16f), 8f);
        ToyKit.Anchor(progressBg, center, new Vector2(-470f, -246f), new Vector2(300f, 16f));
        progressBar = progressBg;
        var fillRect = ToyKit.UIImage("Fill", progressBg, AccentColor, BarSprite != null ? BarSprite : uiSprite);
        ToyKit.Stretch(fillRect);
        progressFill = fillRect.GetComponent<Image>();
        progressFill.type = Image.Type.Sliced;
        progressFill.pixelsPerUnitMultiplier = 1f;
        progressFill.enabled = false;

        // 右：解除条件とポエム（ずかんのパッシブ・必殺技と同じ並び）
        const float x0 = -290f;
        SectionTitle(page, "解除条件", new Vector2(x0 + 100f, 232f));

        conditionText = ToyKit.UIText("Condition", page, BodyFont, "", 40f, ToyKit.Ink);
        conditionText.alignment = TextAlignmentOptions.TopLeft;
        conditionText.textWrappingMode = TextWrappingModes.Normal;
        conditionText.enableAutoSizing = true;
        conditionText.fontSizeMin = 22f;
        conditionText.fontSizeMax = 40f;
        conditionText.rectTransform.anchorMin = conditionText.rectTransform.anchorMax = center;
        conditionText.rectTransform.pivot = new Vector2(0f, 1f);
        conditionText.rectTransform.anchoredPosition = new Vector2(x0 + 10f, 196f);
        conditionText.rectTransform.sizeDelta = new Vector2(880f, 160f);

        Divider(page, x0, 24f);

        SectionTitle(page, "ポエム", new Vector2(x0 + 100f, -52f));

        // ポエムは解除条件と同じ見た目（色・大きさ・行間）にそろえる
        poemText = ToyKit.UIText("Poem", page, BodyFont, "", 40f, ToyKit.Ink);
        poemText.alignment = TextAlignmentOptions.TopLeft;
        poemText.textWrappingMode = TextWrappingModes.Normal;
        poemText.enableAutoSizing = true;
        poemText.fontSizeMin = 22f;
        poemText.fontSizeMax = 40f;
        poemText.rectTransform.anchorMin = poemText.rectTransform.anchorMax = center;
        poemText.rectTransform.pivot = new Vector2(0f, 1f);
        poemText.rectTransform.anchoredPosition = new Vector2(x0 + 10f, -88f);
        poemText.rectTransform.sizeDelta = new Vector2(880f, 180f);
    }

    // 項目名の札（ずかんのパッシブ／必殺技と同じ、橙の札）
    void SectionTitle(Transform parent, string text, Vector2 pos)
    {
        var chip = ToyKit.UIRounded("Section_" + text, parent, ToyKit.Palette[1], 22f);
        ToyKit.Anchor(chip, new Vector2(0.5f, 0.5f), pos, new Vector2(200f, 50f));
        var t = ToyKit.UIText("Text", chip, font, text, 32f, Color.white);
        t.fontStyle = FontStyles.Bold;
        t.enableAutoSizing = true;
        t.fontSizeMin = 16f;
        t.fontSizeMax = 32f;
        ToyKit.Stretch(t.rectTransform);
    }

    // 区切りの線
    void Divider(Transform parent, float x0, float y)
    {
        var line = ToyKit.UIImage("Divider", parent, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.18f));
        line.anchorMin = line.anchorMax = new Vector2(0.5f, 0.5f);
        line.pivot = new Vector2(0f, 0.5f);
        line.anchoredPosition = new Vector2(x0, y);
        line.sizeDelta = new Vector2(CardW * 0.5f - x0 - 30f, 3f);
    }

    // 表示中の実績（フィルター・並び替え後）から行を作り直す
    void BuildRows()
    {
        foreach (Transform c in listContent) Destroy(c.gameObject);
        rows.Clear();
        rowBgs.Clear();
        listContent.anchoredPosition = Vector2.zero;

        if (m_view.Count == 0)
        {
            listContent.sizeDelta = new Vector2(0f, 120f);
            var empty = ToyKit.UIText("Empty", listContent, BodyFont, EmptyText, 26f, Faded);
            empty.alignment = TextAlignmentOptions.Center;
            ToyKit.Anchor(empty.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(ListW, 60f));
            empty.rectTransform.pivot = new Vector2(0.5f, 1f);
            return;
        }

        listContent.sizeDelta = new Vector2(0f, m_view.Count * (RowH + RowGap));

        for (int i = 0; i < m_view.Count; i++)
        {
            Entry e = m_view[i];
            bool hidden = e.secret && !e.unlocked;

            var holder = new GameObject("Row" + i, typeof(RectTransform)).GetComponent<RectTransform>();
            holder.SetParent(listContent, false);
            holder.anchorMin = holder.anchorMax = new Vector2(0f, 1f);
            holder.pivot = new Vector2(0f, 1f);
            holder.sizeDelta = new Vector2(ListW, RowH);
            holder.anchoredPosition = new Vector2(0f, -i * (RowH + RowGap));

            var bg = ToyKit.UIImage("Bg", holder, RowColor, uiSprite);
            ToyKit.Stretch(bg);
            bg.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
            var ol = bg.gameObject.AddComponent<Outline>();
            ol.effectColor = ToyKit.Ink;
            ol.effectDistance = new Vector2(2f, -2f);

            // アイコン：解除済みはレアリティ色の下地、未取得は灰色の下地に鍵
            var tileRect = ToyKit.UIRounded("Tile", holder, e.unlocked ? e.color : LockedTileColor, 16f);
            ToyKit.Anchor(tileRect, new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(60f, 60f));

            Sprite glyphSprite = e.unlocked ? e.glyph : e.lockGlyph;
            if (glyphSprite != null)
            {
                float size = e.unlocked ? 42f : 34f;
                var glyphRect = ToyKit.UIImage("Glyph", tileRect, Color.white, glyphSprite);
                var glyphImage = glyphRect.GetComponent<Image>();
                glyphImage.type = Image.Type.Simple;
                glyphImage.preserveAspect = true;
                ToyKit.Anchor(glyphRect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
                // 下地が明るい色でも白いアイコンが沈まないように、こげ茶の縁取りを付ける
                var glyphOutline = glyphRect.gameObject.AddComponent<Outline>();
                glyphOutline.effectColor = GlyphEdge;
                glyphOutline.effectDistance = new Vector2(1.5f, -1.5f);
            }

            // 名前だけ（条件は右のカードに出す）
            string nameText = hidden ? texts.locked : e.name;
            Color nameColor = e.unlocked ? ToyKit.Ink : FadedInk;
            var name = ToyKit.UIText("Name", holder, font, nameText, 40f, nameColor);
            name.alignment = TextAlignmentOptions.Left;
            name.enableAutoSizing = true;
            name.fontSizeMin = 20f;
            name.fontSizeMax = 40f;
            ToyKit.Anchor(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(276f, 0f), new Vector2(360f, RowH - 10f));

            rows.Add(holder);
            rowBgs.Add(bg.GetComponent<Image>());
        }
    }

    // ---------------- 実績詳細 ----------------
    void RefreshDetail()
    {
        Entry e = (index >= 0 && index < m_view.Count) ? m_view[index] : null;
        if (e == null)
        {
            nameText.text = "";
            numberText.text = "";
            dateText.text = "";
            statusText.text = "";
            statusBg.gameObject.SetActive(false);
            conditionText.text = "";
            poemText.text = "";
            progressLabel.gameObject.SetActive(false);
            progressBar.gameObject.SetActive(false);
            glyph.enabled = false;
            tile.color = LockedTileColor;
            return;
        }

        bool hidden = e.secret && !e.unlocked;

        nameText.text = hidden ? texts.locked : e.name;
        numberText.text = "No." + e.number.ToString("D3");

        statusBg.gameObject.SetActive(true);
        statusText.text = e.unlocked ? "取得済み" : "未取得";
        statusBg.color = e.unlocked ? AccentColor : LockedPillColor;
        statusText.color = e.unlocked ? Color.white : ToyKit.Ink;

        dateText.text = e.unlocked && !string.IsNullOrEmpty(e.obtainedText) ? e.obtainedText : LockedDateText;
        conditionText.text = hidden ? SecretCondition : e.condition;
        poemText.text = hidden ? "" : e.unlocked ? e.poem : PoemHiddenText;

        // アイコン（解除済みはレアリティ色＋トロフィー、未取得は灰色＋鍵）
        tile.color = e.unlocked ? e.color : LockedTileColor;
        Sprite sprite = e.unlocked ? e.glyph : e.lockGlyph;
        glyph.sprite = sprite;
        glyph.enabled = sprite != null;
        glyph.rectTransform.sizeDelta = e.unlocked ? new Vector2(160f, 160f) : new Vector2(120f, 120f);

        // 何回で達成かの進捗
        bool showProgress = e.hasProgress && !hidden;
        progressLabel.gameObject.SetActive(showProgress);
        progressBar.gameObject.SetActive(showProgress);
        if (showProgress)
        {
            int target = Mathf.Max(1, e.target);
            int progress = Mathf.Clamp(e.progress, 0, target);
            progressLabel.text = progress + " / " + target + "（あと" + (target - progress) + "）";
            var rt = progressFill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(Mathf.Max(0.001f, (float)progress / target), 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            progressFill.enabled = progress > 0;
        }
    }
}
