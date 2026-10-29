using Nakagawa;
using Unity.Netcode;
using UnityEngine;

// ネット対戦で、表示しているステージをホストに合わせる。シーンに置いた NetworkObject に付ける。
//  ・ホスト: StageSwitcher がステージを切り替えたら番号を配る(左右キーでの切り替えはホストだけ)
//  ・クライアント: 番号が届いたらそのステージを表示する。StageSwitcher のキー操作と、積み木の生成(TumikiSpawner)は止める
//    (積み木はホストが生成したものが届く)
public class NetStageSync : NetworkBehaviour
{
    [SerializeField] StageSwitcher m_switcher;

    readonly NetworkVariable<int> m_stageIndex = new NetworkVariable<int>(-1);

    void Awake()
    {
        if (m_switcher == null) m_switcher = FindFirstObjectByType<StageSwitcher>(FindObjectsInactive.Include);
    }

    public override void OnNetworkSpawn()
    {
        if (m_switcher == null)
        {
            Debug.LogError("[NetStageSync] StageSwitcher が見つかりません");
            return;
        }

        // 接続前にローカルで出ていた積み木(ネットワークのものでない)を片付ける
        foreach (var block in FindObjectsByType<Nakahira.TumikiBlock>(FindObjectsInactive.Include))
        {
            var net = block.GetComponent<NetworkTumiki>();
            if (net == null || net.CreatedOffline) Destroy(block.gameObject);
        }

        if (IsServer)
        {
            m_stageIndex.Value = m_switcher.CurrentIndex;
            m_switcher.StageApplied += OnStageApplied;
            return;
        }

        // クライアントはホストの決めたステージを見るだけ
        m_switcher.enabled = false;
        foreach (var spawner in FindObjectsByType<TumikiSpawner>(FindObjectsInactive.Include))
        {
            spawner.enabled = false;
        }
        Show(m_stageIndex.Value);
        m_stageIndex.OnValueChanged += OnIndexChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (m_switcher == null) return;
        m_switcher.StageApplied -= OnStageApplied;
        m_stageIndex.OnValueChanged -= OnIndexChanged;
    }

    void OnStageApplied(int index)
    {
        if (IsServer) m_stageIndex.Value = index;
    }

    void OnIndexChanged(int prev, int next) => Show(next);

    void Show(int index)
    {
        if (index < 0 || m_switcher == null || index == m_switcher.CurrentIndex) return;
        m_switcher.ShowStage(index);
    }
}
