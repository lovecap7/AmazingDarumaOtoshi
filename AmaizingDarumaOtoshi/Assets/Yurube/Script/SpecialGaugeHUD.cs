using Nakahira;

namespace Yurube
{
    // 必殺技ゲージの表示の入口。
    // 2026-11-12(中平): 表示は Canvas の Nakahira.SpecialGaugeHud(プレハブ)に移した。
    // 必殺技の OnGUI から今までどおり Draw を呼ぶと、状態の文字(「POWER UP 3.2s」など)をゲージに渡す。描くのは SpecialGaugeHud。
    public static class SpecialGaugeHUD
    {
        // ストーリー用(Yurube): 会話中は描かない。SpecialGaugeHud が読む
        public static bool Suppressed;

        public static void Draw(SpecialGauge gauge, DarumaCharacter character, string statusOverride = null)
        {
            if (gauge == null) return;
            gauge.SetStatusOverride(statusOverride);
        }
    }
}
