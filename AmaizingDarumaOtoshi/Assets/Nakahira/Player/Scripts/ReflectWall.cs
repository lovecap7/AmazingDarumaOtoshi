using UnityEngine;

namespace Nakahira
{
    // 飛んでいる積み木を壁と同じように反射させる物の目印(当たり判定を持つ物か、その親のコンポーネントに実装する)
    // 扇風機などのギミックも、これを実装すれば積み木が壁と同じように跳ね返る
    public interface ITumikiReflector
    {
    }

    // 飛んでいる積み木が当たったとき、反射させるかを当たった物の側で決める(当たり判定を持つ物か、その親のコンポーネントに実装する)
    // ITumikiReflector や「動かない物の側面」の判定より優先する。Rigidbody を持つ物(移動床など)を反射させたいときや、
    // 上に乗れる壁(せり上がり塀など)で上面の継ぎ目に触れただけのときは反射させない、といった判定に使う
    //  contact: 当たった点(collision.GetContact(0))
    public interface ITumikiReflectFilter
    {
        bool ShouldReflect(TumikiBlock block, Collider collider, ContactPoint contact);
    }

    // 飛んでいる積み木を反射させる壁の目印
    public class ReflectWall : MonoBehaviour, ITumikiReflector
    {
    }
}
