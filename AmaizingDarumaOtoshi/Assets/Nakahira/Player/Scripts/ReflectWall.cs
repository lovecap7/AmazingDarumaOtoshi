using UnityEngine;

namespace Nakahira
{
    // 飛んでいる積み木を壁と同じように反射させる物の目印(当たり判定を持つ物か、その親のコンポーネントに実装する)
    // 扇風機などのギミックも、これを実装すれば積み木が壁と同じように跳ね返る
    public interface ITumikiReflector
    {
    }

    // 飛んでいる積み木を反射させる壁の目印
    public class ReflectWall : MonoBehaviour, ITumikiReflector
    {
    }
}
