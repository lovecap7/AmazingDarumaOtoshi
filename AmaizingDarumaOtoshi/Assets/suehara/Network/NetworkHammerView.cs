using Nakahira;
using Unity.Netcode;
using UnityEngine;

// ハンマーの振りをクライアントに見せる。キャラクター・幽霊のネット用プレハブに付ける。
//  ・ホスト      : 振り始めたら(動作が終わってすぐ次を始めたときも)クライアントに知らせる
//  ・クライアント: ハンマーは当たり判定・発射をせず(ViewOnly)、知らせが来たら同じ振りを最初から再生する
//  ・ヒットストップ(当たった瞬間に止まって震える)も、ホストで始まったらクライアントで同じように始める(NetHitStopWatch)
public class NetworkHammerView : NetworkBehaviour
{
    DarumaHammer[] m_hammers;
    bool[] m_prevBusy;
    float[] m_prevTime;
    // ヒットストップ(キャラ・幽霊の HitStop)をクライアントにも出す
    HitStop m_hitStop;
    NetHitStopWatch m_hitStopWatch;
    // ワープなどの瞬間移動を、クライアントでもすべらせずに移す
    NetTeleportWatch m_teleportWatch;

    public override void OnNetworkSpawn()
    {
        m_hammers = GetComponentsInChildren<DarumaHammer>(true);
        m_prevBusy = new bool[m_hammers.Length];
        m_prevTime = new float[m_hammers.Length];
        m_hitStop = GetComponent<HitStop>();
        if (IsServer)
        {
            m_hitStopWatch = new NetHitStopWatch(m_hitStop);
            m_teleportWatch = new NetTeleportWatch(this);
            return;
        }
        foreach (var hammer in m_hammers) hammer.ViewOnly = true;
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsServer || m_hammers == null) return;
        if (m_hitStopWatch != null && m_hitStopWatch.Poll(out float duration, out float scale, out float shake)) HitStopRpc(duration, scale, shake);
        m_teleportWatch?.Update();
        for (int i = 0; i < m_hammers.Length; i++)
        {
            DarumaHammer h = m_hammers[i];
            if (h == null) continue;
            bool busy = h.IsBusy;
            // 動作が終わってすぐ次を始めたとき(経過時間が巻き戻った)も新しい振りとみなす
            bool started = busy && (!m_prevBusy[i] || h.ActionTime < m_prevTime[i]);
            if (started) PlayHammerRpc((byte)i, h.IsShooting);
            m_prevBusy[i] = busy;
            m_prevTime[i] = h.ActionTime;
        }
    }

    [Rpc(SendTo.NotServer)]
    void PlayHammerRpc(byte index, bool shot)
    {
        if (m_hammers == null || index >= m_hammers.Length) return;
        m_hammers[index].PlayVisual(shot);
    }

    [Rpc(SendTo.NotServer)]
    void HitStopRpc(float duration, float timeScale, float shake)
    {
        if (m_hitStop != null) m_hitStop.Begin(duration, timeScale, shake);
    }
}
