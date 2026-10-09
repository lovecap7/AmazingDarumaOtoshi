using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Nakagawa
{
    // 積み木の生成の司令塔
    // 一定間隔(パターンごとの最小〜最大のランダム)で、生成パターンを順番に(2つなら交互に)使って生成命令を出す
    // 生成するエリアは、有効になった後の最初のフレームに盤面(シーン内の有効なTumikiSpawnArea)を読み取って決める
    // 生成する数は各エリアの最小排出量を配ったうえで、少ないエリアから順に(同数ならランダムに)最大排出量まで割り振る
    // 生成予定数が全エリアの最小の合計より少ない / 最大の合計より多い場合は、生成予定数を合計に合わせて色を作り直す
    // 全エリアは同じタイミングで命令を受け取り、それぞれ命令どおりに生成する(プレイヤーがいるエリアはスキップ)
    // 盤面にある積み木(積まれていないもの)が上限以上なら、その回はどのエリアも生成しない
    public class TumikiSpawner : MonoBehaviour
    {
        [SerializeField] TumikiBlock m_tumikiPrefab;
        // 順番に使う生成パターン
        [SerializeField] TumikiSpawnPattern[] m_patterns;

        [Header("条件")]
        // 盤面の積み木がこの数以上なら生成しない
        [SerializeField] int m_maxBlocksOnBoard = 30;
        // 開始から最初の生成までの時間
        [SerializeField] float m_firstDelay = 3.0f;
        // 試合が終わったら生成をやめる(未設定なら止めない)
        [SerializeField] DarumaMatch m_match;
        [SerializeField] bool m_log = true;

        int m_patternIndex;
        float m_timer;
        bool m_needsScan;
        TumikiSpawnArea[] m_areas = Array.Empty<TumikiSpawnArea>();
        readonly List<Vector3> m_reserved = new List<Vector3>();

        // 生成命令を出した(使ったパターン、命令した数、実際に生成した数)。演出・UI等の拡張用
        public event Action<TumikiSpawnPattern, int, int> Spawned;
        // 盤面の積み木が多すぎて生成しなかった
        public event Action<int> SpawnSkipped;

        public TumikiSpawnPattern NextPattern => HasPatterns ? m_patterns[m_patternIndex % m_patterns.Length] : null;
        public IReadOnlyList<TumikiSpawnArea> Areas => m_areas;
        bool HasPatterns => m_patterns != null && m_patterns.Length > 0;

        // 有効になるたび(ゲーム開始・ステージ切り替え)に最初からやり直す
        // 同時に有効になったエリアがまだ見つからないことがあるので、盤面の読み取りは次のUpdateで行う
        private void OnEnable()
        {
            m_patternIndex = 0;
            m_timer = m_firstDelay;
            m_needsScan = true;
        }

        private void Update()
        {
            if (m_needsScan) ScanAreas();
            if (!HasPatterns || m_match != null && m_match.IsFinished) return;

            m_timer -= Time.deltaTime;
            if (m_timer > 0.0f) return;
            SpawnNext();
        }

        // 盤面を読み取り、有効な生成エリアをすべて使う(ステージの構成を変えたときにも呼べる)
        public void ScanAreas()
        {
            m_needsScan = false;
            m_areas = FindObjectsByType<TumikiSpawnArea>();
            // 割り振りの順番が毎回同じになるよう名前順にする
            Array.Sort(m_areas, (a, b) => string.CompareOrdinal(a.name, b.name));
            if (m_log)
            {
                Debug.Log($"[TumikiSpawner] 生成エリアを{m_areas.Length}か所見つけました (排出量の合計 {SumMin()}〜{SumMax()})");
            }
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

            int onBoard = CountBlocksOnBoard();
            if (onBoard >= m_maxBlocksOnBoard)
            {
                if (m_log) Debug.Log($"[TumikiSpawner] 盤面の積み木が{onBoard}個(上限{m_maxBlocksOnBoard})のため生成しません");
                SpawnSkipped?.Invoke(onBoard);
                return;
            }

            int rolled = pattern.RollCount();
            List<TumikiColor> colors = pattern.CreateColors(rolled);

            // 生成予定数がエリアの排出量の範囲外なら、範囲に収めて色を作り直す
            int count = Mathf.Clamp(rolled, SumMin(), SumMax());
            if (count != rolled)
            {
                if (m_log) Debug.Log($"[TumikiSpawner] 生成予定数{rolled}個がエリアの排出量の合計({SumMin()}〜{SumMax()})の範囲外のため、{count}個に調整して作り直します");
                colors = pattern.CreateColors(count);
            }
            int[] amounts = Distribute(count, m_areas);

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

            if (m_log) Debug.Log($"[TumikiSpawner] {pattern.name}: {count}個を命令 → {spawned}個生成 (割り振り {string.Join(",", amounts)})");
            Spawned?.Invoke(pattern, count, spawned);
        }

        // 各エリアに最小排出量を配り、残りは最大排出量に達していないエリアのうち一番少ないところへ1個ずつ(同数ならランダム)
        // count は全エリアの最小の合計〜最大の合計の範囲であること
        private static int[] Distribute(int count, TumikiSpawnArea[] areas)
        {
            var amounts = new int[areas.Length];
            int rest = count;
            for (int i = 0; i < areas.Length; i++)
            {
                amounts[i] = areas[i] != null ? areas[i].MinAmount : 0;
                rest -= amounts[i];
            }

            // 同数のときに選ぶエリアをランダムにするため、調べる順番を混ぜておく
            var order = new List<int>(areas.Length);
            for (int i = 0; i < areas.Length; i++) order.Add(i);
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

        private int SumMin()
        {
            int sum = 0;
            foreach (var area in m_areas) if (area != null) sum += area.MinAmount;
            return sum;
        }

        private int SumMax()
        {
            int sum = 0;
            foreach (var area in m_areas) if (area != null) sum += area.MaxAmount;
            return sum;
        }

        // 盤面にある積み木(プレイヤーに積まれているものは除く)
        public static int CountBlocksOnBoard()
        {
            int n = 0;
            foreach (var block in FindObjectsByType<TumikiBlock>())
            {
                if (block.CurrentState != TumikiBlock.State.Stacked) n++;
            }
            return n;
        }
    }
}
