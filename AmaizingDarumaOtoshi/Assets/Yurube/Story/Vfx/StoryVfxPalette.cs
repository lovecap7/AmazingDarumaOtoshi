using Nakahira;
using UnityEngine;

namespace Yurube.Story.Vfx
{
    // ストーリー演出エフェクトで共通参照するカラーパレット。
    // キャラ色（Assets/Fujihara/Data/Characters/*.asset）と積み木色（TumikiColorUtil / ToyKit.Palette）に揃える。
    public static class StoryVfxPalette
    {
        // ---- キャラクター7体のイメージカラー ----
        public static readonly Color Akadaru  = new Color(0.91f, 0.29f, 0.25f, 1f); // あかだる
        public static readonly Color Aodaru   = new Color(0.20f, 0.40f, 0.95f, 1f); // あおだる
        public static readonly Color Midaru   = new Color(0.25f, 0.80f, 0.30f, 1f); // みだる
        public static readonly Color Kidaru   = new Color(0.95f, 0.85f, 0.20f, 1f); // きだる
        public static readonly Color Iwadaru  = new Color(0.50f, 0.47f, 0.42f, 1f); // いわだる
        public static readonly Color Hebidaru = new Color(0.42f, 0.75f, 0.27f, 1f); // へびだる
        public static readonly Color DaruKing = new Color(0.55f, 0.35f, 0.70f, 1f); // だるきんぐ

        // ---- 演出用アクセントカラー ----
        public static readonly Color GoldBright  = new Color(1.00f, 0.88f, 0.36f, 1f);
        public static readonly Color GoldDeep    = new Color(0.84f, 0.62f, 0.16f, 1f);
        public static readonly Color GoldWhite   = new Color(1.00f, 0.97f, 0.84f, 1f);
        public static readonly Color StoneGray   = new Color(0.62f, 0.59f, 0.55f, 1f);
        public static readonly Color DustWarm    = new Color(0.76f, 0.71f, 0.64f, 0.65f);
        public static readonly Color DarkSmoke   = new Color(0.16f, 0.14f, 0.19f, 0.78f);
        public static readonly Color DarkAuraA   = new Color(0.14f, 0.08f, 0.24f, 0.85f); // 黒紫（抽象的・流血連想なし）
        public static readonly Color DarkAuraB   = new Color(0.28f, 0.16f, 0.44f, 0.75f);
        public static readonly Color DarkMote    = new Color(0.42f, 0.28f, 0.62f, 0.80f);

        // ---- 8年前（プロローグ等）用の彩度を抑えたティント ----
        public static readonly Color PastMutedTint = new Color(0.76f, 0.73f, 0.68f, 1f);
        public static readonly Color DarkMoodTint  = new Color(0.58f, 0.54f, 0.66f, 1f);

        // ---- 積み木4色（TumikiColorUtil と一致） ----
        public static Color[] TumikiColors => new[]
        {
            TumikiColorUtil.ToUnityColor(TumikiColor.Red),
            TumikiColorUtil.ToUnityColor(TumikiColor.Blue),
            TumikiColorUtil.ToUnityColor(TumikiColor.Green),
            TumikiColorUtil.ToUnityColor(TumikiColor.Yellow),
        };

        // ---- 七色セット（紙吹雪・螺旋欠片用：ToyKit.Palette + キャラカラー） ----
        public static readonly Color[] SevenColors =
        {
            new Color(0.91f, 0.29f, 0.25f, 1f), // 赤（あかだる）
            new Color(0.96f, 0.60f, 0.18f, 1f), // 橙
            new Color(0.95f, 0.85f, 0.20f, 1f), // 黄（きだる）
            new Color(0.25f, 0.80f, 0.30f, 1f), // 緑（みだる）
            new Color(0.25f, 0.75f, 0.88f, 1f), // 水色
            new Color(0.20f, 0.40f, 0.95f, 1f), // 青（あおだる）
            new Color(0.55f, 0.35f, 0.70f, 1f), // 紫（だるきんぐ）
        };

        // ランダムカラー用グラデーションを作る（ParticleSystem の startColor 用）
        public static Gradient BuildFixedGradient(Color[] colors)
        {
            var g = new Gradient();
            g.mode = GradientMode.Fixed;
            if (colors == null || colors.Length == 0)
            {
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                return g;
            }

            int n = Mathf.Min(colors.Length, 8);
            var keys = new GradientColorKey[n];
            for (int i = 0; i < n; i++)
            {
                float t = (i + 1f) / n;
                keys[i] = new GradientColorKey(colors[i], t);
            }
            g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
