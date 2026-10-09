using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// モードセレクト → ルールセレクト → キャラクターセレクト → ステージセレクト → ステージ抽選。
// ・モードセレクト：ストック / タイム / ゴースト を選ぶ（←→ / A。B でメニューへ）
// ・ルールセレクト：選んだモードの詳細設定（↑↓：項目　←→ / A：切り替え　Start：キャラクターセレクトへ　B：モードセレクトへ）
//     ストック：ストック数（1〜3）と時間制限（なし / 1分 / 2分 / 3分）
//     タイム　：試合時間（1分 / 2分 / 3分）。点数（崩す +1・崩される -1・顔を飛ばす +1）は Inspector で変えられる
//     ゴースト：時間制限（なし / 1分 / 2分 / 3分）。最後の1人になるまで戦う。やられても幽霊になって復活できる
//   モードとルールはまだだれも参加していないので、どのコントローラーでも操作できる。次の試合にも引き継ぎ、試合後はキャラクターセレクトから
// ・キャラクターセレクト（スマブラ風）
//   - つながっているコントローラー（とキーボード）で A / Start を押すと、空いている一番小さい番号の枠にプレイヤーとして参加する
//     空きが無いときは、一番左の CPU の枠を上書きして参加する
//   - 各枠の右上のボタンをカーソルで押すと「CPU → なし → CPU …」と切り替わる。プレイヤーの枠は本人だけが CPU にできる
//   - 各プレイヤーは自分のカーソルでキャラ（またはランダム）を選ぶ。B で取り消し、何も選んでいなければ B で抜ける
//   - CPU のコインはプレイヤーがカーソルで掴んで（A）キャラの上に置く（A）と、その CPU のキャラになる。CPU の強さは枠の －／＋
//   - 参加者全員が選ぶと「じゅんびOK！」。Start / Enter でステージセレクトへ
// ・ステージセレクト：プレイヤー（CPU 以外）が自分のカーソルでステージ（またはおまかせ）に投票する
//   全員が投票したら、マリオカートのように全員の票の中から1つを抽選する。おまかせ票は当たったときにめくれる
// ・決まった内容は MatchSetup に入れてゲーム本編へ渡す。キャラクターセレクトに入るたびに選択はまっさらに戻る
// キャラとステージは SelectRoster に足すだけで増やせる（並びは数に合わせて自動で調整）。
//
// ■ プレイヤーとキャラの取り違えを防ぐために
//   1人分の情報（番号・入力機器・CPUかどうか・選んだキャラ・投票したステージ）は slots[番号] の Player 1つにまとめて持ち、ほかに写しを作らない。
//   入力は「その Player が持っている機器」からだけ読む。Gamepad.all の並び順や、画面上のマスの順番は使わない。
public class MatchSelect : MonoBehaviour
{
    [Header("素材")]
    [SerializeField] TMP_FontAsset font;
    [Tooltip("角丸の画像（UI/Skin/UISprite）")]
    [SerializeField] Sprite uiSprite;
    [Tooltip("丸い画像（UI/Skin/Knob）")]
    [SerializeField] Sprite knobSprite;

    [Header("データ")]
    [SerializeField] SelectRoster roster;

    [Header("ルール")]
    [SerializeField, Range(1, 8)] int maxPlayers = 4;
    [Tooltip("コントローラーで参加しているプレイヤーが最低何人必要か")]
    [SerializeField, Range(1, 8)] int minHumans = 1;
    [Tooltip("CPU を含めた参加者が最低何人必要か（1人プレイなら CPU を足して2人以上にする）")]
    [SerializeField, Range(1, 8)] int minParticipants = 2;
    [SerializeField, Range(1, 9)] int defaultCpuLevel = 3;
    [Tooltip("ルールセレクトで選べる時間（秒）。ストックの時間制限は「なし」→ 並び順、タイムの試合時間は並び順に切り替わる")]
    [SerializeField] float[] timeLimitChoices = { 60f, 120f, 180f };
    [Tooltip("ストックで選べる最大のストック数（1〜この数）")]
    [SerializeField, Range(1, 9)] int maxStocks = 3;

    [Header("タイムの点数")]
    [Tooltip("相手の積み木を崩したとき")]
    [SerializeField] int knockPoints = 1;
    [Tooltip("自分の積み木を崩されたとき")]
    [SerializeField] int knockedPoints = -1;
    [Tooltip("相手の顔を飛ばしたとき（3 や 5 にするかも）")]
    [SerializeField] int headPoints = 1;
    [Tooltip("相手なしでやられたとき（自分から場外に落ちた など）")]
    [SerializeField] int selfOutPoints = -1;
    [SerializeField] float cursorSpeed = 1300f;
    [Tooltip("全員が投票してから抽選が始まるまでの時間（この間なら取り消せる）")]
    [SerializeField] float lotteryDelay = 0.8f;

    [Header("シーン")]
    [Tooltip("誰も参加していないときに B で戻るシーン")]
    [SerializeField] string backSceneName = "MenuScene";
    [Tooltip("ゲーム本編のシーン（ステージ側で sceneName を指定していればそちらが優先）")]
    [SerializeField] string gameSceneName = "MatchCheckScene";

    static readonly Color[] PlayerColors =
    {
        new Color(0.93f, 0.27f, 0.25f),   // 1P 赤
        new Color(0.22f, 0.55f, 0.95f),   // 2P 青
        new Color(0.98f, 0.76f, 0.12f),   // 3P 黄
        new Color(0.30f, 0.74f, 0.35f),   // 4P 緑
        new Color(0.96f, 0.55f, 0.15f),
        new Color(0.40f, 0.85f, 0.85f),
        new Color(0.85f, 0.45f, 0.80f),
        new Color(0.55f, 0.45f, 0.35f),
    };
    static readonly Color CpuColor = new Color(0.55f, 0.55f, 0.58f);
    static readonly Color RandomColor = new Color(0.7f, 0.7f, 0.72f);

    enum Phase { Mode, Rule, Character, Stage, Lottery, Leaving }

    struct PadState
    {
        public Vector2 move;
        public bool a, b, start;
    }

    // 1人分（プレイヤーまたは CPU）。番号・機器・選んだキャラ・投票したステージを必ずここにまとめて持つ
    class Player
    {
        public int index;               // 0 = 1P。参加してから抜けるまで変わらない
        public InputDevice device;      // CPU は null
        public int cpuLevel;
        public Vector2 cursor;
        public int joinFrame;

        public CharacterData character; // 選んだキャラ（ランダムのときは null）
        public bool randomCharacter;
        public StageData stage;         // 投票したステージ（おまかせのときは null）
        public bool randomStage;

        public RectTransform cursorRect, charToken, stageToken;
        public Player holding;          // カーソルで掴んでいる CPU のコイン
        public Player heldBy;           // （CPU）自分のコインを掴んでいるプレイヤー

        public bool IsCpu => device == null;
        public bool HasCharacter => character != null || randomCharacter;
        public bool HasVote => stage != null || randomStage;
        public Color Color => IsCpu ? CpuColor : PlayerColors[index % PlayerColors.Length];
        public string Label => (index + 1) + "P";
        public string TokenLabel => IsCpu ? "CP" : Label;
    }

    class Tile
    {
        public RectTransform rect;
        public CharacterData character;
        public StageData stage;
        public bool random;
        public Vector2 size;
    }

    class Panel
    {
        public Image bg, portrait, portraitImage, typeButton;
        public TextMeshProUGUI label, name, device, join, ok, portraitLetter, typeText, level;
        public RectTransform levelRow, minus, plus;
    }

    class Chip
    {
        public RectTransform rect;
        public TextMeshProUGUI text;
        public Image icon, iconImage;           // 投票したステージのアイコン（CPU はキャラ）
        public TextMeshProUGUI iconLetter, ok;
        public object shown;                    // いま出しているもの（変わったらアイコンを弾ませる）
    }

    // モードセレクトのカード1枚
    class ModeCard
    {
        public MatchSetup.MatchMode mode;
        public Color color;
        public RectTransform rect;
        public Image bg;
        public TextMeshProUGUI title, desc;
    }

    // ルールセレクトの行の種類
    enum RuleKind { Stocks, StockTime, TimeLength, GhostTime, Info, Next }

    // ルールセレクトの1行
    class RuleRow
    {
        public RuleKind kind;
        public RectTransform rect;
        public Image bg;
        public TextMeshProUGUI label, value, desc;
    }

    Phase phase = Phase.Mode;
    Player[] slots;
    ModeCard[] modeCards;
    int modeIndex;
    readonly List<RuleRow> ruleRows = new List<RuleRow>();
    int ruleIndex;
    readonly List<Tile> charTiles = new List<Tile>();
    readonly List<Tile> stageTiles = new List<Tile>();
    Panel[] panels;
    Chip[] chips;
    int phaseFrame = -1;            // 画面が切り替わったフレーム（そのボタンで続けて操作しないように）
    float allVotedTime = -1f;

    // ---- UI ----
    RectTransform canvas, modeRoot, ruleRoot, charRoot, stageRoot, lotteryRoot, cursorLayer, readyBanner;
    CanvasGroup modeGroup, ruleGroup, charGroup, stageGroup, readyGroup, lotteryGroup;
    TextMeshProUGUI hint, stageStatus, stageDescription, needMoreText, ruleSummary, ruleHeader;
    Image fade;
    Material letterMat, popMat;
    float readyAmount;

    // ---- 背景 ----
    Camera cam;
    readonly List<Transform> floaters = new List<Transform>();

    void Start()
    {
        if (roster == null || roster.characters.Count == 0 || roster.stages.Count == 0)
        {
            Debug.LogError("MatchSelect：roster（SelectRoster）が未設定か、キャラ・ステージが空です", this);
            enabled = false;
            return;
        }
        slots = new Player[maxPlayers];
        letterMat = ToyKit.LetterMaterial(font);
        popMat = ToyKit.PopMaterial(letterMat);

        // 試合から戻ってきたときは、ルールはそのままでキャラクターセレクトから始める（スマブラと同じ）
        bool fromMatch = MatchSetup.Players.Count > 0;

        // 毎回まっさらな状態から選び直す（前回の試合の選択は引き継がない。ルールだけは引き継ぐ）
        MatchSetup.Clear();
        var rules = MatchSetup.Rules;
        rules.knockPoints = knockPoints;
        rules.knockedPoints = knockedPoints;
        rules.headPoints = headPoints;
        rules.selfOutPoints = selfOutPoints;
        rules.stocks = Mathf.Clamp(rules.stocks, 1, maxStocks);

        BuildBackground();
        BuildUI();
        if (fromMatch) GoToCharacterSelect();
        else GoToModeSelect();
        SnapScreens();
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    void OnDestroy() => InputSystem.onDeviceChange -= OnDeviceChange;

    // コントローラーが抜かれたら、そのプレイヤーは抜けたことにする（キャラ・ステージを選んでいる間だけ）
    void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (change != InputDeviceChange.Removed && change != InputDeviceChange.Disconnected) return;
        if (phase != Phase.Character && phase != Phase.Stage) return;
        var p = FindPlayer(device);
        if (p != null) Remove(p);
    }

    // ================= 毎フレーム =================
    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (fade.color.a > 0f) fade.color = new Color(1f, 1f, 1f, Mathf.MoveTowards(fade.color.a, 0f, dt * 2f));

        if (phase == Phase.Mode) UpdateModeSelect(dt);
        else if (phase == Phase.Rule) UpdateRuleSelect(dt);
        else if (phase == Phase.Character) UpdateCharacterSelect(dt);
        else if (phase == Phase.Stage) UpdateStageSelect(dt);

        // 画面の切り替え（モード → ルール → キャラ → ステージ）。左から順に並んでいて、横にすべって切り替わる
        float k = 1f - Mathf.Exp(-10f * dt);
        int screen = Screen();
        var roots = new[] { modeRoot, ruleRoot, charRoot, stageRoot };
        var groups = new[] { modeGroup, ruleGroup, charGroup, stageGroup };
        for (int i = 0; i < roots.Length; i++)
        {
            roots[i].anchoredPosition = Vector2.Lerp(roots[i].anchoredPosition, new Vector2((i - screen) * 2200f, 0f), k);
            groups[i].alpha = Mathf.MoveTowards(groups[i].alpha, i == screen ? 1f : 0f, dt * 4f);
        }
        cursorLayer.gameObject.SetActive(phase == Phase.Character || phase == Phase.Stage);
        stageStatus.gameObject.SetActive(phase == Phase.Stage);
        stageDescription.gameObject.SetActive(phase == Phase.Stage);

        UpdateCursors();
        UpdatePanels();
        UpdateChips();
        UpdateTiles(dt);
        UpdateBackground();

        hint.text = phase == Phase.Mode
            ? "←→：モードをえらぶ　A / Start：決定　B：メニューへ"
            : phase == Phase.Rule
            ? "↑↓：項目をえらぶ　←→ / A：切り替え　Start：キャラクターセレクトへ　B：モードセレクトへ"
            : phase == Phase.Character
            ? "A：参加・えらぶ・CPUのコインをつかむ　B：取り消し・抜ける（だれもいなければルールセレクトへ）　Start：次へ"
            : phase == Phase.Stage ? "A：投票　B：取り消し（投票していなければキャラクターセレクトへ）　全員が投票したら抽選！"
            : "";
    }

    // いま見せている画面（0 = モード, 1 = ルール, 2 = キャラ, 3 = ステージ）
    int Screen() => phase == Phase.Mode ? 0 : phase == Phase.Rule ? 1 : phase == Phase.Character ? 2 : 3;

    // 画面をすべらせずに、いまの画面へ一気に切り替える
    void SnapScreens()
    {
        var roots = new[] { modeRoot, ruleRoot, charRoot, stageRoot };
        var groups = new[] { modeGroup, ruleGroup, charGroup, stageGroup };
        for (int i = 0; i < roots.Length; i++)
        {
            roots[i].anchoredPosition = new Vector2((i - Screen()) * 2200f, 0f);
            groups[i].alpha = i == Screen() ? 1f : 0f;
        }
    }

    // ---------------- モードセレクト ----------------
    // まだだれも参加していないので、モード・ルールはキーボードとどのコントローラーでも操作できる
    void UpdateModeSelect(float dt)
    {
        if (phaseFrame != Time.frameCount)
        {
            int n = modeCards.Length;
            if (MenuInput.Left()) modeIndex = (modeIndex + n - 1) % n;
            else if (MenuInput.Right()) modeIndex = (modeIndex + 1) % n;
            else if (MenuInput.Submit() || MenuInput.Start())
            {
                MatchSetup.Rules.mode = modeCards[modeIndex].mode;
                GoToRuleSelect();
                return;
            }
            else if (MenuInput.Cancel()) { Back(); return; }
        }

        // 選んでいるカードは色を付けて大きく
        float k = 1f - Mathf.Exp(-14f * dt);
        for (int i = 0; i < modeCards.Length; i++)
        {
            var c = modeCards[i];
            bool sel = i == modeIndex;
            c.bg.color = Color.Lerp(c.bg.color, sel ? c.color : Color.white, k);
            c.rect.localScale = Vector3.Lerp(c.rect.localScale, Vector3.one * (sel ? 1.08f : 0.94f), k);
            c.title.color = sel ? Color.white : ToyKit.Ink;
            c.desc.color = sel ? Color.white : new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.75f);
        }
    }

    void GoToModeSelect()
    {
        phase = Phase.Mode;
        phaseFrame = Time.frameCount;
        modeIndex = Mathf.Max(0, System.Array.FindIndex(modeCards, c => c.mode == MatchSetup.Rules.mode));
    }

    // ---------------- ルールセレクト（選んだモードの詳細設定） ----------------
    void GoToRuleSelect()
    {
        phase = Phase.Rule;
        phaseFrame = Time.frameCount;
        BuildRuleRows();
        ruleHeader.text = "ルール：" + MatchSetup.Rules.ModeName;
        ruleIndex = ruleRows.FindIndex(r => r.kind != RuleKind.Info);
    }

    void UpdateRuleSelect(float dt)
    {
        var rules = MatchSetup.Rules;
        if (phaseFrame != Time.frameCount)
        {
            if (MenuInput.Up()) ruleIndex = NextRuleRow(-1);
            else if (MenuInput.Down()) ruleIndex = NextRuleRow(1);
            else if (MenuInput.Start()) { GoToCharacterSelect(); return; }
            else if (MenuInput.Cancel()) { GoToModeSelect(); return; }
            else if (MenuInput.Left() || MenuInput.Right() || MenuInput.Submit())
            {
                int dir = MenuInput.Left() ? -1 : 1;   // ← で戻る、→ / A で進む
                var row = ruleRows[ruleIndex];
                switch (row.kind)
                {
                    case RuleKind.Stocks: rules.stocks = (rules.stocks - 1 + dir + maxStocks) % maxStocks + 1; break;
                    case RuleKind.StockTime: rules.stockTimeLimit = StepTime(rules.stockTimeLimit, true, dir); break;
                    case RuleKind.TimeLength: rules.timeLength = StepTime(rules.timeLength, false, dir); break;
                    case RuleKind.GhostTime: rules.ghostTimeLimit = StepTime(rules.ghostTimeLimit, true, dir); break;
                    case RuleKind.Next: if (MenuInput.Submit()) { GoToCharacterSelect(); return; } break;
                }
                if (row.value != null) Bounce(row.value);
            }
        }

        foreach (var row in ruleRows)
        {
            switch (row.kind)
            {
                case RuleKind.Stocks:
                    SetRule(row, rules.stocks + " ストック", "やられるとストックが1つ減る。無くなったら脱落、最後まで残った人の勝ち");
                    break;
                case RuleKind.StockTime:
                    SetRule(row, rules.stockTimeLimit > 0f ? TimeText(rules.stockTimeLimit) : "なし",
                        rules.stockTimeLimit > 0f ? "時間切れになったら、残りストックが一番多い人の勝ち" : "最後の1人になるまで続ける");
                    break;
                case RuleKind.TimeLength:
                    SetRule(row, TimeText(rules.timeLength), "決めた時間いっぱい戦う。やられても何度でも復活できる");
                    break;
                case RuleKind.GhostTime:
                    SetRule(row, rules.ghostTimeLimit > 0f ? TimeText(rules.ghostTimeLimit) : "なし",
                        rules.ghostTimeLimit > 0f ? "時間切れになったら、生き残っている中で積み木が一番多い人の勝ち" : "最後の1人になるまで続ける");
                    break;
            }
        }

        // 選んでいる行は色を付けて少し大きく（説明だけの行は選べない）
        float k = 1f - Mathf.Exp(-14f * dt);
        for (int i = 0; i < ruleRows.Count; i++)
        {
            var row = ruleRows[i];
            bool sel = i == ruleIndex;
            var on = row.kind == RuleKind.Next ? ToyKit.Palette[0] : ToyKit.Palette[2];
            var off = row.kind == RuleKind.Info ? new Color(1f, 1f, 1f, 0.75f) : Color.white;
            row.bg.color = Color.Lerp(row.bg.color, sel ? on : off, k);
            row.rect.localScale = Vector3.Lerp(row.rect.localScale, Vector3.one * (sel ? 1.05f : 1f), k);
            row.label.color = sel ? Color.white : ToyKit.Ink;
            if (row.desc != null) row.desc.color = sel ? Color.white : new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, row.kind == RuleKind.Info ? 0.9f : 0.7f);
            if (row.value != null)
            {
                row.value.color = sel ? Color.white : ToyKit.Ink;
                row.value.rectTransform.localScale = Vector3.Lerp(row.value.rectTransform.localScale, Vector3.one, k);
            }
        }
    }

    // 上下で次に選べる行（説明だけの行は飛ばす）
    int NextRuleRow(int dir)
    {
        int n = ruleRows.Count, i = ruleIndex;
        for (int step = 0; step < n; step++)
        {
            i = (i + dir + n) % n;
            if (ruleRows[i].kind != RuleKind.Info) return i;
        }
        return ruleIndex;
    }

    // 時間を「（なし →）1分 → 2分 → 3分 → …」と切り替える（dir = -1 で逆回り）。0 = なし
    float StepTime(float current, bool allowNone, int dir)
    {
        int offset = allowNone ? 1 : 0;
        int count = timeLimitChoices.Length + offset;
        int index = System.Array.IndexOf(timeLimitChoices, current);
        int at = index >= 0 ? index + offset : allowNone && current <= 0f ? 0 : count - 1;
        int next = (at + dir + count) % count;
        return next < offset ? 0f : timeLimitChoices[next - offset];
    }

    static string TimeText(float seconds) =>
        seconds % 60f == 0f ? Mathf.RoundToInt(seconds / 60f) + "分" : Mathf.RoundToInt(seconds) + "秒";

    static void SetRule(RuleRow row, string value, string desc)
    {
        row.value.text = "＜　" + value + "　＞";
        row.desc.text = desc;
    }

    static void Bounce(TextMeshProUGUI t) => t.rectTransform.localScale = Vector3.one * 1.25f;

    // キャラクターセレクトの右上に出すルールの説明
    static string RuleSummaryText()
    {
        var r = MatchSetup.Rules;
        switch (r.mode)
        {
            case MatchSetup.MatchMode.Stock:
                return "モード：ストック（" + r.stocks + "ストック　時間制限 " + (r.stockTimeLimit > 0f ? TimeText(r.stockTimeLimit) : "なし") + "）";
            case MatchSetup.MatchMode.Time:
                return "モード：タイム（" + TimeText(r.timeLength) + "）";
            default:
                return "モード：ゴースト（時間制限 " + (r.ghostTimeLimit > 0f ? TimeText(r.ghostTimeLimit) : "なし") + "）";
        }
    }

    void GoToCharacterSelect()
    {
        phase = Phase.Character;
        phaseFrame = Time.frameCount;
        ruleSummary.text = RuleSummaryText();
        Debug.Log(ruleSummary.text);
    }

    // ---------------- キャラクターセレクト ----------------
    void UpdateCharacterSelect(float dt)
    {
        // 参加していない機器で A / Start が押されたら参加
        foreach (var device in Devices())
        {
            if (FindPlayer(device) != null) continue;
            var s = Read(device);
            if (s.a || s.start) Join(device);
            else if (s.b && HumanCount() == 0) { GoToRuleSelect(); return; }
        }

        bool ready = AllChoseCharacter();
        foreach (var p in Humans())
        {
            if (p.joinFrame == Time.frameCount || phaseFrame == Time.frameCount) continue;   // 参加・切り替えのボタンで同時に選ばないように

            var s = Read(p.device);
            MoveCursor(p, s, dt);

            if (s.start && ready && p.holding == null) { GoToStageSelect(); return; }
            if (s.a) PressA(p);
            else if (s.b)
            {
                if (p.holding != null) Release(p);
                else if (p.HasCharacter) ClearCharacter(p);
                else Remove(p);
            }
        }

        readyAmount = Mathf.MoveTowards(readyAmount, ready ? 1f : 0f, dt * 5f);
        float e = 1f - Mathf.Pow(1f - readyAmount, 3f);
        readyBanner.anchoredPosition = new Vector2(Mathf.Lerp(-2200f, 0f, e), readyBanner.anchoredPosition.y);
        readyGroup.alpha = readyAmount;

        // 全員選んだのに人数が足りない（1人だけ）ときは、相手を足すよう案内する
        bool needMore = !ready && EveryoneChose() && ParticipantCount() < minParticipants;
        needMoreText.alpha = Mathf.MoveTowards(needMoreText.alpha, needMore ? 1f : 0f, dt * 5f);
        needMoreText.rectTransform.localScale = Vector3.one * (1f + 0.03f * Mathf.Sin(Time.unscaledTime * 4f));
    }

    // A ボタン：掴んでいるコインを置く → 枠のボタン → CPU のコインを掴む → キャラを選ぶ、の順に調べる
    void PressA(Player p)
    {
        var tile = TileAt(charTiles, p.cursor);

        if (p.holding != null)
        {
            if (tile != null) SetCharacter(p.holding, tile);
            Release(p);
            return;
        }

        for (int i = 0; i < panels.Length; i++)
        {
            var ui = panels[i];
            if (Hit(ui.typeButton.rectTransform, p.cursor)) { ToggleSlot(p, i); return; }
            var cpu = slots[i];
            if (cpu != null && cpu.IsCpu && ui.levelRow.gameObject.activeSelf)
            {
                if (Hit(ui.minus, p.cursor)) { cpu.cpuLevel = Mathf.Max(1, cpu.cpuLevel - 1); return; }
                if (Hit(ui.plus, p.cursor)) { cpu.cpuLevel = Mathf.Min(9, cpu.cpuLevel + 1); return; }
            }
        }

        foreach (var cpu in slots)
        {
            if (cpu == null || !cpu.IsCpu || cpu.heldBy != null || !cpu.charToken.gameObject.activeSelf) continue;
            if (Hit(cpu.charToken, p.cursor)) { Grab(p, cpu); return; }
        }

        if (tile != null) SetCharacter(p, tile);
    }

    void Grab(Player p, Player cpu)
    {
        p.holding = cpu;
        cpu.heldBy = p;
        cpu.charToken.SetParent(cursorLayer, false);
    }

    // 掴んでいるコインを放す（置かずに放したときは元のキャラのマスに戻る）
    void Release(Player p)
    {
        var cpu = p.holding;
        p.holding = null;
        if (cpu == null) return;
        cpu.heldBy = null;
        var tile = cpu.randomCharacter ? charTiles.Find(t => t.random) : charTiles.Find(t => t.character == cpu.character);
        if (tile != null) PlaceToken(cpu, cpu.charToken, tile);
    }

    // 枠の種類を切り替える：CPU → なし、なし → CPU（スマブラと同じく、だれでも切り替えられる）
    // プレイヤーの枠は、そのプレイヤー本人だけが CPU にできる（ほかの人が勝手に CPU に変えることはできない）
    void ToggleSlot(Player by, int i)
    {
        var p = slots[i];
        if (p == null) { AddCpu(i, defaultCpuLevel); return; }
        if (p.IsCpu) { Remove(p); return; }
        if (p != by) { ShakeTypeButton(i); return; }

        // 自分の枠を CPU にする（選んでいたキャラは引き継ぐ。もう一度 A / Start を押せば参加し直せる）
        var character = p.character;
        bool random = p.randomCharacter;
        Remove(p);
        var cpu = AddCpu(i, defaultCpuLevel);
        var tile = random ? charTiles.Find(t => t.random) : charTiles.Find(t => t.character == character);
        if (tile != null) SetCharacter(cpu, tile);
    }

    void MoveCursor(Player p, PadState s, float dt)
    {
        p.cursor += s.move * cursorSpeed * dt;
        p.cursor.x = Mathf.Clamp(p.cursor.x, -930f, 930f);
        p.cursor.y = Mathf.Clamp(p.cursor.y, -510f, 510f);
    }

    // 空いている一番小さい番号の枠に参加する。空きが無ければ、一番左の CPU の枠を上書きして参加する
    void Join(InputDevice device)
    {
        int target = -1;
        for (int i = 0; i < slots.Length && target < 0; i++) if (slots[i] == null) target = i;
        for (int i = 0; i < slots.Length && target < 0; i++) if (slots[i].IsCpu) target = i;
        if (target < 0) return;   // 全員プレイヤーで満員

        if (slots[target] != null) Remove(slots[target]);
        AddHuman(target, device);
    }

    // ほかの人の枠のボタンを押したとき：切り替えられないことを、ボタンを揺らして伝える
    void ShakeTypeButton(int i)
    {
        StartCoroutine(Shake(panels[i].typeButton.rectTransform));
    }

    static IEnumerator Shake(RectTransform r)
    {
        var basePos = new Vector2(-95f, -38f);
        for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
        {
            r.anchoredPosition = basePos + new Vector2(Mathf.Sin(t * 70f) * 10f * (1f - t / 0.3f), 0f);
            yield return null;
        }
        r.anchoredPosition = basePos;
    }

    Player AddHuman(int i, InputDevice device)
    {
        var p = new Player { index = i, device = device, joinFrame = Time.frameCount };
        p.cursor = PanelPosition(i) + new Vector2(0f, 300f);
        p.cursorRect = MakeCursor(p);
        p.charToken = MakeToken(p, charRoot);
        p.stageToken = MakeToken(p, stageRoot);
        slots[i] = p;
        Debug.Log($"{p.Label} 参加：{device.displayName} #{device.deviceId}");
        return p;
    }

    // CPU を作る。最初はランダムのマスにコインを置く
    Player AddCpu(int i, int level)
    {
        var p = new Player { index = i, cpuLevel = level };
        p.charToken = MakeToken(p, charRoot);
        slots[i] = p;
        SetCharacter(p, charTiles.Find(t => t.random));
        Debug.Log($"{p.Label} CPU Lv{level}");
        return p;
    }

    void Remove(Player p)
    {
        if (p.holding != null) Release(p);
        if (p.heldBy != null) p.heldBy.holding = null;
        if (p.cursorRect != null) Destroy(p.cursorRect.gameObject);
        if (p.charToken != null) Destroy(p.charToken.gameObject);
        if (p.stageToken != null) Destroy(p.stageToken.gameObject);
        slots[p.index] = null;
        Debug.Log($"{p.Label} 退出");
    }

    void SetCharacter(Player p, Tile tile)
    {
        p.character = tile.random ? null : tile.character;
        p.randomCharacter = tile.random;
        PlaceToken(p, p.charToken, tile);
    }

    void ClearCharacter(Player p)
    {
        p.character = null;
        p.randomCharacter = false;
        p.charToken.gameObject.SetActive(false);
    }

    // 選んだマスにコインを置く（4隅に分けて重ならないように）
    static void PlaceToken(Player p, RectTransform token, Tile tile)
    {
        token.SetParent(tile.rect, false);
        var corner = new Vector2(p.index % 2 == 0 ? -1f : 1f, p.index / 2 % 2 == 0 ? 1f : -1f);
        token.anchoredPosition = Vector2.Scale(corner, tile.size * 0.36f);
        token.gameObject.SetActive(true);
        token.localScale = Vector3.one * 1.4f;
    }

    // 次へ進めるか：参加者全員がキャラを決めていて、プレイヤーと参加者（CPU 含む）の人数が足りている
    bool AllChoseCharacter() => EveryoneChose() && ParticipantCount() >= minParticipants;

    // 参加しているプレイヤーと CPU が全員キャラを決めたか（人数が足りているかは見ない）
    bool EveryoneChose()
    {
        foreach (var p in slots)
        {
            if (p == null) continue;
            if (!p.HasCharacter || p.holding != null || p.heldBy != null) return false;
        }
        return HumanCount() >= minHumans;
    }

    int ParticipantCount()
    {
        int n = 0;
        foreach (var p in slots) if (p != null) n++;
        return n;
    }

    int HumanCount()
    {
        int n = 0;
        foreach (var p in slots) if (p != null && !p.IsCpu) n++;
        return n;
    }

    // CPU の番号：CPU が1人だけなら 0、複数いるなら左から 1, 2, …
    int CpuNumber(Player p)
    {
        int count = 0, number = 0;
        foreach (var q in slots)
        {
            if (q == null || !q.IsCpu) continue;
            count++;
            if (q == p) number = count;
        }
        return count <= 1 ? 0 : number;
    }

    // 画面に出す名前：「1P」、CPU は「CPU」（複数いるときは「CPU1」「CPU2」）
    string NameOf(Player p)
    {
        if (!p.IsCpu) return p.Label;
        int n = CpuNumber(p);
        return n > 0 ? "CPU" + n : "CPU";
    }

    // コインに書く短い名前：「1P」、CPU は「CP」（複数いるときは「C1」「C2」）
    string TokenNameOf(Player p)
    {
        if (!p.IsCpu) return p.Label;
        int n = CpuNumber(p);
        return n > 0 ? "C" + n : "CP";
    }

    IEnumerable<Player> Humans()
    {
        foreach (var p in slots) if (p != null && !p.IsCpu) yield return p;
    }

    Player FindPlayer(InputDevice device)
    {
        foreach (var p in slots) if (p != null && p.device == device) return p;
        return null;
    }

    static IEnumerable<InputDevice> Devices()
    {
        if (Keyboard.current != null) yield return Keyboard.current;
        foreach (var g in Gamepad.all) yield return g;
    }

    void Back()
    {
        phase = Phase.Leaving;
        Fader.Load(backSceneName);
    }

    // ---------------- ステージセレクト（投票） ----------------
    void GoToStageSelect()
    {
        phase = Phase.Stage;
        phaseFrame = Time.frameCount;
        allVotedTime = -1f;
        foreach (var p in Humans()) p.cursor = new Vector2(PanelPosition(p.index).x, -120f);
    }

    void BackToCharacterSelect()
    {
        phase = Phase.Character;
        phaseFrame = Time.frameCount;
        foreach (var p in Humans()) Unvote(p);
    }

    void UpdateStageSelect(float dt)
    {
        foreach (var p in Humans())
        {
            if (phaseFrame == Time.frameCount) break;
            var s = Read(p.device);
            MoveCursor(p, s, dt);

            if (s.a) Vote(p);
            else if (s.b)
            {
                if (p.HasVote) Unvote(p);
                else { BackToCharacterSelect(); return; }
            }
        }

        // プレイヤー全員が投票したら、少し待ってから抽選（待っている間に取り消せば止まる）
        int voted = 0, humans = 0;
        foreach (var p in Humans()) { humans++; if (p.HasVote) voted++; }
        bool all = humans > 0 && voted == humans;
        if (!all) allVotedTime = -1f;
        else if (allVotedTime < 0f) allVotedTime = Time.unscaledTime;
        else if (Time.unscaledTime - allVotedTime >= lotteryDelay) StartCoroutine(Lottery());

        stageStatus.text = all ? "ちゅうせん するよ！" : $"とうひょう　{voted} / {humans}";

        // 番号の小さいプレイヤーのカーソルが乗っているステージの説明を出す
        Tile hovered = null;
        foreach (var p in Humans()) { hovered = TileAt(stageTiles, p.cursor); if (hovered != null) break; }
        stageDescription.text = hovered == null ? ""
            : hovered.random ? "<b>おまかせ</b>　どのステージになるかは抽選まで分からない"
            : "<b>" + hovered.stage.displayName + "</b>　" + hovered.stage.description;
    }

    void Vote(Player p)
    {
        var tile = TileAt(stageTiles, p.cursor);
        if (tile == null) return;
        p.stage = tile.random ? null : tile.stage;
        p.randomStage = tile.random;
        PlaceToken(p, p.stageToken, tile);
    }

    static void Unvote(Player p)
    {
        p.stage = null;
        p.randomStage = false;
        if (p.stageToken != null) p.stageToken.gameObject.SetActive(false);
    }

    // ---------------- 抽選（マリオカート風） ----------------
    IEnumerator Lottery()
    {
        phase = Phase.Lottery;

        // 票を番号順に並べる。おまかせ票の中身はここで決めるが、当たるまで伏せておく
        var voters = new List<Player>();
        var votes = new List<StageData>();
        foreach (var p in Humans())
        {
            voters.Add(p);
            votes.Add(p.randomStage ? roster.stages[Random.Range(0, roster.stages.Count)] : p.stage);
        }

        // 札を並べる
        foreach (Transform c in lotteryRoot) Destroy(c.gameObject);
        var dim = ToyKit.UIImage("Dim", lotteryRoot, new Color(0.15f, 0.1f, 0.1f, 0.55f));
        ToyKit.Stretch(dim);
        var title = ToyKit.UIText("Title", lotteryRoot, font, "ステージ ちゅうせん！", 90f, Color.white);
        title.fontSharedMaterial = popMat;
        ToyKit.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(1400f, 140f));

        int n = votes.Count;
        float gap = 30f;
        var cardSize = new Vector2(Mathf.Min(380f, (1760f - gap * (n - 1)) / n), 0f);
        cardSize.y = cardSize.x * 0.68f;
        var frame = ToyKit.UIImage("Frame", lotteryRoot, Color.white, uiSprite);
        frame.GetComponent<Image>().pixelsPerUnitMultiplier = 0.3f;
        frame.sizeDelta = cardSize + new Vector2(40f, 120f);
        var cards = new List<Tile>();
        for (int i = 0; i < n; i++)
        {
            var p = voters[i];
            var pos = new Vector2((i - (n - 1) * 0.5f) * (cardSize.x + gap), 0f);
            bool hidden = p.randomStage;
            var card = MakeTile(lotteryRoot, pos, cardSize, hidden ? RandomColor : votes[i].color, hidden ? null : votes[i].preview,
                                hidden ? "おまかせ" : votes[i].displayName, hidden ? "？" : Initial(votes[i].displayName));
            cards.Add(card);

            // 札の上に投票した人
            var chip = ToyKit.UIImage("Voter", card.rect, p.Color, uiSprite);
            ToyKit.Anchor(chip, new Vector2(0.5f, 1f), new Vector2(0f, 34f), new Vector2(120f, 56f));
            var ct = ToyKit.UIText("Text", chip, font, p.Label, 34f, Color.white);
            ct.fontSharedMaterial = letterMat;
            ToyKit.Stretch(ct.rectTransform);
            card.rect.localScale = Vector3.zero;
        }
        frame.anchoredPosition = cards[0].rect.anchoredPosition + new Vector2(0f, 20f);
        frame.gameObject.SetActive(false);

        // 出てくる
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.5f)
        {
            lotteryGroup.alpha = t;
            for (int i = 0; i < n; i++)
                cards[i].rect.localScale = Vector3.one * ToyKit.EaseOutBack(Mathf.Clamp01(t * 1.6f - i * 0.15f));
            yield return null;
        }
        lotteryGroup.alpha = 1f;
        foreach (var c in cards) c.rect.localScale = Vector3.one;
        yield return Wait(0.4f);

        // 枠が札を巡り、だんだん遅くなって止まる
        int winner = Random.Range(0, n);
        int laps = Mathf.Max(2, Mathf.CeilToInt(12f / n));
        int steps = laps * n + winner;
        frame.gameObject.SetActive(true);
        for (int s = 0; s <= steps; s++)
        {
            int idx = s % n;
            frame.anchoredPosition = cards[idx].rect.anchoredPosition + new Vector2(0f, 20f);
            float progress = s / (float)Mathf.Max(1, steps);
            float interval = Mathf.Lerp(0.06f, 0.5f, progress * progress * progress);
            for (float t = 0f; t < interval; t += Time.unscaledDeltaTime)
            {
                cards[idx].rect.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, t / interval);
                yield return null;
            }
        }

        // 当たったのがおまかせ票なら、ここで初めてめくる
        var won = votes[winner];
        if (voters[winner].randomStage)
        {
            yield return Wait(0.3f);
            var c = cards[winner];
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.5f)
            {
                c.rect.localScale = new Vector3(Mathf.Abs(Mathf.Cos(t * Mathf.PI)), 1f, 1f) * 1.1f;
                frame.localScale = c.rect.localScale;
                if (t >= 0.5f) ShowStageOnCard(c, won);
                yield return null;
            }
            ShowStageOnCard(c, won);
            c.rect.localScale = Vector3.one;
            frame.localScale = Vector3.one;
        }

        // 決定！
        var popText = ToyKit.UIText("Decided", lotteryRoot, font, "けってい！　" + won.displayName, 110f, ToyKit.Palette[0]);
        popText.fontSharedMaterial = popMat;
        ToyKit.Anchor(popText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -300f), new Vector2(1600f, 180f));
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.5f)
        {
            float e = ToyKit.EaseOutBack(t);
            for (int i = 0; i < n; i++)
            {
                if (i == winner) cards[i].rect.localScale = Vector3.one * Mathf.Lerp(1f, 1.25f, e);
                else cards[i].rect.GetComponent<Image>().color *= new Color(0.98f, 0.98f, 0.98f, 1f);
            }
            frame.localScale = Vector3.one * Mathf.Lerp(1f, 1.25f, e);
            popText.rectTransform.localScale = Vector3.one * e;
            yield return null;
        }
        yield return Wait(1.2f);

        ConfirmMatch(won);
    }

    void ShowStageOnCard(Tile card, StageData stage)
    {
        card.rect.GetComponent<Image>().color = stage.color;
        var letter = card.rect.Find("Letter");
        if (letter != null) letter.GetComponent<TextMeshProUGUI>().text = stage.preview != null ? "" : Initial(stage.displayName);
        var name = card.rect.Find("NameStrip/Name");
        if (name != null) name.GetComponent<TextMeshProUGUI>().text = stage.displayName;
        if (stage.preview != null && card.rect.Find("Image") == null)
        {
            var img = ToyKit.UIImage("Image", card.rect, Color.white).GetComponent<Image>();
            img.sprite = stage.preview;
            img.preserveAspect = true;
            ToyKit.Stretch(img.rectTransform);
            img.rectTransform.offsetMin = new Vector2(8f, card.size.y * 0.22f);
            img.rectTransform.offsetMax = new Vector2(-8f, -8f);
        }
    }

    static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
    }

    // 決まった内容を MatchSetup に入れて、ゲーム本編へ
    void ConfirmMatch(StageData stage)
    {
        // 1人ずつ、その人の Player からだけ値を取り出して Entry を作る
        var entries = new List<MatchSetup.Entry>();
        foreach (var p in slots)
        {
            if (p == null) continue;
            var character = p.randomCharacter ? roster.characters[Random.Range(0, roster.characters.Count)] : p.character;
            entries.Add(new MatchSetup.Entry
            {
                playerIndex = p.index,
                isCpu = p.IsCpu,
                cpuLevel = p.cpuLevel,
                deviceId = p.IsCpu ? -1 : p.device.deviceId,
                deviceName = p.IsCpu ? "CPU" : p.device.displayName,
                character = character,
                cpuNumber = p.IsCpu ? CpuNumber(p) : 0,
            });
        }
        MatchSetup.Set(entries, stage);
        Debug.Log("試合の設定：" + MatchSetup.Describe());

        phase = Phase.Leaving;
        string scene = !string.IsNullOrEmpty(stage.sceneName) ? stage.sceneName : gameSceneName;
        Fader.Load(scene);
    }

    // ================= 見た目の更新 =================
    void UpdateCursors()
    {
        float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
        foreach (var p in slots)
        {
            if (p == null) continue;
            if (p.cursorRect != null) p.cursorRect.anchoredPosition = p.cursor;
            if (p.heldBy != null) p.charToken.anchoredPosition = p.heldBy.cursor + new Vector2(30f, -30f);   // 掴まれたコインはカーソルについてくる
            foreach (var token in new[] { p.charToken, p.stageToken })
            {
                if (token == null || !token.gameObject.activeSelf) continue;
                token.localScale = Vector3.Lerp(token.localScale, Vector3.one, k);
                var label = token.Find("Label");
                if (label != null) label.GetComponent<TextMeshProUGUI>().text = TokenNameOf(p);   // CPU が増減すると番号が変わる
            }
        }
    }

    void UpdatePanels()
    {
        for (int i = 0; i < panels.Length; i++)
        {
            var ui = panels[i];
            var p = slots[i];
            bool used = p != null;
            bool cpu = used && p.IsCpu;

            ui.bg.color = !used ? new Color(0.86f, 0.84f, 0.83f, 0.85f) : cpu ? new Color(0.8f, 0.8f, 0.82f) : Color.Lerp(PlayerColors[i % PlayerColors.Length], Color.white, 0.55f);
            ui.label.text = used ? NameOf(p) : (i + 1) + "P";
            ui.label.color = !used ? new Color(0.6f, 0.58f, 0.57f) : cpu ? CpuColor : PlayerColors[i % PlayerColors.Length];
            ui.typeText.text = !used ? "なし" : cpu ? "CPU" : "プレイヤー";
            ui.typeButton.color = !used ? new Color(0.75f, 0.73f, 0.72f) : cpu ? CpuColor : PlayerColors[i % PlayerColors.Length];
            ui.join.gameObject.SetActive(!used);
            ui.name.gameObject.SetActive(used);
            ui.device.gameObject.SetActive(used && !cpu);
            ui.levelRow.gameObject.SetActive(cpu);
            ui.portrait.gameObject.SetActive(used);
            ui.ok.gameObject.SetActive(used && p.HasCharacter && p.heldBy == null);
            if (!used) continue;

            if (cpu) ui.level.text = "Lv " + p.cpuLevel;
            else ui.device.text = p.device.displayName + " #" + p.device.deviceId;   // 同じ種類のコントローラーでも見分けられるように

            if (p.heldBy != null) SetPortrait(ui, null, "えらんでね", new Color(0.92f, 0.9f, 0.88f), "");
            else if (p.randomCharacter) SetPortrait(ui, null, "ランダム", RandomColor, "？");
            else if (p.character != null) SetPortrait(ui, p.character.portrait, p.character.displayName, p.character.color, Initial(p.character.displayName));
            else
            {
                // まだ選んでいない：カーソルが乗っているキャラを薄く見せる
                var hover = !cpu && phase == Phase.Character ? TileAt(charTiles, p.cursor) : null;
                if (hover != null && !hover.random) SetPortrait(ui, hover.character.portrait, hover.character.displayName, hover.character.color * 0.75f, Initial(hover.character.displayName));
                else SetPortrait(ui, null, "えらんでね", new Color(0.92f, 0.9f, 0.88f), "");
            }
        }
    }

    static void SetPortrait(Panel ui, Sprite sprite, string name, Color color, string letter)
    {
        ui.name.text = name;
        ui.portrait.color = color;
        ui.portraitImage.sprite = sprite;
        ui.portraitImage.enabled = sprite != null;
        ui.portraitLetter.text = sprite != null ? "" : letter;
    }

    // ステージセレクトの下の札：左に投票したステージのアイコン、右に「1P あかだる / 回る中心」
    // （キャラクターセレクトの枠と同じく、まだ選んでいなければカーソルが乗っているステージを薄く見せる）
    void UpdateChips()
    {
        float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
        for (int i = 0; i < chips.Length; i++)
        {
            var chip = chips[i];
            var p = slots[i];
            chip.rect.gameObject.SetActive(p != null && phase != Phase.Lottery);   // 抽選中は隠す
            if (p == null) { chip.shown = null; continue; }
            chip.rect.GetComponent<Image>().color = p.Color;
            string chara = p.randomCharacter ? "ランダム" : p.character != null ? p.character.displayName : "-";

            object shown;
            string sub;
            if (p.IsCpu)
            {
                // CPU は投票しないので、キャラを出す
                if (p.character != null) SetChipIcon(chip, p.character.portrait, p.character.color, Initial(p.character.displayName), true);
                else SetChipIcon(chip, null, RandomColor, "？", true);
                chip.ok.gameObject.SetActive(false);
                shown = p.character;
                sub = "CPU Lv" + p.cpuLevel;
            }
            else if (p.HasVote)
            {
                if (p.randomStage) SetChipIcon(chip, null, RandomColor, "？", true);
                else SetChipIcon(chip, p.stage.preview, p.stage.color, Initial(p.stage.displayName), true);
                chip.ok.gameObject.SetActive(true);
                shown = p.randomStage ? (object)"random" : p.stage;
                sub = p.randomStage ? "おまかせ" : p.stage.displayName;
            }
            else
            {
                var hover = phase == Phase.Stage ? TileAt(stageTiles, p.cursor) : null;
                if (hover == null) SetChipIcon(chip, null, new Color(0.92f, 0.9f, 0.88f), "", false);
                else if (hover.random) SetChipIcon(chip, null, RandomColor * 0.8f, "？", false);
                else SetChipIcon(chip, hover.stage.preview, hover.stage.color * 0.8f, Initial(hover.stage.displayName), false);
                chip.ok.gameObject.SetActive(false);
                shown = null;
                sub = "えらんでね";
            }

            // 投票した（変えた）瞬間にアイコンを弾ませる
            if (shown != null && shown != chip.shown) chip.icon.rectTransform.localScale = Vector3.one * 1.3f;
            chip.shown = shown;
            chip.icon.rectTransform.localScale = Vector3.Lerp(chip.icon.rectTransform.localScale, Vector3.one, k);

            chip.text.text = NameOf(p) + "　" + chara + "\n<size=75%>" + sub + "</size>";
        }
    }

    static void SetChipIcon(Chip chip, Sprite sprite, Color color, string letter, bool chosen)
    {
        chip.icon.color = new Color(color.r, color.g, color.b, chosen ? 1f : 0.75f);
        chip.iconImage.sprite = sprite;
        chip.iconImage.enabled = sprite != null;
        chip.iconImage.color = new Color(1f, 1f, 1f, chosen ? 1f : 0.6f);
        chip.iconLetter.text = sprite != null ? "" : letter;
    }

    void UpdateTiles(float dt)
    {
        float k = 1f - Mathf.Exp(-14f * dt);
        var active = phase == Phase.Character ? charTiles : phase == Phase.Stage ? stageTiles : null;
        foreach (var list in new[] { charTiles, stageTiles })
            foreach (var t in list)
            {
                bool hovered = false;
                if (list == active) foreach (var p in Humans()) if (TileAt(list, p.cursor) == t) hovered = true;
                t.rect.localScale = Vector3.Lerp(t.rect.localScale, Vector3.one * (hovered ? 1.08f : 1f), k);
            }
    }

    static Tile TileAt(List<Tile> tiles, Vector2 point)
    {
        foreach (var t in tiles)
        {
            var d = point - t.rect.anchoredPosition;
            if (Mathf.Abs(d.x) <= t.size.x * 0.5f && Mathf.Abs(d.y) <= t.size.y * 0.5f) return t;
        }
        return null;
    }

    // カーソル（canvas 上の位置）が UI の上にあるか
    bool Hit(RectTransform rect, Vector2 cursor) =>
        rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, canvas.TransformPoint(cursor), null);

    // ================= 入力 =================
    static PadState Read(InputDevice device)
    {
        var s = new PadState();
        if (device is Gamepad g)
        {
            var m = g.leftStick.ReadValue();
            var dp = g.dpad.ReadValue();
            if (dp.sqrMagnitude > m.sqrMagnitude) m = dp;
            s.move = m.magnitude < 0.2f ? Vector2.zero : m;
            s.a = g.buttonSouth.wasPressedThisFrame;
            s.b = g.buttonEast.wasPressedThisFrame;
            s.start = g.startButton.wasPressedThisFrame;
        }
        else if (device is Keyboard k)
        {
            float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
            float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
            s.move = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            s.start = k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame;
            s.a = k.spaceKey.wasPressedThisFrame || k.zKey.wasPressedThisFrame || s.start;
            s.b = k.escapeKey.wasPressedThisFrame || k.xKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame;
        }
        return s;
    }

    // ================= 組み立て =================
    void BuildBackground()
    {
        var blockMesh = ToyKit.RoundedBox(new Vector3(ToyKit.BlockW, ToyKit.BlockH, ToyKit.BlockD), 0.14f, 10);
        cam = ToyKit.CreateCamera();
        cam.fieldOfView = 45f;
        ToyKit.CreateLights();
        var post = ToyKit.CreatePostProcess(0.35f);
        post.dof.focusDistance.value = 3f;      // 背景はボカして UI を読みやすく
        post.dof.aperture.value = 2f;
        ToyKit.CreateEnvironment(blockMesh);

        // 宙に浮いてゆっくり回る積み木（にぎやかし）
        for (int i = 0; i < 9; i++)
        {
            var b = new GameObject("Floater").transform;
            b.gameObject.AddComponent<MeshFilter>().sharedMesh = blockMesh;
            b.gameObject.AddComponent<MeshRenderer>().sharedMaterial = ToyKit.Lit(ToyKit.Palette[i % ToyKit.Palette.Length], 0.55f);
            b.position = new Vector3(-7f + i * 1.75f, 1.5f + (i % 3) * 1.6f, 4f + (i % 2) * 2f);
            b.rotation = Random.rotation;
            floaters.Add(b);
        }
    }

    void UpdateBackground()
    {
        float t = Time.unscaledTime;
        foreach (var b in floaters) b.Rotate(new Vector3(0.3f, 0.7f, 0.2f), 12f * Time.unscaledDeltaTime);
        cam.transform.position = new Vector3(Mathf.Sin(t * 0.13f) * 0.3f, 3f + Mathf.Sin(t * 0.17f) * 0.1f, -10f);
        cam.transform.LookAt(new Vector3(0f, 3f, 5f));
    }

    void BuildUI()
    {
        canvas = ToyKit.CreateOverlayCanvas("UI", 10);

        // ---- モードセレクト ----
        modeRoot = Layer("ModeSelect", canvas);
        modeGroup = modeRoot.gameObject.AddComponent<CanvasGroup>();
        Header(modeRoot, "モードセレクト", ToyKit.Palette[1]);
        modeCards = new[]
        {
            MakeModeCard(MatchSetup.MatchMode.Stock, "ストック", ToyKit.Palette[1], new Vector2(-580f, -10f),
                "ストックが無くなるまで\n戦う。最後まで\n残った人の勝ち！"),
            MakeModeCard(MatchSetup.MatchMode.Time, "タイム", ToyKit.Palette[4], new Vector2(0f, -10f),
                "時間いっぱい戦って\n点をかせぐ。\n一番点が多い人の勝ち！"),
            MakeModeCard(MatchSetup.MatchMode.Ghost, "ゴースト", ToyKit.Palette[3], new Vector2(580f, -10f),
                "やられても幽霊になって\n相手を叩けば復活！\n最後の1人まで戦う"),
        };

        // ---- ルールセレクト（行はモードを選んだときに作る） ----
        ruleRoot = Layer("RuleSelect", canvas);
        ruleGroup = ruleRoot.gameObject.AddComponent<CanvasGroup>();
        ruleHeader = Header(ruleRoot, "ルール", ToyKit.Palette[2]);

        // ---- キャラクターセレクト ----
        charRoot = Layer("CharacterSelect", canvas);
        charGroup = charRoot.gameObject.AddComponent<CanvasGroup>();
        Header(charRoot, "キャラクターセレクト", ToyKit.Palette[0]);

        // 決めたルール（右上）
        ruleSummary = ToyKit.UIText("RuleSummary", charRoot, font, "", 32f, ToyKit.Ink);
        ruleSummary.alignment = TextAlignmentOptions.Right;
        ruleSummary.rectTransform.pivot = new Vector2(1f, 1f);
        ToyKit.Anchor(ruleSummary.rectTransform, new Vector2(1f, 1f), new Vector2(-50f, -60f), new Vector2(900f, 50f));

        int count = roster.characters.Count + 1;   // 最後にランダム
        var grid = GridLayout(count, 8, new Vector2(1760f, 430f), new Vector2(205f, 205f), 16f, 350f);
        for (int i = 0; i < count; i++)
        {
            bool random = i == roster.characters.Count;
            var c = random ? null : roster.characters[i];
            var tile = MakeTile(charRoot, grid[i].pos, grid[i].size, random ? RandomColor : c.color,
                                random ? null : c.portrait, random ? "ランダム" : c.displayName, random ? "？" : Initial(c.displayName));
            tile.character = c;
            tile.random = random;
            charTiles.Add(tile);
        }

        panels = new Panel[maxPlayers];
        for (int i = 0; i < maxPlayers; i++) panels[i] = MakePanel(charRoot, i);

        // じゅんびOK！の帯
        readyBanner = ToyKit.UIImage("Ready", charRoot, ToyKit.Palette[0]);
        readyBanner.anchorMin = new Vector2(0f, 0.5f); readyBanner.anchorMax = new Vector2(1f, 0.5f);
        readyBanner.sizeDelta = new Vector2(0f, 150f);
        readyBanner.anchoredPosition = new Vector2(-2200f, -150f);
        readyGroup = readyBanner.gameObject.AddComponent<CanvasGroup>();
        readyGroup.alpha = 0f;
        readyGroup.blocksRaycasts = false;
        var rt = ToyKit.UIText("Text", readyBanner, font, "じゅんびOK！", 96f, Color.white);
        rt.fontSharedMaterial = popMat;
        ToyKit.Anchor(rt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-150f, 8f), new Vector2(900f, 140f));
        var rs = ToyKit.UIText("Sub", readyBanner, font, "Start でステージセレクトへ", 38f, Color.white);
        ToyKit.Anchor(rs.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(520f, -10f), new Vector2(700f, 60f));

        needMoreText = ToyKit.UIText("NeedMore", charRoot, font, "あいて（CPU か ほかのプレイヤー）を くわえてね！\n<size=60%>枠の右上のボタンで CPU にできます</size>", 54f, ToyKit.Palette[0]);
        needMoreText.fontSharedMaterial = popMat;
        ToyKit.Anchor(needMoreText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(1600f, 150f));
        needMoreText.alpha = 0f;

        // ---- ステージセレクト ----
        stageRoot = Layer("StageSelect", canvas);
        stageGroup = stageRoot.gameObject.AddComponent<CanvasGroup>();
        Header(stageRoot, "ステージセレクト", ToyKit.Palette[4]);

        int sc = roster.stages.Count + 1;   // 最後におまかせ
        var sgrid = GridLayout(sc, 7, new Vector2(1760f, 470f), new Vector2(380f, 240f), 18f, 345f);
        for (int i = 0; i < sc; i++)
        {
            bool random = i == roster.stages.Count;
            var st = random ? null : roster.stages[i];
            var tile = MakeTile(stageRoot, sgrid[i].pos, sgrid[i].size, random ? RandomColor : st.color,
                                random ? null : st.preview, random ? "おまかせ" : st.displayName, random ? "？" : Initial(st.displayName));
            tile.stage = st;
            tile.random = random;
            stageTiles.Add(tile);
        }

        stageStatus = ToyKit.UIText("Status", stageRoot, font, "", 46f, ToyKit.Ink);
        ToyKit.Anchor(stageStatus.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -285f), new Vector2(1200f, 70f));

        // カーソルを合わせたステージの説明
        stageDescription = ToyKit.UIText("Description", stageRoot, font, "", 32f, ToyKit.Ink);
        stageDescription.textWrappingMode = TextWrappingModes.Normal;
        ToyKit.Anchor(stageDescription.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -195f), new Vector2(1700f, 110f));

        chips = new Chip[maxPlayers];
        for (int i = 0; i < maxPlayers; i++)
        {
            var c = ToyKit.UIImage("Chip" + (i + 1), stageRoot, PlayerColors[i % PlayerColors.Length], uiSprite);
            float w = Mathf.Min(420f, 1800f / maxPlayers - 20f);
            const float h = 140f;
            ToyKit.Anchor(c, new Vector2(0.5f, 0.5f), new Vector2(PanelPosition(i).x, -400f), new Vector2(w, h));
            var col = c.gameObject.AddComponent<Outline>(); col.effectColor = ToyKit.Ink; col.effectDistance = new Vector2(3f, -3f);

            // 左：ステージのアイコン（ステージのマスと同じ横長）
            float iw = Mathf.Min(170f, w * 0.42f), ih = Mathf.Min(h - 24f, iw * 0.63f);
            var icon = ToyKit.UIImage("Icon", c, Color.white, uiSprite).GetComponent<Image>();
            icon.pixelsPerUnitMultiplier = 0.5f;
            ToyKit.Anchor(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(14f + iw * 0.5f, 0f), new Vector2(iw, ih));
            var iol = icon.gameObject.AddComponent<Outline>(); iol.effectColor = ToyKit.Ink; iol.effectDistance = new Vector2(2f, -2f);
            var iconImage = ToyKit.UIImage("Image", icon.rectTransform, Color.white).GetComponent<Image>();
            iconImage.preserveAspect = true;
            ToyKit.Stretch(iconImage.rectTransform);
            iconImage.rectTransform.offsetMin = new Vector2(4f, 4f);
            iconImage.rectTransform.offsetMax = new Vector2(-4f, -4f);
            var letter = ToyKit.UIText("Letter", icon.rectTransform, font, "", ih * 0.6f, Color.white);
            letter.fontSharedMaterial = letterMat;
            ToyKit.Stretch(letter.rectTransform);
            var ok = ToyKit.UIText("OK", icon.rectTransform, font, "OK!", 44f, ToyKit.Palette[0]);
            ok.fontSharedMaterial = popMat;
            ok.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -12f);
            ToyKit.Anchor(ok.rectTransform, new Vector2(1f, 1f), Vector2.zero, new Vector2(120f, 60f));

            // 右：名前と投票先
            float tw = w - iw - 36f;
            var t = ToyKit.UIText("Text", c, font, "", 40f, Color.white);
            t.fontSharedMaterial = letterMat;
            t.enableAutoSizing = true; t.fontSizeMin = 14f; t.fontSizeMax = 40f;
            ToyKit.Anchor(t.rectTransform, new Vector2(1f, 0.5f), new Vector2(-12f - tw * 0.5f, 0f), new Vector2(tw, h - 16f));
            chips[i] = new Chip { rect = c, text = t, icon = icon, iconImage = iconImage, iconLetter = letter, ok = ok };
        }

        cursorLayer = Layer("Cursors", canvas);

        // ---- 抽選 ----
        lotteryRoot = Layer("Lottery", canvas);
        lotteryGroup = lotteryRoot.gameObject.AddComponent<CanvasGroup>();
        lotteryGroup.alpha = 0f;
        lotteryGroup.blocksRaycasts = false;

        hint = ToyKit.UIText("Hint", canvas, font, "", 26f, ToyKit.Ink);
        hint.alignment = TextAlignmentOptions.Right;
        hint.rectTransform.pivot = new Vector2(1f, 0f);
        ToyKit.Anchor(hint.rectTransform, new Vector2(1f, 0f), new Vector2(-40f, 14f), new Vector2(1840f, 40f));

        fade = ToyKit.UIImage("Fade", canvas, Color.white).GetComponent<Image>();
        ToyKit.Stretch(fade.rectTransform);
    }

    // モードセレクトのカード（モード名・説明）
    ModeCard MakeModeCard(MatchSetup.MatchMode mode, string title, Color color, Vector2 pos, string desc)
    {
        var c = new ModeCard { mode = mode, color = color };
        var size = new Vector2(520f, 600f);
        c.rect = ToyKit.UIImage("Mode_" + title, modeRoot, Color.white, uiSprite);
        ToyKit.Anchor(c.rect, new Vector2(0.5f, 0.5f), pos, size);
        c.bg = c.rect.GetComponent<Image>();
        c.bg.pixelsPerUnitMultiplier = 0.3f;
        var ol = c.rect.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(4f, -4f);

        c.title = ToyKit.UIText("Title", c.rect, font, title, 96f, ToyKit.Ink);
        c.title.fontSharedMaterial = letterMat;
        ToyKit.Anchor(c.title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(size.x - 40f, 130f));

        c.desc = ToyKit.UIText("Desc", c.rect, font, desc, 34f, ToyKit.Ink);
        c.desc.enableAutoSizing = true; c.desc.fontSizeMin = 20f; c.desc.fontSizeMax = 34f;   // 改行は文章側で入れる
        c.desc.lineSpacing = 10f;
        ToyKit.Anchor(c.desc.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(size.x - 50f, 180f));
        return c;
    }

    // 選んだモードに合わせて、ルールセレクトの行を作り直す
    void BuildRuleRows()
    {
        foreach (var r in ruleRows) Destroy(r.rect.gameObject);
        ruleRows.Clear();

        var rules = MatchSetup.Rules;
        var rows = new List<KeyValuePair<RuleKind, string>>();
        switch (rules.mode)
        {
            case MatchSetup.MatchMode.Stock:
                rows.Add(new KeyValuePair<RuleKind, string>(RuleKind.Stocks, "ストック数"));
                rows.Add(new KeyValuePair<RuleKind, string>(RuleKind.StockTime, "時間制限"));
                break;
            case MatchSetup.MatchMode.Time:
                rows.Add(new KeyValuePair<RuleKind, string>(RuleKind.TimeLength, "試合時間"));
                break;
            default:
                rows.Add(new KeyValuePair<RuleKind, string>(RuleKind.GhostTime, "時間制限"));
                break;
        }
        rows.Add(new KeyValuePair<RuleKind, string>(RuleKind.Next, "キャラクターセレクトへ"));

        // 上から順に、全体が画面の中央に来るように並べる
        const float gap = 22f;
        float total = -gap;
        foreach (var r in rows) total += RowHeight(r.Key) + gap;
        float y = 30f + total * 0.5f;
        foreach (var r in rows)
        {
            float h = RowHeight(r.Key);
            var row = MakeRuleRow(r.Key, r.Value, new Vector2(0f, y - h * 0.5f), h);
            y -= h + gap;
            ruleRows.Add(row);
        }
    }

    static float RowHeight(RuleKind kind) => kind == RuleKind.Next ? 120f : kind == RuleKind.Info ? 210f : 190f;


    // ルールセレクトの1行
    //   設定の行：左に項目名、右に「＜ 値 ＞」、下に説明
    //   説明の行：左上に項目名、下に2行の説明（選べない）
    //   ボタン　：真ん中に文字だけ
    RuleRow MakeRuleRow(RuleKind kind, string label, Vector2 pos, float height)
    {
        var row = new RuleRow { kind = kind };
        const float w = 1300f;
        row.rect = ToyKit.UIImage("Rule_" + label, ruleRoot, Color.white, uiSprite);
        ToyKit.Anchor(row.rect, new Vector2(0.5f, 0.5f), pos, new Vector2(w, height));
        row.bg = row.rect.GetComponent<Image>();
        row.bg.pixelsPerUnitMultiplier = 0.4f;
        var ol = row.rect.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3f, -3f);

        row.label = ToyKit.UIText("Label", row.rect, font, label, kind == RuleKind.Next ? 56f : 60f, ToyKit.Ink);
        row.label.fontSharedMaterial = letterMat;
        if (kind == RuleKind.Next) { ToyKit.Stretch(row.label.rectTransform); return row; }

        row.label.alignment = TextAlignmentOptions.Left;
        row.desc = ToyKit.UIText("Desc", row.rect, font, "", 32f, ToyKit.Ink);
        row.desc.alignment = TextAlignmentOptions.Left;
        if (kind == RuleKind.Info)
        {
            row.label.fontSize = 48f;
            ToyKit.Anchor(row.label.rectTransform, new Vector2(0f, 1f), new Vector2(60f + 220f, -45f), new Vector2(440f, 70f));
            row.desc.lineSpacing = 8f;
            ToyKit.Anchor(row.desc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(w - 120f, 120f));
            return row;
        }

        ToyKit.Anchor(row.label.rectTransform, new Vector2(0f, 0.5f), new Vector2(60f + 220f, 30f), new Vector2(440f, 90f));
        row.value = ToyKit.UIText("Value", row.rect, font, "", 60f, ToyKit.Ink);
        row.value.fontSharedMaterial = letterMat;
        ToyKit.Anchor(row.value.rectTransform, new Vector2(1f, 0.5f), new Vector2(-60f - 280f, 30f), new Vector2(560f, 90f));
        ToyKit.Anchor(row.desc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -48f), new Vector2(w - 120f, 60f));
        return row;
    }

    RectTransform Layer(string name, Transform parent)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent, false);
        ToyKit.Stretch(r);
        return r;
    }

    TextMeshProUGUI Header(Transform parent, string text, Color accent)
    {
        var bar = ToyKit.UIImage("HeaderBar", parent, accent, uiSprite);
        bar.anchorMin = bar.anchorMax = new Vector2(0f, 1f);
        bar.pivot = new Vector2(0f, 1f);
        bar.anchoredPosition = new Vector2(-30f, -30f);
        bar.sizeDelta = new Vector2(760f, 110f);
        var t = ToyKit.UIText("Header", bar, font, text, 66f, Color.white);
        t.fontSharedMaterial = popMat;
        t.alignment = TextAlignmentOptions.Left;
        ToyKit.Anchor(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(400f, 0f), new Vector2(700f, 110f));
        return t;
    }

    struct Cell { public Vector2 pos, size; }

    // 数に合わせてマスの大きさと並びを決める（入りきらなければ小さくする）
    static Cell[] GridLayout(int count, int maxCols, Vector2 area, Vector2 maxSize, float gap, float top)
    {
        int cols = Mathf.Max(1, Mathf.Min(count, maxCols));
        int rows = Mathf.CeilToInt(count / (float)cols);
        float scale = Mathf.Min(1f,
            (area.x - gap * (cols - 1)) / (cols * maxSize.x),
            (area.y - gap * (rows - 1)) / (rows * maxSize.y));
        var size = maxSize * scale;
        var cells = new Cell[count];
        for (int i = 0; i < count; i++)
        {
            int col = i % cols, row = i / cols;
            int inRow = Mathf.Min(cols, count - row * cols);   // 最後の行は中央寄せ
            cells[i].pos = new Vector2((col - (inRow - 1) * 0.5f) * (size.x + gap), top - size.y * 0.5f - row * (size.y + gap));
            cells[i].size = size;
        }
        return cells;
    }

    Tile MakeTile(Transform parent, Vector2 pos, Vector2 size, Color color, Sprite sprite, string name, string letter)
    {
        var r = ToyKit.UIImage("Tile_" + name, parent, color, uiSprite);
        r.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), pos, size);
        var ol = r.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3f, -3f);

        if (sprite != null)
        {
            var img = ToyKit.UIImage("Image", r, Color.white).GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            ToyKit.Stretch(img.rectTransform);
            img.rectTransform.offsetMin = new Vector2(8f, size.y * 0.22f);
            img.rectTransform.offsetMax = new Vector2(-8f, -8f);
        }
        var l = ToyKit.UIText("Letter", r, font, sprite != null ? "" : letter, Mathf.Min(size.x, size.y) * 0.5f, Color.white);
        l.fontSharedMaterial = letterMat;
        ToyKit.Stretch(l.rectTransform);
        l.rectTransform.offsetMin = new Vector2(0f, size.y * 0.18f);

        var strip = ToyKit.UIImage("NameStrip", r, new Color(1f, 1f, 1f, 0.85f), uiSprite);
        strip.anchorMin = new Vector2(0f, 0f); strip.anchorMax = new Vector2(1f, 0f); strip.pivot = new Vector2(0.5f, 0f);
        strip.sizeDelta = new Vector2(-12f, size.y * 0.22f);
        strip.anchoredPosition = new Vector2(0f, 6f);
        var n = ToyKit.UIText("Name", strip, font, name, size.y * 0.13f, ToyKit.Ink);
        n.enableAutoSizing = true; n.fontSizeMin = 10f; n.fontSizeMax = size.y * 0.14f;
        ToyKit.Stretch(n.rectTransform);

        return new Tile { rect = r, size = size };
    }

    Vector2 PanelPosition(int i) => new Vector2((i - (maxPlayers - 1) * 0.5f) * Mathf.Min(450f, 1800f / maxPlayers), -345f);

    Panel MakePanel(Transform parent, int i)
    {
        var ui = new Panel();
        float w = Mathf.Min(420f, 1800f / maxPlayers - 30f);
        var r = ToyKit.UIImage("Panel" + (i + 1), parent, Color.white, uiSprite);
        ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), PanelPosition(i), new Vector2(w, 270f));
        r.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        var ol = r.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3f, -3f);
        ui.bg = r.GetComponent<Image>();

        ui.label = ToyKit.UIText("Label", r, font, (i + 1) + "P", 60f, Color.white);
        ui.label.fontSharedMaterial = letterMat;
        ui.label.alignment = TextAlignmentOptions.TopLeft;
        ui.label.enableAutoSizing = true; ui.label.fontSizeMin = 30f; ui.label.fontSizeMax = 60f;   //「CPU2」でも入るように
        ToyKit.Anchor(ui.label.rectTransform, new Vector2(0f, 1f), new Vector2(88f, -48f), new Vector2(150f, 80f));

        // プレイヤー／CPU／なし の切り替えボタン（右上）
        var tb = ToyKit.UIImage("TypeButton", r, Color.white, uiSprite);
        ToyKit.Anchor(tb, new Vector2(1f, 1f), new Vector2(-95f, -38f), new Vector2(170f, 56f));
        var tbo = tb.gameObject.AddComponent<Outline>(); tbo.effectColor = ToyKit.Ink; tbo.effectDistance = new Vector2(2f, -2f);
        ui.typeButton = tb.GetComponent<Image>();
        ui.typeText = ToyKit.UIText("Text", tb, font, "なし", 30f, Color.white);
        ui.typeText.fontSharedMaterial = letterMat;
        ToyKit.Stretch(ui.typeText.rectTransform);

        ui.portrait = ToyKit.UIImage("Portrait", r, Color.white, uiSprite).GetComponent<Image>();
        ToyKit.Anchor(ui.portrait.rectTransform, new Vector2(0f, 0.5f), new Vector2(w * 0.27f, -30f), new Vector2(w * 0.38f, w * 0.38f));
        ui.portraitImage = ToyKit.UIImage("Image", ui.portrait.rectTransform, Color.white).GetComponent<Image>();
        ui.portraitImage.preserveAspect = true;
        ToyKit.Stretch(ui.portraitImage.rectTransform);
        ui.portraitLetter = ToyKit.UIText("Letter", ui.portrait.rectTransform, font, "", w * 0.22f, Color.white);
        ui.portraitLetter.fontSharedMaterial = letterMat;
        ToyKit.Stretch(ui.portraitLetter.rectTransform);

        ui.name = ToyKit.UIText("Name", r, font, "", 44f, ToyKit.Ink);
        ui.name.enableAutoSizing = true; ui.name.fontSizeMin = 18f; ui.name.fontSizeMax = 44f;
        ToyKit.Anchor(ui.name.rectTransform, new Vector2(1f, 0.5f), new Vector2(-w * 0.28f, -10f), new Vector2(w * 0.5f, 70f));
        ui.device = ToyKit.UIText("Device", r, font, "", 22f, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.7f));
        ui.device.enableAutoSizing = true; ui.device.fontSizeMin = 12f; ui.device.fontSizeMax = 22f;
        ToyKit.Anchor(ui.device.rectTransform, new Vector2(1f, 0.5f), new Vector2(-w * 0.28f, -70f), new Vector2(w * 0.5f, 40f));

        // CPU の強さ（－ Lv3 ＋）
        ui.levelRow = new GameObject("Level", typeof(RectTransform)).GetComponent<RectTransform>();
        ui.levelRow.SetParent(r, false);
        ToyKit.Anchor(ui.levelRow, new Vector2(1f, 0.5f), new Vector2(-w * 0.28f, -75f), new Vector2(w * 0.5f, 56f));
        ui.minus = SmallButton(ui.levelRow, "－", new Vector2(-w * 0.19f, 0f));
        ui.plus = SmallButton(ui.levelRow, "＋", new Vector2(w * 0.19f, 0f));
        ui.level = ToyKit.UIText("Text", ui.levelRow, font, "Lv 3", 34f, ToyKit.Ink);
        ToyKit.Anchor(ui.level.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 56f));

        ui.join = ToyKit.UIText("Join", r, font, "A / Start で参加", 40f, new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.6f));
        ui.join.enableAutoSizing = true; ui.join.fontSizeMin = 16f; ui.join.fontSizeMax = 40f;
        ToyKit.Anchor(ui.join.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(w - 30f, 80f));

        ui.ok = ToyKit.UIText("OK", ui.portrait.rectTransform, font, "OK!", 56f, ToyKit.Palette[0]);
        ui.ok.fontSharedMaterial = popMat;
        ui.ok.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -12f);
        ToyKit.Anchor(ui.ok.rectTransform, new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(160f, 80f));
        return ui;
    }

    RectTransform SmallButton(Transform parent, string text, Vector2 pos)
    {
        var b = ToyKit.UIImage("Button" + text, parent, ToyKit.Palette[1], uiSprite);
        ToyKit.Anchor(b, new Vector2(0.5f, 0.5f), pos, new Vector2(56f, 52f));
        var ol = b.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(2f, -2f);
        var t = ToyKit.UIText("Text", b, font, text, 36f, ToyKit.Ink);
        ToyKit.Stretch(t.rectTransform);
        return b;
    }

    // プレイヤーのカーソル（色付きの丸に「1P」）
    RectTransform MakeCursor(Player p)
    {
        var r = ToyKit.UIImage("Cursor" + p.Label, cursorLayer, p.Color, knobSprite);
        r.GetComponent<Image>().type = Image.Type.Simple;
        ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), p.cursor, new Vector2(84f, 84f));
        var ol = r.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3f, -3f);
        var t = ToyKit.UIText("Label", r, font, p.Label, 34f, Color.white);
        t.fontSharedMaterial = letterMat;
        ToyKit.Stretch(t.rectTransform);
        return r;
    }

    // 選んだマスに置くコイン（CPU は灰色の「CP」）
    RectTransform MakeToken(Player p, Transform parent)
    {
        var r = ToyKit.UIImage("Token" + p.Label, parent, p.Color, knobSprite);
        r.GetComponent<Image>().type = Image.Type.Simple;
        ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f));
        var ol = r.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(2f, -2f);
        var t = ToyKit.UIText("Label", r, font, p.TokenLabel, 24f, Color.white);
        t.fontSharedMaterial = letterMat;
        ToyKit.Stretch(t.rectTransform);
        r.gameObject.SetActive(false);
        return r;
    }

    static string Initial(string name) => string.IsNullOrEmpty(name) ? "" : name.Substring(0, 1);
}
