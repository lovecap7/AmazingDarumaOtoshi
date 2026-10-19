using System.Collections.Generic;
using UnityEngine;

namespace Nakagawa
{
    // 試合のカメラ(MatchCamera)に、プレイヤーと同じように画面に収める対象として扱わせる目印
    // 復活の出現演出など、まだプレイヤーの体が無いが映しておきたい物に付ける(有効な間だけ数える)
    public class CameraFocusTarget : MonoBehaviour
    {
        static readonly List<CameraFocusTarget> s_all = new List<CameraFocusTarget>();

        // 有効な目印すべて
        public static IReadOnlyList<CameraFocusTarget> All => s_all;

        // ドメインの再読み込みをしない設定でも、再生のたびに空にする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_all.Clear();

        private void OnEnable() => s_all.Add(this);
        private void OnDisable() => s_all.Remove(this);
    }
}
