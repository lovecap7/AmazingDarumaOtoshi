using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// フェードイン・フェードアウト付きのシーン遷移を担当する。
// ゲーム起動時に自動で1個だけ作られ、シーンをまたいで残る。
// シーンに置いたり、ゲームオブジェクトに付けたりする必要はない。
public class Fader : MonoBehaviour
{
    const float FadeOutTime = 0.4f;   // 暗くなる時間（秒）
    const float FadeInTime = 0.4f;    // 明るくなる時間（秒）

    static Fader instance;

    CanvasGroup group;
    bool busy;

    // 最初のシーンが読み込まれる前に、自動でフェード用オブジェクトを作る
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Create()
    {
        if (instance != null) return;

        var go = new GameObject("Fader");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<Fader>();
    }

    // フェードしながらシーン遷移する（遷移中の呼び出しは無視）
    public static void Load(string sceneName)
    {
        if (instance == null)
        {
            SceneManager.LoadScene(sceneName);
            return;
        }

        if (instance.busy) return;
        instance.StartCoroutine(instance.Run(sceneName));
    }

    void Awake()
    {
        // 画面全体を覆う黒い画像を、コードで作る
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;   // 何よりも手前に表示

        gameObject.AddComponent<GraphicRaycaster>();   // フェード中のマウスクリックを遮る

        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 1f;   // 最初は真っ黒（起動直後にフェードインさせるため）

        var imageObject = new GameObject("Black", typeof(RectTransform));
        imageObject.transform.SetParent(transform, false);

        var rect = (RectTransform)imageObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = imageObject.AddComponent<Image>();
        image.color = Color.black;
    }

    void Start()
    {
        // 起動した最初のシーンをフェードインで見せる
        StartCoroutine(FirstFadeIn());
    }

    IEnumerator FirstFadeIn()
    {
        busy = true;
        yield return Fade(1f, 0f, FadeInTime);
        busy = false;
    }

    IEnumerator Run(string sceneName)
    {
        busy = true;

        // フェード中にコントローラーで別の項目を押されないようにする
        if (EventSystem.current != null)
        {
            EventSystem.current.sendNavigationEvents = false;
        }

        yield return Fade(0f, 1f, FadeOutTime);   // 暗くする

        var operation = SceneManager.LoadSceneAsync(sceneName);
        while (!operation.isDone) yield return null;

        yield return Fade(1f, 0f, FadeInTime);    // 明るくする

        busy = false;
    }

    IEnumerator Fade(float from, float to, float time)
    {
        group.blocksRaycasts = true;

        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, t / time);
            yield return null;
        }

        group.alpha = to;
        group.blocksRaycasts = to > 0f;
    }
}