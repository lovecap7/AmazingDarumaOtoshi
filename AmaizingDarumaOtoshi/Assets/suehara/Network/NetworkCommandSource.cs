using UnityEngine;
using Nakahira;
using Unity.Netcode;

// 持ち主の端末で入力を読み、ホストへ送る。ホストでは IDarumaCommandSource として DarumaCharacter に読まれる。
// DarumaCharacter と同じ GameObject(プレハブ)に付けておくと、DarumaCharacter.Awake が自動でこれを操作の供給元にする。
// いまはキーボードのみ(WASD/矢印:移動 Space:ジャンプ J:薙ぎ払い K:ショット)。ゲームパッドは後で足す。
public class NetworkCommandSource : NetworkBehaviour, IDarumaCommandSource
{
    IDarumaCommandSource m_local;   // 持ち主側の実入力
    DarumaCommand m_received;       // ホスト側:最後に届いた命令
    Vector2 m_lastSent;

    public override void OnNetworkSpawn()
    {
        // 持ち主の端末だけ、実際の入力を読む部品を付ける
        // (DarumaCharacter は Awake で供給元を決め済みなので、ここで足しても取り違えない)
        if (IsOwner) m_local = gameObject.AddComponent<KeyboardDarumaInput>();
    }

    void Update()
    {
        if (!IsOwner || m_local == null) return;

        DarumaCommand c = m_local.ReadCommand();
        // 移動は変化したときだけ送る。ボタンは「押したフレーム」の入力なので、押したらすぐ送る
        if (c.Move != m_lastSent || c.Jump || c.Sweep || c.Shot)
        {
            m_lastSent = c.Move;
            SubmitRpc(c.Move, c.Jump, c.Sweep, c.Shot);
        }
    }

    [Rpc(SendTo.Server)]
    void SubmitRpc(Vector2 move, bool jump, bool sweep, bool shot)
    {
        m_received.Move = move;
        // ボタンは読まれるまで溜めておく(取りこぼし防止)
        m_received.Jump |= jump;
        m_received.Sweep |= sweep;
        m_received.Shot |= shot;
    }

    // ホストの DarumaCharacter が毎フレーム読む。ボタンは1回読んだら消す
    public DarumaCommand ReadCommand()
    {
        DarumaCommand c = m_received;
        m_received.Jump = false;
        m_received.Sweep = false;
        m_received.Shot = false;
        return c;
    }
}
