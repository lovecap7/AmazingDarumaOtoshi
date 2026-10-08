using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Nakagawa
{
    // 積み木の生成パターン。1回に生成する数・次の生成までの間隔・色の決め方を持つ
    // 色の決め方はサブクラスで実装する(新しいパターンはこのクラスを継承して作る)
    public abstract class TumikiSpawnPattern : ScriptableObject
    {
        [Header("生成する数")]
        [Min(0)] public int MinCount = 6;
        [Min(0)] public int MaxCount = 10;

        [Header("このパターンを生成してから次の生成までの間隔(秒)")]
        [Min(0.1f)] public float MinInterval = 6.0f;
        [Min(0.1f)] public float MaxInterval = 9.0f;

        // 全色(色が増えても自動で対象になる)
        protected static readonly TumikiColor[] kAllColors = (TumikiColor[])Enum.GetValues(typeof(TumikiColor));

        public int RollCount() => Random.Range(MinCount, MaxCount + 1);
        public float RollInterval() => Random.Range(MinInterval, MaxInterval);

        // count個分の色をランダムな順で返す
        public abstract List<TumikiColor> CreateColors(int count);

        protected static TumikiColor RandomColor() => kAllColors[Random.Range(0, kAllColors.Length)];

        protected static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        protected virtual void OnValidate()
        {
            MaxCount = Mathf.Max(MinCount, MaxCount);
            MaxInterval = Mathf.Max(MinInterval, MaxInterval);
        }
    }
}
