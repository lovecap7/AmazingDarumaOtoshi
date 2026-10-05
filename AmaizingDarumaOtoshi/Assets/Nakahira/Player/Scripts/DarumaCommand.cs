using UnityEngine;

namespace Nakahira
{
    // プレイヤーへの操作命令。入力デバイスやネットワークから作られ、DarumaPlayerに渡される
    public struct DarumaCommand
    {
        // ワールド空間の移動方向。x→ワールドX、y→ワールドZ。大きさ0〜1
        // (カメラ基準への変換は入力側で行う。AIやネットワークはカメラを気にせずそのまま使える)
        public Vector2 Move;
        public bool Jump;     // このフレームでジャンプが押されたか
        public bool Sweep;    // このフレームで薙ぎ払いが押されたか
        public bool Shot;     // このフレームで股抜きショットが押されたか
    }

    // 操作命令の供給元。ゲームパッド入力・敵AI・ネットワーク同期などが実装する
    public interface IDarumaCommandSource
    {
        DarumaCommand ReadCommand();
    }
}
