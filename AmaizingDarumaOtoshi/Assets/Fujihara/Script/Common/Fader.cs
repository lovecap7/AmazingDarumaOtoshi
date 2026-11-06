using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// シーン遷移の演出（画面を隠す → シーンを読み込む → 画面を見せる）。Fader.Transition に入れると差し替えられる
public interface ISceneTransition
{
    // 演出の部品を root（画面いっぱいの、いちばん手前のキャンバス）の下に作る。最初に1回だけ呼ばれる
    void Build(RectTransform root);
    // fromScene → toScene の遷移でこの演出を使うか。false なら黒いフェードになる（起動直後もいつも黒いフェード）
    bool Plays(string fromScene, string toScene);
    // 画面を隠す（終わったら完全に隠れていること）
    IEnumerator Cover();
    // 画面を見せる（終わったら何も残っていないこと）
    IEnumerator Reveal();
}

// フェードイン・フェードアウト付きのシーン遷移を担当する。
// ゲーム起動時に自動で1個だけ作られ、シーンをまたいで残る。
// シーンに置いたり、ゲームオブジェクトに付けたりする必要はない。
// 演出は Transition（ISceneTransition）で差し替えられる。何も入っていなければ黒いフェード
public class Fader : MonoBehaviour
{
    const float FadeOutTime = 0.4f;   // 暗くなる時間（秒）
    const float FadeInTime = 0.4f;    // 明るくなる時間（秒）

    static Fader instance;
    static ISceneTransition transition;

    CanvasGroup group;
    RectTransform root;
    ISceneTransition built;   // root の下に組み立て済みの演出
    bool busy;

    // フェード中（シーン遷移の途中・遷移後に明るくなるまで）か。開始演出などを明るくなってから始めるのに使う
    public static bool IsBusy => instance != null && instance.busy;

    // シーン遷移の演出。null で黒いフェード。起動時（BeforeSceneLoad）などに入れておく
    public static ISceneTransition Transition
    {
        get => transition;
        set => transition = value;
    }

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

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

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

        // 差し替えの演出を入れる場所（黒い画像より手前）
        root = new GameObject("Transition", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(transform, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
    }

    // 演出が入っていれば組み立てる（入れ替わっていたら作り直す）。使うなら黒い画像は隠す
    ISceneTransition Current()
    {
        if (transition != built)
        {
            foreach (Transform c in root) Destroy(c.gameObject);
            built = transition;
            if (built != null) built.Build(root);
        }
        return built;
    }

    void Start()
    {
        // 起動した最初のシーンをフェードインで見せる
        StartCoroutine(FirstFadeIn());
    }

    IEnumerator FirstFadeIn()
    {
        busy = true;
        Current();   // 演出の部品は先に作っておく（起動直後は黒いフェード）
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

        var t = Current();
        if (t != null && !t.Plays(SceneManager.GetActiveScene().name, sceneName)) t = null;   // この組み合わせは黒いフェード
        if (t != null)
        {
            BlackVisible(false);
            group.alpha = 1f;
            group.blocksRaycasts = true;
            yield return t.Cover();   // 隠す
        }
        else yield return Fade(0f, 1f, FadeOutTime);   // 暗くする

        var operation = LoadSceneAsync(sceneName);
        while (operation != null && !operation.isDone) yield return null;

        if (t != null)
        {
            yield return t.Reveal();   // 見せる
            group.blocksRaycasts = false;
        }
        else yield return Fade(1f, 0f, FadeInTime);    // 明るくする

        busy = false;
    }

    void BlackVisible(bool on)
    {
        var black = transform.Find("Black");
        if (black != null) black.gameObject.SetActive(on);
    }

    // Build Settings に入っていないシーンも、エディタ上では名前で開けるようにする（ビルドでは登録が必要）
    static AsyncOperation LoadSceneAsync(string sceneName)
    {
#if UNITY_EDITOR
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets(sceneName + " t:Scene"))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != sceneName) continue;
                Debug.LogWarning($"シーン {sceneName} は Build Settings に入っていません（エディタでは開けますが、ビルドでは登録が必要です）");
                return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            }
        }
#endif
        return SceneManager.LoadSceneAsync(sceneName);
    }

    IEnumerator Fade(float from, float to, float time)
    {
        BlackVisible(true);
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
