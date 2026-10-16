using System.Collections;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.SceneManagement;

// 試合の結果を、リザルト画面（ResultScene）へ渡す係。
//  ・セレクトから始めた試合（DarumaMatch.IsConfigured）のシーンに、自動で付く（シーンに置く必要はない）
//  ・試合が終わったら結果を ResultData に写し、少し待ってからリザルト画面へ移る
//  ・DarumaMatch の「結果を見せてセレクトへ戻る」は、リザルト画面が代わりに行うので止めておく
// MatchSettings の「リザルト（表彰）」で、使う・使わない／シーン名／待ち時間を変えられる。
public class MatchResultRelay : MonoBehaviour
{
    DarumaMatch m_match;

    // シーンが読み込まれるたびに、試合のシーンなら自分を付ける
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // DarumaMatch.Current は Awake で入るので、ここ（Start より前）で見つかる
        var match = DarumaMatch.Current;
        if (match == null || match.gameObject.scene != scene) return;
        if (match.GetComponent<MatchResultRelay>() == null) match.gameObject.AddComponent<MatchResultRelay>();
    }

    void Start()
    {
        m_match = GetComponent<DarumaMatch>();
        // 直接再生（ステージのシーンをそのまま再生）は、今まで通りその場でやり直す
        if (m_match == null || !m_match.IsConfigured) { enabled = false; return; }
        m_match.Finished += OnFinished;
    }

    void OnDestroy()
    {
        if (m_match != null) m_match.Finished -= OnFinished;
    }

    void OnFinished(DarumaMatch.MatchResult result)
    {
        var settings = MatchSettings.Instance;
        if (settings == null || !settings.useResultScene) return;
        // 自動テスト（MatchSoakTest）が戻り先を止めているときは何もしない
        if (!DarumaMatch.AutoReturnToSelect) return;

        ResultData.Capture(result, m_match.Mode);
        // DarumaMatch が自分でセレクトへ戻らないようにする（リザルト画面で元に戻す）
        DarumaMatch.AutoReturnToSelect = false;
        StartCoroutine(GoToResult(settings, settings.ResultSceneFor(m_match.Mode)));
    }

    IEnumerator GoToResult(MatchSettings settings, string sceneName)
    {
        // 試合が終わると Time.timeScale = 0 になるので、実時間で待つ
        float end = Time.unscaledTime + settings.resultSceneDelay;
        while (Time.unscaledTime < end) yield return null;
        Fader.Load(sceneName);
    }
}

// 試合の結果の写し。試合のシーンが消えても残るように、必要な値だけをコピーしておく。
public static class ResultData
{
    public class Entry
    {
        public string label;            // 「1P」「CPU1」
        public Color playerColor;       // プレイヤーの色（1P 赤、2P 青…）
        public CharacterData character; // キャラクター（無ければ null）
        public int rank;                // 1〜（同じ順位あり）
        public int blocks;              // 試合が終わったときの積み木の数
        public List<Color> blockColors = new List<Color>();   // 試合が終わったときの積み木の色（下から）
        public int knocks;              // 崩した数
        public int kos;                 // 顔を飛ばした数
        public MatchPlayer.PlayerState state;   // 試合が終わったときの状態（生存 / 幽霊 / 復活待ち / 脱落）
        public int stocks = -1;         // 残りストック（ストックモード以外は -1）
        public int score;               // 点数（タイムモード）
    }

    public static readonly List<Entry> Ranking = new List<Entry>();   // 1位から順
    public static bool IsDraw { get; private set; }
    public static bool TimeUp { get; private set; }
    public static MatchSetup.MatchMode Mode { get; private set; }
    public static bool HasResult => Ranking.Count > 0;

    public static void Capture(DarumaMatch.MatchResult result, MatchSetup.MatchMode mode)
    {
        Ranking.Clear();
        IsDraw = result.IsDraw;
        TimeUp = result.Reason == DarumaMatch.FinishReason.TimeUp;
        Mode = mode;
        foreach (var p in result.Ranking)
        {
            if (p == null) continue;
            var entry = new Entry
            {
                label = p.Label,
                playerColor = p.Color,
                character = p.CharacterData,
                rank = p.Rank,
                blocks = p.StackCount,
                knocks = p.Knocks,
                kos = p.KOs,
                state = p.State,
                stocks = p.Stocks,
                score = p.Score,
            };
            var stack = p.Stack;
            if (stack != null)
                for (int i = 0; i < stack.Count; i++) entry.blockColors.Add(TumikiColorUtil.ToUnityColor(stack.Get(i).Color));
            Ranking.Add(entry);
        }
    }

    public static void Clear() => Ranking.Clear();
}
