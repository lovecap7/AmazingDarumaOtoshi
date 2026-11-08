using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// メニューの「じっせき」。ずかん（CharacterBook）と同じように開いて見るだけの画面。
// B / Esc / 右クリック：とじる　上下：えらぶ　マウス：ホバー／クリックでえらぶ
// 行の下地はレアリティのグラデーション。ホバー・選択は外枠と光彩で示し、下地の色は変えない。
[RequireComponent(typeof(CanvasGroup))]
public class AchievementBook : MonoBehaviour
{
    [System.Serializable]
    public class Texts
    {
        public string title = "じっせき";
        [Tooltip("シークレット実績の名前の代わりに出す字")]
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

        // 一覧・詳細で使う情報
        public string id;
        public int number;
        public bool secret;
        public bool special;
        public int rarityRank;
        public string rarityLabel;
        public Color displayRarityColor = Color.white;
        public DateTime unlockedAt;
        public string obtainedText;
        public string poem;
        public string condition;
        public int progress;
        public int target;
        public bool hasProgress;

        // カードの下地（レアリティの斜めグラデーション）。未設定なら rarityColor で代用
        public Sprite background;
        // 外側に出す光彩の画像（白いアルファだけの画像。色は glowColor で付ける）
        public Sprite glowSprite;
        // 斜めライティングの光の帯（白いアルファだけの画像）
        public Sprite shineSprite;
        // 未解除のときに出す鍵アイコン
        public Sprite lockSprite;
        // 外側の細い暗い縁・明るい外枠・光彩の色。レアリティ情報（AchievementRarityInfo）から渡す
        public Color edgeColor = new Color(0.2f, 0.15f, 0.12f, 0.55f);
        public Color borderColor = Color.white;
        public Color glowColor = Color.white;

        // （予備）レアリティの代表色と帯。background が無いときの下地に使う
        public Color rarityColor = Color.white;
        public Sprite raritySprite;
        // レアリティ専用のアイコン（全レアリティ＋特別）と、未取得用のシルエット
        public Sprite icon;
        public Sprite lockedIcon;

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

    const float CardW = 1700f, CardH = 640f, RowH = 78f, RowGap = 8f;
    const float TopSpace = 150f, BottomSpace = 250f;   // 見出しの高さ、MainMenu の下の帯の高さ
    const float DashboardH = 180f, ListTop = 210f;
    // 外枠の太さ。選択中は 3px まで太くする（縁の外側の 1px と合わせて最大 4px）
    const float EdgeInset = 1f;
    const float BorderNormal = 1.5f, BorderHover = 2.5f, BorderSelected = 3f;

    // 斜めライティング。選択中は一定間隔で繰り返し、ホバーだけなら1回で止める
    const float ShineWidth = 200f, ShineHeight = 460f, ShineAngle = 35f;
    const float ShineDuration = 0.65f, ShineInterval = 2.6f, ShineAlpha = 0.20f;
    const float ShineRise = 18f, ShinePadding = 260f;

    const string SecretText = "シークレット実績";
    const string SecretCondition = "条件は非公開です";
    const string EmptyText = "条件に合う実績がありません";
    const string DetailTitle = "実績詳細";
    const string LockedDateText = "—";

    // 画面のクロームは緑で統一。文字が乗る部分は白文字が読める濃さにする
    static readonly Color AccentColor = new Color(0.18f, 0.50f, 0.31f);
    static readonly Color ProgressColor = AccentColor;
    static readonly Color LockedPillColor = new Color(0.65f, 0.65f, 0.68f);
    static readonly Color GotPillColor = AccentColor;
    static readonly Color PillColor = new Color(1f, 1f, 1f, 0.55f);
    static readonly Color DividerColor = new Color(0f, 0f, 0f, 0.12f);

    // 1行分の見た目。下地の色は変えず、外枠・光彩・フォーカス線だけを動かす
    class Row
    {
        public RectTransform holder;
        public RectTransform bg;
        public Image glow;
        public Image border;
        public Image focus;
        public Image shine;
        public float shineTime;
        public float shineWait;
        public bool shining;
        public bool wasActive;
        public Color borderBase;
    }

    class SummaryView
    {
        public Image swatch;
        public TextMeshProUGUI label;
    }

    TMP_FontAsset font;
    TMP_FontAsset bodyFont;
    Material letterMat;
    Sprite uiSprite;
    Texts texts;

    static TMP_FontAsset s_bodyFont;

    // 見出しは手書き風の font、本文は読みやすい本文フォント。無ければ font で代用する
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
    RectTransform card, listContent, listView;
    RectTransform scrollTrack, scrollThumb;
    readonly List<Row> rows = new List<Row>();

    // 収集ダッシュボード
    RectTransform dashboard, dashboardRight;
    Image progressFill;
    TextMeshProUGUI percentText, countText, sortText, directionText;
    readonly TextMeshProUGUI[] filterTexts = new TextMeshProUGUI[3];
    readonly Image[] filterBgs = new Image[3];
    readonly Image[] filterUnderlines = new Image[3];
    readonly RectTransform[] filterRects = new RectTransform[3];
    RectTransform sortRect, directionRect;
    readonly List<SummaryView> summaryViews = new List<SummaryView>();
    Filter m_filter = Filter.All;
    SortMode m_sort = SortMode.Acquired;
    bool m_ascending = true;
    readonly List<Entry> m_view = new List<Entry>();

    // 実績詳細
    RectTransform detailRoot, detailPanel, detailProgress;
    CanvasGroup detailGroup;
    Outline detailOutline;
    Image detailIconBg, detailIcon, detailRarityBg, detailStatusBg;
    Image detailLockBg, detailLock;
    Image detailProgressFill;
    TextMeshProUGUI detailName, detailIconText, detailRarityText, detailStatusText;
    TextMeshProUGUI detailCondition, detailPoem, detailDate, detailNumber;
    TextMeshProUGUI detailProgressLabel, detailProgressRemain;
    bool detailOpen;
    Entry detailEntry;

    int index;
    int hoverIndex = -1;
    bool pointerMode;   // 直近の操作がマウスか（キーボード／コントローラーのフォーカス表示を切り替える）
    bool open;
    float fade, punch;
    int openedFrame = -1, closedFrame = -1;

    // 開いている、またはとじたフレーム（同じボタン入力でメニューが反応しないように）
    public bool IsBusy => open || fade > 0f || closedFrame == Time.frameCount;
    public bool HasEntries => Entries.Count > 0;
    // 右下の操作ガイドを「とじる」だけに切り替えるため、MainMenu から参照する
    public bool IsDetailOpen => detailOpen;

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
        hoverIndex = -1;
        pointerMode = false;
        punch = 1f;
        m_filter = Filter.All;
        m_sort = SortMode.Acquired;
        m_ascending = true;
        detailOpen = false;
        detailEntry = null;
        if (detailGroup != null) detailGroup.alpha = 0f;
        RebuildView();
        MenuSounds.Decide();
    }

    void Close()
    {
        open = false;
        hoverIndex = -1;
        detailOpen = false;
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
        UpdateDetailFade(dt);
        if (!open || Time.frameCount == openedFrame) return;

        if (detailOpen)
        {
            if (MenuInput.Cancel() || MenuInput.Submit() || MenuInput.AnyClick()) CloseDetail();
            return;
        }

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
            // キーボード・コントローラーに切り替えたら、マウスのホバーは消してフォーカス線を出す
            pointerMode = false;
            hoverIndex = -1;
        }
        else if (MenuInput.Cancel())
        {
            Close();
            return;
        }
        else if (MenuInput.Submit())
        {
            if (n > 0) OpenDetail(m_view[Mathf.Clamp(index, 0, m_view.Count - 1)]);
        }
        else
        {
            HandlePointer();
        }

        UpdateRows(dt);
    }

    // マウスのホバーとクリック。入力の主導権がマウスに移ったときだけフォーカス線を消す
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
            // ダッシュボードの操作を先に見る
            if (Clicked(filterRects[0])) { SetFilter(Filter.All); return; }
            if (Clicked(filterRects[1])) { SetFilter(Filter.Unlocked); return; }
            if (Clicked(filterRects[2])) { SetFilter(Filter.Locked); return; }
            if (Clicked(sortRect)) { StepSort(1); return; }
            if (Clicked(directionRect)) { ToggleSortDirection(); return; }
        }

        int hover = -1;
        if (pointerMode && listView != null &&
            RectTransformUtility.RectangleContainsScreenPoint(listView, screen, null))
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(rows[i].holder, screen, null))
                {
                    hover = i;
                    break;
                }
            }
        }

        if (pointerMode && click && hover >= 0)
        {
            index = hover;
            OpenDetail(m_view[hover]);
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
        for (int i = 0; i < rows.Count; i++)
        {
            Row r = rows[i];
            bool selected = i == index;
            bool hovered = !selected && i == hoverIndex;

            // 下地（bg / fill）の色は一切変えない。外枠の明るさ・太さ・光彩だけを動かす
            Color targetBorder = selected ? SelectedBorder(r.borderBase)
                : hovered ? HoverBorder(r.borderBase)
                : r.borderBase;
            r.border.color = Color.Lerp(r.border.color, targetBorder, k);

            if (r.glow != null)
            {
                float glow = selected ? 0.45f : hovered ? 0.18f : 0f;
                Color glowColor = r.glow.color;
                glowColor.a = Mathf.Lerp(glowColor.a, glow, k);
                r.glow.color = glowColor;
            }

            float borderWidth = selected ? BorderSelected : hovered ? BorderHover : BorderNormal;
            float inset = EdgeInset + borderWidth;
            r.bg.offsetMin = Vector2.Lerp(r.bg.offsetMin, new Vector2(inset, inset), k);
            r.bg.offsetMax = Vector2.Lerp(r.bg.offsetMax, new Vector2(-inset, -inset), k);

            float focus = selected && !pointerMode ? 1f : 0f;
            Color focusColor = r.focus.color;
            focusColor.a = Mathf.Lerp(focusColor.a, focus, k);
            r.focus.color = focusColor;

            // 選択・ホバー中だけ、カード内を斜めの光の帯が横切る
            UpdateShine(r, selected, hovered, dt);
        }

        // 選んでいる行が見えるように送る
        float view = listView != null ? listView.rect.height : 0f;
        if (view <= 1f) view = CardH - ListTop - 30f;
        float rowTop = index * (RowH + RowGap);
        float scroll = listContent.anchoredPosition.y;
        float target = Mathf.Clamp(scroll, rowTop + RowH - view, rowTop);
        target = Mathf.Clamp(target, 0f, Mathf.Max(0f, rows.Count * (RowH + RowGap) - RowGap - view));
        listContent.anchoredPosition = new Vector2(0f, Mathf.Lerp(scroll, target, k));
        RefreshScrollbar();
    }

    void RefreshScrollbar()
    {
        if (scrollTrack == null || scrollThumb == null) return;
        float content = listContent.rect.height;
        float view = listView != null ? listView.rect.height : 1f;
        bool show = content > view + 1f;
        scrollTrack.gameObject.SetActive(show);
        if (!show) return;

        float ratio = Mathf.Clamp01(view / content);
        float trackH = scrollTrack.rect.height;
        float thumbH = Mathf.Max(24f, trackH * ratio);
        scrollThumb.sizeDelta = new Vector2(4f, thumbH);
        float maxScroll = Mathf.Max(0.01f, content - view);
        float scroll = Mathf.Clamp(-listContent.anchoredPosition.y, 0f, maxScroll);
        float y = -(scroll / maxScroll) * (trackH - thumbH);
        scrollThumb.anchoredPosition = new Vector2(0f, y);
    }

    // 斜めライティング。選択中は一定間隔で繰り返し、ホバーだけなら1回で止める。
    // 帯は Bg のマスクの中だけに描かれるので、カードの外へはみ出さない。
    void UpdateShine(Row r, bool selected, bool hovered, float dt)
    {
        if (r.shine == null) return;

        bool active = selected || hovered;
        if (active && !r.wasActive) StartShine(r);
        else if (!active) r.shining = false;
        r.wasActive = active;

        if (!r.shining && selected)
        {
            r.shineWait -= dt;
            if (r.shineWait <= 0f) StartShine(r);
        }

        float targetAlpha = 0f;
        if (r.shining)
        {
            r.shineTime += dt;
            float t = Mathf.Clamp01(r.shineTime / ShineDuration);
            if (t >= 1f)
            {
                r.shining = false;
                r.shineTime = 0f;
                // 選択中だけ次の光まで間隔をあける。ホバーだけならここで終わり
                r.shineWait = selected ? ShineInterval : 0f;
            }
            else
            {
                float e = Mathf.SmoothStep(0f, 1f, t);
                float half = (CardW - 60f) * 0.5f;   // 行の横幅（List の左右 30px を除いた分）
                r.shine.rectTransform.anchoredPosition = new Vector2(
                    Mathf.Lerp(-half - ShinePadding, half + ShinePadding, e),
                    Mathf.Lerp(ShineRise, -ShineRise, e));
                targetAlpha = ShineAlpha;
            }
        }

        // 消えるときは滑らかに。帯の外側はカードのマスクで切られている
        Color c = r.shine.color;
        c.a = Mathf.Lerp(c.a, targetAlpha, 1f - Mathf.Exp(-18f * dt));
        r.shine.color = c;
    }

    static void StartShine(Row r)
    {
        r.shining = true;
        r.shineTime = 0f;
        r.shineWait = 0f;
    }

    // ---------------- 収集ダッシュボード ----------------
    void BuildDashboard()
    {
        dashboard = new GameObject("Dashboard", typeof(RectTransform)).GetComponent<RectTransform>();
        dashboard.SetParent(card, false);
        dashboard.anchorMin = dashboard.anchorMax = new Vector2(0.5f, 1f);
        dashboard.pivot = new Vector2(0.5f, 1f);
        dashboard.anchoredPosition = new Vector2(0f, -20f);
        dashboard.sizeDelta = new Vector2(CardW - 60f, DashboardH);

        // 左：達成率・フィルター・並び替え
        var left = new GameObject("Left", typeof(RectTransform)).GetComponent<RectTransform>();
        left.SetParent(dashboard, false);
        left.anchorMin = left.anchorMax = new Vector2(0f, 1f);
        left.pivot = new Vector2(0f, 1f);
        left.sizeDelta = new Vector2(800f, DashboardH);

        var title = ToyKit.UIText("Title", left, BodyFont, "実績コンプリート", 22f,
                                  new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.7f));
        title.alignment = TextAlignmentOptions.Left;
        title.rectTransform.pivot = new Vector2(0f, 1f);
        ToyKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -2f), new Vector2(600f, 30f));

        percentText = ToyKit.UIText("Percent", left, BodyFont, "0%", 52f, ProgressColor);
        percentText.fontStyle = FontStyles.Bold;
        percentText.alignment = TextAlignmentOptions.Right;
        percentText.rectTransform.pivot = new Vector2(1f, 1f);
        ToyKit.Anchor(percentText.rectTransform, new Vector2(1f, 1f), new Vector2(0f, -2f), new Vector2(300f, 62f));

        var barBg = ToyKit.UIRounded("ProgressBg", left, new Color(0.90f, 0.90f, 0.90f, 0.8f), 9f);
        barBg.anchorMin = barBg.anchorMax = new Vector2(0.5f, 1f);
        barBg.pivot = new Vector2(0.5f, 1f);
        barBg.anchoredPosition = new Vector2(0f, -66f);
        barBg.sizeDelta = new Vector2(800f, 18f);
        var fill = ToyKit.UIImage("ProgressFill", barBg, ProgressColor, BarSprite != null ? BarSprite : uiSprite);
        ToyKit.Stretch(fill);
        progressFill = fill.GetComponent<Image>();
        progressFill.type = Image.Type.Sliced;
        progressFill.pixelsPerUnitMultiplier = 1f;
        progressFill.enabled = false;

        countText = ToyKit.UIText("Count", left, BodyFont, "", 20f,
                                  new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.7f));
        countText.alignment = TextAlignmentOptions.Left;
        countText.rectTransform.pivot = new Vector2(0f, 1f);
        ToyKit.Anchor(countText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -88f), new Vector2(600f, 24f));

        var filterLabel = ToyKit.UIText("FilterLabel", left, BodyFont, "取得状況", 22f, ToyKit.Ink);
        filterLabel.fontStyle = FontStyles.Bold;
        filterLabel.alignment = TextAlignmentOptions.Left;
        filterLabel.rectTransform.pivot = new Vector2(0f, 1f);
        ToyKit.Anchor(filterLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -112f), new Vector2(110f, 28f));
        var filterKeyBg = ToyKit.UIRounded("FilterKeyBg", left, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.10f), 6f);
        filterKeyBg.anchorMin = filterKeyBg.anchorMax = new Vector2(0f, 1f);
        filterKeyBg.pivot = new Vector2(0f, 1f);
        filterKeyBg.anchoredPosition = new Vector2(110f, -112f);
        filterKeyBg.sizeDelta = new Vector2(44f, 24f);
        var filterKey = ToyKit.UIText("FilterKey", filterKeyBg, BodyFont, "←→", 16f,
                                      new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.8f));
        filterKey.alignment = TextAlignmentOptions.Center;
        ToyKit.Stretch(filterKey.rectTransform);

        string[] labels = { "すべて", "取得済み", "未取得" };
        for (int i = 0; i < 3; i++)
        {
            var pill = ToyKit.UIRounded("Filter" + i, left, PillColor, 16f);
            pill.anchorMin = pill.anchorMax = new Vector2(0f, 1f);
            pill.pivot = new Vector2(0f, 1f);
            pill.anchoredPosition = new Vector2(160f + i * 170f, -108f);
            pill.sizeDelta = new Vector2(150f, 32f);
            filterRects[i] = pill;
            filterBgs[i] = pill.GetComponent<Image>();
            var t = ToyKit.UIText("Text", pill, BodyFont, labels[i], 19f, ToyKit.Ink);
            t.alignment = TextAlignmentOptions.Center;
            ToyKit.Stretch(t.rectTransform);
            filterTexts[i] = t;

            var under = ToyKit.UIRounded("Underline", pill, Color.white, 1.5f);
            under.anchorMin = under.anchorMax = new Vector2(0.5f, 0f);
            under.pivot = new Vector2(0.5f, 0f);
            under.anchoredPosition = new Vector2(0f, 3f);
            under.sizeDelta = new Vector2(100f, 3f);
            filterUnderlines[i] = under.GetComponent<Image>();
        }

        var sortLabel = ToyKit.UIText("SortLabel", left, BodyFont, "並び替え", 22f, ToyKit.Ink);
        sortLabel.fontStyle = FontStyles.Bold;
        sortLabel.alignment = TextAlignmentOptions.Left;
        sortLabel.rectTransform.pivot = new Vector2(0f, 1f);
        ToyKit.Anchor(sortLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -144f), new Vector2(110f, 28f));

        var sortPill = ToyKit.UIRounded("Sort", left, PillColor, 16f);
        sortPill.anchorMin = sortPill.anchorMax = new Vector2(0f, 1f);
        sortPill.pivot = new Vector2(0f, 1f);
        sortPill.anchoredPosition = new Vector2(160f, -140f);
        sortPill.sizeDelta = new Vector2(230f, 32f);
        sortRect = sortPill;
        sortText = ToyKit.UIText("Text", sortPill, BodyFont, "", 20f, ToyKit.Ink);
        sortText.alignment = TextAlignmentOptions.Left;
        sortText.rectTransform.pivot = new Vector2(0f, 0.5f);
        ToyKit.Anchor(sortText.rectTransform, new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(160f, 32f));
        var sortKeyBg = ToyKit.UIRounded("KeyBg", sortPill, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.09f), 5f);
        sortKeyBg.anchorMin = sortKeyBg.anchorMax = new Vector2(1f, 0.5f);
        sortKeyBg.pivot = new Vector2(1f, 0.5f);
        sortKeyBg.anchoredPosition = new Vector2(-8f, 0f);
        sortKeyBg.sizeDelta = new Vector2(42f, 22f);
        var sortKey = ToyKit.UIText("Key", sortKeyBg, BodyFont, "L/R", 16f,
                                    new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.8f));
        sortKey.alignment = TextAlignmentOptions.Center;
        ToyKit.Stretch(sortKey.rectTransform);

        var dirPill = ToyKit.UIRounded("Direction", left, PillColor, 16f);
        dirPill.anchorMin = dirPill.anchorMax = new Vector2(0f, 1f);
        dirPill.pivot = new Vector2(0f, 1f);
        dirPill.anchoredPosition = new Vector2(404f, -140f);
        dirPill.sizeDelta = new Vector2(130f, 32f);
        directionRect = dirPill;
        directionText = ToyKit.UIText("Text", dirPill, BodyFont, "", 20f, ToyKit.Ink);
        directionText.alignment = TextAlignmentOptions.Left;
        directionText.rectTransform.pivot = new Vector2(0f, 0.5f);
        ToyKit.Anchor(directionText.rectTransform, new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(80f, 32f));
        var dirKeyBg = ToyKit.UIRounded("KeyBg", dirPill, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.09f), 5f);
        dirKeyBg.anchorMin = dirKeyBg.anchorMax = new Vector2(1f, 0.5f);
        dirKeyBg.pivot = new Vector2(1f, 0.5f);
        dirKeyBg.anchoredPosition = new Vector2(-8f, 0f);
        dirKeyBg.sizeDelta = new Vector2(24f, 22f);
        var dirKey = ToyKit.UIText("Key", dirKeyBg, BodyFont, "Y", 16f,
                                   new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.8f));
        dirKey.alignment = TextAlignmentOptions.Center;
        ToyKit.Stretch(dirKey.rectTransform);

        // 右：レアリティ別の集計
        dashboardRight = new GameObject("Right", typeof(RectTransform)).GetComponent<RectTransform>();
        dashboardRight.SetParent(dashboard, false);
        dashboardRight.anchorMin = dashboardRight.anchorMax = new Vector2(1f, 1f);
        dashboardRight.pivot = new Vector2(1f, 1f);
        dashboardRight.sizeDelta = new Vector2(800f, DashboardH);
    }

    void EnsureSummaryViews()
    {
        if (summaryViews.Count == Summaries.Count) return;
        foreach (var s in summaryViews) if (s.label != null) Destroy(s.label.transform.parent.gameObject);
        summaryViews.Clear();
        for (int i = 0; i < Summaries.Count; i++)
        {
            var root = new GameObject("Summary" + i, typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(dashboardRight, false);
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(40f + (i % 3) * 240f, -4f - (i / 3) * 52f);
            root.sizeDelta = new Vector2(220f, 48f);

            var swatch = ToyKit.UIRounded("Swatch", root, Color.white, 5f);
            swatch.anchorMin = swatch.anchorMax = new Vector2(0f, 1f);
            swatch.pivot = new Vector2(0f, 1f);
            swatch.anchoredPosition = new Vector2(0f, -2f);
            swatch.sizeDelta = new Vector2(44f, 44f);

            var label = ToyKit.UIText("Label", root, BodyFont, "", 30f, ToyKit.Ink);
            label.alignment = TextAlignmentOptions.Left;
            label.rectTransform.pivot = new Vector2(0f, 1f);
            ToyKit.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(54f, 0f), new Vector2(166f, 44f));

            summaryViews.Add(new SummaryView { swatch = swatch.GetComponent<Image>(), label = label });
        }
    }

    void RefreshDashboard()
    {
        int total = 0, unlocked = 0;
        foreach (var s in Summaries)
        {
            total += s.total;
            unlocked += s.unlocked;
        }
        float rate = total > 0 ? (float)unlocked / total : 0f;
        if (percentText != null) percentText.text = Mathf.RoundToInt(rate * 100f) + "%";
        if (countText != null) countText.text = unlocked + " / " + total + " 解除済み";
        if (progressFill != null)
        {
            // 9スライスの角丸を保ったまま、幅だけを達成率に合わせる
            var rt = progressFill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(rate, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            progressFill.enabled = rate > 0.001f;
        }

        string[] texts = { "すべて " + total, "取得済み " + unlocked, "未取得 " + (total - unlocked) };
        for (int i = 0; i < filterBgs.Length; i++)
        {
            bool on = (int)m_filter == i;
            if (filterBgs[i] != null) filterBgs[i].color = on ? AccentColor : PillColor;
            if (filterTexts[i] != null)
            {
                filterTexts[i].text = texts[i];
                filterTexts[i].color = on ? Color.white : new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.55f);
                filterTexts[i].fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            }
            if (filterUnderlines[i] != null)
            {
                filterUnderlines[i].color = on ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0f);
            }
        }

        if (sortText != null) sortText.text = SortName(m_sort) + " ▼";
        if (directionText != null) directionText.text = m_ascending ? "昇順 ↑" : "降順 ↓";

        EnsureSummaryViews();
        for (int i = 0; i < summaryViews.Count && i < Summaries.Count; i++)
        {
            var s = Summaries[i];
            if (summaryViews[i].label != null) summaryViews[i].label.text = s.unlocked + "/" + s.total;
            var swatch = summaryViews[i].swatch;
            if (swatch != null)
            {
                if (s.icon != null)
                {
                    swatch.sprite = s.icon;
                    swatch.type = Image.Type.Simple;
                    swatch.preserveAspect = true;
                    swatch.color = Color.white;
                }
                else
                {
                    swatch.color = s.color;
                }
            }
        }
    }

    static string SortName(SortMode sort)
    {
        switch (sort)
        {
            case SortMode.Rarity: return "レアリティ順";
            case SortMode.Number: return "実績番号順";
            case SortMode.Name: return "名前順";
            default: return "獲得順";
        }
    }

    void StepFilter(int delta)
    {
        int count = 3;
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
        int count = 4;
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

    // ---------------- 実績詳細 ----------------
    void BuildDetail()
    {
        detailRoot = new GameObject("DetailRoot", typeof(RectTransform)).GetComponent<RectTransform>();
        detailRoot.SetParent(card, false);
        ToyKit.Stretch(detailRoot);
        detailGroup = detailRoot.gameObject.AddComponent<CanvasGroup>();
        detailGroup.alpha = 0f;
        detailGroup.blocksRaycasts = false;
        detailGroup.interactable = false;

        var dim = ToyKit.UIImage("Dim", detailRoot, new Color(0.04f, 0.03f, 0.04f, 0.55f));
        ToyKit.Stretch(dim);

        detailPanel = ToyKit.UIRounded("Detail", detailRoot, new Color(1f, 0.98f, 0.94f, 1f), 26f);
        detailPanel.anchorMin = detailPanel.anchorMax = new Vector2(0.5f, 0.5f);
        detailPanel.anchoredPosition = Vector2.zero;
        detailPanel.sizeDelta = new Vector2(1100f, 440f);
        var panelOutline = detailPanel.gameObject.AddComponent<Outline>();
        panelOutline.effectColor = ToyKit.Ink;
        panelOutline.effectDistance = new Vector2(3f, -3f);
        detailOutline = panelOutline;

        var title = ToyKit.UIText("Title", detailPanel, font, DetailTitle, 34f, ToyKit.Ink);
        if (letterMat != null) title.fontSharedMaterial = letterMat;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Left;
        title.rectTransform.pivot = new Vector2(0f, 1f);
        ToyKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -24f), new Vector2(500f, 44f));

        // レアリティの札
        var rarityBg = ToyKit.UIRounded("Rarity", detailPanel, Color.white, 14f);
        rarityBg.anchorMin = rarityBg.anchorMax = new Vector2(1f, 1f);
        rarityBg.pivot = new Vector2(1f, 1f);
        rarityBg.anchoredPosition = new Vector2(-30f, -20f);
        rarityBg.sizeDelta = new Vector2(100f, 34f);
        detailRarityBg = rarityBg.GetComponent<Image>();
        detailRarityText = ToyKit.UIText("Text", rarityBg, BodyFont, "", 20f, Color.white);
        detailRarityText.alignment = TextAlignmentOptions.Center;
        detailRarityText.enableAutoSizing = true;
        detailRarityText.fontSizeMin = 12f;
        detailRarityText.fontSizeMax = 20f;
        ToyKit.Stretch(detailRarityText.rectTransform);

        // アイコン
        var iconBg = ToyKit.UIRounded("IconBg", detailPanel, Color.white, 22f);
        iconBg.anchorMin = iconBg.anchorMax = new Vector2(0f, 1f);
        iconBg.pivot = new Vector2(0f, 1f);
        iconBg.anchoredPosition = new Vector2(40f, -82f);
        iconBg.sizeDelta = new Vector2(140f, 140f);
        detailIconBg = iconBg.GetComponent<Image>();

        var icon = ToyKit.UIImage("Icon", iconBg, Color.white);
        ToyKit.Stretch(icon);
        icon.offsetMin = new Vector2(18f, 18f);
        icon.offsetMax = new Vector2(-18f, -18f);
        detailIcon = icon.GetComponent<Image>();
        detailIcon.type = Image.Type.Simple;
        detailIcon.preserveAspect = true;
        detailIcon.enabled = false;

        detailIconText = ToyKit.UIText("IconText", iconBg, BodyFont, "", 30f, Color.white);
        detailIconText.fontStyle = FontStyles.Bold;
        detailIconText.alignment = TextAlignmentOptions.Center;
        detailIconText.enableAutoSizing = true;
        detailIconText.fontSizeMin = 18f;
        detailIconText.fontSizeMax = 34f;
        ToyKit.Stretch(detailIconText.rectTransform);

        // 未取得のシルエットに重ねる小さな鍵
        var lockBg = ToyKit.UIRounded("LockBg", iconBg, new Color(ToyKit.Horizon.r, ToyKit.Horizon.g, ToyKit.Horizon.b, 0.95f), 20f);
        lockBg.anchorMin = lockBg.anchorMax = new Vector2(1f, 0f);
        lockBg.pivot = new Vector2(1f, 0f);
        lockBg.anchoredPosition = new Vector2(8f, 8f);
        lockBg.sizeDelta = new Vector2(54f, 54f);
        detailLockBg = lockBg.GetComponent<Image>();
        var lockIcon = ToyKit.UIImage("Lock", lockBg, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.9f));
        detailLock = lockIcon.GetComponent<Image>();
        detailLock.type = Image.Type.Simple;
        detailLock.preserveAspect = true;
        ToyKit.Stretch(lockIcon);
        lockIcon.offsetMin = new Vector2(10f, 10f);
        lockIcon.offsetMax = new Vector2(-10f, -10f);
        detailLockBg.gameObject.SetActive(false);
        detailLock.enabled = false;

        detailName = ToyKit.UIText("Name", detailPanel, BodyFont, "", 38f, ToyKit.Ink);
        detailName.fontStyle = FontStyles.Bold;
        detailName.alignment = TextAlignmentOptions.Left;
        detailName.rectTransform.pivot = new Vector2(0f, 1f);
        ToyKit.Anchor(detailName.rectTransform, new Vector2(0f, 1f), new Vector2(210f, -82f), new Vector2(800f, 52f));

        var statusBg = ToyKit.UIRounded("Status", detailPanel, GotPillColor, 14f);
        statusBg.anchorMin = statusBg.anchorMax = new Vector2(0f, 1f);
        statusBg.pivot = new Vector2(0f, 1f);
        statusBg.anchoredPosition = new Vector2(210f, -136f);
        statusBg.sizeDelta = new Vector2(130f, 30f);
        detailStatusBg = statusBg.GetComponent<Image>();
        detailStatusText = ToyKit.UIText("Text", statusBg, BodyFont, "", 20f, Color.white);
        detailStatusText.alignment = TextAlignmentOptions.Center;
        ToyKit.Stretch(detailStatusText.rectTransform);

        detailCondition = ToyKit.UIText("Condition", detailPanel, BodyFont, "", 24f, ToyKit.Ink);
        detailCondition.alignment = TextAlignmentOptions.Left;
        detailCondition.textWrappingMode = TextWrappingModes.Normal;
        detailCondition.enableAutoSizing = true;
        detailCondition.fontSizeMin = 18f;
        detailCondition.fontSizeMax = 24f;
        detailCondition.rectTransform.pivot = new Vector2(0f, 1f);
        ToyKit.Anchor(detailCondition.rectTransform, new Vector2(0f, 1f), new Vector2(210f, -170f), new Vector2(840f, 38f));

        detailPoem = ToyKit.UIText("Poem", detailPanel, BodyFont, "", 21f,
                                   new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.65f));
        detailPoem.alignment = TextAlignmentOptions.Left;
        detailPoem.textWrappingMode = TextWrappingModes.Normal;
        detailPoem.rectTransform.pivot = new Vector2(0f, 1f);
        ToyKit.Anchor(detailPoem.rectTransform, new Vector2(0f, 1f), new Vector2(210f, -210f), new Vector2(840f, 24f));

        // 複数回条件のときの進捗（あと何回で達成か）
        detailProgress = new GameObject("Progress", typeof(RectTransform)).GetComponent<RectTransform>();
        detailProgress.SetParent(detailPanel, false);
        detailProgress.anchorMin = detailProgress.anchorMax = new Vector2(0f, 1f);
        detailProgress.pivot = new Vector2(0f, 1f);
        detailProgress.anchoredPosition = new Vector2(210f, -238f);
        detailProgress.sizeDelta = new Vector2(840f, 58f);

        detailProgressLabel = ToyKit.UIText("Label", detailProgress, BodyFont, "", 20f, ToyKit.Ink);
        detailProgressLabel.alignment = TextAlignmentOptions.Left;
        detailProgressLabel.rectTransform.pivot = new Vector2(0f, 1f);
        ToyKit.Anchor(detailProgressLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(400f, 24f));
        detailProgressRemain = ToyKit.UIText("Remain", detailProgress, BodyFont, "", 20f,
                                             new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.7f));
        detailProgressRemain.alignment = TextAlignmentOptions.Right;
        detailProgressRemain.rectTransform.pivot = new Vector2(1f, 1f);
        ToyKit.Anchor(detailProgressRemain.rectTransform, new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(400f, 24f));

        var progressBg = ToyKit.UIRounded("ProgressBg", detailProgress, new Color(0.90f, 0.90f, 0.90f, 0.8f), 8f);
        progressBg.anchorMin = progressBg.anchorMax = new Vector2(0f, 1f);
        progressBg.pivot = new Vector2(0f, 1f);
        progressBg.anchoredPosition = new Vector2(0f, -30f);
        progressBg.sizeDelta = new Vector2(500f, 16f);
        var progressFill = ToyKit.UIImage("ProgressFill", progressBg, AccentColor, BarSprite != null ? BarSprite : uiSprite);
        ToyKit.Stretch(progressFill);
        detailProgressFill = progressFill.GetComponent<Image>();
        detailProgressFill.type = Image.Type.Sliced;
        detailProgressFill.pixelsPerUnitMultiplier = 1f;
        detailProgressFill.enabled = false;
        detailProgress.gameObject.SetActive(false);

        var line = ToyKit.UIImage("Line", detailPanel, DividerColor);
        line.anchorMin = line.anchorMax = new Vector2(0f, 1f);
        line.pivot = new Vector2(0f, 1f);
        line.anchoredPosition = new Vector2(40f, -300f);
        line.sizeDelta = new Vector2(1020f, 2f);

        var dateLabel = ToyKit.UIText("DateLabel", detailPanel, BodyFont, "獲得日時", 20f,
                                      new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.65f));
        dateLabel.alignment = TextAlignmentOptions.Left;
        dateLabel.rectTransform.pivot = new Vector2(0f, 1f);
        ToyKit.Anchor(dateLabel.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -322f), new Vector2(300f, 26f));
        detailDate = ToyKit.UIText("Date", detailPanel, BodyFont, "", 21f, ToyKit.Ink);
        detailDate.alignment = TextAlignmentOptions.Right;
        detailDate.rectTransform.pivot = new Vector2(1f, 1f);
        ToyKit.Anchor(detailDate.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -322f), new Vector2(600f, 26f));

        var numberLabel = ToyKit.UIText("NumberLabel", detailPanel, BodyFont, "実績番号", 20f,
                                        new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.65f));
        numberLabel.alignment = TextAlignmentOptions.Left;
        numberLabel.rectTransform.pivot = new Vector2(0f, 1f);
        ToyKit.Anchor(numberLabel.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -356f), new Vector2(300f, 26f));
        detailNumber = ToyKit.UIText("Number", detailPanel, BodyFont, "", 21f, ToyKit.Ink);
        detailNumber.alignment = TextAlignmentOptions.Right;
        detailNumber.rectTransform.pivot = new Vector2(1f, 1f);
        ToyKit.Anchor(detailNumber.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -356f), new Vector2(600f, 26f));
    }

    void OpenDetail(Entry e)
    {
        if (e == null) return;
        detailEntry = e;
        detailOpen = true;
        RefreshDetail();
        MenuSounds.Decide();
    }

    void CloseDetail()
    {
        detailOpen = false;
        detailEntry = null;
        MenuSounds.Cancel();
    }

    void UpdateDetailFade(float dt)
    {
        if (detailGroup == null) return;
        detailGroup.alpha = Mathf.MoveTowards(detailGroup.alpha, detailOpen ? 1f : 0f, dt / 0.16f);
        detailGroup.blocksRaycasts = false;
        detailGroup.interactable = false;
    }

    void RefreshDetail()
    {
        Entry e = detailEntry;
        if (e == null) return;

        bool hidden = e.secret && !e.unlocked;
        detailName.text = hidden ? texts.locked : e.name;
        detailRarityText.text = hidden ? "？？？" : e.rarityLabel;
        detailRarityBg.color = hidden ? LockedPillColor : e.displayRarityColor;
        detailRarityText.color = ContrastColor(detailRarityBg.color);

        detailStatusText.text = e.unlocked ? "取得済み" : "未取得";
        detailStatusBg.color = e.unlocked ? GotPillColor : LockedPillColor;
        detailStatusText.color = e.unlocked ? Color.white : ToyKit.Ink;
        if (detailOutline != null) detailOutline.effectColor = e.special ? e.borderColor : ToyKit.Ink;

        detailDate.text = e.unlocked && !string.IsNullOrEmpty(e.obtainedText) ? e.obtainedText : LockedDateText;
        detailNumber.text = "No." + e.number.ToString("D3");
        detailCondition.text = hidden ? SecretCondition : "条件：" + e.condition;
        detailPoem.text = hidden || !e.unlocked ? "" : e.poem;

        bool showProgress = e.hasProgress && !hidden;
        detailProgress.gameObject.SetActive(showProgress);
        if (showProgress)
        {
            int target = Mathf.Max(1, e.target);
            int progress = Mathf.Clamp(e.progress, 0, target);
            detailProgressLabel.text = "進捗 " + progress + " / " + target;
            detailProgressRemain.text = "あと" + (target - progress) + "回";
            float ratio = (float)progress / target;
            var rt = detailProgressFill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(ratio, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            detailProgressFill.enabled = ratio > 0.001f;
        }

        // アイコン
        detailIconText.gameObject.SetActive(false);
        detailIcon.sprite = null;
        detailIcon.enabled = false;
        detailIcon.rectTransform.offsetMin = new Vector2(18f, 18f);
        detailIcon.rectTransform.offsetMax = new Vector2(-18f, -18f);
        detailLockBg.gameObject.SetActive(false);
        detailLock.enabled = false;

        if (hidden)
        {
            detailIconBg.color = new Color(0.78f, 0.78f, 0.80f, 1f);
            if (e.lockSprite != null)
            {
                detailIcon.sprite = e.lockSprite;
                detailIcon.enabled = true;
                detailIcon.color = new Color(0.45f, 0.45f, 0.48f, 1f);
            }
        }
        else if (!e.unlocked)
        {
            if (e.lockedIcon != null)
            {
                detailIconBg.color = new Color(0.93f, 0.92f, 0.90f, 1f);
                detailIcon.sprite = e.lockedIcon;
                detailIcon.enabled = true;
                detailIcon.color = new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.55f);
                detailIcon.rectTransform.offsetMin = new Vector2(8f, 8f);
                detailIcon.rectTransform.offsetMax = new Vector2(-8f, -8f);
                if (e.lockSprite != null)
                {
                    detailLock.sprite = e.lockSprite;
                    detailLock.enabled = true;
                    detailLockBg.gameObject.SetActive(true);
                }
            }
            else if (e.lockSprite != null)
            {
                detailIconBg.color = Color.Lerp(e.rarityColor, Color.gray, 0.35f);
                detailIcon.sprite = e.lockSprite;
                detailIcon.enabled = true;
                detailIcon.color = new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.8f);
            }
        }
        else if (e.icon != null)
        {
            detailIconBg.color = new Color(1f, 1f, 1f, 0f);
            detailIcon.sprite = e.icon;
            detailIcon.enabled = true;
            detailIcon.color = Color.white;
            detailIcon.rectTransform.offsetMin = new Vector2(2f, 2f);
            detailIcon.rectTransform.offsetMax = new Vector2(-2f, -2f);
        }
        else
        {
            detailIconBg.color = e.displayRarityColor;
            detailIconText.gameObject.SetActive(true);
            detailIconText.text = e.rarityLabel;
            detailIconText.color = ContrastColor(e.displayRarityColor);
        }
    }

    static Color ContrastColor(Color bg) => bg.grayscale > 0.55f ? ToyKit.Ink : Color.white;

    static Color SelectedBorder(Color c) => new Color(
        Mathf.Lerp(c.r, 1f, 0.55f), Mathf.Lerp(c.g, 1f, 0.55f), Mathf.Lerp(c.b, 1f, 0.55f), 1f);

    static Color HoverBorder(Color c) => new Color(
        Mathf.Lerp(c.r, 1f, 0.30f), Mathf.Lerp(c.g, 1f, 0.30f), Mathf.Lerp(c.b, 1f, 0.30f), 1f);

    static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    // 下地が暗い側でも文字が読めるように、白い影をうっすら付ける
    // 特別実績用の内側の細い枠（4本のバー）
    void AddInnerFrame(RectTransform holder, Color color, float thickness, float inset)
    {
        var top = ToyKit.UIImage("FrameTop", holder, color, uiSprite);
        top.anchorMin = new Vector2(0f, 1f); top.anchorMax = new Vector2(1f, 1f);
        top.pivot = new Vector2(0.5f, 1f);
        top.offsetMin = new Vector2(inset, -inset - thickness);
        top.offsetMax = new Vector2(-inset, -inset);

        var bottom = ToyKit.UIImage("FrameBottom", holder, color, uiSprite);
        bottom.anchorMin = new Vector2(0f, 0f); bottom.anchorMax = new Vector2(1f, 0f);
        bottom.pivot = new Vector2(0.5f, 0f);
        bottom.offsetMin = new Vector2(inset, inset);
        bottom.offsetMax = new Vector2(-inset, inset + thickness);

        var left = ToyKit.UIImage("FrameLeft", holder, color, uiSprite);
        left.anchorMin = new Vector2(0f, 0f); left.anchorMax = new Vector2(0f, 1f);
        left.pivot = new Vector2(0f, 0.5f);
        left.offsetMin = new Vector2(inset, inset);
        left.offsetMax = new Vector2(inset + thickness, -inset);

        var right = ToyKit.UIImage("FrameRight", holder, color, uiSprite);
        right.anchorMin = new Vector2(1f, 0f); right.anchorMax = new Vector2(1f, 1f);
        right.pivot = new Vector2(1f, 0.5f);
        right.offsetMin = new Vector2(-inset - thickness, inset);
        right.offsetMax = new Vector2(-inset, -inset);
    }

    static void AddTextShadow(TMP_Text text)
    {
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(1f, 1f, 1f, 0.38f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        shadow.useGraphicAlpha = true;
    }

    // 暗いレアリティ帯でも本文が沈まないよう、細いクリーム色の縁取りを足す
    static void AddTextOutline(TMP_Text text)
    {
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.97f, 0.92f, 0.55f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.useGraphicAlpha = true;
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
        var bar = ToyKit.UIImage("HeaderBar", root, AccentColor, uiSprite);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 1f);
        bar.anchoredPosition = new Vector2(-30f, -30f);
        bar.sizeDelta = new Vector2(560f, 110f);
        var ht = ToyKit.UIText("Header", bar, font, texts.title, 66f, Color.white);
        if (letterMat != null) ht.fontSharedMaterial = letterMat;
        ht.alignment = TextAlignmentOptions.Left;
        ToyKit.Anchor(ht.rectTransform, new Vector2(0f, 0.5f), new Vector2(300f, 0f), new Vector2(500f, 110f));

        // 実績を並べるカード
        card = ToyKit.UIImage("Card", root, new Color(1f, 0.98f, 0.94f, 0.96f), uiSprite);
        card.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        ToyKit.Anchor(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW, CardH));
        var ol = card.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3f, -3f);

        BuildDashboard();

        listView = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
        listView.SetParent(card, false);
        ToyKit.Stretch(listView);
        listView.offsetMin = new Vector2(30f, 30f);
        listView.offsetMax = new Vector2(-30f, -ListTop);
        listView.gameObject.AddComponent<RectMask2D>();

        listContent = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        listContent.SetParent(listView, false);
        listContent.anchorMin = new Vector2(0f, 1f);
        listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.anchoredPosition = Vector2.zero;

        // 右端のスクロール位置表示（一覧の外側のカード余白に置く）
        var track = ToyKit.UIRounded("ScrollTrack", card, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.16f), 2f);
        track.anchorMin = track.anchorMax = new Vector2(1f, 0.5f);
        track.pivot = new Vector2(1f, 0.5f);
        track.anchoredPosition = new Vector2(-14f, -90f);
        track.sizeDelta = new Vector2(4f, CardH - ListTop - 30f);
        scrollTrack = track;

        var thumb = ToyKit.UIRounded("Thumb", track, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.55f), 2f);
        thumb.anchorMin = thumb.anchorMax = new Vector2(0.5f, 1f);
        thumb.pivot = new Vector2(0.5f, 1f);
        thumb.sizeDelta = new Vector2(4f, 80f);
        thumb.anchoredPosition = Vector2.zero;
        scrollThumb = thumb;

        BuildDetail();
    }

    // 表示中の実績（フィルター・並び替え後）から行を作り直す
    void BuildRows()
    {
        foreach (Transform c in listContent) Destroy(c.gameObject);
        rows.Clear();
        listContent.anchoredPosition = Vector2.zero;

        if (m_view.Count == 0)
        {
            listContent.sizeDelta = new Vector2(0f, 120f);
            var empty = ToyKit.UIText("Empty", listContent, BodyFont, EmptyText, 28f,
                                      new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.55f));
            empty.alignment = TextAlignmentOptions.Center;
            ToyKit.Anchor(empty.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(1000f, 60f));
            return;
        }

        listContent.sizeDelta = new Vector2(0f, m_view.Count * (RowH + RowGap));

        for (int i = 0; i < m_view.Count; i++)
        {
            Entry e = m_view[i];
            bool selected = i == index;
            bool hidden = e.secret && !e.unlocked;

            var holder = new GameObject("Row" + i, typeof(RectTransform)).GetComponent<RectTransform>();
            holder.SetParent(listContent, false);
            holder.anchorMin = new Vector2(0f, 1f); holder.anchorMax = new Vector2(1f, 1f);
            holder.pivot = new Vector2(0.5f, 1f);
            holder.sizeDelta = new Vector2(0f, RowH);
            holder.anchoredPosition = new Vector2(0f, -i * (RowH + RowGap));

            var row = new Row { holder = holder, borderBase = e.borderColor };

            // 外側の光彩（カードの外側だけに薄く出す。下地の色は変えない）
            float initialGlow = selected ? 0.45f : 0f;
            if (e.glowSprite != null)
            {
                var glow = ToyKit.UIImage("Glow", holder, WithAlpha(e.glowColor, initialGlow), e.glowSprite);
                var glowImage = glow.GetComponent<Image>();
                glowImage.type = Image.Type.Simple;
                ToyKit.Stretch(glow);
                glow.offsetMin = new Vector2(-8f, -7f);
                glow.offsetMax = new Vector2(8f, 7f);
                row.glow = glowImage;
            }

            // 外側の細い暗い縁。カードの下地と明るい外枠を分ける
            var edge = ToyKit.UIImage("Edge", holder, e.edgeColor, uiSprite);
            ToyKit.Stretch(edge);
            edge.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;

            // 明るい外枠。選択中は太く・明るくする
            var border = ToyKit.UIImage("Border", holder, e.borderColor, uiSprite);
            ToyKit.Stretch(border);
            border.offsetMin = new Vector2(EdgeInset, EdgeInset);
            border.offsetMax = new Vector2(-EdgeInset, -EdgeInset);
            border.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
            row.border = border.GetComponent<Image>();
            row.border.color = selected ? SelectedBorder(e.borderColor) : e.borderColor;

            // レアリティのグラデーション。角丸のマスクで切り抜く
            var bg = ToyKit.UIImage("Bg", holder, Color.white, uiSprite);
            ToyKit.Stretch(bg);
            bg.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
            var mask = bg.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            float borderWidth = selected ? BorderSelected : BorderNormal;
            float inset = EdgeInset + borderWidth;
            bg.offsetMin = new Vector2(inset, inset);
            bg.offsetMax = new Vector2(-inset, -inset);
            row.bg = bg;

            var fill = ToyKit.UIImage("Fill", bg, e.background != null ? Color.white : e.rarityColor, e.background);
            ToyKit.Stretch(fill);
            if (e.background != null) fill.GetComponent<Image>().type = Image.Type.Simple;

            // 斜めライティング。カードの角丸マスクの中だけを白い光の帯が横切る
            if (e.shineSprite != null)
            {
                var shine = ToyKit.UIImage("Shine", bg, WithAlpha(Color.white, 0f), e.shineSprite);
                var shineImage = shine.GetComponent<Image>();
                shineImage.type = Image.Type.Simple;
                shine.anchorMin = shine.anchorMax = new Vector2(0.5f, 0.5f);
                shine.pivot = new Vector2(0.5f, 0.5f);
                shine.sizeDelta = new Vector2(ShineWidth, ShineHeight);
                shine.anchoredPosition = new Vector2(-2000f, 0f);
                shine.localEulerAngles = new Vector3(0f, 0f, ShineAngle);
                row.shine = shineImage;
            }

            // 特別は専用の二重枠
            if (e.special)
            {
                AddInnerFrame(holder, e.borderColor, 2f, 9f);
            }

            // キーボード・コントローラー操作のフォーカス表示（選択中の行だけ左に太い線）
            var focus = ToyKit.UIImage("Focus", holder, ToyKit.Ink, uiSprite);
            focus.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
            focus.anchorMin = focus.anchorMax = new Vector2(0f, 0.5f);
            focus.pivot = new Vector2(0f, 0.5f);
            focus.sizeDelta = new Vector2(6f, RowH - 16f);
            focus.anchoredPosition = new Vector2(10f, 0f);
            var focusOutline = focus.gameObject.AddComponent<Outline>();
            focusOutline.effectColor = new Color(1f, 1f, 1f, 0.55f);
            focusOutline.effectDistance = new Vector2(2f, -2f);
            focusOutline.useGraphicAlpha = true;
            row.focus = focus.GetComponent<Image>();
            row.focus.color = new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b,
                                        selected && !pointerMode ? 1f : 0f);

            // 右端のアイコン。解除済みはレアリティ／特別の専用アイコン、
            // 未取得は同じ形のシルエット＋鍵、シークレットは鍵だけ。
            bool showSilhouette = !e.unlocked && !hidden && e.lockedIcon != null;
            Sprite sideSprite = e.unlocked ? e.icon
                : showSilhouette ? e.lockedIcon
                : e.lockSprite;
            Color sideColor = e.unlocked ? Color.white
                : showSilhouette ? new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.55f)
                : new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.72f);
            if (sideSprite != null)
            {
                var icon = ToyKit.UIImage("Icon", holder, sideColor, sideSprite);
                var iconImage = icon.GetComponent<Image>();
                iconImage.type = Image.Type.Simple;
                iconImage.preserveAspect = true;
                icon.anchorMin = icon.anchorMax = new Vector2(1f, 0.5f);
                icon.pivot = new Vector2(1f, 0.5f);
                icon.sizeDelta = new Vector2(60f, 60f);
                icon.anchoredPosition = new Vector2(-10f, 0f);
            }
            if (showSilhouette && e.lockSprite != null)
            {
                // シルエットの右下に小さな鍵を重ねる
                var lockBg = ToyKit.UIRounded("LockBg", holder, new Color(ToyKit.Horizon.r, ToyKit.Horizon.g, ToyKit.Horizon.b, 0.95f), 12f);
                lockBg.anchorMin = lockBg.anchorMax = new Vector2(1f, 0.5f);
                lockBg.pivot = new Vector2(1f, 0.5f);
                lockBg.sizeDelta = new Vector2(26f, 26f);
                lockBg.anchoredPosition = new Vector2(-8f, -20f);
                var lockRect = ToyKit.UIImage("Lock", lockBg, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.9f), e.lockSprite);
                var lockImage = lockRect.GetComponent<Image>();
                lockImage.type = Image.Type.Simple;
                lockImage.preserveAspect = true;
                ToyKit.Stretch(lockRect);
                lockRect.offsetMin = new Vector2(5f, 5f);
                lockRect.offsetMax = new Vector2(-5f, -5f);
            }

            // 文字。未解除（シークレット以外）も名前と条件を出す
            string nameText = hidden ? texts.locked : (e.special ? "【特別】" + e.name : e.name);
            Color nameColor = e.unlocked ? ToyKit.Ink
                : hidden ? new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.8f)
                         : new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.9f);
            var name = ToyKit.UIText("Name", holder, BodyFont, nameText, 38f, nameColor);
            name.fontStyle = FontStyles.Bold;
            name.alignment = TextAlignmentOptions.Left;
            ToyKit.Anchor(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(30f + 250f, 0f), new Vector2(500f, RowH - 10f));
            AddTextShadow(name);
            AddTextOutline(name);

            string descText = hidden ? SecretText : e.description;
            Color descColor = new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, e.unlocked ? 1f : 0.9f);
            var desc = ToyKit.UIText("Desc", holder, BodyFont, descText, 28f, descColor);
            desc.alignment = TextAlignmentOptions.Left;
            desc.enableAutoSizing = true; desc.fontSizeMin = 24f; desc.fontSizeMax = 28f;
            ToyKit.Anchor(desc.rectTransform, new Vector2(0f, 0.5f), new Vector2(560f + 500f, 0f), new Vector2(1000f, RowH - 10f));
            AddTextShadow(desc);
            AddTextOutline(desc);

            rows.Add(row);
        }
    }
}
