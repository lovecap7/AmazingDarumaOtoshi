using UnityEngine;

namespace Nakahira
{
    // ハンマーの性能。キャラクターごと(幽霊も含む)にアセットを用意して差をつける
    // 値は使うたびに直接読むので、再生中に変更しても即座に反映される
    [CreateAssetMenu(fileName = "HammerParams", menuName = "Nakahira/Hammer Params")]
    public class HammerParams : ScriptableObject
    {
        [Header("振る速さ")]
        [Tooltip("薙ぎ払いにかかる時間(秒)")]
        [Min(0.01f)] public float SweepDuration = 0.25f;
        [Tooltip("股抜きショットにかかる時間(秒)")]
        [Min(0.01f)] public float ShotDuration = 0.3f;

        [Header("当たり判定")]
        [Tooltip("ハンマーが届く距離の倍率(1で見た目のハンマーの長さ)")]
        [Min(0.1f)] public float HitReachScale = 1.5f;

        [Header("打ち返し")]
        [Tooltip("打ち返した積み木が自分に当たらない時間(秒)")]
        [Min(0.0f)] public float ReturnImmunityTime = 0.5f;
        [Tooltip("打ち返すたびにヒットストップの時間に掛ける倍率")]
        [Min(1.0f)] public float ReturnHitStopGrowth = 1.6f;
        [Tooltip("打ち返しのヒットストップの上限(秒)")]
        [Min(0.0f)] public float ReturnHitStopMax = 2.0f;

        [Header("弾速")]
        [Tooltip("股抜きショットで打ち出す弾速")]
        public float ShotSpeed = 10.0f;
        [Tooltip("薙ぎ払いで落ちている積み木・相手の最下段を弾いたときの弾速(打ち返しの基本弾速でもある)")]
        public float SweepLaunchSpeed = 10.0f;
        [Tooltip("打ち返しの弾速 = 基本弾速 × 倍率^打ち返し回数")]
        public float ReturnSpeedRate = 1.25f;
        [Tooltip("打ち返しの弾速の上限")]
        public float MaxProjectileSpeed = 30.0f;

        [Header("手ごたえ(ヒットストップ)")]
        [Tooltip("積み木を弾いた・相手を叩いたときにスローになる時間(秒)")]
        [Min(0.0f)] public float HitStopDuration = 0.1f;
        [Tooltip("スロー中の時間の倍率(0.1なら0.1倍速)")]
        [Range(0.0f, 1.0f)] public float HitStopTimeScale = 0.1f;
        [Tooltip("スロー中に自分が震える幅(m)。見た目だけで当たり判定は動かない")]
        [Min(0.0f)] public float SelfShake = 0.04f;
        [Tooltip("スロー中に弾いた積み木・叩いた相手が震える幅(m)")]
        [Min(0.0f)] public float TargetShake = 0.1f;
    }
}
