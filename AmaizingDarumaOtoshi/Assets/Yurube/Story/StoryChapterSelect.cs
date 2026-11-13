using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Yurube.Story
{
    // 章選択。メインメニューの「ものがたり」から開く。
    //  ・Resources/Story/*.json を order の小さい順に、積み木の段として下から積む
    //  ・クリア済みは色付き、未解放は灰色でロック
    //  ・A：けってい / B：もどる
    // 章を増やすときは JSON を足すだけで、この画面の並びも自動で増える。
    public class StoryChapterSelect : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        // 章選択を閉じたフレームだけ、メニュー側の入力を1フレーム止める（同じB/Aで二重に反応しないように）
        public static bool SuppressMenuInput => Time.frameCount == s_menuSuppressFrame;
        static int s_menuSuppressFrame = -1;

        MonoBehaviour m_menu;
        bool m_starting;
        List<StoryChapter> m_chapters;
        int m_index;
        TMP_FontAsset m_font;
        readonly List<Image> m_blocks = new List<Image>();
        readonly List<Outline> m_outlines = new List<Outline>();
        readonly List<TextMeshProUGUI> m_titles = new List<TextMeshProUGUI>();
        readonly List<TextMeshProUGUI> m_states = new List<TextMeshProUGUI>();

        public static void Open(MonoBehaviour menu)
        {
            if (IsOpen) return;
            var go = new GameObject("StoryChapterSelect");
            var select = go.AddComponent<StoryChapterSelect>();
            select.m_menu = menu;
            select.Build();
            IsOpen = true;
            if (menu != null) menu.enabled = false;
        }

        void OnDestroy()
        {
            IsOpen = false;
            // 章を始めるときは、シーンが変わるまでメニューを動かさない
            if (!m_starting && m_menu != null)
            {
                s_menuSuppressFrame = Time.frameCount;
                m_menu.enabled = true;
            }
        }

        void Build()
        {
            m_font = PresentationSettings.DefaultFont;
            var root = ToyKit.CreateOverlayCanvas("StoryChapterSelect", 100);

            var dim = ToyKit.UIImage("Dim", root, new Color(0.04f, 0.03f, 0.02f, 0.8f));
            ToyKit.Stretch(dim);

            var panel = ToyKit.UIRounded("Panel", root, new Color(0.99f, 0.97f, 0.92f, 0.99f), 28f);
            ToyKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300f, 860f));
            panel.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;

            var title = ToyKit.UIText("Title", panel, m_font, "ものがたり", 64f, ToyKit.Ink);
            title.fontStyle = FontStyles.Bold;
            ToyKit.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(800f, 80f));

            var sub = ToyKit.UIText("Sub", panel, m_font, "章をえらんでください", 28f, new Color(0.35f, 0.25f, 0.18f));
            ToyKit.Anchor(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -135f), new Vector2(800f, 40f));

            m_chapters = StoryData.LoadAll();
            if (m_chapters.Count == 0)
            {
                var empty = ToyKit.UIText("Empty", panel, m_font, "章がありません", 36f, ToyKit.Ink);
                ToyKit.Anchor(empty.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 60f));
            }

            float bottom = -378f;
            float top = -170f;
            int count = Mathf.Max(1, m_chapters.Count);
            float gap = 10f;
            float blockHeight = Mathf.Clamp((top - bottom - gap * (count - 1)) / count, 46f, 100f);
            float spacing = blockHeight + gap;
            float start = bottom + blockHeight * 0.5f;

            for (int i = 0; i < m_chapters.Count; i++)
            {
                var chapter = m_chapters[i];
                var block = ToyKit.UIRounded("Chapter" + i, panel, Color.white, 20f);
                ToyKit.Anchor(block, new Vector2(0.5f, 0.5f), new Vector2(0f, start + i * spacing),
                    new Vector2(1080f, blockHeight));
                var image = block.GetComponent<Image>();
                var outline = block.gameObject.AddComponent<Outline>();
                outline.effectDistance = new Vector2(4f, -4f);
                m_blocks.Add(image);
                m_outlines.Add(outline);

                float fontSize = Mathf.Clamp(blockHeight * 0.31f, 18f, 32f);
                var name = ToyKit.UIText("Name", block, m_font, chapter.title, fontSize, ToyKit.Ink);
                name.alignment = TextAlignmentOptions.Left;
                name.enableAutoSizing = true;
                name.fontSizeMin = 16f;
                name.fontSizeMax = fontSize;
                var nameRect = name.rectTransform;
                nameRect.anchorMin = nameRect.anchorMax = new Vector2(0f, 0.5f);
                nameRect.pivot = new Vector2(0f, 0.5f);
                nameRect.anchoredPosition = new Vector2(36f, 0f);
                nameRect.sizeDelta = new Vector2(660f, blockHeight - 12f);

                var state = ToyKit.UIText("State", block, m_font, "", Mathf.Min(20f, fontSize), ToyKit.Ink);
                state.alignment = TextAlignmentOptions.Right;
                var stateRect = state.rectTransform;
                stateRect.anchorMin = stateRect.anchorMax = new Vector2(1f, 0.5f);
                stateRect.pivot = new Vector2(1f, 0.5f);
                stateRect.anchoredPosition = new Vector2(-32f, 0f);
                stateRect.sizeDelta = new Vector2(380f, blockHeight - 12f);
                m_titles.Add(name);
                m_states.Add(state);
            }

            m_index = DefaultIndex();
            Refresh();

            var hint = ToyKit.UIText("Hint", panel, m_font, "↑↓：えらぶ　A：けってい　B：もどる", 24f, new Color(0.35f, 0.25f, 0.18f));
            ToyKit.Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(900f, 40f));
        }

        int DefaultIndex()
        {
            for (int i = 0; i < m_chapters.Count; i++)
            {
                if (StoryProgress.IsUnlocked(m_chapters[i]) && !StoryProgress.IsCleared(m_chapters[i].id)) return i;
            }
            return 0;
        }

        void Update()
        {
            if (!IsOpen) return;
            if (MenuInput.Cancel()) { Close(); return; }
            if (m_chapters == null || m_chapters.Count == 0) return;

            if (MenuInput.Up()) Move(-1);
            else if (MenuInput.Down()) Move(1);
            else if (MenuInput.Submit()) StartSelected();
        }

        void Move(int delta)
        {
            m_index = (m_index + delta + m_chapters.Count) % m_chapters.Count;
            Refresh();
        }

        void Refresh()
        {
            for (int i = 0; i < m_chapters.Count; i++)
            {
                var chapter = m_chapters[i];
                bool cleared = StoryProgress.IsCleared(chapter.id);
                bool unlocked = StoryProgress.IsUnlocked(chapter);
                bool selected = i == m_index;

                m_blocks[i].color = !unlocked
                    ? new Color(0.62f, 0.60f, 0.56f)
                    : cleared ? new Color(0.72f, 0.88f, 0.68f) : new Color(0.78f, 0.90f, 0.99f);
                m_blocks[i].rectTransform.localScale = selected ? Vector3.one * 1.02f : Vector3.one;

                m_outlines[i].effectColor = selected ? new Color(0.95f, 0.62f, 0.15f) : new Color(0f, 0f, 0f, 0f);
                m_outlines[i].enabled = selected;

                m_titles[i].color = unlocked ? ToyKit.Ink : new Color(0.35f, 0.33f, 0.30f);
                m_states[i].color = unlocked ? ToyKit.Ink : new Color(0.40f, 0.38f, 0.35f);
                m_states[i].text = !unlocked
                    ? "ロック（前の章をクリアで解放）"
                    : cleared ? "クリア済み（Aで再プレイ）" : "あそべる";
            }
        }

        void StartSelected()
        {
            var chapter = m_chapters[m_index];
            if (!StoryProgress.IsUnlocked(chapter)) return;

            string id = chapter.id;
            m_starting = true;
            IsOpen = false;
            Destroy(gameObject);
            StoryLauncher.Start(id);
        }

        void Close()
        {
            IsOpen = false;
            if (m_menu != null)
            {
                s_menuSuppressFrame = Time.frameCount;
                m_menu.enabled = true;
            }
            Destroy(gameObject);
        }
    }
}
