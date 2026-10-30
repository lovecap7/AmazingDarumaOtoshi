using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Nakagawa
{
    // 積み木の生成の司令塔
    // 一定間隔(パターンごとの最小〜最大のランダム)で、生成パターンを順番に(2つなら交互に)使って生成命令を出す
    // 生成するエリアは、有効になった後の最初のフレームに盤面(シーン内の有効なTumikiSpawnSource。生成エリア・回転砲台など)を読み取って決める
    // 生成する数は各エリアの最小排出量を配ったうえで、少ないエリアから順に(同数ならランダムに)最大排出量まで割り振る
    // 生成予定数が全エリアの最小の合計より少ない / 最大の合計より多い場合は、生成予定数を合計に合わせて色を作り直す
    // 全エリアは同じタイミングで命令を受け取り、それぞれ命令どおりに生成する(プレイヤーがいるエリアはスキップ)
    // 生成は床の予告(TumikiSpawnWarning)を出してから行う(予告の時間はエリアごとの設定)
    // 盤面にある積み木(積まれていないもの。予告中のものも含む)が上限以上なら、その回はどのエリアも生成しない
    // 開始時(ゲーム開始・ステージ切り替え・試合のやり直し)には、予告なしですぐに置けるエリアへ最初から積み木を置いておく
    // 生成する数・盤面の上限・エリアの最小排出量は「基準の人数(4人)のときの値」として、ステージにいる人数に合わせて増減する
    // (ステージにいる人数 = 生きている人と復活待ちの人。幽霊・脱落した人は数えない)
    public class TumikiSpawner : MonoBehaviour
    {
        [SerializeField] TumikiBlock m_tumikiPrefab;
        // 順番に使う生成パターン
        [SerializeField] TumikiSpawnPattern[] m_patterns;

        [Header("条件")]
        // 盤面の積み木がこの数以上なら生成しない(基準の人数のとき)
        [SerializeField] int m_maxBlocksOnBoard = 30;
        // 開始から最初の生成までの時間
        [SerializeField] float m_firstDelay = 3.0f;
        // 試合が終わったら生成をやめる(未設定なら止めない)
        [SerializeField] DarumaMatch m_match;
        [SerializeField] bool m_log = true;

        [Header("開始時")]
        // 開始時に、最初から積み木を置いておく
        [SerializeField] bool m_fillAtStart = true;
        // 最初に置く数(基準の人数のとき)。0 なら最初の生成パターンの数
        [SerializeField, Min(0)] int m_startCount = 0;

        [Header("人数による調整")]
        // 生成する数・盤面の上限・エリアの最小排出量を、ステージにいる人数に合わせて増減する
        [SerializeField] bool m_scaleByPlayers = true;
        // 設定した数が何人のときのものか
        [SerializeField, Min(1)] int m_referencePlayers = 4;

        int m_patternIndex;
        float m_timer;
        bool m_needsScan;
        bool m_needsFill;
        DarumaMatch m_subscribed;
        TumikiSpawnSource[] m_areas = Array.Empty<TumikiSpawnSource>();
        readonly List<Vector3> m_reserved = new List<Vector3>();
        readonly List<TumikiSpawnSource> m_placeable = new List<TumikiSpawnSource>();

        // 生成命令を出した(使ったパターン、命令した数、実際に生成(予告)した数)。演出・UI等の拡張用
        public event Action<TumikiSpawnPattern, int, int> Spawned;
        // 盤面の積み木が多すぎて生成しなかった
        public event Action<int> SpawnSkipped;
        // 開始時に積み木を置いた(置いた数)
        public event Action<int> Filled;

        public TumikiSpawnPattern NextPattern => HasPatterns ? m_patterns[m_patternIndex % m_patterns.Length] : null;
        public IReadOnlyList<TumikiSpawnSource> Areas => m_areas;
        bool HasPatterns => m_patterns != null && m_patterns.Length > 0;
        DarumaMatch Match => m_match != null ? m_match : DarumaMatch.Current;

        // ステージにいる人数(生きている人と復活待ちの人。幽霊・脱落した人は数えない)。試合がなければ基準の人数
        public int PlayersOnStage
        {
            get
            {
                var match = Match;
                if (match == null) return m_referencePlayers;
                int n = 0;
                foreach (var p in match.Players)
                {
                    if (p != null && (p.State == MatchPlayer.PlayerState.Alive || p.State == MatchPlayer.PlayerState.Respawning)) n++;
                }
                return n;
            }
        }

        // 基準の人数に対する倍率(4人基準で2人なら 0.5。最低1人として数える)
        public float PlayerScale => m_scaleByPlayers ? Mathf.Max(1, PlayersOnStage) / (float)m_referencePlayers : 1.0f;
        // 今の人数での盤面の上限
        public int MaxBlocksOnBoard => ScaleCount(m_maxBlocksOnBoard, PlayerScale);

        // 有効になるたび(ゲーム開始・ステージ切り替え)に最初からやり直す
        // 同時に有効になったエリアがまだ見つからないことがあるので、盤面の読み取りと最初の配置は次のUpdateで行う
        // (そのときにはプレイヤーが出ているので、プレイヤーと重ならない位置に置ける)
        private void OnEnable()
        {
            ResetSpawning();
            m_needsScan = true;
            m_subscribed = Match;
            if (m_subscribed != null) m_subscribed.Started += ResetSpawning;
        }

        private void OnDisable()
        {
            if (m_subscribed != null) m_subscribed.Started -= ResetSpawning;
            m_subscribed = null;
        }

        // 最初からやり直す(試合が始まり直したとき。ネット対戦では積み木を消してからやり直す)
        private void ResetSpawning()
        {
            m_patternIndex = 0;
            m_timer = m_firstDelay;
            m_needsFill = m_fillAtStart;
        }

        private void Update()
        {
            if (m_needsScan) ScanAreas();
            if (m_needsFill) FillAtStart();
            // 試合が終わった・開始のカウントダウン中は生成しない(最初の生成までの時間もカウントダウンが終わってから数える)
            if (!HasPatterns || m_match != null && (m_match.IsFinished || m_match.IsCountingDown)) return;

            m_timer -= Time.deltaTime;
            if (m_timer > 0.0f) return;
            SpawnNext();
        }

        // 盤面を読み取り、有効な生成エリアをすべて使う(ステージの構成を変えたときにも呼べる)
        public void ScanAreas()
        {
            m_needsScan = false;
            m_areas = FindObjectsByType<TumikiSpawnSource>();
            // 割り振りの順番が毎回同じになるよう名前順にする
            Array.Sort(m_areas, (a, b) => string.CompareOrdinal(a.name, b.name));
            if (m_log)
            {
                Debug.Log($"[TumikiSpawner] 生成エリアを{m_areas.Length}か所見つけました (排出量の合計 {SumMin(1.0f)}〜{SumMax()})");
            }
        }

        // 開始時の配置: 予告なしですぐに置けるエリア(回転砲台などは除く)へ、最初のパターンの色で置く
        // 数は基準の人数のときの数を人数に合わせ、盤面の上限を超えないようにする
        [ContextMenu("Fill Now")]
        public void FillAtStart()
        {
            m_needsFill = false;
            if (!HasPatterns || m_tumikiPrefab == null) return;
            if (m_needsScan) ScanAreas();

            m_placeable.Clear();
            foreach (var area in m_areas) if (area != null && area.CanPlaceImmediately) m_placeable.Add(area);
            if (m_placeable.Count == 0) return;

            TumikiSpawnPattern pattern = NextPattern;
            float scale = PlayerScale;
            int baseCount = m_startCount > 0 ? m_startCount : pattern.RollCount();
            int count = ScaleCount(baseCount, scale);
            count = Mathf.Min(count, MaxBlocksOnBoard - CountBlocksOnBoard());
            int max = 0;
            foreach (var area in m_placeable) max += area.MaxAmount;
            count = Mathf.Min(count, max);
            if (count <= 0) return;

            // 開始時は最小排出量にこだわらず、置ける数の中で少ないエリアから順に配る
            List<TumikiColor> colors = pattern.CreateColors(count);
            int[] amounts = Distribute(count, m_placeable, 0.0f);
            m_reserved.Clear();
            int placed = 0;
            int start = 0;
            for (int i = 0; i < m_placeable.Count; i++)
            {
                var slice = colors.GetRange(start, amounts[i]);
                start += amounts[i];
                placed += m_placeable[i].PlaceImmediately(slice, m_tumikiPrefab, m_reserved);
            }

            if (m_log) Debug.Log($"[TumikiSpawner] 開始時の配置: {count}個を命令 → {placed}個配置 (人数{PlayersOnStage} / 倍率{scale:0.##} / 割り振り {string.Join(",", amounts)})");
            Filled?.Invoke(placed);
        }

        // 次のパターンですぐに生成命令を出す(次の生成までの時間もリセットされる)
        [ContextMenu("Spawn Now")]
        public void SpawnNext()
        {
            if (!HasPatterns) return;
            if (m_needsScan) ScanAreas();
            TumikiSpawnPattern pattern = NextPattern;
            m_patternIndex = (m_patternIndex + 1) % m_patterns.Length;
            m_timer = pattern.RollInterval();

            // 盤面の上限・生成する数・エリアの最小排出量は、今ステージにいる人数に合わせる
            float scale = PlayerScale;
            int maxOnBoard = ScaleCount(m_maxBlocksOnBoard, scale);
            int onBoard = CountBlocksOnBoard();
            if (onBoard >= maxOnBoard)
            {
                if (m_log) Debug.Log($"[TumikiSpawner] 盤面の積み木が{onBoard}個(上限{maxOnBoard})のため生成しません");
                SpawnSkipped?.Invoke(onBoard);
                return;
            }

            int rolled = ScaleCount(pattern.RollCount(), scale);

            // 生成予定数がエリアの排出量の範囲外なら、範囲に収める
            int sumMin = SumMin(scale), sumMax = SumMax();
            int count = Mathf.Clamp(rolled, sumMin, sumMax);
            if (count != rolled && m_log)
            {
                Debug.Log($"[TumikiSpawner] 生成予定数{rolled}個がエリアの排出量の合計({sumMin}〜{sumMax})の範囲外のため、{count}個に調整します");
            }
            List<TumikiColor> colors = pattern.CreateColors(count);
            int[] amounts = Distribute(count, m_areas, scale);

            // 全エリアに同じタイミングで命令する。生成位置は共有して、エリアをまたいでも重ならないようにする
            m_reserved.Clear();
            int spawned = 0;
            int start = 0;
            for (int i = 0; i < m_areas.Length; i++)
            {
                var slice = colors.GetRange(start, amounts[i]);
                start += amounts[i];
                if (m_areas[i] != null) spawned += m_areas[i].Spawn(slice, m_tumikiPrefab, m_reserved);
            }

            if (m_log) Debug.Log($"[TumikiSpawner] {pattern.name}: {count}個を命令 → {spawned}個生成 (人数{PlayersOnStage} / 倍率{scale:0.##} / 割り振り {string.Join(",", amounts)})");
            Spawned?.Invoke(pattern, count, spawned);
        }

        // 基準の人数のときの数を、倍率に合わせる(1以上の数は、減らしても1は残す)
        private static int ScaleCount(int count, float scale)
        {
            if (count <= 0) return 0;
            return Mathf.Max(1, Mathf.RoundToInt(count * scale));
        }

        // エリアの最小排出量を人数に合わせる(倍率 1 なら設定どおり。0 なら最小排出量なし)
        private static int ScaledMin(TumikiSpawnSource area, float scale)
        {
            return area != null ? Mathf.FloorToInt(area.MinAmount * scale) : 0;
        }

        // 各エリアに最小排出量(人数に合わせたもの)を配り、残りは最大排出量に達していないエリアのうち一番少ないところへ1個ずつ(同数ならランダム)
        // count は全エリアの最小の合計〜最大の合計の範囲であること
        private static int[] Distribute(int count, IReadOnlyList<TumikiSpawnSource> areas, float minScale)
        {
            var amounts = new int[areas.Count];
            int rest = count;
            for (int i = 0; i < areas.Count; i++)
            {
                amounts[i] = ScaledMin(areas[i], minScale);
                rest -= amounts[i];
            }

            // 同数のときに選ぶエリアをランダムにするため、調べる順番を混ぜておく
            var order = new List<int>(areas.Count);
            for (int i = 0; i < areas.Count; i++) order.Add(i);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            for (; rest > 0; rest--)
            {
                int best = -1;
                foreach (int i in order)
                {
                    if (areas[i] == null || amounts[i] >= areas[i].MaxAmount) continue;
                    if (best < 0 || amounts[i] < amounts[best]) best = i;
                }
                if (best < 0) break;
                amounts[best]++;
            }
            return amounts;
        }

        private int SumMin(float scale)
        {
            int sum = 0;
            foreach (var area in m_areas) sum += ScaledMin(area, scale);
            return sum;
        }

        private int SumMax()
        {
            int sum = 0;
            foreach (var area in m_areas) if (area != null) sum += area.MaxAmount;
            return sum;
        }

        // 盤面にある積み木(プレイヤーに積まれているものは除く。予告中・発射待ちでまだ生成していないものは含む)
        public static int CountBlocksOnBoard()
        {
            int n = TumikiSpawnWarning.Active.Count;
            foreach (var source in FindObjectsByType<TumikiSpawnSource>()) n += source.PendingCount;
            foreach (var block in FindObjectsByType<TumikiBlock>())
            {
                if (block.CurrentState != TumikiBlock.State.Stacked) n++;
            }
            return n;
        }
    }
}
