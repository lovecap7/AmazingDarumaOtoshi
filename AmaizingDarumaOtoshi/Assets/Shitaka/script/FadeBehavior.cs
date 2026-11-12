using UnityEngine;
using UnityEngine.Rendering;

// 飛んでいる途中で、だんだん薄くなって見えにくくなる積み木。
// 当たり判定は変わらない。止まったら元の濃さに戻る
public class FadeBehavior : NormalBehavior
{
    readonly Renderer rend;
    readonly float fadeDelay;    // 発射してから、薄くなり始めるまでの時間(秒)
    readonly float fadeDuration; // 薄くなりきるまでの時間(秒)
    readonly float minAlpha;     // 最も薄いときのアルファ値(0で完全に透明)

    float elapsed;

    public FadeBehavior(BlockController owner, float fadeDelay = 0.5f,
                        float fadeDuration = 0.8f, float minAlpha = 0.12f)
    {
        this.rend = owner.GetComponent<Renderer>();
        this.fadeDelay = fadeDelay;
        this.fadeDuration = fadeDuration;
        this.minAlpha = minAlpha;

        // 飛行が終わって止まったら、元の濃さに戻す
        owner.OnFlightEnded += Restore;
    }

    public override Vector3 UpdateDirection(Vector3 currentDirection, float deltaTime)
    {
        elapsed += deltaTime;

        // 0(薄くなり始める前)〜1(薄くなりきった)
        float t = Mathf.Clamp01((elapsed - fadeDelay) / fadeDuration);
        SetAlpha(Mathf.Lerp(1f, minAlpha, t));

        return currentDirection; // 向きは変えない
    }

    void Restore()
    {
        elapsed = 0.0f;
        SetAlpha(1.0f);
    }

    void SetAlpha(float a)
    {
        if (rend == null) return;

        // 積み木ごとの色を保ったまま、アルファだけ変える
        Color c = rend.material.color;
        c.a = a;
        rend.material.color = c;

        // 影が残ると、薄くしても位置が分かってしまうので、薄いあいだは影を消す
        rend.shadowCastingMode = (a < 0.5f) ? ShadowCastingMode.Off : ShadowCastingMode.On;
    }
}
