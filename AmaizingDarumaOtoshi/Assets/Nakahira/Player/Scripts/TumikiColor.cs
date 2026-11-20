using UnityEngine;

namespace Nakahira
{
    // 積み木の色。キャラクターのパッシブ(特定の色を集めると強化)で使う予定
    public enum TumikiColor { Red, Blue, Green, Yellow }

    public static class TumikiColorUtil
    {
        // 積み木の色で性能を変えない(Fujihara 2026-11-20: 色の性能を撤廃。積み木は木の積み木になり、見た目は積んだ人・打った人の色)
        // true のとき、DarumaStack.CountColor はどの色を聞かれても積んでいる数をそのまま返し、
        // 色を見ていたパッシブ(あかだる・あおだる・きだる・みだる)は、どの積み木でも働く。false に戻すと元の色ごとの判定
        public static bool IgnoreColors = true;

        public static Color ToUnityColor(TumikiColor color)
        {
            switch (color)
            {
                case TumikiColor.Red: return new Color(0.9f, 0.2f, 0.2f);
                case TumikiColor.Blue: return new Color(0.2f, 0.4f, 0.95f);
                case TumikiColor.Green: return new Color(0.25f, 0.8f, 0.3f);
                case TumikiColor.Yellow: return new Color(0.95f, 0.85f, 0.2f);
                default: return Color.white;
            }
        }
    }
}
