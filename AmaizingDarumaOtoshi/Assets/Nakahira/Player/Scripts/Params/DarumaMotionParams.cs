using UnityEngine;

namespace Nakahira
{
    // 生存キャラクターの体の動き(見た目だけ。当たり判定・移動は変えない)の調整値(全キャラ共通。JumpParams.Motion に入れる)
    // 積み上げているのは硬い積み木なので、伸び縮み(大きさの変化)は使わず、ふだんは段ごとの平行なずれだけで動かす
    // (横のずれは上の段ほど大きく、だるま落としのような階段の形になる。「角度」の値は、足元と一番上を結んだ線の傾き)
    // ジャンプ・着地では、段ごとの上下の動きと傾きを簡単な物理で計算する(傾きの向きはランダム)
    // 値は使うたびに直接読むので、再生中に変更しても即座に反映される
    [CreateAssetMenu(fileName = "DarumaMotionParams", menuName = "Nakahira/Daruma Motion Params")]
    public class DarumaMotionParams : ScriptableObject
    {
        [Header("全体")]
        [Tooltip("どの段も、横にこれ以上はずれない(m)。積み木の半径が 0.5。歩き・傾き・ゆらゆらを全部足した量をこの中に収める")]
        [Min(0.0f)] public float MaxHorizontalOffset = 0.5f;

        [Header("歩く(足踏み)")]
        [Tooltip("1 歩で進む距離(m)。小さいほどせかせか跳ねる")]
        [Min(0.05f)] public float StepLength = 1.4f;
        [Tooltip("1 歩ごとに跳ねる高さ(m)")]
        [Min(0.0f)] public float HopHeight = 0.08f;
        [Tooltip("跳ねたとき、段と段の間に空く隙間(m。1 段ごと)。硬い積み木がカタカタ鳴るように見せる")]
        [Min(0.0f)] public float HopGap = 0.006f;
        [Tooltip("よちよち歩きで左右にずれる量(度。上の段ほど大きく平行にずれる。積み木そのものは傾かない)")]
        [Range(0.0f, 15.0f)] public float WaddleAngle = 2.5f;
        [Tooltip("いちばん速く歩いたとき、進む向きへ前にずれる量(度。上の段ほど大きく平行にずれる。積み木そのものは傾かない)")]
        [Range(0.0f, 15.0f)] public float LeanAngle = 3.0f;
        [Tooltip("前のめりが変わるまでの時間(秒)")]
        [Min(0.01f)] public float LeanSmoothTime = 0.12f;
        [Tooltip("歩きの動きが出る・消えるまでの時間(秒)")]
        [Min(0.01f)] public float WalkBlendTime = 0.08f;

        [Header("ゆらゆら(段が多いほど上が揺れる)")]
        [Tooltip("この高さ(m)のときの揺れ幅を基準にする。10 段 + 頭でおよそ 5.9m")]
        [Min(0.1f)] public float SwayReferenceHeight = 5.0f;
        [Tooltip("高さによる揺れの増え方。大きいほど、低いタワーは揺れず、高いタワーの上だけが揺れる")]
        [Min(1.0f)] public float SwayHeightExponent = 2.0f;
        [Tooltip("揺れの速さ(1 秒に揺れる回数)")]
        [Min(0.1f)] public float SwayFrequency = 1.4f;
        [Tooltip("揺れの止まりやすさ(0 で止まらない・1 で揺れ返さない)")]
        [Range(0.0f, 1.0f)] public float SwayDamping = 0.25f;
        [Tooltip("動き出し・止まる・曲がるときに、上が置いていかれる強さ")]
        [Min(0.0f)] public float SwayInertia = 0.05f;
        [Tooltip("止まっているときの、かすかな揺れの幅(m。基準の高さで)")]
        [Min(0.0f)] public float IdleSway = 0.03f;
        [Tooltip("止まっているときの揺れの周期(秒)")]
        [Min(0.1f)] public float IdleSwayPeriod = 2.6f;

        [Header("ジャンプ・着地(段ごとに物理で計算する。跳んだ・落ちた・押し上げられたも同じ仕組み)")]
        [Tooltip("体の上下の勢いの変化を、積み木がどれだけ受けるか。1 で本物どおり(キャラの跳び方が速すぎるので小さくする)")]
        [Range(0.0f, 1.0f)] public float BodyInertia = 0.12f;
        [Tooltip("宙に浮いた積み木を下へ引き戻す強さ(m/秒²)。大きいほど早く戻る。0 で体といっしょに落ちる")]
        [Min(0.0f)] public float BlockPull = 25.0f;
        [Tooltip("積み木どうしがぶつかったときの硬さ(Hz)。大きいほど硬く、小さいほどぐにゃっと弾む")]
        [Range(5.0f, 80.0f)] public float ContactFrequency = 30.0f;
        [Tooltip("ぶつかったときの跳ね返りにくさ(0 でよく跳ね返る〜1 で跳ね返らない)")]
        [Range(0.0f, 1.0f)] public float ContactDamping = 0.5f;
        [Tooltip("段と段の間の隙間の上限(m)。これより離れない")]
        [Min(0.0f)] public float MaxGap = 0.3f;

        [Header("ジャンプ・着地の傾き(向きはランダム)")]
        [Tooltip("下の段から離れたとき・ぶつかったときに傾く勢い(度/秒。ぶつかる速さ 1m/秒あたり)")]
        [Min(0.0f)] public float TiltKick = 15.0f;
        [Tooltip("この速さ(m/秒)より弱く離れた・ぶつかったときは傾かない")]
        [Min(0.0f)] public float TiltKickMinSpeed = 0.3f;
        [Tooltip("どの段も、これ以上は傾かない(度)")]
        [Range(0.0f, 60.0f)] public float MaxTilt = 15.0f;
        [Tooltip("下の段に乗っているとき、下の段と同じ傾きに戻る速さ(Hz)")]
        [Min(0.1f)] public float TiltFrequency = 6.0f;
        [Tooltip("傾きが戻るときの揺れ返しにくさ(0 で揺れ続ける〜1 で揺れ返さない)")]
        [Range(0.0f, 1.0f)] public float TiltDamping = 0.5f;

        [Header("拾う")]
        [Tooltip("積み木を拾った瞬間、段と段の間に空く隙間(m。1 段ごと。すぐ詰まる)")]
        [Min(0.0f)] public float PickupGap = 0.03f;

        [Header("ハンマーを振る(振った反動で上が揺れる)")]
        [Tooltip("薙ぎ払いの反動(右へ揺れる勢い m/秒。基準の高さで)")]
        [Min(0.0f)] public float SweepKick = 1.2f;
        [Tooltip("股抜きショットの反動(後ろへ揺れる勢い m/秒。基準の高さで)")]
        [Min(0.0f)] public float ShotKick = 0.8f;
    }
}
