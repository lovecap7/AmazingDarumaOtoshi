using UnityEngine;

// セレクト画面（MatchSelect）に出す文字。MatchSelect の Inspector の「Texts」で書き換えられる。
// {0} {1} のところには数字や名前が入る（例：「{0} ストック」→「3 ストック」）。消すとその値は出なくなる
[System.Serializable]
public class SelectTexts
{
    [Header("見出し")]
    public string modeHeader = "モードセレクト";
    [Tooltip("{0}：選んだモードの名前")]
    public string ruleHeader = "ルール：{0}";
    public string characterHeader = "キャラクターセレクト";
    public string stageHeader = "ステージセレクト";

    [Header("モードセレクト（上から ストック、タイム、ゴースト）")]
    public string[] modeNames = { "ストック", "タイム", "ゴースト" };
    [TextArea(2, 4)]
    public string[] modeDescriptions =
    {
        "ストックが無くなるまで\n戦う。最後まで\n残った人の勝ち！",
        "時間いっぱい戦って\n点をかせぐ。\n一番点が多い人の勝ち！",
        "やられても幽霊になって\n相手を叩けば復活！\n最後の1人まで戦う",
    };

    [Header("ルールセレクト：行の名前")]
    public string ruleStocks = "ストック数";
    public string ruleStockTime = "時間制限";
    public string ruleTimeLength = "試合時間";
    public string ruleGhostTime = "時間制限";
    public string ruleNext = "キャラクターセレクトへ";

    [Header("ルールセレクト：値")]
    [Tooltip("{0}：ストック数")]
    public string stocksValue = "{0} ストック";
    public string none = "なし";
    [Tooltip("{0}：分")]
    public string minutes = "{0}分";
    [Tooltip("{0}：秒")]
    public string seconds = "{0}秒";

    [Header("ルールセレクト：説明")]
    [TextArea(1, 3)] public string descStocks = "やられるとストックが1つ減る。無くなったら脱落、最後まで残った人の勝ち";
    [TextArea(1, 3)] public string descStockTimeOn = "時間切れになったら、残りストックが一番多い人の勝ち（同じなら積み木が多い人）";
    [TextArea(1, 3)] public string descStockTimeOff = "最後の1人になるまで続ける";
    [TextArea(1, 3)] public string descTimeLength = "決めた時間いっぱい戦う。やられても何度でも復活できる";
    [TextArea(1, 3)] public string descGhostTimeOn = "時間切れになったら、生き残っている中で積み木が一番多い人の勝ち";
    [TextArea(1, 3)] public string descGhostTimeOff = "最後の1人になるまで続ける";

    [Header("キャラクターセレクト右上のルール（{0} {1} に値が入る）")]
    [Tooltip("{0}：ストック数　{1}：時間制限")]
    public string summaryStock = "モード：ストック（{0}ストック　時間制限 {1}）";
    [Tooltip("{0}：試合時間")]
    public string summaryTime = "モード：タイム（{0}）";
    [Tooltip("{0}：時間制限")]
    public string summaryGhost = "モード：ゴースト（時間制限 {0}）";

    [Header("キャラクターセレクト")]
    public string randomCharacter = "ランダム";
    [Tooltip("ランダム・おまかせ・画像の無いところに出す字")]
    public string randomLetter = "？";
    public string chooseCharacter = "えらんでね";
    public string join = "A / Start で参加";
    public string typeNone = "なし";
    public string typeCpu = "CPU";
    public string typePlayer = "プレイヤー";
    [Tooltip("CPU の強さ　{0}：レベル")]
    public string cpuLevel = "Lv {0}";
    [Tooltip("ステージセレクトの下の札の CPU　{0}：レベル")]
    public string chipCpuLevel = "CPU Lv{0}";
    [Tooltip("キャラ・ステージを決めた人に出す字")]
    public string ok = "OK!";
    public string ready = "じゅんびOK！";
    public string readySub = "Start でステージセレクトへ";
    [TextArea(2, 4)] public string needMore = "あいて（CPU か ほかのプレイヤー）を くわえてね！\n<size=60%>枠の右上のボタンで CPU にできます</size>";

    [Header("ステージセレクト")]
    public string randomStage = "おまかせ";
    [TextArea(1, 3)] public string randomStageDescription = "どのステージになるかは、決めるまで分からない";
    [TextArea(1, 3)] public string randomStageVoteDescription = "<b>おまかせ</b>　どのステージになるかは抽選まで分からない";
    [Tooltip("ローカル：{0}：えらぶ人（1P など）")]
    public string chooserStatus = "{0} が えらんでね";
    public string previewTip = "右スティックで見回せる";
    [Tooltip("オンライン用の投票：{0}：投票した人数　{1}：プレイヤーの人数")]
    public string voteStatus = "とうひょう　{0} / {1}";
    public string voteAll = "ちゅうせん するよ！";
    public string lotteryTitle = "ステージ ちゅうせん！";
    [Tooltip("{0}：決まったステージの名前")]
    public string decided = "けってい！　{0}";

    [Header("下のヒント")]
    public string hintMode = "←→：モードをえらぶ　A / Start：決定　B：メニューへ";
    public string hintRule = "↑↓ / A：項目をえらぶ　←→：切り替え　Start：キャラクターセレクトへ　B：モードセレクトへ";
    public string hintCharacter = "A：参加・えらぶ・CPUのコインをつかむ　B：取り消し・抜ける　Start：次へ";
    [Tooltip("ローカル：{0}：えらぶ人（1P など）")]
    public string hintStageLocal = "A：決定　B：キャラクターセレクトへ　右スティック（IJKL）：ステージを見回す　えらべるのは {0} だけ";
    public string hintStageVote = "A：投票　B：取り消し（投票していなければキャラクターセレクトへ）　全員が投票したら抽選！";

    // {0} {1} に値を入れる（書き方をまちがえても止まらないよう、そのときは元の文字のまま）
    public static string F(string format, params object[] args)
    {
        if (string.IsNullOrEmpty(format)) return "";
        try { return string.Format(format, args); }
        catch (System.FormatException) { return format; }
    }

    // 配列の i 番目（足りなければ空欄）
    public static string At(string[] a, int i) => a != null && i >= 0 && i < a.Length ? a[i] : "";
}
