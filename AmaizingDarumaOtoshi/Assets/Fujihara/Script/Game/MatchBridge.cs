using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// セレクト画面で決めた内容（MatchSetup）でステージのシーンを始めるための入口。シーン側のファイルは変更せず、読み込まれたときに自動で動く。
//  ・MatchSetup が空（ステージのシーンを直接開いた）なら何もしない → 本編（Nakahira）の「ボタンを押して参加」でそのまま遊べる
//  ・MatchSetup があれば、シーンにある本編の試合管理（DarumaMatch・PlayerInputManager）を止めて、
//    Fujihara の MatchDirector（試合の進行）と MatchDebugView（仮の表示）を置く
//  ・出現ポイントは「SpawnPoints」の子（Nakagawa のステージ）から、ステージの範囲は StageArea から読む
public static class MatchBridge
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // シーンの Awake の後・Start の前に呼ばれる
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (MatchSetup.Players.Count == 0) return;
        var stageArea = Object.FindAnyObjectByType<StageArea>();
        var oldMatch = Object.FindAnyObjectByType<DarumaMatch>();
        if (stageArea == null && oldMatch == null) return;   // 試合のシーンではない

        // 本編の試合管理は使わない（Start で出るダミーや、ボタンを押した人の勝手な参加を止める）
        if (oldMatch != null) oldMatch.enabled = false;
        var inputManager = Object.FindAnyObjectByType<PlayerInputManager>();
        if (inputManager != null)
        {
            inputManager.DisableJoining();
            inputManager.enabled = false;
        }

        var go = new GameObject("MatchDirector");
        go.AddComponent<MatchDirector>().Init(stageArea, FindSpawnPoints(oldMatch));
        go.AddComponent<MatchDebugView>();
    }

    static Transform[] FindSpawnPoints(DarumaMatch oldMatch)
    {
        // Nakagawa のステージ：「SpawnPoints」の子（SpawnPoint1, 2, …）
        var root = GameObject.Find("SpawnPoints");
        if (root != null && root.transform.childCount > 0)
        {
            var points = new Transform[root.transform.childCount];
            for (int i = 0; i < points.Length; i++) points[i] = root.transform.GetChild(i);
            return points;
        }

        // それ以外（PlayerTest など）：本編の試合管理に設定されている出現ポイント
        if (oldMatch != null)
        {
            var field = typeof(DarumaMatch).GetField("m_spawnPoints", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null && field.GetValue(oldMatch) is Transform[] fromMatch && fromMatch.Length > 0) return fromMatch;
        }
        Debug.LogWarning("[MatchBridge] 出現ポイントが見つからないので、原点に出します");
        return new Transform[0];
    }
}
