using UnityEngine;

// クレジットを下から上へ流す。決定・キャンセル・クリックで途中終了できる。
[RequireComponent(typeof(CanvasGroup))]
public class CreditRoll : MonoBehaviour
{
    [Tooltip("流れるテキスト（縦に長い RectTransform）")]
    [SerializeField] RectTransform content;
    [SerializeField] float scrollSpeed = 120f;   // 1秒に流れる距離
    [SerializeField] float fadeTime = 0.3f;

    CanvasGroup group;
    RectTransform viewport;
    bool playing;
    float fade;          // 0=非表示 1=表示
    int openedFrame = -1;
    int closedFrame = -1;

    // 再生中、または閉じたフレーム（同じボタン入力でメニューが反応しないように）
    public bool IsBusy => playing || fade > 0f || closedFrame == Time.frameCount;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        viewport = (RectTransform)transform;
        Apply();
    }

    // コードから組み立てるときに部品を渡す
    public void Setup(RectTransform scrollContent) => content = scrollContent;

    public void Play()
    {
        if (playing) return;
        playing = true;
        openedFrame = Time.frameCount;

        // 画面の下端から開始
        float h = viewport.rect.height;
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, -h * 0.5f - content.rect.height * 0.5f);
    }

    public void Stop()
    {
        if (!playing) return;
        playing = false;
        closedFrame = Time.frameCount;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        fade = Mathf.MoveTowards(fade, playing ? 1f : 0f, dt / fadeTime);
        Apply();
        if (!playing) return;

        content.anchoredPosition += Vector2.up * scrollSpeed * dt;

        // 上端まで流れきったら終了
        if (content.anchoredPosition.y > viewport.rect.height * 0.5f + content.rect.height * 0.5f)
            Stop();

        if (Time.frameCount != openedFrame && (MenuInput.Submit() || MenuInput.Cancel() || MenuInput.AnyClick()))
            Stop();
    }

    void Apply()
    {
        group.alpha = fade;
        group.blocksRaycasts = fade > 0f;
        group.interactable = false;
    }
}
