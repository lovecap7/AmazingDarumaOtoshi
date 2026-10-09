#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nakahira;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 自動テスト（エディタ専用）。CPU だけの試合をモードごとに何回も続けて行い、ルールどおりに終わっているかを調べる。
// 再生中に MatchSoakTest.Run(1000) を呼ぶ（Unity MCP などから）。結果は Temp/SoakTest/ に書き出す。
//  ・人数 2〜4（全員 CPU、強さ・キャラ・ステージはランダム）、ルールもランダム（タイムの試合時間だけ testTimeLength）
//  ・試合が終わるたびに次の試合へ（セレクト画面には戻らない）
//  ・調べること：試合が終わる / 勝者と順位が正しい / ストック・点数の計算が合う / キャラクターが残っていない / エラーが出ない
public class MatchSoakTest : MonoBehaviour
{
    public static MatchSoakTest Instance { get; private set; }
    public static string Status => Instance == null ? "not running" : Instance.StatusText();

    // ---- 1試合分の記録 ----
    class Record
    {
        public MatchSetup.MatchMode mode;
        public string stage;
        public int players;
        public float timeLimit, duration, firstKO = -1f;
        public MatchDirector.FinishReason reason;
        public bool stalled, draw;
        public int winnerIndex = -1, winnerLevel;
        public float avgLevel;
        public int kos, attributedKOs, selfOuts, knocks, ghostReviveKOs, finishSavedByRevive;
        public List<string> problems = new List<string>();
    }

    int m_perMode;
    float m_speed, m_timeLength, m_stallLimit;
    MatchSetup.MatchMode[] m_modes = { MatchSetup.MatchMode.Stock, MatchSetup.MatchMode.Time, MatchSetup.MatchMode.Ghost };
    int m_modeIndex, m_done;
    readonly List<Record> m_records = new List<Record>();
    Record m_current;
    MatchDirector m_director;
    SelectRoster m_roster;
    readonly Dictionary<string, string> m_scenePaths = new Dictionary<string, string>();
    readonly Dictionary<string, int> m_errors = new Dictionary<string, int>();
    int m_errorCount;
    float m_realStart;
    int m_reviveFrame = -1;
    MatchPlayer m_revived;
    bool m_loading, m_finished;
    int m_checkTimer;

    // modes：テストするモード（"Stock,Time,Ghost" のように。省略ですべて）
    public static void Run(int perMode = 1000, float speed = 40f, float timeLength = 60f, string modes = null)
    {
        if (!Application.isPlaying) { Debug.LogError("再生中に呼んでください"); return; }
        if (Instance != null) Destroy(Instance.gameObject);
        var go = new GameObject("MatchSoakTest");
        DontDestroyOnLoad(go);
        var t = go.AddComponent<MatchSoakTest>();
        t.m_perMode = perMode;
        t.m_speed = speed;
        t.m_timeLength = timeLength;
        t.m_stallLimit = 600f;
        if (!string.IsNullOrEmpty(modes))
            t.m_modes = modes.Split(',').Select(m => (MatchSetup.MatchMode)System.Enum.Parse(typeof(MatchSetup.MatchMode), m.Trim())).ToArray();
        Instance = t;
    }

    public static void Stop()
    {
        if (Instance != null) { Instance.WriteReport(true); Destroy(Instance.gameObject); }
    }

    void Start()
    {
        Application.runInBackground = true;
        m_roster = AssetDatabase.LoadAssetAtPath<SelectRoster>("Assets/Fujihara/Data/SelectRoster.asset");
        foreach (var s in m_roster.stages)
        {
            var guid = AssetDatabase.FindAssets("t:Scene " + s.sceneName).FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == s.sceneName);
            if (guid != null) m_scenePaths[s.sceneName] = AssetDatabase.GUIDToAssetPath(guid);
        }
        MatchDirector.AutoReturnToSelect = false;
        MatchDirector.PlaySpeed = m_speed;
        Application.logMessageReceived += OnLog;
        SceneManager.sceneLoaded += OnSceneLoaded;
        m_realStart = Time.realtimeSinceStartup;
        NextMatch();
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        MatchDirector.AutoReturnToSelect = true;
        MatchDirector.PlaySpeed = 1f;
        Time.timeScale = 1f;
        if (Instance == this) Instance = null;
    }

    void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        m_errorCount++;
        string key = message.Length > 160 ? message.Substring(0, 160) : message;
        m_errors[key] = m_errors.TryGetValue(key, out int n) ? n + 1 : 1;
        if (m_current != null) m_current.problems.Add("エラー: " + key);
    }

    // ================= 試合を始める =================
    void NextMatch()
    {
        if (m_done >= m_perMode)
        {
            WriteReport(false);
            m_modeIndex++;
            m_done = 0;
            if (m_modeIndex >= m_modes.Length)
            {
                Debug.Log("[SoakTest] すべて終了");
                Destroy(gameObject);
                return;
            }
        }

        var mode = m_modes[m_modeIndex];
        var rules = MatchSetup.Rules;
        rules.mode = mode;
        float[] limits = { 0f, 60f, 120f, 180f };
        rules.stocks = Random.Range(1, 4);
        rules.stockTimeLimit = limits[Random.Range(0, limits.Length)];
        rules.ghostTimeLimit = limits[Random.Range(0, limits.Length)];
        rules.timeLength = m_timeLength;
        rules.knockPoints = 1; rules.knockedPoints = -1; rules.headPoints = 1; rules.selfOutPoints = -1;

        int count = Random.Range(2, 5);
        var entries = new List<MatchSetup.Entry>();
        for (int i = 0; i < count; i++)
        {
            entries.Add(new MatchSetup.Entry
            {
                playerIndex = i,
                isCpu = true,
                cpuLevel = Random.Range(1, 10),
                deviceName = "CPU",
                character = m_roster.characters[Random.Range(0, m_roster.characters.Count)],
                cpuNumber = count > 1 ? i + 1 : 0,
            });
        }
        var stage = m_roster.stages[Random.Range(0, m_roster.stages.Count)];
        MatchSetup.Set(entries, stage);

        m_current = new Record
        {
            mode = mode,
            stage = stage.sceneName,
            players = count,
            timeLimit = rules.TimeLimit,
            avgLevel = (float)entries.Average(e => e.cpuLevel),
        };
        m_director = null;
        m_finished = false;
        m_loading = true;
        Time.timeScale = 1f;
        EditorSceneManager.LoadSceneInPlayMode(m_scenePaths[stage.sceneName], new LoadSceneParameters(LoadSceneMode.Single));
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        m_loading = false;
        m_director = MatchDirector.Current;
        if (m_director == null) { m_current.problems.Add("MatchDirector が作られなかった"); return; }
        m_director.PlayerKO += OnKO;
        m_director.PlayerKnocked += (v, a) => m_current.knocks++;
        m_director.PlayerRespawned += p => { m_reviveFrame = Time.frameCount; m_revived = p; };
        m_director.Finished += OnFinished;
    }

    void OnKO(MatchPlayer victim, MatchPlayer attacker)
    {
        var r = m_current;
        r.kos++;
        if (r.firstKO < 0f) r.firstKO = m_director.Elapsed;
        if (attacker != null) r.attributedKOs++; else r.selfOuts++;

        // 幽霊が生存者を倒して復活した（同じフレームで復活したのが当てた人）
        if (r.mode == MatchSetup.MatchMode.Ghost && attacker != null && attacker == m_revived && m_reviveFrame == Time.frameCount)
        {
            r.ghostReviveKOs++;
            int alive = m_director.Players.Count(p => p.IsAlive);
            // 復活がなければ生存者が1人だった＝修正前なら、ここで試合が終わっていた
            if (alive == 2) r.finishSavedByRevive++;
        }
    }

    void OnFinished(MatchDirector.MatchResult result)
    {
        m_finished = true;
        m_current.reason = result.Reason;
        m_current.draw = result.IsDraw;
        m_current.duration = m_director.Elapsed;
        if (result.Winner != null)
        {
            m_current.winnerIndex = result.Winner.Index;
            m_current.winnerLevel = result.Winner.Entry.cpuLevel;
        }
    }

    // ================= 毎フレーム =================
    void Update()
    {
        if (m_loading || m_director == null) return;

        if (!m_finished)
        {
            if (m_director.Elapsed > m_stallLimit)
            {
                m_current.stalled = true;
                string states = string.Join(" ", m_director.Players.Select(p => p.Label + ":" + p.State));
                m_current.problems.Add($"{m_stallLimit} 秒たっても終わらない（{m_current.stage}・時間制限 {m_current.timeLimit} 秒・{states}）");
                m_current.duration = m_director.Elapsed;
                EndMatch();
                return;
            }
            if (++m_checkTimer % 20 == 0) CheckDuring();
            return;
        }

        // 試合が終わった：最後のフレームの処理（LateUpdate）が済んでから調べる
        if (m_director.IsFinished)
        {
            CheckResult();
            EndMatch();
        }
    }

    void EndMatch()
    {
        m_records.Add(m_current);
        m_done++;
        if (m_done % 100 == 0) WriteReport(true);
        var d = m_director;
        m_director = null;
        if (d != null) { d.PlayerKO -= OnKO; d.Finished -= OnFinished; }
        NextMatch();
    }

    // ================= 調べる =================
    void CheckDuring()
    {
        var d = m_director;
        int aliveState = d.Players.Count(p => p.State == MatchPlayer.PlayerState.Alive);
        int characters = FindObjectsByType<DarumaCharacter>(FindObjectsSortMode.None).Length;
        if (characters != aliveState) Problem($"キャラクターの数 {characters} と生存者 {aliveState} が合わない");
        int ghostState = d.Players.Count(p => p.State == MatchPlayer.PlayerState.Ghost);
        int ghosts = FindObjectsByType<DarumaGhost>(FindObjectsSortMode.None).Length;
        if (ghosts != ghostState) Problem($"幽霊の数 {ghosts} と幽霊状態 {ghostState} が合わない");
        foreach (var p in d.Players)
        {
            if (p.State == MatchPlayer.PlayerState.Respawning && p.RespawnTimeLeft < -0.5f) Problem($"{p.Label} が復活しない");
            if (p.State == MatchPlayer.PlayerState.Alive && p.Character == null) Problem($"{p.Label} は生存なのに体がない");
        }
        if (d.Mode != MatchSetup.MatchMode.Ghost && ghosts > 0) Problem("ゴースト以外なのに幽霊がいる");
    }

    void CheckResult()
    {
        var d = m_director;
        var r = d.Result;
        var rules = d.Rules;

        // 終わり方
        if (r.Reason == MatchDirector.FinishReason.TimeUp && (!d.HasTimeLimit || d.Elapsed + 0.05f < d.TimeLimit)) Problem("時間制限前にタイムアップした");
        if (d.Mode == MatchSetup.MatchMode.Time && r.Reason != MatchDirector.FinishReason.TimeUp) Problem("タイムなのに時間切れ以外で終わった");
        if (r.Reason == MatchDirector.FinishReason.LastOneStanding)
        {
            int left = d.Mode == MatchSetup.MatchMode.Ghost
                ? d.Players.Count(p => p.IsAlive)
                : d.Players.Count(p => p.State != MatchPlayer.PlayerState.Out);
            if (left > 1) Problem($"まだ {left} 人戦えるのに終わった");
            if (r.Winner != null)
            {
                if (d.Mode == MatchSetup.MatchMode.Ghost && !r.Winner.IsAlive) Problem("勝者が生存者ではない");
                if (d.Mode == MatchSetup.MatchMode.Stock && r.Winner.State == MatchPlayer.PlayerState.Out) Problem("勝者が脱落している");
            }
        }

        // 順位
        if (r.Ranking.Count != d.Players.Count) Problem("順位の人数が合わない");
        int firsts = r.Ranking.Count(p => p.Rank == 1);
        if (r.Winner != null && (firsts != 1 || r.Winner.Rank != 1)) Problem("勝者が1位ひとりになっていない");
        if (r.Winner == null && firsts < 2 && r.Reason == MatchDirector.FinishReason.TimeUp) Problem("引き分けなのに1位がひとり");
        for (int i = 1; i < r.Ranking.Count; i++)
            if (r.Ranking[i].Rank < r.Ranking[i - 1].Rank) Problem("順位の並びがおかしい");
        if (r.Reason == MatchDirector.FinishReason.TimeUp && r.Winner != null)
        {
            if (d.Mode == MatchSetup.MatchMode.Time && d.Players.Any(p => p != r.Winner && p.Score >= r.Winner.Score)) Problem("タイム：一番点が多い人が勝っていない");
            if (d.Mode == MatchSetup.MatchMode.Stock && d.Players.Any(p => p != r.Winner && p.State != MatchPlayer.PlayerState.Out && p.Stocks >= r.Winner.Stocks)) Problem("ストック：一番ストックが多い人が勝っていない");
            if (d.Mode == MatchSetup.MatchMode.Ghost && d.Players.Any(p => p != r.Winner && p.IsAlive && p.StackCount >= r.Winner.StackCount)) Problem("ゴースト：一番積み木が多い人が勝っていない");
        }

        // 数の計算
        int falls = 0, kos = 0, selfs = 0, knocks = 0, knocked = 0;
        foreach (var p in d.Players)
        {
            falls += p.Falls; kos += p.KOs; selfs += p.SelfOuts; knocks += p.Knocks; knocked += p.Knocked;
            if (d.Mode == MatchSetup.MatchMode.Stock)
            {
                int expect = Mathf.Max(0, rules.stocks - p.Falls);
                if (p.Stocks != expect) Problem($"{p.Label} のストック {p.Stocks}（やられた {p.Falls} 回、最初 {rules.stocks}）");
                if ((p.Stocks == 0) != (p.State == MatchPlayer.PlayerState.Out)) Problem($"{p.Label} のストックと脱落が合わない");
            }
            if (d.Mode == MatchSetup.MatchMode.Time)
            {
                int expect = rules.knockPoints * p.Knocks + rules.knockedPoints * p.Knocked + rules.headPoints * p.KOs + rules.selfOutPoints * p.SelfOuts;
                if (p.Score != expect) Problem($"{p.Label} の点数 {p.Score} が計算 {expect} と合わない");
            }
            else if (p.Score != 0) Problem($"タイム以外なのに点数がある（{p.Label}）");
        }
        if (kos + selfs != falls) Problem($"やられた回数 {falls} と（顔を飛ばされた {kos} + 自分から {selfs}）が合わない");
        if (knocks != knocked) Problem($"崩した {knocks} と崩された {knocked} が合わない");
        m_current.knocks = knocks;
    }

    void Problem(string text)
    {
        if (!m_current.problems.Contains(text)) m_current.problems.Add(text);
    }

    // ================= 結果 =================
    string StatusText()
    {
        float real = Time.realtimeSinceStartup - m_realStart;
        return $"{m_modes[Mathf.Min(m_modeIndex, m_modes.Length - 1)]} {m_done}/{m_perMode}（全体 {m_records.Count}/{m_perMode * m_modes.Length}）経過 {real / 60f:F1} 分 / エラー {m_errorCount} / 問題のあった試合 {m_records.Count(r => r.problems.Count > 0)}";
    }

    void WriteReport(bool partial)
    {
        Directory.CreateDirectory("Temp/SoakTest");
        var sb = new StringBuilder();
        sb.AppendLine($"# 自動テスト結果 {(partial ? "（途中）" : "")}  {System.DateTime.Now}");
        sb.AppendLine($"速さ x{m_speed} / タイムの試合時間 {m_timeLength} 秒 / 経過 {(Time.realtimeSinceStartup - m_realStart) / 60f:F1} 分");
        foreach (var mode in m_modes)
        {
            var list = m_records.Where(r => r.mode == mode).ToList();
            if (list.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"## {mode}：{list.Count} 試合");
            var bad = list.Where(r => r.problems.Count > 0).ToList();
            sb.AppendLine($"問題のあった試合：{bad.Count}（終わらない {list.Count(r => r.stalled)}）");
            foreach (var g in bad.SelectMany(r => r.problems).GroupBy(x => x).OrderByDescending(g => g.Count()).Take(15))
                sb.AppendLine($"  - {g.Key} ×{g.Count()}");
            var ok = list.Where(r => !r.stalled).ToList();
            if (ok.Count == 0) continue;
            sb.AppendLine($"試合時間：平均 {ok.Average(r => r.duration):F1} 秒 / 最短 {ok.Min(r => r.duration):F1} / 最長 {ok.Max(r => r.duration):F1}");
            sb.AppendLine($"最初にやられるまで：平均 {ok.Where(r => r.firstKO >= 0).DefaultIfEmpty(new Record()).Average(r => r.firstKO):F1} 秒");
            sb.AppendLine($"終わり方：最後の1人 {ok.Count(r => r.reason == MatchDirector.FinishReason.LastOneStanding)} / 時間切れ {ok.Count(r => r.reason == MatchDirector.FinishReason.TimeUp)} / 引き分け {ok.Count(r => r.draw)}（{100f * ok.Count(r => r.draw) / ok.Count:F1}%）");
            int kos = ok.Sum(r => r.kos), att = ok.Sum(r => r.attributedKOs), self = ok.Sum(r => r.selfOuts);
            sb.AppendLine($"やられた回数：{kos}（相手あり {att} / 相手なし {self} = {(kos > 0 ? 100f * self / kos : 0f):F1}%）　崩した回数：{ok.Sum(r => r.knocks)}");
            if (mode == MatchSetup.MatchMode.Ghost)
                sb.AppendLine($"幽霊が生存者を倒して復活：{ok.Sum(r => r.ghostReviveKOs)} 回（うち、修正前なら誤って試合が終わっていた場面 {ok.Sum(r => r.finishSavedByRevive)} 回）");
            // 番号ごとの勝率（出現ポイントの有利不利）
            var slot = new StringBuilder("番号ごとの勝率：");
            for (int i = 0; i < 4; i++)
            {
                int played = ok.Count(r => r.players > i);
                int won = ok.Count(r => r.winnerIndex == i);
                if (played > 0) slot.Append($"{i + 1}番 {100f * won / played:F0}%（{won}/{played}）  ");
            }
            sb.AppendLine(slot.ToString());
            var winners = ok.Where(r => r.winnerIndex >= 0).ToList();
            if (winners.Count > 0) sb.AppendLine($"CPU の強さ：勝者の平均 Lv{winners.Average(r => r.winnerLevel):F1} / 参加者の平均 Lv{ok.Average(r => r.avgLevel):F1}");
            // ステージごとの相手なしでやられた割合
            var stages = ok.GroupBy(r => r.stage).Select(g => new { g.Key, n = g.Count(), self = g.Sum(r => r.selfOuts), kos = g.Sum(r => r.kos), dur = g.Average(r => r.duration) })
                .OrderByDescending(s => s.kos > 0 ? (float)s.self / s.kos : 0f).ToList();
            sb.AppendLine("ステージ（相手なしでやられた割合の高い順）：");
            foreach (var s in stages) sb.AppendLine($"  {s.Key}：{s.n} 試合 / 自滅 {(s.kos > 0 ? 100f * s.self / s.kos : 0f):F0}% / 平均 {s.dur:F1} 秒");
        }
        sb.AppendLine();
        sb.AppendLine($"## エラー：{m_errorCount} 件");
        foreach (var e in m_errors.OrderByDescending(e => e.Value).Take(20)) sb.AppendLine($"  - ×{e.Value} {e.Key}");
        File.WriteAllText("Temp/SoakTest/report_" + string.Join("_", m_modes) + ".md", sb.ToString(), new UTF8Encoding(true));
    }
}
#endif
