using UnityEngine;

namespace Nakahira
{
    // CPU(コンピュータープレイヤー)の考え方・腕前。強さの違いはこのアセットを分けて作る
    // 値は使うたびに直接読むので、再生中に変更しても即座に反映される
    [CreateAssetMenu(fileName = "CpuParams", menuName = "Nakahira/Cpu Params")]
    public class CpuParams : ScriptableObject
    {
        [Header("考える")]
        [Tooltip("狙う積み木を選び直す間隔(秒)。短いほど状況の変化にすぐ反応する")]
        [Min(0.02f)] public float ThinkInterval = 0.2f;

        [Header("積む")]
        [Tooltip("この段数になるまで積み木を集める")]
        [Range(1, 10)] public int CollectUntil = 10;
        [Tooltip("この距離(m)より遠い積み木は探さない")]
        [Min(1.0f)] public float SearchRadius = 25.0f;
        [Tooltip("狙っている積み木を別の積み木に変えるのは、こちらの方がこの距離(m)ぶん以上近いときだけ(迷わないように)")]
        [Min(0.0f)] public float SwitchMargin = 1.5f;
        [Tooltip("同じ積み木をこの時間(秒)追っても拾えなければ、しばらく諦める")]
        [Min(0.5f)] public float GiveUpTime = 4.0f;
        [Tooltip("諦めた積み木を狙わない時間(秒)")]
        [Min(0.0f)] public float GiveUpCooldown = 3.0f;

        [Header("積む:狙う積み木の選び方(距離に足すコスト m)")]
        [Tooltip("相手がこの距離(m)以内にいる積み木は避ける")]
        [Min(0.0f)] public float OpponentAvoidRadius = 3.0f;
        [Tooltip("相手の近くにある積み木のコスト(相手に近いほど大きくなる)")]
        [Min(0.0f)] public float OpponentPenalty = 6.0f;
        [Tooltip("途中に壁がある積み木のコスト")]
        [Min(0.0f)] public float WallPenalty = 8.0f;

        [Header("積む:ジャンプ")]
        [Tooltip("ジャンプする距離の誤差(割合)。大きいほど跳ぶのが早すぎたり遅すぎたりする")]
        [Range(0.0f, 1.0f)] public float JumpDistanceError = 0.1f;
        [Tooltip("着地を狙う位置の誤差(m)")]
        [Min(0.0f)] public float AimError = 0.1f;

        [Header("足場")]
        [Tooltip("進む先の足元に地面があるか確かめる距離(m)。移動速度×0.1秒ぶんを足して使う")]
        [Min(0.0f)] public float EdgeLookAhead = 0.6f;
        [Tooltip("足元からこの深さ(m)までに地面がなければ奈落とみなす")]
        [Min(0.1f)] public float GroundProbeDepth = 2.0f;
        [Tooltip("積み木までの道に奈落がないか確かめる間隔(m)")]
        [Min(0.1f)] public float PathSampleStep = 0.5f;
    }
}
