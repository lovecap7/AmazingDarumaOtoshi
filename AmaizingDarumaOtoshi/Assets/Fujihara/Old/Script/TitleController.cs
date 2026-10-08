using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// タイトル画面のだるま落とし演出。
// 1. 文字の積み木が縦に積まれ、一番上にだるまの顔が乗っている
// 2. ハンマーで一番下から1個ずつ叩き出す（だんだん速く）。上の積み木は1段ずつ落ちる
// 3. 叩かれた文字は右へ飛び出し、弧を描いてロゴの位置へ着地する
// 4. 最後の1個は大きめのヒットストップと画面揺れ。顔は地面に落ちる
// 5. 「はじめる」「やめる」の積み木が降ってくる。カーソルはハンマー
// 演出中にボタンでスキップ。ボタン表示後に放置すると演出を最初から再生する（展示用）
//
// ロゴの完成形は logo の子に並べた積み木そのもの。エディタで位置・大きさ・角度・色を変えてよい。
// 子の並び順に叩かれる（＝下から積まれる）。
public class TitleController : MonoBehaviour, IBlockListOwner
{
    [Header("参照")]
    [Tooltip("ロゴの完成形。子の積み木（Image＋子にTMP文字）を並べておく")]
    [SerializeField] RectTransform logo;
    [Tooltip("だるまの顔（積み木の一番上に乗る）")]
    [SerializeField] RectTransform head;
    [Tooltip("ハンマーの根元（持ち手の端）。ここを中心に回転する")]
    [SerializeField] RectTransform hammer;
    [SerializeField] RectTransform hammerHead;
    [SerializeField] ImpactEffect impactEffect;
    [Tooltip("画面揺れで動かす親（背景以外をまとめたもの）")]
    [SerializeField] RectTransform shakeRoot;
    [Tooltip("上から順に：はじめる、やめる")]
    [SerializeField] MenuBlock[] buttons;
    [SerializeField] CanvasGroup skipHint;
    [SerializeField] CanvasGroup readyHint;

    [Header("シーン")]
    [SerializeField] string menuSceneName = "TestMenu";

    [Header("音（任意）")]
    [SerializeField] AudioSource seSource;
    [SerializeField] AudioClip hitSound;
    [SerializeField] AudioClip landSound;
    [SerializeField] AudioClip moveSound;

    [Header("積み上げ")]
    [SerializeField] float stackX = 0f;
    [SerializeField] float groundY = -380f;
    [SerializeField] Vector2 stackBlockSize = new Vector2(150f, 52f);
    [SerializeField] float stackFontSize = 36f;

    [Header("テンポ")]
    [SerializeField] float startDelay = 0.9f;
    [SerializeField] float firstInterval = 0.65f;   // 最初の叩く間隔
    [SerializeField] float lastInterval = 0.2f;     // 最後の叩く間隔
    [SerializeField] float hangTime = 0.05f;        // 叩き出した後、上の積み木が宙に残る時間
    [SerializeField] float dropTime = 0.1f;         // 1段落ちる時間

    [Header("飛び方")]
    [SerializeField] float flyTime = 0.6f;
    [SerializeField] float flyOut = 650f;           // 右へ飛び出す強さ
    [SerializeField] float flyUp = 180f;            // 弧の高さ
    [SerializeField] int spins = 1;                 // 空中での回転数
    [SerializeField] float squash = 0.18f;

    [Header("ハンマー")]
    [SerializeField] float idleAngle = 25f;
    [SerializeField] float idleSway = 4f;
    [SerializeField] float windupAngle = 75f;
    [SerializeField] float strikeAngle = 0f;
    [SerializeField] float windupTime = 0.1f;
    [SerializeField] float swingTime = 0.05f;
    [SerializeField] float recoverTime = 0.25f;

    [Header("打撃の手応え")]
    [SerializeField] float hitStopTime = 0.04f;
    [SerializeField] float finalHitStopTime = 0.18f;
    [SerializeField] float shakeStrength = 14f;
    [SerializeField] float shakeTime = 0.25f;

    [Header("ボタン")]
    [SerializeField] float cursorOffset = 30f;
    [SerializeField] float cursorScale = 1.08f;
    [SerializeField] float buttonDropHeight = 700f;
    [SerializeField] float buttonDropTime = 0.35f;

    [Header("展示用")]
    [Tooltip("ボタン表示後、この秒数操作がないと演出を最初から再生する（0で無効）")]
    [SerializeField] float attractDelay = 30f;

    enum LetterState { Stacked, Flying, Landed }

    class Letter
    {
        public RectTransform rect;
        public TMP_Text label;
        public Vector2 slotPos, slotSize;
        public float slotRot, slotFont;
        public LetterState state;
        public float flyStart, landTime;
        public Vector2 flyFrom;
    }

    Letter[] letters;
    float headRadius;
    Vector2[] buttonSlots;

    float clock;            // ヒットストップ中は止まる演出用の時計
    float hitStop;
    float shake;
    Vector2 shakeBase;

    int knocked;            // 叩き出した数
    float dropStart = -100f;
    float headLandTime = -100f;
    float pulseStart = -100f;

    bool swinging;
    float hammerAngle;
    float recoverStart = -100f;
    Vector3 hammerPos;

    bool introPlaying;
    bool buttonsShown;
    float buttonsStart = -100f;
    bool ready;             // ボタン操作を受け付ける
    bool leaving;           // ボタン決定後
    int cursor;
    int selected = -1;
    float selectedStart;
    float[] offsetX, scale;
    float lastInputClock;
    int skipFrame = -1;

    float H => stackBlockSize.y;

    void Start()
    {
        int n = logo.childCount;
        letters = new Letter[n];
        for (int i = 0; i < n; i++)
        {
            var rect = (RectTransform)logo.GetChild(i);
            var label = rect.GetComponentInChildren<TMP_Text>();
            letters[i] = new Letter
            {
                rect = rect,
                label = label,
                slotPos = rect.anchoredPosition,
                slotSize = rect.sizeDelta,
                slotRot = rect.localEulerAngles.z,
                slotFont = label != null ? label.fontSize : 0f,
            };
        }
        headRadius = head.rect.height * 0.5f;

        buttonSlots = new Vector2[buttons.Length];
        offsetX = new float[buttons.Length];
        scale = new float[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Owner = this;
            buttons[i].Index = i;
            buttonSlots[i] = buttons[i].Rect.anchoredPosition;
        }

        if (shakeRoot != null) shakeBase = shakeRoot.anchoredPosition;
        hammerAngle = idleAngle;

        Restart();
    }

    // ---------------- 流れ ----------------
    void Restart()
    {
        StopAllCoroutines();
        foreach (var l in letters) l.state = LetterState.Stacked;
        knocked = 0;
        dropStart = headLandTime = pulseStart = buttonsStart = -100f;
        buttonsShown = ready = leaving = swinging = false;
        selected = -1;
        cursor = 0;
        hitStop = 0f;
        for (int i = 0; i < buttons.Length; i++) { offsetX[i] = 0f; scale[i] = 1f; }

        hammerPos = HammerTargetForStack();
        if (hammer != null) hammer.localPosition = hammerPos;

        StartCoroutine(Intro());
    }

    IEnumerator Intro()
    {
        introPlaying = true;
        yield return WaitClock(startDelay);

        int n = letters.Length;
        for (int k = 0; k < n; k++)
        {
            int index = k;
            bool last = k == n - 1;
            float t = n > 1 ? k / (float)(n - 1) : 1f;
            float interval = Mathf.Lerp(firstInterval, lastInterval, 1f - (1f - t) * (1f - t));

            yield return Swing(() => Knock(index, last));
            yield return WaitClock(Mathf.Max(0f, interval - windupTime - swingTime));
        }

        // 最後の文字が着地するまで待ってからロゴを弾ませる
        yield return WaitClock(flyTime + 0.25f);
        pulseStart = clock;
        yield return WaitClock(0.45f);

        introPlaying = false;
        ShowButtons();
    }

    void Knock(int index, bool last)
    {
        var l = letters[index];
        l.state = LetterState.Flying;
        l.flyStart = clock;
        l.flyFrom = l.rect.anchoredPosition;

        knocked = index + 1;
        dropStart = clock + hangTime;

        hitStop = last ? finalHitStopTime : hitStopTime;
        shake = last ? 1.6f : 0.6f;
        PlaySe(hitSound);
        if (impactEffect != null) impactEffect.Play(logo.TransformPoint(StackContact()));
    }

    void ShowButtons()
    {
        buttonsShown = true;
        buttonsStart = clock;
        lastInputClock = clock;
    }

    void SkipToEnd()
    {
        StopAllCoroutines();
        introPlaying = false;
        swinging = false;
        recoverStart = -100f;
        hitStop = 0f;
        foreach (var l in letters) { l.state = LetterState.Landed; l.landTime = -100f; }
        knocked = letters.Length;
        dropStart = -100f;
        headLandTime = -100f;
        pulseStart = clock;
        skipFrame = Time.frameCount;
        ShowButtons();
    }

    // ---------------- 入力 ----------------
    void HandleInput()
    {
        bool any = MenuInput.Up() || MenuInput.Down() || MenuInput.Submit() || MenuInput.Cancel() || MenuInput.AnyClick()
                   || (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 0f);
        if (any) lastInputClock = clock;

        if (introPlaying)
        {
            if (MenuInput.Submit() || MenuInput.Cancel() || MenuInput.AnyClick()) SkipToEnd();
            return;
        }

        if (!ready || leaving || Time.frameCount == skipFrame) return;

        if (MenuInput.Up()) MoveCursor(-1);
        else if (MenuInput.Down()) MoveCursor(+1);
        else if (MenuInput.Submit()) Select(cursor);

        if (attractDelay > 0f && clock - lastInputClock > attractDelay) Restart();
    }

    void MoveCursor(int dir)
    {
        int next = ((cursor + dir) % buttons.Length + buttons.Length) % buttons.Length;
        if (next == cursor) return;
        cursor = next;
        PlaySe(moveSound);
    }

    void Select(int index)
    {
        if (!ready || leaving) return;
        cursor = index;
        leaving = true;
        StartCoroutine(SelectRoutine(index));
    }

    IEnumerator SelectRoutine(int index)
    {
        yield return Swing(() =>
        {
            selected = index;
            selectedStart = clock;
            hitStop = hitStopTime * 1.5f;
            shake = 0.8f;
            PlaySe(hitSound);
            if (impactEffect != null) impactEffect.Play(buttons[index].Rect.parent.TransformPoint(ButtonContact(index)));
        });
        yield return WaitClock(0.35f);

        if (index == 0) Fader.Load(menuSceneName);
        else Quit();
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void OnBlockHover(int index)
    {
        if (ready && !leaving && index != cursor) { cursor = index; PlaySe(moveSound); }
    }

    public void OnBlockClick(int index)
    {
        if (ready && Time.frameCount != skipFrame) Select(index);
    }

    // ---------------- 更新 ----------------
    void Update()
    {
        float raw = Time.unscaledDeltaTime;
        float dt = raw;
        if (hitStop > 0f) { hitStop -= raw; dt = 0f; }
        clock += dt;

        HandleInput();
        UpdateShake(raw);
        UpdateLetters();
        UpdateHead();
        UpdateButtons(dt);
        UpdateHammer(dt);

        if (skipHint != null) skipHint.alpha = Mathf.MoveTowards(skipHint.alpha, introPlaying ? 0.7f : 0f, raw * 4f);
        if (readyHint != null) readyHint.alpha = Mathf.MoveTowards(readyHint.alpha, ready && !leaving ? 1f : 0f, raw * 4f);
    }

    // 積み木の山が落ちていく量（叩き出すたびに1段ずつ、重力で加速しながら）
    float StackOffset()
    {
        if (knocked == 0) return 0f;
        float d = Mathf.Clamp01((clock - dropStart) / dropTime);
        return -H * (knocked - 1 + d * d);
    }

    void UpdateLetters()
    {
        float offset = StackOffset();
        float stackSquash = Squash((clock - dropStart - dropTime) / 0.25f);

        for (int i = 0; i < letters.Length; i++)
        {
            var l = letters[i];
            Vector2 pos, size;
            float rot, font;
            Vector2 s = Vector2.one;

            switch (l.state)
            {
                case LetterState.Stacked:
                    pos = new Vector2(stackX, groundY + H * 0.5f + i * H + offset);
                    size = stackBlockSize;
                    rot = 0f;
                    font = stackFontSize;
                    s = ApplySquash(s, stackSquash);
                    break;

                case LetterState.Flying:
                {
                    float t = Mathf.Clamp01((clock - l.flyStart) / flyTime);
                    float u = 1f - (1f - t) * (1f - t);   // 叩かれた直後が一番速い
                    Vector2 ctrl = new Vector2(l.flyFrom.x + flyOut, Mathf.Max(l.flyFrom.y, l.slotPos.y) + flyUp);
                    pos = Bezier(l.flyFrom, ctrl, l.slotPos, u);
                    size = Vector2.Lerp(stackBlockSize, l.slotSize, u);
                    rot = Mathf.Lerp(0f, l.slotRot - 360f * spins, u);
                    font = Mathf.Lerp(stackFontSize, l.slotFont, u);

                    if (t >= 1f)
                    {
                        l.state = LetterState.Landed;
                        l.landTime = clock;
                        shake = Mathf.Max(shake, 0.3f);
                        PlaySe(landSound);
                    }
                    break;
                }

                default:
                    pos = l.slotPos;
                    size = l.slotSize;
                    rot = l.slotRot;
                    font = l.slotFont;
                    s = ApplySquash(s, Squash((clock - l.landTime) / 0.3f));
                    // 完成したロゴが左から順に弾む
                    float p = (clock - pulseStart - i * 0.05f) / 0.3f;
                    if (p > 0f && p < 1f) s *= 1f + 0.15f * Mathf.Sin(p * Mathf.PI);
                    break;
            }

            l.rect.anchoredPosition = pos;
            l.rect.sizeDelta = size;
            l.rect.localRotation = Quaternion.Euler(0f, 0f, rot);
            l.rect.localScale = new Vector3(s.x, s.y, 1f);
            if (l.label != null && !Mathf.Approximately(l.label.fontSize, font)) l.label.fontSize = font;
        }
    }

    void UpdateHead()
    {
        float y = groundY + letters.Length * H + headRadius + StackOffset();
        Vector2 s = Vector2.one;

        // 最後の1個が抜けて地面に着いたら大きめにつぶれる
        bool onGround = knocked >= letters.Length && clock - dropStart >= dropTime;
        if (onGround && headLandTime < -50f && dropStart > -50f)
        {
            headLandTime = clock;
            shake = Mathf.Max(shake, 0.8f);
            PlaySe(landSound);
        }
        float squashT = (clock - headLandTime) / 0.45f;
        if (squashT > 0f && squashT < 1f) s = ApplySquash(s, 1.8f * Squash(squashT));
        else if (!onGround) s = ApplySquash(s, Squash((clock - dropStart - dropTime) / 0.25f));

        head.anchoredPosition = new Vector2(stackX, y);
        head.localScale = new Vector3(s.x, s.y, 1f);
    }

    void UpdateButtons(float dt)
    {
        float follow = 1f - Mathf.Exp(-16f * dt);

        for (int i = 0; i < buttons.Length; i++)
        {
            var rect = buttons[i].Rect;
            if (rect.gameObject.activeSelf != buttonsShown) rect.gameObject.SetActive(buttonsShown);

            float t = buttonsShown ? (clock - buttonsStart - i * 0.12f) / buttonDropTime : 0f;
            float d = Mathf.Clamp01(t);
            Vector2 pos = buttonSlots[i] + Vector2.up * buttonDropHeight * (1f - d * d);
            Vector2 s = Vector2.one;
            float rot = 0f;

            if (t >= 1f) s = ApplySquash(s, Squash((t - 1f) * buttonDropTime / 0.3f));

            bool isCursor = ready && i == cursor && selected < 0;
            offsetX[i] = Mathf.Lerp(offsetX[i], isCursor ? cursorOffset : 0f, follow);
            scale[i] = Mathf.Lerp(scale[i], isCursor ? cursorScale : 1f, follow);
            pos.x += offsetX[i];
            s *= scale[i];

            // 決定されたボタンは右へ吹き飛ぶ
            if (i == selected)
            {
                float f = (clock - selectedStart) / 0.5f;
                pos.x += 2000f * f * f + 600f * f;
                rot = -540f * f;
            }

            rect.anchoredPosition = pos;
            rect.localScale = new Vector3(s.x, s.y, 1f);
            rect.localRotation = Quaternion.Euler(0f, 0f, rot);
        }

        if (buttonsShown && !ready && clock - buttonsStart > buttonDropTime + 0.12f * buttons.Length)
        {
            ready = true;
            lastInputClock = clock;
        }
    }

    void UpdateHammer(float dt)
    {
        if (hammer == null) return;

        float sway = Mathf.Sin(Time.unscaledTime * 3f) * idleSway;
        if (!swinging)
        {
            float t = Mathf.Clamp01((clock - recoverStart) / recoverTime);
            hammerAngle = Mathf.LerpUnclamped(strikeAngle, idleAngle + sway, EaseOutBack(t));
        }

        // 演出中は山の一番下、ボタン表示後は選択中のボタンを狙う
        Vector3 target = buttonsShown ? HammerTargetForButton(cursor) : HammerTargetForStack();
        if (!swinging && !leaving)
            hammerPos = Vector3.Lerp(hammerPos, target, 1f - Mathf.Exp(-(buttonsShown ? 10f : 30f) * dt));

        hammer.localPosition = hammerPos;
        hammer.localRotation = Quaternion.Euler(0f, 0f, hammerAngle);
    }

    void UpdateShake(float dt)
    {
        if (shakeRoot == null) return;
        shake = Mathf.Max(0f, shake - dt / Mathf.Max(0.01f, shakeTime));
        shakeRoot.anchoredPosition = shakeBase + UnityEngine.Random.insideUnitCircle * shakeStrength * shake * shake;
    }

    // ---------------- ハンマー ----------------
    IEnumerator Swing(Action onImpact)
    {
        swinging = true;
        float from = hammerAngle;

        float s = clock;
        while (clock - s < windupTime)
        {
            float t = (clock - s) / windupTime;
            hammerAngle = Mathf.Lerp(from, windupAngle, 1f - (1f - t) * (1f - t));
            yield return null;
        }
        s = clock;
        while (clock - s < swingTime)
        {
            float t = (clock - s) / swingTime;
            hammerAngle = Mathf.Lerp(windupAngle, strikeAngle, t * t);
            yield return null;
        }

        hammerAngle = strikeAngle;
        swinging = false;
        recoverStart = clock;
        onImpact();
    }

    // 山の一番下の積み木の左面（logo のローカル座標）
    Vector3 StackContact() => new Vector3(stackX - stackBlockSize.x * 0.5f, groundY + H * 0.5f, 0f);

    Vector3 ButtonContact(int index)
    {
        var rect = buttons[index].Rect;
        float w = rect.rect.width * cursorScale;
        return new Vector3(buttonSlots[index].x + cursorOffset - w * 0.5f, buttonSlots[index].y, 0f);
    }

    Vector3 HammerTargetForStack() => ToHammerSpace(logo.TransformPoint(StackContact()));
    Vector3 HammerTargetForButton(int index) => ToHammerSpace(buttons[index].Rect.parent.TransformPoint(ButtonContact(index)));

    // 角度0のとき、ハンマーの頭の右面がちょうど contact に当たる根元の位置
    Vector3 ToHammerSpace(Vector3 contactWorld)
    {
        if (hammer == null) return Vector3.zero;
        Vector3 contact = hammer.parent.InverseTransformPoint(contactWorld);
        if (hammerHead == null) return contact;
        return contact + new Vector3(-hammerHead.rect.width * 0.5f, -hammerHead.localPosition.y, 0f);
    }

    // ---------------- 補助 ----------------
    IEnumerator WaitClock(float seconds)
    {
        float end = clock + seconds;
        while (clock < end) yield return null;
    }

    void PlaySe(AudioClip clip)
    {
        if (seSource != null && clip != null) seSource.PlayOneShot(clip, GameSettings.SeVolume);
    }

    float Squash(float t)
    {
        if (t <= 0f || t >= 1f) return 0f;
        return squash * Mathf.Sin(t * Mathf.PI * 2f) * (1f - t);
    }

    static Vector2 ApplySquash(Vector2 s, float amount) => new Vector2(s.x * (1f + amount * 0.7f), s.y * (1f - amount));

    static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        float m = 1f - t;
        return m * m * a + 2f * m * t * b + t * t * c;
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
