using System;
using Nakahira;
using UnityEngine;

// 試合に出ている1人分（プレイヤーまたは CPU）。MatchDirector が作り、状態を書き換える。
// ゲーム画面の UI（HUD）はここを読むだけでよい。値が変わったときはイベントで知らせる。
//
//   誰か　　　：Entry（セレクトで決めた内容）・Index・Label（1P / CPU1）・Color・CharacterData（名前・顔の画像）
//   いまの状態：State（生存 / 幽霊 / 復活待ち / 脱落）・Character / Ghost / Body（いま動かしている体）
//   積み木　　：StackCount・StackMax・Stack（色を数えるなど）
//   ストック　：Stocks（ストックモード以外は -1）
//   点数　　　：Score（タイムモード）
//   記録　　　：Knocks（崩した数）・Knocked（崩された数）・KOs（顔を飛ばした数）・Falls（やられた回数）・SelfOuts（相手なしでやられた回数）
//   結果　　　：Rank（試合が終わったら 1〜。それまでは 0）
//
// 例）HUD で積み木の数を出す
//   foreach (var p in MatchDirector.Current.Players)
//       p.StackChanged += () => label.text = p.StackCount.ToString();
public class MatchPlayer : MonoBehaviour
{
    public enum PlayerState
    {
        Alive,          // 生きて動いている（Character がいる）
        Ghost,          // 幽霊（ゴーストモード。Ghost がいる）
        Respawning,     // やられて、復活を待っている（ストック・タイム）
        Out,            // 脱落（ストックが無くなった / ゴースト以外で時間内に戻れない）
    }

    // ---- 誰か ----
    public MatchSetup.Entry Entry { get; private set; }
    public int Index => Entry.playerIndex;                      // 0 = 1P
    public string Label => Entry.DisplayLabel;                  // 「1P」「CPU」「CPU1」
    public bool IsCpu => Entry.isCpu;
    public CharacterData CharacterData => Entry.character;      // 名前・顔の画像・色
    public Color Color { get; private set; }                    // プレイヤーの色（1P 赤、2P 青…、CPU 灰色）

    // ---- いまの状態 ----
    public PlayerState State { get; private set; }
    public DarumaCharacter Character { get; private set; }      // 生存中の体（いなければ null）
    public DarumaGhost Ghost { get; private set; }              // 幽霊の体（いなければ null）
    public Transform Body => Character != null ? Character.transform : Ghost != null ? Ghost.transform : null;
    public bool IsAlive => State == PlayerState.Alive;
    public bool IsInvincible => Character != null && Character.IsInvincible;
    public float RespawnTimeLeft { get; internal set; }         // 復活までの残り（秒）

    // ---- 積み木 ----
    public DarumaStack Stack => Character != null ? Character.Stack : null;
    public int StackCount => Character != null ? Character.Stack.Count : 0;
    public int StackMax => Character != null ? Character.Stack.MaxCount : 0;

    // ---- ストック・点数・記録 ----
    public int Stocks { get; private set; } = -1;
    public int Score { get; private set; }
    public int Knocks { get; private set; }
    public int Knocked { get; private set; }
    public int KOs { get; private set; }
    public int Falls { get; private set; }
    public int SelfOuts { get; private set; }   // 相手なしでやられた回数（自分から場外に落ちた など）
    public int Rank { get; internal set; }
    internal int OutOrder;      // 何番目に脱落したか（順位付け用）

    // ---- 操作 ----
    public IDarumaCommandSource CommandSource { get; private set; }

    // ---- 変わったときのお知らせ（HUD 用） ----
    public event Action StateChanged;       // State / Character / Ghost が変わった
    public event Action StackChanged;       // 積み木の数が変わった（拾った・崩された・撃った）
    public event Action StocksChanged;
    public event Action<int> ScoreChanged;  // 増えた（減った）点

    internal void Setup(MatchSetup.Entry entry, Color color, IDarumaCommandSource source, int stocks)
    {
        Entry = entry;
        Color = color;
        CommandSource = source;
        Stocks = stocks;
        name = entry.DisplayLabel;
    }

    internal void SetCharacter(DarumaCharacter character)
    {
        Character = character;
        Ghost = null;
        State = PlayerState.Alive;
        RespawnTimeLeft = 0f;
        character.name = Label + "_Character";
        character.SetCommandSource(CommandSource);
        character.Stack.Changed += OnStackChanged;
        StateChanged?.Invoke();
        StackChanged?.Invoke();
    }

    internal void SetGhost(DarumaGhost ghost)
    {
        Character = null;
        Ghost = ghost;
        State = PlayerState.Ghost;
        ghost.name = Label + "_Ghost";
        ghost.SetCommandSource(CommandSource);
        StateChanged?.Invoke();
        StackChanged?.Invoke();
    }

    internal void SetRespawning(float delay)
    {
        Character = null;
        Ghost = null;
        State = PlayerState.Respawning;
        RespawnTimeLeft = delay;
        StateChanged?.Invoke();
        StackChanged?.Invoke();
    }

    internal void SetOut(int order)
    {
        Character = null;
        if (Ghost != null) Destroy(Ghost.gameObject);
        Ghost = null;
        State = PlayerState.Out;
        OutOrder = order;
        StateChanged?.Invoke();
        StackChanged?.Invoke();
    }

    internal void StopControl()
    {
        if (Character != null) Character.SetCommandSource(null);
        if (Ghost != null) Ghost.SetCommandSource(null);
    }

    internal void LoseStock()
    {
        if (Stocks <= 0) return;
        Stocks--;
        StocksChanged?.Invoke();
    }

    internal void AddScore(int delta)
    {
        if (delta == 0) return;
        Score += delta;
        ScoreChanged?.Invoke(delta);
    }

    internal void CountKnock() => Knocks++;
    internal void CountKnocked() => Knocked++;
    internal void CountKO() => KOs++;
    internal void CountFall() => Falls++;
    internal void CountSelfOut() => SelfOuts++;

    void OnStackChanged() => StackChanged?.Invoke();
}
