using UnityEngine;
using UnityEngine.UI;

// OptionSelector（「＜ フルスクリーン ＞」）の見た目の動き。
//  ・値の下に、いま何番目かを表す小さな丸を並べる
//  ・切り替えたら値の板をぽよんと弾ませ、押した側の矢印を少し飛び出させる
public class OptionSelectorLook : MonoBehaviour
{
    OptionSelector selector;
    RectTransform pill, prev, next, dotRoot;
    Color dotOn, dotOff;
    Image[] dots = new Image[0];
    int lastIndex = -1, lastCount = -1;
    float punchStart = -100f, nudgeStart = -100f;
    int nudgeDir;

    public void Setup(OptionSelector target, RectTransform valuePill, RectTransform prevArrow, RectTransform nextArrow, RectTransform dotArea, Color on, Color off)
    {
        selector = target;
        pill = valuePill; prev = prevArrow; next = nextArrow; dotRoot = dotArea;
        dotOn = on; dotOff = off;
        selector.onValueChanged.AddListener(OnChanged);
    }

    void OnChanged(int index)
    {
        int count = selector.Count;
        if (lastIndex >= 0 && count > 1)
            nudgeDir = index == (lastIndex + 1) % count ? 1 : -1;
        punchStart = nudgeStart = Time.unscaledTime;
    }

    void Update()
    {
        if (selector == null) return;

        // 選択肢の数が変わったら丸を作り直す（解像度の一覧は後から入る）
        if (selector.Count != lastCount)
        {
            foreach (var d in dots) if (d != null) Destroy(d.gameObject);
            lastCount = selector.Count;
            dots = new Image[lastCount > 1 ? lastCount : 0];
            const float size = 12f, gap = 8f;
            float total = dots.Length * size + (dots.Length - 1) * gap;
            for (int i = 0; i < dots.Length; i++)
            {
                var r = ToyKit.UIRounded("Dot" + (i + 1), dotRoot, dotOff, size * 0.5f);
                ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), new Vector2(-total * 0.5f + size * 0.5f + i * (size + gap), 0f), new Vector2(size, size));
                dots[i] = r.GetComponent<Image>();
            }
            lastIndex = -1;
        }
        if (selector.Index != lastIndex)
        {
            lastIndex = selector.Index;
            for (int i = 0; i < dots.Length; i++) dots[i].color = i == lastIndex ? dotOn : dotOff;
        }

        float p = ToyKit.Punch((Time.unscaledTime - punchStart) / 0.3f, 0.12f);
        pill.localScale = new Vector3(1f + p, 1f - p * 0.6f, 1f);

        float n = ToyKit.Punch((Time.unscaledTime - nudgeStart) / 0.25f, 1f);
        prev.localScale = Vector3.one * (1f + (nudgeDir < 0 ? n * 0.25f : 0f));
        next.localScale = Vector3.one * (1f + (nudgeDir > 0 ? n * 0.25f : 0f));
    }
}
