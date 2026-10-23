using System.Collections;
using UnityEngine;

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
        MenuSounds.Play(MatchSounds.Instance.Swing);
        // 角度をそのまま補間して前へ振る（Slerp だと体の後ろを回る）
        yield return Tween(0.12f, u => hero.pivotRot = champ.pivotRot = Quaternion.Euler(Mathf.Lerp(-10f, -5f, u), Mathf.Lerp(125f, -28f, u), 0f));
        var clashAt = (FindPart(hero.pivot, "HammerHead").position + FindPart(champ.pivot, "HammerHead").position) * 0.5f;
        Sparks(clashAt, 40, new Color(2.4f, 2f, 0.8f));
        MenuSounds.Play(MatchSounds.Instance.Knock);
        MenuSounds.Play(MatchSounds.Instance.HitBlock);
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
        // ラッシュ：速い振りで交互に打ち合う。王者は首だけに、主人公は最後の1個になるまで
        float r0 = mt;
        int heroHits = champ.blocks.Count, champHits = hero.blocks.Count - 1;
        for (int i = 0; i < heroHits; i++) StartCoroutine(HitAfter(hero, champ, i * 0.42f));
        for (int i = 0; i < champHits; i++) StartCoroutine(HitAfter(champ, hero, 0.21f + i * 0.42f));
        float rushEnd = Mathf.Max(heroHits, champHits) * 0.42f + 0.5f;
        shakeFloor = 0.45f;   // ラッシュのあいだはずっと揺らす
        // 2人の体の高さ（積み木が減るとカメラも下がる）
        System.Func<float> bodyH = () => (hero.bodyY + champ.bodyY) * 0.5f;
        // 低い位置から横に
        SetShot(lt => mid + side * (4.8f - lt * 0.3f) + Vector3.up * (bodyH() * 0.6f + 0.8f), lt => mid + Vector3.up * (bodyH() * 0.75f + 0.3f), 50f);
        yield return Until(r0 + 0.8f);
        // 主人公の肩越し
        SetShot(lt => hero.Pos - dir * 3.0f + side * 1.6f + Vector3.up * (bodyH() + 1.2f), lt => champ.Pos + Vector3.up * (bodyH() * 0.6f + 0.3f), 48f);
        yield return Until(r0 + 1.6f);
        // 王者の肩越し
        SetShot(lt => champ.Pos + dir * 3.0f + side * 1.6f + Vector3.up * (bodyH() + 1.0f), lt => hero.Pos + Vector3.up * (bodyH() * 0.6f + 0.3f), 48f);
        yield return Until(r0 + 2.3f);
        // 上から回り込みながら、飛び散る積み木を見せる
        SetShot(lt => mid + Quaternion.Euler(0f, -40f + lt * 45f, 0f) * (side * 5.6f) + Vector3.up * 4.4f, lt => mid + Vector3.up * 0.6f, 50f);
        yield return Until(r0 + rushEnd);
        shakeFloor = 0f;
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

        // まわりにいくつか積み木が落ちている（試合場らしく）
        var rng = new System.Random(31);
        for (int i = 0; i < 10; i++)
        {
            var want = L(((float)rng.NextDouble() * 2f - 1f) * (TrayHalfW - 1f), ((float)rng.NextDouble() * 2f - 1f) * (TrayHalfD - 1f));
            if (Flat(want - MatchSet).magnitude < 3f) continue;
            var spot = FreeSpot(want);
            if (!spot.HasValue) continue;
            var b = CreateGBlock((Nakahira.TumikiColor)(i % 4));
            b.position = spot.Value + Vector3.up * GBlockH * 0.5f;
            b.rotation = Quaternion.Euler(0f, rng.Next(360), 0f);
            looseBlocks.Add(b);
        }

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
            MenuSounds.Play(MatchSounds.Instance.BlockHitBlock);
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
        ToTumble(b, d * Random.Range(5f, 8f) + s * Random.Range(-5f, 5f) + Vector3.up * Random.Range(4f, 7f));
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
        MenuSounds.Play(MatchSounds.Instance.HitBlock);
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
