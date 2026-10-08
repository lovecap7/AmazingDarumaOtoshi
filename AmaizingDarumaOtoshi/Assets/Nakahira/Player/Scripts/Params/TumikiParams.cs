using UnityEngine;

namespace Nakahira
{
    // 積み木共通の性能
    // 値は使うたびに直接読むので、再生中に変更しても即座に反映される
    [CreateAssetMenu(fileName = "TumikiParams", menuName = "Nakahira/Tumiki Params")]
    public class TumikiParams : ScriptableObject
    {
        [Tooltip("飛んでいるときの減速度(1秒あたりに落ちる速さ)。弾速10・減速度5なら約10m進んで止まる")]
        [Min(0.0f)] public float Deceleration = 5.0f;
        [Tooltip("これ未満の速さになったら弾ではなくなる(無害になり、拾える)")]
        [Min(0.0f)] public float HarmlessSpeed = 3.0f;

        [Header("衝突の手ごたえ(ヒットストップ)。速い積み木ほど長く強く止まる")]
        [Tooltip("この速さ以下で当たったときに最小の値になる")]
        [Min(0.0f)] public float ImpactMinSpeed = 5.0f;
        [Tooltip("この速さ以上で当たったときに最大の値になる")]
        [Min(0.0f)] public float ImpactMaxSpeed = 30.0f;
        [Tooltip("スローになる時間(秒) 最小/最大")]
        [Min(0.0f)] public float ImpactMinDuration = 0.04f;
        [Min(0.0f)] public float ImpactMaxDuration = 0.12f;
        [Tooltip("震える幅(m) 最小/最大。見た目だけで当たり判定は動かない")]
        [Min(0.0f)] public float ImpactMinShake = 0.04f;
        [Min(0.0f)] public float ImpactMaxShake = 0.12f;
        [Tooltip("スロー中の時間の倍率")]
        [Range(0.0f, 1.0f)] public float ImpactTimeScale = 0.1f;

        // 当たったときの速さに応じたヒットストップの長さと震え
        public void GetImpactHitStop(float speed, out float duration, out float shake)
        {
            float t = Mathf.InverseLerp(ImpactMinSpeed, ImpactMaxSpeed, speed);
            duration = Mathf.Lerp(ImpactMinDuration, ImpactMaxDuration, t);
            shake = Mathf.Lerp(ImpactMinShake, ImpactMaxShake, t);
        }
    }
}
