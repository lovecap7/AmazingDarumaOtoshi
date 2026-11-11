using UnityEngine;
using Nakahira;
using Unity.Netcode;

// 持ち主の端末で入力を読み、ホストへ送る。ホストでは IDarumaCommandSource として DarumaCharacter に読まれる。
// DarumaCharacter と同じ GameObject(プレハブ)に付けておくと、DarumaCharacter.Awake が自動でこれを操作の供給元にする。
// 操作はキーボードとゲームパッドの両方(NetPadKeyboardInput)。
// 必殺技ボタン・行動ボタン(だるきんぐの詠唱の確定)も一緒に送り、ホストの必殺技(Yurube.SpecialInput)が ConsumeSpecial / ConsumeAnyAction で読む
public class NetworkCommandSource : NetworkBehaviour, IDarumaCommandSource
{
    // 届いたボタンを「押した」とみなすフレーム数(届いたフレームの必殺技の Update で読まれなかったときのため。長いと前の押下で次の操作が出てしまう)
    const int kPressFrames = 2;

    NetPadKeyboardInput m_local;    // 持ち主側の実入力
    DarumaCommand m_received;       // ホスト側:最後に届いた命令
    Vector2 m_lastSent;

    // ホスト側: 届いた必殺技ボタン・行動ボタン(届いたフレーム)
    bool m_special, m_anyAction;
    int m_specialFrame, m_anyActionFrame;

    public override void OnNetworkSpawn()
    {
        // 持ち主の端末だけ、実際の入力を読む部品を付ける
        // (DarumaCharacter は Awake で供給元を決め済みなので、ここで足しても取り違えない)
        if (IsOwner) m_local = gameObject.AddComponent<NetPadKeyboardInput>();
    }

    void Update()
    {
        if (!IsOwner || m_local == null) return;

        DarumaCommand c = m_local.ReadCommand();
        bool special = m_local.ReadSpecialPressed();
        bool any = m_local.ReadAnyActionPressed();
        // 移動は変化したときだけ送る。ボタンは「押したフレーム」の入力なので、押したらすぐ送る
        if (c.Move != m_lastSent || c.Jump || c.Sweep || c.Shot || special || any)
        {
            m_lastSent = c.Move;
            SubmitRpc(c.Move, c.Jump, c.Sweep, c.Shot, special, any);
        }
    }

    [Rpc(SendTo.Server)]
    void SubmitRpc(Vector2 move, bool jump, bool sweep, bool shot, bool special, bool anyAction)
    {
        m_received.Move = move;
        // ボタンは読まれるまで溜めておく(取りこぼし防止)
        m_received.Jump |= jump;
        m_received.Sweep |= sweep;
        m_received.Shot |= shot;
        if (special)
        {
            m_special = true;
            m_specialFrame = Time.frameCount;
        }
        if (anyAction)
        {
            m_anyAction = true;
            m_anyActionFrame = Time.frameCount;
        }
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

    // ホスト: 必殺技ボタンが押されたか(読んだら消える。届いてから kPressFrames を過ぎたものは押していないことにする)
    public bool ConsumeSpecial() => Consume(ref m_special, m_specialFrame);

    // ホスト: 何かの行動ボタンが押されたか
    public bool ConsumeAnyAction() => Consume(ref m_anyAction, m_anyActionFrame);

    static bool Consume(ref bool flag, int frame)
    {
        if (!flag) return false;
        flag = false;
        return Time.frameCount - frame <= kPressFrames;
    }
}
