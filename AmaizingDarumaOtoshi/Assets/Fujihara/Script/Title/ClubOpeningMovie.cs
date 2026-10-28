using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// 【試作】部室タイトル（TitleClubRoomScene）のオープニングムービー。
// 野球ゲームのアニメ OP のように、部活の青春をカットの積み重ねで見せてからタイトル画面へ入る。
//  1. ノート「目指せ！全国制覇！！」         5. 新聞「だるま落とし部 快進撃！」
//  2. 部室：ホワイトボードの前に集まる部員     6. 全国大会：ライバルとのにらみ合い・打ち合い
//  3. 練習：素振り・走り込み・だるま落とし     7. 思い出の写真（それまでのカットを撮った写真）
//  4. 神社：絵馬に「全国制覇」               8. 夕焼けの帰り道
//                                            9. 決勝の一撃 → ホワイトアウト → タイトル画面（完成形）
// ムービーは専用のカメラで RenderTexture に撮って、いちばん手前の画面に貼る（TitleScreen の画面より上に出る）。
// 部室のカット以外は、部室から遠く離れた場所に作ったセットで撮る。A / クリックで飛ばせる。
[DefaultExecutionOrder(-100)]   // TitleScreen.Start より前に Hold する
public partial class ClubOpeningMovie : MonoBehaviour
{
    [SerializeField] bool playMovie = true;

    [Header("カメラの揺れ")]
    [Tooltip("ムービー全体の揺れの強さ（当たったとき・ぶつかったとき・ラッシュ中、すべてにかかる倍率）。0 で揺れなし")]
    [SerializeField, Range(0f, 3f)] float shakeStrength = 1f;
    [Tooltip("決勝のラッシュ中に揺れ続ける強さ（当たった瞬間はこれより強く揺れる）")]
    [SerializeField, Range(0f, 1.5f)] float rushShake = 0.45f;

    static readonly Color Red = new Color(0.93f, 0.27f, 0.25f), Blue = new Color(0.22f, 0.55f, 0.95f),
                          Yellow = new Color(0.98f, 0.76f, 0.12f), Green = new Color(0.30f, 0.74f, 0.35f),
                          Shirt = new Color(0.95f, 0.95f, 0.92f),
                          ChampBody = new Color(0.12f, 0.12f, 0.15f), ChampFace = new Color(0.78f, 0.8f, 0.86f);

    // セットの場所（部室から遠く離す）
    static readonly Vector3 NoteSet = new Vector3(1000f, 0f, 0f), YardSet = new Vector3(2000f, 0f, 0f), ShrineSet = new Vector3(3000f, 0f, 0f),
                            PaperSet = new Vector3(4000f, 0f, 0f), ArenaSet = new Vector3(5000f, 0f, 0f), PhotoSet = new Vector3(6000f, 0f, 0f),
                            MatchSet = new Vector3(7000f, 0f, 0f);

    TitleScreen title;
    TMP_FontAsset font;
    Camera movieCam, mainCam;
    RenderTexture rt;
    GameObject ui;
    Image flash;
    Transform setRoot;
    float mt;                  // ムービーの時計（スローの影響を受ける）
    bool finishing;
    Light keyLight;
    DepthOfField dof;
    bool dofWasActive;
    Color keyColor;
    float keyIntensity;

    // カメラ
    System.Func<float, Vector3> camPos, camLook;
    float shotStart, camShake;

    // 写真（それまでのカットを撮っておく）
    readonly List<RenderTexture> snaps = new List<RenderTexture>();

    // 飛んでいく積み木
    class Flyer { public Transform t; public Vector3 v, spin; public float life; }
    readonly List<Flyer> flyers = new List<Flyer>();

    void Awake()
    {
        if (!playMovie) { enabled = false; return; }
        title = FindAnyObjectByType<TitleScreen>();
        if (title != null) title.Hold();
    }

    IEnumerator Start()
    {
        // TitleScreen・部室の飾りが Start でカメラや画面効果・部屋を作り終えるのを1フレーム待つ（その間は TitleScreen の黒い幕で隠れている）
        yield return null;
        font = TitleDressingKit.TitleFont();
        mainCam = Camera.main;
        setRoot = new GameObject("OpeningMovie").transform;
        keyLight = TitleDressingKit.FindLight("KeyLight");
        if (keyLight != null) { keyColor = keyLight.color; keyIntensity = keyLight.intensity; }

        // TitleScreen は被写界深度（ボケ）を自分のカメラに合わせて動かすので、ムービーの間は切る
        var profile = TitleDressingKit.PostProfile();
        if (profile != null && profile.TryGet<DepthOfField>(out var d)) { dof = d; dofWasActive = d.active; d.active = false; }

        BuildCameraAndScreen();
        BuildNotebook();
        BuildYard();
        BuildShrine();
        BuildNewspaper();
        BuildMemoryUI();
        BuildArena();
        BuildMatchSet();   // ClubOpeningMovie.Match.cs
        BuildTown();
        StartCoroutine(Script());
    }

    // ================= 台本 =================
    IEnumerator Script()
    {
        // ---- 1. ノート ----
        movieCam.backgroundColor = new Color(0.2f, 0.15f, 0.1f);
        // 「目指せ！全国制覇！！」から右のページへ流す
        SetShot(lt => NoteSet + new Vector3(-2.4f + lt * 0.55f, 3.4f + lt * 0.1f, -2.2f), lt => NoteSet + new Vector3(-2.0f + lt * 0.55f, 0f, 0.5f), 44f);
        StartCoroutine(FadeFlash(Color.black, 1f, 0f, 1.0f));
        yield return At(1.8f);
        yield return At(4.4f);

        // ---- 2. 部室：ホワイトボードの前に集まる ----
        Flash();
        var members = new List<Member>();
        var colors = new[] { (Red, Shirt), (Blue, Shirt), (Yellow, Shirt), (Green, Shirt) };
        for (int i = 0; i < 4; i++)
            members.Add(CreateMember(setRoot, new Vector3(-22f + i * 3.4f, -5f, 20.5f), 0f, colors[i].Item1, colors[i].Item2, 3.2f, 2));
        var captain = members[1];
        // ロッカーにさえぎられないよう、右手前から見る
        SetShot(lt => new Vector3(-8.5f - lt * 0.4f, 2.6f, 5f + lt * 0.8f), lt => new Vector3(-17.5f, 2f, 21f), 50f);
        yield return At(5.6f);
        StartCoroutine(Turn(captain, 180f, 0.35f));
        yield return At(6.1f);
        StartCoroutine(RaiseHammer(captain, 0.4f));
        for (int i = 0; i < 4; i++)
            if (members[i] != captain) StartCoroutine(TurnAndHop(members[i], 6.3f + i * 0.12f));
        yield return At(7.6f);
        Snap();
        yield return At(8.8f);

        // ---- 3. 練習 ----
        Flash();
        movieCam.backgroundColor = new Color(0.5f, 0.75f, 1f);
        // 素振り（下から見上げる）
        var swinger = CreateMember(setRoot, YardSet, 200f, Red, Shirt, 1.2f, 2);
        StartCoroutine(SwingLoop(swinger, 8.8f, 11.2f, 0.75f));
        SetShot(lt => YardSet + new Vector3(2.6f - lt * 0.3f, 0.5f, -3.6f), lt => YardSet + new Vector3(0f, 1.5f, 0f), 46f);
        yield return At(10.3f);
        Snap();
        yield return At(11.2f);
        // 走り込み（横から追う）
        var runner = CreateMember(setRoot, YardSet + new Vector3(-12f, 0f, 8f), 90f, Green, Shirt, 1.2f, 2);
        runner.pivot.gameObject.SetActive(false);
        StartCoroutine(Run(runner, YardSet + new Vector3(-12f, 0f, 8f), YardSet + new Vector3(12f, 0f, 8f), 2.2f));
        SetShot(lt => runner.root.position + new Vector3(0.8f, 1.4f, -4.2f), lt => runner.root.position + new Vector3(0.6f, 1.4f, 0f), 44f);
        yield return At(12.2f);
        Snap();
        yield return At(13.2f);
        // だるま落としの練習：一番下を叩き出す（スロー）
        var tower = CreateTower(YardSet + new Vector3(8f, 0f, -6f), 5);
        var hitter = CreateMember(setRoot, YardSet + new Vector3(6.5f, 0f, -6f), 90f, Yellow, Shirt, 1.2f, 2);
        SetShot(lt => YardSet + new Vector3(8.0f - lt * 0.1f, 1.3f, -11.8f + lt * 0.4f), lt => YardSet + new Vector3(7.5f, 1.6f, -6f), 44f);
        yield return At(13.8f);
        yield return Swing(hitter, 0.25f, 0.12f, () =>
        {
            KnockOut(tower, 0, new Vector3(1f, 0.15f, -0.6f));
            StartCoroutine(SlowMo(0.15f, 0.9f));
        });
        yield return At(15.6f);

        // ---- 4. 神社：絵馬 ----
        Flash();
        movieCam.backgroundColor = new Color(0.7f, 0.85f, 1f);
        var pray = new List<Member>();
        for (int i = 0; i < 4; i++)
            pray.Add(CreateMember(setRoot, ShrineSet + new Vector3(-2.4f + i * 1.6f, 0f, -1.2f), 0f, colors[i].Item1, colors[i].Item2, 1f, 2));
        // 後ろの少し高いところから、並んで拝む4人と絵馬・鳥居を見る
        SetShot(lt => ShrineSet + new Vector3(0.3f, 3.6f, -7.5f + lt * 0.7f), lt => ShrineSet + new Vector3(0f, 2.2f, 3f), 44f);
        yield return At(16.4f);
        foreach (var m in pray) StartCoroutine(Bow(m));
        yield return At(17.6f);
        // 絵馬に寄る
        SetShot(lt => ShrineSet + new Vector3(0.2f - lt * 0.15f, 2.05f, 0.3f + lt * 0.25f), lt => ShrineSet + new Vector3(0f, 2.05f, 2.2f), 36f);
        yield return At(18.4f);
        Snap();
        yield return At(19.4f);

        // ---- 5. 地区大会の試合（実際のゲーム画面ふう）→ 映像の上に新聞が重なる ----
        yield return MatchScene();
        shift = mt - 22.6f;   // ここから先の台本の時刻は、試合のぶんだけ後ろへずれる

        // ---- 6. 全国大会の決勝：試合場の上で王者と一騎打ち（ClubOpeningMovie.Final.cs）----
        yield return FinalFight();
        shift = mt - 29.2f;
        GPuppet hero = fHero, champ = fChamp;

        // ---- 7. 王者が最後の1個に：託されたハンマーを振りかぶる ----
        Flash();
        champ.tremble = true;
        var mid = (hero.Pos + champ.Pos) * 0.5f;
        var side = FrontSide(hero, champ);
        SetShot(lt => mid + side * (3.4f - lt * 0.3f) + Vector3.up * 1.1f, lt => mid + Vector3.up * 0.9f, 46f);
        var back = Quaternion.Euler(-60f, 130f, 0f);
        yield return Tween(0.7f, u => hero.pivotRot = Quaternion.Slerp(GHammerIdle, back, Mathf.SmoothStep(0f, 1f, u)));
        // 託されたハンマーに寄る（紫の頭に金の帯）
        var headT = FindPart(hero.pivot, "HammerHead");
        SetShot(lt => headT.position + side * (1.4f - lt * 0.2f) - hero.root.forward * 0.5f + Vector3.up * 0.25f, lt => headT.position, 38f);
        yield return Tween(1.1f, u => hero.pivotRot = back * Quaternion.Euler(0f, Mathf.Sin(u * Mathf.PI) * 6f, 0f));
        yield return At(31.0f);

        // ---- 8. 振りかぶったまま、思い出がよみがえる（フラッシュバック）----
        // ハンマーのアップから、ふわっと白くにじんで回想に入る
        yield return FadeFlash(Color.white, 0f, 1f, 0.35f);
        SetMemory(true);
        StartCoroutine(FadeFlash(Color.white, 1f, 0f, 0.8f));
        // 思い出の写真
        BuildPhotos();
        movieCam.backgroundColor = new Color(0.3f, 0.22f, 0.15f);
        SetShot(lt => PhotoSet + new Vector3(-3.6f + lt * 2.0f, 0.2f, -4.2f + lt * 0.15f), lt => PhotoSet + new Vector3(-3.2f + lt * 2.0f, 0f, 0f), 44f);
        yield return At(34.6f);
        // 夕焼けの帰り道：住宅街の道を、夕日を背に 2 人ずつ 2 列で並んで、こちらへ歩いてくる（ゆっくり、軽くはずむ程度）
        // 回想の中は、白いフラッシュではなくゆっくり白く溶けてつなぐ
        yield return FadeFlash(Color.white, 0f, 0.9f, 0.3f);
        StartCoroutine(FadeFlash(Color.white, 0.9f, 0f, 0.6f));
        SetSunset(true);
        movieCam.backgroundColor = new Color(1f, 0.6f, 0.35f);
        const float walkFrom = 9f, walkSpeed = 1.6f, walkDur = 3.4f;
        var slots = new[] { new Vector2(-0.75f, 0f), new Vector2(0.75f, 0f), new Vector2(-0.75f, 2.4f), new Vector2(0.75f, 2.4f) };
        for (int i = 0; i < 4; i++)
        {
            var from = TownSet + new Vector3(slots[i].x, 0f, walkFrom + slots[i].y);
            var w = CreateMember(setRoot, from, 180f, colors[i].Item1, colors[i].Item2, 1.2f, 2);
            w.face.SetHappy(true);
            StartCoroutine(Run(w, from, from + Vector3.back * walkSpeed * walkDur, walkDur, 0.06f, 4f + i * 0.3f));
        }
        // 前を後ろ向きに歩きながら、後ろの列も見えるよう少し高いところから見下ろす
        SetShot(lt => TownSet + new Vector3(2.3f, 4.0f, walkFrom - 6.6f - lt * walkSpeed), lt => TownSet + new Vector3(0f, 1.0f, walkFrom + 1.4f - lt * walkSpeed), 44f);
        yield return At(38.0f);

        // ---- 9. 決勝に戻って振り下ろす：王者の寸前でホワイトアウト（当たる瞬間は見せない）----
        // 強いフラッシュで現実に引き戻される
        SetMemory(false);
        StartCoroutine(FadeFlash(Color.white, 1f, 0f, 0.35f));
        camShake = 0.5f;
        SetSunset(false);
        movieCam.backgroundColor = new Color(0.25f, 0.17f, 0.1f);
        // 主人公の顔（覚悟を決める）
        SetShot(lt => hero.Head + hero.root.forward * (1.9f - lt * 0.3f) + side * 0.6f + Vector3.up * 0.1f, lt => hero.Head, 36f);
        yield return Tween(0.5f, u => { });
        // 振り下ろす：スローで王者に迫り、届く寸前で白く飛ぶ
        // 王者の側から：迫ってくる紫のハンマーと、王者の顔を同じ画面に
        var toChamp = Flat(champ.Pos - hero.Pos).normalized;
        SetShot(lt => champ.Head + side * (2.4f - lt * 0.8f) + toChamp * 0.9f + Vector3.up * 0.3f, lt => champ.Head - toChamp * 0.7f, 50f);
        StartCoroutine(SlowMo(0.12f, 10f));
        bool white = false;
        // 振り終わりで、ハンマーの頭が王者の顔の真横（ほぼ触れるところ）にぴたりと来るように、踏み込みの距離と振りの角度を決める
        //   ・横向き：yaw -28 で、頭が2人を結ぶ線の上に来る（GHammerClash と同じ）
        //   ・前後：柄の根元から頭までが約 0.97、頭どうしの間を 0.62 あける → 2人の距離を 1.59 まで詰める
        //   ・高さ：根元（体の上 0.35）から王者の顔の高さまで下げる角度
        var lungeFrom = hero.Pos;
        var lungeDir = Flat(champ.Pos - hero.Pos).normalized;
        float lunge = Mathf.Max(0f, Flat(champ.Pos - hero.Pos).magnitude - 1.59f);
        float drop = (hero.bodyY + 0.35f) - champ.Head.y;
        float endPitch = Mathf.Atan2(drop, 1.05f) * Mathf.Rad2Deg;
        yield return Tween(0.16f, u =>
        {
            float e = 1f - (1f - u) * (1f - u);   // 振り抜く勢い（最後にゆるむ）
            hero.root.position = lungeFrom + lungeDir * lunge * e;
            // 角度をそのまま補間して前へ振る（Slerp だと近い向きを通って、体の後ろを回ってしまう）
            hero.pivotRot = Quaternion.Euler(Mathf.Lerp(-60f, endPitch, e), Mathf.Lerp(130f, -28f, e), 0f);
            // 王者の顔の真横に届く寸前で白く飛ぶ（ここで台本が止まるので、ハンマーはそこで止まったまま白くなる）
            if (white || u < 0.93f) return;
            white = true;
            BeginFinish(false);
        });
    }

    // ================= 片づけ → タイトル画面へ =================
    // 台本や動きのコルーチンを全部止めてから、別のコンポーネントで片づけを走らせる
    // （止めないと、片づけたあとも動きが消えた物にさわったり、スローを戻したりする）
    void BeginFinish(bool skipped)
    {
        if (finishing) return;
        finishing = true;
        ffwd = false;   // 早送りはここまで
        Time.timeScale = baseScale;
        shakeFloor = 0f;
        if (ffwdMark != null) ffwdMark.alpha = 0f;
        StopAllCoroutines();
        var host = gameObject.AddComponent<CoroutineHost>();
        host.StartCoroutine(Finish(skipped, host));
    }

    IEnumerator Finish(bool skipped, CoroutineHost host)
    {
        yield return FadeFlash(Color.white, flash.color.a, 1f, skipped ? 0.2f : 1.0f);
        yield return new WaitForSecondsRealtime(0.25f);

        Time.timeScale = 1f;
        SetSunset(false);
        SetMemory(false);
        if (dof != null) dof.active = dofWasActive;
        if (movieCam != null) Destroy(movieCam.gameObject);
        if (mainCam != null) mainCam.enabled = true;
        Destroy(setRoot.gameObject);
        foreach (var f in flyers) if (f.t != null) Destroy(f.t.gameObject);
        flyers.Clear();
        foreach (var s in snaps) s.Release();
        snaps.Clear();
        if (title != null) title.ReleaseAssemble(0.8f);   // だるま落としの演出は飛ばし、白が明けるころにロゴが右から1個ずつ飛んでくる

        // 真っ白から、タイトル画面へ
        for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime)
        {
            var c = flash.color;
            c.a = 1f - Mathf.SmoothStep(0f, 1f, t / 1.2f);
            flash.color = c;
            yield return null;
        }
        Destroy(ui);
        if (rt != null) rt.Release();
        Destroy(host);
        Destroy(this);
    }

    // ================= 毎フレーム =================
    void Update()
    {
        if (finishing || movieCam == null) return;
        mt += Time.deltaTime;
        // A / クリック：飛ばす。B を押しているあいだ：早送り
        if (mt > 0.3f && (MenuInput.Submit() || MenuInput.Start() || MenuInput.AnyClick()))
        {
            BeginFinish(true);
            return;
        }
        ffwd = MenuInput.CancelHeld();
        // ヒットストップ：当たった瞬間だけ、ほんの一瞬止まったように見せる
        hitStop = Mathf.Max(0f, hitStop - Time.unscaledDeltaTime);
        Time.timeScale = baseScale * (ffwd ? FfwdSpeed : 1f) * (hitStop > 0f ? 0.04f : 1f);
        if (ffwdMark != null) ffwdMark.alpha = ffwd ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 10f) : 0f;
        camShake = Mathf.Max(camShake, shakeFloor);

        float dt = Time.deltaTime;
        for (int i = flyers.Count - 1; i >= 0; i--)
        {
            var f = flyers[i];
            f.v += Physics.gravity * dt;
            f.t.position += f.v * dt;
            f.t.Rotate(f.spin * dt, Space.World);
            f.life -= dt;
            if (f.life <= 0f) { Destroy(f.t.gameObject); flyers.RemoveAt(i); }
        }
        foreach (var m in allMembers) m.Update(dt, mt);
        UpdateMatch(dt);
        UpdateMemory();

        if (camPos != null)
        {
            float lt = mt - shotStart;
            camShake = Mathf.MoveTowards(camShake, 0f, Time.unscaledDeltaTime * 2f);
            var shake = Random.insideUnitSphere * camShake * 0.15f * shakeStrength;
            movieCam.transform.position = camPos(lt) + shake;
            movieCam.transform.LookAt(camLook(lt) + shake * 0.5f);
            // 当たった瞬間にグッと寄る（ズームパンチ）
            fovPunch = Mathf.MoveTowards(fovPunch, 0f, Time.unscaledDeltaTime * 45f);
            movieCam.fieldOfView = shotFov - fovPunch;
        }
        UpdateRushFx();
    }

    float hitStop, fovPunch, shotFov = 46f;

    // ================= 道具 =================
    float shift;   // 途中で足したカットのぶん、それ以降の時刻を後ろへずらす
    IEnumerator At(float time) { while (mt < time + shift) yield return null; }

    IEnumerator Tween(float dur, System.Action<float> step)
    {
        float s = mt;
        while (mt < s + dur) { step(Mathf.Clamp01((mt - s) / dur)); yield return null; }
        step(1f);
    }

    void SetShot(System.Func<float, Vector3> pos, System.Func<float, Vector3> look, float fov)
    {
        camPos = pos;
        camLook = look;
        shotStart = mt;
        movieCam.fieldOfView = fov;
        shotFov = fov;
    }

    // 白いフラッシュでカットをつなぐ
    void Flash() => StartCoroutine(FadeFlash(Color.white, 0.85f, 0f, 0.25f));

    // ---- 回想の見た目 ----
    // セピアで色あせ、まわりが白くにじみ、フィルムのざらつきとちらつき、上下に黒い帯
    GameObject memoryUI;
    RawImage grain;
    Image memoryGlow;
    ColorAdjustments memCA;
    Vignette memVig;
    Bloom memBloom;
    bool memoryOn;
    float caSat, caContrast, caExposure, vigInt, bloomInt;
    Color caFilter, vigColor;

    void SetMemory(bool on)
    {
        if (memoryOn == on) return;
        memoryOn = on;
        var p = TitleDressingKit.PostProfile();
        if (on && p != null)
        {
            // いまの値をとっておいて、回想のあいだだけ変える
            if (p.TryGet(out memCA)) { caSat = memCA.saturation.value; caContrast = memCA.contrast.value; caExposure = memCA.postExposure.value; caFilter = memCA.colorFilter.value; }
            if (p.TryGet(out memVig)) { vigInt = memVig.intensity.value; vigColor = memVig.color.value; }
            if (p.TryGet(out memBloom)) bloomInt = memBloom.intensity.value;
            if (memCA != null)
            {
                memCA.saturation.overrideState = memCA.contrast.overrideState = memCA.postExposure.overrideState = memCA.colorFilter.overrideState = true;
                // 色あせさせる（URP は色フィルターのあとで彩度を下げるので、下げすぎると暖色も消える。暖色は画面の上の色の膜で足す）
                memCA.saturation.value = -55f;
                memCA.contrast.value = -5f;
                memCA.postExposure.value = 0.2f;
                memCA.colorFilter.value = new Color(1f, 0.78f, 0.5f);
            }
            if (memVig != null) { memVig.intensity.overrideState = memVig.color.overrideState = true; memVig.intensity.value = 0.45f; memVig.color.value = new Color(0.25f, 0.15f, 0.08f); }
            if (memBloom != null) { memBloom.intensity.overrideState = true; memBloom.intensity.value = bloomInt + 0.8f; }
        }
        else if (!on)
        {
            if (memCA != null) { memCA.saturation.value = caSat; memCA.contrast.value = caContrast; memCA.postExposure.value = caExposure; memCA.colorFilter.value = caFilter; }
            if (memVig != null) { memVig.intensity.value = vigInt; memVig.color.value = vigColor; }
            if (memBloom != null) memBloom.intensity.value = bloomInt;
        }
        if (memoryUI != null) memoryUI.SetActive(on);
    }

    // 回想用の画面の重ね物（白いにじみ・ざらつき・黒い帯）。白いフラッシュより下に置く
    void BuildMemoryUI()
    {
        var root = new GameObject("MemoryOverlay", typeof(RectTransform));
        root.transform.SetParent(ui.transform, false);
        root.transform.SetSiblingIndex(flash.transform.GetSiblingIndex());
        ToyKit.Stretch((RectTransform)root.transform);
        memoryUI = root;

        // 画面全体にうすくかける茶色の膜（セピア）
        var tint = ToyKit.UIImage("SepiaTint", root.transform, new Color(0.72f, 0.48f, 0.22f, 0.28f));
        ToyKit.Stretch(tint);

        // まわりの白いにじみ（真ん中は透明、ふちほど白い）
        const int s = 128;
        var glowTex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 1.1f);
                px[y * s + x] = new Color(1f, 0.9f, 0.72f,Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.25f, d)) * 0.85f);
            }
        glowTex.SetPixels(px);
        glowTex.Apply();
        memoryGlow = ToyKit.UIImage("Glow", root.transform, Color.white).GetComponent<Image>();
        memoryGlow.sprite = Sprite.Create(glowTex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        ToyKit.Stretch(memoryGlow.rectTransform);

        // フィルムのざらつき（ノイズを毎フレームずらす）
        const int g = 256;
        var grainTex = new Texture2D(g, g, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        var gp = new Color[g * g];
        var rng = new System.Random(3);
        for (int i = 0; i < gp.Length; i++)
        {
            float v = (float)rng.NextDouble();
            gp[i] = v > 0.5f ? new Color(1f, 1f, 1f, (v - 0.5f) * 0.18f) : new Color(0.2f, 0.12f, 0.05f, (0.5f - v) * 0.18f);
        }
        grainTex.SetPixels(gp);
        grainTex.Apply();
        grain = new GameObject("Grain", typeof(RectTransform)).AddComponent<RawImage>();
        grain.transform.SetParent(root.transform, false);
        grain.texture = grainTex;
        grain.raycastTarget = false;
        ToyKit.Stretch(grain.rectTransform);

        // 上下の黒い帯（映画のような回想の画面）
        foreach (float y in new[] { 0f, 1f })
        {
            var bar = ToyKit.UIImage("Letterbox", root.transform, new Color(0.03f, 0.02f, 0.02f));
            bar.anchorMin = new Vector2(0f, y);
            bar.anchorMax = new Vector2(1f, y);
            bar.pivot = new Vector2(0.5f, y);
            bar.sizeDelta = new Vector2(0f, 110f);
            bar.anchoredPosition = Vector2.zero;
        }
        root.SetActive(false);
    }

    void UpdateMemory()
    {
        if (!memoryOn || grain == null) return;
        // ざらつきは 1 コマごとに位置を変え、明るさをかすかに揺らす（古いフィルムのちらつき）
        grain.uvRect = new Rect(Random.value, Random.value, Screen.width / 256f, Screen.height / 256f);
        float flicker = 0.9f + Random.value * 0.1f;
        memoryGlow.color = new Color(1f, 1f, 1f, flicker);
        if (memCA != null) memCA.postExposure.value = 0.15f + Random.value * 0.1f;
    }

    // ---- 早送り（B を押しているあいだ） ----
    // 台本は baseScale で時間の速さを決め（スローなど）、Update で早送りの倍率をかけて Time.timeScale にする。
    // 実時間で動くもの（白いフラッシュ・スローの止め時間）も RealDt で早送りに合わせる
    const float FfwdSpeed = 4f;
    float baseScale = 1f;
    bool ffwd;
    float shakeFloor;   // ラッシュ中など、揺れ続けるときの最低の揺れ
    TMPro.TextMeshProUGUI ffwdMark;
    float RealDt => Time.unscaledDeltaTime * (ffwd ? FfwdSpeed : 1f);

    IEnumerator FadeFlash(Color c, float from, float to, float dur)
    {
        for (float t = 0f; t < dur; t += RealDt)
        {
            c.a = Mathf.Lerp(from, to, t / dur);
            flash.color = c;
            yield return null;
        }
        c.a = to;
        flash.color = c;
    }

    IEnumerator SlowMo(float scale, float holdReal)
    {
        baseScale = scale;
        for (float t = 0f; t < holdReal; t += RealDt) yield return null;
        for (float t = 0f; t < 0.3f; t += RealDt)
        {
            baseScale = Mathf.Lerp(scale, 1f, t / 0.3f);
            yield return null;
        }
        baseScale = 1f;
    }

    // 今の画面を写真として取っておく（あとで「思い出の写真」に貼る）
    void Snap()
    {
        var s = new RenderTexture(640, 360, 0) { name = "MovieSnap" };
        Graphics.Blit(rt, s);
        snaps.Add(s);
    }

    void SetSunset(bool on)
    {
        if (keyLight == null) return;
        keyLight.color = on ? new Color(1f, 0.62f, 0.35f) : keyColor;
        keyLight.intensity = on ? keyIntensity * 0.8f : keyIntensity;
        if (yardSky != null) yardSky.SetTexture("_BaseMap", on ? sunsetTex : daySkyTex);
    }

    // ================= カメラと画面 =================
    void BuildCameraAndScreen()
    {
        rt = new RenderTexture(Mathf.Max(640, Screen.width), Mathf.Max(360, Screen.height), 24) { name = "OpeningMovie" };
        var go = new GameObject("MovieCamera");
        movieCam = go.AddComponent<Camera>();
        movieCam.targetTexture = rt;
        movieCam.nearClipPlane = 0.05f;
        movieCam.farClipPlane = 300f;
        movieCam.clearFlags = CameraClearFlags.SolidColor;
        go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        if (mainCam != null) mainCam.enabled = false;

        // いちばん手前の画面（TitleScreen の画面より上）
        var canvas = ToyKit.CreateOverlayCanvas("MovieScreen", 900);
        ui = canvas.gameObject;
        var raw = new GameObject("Movie").AddComponent<RawImage>();
        raw.transform.SetParent(canvas, false);
        raw.texture = rt;
        ToyKit.Stretch(raw.rectTransform);


        flash = ToyKit.UIImage("Flash", canvas, Color.black).GetComponent<Image>();
        ToyKit.Stretch(flash.rectTransform);
        flash.raycastTarget = false;

        // 早送り中だけ右上に出る印
        // 明るい空の上でも読めるよう、黄色の文字に濃い縁取り
        ffwdMark = ToyKit.UIText("FastForward", canvas, font, "早送り ×4", 52f, new Color(1f, 0.88f, 0.25f));
        ffwdMark.outlineWidth = 0.3f;
        ffwdMark.outlineColor = new Color32(40, 25, 15, 255);
        ffwdMark.alignment = TMPro.TextAlignmentOptions.TopRight;
        ffwdMark.rectTransform.pivot = new Vector2(1f, 1f);
        ToyKit.Anchor(ffwdMark.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(400f, 70f));
        ffwdMark.alpha = 0f;
    }

    // ================= 部員（頭＋積み木の体＋ハンマー） =================
    readonly List<Member> allMembers = new List<Member>();

    class Member
    {
        public Transform root, pivot;
        public ToyKit.Face face;
        public readonly List<Transform> blocks = new List<Transform>();
        public float s, bodyY, hop, bow;
        public bool tremble;
        public Vector3 basePos;

        // 抜けた積み木の上は落ちる。頭は積み木の上に乗る
        public void Update(float dt, float t)
        {
            if (root == null) return;
            float bh = ToyKit.BlockH * s;
            for (int i = 0; i < blocks.Count; i++)
            {
                var lp = blocks[i].localPosition;
                lp.y = Mathf.MoveTowards(lp.y, bh * (i + 0.5f), 14f * s * dt);
                blocks[i].localPosition = lp;
            }
            bodyY = Mathf.MoveTowards(bodyY, bh * blocks.Count, 14f * s * dt);
            var jitter = tremble ? new Vector3(Mathf.Sin(t * 70f), 0f, Mathf.Cos(t * 53f)) * 0.03f * s : Vector3.zero;
            face.root.localPosition = new Vector3(0f, bodyY + ToyKit.HeadR * s + hop, 0f) + jitter;
            face.root.localRotation = Quaternion.Euler(bow, 180f, 0f);
            pivot.localPosition = new Vector3(ToyKit.BlockW * s * 0.45f, Mathf.Max(bh * 0.7f, bodyY - bh * 0.6f) + hop, 0.2f * s);
        }
    }

    Member CreateMember(Transform parent, Vector3 pos, float yaw, Color bodyA, Color bodyB, float s, int count)
    {
        var m = new Member { s = s, basePos = pos };
        m.root = new GameObject("MovieMember").transform;
        m.root.SetParent(parent, false);
        m.root.position = pos;
        m.root.rotation = Quaternion.Euler(0f, yaw, 0f);
        float bh = ToyKit.BlockH * s;
        for (int i = 0; i < count; i++)
        {
            // 本編と同じ円柱の積み木（円柱は高さ 2 なので半分にする）
            var b = ToyKit.Primitive(PrimitiveType.Cylinder, "BodyBlock", m.root);
            b.localScale = new Vector3(ToyKit.BlockW * s * 0.85f, bh * 0.49f, ToyKit.BlockD * s * 0.85f);
            b.localPosition = new Vector3(0f, bh * (i + 0.5f), 0f);
            b.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(i % 2 == 0 ? bodyA : bodyB, 0.5f);
            m.blocks.Add(b);
        }
        m.bodyY = bh * count;
        m.face = ToyKit.CreateHead();
        m.face.root.SetParent(m.root, false);
        m.face.root.localScale = Vector3.one * s;
        m.pivot = new GameObject("HammerPivot").transform;
        m.pivot.SetParent(m.root, false);
        m.pivot.localRotation = Quaternion.Euler(-70f, 0f, 0f);   // 柄を上に向けて持つ
        var hammer = ToyKit.CreateHammer();
        hammer.SetParent(m.pivot, false);
        hammer.localScale = Vector3.one * s * 0.6f;
        m.Update(0f, 0f);
        allMembers.Add(m);
        return m;
    }

    // 横に振る（構え → 振り抜く。途中で onHit）
    IEnumerator Swing(Member m, float windup, float strike, System.Action onHit)
    {
        var idle = m.pivot.localRotation;
        var back = Quaternion.Euler(-15f, 130f, 0f);
        var follow = Quaternion.Euler(10f, -70f, 0f);
        yield return Tween(windup, u => m.pivot.localRotation = Quaternion.Slerp(idle, back, Mathf.SmoothStep(0f, 1f, u)));
        bool hit = false;
        yield return Tween(strike, u =>
        {
            // 角度をそのまま補間して前へ振る（上から見て反時計回り。Slerp だと体の後ろを回ってしまう）
            m.pivot.localRotation = Quaternion.Euler(Mathf.Lerp(-15f, 10f, u), Mathf.Lerp(130f, -70f, u), 0f);
            if (!hit && u > 0.55f) { hit = true; onHit?.Invoke(); }
        });
        yield return Tween(0.3f, u => m.pivot.localRotation = Quaternion.Slerp(follow, Quaternion.Euler(-70f, 0f, 0f), Mathf.SmoothStep(0f, 1f, u)));
    }

    IEnumerator SwingLoop(Member m, float from, float to, float period)
    {
        yield return At(from);
        while (mt < to)
        {
            yield return Swing(m, period * 0.45f, period * 0.15f, null);
            m.hop = 0f;
        }
    }

    // 積み木を1つ叩き出す（dir の向きへ飛ぶ）
    void KnockOut(Member m, int index, Vector3 dir)
    {
        if (index >= m.blocks.Count) return;
        var b = m.blocks[index];
        m.blocks.RemoveAt(index);
        b.SetParent(setRoot, true);
        flyers.Add(new Flyer { t = b, v = dir.normalized * 9f * m.s + Vector3.up * 2f, spin = Random.onUnitSphere * 720f, life = 3f });
        camShake = Mathf.Max(camShake, 0.8f);
    }

    IEnumerator Turn(Member m, float yaw, float dur)
    {
        var from = m.root.rotation;
        var to = Quaternion.Euler(0f, yaw, 0f);
        yield return Tween(dur, u => m.root.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, u)));
    }

    IEnumerator RaiseHammer(Member m, float dur)
    {
        var from = m.pivot.localRotation;
        yield return Tween(dur, u => m.pivot.localRotation = Quaternion.Slerp(from, Quaternion.Euler(-150f, 0f, 0f), Mathf.SmoothStep(0f, 1f, u)));
        m.face.SetHappy(true);
    }

    IEnumerator TurnAndHop(Member m, float at)
    {
        yield return At(at);
        yield return Turn(m, 180f, 0.3f);
        m.face.SetHappy(true);
        for (int k = 0; k < 2; k++)
            yield return Tween(0.3f, u => m.hop = Mathf.Sin(u * Mathf.PI) * 0.6f * m.s);
        m.hop = 0f;
    }

    IEnumerator Bow(Member m)
    {
        yield return Tween(0.4f, u => m.bow = Mathf.SmoothStep(0f, 1f, u) * 35f);
        yield return Tween(0.6f, u => { });
        yield return Tween(0.4f, u => m.bow = (1f - Mathf.SmoothStep(0f, 1f, u)) * 35f);
    }

    IEnumerator Run(Member m, Vector3 from, Vector3 to, float dur, float bounce = 0.25f, float stepRate = 9f)
    {
        float s = mt;
        yield return Tween(dur, u =>
        {
            m.root.position = Vector3.Lerp(from, to, u);
            m.hop = Mathf.Abs(Mathf.Sin((mt - s) * stepRate)) * bounce * m.s;
        });
        m.hop = 0f;
    }

    // だるま落としの塔（色の積み木＋てっぺんに小さな顔）
    Member CreateTower(Vector3 pos, int count)
    {
        var t = CreateMember(setRoot, pos, -90f, Blue, Yellow, 1.2f, count);
        var palette = new[] { Red, Blue, Yellow, Green, new Color(0.7f, 0.4f, 0.9f) };
        for (int i = 0; i < t.blocks.Count; i++) t.blocks[i].GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(palette[i % palette.Length], 0.55f);
        t.pivot.gameObject.SetActive(false);
        return t;
    }

    // ================= セット =================
    Material yardSky;
    Texture2D daySkyTex, sunsetTex;
    Transform paper;

    static Texture2D SkyGradient(Color top, Color bottom)
    {
        const int h = 64;
        var tex = TitleDressingKit.NewTexture(2, h);
        var px = new Color[2 * h];
        for (int y = 0; y < h; y++) { var c = Color.Lerp(bottom, top, y / (h - 1f)); px[y * 2] = px[y * 2 + 1] = c; }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    Transform SetBase(string name, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(setRoot, false);
        t.position = pos;
        return t;
    }

    Transform Box(Transform parent, string name, Vector3 worldPos, Vector3 size, Material mat, float yaw = 0f)
    {
        var b = ToyKit.Primitive(PrimitiveType.Cube, name, parent);
        b.position = worldPos;
        b.rotation = Quaternion.Euler(0f, yaw, 0f);
        b.localScale = size;
        b.GetComponent<Renderer>().sharedMaterial = mat;
        return b;
    }

    TextMeshPro Write(Transform parent, string text, Vector3 pos, Vector2 box, float size, Color c, Quaternion rot)
    {
        var t = TitleDressingKit.Text(parent, font, text, pos, box, size, c, c, 0.05f, 0f);
        t.transform.rotation = rot;
        return t;
    }

    // 1. 机の上のノート
    void BuildNotebook()
    {
        var root = SetBase("Notebook", NoteSet);
        Box(root, "Desk", NoteSet + new Vector3(0f, -0.3f, 0f), new Vector3(14f, 0.5f, 9f), ToyKit.Lit(new Color(0.55f, 0.38f, 0.24f), 0.35f));
        var paperMat = ToyKit.Lit(new Color(0.98f, 0.97f, 0.92f), 0.1f);
        Box(root, "PageL", NoteSet + new Vector3(-2.05f, -0.02f, 0.4f), new Vector3(4f, 0.06f, 5.4f), paperMat, -4f);
        Box(root, "PageR", NoteSet + new Vector3(2.05f, -0.02f, 0.4f), new Vector3(4f, 0.06f, 5.4f), paperMat, -4f);
        var line = ToyKit.Lit(new Color(0.55f, 0.7f, 0.9f), 0.1f);
        for (int i = 0; i < 9; i++)
            Box(root, "Rule", NoteSet + Quaternion.Euler(0f, -4f, 0f) * new Vector3(0f, 0.015f, -1.8f + i * 0.55f), new Vector3(8f, 0.005f, 0.025f), line, -4f);
        var flat = Quaternion.Euler(90f, -4f, 0f);
        Write(root, "目指せ！\n全国制覇！！", NoteSet + new Vector3(-2.1f, 0.03f, 0.9f), new Vector2(3.6f, 2.6f), 14f, new Color(0.85f, 0.15f, 0.12f), flat);
        Write(root, "だるま落とし部", NoteSet + new Vector3(-2.0f, 0.03f, -1.2f), new Vector2(3.2f, 0.7f), 8f, new Color(0.15f, 0.15f, 0.2f), flat);
        Write(root, "一撃入魂", NoteSet + new Vector3(2.0f, 0.03f, 1.2f), new Vector2(2.6f, 0.9f), 10f, new Color(0.15f, 0.25f, 0.6f), flat);
        Write(root, "・素振り 1000回\n・積み上げ 100段\n・走り込み！！", NoteSet + new Vector3(2.1f, 0.03f, -0.6f), new Vector2(3.2f, 2f), 6f, new Color(0.2f, 0.2f, 0.25f), flat);
        // 鉛筆と、消しゴムがわりの小さなだるま
        var pencil = ToyKit.Primitive(PrimitiveType.Cylinder, "Pencil", root);
        pencil.position = NoteSet + new Vector3(3.6f, 0.12f, -2.2f);
        pencil.rotation = Quaternion.Euler(0f, 30f, 90f);
        pencil.localScale = new Vector3(0.18f, 1.6f, 0.18f);
        pencil.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(1f, 0.8f, 0.2f), 0.4f);
        var mini = ToyKit.CreateHead();
        mini.root.SetParent(root, false);
        mini.root.position = NoteSet + new Vector3(-4.6f, 0.45f, -1.6f);
        mini.root.rotation = Quaternion.Euler(0f, 200f, 0f);
        mini.SetHappy(true);
    }

    // 3・8. 校庭（練習と夕焼け）
    void BuildYard()
    {
        var root = SetBase("Yard", YardSet);
        var ground = ToyKit.Primitive(PrimitiveType.Plane, "Ground", root);
        ground.position = YardSet;
        ground.localScale = new Vector3(12f, 1f, 12f);
        ground.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.8f, 0.66f, 0.48f), 0.15f);
        daySkyTex = SkyGradient(new Color(0.35f, 0.6f, 1f), new Color(0.85f, 0.93f, 1f));
        sunsetTex = SkyGradient(new Color(0.45f, 0.3f, 0.6f), new Color(1f, 0.6f, 0.3f));
        yardSky = TitleDressingKit.Unlit(Color.white, daySkyTex);
        foreach (var (pos, yaw) in new[] { (new Vector3(0f, 12f, 40f), 0f), (new Vector3(0f, 12f, -40f), 180f), (new Vector3(40f, 12f, 0f), 90f), (new Vector3(-40f, 12f, 0f), -90f) })
        {
            var q = TitleDressingKit.Quad(root, "Sky", YardSet + pos, new Vector3(80f, 30f, 1f), yardSky);
            q.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
        var leaf = ToyKit.Lit(new Color(0.3f, 0.6f, 0.3f), 0.2f);
        var rng = new System.Random(5);
        for (int i = 0; i < 16; i++)
        {
            var t = ToyKit.Primitive(PrimitiveType.Sphere, "Tree", root);
            float s = 4f + (float)rng.NextDouble() * 3f;
            t.localScale = new Vector3(s, s * 0.9f, s);
            t.position = YardSet + new Vector3(-30f + i * 4f, 2f, 30f + (float)rng.NextDouble() * 4f);
            t.GetComponent<Renderer>().sharedMaterial = leaf;
        }
        // 白いライン（走り込みのコース）
        var white = ToyKit.Lit(Color.white, 0.1f);
        foreach (float z in new[] { 7f, 9f, 13f, 15f })
            Box(root, "Line", YardSet + new Vector3(0f, 0.01f, z), new Vector3(60f, 0.01f, 0.12f), white);
        // 金網
        var fence = ToyKit.Lit(new Color(0.55f, 0.6f, 0.62f), 0.5f);
        for (int i = 0; i < 20; i++)
            Box(root, "FencePost", YardSet + new Vector3(-28f + i * 3f, 1.5f, 22f), new Vector3(0.12f, 3f, 0.12f), fence);
        Box(root, "FenceTop", YardSet + new Vector3(0f, 3f, 22f), new Vector3(60f, 0.1f, 0.1f), fence);
    }

    // 4. 神社と絵馬
    void BuildShrine()
    {
        var root = SetBase("Shrine", ShrineSet);
        var ground = ToyKit.Primitive(PrimitiveType.Plane, "Ground", root);
        ground.position = ShrineSet;
        ground.localScale = new Vector3(8f, 1f, 8f);
        ground.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.72f, 0.7f, 0.66f), 0.2f);
        var sky = TitleDressingKit.Quad(root, "Sky", ShrineSet + new Vector3(0f, 10f, 30f), new Vector3(90f, 30f, 1f), TitleDressingKit.Unlit(Color.white, SkyGradient(new Color(0.4f, 0.65f, 1f), new Color(0.9f, 0.95f, 1f))));
        // 鳥居（奥）
        var vermilion = ToyKit.Lit(new Color(0.85f, 0.2f, 0.12f), 0.4f);
        foreach (float x in new[] { -3.2f, 3.2f })
        {
            var p = ToyKit.Primitive(PrimitiveType.Cylinder, "ToriiPillar", root);
            p.position = ShrineSet + new Vector3(x, 3.5f, 9f);
            p.localScale = new Vector3(0.5f, 3.5f, 0.5f);
            p.GetComponent<Renderer>().sharedMaterial = vermilion;
        }
        Box(root, "Kasagi", ShrineSet + new Vector3(0f, 7.2f, 9f), new Vector3(9f, 0.5f, 0.7f), ToyKit.Lit(new Color(0.12f, 0.1f, 0.1f), 0.3f));
        Box(root, "Nuki", ShrineSet + new Vector3(0f, 5.9f, 9f), new Vector3(7.6f, 0.35f, 0.4f), vermilion);
        var leaf = ToyKit.Lit(new Color(0.25f, 0.5f, 0.28f), 0.2f);
        for (int i = 0; i < 8; i++)
        {
            var t = ToyKit.Primitive(PrimitiveType.Sphere, "Tree", root);
            t.localScale = Vector3.one * 5f;
            t.position = ShrineSet + new Vector3(-14f + i * 4f, 3f, 16f + (i % 2) * 2f);
            t.GetComponent<Renderer>().sharedMaterial = leaf;
        }
        // 絵馬掛け
        var wood = ToyKit.Lit(new Color(0.55f, 0.38f, 0.22f), 0.3f);
        Box(root, "EmaRackBar", ShrineSet + new Vector3(0f, 2.6f, 2.35f), new Vector3(5f, 0.12f, 0.12f), wood);
        Box(root, "EmaRackBar", ShrineSet + new Vector3(0f, 1.6f, 2.35f), new Vector3(5f, 0.12f, 0.12f), wood);
        foreach (float x in new[] { -2.5f, 2.5f })
            Box(root, "EmaRackLeg", ShrineSet + new Vector3(x, 1.4f, 2.35f), new Vector3(0.14f, 2.8f, 0.14f), wood);
        var emaMat = ToyKit.Lit(new Color(0.92f, 0.8f, 0.6f), 0.2f);
        var wishes = new[] { "合格祈願", "健康第一", "商売繁盛", "家内安全", "必勝", "優勝！" };
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < 6; i++)
            {
                float x = -2.1f + i * 0.84f;
                float y = row == 0 ? 2.3f : 1.3f;
                if (row == 0 && (i == 2 || i == 3)) continue;   // 真ん中は部の絵馬
                var e = Box(root, "Ema", ShrineSet + new Vector3(x, y, 2.3f), new Vector3(0.7f, 0.45f, 0.04f), emaMat, (i - 2.5f) * 3f);
                Write(root, wishes[(i + row * 3) % wishes.Length], e.position + new Vector3(0f, 0f, -0.03f), new Vector2(0.6f, 0.3f), 2f, new Color(0.2f, 0.15f, 0.1f), e.rotation);
            }
        var main = Box(root, "BigEma", ShrineSet + new Vector3(0f, 2.05f, 2.25f), new Vector3(1.4f, 0.9f, 0.05f), emaMat);
        Write(root, "全国制覇！\nだるま落とし部", main.position + new Vector3(0f, 0f, -0.04f), new Vector2(1.2f, 0.75f), 4f, new Color(0.75f, 0.1f, 0.08f), Quaternion.identity);
        _ = sky;
    }

    // 5. 新聞：3D ではなく、ムービーの画面の上に重ねる UI で作る（映像の上に必ず重なり、奥行きのチラつきも起きない）
    // 大きさは 3D で作っていたときと同じ比率（1 単位 = 150 px）
    const float PaperPx = 150f;

    void BuildNewspaper()
    {
        paper = new GameObject("Paper", typeof(RectTransform)).transform;
        paper.SetParent(ui.transform, false);
        paper.SetSiblingIndex(flash.transform.GetSiblingIndex());   // 白いフラッシュよりは下
        var rect = (RectTransform)paper;
        ToyKit.Anchor(rect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6.4f, 4.4f) * PaperPx);
        var sheet = ToyKit.UIImage("Sheet", paper, new Color(0.95f, 0.93f, 0.86f));
        ToyKit.Stretch(sheet);
        var ink = new Color(0.1f, 0.1f, 0.12f);
        var red = new Color(0.85f, 0.12f, 0.1f);
        PaperText("だるま新聞", new Vector2(-1.9f, 1.85f), new Vector2(2.2f, 0.5f), ink);
        PaperText("快進撃！", new Vector2(1.6f, 0.2f), new Vector2(2.8f, 1.6f), red);
        PaperText("だるま落とし部\nチーム一丸", new Vector2(-1.5f, 0.9f), new Vector2(2.8f, 1.1f), ink);
        PaperText("地区大会 優勝", new Vector2(1.6f, -1.2f), new Vector2(2.6f, 0.5f), ink);
        // 本文の行（灰色の線）と写真の枠（試合で撮った写真をあとで貼る）
        var gray = new Color(0.55f, 0.55f, 0.58f);
        for (int i = 0; i < 10; i++)
        {
            var pos = new Vector2(1.6f, -1.55f - (i % 5) * 0.14f) + new Vector2(i < 5 ? 0f : -3.2f, i < 5 ? 0f : 1.0f);
            ToyKit.Anchor(ToyKit.UIImage("TextLine", paper, gray), new Vector2(0.5f, 0.5f), pos * PaperPx, new Vector2(2.6f, 0.06f) * PaperPx);
        }
        paperPhoto = new GameObject("Photo", typeof(RectTransform)).AddComponent<RawImage>();
        paperPhoto.transform.SetParent(paper, false);
        paperPhoto.color = Color.gray;
        paperPhoto.raycastTarget = false;
        ToyKit.Anchor(paperPhoto.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-1.6f, -0.5f) * PaperPx, new Vector2(2.6f, 1.46f) * PaperPx);
        paper.gameObject.SetActive(false);
    }

    void PaperText(string text, Vector2 pos, Vector2 box, Color c)
    {
        var t = ToyKit.UIText("Text", paper, font, text, 40f, c);
        t.enableAutoSizing = true;
        t.fontSizeMin = 8f;
        t.fontSizeMax = 200f;
        ToyKit.Anchor(t.rectTransform, new Vector2(0.5f, 0.5f), pos * PaperPx, box * PaperPx);
    }

    RawImage paperPhoto;

    IEnumerator SpinIn(Transform t, float dur)
    {
        // 写真は直前の試合のカット
        if (paperPhoto != null && snaps.Count > 0) { paperPhoto.texture = snaps[snaps.Count - 1]; paperPhoto.color = Color.white; }
        t.gameObject.SetActive(true);
        yield return Tween(dur, u =>
        {
            float e = 1f - Mathf.Pow(1f - u, 3f);
            t.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, e);
            t.localRotation = Quaternion.Euler(0f, 0f, (1f - e) * 900f - 4f);
        });
    }


    // 6・9. 全国大会のステージ
    void BuildArena()
    {
        var root = SetBase("Arena", ArenaSet);
        var floor = ToyKit.Primitive(PrimitiveType.Cylinder, "Stage", root);
        floor.position = ArenaSet + Vector3.down * 0.15f;
        floor.localScale = new Vector3(10f, 0.15f, 10f);
        floor.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.12f, 0.1f, 0.16f), 0.85f);
        var ring = new GameObject("Edge").AddComponent<LineRenderer>();
        ring.transform.SetParent(root, false);
        ring.loop = true;
        ring.positionCount = 64;
        for (int i = 0; i < 64; i++)
        {
            float a = i / 64f * Mathf.PI * 2f;
            ring.SetPosition(i, ArenaSet + new Vector3(Mathf.Cos(a) * 5f, 0.02f, Mathf.Sin(a) * 5f));
        }
        ring.widthMultiplier = 0.08f;
        ring.material = TitleDressingKit.Unlit(new Color(1f, 0.5f, 0.2f) * 2.5f);
        // 奥の横断幕
        Box(root, "BackWall", ArenaSet + new Vector3(0f, 3f, 8f), new Vector3(24f, 8f, 0.3f), ToyKit.Lit(new Color(0.06f, 0.05f, 0.1f), 0.5f));
        var banner = Box(root, "Banner", ArenaSet + new Vector3(0f, 4.2f, 7.8f), new Vector3(9f, 1.6f, 0.05f), ToyKit.Lit(new Color(0.85f, 0.12f, 0.1f), 0.3f));
        Write(root, "全国大会 決勝", banner.position + new Vector3(0f, 0f, -0.04f), new Vector2(8.4f, 1.3f), 12f, Color.white, Quaternion.identity);
        // スポットライト（最初は消えている）
        spots = new List<Light>();
        foreach (float x in new[] { -1.6f, 1.6f })
        {
            var lg = new GameObject("Spot").AddComponent<Light>();
            lg.transform.SetParent(root, false);
            lg.transform.position = ArenaSet + new Vector3(x, 9f, -3f);
            lg.transform.LookAt(ArenaSet + new Vector3(x, 0f, 0f));
            lg.type = LightType.Spot;
            lg.spotAngle = 30f;
            lg.range = 20f;
            lg.intensity = 0f;
            lg.color = new Color(1f, 0.92f, 0.8f);
            spots.Add(lg);
        }
    }

    List<Light> spots;

    IEnumerator SpotOn()
    {
        for (int i = 0; i < spots.Count; i++)
        {
            yield return Tween(0.25f, u => { });
            spots[i].intensity = 60f;
        }
    }

    // 7. 思い出の写真（コルクボードにそれまでのカットの写真）
    void BuildPhotos()
    {
        var root = SetBase("Photos", PhotoSet);
        Box(root, "Cork", PhotoSet + new Vector3(0f, 0f, 0.3f), new Vector3(16f, 6f, 0.3f), ToyKit.Lit(new Color(0.7f, 0.5f, 0.3f), 0.1f));
        var white = ToyKit.Lit(new Color(0.97f, 0.97f, 0.95f), 0.2f);
        var pin = ToyKit.Lit(new Color(0.9f, 0.2f, 0.2f), 0.6f);
        var notes = new[] { "部室にて", "毎日素振り！", "走れ！！", "必勝祈願", "託されたハンマー" };   // 決勝はまだ戦っている最中なので、写真にはしない
        int count = Mathf.Max(1, snaps.Count);
        for (int i = 0; i < count; i++)
        {
            float x = -6f + i * (12f / Mathf.Max(1, count - 1));
            float y = (i % 2 == 0 ? 0.4f : -0.5f);
            float angle = (i % 2 == 0 ? -6f : 5f) + i;
            // 写真どうしは少し重なるので、1枚ずつ手前にずらす（同じ奥行きだと重なったところがチラつく）
            var frame = ToyKit.Primitive(PrimitiveType.Cube, "Polaroid", root);
            frame.position = PhotoSet + new Vector3(x, y, 0.1f - i * 0.12f);
            frame.rotation = Quaternion.Euler(0f, 0f, angle);
            frame.localScale = new Vector3(3f, 2.4f, 0.03f);
            frame.GetComponent<Renderer>().sharedMaterial = white;
            if (i < snaps.Count)
            {
                var img = TitleDressingKit.Quad(root, "Picture", frame.position + frame.rotation * new Vector3(0f, 0.2f, -0.05f), new Vector3(2.7f, 1.52f, 1f), TitleDressingKit.Unlit(Color.white, snaps[i]));
                img.rotation = frame.rotation;
            }
            var t = Write(root, notes[i % notes.Length], frame.position + frame.rotation * new Vector3(0f, -0.88f, -0.05f), new Vector2(2.6f, 0.4f), 3f, new Color(0.15f, 0.2f, 0.5f), frame.rotation);
            var p = ToyKit.Primitive(PrimitiveType.Sphere, "Pin", root);
            p.position = frame.position + frame.rotation * new Vector3(0f, 1.1f, -0.06f);
            p.localScale = Vector3.one * 0.18f;
            p.GetComponent<Renderer>().sharedMaterial = pin;
            _ = t;
        }
        Write(root, "みんなで全国へ！", PhotoSet + new Vector3(4.6f, 2.4f, 0.08f), new Vector2(4f, 0.8f), 6f, new Color(0.85f, 0.15f, 0.12f), Quaternion.Euler(0f, 0f, 6f));
    }

    void OnDisable()
    {
        Time.timeScale = 1f;
        SetSunset(false);
        SetMemory(false);
    }

    void OnDestroy()
    {
        foreach (var s in snaps) if (s != null) s.Release();
        if (rt != null) rt.Release();
    }
}
