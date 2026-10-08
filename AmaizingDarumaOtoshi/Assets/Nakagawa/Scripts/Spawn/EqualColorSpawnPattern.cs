using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 各色均等: 全色を同じ数ずつ生成し、割り切れない余りはランダムな色にする
    // (9個なら赤青黄緑を2個ずつ + ランダムな1色。余りが複数なら、余り同士は別の色にする)
    [CreateAssetMenu(fileName = "EqualColorSpawnPattern", menuName = "Nakagawa/Tumiki Spawn Pattern/Equal Color")]
    public class EqualColorSpawnPattern : TumikiSpawnPattern
    {
        public override List<TumikiColor> CreateColors(int count)
        {
            var colors = new List<TumikiColor>(count);
            int each = count / kAllColors.Length;
            foreach (var color in kAllColors)
            {
                for (int i = 0; i < each; i++) colors.Add(color);
            }

            // 余りは色が偏らないよう、重複しないランダムな色にする
            var rest = new List<TumikiColor>(kAllColors);
            Shuffle(rest);
            int remainder = count - colors.Count;
            for (int i = 0; i < remainder; i++) colors.Add(rest[i]);

            Shuffle(colors);
            return colors;
        }
    }
}
