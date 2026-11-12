using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

// セレクト画面で決まった「誰が・どの入力機器で・どのキャラを使うか」と「ステージ」を、ゲーム本編へ渡す。
//
// ■ プレイヤーとキャラの取り違えを防ぐための決まりごと
//   ・1人分の情報（プレイヤー番号・入力機器・キャラ）は必ず1つの Entry にまとめて持つ。
//     「キャラの配列」と「コントローラーの配列」を別々に持って番号で突き合わせる、といったことはしない
//   ・プレイヤー番号（0=1P, 1=2P…）が唯一の目印。リストの何番目か、Gamepad.all の何番目かは当てにしない
//     （コントローラーの並び順は抜き差しで変わるし、途中参加・離脱で番号が飛ぶこともある）
//   ・ゲーム本編では ForPlayer(番号) か、Players を順に回して Entry の playerIndex / Device / character を使う
//     例）PlayerInput.Instantiate(e.character.prefab, playerIndex: e.playerIndex, pairWithDevice: e.Device);
//   ・CPU は isCpu = true（Device は null、強さは cpuLevel）
public static class MatchSetup
{
    // CPU の強さの段階数（セレクトで 1〜この数を選ぶ）
    public const int MaxCpuLevel = 3;

    // ストーリー用(Yurube): 画面の名札を「1P」「CPU」ではなく、だるまの名前で出す
    public static bool StoryNames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => StoryNames = false;

    public class Entry
    {
        public int playerIndex;         // 0 = 1P, 1 = 2P, ...
        public bool isCpu;
        public int cpuLevel;            // CPU の強さ（1〜MaxCpuLevel。1 = よわい、2 = ふつう、3 = つよい）
        public int deviceId = -1;       // InputDevice.deviceId（CPU は -1）
        public string deviceName;
        public CharacterData character;
        public int cpuNumber;           // CPU が複数いるときの番号（1〜）。CPU が1人だけなら 0
        // この人だけストック数を変えたいとき（0 ならルールのストック数）。ストーリーのチュートリアル用
        public int stocks;
        // ストーリー用(Yurube): 同じキャラでも章ごとに名札を変える（例：巌高校 選手A）。空ならキャラの名前
        public string displayNameOverride;
        // チーム戦のチーム（0 = 赤、1 = 青、2 = 緑、3 = 黄）。個人戦なら -1
        // 同じチームどうしかは MatchSetup.SameTeam(a, b) で調べる（個人戦では自分以外みんな敵）
        public int team = -1;

        public InputDevice Device => isCpu ? null : InputSystem.GetDeviceById(deviceId);
        public string PlayerLabel => (playerIndex + 1) + "P";
        // 画面に出す名前：「1P」、CPU は「CPU」（複数いるときは「CPU1」「CPU2」）。
        // ストーリー中は、だるまの名前（きだる／みだる）。displayNameOverride があればそちらを優先
        public string DisplayLabel => StoryNames && character != null
            ? (!string.IsNullOrEmpty(displayNameOverride) ? displayNameOverride : character.displayName)
            : !isCpu ? PlayerLabel : cpuNumber > 0 ? "CPU" + cpuNumber : "CPU";
    }

    // 試合のモード
    //   Stock：ストックが無くなるまで戦う。最後の1人が勝ち（時間制限ありなら、時間切れでストックが一番多い人）
    //   Time ：決めた時間いっぱい戦う。崩す +1・崩される -1・顔を飛ばす +headPoints。一番点が多い人が勝ち
    //   Ghost：最後の1人になるまで戦う。やられても幽霊になり、生き残っている相手を叩けば復活できる
    //          （時間制限ありなら、時間切れで生き残っている中で積み木が一番多い人）
    public enum MatchMode { Stock, Time, Ghost }

    // モードセレクト・ルールセレクトで決めたルール。キャラクターセレクトと違い、次の試合にも引き継ぐ（Clear では消さない）
    public class MatchRules
    {
        public MatchMode mode = MatchMode.Stock;
        public int stocks = 3;                  // ストック：ストック数（1〜3）
        public float stockTimeLimit = 0f;       // ストック：時間制限（秒）。0 = なし
        public float timeLength = 120f;         // タイム：試合の時間（秒）
        public float ghostTimeLimit = 0f;       // ゴースト：時間制限（秒）。0 = なし

        // タイムの点数
        public int knockPoints = 1;             // 相手の積み木を崩した
        public int knockedPoints = -1;          // 自分の積み木を崩された
        public int headPoints = 1;              // 相手の顔を飛ばした
        public int selfOutPoints = -1;          // 相手なしでやられた（自分から場外に落ちた など）

        // チーム戦（キャラクターセレクトの上のボタンで切り替える）。チームは Entry.team
        public bool teams;

        public bool Ghost => mode == MatchMode.Ghost;
        // 試合の時間制限（秒）。0 = なし
        public float TimeLimit => mode == MatchMode.Time ? timeLength : mode == MatchMode.Stock ? stockTimeLimit : ghostTimeLimit;

        public string ModeName => mode == MatchMode.Stock ? "ストック" : mode == MatchMode.Time ? "タイム" : "ゴースト";
    }

    static readonly List<Entry> players = new List<Entry>();

    // プレイヤー番号の小さい順
    public static IReadOnlyList<Entry> Players => players;
    public static StageData Stage { get; private set; }
    public static MatchRules Rules { get; } = new MatchRules();

    public static void Set(IEnumerable<Entry> entries, StageData stage)
    {
        players.Clear();
        players.AddRange(entries.OrderBy(e => e.playerIndex));
        Stage = stage;
    }

    public static Entry ForPlayer(int playerIndex) => players.Find(e => e.playerIndex == playerIndex);

    // チームの名前と色（0 = 赤、1 = 青、2 = 緑、3 = 黄）
    public const int TeamCount = 4;
    public static readonly string[] TeamNames = { "赤", "青", "緑", "黄" };
    public static readonly Color[] TeamColors =
    {
        new Color(0.93f, 0.27f, 0.25f),
        new Color(0.22f, 0.55f, 0.95f),
        new Color(0.30f, 0.74f, 0.35f),
        new Color(0.98f, 0.76f, 0.12f),
    };

    // チーム戦か（チームが決まっているのはチーム戦のときだけ）
    public static bool IsTeamBattle => Rules.teams;

    // 2人が同じチームか（個人戦なら、同じ人のときだけ true）
    public static bool SameTeam(int playerIndexA, int playerIndexB)
    {
        if (playerIndexA == playerIndexB) return true;
        if (!IsTeamBattle) return false;
        var a = ForPlayer(playerIndexA);
        var b = ForPlayer(playerIndexB);
        return a != null && b != null && a.team >= 0 && a.team == b.team;
    }

    public static void Clear()
    {
        players.Clear();
        Stage = null;
    }

    // 確認用の一覧（例：「1P あかだる (Keyboard) / 2P へびだる (Xbox Controller) / ステージ：…」）
    public static string Describe()
    {
        var sb = new StringBuilder();
        foreach (var e in players)
            sb.Append(e.DisplayLabel).Append(' ').Append(e.character != null ? e.character.displayName : "(なし)")
              .Append(" (").Append(e.isCpu ? "Lv" + e.cpuLevel : e.deviceName)
              .Append(e.team >= 0 ? " " + TeamNames[e.team % TeamCount] + "チーム" : "").Append(")  ");
        sb.Append(Rules.teams ? "チーム戦  " : "個人戦  ");
        sb.Append("ステージ：").Append(Stage != null ? Stage.displayName : "(なし)");
        sb.Append("  モード：").Append(Rules.ModeName);
        if (Rules.mode == MatchMode.Stock) sb.Append(" ").Append(Rules.stocks).Append("ストック");
        sb.Append("  時間制限：").Append(Rules.TimeLimit > 0f ? Mathf.RoundToInt(Rules.TimeLimit) + "秒" : "なし");
        return sb.ToString();
    }
}
