using UnityEngine;
using UnityEngine.UI;

// 画面右の設定パネル（サウンド・ビデオ）。
// 未決定のときは暗いプレビューとして表示し、決定されると明るくなって操作できるようになる。
[RequireComponent(typeof(CanvasGroup))]
public class MenuPanel : MonoBehaviour
{
    [Tooltip("プレビュー中に暗くするための黒い Image（パネルの子で最前面）")]
    [SerializeField] Image dimOverlay;

    [Tooltip("決定したメニューの積み木が飛んでくる位置")]
    [SerializeField] RectTransform dock;

    [Tooltip("パネルが操作可能になったとき最初に選ぶUI")]
    [SerializeField] Selectable firstSelected;

    [Header("着地の揺れ")]
    [SerializeField] float punchScale = 0.04f;
    [SerializeField] float punchDuration = 0.25f;

    CanvasGroup group;
    float punchT = 1f;

    public RectTransform Dock => dock;
    public Selectable FirstSelected => firstSelected;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        SetState(0f, 1f, false);
    }

    // visibility: 表示の濃さ(0〜1) / darkness: 暗さ(0〜1) / interactable: 操作できるか
    public void SetState(float visibility, float darkness, bool interactable)
    {
        group.alpha = visibility;
        group.interactable = interactable;
        group.blocksRaycasts = interactable;

        if (dimOverlay != null)
        {
            var c = dimOverlay.color;
            c.a = darkness;
            dimOverlay.color = c;
        }
    }

    // 積み木が当たった瞬間にパネルを少し弾ませる
    public void Punch() => punchT = 0f;

    void Update()
    {
        if (punchT >= 1f) return;
        punchT = Mathf.Min(1f, punchT + Time.unscaledDeltaTime / punchDuration);
        float s = 1f + punchScale * Mathf.Sin(punchT * Mathf.PI) * (1f - punchT);
        transform.localScale = Vector3.one * s;
    }
}
