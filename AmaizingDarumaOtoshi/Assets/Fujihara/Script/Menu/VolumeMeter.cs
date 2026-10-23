using UnityEngine;
using UnityEngine.UI;

// 音量スライダーの見た目。0〜10 の目盛りを、左から少しずつ高くなる積み木の列で表す。
// 入力はそのまま Slider（左右キー・ドラッグ・クリック）。値が変わったら積み木の色を変えて、変わった1個をぽよんと弾ませる。
public class VolumeMeter : MonoBehaviour
{
    Slider slider;
    RectTransform[] steps;
    Image[] images;
    Color onColor, offColor;
    float[] punchStart;
    int shown = -1;

    public void Setup(Slider target, RectTransform area, Color on, Color off)
    {
        slider = target;
        onColor = on;
        offColor = off;
        int n = Mathf.RoundToInt(slider.maxValue - slider.minValue);
        steps = new RectTransform[n];
        images = new Image[n];
        punchStart = new float[n];

        float w = area.rect.width, h = area.rect.height;
        const float gap = 8f;
        float stepW = (w - gap * (n - 1)) / n;
        for (int i = 0; i < n; i++)
        {
            float height = Mathf.Lerp(h * 0.45f, h, i / (float)(n - 1));
            var r = ToyKit.UIRounded("Step" + (i + 1), area, offColor, 10f);
            r.anchorMin = r.anchorMax = new Vector2(0f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.anchoredPosition = new Vector2(stepW * 0.5f + i * (stepW + gap), 0f);
            r.sizeDelta = new Vector2(stepW, height);
            steps[i] = r;
            images[i] = r.GetComponent<Image>();
            punchStart[i] = -100f;
        }
        slider.onValueChanged.AddListener(_ => Refresh(true));
        Refresh(false);
    }

    void Refresh(bool animate)
    {
        int v = Mathf.RoundToInt(slider.value - slider.minValue);
        for (int i = 0; i < images.Length; i++)
        {
            // 左から右へ少しずつ明るくする
            var c = i < v ? Color.Lerp(onColor * 0.85f, onColor, i / (float)Mathf.Max(1, images.Length - 1)) : offColor;
            c.a = 1f;
            images[i].color = c;
        }
        if (animate && shown >= 0 && v != shown)
        {
            int changed = v > shown ? v - 1 : v;   // 増えたら新しく点いた1個、減ったら消えた1個
            if (changed >= 0 && changed < steps.Length) punchStart[changed] = Time.unscaledTime;
        }
        shown = v;
    }

    void Update()
    {
        if (steps == null) return;
        for (int i = 0; i < steps.Length; i++)
        {
            float t = (Time.unscaledTime - punchStart[i]) / 0.3f;
            float p = ToyKit.Punch(t, 0.35f);
            steps[i].localScale = new Vector3(1f - p * 0.3f, 1f + p, 1f);
        }
    }
}
