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
        // 章選択のUIは、このコンポーネントとは別のCanvasに作られる。
        // Destroy(gameObject) だけではCanvasが残ってしまうので、必ず一緒に消す
        GameObject m_canvas;
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
            if (m_canvas != null)
            {
                Destroy(m_canvas);
                m_canvas = null;
            }
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
            m_canvas = root.gameObject;

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
            // タイトル・説明の下から、下の余白までを使って章の段を大きく見せる
            float top = 230f;
            // まだ実装していない章も「？？？」のロック枠として見せ、物語の全体像が分かるようにする
            int futureCount = Mathf.Max(0, 4 - m_chapters.Count);
            int totalCount = m_chapters.Count + futureCount;
            int count = Mathf.Max(1, totalCount);
            float gap = 10f;
            float blockHeight = Mathf.Clamp((top - bottom - gap * (count - 1)) / count, 46f, 100f);
            float spacing = blockHeight + gap;
            float stackHeight = blockHeight * count + gap * (count - 1);
            float start = bottom + (top - bottom - stackHeight) * 0.5f + blockHeight * 0.5f;

            for (int i = 0; i < totalCount; i++)
            {
                bool placeholder = i >= m_chapters.Count;
                string chapterTitle = placeholder
                    ? (i == totalCount - 1 ? "最終章「？？？」" : $"第{i}章「？？？」")
                    : m_chapters[i].title;
                var block = ToyKit.UIRounded("Chapter" + i, panel, Color.white, 20f);
                ToyKit.Anchor(block, new Vector2(0.5f, 0.5f), new Vector2(0f, start + i * spacing),
                    new Vector2(1080f, blockHeight));
                var image = block.GetComponent<Image>();
                var outline = block.gameObject.AddComponent<Outline>();
                outline.effectDistance = new Vector2(4f, -4f);
                m_blocks.Add(image);
                m_outlines.Add(outline);

                float fontSize = Mathf.Clamp(blockHeight * 0.52f, 24f, 38f);
                var name = ToyKit.UIText("Name", block, m_font, chapterTitle, fontSize, ToyKit.Ink);
                name.alignment = TextAlignmentOptions.Left;
                name.fontStyle = FontStyles.Bold;
                name.enableAutoSizing = true;
                name.fontSizeMin = 22f;
                name.fontSizeMax = fontSize;
                // 白い縁取りが付いていると読みにくいので消す
                name.outlineWidth = 0f;
                name.outlineColor = Color.clear;
                var nameRect = name.rectTransform;
                nameRect.anchorMin = nameRect.anchorMax = new Vector2(0f, 0.5f);
                nameRect.pivot = new Vector2(0f, 0.5f);
                nameRect.anchoredPosition = new Vector2(36f, 0f);
                nameRect.sizeDelta = new Vector2(680f, blockHeight - 10f);

                float stateSize = Mathf.Clamp(fontSize * 0.78f, 20f, 28f);
                var state = ToyKit.UIText("State", block, m_font, "", stateSize, ToyKit.Ink);
                state.alignment = TextAlignmentOptions.Right;
                state.fontStyle = FontStyles.Bold;
                state.outlineWidth = 0f;
                state.outlineColor = Color.clear;
                state.enableAutoSizing = true;
                state.fontSizeMin = 18f;
                state.fontSizeMax = stateSize;
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
            int lastUnlocked = 0;
            for (int i = 0; i < m_chapters.Count; i++)
            {
                if (!StoryProgress.IsUnlocked(m_chapters[i])) continue;
                lastUnlocked = i;
                if (!StoryProgress.IsCleared(m_chapters[i].id)) return i;
            }
            // 全部クリア済みなら、最後に遊べる章（第2章）を選んでおく
            return lastUnlocked;
        }

        void Update()
        {
            if (!IsOpen) return;
            if (MenuInput.Cancel()) { Close(); return; }
            if (m_chapters == null || m_chapters.Count == 0) return;

            if (MenuInput.Up()) Move(1);
            else if (MenuInput.Down()) Move(-1);
            else if (MenuInput.Submit()) StartSelected();
        }

        void Move(int delta)
        {
            if (m_chapters.Count == 0) return;
            m_index = (m_index + delta + m_chapters.Count) % m_chapters.Count;
            Refresh();
        }

        void Refresh()
        {
            for (int i = 0; i < m_blocks.Count; i++)
            {
                bool placeholder = i >= m_chapters.Count;
                bool cleared = !placeholder && StoryProgress.IsCleared(m_chapters[i].id);
                bool unlocked = !placeholder && StoryProgress.IsUnlocked(m_chapters[i]);
                bool selected = !placeholder && i == m_index;

                m_blocks[i].color = placeholder
                    ? new Color(0.82f, 0.80f, 0.76f)
                    : !unlocked ? new Color(0.72f, 0.70f, 0.66f)
                    : cleared ? new Color(0.72f, 0.88f, 0.68f) : new Color(0.78f, 0.90f, 0.99f);
                m_blocks[i].rectTransform.localScale = selected ? Vector3.one * 1.02f : Vector3.one;

                m_outlines[i].effectColor = selected ? new Color(0.95f, 0.62f, 0.15f) : new Color(0f, 0f, 0f, 0f);
                m_outlines[i].enabled = selected;

                // 灰色の台の上でも読めるよう、ロック中は濃い文字にする
                m_titles[i].color = unlocked ? ToyKit.Ink : new Color(0.16f, 0.14f, 0.12f);
                m_states[i].color = unlocked ? ToyKit.Ink : new Color(0.24f, 0.22f, 0.19f);
                m_states[i].text = placeholder
                    ? PlaceholderState(i)
                    : !unlocked ? "ロック（前の章をクリアで解放）"
                    : cleared ? "クリア済み（Aで再プレイ）" : "あそべる";
            }
        }

        // 実装待ちの最終章の表示。直前の章をクリアしたかで文言を変える
        string PlaceholderState(int index)
        {
            if (m_chapters == null || m_chapters.Count == 0) return "ロック（準備中）";
            if (index != m_chapters.Count) return "ロック（準備中）";
            var last = m_chapters[m_chapters.Count - 1];
            if (last == null) return "ロック（準備中）";
            if (StoryProgress.IsCleared(last.id)) return "ロック（準備中）";
            string label = last.order <= 0 ? "序章" : "第" + last.order + "章";
            return "ロック（" + label + "クリアで解放）";
        }

        void StartSelected()
        {
            if (m_chapters.Count == 0 || m_index < 0 || m_index >= m_chapters.Count) return;
            var chapter = m_chapters[m_index];
            if (!StoryProgress.IsUnlocked(chapter)) return;

            string id = chapter.id;
            m_starting = true;
            IsOpen = false;
            if (m_canvas != null)
            {
                m_canvas.SetActive(false);
                Destroy(m_canvas);
                m_canvas = null;
            }
            Destroy(gameObject);
            StoryLauncher.Start(id);
        }

        void Close()
        {
            IsOpen = false;
            // ウィンドウ（別Canvas）を先に消す。これを忘れると、Bで閉じても
            // 見た目が残ったまま裏のメニューだけ操作できてしまう
            if (m_canvas != null)
            {
                m_canvas.SetActive(false);
                Destroy(m_canvas);
                m_canvas = null;
            }
            if (m_menu != null)
            {
                s_menuSuppressFrame = Time.frameCount;
                m_menu.enabled = true;
            }
            Destroy(gameObject);
        }
    }
}
