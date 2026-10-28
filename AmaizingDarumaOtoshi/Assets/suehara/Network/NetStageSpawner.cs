using System.Collections.Generic;
using Nakagawa;
using Unity.Netcode;
using UnityEngine;

// ネット対戦で、接続した人ごとにキャラクターを出す(ホストだけが行う)。
// ステージを切り替えたら、全員のキャラクターを消して新しいステージの出現位置に出し直す。
public class NetStageSpawner : MonoBehaviour
{
    [SerializeField] NetworkObject m_characterPrefab;
    // 共有のSpawnPointsの子(StageSwitcherがステージに合わせて動かすもの)を登録する
    [SerializeField] Transform[] m_spawnPoints;
    // 未設定ならシーンから探す
    [SerializeField] StageSwitcher m_switcher;

    // 接続した人ごとの出現位置の番号(ステージを変えても同じ番号の位置に出す)
    readonly Dictionary<ulong, int> m_slots = new Dictionary<ulong, int>();
    int m_next;
    bool m_subscribed;

    void Start()
    {
        if (m_switcher == null) m_switcher = FindFirstObjectByType<StageSwitcher>(FindObjectsInactive.Include);
        if (m_switcher != null)
        {
            m_switcher.SwitchStarting += OnSwitchStarting;
            m_switcher.StageApplied += OnStageApplied;
        }
        Subscribe();
    }

    void OnDestroy()
    {
        if (m_switcher != null)
        {
            m_switcher.SwitchStarting -= OnSwitchStarting;
            m_switcher.StageApplied -= OnStageApplied;
        }
        if (m_subscribed && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    void Subscribe()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("[NetStageSpawner] NetworkManagerが見つかりません");
            return;
        }
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        m_subscribed = true;
    }

    static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;
        if (!m_slots.ContainsKey(clientId)) m_slots[clientId] = m_next++;
        SpawnFor(clientId);
    }

    void OnClientDisconnected(ulong clientId)
    {
        // キャラクター(PlayerObject)は NGO が消す
        m_slots.Remove(clientId);
    }

    // ステージの切り替えが始まった: 前のステージのキャラクターを消す
    // (積んでいる積み木は StageSwitcher が片付ける)
    void OnSwitchStarting()
    {
        if (!IsServer) return;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null || !client.PlayerObject.IsSpawned) continue;
            // 積んでいる積み木(これも NetworkObject)を子から外してから消す(積み木はこのあと StageSwitcher が片付ける)
            foreach (var block in client.PlayerObject.GetComponentsInChildren<Nakahira.TumikiBlock>(true)) block.transform.SetParent(null, true);
            client.PlayerObject.Despawn(true);
        }
    }

    // 新しいステージが表示された: 全員を出現位置に出し直す
    void OnStageApplied(int index)
    {
        if (!IsServer) return;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null && client.PlayerObject.IsSpawned) continue;
            SpawnFor(client.ClientId);
        }
    }

    void SpawnFor(ulong clientId)
    {
        if (m_characterPrefab == null)
        {
            Debug.LogError("[NetStageSpawner] Character Prefabが未設定です");
            return;
        }

        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;
        if (m_spawnPoints != null && m_spawnPoints.Length > 0)
        {
            int slot = m_slots.TryGetValue(clientId, out int s) ? s : 0;
            Transform p = m_spawnPoints[slot % m_spawnPoints.Length];
            pos = p.position;
            rot = p.rotation;
        }

        NetworkObject obj = Instantiate(m_characterPrefab, pos, rot);
        obj.SpawnAsPlayerObject(clientId, true);
    }
}
