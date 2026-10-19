using Nakahira;
using TMPro;
using UnityEngine;

namespace Nakagawa
{
    // 試合開始のカウントダウン(3, 2, 1, GO!)の表示。DarumaMatch のカウントダウン(IsCountingDown / CountdownLeft)を読むだけ
    //  ・数字は右から勢いよく入ってきて、中央付近でゆっくり流れ(止まらない)、左へ勢いよく抜けていく。速いときは残像が出る
    //  ・カウントダウンが終わったら「GO!」を中央に大きく出して消す
    // MatchHud が試合と同じ GameObject に自動で付ける(カウントダウンの秒数・有無は DarumaMatch で決める)
    public class StartCountdownView : MonoBehaviour
    {
        [Header("見た目(1920x1080 基準)")]
        [SerializeField] float m_fontSize = 320.0f;
        [SerializeField] int m_sortingOrder = 30;
        // 3, 2, 1 の色(足りなければ繰り返す)
        [SerializeField] Color[] m_colors =
        {
            new Color(0.95f, 0.30f, 0.28f), new Color(0.25f, 0.60f, 0.98f), new Color(1.0f, 0.80f, 0.18f),
        };
        [SerializeField] string m_goText = "GO!";
        [SerializeField] Color m_goColor = new Color(0.40f, 0.90f, 0.40f);
        [SerializeField, Min(0.1f)] float m_goDuration = 0.9f;

        [Header("動き(1つの数字の時間を 0〜1 として)")]
        // 入ってくる位置 / 中央付近で流れる範囲 / 抜けていく位置(中心からの横の位置)
        [SerializeField] float m_enterX = 1350.0f;
        [SerializeField] float m_driftX = 90.0f;
        [SerializeField] float m_exitX = -1350.0f;
        // 入り終わる時間 / 抜け始める時間
        [SerializeField, Range(0.05f, 0.5f)] float m_enterEnd = 0.22f;
        [SerializeField, Range(0.5f, 0.95f)] float m_exitStart = 0.78f;
        // 残像の数(0 で出さない)
        [SerializeField, Range(0, 4)] int m_trails = 3;

        DarumaMatch m_match;
        RectTransform m_canvas;
        TextMeshProUGUI m_text;
        TextMeshProUGUI[] m_trailTexts;
        bool m_wasCounting;
        float m_goTime = -1.0f;

        private void Start()
        {
            m_match = GetComponent<DarumaMatch>();
            if (m_match == null) m_match = DarumaMatch.Current;
            var font = PresentationSettings.DefaultFont;
            var mat = ToyKit.PopMaterial(ToyKit.LetterMaterial(font));
            m_canvas = ToyKit.CreateOverlayCanvas("StartCountdown", m_sortingOrder);
            m_canvas.SetParent(transform, false);

            m_trailTexts = new TextMeshProUGUI[m_trails];
            for (int i = m_trails - 1; i >= 0; i--) m_trailTexts[i] = CreateText($"Trail{i}", font, mat);
            m_text = CreateText("Count", font, mat);
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (m_canvas != null) Destroy(m_canvas.gameObject);
        }

        private TextMeshProUGUI CreateText(string name, TMP_FontAsset font, Material mat)
        {
            var t = ToyKit.UIText(name, m_canvas, font, "", m_fontSize, Color.white);
            t.fontSharedMaterial = mat;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            ToyKit.Anchor(t.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900.0f, 500.0f));
            return t;
        }

        private void LateUpdate()
        {
            if (m_match == null || m_text == null) return;

            bool counting = m_match.IsCountingDown;
            if (m_wasCounting && !counting && !string.IsNullOrEmpty(m_goText)) m_goTime = 0.0f;
            m_wasCounting = counting;

            if (counting) ShowCount(m_match.CountdownLeft);
            else if (m_goTime >= 0.0f) ShowGo();
            else SetVisible(false);
        }

        private void ShowCount(float left)
        {
            int n = Mathf.CeilToInt(left);
            if (n <= 0) { SetVisible(false); return; }
            // この数字の中での進み具合(0: 出始め → 1: 抜けきり)
            float t = Mathf.Clamp01(1.0f - (left - (n - 1)));
            Color color = m_colors != null && m_colors.Length > 0 ? m_colors[(n - 1) % m_colors.Length] : Color.white;

            SetVisible(true);
            m_text.text = n.ToString();
            m_text.color = color;
            float x = XAt(t);
            float speed = Mathf.Abs(XAt(Mathf.Min(1.0f, t + 0.02f)) - x);
            Place(m_text, x, color, 1.0f, speed);

            // 速く動いている間だけ、来た方向(右)に残像を出す
            float trailAlpha = Mathf.Clamp01(speed / 60.0f);
            for (int i = 0; i < m_trailTexts.Length; i++)
            {
                var tt = m_trailTexts[i];
                tt.text = m_text.text;
                float back = XAt(Mathf.Max(0.0f, t - 0.025f * (i + 1)));
                Color c = color;
                c.a = trailAlpha * 0.35f * (1.0f - (float)i / m_trailTexts.Length);
                Place(tt, back, c, 1.0f, speed);
            }
        }

        // 横の位置: 入る(減速)→ 中央付近を流れる → 抜ける(加速)
        private float XAt(float t)
        {
            if (t < m_enterEnd)
            {
                float u = t / m_enterEnd;
                return Mathf.Lerp(m_enterX, m_driftX, 1.0f - Mathf.Pow(1.0f - u, 3.0f));
            }
            if (t < m_exitStart)
            {
                float u = (t - m_enterEnd) / (m_exitStart - m_enterEnd);
                return Mathf.Lerp(m_driftX, -m_driftX, u);
            }
            float v = (t - m_exitStart) / (1.0f - m_exitStart);
            return Mathf.Lerp(-m_driftX, m_exitX, v * v * v);
        }

        // 速いほど進む向きに傾け、横に少し伸ばす
        private void Place(TextMeshProUGUI text, float x, Color color, float scale, float speed)
        {
            text.color = color;
            var rt = text.rectTransform;
            rt.anchoredPosition = new Vector2(x, 0.0f);
            float stretch = Mathf.Clamp(speed / 400.0f, 0.0f, 0.25f);
            rt.localScale = new Vector3(scale * (1.0f + stretch), scale * (1.0f - stretch * 0.4f), 1.0f);
            rt.localRotation = Quaternion.Euler(0.0f, 0.0f, Mathf.Clamp(speed / 30.0f, 0.0f, 10.0f));
        }

        // GO!: 大きく出て縮みながら止まり、少しして消える
        private void ShowGo()
        {
            m_goTime += Time.unscaledDeltaTime;
            float t = m_goTime / m_goDuration;
            if (t >= 1.0f)
            {
                m_goTime = -1.0f;
                SetVisible(false);
                return;
            }
            SetVisible(true);
            foreach (var tt in m_trailTexts) tt.gameObject.SetActive(false);
            m_text.text = m_goText;
            float pop = Mathf.Clamp01(t / 0.2f);
            float scale = Mathf.Lerp(2.2f, 1.0f, 1.0f - Mathf.Pow(1.0f - pop, 3.0f)) * (1.0f + 0.08f * t);
            Color c = m_goColor;
            c.a = 1.0f - Mathf.Clamp01((t - 0.6f) / 0.4f);
            Place(m_text, 0.0f, c, scale, 0.0f);
        }

        private void SetVisible(bool visible)
        {
            if (m_text.gameObject.activeSelf != visible) m_text.gameObject.SetActive(visible);
            foreach (var tt in m_trailTexts) if (tt.gameObject.activeSelf != visible) tt.gameObject.SetActive(visible);
        }
    }
}
