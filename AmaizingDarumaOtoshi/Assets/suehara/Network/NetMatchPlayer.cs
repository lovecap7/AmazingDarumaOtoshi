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
        public byte Character;   // キャラの番号(NetCharacterList の並び。NetCharacterList.None なら基本のだるま)
        public byte StageVote;   // 選んだステージの番号(NetCharacterList.stages の並び。NetCharacterList.None ならおまかせ)

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Ready);
            s.SerializeValue(ref Index);
            s.SerializeValue(ref Character);
            s.SerializeValue(ref StageVote);
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
            Rank == o.Rank && Stack == o.Stack && RespawnLeft == o.RespawnLeft && Character == o.Character && StageVote == o.StageVote &&
            HasBody == o.HasBody && (!HasBody || Body.Equals(o.Body));
    }

    readonly NetworkVariable<Snapshot> m_snapshot = new NetworkVariable<Snapshot>();

    // ホストが Spawn する前に決める番号(0 = 1P)
    public int Index { get; set; }
    public MatchPlayer Player { get; private set; }
    bool m_registered;
    // いまのキャラの番号(ホストが決めて配る)
    int m_character = NetCharacterList.None;

    // 部屋に入ったときのキャラ(あかだる)とステージ(おまかせ)
    public const string DefaultCharacterId = "akadaru";

    // この PC の人が選んだキャラ・ステージ(試合のシーンに入るたびにホストへ送る)。
    // 同じ部屋にいる間(試合 → リザルト → ロビー)は覚えておき、部屋に入るたび(ResetChoices)にあかだる・おまかせへ戻す
    public static string SavedCharacterId { get; private set; } = DefaultCharacterId;
    public static string SavedStageId { get; private set; } = "";
    // キャラを「ランダム」にしたときの SavedCharacterId(試合のシーンに入るたびに、この PC で選べるキャラから選び直す)
    const string RandomCharacterId = "*random";
    public static bool IsRandomCharacter => SavedCharacterId == RandomCharacterId;

    // 部屋に入る(ホストになる・さんかする)ときに呼ぶ
    public static void ResetChoices()
    {
        SavedCharacterId = DefaultCharacterId;
        SavedStageId = "";
    }

    // ドメインの再読み込みをしない設定でも、再生のたびに戻す
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ResetChoices();

    // この PC の人の1人分(なければ null)
    public static NetMatchPlayer Local
    {
        get
        {
            var nm = NetworkManager.Singleton;
            var obj = nm != null && nm.IsListening ? nm.LocalClient?.PlayerObject : null;
            return obj != null ? obj.GetComponent<NetMatchPlayer>() : null;
        }
    }

    // いまのキャラの番号(NetCharacterList の並び。NetCharacterList.None なら基本のだるま)
    public int CharacterIndex => m_character;

    // 選んだステージ(NetCharacterList.stages の番号。NetCharacterList.None ならおまかせ)。ホストが試合を始めるときに抽選する
    int m_stageVote = NetCharacterList.None;
    public int StageVote => m_stageVote;

    public override void OnNetworkSpawn()
    {
        Player = gameObject.AddComponent<MatchPlayer>();
        if (IsServer)
        {
            var match = DarumaMatch.Current;
            if (match == null) Debug.LogError("[NetMatchPlayer] DarumaMatch がありません");
            else
            {
                var entry = new MatchSetup.Entry { playerIndex = Index, deviceName = "Net" };
                // 最初はあかだるで出す(その人が別のキャラを選んでいれば、このあと届く頼みで入れ替わる)
                var list = NetCharacterList.Instance;
                int first = list != null ? list.IndexOf(DefaultCharacterId) : NetCharacterList.None;
                if (first != NetCharacterList.None)
                {
                    entry.character = list.HostData(first);
                    m_character = first;
                }
                match.AddNetPlayer(Player, entry, GetComponent<NetworkCommandSource>());
                m_registered = true;
            }
        }

        // 選んでいるキャラ・ステージをホストに伝える(試合のシーンに入るたびに NetPlayer は作り直されるので、そのたびに送る。
        // 部屋に入ったばかりなら、あかだる・おまかせ)
        if (IsOwner)
        {
            var list = NetCharacterList.Instance;
            if (IsRandomCharacter) RequestRandomCharacter();
            else
            {
                int saved = list != null ? list.IndexOf(SavedCharacterId) : NetCharacterList.None;
                if (saved != NetCharacterList.None && list.IsAvailable(saved)) RequestCharacter(saved);
            }
            int stage = list != null ? list.StageIndexOf(SavedStageId) : NetCharacterList.None;
            if (stage != NetCharacterList.None) RequestStage(stage);
        }
    }

    //========================================
    // ステージの選択(開始待ちの間だけ。ホストが試合を始めるときに、全員が選んだものから抽選する)
    //========================================

    // この PC の人: ステージを選ぶ(NetCharacterList.None ならおまかせ)
    public void RequestStage(int index)
    {
        if (!IsOwner) return;
        var list = NetCharacterList.Instance;
        var stage = list != null ? list.Stage(index) : null;
        SavedStageId = stage != null ? stage.id : "";
        ChooseStageRpc((byte)(stage != null ? index : NetCharacterList.None));
    }

    [Rpc(SendTo.Server)]
    void ChooseStageRpc(byte index, RpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId != OwnerClientId) return;
        var list = NetCharacterList.Instance;
        var net = NetMatch.Instance;
        if (list == null || net == null || net.IsStarted || net.IsLotteryRunning) return;
        m_stageVote = list.Stage(index) != null ? index : NetCharacterList.None;
    }

    //========================================
    // キャラの選択(開始待ちの間だけ変えられる)
    //========================================

    // この PC の人: キャラを選ぶ(ホストに頼む。選べたら体が入れ替わる)
    public void RequestCharacter(int index)
    {
        if (!IsOwner) return;
        var list = NetCharacterList.Instance;
        if (list != null && list.Character(index) != null) SavedCharacterId = list.Character(index).id;
        ChooseCharacterRpc((byte)Mathf.Clamp(index, 0, NetCharacterList.None));
    }

    // この PC の人: キャラをランダムにする(キャラクターセレクトの「ランダム」と同じく、この PC で選べるキャラから1人)
    public void RequestRandomCharacter()
    {
        if (!IsOwner) return;
        var list = NetCharacterList.Instance;
        if (list == null) return;
        var choices = new System.Collections.Generic.List<int>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list.Character(i) != null && list.IsAvailable(i)) choices.Add(i);
        }
        if (choices.Count == 0) return;
        SavedCharacterId = RandomCharacterId;
        ChooseCharacterRpc((byte)choices[UnityEngine.Random.Range(0, choices.Count)]);
    }

    [Rpc(SendTo.Server)]
    void ChooseCharacterRpc(byte index, RpcParams rpc = default)
    {
        // 持ち主からの頼みだけ受ける
        if (rpc.Receive.SenderClientId != OwnerClientId) return;
        var list = NetCharacterList.Instance;
        var match = DarumaMatch.Current;
        if (list == null || match == null || !m_registered || !match.IsNetWaiting) return;
        var data = list.HostData(index);
        if (data == null || index == m_character) return;
        m_character = index;
        Player.Entry.character = data;
        match.NetRespawnPlayer(Player);
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
            Character = (byte)m_character,
            StageVote = (byte)m_stageVote,
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

        m_stageVote = s.StageVote;

        // キャラ(名前・顔のモデル。HUD・リザルト・頭のモデルの差し替えが使う)
        if (s.Character != m_character)
        {
            m_character = s.Character;
            var list = NetCharacterList.Instance;
            Player.Entry.character = list != null ? list.Character(m_character) : null;
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
