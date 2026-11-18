using System;
using System.Collections.Generic;
using Nakahira;
using Unity.Netcode;
using UnityEngine;

// ネット対戦の1人分(接続した人ごとに1つ。NetPlayer プレハブ)。持ち主の入力は同じ GameObject の NetworkCommandSource がホストへ送る。
//  ・ホスト      : MatchPlayer を付けて DarumaMatch に参加させ、毎フレーム中身(状態・ストック・点数・記録・体)をまとめて配る
//  ・クライアント: MatchPlayer を付けて DarumaMatch に並べ、届いた中身を書き込む(HUD・カメラはそれを読む)
// 体(キャラクター・幽霊)は DarumaMatch が出してホストが配るので、ここでは「どの体か」だけを送る。
// ロビーでの状態(NetStatus)もホストが決めて配る:
//  ・準備完了(ホスト以外が自分で切り替える。キャラ・ステージを変えたら外れる) ・観戦(自分で切り替える。試合に体を出さない)
//  ・試合中に入ってきた人は待機(体を出さない。試合が終わってロビーに戻ったら参加できる)
public class NetMatchPlayer : NetworkBehaviour
{
    // ロビーでの状態
    public enum NetStatus : byte
    {
        Preparing,   // 準備中
        Ready,       // 準備完了
        Spectating,  // 観戦
        Waiting,     // 試合中に入ってきて待っている
        Playing,     // 試合中
        Picking,     // キャラ・ステージを選んでいる
    }

    public struct Snapshot : INetworkSerializable, IEquatable<Snapshot>
    {
        public bool Ready;
        public byte Status;      // NetStatus
        public bool InMatch;     // 試合(DarumaMatch)に参加している(観戦・待機の人は false)
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
        public byte Team;        // チーム戦のチーム(0 赤・1 青・2 緑・3 黄。個人戦でも持っておく)

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Ready);
            s.SerializeValue(ref Status);
            s.SerializeValue(ref InMatch);
            s.SerializeValue(ref Index);
            s.SerializeValue(ref Character);
            s.SerializeValue(ref StageVote);
            s.SerializeValue(ref Team);
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
            Ready == o.Ready && Status == o.Status && InMatch == o.InMatch && Index == o.Index && State == o.State && Stocks == o.Stocks && Score == o.Score &&
            Knocks == o.Knocks && Knocked == o.Knocked && KOs == o.KOs && Falls == o.Falls && SelfOuts == o.SelfOuts &&
            Rank == o.Rank && Stack == o.Stack && RespawnLeft == o.RespawnLeft && Character == o.Character && StageVote == o.StageVote && Team == o.Team &&
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
    // この PC の人が選んだチーム(-1 なら選んでいない。番号から決まるチームのまま)。キャラと同じく部屋にいる間だけ覚えておく
    public static int SavedTeam { get; private set; } = -1;

    // 部屋に入る(ホストになる・さんかする)ときに呼ぶ
    public static void ResetChoices()
    {
        SavedCharacterId = DefaultCharacterId;
        SavedStageId = "";
        SavedTeam = -1;
        s_spectators.Clear();
    }

    // いる人全員(試合に参加していない観戦・待機の人も。ロビーの表示が読む)
    public static readonly List<NetMatchPlayer> All = new List<NetMatchPlayer>();

    // ホスト: 観戦を選んでいる人(clientId。部屋にいる間は試合をまたいで覚えておく)
    static readonly HashSet<ulong> s_spectators = new HashSet<ulong>();
    // ホスト: 準備完了・選んでいる最中・試合中に入ってきた
    bool m_ready, m_picking, m_late;
    // クライアント: 届いた状態
    NetStatus m_status;
    bool m_inMatch;

    // ロビーでの状態(ホストが決めて配る)
    public NetStatus Status => m_status;
    // 試合(DarumaMatch)に参加しているか(観戦・待機の人は false)
    public bool InMatch => IsServer ? m_registered : m_inMatch;
    // ホストの人か
    public bool IsHostPlayer => OwnerClientId == NetworkManager.ServerClientId;
    // ホスト: 準備完了か
    public bool IsReady => m_ready;
    public bool IsSpectating => m_status == NetStatus.Spectating;
    public string Label => $"{Index + 1}P";
    public Color Color => DarumaMatch.ColorFor(new MatchSetup.Entry { playerIndex = Index, deviceName = "Net" });

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

    // チーム戦のチーム(0 赤・1 青・2 緑・3 黄)。最初はキャラクターセレクトと同じく番号から(1P 赤、2P 青…)。ホストが決めて配る
    int m_team;
    public int Team => m_team;
    public static string TeamName(int team) => MatchSetup.TeamNames[((team % MatchSetup.TeamCount) + MatchSetup.TeamCount) % MatchSetup.TeamCount];
    public static Color TeamColor(int team) => MatchSetup.TeamColors[((team % MatchSetup.TeamCount) + MatchSetup.TeamCount) % MatchSetup.TeamCount];
    // いまチーム戦か(ホストが選んでいるルール)
    public static bool TeamBattle => NetMatch.Instance != null && NetMatch.Instance.RulesValue.Teams;

    public override void OnNetworkSpawn()
    {
        Player = gameObject.AddComponent<MatchPlayer>();
        All.Add(this);
        if (IsServer)
        {
            m_team = Index % MatchSetup.TeamCount;
            // 最初はあかだる(その人が別のキャラを選んでいれば、このあと届く頼みで入れ替わる)
            var list = NetCharacterList.Instance;
            int first = list != null ? list.IndexOf(DefaultCharacterId) : NetCharacterList.None;
            if (first != NetCharacterList.None) m_character = first;
            // 試合中に入ってきた人は待機(試合には出さない)。観戦を選んでいる人も出さない
            var net = NetMatch.Instance;
            m_late = net != null && net.IsStarted;
            if (!m_late && !s_spectators.Contains(OwnerClientId)) Register();
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
            if (SavedTeam >= 0) RequestTeam(SavedTeam);
        }
    }

    // ホスト: 試合に参加させる(体を出す)
    void Register()
    {
        var match = DarumaMatch.Current;
        if (m_registered) return;
        if (match == null)
        {
            Debug.LogError("[NetMatchPlayer] DarumaMatch がありません");
            return;
        }
        var entry = new MatchSetup.Entry { playerIndex = Index, deviceName = "Net", team = TeamBattle ? m_team : -1 };
        var list = NetCharacterList.Instance;
        if (list != null && list.Character(m_character) != null) entry.character = list.HostData(m_character);
        match.AddNetPlayer(Player, entry, GetComponent<NetworkCommandSource>());
        m_registered = true;
    }

    // ホスト: 試合から外す(体を消す。観戦にしたとき)
    void Unregister()
    {
        var match = DarumaMatch.Current;
        if (!m_registered || match == null) return;
        match.RemoveNetPlayer(Player);
        m_registered = false;
        RenewMatchPlayer();
    }

    // 試合から外したら MatchPlayer を作り直す(画面下の枠(MatchHud)は、MatchPlayer が消えたときに枠を消すため)
    void RenewMatchPlayer()
    {
        var old = Player;
        Player = gameObject.AddComponent<MatchPlayer>();
        if (old != null) Destroy(old);
    }

    //========================================
    // 準備完了・観戦・選んでいる最中(ロビーの状態)
    //========================================

    // この PC の人: 準備完了を切り替える(ホスト以外。開始待ちの間だけ)
    public void RequestReady(bool ready)
    {
        if (IsOwner) ReadyRpc(ready);
    }

    // この PC の人: 観戦を切り替える(開始待ちの間はいつでも。試合中は試合に出ていない人だけ)
    public void RequestSpectate(bool spectate)
    {
        if (IsOwner) SpectateRpc(spectate);
    }

    // この PC の人: キャラ・ステージを選ぶ画面を開いている(ほかの人の表示に「選んでいる」と出す)
    public void SetPicking(bool picking)
    {
        if (IsOwner) PickingRpc(picking);
    }

    [Rpc(SendTo.Server)]
    void ReadyRpc(bool ready, RpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId != OwnerClientId || IsHostPlayer) return;
        var net = NetMatch.Instance;
        if (net == null || net.IsStarted || net.IsLotteryRunning || !m_registered) return;
        m_ready = ready;
    }

    [Rpc(SendTo.Server)]
    void SpectateRpc(bool spectate, RpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId != OwnerClientId) return;
        var net = NetMatch.Instance;
        if (net == null || net.IsLotteryRunning) return;
        // 試合中は、試合に出ている人は変えられない(待機 ⇔ 観戦の表示と、次の試合の参加だけ変わる)
        if (net.IsStarted && m_registered) return;
        if (spectate) s_spectators.Add(OwnerClientId);
        else s_spectators.Remove(OwnerClientId);
        m_ready = false;
        if (net.IsStarted) return;
        if (spectate) Unregister();
        else Register();
    }

    [Rpc(SendTo.Server)]
    void PickingRpc(bool picking, RpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId == OwnerClientId) m_picking = picking;
    }

    // ホスト: いまの状態
    NetStatus ServerStatus()
    {
        var net = NetMatch.Instance;
        bool spectator = s_spectators.Contains(OwnerClientId);
        if (net != null && net.IsStarted) return m_registered ? NetStatus.Playing : spectator ? NetStatus.Spectating : NetStatus.Waiting;
        if (spectator) return NetStatus.Spectating;
        // 準備完了なら、選ぶ画面を開いていても準備完了と出す(準備完了にして画面を閉じる途中で「えらんでいます」に戻らないように)
        if (m_ready) return NetStatus.Ready;
        return m_picking ? NetStatus.Picking : NetStatus.Preparing;
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
        int vote = list.Stage(index) != null ? index : NetCharacterList.None;
        if (vote != m_stageVote) m_ready = false;   // 変えたら準備完了は外れる
        m_stageVote = vote;
    }

    //========================================
    // チームの選択(チーム戦。開始待ちの間だけ。キャラクターセレクトの旗と同じく 赤 → 青 → 緑 → 黄)
    //========================================

    // この PC の人: チームを選ぶ
    public void RequestTeam(int team)
    {
        if (!IsOwner) return;
        team = ((team % MatchSetup.TeamCount) + MatchSetup.TeamCount) % MatchSetup.TeamCount;
        SavedTeam = team;
        ChooseTeamRpc((byte)team);
    }

    // この PC の人: 次(delta = 1)・前(-1)のチームにする
    public void CycleTeam(int delta) => RequestTeam(m_team + delta);

    [Rpc(SendTo.Server)]
    void ChooseTeamRpc(byte team, RpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId != OwnerClientId || team >= MatchSetup.TeamCount) return;
        var net = NetMatch.Instance;
        if (net == null || net.IsStarted || net.IsLotteryRunning) return;
        if (team != m_team) m_ready = false;   // 変えたら準備完了は外れる
        m_team = team;
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

    // この PC の人: 前のキャラに戻す(選ぶ画面を B で閉じたとき)。random: 前はランダムだった(ランダムのまま同じキャラに戻す)
    public void RestoreCharacter(int index, bool random)
    {
        if (!IsOwner) return;
        RequestCharacter(index);
        if (random) SavedCharacterId = RandomCharacterId;
    }

    // この PC の人: キャラをランダムにする(キャラクターセレクトの「ランダム」と同じく、この PC で選べるキャラから1人)
    public void RequestRandomCharacter()
    {
        if (!IsOwner) return;
        var list = NetCharacterList.Instance;
        if (list == null) return;
        // 選ぶ画面に並んでいるキャラから選ぶ(キャラクターセレクトのランダムと同じ。並びに無い試作のキャラは出さない)
        var choices = new System.Collections.Generic.List<int>();
        foreach (var c in list.SelectableCharacters()) choices.Add(list.IndexOf(c.id));
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
        if (list == null || match == null || !match.IsNetWaiting) return;
        var data = list.HostData(index);
        if (data == null || index == m_character) return;
        m_character = index;
        m_ready = false;   // 変えたら準備完了は外れる
        // 観戦の人は体が無いので、キャラだけ覚えておく(参加したときにそのキャラで出る)
        if (!m_registered) return;
        Player.Entry.character = data;
        match.NetRespawnPlayer(Player);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
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
        m_status = ServerStatus();
        // チーム(DarumaMatch は Entry.team で味方・勝ち負けを決める。個人戦は -1。試合中はルールもチームも変わらない)
        if (m_registered && p.Entry != null) p.Entry.team = TeamBattle ? m_team : -1;
        var s = new Snapshot
        {
            Ready = true,
            Status = (byte)m_status,
            InMatch = m_registered,
            Index = (byte)Index,
            Character = (byte)m_character,
            StageVote = (byte)m_stageVote,
            Team = (byte)m_team,
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

        Index = s.Index;
        m_status = (NetStatus)s.Status;
        m_inMatch = s.InMatch;
        m_stageVote = s.StageVote;
        m_team = s.Team;

        // 試合に参加している人だけ DarumaMatch に並べる(観戦・待機の人は HUD・カメラに出さない)
        if (s.InMatch && !m_registered)
        {
            var entry = new MatchSetup.Entry { playerIndex = s.Index, deviceName = "Net" };
            Player.Setup(entry, DarumaMatch.ColorFor(entry), null, -1);
            match.NetClientAddPlayer(Player);
            m_registered = true;
            m_character = NetCharacterList.None;   // キャラは下で入れ直す
        }
        else if (!s.InMatch && m_registered)
        {
            match.NetClientRemovePlayer(Player);
            m_registered = false;
            RenewMatchPlayer();
        }
        if (!m_registered)
        {
            m_character = s.Character;
            return;
        }

        // キャラ(名前・顔のモデル。HUD・リザルト・頭のモデルの差し替えが使う)
        if (s.Character != m_character)
        {
            m_character = s.Character;
            var list = NetCharacterList.Instance;
            Player.Entry.character = list != null ? list.Character(m_character) : null;
        }

        // チーム(リザルトの「〇チーム」・味方の判定に使う。個人戦は -1)
        if (Player.Entry != null) Player.Entry.team = TeamBattle ? m_team : -1;
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
