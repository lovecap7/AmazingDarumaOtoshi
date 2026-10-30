using Nakahira;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nakagawa
{
    // 試合中のポーズ画面。コントローラーの Start(キーボードは Tab)で試合を止めて出す
    //  ・ポーズした人を色付きの「nP」で出す
    //  ・「再開」「ルール選択に戻る」「タイトルに戻る」を選ぶ。再開以外は「はい / いいえ」で確かめる
    //  ・操作できるのはポーズした人のコントローラーだけ(↑↓：えらぶ　A：決定　B / Start：再開(確認中は B で戻る))
    // 止める・解く・シーンを移るのは DarumaMatch(Pause / Resume / LeaveTo)が行う
    // MatchHud が試合と同じ GameObject に自動で付ける
    [DefaultExecutionOrder(100)]   // キャラクターが入力を読んだあとに解く(再開の A でジャンプしないように)
    public class PauseMenu : MonoBehaviour
    {
        enum Item { Resume, RuleSelect, Title }

        [Header("操作")]
        [SerializeField] Key m_keyboardPauseKey = Key.Tab;

        [Header("シーン")]
        [SerializeField] string m_selectSceneName = "SelectScene";
        [SerializeField] string m_titleSceneName = "TitleScene";

        [Header("見た目(1920x1080 基準)")]
        [SerializeField] int m_sortingOrder = 40;
        [SerializeField] Color m_dimColor = new Color(0.0f, 0.0f, 0.0f, 0.55f);
        [SerializeField] Color m_panelColor = new Color(0.08f, 0.06f, 0.05f, 0.92f);
        [SerializeField] Color m_itemColor = new Color(1.0f, 1.0f, 1.0f, 0.12f);
        [SerializeField] Color m_selectedColor = new Color(1.0f, 0.82f, 0.25f, 1.0f);
        [SerializeField] Color m_unknownPlayerColor = new Color(0.7f, 0.7f, 0.72f);

        static readonly string[] kItemNames = { "再開", "ルール選択に戻る", "タイトルに戻る" };
        static readonly string[] kConfirmNames = { "はい", "いいえ" };

        DarumaMatch m_match;
        RectTransform m_canvas, m_panel, m_confirm;
        CanvasGroup m_group;
        Image m_tag;
        TextMeshProUGUI m_tagText, m_confirmText;
        Image[] m_items, m_confirmItems;
        TextMeshProUGUI[] m_itemTexts, m_confirmTexts;
        TMP_FontAsset m_font;
        Material m_letterMat, m_popMat;

        // ポーズした機器(この機器だけで操作する)
        InputDevice m_device;
        int m_index;
        int m_confirmIndex;
        bool m_confirming;
        int m_openFrame;
        float m_openTime;

        private void Start()
        {
            m_match = GetComponent<DarumaMatch>();
            if (m_match == null) m_match = DarumaMatch.Current;
            m_font = PresentationSettings.DefaultFont;
            m_letterMat = ToyKit.LetterMaterial(m_font);
            m_popMat = ToyKit.PopMaterial(m_letterMat);
            Build();
            m_canvas.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (m_canvas != null) Destroy(m_canvas.gameObject);
        }

        private void Update()
        {
            if (m_match == null || m_canvas == null) return;
            if (!m_match.IsPaused)
            {
                if (m_canvas.gameObject.activeSelf) m_canvas.gameObject.SetActive(false);
                CheckPause();
                return;
            }
            if (!m_canvas.gameObject.activeSelf) Open();
            // 開いたフレームの Start で、そのまま閉じないようにする
            if (Time.frameCount != m_openFrame) UpdateMenu();
            Animate();
        }

        //---- 開く ----

        // ポーズボタンを押した人で止める
        private void CheckPause()
        {
            if (!m_match.CanPause) return;
            foreach (var g in Gamepad.all)
            {
                if (g.startButton.wasPressedThisFrame && TryPause(g)) return;
            }
            var k = Keyboard.current;
            if (k != null && m_keyboardPauseKey != Key.None && k[m_keyboardPauseKey].wasPressedThisFrame) TryPause(k);
        }

        // 試合に出ている人の機器だけがポーズできる
        private bool TryPause(InputDevice device)
        {
            var player = m_match.FindPlayer(device);
            if (player == null) return false;
            if (!m_match.Pause(player)) return false;
            m_device = device;
            return true;
        }

        private void Open()
        {
            // ほかのコンポーネントが止めたときは、その人の機器(分からなければ全部)で操作する
            var by = m_match.PausedBy;
            if (by == null || m_device == null || m_match.FindPlayer(m_device) != by) m_device = by != null ? by.Entry.Device : null;

            Color color = by != null ? by.Color : m_unknownPlayerColor;
            m_tag.color = color;
            m_tagText.text = by != null ? by.Label : "？";
            m_index = (int)Item.Resume;
            m_confirming = false;
            m_openFrame = Time.frameCount;
            m_openTime = Time.unscaledTime;
            m_canvas.gameObject.SetActive(true);
            MenuSounds.Decide();
        }

        //---- 操作 ----

        private void UpdateMenu()
        {
            if (m_confirming)
            {
                if (Pressed(Dir.Left) || Pressed(Dir.Right) || Pressed(Dir.Up) || Pressed(Dir.Down))
                {
                    m_confirmIndex = 1 - m_confirmIndex;
                    MenuSounds.Cursor();
                }
                if (PressedSubmit())
                {
                    if (m_confirmIndex == 0) Leave((Item)m_index);
                    else CloseConfirm();
                }
                else if (PressedCancel()) CloseConfirm();
                return;
            }

            if (Pressed(Dir.Up)) Move(-1);
            if (Pressed(Dir.Down)) Move(1);
            if (PressedStart() || PressedCancel()) { Resume(); return; }
            if (!PressedSubmit()) return;
            if (m_index == (int)Item.Resume) { Resume(); return; }
            // 再開以外は確かめる(最初は「いいえ」)
            m_confirming = true;
            m_confirmIndex = 1;
            m_confirmText.text = kItemNames[m_index] + "？\n<size=70%>いまの試合は終わります</size>";
            MenuSounds.Decide();
        }

        private void Move(int delta)
        {
            m_index = (m_index + delta + kItemNames.Length) % kItemNames.Length;
            MenuSounds.Cursor();
        }

        private void CloseConfirm()
        {
            m_confirming = false;
            MenuSounds.Cancel();
        }

        private void Resume()
        {
            MenuSounds.Cancel();
            m_match.Resume();
            m_canvas.gameObject.SetActive(false);
        }

        private void Leave(Item item)
        {
            MenuSounds.Decide();
            if (item == Item.RuleSelect)
            {
                MatchSelect.NextStartScreen = MatchSelect.StartScreen.Rule;
                m_match.LeaveTo(m_selectSceneName);
            }
            else
            {
                // タイトルから始め直すときは、前の試合の参加者を残さない(セレクトがモードセレクトから始まるように)
                MatchSetup.Clear();
                m_match.LeaveTo(m_titleSceneName);
            }
        }

        //---- 入力(ポーズした人の機器だけ。分からなければ全部) ----

        enum Dir { Up, Down, Left, Right }

        private bool Pressed(Dir dir)
        {
            foreach (var g in Gamepad.all)
            {
                if (!IsOperator(g)) continue;
                var pad = dir == Dir.Up ? g.dpad.up : dir == Dir.Down ? g.dpad.down : dir == Dir.Left ? g.dpad.left : g.dpad.right;
                var stick = dir == Dir.Up ? g.leftStick.up : dir == Dir.Down ? g.leftStick.down : dir == Dir.Left ? g.leftStick.left : g.leftStick.right;
                if (pad.wasPressedThisFrame || stick.wasPressedThisFrame) return true;
            }
            var k = Keyboard.current;
            if (k == null || !IsOperator(k)) return false;
            switch (dir)
            {
                case Dir.Up: return k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame;
                case Dir.Down: return k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame;
                case Dir.Left: return k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame;
                default: return k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame;
            }
        }

        private bool PressedSubmit()
        {
            foreach (var g in Gamepad.all) if (IsOperator(g) && g.buttonSouth.wasPressedThisFrame) return true;
            var k = Keyboard.current;
            return k != null && IsOperator(k) && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame);
        }

        private bool PressedCancel()
        {
            foreach (var g in Gamepad.all) if (IsOperator(g) && g.buttonEast.wasPressedThisFrame) return true;
            var k = Keyboard.current;
            return k != null && IsOperator(k) && (k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame);
        }

        private bool PressedStart()
        {
            foreach (var g in Gamepad.all) if (IsOperator(g) && g.startButton.wasPressedThisFrame) return true;
            var k = Keyboard.current;
            return k != null && IsOperator(k) && m_keyboardPauseKey != Key.None && k[m_keyboardPauseKey].wasPressedThisFrame;
        }

        private bool IsOperator(InputDevice device) => m_device == null || device == m_device;

        //---- 見た目 ----

        private void Animate()
        {
            float t = Time.unscaledTime - m_openTime;
            // 開いたときに少し弾んで出る
            float pop = Mathf.Clamp01(t / 0.2f);
            m_group.alpha = pop;
            m_panel.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1.0f, ToyKit.EaseOutBack(pop));

            float pulse = 1.0f + 0.04f * Mathf.Sin(t * 8.0f);
            for (int i = 0; i < m_items.Length; i++)
            {
                bool on = i == m_index;
                m_items[i].color = on ? m_selectedColor : m_itemColor;
                m_itemTexts[i].color = on ? ToyKit.Ink : Color.white;
                m_items[i].rectTransform.localScale = Vector3.one * (on && !m_confirming ? pulse : 1.0f);
            }

            m_confirm.gameObject.SetActive(m_confirming);
            for (int i = 0; i < m_confirmItems.Length; i++)
            {
                bool on = i == m_confirmIndex;
                m_confirmItems[i].color = on ? m_selectedColor : m_itemColor;
                m_confirmTexts[i].color = on ? ToyKit.Ink : Color.white;
                m_confirmItems[i].rectTransform.localScale = Vector3.one * (on ? pulse : 1.0f);
            }
        }

        // 画面を暗くして、中央にパネル(上に「nP」の名札と「ポーズ」、下に項目)。確認はパネルの上に重ねる
        private void Build()
        {
            m_canvas = ToyKit.CreateOverlayCanvas("PauseMenu", m_sortingOrder);
            m_canvas.SetParent(transform, false);
            m_group = m_canvas.gameObject.AddComponent<CanvasGroup>();
            m_group.blocksRaycasts = false;

            var dim = ToyKit.UIImage("Dim", m_canvas, m_dimColor);
            ToyKit.Stretch(dim);

            m_panel = ToyKit.UIRounded("Panel", m_canvas, m_panelColor, 28.0f);
            ToyKit.Anchor(m_panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620.0f, 520.0f));
            m_panel.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;

            // 名札(ポーズした人の色)
            var tag = ToyKit.UIRounded("Tag", m_panel, m_unknownPlayerColor, 18.0f);
            ToyKit.Anchor(tag, new Vector2(0.5f, 1.0f), new Vector2(0.0f, 0.0f), new Vector2(150.0f, 72.0f));
            tag.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
            m_tag = tag.GetComponent<Image>();
            m_tagText = Text("Label", tag, "", 48.0f, Color.white, m_popMat);
            ToyKit.Stretch(m_tagText.rectTransform);

            var title = Text("Title", m_panel, "ポーズ", 64.0f, Color.white, m_popMat);
            ToyKit.Anchor(title.rectTransform, new Vector2(0.5f, 1.0f), new Vector2(0.0f, -90.0f), new Vector2(560.0f, 80.0f));

            m_items = new Image[kItemNames.Length];
            m_itemTexts = new TextMeshProUGUI[kItemNames.Length];
            for (int i = 0; i < kItemNames.Length; i++)
            {
                var item = ToyKit.UIRounded($"Item{i}", m_panel, m_itemColor, 20.0f);
                ToyKit.Anchor(item, new Vector2(0.5f, 1.0f), new Vector2(0.0f, -190.0f - i * 92.0f), new Vector2(480.0f, 76.0f));
                m_items[i] = item.GetComponent<Image>();
                m_itemTexts[i] = Text("Text", item, kItemNames[i], 36.0f, Color.white, m_letterMat);
                ToyKit.Stretch(m_itemTexts[i].rectTransform);
            }

            var hint = Text("Hint", m_panel, "↑↓：えらぶ　A：決定　B / Start：再開", 20.0f, new Color(1.0f, 1.0f, 1.0f, 0.7f), m_letterMat);
            ToyKit.Anchor(hint.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(0.0f, 26.0f), new Vector2(580.0f, 30.0f));

            // 確認
            m_confirm = ToyKit.UIRounded("Confirm", m_panel, new Color(0.14f, 0.11f, 0.09f, 0.98f), 24.0f);
            ToyKit.Anchor(m_confirm, new Vector2(0.5f, 0.5f), new Vector2(0.0f, -30.0f), new Vector2(560.0f, 300.0f));
            m_confirm.gameObject.AddComponent<Outline>().effectColor = m_selectedColor;
            m_confirmText = Text("Message", m_confirm, "", 38.0f, Color.white, m_letterMat);
            ToyKit.Anchor(m_confirmText.rectTransform, new Vector2(0.5f, 1.0f), new Vector2(0.0f, -85.0f), new Vector2(520.0f, 130.0f));
            m_confirmItems = new Image[kConfirmNames.Length];
            m_confirmTexts = new TextMeshProUGUI[kConfirmNames.Length];
            for (int i = 0; i < kConfirmNames.Length; i++)
            {
                var item = ToyKit.UIRounded($"Choice{i}", m_confirm, m_itemColor, 20.0f);
                ToyKit.Anchor(item, new Vector2(0.5f, 0.0f), new Vector2((i - 0.5f) * 220.0f, 60.0f), new Vector2(190.0f, 70.0f));
                m_confirmItems[i] = item.GetComponent<Image>();
                m_confirmTexts[i] = Text("Text", item, kConfirmNames[i], 34.0f, Color.white, m_letterMat);
                ToyKit.Stretch(m_confirmTexts[i].rectTransform);
            }
            m_confirm.gameObject.SetActive(false);
        }

        private TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color, Material mat)
        {
            var t = ToyKit.UIText(name, parent, m_font, text, size, color);
            if (mat != null) t.fontSharedMaterial = mat;
            return t;
        }
    }
}
