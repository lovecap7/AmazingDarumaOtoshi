using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 非破壊: ぶつかった積み木は崩れず、相手と弾き合って自然に跳ね返る
    // 中心同士を結ぶ方向の反発係数つきの衝突として計算する。相手は速ければ攻撃判定を持ち、持ち主を引き継ぐ(連鎖は続く)
    [CreateAssetMenu(fileName = "TumikiImpact_Bounce", menuName = "Nakagawa/Tumiki Impact/Bounce")]
    public class TumikiBounceImpact : TumikiImpactAsset
    {
        [Tooltip("反発係数(1で弾性衝突、0で相手にくっつくように止まる)")]
        [SerializeField, Range(0.0f, 1.0f)] float m_restitution = 0.9f;
        [Tooltip("ぶつかられた積み木の重さ(ぶつかった積み木を1とする)。1より重いほど、ぶつかった方が大きく跳ね返る")]
        [SerializeField, Min(0.1f)] float m_otherMassRatio = 2.0f;
        [Tooltip("当たったときにヒットストップを入れる")]
        [SerializeField] bool m_hitStop = true;

        public override void OnHitBlock(TumikiBlock self, TumikiBlock other)
        {
            Vector3 n = other.transform.position - self.transform.position;
            n.y = 0.0f;
            if (n.sqrMagnitude < 0.0001f) return;
            n.Normalize();

            Vector3 v1 = self.HorizontalVelocity;
            // 落ちている積み木は止まっていたとみなす(衝突の通知時点では物理が既に少し押してしまっているため)
            Vector3 v2 = other.CurrentState == TumikiBlock.State.Flying ? other.HorizontalVelocity : Vector3.zero;
            // 近づいているときだけ(両方の積み木から通知が来ても1回だけ弾き合う)
            float approach = Vector3.Dot(v1 - v2, n);
            if (approach <= 0.0f) return;

            const float kSelfMass = 1.0f;
            float impulse = (1.0f + m_restitution) * approach / (kSelfMass + m_otherMassRatio);
            Vector3 v1After = v1 - n * (impulse * m_otherMassRatio);
            Vector3 v2After = v2 + n * (impulse * kSelfMass);

            if (m_hitStop)
            {
                self.GetImpactHitStop(out float duration, out float timeScale, out float shake);
                self.StartHitStop(duration, timeScale, shake);
                other.StartHitStop(duration, timeScale, shake);
            }
            other.Push(v2After, v2After.magnitude, self.Owner, self.ReturnCount);
            self.SetFlyingVelocity(v1After);
        }
    }
}
