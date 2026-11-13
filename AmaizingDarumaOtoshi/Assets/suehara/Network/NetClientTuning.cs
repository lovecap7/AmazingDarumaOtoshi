using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// クライアントの見た目の遅れを減らす(ラグ対策)。NetMatch と同じ GameObject(シーンの NetworkObject)に付ける。
// ホストで動いた結果がクライアントに映るまでには、次の待ちが重なっている:
//  ・ホストの時刻をどれだけ遅らせて映すか(NetworkTimeSystem.ServerBufferSec。NGO の既定は 0.05 秒)
//  ・NetworkTransform の「なめらかにする追いかけ」(LerpSmoothing。届いた位置へ約 0.1 秒かけて寄っていく)
// どちらもクライアントの表示だけの設定で、ホスト・ローカル対戦には影響しない。
// (通信の刻み(NetworkManager の TickRate)は 60 にしてある。届く間隔が短いので、追いかけなしでもなめらかに動く)
public class NetClientTuning : NetworkBehaviour
{
    // ホストの時刻を何刻み分遅らせて映すか(1 = 1/60 秒。届くのが遅れがちな回線でカクつくなら増やす)
    const double kServerBufferTicks = 1.0;
    // 届いた位置へなめらかに寄せるか(オフにすると遅れは減るが、届く間隔が乱れるとカクつきやすい)
    const bool kLerpSmoothing = false;

    readonly HashSet<NetworkObject> m_tuned = new HashSet<NetworkObject>();
    float m_nextPrune;

    public override void OnNetworkSpawn()
    {
        if (IsServer) return;
        var nm = NetworkManager;
        nm.NetworkTimeSystem.ServerBufferSec = kServerBufferTicks / nm.NetworkConfig.TickRate;
    }

    public override void OnNetworkDespawn()
    {
        m_tuned.Clear();
    }

    private void Update()
    {
        if (!IsSpawned || IsServer) return;
        // 新しく届いた NetworkObject(キャラ・積み木・幽霊など)の NetworkTransform に設定を入れる
        foreach (var no in NetworkManager.SpawnManager.SpawnedObjectsList)
        {
            if (no == null || !m_tuned.Add(no)) continue;
            foreach (var nt in no.GetComponentsInChildren<NetworkTransform>(true))
            {
                nt.PositionLerpSmoothing = kLerpSmoothing;
                nt.RotationLerpSmoothing = kLerpSmoothing;
                nt.ScaleLerpSmoothing = kLerpSmoothing;
            }
        }
        // 消えたものを時々片付ける
        if (Time.unscaledTime >= m_nextPrune)
        {
            m_nextPrune = Time.unscaledTime + 5.0f;
            m_tuned.RemoveWhere(no => no == null);
        }
    }
}
