using System.Collections;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.Rendering;

// オープニングムービーの「地区大会の試合」（実際のゲーム画面ふう。UI はなし）。
// 机の上の木のトレイが試合場。丸いバンパー、散らばった円柱の積み木、4人が煙を引いて走り回り、
// 積み木を拾って積み、自分の一番下の積み木を叩き出して炎の尾を引いて飛ばし、飛んできた積み木を打ち返す。
// 4人は顔の色で見分ける：主人公＝赤、ライバル＝紫（紫の頭に金の帯のハンマー）、ほか＝青・緑。
//  1. 試合前：主人公とライバルの顔見せ
//  2. 乱戦 → 主人公が青を、ライバルが緑を撃墜
//  3. 主人公とライバルの一騎打ち（打ち返し → 積み木を削る → 最後はハンマーで直接）
//  4. ライバルから主人公へハンマーを託す → 映像の上に新聞が重なる
// 見どころ以外の動きは簡単な自動の試合。
public partial class ClubOpeningMovie
{
    const float TrayHalfW = 9f, TrayHalfD = 6.5f;   // トレイの内側の半分の大きさ
    const float GBlockH = 0.5f;                     // 本編の積み木（円柱 1 × 0.5）
    const float GHeadD = 0.9f;                      // 本編の頭（球 0.9）

    static readonly Color RivalFace = new Color(0.6f, 0.35f, 0.85f);
    static readonly Color RivalHammerHead = new Color(0.5f, 0.28f, 0.78f), RivalHammerBand = new Color(1f, 0.8f, 0.2f),
                          RivalHammerHandle = new Color(0.22f, 0.18f, 0.26f);

    static readonly Quaternion GHammerIdle = Quaternion.Euler(-55f, 25f, 0f);
    static readonly Quaternion GHammerWindup = Quaternion.Euler(-10f, 125f, 0f);
    static readonly Quaternion GHammerFollow = Quaternion.Euler(15f, -65f, 0f);

    class GPuppet
    {
        public Transform root, body, pivot, hammer;
        public readonly List<Transform> blocks = new List<Transform>();
        public float bodyY, hop, cooldown;
        public Quaternion pivotRot = GHammerIdle;
        public Vector3 vel;
        public bool swinging, reflectReady, holdFire, koable, out_, scripted, tremble;
        public Vector3? goal;
        public GPuppet target;
        public TrailRenderer trail;
        public Vector3 Pos => root.position;
        public Vector3 Head => body.position + Vector3.up * GHeadD * 0.5f;
    }

    class GShot
    {
        public Transform t;
        public Vector3 dir;
        public float speed;
        public GPuppet owner;
        public bool reflected;
        public System.Action onReflect;
    }

    class GTumble { public Transform t; public Vector3 v, spin; }

    Transform matchRoot;
    readonly List<GPuppet> gPuppets = new List<GPuppet>();
    readonly List<Transform> looseBlocks = new List<Transform>();
    readonly List<GShot> gShots = new List<GShot>();
    readonly List<GTumble> tumbles = new List<GTumble>();
    bool matchRunning;
    float nextDrop;
    Material smokeMat, fireMat;
    readonly Vector2[] bumpers = { new Vector2(-5f, 3.2f), new Vector2(5f, 3.2f), new Vector2(-5.5f, -3f), new Vector2(5.5f, -3f) };
    Transform bumperRoot;
    // 決勝：バンパーなしの平らな試合場。飛んだ積み木はふちで跳ね返らず、場外へ落ちて消える（2人を目立たせる）
    bool flatField;

    Vector3 L(float x, float z) => MatchSet + new Vector3(x, 0f, z);

    // ================= 台本 =================
    IEnumerator MatchScene()
    {
        float t0 = mt;
        Flash();
        movieCam.backgroundColor = new Color(0.25f, 0.17f, 0.1f);
        StartMatch();
        GPuppet hero = gPuppets[0], other1 = gPuppets[1], rival = gPuppets[2], other2 = gPuppets[3];
        foreach (var p in gPuppets) { p.holdFire = true; p.goal = p.Pos; FacePuppet(p, MatchSet); }

        // ---- 1. 試合前：顔見せ ----
        SetShot(lt => MatchSet + new Vector3(0f, 12.5f - lt * 0.6f, -9.8f + lt * 0.5f), lt => MatchSet + new Vector3(0f, 0f, 0.8f), 50f);
        yield return Until(t0 + 0.9f);
        // 主人公：正面から寄る。ぴょんと跳ねてハンマーを掲げる
        SetShot(lt => hero.Pos + hero.root.forward * (2.6f - lt * 0.4f) + Vector3.up * (1.3f + hero.bodyY), lt => hero.Head, 40f);
        StartCoroutine(Tween(0.3f, u => hero.hop = Mathf.Sin(u * Mathf.PI) * 0.45f));
        StartCoroutine(Tween(0.35f, u => hero.pivotRot = Quaternion.Slerp(GHammerIdle, Quaternion.Euler(-95f, 0f, 0f), Mathf.SmoothStep(0f, 1f, u))));
        yield return Until(t0 + 1.9f);
        // ライバル：正面から寄る。ハンマーをくるくる回して余裕を見せる
        SetShot(lt => rival.Pos + rival.root.forward * (2.6f - lt * 0.4f) + Vector3.up * (1.3f + rival.bodyY), lt => rival.Head, 40f);
        StartCoroutine(Tween(0.8f, u => rival.pivotRot = Quaternion.Euler(-55f, 25f + Mathf.SmoothStep(0f, 1f, u) * 720f, 0f)));
        yield return Until(t0 + 2.9f);
        // 試合開始
        hero.pivotRot = GHammerIdle;
        foreach (var p in gPuppets) { p.holdFire = false; p.goal = null; }

        // ---- 2. 乱戦 ----
        Flash();
        SetShot(lt => MatchSet + Orbit(-70f + lt * 30f, 8.8f, 3.4f), lt => MatchSet + Vector3.up * 0.5f, 50f);
        yield return Until(t0 + 4.2f);

        // 主人公が青を撃墜（2発）
        Flash();
        Place(hero, L(-2.6f, -2.2f), 3);
        Place(other1, L(2.2f, -1.6f), 1);
        Place(rival, L(4f, 4.6f), 3);
        Place(other2, L(-5.5f, 4.6f), 1);
        other1.koable = true;
        FacePuppet(hero, other1.Pos);
        FacePuppet(other1, hero.Pos);
        ClearLane(hero.Pos, other1.Pos);
        SetShot(lt => (hero.Pos + other1.Pos) * 0.5f + new Vector3(-0.6f, 2.6f, -5.8f + lt * 0.4f), lt => (hero.Pos + other1.Pos) * 0.5f + Vector3.up * 0.8f, 46f);
        yield return Until(t0 + 4.45f);
        StartCoroutine(Fire(hero, other1, null));
        yield return Until(t0 + 5.05f);
        StartCoroutine(Fire(hero, other1, null));
        yield return Until(t0 + 6.1f);

        // ライバルが緑を撃墜（肩越しに）
        Flash();
        Place(rival, L(2.6f, 2.0f), 3);
        Place(other2, L(-1.9f, 2.8f), 0);
        other2.koable = true;
        other2.tremble = true;
        hero.goal = hero.Pos;
        FacePuppet(rival, other2.Pos);
        FacePuppet(other2, rival.Pos);
        ClearLane(rival.Pos, other2.Pos);
        SetShot(lt =>
        {
            var d = Flat(other2.Pos - rival.Pos).normalized;
            return rival.Pos - d * 3.6f + Vector3.Cross(Vector3.up, d) * -2.2f + Vector3.up * 3.2f;
        }, lt => Vector3.Lerp(rival.Pos, other2.Pos, 0.6f) + Vector3.up * 0.7f, 46f);
        yield return Until(t0 + 6.4f);
        StartCoroutine(Fire(rival, other2, null));
        yield return Until(t0 + 7.5f);

        // ---- 3. 一騎打ち ----
        Flash();
        Place(hero, L(-3f, -0.6f), 3);
        Place(rival, L(3f, 0.4f), 3);
        hero.holdFire = rival.holdFire = true;
        FacePuppet(hero, rival.Pos);
        FacePuppet(rival, hero.Pos);
        ClearLane(hero.Pos, rival.Pos);
        var mid = (hero.Pos + rival.Pos) * 0.5f;
        var side = Vector3.Cross(Vector3.up, Flat(rival.Pos - hero.Pos).normalized);
        if (Vector3.Dot(side, Vector3.back) < 0f) side = -side;   // 手前（-Z）側から見る
        SetShot(lt => mid + side * (6.6f - lt * 0.8f) + Vector3.up * 3.0f, lt => mid + Vector3.up * 0.8f, 46f);
        hero.reflectReady = true;
        yield return Until(t0 + 7.8f);
        // ライバルが撃った積み木を、主人公が打ち返す
        StartCoroutine(Fire(rival, hero, () => StartCoroutine(SlowMo(0.2f, 0.45f))));
        yield return Until(t0 + 9.0f);
        hero.reflectReady = false;
        // 主人公の肩越し：続けて撃って、ライバルの積み木を削る
        SetShot(lt =>
        {
            var d = Flat(rival.Pos - hero.Pos).normalized;
            var s = Vector3.Cross(Vector3.up, d);
            if (Vector3.Dot(s, Vector3.back) < 0f) s = -s;   // 手前側から（奥の小物でライバルが隠れないように）
            return hero.Pos - d * 3.0f + s * 2.8f + Vector3.up * 2.8f;
        }, lt => Vector3.Lerp(hero.Pos, rival.Pos, 0.75f) + Vector3.up * 0.6f, 46f);
        StartCoroutine(Fire(hero, rival, null));
        yield return Until(t0 + 9.6f);
        StartCoroutine(Fire(hero, rival, null));
        yield return Until(t0 + 10.2f);
        // 首だけになったライバルに寄る
        rival.tremble = true;
        SetShot(lt => rival.Head + rival.root.forward * 2.2f + rival.root.right * 0.8f + Vector3.up * 0.3f, lt => rival.Head, 40f);
        yield return Until(t0 + 10.9f);
        // 最後は駆け寄ってハンマーで直接
        SetShot(lt => mid + side * 4.4f + Vector3.up * 1.6f, lt => Vector3.Lerp(hero.Pos, rival.Pos, 0.7f) + Vector3.up * 0.6f, 46f);
        var reach = rival.Pos - Flat(rival.Pos - hero.Pos).normalized * 1.3f;
        yield return Dash(hero, reach, 0.3f);
        yield return Swing(hero, () =>
        {
            rival.tremble = false;
            camShake = 1f;
            StartCoroutine(SlowMo(0.15f, 0.5f));
            StartCoroutine(KnockDown(rival, Flat(rival.Pos - hero.Pos).normalized * 2.4f));
        });
        yield return Until(t0 + 12.4f);

        // ---- 4. ハンマーを託す ----
        Flash();
        Place(hero, L(-0.9f, -0.4f), 1);
        hero.pivotRot = GHammerIdle;
        rival.root.position = L(0.9f, 0.1f);
        rival.root.rotation = Quaternion.identity;
        rival.tremble = false;
        FacePuppet(hero, rival.Pos);
        FacePuppet(rival, hero.Pos);
        hero.scripted = rival.scripted = true;
        var hm = (hero.Pos + rival.Pos) * 0.5f;
        // 2人のまわりに転がっている積み木は片づける（大事なカットなので、手前に何も映らないように）
        for (int i = looseBlocks.Count - 1; i >= 0; i--)
            if (Flat(looseBlocks[i].position - hm).magnitude < 4.5f) { Destroy(looseBlocks[i].gameObject); looseBlocks.RemoveAt(i); }
        foreach (var tb in tumbles) Destroy(tb.t.gameObject);
        tumbles.Clear();
        foreach (var s in gShots) Destroy(s.t.gameObject);
        gShots.Clear();
        nextDrop = float.MaxValue;   // もう空から降らせない
        SetShot(lt => hm + new Vector3(0.3f - lt * 0.2f, 0.9f, -3.4f + lt * 0.25f), lt => hm + Vector3.up * 0.55f, 40f);
        // ライバルがうなずいて、ハンマーを差し出す
        yield return Tween(0.35f, u => rival.hop = Mathf.Sin(u * Mathf.PI) * 0.12f);
        var offer = Quaternion.Euler(-12f, -35f, 0f);
        yield return Tween(0.5f, u => rival.pivotRot = Quaternion.Slerp(GHammerIdle, offer, Mathf.SmoothStep(0f, 1f, u)));
        yield return Until(t0 + 13.6f);
        // 主人公が受け取る（自分のハンマーは置いて、ライバルのハンマーを手に）
        hero.hammer.gameObject.SetActive(false);
        hero.pivotRot = Quaternion.Euler(-12f, 35f, 0f);
        hero.pivot.localRotation = hero.pivotRot;
        var given = rival.hammer;
        var fromPos = given.position;
        var fromRot = given.rotation;
        yield return Tween(0.45f, u =>
        {
            float e = Mathf.SmoothStep(0f, 1f, u);
            given.position = Vector3.Lerp(fromPos, hero.pivot.position, e);
            given.rotation = Quaternion.Slerp(fromRot, hero.pivot.rotation, e);
        });
        given.SetParent(hero.pivot, false);
        given.localPosition = Vector3.zero;
        given.localRotation = Quaternion.identity;
        hero.hammer = given;
        rival.pivotRot = GHammerIdle;
        yield return Until(t0 + 14.4f);
        // 受け取ったハンマーを高く掲げる（下から見上げる）
        var heroSide = -hero.root.right;
        if (Vector3.Dot(heroSide, Vector3.back) < 0f) heroSide = -heroSide;   // 手前側から
        SetShot(lt => hero.Pos + heroSide * (3.0f - lt * 0.3f) + hero.root.forward * 0.4f + Vector3.up * 0.4f, lt => hero.Pos + Vector3.up * 1.5f, 52f);
        StartCoroutine(Tween(0.3f, u => hero.hop = Mathf.Sin(u * Mathf.PI) * 0.4f));
        yield return Tween(0.4f, u => hero.pivotRot = Quaternion.Slerp(Quaternion.Euler(-12f, 35f, 0f), Quaternion.Euler(-95f, 0f, 0f), Mathf.SmoothStep(0f, 1f, u)));   // 柄をまっすぐ上に
        yield return Until(t0 + 15.3f);
        Snap();   // 新聞の写真・思い出の写真になる

        // 映像を止めずに、上から新聞が回転しながら重なる
        var frozenPos = movieCam.transform.position;
        var frozenLook = frozenPos + movieCam.transform.forward * 10f;
        SetShot(lt => frozenPos, lt => frozenLook, 46f);
        StartCoroutine(SpinIn(paper, 0.55f));
        yield return Until(t0 + 15.85f);
        camShake = 0.6f;
        yield return Until(t0 + 17.8f);
        paper.gameObject.SetActive(false);
        matchRunning = false;
        matchRoot.gameObject.SetActive(false);
    }

    IEnumerator Until(float time) { while (mt < time) yield return null; }

    // カットの頭で選手を置き直す（積み木の数もそろえる）
    void Place(GPuppet p, Vector3 pos, int blocks)
    {
        p.root.position = pos;
        p.vel = Vector3.zero;
        p.goal = pos;
        while (p.blocks.Count < blocks) GiveBlock(p);
        while (p.blocks.Count > blocks) { var b = p.blocks[0]; p.blocks.RemoveAt(0); Destroy(b.gameObject); }
        p.bodyY = GBlockH * p.blocks.Count;
        for (int i = 0; i < p.blocks.Count; i++) p.blocks[i].localPosition = new Vector3(0f, GBlockH * (i + 0.5f), 0f);
    }

    // 2人のあいだ（とカメラ側）に落ちている積み木を片づける
    // （奥へ寄せると、寄せた積み木どうしが重なって見苦しくなるので消す）
    void ClearLane(Vector3 a, Vector3 b)
    {
        for (int i = looseBlocks.Count - 1; i >= 0; i--)
        {
            var p = Flat(looseBlocks[i].position);
            var ab = Flat(b - a);
            float u = Mathf.Clamp01(Vector3.Dot(p - Flat(a), ab) / ab.sqrMagnitude);
            var near = Flat(a) + ab * u;
            if ((p - near).magnitude < 1.6f || (p.z < near.z && (p - near).magnitude < 4f))
            {
                Destroy(looseBlocks[i].gameObject);
                looseBlocks.RemoveAt(i);
            }
        }
    }

    // 積み木どうし・バンパー・人と重ならない置き場所を、want の近くから探す（見つからなければ null）
    const float BlockGap = 1.1f;   // 積み木（直径 1）どうしの中心の最小距離

    Vector3? FreeSpot(Vector3 want)
    {
        for (int ring = 0; ring < 6; ring++)
            for (int k = 0; k < (ring == 0 ? 1 : 8); k++)
            {
                float a = k * Mathf.PI * 0.25f + ring;
                var p = Flat(want - MatchSet) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring * 0.6f;
                if (Mathf.Abs(p.x) > TrayHalfW - 0.7f || Mathf.Abs(p.z) > TrayHalfD - 0.7f || NearBumper(p, 1.9f)) continue;
                var w = MatchSet + p;
                bool ok = true;
                foreach (var lb in looseBlocks) if (Flat(lb.position - w).magnitude < BlockGap) { ok = false; break; }
                if (ok) foreach (var pp in gPuppets) if (!pp.out_ && Flat(pp.Pos - w).magnitude < 0.9f) { ok = false; break; }
                if (ok) return w;
            }
        return null;
    }

    // ================= 試合のはじまり =================
    void StartMatch()
    {
        matchRunning = true;
        smokeMat = ToyKit.ParticleMaterial(false, null);
        fireMat = ToyKit.ParticleMaterial(true, null);
        fireMat.SetColor("_BaseColor", new Color(2.6f, 1.7f, 0.7f, 1f));   // 光って見えるように明るく（ブルームがかかる）
        // 主人公（赤）・青・ライバル（紫）・緑
        var starts = new[] { new Vector3(-6.5f, 0f, -4.5f), new Vector3(6.5f, 0f, -4.5f), new Vector3(6.5f, 0f, 4.5f), new Vector3(-6.5f, 0f, 4.5f) };
        var faces = new[] { Red, Blue, RivalFace, Green };
        for (int i = 0; i < 4; i++)
        {
            var p = CreateGPuppet(faces[i], i == 2);
            p.root.position = MatchSet + starts[i];
            for (int k = 0; k < 2; k++) GiveBlock(p);
            p.cooldown = 0.6f + i * 0.35f;
            gPuppets.Add(p);
        }
        for (int i = 0; i < 4; i++) gPuppets[i].target = gPuppets[(i + 1 + (i % 2)) % 4];
        var rng = new System.Random(11);
        for (int i = 0; i < 16; i++)
        {
            var pos = new Vector3(((float)rng.NextDouble() * 2f - 1f) * (TrayHalfW - 1f), 0f, ((float)rng.NextDouble() * 2f - 1f) * (TrayHalfD - 1f));
            var spot = FreeSpot(MatchSet + pos);
            if (!spot.HasValue) continue;
            var b = CreateGBlock((TumikiColor)(i % 4));
            b.position = spot.Value + Vector3.up * GBlockH * 0.5f;
            b.rotation = Quaternion.Euler(0f, rng.Next(360), 0f);
            looseBlocks.Add(b);
        }
        nextDrop = mt + 0.3f;
    }

    // ================= 毎フレーム（自動の試合） =================
    void UpdateMatch(float dt)
    {
        if (!matchRunning) return;

        // 空から積み木を足す
        if (mt >= nextDrop)
        {
            nextDrop = mt + 0.45f;
            if (looseBlocks.Count + tumbles.Count < 16)
            {
                var pos = new Vector3(Random.Range(-TrayHalfW + 1f, TrayHalfW - 1f), 7f, Random.Range(-TrayHalfD + 1f, TrayHalfD - 1f));
                if (!NearBumper(pos, 1.6f))
                {
                    var b = CreateGBlock((TumikiColor)Random.Range(0, 4));
                    b.position = MatchSet + pos;
                    tumbles.Add(new GTumble { t = b, v = Vector3.zero, spin = Random.onUnitSphere * 200f });
                }
            }
        }

        foreach (var p in gPuppets) if (!p.out_ || p.scripted) UpdatePuppet(p, dt);
        UpdateShots(dt);
        UpdateTumbles(dt);
    }

    void UpdatePuppet(GPuppet p, float dt)
    {
        p.cooldown -= dt;
        if (!p.scripted)
        {
            Vector3 desired = Vector3.zero;
            if (!p.swinging)
            {
                if (p.goal.HasValue)
                {
                    var d = Flat(p.goal.Value - p.Pos);
                    if (d.magnitude > 0.2f) desired = d.normalized * 6.5f;
                }
                else
                {
                    // 撃てるなら撃つ。撃てないなら一番近い積み木を拾いに行く
                    var target = p.target != null && !p.target.out_ ? p.target : null;
                    if (!p.holdFire && target != null && p.cooldown <= 0f && p.blocks.Count >= 2 && Flat(target.Pos - p.Pos).magnitude < 11f)
                    {
                        StartCoroutine(Fire(p, target, null));
                    }
                    else
                    {
                        var near = NearestLoose(p.Pos);
                        var goal = near != null ? near.position : MatchSet + new Vector3(Mathf.Sin(mt + p.GetHashCode()) * 5f, 0f, Mathf.Cos(mt * 0.7f) * 3.5f);
                        desired = Flat(goal - p.Pos).normalized * 7f;
                    }
                }
            }
            p.vel = Vector3.Lerp(p.vel, desired, 1f - Mathf.Exp(-8f * dt));
            var pos = p.Pos + p.vel * dt;
            // バンパーとトレイのふちで止まる
            if (!flatField)
                foreach (var bp in bumpers)
                {
                    var c = MatchSet + new Vector3(bp.x, 0f, bp.y);
                    var away = Flat(pos - c);
                    if (away.magnitude < 1.7f) pos = c + away.normalized * 1.7f;
                }
            pos.x = Mathf.Clamp(pos.x, MatchSet.x - TrayHalfW + 0.6f, MatchSet.x + TrayHalfW - 0.6f);
            pos.z = Mathf.Clamp(pos.z, MatchSet.z - TrayHalfD + 0.6f, MatchSet.z + TrayHalfD - 0.6f);
            pos.y = MatchSet.y;
            p.root.position = pos;

            float speed = p.vel.magnitude;
            if (!p.swinging && speed > 0.8f) p.root.rotation = Quaternion.Slerp(p.root.rotation, Quaternion.LookRotation(p.vel), 1f - Mathf.Exp(-14f * dt));
            if (speed > 1f) p.hop = Mathf.Abs(Mathf.Sin(mt * 16f + p.GetHashCode())) * 0.12f;
            p.trail.emitting = speed > 2.5f;

            // 足もとの積み木を拾って、体の一番下に差し込む（台本で積み木の数を決めている人は拾わない）
            if (!p.swinging && !p.goal.HasValue)
                for (int i = looseBlocks.Count - 1; i >= 0; i--)
                {
                    var b = looseBlocks[i];
                    if (Flat(b.position - p.Pos).magnitude > 0.75f) continue;
                    looseBlocks.RemoveAt(i);
                    AttachBlock(p, b);
                    break;
                }
        }

        // 積み木と体（抜けたところはストンと落ちる）
        for (int i = 0; i < p.blocks.Count; i++)
        {
            var lp = p.blocks[i].localPosition;
            lp.y = Mathf.MoveTowards(lp.y, GBlockH * (i + 0.5f), 10f * dt);
            lp.x = Mathf.MoveTowards(lp.x, 0f, 8f * dt);
            lp.z = Mathf.MoveTowards(lp.z, 0f, 8f * dt);
            p.blocks[i].localPosition = lp;
        }
        p.bodyY = Mathf.MoveTowards(p.bodyY, GBlockH * p.blocks.Count, 10f * dt);
        var jitter = p.tremble ? new Vector3(Mathf.Sin(mt * 70f), 0f, Mathf.Cos(mt * 53f)) * 0.035f : Vector3.zero;
        p.body.localPosition = new Vector3(0f, p.bodyY + p.hop, 0f) + jitter;
        p.pivot.localRotation = p.pivotRot;
    }

    void UpdateShots(float dt)
    {
        for (int i = gShots.Count - 1; i >= 0; i--)
        {
            var s = gShots[i];
            s.t.position += s.dir * s.speed * dt;
            s.t.Rotate(Vector3.Cross(Vector3.up, s.dir) * 1500f * dt, Space.World);
            GPuppet hit = null;
            foreach (var p in gPuppets)
                if (p != s.owner && !p.out_ && Flat(p.Pos - s.t.position).magnitude < 0.8f) { hit = p; break; }
            if (hit != null)
            {
                if (hit.reflectReady && !s.reflected)
                {
                    // ハンマーで打ち返す：撃った本人に当たりに行く
                    StartCoroutine(SwingOnly(hit));
                    var shooter = s.owner;
                    s.dir = shooter != null ? Flat(shooter.Pos - s.t.position).normalized : -s.dir;
                    s.speed *= 1.25f;
                    s.owner = hit;
                    s.reflected = true;
                    camShake = Mathf.Max(camShake, 0.8f);
                    s.onReflect?.Invoke();
                    continue;
                }
                Impact(hit, s);
                gShots.RemoveAt(i);
                continue;
            }
            var lp = s.t.position - MatchSet;
            if (Mathf.Abs(lp.x) > TrayHalfW - 0.4f || Mathf.Abs(lp.z) > TrayHalfD - 0.4f)
            {
                // ふちに当たって、はね返って落ちる
                ToTumble(s.t, -s.dir * 3f + Vector3.up * 3f);
                gShots.RemoveAt(i);
            }
        }
    }

    void Impact(GPuppet victim, GShot s)
    {
        camShake = Mathf.Max(camShake, 0.5f);
        if (victim.blocks.Count > 0)
        {
            var b = victim.blocks[0];
            victim.blocks.RemoveAt(0);
            b.SetParent(matchRoot, true);
            ToTumble(b, s.dir * 8f + Vector3.up * 4f);
            AddFire(b);
            if (!victim.scripted) victim.vel += s.dir * 6f;   // 当たった勢いで押される
        }
        else if (victim.koable)
        {
            // 首だけのところに当たった：撃墜（場外へ吹っ飛ぶ）
            victim.out_ = true;
            victim.tremble = false;
            camShake = 1f;
            StartCoroutine(SlowMo(0.3f, 0.4f));
            StartCoroutine(Launch(victim, s.dir));
        }
        ToTumble(s.t, -s.dir * 2.5f + Vector3.up * 4.5f);
    }

    // 撃墜：くるくる回りながら高く飛んで、トレイの外へ消える
    IEnumerator Launch(GPuppet p, Vector3 dir)
    {
        p.scripted = true;
        p.trail.emitting = false;
        var v = dir * 9f + Vector3.up * 9f;
        var spin = Random.onUnitSphere * 900f;
        float s = mt;
        while (mt < s + 1.6f)
        {
            v += Physics.gravity * Time.deltaTime;
            p.root.position += v * Time.deltaTime;
            p.root.Rotate(spin * Time.deltaTime, Space.World);
            yield return null;
        }
        p.root.gameObject.SetActive(false);
    }

    // ライバルを倒す：大きく吹き飛んでトレイの上に転がる（退場はしない。このあとハンマーを託す）
    IEnumerator KnockDown(GPuppet p, Vector3 offset)
    {
        p.out_ = true;
        p.scripted = true;
        p.trail.emitting = false;
        var from = p.Pos;
        var to = from + offset;
        var startRot = p.root.rotation;
        yield return Tween(0.6f, u =>
        {
            p.root.position = Vector3.Lerp(from, to, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 1.6f;
            p.root.rotation = startRot * Quaternion.Euler(u * 540f, 0f, 0f);
        });
        p.root.rotation = startRot * Quaternion.Euler(90f, 0f, 0f);   // あお向けに倒れる
    }

    void UpdateTumbles(float dt)
    {
        for (int i = tumbles.Count - 1; i >= 0; i--)
        {
            var tb = tumbles[i];
            tb.v += Physics.gravity * dt;
            var pos = tb.t.position + tb.v * dt;
            var lp = pos - MatchSet;
            if (flatField)
            {
                // ふちを越えて場外の机へ落ちたら（トレイの中に落ちても）消す
                tb.t.position = pos;
                tb.t.Rotate(tb.spin * dt, Space.World);
                bool outside = Mathf.Abs(lp.x) > TrayHalfW || Mathf.Abs(lp.z) > TrayHalfD;
                if (tb.v.y < 0f && lp.y <= (outside ? -0.4f : GBlockH * 0.5f))
                {
                    tumbles.RemoveAt(i);
                    Destroy(tb.t.gameObject);
                }
                continue;
            }
            if (Mathf.Abs(lp.x) > TrayHalfW - 0.5f) { tb.v.x = -tb.v.x * 0.5f; lp.x = Mathf.Sign(lp.x) * (TrayHalfW - 0.5f); }
            if (Mathf.Abs(lp.z) > TrayHalfD - 0.5f) { tb.v.z = -tb.v.z * 0.5f; lp.z = Mathf.Sign(lp.z) * (TrayHalfD - 0.5f); }
            pos = MatchSet + lp;
            tb.t.position = pos;
            tb.t.Rotate(tb.spin * dt, Space.World);
            if (pos.y <= MatchSet.y + GBlockH * 0.5f && tb.v.y < 0f)
            {
                // 着地したら、また拾える積み木になる。ほかの積み木と重なるところには置かない（空きがなければ消す）
                tumbles.RemoveAt(i);
                var spot = FreeSpot(pos);
                if (!spot.HasValue) { Destroy(tb.t.gameObject); continue; }
                tb.t.position = spot.Value + Vector3.up * GBlockH * 0.5f;
                tb.t.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                var tr = tb.t.GetComponent<TrailRenderer>();
                if (tr != null) tr.emitting = false;
                looseBlocks.Add(tb.t);
            }
        }
    }

    void ToTumble(Transform b, Vector3 v)
    {
        b.SetParent(matchRoot, true);
        tumbles.Add(new GTumble { t = b, v = v, spin = Random.onUnitSphere * 700f });
    }

    // 自分の一番下の積み木を叩き出して、相手へ飛ばす
    IEnumerator Fire(GPuppet p, GPuppet target, System.Action onReflect)
    {
        if (p.swinging || p.blocks.Count == 0) yield break;
        p.vel = Vector3.zero;
        FacePuppet(p, target.Pos);
        yield return Swing(p, () =>
        {
            if (p.blocks.Count == 0) return;
            var b = p.blocks[0];
            p.blocks.RemoveAt(0);
            b.SetParent(matchRoot, true);
            var dir = Flat(target.Pos - p.Pos).normalized;
            b.position = p.Pos + Vector3.up * GBlockH * 0.5f + dir * 0.7f;
            AddFire(b);
            gShots.Add(new GShot { t = b, dir = dir, speed = 15f, owner = p, onReflect = onReflect });
        });
        p.cooldown = Random.Range(0.8f, 1.6f);
    }

    // 横に振る（構え → 振り抜く。途中で onHit）
    IEnumerator Swing(GPuppet p, System.Action onHit)
    {
        p.swinging = true;
        yield return Tween(0.12f, u => p.pivotRot = Quaternion.Slerp(GHammerIdle, GHammerWindup, Mathf.SmoothStep(0f, 1f, u)));
        bool hit = false;
        yield return Tween(0.08f, u =>
        {
            p.pivotRot = Quaternion.Slerp(GHammerWindup, GHammerFollow, u);
            if (!hit && u >= 0.5f) { hit = true; onHit?.Invoke(); }
        });
        yield return Tween(0.18f, u => p.pivotRot = Quaternion.Slerp(GHammerFollow, GHammerIdle, Mathf.SmoothStep(0f, 1f, u)));
        p.swinging = false;
    }

    // 打ち返すときの振り
    IEnumerator SwingOnly(GPuppet p)
    {
        p.swinging = true;
        yield return Tween(0.06f, u => p.pivotRot = Quaternion.Slerp(GHammerWindup, GHammerFollow, u));
        yield return Tween(0.2f, u => p.pivotRot = Quaternion.Slerp(GHammerFollow, GHammerIdle, Mathf.SmoothStep(0f, 1f, u)));
        p.swinging = false;
    }

    // 台本で走らせる（煙を引いて）
    IEnumerator Dash(GPuppet p, Vector3 to, float dur)
    {
        p.scripted = true;
        p.trail.emitting = true;
        var from = p.Pos;
        FacePuppet(p, to);
        yield return Tween(dur, u =>
        {
            p.root.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, u));
            p.hop = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 3f)) * 0.15f;
        });
        p.hop = 0f;
        p.trail.emitting = false;
        p.scripted = false;
        p.goal = p.Pos;
    }

    // ================= 部品 =================
    GPuppet CreateGPuppet(Color color, bool rivalHammer)
    {
        var p = new GPuppet();
        p.root = new GameObject("MatchDaruma").transform;
        p.root.SetParent(matchRoot, false);
        p.body = new GameObject("Body").transform;
        p.body.SetParent(p.root, false);
        var head = ToyKit.Primitive(PrimitiveType.Sphere, "Head", p.body);
        head.localScale = Vector3.one * GHeadD;
        head.localPosition = new Vector3(0f, GHeadD * 0.5f, 0f);
        head.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(color, 0.7f);
        var eye = ToyKit.Lit(new Color(0.08f, 0.07f, 0.09f), 0.9f);
        foreach (float x in new[] { -0.17f, 0.17f })
        {
            var e = ToyKit.Primitive(PrimitiveType.Sphere, "Eye", p.body);
            e.localScale = Vector3.one * 0.14f;
            e.localPosition = new Vector3(x, GHeadD * 0.5f + 0.09f, 0.38f);
            e.GetComponent<Renderer>().sharedMaterial = eye;
        }
        p.pivot = new GameObject("HammerPivot").transform;
        p.pivot.SetParent(p.body, false);
        p.pivot.localPosition = new Vector3(0.5f, 0.35f, 0.05f);
        p.hammer = new GameObject("Hammer").transform;
        p.hammer.SetParent(p.pivot, false);
        var handle = ToyKit.Primitive(PrimitiveType.Cube, "Handle", p.hammer);
        handle.localScale = new Vector3(0.08f, 0.08f, 1f);
        handle.localPosition = new Vector3(0f, 0f, 0.5f);
        handle.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.72f, 0.52f, 0.32f), 0.4f);
        var hd = ToyKit.Primitive(PrimitiveType.Cube, "HammerHead", p.hammer);
        hd.localScale = new Vector3(0.35f, 0.6f, 0.35f);
        hd.localPosition = new Vector3(0f, 0f, 1.05f);
        hd.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(Color.Lerp(color, new Color(0.25f, 0.25f, 0.3f), 0.55f), 0.6f);
        if (rivalHammer)
        {
            // ライバルのハンマー：紫の頭に金の帯（決勝で主人公が持つものと同じ色）
            foreach (float y in new[] { -0.2f, 0.2f })
            {
                var band = ToyKit.Primitive(PrimitiveType.Cube, "Band", p.hammer);
                band.localScale = new Vector3(0.38f, 0.07f, 0.38f);
                band.localPosition = new Vector3(0f, y, 1.05f);
            }
            ApplyRivalHammer(p.hammer);
        }
        // 走ったあとの煙
        p.trail = p.root.gameObject.AddComponent<TrailRenderer>();
        p.trail.time = 0.45f;
        p.trail.widthMultiplier = 0.8f;
        p.trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.2f);
        p.trail.material = smokeMat;
        p.trail.colorGradient = Grad(new Color(0.7f, 0.66f, 0.6f, 0.8f), new Color(0.8f, 0.76f, 0.7f, 0f));
        p.trail.shadowCastingMode = ShadowCastingMode.Off;
        p.trail.emitting = false;
        return p;
    }

    // ライバルのハンマーの色にする（試合の角ばったハンマーにも、決勝の丸いハンマーにも使う）
    static void ApplyRivalHammer(Transform hammer)
    {
        var head = ToyKit.Lit(RivalHammerHead, 0.65f);
        var band = ToyKit.Lit(RivalHammerBand, 0.75f);
        var handle = ToyKit.Lit(RivalHammerHandle, 0.5f);
        foreach (var r in hammer.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name == "Head" || r.name == "HammerHead") r.sharedMaterial = head;
            else if (r.name == "Band") r.sharedMaterial = band;
            else if (r.name == "Handle") r.sharedMaterial = handle;
        }
    }

    static Transform FindPart(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return root;
    }

    Transform CreateGBlock(TumikiColor color)
    {
        var b = ToyKit.Primitive(PrimitiveType.Cylinder, "MatchTumiki", matchRoot);
        b.localScale = new Vector3(1f, GBlockH * 0.5f, 1f);
        b.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(TumikiColorUtil.ToUnityColor(color), 0.55f);
        return b;
    }

    void GiveBlock(GPuppet p) => AttachBlock(p, CreateGBlock((TumikiColor)Random.Range(0, 4)));

    void AttachBlock(GPuppet p, Transform b)
    {
        var tr = b.GetComponent<TrailRenderer>();
        if (tr != null) { tr.emitting = false; tr.Clear(); }
        b.SetParent(p.root, false);
        b.localPosition = new Vector3(0f, GBlockH * 0.5f, 0f);
        b.localRotation = Quaternion.identity;
        p.blocks.Insert(0, b);
    }

    // 飛んでいく積み木の炎の尾
    void AddFire(Transform b)
    {
        var tr = b.GetComponent<TrailRenderer>();
        if (tr == null)
        {
            tr = b.gameObject.AddComponent<TrailRenderer>();
            tr.time = 0.4f;
            tr.widthMultiplier = 0.85f;
            tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            tr.material = fireMat;
            tr.colorGradient = Grad(new Color(1f, 0.95f, 0.6f, 1f), new Color(1f, 0.3f, 0.05f, 0f));
            tr.shadowCastingMode = ShadowCastingMode.Off;
        }
        tr.Clear();
        tr.emitting = true;
    }

    static Gradient Grad(Color a, Color b)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) }, new[] { new GradientAlphaKey(a.a, 0f), new GradientAlphaKey(b.a, 1f) });
        return g;
    }

    Transform NearestLoose(Vector3 from)
    {
        Transform best = null;
        float bd = float.MaxValue;
        foreach (var b in looseBlocks)
        {
            float d = Flat(b.position - from).sqrMagnitude;
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }

    bool NearBumper(Vector3 local, float r)
    {
        if (flatField) return false;
        foreach (var bp in bumpers) if (new Vector2(local.x - bp.x, local.z - bp.y).magnitude < r) return true;
        return false;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static Vector3 Orbit(float deg, float radius, float height)
    {
        float a = deg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius);
    }

    static void FacePuppet(GPuppet p, Vector3 target)
    {
        var d = Flat(target - p.Pos);
        if (d.sqrMagnitude > 0.0001f) p.root.rotation = Quaternion.LookRotation(d);
    }

    // ================= 試合場（机の上の木のトレイ） =================
    void BuildMatchSet()
    {
        var root = SetBase("Match", MatchSet);
        matchRoot = new GameObject("MatchActors").transform;
        matchRoot.SetParent(root, false);
        matchRoot.position = MatchSet;

        // 机とトレイ
        Box(root, "Table", MatchSet + new Vector3(0f, -1.2f, 2f), new Vector3(70f, 1.6f, 50f), ToyKit.Lit(new Color(0.42f, 0.26f, 0.15f), 0.45f));
        Box(root, "TrayFloor", MatchSet + new Vector3(0f, -0.2f, 0f), new Vector3(TrayHalfW * 2f, 0.4f, TrayHalfD * 2f), ToyKit.Lit(new Color(0.93f, 0.85f, 0.7f), 0.25f));
        var rim = ToyKit.Lit(new Color(0.5f, 0.31f, 0.18f), 0.4f);
        Box(root, "RimN", MatchSet + new Vector3(0f, 0.3f, TrayHalfD + 0.35f), new Vector3(TrayHalfW * 2f + 1.4f, 1.4f, 0.7f), rim);
        Box(root, "RimS", MatchSet + new Vector3(0f, 0.3f, -TrayHalfD - 0.35f), new Vector3(TrayHalfW * 2f + 1.4f, 1.4f, 0.7f), rim);
        Box(root, "RimE", MatchSet + new Vector3(TrayHalfW + 0.35f, 0.3f, 0f), new Vector3(0.7f, 1.4f, TrayHalfD * 2f), rim);
        Box(root, "RimW", MatchSet + new Vector3(-TrayHalfW - 0.35f, 0.3f, 0f), new Vector3(0.7f, 1.4f, TrayHalfD * 2f), rim);

        // 丸いバンパー（ピンクの台に黄色のふた）。決勝では片づける
        var pink = ToyKit.Lit(new Color(0.95f, 0.4f, 0.5f), 0.5f);
        var yellow = ToyKit.Lit(new Color(1f, 0.82f, 0.25f), 0.5f);
        bumperRoot = new GameObject("Bumpers").transform;
        bumperRoot.SetParent(root, false);
        foreach (var bp in bumpers)
        {
            var c = MatchSet + new Vector3(bp.x, 0f, bp.y);
            Cyl(bumperRoot, c + Vector3.up * 0.05f, new Vector3(2.7f, 0.05f, 2.7f), yellow);
            Cyl(bumperRoot, c + Vector3.up * 0.3f, new Vector3(2.4f, 0.3f, 2.4f), pink);
            Cyl(bumperRoot, c + Vector3.up * 0.62f, new Vector3(1.4f, 0.03f, 1.4f), yellow);
        }

        // 机の上の大きな小物（トレイのまわり）
        var white = ToyKit.Lit(new Color(0.97f, 0.97f, 0.95f), 0.6f);
        Cyl(root, MatchSet + new Vector3(-15f, -0.3f, -5f), new Vector3(9f, 0.08f, 9f), white);
        Cyl(root, MatchSet + new Vector3(-15f, -0.2f, -5f), new Vector3(9.4f, 0.04f, 9.4f), ToyKit.Lit(new Color(0.2f, 0.35f, 0.75f), 0.5f));
        foreach (var o in new[] { new Vector3(-16f, 0.6f, -6f), new Vector3(-13.8f, 0.6f, -3.6f) })
        {
            var rice = ToyKit.Primitive(PrimitiveType.Sphere, "Onigiri", root);
            rice.position = MatchSet + o;
            rice.localScale = new Vector3(3f, 1.6f, 2.6f);
            rice.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.9f, 0.68f, 0.45f), 0.3f);
            Box(root, "Nori", MatchSet + o + new Vector3(0f, 0.2f, -1.05f), new Vector3(1.6f, 1.4f, 0.3f), ToyKit.Lit(new Color(0.1f, 0.12f, 0.1f), 0.4f));
        }
        var orange = ToyKit.Lit(new Color(1f, 0.55f, 0.12f), 0.35f);
        foreach (var o in new[] { new Vector3(13.5f, 1.1f, -5.5f), new Vector3(15.8f, 1.1f, -3f), new Vector3(13.2f, 1.1f, -1.8f), new Vector3(15f, 3f, -4f) })
        {
            var s = ToyKit.Primitive(PrimitiveType.Sphere, "Orange", root);
            s.position = MatchSet + o;
            s.localScale = Vector3.one * 2.7f;
            s.GetComponent<Renderer>().sharedMaterial = orange;
        }
        var pot = ToyKit.Primitive(PrimitiveType.Sphere, "Pot", root);
        pot.position = MatchSet + new Vector3(12.5f, 2.2f, 10f);
        pot.localScale = new Vector3(7f, 5f, 7f);
        pot.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.35f, 0.2f, 0.14f), 0.7f);
        Box(root, "Shaker", MatchSet + new Vector3(14f, 1.5f, 4f), new Vector3(2.4f, 3f, 2.4f), ToyKit.Lit(new Color(0.55f, 0.75f, 0.95f), 0.6f));
        var egg = ToyKit.Primitive(PrimitiveType.Sphere, "Egg", root);
        egg.position = MatchSet + new Vector3(14f, 3.8f, 4f);
        egg.localScale = new Vector3(1.6f, 2f, 1.6f);
        egg.GetComponent<Renderer>().sharedMaterial = white;
    }

    void Cyl(Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var c = ToyKit.Primitive(PrimitiveType.Cylinder, "Cylinder", parent);
        c.position = pos;
        c.localScale = scale;
        c.GetComponent<Renderer>().sharedMaterial = mat;
    }
}
