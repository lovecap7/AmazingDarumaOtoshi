using UnityEngine;

namespace Nakahira
{
    // 生存キャラクターのジャンプの挙動(全キャラ共通)
    // 高さと頂点までの時間から、キャラクター専用の重力と初速を計算する
    // 値は使うたびに直接読むので、再生中に変更しても即座に反映される
    [CreateAssetMenu(fileName = "JumpParams", menuName = "Nakahira/Jump Params")]
    public class JumpParams : ScriptableObject
    {
        [Tooltip("ジャンプの高さ(m)")]
        [Min(0.01f)] public float Height = 1.8f;
        [Tooltip("頂点に達するまでの時間(秒)。長いほどふわっとする")]
        [Min(0.01f)] public float TimeToApex = 0.6f;
        [Tooltip("落下中の重力倍率。大きいほどストンと落ちる")]
        [Min(0.01f)] public float FallGravityMultiplier = 1.0f;

        // 上昇中の重力の強さ
        public float Gravity => 2.0f * Height / (TimeToApex * TimeToApex);
        // ジャンプの初速
        public float JumpSpeed => 2.0f * Height / TimeToApex;
    }
}
