using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Nakagawa
{
    // 積み木の生成の司令塔
    // 一定間隔(パターンごとの最小〜最大のランダム)で、生成パターンを順番に(2つなら交互に)使って生成命令を出す
    // 生成する数は全エリアに均等に割り振り、余りはランダムなエリアに1個ずつ割り振る
    // 全エリアは同じタイミングで命令を受け取り、それぞれ命令どおりに生成する(プレイヤーがいるエリアはスキップ)
    // 盤面にある積み木(積まれていないもの)が上限以上なら、その回はどのエリアも生成しない
    public class TumikiSpawner : MonoBehaviour
    {
        [SerializeField] TumikiBlock m_tumikiPrefab;
        // 順番に使う生成パターン
        [SerializeField] TumikiSpawnPattern[] m_patterns;
        // 生成するエリア(空なら開始時にシーン内のすべてのエリアを使う)
        [SerializeField] TumikiSpawnArea[] m_areas;

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
        readonly List<Vector3> m_reserved = new List<Vector3>();

        // 生成命令を出した(使ったパターン、命令した数、実際に生成した数)。演出・UI等の拡張用
        public event Action<TumikiSpawnPattern, int, int> Spawned;
        // 盤面の積み木が多すぎて生成しなかった
        public event Action<int> SpawnSkipped;

        public TumikiSpawnPattern NextPattern => HasPatterns ? m_patterns[m_patternIndex % m_patterns.Length] : null;
        bool HasPatterns => m_patterns != null && m_patterns.Length > 0;

        private void Start()
        {
            if (m_areas == null || m_areas.Length == 0)
            {
                m_areas = FindObjectsByType<TumikiSpawnArea>();
            }
            m_timer = m_firstDelay;
        }

        private void Update()
        {
            if (!HasPatterns || m_match != null && m_match.IsFinished) return;

            m_timer -= Time.deltaTime;
            if (m_timer > 0.0f) return;
            SpawnNext();
        }

        // 次のパターンですぐに生成命令を出す(次の生成までの時間もリセットされる)
        [ContextMenu("Spawn Now")]
        public void SpawnNext()
        {
            if (!HasPatterns) return;
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

            int count = pattern.RollCount();
            List<TumikiColor> colors = pattern.CreateColors(count);
            int[] amounts = Distribute(count, m_areas.Length);

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

        // 均等に割り振り、余りは重複しないランダムなエリアに1個ずつ
        private static int[] Distribute(int count, int areaCount)
        {
            var amounts = new int[areaCount];
            if (areaCount == 0) return amounts;

            int each = count / areaCount;
            for (int i = 0; i < areaCount; i++) amounts[i] = each;

            var order = new List<int>(areaCount);
            for (int i = 0; i < areaCount; i++) order.Add(i);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            int remainder = count - each * areaCount;
            for (int i = 0; i < remainder; i++) amounts[order[i]]++;
            return amounts;
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
