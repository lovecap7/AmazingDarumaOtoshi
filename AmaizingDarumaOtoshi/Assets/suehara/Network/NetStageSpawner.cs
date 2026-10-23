using Unity.Netcode;
using UnityEngine;

public class NetStageSpawner : MonoBehaviour
{
    [SerializeField] NetworkObject m_characterPrefab;
    // 共有のSpawnPointsの子(StageSwitcerがステージに合わせて動かすもの)を登録する
    [SerializeField] Transform[] m_spawnPoints;

    int m_next;
    bool m_subscribed;
    void Start() => Subscribe();

    void OnDestroy()
    {
        if(m_subscribed && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    void Subscribe()
    {
        if(NetworkManager.Singleton == null)
        {
            Debug.LogWarning("[NetStageSpawner] NerworkManagerが見つかりません");
            return;
        }
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        m_subscribed = true;
    }

    void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if(m_characterPrefab == null)
        {
            Debug.LogError("[NetStageSpawner] Character Prefabが未設定です");
            return;
        }

        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;
        if(m_spawnPoints != null && m_spawnPoints.Length > 0)
        {
            Transform p = m_spawnPoints[m_next % m_spawnPoints.Length];
            pos = p.position;
            rot = p.rotation;
        }
        m_next++;

        NetworkObject obj = Instantiate(m_characterPrefab, pos, rot);
        obj.SpawnAsPlayerObject(clientId, true);
    }
}
