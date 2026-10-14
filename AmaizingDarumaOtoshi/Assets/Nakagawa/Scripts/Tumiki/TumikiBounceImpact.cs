using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 非破壊: ぶつかった積み木は崩れず、相手を弾き飛ばして自分は跳ね返る
    // 相手はビリヤードのように中心同士を結ぶ方向へ、当てた積み木の速さと当たった角度で決まる速さで飛ぶ
    // (正面から当てるほど速く、かすめるほど遅い。相手は速ければ攻撃判定を持ち、持ち主を引き継ぐので連鎖は続く)
    [CreateAssetMenu(fileName = "TumikiImpact_Bounce", menuName = "Nakagawa/Tumiki Impact/Bounce")]
    public class TumikiBounceImpact : TumikiImpactAsset
    {
        [Header("当てられた積み木")]
        [Tooltip("正面から当てたときに、当てられた積み木が飛ぶ速さ(当てた積み木の速さに対する割合)")]
        [SerializeField, Min(0.0f)] float m_targetSpeedRate = 1.0f;
        [Tooltip("当たった角度による減り方。1で自然(正面方向の成分だけ伝わる)、大きいほど斜めに当てたときに遅くなる")]
        [SerializeField, Min(0.0f)] float m_angleFalloff = 1.0f;

        [Header("当てた積み木")]
        [Tooltip("正面から当てたときに跳ね返る速さ(当たる直前の、相手に向かう速さに対する割合)。斜めなら横へそれる")]
        [SerializeField, Range(0.0f, 1.0f)] float m_selfRebound = 0.6f;
        [Tooltip("跳ね返った後に最低限残す速さ(当たる直前の自分の速さに対する割合)。正面から当てても、この割合の速さは引き継いで跳ね返る")]
        [SerializeField, Range(0.0f, 1.0f)] float m_selfSpeedKeep = 0.75f;

        [Tooltip("当たったときにヒットストップを入れる")]
        [SerializeField] bool m_hitStop = true;

        public override void OnHitBlock(TumikiBlock self, TumikiBlock other)
        {
            float approach = Approach(self, other, out Vector3 n, out Vector3 v1, out Vector3 v2);
            if (approach <= 0.0f) return;

            // 当たった角度(相対速度と中心同士を結ぶ方向のなす角)の cos
            float relative = (v1 - v2).magnitude;
            float cos = relative > 0.0001f ? Mathf.Clamp01(approach / relative) : 1.0f;
            float transfer = m_targetSpeedRate * relative * Mathf.Pow(cos, m_angleFalloff);

            Vector3 v2After = v2 + n * transfer;
            Vector3 v1After = v1 - n * (approach * (1.0f + m_selfRebound));
            // 当てた勢いをある程度引き継ぐ: 跳ね返る向きはそのままに、元の速さの一定割合までは速さを残す
            float keep = v1.magnitude * m_selfSpeedKeep;
            float after = v1After.magnitude;
            if (after > 0.0001f && after < keep) v1After *= keep / after;

            if (m_hitStop)
            {
                self.GetImpactHitStop(out float duration, out float timeScale, out float shake);
                self.StartHitStop(duration, timeScale, shake);
                other.StartHitStop(duration, timeScale, shake);
            }
            other.Push(v2After, v2After.magnitude, self.Owner, self.ReturnCount);
            self.SetFlyingVelocity(v1After);
        }

        // self が other に向かって近づく速さ(0以下なら離れている)
        // n: self から other への向き / v1, v2: 水平速度(落ちている積み木は止まっていたとみなす。衝突の通知時点では物理が既に少し押しているため)
        public static float Approach(TumikiBlock self, TumikiBlock other, out Vector3 n, out Vector3 v1, out Vector3 v2)
        {
            v1 = self.HorizontalVelocity;
            v2 = other.CurrentState == TumikiBlock.State.Flying ? other.HorizontalVelocity : Vector3.zero;
            n = other.transform.position - self.transform.position;
            n.y = 0.0f;
            if (n.sqrMagnitude < 0.0001f) return 0.0f;
            n.Normalize();
            return Vector3.Dot(v1 - v2, n);
        }
    }
}
