using UnityEngine;

namespace Yurube
{
    // トロンの残像ビームの当たり判定。残像の BoxCollider(トリガー) から呼ばれる。
    public class TronTrailHit : MonoBehaviour
    {
        public TronProjectile projectile;

        private void OnTriggerEnter(Collider other)
        {
            if (projectile != null) projectile.OnTrailHit(other);
        }

        // ビームが伸びて相手を覆った場合も拾えるようにする
        private void OnTriggerStay(Collider other)
        {
            if (projectile != null) projectile.OnTrailHit(other);
        }
    }
}
