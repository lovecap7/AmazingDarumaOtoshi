using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace Yurube.Story
{
    // 章選択。メインメニューの「ものがたり」から開く。
    //  ・Resources/Story/*.json を order の小さい順に、積み木の段として上から積む（序章が一番上、最終章が一番下）
    //  ・未解放（鍵付き・伏せ字）/ 解放済み（赤積み木）/ クリア済み（木目＋★クリア済み）で見た目を変える
    //  ・A：けってい / B：もどる
    // 章を増やすときは JSON を足すだけで、この画面の並びも自動で増える。
    public class StoryChapterSelect : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        // 章選択を閉じたフレームだけ、メニュー側の入力を1フレーム止める（同じB/Aで二重に反応しないように）
        public static bool SuppressMenuInput => Time.frameCount == s_menuSuppressFrame;
        static int s_menuSuppressFrame = -1;

        [Header("解放演出")]
        [Tooltip("次の章が初めて解放されるときの演出の長さ（秒）")]
        [SerializeField, Min(0.2f)] float m_unlockAnimSeconds = 1.4f;
        [Tooltip("解放演出のSE（あとから差し替え可能。未設定なら鳴らさない）")]
        [SerializeField] AudioClip m_unlockSe;

        // 見た目（既存のメニューと同じ「赤と木目」の積み木テーマ。あとから差し替えやすいよう定数化）
        static readonly Color WoodFrameColor = new Color(0.40f, 0.25f, 0.14f, 1f);
        static readonly Color PanelBgColor = new Color(0.96f, 0.91f, 0.82f, 0.99f);
        static readonly Color UnlockedBlockColor = new Color(0.84f, 0.25f, 0.20f, 1f);   // 赤い積み木
        static readonly Color ClearedBlockColor = new Color(0.76f, 0.56f, 0.36f, 1f);    // 木目の積み木
        static readonly Color LockedBlockColor = new Color(0.36f, 0.33f, 0.30f, 1f);     // ロック中の暗い木石
        static readonly Color SelectedOutlineColor = new Color(1.0f, 0.84f, 0.24f, 1f);  // 選択中の金の縁取り

        MonoBehaviour m_menu;
        bool m_starting;
        bool m_animating;
        string m_animatingUnlockId;
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
        readonly List<RectTransform> m_lockBadges = new List<RectTransform>();
        readonly List<Vector2> m_basePositions = new List<Vector2>();

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
            var settings = StorySettings.Instance;
            if (settings != null) m_unlockAnimSeconds = settings.UnlockAnimSeconds;

            var root = ToyKit.CreateOverlayCanvas("StoryChapterSelect", 100);
            m_canvas = root.gameObject;

            var dim = ToyKit.UIImage("Dim", root, new Color(0.05f, 0.03f, 0.02f, 0.82f));
            ToyKit.Stretch(dim);

            // 木目調の外枠＋温かい和紙色の内側パネル
            var frame = ToyKit.UIRounded("Frame", root, WoodFrameColor, 32f);
            ToyKit.Anchor(frame, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1332f, 892f));
            frame.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;

            var panel = ToyKit.UIRounded("Panel", root, PanelBgColor, 26f);
            ToyKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300f, 860f));

            // 赤い見出し帯
            var headerBand = ToyKit.UIRounded("HeaderBand", panel, UnlockedBlockColor, 18f);
            ToyKit.Anchor(headerBand, new Vector2(0.5f, 1f), new Vector2(0f, -68f), new Vector2(560f, 76f));
            var title = ToyKit.UIText("Title", headerBand, m_font, "ものがたり", 52f, Color.white);
            title.fontStyle = FontStyles.Bold;
            ToyKit.Stretch(title.rectTransform);

            var sub = ToyKit.UIText("Sub", panel, m_font, "章をえらんでください", 28f, new Color(0.35f, 0.22f, 0.14f));
            ToyKit.Anchor(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -135f), new Vector2(800f, 40f));

            m_chapters = StoryData.LoadAll();
            if (m_chapters.Count == 0)
            {
                var empty = ToyKit.UIText("Empty", panel, m_font, "章がありません", 36f, ToyKit.Ink);
                ToyKit.Anchor(empty.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 60f));
            }

            float bottom = -362f;
            float top = 230f;
            int futureCount = Mathf.Max(0, 4 - m_chapters.Count);
            int totalCount = m_chapters.Count + futureCount;
            int count = Mathf.Max(1, totalCount);
            float gap = 12f;
            float blockHeight = Mathf.Clamp((top - bottom - gap * (count - 1)) / count, 46f, 100f);
            float spacing = blockHeight + gap;
            float stackHeight = blockHeight * count + gap * (count - 1);
            float start = bottom + (top - bottom - stackHeight) * 0.5f + blockHeight * 0.5f;

            for (int i = 0; i < totalCount; i++)
            {
                var block = ToyKit.UIRounded("Chapter" + i, panel, Color.white, 18f);
                // 並びは上が序章（若い章）、下が最終章。i が大きいほど下に置く
                var basePos = new Vector2(0f, start + (totalCount - 1 - i) * spacing);
                m_basePositions.Add(basePos);
                ToyKit.Anchor(block, new Vector2(0.5f, 0.5f), basePos, new Vector2(1080f, blockHeight));
                var image = block.GetComponent<Image>();
                var outline = block.gameObject.AddComponent<Outline>();
                outline.effectDistance = new Vector2(5f, -5f);
                m_blocks.Add(image);
                m_outlines.Add(outline);

                // 鍵アイコン代わりのロック目印（未解放時のみ表示）
                var lockBadge = ToyKit.UIRounded("LockBadge", block, new Color(0.18f, 0.14f, 0.12f, 0.95f), 10f);
                lockBadge.anchorMin = lockBadge.anchorMax = new Vector2(0f, 0.5f);
                lockBadge.pivot = new Vector2(0f, 0.5f);
                lockBadge.anchoredPosition = new Vector2(24f, 0f);
                lockBadge.sizeDelta = new Vector2(72f, Mathf.Min(48f, blockHeight - 18f));
                var lockText = ToyKit.UIText("LockText", lockBadge, m_font, "鍵", 24f, new Color(1f, 0.85f, 0.35f));
                lockText.fontStyle = FontStyles.Bold;
                ToyKit.Stretch(lockText.rectTransform);
                m_lockBadges.Add(lockBadge);

                float fontSize = Mathf.Clamp(blockHeight * 0.50f, 24f, 36f);
                var name = ToyKit.UIText("Name", block, m_font, "", fontSize, Color.white);
                name.alignment = TextAlignmentOptions.Left;
                name.fontStyle = FontStyles.Bold;
                name.enableAutoSizing = true;
                name.fontSizeMin = 20f;
                name.fontSizeMax = fontSize;
                name.outlineWidth = 0f;
                name.outlineColor = Color.clear;
                var nameRect = name.rectTransform;
                nameRect.anchorMin = nameRect.anchorMax = new Vector2(0f, 0.5f);
                nameRect.pivot = new Vector2(0f, 0.5f);
                nameRect.anchoredPosition = new Vector2(36f, 0f);
                nameRect.sizeDelta = new Vector2(640f, blockHeight - 10f);

                float stateSize = Mathf.Clamp(fontSize * 0.74f, 18f, 26f);
                var state = ToyKit.UIText("State", block, m_font, "", stateSize, Color.white);
                state.alignment = TextAlignmentOptions.Right;
                state.fontStyle = FontStyles.Bold;
                state.outlineWidth = 0f;
                state.outlineColor = Color.clear;
                state.enableAutoSizing = true;
                state.fontSizeMin = 16f;
                state.fontSizeMax = stateSize;
                var stateRect = state.rectTransform;
                stateRect.anchorMin = stateRect.anchorMax = new Vector2(1f, 0.5f);
                stateRect.pivot = new Vector2(1f, 0.5f);
                stateRect.anchoredPosition = new Vector2(-28f, 0f);
                stateRect.sizeDelta = new Vector2(370f, blockHeight - 12f);
                m_titles.Add(name);
                m_states.Add(state);
            }

            var hint = ToyKit.UIText("Hint", panel, m_font, "↑↓：えらぶ　A：けってい　B：もどる", 24f, new Color(0.35f, 0.22f, 0.14f));
            ToyKit.Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(900f, 38f));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var devHint = ToyKit.UIText("DevHint", panel, m_font, "[開発用] F8：全章解放　F9：セーブ初期化", 18f, new Color(0.45f, 0.35f, 0.28f));
            ToyKit.Anchor(devHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(900f, 24f));
#endif

            // 今回のクリアで初めて解放された次章があれば、解放演出を再生する
            string pendingUnlock = StorySession.ConsumePendingUnlockChapter();
            int unlockIndex = FindChapterIndex(pendingUnlock);
            if (unlockIndex >= 0 && !StoryProgress.HasSeenUnlockAnim(pendingUnlock))
            {
                m_animatingUnlockId = pendingUnlock;
                m_index = Mathf.Max(0, unlockIndex - 1);
                Refresh();
                StartCoroutine(PlayUnlockAnimation(unlockIndex, pendingUnlock));
            }
            else
            {
                m_index = DefaultIndex();
                Refresh();
            }
        }

        int FindChapterIndex(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId) || m_chapters == null) return -1;
            for (int i = 0; i < m_chapters.Count; i++)
            {
                if (m_chapters[i] != null && m_chapters[i].id == chapterId) return i;
            }
            return -1;
        }

        // カーソルの初期位置：解放済みで最新の章
        int DefaultIndex()
        {
            int lastUnlocked = 0;
            if (m_chapters == null) return 0;
            for (int i = 0; i < m_chapters.Count; i++)
            {
                if (StoryProgress.IsUnlocked(m_chapters[i])) lastUnlocked = i;
            }
            return lastUnlocked;
        }

        void Update()
        {
            if (!IsOpen || m_animating) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f8Key.wasPressedThisFrame)
                {
                    StoryProgress.UnlockAll();
                    m_index = DefaultIndex();
                    Refresh();
                    return;
                }
                if (kb.f9Key.wasPressedThisFrame)
                {
                    StoryProgress.ResetAll();
                    m_index = DefaultIndex();
                    Refresh();
                    return;
                }
            }
#endif

            if (MenuInput.Cancel()) { Close(); return; }
            if (m_chapters == null || m_chapters.Count == 0) return;

            // 上の段（序章側）へは index を下げる、下の段（最終章側）へは上げる
            if (MenuInput.Up()) Move(-1);
            else if (MenuInput.Down()) Move(1);
            else if (MenuInput.Submit()) StartSelected();
        }

        void Move(int delta)
        {
            if (m_chapters.Count == 0) return;
            m_index = (m_index + delta + m_chapters.Count) % m_chapters.Count;
            Refresh();
        }

        // 段に出す名前。未実装の章は「？？？」、未解放の章は伏せ字にする
        string TitleFor(int index, bool forceLocked = false)
        {
            if (m_chapters == null) return "";
            int totalCount = Mathf.Max(4, m_chapters.Count);
            if (index >= m_chapters.Count)
                return index == totalCount - 1 ? "最終章「？？？」" : $"第{index}章「？？？」";
            var chapter = m_chapters[index];
            if (chapter == null) return "";
            if (forceLocked || !StoryProgress.IsUnlocked(chapter))
            {
                if (!string.IsNullOrEmpty(chapter.hiddenTitle)) return chapter.hiddenTitle;
                if (chapter.order <= 0) return "序章「？？？」";
                return index == totalCount - 1 ? "最終章「？？？」" : $"第{chapter.order}章「？？？」";
            }
            return chapter.title;
        }

        void Refresh()
        {
            for (int i = 0; i < m_blocks.Count; i++)
            {
                bool placeholder = i >= m_chapters.Count;
                bool forceLocked = !placeholder && m_chapters[i] != null && m_chapters[i].id == m_animatingUnlockId;
                bool cleared = !placeholder && !forceLocked && StoryProgress.IsCleared(m_chapters[i].id);
                bool unlocked = !placeholder && !forceLocked && StoryProgress.IsUnlocked(m_chapters[i]);
                bool selected = !placeholder && i == m_index;

                m_titles[i].text = TitleFor(i, forceLocked);

                m_blocks[i].color = placeholder || !unlocked
                    ? LockedBlockColor
                    : cleared ? ClearedBlockColor : UnlockedBlockColor;
                m_blocks[i].rectTransform.localScale = selected ? Vector3.one * 1.03f : Vector3.one;

                m_outlines[i].effectColor = selected ? SelectedOutlineColor : new Color(0.18f, 0.10f, 0.05f, 0.85f);
                m_outlines[i].effectDistance = selected ? new Vector2(5f, -5f) : new Vector2(2f, -2f);
                m_outlines[i].enabled = true;

                bool showLock = placeholder || !unlocked;
                if (i < m_lockBadges.Count && m_lockBadges[i] != null)
                {
                    m_lockBadges[i].gameObject.SetActive(showLock);
                    m_lockBadges[i].localScale = Vector3.one;
                }
                m_titles[i].rectTransform.anchoredPosition = new Vector2(showLock ? 112f : 32f, 0f);
                m_titles[i].rectTransform.sizeDelta = new Vector2(showLock ? 560f : 640f, m_titles[i].rectTransform.sizeDelta.y);

                m_titles[i].color = unlocked ? Color.white : new Color(0.78f, 0.75f, 0.72f);
                m_states[i].color = unlocked ? new Color(1f, 0.96f, 0.85f) : new Color(0.68f, 0.65f, 0.62f);
                m_states[i].text = placeholder
                    ? PlaceholderState(i)
                    : !unlocked ? "前の章をクリアで解放"
                    : cleared ? "★ クリア済み（Aで再プレイ）" : "あそべる！";
            }
        }

        //========================================
        // 次章の解放演出（鍵外れ＋積み木が積まれる動き）
        //========================================

        IEnumerator PlayUnlockAnimation(int unlockIndex, string chapterId)
        {
            m_animating = true;
            float total = Mathf.Max(0.2f, m_unlockAnimSeconds);
            float lockDuration = total * 0.32f;
            float dropDuration = total * 0.45f;
            float holdDuration = Mathf.Max(0.1f, total - lockDuration - dropDuration);

            var blockRect = m_blocks[unlockIndex].rectTransform;
            var basePos = m_basePositions[unlockIndex];
            var lockBadge = unlockIndex < m_lockBadges.Count ? m_lockBadges[unlockIndex] : null;

            // 1. 鍵が震えて外れる
            float t = 0f;
            while (t < lockDuration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / lockDuration);
                if (lockBadge != null)
                {
                    float shake = Mathf.Sin(u * Mathf.PI * 8f) * 6f * (1f - u);
                    lockBadge.anchoredPosition = new Vector2(24f + shake, 0f);
                    float s = u < 0.65f ? 1f + u * 0.25f : Mathf.Lerp(1.16f, 0f, (u - 0.65f) / 0.35f);
                    lockBadge.localScale = Vector3.one * s;
                }
                yield return null;
            }

            // 2. 解放状態へ切り替え、上から積み木が落ちて着地バウンド
            m_animatingUnlockId = null;
            m_index = unlockIndex;
            Refresh();
            PlayUnlockSe();
            OnUnlockVisualEffect(unlockIndex);

            t = 0f;
            while (t < dropDuration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / dropDuration);
                // 最初の60%で上から落下、残り40%で着地バウンド（縦に潰れて戻る）
                if (u < 0.6f)
                {
                    float fall = u / 0.6f;
                    float y = Mathf.Lerp(70f, 0f, fall * fall);
                    blockRect.anchoredPosition = basePos + new Vector2(0f, y);
                    blockRect.localScale = Vector3.one * 1.03f;
                }
                else
                {
                    blockRect.anchoredPosition = basePos;
                    float b = (u - 0.6f) / 0.4f;
                    float squash = Mathf.Sin(b * Mathf.PI) * 0.16f;
                    blockRect.localScale = new Vector3(1.03f + squash, 1.03f - squash * 0.8f, 1f);
                }
                yield return null;
            }

            blockRect.anchoredPosition = basePos;
            blockRect.localScale = Vector3.one * 1.03f;

            // 3. タイトルを見せながら少し間を置いて、操作を受け付ける
            yield return new WaitForSecondsRealtime(holdDuration);
            StoryProgress.MarkUnlockAnimSeen(chapterId);
            m_animating = false;
        }

        // あとからSE素材を差し替えるための呼び出し箇所
        void PlayUnlockSe()
        {
            if (m_unlockSe != null) MenuSounds.Play(m_unlockSe, 0.85f);
        }

        // あとからパーティクルや演出素材を差し替えるための呼び出し箇所
        void OnUnlockVisualEffect(int chapterIndex)
        {
        }

        // 実装待ちの最終章の表示。直前の章をクリアしたかで文言を変える
        string PlaceholderState(int index)
        {
            if (m_chapters == null || m_chapters.Count == 0) return "準備中";
            if (index != m_chapters.Count) return "準備中";
            var last = m_chapters[m_chapters.Count - 1];
            if (last == null) return "準備中";
            if (StoryProgress.IsCleared(last.id)) return "準備中";
            string label = last.order <= 0 ? "序章" : "第" + last.order + "章";
            return label + "クリアで解放";
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
