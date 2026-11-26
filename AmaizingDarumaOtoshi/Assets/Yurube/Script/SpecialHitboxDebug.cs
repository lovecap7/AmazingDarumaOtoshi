using UnityEngine;

namespace Yurube
{
    // キャラクターラボの「当たり判定を表示」を全必殺技で共有するフラグ。
    public static class SpecialHitboxDebug
    {
        static int s_lastToggleFrame = -1;

        public static bool Visible { get; set; }

        // 1フレームに複数箇所から呼ばれても1回だけ切り替える
        public static void Toggle()
        {
            if (s_lastToggleFrame == Time.frameCount) return;
            s_lastToggleFrame = Time.frameCount;
            Visible = !Visible;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetVisible()
        {
            Visible = false;
            s_lastToggleFrame = -1;
        }
    }
}
