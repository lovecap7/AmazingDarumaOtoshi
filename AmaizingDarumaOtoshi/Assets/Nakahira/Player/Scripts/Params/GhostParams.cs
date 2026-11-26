using UnityEngine;

namespace Nakahira
{
    // 幽霊の性能(ハンマーは HammerParams で別に設定する)
    // 値は使うたびに直接読むので、再生中に変更しても即座に反映される
    [CreateAssetMenu(fileName = "GhostParams", menuName = "Nakahira/Ghost Params")]
    public class GhostParams : ScriptableObject
    {
        [Tooltip("移動速度")]
        [Min(0.0f)] public float MoveSpeed = 5.0f;

        [Header("浮いている揺れ(見た目だけ)")]
        [Tooltip("上下に揺れる幅(m。真ん中から上・下それぞれ)。0 なら揺れない")]
        [Min(0.0f)] public float FloatAmplitude = 0.08f;
        [Tooltip("上下に 1 往復する時間(秒)")]
        [Min(0.1f)] public float FloatPeriod = 3.0f;
    }
}
