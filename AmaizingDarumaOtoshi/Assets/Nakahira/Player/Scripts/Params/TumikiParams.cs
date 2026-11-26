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
        [Tooltip("これ未満の速さになったら攻撃判定がなくなる(連鎖・ダメージなし。壁での反射や減速は続く)")]
        [Min(0.0f)] public float HarmlessSpeed = 3.0f;
        [Tooltip("これ未満の速さになったら止まって落ちている積み木に戻る")]
        [Min(0.0f)] public float StopSpeed = 0.5f;

        [Tooltip("飛んでいる積み木が床から落ちている間の重力の倍率(1で通常)。大きいほど素早く落ちる")]
        [Min(1.0f)] public float FallGravityScale = 2.0f;

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

        [Header("攻撃力(ハンマーで打ち返すたびに 1 上がる。当たった相手の段がその数だけ飛ぶ)")]
        [Tooltip("2 段以上飛ぶとき、飛ぶ向きを左右に散らす角度(度。当たった向きから左右それぞれ)")]
        [Range(0.0f, 90.0f)] public float MultiKnockSpread = 35.0f;
        [Tooltip("炎の色が青になりきる打ち返し回数(1 回目が赤、この回数で青)")]
        [Min(1)] public int FlameBlueReturns = 5;
        [Tooltip("1 回打ち返したときの炎の色(粒が出てから消えるまでの色)")]
        public Gradient FlameRed = MakeFlame(new Color(1.0f, 0.85f, 0.6f), new Color(1.0f, 0.15f, 0.05f), new Color(0.6f, 0.0f, 0.0f));
        [Tooltip("FlameBlueReturns 回打ち返したときの炎の色。その間は赤と青を混ぜる")]
        public Gradient FlameBlue = MakeFlame(new Color(0.75f, 0.9f, 1.0f), new Color(0.1f, 0.35f, 1.0f), new Color(0.0f, 0.05f, 0.6f));

        private static Gradient MakeFlame(Color start, Color middle, Color end)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(start, 0.0f), new GradientColorKey(middle, 0.5f), new GradientColorKey(end, 1.0f) },
                new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0.0f, 1.0f) });
            return g;
        }

        // 打ち返し回数(攻撃力 - 1)に合わせた炎の色。0 回なら null(元の炎のまま)
        public Gradient FlameFor(int returns, Gradient buffer)
        {
            if (returns <= 0) return null;
            float t = FlameBlueReturns <= 1 ? 1.0f : Mathf.Clamp01((returns - 1) / (float)(FlameBlueReturns - 1));
            var colors = new GradientColorKey[3];
            var alphas = new GradientAlphaKey[3];
            for (int i = 0; i < 3; i++)
            {
                float time = i * 0.5f;
                colors[i] = new GradientColorKey(Color.Lerp(FlameRed.Evaluate(time), FlameBlue.Evaluate(time), t), time);
                alphas[i] = new GradientAlphaKey(Mathf.Lerp(FlameRed.Evaluate(time).a, FlameBlue.Evaluate(time).a, t), time);
            }
            buffer.SetKeys(colors, alphas);
            return buffer;
        }

        // 当たったときの速さに応じたヒットストップの長さと震え
        public void GetImpactHitStop(float speed, out float duration, out float shake)
        {
            float t = Mathf.InverseLerp(ImpactMinSpeed, ImpactMaxSpeed, speed);
            duration = Mathf.Lerp(ImpactMinDuration, ImpactMaxDuration, t);
            shake = Mathf.Lerp(ImpactMinShake, ImpactMaxShake, t);
        }
    }
}
