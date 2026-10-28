using UnityEngine;
using Unity.Netcode;

// ネット対戦のテスト用ボタン(Host / Client / 切断)と接続状態の表示
// 左上はステージ名の表示(StageSwitcher)と重なるので、その下に出す
public class NetTestUI : MonoBehaviour
{
    void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        var button = new GUIStyle(GUI.skin.button) { fontSize = 20 };
        var label = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
        GUILayout.BeginArea(new Rect(10, 60, 260, 200));
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
        }
        GUILayout.EndArea();
    }
}
