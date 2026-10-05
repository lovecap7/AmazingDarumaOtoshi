using UnityEngine;

namespace Nakahira
{
    // 攻撃する側(生存キャラクター・幽霊)。弾の持ち主として手柄の通知先になる
    public interface IDarumaAttacker
    {
        Transform transform { get; }
        // 発射直後に自分の弾と当たらないようにするための当たり判定(無ければnull)
        Collider Body { get; }
        // 自分の攻撃(弾・連鎖・ハンマー)で生存者にダメージを与えた
        void OnDealtDamage();
    }

    public static class DarumaAttackerUtil
    {
        // 破棄済みのMonoBehaviourもnull扱いにする
        public static bool IsAlive(IDarumaAttacker attacker)
        {
            return attacker is Object obj && obj != null;
        }
    }
}
