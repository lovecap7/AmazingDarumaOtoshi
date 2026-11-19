using UnityEngine;

namespace Nakahira
{
    // 生存キャラクターの体の動き(見た目だけ。当たり判定・移動は変えない)の調整値(全キャラ共通。JumpParams.Motion に入れる)
    // 積み上げているのは硬い積み木なので、伸び縮み(大きさの変化)も傾きも使わず、段ごとの平行なずれだけで動かす
    // (横のずれは上の段ほど大きく、だるま落としのような階段の形になる。「角度」の値は、足元と一番上を結んだ線の傾き)
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

        [Header("ジャンプ(上の段ほど浮いて隙間が開く)")]
        [Tooltip("跳んだ瞬間に、段と段の間に空く隙間(m。1 段ごと)")]
        [Min(0.0f)] public float JumpGap = 0.05f;
        [Tooltip("隙間を全部足した量の上限(m)。段が多いときに頭が浮きすぎないように")]
        [Min(0.0f)] public float MaxJumpSpread = 0.35f;
        [Tooltip("跳んでから隙間が開ききるまでの時間(秒)")]
        [Min(0.01f)] public float GapOpenTime = 0.06f;
        [Tooltip("落ち始めてから隙間が詰まるまでの時間(秒)")]
        [Min(0.01f)] public float GapCloseTime = 0.3f;

        [Header("着地(上の段から順にトトトッと詰まる)")]
        [Tooltip("着地で 1 段ずつ跳ねる高さ(m)")]
        [Min(0.0f)] public float LandBounce = 0.03f;
        [Tooltip("上の段から次の段へ移るまでの間(秒)")]
        [Min(0.0f)] public float LandInterval = 0.03f;
        [Tooltip("1 段が跳ねている時間(秒)")]
        [Min(0.01f)] public float LandDuration = 0.08f;
        [Tooltip("これより短い空中(秒)からの着地では詰まる動きを出さない")]
        [Min(0.0f)] public float LandMinAirTime = 0.15f;

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
