using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 完全ランダム: 1個ずつ色を抽選する(9個なら9回抽選)
    [CreateAssetMenu(fileName = "RandomColorSpawnPattern", menuName = "Nakagawa/Tumiki Spawn Pattern/Random Color")]
    public class RandomColorSpawnPattern : TumikiSpawnPattern
    {
        public override List<TumikiColor> CreateColors(int count)
        {
            var colors = new List<TumikiColor>(count);
            for (int i = 0; i < count; i++) colors.Add(RandomColor());
            return colors;
        }
    }
}
