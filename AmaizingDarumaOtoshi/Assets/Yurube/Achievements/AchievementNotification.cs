using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Yurube.Achievements
{
    // 実績を解除したときの通知。画面の上から1件ずつ出して、少し置いて消える。
    // 同時に何件解除されても、順番に並べて出す。
    // 見た目（色・大きさ・文言・音・出す秒数）は下の定数と AchievementSettings で変えられる。
    public class AchievementNotification : MonoBehaviour
    {
        const int SortingOrder = 210;
        const float PanelWidth = 660f;
        const float PanelHeight = 150f;
        // 画面の右上。パネルの右下を基準に、画面の端からこの距離だけ内側に置く
        const float ShowX = -36f;
        const float ShowY = -36f;
        const float HiddenX = PanelWidth + 80f;   // 画面の右の外（ここから左へ滑り込む）
        const float SlideIn = 0.26f;
        const float FadeOut = 0.28f;
        const float Gap = 0.12f;
        const float DefaultSeconds = 3.5f;

        static AchievementNotification s_instance;
        static int s_suppress;
        static float s_duration = -1f;

        // 1件を出しておく秒数。ラボのスライダーから変えられる
        public static float Duration
        {
            get
            {
                if (s_duration <= 0f)
                {
                    var settings = AchievementSettings.Instance;
                    s_duration = settings != null ? settings.NotificationSeconds : DefaultSeconds;
                }
                return s_duration;
            }
            set { s_duration = Mathf.Max(0.5f, value); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_instance = null;
            s_suppress = 0;
            s_duration = -1f;
        }

        // まとめて解除するときなど、通知だけを止める（解除そのものはふつうに行う）
        public static void BeginSuppress() => s_suppress++;
        public static void EndSuppress() => s_suppress = Mathf.Max(0, s_suppress - 1);

        // 解除された実績を通知する
        public static void Enqueue(AchievementData data)
        {
            if (data == null || s_suppress > 0) return;
            var settings = AchievementSettings.Instance;
            if (settings != null && !settings.NotificationsEnabled) return;
            Instance.Push(data);
        }

        // 解除はせずに、通知の見た目だけを確かめる（実績ラボ用）
        public static void Preview(AchievementData data)
        {
            if (data == null) return;
            Instance.Push(data);
        }

        public static void Clear()
        {
            if (s_instance != null) s_instance.m_queue.Clear();
        }

        static AchievementNotification Instance
        {
            get
            {
                if (s_instance == null)
                {
                    var go = new GameObject("AchievementNotification");
                    DontDestroyOnLoad(go);
                    s_instance = go.AddComponent<AchievementNotification>();
                }
                return s_instance;
            }
        }

        readonly Queue<AchievementData> m_queue = new Queue<AchievementData>();
        Coroutine m_routine;

        RectTransform m_panel;
        Image m_panelImage;
        CanvasGroup m_group;
        Image m_icon;
        Image m_accent;
        TextMeshProUGUI m_iconText, m_caption, m_name, m_condition;

        void Push(AchievementData data)
        {
            if (m_panel == null) Build();
            m_queue.Enqueue(data);
            if (m_routine == null) m_routine = StartCoroutine(Run());
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        //========================================
        // 見た目（ここを変えると通知のデザインが変わる）
        //========================================

        void Build()
        {
            var font = PresentationSettings.DefaultFont;
            var canvas = ToyKit.CreateOverlayCanvas("AchievementNotification", SortingOrder);
            canvas.SetParent(transform, false);

            // パネル（画面の右上。右の外から滑り込む）
            m_panel = ToyKit.UIRounded("Panel", canvas, new Color(ToyKit.Horizon.r, ToyKit.Horizon.g, ToyKit.Horizon.b, 0.97f), 34f);
            m_panelImage = m_panel.GetComponent<Image>();
            m_panel.anchorMin = m_panel.anchorMax = m_panel.pivot = new Vector2(1f, 1f);
            m_panel.anchoredPosition = new Vector2(HiddenX, ShowY);
            m_panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            m_group = m_panel.gameObject.AddComponent<CanvasGroup>();
            m_group.alpha = 0f;
            m_group.blocksRaycasts = false;
            m_group.interactable = false;
            var outline = m_panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.35f);
            outline.effectDistance = new Vector2(4f, -4f);

            // 左のアクセント
            var settings = AchievementSettings.Instance;
            Color accentColor = settings != null ? settings.AccentColor : new Color(0.96f, 0.60f, 0.18f);
            var accentRect = ToyKit.UIRounded("Accent", m_panel, accentColor, 30f);
            m_accent = accentRect.GetComponent<Image>();
            ToyKit.Anchor(accentRect, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(16f, PanelHeight - 28f));

            // アイコン
            var iconRect = ToyKit.UIRounded("Icon", m_panel, accentColor, 26f);
            m_icon = iconRect.GetComponent<Image>();
            ToyKit.Anchor(iconRect, new Vector2(0f, 0.5f), new Vector2(88f, 0f), new Vector2(88f, 88f));
            m_iconText = ToyKit.UIText("IconText", iconRect, font, "", 26f, Color.white);
            m_iconText.fontStyle = FontStyles.Bold;
            ToyKit.Stretch(m_iconText.rectTransform);

            // 文字
            m_caption = ToyKit.UIText("Caption", m_panel, font, "実績解除！", 24f, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.75f));
            m_caption.alignment = TextAlignmentOptions.Left;
            ToyKit.Anchor(m_caption.rectTransform, new Vector2(0f, 1f), new Vector2(386f, -14f), new Vector2(480f, 28f));

            m_name = ToyKit.UIText("Name", m_panel, font, "", 38f, ToyKit.Ink);
            m_name.fontStyle = FontStyles.Bold;
            m_name.alignment = TextAlignmentOptions.Left;
            ToyKit.Anchor(m_name.rectTransform, new Vector2(0f, 1f), new Vector2(386f, -44f), new Vector2(480f, 46f));

            m_condition = ToyKit.UIText("Description", m_panel, font, "", 24f, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.85f));
            m_condition.alignment = TextAlignmentOptions.Left;
            m_condition.textWrappingMode = TextWrappingModes.Normal;
            m_condition.enableAutoSizing = true;
            m_condition.fontSizeMin = 14f;
            m_condition.fontSizeMax = 24f;
            ToyKit.Anchor(m_condition.rectTransform, new Vector2(0f, 1f), new Vector2(386f, -95f), new Vector2(480f, 48f));
        }

        void Fill(AchievementData data)
        {
            var settings = AchievementSettings.Instance;

            // レアリティは文字ではなく色と光沢で示す（パネル・左の帯・アイコンが金属風になる）
            if (m_panelImage != null)
            {
                Color panelBase = new Color(ToyKit.Horizon.r, ToyKit.Horizon.g, ToyKit.Horizon.b, 0.97f);
                m_panelImage.sprite = AchievementVisuals.Plate(Color.Lerp(panelBase, data.RarityColor, 0.5f));
                m_panelImage.color = Color.white;
            }
            m_accent.sprite = AchievementVisuals.Bar(data.RarityColor);
            m_accent.type = Image.Type.Simple;
            m_accent.color = Color.white;
            m_caption.text = "実績解除！";
            m_caption.color = ToyKit.Ink;

            // アイコン。実績ごとの画像 → レアリティ専用の画像（トロフィー／ダイヤ／特別）→ 設定の既定画像 → 色の板
            Sprite sprite = data.Icon;
            if (sprite == null)
            {
                sprite = data.IsSpecial
                    ? AchievementRarityIcons.SpecialIcon()
                    : AchievementRarityIcons.IconOf(data.Rarity);
            }
            if (sprite == null && settings != null) sprite = settings.DefaultIcon;
            if (sprite != null)
            {
                m_icon.sprite = sprite;
                m_icon.type = Image.Type.Simple;
                m_icon.preserveAspect = true;
                m_icon.color = Color.white;
                m_iconText.text = "";
            }
            else
            {
                m_icon.sprite = AchievementVisuals.Plate(data.RarityColor);
                m_icon.type = Image.Type.Sliced;
                m_icon.preserveAspect = false;
                m_icon.color = Color.white;
                m_iconText.text = data.CategoryName;
                // レアリティの板が明るい色のときは、白い文字だと読めないのでインク色にする
                m_iconText.color = data.RarityColor.grayscale > 0.45f ? ToyKit.Ink : Color.white;
            }

            m_name.text = data.Name;
            // ゲーム中の通知は解除条件だけ（ポエムはメニューの「じっせき」で見せる）
            m_condition.text = data.ConditionText;
        }

        void PlaySound()
        {
            var settings = AchievementSettings.Instance;
            if (settings != null && settings.UnlockSound != null) MenuSounds.Play(settings.UnlockSound);
            else MenuSounds.Decide();
        }

        //========================================
        // 出す
        //========================================

        IEnumerator Run()
        {
            while (m_queue.Count > 0)
            {
                var data = m_queue.Dequeue();
                Fill(data);
                PlaySound();

                SetAlpha(0f);
                SetX(HiddenX);
                for (float t = 0f; t < SlideIn; t += Time.unscaledDeltaTime)
                {
                    float u = Mathf.Clamp01(t / SlideIn);
                    SetAlpha(u);
                    SetX(Mathf.Lerp(HiddenX, ShowX, 1f - (1f - u) * (1f - u)));
                    yield return null;
                }
                SetAlpha(1f);
                SetX(ShowX);

                yield return new WaitForSecondsRealtime(Duration);

                for (float t = 0f; t < FadeOut; t += Time.unscaledDeltaTime)
                {
                    float u = Mathf.Clamp01(t / FadeOut);
                    SetAlpha(1f - u);
                    SetX(Mathf.Lerp(ShowX, HiddenX, u * u));
                    yield return null;
                }
                SetAlpha(0f);
                SetX(HiddenX);

                if (m_queue.Count > 0) yield return new WaitForSecondsRealtime(Gap);
            }
            m_routine = null;
        }

        void SetAlpha(float alpha)
        {
            if (m_group != null) m_group.alpha = alpha;
        }

        void SetX(float x)
        {
            if (m_panel == null) return;
            var pos = m_panel.anchoredPosition;
            pos.x = x;
            m_panel.anchoredPosition = pos;
        }
    }
}
