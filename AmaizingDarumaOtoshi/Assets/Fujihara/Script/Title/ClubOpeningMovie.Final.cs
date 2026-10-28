using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// オープニングムービーの「全国大会の決勝」。予選と同じ試合場（机の上のトレイ）で、王者と一騎打ち。
//  1. ある程度積んだ状態で向かい合う（スポットライト・顔見せ）
//  2. お互いに振りかぶって、ハンマー同士がぶつかる → はじかれて一度引く
//  3. 打ち合いのラッシュ：積み木が飛び散りまくる
//  4. 王者が最後の1個になったところで終わり（このあと 振りかぶる → 回想 → ホワイトアウト は本体の台本）
public partial class ClubOpeningMovie
{
    GPuppet fHero, fChamp;

    static readonly Quaternion GHammerClash = Quaternion.Euler(-5f, -28f, 0f);   // ハンマーの頭が2人の真ん中で正面からぶつかる向き

    IEnumerator FinalFight()
    {
        float t0 = mt;
        Flash();
        movieCam.backgroundColor = new Color(0.25f, 0.17f, 0.1f);
        SetupFinalField();
        GPuppet hero = fHero, champ = fChamp;
        var mid = (hero.Pos + champ.Pos) * 0.5f;
        var side = FrontSide(hero, champ);

        // ---- 1. 向かい合う ----
        SetShot(lt => MatchSet + new Vector3(0f, 9.5f - lt * 0.8f, -10f + lt * 0.8f), lt => mid + Vector3.up * 1.4f, 46f);
        StartCoroutine(FinalSpotsOn());
        yield return Until(t0 + 1.2f);
        // 王者の顔 → 主人公の顔
        SetShot(lt => champ.Head + champ.root.forward * (2.0f - lt * 0.3f) - side * 0.5f + Vector3.up * 0.15f, lt => champ.Head, 36f);
        yield return Until(t0 + 1.9f);
        SetShot(lt => hero.Head + hero.root.forward * (2.0f - lt * 0.3f) + side * 0.5f + Vector3.up * 0.15f, lt => hero.Head, 36f);
        yield return Until(t0 + 2.6f);

        // ---- 2. ハンマー同士がぶつかる ----
        Flash();
        SetShot(lt => mid + side * (6.0f - lt * 0.6f) + Vector3.up * 4.0f, lt => mid + Vector3.up * 3.4f, 44f);
        StartCoroutine(Tween(0.35f, u => hero.pivotRot = champ.pivotRot = Quaternion.Slerp(GHammerIdle, GHammerWindup, Mathf.SmoothStep(0f, 1f, u))));
        yield return Until(t0 + 2.95f);
        // 角度をそのまま補間して前へ振る（Slerp だと体の後ろを回る）
        yield return Tween(0.12f, u => hero.pivotRot = champ.pivotRot = Quaternion.Euler(Mathf.Lerp(-10f, -5f, u), Mathf.Lerp(125f, -28f, u), 0f));
        var clashAt = (FindPart(hero.pivot, "HammerHead").position + FindPart(champ.pivot, "HammerHead").position) * 0.5f;
        Sparks(clashAt, 40, new Color(2.4f, 2f, 0.8f));
        StartCoroutine(FadeFlash(Color.white, 0.6f, 0f, 0.2f));
        camShake = 1.2f;
        StartCoroutine(SlowMo(0.12f, 0.45f));
        // はじかれて、お互いに一度引く
        var dir = Flat(champ.Pos - hero.Pos).normalized;
        StartCoroutine(Slide(hero, hero.Pos - dir * 1.4f, 0.35f));
        StartCoroutine(Slide(champ, champ.Pos + dir * 1.4f, 0.35f));
        StartCoroutine(Tween(0.3f, u => hero.pivotRot = champ.pivotRot = Quaternion.Slerp(GHammerClash, GHammerIdle, u)));
        yield return Until(t0 + 3.9f);

        // ---- 3. ラッシュ ----
        // 駆け寄って、振りまくる
        StartCoroutine(Slide(hero, mid - dir * 1.3f, 0.22f));
        yield return Slide(champ, mid + dir * 1.3f, 0.22f);
        // ラッシュ（ドラゴンボールのような超高速の打ち合い）
        //   H：主人公が当てる　C：王者が当てる　X：ハンマー同士が激突　T：2人が残像を残して瞬間移動し、別の角度で打ち合う
        //   主人公の H は王者の積み木の数（最後は首だけ）、王者の C は主人公の積み木の数 − 1（最後の1個は残る）
        float r0 = mt;
        shakeFloor = rushShake;   // ラッシュのあいだはずっと揺らす（強さは Inspector で）
        SetRushFx(true, hero, champ);
        System.Func<float> bodyH = () => (hero.bodyY + champ.bodyY) * 0.5f;
        System.Func<Vector3> center = () => (hero.Pos + champ.Pos) * 0.5f;
        System.Func<Vector3> axis = () => Flat(champ.Pos - hero.Pos).normalized;
        System.Func<Vector3> front = () => FrontSide(hero, champ);
        // カメラは数カットごとに切り替える（どれも 2 人の位置と向きに追従）
        System.Func<float, Vector3>[] camPositions =
        {
            lt => center() + front() * (4.4f - lt * 0.6f) + Vector3.up * (bodyH() * 0.6f + 0.6f),                        // 低い横
            lt => hero.Pos - axis() * 2.8f + front() * 1.5f + Vector3.up * (bodyH() + 1.1f),                                // 主人公の肩越し
            lt => champ.Pos + axis() * 2.8f + front() * 1.5f + Vector3.up * (bodyH() + 1.0f),                               // 王者の肩越し
            lt => center() + Quaternion.Euler(0f, lt * 70f, 0f) * (front() * 5.2f) + Vector3.up * 4.2f,                      // 上から回り込み
            lt => center() + front() * 3.8f - axis() * 0.8f + Vector3.up * 0.5f,                                             // 低い位置から見上げる
        };
        System.Func<float, Vector3>[] camLooks =
        {
            lt => center() + Vector3.up * (bodyH() * 0.75f + 0.3f),
            lt => champ.Pos + Vector3.up * (bodyH() * 0.6f + 0.3f),
            lt => hero.Pos + Vector3.up * (bodyH() * 0.6f + 0.3f),
            lt => center() + Vector3.up * 0.6f,
            lt => center() + Vector3.up * (bodyH() * 0.7f + 0.3f),
        };
        int cut = 0;
        float nextCut = mt;
        const string pattern = "THCXHCTHXCHCTXHCHTCHX";
        foreach (char act in pattern)
        {
            if (mt >= nextCut)
            {
                SetShot(camPositions[cut % camPositions.Length], camLooks[cut % camLooks.Length], 50f);
                cut++;
                nextCut = mt + 1.0f;
            }
            switch (act)
            {
                case 'H': StartCoroutine(RushHit(hero, champ)); break;
                case 'C': StartCoroutine(RushHit(champ, hero)); break;
                case 'X': StartCoroutine(RushClash(hero, champ)); break;
                case 'T': yield return Teleport(hero, champ); break;
            }
            yield return Tween(0.24f, u => { });
        }
        yield return Tween(0.4f, u => { });
        shakeFloor = 0f;
        SetRushFx(false, hero, champ);
        hero.pivotRot = champ.pivotRot = GHammerIdle;
    }

    // ================= ラッシュの演出 =================
    // 超高速の振り（構え 0.05 → 振り抜き 0.05 → 戻し 0.1）
    IEnumerator FastSwing(GPuppet p, System.Action onHit)
    {
        p.swinging = true;
        yield return Tween(0.05f, u => p.pivotRot = Quaternion.Slerp(GHammerIdle, GHammerWindup, u));
        bool hit = false;
        yield return Tween(0.05f, u =>
        {
            p.pivotRot = Quaternion.Euler(Mathf.Lerp(-10f, 15f, u), Mathf.Lerp(125f, -65f, u), 0f);   // 前へ振る
            if (!hit && u >= 0.5f) { hit = true; onHit?.Invoke(); }
        });
        yield return Tween(0.1f, u => p.pivotRot = Quaternion.Slerp(GHammerFollow, GHammerIdle, u));
        p.swinging = false;
    }

    IEnumerator RushHit(GPuppet a, GPuppet v)
    {
        FacePuppet(a, v.Pos);
        FacePuppet(v, a.Pos);
        // 半歩踏み込みながら打つ
        var from = a.Pos;
        var step = Flat(v.Pos - a.Pos).normalized * 0.25f;
        StartCoroutine(Tween(0.1f, u => a.root.position = from + step * Mathf.Sin(u * Mathf.PI)));
        yield return FastSwing(a, () =>
        {
            int before = v.blocks.Count;
            KnockBottom(a, v);
            if (v.blocks.Count == before) return;
            var at = v.Pos + Vector3.up * GBlockH * 0.5f;
            Impact(at, false);
            // 当たった勢いで少しのけぞって戻る（足し算にするとヒットストップ中のフレーム数だけ離れていくので、元の位置からの量で動かす）
            var back = Flat(v.Pos - a.Pos).normalized * 0.3f;
            var vFrom = v.Pos;
            StartCoroutine(Tween(0.2f, u => v.root.position = vFrom + back * Mathf.Sin(u * Mathf.PI)));
        });
    }

    // ハンマー同士がぶつかる（積み木は減らないが、いちばん派手）
    IEnumerator RushClash(GPuppet a, GPuppet b)
    {
        FacePuppet(a, b.Pos);
        FacePuppet(b, a.Pos);
        a.swinging = b.swinging = true;
        yield return Tween(0.05f, u => a.pivotRot = b.pivotRot = Quaternion.Slerp(GHammerIdle, GHammerWindup, u));
        yield return Tween(0.05f, u => a.pivotRot = b.pivotRot = Quaternion.Euler(Mathf.Lerp(-10f, -5f, u), Mathf.Lerp(125f, -28f, u), 0f));
        var at = (FindPart(a.pivot, "HammerHead").position + FindPart(b.pivot, "HammerHead").position) * 0.5f;
        Impact(at, true);
        Sparks(at, 30, new Color(2.6f, 2.2f, 0.9f));
        yield return Tween(0.12f, u => a.pivotRot = b.pivotRot = Quaternion.Slerp(GHammerClash, GHammerIdle, u));
        a.swinging = b.swinging = false;
    }

    // 当たった瞬間：ヒットストップ・ズームパンチ・白黒のインパクトフレーム・衝撃波の輪
    int impactCount;

    void Impact(Vector3 at, bool big)
    {
        impactCount++;
        hitStop = big ? 0.09f : 0.05f;
        fovPunch = big ? 12f : 7f;
        camShake = Mathf.Max(camShake, big ? 1.4f : 0.9f);
        // インパクトフレーム：白と黒を交互に一瞬だけ
        StartCoroutine(FadeFlash(impactCount % 2 == 0 ? Color.white : Color.black, big ? 0.75f : 0.45f, 0f, big ? 0.12f : 0.07f));
        StartCoroutine(Shockwave(at, big ? 4.5f : 2.6f));
        if (big) StartCoroutine(Shockwave(at, 7f));
    }

    IEnumerator Shockwave(Vector3 at, float size)
    {
        var q = TitleDressingKit.Quad(setRoot, "Shockwave", at, Vector3.one * 0.2f, null);
        var m = ToyKit.ParticleMaterial(true, RingTexture());
        q.GetComponent<Renderer>().sharedMaterial = m;
        yield return Tween(0.28f, u =>
        {
            q.rotation = Quaternion.LookRotation(q.position - movieCam.transform.position);   // いつもカメラの方を向く
            q.localScale = Vector3.one * Mathf.Lerp(0.3f, size, 1f - (1f - u) * (1f - u));
            m.SetColor("_BaseColor", new Color(1.8f, 1.6f, 1.1f, 1f - u));
        });
        Destroy(q.gameObject);
    }

    // 残像を残して瞬間移動し、2人の向きを変えて打ち合いを続ける
    float rushAngle;

    IEnumerator Teleport(GPuppet a, GPuppet b)
    {
        rushAngle += Random.Range(60f, 110f) * (Random.value < 0.5f ? -1f : 1f);
        var c = MatchSet + new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1f, 1f));
        var axis = Quaternion.Euler(0f, rushAngle, 0f) * Vector3.right;
        var toA = c - axis * 1.3f;
        var toB = c + axis * 1.3f;
        Afterimage(a);
        Afterimage(b);
        var fa = a.Pos;
        var fb = b.Pos;
        a.trail.emitting = b.trail.emitting = true;
        yield return Tween(0.08f, u =>
        {
            a.root.position = Vector3.Lerp(fa, toA, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.6f;
            b.root.position = Vector3.Lerp(fb, toB, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.6f;
        });
        a.root.position = toA;
        b.root.position = toB;
        a.trail.emitting = b.trail.emitting = false;
        FacePuppet(a, b.Pos);
        FacePuppet(b, a.Pos);
        StartCoroutine(Shockwave(toA + Vector3.up * 0.1f, 2f));
        StartCoroutine(Shockwave(toB + Vector3.up * 0.1f, 2f));
        camShake = Mathf.Max(camShake, 0.8f);
    }

    // 体の形をそのまま写した半透明の影を、その場に少し残して消す
    void Afterimage(GPuppet p)
    {
        var ghost = Instantiate(p.root.gameObject, p.root.position, p.root.rotation, setRoot);
        foreach (var tr in ghost.GetComponentsInChildren<TrailRenderer>(true)) Destroy(tr);
        foreach (var ps in ghost.GetComponentsInChildren<ParticleSystem>(true)) Destroy(ps.gameObject);
        var mat = ToyKit.ParticleMaterial(true, null);
        foreach (var r in ghost.GetComponentsInChildren<Renderer>(true)) { r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        StartCoroutine(FadeGhost(ghost, mat));
    }

    IEnumerator FadeGhost(GameObject ghost, Material mat)
    {
        yield return Tween(0.35f, u => mat.SetColor("_BaseColor", new Color(1f, 0.9f, 0.6f, 0.55f * (1f - u))));
        Destroy(ghost);
    }

    // ラッシュのあいだ：画面の集中線と、2人のオーラ
    RawImage speedLines;
    readonly System.Collections.Generic.List<ParticleSystem> auras = new System.Collections.Generic.List<ParticleSystem>();
    bool rushFx;

    void SetRushFx(bool on, GPuppet a, GPuppet b)
    {
        rushFx = on;
        if (on && speedLines == null)
        {
            speedLines = new GameObject("SpeedLines", typeof(RectTransform)).AddComponent<RawImage>();
            speedLines.transform.SetParent(ui.transform, false);
            speedLines.transform.SetSiblingIndex(flash.transform.GetSiblingIndex());
            speedLines.texture = SpeedLineTexture();
            speedLines.raycastTarget = false;
            var r = speedLines.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(2600f, 2600f);   // 画面より大きく（回しても角が出ない）
        }
        if (speedLines != null) speedLines.gameObject.SetActive(on);
        if (on)
        {
            auras.Add(MakeAura(a, new Color(1.6f, 0.7f, 0.25f)));
            auras.Add(MakeAura(b, new Color(0.7f, 0.85f, 1.6f)));
        }
        else
        {
            foreach (var ps in auras) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            auras.Clear();
        }
    }

    void UpdateRushFx()
    {
        if (!rushFx || speedLines == null) return;
        // 集中線は毎コマ角度と大きさを変えて、ガタガタ動いて見えるように
        speedLines.rectTransform.localEulerAngles = new Vector3(0f, 0f, Random.Range(0f, 360f));
        speedLines.rectTransform.localScale = Vector3.one * Random.Range(0.95f, 1.12f);
        speedLines.color = new Color(1f, 1f, 1f, Random.Range(0.55f, 0.85f));
    }

    // 体のまわりから立ちのぼる光の粒（オーラ）
    ParticleSystem MakeAura(GPuppet p, Color c)
    {
        var go = new GameObject("Aura");
        go.transform.SetParent(p.root, false);
        go.transform.localPosition = Vector3.up * 0.2f;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startColor = c;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;
        var em = ps.emission; em.rateOverTime = 120f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.55f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = ToyKit.ParticleMaterial(true, SoftDot());
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    static Texture2D softDot, ringTex, speedTex;

    static Texture2D SoftDot()
    {
        if (softDot != null) return softDot;
        const int s = 32;
        softDot = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s * 0.5f, s * 0.5f)) / (s * 0.5f);
                px[y * s + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d));
            }
        softDot.SetPixels(px);
        softDot.Apply();
        return softDot;
    }

    static Texture2D RingTexture()
    {
        if (ringTex != null) return ringTex;
        const int s = 128;
        ringTex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s * 0.5f, s * 0.5f)) / (s * 0.5f);
                px[y * s + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) * 9f));
            }
        ringTex.SetPixels(px);
        ringTex.Apply();
        return ringTex;
    }

    // 漫画の集中線：中心は透明、外へ向かって白い線が細く伸びる
    static Texture2D SpeedLineTexture()
    {
        if (speedTex != null) return speedTex;
        const int s = 512;
        speedTex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s];
        var rng = new System.Random(9);
        const int rays = 140;
        var width = new float[rays];
        var start = new float[rays];
        for (int i = 0; i < rays; i++) { width[i] = 0.004f + (float)rng.NextDouble() * 0.012f; start[i] = 0.35f + (float)rng.NextDouble() * 0.25f; }
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 1f) % 1f;
                int i = Mathf.FloorToInt(ang * rays) % rays;
                float center = (i + 0.5f) / rays;
                float off = Mathf.Abs(ang - center) * Mathf.PI * 2f;   // 線の中心からの角度
                float w = width[i] * (0.4f + d);                        // 外ほど太く
                float a = off < w && d > start[i] ? Mathf.Clamp01((d - start[i]) / 0.25f) * (1f - off / w) : 0f;
                px[y * s + x] = new Color(1f, 1f, 1f, a * 0.9f);
            }
        speedTex.SetPixels(px);
        speedTex.Apply();
        return speedTex;
    }

    void SetupFinalField()
    {
        // 予選の選手と積み木を片づけて、同じ試合場を使う
        foreach (var p in gPuppets) if (p.root != null) Destroy(p.root.gameObject);
        gPuppets.Clear();
        foreach (var b in looseBlocks) if (b != null) Destroy(b.gameObject);
        looseBlocks.Clear();
        foreach (var tb in tumbles) if (tb.t != null) Destroy(tb.t.gameObject);
        tumbles.Clear();
        foreach (var s in gShots) if (s.t != null) Destroy(s.t.gameObject);
        gShots.Clear();
        matchRoot.gameObject.SetActive(true);
        matchRunning = true;
        nextDrop = float.MaxValue;   // 決勝は空から積み木を降らせない
        // バンパーも落ちている積み木もない平らな試合場にして、2人だけを目立たせる
        flatField = true;
        if (bumperRoot != null) bumperRoot.gameObject.SetActive(false);

        // 主人公（託された紫のハンマー）と王者（銀の顔、黒いハンマー）
        fHero = CreateGPuppet(Red, true);
        fChamp = CreateGPuppet(ChampFace, false);
        foreach (var r in fChamp.hammer.GetComponentsInChildren<Renderer>(true))
            if (r.name == "HammerHead") r.sharedMaterial = ToyKit.Lit(new Color(0.1f, 0.1f, 0.12f), 0.85f);
            else if (r.name == "Handle") r.sharedMaterial = ToyKit.Lit(new Color(0.75f, 0.77f, 0.82f), 0.8f);
        gPuppets.Add(fHero);
        gPuppets.Add(fChamp);
        foreach (var p in gPuppets) p.scripted = true;
        Place(fHero, L(-1f, 0f), 7);
        Place(fChamp, L(1f, 0f), 7);
        FacePuppet(fHero, fChamp.Pos);
        FacePuppet(fChamp, fHero.Pos);

        // 全国大会らしく：奥に横断幕、上から2本のスポットライト（最初は消えている）
        var banner = Box(setRoot, "FinalBanner", MatchSet + new Vector3(0f, 3.6f, 8.6f), new Vector3(9f, 1.6f, 0.05f), ToyKit.Lit(new Color(0.85f, 0.12f, 0.1f), 0.3f));
        Write(setRoot, "全国大会 決勝", banner.position + new Vector3(0f, 0f, -0.05f), new Vector2(8.4f, 1.3f), 12f, Color.white, Quaternion.identity);
        finalSpots = new Light[2];
        for (int i = 0; i < 2; i++)
        {
            var lg = new GameObject("FinalSpot").AddComponent<Light>();
            lg.transform.SetParent(setRoot, false);
            lg.transform.position = MatchSet + new Vector3(i == 0 ? -3f : 3f, 10f, -4f);
            lg.transform.LookAt(i == 0 ? fHero.Pos : fChamp.Pos);
            lg.type = LightType.Spot;
            lg.spotAngle = 28f;
            lg.range = 25f;
            lg.intensity = 0f;
            lg.color = new Color(1f, 0.93f, 0.8f);
            finalSpots[i] = lg;
        }
    }

    Light[] finalSpots;

    IEnumerator FinalSpotsOn()
    {
        foreach (var lg in finalSpots)
        {
            yield return Tween(0.25f, u => { });
            lg.intensity = 60f;
        }
    }

    // 2人の横で、カメラ側（手前 -Z）の向き
    static Vector3 FrontSide(GPuppet a, GPuppet b)
    {
        var s = Vector3.Cross(Vector3.up, Flat(b.Pos - a.Pos).normalized);
        return Vector3.Dot(s, Vector3.back) < 0f ? -s : s;
    }

    IEnumerator Slide(GPuppet p, Vector3 to, float dur)
    {
        var from = p.Pos;
        p.trail.emitting = true;
        yield return Tween(dur, u =>
        {
            p.root.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, u));
            p.hop = Mathf.Sin(u * Mathf.PI) * 0.12f;
        });
        p.hop = 0f;
        p.trail.emitting = false;
    }

    IEnumerator HitAfter(GPuppet a, GPuppet v, float delay)
    {
        yield return Tween(delay, u => { });
        yield return Swing(a, () => KnockBottom(a, v));
    }

    // 相手の一番下の積み木を、炎の尾を引いて横へはじき飛ばす（王者は首だけまで、主人公は最後の1個は残す）
    void KnockBottom(GPuppet a, GPuppet v)
    {
        if (v.blocks.Count <= (v == fHero ? 1 : 0)) return;
        var d = Flat(v.Pos - a.Pos).normalized;
        var s = Vector3.Cross(Vector3.up, d);
        var b = v.blocks[0];
        v.blocks.RemoveAt(0);
        b.SetParent(matchRoot, true);
        AddFire(b);
        // 場外まで大きく飛ばす（試合場に残して2人の邪魔にならないように）
        ToTumble(b, d * Random.Range(10f, 14f) + s * Random.Range(-4f, 4f) + Vector3.up * Random.Range(5f, 7f));
        Sparks(b.position, 18, new Color(2f, 1.4f, 0.5f));
        // 当たった勢いで、積み木のかけらも飛び散る
        var mat = b.GetComponent<Renderer>().sharedMaterial;
        for (int k = 0; k < 5; k++)
        {
            var chip = ToyKit.Primitive(PrimitiveType.Cylinder, "Chip", setRoot);
            chip.position = b.position + Random.insideUnitSphere * 0.3f;
            float sc = Random.Range(0.22f, 0.4f);
            chip.localScale = new Vector3(sc, sc * 0.3f, sc);
            chip.GetComponent<Renderer>().sharedMaterial = mat;
            flyers.Add(new Flyer { t = chip, v = d * Random.Range(3f, 7f) + s * Random.Range(-6f, 6f) + Vector3.up * Random.Range(3f, 7f), spin = Random.onUnitSphere * 800f, life = 1.2f });
        }
        camShake = Mathf.Max(camShake, 0.6f);
    }

    // 火花（小さな光る粒が飛び散る）
    void Sparks(Vector3 at, int count, Color c)
    {
        var mat = TitleDressingKit.Unlit(c);
        for (int i = 0; i < count; i++)
        {
            var sp = ToyKit.Primitive(PrimitiveType.Cube, "Spark", setRoot);
            sp.position = at;
            sp.localScale = Vector3.one * Random.Range(0.05f, 0.11f);
            var r = sp.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flyers.Add(new Flyer { t = sp, v = Random.onUnitSphere * Random.Range(3f, 7f) + Vector3.up * 2f, spin = Random.onUnitSphere * 900f, life = Random.Range(0.3f, 0.6f) });
        }
    }
}
