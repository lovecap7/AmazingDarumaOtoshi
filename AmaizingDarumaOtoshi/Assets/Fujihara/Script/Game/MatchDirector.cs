using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

// 試合の進行（Fujihara 版）。セレクト画面で決めた内容（MatchSetup）どおりにプレイヤーを出し、モードのルールで勝ち負けを決める。
// Nakahira の DarumaMatch / DarumaPlayer は使わない（キャラクター・幽霊・積み木・ハンマーなどの部品はそのまま使う）。
//
// ■ モード
//   ストック：やられるとストックが1つ減り、少ししてから首だけで復活。0 になったら脱落。最後の1人が勝ち
//             時間制限ありなら、時間切れでストックが一番多い人の勝ち
//   タイム　：やられても少ししてから復活。崩す +1・崩される -1・顔を飛ばす +1・相手なしで落ちる -1（MatchSetup.Rules）。
//             時間切れで点が一番多い人の勝ち
//   ゴースト：やられると幽霊になり、生き残っている相手を叩くと復活（出現ポイントのどれかにランダムで）。生き残りが1人になったら終わり
//             時間制限ありなら、時間切れで生き残っている中で積み木が一番多い人の勝ち
//   同点（同じ数）のときは引き分け。
//
// ■ ゲーム画面の UI（HUD）を作るとき
//   MatchDirector.Current から読む。値を毎フレーム読んでも、イベントで受け取ってもよい。
//     Players            ：出ている人（番号順）。1人分の中身は MatchPlayer を参照
//     Rules / Mode       ：モードとルール
//     HasTimeLimit / TimeLeft / Elapsed：時間
//     IsFinished / Result：結果（勝者・理由・順位）
//   イベント
//     Started                  ：試合が始まった（全員が出たあと）
//     PlayerKnocked(被害, 相手)：積み木を崩された（相手が分かったときだけ）
//     PlayerKO(被害, 相手)     ：顔を飛ばされた / 落ちた（相手は分からなければ null）
//     PlayerRespawned(人)      ：復活した（ストック・タイムの復活、幽霊からの復活）
//     PlayerOut(人)            ：脱落した（ストックが無くなった）
//     Scored(人, 点, 理由)      ：点が動いた（タイム）
//     Finished(結果)            ：試合が終わった
//   1人分の積み木・ストック・点数の変化は MatchPlayer のイベント（StackChanged など）でも受け取れる。
[DefaultExecutionOrder(-50)]   // HUD などより先にプレイヤーを出す
public class MatchDirector : MonoBehaviour
{
    public enum ScoreReason { Knock, Knocked, KO, SelfOut }
    public enum FinishReason { LastOneStanding, TimeUp }

    public class MatchResult
    {
        public FinishReason Reason;
        public MatchPlayer Winner;                  // null = 引き分け
        public List<MatchPlayer> Ranking = new List<MatchPlayer>();   // 1位から順（同じ順位の人もいる。Rank を見る）
        public bool IsDraw => Winner == null;
    }

    public static MatchDirector Current { get; private set; }

    // false にすると、試合が終わってもセレクトへ戻らない（自動テスト用）
    public static bool AutoReturnToSelect = true;
    // 試合中の速さ（自動テスト用。ふだんは 1）
    public static float PlaySpeed = 1f;

    // ---- 読むもの ----
    public MatchSetup.MatchRules Rules => MatchSetup.Rules;
    public MatchSetup.MatchMode Mode => Rules.mode;
    public IReadOnlyList<MatchPlayer> Players => m_players;
    public bool HasTimeLimit => m_timeLimit > 0f;
    public float TimeLimit => m_timeLimit;
    public float TimeLeft => HasTimeLimit ? Mathf.Max(0f, m_timeLimit - Elapsed) : -1f;
    public float Elapsed { get; private set; }
    public bool IsFinished => Result != null;
    public MatchResult Result { get; private set; }
    public StageArea StageArea => m_stageArea;

    // ---- お知らせ ----
    public event Action Started;
    public event Action<MatchPlayer, MatchPlayer> PlayerKnocked;
    public event Action<MatchPlayer, MatchPlayer> PlayerKO;
    public event Action<MatchPlayer> PlayerRespawned;
    public event Action<MatchPlayer> PlayerOut;
    public event Action<MatchPlayer, int, ScoreReason> Scored;
    public event Action<MatchResult> Finished;

    const string SelectSceneName = "SelectScene";
    static readonly Color[] PlayerColors =
    {
        new Color(0.93f, 0.27f, 0.25f), new Color(0.22f, 0.55f, 0.95f), new Color(0.98f, 0.76f, 0.12f), new Color(0.30f, 0.74f, 0.35f),
        new Color(0.96f, 0.55f, 0.15f), new Color(0.40f, 0.85f, 0.85f), new Color(0.85f, 0.45f, 0.80f), new Color(0.55f, 0.45f, 0.35f),
    };
    static readonly Color CpuColor = new Color(0.55f, 0.55f, 0.58f);

    public static Color ColorFor(MatchSetup.Entry e) => e.isCpu ? CpuColor : PlayerColors[e.playerIndex % PlayerColors.Length];

    // 崩した / 顔を飛ばした、の記録（同じフレームの「やられた」と「当てた」を組にする）
    class Hit
    {
        public MatchPlayer victim, attacker;
        public bool head;
    }

    MatchSettings m_settings;
    StageArea m_stageArea;
    Transform[] m_spawnPoints;
    float m_timeLimit;
    readonly List<MatchPlayer> m_players = new List<MatchPlayer>();
    readonly Dictionary<DarumaCharacter, MatchPlayer> m_owners = new Dictionary<DarumaCharacter, MatchPlayer>();
    readonly List<Hit> m_hits = new List<Hit>();
    int m_outCount, m_lastOutFrame = -1;
    bool m_lastOnePending, m_timeUpFinish;
    float m_finishTime = -1f;
    bool m_leaving;

    // MatchBridge から呼ばれる（シーンの Awake の後・Start の前）
    public void Init(StageArea stageArea, Transform[] spawnPoints)
    {
        m_settings = MatchSettings.Instance;
        m_stageArea = stageArea;
        m_spawnPoints = spawnPoints;
        m_timeLimit = Rules.TimeLimit;
    }

    void Awake() => Current = this;

    void OnDestroy()
    {
        if (Current == this) Current = null;
        Time.timeScale = 1f;
    }

    void Start()
    {
        if (m_settings == null || m_settings.characterPrefab == null)
        {
            Debug.LogError("[MatchDirector] Resources/MatchSettings が無いか、characterPrefab が未設定です");
            enabled = false;
            return;
        }

        // プレイヤー番号の小さい順に出す
        foreach (var e in MatchSetup.Players)
        {
            var p = CreatePlayer(e);
            m_players.Add(p);
            SpawnCharacter(p, InitialSpawnPoint(m_players.Count - 1), false);
            Debug.Log($"[MatchDirector] {p.Label} {(e.character != null ? e.character.displayName : "-")} ({(e.isCpu ? "CPU Lv" + e.cpuLevel : e.deviceName + " #" + e.deviceId)})");
        }
        Debug.Log("[MatchDirector] " + MatchSetup.Describe());
        Time.timeScale = PlaySpeed;
        Started?.Invoke();
    }

    // ================= プレイヤーを作る =================
    MatchPlayer CreatePlayer(MatchSetup.Entry e)
    {
        GameObject go;
        IDarumaCommandSource source = null;
        var device = e.Device;
        if (e.isCpu)
        {
            go = new GameObject();
            var cpu = go.AddComponent<CpuDarumaInput>();
            cpu.level = e.cpuLevel;
            source = cpu;
        }
        else if (device is Gamepad && m_settings.gamepadInputPrefab != null)
        {
            // そのコントローラーと組にして、本編と同じ操作設定（DarumaInputHandler）で動かす
            var input = PlayerInput.Instantiate(m_settings.gamepadInputPrefab, playerIndex: e.playerIndex, controlScheme: "Gamepad", pairWithDevice: device);
            go = input.gameObject;
            source = go.GetComponent<IDarumaCommandSource>();
        }
        else if (device is Keyboard)
        {
            go = new GameObject();
            source = go.AddComponent<KeyboardDarumaInput>();
        }
        else
        {
            // コントローラーが抜けているなど：操作なしで出す（人数と番号はセレクトのまま）
            Debug.LogWarning($"[MatchDirector] {e.DisplayLabel} の入力機器（{e.deviceName} #{e.deviceId}）が見つからないので、操作なしで出します");
            go = new GameObject();
        }

        var p = go.AddComponent<MatchPlayer>();
        p.Setup(e, ColorFor(e), source, Mode == MatchSetup.MatchMode.Stock ? Mathf.Max(1, Rules.stocks) : -1);
        return p;
    }

    void SpawnCharacter(MatchPlayer p, Vector3 pos, bool invincible)
    {
        Vector3 toCenter = (m_stageArea != null ? m_stageArea.transform.position : Vector3.zero) - pos;
        toCenter.y = 0f;
        var rot = toCenter.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toCenter) : Quaternion.identity;

        var c = Instantiate(CharacterPrefab(p), pos, rot);
        if (invincible) c.SetInvincible(m_settings.respawnInvincibleTime);

        m_owners[c] = p;
        c.Eliminated += OnEliminated;
        c.DealtDamage += OnDealtDamage;
        int last = 0;
        c.Stack.Changed += () =>
        {
            // 積み木が減った：崩された（だれに崩されたかは、このあと同じフレームの DealtDamage で分かる）
            int n = c.Stack.Count;
            if (n < last) m_hits.Add(new Hit { victim = p });
            last = n;
        };
        p.SetCharacter(c);
    }

    DarumaCharacter CharacterPrefab(MatchPlayer p)
    {
        var data = p.CharacterData;
        if (data != null && data.prefab != null)
        {
            var custom = data.prefab.GetComponent<DarumaCharacter>();
            if (custom != null) return custom;
        }
        return m_settings.characterPrefab;
    }

    // ================= 毎フレーム =================
    void Update()
    {
        if (IsFinished) { UpdateResult(); return; }

        Elapsed += Time.deltaTime;

        // 復活待ち
        foreach (var p in m_players)
        {
            if (p.State != MatchPlayer.PlayerState.Respawning) continue;
            p.RespawnTimeLeft -= Time.deltaTime;
            if (p.RespawnTimeLeft > 0f) continue;
            SpawnCharacter(p, FarthestSpawnPoint(p), true);
            PlayerRespawned?.Invoke(p);
        }

        if (HasTimeLimit && Elapsed >= m_timeLimit) Finish(FinishReason.TimeUp);
        else if (AutoReturnToSelect && PressedBack()) BackToSelect();   // テスト用：途中でもセレクトへ戻れる
    }

    void LateUpdate()
    {
        ProcessHits();
        // 生き残りが1人になったあと、同じフレームで幽霊が復活していることがある（幽霊が生存者を倒した → 復活）。
        // 復活を優先し、フレームの最後にもう一度数えてから終わる
        if (m_lastOnePending && !IsFinished)
        {
            m_lastOnePending = false;
            if (CountLeft() <= 1) Finish(FinishReason.LastOneStanding);
        }
    }

    // 同じフレームの「やられた」と「当てた」を組にしてから、点数とお知らせを出す
    // （当てた側の DealtDamage は、やられた側の処理が終わってから届くので、フレームの最後にまとめて行う）
    void ProcessHits()
    {
        if (m_hits.Count == 0) return;
        var hits = m_hits.ToArray();
        m_hits.Clear();
        foreach (var h in hits)
        {
            if (h.head)
            {
                if (h.attacker != null)
                {
                    h.attacker.CountKO();
                    AddScore(h.attacker, Rules.headPoints, ScoreReason.KO);
                }
                else
                {
                    // 相手なしでやられた（自分から場外に落ちた など）
                    h.victim.CountSelfOut();
                    AddScore(h.victim, Rules.selfOutPoints, ScoreReason.SelfOut);
                }
                PlayerKO?.Invoke(h.victim, h.attacker);
            }
            else if (h.attacker != null)
            {
                // 相手が分からない減り方（自分で撃った・こぼれた等）は数えない
                h.attacker.CountKnock();
                h.victim.CountKnocked();
                AddScore(h.attacker, Rules.knockPoints, ScoreReason.Knock);
                AddScore(h.victim, Rules.knockedPoints, ScoreReason.Knocked);
                PlayerKnocked?.Invoke(h.victim, h.attacker);
            }
        }
    }

    void AddScore(MatchPlayer p, int points, ScoreReason reason)
    {
        if (Mode != MatchSetup.MatchMode.Time || IsFinished || points == 0) return;
        p.AddScore(points);
        Scored?.Invoke(p, points, reason);
    }

    // ================= 当てた・やられた =================
    // ハンマー・弾・連鎖で生存者にダメージを与えた（このすぐ前に、やられた側の記録が入っている）
    void OnDealtDamage(DarumaCharacter attackerCharacter)
    {
        if (m_owners.TryGetValue(attackerCharacter, out var attacker)) Attribute(attacker);
    }

    // いちばん新しい「相手がまだ分からないやられ」を、当てた人のものにする
    void Attribute(MatchPlayer attacker)
    {
        for (int i = m_hits.Count - 1; i >= 0; i--)
        {
            var h = m_hits[i];
            if (h.attacker != null || h.victim == attacker) continue;
            h.attacker = attacker;
            return;
        }
    }

    // 脱落した順番。同じフレームでやられた人は同じ順番（相打ち）にして、順位を同じにする（最後の2人が相打ちなら引き分け）
    int NextOutOrder()
    {
        if (Time.frameCount != m_lastOutFrame)
        {
            m_lastOutFrame = Time.frameCount;
            m_outCount++;
        }
        return m_outCount;
    }

    // 首だけで叩かれた・頭に当たった・落ちた（この直後にキャラクターは消える）
    void OnEliminated(DarumaCharacter c)
    {
        if (!m_owners.TryGetValue(c, out var p)) return;
        m_owners.Remove(c);
        if (IsFinished) return;

        p.CountFall();
        m_hits.Add(new Hit { victim = p, head = true });
        Vector3 pos = c.transform.position;

        switch (Mode)
        {
            case MatchSetup.MatchMode.Ghost:
                p.OutOrder = NextOutOrder();
                SpawnGhost(p, pos);
                break;
            case MatchSetup.MatchMode.Stock:
                p.LoseStock();
                if (p.Stocks > 0) p.SetRespawning(m_settings.respawnDelay);
                else
                {
                    p.SetOut(NextOutOrder());
                    PlayerOut?.Invoke(p);
                }
                break;
            default:
                p.SetRespawning(m_settings.respawnDelay);
                break;
        }

        CheckLastOne();
    }

    void SpawnGhost(MatchPlayer p, Vector3 pos)
    {
        if (m_settings.ghostPrefab == null) { p.SetOut(p.OutOrder); return; }
        if (m_stageArea != null) pos = m_stageArea.Clamp(pos, 0.5f);
        var ghost = Instantiate(m_settings.ghostPrefab, pos, Quaternion.identity);
        ghost.Init(m_stageArea);
        ghost.ReviveRequested += g =>
        {
            // 幽霊が生存者にダメージを与えた（幽霊には DealtDamage が無いので、ここで当てた人を記録する）
            Attribute(p);
            if (IsFinished) return;
            // その場で復活すると、直接殴った相手（近くにいる幽霊など）にすぐ殴り返されるので、出現ポイントのどれかに出す（仮）
            Destroy(g.gameObject);
            SpawnCharacter(p, RandomSpawnPoint(), true);
            PlayerRespawned?.Invoke(p);
        };
        p.SetGhost(ghost);
    }

    // ================= 試合の終わり =================
    // 生き残りが1人以下になったら終わり（ストック・ゴースト）
    void CheckLastOne()
    {
        if (Mode == MatchSetup.MatchMode.Time || m_players.Count < 2) return;
        if (CountLeft() <= 1) m_lastOnePending = true;   // 最後の一撃の「当てた人」と幽霊の復活が済んでから終わる（LateUpdate）
    }

    // まだ戦っている人数（ゴースト：生存者、ストック：脱落していない人）
    int CountLeft()
    {
        int left = 0;
        foreach (var p in m_players)
        {
            bool inGame = Mode == MatchSetup.MatchMode.Ghost ? p.IsAlive : p.State != MatchPlayer.PlayerState.Out;
            if (inGame) left++;
        }
        return left;
    }

    void Finish(FinishReason reason)
    {
        if (IsFinished) return;
        ProcessHits();   // このフレームの点数を先に入れる
        m_timeUpFinish = reason == FinishReason.TimeUp;

        foreach (var p in m_players) p.StopControl();
        Time.timeScale = 0f;   // その場で止める（セレクトへ戻るときに戻す）

        var result = new MatchResult { Reason = reason };
        result.Ranking.AddRange(m_players);
        result.Ranking.Sort((a, b) => RankKey(b).CompareTo(RankKey(a)));
        for (int i = 0; i < result.Ranking.Count; i++)
        {
            var p = result.Ranking[i];
            p.Rank = i > 0 && RankKey(p) == RankKey(result.Ranking[i - 1]) ? result.Ranking[i - 1].Rank : i + 1;
        }
        // 1位が1人だけなら勝者、2人以上なら引き分け
        if (result.Ranking.Count > 0 && (result.Ranking.Count == 1 || result.Ranking[1].Rank != 1)) result.Winner = result.Ranking[0];

        Result = result;
        m_finishTime = Time.unscaledTime;
        Debug.Log($"[MatchDirector] 試合終了（{(reason == FinishReason.TimeUp ? "時間切れ" : "最後の1人")}）：{(result.Winner != null ? result.Winner.Label + " の勝ち" : "引き分け")}");
        Finished?.Invoke(result);
    }

    // 順位の比べ方（大きいほど上）
    double RankKey(MatchPlayer p)
    {
        bool timeUp = m_timeUpFinish;
        switch (Mode)
        {
            case MatchSetup.MatchMode.Time:
                return p.Score;
            case MatchSetup.MatchMode.Stock:
                // 残っている人が上（時間切れならストックが多い順）。脱落した人は後に脱落した人ほど上
                if (p.State != MatchPlayer.PlayerState.Out) return 1000 + (timeUp ? p.Stocks : 0);
                return p.OutOrder;
            default:
                // 生き残っている人が上（時間切れなら積み木が多い順）。幽霊は後にやられた人ほど上
                if (p.IsAlive) return 1000 + (timeUp ? p.StackCount : 0);
                return p.OutOrder;
        }
    }

    void UpdateResult()
    {
        if (m_leaving || !AutoReturnToSelect) return;
        float t = Time.unscaledTime - m_finishTime;
        if (t > m_settings.resultTime || (t > 1f && PressedStart())) BackToSelect();
    }

    public void BackToSelect()
    {
        if (m_leaving) return;
        m_leaving = true;
        Time.timeScale = 1f;
        Fader.Load(SelectSceneName);
    }

    // ================= 出現ポイント =================
    // 最初は 1P から順に出現ポイント 1, 2, … に出す
    Vector3 InitialSpawnPoint(int order)
    {
        if (m_spawnPoints == null || m_spawnPoints.Length == 0) return Vector3.zero;
        return m_spawnPoints[order % m_spawnPoints.Length].position;
    }

    // 幽霊からの復活：出現ポイントからランダムに選ぶ
    Vector3 RandomSpawnPoint()
    {
        if (m_spawnPoints == null || m_spawnPoints.Length == 0) return Vector3.zero;
        return m_spawnPoints[UnityEngine.Random.Range(0, m_spawnPoints.Length)].position;
    }

    // 復活：ほかのキャラクター（生存者・幽霊）すべてから一番遠い出現ポイント
    Vector3 FarthestSpawnPoint(MatchPlayer self)
    {
        if (m_spawnPoints == null || m_spawnPoints.Length == 0) return Vector3.zero;
        Vector3 best = m_spawnPoints[0].position;
        float bestScore = float.MinValue;
        foreach (var point in m_spawnPoints)
        {
            float nearest = float.MaxValue;
            foreach (var p in m_players)
            {
                if (p == self || p.Body == null) continue;
                nearest = Mathf.Min(nearest, Vector3.Distance(point.position, p.Body.position));
            }
            if (nearest > bestScore) { bestScore = nearest; best = point.position; }
        }
        return best;
    }

    // ================= 入力 =================
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
}
