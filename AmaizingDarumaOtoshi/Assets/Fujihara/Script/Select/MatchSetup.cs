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
    public class Entry
    {
        public int playerIndex;         // 0 = 1P, 1 = 2P, ...
        public bool isCpu;
        public int cpuLevel;            // CPU の強さ（1〜9）
        public int deviceId = -1;       // InputDevice.deviceId（CPU は -1）
        public string deviceName;
        public CharacterData character;

        public InputDevice Device => isCpu ? null : InputSystem.GetDeviceById(deviceId);
        public string PlayerLabel => (playerIndex + 1) + "P";
    }

    static readonly List<Entry> players = new List<Entry>();

    // プレイヤー番号の小さい順
    public static IReadOnlyList<Entry> Players => players;
    public static StageData Stage { get; private set; }

    public static void Set(IEnumerable<Entry> entries, StageData stage)
    {
        players.Clear();
        players.AddRange(entries.OrderBy(e => e.playerIndex));
        Stage = stage;
    }

    public static Entry ForPlayer(int playerIndex) => players.Find(e => e.playerIndex == playerIndex);

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
            sb.Append(e.PlayerLabel).Append(' ').Append(e.character != null ? e.character.displayName : "(なし)")
              .Append(" (").Append(e.isCpu ? "CPU Lv" + e.cpuLevel : e.deviceName).Append(")  ");
        sb.Append("ステージ：").Append(Stage != null ? Stage.displayName : "(なし)");
        return sb.ToString();
    }
}
