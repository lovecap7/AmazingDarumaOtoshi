using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// ネット対戦のリザルト画面(ResultTowerScreen / ResultScreen)で、次へ進むのをホストに任せる。
//  ・リザルト画面は全員がそれぞれ再生する(結果は NetMatch が ResultData に入れておく)
//  ・リザルト画面は決定ボタンでセレクトへ移る(Fader.Load)が、ネット対戦では全員そろって試合のシーンに戻りたいので、
//    演出が終わったら画面自身の「次へ」を止め(leaving)、ホストの決定ボタン(または一定時間)で NetSceneFlow から戻る
// 接続中にリザルト画面のシーンが読み込まれたら、自動で付く(シーンに置く必要はない)。リザルト画面のスクリプトは変えない。
public class NetResultFlow : MonoBehaviour
{
    const BindingFlags kPrivate = BindingFlags.NonPublic | BindingFlags.Instance;

    MonoBehaviour m_screen;
    FieldInfo m_done, m_leaving, m_autoReturnDelay;
    float m_doneTime = -1.0f;
    bool m_leavingNow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;
        MonoBehaviour screen = FindAnyObjectByType<ResultTowerScreen>();
        if (screen == null) screen = FindAnyObjectByType<ResultScreen>();
        if (screen == null || screen.gameObject.scene != scene) return;
        screen.gameObject.AddComponent<NetResultFlow>().m_screen = screen;
    }

    void Start()
    {
        // ResultTowerScreen と ResultScreen は同じ名前のフィールドで「演出が終わった」「次へ移る途中」を持っている
        var t = m_screen.GetType();
        m_done = t.GetField("done", kPrivate);
        m_leaving = t.GetField("leaving", kPrivate);
        m_autoReturnDelay = t.GetField("autoReturnDelay", kPrivate);
        if (m_done == null || m_leaving == null)
        {
            Debug.LogError($"[NetResultFlow] {t.Name} の done / leaving が見つかりません(リザルト画面の作りが変わった?)。次へ進めません");
            enabled = false;
        }
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || m_leavingNow) return;
        if (!(bool)m_done.GetValue(m_screen)) return;

        // 演出が終わったら、画面自身がセレクトへ移らないようにする(早送りは今までどおり画面が受け付ける)
        if (m_doneTime < 0.0f)
        {
            m_doneTime = Time.unscaledTime;
            m_leaving.SetValue(m_screen, true);
        }
        if (!nm.IsServer) return;

        // ホスト: 決定ボタン(この画面を選んでいるときだけ)か、一定時間たったら全員で戻る
        float delay = m_autoReturnDelay != null ? (float)m_autoReturnDelay.GetValue(m_screen) : 30.0f;
        bool submit = Application.isFocused && (MenuInput.Submit() || MenuInput.AnyClick()) && Time.unscaledTime - m_doneTime > 0.4f;
        bool timeout = delay > 0.0f && Time.unscaledTime - m_doneTime > delay;
        if (!submit && !timeout) return;

        m_leavingNow = true;
        if (submit) MenuSounds.Decide();
        NetSceneFlow.Load(NetSceneFlow.ReturnScene);
    }
}
