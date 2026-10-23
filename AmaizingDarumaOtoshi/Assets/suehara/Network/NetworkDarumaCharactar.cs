using Nakahira;
using Unity.Netcode;
using UnityEngine;

// ネットワーク対応のキャラクターに付ける。
// 物理・当たり判定・ルールはホストだけで動かし、クライアントは表示専用にする。
// (クライアントでも動いたままだと、奈落判定の Eliminate → Destroy などがクライアント側で勝手に走るため)
public class NetworkDarumaCharacter : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (IsServer) return;

        var character = GetComponent<DarumaCharacter>();
        if (character != null) character.enabled = false;

        foreach (var hammer in GetComponentsInChildren<DarumaHammer>(true))
        {
            hammer.enabled = false;
        }
        // Rigidbody を動かさない設定(Kinematic)は NetworkRigidbody が行う
    }
}
