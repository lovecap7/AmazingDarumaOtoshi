using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

// だるま落とし風のメニュー。
// ・左に縦並びの積み木（メニュー項目）。ハンマーがカーソルとして選択中の積み木の横で構える
// ・決定するとハンマーを振りかぶって叩く（ヒットストップ・画面揺れ・衝撃エフェクト）
// ・パネルを持つ項目（サウンド/ビデオ）は積み木が右へ飛んでパネルに着地し、上の積み木が落ちてくる
//   パネルが明るくなって操作できるようになり、左の積み木は暗くなる。キャンセルで元に戻る
// ・パネルを持たない項目（対戦/クレジット/ゲーム終了）は積み木がぐらつき、onSubmit を呼ぶ
public class MenuController : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public MenuBlock block;

        [Tooltip("右側に出す設定パネル。不要なら空のまま")]
        public MenuPanel panel;

        [Tooltip("ハンマーが当たった瞬間の処理（シーン遷移・クレジット再生など）")]
        public UnityEvent onSubmit;
    }

    enum HammerPhase { Idle, Windup, Swing, Recover }

    [Header("項目（上から順に登録）")]
    [SerializeField] Entry[] entries;

    [Tooltip("再生中はメニューの入力を止める")]
    [SerializeField] CreditRoll credits;

    [Header("参照")]
    [Tooltip("ハンマーの根元（持ち手の端）。ここを中心に回転する")]
    [SerializeField] RectTransform hammer;
    [Tooltip("ハンマーの頭。叩く位置の計算に使う")]
    [SerializeField] RectTransform hammerHead;
    [SerializeField] ImpactEffect impactEffect;
    [Tooltip("画面揺れで動かす親（背景以外をまとめたもの）")]
    [SerializeField] RectTransform shakeRoot;

    [Header("音（任意）")]
    [SerializeField] AudioSource seSource;
    [SerializeField] AudioClip moveSound;
    [SerializeField] AudioClip hitSound;
    [SerializeField] AudioClip landSound;

    [Header("カーソル")]
    [SerializeField] float cursorOffset = 40f;      // 選択中の積み木が右へずれる量
    [SerializeField] float cursorScale = 1.08f;
    [SerializeField] float sharpness = 16f;         // 大きいほど素早く追従

    [Header("ハンマー")]
    [SerializeField] float idleAngle = 25f;         // 構え（+で後ろへ傾く）
    [SerializeField] float idleSway = 4f;           // 構え中のゆらゆら
    [SerializeField] float windupAngle = 75f;       // 振りかぶり
    [SerializeField] float strikeAngle = 0f;        // 当たる角度
    [SerializeField] float windupTime = 0.12f;
    [SerializeField] float swingTime = 0.06f;
    [SerializeField] float recoverTime = 0.3f;

    [Header("打撃の手応え")]
    [SerializeField] float hitStopTime = 0.07f;     // 当たった瞬間に止まる時間
    [SerializeField] float shakeStrength = 18f;
    [SerializeField] float shakeTime = 0.25f;
    [SerializeField] float wobbleAmount = 28f;      // パネル無し項目を叩いたときのぐらつき

    [Header("プレビュー")]
    [SerializeField] float previewFadeTime = 0.15f;
    [SerializeField, Range(0f, 1f)] float previewDarkness = 0.6f;

    [Header("だるま落とし演出")]
    [SerializeField] float knockDuration = 0.75f;   // 叩き出し〜着地の揺れが収まるまで
    [SerializeField] float dockScale = 0.85f;       // パネルに着地したときの大きさ
    [SerializeField] float arcHeight = 50f;
    [SerializeField] float spinDegrees = -360f;     // 飛んでいる間の回転
    [SerializeField] float squash = 0.14f;          // 着地でつぶれる量
    [SerializeField, Range(0f, 1f)] float listDarkness = 0.6f;

    // 演出の区切り（knockT に対する割合）
    const float FlyEnd = 0.45f;     // 積み木がパネルに着く
    const float DropStart = 0.08f;  // 上の積み木が落ち始める（一瞬宙に浮く）
    const float DropEnd = 0.32f;    // 上の積み木が着地する

    int count;
    int cursor;
    int target = -1;        // ハンマーで叩こうとしている項目
    int active = -1;        // 叩き出されている項目
    bool confirmed;
    bool pendingFocus;
    bool landedDock, landedDrop;
    float knockT;           // 0=並んでいる 1=演出が終わった

    HammerPhase phase = HammerPhase.Idle;
    float phaseT;
    float hitStop;
    float shake;
    float hammerAngle;
    Vector3 hammerPos;
    CanvasGroup hammerGroup;
    Vector2 shakeBase;

    int wobbleIndex = -1;
    float wobbleT = 1f;

    Vector3[] slots;        // 並んでいるときの位置
    float[] offsetX, scale, preview;

    bool Locked => confirmed || knockT > 0f || phase == HammerPhase.Windup || phase == HammerPhase.Swing;

    void Start()
    {
        count = entries.Length;
        slots = new Vector3[count];
        offsetX = new float[count];
        scale = new float[count];
        preview = new float[count];

        for (int i = 0; i < count; i++)
        {
            var b = entries[i].block;
            b.Owner = this;
            b.Index = i;
            slots[i] = b.Rect.localPosition;
            scale[i] = 1f;
        }

        if (hammer != null)
        {
            hammerGroup = hammer.GetComponent<CanvasGroup>();
            hammerAngle = idleAngle;
            hammerPos = HammerTarget(cursor);
            hammer.localPosition = hammerPos;
        }
        if (shakeRoot != null) shakeBase = shakeRoot.anchoredPosition;
    }

    // ---------------- 入力 ----------------
    void HandleInput()
    {
        if (credits != null && credits.IsBusy) return;

        if (!Locked)
        {
            if (MenuInput.Up()) MoveCursor(-1);
            else if (MenuInput.Down()) MoveCursor(+1);
            else if (MenuInput.Submit()) Confirm();
        }
        else if (confirmed && knockT >= 1f && MenuInput.Cancel())
        {
            Cancel();
        }
    }

    public void MoveCursor(int dir)
    {
        if (Locked) return;
        SetCursor(((cursor + dir) % count + count) % count);
    }

    void SetCursor(int index)
    {
        if (index == cursor) return;
        cursor = index;
        PlaySe(moveSound);
    }

    // 決定：ハンマーを振りかぶる。実際の処理は当たった瞬間（Impact）に行う
    public void Confirm()
    {
        if (Locked) return;
        target = cursor;
        phase = HammerPhase.Windup;
        phaseT = 0f;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    void Impact()
    {
        var entry = entries[target];

        hitStop = hitStopTime;
        shake = 1f;
        PlaySe(hitSound);
        if (impactEffect != null) impactEffect.Play(ContactPoint(target));

        entry.onSubmit?.Invoke();

        if (entry.panel == null)
        {
            wobbleIndex = target;
            wobbleT = 0f;
            return;
        }

        confirmed = true;
        active = target;
        pendingFocus = true;
        landedDock = landedDrop = false;
        entry.block.transform.SetAsLastSibling();   // 飛んでいる積み木を一番手前に
    }

    public void Cancel()
    {
        if (!confirmed) return;
        confirmed = false;
        pendingFocus = false;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        GameSettings.Save();
    }

    public void OnBlockHover(int index)
    {
        if (!Locked) SetCursor(index);
    }

    public void OnBlockClick(int index)
    {
        if (credits != null && credits.IsBusy) return;

        if (confirmed)
        {
            if (knockT < 1f) return;
            Cancel();
            cursor = index;
        }
        else if (!Locked)
        {
            SetCursor(index);
            Confirm();
        }
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------------- 更新 ----------------
    void Update()
    {
        HandleInput();

        float rawDt = Time.unscaledDeltaTime;

        // ヒットストップ中は演出の時間を止める（画面揺れとエフェクトだけ動く）
        float dt = rawDt;
        if (hitStop > 0f)
        {
            hitStop -= rawDt;
            dt = 0f;
        }

        UpdateHammer(dt);
        UpdateShake(rawDt);

        float follow = 1f - Mathf.Exp(-sharpness * dt);
        knockT = Mathf.MoveTowards(knockT, confirmed ? 1f : 0f, dt / Mathf.Max(0.01f, knockDuration));
        if (knockT <= 0f && !confirmed) active = -1;
        wobbleT = Mathf.Min(1f, wobbleT + dt / 0.4f);

        // 叩き出し（行き）と戻し（キャンセル）で動きを変える
        float fly, drop, dim = Mathf.SmoothStep(0f, 1f, knockT);
        float dockSquash = 0f, dropSquash = 0f;
        if (confirmed)
        {
            fly = Mathf.Clamp01(knockT / FlyEnd);
            float d = Mathf.Clamp01((knockT - DropStart) / (DropEnd - DropStart));
            drop = d * d;   // 重力で加速しながら落ちる
            dockSquash = Squash((knockT - FlyEnd) / 0.3f);
            dropSquash = Squash((knockT - DropEnd) / 0.3f);

            if (!landedDock && knockT >= FlyEnd) { landedDock = true; entries[active].panel.Punch(); PlaySe(landSound); }
            if (!landedDrop && knockT >= DropEnd && active > 0) { landedDrop = true; shake = Mathf.Max(shake, 0.35f); PlaySe(landSound); }
        }
        else
        {
            fly = Mathf.SmoothStep(0f, 1f, knockT);
            drop = Mathf.SmoothStep(0f, 1f, knockT);
        }

        for (int i = 0; i < count; i++)
        {
            var entry = entries[i];
            var rect = entry.block.Rect;
            bool isCursor = i == cursor && !confirmed && knockT <= 0f;

            offsetX[i] = Mathf.Lerp(offsetX[i], isCursor ? cursorOffset : 0f, follow);
            scale[i] = Mathf.Lerp(scale[i], isCursor ? cursorScale : 1f, follow);

            // 叩き出された積み木より上にあるものは、1段下へ落ちる
            Vector3 basePos = slots[i];
            bool above = active >= 0 && i < active;
            if (above) basePos = Vector3.LerpUnclamped(slots[i], slots[i + 1], drop);

            Vector3 pos = basePos + Vector3.right * offsetX[i];
            Vector2 s = Vector2.one * scale[i];
            float rot = 0f;

            if (above) s = ApplySquash(s, dropSquash);

            if (i == active)
            {
                Vector3 dockPos = rect.parent.InverseTransformPoint(entry.panel.Dock.position);
                float fx = confirmed ? 1f - Mathf.Pow(1f - fly, 4f) : fly;       // 横へは一気に
                float fy = Mathf.SmoothStep(0f, 1f, fly);
                pos.x = Mathf.Lerp(pos.x, dockPos.x, fx);
                pos.y = Mathf.Lerp(pos.y, dockPos.y, fy) + Mathf.Sin(fly * Mathf.PI) * arcHeight;
                rot = spinDegrees * (confirmed ? 1f - Mathf.Pow(1f - fly, 3f) : fly);
                s = ApplySquash(Vector2.one * Mathf.Lerp(scale[i], dockScale, fly), dockSquash);
            }
            else if (i == target && phase == HammerPhase.Swing)
            {
                // 当たる直前、わずかに身構える
                s.x *= 1f - 0.05f * Mathf.Clamp01(phaseT / swingTime);
            }
            else if (i == target && hitStop > 0f)
            {
                s.x *= 0.88f;   // 当たった瞬間につぶれる
            }

            if (i == wobbleIndex && wobbleT < 1f)
            {
                float w = Mathf.Sin(wobbleT * Mathf.PI * 4f) * (1f - wobbleT);
                pos.x += wobbleAmount * w;
                rot += -6f * w;
            }

            rect.localPosition = pos;
            rect.localRotation = Quaternion.Euler(0f, 0f, rot);
            rect.localScale = new Vector3(s.x, s.y, 1f);
            entry.block.SetDim(i == active ? 0f : listDarkness * dim);

            // 右側のプレビュー
            if (entry.panel != null)
            {
                int shown = active >= 0 ? active : (phase != HammerPhase.Idle && target >= 0 ? target : cursor);
                preview[i] = Mathf.MoveTowards(preview[i], i == shown ? 1f : 0f, dt / Mathf.Max(0.01f, previewFadeTime));

                float darkness = previewDarkness * (1f - (i == active ? dim : 0f));
                bool interactable = i == active && confirmed && knockT >= 1f;
                entry.panel.SetState(preview[i], darkness, interactable);
            }
        }

        // パネル操作中はハンマーを隠す
        if (hammerGroup != null) hammerGroup.alpha = 1f - dim;

        // 演出が終わってからパネル内のUIを選択する
        if (pendingFocus && confirmed && knockT >= 1f)
        {
            pendingFocus = false;
            var first = entries[active].panel.FirstSelected;
            if (first != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(first.gameObject);
        }
    }

    void UpdateHammer(float dt)
    {
        if (hammer == null)
        {
            // ハンマー未設定なら即座に当たったことにする
            if (phase == HammerPhase.Windup) { Impact(); phase = HammerPhase.Idle; }
            return;
        }

        phaseT += dt;
        float sway = Mathf.Sin(Time.unscaledTime * 3f) * idleSway;

        switch (phase)
        {
            case HammerPhase.Idle:
                hammerAngle = Mathf.Lerp(hammerAngle, idleAngle + sway, 1f - Mathf.Exp(-12f * dt));
                break;

            case HammerPhase.Windup:
            {
                float t = Mathf.Clamp01(phaseT / windupTime);
                hammerAngle = Mathf.Lerp(idleAngle, windupAngle, 1f - (1f - t) * (1f - t));
                if (t >= 1f) { phase = HammerPhase.Swing; phaseT = 0f; }
                break;
            }

            case HammerPhase.Swing:
            {
                float t = Mathf.Clamp01(phaseT / swingTime);
                hammerAngle = Mathf.Lerp(windupAngle, strikeAngle, t * t);
                if (t >= 1f) { phase = HammerPhase.Recover; phaseT = 0f; Impact(); }
                break;
            }

            case HammerPhase.Recover:
            {
                float t = Mathf.Clamp01(phaseT / recoverTime);
                hammerAngle = Mathf.LerpUnclamped(strikeAngle, idleAngle + sway, EaseOutBack(t));
                if (t >= 1f) phase = HammerPhase.Idle;
                break;
            }
        }

        // 叩いている間は狙った積み木の位置に固定、それ以外はカーソルへ追従
        int follow = phase == HammerPhase.Idle ? cursor : target;
        if (!confirmed && knockT <= 0f)
            hammerPos = Vector3.Lerp(hammerPos, HammerTarget(follow), phase == HammerPhase.Idle ? 1f - Mathf.Exp(-sharpness * dt) : 1f);

        hammer.localPosition = hammerPos;
        hammer.localRotation = Quaternion.Euler(0f, 0f, hammerAngle);
    }

    void UpdateShake(float dt)
    {
        if (shakeRoot == null) return;
        shake = Mathf.Max(0f, shake - dt / Mathf.Max(0.01f, shakeTime));
        Vector2 offset = UnityEngine.Random.insideUnitCircle * shakeStrength * shake * shake;
        shakeRoot.anchoredPosition = shakeBase + offset;
    }

    // 積み木の左側の面（ハンマーが当たる位置）。ワールド座標
    Vector3 ContactPoint(int index)
    {
        var rect = entries[index].block.Rect;
        float w = rect.rect.width * cursorScale;
        Vector3 local = slots[index] + Vector3.right * (cursorOffset - w * 0.5f);
        return rect.parent.TransformPoint(local);
    }

    // 角度0のとき、ハンマーの頭の右面がちょうど積み木の左面に当たる根元の位置（hammer の親のローカル座標）
    Vector3 HammerTarget(int index)
    {
        Vector3 contact = hammer.parent.InverseTransformPoint(ContactPoint(index));
        if (hammerHead == null) return contact;

        float reach = hammerHead.localPosition.y;
        float halfHead = hammerHead.rect.width * 0.5f;
        return contact + new Vector3(-halfHead, -reach, 0f);
    }

    void PlaySe(AudioClip clip)
    {
        if (seSource != null && clip != null) seSource.PlayOneShot(clip, GameSettings.SeVolume);
    }

    // 着地でつぶれて、ぽよんと戻る
    float Squash(float t)
    {
        if (t <= 0f || t >= 1f) return 0f;
        return squash * Mathf.Sin(t * Mathf.PI * 2f) * (1f - t);
    }

    static Vector2 ApplySquash(Vector2 s, float amount) => new Vector2(s.x * (1f + amount * 0.7f), s.y * (1f - amount));

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
