using Nakahira;
using UnityEngine;
using UnityEngine.UI;

// キャラクターの足元に出すスタミナのゲージ。スタミナが減っている間だけ表示する
// キャラクターの回転に影響されないよう子にはせず、毎フレーム追いかける
public class DashStaminaGauge : MonoBehaviour
{
    const float kWidth = 100.0f;
    const float kHeight = 14.0f;
    const float kBorder = 2.0f;
    // 1 単位 = 0.01m
    const float kWorldScale = 0.01f;

    static readonly Color kBackColor = new Color(0.0f, 0.0f, 0.0f, 0.65f);
    static readonly Color kNormalColor = new Color(0.35f, 0.95f, 0.45f);
    static readonly Color kLowColor = new Color(1.0f, 0.75f, 0.15f);
    static readonly Color kExhaustedColor = new Color(0.95f, 0.25f, 0.2f);

    DarumaCharacter m_target;
    DashStamina m_stamina;
    Canvas m_canvas;
    RectTransform m_fill;
    Image m_fillImage;

    public static DashStaminaGauge Create(DarumaCharacter target, DashStamina stamina)
    {
        var go = new GameObject("DashStaminaGauge", typeof(RectTransform));
        var gauge = go.AddComponent<DashStaminaGauge>();
        gauge.Build(target, stamina);
        return gauge;
    }

    void Build(DarumaCharacter target, DashStamina stamina)
    {
        m_target = target;
        m_stamina = stamina;

        m_canvas = gameObject.AddComponent<Canvas>();
        m_canvas.renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)transform;
        root.sizeDelta = new Vector2(kWidth, kHeight);
        root.localScale = Vector3.one * kWorldScale;

        Image back = CreateImage("Back", root, kBackColor);
        Stretch(back.rectTransform, 0.0f);

        m_fillImage = CreateImage("Fill", back.rectTransform, kNormalColor);
        m_fill = m_fillImage.rectTransform;
        Stretch(m_fill, kBorder);
    }

    static Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    void LateUpdate()
    {
        // キャラクターが消えたら一緒に消える
        if (m_target == null)
        {
            Destroy(gameObject);
            return;
        }

        float ratio = m_stamina.Ratio;
        Camera cam = Camera.main;
        m_canvas.enabled = ratio < 1.0f && cam != null;
        if (!m_canvas.enabled) return;

        m_fill.anchorMax = new Vector2(ratio, 1.0f);
        // 黄色はジャンプできない残量
        m_fillImage.color = m_stamina.IsExhausted ? kExhaustedColor : ratio < DashParams.Instance.JumpCostRatio ? kLowColor : kNormalColor;

        // カメラから見てキャラクターの手前に出す(体や積み木に隠れないように)
        Transform view = cam.transform;
        Vector3 toCamera = Vector3.ProjectOnPlane(-view.forward, Vector3.up).normalized;
        transform.position = m_target.transform.position + Vector3.up * 0.1f + toCamera * 0.8f - view.forward * 0.5f;
        transform.rotation = view.rotation;
    }
}
