using UnityEngine;

// シーン遷移のフェードの見た目を決める(Nakagawa)。
// 起動時に自動で Fader へ入れる（シーンに置く必要はない）。
// ・色：Color で変える（今は白）
// ・時間：FadeOut / FadeIn（秒。0 なら Fader の既定 0.4 秒）
// ・積み木が転がる遷移演出（TumikiRollTransition）は使わない
public static class SceneFadeStyle
{
    public static readonly Color FadeColor = Color.white;
    public const float FadeOut = 0f;
    public const float FadeIn = 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        Fader.FadeColor = FadeColor;
        Fader.FadeOutDuration = FadeOut;
        Fader.FadeInDuration = FadeIn;

        // 積み木の演出を外して、どの遷移もフェードにする
        if (!TumikiRollTransition.Enabled && Fader.Transition is TumikiRollTransition) Fader.Transition = null;
    }
}
