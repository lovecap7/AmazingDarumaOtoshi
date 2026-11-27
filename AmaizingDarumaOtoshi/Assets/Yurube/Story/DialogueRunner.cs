using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
        const string HintText = StoryText.Hint;
        const string ChoiceHintText = "↑↓：えらぶ　A：けってい";

        static readonly Color ChoiceSelectedBg = new Color(1f, 0.95f, 0.78f, 1f);
        static readonly Color ChoiceNormalBg = new Color(0.95f, 0.92f, 0.87f, 0.94f);
        static readonly Color ChoiceSelectedOutline = new Color(1.0f, 0.84f, 0.24f, 1f);

        // 文字送りの速さ（1秒あたりの文字数）。インスペクターで変えられる
        [SerializeField, Min(1f)] float m_charsPerSecond = 34f;
        // 小さいウィンドウを自動で閉じるまでの秒数
        [SerializeField, Min(0.5f)] float m_smallSeconds = 3.0f;
        // 背景を暗くする濃さ（大きいウィンドウのとき）
        [SerializeField, Range(0f, 0.9f)] float m_dimAlpha = 0.62f;

        static readonly Color PanelColor = new Color(1f, 0.98f, 0.94f, 0.97f);
        static readonly Color Ink = ToyKit.Ink;

        TMP_FontAsset m_font;
        RectTransform m_root, m_bigPanel, m_bigPlate, m_smallPanel, m_newsPanel, m_objectivePanel, m_koPanel;
        Image m_dim, m_portrait, m_koDim, m_portraitFace;
        Image m_smallPortrait, m_smallFace;
        TextMeshProUGUI m_bigName, m_bigText, m_smallName, m_smallText, m_hint;
        RectTransform m_hintPlate;
        TextMeshProUGUI m_newsHeader, m_newsHeadline, m_newsBody;
        TextMeshProUGUI m_objectiveName, m_objectiveText, m_koMain, m_koSub;
        Image m_newsImage;
        TextMeshProUGUI m_newsCaption;
        GameObject m_newsCaptionBg;
        RectTransform m_newsSubBg;
        TextMeshProUGUI m_newsSubHeader, m_newsSubBody;
        CanvasGroup m_bigGroup, m_smallGroup, m_newsGroup, m_objectiveGroup, m_koGroup, m_hintGroup;
        class ChoiceButton
        {
            public RectTransform Rect;
            public Image Bg;
            public Outline Outline;
            public TextMeshProUGUI Arrow;
            public TextMeshProUGUI Label;
        }

        RectTransform m_choiceRoot;
        CanvasGroup m_choiceGroup;
        readonly List<ChoiceButton> m_choiceButtons = new List<ChoiceButton>();
        RectTransform m_unlockBanner;
        CanvasGroup m_unlockBannerGroup;
        TextMeshProUGUI m_unlockBannerText;
        Coroutine m_unlockBannerRoutine;
        GameObject m_darkCardRoot;
        CanvasGroup m_darkCardGroup;
        CanvasGroup m_darkCardTextGroup;
        TextMeshProUGUI m_darkCardText;
        bool m_skipped;
        bool m_smallShowing;
        bool m_bigShowing;
        bool m_choiceShowing;
        bool m_compactLayout;
        // いま出している小窓のコルーチン。新しい小窓が来たら止めて、すぐ切り替える
        Coroutine m_smallRoutine;

        // 大きいウィンドウ・小さいウィンドウ・選択肢・新聞のどれかが出ているか
        public bool IsShowing => m_bigShowing || m_smallShowing || m_choiceShowing;

        // 直前の会話で B（スキップ）が押されたか。エピローグのように「Bで残りを飛ばす」場面で使う
        public bool WasSkipped => m_skipped;

        // 直前の2択で選ばれた結果（未選択なら null）。セーブには書かず、呼び出し元の分岐判定に使う
        public StoryChoiceResult? LastChoiceResult { get; private set; }

        // 1行を表示し始めた（話者ID。ト書きは空）。会話中にカメラを話者へ寄せる演出用
        public event Action<string> LineStarted;
        // 1行を表示し始めた（行データ全体。ショットIDなどを使ったカメラ切り替え用）
        public event Action<StoryLine> StoryLineStarted;
        // 1行の文字送りが完了した（timing="end" の演出キュー発火用）
        public event Action<StoryLine> StoryLineTextCompleted;
        // Bスキップで会話を飛ばした（演出キューのキャンセル・暗転整合用）
        public event Action DialogueSkipped;
        // 2択の選択肢を決定した（最終章のエンディング分岐などで使用）
        public event Action<StoryChoiceResult> ChoiceSelected;

        // 操作バナーを目立たせる（連鎖の詰まり防止）
        bool m_objectiveEmphasis;

        // 会話の入力を最後に受け取ったフレーム。
        // MenuInput は「このフレームで押されたか」を返すので、同じフレームで2回見ると
        // A を1回押しただけで「全文表示」と「次へ」や、次の行の分まで進んでしまう（1行おきに台詞が抜けていた）。
        // 1回の押下は1回の操作にだけ使う。
        int m_inputFrame = -1;

        bool InputFree => Time.frameCount != m_inputFrame;
        void ConsumeInput() => m_inputFrame = Time.frameCount;

        // A／Enter／クリック（このフレームでまだ使っていない押下だけ）
        bool AdvancePressed()
        {
            if (!InputFree) return false;
            if (!(MenuInput.Submit() || MenuInput.AnyClick())) return false;
            ConsumeInput();
            return true;
        }

        // B／Esc（このフレームでまだ使っていない押下だけ）
        bool SkipPressed()
        {
            if (!InputFree) return false;
            if (!MenuInput.Cancel()) return false;
            ConsumeInput();
            return true;
        }

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
            BuildChoice();
            BuildSmall();
            BuildNews();
            BuildObjective();
            BuildKo();
            BuildUnlockBanner();
            BuildDarkCard();

            // 右下の操作説明（暗い帯を敷いて、明るい会話ウィンドウの上でも読めるようにする）
            var hintPlate = ToyKit.UIRounded("HintPlate", m_root, new Color(0.05f, 0.04f, 0.03f, 0.78f), 16f);
            hintPlate.pivot = new Vector2(1f, 0f);
            ToyKit.Anchor(hintPlate, new Vector2(1f, 0f), new Vector2(-32f, 30f), new Vector2(560f, 56f));
            m_hintPlate = hintPlate;
            hintPlate.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.18f);
            m_hintGroup = hintPlate.gameObject.AddComponent<CanvasGroup>();
            m_hint = ToyKit.UIText("Hint", hintPlate, m_font, HintText, 30f, Color.white);
            ToyKit.Stretch(m_hint.rectTransform);
            m_hint.rectTransform.offsetMin = new Vector2(14f, 0f);
            m_hint.rectTransform.offsetMax = new Vector2(-14f, 0f);
            m_hint.alignment = TextAlignmentOptions.Right;
            m_hint.ForceMeshUpdate();
            // 文字の幅に合わせて背景を縮める（背景だけが大きくならないように）
            m_hintPlate.sizeDelta = new Vector2(Mathf.Clamp(m_hint.preferredWidth + 30f, 280f, 560f), 56f);
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

        // キャラ解放などの画面表示バナー（例：「だるきんぐが使えるようになった！」）
        void BuildUnlockBanner()
        {
            var panel = ToyKit.UIRounded("UnlockBanner", m_root, new Color(0.14f, 0.08f, 0.22f, 0.96f), 26f);
            ToyKit.Anchor(panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(1140f, 126f));
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.84f, 0.26f, 1f);
            outline.effectDistance = new Vector2(4f, -4f);
            m_unlockBanner = panel;
            m_unlockBannerGroup = panel.gameObject.AddComponent<CanvasGroup>();
            m_unlockBannerGroup.blocksRaycasts = false;
            m_unlockBannerGroup.interactable = false;

            m_unlockBannerText = ToyKit.UIText("Text", panel, m_font, "", 44f, new Color(1f, 0.95f, 0.78f));
            m_unlockBannerText.fontStyle = FontStyles.Bold;
            m_unlockBannerText.enableAutoSizing = true;
            m_unlockBannerText.fontSizeMin = 24f;
            m_unlockBannerText.fontSizeMax = 44f;
            ToyKit.Stretch(m_unlockBannerText.rectTransform);
            m_unlockBannerText.rectTransform.offsetMin = new Vector2(30f, 10f);
            m_unlockBannerText.rectTransform.offsetMax = new Vector2(-30f, -10f);

            m_unlockBannerGroup.alpha = 0f;
            panel.gameObject.SetActive(false);
        }

        // バッドエンド用の黒背景白文字画面表示（例：「アメイジングだるま落としは、終わった」）
        void BuildDarkCard()
        {
            var bg = ToyKit.UIImage("DarkCard", m_root, Color.black);
            ToyKit.Stretch(bg);
            m_darkCardRoot = bg.gameObject;
            m_darkCardGroup = m_darkCardRoot.AddComponent<CanvasGroup>();
            m_darkCardGroup.blocksRaycasts = false;
            m_darkCardGroup.interactable = false;

            var textGo = new GameObject("DarkCardTextWrap", typeof(RectTransform)).GetComponent<RectTransform>();
            textGo.SetParent(bg, false);
            ToyKit.Stretch(textGo);
            m_darkCardTextGroup = textGo.gameObject.AddComponent<CanvasGroup>();
            m_darkCardText = ToyKit.UIText("Text", textGo, m_font, "", 56f, Color.white);
            m_darkCardText.fontStyle = FontStyles.Bold;
            m_darkCardText.enableAutoSizing = true;
            m_darkCardText.fontSizeMin = 28f;
            m_darkCardText.fontSizeMax = 56f;
            ToyKit.Anchor(m_darkCardText.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 220f));

            m_darkCardGroup.alpha = 0f;
            m_darkCardTextGroup.alpha = 0f;
            m_darkCardRoot.SetActive(false);
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
            m_bigPlate = plate;
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

        void BuildChoice()
        {
            var root = new GameObject("Choice", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(m_root, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.anchoredPosition = new Vector2(220f, 642f);
            root.sizeDelta = new Vector2(940f, 170f);
            m_choiceRoot = root;
            m_choiceGroup = root.gameObject.AddComponent<CanvasGroup>();
            m_choiceGroup.alpha = 0f;
            root.gameObject.SetActive(false);
        }

        void EnsureChoiceButtons(int count)
        {
            const float itemWidth = 940f;
            const float itemHeight = 78f;
            const float gap = 14f;
            float totalHeight = count > 0 ? count * itemHeight + (count - 1) * gap : itemHeight;
            m_choiceRoot.sizeDelta = new Vector2(itemWidth, totalHeight);

            while (m_choiceButtons.Count < count)
            {
                int i = m_choiceButtons.Count;
                var rect = ToyKit.UIRounded("Option" + i, m_choiceRoot, ChoiceNormalBg, 20f);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.GetComponent<Image>().pixelsPerUnitMultiplier = 0.55f;

                var outline = rect.gameObject.AddComponent<Outline>();
                outline.effectColor = Ink;
                outline.effectDistance = new Vector2(2f, -2f);

                var arrow = ToyKit.UIText("Arrow", rect, m_font, "▶", 28f, new Color(0.84f, 0.25f, 0.20f, 1f));
                arrow.fontStyle = FontStyles.Bold;
                arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                arrow.rectTransform.pivot = new Vector2(0f, 0.5f);
                arrow.rectTransform.anchoredPosition = new Vector2(24f, 0f);
                arrow.rectTransform.sizeDelta = new Vector2(36f, 44f);

                var label = ToyKit.UIText("Label", rect, m_font, "", 32f, Ink);
                label.alignment = TextAlignmentOptions.Left;
                label.fontStyle = FontStyles.Bold;
                label.textWrappingMode = TextWrappingModes.Normal;
                label.enableAutoSizing = true;
                label.fontSizeMin = 20f;
                label.fontSizeMax = 32f;
                var labelRect = label.rectTransform;
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 0.5f);
                labelRect.pivot = new Vector2(0f, 0.5f);
                labelRect.anchoredPosition = new Vector2(72f, 0f);
                labelRect.sizeDelta = new Vector2(itemWidth - 98f, itemHeight - 14f);

                m_choiceButtons.Add(new ChoiceButton
                {
                    Rect = rect,
                    Bg = rect.GetComponent<Image>(),
                    Outline = outline,
                    Arrow = arrow,
                    Label = label,
                });
            }

            for (int i = 0; i < m_choiceButtons.Count; i++)
            {
                bool active = i < count;
                m_choiceButtons[i].Rect.gameObject.SetActive(active);
                if (active)
                {
                    m_choiceButtons[i].Rect.anchoredPosition = new Vector2(0f, -i * (itemHeight + gap));
                    m_choiceButtons[i].Rect.sizeDelta = new Vector2(itemWidth, itemHeight);
                }
            }
        }

        void UpdateChoiceVisuals(int selectedIndex, int count)
        {
            for (int i = 0; i < count && i < m_choiceButtons.Count; i++)
            {
                bool selected = i == selectedIndex;
                var btn = m_choiceButtons[i];
                btn.Bg.color = selected ? ChoiceSelectedBg : ChoiceNormalBg;
                btn.Outline.effectColor = selected ? ChoiceSelectedOutline : Ink;
                btn.Outline.effectDistance = selected ? new Vector2(4f, -4f) : new Vector2(2f, -2f);
                btn.Rect.localScale = selected ? Vector3.one * 1.03f : Vector3.one;
                btn.Arrow.gameObject.SetActive(selected);
            }
        }

        void UpdateHint(string text)
        {
            if (m_hint == null || m_hintPlate == null) return;
            m_hint.text = text;
            m_hint.ForceMeshUpdate();
            m_hintPlate.sizeDelta = new Vector2(Mathf.Clamp(m_hint.preferredWidth + 30f, 280f, 560f), 56f);
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

            // 顔アイコン（対戦中の掛け合いでも、誰が話しているか分かるように）
            var portrait = ToyKit.UIRounded("Portrait", panel, Color.white, 999f);
            ToyKit.Anchor(portrait, new Vector2(0f, 0.5f), new Vector2(88f, 0f), new Vector2(118f, 118f));
            portrait.gameObject.AddComponent<Outline>().effectColor = Ink;
            portrait.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            m_smallPortrait = portrait.GetComponent<Image>();
            var face = ToyKit.UIImage("Face", portrait, Color.white);
            ToyKit.Stretch(face);
            face.offsetMin = new Vector2(4f, 4f);
            face.offsetMax = new Vector2(-4f, -4f);
            m_smallFace = face.GetComponent<Image>();
            m_smallFace.preserveAspect = true;
            m_smallFace.enabled = false;

            var plate = ToyKit.UIRounded("Plate", panel, ToyKit.Palette[3], 14f);
            ToyKit.Anchor(plate, new Vector2(0f, 1f), new Vector2(300f, -26f), new Vector2(260f, 44f));
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
            textRect.anchoredPosition = new Vector2(185f, -70f);
            textRect.sizeDelta = new Vector2(1120f, 86f);

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

            // 1枚絵（空なら新聞用の絵を作る）
            var frame = ToyKit.UIRounded("Image", panel, new Color(0.88f, 0.84f, 0.78f, 1f), 10f);
            ToyKit.Anchor(frame, new Vector2(0.5f, 1f), new Vector2(0f, -430f), new Vector2(1180f, 320f));
            m_newsImage = frame.GetComponent<Image>();

            // 写真のキャプション（画像の下端に重ねる）
            var captionBg = ToyKit.UIImage("CaptionBg", panel, new Color(0.07f, 0.06f, 0.05f, 0.72f));
            captionBg.pivot = new Vector2(0f, 1f);
            ToyKit.Anchor(captionBg, new Vector2(0f, 1f), new Vector2(66f, -552f), new Vector2(320f, 34f));
            m_newsCaptionBg = captionBg.gameObject;
            m_newsCaption = ToyKit.UIText("Caption", panel, m_font, "", 24f, Color.white);
            m_newsCaption.alignment = TextAlignmentOptions.Left;
            m_newsCaption.rectTransform.pivot = new Vector2(0f, 1f);
            ToyKit.Anchor(m_newsCaption.rectTransform, new Vector2(0f, 1f), new Vector2(80f, -556f), new Vector2(300f, 30f));
            m_newsCaptionBg.SetActive(false);
            m_newsCaption.gameObject.SetActive(false);

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

            // 紙面の隅の小さな囲み記事（最終章）。subBody があるときだけ出す
            var subBg = ToyKit.UIRounded("SubArticle", panel, new Color(0.97f, 0.94f, 0.87f, 1f), 10f);
            subBg.pivot = new Vector2(0f, 1f);
            ToyKit.Anchor(subBg, new Vector2(0f, 1f), new Vector2(60f, -716f), new Vector2(640f, 96f));
            subBg.gameObject.AddComponent<Outline>().effectColor = Ink;
            m_newsSubBg = subBg;
            m_newsSubHeader = ToyKit.UIText("SubHeader", subBg, m_font, "", 26f, Ink);
            m_newsSubHeader.alignment = TextAlignmentOptions.TopLeft;
            m_newsSubHeader.fontStyle = FontStyles.Bold;
            m_newsSubHeader.textWrappingMode = TextWrappingModes.NoWrap;
            m_newsSubHeader.rectTransform.anchorMin = m_newsSubHeader.rectTransform.anchorMax = new Vector2(0f, 1f);
            m_newsSubHeader.rectTransform.pivot = new Vector2(0f, 1f);
            m_newsSubHeader.rectTransform.anchoredPosition = new Vector2(14f, -8f);
            m_newsSubHeader.rectTransform.sizeDelta = new Vector2(612f, 30f);
            m_newsSubBody = ToyKit.UIText("SubBody", subBg, m_font, "", 20f, Ink);
            m_newsSubBody.alignment = TextAlignmentOptions.TopLeft;
            m_newsSubBody.textWrappingMode = TextWrappingModes.Normal;
            m_newsSubBody.enableAutoSizing = true;
            m_newsSubBody.fontSizeMin = 14f;
            m_newsSubBody.fontSizeMax = 20f;
            m_newsSubBody.rectTransform.anchorMin = m_newsSubBody.rectTransform.anchorMax = new Vector2(0f, 1f);
            m_newsSubBody.rectTransform.pivot = new Vector2(0f, 1f);
            m_newsSubBody.rectTransform.anchoredPosition = new Vector2(14f, -38f);
            m_newsSubBody.rectTransform.sizeDelta = new Vector2(612f, 52f);
            subBg.gameObject.SetActive(false);

            m_newsGroup.alpha = 0f;
            panel.gameObject.SetActive(false);
        }

        //========================================
        // 大きいウィンドウ（物語の会話）
        //========================================

        // onLine: 行を出し始めたときに呼ぶ（章ごとの演出を台詞に合わせるため。省略可）
        // onChoice: 2択の選択肢を選んだときに呼ぶ（最終章の分岐など。省略可）
        // onBeforeLine: 行を表示する前に挟む非同期演出（場面転換・暗転テロップ・専用演出など。省略可）
        public IEnumerator PlayBig(StoryChapter chapter, IList<StoryLine> lines,
                                   Action<int, StoryLine> onLine = null,
                                   Action<StoryChoiceResult> onChoice = null,
                                   Func<StoryLine, IEnumerator> onBeforeLine = null)
        {
            if (lines == null || lines.Count == 0) yield break;
            Build();
            while (m_smallShowing) yield return null;   // 小窓と重ねない
            m_bigShowing = true;
            m_skipped = false;
            HideObjective();

            // OnGUI の必殺技ゲージは会話より手前に描かれてしまうので、会話のあいだは描かせない
            bool gaugeWasSuppressed = SpecialGaugeHUD.Suppressed;
            SpecialGaugeHUD.Suppressed = true;

            m_bigPanel.gameObject.SetActive(true);
            m_smallPanel.gameObject.SetActive(false);
            UpdateHint(HintText);
            m_hintGroup.gameObject.SetActive(true);
            m_hintGroup.alpha = 1f;
            // フェードインの前に最初の行を入れておく（空のウィンドウが一瞬出ないように）
            var first = FirstLine(lines);
            if (first != null) PrepareLine(chapter, first, true);
            yield return Fade(m_dim, 0f, m_dimAlpha, 0.2f);
            yield return FadeGroup(m_bigGroup, 0f, 1f, 0.15f);

            IList<StoryLine> currentLines = lines;
            int index = 0;
            bool firstPrepared = first != null;
            StoryLine lastDialogueLine = first;
            while (currentLines != null && index < currentLines.Count)
            {
                var line = currentLines[index];
                if (line == null) { index++; continue; }
                if (line.HasChoice)
                {
                    if (m_skipped)
                    {
                        // Bスキップは選択肢の手前で止める。スキップされた直前の台詞が画面に出た状態にする
                        m_skipped = false;
                        if (lastDialogueLine != null)
                        {
                            PrepareLine(chapter, lastDialogueLine, true);
                            m_bigText.maxVisibleCharacters = m_bigText.textInfo.characterCount;
                        }
                    }
                    string nextBranchId = null;
                    yield return RunChoice(chapter, line.choice, onChoice, id => nextBranchId = id);
                    firstPrepared = false;
                    lastDialogueLine = null;
                    if (m_skipped) break;

                    // 選んだ branchId に一致する分岐ブロックがあれば、そこへ切り替えて再生する
                    // （第1章〜第3章など branches が未定義の章は従来どおり元のリストの続きへ）
                    var branch = chapter != null ? chapter.FindBranch(nextBranchId) : null;
                    if (branch != null && branch.lines != null && branch.lines.Count > 0)
                    {
                        currentLines = branch.lines;
                        index = 0;
                        continue;
                    }
                    index++;
                    continue;
                }

                lastDialogueLine = line;
                if (m_skipped) { index++; continue; }

                if (onBeforeLine != null)
                {
                    var pre = onBeforeLine(line);
                    if (pre != null)
                    {
                        yield return pre;
                        firstPrepared = false;
                        if (m_skipped) break;
                    }
                }

                yield return ShowLine(chapter, line, big: true, index, onLine, firstPrepared);
                firstPrepared = false;
                index++;
            }

            if (m_choiceRoot != null)
            {
                m_choiceGroup.alpha = 0f;
                m_choiceRoot.gameObject.SetActive(false);
            }
            m_choiceShowing = false;
            UpdateHint(HintText);
            m_hintGroup.alpha = 0f;
            m_hintGroup.gameObject.SetActive(false);
            yield return FadeGroup(m_bigGroup, m_bigGroup.alpha, 0f, 0.15f);
            yield return Fade(m_dim, m_dim.color.a, 0f, 0.2f);
            m_bigPanel.gameObject.SetActive(false);
            m_bigShowing = false;
            SpecialGaugeHUD.Suppressed = gaugeWasSuppressed;
        }

        // 会話中の場面転換などで、大きいウィンドウとヒントを一時的に隠す／戻す
        public void SetBigVisible(bool visible)
        {
            if (m_bigGroup != null) m_bigGroup.alpha = visible ? 1f : 0f;
            if (m_bigPanel != null) m_bigPanel.gameObject.SetActive(visible);
            if (m_hintGroup != null)
            {
                m_hintGroup.alpha = visible ? 1f : 0f;
                m_hintGroup.gameObject.SetActive(visible);
            }
            if (m_dim != null)
            {
                var c = m_dim.color;
                c.a = visible ? m_dimAlpha : 0f;
                m_dim.color = c;
            }
        }

        // キャラ解放の簡易バナーを表示する（例：「だるきんぐが使えるようになった！」）
        public void ShowUnlockBanner(string text, float seconds = 3.2f)
        {
            if (string.IsNullOrEmpty(text)) return;
            Build();
            if (m_unlockBannerRoutine != null) StopCoroutine(m_unlockBannerRoutine);
            m_unlockBannerRoutine = StartCoroutine(UnlockBannerRoutine(text, Mathf.Max(1.0f, seconds)));
        }

        public IEnumerator PlayUnlockBanner(string text, float seconds = 3.0f)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            Build();
            if (m_unlockBannerRoutine != null) StopCoroutine(m_unlockBannerRoutine);
            yield return UnlockBannerRoutine(text, Mathf.Max(1.0f, seconds));
        }

        IEnumerator UnlockBannerRoutine(string text, float seconds)
        {
            m_unlockBannerText.text = text;
            m_unlockBanner.gameObject.SetActive(true);
            m_unlockBanner.SetAsLastSibling();
            m_unlockBanner.localScale = Vector3.one * 0.92f;
            PreludeSfx.Play(PreludeSfx.Unlock, this);

            float t = 0f;
            while (t < 0.25f)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / 0.25f);
                m_unlockBannerGroup.alpha = u;
                m_unlockBanner.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, ToyKit.EaseOutBack(u));
                yield return null;
            }
            m_unlockBannerGroup.alpha = 1f;
            m_unlockBanner.localScale = Vector3.one;

            yield return new WaitForSecondsRealtime(seconds);

            yield return FadeGroup(m_unlockBannerGroup, 1f, 0f, 0.3f);
            m_unlockBanner.gameObject.SetActive(false);
            m_unlockBannerRoutine = null;
        }

        // バッドエンド用：黒背景に白文字のメッセージを表示し、その後の暗転＋SEを再生する
        public IEnumerator PlayDarkMessageCard(string text, float holdSeconds, string afterSe = null, float afterDarkSeconds = 0f)
        {
            Build();
            m_darkCardRoot.SetActive(true);
            m_darkCardRoot.transform.SetAsLastSibling();
            m_darkCardText.text = text ?? "";
            m_darkCardGroup.alpha = 1f;
            m_darkCardTextGroup.alpha = 0f;

            yield return FadeGroup(m_darkCardTextGroup, 0f, 1f, 0.65f);
            float end = Time.unscaledTime + Mathf.Max(0.8f, holdSeconds);
            while (Time.unscaledTime < end) yield return null;
            yield return FadeGroup(m_darkCardTextGroup, 1f, 0f, 0.65f);

            if (!string.IsNullOrEmpty(afterSe)) PreludeSfx.Play(afterSe, this);
            if (afterDarkSeconds > 0f)
                yield return new WaitForSecondsRealtime(afterDarkSeconds);

            m_darkCardGroup.alpha = 0f;
            m_darkCardRoot.SetActive(false);
        }

        IEnumerator RunChoice(StoryChapter chapter, StoryChoice choice, Action<StoryChoiceResult> onChoice, Action<string> onBranchResolved = null)
        {
            if (choice == null || choice.options == null || choice.options.Count == 0) yield break;

            int count = choice.options.Count;
            EnsureChoiceButtons(count);
            for (int i = 0; i < count; i++)
            {
                var opt = choice.options[i];
                m_choiceButtons[i].Label.text = opt != null && !string.IsNullOrEmpty(opt.text) ? opt.text : "";
            }

            int selected = 0;
            UpdateChoiceVisuals(selected, count);
            UpdateHint(ChoiceHintText);

            m_choiceShowing = true;
            m_choiceRoot.gameObject.SetActive(true);
            yield return FadeGroup(m_choiceGroup, 0f, 1f, 0.12f);

            while (true)
            {
                // B（スキップ）を押しても選択肢は閉じない・飛ばさない
                if (MenuInput.Up())
                {
                    selected = (selected - 1 + count) % count;
                    UpdateChoiceVisuals(selected, count);
                    MenuSounds.Cursor();
                }
                else if (MenuInput.Down())
                {
                    selected = (selected + 1) % count;
                    UpdateChoiceVisuals(selected, count);
                    MenuSounds.Cursor();
                }

                var mouse = Mouse.current;
                bool mouseMoved = mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0f;
                bool click = InputFree && MenuInput.AnyClick();
                int hover = -1;
                if (mouse != null && (mouseMoved || click))
                {
                    Vector2 mp = mouse.position.ReadValue();
                    for (int i = 0; i < count; i++)
                    {
                        if (RectTransformUtility.RectangleContainsScreenPoint(m_choiceButtons[i].Rect, mp, null))
                        {
                            hover = i;
                            break;
                        }
                    }
                    if (hover >= 0 && hover != selected)
                    {
                        selected = hover;
                        UpdateChoiceVisuals(selected, count);
                        MenuSounds.Cursor();
                    }
                }

                bool submitKey = InputFree && MenuInput.Submit();
                bool clickOption = click && hover >= 0;
                if (submitKey || clickOption)
                {
                    ConsumeInput();
                    MenuSounds.Decide();
                    break;
                }
                yield return null;
            }

            yield return FadeGroup(m_choiceGroup, 1f, 0f, 0.1f);
            m_choiceRoot.gameObject.SetActive(false);
            m_choiceShowing = false;
            UpdateHint(HintText);

            var chosen = choice.options[selected];
            string chosenText = chosen != null ? chosen.text : "";
            string branchId = chosen != null ? chosen.branchId : "";
            var result = new StoryChoiceResult(choice.id, selected, branchId, chosenText);
            LastChoiceResult = result;
            onBranchResolved?.Invoke(branchId);
            onChoice?.Invoke(result);
            ChoiceSelected?.Invoke(result);

            // 選んだ選択肢を操作キャラ（または指定話者）の台詞として表示してから、反応を再生する
            string speakerId = !string.IsNullOrEmpty(choice.speaker)
                ? choice.speaker
                : (chapter != null && !string.IsNullOrEmpty(chapter.playerCharacter) ? chapter.playerCharacter : "akadaru");
            if (!string.IsNullOrEmpty(chosenText))
            {
                var selfLine = new StoryLine { speaker = speakerId, text = chosenText };
                yield return ShowLine(chapter, selfLine, big: true);
                if (m_skipped) yield break;
            }

            if (chosen != null && chosen.reaction != null)
            {
                foreach (var r in chosen.reaction)
                {
                    if (r == null) continue;
                    yield return ShowLine(chapter, r.ToStoryLine(), big: true);
                    if (m_skipped) yield break;
                }
            }
        }

        // 右下の操作ヒントを消す（GO!やメニュー移行時に残らないように）
        public void HideHint()
        {
            if (m_hintGroup == null) return;
            m_hintGroup.alpha = 0f;
            m_hintGroup.gameObject.SetActive(false);
        }

        // 背景を暗くする濃さを変える（ムービーを背景にするときは薄くする）
        public void SetDimAlpha(float alpha)
        {
            m_dimAlpha = Mathf.Clamp01(alpha);
        }

        // 試合のない章（プロローグ等）向けに、背景カットシーンを遮らないよう会話ウィンドウを画面最下部に小さく配置する
        public void SetCompactBottomLayout(bool compact)
        {
            Build();
            m_compactLayout = compact;
            ApplyBigPanelLayout(hidePortrait: false);
        }

        void ApplyBigPanelLayout(bool hidePortrait)
        {
            if (m_bigPanel == null) return;
            var bg = m_bigPanel.GetComponent<Image>();
            if (m_compactLayout)
            {
                if (bg != null) bg.color = new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.90f);
                ToyKit.Anchor(m_bigPanel, new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(1500f, 200f));
                if (m_portrait != null)
                    ToyKit.Anchor(m_portrait.rectTransform, new Vector2(0f, 0.5f), new Vector2(105f, 0f), new Vector2(144f, 144f));
                if (m_bigPlate != null)
                    ToyKit.Anchor(m_bigPlate, new Vector2(0f, 1f), new Vector2(hidePortrait ? 206f : 360f, -38f), new Vector2(320f, 48f));
                if (m_bigText != null)
                {
                    var textRect = m_bigText.rectTransform;
                    textRect.anchorMin = textRect.anchorMax = new Vector2(0f, 1f);
                    textRect.pivot = new Vector2(0f, 1f);
                    float left = hidePortrait ? 48f : 204f;
                    textRect.anchoredPosition = new Vector2(left, -72f);
                    textRect.sizeDelta = new Vector2(1500f - left - 44f, 114f);
                }
                if (m_choiceRoot != null)
                    ToyKit.Anchor(m_choiceRoot, new Vector2(0.5f, 0f), new Vector2(220f, 316f), m_choiceRoot.sizeDelta);
            }
            else
            {
                if (bg != null) bg.color = PanelColor;
                ToyKit.Anchor(m_bigPanel, new Vector2(0.5f, 0f), new Vector2(0f, 280f), new Vector2(1460f, 340f));
                if (m_portrait != null)
                    ToyKit.Anchor(m_portrait.rectTransform, new Vector2(0f, 0.5f), new Vector2(140f, 20f), new Vector2(210f, 210f));
                if (m_bigPlate != null)
                    ToyKit.Anchor(m_bigPlate, new Vector2(0f, 1f), new Vector2(hidePortrait ? 226f : 300f, -46f), new Vector2(340f, 62f));
                if (m_bigText != null)
                {
                    var textRect = m_bigText.rectTransform;
                    textRect.anchorMin = textRect.anchorMax = new Vector2(0f, 1f);
                    textRect.pivot = new Vector2(0f, 1f);
                    float left = hidePortrait ? 60f : 300f;
                    textRect.anchoredPosition = new Vector2(left, -100f);
                    textRect.sizeDelta = new Vector2(1460f - left - 40f, 190f);
                }
                if (m_choiceRoot != null)
                    ToyKit.Anchor(m_choiceRoot, new Vector2(0.5f, 0f), new Vector2(220f, 642f), m_choiceRoot.sizeDelta);
            }
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
            m_objectivePanel.localScale = Vector3.one;
            m_objectivePanel.gameObject.SetActive(true);
            m_objectiveGroup.alpha = 1f;
        }

        // 操作バナーを点滅させて目立たせる（連鎖が起きないときの強調）
        public void SetObjectiveEmphasis(bool on)
        {
            m_objectiveEmphasis = on;
            if (!on && m_objectivePanel != null) m_objectivePanel.localScale = Vector3.one;
        }

        public void HideObjective()
        {
            m_objectiveEmphasis = false;
            if (m_objectivePanel == null) return;
            m_objectivePanel.localScale = Vector3.one;
            m_objectiveGroup.alpha = 0f;
            m_objectivePanel.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!m_objectiveEmphasis || m_objectivePanel == null || !m_objectivePanel.gameObject.activeSelf) return;
            float s = 1f + 0.035f * Mathf.Sin(Time.unscaledTime * 6f);
            m_objectivePanel.localScale = new Vector3(s, s, 1f);
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
        // 新しい小窓が来たら、前の小窓を待たせずにすぐ入れ替える（台詞がキューに溜まらないように）。
        public IEnumerator PlaySmall(string speakerId, string text, StoryChapter chapter, float secondsOverride = -1f)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            Build();
            CloseSmallNow();
            if (m_bigShowing) yield break;   // 大きい会話の最中は割り込まない

            m_smallRoutine = StartCoroutine(ShowSmall(speakerId, text, chapter, secondsOverride));
            // 新しい小窓が来たら、この小窓は閉じられる。そのときは待たずに戻る。
            // （待ち続けると、前の台詞の続きが新しい台詞を上書きしてしまう）
            Coroutine mine = m_smallRoutine;
            while (m_smallShowing && ReferenceEquals(m_smallRoutine, mine)) yield return null;
        }

        IEnumerator ShowSmall(string speakerId, string text, StoryChapter chapter, float secondsOverride)
        {
            m_smallShowing = true;

            ApplySpeaker(chapter, speakerId, big: false, hidePortrait: false);
            m_smallText.text = text;
            m_smallPanel.gameObject.SetActive(true);
            yield return FadeGroup(m_smallGroup, m_smallGroup.alpha, 1f, 0.12f);

            // 読み終わるくらいの時間だけ出す（短い台詞でも最低 m_smallSeconds）
            float seconds = secondsOverride > 0f
                ? secondsOverride
                : Mathf.Clamp(m_smallSeconds + text.Length * 0.03f, m_smallSeconds, 4.5f);
            float time = 0f;
            while (time < seconds)
            {
                time += Time.unscaledDeltaTime;
                yield return null;
            }

            yield return FadeGroup(m_smallGroup, 1f, 0f, 0.2f);
            m_smallPanel.gameObject.SetActive(false);
            m_smallShowing = false;
            m_smallRoutine = null;
        }

        // いま出ている小窓をすぐ閉じる（次の小窓に切り替えるとき・シーンを離れるとき）
        void CloseSmallNow()
        {
            if (m_smallRoutine != null)
            {
                StopCoroutine(m_smallRoutine);
                m_smallRoutine = null;
            }
            m_smallShowing = false;
            if (m_smallGroup != null) m_smallGroup.alpha = 0f;
            if (m_smallPanel != null) m_smallPanel.gameObject.SetActive(false);
        }

        // 外から小窓を閉じる（試合終了・敗北のときに、台詞が残らないように）
        public void CloseSmallWindow()
        {
            if (m_smallPanel == null) return;
            CloseSmallNow();
        }

        //========================================
        // だるま新聞
        //========================================

        public IEnumerator PlayNewspaper(StoryNewspaper paper)
        {
            if (paper == null || string.IsNullOrEmpty(paper.headline)) yield break;
            Build();
            m_bigShowing = true;

            m_newsHeader.text = string.IsNullOrEmpty(paper.header) ? "だるま新聞" : paper.header;
            m_newsHeadline.text = paper.headline;
            m_newsBody.text = paper.body;

            // 画像が指定されていなければ、新聞用のイラストをコードで描く
            Sprite art = string.IsNullOrEmpty(paper.image) ? null : Resources.Load<Sprite>(paper.image);
            if (art == null) art = StoryNewspaperArt.Create(paper.art);
            m_newsImage.sprite = art;
            m_newsImage.color = Color.white;
            m_newsImage.type = Image.Type.Simple;
            m_newsImage.preserveAspect = true;

            bool hasCaption = !string.IsNullOrEmpty(paper.caption);
            m_newsCaption.text = hasCaption ? paper.caption : "";
            m_newsCaption.gameObject.SetActive(hasCaption);
            m_newsCaptionBg.SetActive(hasCaption);

            // 紙面の隅の小さな囲み記事（最終章など）。subHeader か subBody があれば出す
            bool hasSub = !string.IsNullOrEmpty(paper.subHeader) || !string.IsNullOrEmpty(paper.subBody);
            m_newsSubBg.gameObject.SetActive(hasSub);
            if (hasSub)
            {
                m_newsSubHeader.text = string.IsNullOrEmpty(paper.subHeader) ? "囲み記事" : paper.subHeader;
                m_newsSubBody.text = string.IsNullOrEmpty(paper.subBody) ? "" : paper.subBody;
            }

            m_newsPanel.gameObject.SetActive(true);
            if (hasCaption)
            {
                // キャプションの文字幅に帯を合わせる（左端が画面外に出ないようにする）
                m_newsCaption.ForceMeshUpdate();
                float captionWidth = Mathf.Clamp(m_newsCaption.preferredWidth + 36f, 220f, 1080f);
                ((RectTransform)m_newsCaptionBg.transform).sizeDelta = new Vector2(captionWidth, 34f);
                m_newsCaption.rectTransform.sizeDelta = new Vector2(captionWidth - 20f, 30f);
            }

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
            m_bigShowing = false;
            SpecialGaugeHUD.Suppressed = gaugeWasSuppressed;
        }

        //========================================
        // 1行の表示（文字送り）
        //========================================

        static StoryLine FirstLine(IList<StoryLine> lines)
        {
            if (lines == null) return null;
            foreach (var line in lines)
            {
                if (line != null && !line.HasChoice) return line;
            }
            return null;
        }

        // 文字送りを始める前の状態にする（フェードイン中に空のウィンドウを見せないため）
        void PrepareLine(StoryChapter chapter, StoryLine line, bool big)
        {
            bool direction = line.direction || string.IsNullOrEmpty(line.speaker);
            bool hidePortrait = line.portrait == "none";
            if (big) ApplyBigPanelLayout(hidePortrait);
            ApplySpeaker(chapter, line.speaker, big, hidePortrait);
            StoryLineStarted?.Invoke(line);
            LineStarted?.Invoke(line.speaker);

            var text = big ? m_bigText : m_smallText;
            text.text = line.text;
            // ト書きは小さめ・薄めにして、台詞と区別する
            float bigSize = m_compactLayout ? (direction ? 25f : 34f) : (direction ? 28f : 38f);
            text.fontSize = big ? bigSize : (direction ? 24f : 30f);
            if (big) text.fontSizeMax = bigSize;
            text.color = direction ? new Color(Ink.r, Ink.g, Ink.b, 0.75f) : Ink;
            text.maxVisibleCharacters = 0;
            text.ForceMeshUpdate();
        }

        IEnumerator ShowLine(StoryChapter chapter, StoryLine line, bool big, int index = -1,
                             Action<int, StoryLine> onLine = null, bool alreadyPrepared = false)
        {
            if (!alreadyPrepared) PrepareLine(chapter, line, big);
            var text = big ? m_bigText : m_smallText;

            // 行を出し始めたことを外へ知らせる（横断幕など、台詞に合わせて起こす演出用）
            if (onLine != null) onLine(index, line);

            // 演出イベント（行を出した瞬間）
            if (!string.IsNullOrEmpty(line.eventId)) StoryData.PlayEvent(line.eventId);

            int total = text.textInfo.characterCount;
            float shown = 0f;
            while (shown < total)
            {
                if (SkipPressed()) { m_skipped = true; text.maxVisibleCharacters = total; DialogueSkipped?.Invoke(); yield break; }
                if (AdvancePressed()) break;   // 全文表示（この押下では次へ進まない）
                shown += m_charsPerSecond * Time.unscaledDeltaTime;
                text.maxVisibleCharacters = Mathf.Clamp(Mathf.FloorToInt(shown), 0, total);
                yield return null;
            }
            text.maxVisibleCharacters = total;
            StoryLineTextCompleted?.Invoke(line);

            // 表示し終わったら次へ（入力待ち。全文表示に使った押下とは別の押下を待つ）
            if (big)
            {
                while (true)
                {
                    if (SkipPressed()) { m_skipped = true; DialogueSkipped?.Invoke(); yield break; }
                    if (AdvancePressed()) break;
                    yield return null;
                }
            }
        }

        void ApplySpeaker(StoryChapter chapter, string speakerId, bool big, bool hidePortrait = false)
        {
            bool direction = string.IsNullOrEmpty(speakerId);
            string name = direction ? "" : StoryData.SpeakerName(chapter, speakerId);
            Color color = StoryData.SpeakerColor(chapter, speakerId, ToyKit.Palette[1]);

            if (big)
            {
                m_bigName.text = name;
                var plate = m_bigName.transform.parent.GetComponent<Image>();
                plate.gameObject.SetActive(!direction);
                plate.color = direction ? new Color(color.r, color.g, color.b, 0.5f) : color;

                if (m_portrait != null) m_portrait.gameObject.SetActive(!hidePortrait);
                var sprite = direction || hidePortrait ? null : StoryData.Portrait(chapter, speakerId);
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
                if (m_portrait != null)
                {
                    m_portrait.color = sprite != null
                        ? new Color(0.92f, 0.88f, 0.8f)
                        : direction ? new Color(color.r, color.g, color.b, 0.35f) : color;
                }
            }
            else
            {
                m_smallName.text = name;
                var plate = m_smallName.transform.parent.GetComponent<Image>();
                plate.gameObject.SetActive(!direction);
                plate.color = direction ? new Color(color.r, color.g, color.b, 0.5f) : color;

                var sprite = direction ? null : StoryData.Portrait(chapter, speakerId);
                if (m_smallFace != null)
                {
                    m_smallFace.enabled = sprite != null;
                    if (sprite != null)
                    {
                        m_smallFace.sprite = sprite;
                        m_smallFace.color = Color.white;
                        m_smallFace.preserveAspect = true;
                    }
                }
                if (m_smallPortrait != null)
                {
                    m_smallPortrait.color = sprite != null
                        ? new Color(0.92f, 0.88f, 0.8f)
                        : direction ? new Color(color.r, color.g, color.b, 0.35f) : color;
                }
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
