using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// タイトル画面。だるま落とし演出を3Dで見せる。
//  1. 「アメイジングだるま落とし」の積み木が縦に積まれ、てっぺんにスマイルの顔
//  2. ハンマーが横から一番下を1個ずつ叩き出す（だんだん速く）。上の山はストンと1段落ちる
//     1発目は寄りでスローモーション、中盤は引きの画で連打
//  3. 叩かれた積み木は右へ飛び出し、くるっと回ってロゴの位置へピタッと収まる
//  4. 最後の1個はバレットタイム。顔は一瞬宙に残ってから床にポテッ。カメラを見てニコッ
//  5. 「はじめる」「やめる」の積み木が降ってくる。カーソルはハンマー、決定でその積み木を叩き飛ばす
// 演出中はボタンでスキップ。放置すると最初から再生（展示用。Build Settings に入っているときのみ）。
// 舞台の組み立ては TitleScreen.Stage.cs。
public partial class TitleScreen : MonoBehaviour
{
    [Header("素材")]
    [SerializeField] TMP_FontAsset font;

    [Header("文字")]
    [SerializeField] string lineTop = "アメイジング";
    [SerializeField] string lineBottom = "だるま落とし";
    [SerializeField] string subtitleText = "AMAZING  DARUMA  OTOSHI";
    [Tooltip("上から順に積まれる。0番目が決定でメニューへ、それ以外は終了")]
    [SerializeField] string[] menuLabels = { "はじめる", "やめる" };
    [SerializeField] string menuSceneName = "MenuScene";

    [Header("大きさ")]
    [SerializeField] float stackScale = 0.75f;      // 山に積まれているときの積み木
    [SerializeField] float logoScale = 1.45f;       // ロゴになったときの積み木
    [SerializeField] float hammerScale = 0.75f;
    [SerializeField] float buttonScale = 0.9f;

    [Header("ロゴの配置")]
    [SerializeField] float[] logoRowY = { 5.75f, 4.55f };
    [SerializeField] float logoZ = 2f;
    [SerializeField] float logoSpacing = 1.95f;

    [Header("ボタンの配置")]
    [SerializeField] float buttonX = 3.6f;
    [SerializeField] float buttonZ = -0.6f;

    [Header("最終カメラ")]
    [SerializeField] Vector3 finalCamPos = new Vector3(0.6f, 2.8f, -12f);
    [SerializeField] Vector3 finalLook = new Vector3(0.6f, 3.0f, 1.5f);
    [SerializeField] float finalFov = 40f;

    [Header("展示用")]
    [Tooltip("ボタン表示後、この秒数操作がないと最初から再生（0で無効）")]
    [SerializeField] float attractDelay = 30f;

    const float BloomBase = 0.35f;
    const float LetterboxHeight = 90f;
    const float DefaultAperture = 2.8f;
    const float FlyTime = 0.8f;
    const float HangTime = 0.05f;       // 叩き出した後、上の山が宙に残る時間
    const float DropTime = 0.1f;        // 1段落ちる時間
    const float FinalHang = 0.55f;      // 最後の1個を抜いた後、顔が宙に残る時間
    const float IdleYaw = -32f;
    const float CursorOffset = 0.25f;
    const float CursorScale = 1.06f;

    enum Phase { Cinematic, Ready, Leaving }
    enum LetterState { Stacked, Flying, Landed }
    Phase phase;

    // ---- ゲーム内時間（スローモーションの影響を受ける） ----
    float sim;

    // ---- 文字の積み木 ----
    LetterState[] state;
    float[] flyStart, landTime, stackYaw;
    Vector3[] flyFrom, tumbleAxis;
    int knocked;
    float dropStart = -100f;
    float pulseStart = -100f;

    // ---- 顔 ----
    float headDropStart = -100f, headLandTime = -100f, headSquashStart = -100f, headHopStart = -100f;
    float headHopHeight;
    bool headLanded;
    Quaternion headRot = Quaternion.Euler(0f, 28f, 0f);    // 少しよそ見している

    // ---- ハンマー ----
    bool swinging;
    float hammerYaw = IdleYaw;
    float recoverStart = -100f;
    Vector3 hammerPos;

    // ---- ボタン ----
    bool buttonsShown;
    float buttonsStart = -100f;
    bool[] buttonLanded;
    float[] buttonOffset, buttonScaleNow;
    int cursor;
    int selected = -1;
    float selectedTime;

    // ---- カメラ（実時間で動く。スロー中も動ける） ----
    struct Shot
    {
        public Vector3 p0, p1, l0, l1;
        public float f0, f1, dur, start;
        public bool orbit;
        public Vector3 center;
        public float a0, a1, radius, height;
        public float ap0, ap1;      // 絞り（小さいほど背景がボケる）。0なら既定値
    }
    Shot shot;
    Transform track;
    float trackWeight, trackTarget;
    float shake;
    float driftAmount = 0.7f;
    Vector3 lastLook;

    // ---- 効果 ----
    float chromaPulse, bloomPulse, lensPulse;
    float speedTarget, speedReroll;
    float shockStart = -100f, ringStart = -100f;
    Coroutine slowRoutine;
    readonly List<GameObject> pops = new List<GameObject>();

    float lastInputTime;
    int skipFrame = -1;

    float BH => BlockH * stackScale;    // 山の1段の高さ

    void Start()
    {
        BuildStage();

        int n = blocks.Length;
        state = new LetterState[n];
        flyStart = new float[n];
        landTime = new float[n];
        stackYaw = new float[n];
        flyFrom = new Vector3[n];
        tumbleAxis = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            stackYaw[i] = UnityEngine.Random.Range(-5f, 5f);
            tumbleAxis[i] = UnityEngine.Random.onUnitSphere;
        }

        buttonLanded = new bool[buttons.Length];
        buttonOffset = new float[buttons.Length];
        buttonScaleNow = new float[buttons.Length];
        for (int i = 0; i < buttons.Length; i++) buttonScaleNow[i] = 1f;

        hammerPos = HammerPivot(StackContact());
        StartCoroutine(Cinematic());
    }

    void OnDestroy() => Time.timeScale = 1f;

    // ================= 演出の台本 =================
    IEnumerator Cinematic()
    {
        phase = Phase.Cinematic;
        int n = blocks.Length;

        // 積み上がった塔を、床に近い位置から見上げる
        Cut(Shallow(Line(new Vector3(3.4f, 0.6f, -10f), new Vector3(2.7f, 1.0f, -8.8f), new Vector3(0f, 2.9f, 0f), new Vector3(0f, 3.2f, 0f), 48f, 46f, 3.2f)));
        StartCoroutine(FadeTo(0f, 1.4f));
        yield return Real(2.4f);

        // 1発目：寄りで。当たった瞬間スロー、飛んでいく積み木を目で追う
        Cut(Shallow(Line(new Vector3(1.5f, 0.4f, -3.6f), new Vector3(1.2f, 0.42f, -3.1f), new Vector3(-0.5f, 0.35f, 0f), new Vector3(-0.3f, 0.4f, 0f), 46f, 44f, 2.6f)));
        yield return Real(0.45f);
        yield return Swing(0.42f, -85f, () =>
        {
            Knock(0, 1f);
            SlowMo(0.08f, 0.45f);
            Track(blocks[0].t, 0.65f);
            Pop("カコーン！", StackContact() + new Vector3(-0.2f, 1.0f, -0.3f), 96f, Palette[1], -8f);
        });
        yield return Real(1.2f);
        Track(null, 0f);

        // 2〜4発目：山が1段ずつ落ちていくのを見せる
        Cut(Line(new Vector3(3.6f, 1.5f, -7f), new Vector3(3.1f, 1.7f, -6.3f), new Vector3(0f, 1.9f, 0.5f), new Vector3(0f, 1.7f, 0.5f), 44f, 42f, 3.2f));
        yield return Real(0.25f);
        for (int k = 1; k <= 3 && k < n - 1; k++)
        {
            int index = k;
            yield return Swing(0.2f, -78f, () => { Knock(index, 0.6f); SlowMo(0.2f, 0.05f); });
            yield return Sim(0.3f);
        }

        // 5〜11発目：引きの画で連打。奥でロゴができていく
        Cut(Line(new Vector3(0.4f, 3.0f, -13.5f), new Vector3(0.2f, 3.0f, -12.3f), new Vector3(0f, 3.4f, 1.5f), new Vector3(0f, 3.3f, 1.5f), 42f, 41f, 5f));
        yield return Real(0.3f);
        for (int k = 4; k < n - 1; k++)
        {
            int index = k;
            float t = (k - 4f) / Mathf.Max(1f, n - 6f);
            float interval = Mathf.Lerp(0.42f, 0.16f, t);
            yield return Swing(0.1f, -70f, () => Knock(index, 0.4f));
            yield return Sim(Mathf.Max(0.02f, interval - 0.15f));
        }
        yield return Sim(0.35f);

        // 最後の1発：振りかぶりで時間が溜まり、当たった瞬間ほぼ止まる。カメラは回り込む
        Cut(Orbit(new Vector3(0f, 0.3f, 0f), new Vector3(0f, 0.65f, 0f), 2.8f, 0.25f, -55f, 15f, 40f, 44f, 2.8f));
        Time.timeScale = 0.5f;
        yield return Swing(0.45f, -98f, () =>
        {
            Knock(n - 1, 1.4f);
            SlowMo(0.04f, 1.3f);
            speedTarget = 1f;
            PopAt("ドンッ！！", new Vector2(-520f, 270f), 150f, Palette[0], -8f, 1.3f);   // 寄りのカメラなので画面上の位置で出す
        });
        float timeout = Time.unscaledTime + 6f;
        while (!headLanded && Time.unscaledTime < timeout) yield return null;
        yield return Real(0.4f);

        // クレーンで引いて全体を見せ、顔がニコッ
        Cut(Line(cam.transform.position, finalCamPos, lastLook, finalLook, cam.fieldOfView, finalFov, 2.2f));
        yield return Real(1.0f);
        yield return HeadSmile();
        StartCoroutine(Reveal());
        yield return Real(0.6f);

        ShowButtons();
        while (!AllButtonsLanded()) yield return null;
        EnterReady();
    }

    void Knock(int index, float power)
    {
        state[index] = LetterState.Flying;
        flyStart[index] = sim;
        flyFrom[index] = blocks[index].t.position;
        knocked = index + 1;
        dropStart = sim + HangTime;
        if (knocked >= blocks.Length) headDropStart = sim + FinalHang;

        var contact = StackContact();
        Emit(confetti, contact, Quaternion.Euler(-30f, 90f, 0f), Mathf.RoundToInt(10 + 40 * power));
        Emit(sparkle, contact, Quaternion.Euler(0f, 90f, 0f), Mathf.RoundToInt(8 + 20 * power));
        Emit(puff, new Vector3(0f, 0.05f, 0f), Quaternion.Euler(-90f, 0f, 0f), Mathf.RoundToInt(4 + 8 * power));
        if (power >= 1f)
        {
            shockStart = Time.unscaledTime;
            shockwave.position = contact;
            ringStart = Time.unscaledTime;
            groundRing.position = new Vector3(0f, 0.02f, 0f);
        }
        shake = Mathf.Max(shake, 0.45f * power);
        chromaPulse = Mathf.Max(chromaPulse, 0.5f * power);
        lensPulse = Mathf.Max(lensPulse, 0.12f * power);
        bloomPulse = Mathf.Max(bloomPulse, 0.2f * power);
    }

    IEnumerator HeadSmile()
    {
        // カメラの方を向く
        var start = headRot;
        var dir = new Vector3(0f, HeadR, 0f) - finalCamPos;
        dir.y = 0f;
        var face = Quaternion.LookRotation(dir);   // 顔は -Z なので、カメラから顔への向きを +Z にする
        for (float t = 0f; t < 0.45f; t += Time.deltaTime)
        {
            headRot = Quaternion.Slerp(start, face, Mathf.SmoothStep(0f, 1f, t / 0.45f));
            yield return null;
        }
        headRot = face;

        // まばたき → ニコッ
        yield return Sim(0.2f);
        for (float t = 0f; t < 0.14f; t += Time.deltaTime)
        {
            eyesNormal.localScale = new Vector3(1f, Mathf.Abs(1f - 2f * t / 0.14f) * 0.9f + 0.1f, 1f);
            yield return null;
        }
        eyesNormal.localScale = Vector3.one;
        yield return Sim(0.25f);

        SetHappy(true);
        Emit(sparkle, new Vector3(0f, HeadR + 0.3f, 0f), Quaternion.Euler(-90f, 0f, 0f), 25);
        Pop("ニコッ", new Vector3(0.95f, HeadR + 0.8f, 0f), 64f, Palette[5], 10f);
        headHopStart = sim;
        headHopHeight = 0.22f;
    }

    IEnumerator Reveal()
    {
        pulseStart = sim;
        Emit(confetti, new Vector3(0f, 8.5f, logoZ - 1f), Quaternion.Euler(90f, 0f, 0f), 160);
        bloomPulse = Mathf.Max(bloomPulse, 0.3f);
        for (float t = 0f; t < 1.4f; t += Time.unscaledDeltaTime)
        {
            subtitle.alpha = Mathf.Clamp01((t - 0.3f) / 0.8f);
            SetLetterbox(1f - Mathf.SmoothStep(0f, 1f, t / 1.2f));
            yield return null;
        }
        subtitle.alpha = 1f;
        SetLetterbox(0f);
    }

    void ShowButtons()
    {
        buttonsShown = true;
        buttonsStart = sim;
        foreach (var b in buttons) b.t.gameObject.SetActive(true);
    }

    void EnterReady()
    {
        phase = Phase.Ready;
        lastInputTime = Time.unscaledTime;
        driftAmount = 1f;
    }

    void SkipToEnd()
    {
        StopAllCoroutines();
        slowRoutine = null;
        Time.timeScale = 1f;
        speedTarget = 0f;
        foreach (var p in pops) Destroy(p);
        pops.Clear();
        swinging = false;
        recoverStart = -100f;
        Track(null, 0f);

        int n = blocks.Length;
        for (int i = 0; i < n; i++) { state[i] = LetterState.Landed; landTime[i] = -100f; }
        knocked = n;
        dropStart = headDropStart = headLandTime = headSquashStart = headHopStart = -100f;
        headLanded = true;
        var dir = new Vector3(0f, HeadR, 0f) - finalCamPos; dir.y = 0f;
        headRot = Quaternion.LookRotation(dir);
        SetHappy(true);
        pulseStart = sim;

        ShowButtons();
        buttonsStart = sim - 10f;
        for (int i = 0; i < buttonLanded.Length; i++) buttonLanded[i] = true;

        fade.color = new Color(1f, 1f, 1f, 0f);
        subtitle.alpha = 1f;
        SetLetterbox(0f);
        skipFrame = Time.frameCount;

        Cut(Line(finalCamPos, finalCamPos, finalLook, finalLook, finalFov, finalFov, 0.01f));
        EnterReady();
    }

    void SetHappy(bool happy)
    {
        eyesNormal.gameObject.SetActive(!happy);
        eyesHappy.gameObject.SetActive(happy);
        cheeks.gameObject.SetActive(happy);
    }

    // ================= 入力・ボタン =================
    void HandleInput()
    {
        bool submit = MenuInput.Submit(), cancel = MenuInput.Cancel(), click = MenuInput.AnyClick();
        bool up = MenuInput.Up(), down = MenuInput.Down();
        var mouse = Mouse.current;
        bool mouseMoved = mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0f;
        if (submit || cancel || click || up || down || mouseMoved) lastInputTime = Time.unscaledTime;
        if (Time.frameCount == skipFrame) return;

        switch (phase)
        {
            case Phase.Cinematic:
                if (submit || cancel || click) SkipToEnd();
                break;

            case Phase.Ready:
                int hover = mouse != null && (mouseMoved || click) ? ButtonUnderMouse(mouse.position.ReadValue()) : -1;
                if (hover >= 0) cursor = hover;

                if (up) cursor = (cursor + buttons.Length - 1) % buttons.Length;
                else if (down) cursor = (cursor + 1) % buttons.Length;
                else if (submit || (click && hover >= 0)) Select(cursor);
                else if (attractDelay > 0f && Time.unscaledTime - lastInputTime > attractDelay
                         && Application.CanStreamedLevelBeLoaded(SceneManager.GetActiveScene().name))   // Build Settings に入っているときだけ
                {
                    phase = Phase.Leaving;
                    Fader.Load(SceneManager.GetActiveScene().name);
                }
                break;
        }
    }

    int ButtonUnderMouse(Vector2 screen)
    {
        if (!Physics.Raycast(cam.ScreenPointToRay(screen), out var hit, 100f)) return -1;
        for (int i = 0; i < buttons.Length; i++)
            if (hit.transform == buttons[i].t) return i;
        return -1;
    }

    void Select(int index)
    {
        phase = Phase.Leaving;
        StartCoroutine(SelectRoutine(index));
    }

    IEnumerator SelectRoutine(int index)
    {
        yield return Swing(0.25f, -85f, () =>
        {
            selected = index;
            selectedTime = sim;
            SlowMo(0.15f, 0.12f);
            var contact = ButtonContact(index);
            Emit(confetti, contact, Quaternion.Euler(-30f, 90f, 0f), 50);
            Emit(sparkle, contact, Quaternion.Euler(0f, 90f, 0f), 20);
            shake = 0.7f;
            chromaPulse = 0.5f;
            Pop("カコーン！", contact + new Vector3(-0.3f, 1.0f, -0.3f), 96f, Palette[1], -8f);
        });
        yield return Real(0.55f);

        if (index == 0) Fader.Load(menuSceneName);
        else
        {
            yield return FadeTo(1f, 0.5f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    // ================= 毎フレーム =================
    void Update()
    {
        sim += Time.deltaTime;
        float raw = Time.unscaledDeltaTime;

        HandleInput();
        UpdateLetters();
        UpdateHead();
        UpdateButtons();
        UpdateHammer();
        UpdateCamera(raw);
        UpdateFx(raw);
        UpdateUI(raw);
    }

    // 山が落ちていく量（叩き出すたびに1段ずつ、重力で加速しながら）
    float StackOffset()
    {
        if (knocked == 0) return 0f;
        float d = Mathf.Clamp01((sim - dropStart) / DropTime);
        return -BH * (knocked - 1 + d * d);
    }

    void UpdateLetters()
    {
        int n = blocks.Length;
        float off = StackOffset();
        float sq = Squash((sim - dropStart - DropTime) / 0.25f, 0.12f);
        float time = Time.time;

        for (int i = 0; i < n; i++)
        {
            var b = blocks[i];
            switch (state[i])
            {
                case LetterState.Stacked:
                    b.t.position = new Vector3(0f, BH * 0.5f + i * BH + off, 0f);
                    b.t.rotation = Quaternion.Euler(0f, stackYaw[i], 0f);
                    b.t.localScale = SquashVec(sq) * stackScale;
                    break;

                case LetterState.Flying:
                {
                    float t = Mathf.Clamp01((sim - flyStart[i]) / FlyTime);
                    float u = 1f - Mathf.Pow(1f - t, 2.2f);   // 叩かれた直後が一番速い
                    Vector3 p0 = flyFrom[i], p3 = Slot(i);
                    Vector3 p1 = p0 + new Vector3(3.4f, 1.2f, -1.8f);   // 右へ飛び出し、塔の手前を回り込む
                    Vector3 p2 = p3 + new Vector3(0f, 2.0f, -2.4f);
                    b.t.position = Bezier(p0, p1, p2, p3, u);
                    b.t.rotation = Quaternion.Slerp(Quaternion.Euler(0f, stackYaw[i], 0f), Quaternion.identity, Mathf.SmoothStep(0f, 1f, u))
                                   * Quaternion.AngleAxis(720f * (1f - u) * (1f - u), tumbleAxis[i]);
                    b.t.localScale = Vector3.one * Mathf.Lerp(stackScale, logoScale, u);
                    if (t >= 1f)
                    {
                        // ピタッ
                        state[i] = LetterState.Landed;
                        landTime[i] = sim;
                        b.flash = 2.2f;
                        Emit(sparkle, p3 + new Vector3(0f, 0f, -0.8f), Quaternion.Euler(0f, 180f, 0f), 10);
                        shake = Mathf.Max(shake, 0.12f);
                    }
                    break;
                }

                default:
                {
                    // ロゴの位置でふわふわ。着地とロゴ完成のときに弾む
                    b.t.position = Slot(i) + Vector3.up * Mathf.Sin(time * 1.4f + b.phase) * 0.06f;
                    b.t.rotation = Quaternion.Euler(Mathf.Sin(time * 1.1f + b.phase) * 3f, Mathf.Sin(time * 0.8f + b.phase * 1.3f) * 5f, Mathf.Sin(time * 0.9f + b.phase) * 2f);
                    float s = 1f + Punch((sim - landTime[i]) / 0.45f, 0.3f);
                    int row = i < lineTop.Length ? 0 : 1, col = row == 0 ? i : i - lineTop.Length;
                    s += Punch((sim - pulseStart - col * 0.06f - row * 0.15f) / 0.45f, 0.18f);
                    b.t.localScale = Vector3.one * s * logoScale;
                    break;
                }
            }

            if (b.flash > 0.001f)
            {
                b.flash *= Mathf.Exp(-6f * Time.deltaTime);
                b.mat.SetColor("_EmissionColor", Color.white * b.flash);
            }
        }
    }

    void UpdateHead()
    {
        int n = blocks.Length;
        float y;
        Vector3 s = Vector3.one;

        if (knocked < n)
        {
            // 山と一緒に動く
            y = n * BH + HeadR + StackOffset();
            s = SquashVec(Squash((sim - dropStart - DropTime) / 0.25f, 0.12f));
        }
        else if (!headLanded)
        {
            // 最後の1個が抜けても、しばらく宙に残る（下を見てから落ちる）
            float hangY = BH + HeadR;
            float lookT = Mathf.Clamp01((sim - (headDropStart - 0.3f)) / 0.2f);
            headRot = Quaternion.Slerp(Quaternion.Euler(0f, 28f, 0f), Quaternion.Euler(-28f, 10f, 0f), lookT);
            float tau = Mathf.Max(0f, sim - headDropStart);
            y = hangY - 0.5f * 18f * tau * tau;
            if (y <= HeadR)
            {
                y = HeadR;
                headLanded = true;
                headLandTime = headSquashStart = sim;
                headHopStart = sim;
                headHopHeight = 0.3f;
                Emit(puff, new Vector3(0f, 0.05f, 0f), Quaternion.Euler(-90f, 0f, 0f), 18);
                ringStart = Time.unscaledTime;
                groundRing.position = new Vector3(0f, 0.02f, 0f);
                shake = Mathf.Max(shake, 0.5f);
                Pop("ポテッ", new Vector3(1.0f, HeadR + 0.7f, 0f), 72f, Palette[2], 12f);
            }
        }
        else y = HeadR;

        if (headLanded) s = SquashVec(Squash((sim - headSquashStart) / 0.45f, 0.35f));

        float hp = (sim - headHopStart) / 0.3f;
        if (hp > 0f && hp < 1f) y += headHopHeight * 4f * hp * (1f - hp);

        head.position = new Vector3(0f, y, 0f);
        head.rotation = headRot;
        head.localScale = s;
    }

    void UpdateButtons()
    {
        float follow = 1f - Mathf.Exp(-16f * Time.deltaTime);
        float bh = ButtonSize.y * buttonScale;
        int count = buttons.Length;

        for (int i = 0; i < count; i++)
        {
            var b = buttons[i];
            if (!buttonsShown) continue;

            // 下のボタンから順に降ってきて積み重なる
            int order = count - 1 - i;
            Vector3 slot = ButtonSlot(i);
            float t = (sim - buttonsStart - order * 0.35f) / 0.45f;
            float d = Mathf.Clamp01(t);
            Vector3 pos = slot + Vector3.up * 9f * (1f - d * d);
            Vector3 s = Vector3.one;

            if (t >= 1f && !buttonLanded[i])
            {
                buttonLanded[i] = true;
                Emit(puff, slot - Vector3.up * bh * 0.5f, Quaternion.Euler(-90f, 0f, 0f), 8);
                shake = Mathf.Max(shake, 0.2f);
                Pop("コトン", slot + new Vector3(1.6f, 0.2f, 0f), 46f, Palette[(i + 2) % Palette.Length], UnityEngine.Random.Range(-12f, 12f));
            }
            if (t >= 1f) s = SquashVec(Squash((t - 1f) * 0.45f / 0.35f, 0.18f));

            bool isCursor = phase == Phase.Ready && i == cursor;
            buttonOffset[i] = Mathf.Lerp(buttonOffset[i], isCursor ? CursorOffset : 0f, follow);
            buttonScaleNow[i] = Mathf.Lerp(buttonScaleNow[i], isCursor ? CursorScale : 1f, follow);
            pos.x += buttonOffset[i];
            float rot = 0f;

            if (selected >= 0)
            {
                float f = (sim - selectedTime) / 0.6f;
                if (i == selected)
                {
                    // 右へ吹き飛ぶ
                    pos += new Vector3(14f * f * f + 5f * f, 1.5f * f, 0f);
                    rot = -540f * f;
                }
                else if (i < selected)
                {
                    // 下が抜けたら上はストンと落ちる
                    float dd = Mathf.Clamp01((sim - selectedTime - HangTime) / 0.12f);
                    pos.y -= bh * dd * dd;
                }
            }

            b.t.position = pos;
            b.t.rotation = Quaternion.Euler(0f, 0f, rot);
            b.t.localScale = s * buttonScale * buttonScaleNow[i];
            float glow = isCursor ? 0.08f + 0.05f * Mathf.Sin(Time.unscaledTime * 5f) : 0f;
            b.mat.SetColor("_EmissionColor", Color.white * glow);
        }
    }

    void UpdateHammer()
    {
        if (!swinging)
        {
            float t = Mathf.Clamp01((sim - recoverStart) / 0.35f);
            float sway = Mathf.Sin(Time.unscaledTime * 1.7f) * 3f;
            hammerYaw = Mathf.LerpUnclamped(14f, IdleYaw + sway, EaseOutBack(t));
        }

        // 演出中は山の一番下、ボタン操作中は選択中のボタンを狙う
        Vector3 target = phase == Phase.Cinematic || !buttonsShown ? HammerPivot(StackContact()) : HammerPivot(ButtonContact(cursor));
        if (!swinging && phase != Phase.Leaving)
            hammerPos = Vector3.Lerp(hammerPos, target, 1f - Mathf.Exp(-10f * Time.deltaTime));

        hammer.position = hammerPos;
        hammer.rotation = Quaternion.Euler(0f, hammerYaw, 0f);
    }

    // 振りかぶって叩く（ゲーム内時間で動く）
    IEnumerator Swing(float windup, float windupYaw, Action onImpact)
    {
        swinging = true;
        float from = hammerYaw;
        for (float t = 0f; t < windup; t += Time.deltaTime)
        {
            float u = t / windup;
            hammerYaw = Mathf.Lerp(from, windupYaw, 1f - (1f - u) * (1f - u));
            yield return null;
        }
        const float strike = 0.07f;
        for (float t = 0f; t < strike; t += Time.deltaTime)
        {
            float u = t / strike;
            hammerYaw = Mathf.Lerp(windupYaw, 0f, u * u);
            yield return null;
        }
        hammerYaw = 0f;
        swinging = false;
        recoverStart = sim;
        onImpact();
    }

    // ---- 位置の計算 ----
    Vector3 StackContact() => new Vector3(-BlockW * stackScale * 0.5f, BH * 0.5f, 0f);

    Vector3 ButtonSlot(int i)
    {
        float bh = ButtonSize.y * buttonScale;
        return new Vector3(buttonX, (buttons.Length - 1 - i) * bh + bh * 0.5f, buttonZ);
    }

    Vector3 ButtonContact(int i)
    {
        var slot = ButtonSlot(i);
        return new Vector3(slot.x + CursorOffset - ButtonSize.x * buttonScale * CursorScale * 0.5f, slot.y, slot.z);
    }

    // 角度0のとき、ハンマーの頭の右面がちょうど contact に当たる根元の位置
    Vector3 HammerPivot(Vector3 contact) =>
        contact + new Vector3(-HammerHeadHalf * hammerScale, 0f, -HammerLength * hammerScale);

    Vector3 Slot(int i)
    {
        int row = i < lineTop.Length ? 0 : 1;
        int col = row == 0 ? i : i - lineTop.Length;
        int count = row == 0 ? lineTop.Length : lineBottom.Length;
        return new Vector3((col - (count - 1) * 0.5f) * logoSpacing, logoRowY[Mathf.Min(row, logoRowY.Length - 1)], logoZ);
    }

    bool AllButtonsLanded()
    {
        foreach (bool b in buttonLanded) if (!b) return false;
        return true;
    }

    // ================= カメラ =================
    static Shot Line(Vector3 p0, Vector3 p1, Vector3 l0, Vector3 l1, float f0, float f1, float dur) =>
        new Shot { p0 = p0, p1 = p1, l0 = l0, l1 = l1, f0 = f0, f1 = f1, dur = dur };

    static Shot Orbit(Vector3 center, Vector3 look, float radius, float height, float a0, float a1, float f0, float f1, float dur) =>
        new Shot { orbit = true, center = center, l0 = look, l1 = look, radius = radius, height = height, a0 = a0, a1 = a1, f0 = f0, f1 = f1, dur = dur };

    // 背景を大きくボカす
    static Shot Shallow(Shot s)
    {
        s.ap0 = s.ap1 = 1.6f;
        return s;
    }

    void Cut(Shot s)
    {
        s.start = Time.unscaledTime;
        shot = s;
    }

    void Track(Transform t, float weight)
    {
        if (t != null) track = t;
        trackTarget = weight;
    }

    void UpdateCamera(float dt)
    {
        float t = Mathf.Clamp01((Time.unscaledTime - shot.start) / Mathf.Max(0.001f, shot.dur));
        float e = Mathf.SmoothStep(0f, 1f, t);

        Vector3 pos, look = Vector3.Lerp(shot.l0, shot.l1, e);
        if (shot.orbit)
        {
            float a = Mathf.Lerp(shot.a0, shot.a1, e) * Mathf.Deg2Rad;
            pos = shot.center + new Vector3(Mathf.Sin(a) * shot.radius, shot.height, -Mathf.Cos(a) * shot.radius);
        }
        else pos = Vector3.Lerp(shot.p0, shot.p1, e);

        trackWeight = Mathf.MoveTowards(trackWeight, trackTarget, dt * 2.5f);
        if (track != null && trackWeight > 0f) look = Vector3.Lerp(look, track.position, trackWeight);
        lastLook = look;

        // 手持ちカメラのような揺らぎ＋衝撃の揺れ
        float tt = Time.unscaledTime;
        Vector3 drift = new Vector3(Mathf.PerlinNoise(tt * 0.21f, 1.3f) - 0.5f, Mathf.PerlinNoise(2.7f, tt * 0.17f) - 0.5f, 0f) * 0.1f * driftAmount;
        shake = Mathf.Max(0f, shake - dt * 2.2f);
        Vector3 jolt = UnityEngine.Random.insideUnitSphere * 0.1f * shake * shake;

        cam.transform.position = pos + drift + jolt;
        cam.transform.LookAt(look + drift * 0.5f);
        cam.transform.Rotate(0f, 0f, (Mathf.PerlinNoise(tt * 0.13f, 7.1f) - 0.5f) * 1.2f * driftAmount + UnityEngine.Random.Range(-1f, 1f) * 1.5f * shake * shake, Space.Self);
        cam.fieldOfView = Mathf.Lerp(shot.f0, shot.f1, e);

        dof.focusDistance.value = Vector3.Distance(pos, look);
        dof.aperture.value = Mathf.Lerp(shot.ap0 > 0f ? shot.ap0 : DefaultAperture, shot.ap1 > 0f ? shot.ap1 : DefaultAperture, e);
    }

    // ================= 効果・UI =================
    void UpdateFx(float dt)
    {
        chromaPulse = Mathf.MoveTowards(chromaPulse, 0f, dt * 1.2f);
        bloomPulse = Mathf.MoveTowards(bloomPulse, 0f, dt * 0.8f);
        lensPulse = Mathf.MoveTowards(lensPulse, 0f, dt * 0.5f);
        chroma.intensity.value = Mathf.Clamp01(chromaPulse);
        bloom.intensity.value = BloomBase + bloomPulse * 2f;
        lens.intensity.value = -lensPulse;

        // 衝撃波（カメラを向いた輪）と床の輪
        float s = (Time.unscaledTime - shockStart) / 0.7f;
        if (s >= 0f && s <= 1f)
        {
            shockwave.rotation = cam.transform.rotation;
            shockwave.localScale = Vector3.one * Mathf.Lerp(0.3f, 4f, 1f - (1f - s) * (1f - s));
            shockMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.9f * (1f - s)));
        }
        else shockwave.localScale = Vector3.zero;

        float r = (Time.unscaledTime - ringStart) / 0.8f;
        if (r >= 0f && r <= 1f)
        {
            groundRing.localScale = Vector3.one * Mathf.Lerp(0.5f, 6f, 1f - (1f - r) * (1f - r));
            ringMat.SetColor("_BaseColor", new Color(1f, 0.97f, 0.9f, 0.7f * (1f - r)));
        }
        else groundRing.localScale = Vector3.zero;

        // 集中線はスローモーション中だけ
        if (speedTarget > 0f && slowRoutine == null) speedTarget = 0f;
        speedGroup.alpha = Mathf.MoveTowards(speedGroup.alpha, speedTarget, dt * (speedTarget > 0f ? 8f : 2.5f));
        speedReroll -= dt;
        if (speedGroup.alpha > 0f && speedReroll <= 0f) { speedLines.Reroll(); speedReroll = 0.05f; }
    }

    void UpdateUI(float dt)
    {
        skipGroup.alpha = Mathf.MoveTowards(skipGroup.alpha, phase == Phase.Cinematic && fade.color.a < 0.5f ? 1f : 0f, dt * 3f);
        hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, phase == Phase.Ready ? 1f : 0f, dt * 3f);
    }

    // 擬音をワールド座標の位置に出す（ポンと膨らんで、少し浮いて消える）
    void Pop(string text, Vector3 world, float size, Color color, float angle, float hold = 0.45f)
    {
        Vector2 sp = cam.WorldToScreenPoint(world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(uiRoot, sp, null, out var local);
        PopAt(text, local, size, color, angle, hold);
    }

    // 擬音を画面上の位置（中央が原点、1920x1080 基準）に出す。表示のオンオフは PresentationSettings
    void PopAt(string text, Vector2 local, float size, Color color, float angle, float hold = 0.45f)
    {
        var go = ToyKit.Pop(this, uiRoot, font, popMat, text, local, size, color, angle, hold, pops);
        if (go != null) go.transform.SetSiblingIndex(fade.rectTransform.GetSiblingIndex());
    }

    IEnumerator FadeTo(float target, float time)
    {
        var c = fade.color;
        float from = c.a, start = Time.unscaledTime;
        while (Time.unscaledTime - start < time)
        {
            c.a = Mathf.Lerp(from, target, (Time.unscaledTime - start) / time);
            fade.color = c;
            yield return null;
        }
        c.a = target;
        fade.color = c;
    }

    // ================= 補助 =================
    void SlowMo(float scale, float holdReal)
    {
        if (slowRoutine != null) StopCoroutine(slowRoutine);
        slowRoutine = StartCoroutine(SlowRoutine(scale, holdReal));
    }

    IEnumerator SlowRoutine(float scale, float holdReal)
    {
        Time.timeScale = scale;
        yield return Real(holdReal);
        float start = Time.unscaledTime;
        float ramp = holdReal > 0.3f ? 0.35f : 0.08f;
        while (Time.unscaledTime - start < ramp)
        {
            Time.timeScale = Mathf.Lerp(scale, 1f, (Time.unscaledTime - start) / ramp);
            yield return null;
        }
        Time.timeScale = 1f;
        slowRoutine = null;
    }

    static float Squash(float t, float amount)
    {
        if (t <= 0f || t >= 1f) return 0f;
        return amount * Mathf.Sin(t * Mathf.PI * 2f) * (1f - t);
    }

    static Vector3 SquashVec(float a) => new Vector3(1f + a * 0.6f, 1f - a, 1f + a * 0.6f);

    // ぽよんと弾む（0→膨らむ→戻る）
    static float Punch(float t, float amount)
    {
        if (t <= 0f || t >= 1f) return 0f;
        return amount * Mathf.Sin(t * Mathf.PI * 3f) * (1f - t) * (1f - t);
    }

    // 実時間で待つ（スローモーションの影響を受けない）
    static IEnumerator Real(float seconds)
    {
        float end = Time.unscaledTime + seconds;
        while (Time.unscaledTime < end) yield return null;
    }

    // ゲーム内時間で待つ（スローモーション中はゆっくり進む）
    static IEnumerator Sim(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
    }

    static void Emit(ParticleSystem ps, Vector3 pos, Quaternion rot, int count)
    {
        ps.transform.SetPositionAndRotation(pos, rot);
        ps.Emit(count);
    }

    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float m = 1f - t;
        return m * m * m * a + 3f * m * m * t * b + 3f * m * t * t * c + t * t * t * d;
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
