using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ネット対戦のシーン遷移(試合 → リザルト → 試合のシーン)。ホストが全員そろって切り替える(NGO の SceneManager)。
//  ・ホストが Load を呼ぶと、全員の画面を暗くしてからシーンを読み、読み終わったら明るくする(ローカルの Fader と同じ見え方)
//  ・戻り先(試合のシーン = NetStages)は NetMatch が ReturnScene に入れる
//  ・戻り先以外のシーン(リザルト)で接続が切れたら(ホストがいなくなったなど)、戻り先を読み直して最初の状態に戻す
// ゲーム起動時に自動で1個だけ作られ、シーンをまたいで残る(Fader と同じ)。シーンに置く必要はない。
public class NetSceneFlow : MonoBehaviour
{
    const string kFadeOutMessage = "NetSceneFlow.FadeOut";
    const float kFadeTime = 0.4f;

    static NetSceneFlow s_instance;

    // 試合のシーン(リザルトのあとに戻る・切断したときに戻る)
    public static string ReturnScene { get; set; }
    // 暗くしている・シーンを切り替えている途中か
    public static bool IsBusy => s_instance != null && s_instance.m_busy;

    CanvasGroup m_group;
    Coroutine m_fade;
    bool m_busy;
    // 名前付きメッセージを登録した NetworkManager(つなぎ直したら登録し直す)
    NetworkManager m_registered;
    bool m_wasListening;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Create()
    {
        if (s_instance != null) return;
        var go = new GameObject("NetSceneFlow");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<NetSceneFlow>();
    }

    // ホスト: 全員を暗くしてから sceneName に切り替える
    public static void Load(string sceneName)
    {
        var nm = NetworkManager.Singleton;
        if (s_instance == null || nm == null || !nm.IsServer || s_instance.m_busy) return;
        s_instance.StartCoroutine(s_instance.LoadRoutine(nm, sceneName));
    }

    void Awake()
    {
        // 画面全体を覆う黒い画像(Fader のすぐ下に出す)
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32766;
        gameObject.AddComponent<GraphicRaycaster>();
        m_group = gameObject.AddComponent<CanvasGroup>();
        m_group.alpha = 0.0f;
        m_group.blocksRaycasts = false;

        var image = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
        image.transform.SetParent(transform, false);
        var rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        image.color = Color.black;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void Update()
    {
        var nm = NetworkManager.Singleton;
        bool listening = nm != null && nm.IsListening;
        if (listening && m_registered != nm)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(kFadeOutMessage, (_, _) => FadeOut());
            m_registered = nm;
        }
        if (!listening) m_registered = null;

        // 戻り先以外で切れたら、戻り先を読み直す(NetStages の NetLobbyUI が最初の画面を出す)
        if (m_wasListening && !listening) OnStopped(nm);
        m_wasListening = listening;
    }

    IEnumerator LoadRoutine(NetworkManager nm, string sceneName)
    {
        // クライアントも同時に暗くする
        using (var writer = new FastBufferWriter(1, Allocator.Temp))
        {
            writer.WriteValueSafe((byte)0);
            nm.CustomMessagingManager.SendNamedMessageToAll(kFadeOutMessage, writer);
        }
        FadeOut();
        yield return new WaitForSecondsRealtime(kFadeTime);

        var status = nm.IsListening ? nm.SceneManager.LoadScene(sceneName, LoadSceneMode.Single) : SceneEventProgressStatus.None;
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"[NetSceneFlow] {sceneName} を読めませんでした({status})。Build Settings に入っているか確かめてください");
            FadeIn();
        }
        // 明るくするのはシーンを読み終わったとき(OnSceneLoaded)
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (m_busy) FadeIn();
    }

    void OnStopped(NetworkManager nm)
    {
        // 試合のシーン(NetLobbyUI)と接続画面(NetConnectScreen)は、切れたときの面倒を自分で見る
        string active = SceneManager.GetActiveScene().name;
        string connect = NetConnectScreen.SceneName;
        if (active == connect || string.IsNullOrEmpty(ReturnScene) || active == ReturnScene)
        {
            if (m_busy) FadeIn();
            return;
        }
        // 接続画面から来たときは接続画面へ、そうでなければ試合のシーンへ戻る
        const string message = "ホストとの接続が切れました";
        NetLobbyUI.SetMessage(message);
        NetConnectScreen.Message = message;
        // 戻り先のシーンにも NetworkManager があるので、二重にならないように今のものを消す
        if (nm != null) Destroy(nm.gameObject);
        m_busy = false;
        Time.timeScale = 1.0f;
        Fader.Load(string.IsNullOrEmpty(connect) ? ReturnScene : connect);
        FadeIn();
    }

    void FadeOut()
    {
        m_busy = true;
        StartFade(1.0f);
    }

    void FadeIn()
    {
        StartFade(0.0f);
    }

    void StartFade(float to)
    {
        if (m_fade != null) StopCoroutine(m_fade);
        m_fade = StartCoroutine(Fade(to));
    }

    IEnumerator Fade(float to)
    {
        m_group.blocksRaycasts = true;
        // 読み終わった直後は重いので、1フレーム待ってから明るくする
        if (to <= 0.0f) yield return null;
        float from = m_group.alpha;
        for (float t = 0.0f; t < kFadeTime; t += Time.unscaledDeltaTime)
        {
            m_group.alpha = Mathf.Lerp(from, to, t / kFadeTime);
            yield return null;
        }
        m_group.alpha = to;
        m_group.blocksRaycasts = to > 0.0f;
        if (to <= 0.0f) m_busy = false;
        m_fade = null;
    }
}
