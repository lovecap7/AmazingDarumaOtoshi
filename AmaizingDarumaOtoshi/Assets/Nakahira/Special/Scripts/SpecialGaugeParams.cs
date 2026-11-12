using UnityEngine;

namespace Nakahira
{
    // 必殺技ゲージの調整値(キャラクターごと)。キャラのプレハブの SpecialGauge に割り当てる
    // 共通は SpecialGauge_Default。変えたいキャラだけ別のアセットを作って割り当てる
    // 使うたびに読むので、再生中に変えても反映される
    [CreateAssetMenu(fileName = "SpecialGauge_", menuName = "Nakahira/Special Gauge Params")]
    public class SpecialGaugeParams : ScriptableObject
    {
        [Header("最大")]
        [Tooltip("満タンのポイント。表示は最大に対する割合(0〜100%)")]
        [Min(1.0f)] public float MaxPoints = 100.0f;

        [Header("行動で得る量(1 回ごと。多い順)")]
        [Tooltip("攻撃判定のある速い積み木にジャンプして乗った(積む分は足さない)")]
        [Min(0.0f)] public float RideHarmfulBlock = 25.0f;
        [Tooltip("飛んできた積み木をハンマーで打ち返した")]
        [Min(0.0f)] public float ReturnBlock = 20.0f;
        [Tooltip("相手をハンマーで直接殴ってダメージを与えた")]
        [Min(0.0f)] public float DirectHit = 15.0f;
        [Tooltip("積み木を拾って段が増えた(上限で古い段がこぼれる入れ替えでは入らない。パッシブで増えた段も入らない)")]
        [Min(0.0f)] public float StackBlock = 10.0f;

        [Header("続けて得る量(1 秒あたり)")]
        [Tooltip("敵(味方・幽霊は数えない)がこの距離(m)以内にいる間")]
        [Min(0.0f)] public float NearEnemyDistance = 5.0f;
        [Min(0.0f)] public float NearEnemyPerSecond = 3.0f;
        [Tooltip("スティックを倒して、この速さ(m/秒)以上で動いている間")]
        [Min(0.0f)] public float MoveMinSpeed = 1.0f;
        [Min(0.0f)] public float MovePerSecond = 1.5f;

        [Header("やられたとき(逆転要素)")]
        [Tooltip("やられたら溜まり具合を次の体に引き継ぎ、最大のこの割合を足す")]
        [Range(0.0f, 1.0f)] public float DeathBonusRatio = 0.15f;

        [Header("表示")]
        [Tooltip("試合中のゲージの表示(Canvas のプレハブ)。試合で最初にゲージを持つキャラが出たときに 1 つだけ作る。空なら出さない")]
        public SpecialGaugeHud HudPrefab;
    }
}
