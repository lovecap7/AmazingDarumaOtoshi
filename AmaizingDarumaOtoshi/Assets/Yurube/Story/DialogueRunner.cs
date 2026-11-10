using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Yurube.Story
{
    // 会話ウィンドウを出す係（表示だけ）。台本は StoryChapter（JSON）から受け取る。
    //  ・大きいウィンドウ：物語の会話。背景を暗くし、外側で試合をポーズする
    //  ・小さいウィンドウ：対戦中の説明。ポーズせず、数秒で自動的に閉じる
    //  ・A／Enter／クリック：表示中なら全文、表示済みなら次へ　B／Esc：残りを全部スキップ
    //  ・ト書き（direction）は小さめの字で出す。eventId があれば、その行で演出を起こす
    // 操作説明は画面右下に「A：つぎへ　B：スキップ」を出す（ほかの画面と同じ場所）。
    public class DialogueRunner : MonoBehaviour
    {
        const string HintText = "A：つぎへ　B：スキップ";

        // 文字送りの速さ（1秒あたりの文字数）。インスペクターで変えられる
        [SerializeField, Min(1f)] float m_charsPerSecond = 34f;
        // 小さいウィンドウを自動で閉じるまでの秒数
        [SerializeField, Min(0.5f)] float m_smallSeconds = 4.5f;
        // 背景を暗くする濃さ（大きいウィンドウのとき）
        [SerializeField, Range(0f, 0.9f)] float m_dimAlpha = 0.62f;

        static readonly Color PanelColor = new Color(1f, 0.98f, 0.94f, 0.97f);
        static readonly Color Ink = ToyKit.Ink;

        TMP_FontAsset m_font;
        RectTransform m_root, m_bigPanel, m_smallPanel, m_newsPanel, m_objectivePanel, m_koPanel;
        Image m_dim, m_portrait, m_koDim, m_portraitFace;
        TextMeshProUGUI m_bigName, m_bigText, m_smallName, m_smallText, m_hint;
        TextMeshProUGUI m_newsHeader, m_newsHeadline, m_newsBody;
        TextMeshProUGUI m_objectiveName, m_objectiveText, m_koMain, m_koSub;
        Image m_newsImage;
        CanvasGroup m_bigGroup, m_smallGroup, m_newsGroup, m_objectiveGroup, m_koGroup, m_hintGroup;
        bool m_skipped;
        bool m_smallShowing;

        public bool IsShowing { get; private set; }

        //========================================
        // 作る
        //========================================

        public static DialogueRunner Create()
        {
            var go = new GameObject("StoryDialogue");
            var runner = go.AddComponent<DialogueRunner>();
            runner.Build();
            return runner;
        }

        void Build()
        {
            if (m_root != null) return;
            m_font = PresentationSettings.DefaultFont;

            // 調整値（StorySettings があればそちらを使う）
            var settings = StorySettings.Instance;
            if (settings != null)
            {
                m_charsPerSecond = settings.CharsPerSecond;
                m_smallSeconds = settings.SmallSeconds;
                m_dimAlpha = settings.DimAlpha;
            }

            var canvas = ToyKit.CreateOverlayCanvas("StoryUI", 60);
            m_root = canvas;

            // 背景を暗くする膜（大きいウィンドウのときだけ）
            var dim = ToyKit.UIImage("Dim", m_root, new Color(0.04f, 0.02f, 0.01f, 0f));
            ToyKit.Stretch(dim);
            m_dim = dim.GetComponent<Image>();

            BuildBig();
            BuildSmall();
            BuildNews();
            BuildObjective();
            BuildKo();

            // 右下の操作説明（暗い帯を敷いて、明るい会話ウィンドウの上でも読めるようにする）
            var hintPlate = ToyKit.UIRounded("HintPlate", m_root, new Color(0.05f, 0.04f, 0.03f, 0.78f), 16f);
            hintPlate.pivot = new Vector2(1f, 0f);
            ToyKit.Anchor(hintPlate, new Vector2(1f, 0f), new Vector2(-32f, 30f), new Vector2(560f, 56f));
            hintPlate.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.18f);
            m_hintGroup = hintPlate.gameObject.AddComponent<CanvasGroup>();
            m_hint = ToyKit.UIText("Hint", hintPlate, m_font, HintText, 30f, Color.white);
            ToyKit.Stretch(m_hint.rectTransform);
            m_hint.rectTransform.offsetMin = new Vector2(14f, 0f);
            m_hint.rectTransform.offsetMax = new Vector2(-14f, 0f);
            m_hint.alignment = TextAlignmentOptions.Right;
            m_hintGroup.alpha = 0f;
            hintPlate.gameObject.SetActive(false);
        }

        // 対戦中に「いま何をすればいいか」を上に出す帯（操作のあいだは消さずに残す）
        void BuildObjective()
        {
            var plate = ToyKit.UIRounded("Objective", m_root, new Color(0.07f, 0.05f, 0.04f, 0.92f), 24f);
            plate.pivot = new Vector2(0.5f, 1f);
            ToyKit.Anchor(plate, new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(1180f, 88f));
            plate.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 0.82f, 0.25f, 0.9f);
            m_objectivePanel = plate;
            m_objectiveGroup = plate.gameObject.AddComponent<CanvasGroup>();

            var tag = ToyKit.UIRounded("Tag", plate, ToyKit.Palette[3], 14f);
            ToyKit.Anchor(tag, new Vector2(0f, 0.5f), new Vector2(90f, 0f), new Vector2(150f, 46f));
            m_objectiveName = ToyKit.UIText("Name", tag, m_font, "", 26f, Color.white);
            m_objectiveName.fontStyle = FontStyles.Bold;
            m_objectiveName.enableAutoSizing = true;
            m_objectiveName.fontSizeMin = 14f;
            m_objectiveName.fontSizeMax = 26f;
            ToyKit.Stretch(m_objectiveName.rectTransform);

            m_objectiveText = ToyKit.UIText("Text", plate, m_font, "", 32f, Color.white);
            m_objectiveText.alignment = TextAlignmentOptions.Left;
            m_objectiveText.textWrappingMode = TextWrappingModes.Normal;
            m_objectiveText.enableAutoSizing = true;
            m_objectiveText.fontSizeMin = 20f;
            m_objectiveText.fontSizeMax = 32f;
            var textRect = m_objectiveText.rectTransform;
            textRect.anchorMin = textRect.anchorMax = new Vector2(0f, 0.5f);
            textRect.pivot = new Vector2(0f, 0.5f);
            textRect.anchoredPosition = new Vector2(185f, 0f);
            textRect.sizeDelta = new Vector2(960f, 70f);

            m_objectiveGroup.alpha = 0f;
            plate.gameObject.SetActive(false);
        }

        // やられたときの赤い閃光と大きな文字（ストックが残っているときだけ）
        void BuildKo()
        {
            var dim = ToyKit.UIImage("KoDim", m_root, new Color(0.55f, 0.04f, 0.03f, 0f));
            ToyKit.Stretch(dim);
            m_koDim = dim.GetComponent<Image>();

            m_koPanel = new GameObject("KoText", typeof(RectTransform)).GetComponent<RectTransform>();
            m_koPanel.SetParent(m_root, false);
            ToyKit.Anchor(m_koPanel, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1400f, 320f));
            m_koGroup = m_koPanel.gameObject.AddComponent<CanvasGroup>();

            m_koMain = ToyKit.UIText("Main", m_koPanel, m_font, "やられた！", 130f, Color.white);
            m_koMain.fontStyle = FontStyles.Bold;
            m_koMain.outlineWidth = 0.22f;
            m_koMain.outlineColor = new Color32(20, 8, 6, 255);
            ToyKit.Anchor(m_koMain.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1400f, 190f));

            m_koSub = ToyKit.UIText("Sub", m_koPanel, m_font, "", 40f, new Color(1f, 0.92f, 0.7f));
            m_koSub.outlineWidth = 0.15f;
            m_koSub.outlineColor = new Color32(20, 8, 6, 255);
            ToyKit.Anchor(m_koSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(1400f, 60f));

            m_koGroup.alpha = 0f;
            m_koPanel.gameObject.SetActive(false);
        }

        void BuildBig()
        {
            var panel = ToyKit.UIRounded("Big", m_root, PanelColor, 30f);
            // 画面下の HUD（パネル + 必殺技ゲージ）より上に置く
            panel.pivot = new Vector2(0.5f, 0f);
            ToyKit.Anchor(panel, new Vector2(0.5f, 0f), new Vector2(0f, 280f), new Vector2(1460f, 340f));
            panel.GetComponent<Image>().pixelsPerUnitMultiplier = 0.5f;
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = Ink;
            outline.effectDistance = new Vector2(3f, -3f);
            m_bigPanel = panel;
            m_bigGroup = panel.gameObject.AddComponent<CanvasGroup>();

            // 顔アイコン（色付きの丸。立ち絵が用意できたら差し替える）
            var portrait = ToyKit.UIRounded("Portrait", panel, Color.white, 999f);
            ToyKit.Anchor(portrait, new Vector2(0f, 0.5f), new Vector2(140f, 20f), new Vector2(210f, 210f));
            var portraitOutline = portrait.gameObject.AddComponent<Outline>();
            portraitOutline.effectColor = Ink;
            portraitOutline.effectDistance = new Vector2(3f, -3f);
            m_portrait = portrait.GetComponent<Image>();

            // 顔の立ち絵（あれば丸く切り抜いて重ねる）
            portrait.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var face = ToyKit.UIImage("Face", portrait, Color.white);
            ToyKit.Stretch(face);
            face.offsetMin = new Vector2(6f, 6f);
            face.offsetMax = new Vector2(-6f, -6f);
            m_portraitFace = face.GetComponent<Image>();
            m_portraitFace.preserveAspect = true;
            m_portraitFace.enabled = false;

            // 名前の札
            var plate = ToyKit.UIRounded("Plate", panel, ToyKit.Palette[1], 16f);
            ToyKit.Anchor(plate, new Vector2(0f, 1f), new Vector2(300f, -46f), new Vector2(340f, 62f));
            m_bigName = ToyKit.UIText("Name", plate, m_font, "", 36f, Color.white);
            m_bigName.fontStyle = FontStyles.Bold;
            m_bigName.enableAutoSizing = true;
            m_bigName.fontSizeMin = 18f;
            m_bigName.fontSizeMax = 36f;
            m_bigName.textWrappingMode = TextWrappingModes.NoWrap;
            ToyKit.Stretch(m_bigName.rectTransform);

            // 本文
            m_bigText = ToyKit.UIText("Text", panel, m_font, "", 38f, Ink);
            m_bigText.alignment = TextAlignmentOptions.TopLeft;
            m_bigText.textWrappingMode = TextWrappingModes.Normal;
            m_bigText.enableAutoSizing = true;
            m_bigText.fontSizeMin = 22f;
            m_bigText.fontSizeMax = 38f;
            var textRect = m_bigText.rectTransform;
            textRect.anchorMin = textRect.anchorMax = new Vector2(0f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = new Vector2(300f, -100f);
            textRect.sizeDelta = new Vector2(1120f, 190f);

            m_bigGroup.alpha = 0f;
            panel.gameObject.SetActive(false);
        }

        void BuildSmall()
        {
            var panel = ToyKit.UIRounded("Small", m_root, PanelColor, 22f);
            // 画面下の HUD（パネル + 必殺技ゲージ）より上に置く
            panel.pivot = new Vector2(0.5f, 0f);
            ToyKit.Anchor(panel, new Vector2(0.5f, 0f), new Vector2(0f, 290f), new Vector2(1340f, 160f));
            panel.GetComponent<Image>().pixelsPerUnitMultiplier = 0.6f;
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = Ink;
            outline.effectDistance = new Vector2(2f, -2f);
            m_smallPanel = panel;
            m_smallGroup = panel.gameObject.AddComponent<CanvasGroup>();

            var plate = ToyKit.UIRounded("Plate", panel, ToyKit.Palette[3], 14f);
            ToyKit.Anchor(plate, new Vector2(0f, 1f), new Vector2(150f, -26f), new Vector2(260f, 44f));
            m_smallName = ToyKit.UIText("Name", plate, m_font, "", 26f, Color.white);
            m_smallName.fontStyle = FontStyles.Bold;
            m_smallName.enableAutoSizing = true;
            m_smallName.fontSizeMin = 14f;
            m_smallName.fontSizeMax = 26f;
            ToyKit.Stretch(m_smallName.rectTransform);

            m_smallText = ToyKit.UIText("Text", panel, m_font, "", 30f, Ink);
            m_smallText.alignment = TextAlignmentOptions.TopLeft;
            m_smallText.textWrappingMode = TextWrappingModes.Normal;
            m_smallText.enableAutoSizing = true;
            m_smallText.fontSizeMin = 18f;
            m_smallText.fontSizeMax = 30f;
            var textRect = m_smallText.rectTransform;
            textRect.anchorMin = textRect.anchorMax = new Vector2(0f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = new Vector2(40f, -70f);
            textRect.sizeDelta = new Vector2(1260f, 86f);

            m_smallGroup.alpha = 0f;
            panel.gameObject.SetActive(false);
        }

        void BuildNews()
        {
            var panel = ToyKit.UIRounded("News", m_root, new Color(0.99f, 0.97f, 0.92f, 1f), 14f);
            ToyKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300f, 820f));
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = Ink;
            outline.effectDistance = new Vector2(4f, -4f);
            m_newsPanel = panel;
            m_newsGroup = panel.gameObject.AddComponent<CanvasGroup>();

            m_newsHeader = ToyKit.UIText("Header", panel, m_font, "だるま新聞", 30f, Ink);
            m_newsHeader.alignment = TextAlignmentOptions.Left;
            m_newsHeader.fontStyle = FontStyles.Bold;
            ToyKit.Anchor(m_newsHeader.rectTransform, new Vector2(0f, 1f), new Vector2(60f, -40f), new Vector2(500f, 44f));
            m_newsHeader.rectTransform.pivot = new Vector2(0f, 1f);

            var line = ToyKit.UIImage("Line", panel, Ink);
            ToyKit.Anchor(line, new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(1180f, 3f));

            m_newsHeadline = ToyKit.UIText("Headline", panel, m_font, "", 54f, Ink);
            m_newsHeadline.alignment = TextAlignmentOptions.Left;
            m_newsHeadline.fontStyle = FontStyles.Bold;
            m_newsHeadline.textWrappingMode = TextWrappingModes.Normal;
            m_newsHeadline.enableAutoSizing = true;
            m_newsHeadline.fontSizeMin = 24f;
            m_newsHeadline.fontSizeMax = 54f;
            var headRect = m_newsHeadline.rectTransform;
            headRect.anchorMin = headRect.anchorMax = new Vector2(0f, 1f);
            headRect.pivot = new Vector2(0f, 1f);
            headRect.anchoredPosition = new Vector2(60f, -120f);
            headRect.sizeDelta = new Vector2(1180f, 130f);

            // 1枚絵（空なら枠だけ）
            var frame = ToyKit.UIRounded("Image", panel, new Color(0.88f, 0.84f, 0.78f, 1f), 10f);
            ToyKit.Anchor(frame, new Vector2(0.5f, 1f), new Vector2(0f, -430f), new Vector2(1180f, 320f));
            m_newsImage = frame.GetComponent<Image>();

            m_newsBody = ToyKit.UIText("Body", panel, m_font, "", 30f, Ink);
            m_newsBody.alignment = TextAlignmentOptions.TopLeft;
            m_newsBody.textWrappingMode = TextWrappingModes.Normal;
            m_newsBody.enableAutoSizing = true;
            m_newsBody.fontSizeMin = 18f;
            m_newsBody.fontSizeMax = 30f;
            var bodyRect = m_newsBody.rectTransform;
            bodyRect.anchorMin = bodyRect.anchorMax = new Vector2(0f, 1f);
            bodyRect.pivot = new Vector2(0f, 1f);
            bodyRect.anchoredPosition = new Vector2(60f, -610f);
            bodyRect.sizeDelta = new Vector2(1180f, 100f);

            m_newsGroup.alpha = 0f;
            panel.gameObject.SetActive(false);
        }

        //========================================
        // 大きいウィンドウ（物語の会話）
        //========================================

        public IEnumerator PlayBig(StoryChapter chapter, IList<StoryLine> lines)
        {
            if (lines == null || lines.Count == 0) yield break;
            Build();
            while (m_smallShowing) yield return null;   // 小窓と重ねない
            IsShowing = true;
            m_skipped = false;
            HideObjective();

            // OnGUI の必殺技ゲージは会話より手前に描かれてしまうので、会話のあいだは描かせない
            bool gaugeWasSuppressed = SpecialGaugeHUD.Suppressed;
            SpecialGaugeHUD.Suppressed = true;

            m_bigPanel.gameObject.SetActive(true);
            m_smallPanel.gameObject.SetActive(false);
            m_hintGroup.gameObject.SetActive(true);
            m_hintGroup.alpha = 1f;
            yield return Fade(m_dim, 0f, m_dimAlpha, 0.2f);
            yield return FadeGroup(m_bigGroup, 0f, 1f, 0.15f);

            foreach (var line in lines)
            {
                if (line == null) continue;
                yield return ShowLine(chapter, line, big: true);
                if (m_skipped) break;
            }

            m_hintGroup.alpha = 0f;
            m_hintGroup.gameObject.SetActive(false);
            yield return FadeGroup(m_bigGroup, 1f, 0f, 0.15f);
            yield return Fade(m_dim, m_dim.color.a, 0f, 0.2f);
            m_bigPanel.gameObject.SetActive(false);
            IsShowing = false;
            SpecialGaugeHUD.Suppressed = gaugeWasSuppressed;
        }

        // 背景を暗くする濃さを変える（ムービーを背景にするときは薄くする）
        public void SetDimAlpha(float alpha)
        {
            m_dimAlpha = Mathf.Clamp01(alpha);
        }

        // 対戦中の「いま何をすればいいか」を上に出す（操作のあいだ残す）
        public void ShowObjective(StoryChapter chapter, string speakerId, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Build();
            m_objectiveName.text = StoryData.SpeakerName(chapter, speakerId);
            var tag = m_objectiveName.transform.parent.GetComponent<Image>();
            tag.color = StoryData.SpeakerColor(chapter, speakerId, ToyKit.Palette[3]);
            tag.gameObject.SetActive(!string.IsNullOrEmpty(speakerId));
            m_objectiveText.text = text;
            m_objectivePanel.gameObject.SetActive(true);
            m_objectiveGroup.alpha = 1f;
        }

        public void HideObjective()
        {
            if (m_objectivePanel == null) return;
            m_objectiveGroup.alpha = 0f;
            m_objectivePanel.gameObject.SetActive(false);
        }

        // やられたときの赤い閃光（ストックが残っているとき）
        public IEnumerator PlayKoFlash(string main, string sub)
        {
            Build();
            bool gaugeWasSuppressed = SpecialGaugeHUD.Suppressed;
            SpecialGaugeHUD.Suppressed = true;

            m_koMain.text = main;
            m_koSub.text = sub;
            m_koPanel.gameObject.SetActive(true);
            m_koGroup.alpha = 1f;

            float t = 0f;
            while (t < 0.18f)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / 0.18f);
                m_koDim.color = new Color(0.55f, 0.04f, 0.03f, 0.45f * k);
                m_koMain.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.2f, 1f, ToyKit.EaseOutBack(k));
                yield return null;
            }
            m_koDim.color = new Color(0.55f, 0.04f, 0.03f, 0.45f);
            m_koMain.rectTransform.localScale = Vector3.one;

            yield return new WaitForSecondsRealtime(0.85f);

            t = 0f;
            while (t < 0.3f)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / 0.3f);
                m_koDim.color = new Color(0.55f, 0.04f, 0.03f, Mathf.Lerp(0.45f, 0f, k));
                m_koGroup.alpha = 1f - k;
                yield return null;
            }
            m_koGroup.alpha = 0f;
            m_koDim.color = new Color(0.55f, 0.04f, 0.03f, 0f);
            m_koPanel.gameObject.SetActive(false);
            SpecialGaugeHUD.Suppressed = gaugeWasSuppressed;
        }

        // 敗北時の1行（大きいウィンドウを使うが、すぐ閉じる）
        public IEnumerator PlayOneLine(StoryChapter chapter, IList<StoryLine> lines)
        {
            yield return PlayBig(chapter, lines);
        }

        //========================================
        // 小さいウィンドウ（対戦中の説明）
        //========================================

        // 小さいウィンドウ（対戦中の説明）。対戦の操作ボタン（Aジャンプ・Bショットなど）で
        // 消えないように、入力では閉じず、文字数に応じた時間で自動的に閉じる。
        public IEnumerator PlaySmall(string speakerId, string text, StoryChapter chapter)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            Build();
            while (m_smallShowing) yield return null;   // 前の小窓が閉じるまで待つ（重ねない）
            m_smallShowing = true;
            bool wasShowing = IsShowing;
            IsShowing = true;

            ApplySpeaker(chapter, speakerId, big: false);
            m_smallText.text = text;
            m_smallPanel.gameObject.SetActive(true);
            yield return FadeGroup(m_smallGroup, m_smallGroup.alpha, 1f, 0.12f);

            // 読み終わるくらいの時間だけ出す（短い台詞でも最低 m_smallSeconds）
            float seconds = Mathf.Clamp(m_smallSeconds + text.Length * 0.05f, m_smallSeconds, 9f);
            float time = 0f;
            while (time < seconds)
            {
                time += Time.unscaledDeltaTime;
                yield return null;
            }

            yield return FadeGroup(m_smallGroup, 1f, 0f, 0.2f);
            m_smallPanel.gameObject.SetActive(false);
            IsShowing = wasShowing;
            m_smallShowing = false;
        }

        //========================================
        // だるま新聞
        //========================================

        public IEnumerator PlayNewspaper(StoryNewspaper paper)
        {
            if (paper == null || string.IsNullOrEmpty(paper.headline)) yield break;
            Build();
            IsShowing = true;

            m_newsHeader.text = string.IsNullOrEmpty(paper.header) ? "だるま新聞" : paper.header;
            m_newsHeadline.text = paper.headline;
            m_newsBody.text = paper.body;

            Sprite art = string.IsNullOrEmpty(paper.image) ? null : Resources.Load<Sprite>(paper.image);
            m_newsImage.sprite = art;
            m_newsImage.color = art != null ? Color.white : new Color(0.88f, 0.84f, 0.78f, 1f);
            m_newsImage.type = art != null ? Image.Type.Simple : Image.Type.Sliced;
            m_newsImage.preserveAspect = art != null;

            m_newsPanel.gameObject.SetActive(true);
            m_hintGroup.gameObject.SetActive(true);
            m_hintGroup.alpha = 1f;
            bool gaugeWasSuppressed = SpecialGaugeHUD.Suppressed;
            SpecialGaugeHUD.Suppressed = true;
            yield return Fade(m_dim, m_dim.color.a, 0.55f, 0.2f);
            yield return FadeGroup(m_newsGroup, 0f, 1f, 0.2f);

            // 決定で閉じる（B でも閉じる）
            while (true)
            {
                if (MenuInput.Submit() || MenuInput.AnyClick() || MenuInput.Cancel()) break;
                yield return null;
            }

            m_hintGroup.alpha = 0f;
            m_hintGroup.gameObject.SetActive(false);
            yield return FadeGroup(m_newsGroup, 1f, 0f, 0.2f);
            m_newsPanel.gameObject.SetActive(false);
            m_dim.color = new Color(m_dim.color.r, m_dim.color.g, m_dim.color.b, 0f);
            IsShowing = false;
            SpecialGaugeHUD.Suppressed = gaugeWasSuppressed;
        }

        //========================================
        // 1行の表示（文字送り）
        //========================================

        IEnumerator ShowLine(StoryChapter chapter, StoryLine line, bool big)
        {
            bool direction = line.direction || string.IsNullOrEmpty(line.speaker);
            ApplySpeaker(chapter, line.speaker, big);

            var text = big ? m_bigText : m_smallText;
            text.text = line.text;
            // ト書きは小さめ・薄めにして、台詞と区別する
            text.fontSize = direction ? (big ? 28f : 24f) : (big ? 38f : 30f);
            text.color = direction ? new Color(Ink.r, Ink.g, Ink.b, 0.75f) : Ink;
            text.maxVisibleCharacters = 0;
            text.ForceMeshUpdate();

            // 演出イベント（行を出した瞬間）
            if (!string.IsNullOrEmpty(line.eventId)) StoryData.PlayEvent(line.eventId);

            int total = text.textInfo.characterCount;
            float shown = 0f;
            while (shown < total)
            {
                if (MenuInput.Cancel()) { m_skipped = true; text.maxVisibleCharacters = total; yield break; }
                if (MenuInput.Submit() || MenuInput.AnyClick()) break;   // 全文表示
                shown += m_charsPerSecond * Time.unscaledDeltaTime;
                text.maxVisibleCharacters = Mathf.Clamp(Mathf.FloorToInt(shown), 0, total);
                yield return null;
            }
            text.maxVisibleCharacters = total;

            // 表示し終わったら次へ（入力待ち）
            if (big)
            {
                while (true)
                {
                    if (MenuInput.Cancel()) { m_skipped = true; yield break; }
                    if (MenuInput.Submit() || MenuInput.AnyClick()) break;
                    yield return null;
                }
            }
        }

        void ApplySpeaker(StoryChapter chapter, string speakerId, bool big)
        {
            bool direction = string.IsNullOrEmpty(speakerId);
            string name = direction ? "" : StoryData.SpeakerName(chapter, speakerId);
            Color color = StoryData.SpeakerColor(chapter, speakerId, ToyKit.Palette[1]);

            if (big)
            {
                m_bigName.text = name;
                var plate = m_bigName.transform.parent.GetComponent<Image>();
                plate.gameObject.SetActive(!direction);

                var sprite = direction ? null : StoryData.Portrait(chapter, speakerId);
                if (m_portraitFace != null)
                {
                    m_portraitFace.enabled = sprite != null;
                    if (sprite != null)
                    {
                        m_portraitFace.sprite = sprite;
                        m_portraitFace.color = Color.white;
                        m_portraitFace.preserveAspect = true;
                    }
                }
                m_portrait.color = sprite != null
                    ? new Color(0.92f, 0.88f, 0.8f)
                    : direction ? new Color(color.r, color.g, color.b, 0.35f) : color;
            }
            else
            {
                m_smallName.text = name;
                var plate = m_smallName.transform.parent.GetComponent<Image>();
                plate.gameObject.SetActive(!direction);
            }
        }

        //========================================
        // 補助
        //========================================

        IEnumerator Fade(Image image, float from, float to, float time)
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, time))
            {
                float a = Mathf.Lerp(from, to, Mathf.Clamp01(t));
                image.color = new Color(image.color.r, image.color.g, image.color.b, a);
                yield return null;
            }
            image.color = new Color(image.color.r, image.color.g, image.color.b, to);
        }

        IEnumerator FadeGroup(CanvasGroup group, float from, float to, float time)
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, time))
            {
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t));
                yield return null;
            }
            group.alpha = to;
        }
    }
}
