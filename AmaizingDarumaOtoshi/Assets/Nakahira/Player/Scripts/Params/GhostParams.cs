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
    }
}
