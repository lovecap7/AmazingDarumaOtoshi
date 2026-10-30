using System;
using Nakahira;
using Unity.Netcode;
using UnityEngine;

// ネット対戦の1人分(接続した人ごとに1つ。NetPlayer プレハブ)。持ち主の入力は同じ GameObject の NetworkCommandSource がホストへ送る。
//  ・ホスト      : MatchPlayer を付けて DarumaMatch に参加させ、毎フレーム中身(状態・ストック・点数・記録・体)をまとめて配る
//  ・クライアント: MatchPlayer を付けて DarumaMatch に並べ、届いた中身を書き込む(HUD・カメラはそれを読む)
// 体(キャラクター・幽霊)は DarumaMatch が出してホストが配るので、ここでは「どの体か」だけを送る。
public class NetMatchPlayer : NetworkBehaviour
{
    public struct Snapshot : INetworkSerializable, IEquatable<Snapshot>
    {
        public bool Ready;
        public byte Index;
        public byte State;
        public sbyte Stocks;
        public short Score, Knocks, Knocked, KOs, Falls, SelfOuts;
        public byte Rank, Stack;
        public float RespawnLeft;
        public bool HasBody;
        public NetworkObjectReference Body;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Ready);
            s.SerializeValue(ref Index);
            s.SerializeValue(ref State);
            s.SerializeValue(ref Stocks);
            s.SerializeValue(ref Score);
            s.SerializeValue(ref Knocks);
            s.SerializeValue(ref Knocked);
            s.SerializeValue(ref KOs);
            s.SerializeValue(ref Falls);
            s.SerializeValue(ref SelfOuts);
            s.SerializeValue(ref Rank);
            s.SerializeValue(ref Stack);
            s.SerializeValue(ref RespawnLeft);
            s.SerializeValue(ref HasBody);
            if (HasBody) s.SerializeValue(ref Body);
        }

        public bool Equals(Snapshot o) =>
            Ready == o.Ready && Index == o.Index && State == o.State && Stocks == o.Stocks && Score == o.Score &&
            Knocks == o.Knocks && Knocked == o.Knocked && KOs == o.KOs && Falls == o.Falls && SelfOuts == o.SelfOuts &&
            Rank == o.Rank && Stack == o.Stack && RespawnLeft == o.RespawnLeft &&
            HasBody == o.HasBody && (!HasBody || Body.Equals(o.Body));
    }

    readonly NetworkVariable<Snapshot> m_snapshot = new NetworkVariable<Snapshot>();

    // ホストが Spawn する前に決める番号(0 = 1P)
    public int Index { get; set; }
    public MatchPlayer Player { get; private set; }
    bool m_registered;

    public override void OnNetworkSpawn()
    {
        Player = gameObject.AddComponent<MatchPlayer>();
        if (!IsServer) return;

        var match = DarumaMatch.Current;
        if (match == null)
        {
            Debug.LogError("[NetMatchPlayer] DarumaMatch がありません");
            return;
        }
        var entry = new MatchSetup.Entry { playerIndex = Index, deviceName = "Net" };
        match.AddNetPlayer(Player, entry, GetComponent<NetworkCommandSource>());
        m_registered = true;
    }

    public override void OnNetworkDespawn()
    {
        var match = DarumaMatch.Current;
        if (match == null || !m_registered) return;
        if (IsServer) match.RemoveNetPlayer(Player);
        else match.NetClientRemovePlayer(Player);
        m_registered = false;
    }

    private void LateUpdate()
    {
        if (!IsSpawned || Player == null) return;
        if (IsServer) ServerSend();
        else ClientApply();
    }

    void ServerSend()
    {
        var p = Player;
        var s = new Snapshot
        {
            Ready = true,
            Index = (byte)Index,
            State = (byte)p.State,
            Stocks = (sbyte)Mathf.Clamp(p.Stocks, sbyte.MinValue, sbyte.MaxValue),
            Score = (short)p.Score,
            Knocks = (short)p.Knocks,
            Knocked = (short)p.Knocked,
            KOs = (short)p.KOs,
            Falls = (short)p.Falls,
            SelfOuts = (short)p.SelfOuts,
            Rank = (byte)p.Rank,
            Stack = (byte)p.StackCount,
            // 復活までの残りは 0.1 秒刻みで十分(毎フレーム送らない)
            RespawnLeft = Mathf.Ceil(Mathf.Max(0.0f, p.RespawnTimeLeft) * 10.0f) / 10.0f,
        };
        var body = BodyObject(p);
        if (body != null)
        {
            s.HasBody = true;
            s.Body = new NetworkObjectReference(body);
        }
        if (!m_snapshot.Value.Equals(s)) m_snapshot.Value = s;
    }

    static NetworkObject BodyObject(MatchPlayer p)
    {
        Component body = p.Character != null ? p.Character : p.Ghost != null ? p.Ghost : null;
        if (body == null) return null;
        var no = body.GetComponent<NetworkObject>();
        return no != null && no.IsSpawned ? no : null;
    }

    void ClientApply()
    {
        var s = m_snapshot.Value;
        if (!s.Ready) return;
        var match = DarumaMatch.Current;
        if (match == null) return;

        if (!m_registered)
        {
            Index = s.Index;
            var entry = new MatchSetup.Entry { playerIndex = s.Index, deviceName = "Net" };
            Player.Setup(entry, DarumaMatch.ColorFor(entry), null, -1);
            match.NetClientAddPlayer(Player);
            m_registered = true;
        }

        Player.NetApply(s.Stocks, s.Score, s.Knocks, s.Knocked, s.KOs, s.Falls, s.SelfOuts, s.Rank, s.Stack, s.RespawnLeft);

        // 体は別に届くので、まだ届いていなければ次のフレームでもう一度探す
        DarumaCharacter character = null;
        DarumaGhost ghost = null;
        if (s.HasBody && s.Body.TryGet(out NetworkObject body))
        {
            character = body.GetComponent<DarumaCharacter>();
            ghost = body.GetComponent<DarumaGhost>();
        }
        Player.NetSetBody((MatchPlayer.PlayerState)s.State, character, ghost);
    }
}
