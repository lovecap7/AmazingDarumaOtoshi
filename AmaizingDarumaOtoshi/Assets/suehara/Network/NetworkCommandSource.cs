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

    // ラグの計測(クライアントだけ): 止まっている状態からスティックを倒して、自分のキャラが動き出すまでの時間を Console に出す
    public static bool LogLag = true;
    const float kLagMoveDistance = 0.05f;   // これだけ動いたら「動き出した」とみなす(m)
    const float kLagTimeout = 1.0f;         // これを過ぎても動かなければ計り直す(壁に向かって倒したときなど)
    float m_lagStart = -1.0f;
    Vector3 m_lagFrom, m_lastBodyPos;
    bool m_wasIdle;

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
        if (LogLag && !IsServer) ProbeLag(c.Move);
    }

    // クライアント: スティックを倒した時刻と、自分のキャラが動き出した時刻の差を出す
    void ProbeLag(Vector2 move)
    {
        var mp = GetComponent<MatchPlayer>();
        var body = mp != null && mp.Character != null ? mp.Character.transform : null;
        if (body == null)
        {
            m_lagStart = -1.0f;
            return;
        }
        Vector3 pos = body.position;
        Vector3 step = pos - m_lastBodyPos;
        step.y = 0.0f;
        m_lastBodyPos = pos;

        if (m_lagStart < 0.0f)
        {
            // スティックを離していて、キャラも止まっていたところから倒したときだけ計る
            if (m_wasIdle && move.sqrMagnitude > 0.25f && step.sqrMagnitude < 0.0001f)
            {
                m_lagStart = Time.realtimeSinceStartup;
                m_lagFrom = pos;
            }
        }
        else
        {
            Vector3 d = pos - m_lagFrom;
            d.y = 0.0f;
            float elapsed = Time.realtimeSinceStartup - m_lagStart;
            if (d.magnitude >= kLagMoveDistance)
            {
                Debug.Log($"[ラグ計測] スティックを倒してから自分のキャラが動き出すまで {elapsed * 1000.0f:0} ms" +
                          $"(往復 {NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId)} ms)");
                m_lagStart = -1.0f;
            }
            else if (elapsed > kLagTimeout) m_lagStart = -1.0f;
        }
        m_wasIdle = move.sqrMagnitude < 0.01f;
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
