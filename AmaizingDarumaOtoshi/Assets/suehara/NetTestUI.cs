using UnityEngine;
using Unity.Netcode;

public class NetTestUI : MonoBehaviour
{
    void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        GUILayout.BeginArea(new Rect(10, 10, 200, 200));
        if (!nm.IsClient && !nm.IsServer)
        {
            if (GUILayout.Button("Host")) nm.StartHost();
            if (GUILayout.Button("Client")) nm.StartClient();
        }
        GUILayout.EndArea();
    }
}
