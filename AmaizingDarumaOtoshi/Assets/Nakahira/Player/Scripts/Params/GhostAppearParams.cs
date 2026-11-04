using UnityEngine;

namespace Nakahira
{
    // 幽霊が出るときの演出(GhostAppearEffect)の値
    // やられた所から魂(人魂)が上へ飛んで消える → 待つ → 着地点へ上から降ってくる → 幽霊が出る
    // 時間はすべてゲーム内の時間(Time.deltaTime)。撃破演出のスロー中はゆっくり進む
    // 値は使うたびに直接読むので、再生中に変更しても即座に反映される
    [CreateAssetMenu(fileName = "GhostAppearParams", menuName = "Nakahira/Ghost Appear Params")]
    public class GhostAppearParams : ScriptableObject
    {
        [Header("時間(秒)")]
        [Tooltip("魂が上へ飛んで消えるまで")]
        [Min(0.01f)] public float RiseTime = 0.5f;
        [Tooltip("魂が見えない間(着地点に目印が出る)")]
        [Min(0.0f)] public float WaitTime = 1.0f;
        [Tooltip("魂が上から降ってきて着地するまで")]
        [Min(0.01f)] public float FallTime = 0.5f;
        [Tooltip("着地したあと、幽霊がぽんと大きくなるまで")]
        [Min(0.01f)] public float PopTime = 0.3f;

        [Header("動き")]
        [Tooltip("魂が飛び出す高さ(やられた位置から。落下のときは場外判定の位置から飛び出す)")]
        public float StartHeight = 0.6f;
        [Tooltip("魂が上がる高さ(画面の外まで)")]
        [Min(0.0f)] public float RiseHeight = 25.0f;
        [Tooltip("魂が降り始める高さ(着地点から)")]
        [Min(0.0f)] public float FallHeight = 25.0f;
        [Tooltip("魂が着地する高さ(幽霊の頭の高さ)")]
        public float LandHeight = 0.75f;
        [Tooltip("上がる動き(横: 時間 0〜1 / 縦: 高さ 0〜1)")]
        public AnimationCurve RiseCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.0f, 0.3f, 0.3f), new Keyframe(1.0f, 1.0f, 2.2f, 2.2f));
        [Tooltip("降りる動き(横: 時間 0〜1 / 縦: 進んだ割合 0〜1)")]
        public AnimationCurve FallCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.0f, 0.4f, 0.4f), new Keyframe(1.0f, 1.0f, 2.0f, 2.0f));

        [Header("魂の見た目")]
        [Tooltip("魂の色(外側の光)")]
        public Color SoulColor = new Color(0.55f, 0.8f, 1.0f, 1.0f);
        [Tooltip("魂の芯の色")]
        public Color CoreColor = new Color(0.9f, 0.97f, 1.0f, 1.0f);
        [Tooltip("魂の頭の半径(m)")]
        [Min(0.01f)] public float SoulRadius = 0.32f;
        [Tooltip("しっぽの長さ(頭の半径に対する倍率)")]
        [Min(0.0f)] public float TailLength = 1.0f;
        [Tooltip("速く動くほどしっぽを伸ばす倍率(最大)")]
        [Min(1.0f)] public float TailStretch = 1.8f;
        [Tooltip("ゆらめく速さ")]
        [Min(0.0f)] public float FlickerSpeed = 18.0f;
        [Tooltip("軌跡が残る時間(秒)")]
        [Min(0.0f)] public float TrailTime = 0.25f;
        [Tooltip("軌跡の太さ(m)")]
        [Min(0.0f)] public float TrailWidth = 0.45f;

        [Header("着地点の目印")]
        [Tooltip("待ち始めたときの輪の半径(m)")]
        [Min(0.01f)] public float MarkerStartRadius = 2.0f;
        [Tooltip("着地したときの輪の半径(m)")]
        [Min(0.01f)] public float MarkerEndRadius = 0.7f;
        [Tooltip("目印の濃さ")]
        [Range(0.0f, 1.0f)] public float MarkerAlpha = 0.85f;

        [Header("着地")]
        [Tooltip("着地の閃光の時間(秒)")]
        [Min(0.01f)] public float FlashTime = 0.35f;
        [Tooltip("閃光で輪が広がる大きさ(着地したときの半径に対する倍率)")]
        [Min(1.0f)] public float FlashSpread = 3.5f;
        [Tooltip("幽霊がぽんと出るときに一瞬大きくなる倍率")]
        [Min(1.0f)] public float PopOvershoot = 1.2f;

        [Header("復活: 魂が出現ポイントへ飛ぶ(時間は DarumaMatch の「復活で魂が移動する時間」)")]
        [Tooltip("魂が飛び出す高さ(幽霊の位置から)")]
        public float ReviveStartHeight = 0.75f;
        [Tooltip("魂がたどり着く高さ(出現ポイントから。ここで光の柱に変わる)")]
        public float ReviveEndHeight = 0.9f;
        [Tooltip("弧の高さ(距離 0 のとき, m)")]
        [Min(0.0f)] public float ReviveArcHeight = 1.5f;
        [Tooltip("距離 1m ごとに弧を高くする量(m)")]
        [Min(0.0f)] public float ReviveArcPerMeter = 0.2f;
        [Tooltip("弧の高さの上限(m)")]
        [Min(0.0f)] public float ReviveArcMax = 5.0f;
        [Tooltip("進み方(横: 時間 0〜1 / 縦: 進んだ割合 0〜1)")]
        public AnimationCurve ReviveCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.0f, 0.0f, 0.0f), new Keyframe(1.0f, 1.0f, 0.6f, 0.6f));

        [Header("光源")]
        [Min(0.0f)] public float LightIntensity = 3.0f;
        [Min(0.1f)] public float LightRange = 3.0f;
    }
}
