using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nakahira
{
    // 必殺技ゲージ 1 人分の表示(画面下のパネルの上の細いバー)。SpecialGaugeHud が人数分作って動かす
    // 見た目(色・大きさ・文字)はプレハブ SpecialGaugeBar で編集する
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public class SpecialGaugeBar : MonoBehaviour
    {
        [Header("部品")]
        [SerializeField] Image m_background;
        [Tooltip("溜まった分。横の割合は anchorMax.x で変える(左端が 0)")]
        [SerializeField] RectTransform m_fill;
        [SerializeField] Image m_fillImage;
        [Tooltip("満タンのとき外側で光る")]
        [SerializeField] Image m_glow;
        [Tooltip("満タンのときバーの上を流れる光")]
        [SerializeField] RectTransform m_shine;
        [SerializeField] TextMeshProUGUI m_label;

        [Header("色")]
        [Tooltip("溜まっている途中のバーの色に、プレイヤーの色をどれだけ使うか(0 なら FillColor のまま)")]
        [Range(0.0f, 1.0f)] [SerializeField] float m_playerColorWeight = 1.0f;
        [SerializeField] Color m_fillColor = Color.white;
        [SerializeField] Color m_fullColorA = new Color(1.0f, 0.74f, 0.12f);
        [SerializeField] Color m_fullColorB = Color.white;
        [SerializeField] Color m_labelColor = new Color(0.92f, 0.94f, 0.98f);

        [Header("動き")]
        [Tooltip("バーが目標の長さに追いつく速さ(1 秒あたりの割合)")]
        [Min(0.1f)] [SerializeField] float m_fillSpeed = 2.5f;
        [Tooltip("満タンで光る速さ")]
        [Min(0.0f)] [SerializeField] float m_pulseSpeed = 7.0f;
        [Tooltip("満タンで流れる光が 1 往復する速さ(1 秒あたり)")]
        [Min(0.0f)] [SerializeField] float m_shineSpeed = 0.9f;
        [Tooltip("体がない(幽霊・復活待ち)間の濃さ")]
        [Range(0.0f, 1.0f)] [SerializeField] float m_absentAlpha = 0.45f;

        [Header("文字")]
        [SerializeField] string m_readyText = "READY!";
        [Tooltip("割合の書き方({0} が 0〜100)")]
        [SerializeField] string m_percentFormat = "{0}%";

        RectTransform m_rect;
        CanvasGroup m_group;
        float m_shown = -1.0f;

        public RectTransform Rect => m_rect != null ? m_rect : (m_rect = (RectTransform)transform);

        private void Awake()
        {
            m_rect = (RectTransform)transform;
            m_group = GetComponent<CanvasGroup>();
        }

        // 1 フレーム分の表示
        // ratio: 0〜1 / full: 満タン / status: 割合の代わりに出す文字(null なら割合) / present: 体がある / alpha: 全体の濃さ
        public void Show(Color playerColor, float ratio, bool full, int percent, string status, bool present, float alpha)
        {
            float dt = Time.unscaledDeltaTime;
            ratio = Mathf.Clamp01(ratio);
            // 減るとき(必殺技を使った)はすぐ、増えるときはなめらかに
            if (m_shown < 0.0f || ratio < m_shown) m_shown = ratio;
            else m_shown = Mathf.MoveTowards(m_shown, ratio, m_fillSpeed * dt);
            if (full) m_shown = 1.0f;

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * m_pulseSpeed);
            Color fullColor = Color.Lerp(m_fullColorA, m_fullColorB, pulse);
            Color fillColor = Color.Lerp(m_fillColor, playerColor, m_playerColorWeight);
            fillColor.a = 1.0f;

            if (m_fill != null)
            {
                m_fill.anchorMin = new Vector2(0.0f, m_fill.anchorMin.y);
                m_fill.anchorMax = new Vector2(m_shown, m_fill.anchorMax.y);
                m_fill.offsetMin = new Vector2(0.0f, m_fill.offsetMin.y);
                m_fill.offsetMax = new Vector2(0.0f, m_fill.offsetMax.y);
                m_fill.gameObject.SetActive(m_shown > 0.0005f);
            }
            if (m_fillImage != null) m_fillImage.color = full ? fullColor : fillColor;

            if (m_glow != null)
            {
                m_glow.gameObject.SetActive(full);
                if (full)
                {
                    Color g = fullColor;
                    g.a = 0.25f + 0.35f * pulse;
                    m_glow.color = g;
                }
            }

            if (m_shine != null)
            {
                m_shine.gameObject.SetActive(full);
                if (full)
                {
                    float t = Mathf.Repeat(Time.unscaledTime * m_shineSpeed, 1.0f);
                    m_shine.anchorMin = new Vector2(t, m_shine.anchorMin.y);
                    m_shine.anchorMax = new Vector2(t, m_shine.anchorMax.y);
                    m_shine.anchoredPosition = new Vector2(0.0f, m_shine.anchoredPosition.y);
                }
            }

            if (m_label != null)
            {
                string text = !string.IsNullOrEmpty(status) ? status : full ? m_readyText : string.Format(m_percentFormat, percent);
                if (m_label.text != text) m_label.text = text;
                m_label.color = full ? fullColor : m_labelColor;
            }

            if (m_group != null) m_group.alpha = alpha * (present ? 1.0f : m_absentAlpha);
        }

        // 次に出すときは今の値から始める(人が入れ替わったときなど)
        public void ResetFill()
        {
            m_shown = -1.0f;
        }
    }
}
