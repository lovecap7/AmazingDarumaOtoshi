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
    }
}
