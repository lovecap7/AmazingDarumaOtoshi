using UnityEngine;

// ハンマーが当たった位置に出る衝撃エフェクト（星形＋文字）。ポンと膨らんで消える。
[RequireComponent(typeof(CanvasGroup))]
public class ImpactEffect : MonoBehaviour
{
    [SerializeField] RectTransform star;
    [SerializeField] RectTransform label;
    [SerializeField] float duration = 0.45f;
    [SerializeField] float popScale = 1.3f;
    [SerializeField] float labelRise = 40f;

    CanvasGroup group;
    float t = 1f;
    Vector2 labelBase;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        if (label != null) labelBase = label.anchoredPosition;
    }

    public void Play(Vector3 worldPosition)
    {
        transform.position = worldPosition;
        t = 0f;
        if (star != null) star.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
    }

    void Update()
    {
        if (t >= 1f) return;
        t = Mathf.Min(1f, t + Time.unscaledDeltaTime / duration);

        // 一気に膨らんでから少し戻る
        float s = t < 0.2f ? Mathf.Lerp(0.2f, popScale, t / 0.2f) : Mathf.Lerp(popScale, 1f, (t - 0.2f) / 0.8f);
        transform.localScale = Vector3.one * s;
        group.alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;

        if (star != null) star.localRotation *= Quaternion.Euler(0f, 0f, 90f * Time.unscaledDeltaTime);
        if (label != null) label.anchoredPosition = labelBase + Vector2.up * labelRise * (1f - (1f - t) * (1f - t));
    }
}
