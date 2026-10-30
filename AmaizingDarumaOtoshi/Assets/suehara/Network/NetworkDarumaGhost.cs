using Nakahira;
using Unity.Netcode;

// ネット対戦用の幽霊(DarumaGhost_Net)に付ける。動き・ハンマーの当たりはホストだけで動かし、クライアントは位置(NetworkTransform)を受け取って見せるだけ。
// ハンマーの振りの見た目は NetworkHammerView が見せる。
public class NetworkDarumaGhost : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (IsServer) return;
        var ghost = GetComponent<DarumaGhost>();
        if (ghost != null) ghost.enabled = false;
    }
}
