using UnityEngine;

namespace Nakagawa
{
    // ギミック全体でそろえる色の意味
    //  オレンジ = 加速 / 青緑 = 減速 / 黄色の点滅 = 予兆
    // 各ギミックはこの色を既定値にした色をインスペクターに持つ(ステージに合わせて個別に変えてもよい)
    public static class GimmickPalette
    {
        public static readonly Color Accelerate = new Color(1.0f, 0.55f, 0.1f, 1.0f);
        public static readonly Color Decelerate = new Color(0.1f, 0.85f, 0.8f, 1.0f);
        public static readonly Color Warning = new Color(1.0f, 0.9f, 0.15f, 1.0f);

        // 予兆の点滅の強さ(0〜1)。rate: 1秒あたりの点滅回数
        public static float Blink(float time, float rate)
        {
            return 0.5f + 0.5f * Mathf.Cos(time * rate * Mathf.PI * 2.0f);
        }

        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
