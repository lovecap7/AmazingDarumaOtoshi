using System.Collections.Generic;
using Nakahira;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// セレクト画面で決めた内容（MatchSetup）を、本編（Nakahira の DarumaMatch があるシーン）につなぐ。
// 本編側のファイルは変更せず、シーンが読み込まれたときに自動で動く。
//  ・MatchSetup が空（本編のシーンを直接開いた）なら何もしない → 本編の「ボタンを押して参加」がそのまま使える
//  ・MatchSetup があれば、本編の「ボタンで参加」を止めて、決まった人数・番号・機器・キャラでプレイヤーを出す
//      ゲームパッド：本編のプレイヤー（PlayerInputManager の Player Prefab）を、そのコントローラーと組にして出す
//      キーボード  ：KeyboardDarumaInput で操作する（本編の操作設定はゲームパッドのみのため）
//      CPU         ：CpuDarumaInput（仮の AI）で操作する
//  ・プレイヤーの番号はセレクトの番号（1P〜）に揃える（本編は登録順で番号を振るため、空き番があるとずれる）
//  ・頭の上に「1P」の名札を出し、頭の色をキャラの色にする（キャラごとのモデルができるまでの目印）
//  ・試合が終わったら勝者を出して、キャラクターセレクトへ戻る
//  ・ルールセレクトの内容（MatchSetup.Rules）を反映する
//      ゴーストなし：脱落したプレイヤーの幽霊を出さない（本編は必ず幽霊を出すので、出てきたらすぐ隠す）
//      時間制限あり：画面上に残り時間を出し、0 になったら試合終了。生き残っている中で積み木が一番多い人の勝ち（同じなら引き分け）
public class MatchBridge : MonoBehaviour
{
    const string SelectSceneName = "SelectScene";
    const float ReturnDelay = 4f;

    static readonly Color[] PlayerColors =
    {
        new Color(0.93f, 0.27f, 0.25f), new Color(0.22f, 0.55f, 0.95f), new Color(0.98f, 0.76f, 0.12f), new Color(0.30f, 0.74f, 0.35f),
        new Color(0.96f, 0.55f, 0.15f), new Color(0.40f, 0.85f, 0.85f), new Color(0.85f, 0.45f, 0.80f), new Color(0.55f, 0.45f, 0.35f),
    };
    static readonly Color CpuColor = new Color(0.55f, 0.55f, 0.58f);

    // 1人分（本編のプレイヤーと、セレクトで決めた内容を必ず組で持つ）
    class Slot
    {
        public DarumaPlayer player;
        public MatchSetup.Entry entry;
        public RectTransform tag;
        public DarumaCharacter tinted;   // 頭の色を付けたキャラクター（復活で作り直されたら付け直す）
    }

    DarumaMatch m_match;
    PlayerInputManager m_inputManager;
    readonly List<Slot> m_slots = new List<Slot>();
    RectTransform m_canvas;
    CanvasGroup m_finishGroup;
    TextMeshProUGUI m_finishText, m_timerText;
    TMP_FontAsset m_font;
    Material m_letterMat, m_popMat;
    float m_finishTime = -1f;
    float m_timeLeft = -1f;     // 時間制限の残り（時間制限なしなら -1）
    bool m_timeUp;
    bool m_leaving;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // シーンの Awake の後・Start の前に呼ばれる
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (MatchSetup.Players.Count == 0) return;
        var match = FindAnyObjectByType<DarumaMatch>();
        if (match == null) return;

        var inputManager = FindAnyObjectByType<PlayerInputManager>();
        if (inputManager != null) inputManager.DisableJoining();   // ボタンを押した人が勝手に参加しないように

        var bridge = new GameObject("MatchBridge").AddComponent<MatchBridge>();
        bridge.m_match = match;
        bridge.m_inputManager = inputManager;
    }

    void Start()
    {
        m_font = PresentationSettings.DefaultFont;
        m_letterMat = ToyKit.LetterMaterial(m_font);
        m_popMat = ToyKit.PopMaterial(m_letterMat);
        if (MatchSetup.Rules.timeLimit) m_timeLeft = MatchSetup.Rules.timeLimitSeconds;
        BuildUI();

        // プレイヤー番号の小さい順に出す
        foreach (var e in MatchSetup.Players)
        {
            var player = CreatePlayer(e);
            if (player == null) continue;
            if (!m_match.Register(player))
            {
                Destroy(player.gameObject);
                continue;
            }
            // 本編は登録順で番号を振るので、セレクトで決めた番号に揃える
            player.OnRegistered(m_match, e.playerIndex);
            if (player.Character != null) player.Character.name = player.name + "_Character";

            var slot = new Slot { player = player, entry = e };
            slot.tag = MakeTag(e);
            m_slots.Add(slot);
            Debug.Log($"[MatchBridge] {e.DisplayLabel} {(e.character != null ? e.character.displayName : "-")} ({(e.isCpu ? "CPU Lv" + e.cpuLevel : e.deviceName + " #" + e.deviceId)}) → {player.name}");
        }
    }

    DarumaPlayer CreatePlayer(MatchSetup.Entry e)
    {
        if (e.isCpu)
        {
            var go = new GameObject("CPU");
            go.AddComponent<CpuDarumaInput>().level = e.cpuLevel;   // DarumaPlayer より先に付ける（Awake で操作元として拾われる）
            return go.AddComponent<DarumaPlayer>();
        }

        var device = e.Device;
        if (device is Gamepad && m_inputManager != null && m_inputManager.playerPrefab != null)
        {
            var input = PlayerInput.Instantiate(m_inputManager.playerPrefab, playerIndex: e.playerIndex, controlScheme: "Gamepad", pairWithDevice: device);
            return input.GetComponent<DarumaPlayer>();
        }
        if (device is Keyboard)
        {
            var go = new GameObject("KeyboardPlayer");
            go.AddComponent<KeyboardDarumaInput>();
            return go.AddComponent<DarumaPlayer>();
        }

        // コントローラーが抜けているなど：操作なしで出す（人数と番号はセレクトのまま）
        Debug.LogWarning($"[MatchBridge] {e.PlayerLabel} の入力機器（{e.deviceName} #{e.deviceId}）が見つからないので、操作なしで出します");
        return new GameObject("NoInput").AddComponent<DarumaPlayer>();
    }

    void Update()
    {
        foreach (var s in m_slots)
        {
            if (!MatchSetup.Rules.ghost) HideGhost(s);
            TintHead(s);
            UpdateTag(s);
        }

        if (m_leaving) return;

        // 時間制限
        if (m_timeLeft >= 0f && m_finishTime < 0f)
        {
            m_timeLeft = Mathf.Max(0f, m_timeLeft - Time.deltaTime);
            int sec = Mathf.CeilToInt(m_timeLeft);
            m_timerText.text = $"{sec / 60}:{sec % 60:00}";
            m_timerText.color = sec <= 10 ? ToyKit.Palette[0] : Color.white;
            m_timerText.rectTransform.localScale = Vector3.one * (sec <= 10 ? 1f + 0.12f * Mathf.Pow(1f - (m_timeLeft % 1f), 4f) : 1f);
            if (m_timeLeft <= 0f) TimeUp();
        }

        // 試合終了 → 勝者を出して、しばらくしたらキャラクターセレクトへ
        if (m_match.IsFinished && m_finishTime < 0f)
        {
            Slot winner = m_slots.Find(s => s.player != null && s.player.IsSurvivor);
            ShowFinish("しあい しゅうりょう！", winner);
        }
        if (m_finishTime >= 0f)
        {
            m_finishGroup.alpha = Mathf.MoveTowards(m_finishGroup.alpha, 1f, Time.unscaledDeltaTime * 3f);
            float t = Time.unscaledTime - m_finishTime;
            if (t > ReturnDelay || (t > 1f && PressedStart())) BackToSelect();
        }
        else if (PressedBack()) BackToSelect();   // テスト用：途中でもセレクトへ戻れる
    }

    void ShowFinish(string title, Slot winner)
    {
        m_finishTime = Time.unscaledTime;
        m_finishText.text = winner != null
            ? $"{title}\n<size=70%>しょうしゃ：{winner.entry.DisplayLabel}　{CharaName(winner.entry)}</size>"
            : $"{title}\n<size=70%>ひきわけ</size>";
    }

    // 時間切れ：その場で止めて、生き残っている中で積み木が一番多い人を勝ちにする
    void TimeUp()
    {
        m_timeUp = true;
        foreach (var s in m_slots) if (s.player != null) s.player.StopControl();
        Time.timeScale = 0f;   // 本編の試合は時間制限を知らないので、止めておく（セレクトへ戻るときに戻す）

        Slot winner = null;
        int best = -1;
        bool tie = false;
        foreach (var s in m_slots)
        {
            if (s.player == null || !s.player.IsSurvivor) continue;
            int count = s.player.Character.Stack.Count;
            if (count > best) { best = count; winner = s; tie = false; }
            else if (count == best) tie = true;
        }
        if (tie) winner = null;
        Debug.Log($"[MatchBridge] 時間切れ：{(winner != null ? winner.entry.DisplayLabel + " の勝ち（積み木 " + best + " 個）" : "引き分け")}");
        ShowFinish("タイムアップ！", winner);
    }

    // ゴーストなし：本編が出した幽霊をすぐ隠す（隠れた幽霊は動かず、叩けないので復活もしない）
    static void HideGhost(Slot s)
    {
        var ghost = s.player != null ? s.player.Ghost : null;
        if (ghost != null && ghost.gameObject.activeSelf) ghost.gameObject.SetActive(false);
    }

    void BackToSelect()
    {
        m_leaving = true;
        Time.timeScale = 1f;
        Fader.Load(SelectSceneName);
    }

    void OnDestroy()
    {
        if (m_timeUp) Time.timeScale = 1f;
    }

    static bool PressedStart()
    {
        if (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)) return true;
        foreach (var g in Gamepad.all) if (g.startButton.wasPressedThisFrame || g.buttonSouth.wasPressedThisFrame) return true;
        return false;
    }

    static bool PressedBack()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
        foreach (var g in Gamepad.all) if (g.selectButton.wasPressedThisFrame) return true;
        return false;
    }

    // ---------------- 目印 ----------------
    // 頭の色をキャラの色にする（キャラクターは復活のたびに作り直されるので、変わったら付け直す）
    static void TintHead(Slot s)
    {
        var c = s.player != null ? s.player.Character : null;
        if (c == null || c == s.tinted || s.entry.character == null) return;
        s.tinted = c;
        foreach (var t in c.GetComponentsInChildren<Transform>())
        {
            if (t.name != "Head") continue;
            var r = t.GetComponent<Renderer>();
            if (r == null) continue;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", s.entry.character.color);
            r.SetPropertyBlock(mpb);
        }
    }

    void UpdateTag(Slot s)
    {
        Transform body = s.player != null ? s.player.CurrentBody : null;
        var cam = Camera.main;
        if (body == null || !body.gameObject.activeInHierarchy || cam == null) { s.tag.gameObject.SetActive(false); return; }

        float height = s.player.Character != null ? s.player.Character.Stack.Height + 0.6f : 1.8f;
        Vector3 sp = cam.WorldToScreenPoint(body.position + Vector3.up * height);
        bool visible = sp.z > 0f;
        s.tag.gameObject.SetActive(visible);
        if (!visible) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(m_canvas, sp, null, out var local);
        s.tag.anchoredPosition = local + new Vector2(0f, 24f);
        // 幽霊のときは薄く
        s.tag.GetComponent<CanvasGroup>().alpha = s.player.IsSurvivor ? 1f : 0.55f;
    }

    // ---------------- UI ----------------
    void BuildUI()
    {
        m_canvas = ToyKit.CreateOverlayCanvas("MatchUI", 20);

        var hint = ToyKit.UIText("Hint", m_canvas, m_font,
            "Esc / Select：キャラクターセレクトにもどる　（キーボード：WASD 移動 / Space ジャンプ / J 薙ぎ払い / K ショット）", 24f, Color.white);
        hint.fontSharedMaterial = m_letterMat;
        ToyKit.Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1800f, 40f));

        // 残り時間（時間制限ありのときだけ）
        m_timerText = ToyKit.UIText("Timer", m_canvas, m_font, "", 72f, Color.white);
        m_timerText.fontSharedMaterial = m_popMat;
        ToyKit.Anchor(m_timerText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(400f, 100f));
        m_timerText.gameObject.SetActive(m_timeLeft >= 0f);

        var finish = ToyKit.UIImage("Finish", m_canvas, new Color(0f, 0f, 0f, 0.45f));
        ToyKit.Stretch(finish);
        m_finishGroup = finish.gameObject.AddComponent<CanvasGroup>();
        m_finishGroup.alpha = 0f;
        m_finishGroup.blocksRaycasts = false;
        m_finishText = ToyKit.UIText("Text", finish, m_font, "", 110f, Color.white);
        m_finishText.fontSharedMaterial = m_popMat;
        ToyKit.Stretch(m_finishText.rectTransform);
    }

    // 「1P」の名札（CPU は灰色で「CPU」、複数いるときは「CPU1」「CPU2」）
    RectTransform MakeTag(MatchSetup.Entry e)
    {
        var color = e.isCpu ? CpuColor : PlayerColors[e.playerIndex % PlayerColors.Length];
        string label = e.DisplayLabel;
        var bg = ToyKit.UIImage("Tag" + label, m_canvas, color);
        ToyKit.Anchor(bg, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f + label.Length * 16f, 36f));
        bg.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
        bg.gameObject.AddComponent<CanvasGroup>();
        var t = ToyKit.UIText("Text", bg, m_font, label, 24f, Color.white);
        t.fontSharedMaterial = m_letterMat;
        ToyKit.Stretch(t.rectTransform);
        return bg;
    }

    static string CharaName(MatchSetup.Entry e) => e.character != null ? e.character.displayName : "-";
}
