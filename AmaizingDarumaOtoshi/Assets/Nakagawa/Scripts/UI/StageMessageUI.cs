using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nakagawa
{
    // ステージのギミックが発動したときなどの文章を、画面の上の方に大きく出す(Stage19・Stage20 など)
    //  ・StageMessageUI.Show で出す。最初に呼ばれたときに画面全体の UI(重ねて表示する Canvas)を作る
    //  ・ポンと大きく出てから少し縮み、一定時間で上へ浮かびながら消える
    //  ・同時に複数出たときは、新しいものを上にして縦に並べる(古いものから消える)
    // 文字はゲームの既定のフォント(PresentationSettings.DefaultFont)に縁取りを付けて使う
    public class StageMessageUI : MonoBehaviour
    {
        const int kSortingOrder = 18;
        const int kMaxMessages = 4;
        const float kTopOffset = 150.0f;    // 画面の上端から1つ目の上端まで(UIの単位。1920×1080基準。上の時間表示の下)
        const float kGap = 14.0f;           // 並べたときの間
        const float kBaseFontSize = 56.0f;
        const float kAppearTime = 0.18f;
        const float kFadeTime = 0.35f;

        class Message
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public float Time, Duration;
            public float Y;
            public float Height;
        }

        static StageMessageUI s_instance;
        RectTransform m_canvas;
        TMP_FontAsset m_font;
        Material m_letterMat;
        readonly List<Message> m_messages = new List<Message>();

        // 文章を画面に出す。size: 文字の大きさの倍率、duration: 出しておく時間(秒)
        public static void Show(string text, Color color, float size = 1.0f, float duration = 2.0f)
        {
            if (string.IsNullOrEmpty(text)) return;
            // ネット対戦用(suehara): クライアントで二重に出さない / ホストで出した文章をクライアントにも送る(GimmickNet)
            if (GimmickNet.Muted) return;
            if (GimmickNet.Relaying) GimmickNet.MessageSent?.Invoke(text, color, size, duration);
            if (s_instance == null)
            {
                var go = new GameObject("StageMessageUI");
                s_instance = go.AddComponent<StageMessageUI>();
            }
            s_instance.Add(text, color, size, duration);
        }

        private void Awake()
        {
            m_canvas = ToyKit.CreateOverlayCanvas("StageMessageCanvas", kSortingOrder);
            m_canvas.SetParent(transform, false);
            m_font = PresentationSettings.DefaultFont;
            if (m_font != null) m_letterMat = ToyKit.LetterMaterial(m_font);
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            if (m_letterMat != null) Destroy(m_letterMat);
        }

        void Add(string text, Color color, float size, float duration)
        {
            // 多すぎたら一番古いものを消す
            while (m_messages.Count >= kMaxMessages)
            {
                Destroy(m_messages[m_messages.Count - 1].Root.gameObject);
                m_messages.RemoveAt(m_messages.Count - 1);
            }

            var root = new GameObject("Message", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(m_canvas, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1.0f);
            root.pivot = new Vector2(0.5f, 1.0f);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            float fontSize = kBaseFontSize * size;
            var plate = ToyKit.UIRounded("Plate", root, new Color(0.05f, 0.04f, 0.08f, 0.72f), 30.0f);
            var label = ToyKit.UIText("Text", root, m_font, text, fontSize, Color.white);
            if (m_letterMat != null) label.fontSharedMaterial = m_letterMat;
            label.color = color;
            Vector2 pref = label.GetPreferredValues(text);
            Vector2 boxSize = new Vector2(pref.x + 80.0f, Mathf.Max(fontSize * 1.35f, pref.y + 24.0f));
            root.sizeDelta = boxSize;
            ToyKit.Stretch(plate);
            ToyKit.Stretch(label.rectTransform);
            // 色の帯(左端)
            var bar = ToyKit.UIRounded("Bar", root, color, 10.0f);
            bar.anchorMin = new Vector2(0.0f, 0.15f);
            bar.anchorMax = new Vector2(0.0f, 0.85f);
            bar.sizeDelta = new Vector2(14.0f, 0.0f);
            bar.anchoredPosition = new Vector2(22.0f, 0.0f);

            var m = new Message { Root = root, Group = group, Duration = Mathf.Max(0.5f, duration), Y = -kTopOffset + 30.0f, Height = boxSize.y };
            m_messages.Insert(0, m);
            Layout(true);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = m_messages.Count - 1; i >= 0; i--)
            {
                var m = m_messages[i];
                m.Time += dt;
                if (m.Time >= m.Duration)
                {
                    Destroy(m.Root.gameObject);
                    m_messages.RemoveAt(i);
                }
            }
            Layout(false);
        }

        void Layout(bool immediate)
        {
            float k = immediate ? 0.0f : 1.0f - Mathf.Exp(-14.0f * Time.unscaledDeltaTime);
            float y = -kTopOffset;
            for (int i = 0; i < m_messages.Count; i++)
            {
                var m = m_messages[i];
                // 新しいものから順に、上から高さの分ずつ下へ並べる
                float targetY = y;
                y -= m.Height + kGap;
                // 出たばかりのものは少し上から降りてくる
                if (m.Time <= 0.0f && immediate && i == 0) m.Y = targetY + 30.0f;
                else m.Y = Mathf.Lerp(m.Y, targetY, k);

                float appear = Mathf.Clamp01(m.Time / kAppearTime);
                float fade = Mathf.Clamp01((m.Duration - m.Time) / kFadeTime);
                // ポンと大きく出てから少し縮む
                float pop = appear < 1.0f ? Mathf.Lerp(1.35f, 1.0f, appear * appear) : 1.0f;
                m.Root.localScale = Vector3.one * pop;
                m.Group.alpha = Mathf.Min(appear * 1.5f, fade);
                float rise = (1.0f - fade) * 40.0f;
                m.Root.anchoredPosition = new Vector2(0.0f, m.Y + rise);
            }
        }
    }
}
