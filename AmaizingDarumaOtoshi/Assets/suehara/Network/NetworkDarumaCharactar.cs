using System.Reflection;
using Nakahira;
using Unity.Netcode;
using UnityEngine;

// ネットワーク対応のキャラクターに付ける。
// 物理・当たり判定・ルールはホストだけで動かし、クライアントは表示専用にする。
// (クライアントでも動いたままだと、奈落判定の Eliminate → Destroy などがクライアント側で勝手に走るため)
//  ・ハンマー: クライアントでは「見た目だけ」動かし、ホストで振り始めたら同じ振りを再生する
//  ・頭(本体)の高さ: 積み木の段数をホストから受け取り、クライアントで DarumaStack と同じように頭を上下させる
//    (積み木そのものは NetworkTumiki が位置を同期する)
public class NetworkDarumaCharacter : NetworkBehaviour
{
    readonly NetworkVariable<byte> m_stackCount = new NetworkVariable<byte>();

    DarumaHammer[] m_hammers;
    bool[] m_prevBusy;
    float[] m_prevTime;

    DarumaStack m_stack;
    Transform m_head;
    float m_fallSpeed = 8.0f;

    public override void OnNetworkSpawn()
    {
        m_hammers = GetComponentsInChildren<DarumaHammer>(true);
        m_prevBusy = new bool[m_hammers.Length];
        m_prevTime = new float[m_hammers.Length];
        m_stack = GetComponentInChildren<DarumaStack>(true);

        if (IsServer) return;

        var character = GetComponent<DarumaCharacter>();
        if (character != null) character.enabled = false;

        // ハンマーは当たり判定・発射をせず、振る見た目だけ動かす
        foreach (var hammer in m_hammers) hammer.ViewOnly = true;

        // 積み木を持っていないので、DarumaStack の代わりにここで頭の高さを動かす
        if (m_stack != null)
        {
            const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            m_head = typeof(DarumaStack).GetField("m_head", f)?.GetValue(m_stack) as Transform;
            var speed = typeof(DarumaStack).GetField("m_fallSpeed", f);
            if (speed != null) m_fallSpeed = (float)speed.GetValue(m_stack);
            m_stack.enabled = false;
        }
        // Rigidbody を動かさない設定(Kinematic)は NetworkRigidbody が行う
    }

    private void LateUpdate()
    {
        if (!IsSpawned) return;
        if (IsServer) ServerLateUpdate();
        else ClientLateUpdate();
    }

    void ServerLateUpdate()
    {
        if (m_stack != null && m_stackCount.Value != (byte)m_stack.Count) m_stackCount.Value = (byte)m_stack.Count;

        // ハンマーを振り始めたらクライアントに知らせる
        if (m_hammers == null) return;
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

    // DarumaStack.LateUpdate の頭の部分と同じ動き
    void ClientLateUpdate()
    {
        if (m_head == null || m_stack == null) return;
        Vector3 target = new Vector3(0.0f, m_stackCount.Value * TumikiBlock.kHeight + m_stack.HeadRadius, 0.0f);
        m_head.localPosition = Vector3.MoveTowards(m_head.localPosition, target, m_fallSpeed * Time.deltaTime);
    }

    [Rpc(SendTo.NotServer)]
    void PlayHammerRpc(byte index, bool shot)
    {
        if (m_hammers == null || index >= m_hammers.Length) return;
        m_hammers[index].PlayVisual(shot);
    }
}
