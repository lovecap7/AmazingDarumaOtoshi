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

        [Header("弾速")]
        [Tooltip("股抜きショットで打ち出す弾速")]
        public float ShotSpeed = 10.0f;
        [Tooltip("薙ぎ払いで落ちている積み木・相手の最下段を弾いたときの弾速(打ち返しの基本弾速でもある)")]
        public float SweepLaunchSpeed = 10.0f;
        [Tooltip("打ち返しの弾速 = 基本弾速 × 倍率^打ち返し回数")]
        public float ReturnSpeedRate = 1.25f;
        [Tooltip("打ち返しの弾速の上限")]
        public float MaxProjectileSpeed = 30.0f;
    }
}
