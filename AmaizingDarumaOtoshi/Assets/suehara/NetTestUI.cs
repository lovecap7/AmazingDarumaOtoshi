using UnityEngine;
using Unity.Netcode;

// ネット対戦のテスト用ボタン(Host / Client / 切断)と接続状態の表示
// ホストは試合のルール(モード・ストック・時間)を選んで「試合開始」できる(試合中・試合後は「やり直し」)
// 左上はステージ名の表示(StageSwitcher)と重なるので、その下に出す
public class NetTestUI : MonoBehaviour
{
    // 時間制限の選択肢(秒。0 = なし。タイムはなしを選べないので 120 秒)
    static readonly float[] kTimeLimits = { 0.0f, 60.0f, 120.0f, 180.0f };

    MatchSetup.MatchMode m_mode = MatchSetup.MatchMode.Stock;
    int m_stocks = 3;
    int m_timeIndex;

    void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        var button = new GUIStyle(GUI.skin.button) { fontSize = 20 };
        var label = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
        GUILayout.BeginArea(new Rect(10, 60, 260, 400));
        if (!nm.IsClient && !nm.IsServer)
        {
            if (GUILayout.Button("Host", button, GUILayout.Height(40))) nm.StartHost();
            if (GUILayout.Button("Client", button, GUILayout.Height(40))) nm.StartClient();
        }
        else
        {
            string state = nm.IsHost ? "Host" : nm.IsConnectedClient ? "Client(接続中)" : "Client(接続待ち…)";
            GUILayout.Box(state + "  人数:" + (nm.IsServer ? nm.ConnectedClientsIds.Count.ToString() : "-"), label);
            if (GUILayout.Button("切断", button, GUILayout.Height(32))) nm.Shutdown();
            MatchGUI(nm, button, label);
        }
        GUILayout.EndArea();
    }

    void MatchGUI(NetworkManager nm, GUIStyle button, GUIStyle label)
    {
        var net = NetMatch.Instance;
        if (net == null || !net.IsSpawned || net.Match == null) return;

        var match = net.Match;
        string phase = !net.IsStarted ? "開始待ち" : match.IsFinished ? "試合終了" : "試合中";
        GUILayout.Box($"{phase}  {net.Rules.ModeName}  {net.PlayerCount}人", label);
        if (!nm.IsServer) return;

        GUILayout.Space(8);
        if (GUILayout.Button("モード: " + ModeName(m_mode), button)) m_mode = (MatchSetup.MatchMode)(((int)m_mode + 1) % 3);
        if (m_mode == MatchSetup.MatchMode.Stock && GUILayout.Button($"ストック: {m_stocks}", button)) m_stocks = m_stocks % 5 + 1;
        if (GUILayout.Button("時間: " + TimeLabel(TimeLimit), button)) m_timeIndex = (m_timeIndex + 1) % kTimeLimits.Length;
        if (GUILayout.Button(net.IsStarted ? "やり直し" : "試合開始", button, GUILayout.Height(40))) net.StartMatch(BuildRules());
    }

    // タイムは時間制限なしを選べない
    float TimeLimit => m_mode == MatchSetup.MatchMode.Time && kTimeLimits[m_timeIndex] <= 0.0f ? 120.0f : kTimeLimits[m_timeIndex];

    MatchSetup.MatchRules BuildRules() => new MatchSetup.MatchRules
    {
        mode = m_mode,
        stocks = m_stocks,
        stockTimeLimit = TimeLimit,
        timeLength = TimeLimit,
        ghostTimeLimit = TimeLimit,
    };

    static string ModeName(MatchSetup.MatchMode mode) => new MatchSetup.MatchRules { mode = mode }.ModeName;
    static string TimeLabel(float seconds) => seconds > 0.0f ? $"{seconds:0}秒" : "なし";
}
