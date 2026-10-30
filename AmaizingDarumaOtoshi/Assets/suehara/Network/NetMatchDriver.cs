using Unity.Netcode;
using UnityEngine;

// DarumaMatch と同じ GameObject に付けて、ネット対戦の試合にする(NetMatch が接続したときに付ける)。
// ホストで出したキャラクター・幽霊を、ネットワークに乗せて全員に配る。
public class NetMatchDriver : MonoBehaviour, INetMatchDriver
{
    public bool IsServer { get; set; }

    public void OnBodySpawned(GameObject body)
    {
        if (!IsServer) return;
        var no = body.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError($"[NetMatchDriver] {body.name} に NetworkObject がありません。Match のキャラクター・幽霊にはネット用プレハブ(*_Net)を設定してください");
            return;
        }
        no.Spawn(true);
    }
}
