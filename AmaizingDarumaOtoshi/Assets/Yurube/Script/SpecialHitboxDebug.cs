using UnityEngine;

namespace Yurube
{
    // キャラクターラボの「当たり判定を表示」を全必殺技で共有するフラグ。
    public static class SpecialHitboxDebug
    {
        public static bool Visible { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetVisible()
        {
            Visible = false;
        }
    }
}
