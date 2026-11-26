using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// 5. だるきんぐの必殺技「エクスカリバー」の演出（イメージ：Fate のセイバーの「約束された勝利の剣」）。
//    Yurube さんの DaruKingSpecial（詠唱 → カットイン → 斬る）を読むだけで、上に重ねて出す（必殺技の動き・当たり判定は変えない）。
//    参考にした描写：風の結界（風王結界）が解けて剣のまわりに嵐が吹き荒れる／剣は金色で、星の光が剣に集まる／
//    振り下ろすと「一本の線の光」が通り道を斬り、光の波が大地を走って、最後は空へ抜けていく
//    1. 詠唱：ステージ中の地面から金色の光の粒が浮かび上がり、剣へ吸い込まれる。だるきんぐのまわりに風が渦巻き、
//       剣が金色に輝いて、剣から空へ光の柱が立ち上る。画面は少し暗く金色に寄り、光がにじむ
//    2. カットイン〜振りかぶり：光の柱がいちばん太くなって脈打ち、光が集まり続ける
//    3. 斬る：金色の光の波が攻撃の線に沿って一気に走り、そのあとに光の幕（壁）が立ち上がる。床にも光の線。
//       線の終わりから光が空へ抜け、画面が金色にはじけて、光の粒が舞い上がる
//       終点（ステージの端から少し手前）で爆発してクレーターが残り、通った跡は溝のように抉れて瓦礫が飛び散る
public class ExcaliburVfx : MonoBehaviour
{
    class Track
    {
        public Yurube.DaruKingSpecial special;
        public bool wasThreat;
        public float chargeTime;
        public GameObject glow, glowCore, pillar;
        public Light light;
    }

    readonly Dictionary<Yurube.DaruKingSpecial, Track> m_tracks = new Dictionary<Yurube.DaruKingSpecial, Track>();
    readonly List<Yurube.DaruKingSpecial> m_dead = new List<Yurube.DaruKingSpecial>();
    float m_scan;

    ParticleSystem m_motes, m_gather, m_wind, m_embers, m_dust;
    Material m_glowMat, m_pillarMat, m_curtainMat, m_frontMat;
    Image m_flash;
    Volume m_volume;
    ColorAdjustments m_color;
    Bloom m_bloom;
    Vignette m_vignette;
    ChromaticAberration m_chroma;
    LensDistortion m_lens;
    int m_postUsers;
    float m_fireLevel;   // 斬ったあとの画面の強さ（だんだん 0 へ）

    // 演出の時間の進み（スロー中も動くよう unscaled。処理落ち・一時停止のあとで一気に進まないよう上限をつける）
    static float Dt => Mathf.Min(Time.unscaledDeltaTime, 0.1f);

    void LateUpdate()
    {
        ClearMarksOnSceneChange();
        UpdateRocks();
        var s = MatchVfxSettings.Current;
        if (!s.excalibur) return;

        if ((m_scan -= Dt) <= 0f)
        {
            m_scan = 0.5f;
            foreach (var sp in FindObjectsByType<Yurube.DaruKingSpecial>())
                if (sp != null && !m_tracks.ContainsKey(sp)) m_tracks[sp] = new Track { special = sp };
        }

        float chargeLevel = 0f;
        m_dead.Clear();
        foreach (var kv in m_tracks)
        {
            var t = kv.Value;
            if (kv.Key == null || t.special.Caster == null) { Clear(t); m_dead.Add(kv.Key); continue; }
            chargeLevel = Mathf.Max(chargeLevel, Step(t, s));
        }
        foreach (var k in m_dead) m_tracks.Remove(k);

        UpdateScreen(s, chargeLevel);
    }

    // 1人のだるきんぐの演出を1フレーム進める。詠唱の強さ（0〜1）を返す
    float Step(Track t, MatchVfxSettings s)
    {
        var sp = t.special;
        bool threat = sp.IsThreatening;
        bool charging = sp.IsCharging;

        // 斬った瞬間：「これから斬る」状態が終わって、まだ必殺技中（斬ったあとの余韻）
        if (t.wasThreat && !threat && sp.IsActive)
        {
            sp.GetAttackLine(out var start, out var end);
            StartCoroutine(Fire(start, end, sp.AttackWidth, s));
        }
        t.wasThreat = threat;

        if (!threat) { Clear(t); t.chargeTime = 0f; return 0f; }

        Ensure(s);
        float dt = Time.deltaTime;
        t.chargeTime += dt;
        // 詠唱の進み（0〜1）。詠唱のあと（カットイン〜振りかぶり）はいちばん強いまま
        float remaining = sp.ChargeRemaining;
        float u = charging ? Mathf.Clamp01(t.chargeTime / Mathf.Max(0.01f, t.chargeTime + remaining)) : 1f;

        var caster = sp.Caster.transform;
        Vector3 tip = SwordTip(sp.Caster, caster);

        // 地面から光の粒が浮かび上がる（ステージ中から）
        Emit(m_motes, dt * Mathf.Lerp(s.excaliburMoteRateStart, s.excaliburMoteRateEnd, u), () =>
        {
            Vector2 d = Random.insideUnitCircle * s.excaliburMoteRadius;
            Vector3 at = caster.position + new Vector3(d.x, 0.1f, d.y);
            VfxKit.Emit(m_motes, at, new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.8f, 2.4f), Random.Range(-0.2f, 0.2f)),
                Random.Range(0.08f, 0.2f), Random.Range(1.2f, 2.2f), Random.value < 0.7f ? s.excaliburGold : s.excaliburCore);
        });
        // まわりの光が剣へ吸い込まれる（星の光が集まる）
        Emit(m_gather, dt * s.excaliburGatherRate * Mathf.Clamp01(u * 1.3f), () =>
        {
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.8f;
            float r = Random.Range(4f, 11f);
            float life = Random.Range(0.25f, 0.5f);
            Vector3 from = tip + dir * r;
            VfxKit.Emit(m_gather, from, (tip - from) / life, Random.Range(0.06f, 0.14f), life, Random.value < 0.6f ? s.excaliburGold : s.excaliburCore);
        });
        // 風の結界が解けて、まわりに嵐が渦巻く（白い風の筋がだるきんぐのまわりを回る）
        Emit(m_wind, dt * Mathf.Lerp(30f, 140f, u), () =>
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(1.0f, 2.6f);
            Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 tangent = Vector3.Cross(Vector3.up, radial);
            Vector3 at = caster.position + radial * r + Vector3.up * Random.Range(0.1f, 2.6f);
            VfxKit.Emit(m_wind, at, tangent * Random.Range(7f, 12f) * (1f + u) - radial * 1.5f + Vector3.up * Random.Range(0.5f, 2f),
                Random.Range(0.05f, 0.1f), Random.Range(0.2f, 0.4f), new Color(1f, 1f, 0.95f, 0.45f));
        });

        // 剣の輝き（だんだん大きく、速く脈打つ）
        float pulse = 1f + 0.15f * Mathf.Sin(t.chargeTime * Mathf.Lerp(12f, 30f, u));
        if (t.glow == null) { t.glow = MakeQuad("ExcaliburGlow", m_glowMat); t.glowCore = MakeQuad("ExcaliburGlowCore", m_glowMat); }
        Billboard(t.glow, tip, Mathf.Lerp(0.8f, 3.2f, u) * pulse, s.excaliburGold);
        Billboard(t.glowCore, tip, Mathf.Lerp(0.3f, 1.3f, u) * pulse, s.excaliburCore);
        if (t.light == null)
        {
            t.light = new GameObject("ExcaliburLight").AddComponent<Light>();
            t.light.transform.SetParent(transform, false);
            t.light.type = LightType.Point;
            t.light.color = s.excaliburGold;
            t.light.shadows = LightShadows.None;
        }
        t.light.transform.position = tip;
        t.light.range = 8f + 10f * u;
        t.light.intensity = (2f + 10f * u) * pulse;

        // 剣から空へ立ち上る光の柱（縦の軸のまわりだけカメラを向く）
        if (t.pillar == null) t.pillar = MakeQuad("ExcaliburPillar", m_pillarMat);
        float width = Mathf.Lerp(0.2f, charging ? 1.4f : 2.2f, u) * pulse;
        Column(t.pillar, tip, width, s.excaliburPillarHeight * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 1.6f)),
            new Color(s.excaliburGold.r, s.excaliburGold.g, s.excaliburGold.b, Mathf.Clamp01(u * 1.5f)));
        return u;
    }

    // 剣の先のあたり（だるきんぐのハンマー＝剣の見た目の、いちばん遠い所。無ければ頭の上）
    static Vector3 SwordTip(Nakahira.DarumaCharacter c, Transform caster)
    {
        var hammer = c.Hammer;
        if (hammer != null) return hammer.transform.TransformPoint(0f, 0f, hammer.ArmLength * 1.4f);
        return caster.position + Vector3.up * 2f;
    }

    // 斬る：光の波が線に沿って走り、光の幕が立ち上がって、線の終わりから空へ抜ける
    IEnumerator Fire(Vector3 start, Vector3 end, float attackWidth, MatchVfxSettings s)
    {
        Ensure(s);
        AcquirePost();
        m_fireLevel = 1f;
        Vector3 dir = end - start;
        dir.y = 0f;
        float length = dir.magnitude;
        if (length < 0.1f) { ReleasePost(); yield break; }
        dir /= length;
        // 攻撃の線はステージの外まで伸びているので、演出の終点（爆発）はステージの内側、端から少し手前にする
        length = BlastDistance(start, dir, length, s.excaliburBlastInset);
        end = start + dir * length;
        end.y = start.y;
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        Color gold = s.excaliburGold, core = s.excaliburCore;
        float width = Mathf.Max(1f, attackWidth);
        StartCoroutine(Scar(start, dir, length, width, s));
        bool blasted = false;

        var front = MakeQuad("ExcaliburFront", m_frontMat);
        var curtain = MakeQuad("ExcaliburCurtain", m_curtainMat);
        var curtainCore = MakeQuad("ExcaliburCurtainCore", m_curtainMat);
        var floor = MakeQuad("ExcaliburFloor", m_glowMat);
        var endPillar = MakeQuad("ExcaliburEndPillar", m_pillarMat);
        var light = new GameObject("ExcaliburFireLight").AddComponent<Light>();
        light.transform.SetParent(transform, false);
        light.type = LightType.Point;
        light.color = gold;
        light.shadows = LightShadows.None;

        var cam = Camera.main;
        var shaker = cam != null ? cam.GetComponent<Nakagawa.CameraShake>() : null;
        if (shaker != null) shaker.Shake(new Nakagawa.CameraShake.ShakeSettings(1.1f, 0.9f));

        float total = Mathf.Max(0.3f, s.excaliburFireTime);
        float waveTime = Mathf.Max(0.05f, s.excaliburWaveTime);
        float emitted = 0f;
        for (float t = 0f; t < total; t += Dt)
        {
            float wave = Mathf.Clamp01(t / waveTime);
            float reach = length * (1f - (1f - wave) * (1f - wave));   // 光の波の先（速く伸びて、端で止まる）
            float fade = Mathf.Clamp01((total - t) / (total * 0.55f));
            Vector3 head = start + dir * reach;

            // 光の波の先頭：まぶしい光のかたまり
            Billboard(front, head + Vector3.up * 1.2f, width * 2.6f * (wave < 1f ? 1f : 1f + (t - waveTime) * 3f),
                new Color(core.r, core.g, core.b, wave < 1f ? 1f : fade * fade));

            // 光の幕：線に沿った縦の板（外側の金・内側の白）。立ち上がって、ゆらめきながら消える
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / (waveTime * 1.6f)));
            float shimmer = 1f + 0.08f * Mathf.Sin(t * 40f);
            Wall(curtain, start, dir, reach, s.excaliburCurtainHeight * rise * shimmer, new Color(gold.r, gold.g, gold.b, 0.9f * fade));
            Wall(curtainCore, start, dir, reach, s.excaliburCurtainHeight * 0.55f * rise, new Color(core.r, core.g, core.b, fade));
            // 床の光の線
            floor.transform.position = start + dir * (reach * 0.5f) + Vector3.up * 0.06f;
            floor.transform.rotation = Quaternion.LookRotation(Vector3.down, dir);
            floor.transform.localScale = new Vector3(width * 1.6f, Mathf.Max(0.01f, reach), 1f);
            floor.GetComponent<MeshRenderer>().material.color = new Color(gold.r, gold.g, gold.b, fade);

            // 線の終わりから空へ抜ける光
            if (wave >= 1f)
            {
                if (!blasted) { blasted = true; StartCoroutine(Blast(end, dir, width, s)); }
                float up = Mathf.Clamp01((t - waveTime) / 0.25f);
                Column(endPillar, end, width * 1.4f * (1f + up), s.excaliburPillarHeight * 1.3f * up, new Color(core.r, core.g, core.b, fade));
            }

            light.transform.position = head + Vector3.up * 1.5f;
            light.range = width * 6f;
            light.intensity = 25f * fade;

            // 光の粒（火の粉）：光の波が通ったところから舞い上がる
            emitted += Dt * 400f * fade;
            while (emitted >= 1f)
            {
                emitted -= 1f;
                Vector3 at = start + dir * Random.Range(0f, reach) + right * Random.Range(-width, width) * 0.6f;
                VfxKit.Emit(m_embers, at + Vector3.up * Random.Range(0f, 1.5f), new Vector3(Random.Range(-1f, 1f), Random.Range(2f, 7f), Random.Range(-1f, 1f)),
                    Random.Range(0.08f, 0.2f), Random.Range(0.6f, 1.4f), Random.value < 0.7f ? gold : core);
            }
            // 光の波の先頭で、土ぼこりが両側へ吹き飛ぶ
            if (wave < 1f)
                for (int i = 0; i < 6; i++)
                {
                    float side = Random.value < 0.5f ? -1f : 1f;
                    VfxKit.Emit(m_dust, head + right * side * width * 0.6f, right * side * Random.Range(4f, 10f) + Vector3.up * Random.Range(1f, 4f),
                        Random.Range(0.6f, 1.2f), Random.Range(0.6f, 1.1f), new Color(0.85f, 0.75f, 0.55f, 0.5f));
                }

            // 画面：金色にはじける
            m_flash.color = new Color(1f, 0.92f, 0.65f, 0.7f * Mathf.Clamp01(1f - t / 0.25f));
            yield return null;
        }
        m_flash.color = new Color(1f, 1f, 1f, 0f);
        Destroy(front); Destroy(curtain); Destroy(curtainCore); Destroy(floor); Destroy(endPillar);
        Destroy(light.gameObject);
        ReleasePost();
    }

    // ---------------- 抉れた跡・終点の爆発 ----------------
    class Rock
    {
        public Transform tr;
        public Vector3 pos, scale;
        public float life, age;
    }
    readonly List<Rock> m_rocks = new List<Rock>();
    MaterialPropertyBlock m_rockBlock;
    static Mesh[] s_rockMeshes;
    readonly List<GameObject> m_marks = new List<GameObject>();   // 溝・クレーターなど（シーンが変わったら消す）
    Material m_rockMat, m_trenchMat, m_craterMat, m_moltenMat, m_poolMat, m_ringMat;
    UnityEngine.SceneManagement.Scene m_scene;
    static Texture2D s_trench, s_crater, s_molten;

    // 線の上で、ステージの端から inset だけ内側に入っている、いちばん遠い所までの距離（ステージが無ければ線の長さ - inset）
    // ステージの範囲（StageArea）は見た目の床より広いことがあるので、実際の床が途切れる所からも inset だけ手前にする
    static float BlastDistance(Vector3 start, Vector3 dir, float length, float inset)
    {
        float floorEnd = length;
        for (float d = 0.5f; d <= length; d += 0.5f)
        {
            Vector3 p = start + dir * d;
            bool floor = Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)
                && hit.point.y > start.y - 0.6f;
            if (!floor) { floorEnd = d - 0.5f; break; }
        }
        length = Mathf.Max(Mathf.Min(1f, length), Mathf.Min(length, floorEnd - inset * 0.5f));

        // 画面の端（下の HUD のあたりも）にかからないよう、爆発がカメラに収まる所まで手前にする
        var cam = Camera.main;
        if (cam != null)
        {
            float d = length;
            for (; d > 1f; d -= 0.5f)
            {
                Vector3 v = cam.WorldToViewportPoint(start + dir * d + Vector3.up * 1f);
                if (v.z > 0f && v.x > 0.1f && v.x < 0.9f && v.y > 0.2f && v.y < 0.9f) break;
            }
            length = Mathf.Max(Mathf.Min(1f, length), d);
        }

        var match = Nakahira.DarumaMatch.Current;
        var area = match != null ? match.StageArea : null;
        if (area == null) return Mathf.Max(1f, length - inset);
        float low = 0f, high = length;
        for (int i = 0; i < 20; i++)
        {
            float mid = (low + high) * 0.5f;
            Vector3 p = start + dir * mid;
            Vector3 c = area.Clamp(p, inset);
            float dx = c.x - p.x, dz = c.z - p.z;
            if (dx * dx + dz * dz < 0.0004f) low = mid;
            else high = mid;
        }
        return Mathf.Clamp(low, Mathf.Min(1f, length), length);
    }

    // 光の波が通ったあとの地面を抉る：ギザギザの縁の溝（片側は光が当たり、反対側は影）・縁のひび・溶けて光る底・跳ね上がる瓦礫
    IEnumerator Scar(Vector3 start, Vector3 dir, float length, float width, MatchVfxSettings s)
    {
        EnsureScar();
        float w = width * Mathf.Max(0.3f, s.excaliburScarWidth);
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        var trench = MakeStrip("ExcaliburTrench", m_trenchMat);
        var molten = MakeStrip("ExcaliburMolten", m_moltenMat);
        var trenchMesh = trench.GetComponent<MeshFilter>().sharedMesh;
        var moltenMesh = molten.GetComponent<MeshFilter>().sharedMesh;
        var trenchMat = trench.GetComponent<MeshRenderer>().material;
        var moltenMat = molten.GetComponent<MeshRenderer>().material;
        // 斬り始めの所：剣を叩きつけた小さなくぼみ。溝はこの上に重ねて、ぼかしながら始まる（四角い端を見せない）
        var startPit = MakeQuad("ExcaliburStartPit", m_craterMat);
        m_marks.Add(startPit);
        startPit.transform.SetPositionAndRotation(start + Vector3.up * 0.04f + dir * (0.1f * w), Quaternion.LookRotation(Vector3.down, dir));
        startPit.transform.localScale = new Vector3(w * 1.25f, w * 1.4f, 1f);
        var pitMat = startPit.GetComponent<MeshRenderer>().material;

        float waveTime = Mathf.Max(0.05f, s.excaliburWaveTime);
        float hold = Mathf.Max(0.5f, s.excaliburScarTime);
        const float fadeTime = 1.5f;
        // 押しのけられた土の岩：溝の両側に、内側の列（大きい）と外側の列（小さい・まばら）
        float rockSize = Mathf.Max(0.1f, s.excaliburRockSize);
        float density = Mathf.Max(0.1f, s.excaliburRockDensity);
        float rocksUntil = length - 0.55f * w;   // ここから先はクレーターの縁の岩にまかせる
        float nextLeft = 0.8f, nextRight = 0.8f + 0.08f * w;
        for (float t = 0f; t < hold + fadeTime; t += Dt)
        {
            if (trench == null || molten == null) yield break;   // シーンが変わって消えた
            float wave = Mathf.Clamp01(t / waveTime);
            float reach = Mathf.Max(0.01f, length * (1f - (1f - wave) * (1f - wave)));
            float a = 1f - Mathf.Clamp01((t - hold) / fadeTime);

            // 溝は、終点のクレーターの中まで入ってから、だんだん透明になって終わる
            // （クレーターの上に重ねて描くので、クレーターの縁が溝を横切らず、ひと続きの穴に見える）
            float endD = Mathf.Min(reach, length - 0.1f * w);
            SetStrip(trenchMesh, start + Vector3.up * 0.045f, dir, Mathf.Max(0.01f, endD), w, 1f / (w * 1.2f), 0.4f * w, 0.45f * w);
            trenchMat.color = new Color(1f, 1f, 1f, a);
            if (startPit != null) pitMat.color = new Color(1f, 1f, 1f, a * Mathf.Clamp01(t / 0.05f));
            // 溝の底：溶けて白く光り、冷えて赤くなって消える（クレーターの真ん中の溶けた溜まりへ続く）
            float heat = Mathf.Clamp01(1f - t / 2.5f);
            SetStrip(moltenMesh, start + Vector3.up * 0.06f, dir, Mathf.Max(0.01f, endD), w * 0.5f, 0.1f, 0.5f * w, 0.5f * w);
            var hot = Color.Lerp(new Color(0.9f, 0.18f, 0.02f), s.excaliburCore, heat * heat);
            moltenMat.color = new Color(hot.r, hot.g, hot.b, heat);

            // 光の波が通ったところから、溝の両側の地面が押しのけられて盛り上がる（岩が地面から突き上がる）
            float limit = Mathf.Min(reach, rocksUntil);
            while (nextLeft < limit) { BermRocks(start, dir, nextLeft, -1f, w, rockSize, hold); nextLeft += Random.Range(0.12f, 0.2f) * w / density; }
            while (nextRight < limit) { BermRocks(start, dir, nextRight, 1f, w, rockSize, hold); nextRight += Random.Range(0.12f, 0.2f) * w / density; }
            yield return null;
        }
        m_marks.Remove(trench); m_marks.Remove(molten); m_marks.Remove(startPit);
        Destroy(trench); Destroy(molten);
        if (startPit != null) Destroy(startPit);
    }

    // 終点の爆発：まぶしい光の玉がふくらみ、床に輪が広がって、クレーターが残る。瓦礫・火の粉・土埃が四方へ
    IEnumerator Blast(Vector3 at, Vector3 dir, float width, MatchVfxSettings s)
    {
        EnsureScar();
        var cam = Camera.main;
        var shaker = cam != null ? cam.GetComponent<Nakagawa.CameraShake>() : null;
        if (shaker != null) shaker.Shake(new Nakagawa.CameraShake.ShakeSettings(1.4f, 0.6f));

        float w = width * Mathf.Max(0.3f, s.excaliburScarWidth);
        var crater = MakeQuad("ExcaliburCrater", m_craterMat);
        var glow = MakeQuad("ExcaliburBlastGlow", m_frontMat);
        var ring = MakeQuad("ExcaliburBlastRing", m_ringMat);
        var ring2 = MakeQuad("ExcaliburBlastRing2", m_ringMat);
        var pool = MakeQuad("ExcaliburMoltenPool", m_poolMat);
        m_marks.Add(crater); m_marks.Add(glow); m_marks.Add(ring); m_marks.Add(ring2); m_marks.Add(pool);
        // クレーターは攻撃の向きにそろえる（画像の右側の壁が明るい＝溝と同じ側が明るくなる）。少し縦長にして溝となじませる
        var flat = Quaternion.LookRotation(Vector3.down, dir);
        crater.transform.SetPositionAndRotation(at + Vector3.up * 0.04f, flat);
        pool.transform.SetPositionAndRotation(at + Vector3.up * 0.06f, flat);
        var poolMat = pool.GetComponent<MeshRenderer>().material;
        float waveTime = Mathf.Max(0.05f, s.excaliburWaveTime);
        ring.transform.SetPositionAndRotation(at + Vector3.up * 0.07f, Quaternion.Euler(90f, 0f, 0f));
        ring2.transform.SetPositionAndRotation(at + Vector3.up * 0.08f, Quaternion.Euler(90f, 0f, 0f));
        var craterMat = crater.GetComponent<MeshRenderer>().material;
        var ringMat = ring.GetComponent<MeshRenderer>().material;
        var ring2Mat = ring2.GetComponent<MeshRenderer>().material;
        var light = new GameObject("ExcaliburBlastLight").AddComponent<Light>();
        light.transform.SetParent(transform, false);
        light.transform.position = at + Vector3.up * 2f;
        light.type = LightType.Point;
        light.color = s.excaliburGold;
        light.shadows = LightShadows.None;
        light.range = w * 8f;

        Color gold = s.excaliburGold, core = s.excaliburCore;
        for (int i = 0; i < 160; i++)
        {
            Vector3 d = Random.onUnitSphere;
            d.y = Mathf.Abs(d.y) * 1.3f + 0.2f;
            VfxKit.Emit(m_embers, at + Vector3.up * 0.5f, d * Random.Range(5f, 16f), Random.Range(0.1f, 0.28f), Random.Range(0.6f, 1.5f),
                Random.value < 0.6f ? gold : core);
        }
        for (int i = 0; i < 40; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            VfxKit.Emit(m_dust, at + d * w * 0.5f, d * Random.Range(5f, 12f) + Vector3.up * Random.Range(1f, 5f),
                Random.Range(0.9f, 1.8f), Random.Range(0.7f, 1.3f), new Color(0.85f, 0.75f, 0.55f, 0.55f));
        }
        // クレーターの縁：押しのけられた地面が、まわりにぐるりと盛り上がる（溝が入ってくる側は空ける）
        {
            float rockSize = Mathf.Max(0.1f, s.excaliburRockSize);
            float density = Mathf.Max(0.1f, s.excaliburRockDensity);
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            float step = 22f / density;
            for (float deg = Random.Range(0f, step); deg < 360f; deg += step * Random.Range(0.8f, 1.2f))
            {
                // 0° が攻撃の向き（奥）。180° のまわり（溝から入ってくる所）は空ける
                if (Mathf.Abs(Mathf.DeltaAngle(deg, 180f)) < 38f) continue;
                float rad = deg * Mathf.Deg2Rad;
                Vector3 outward = (dir * Mathf.Cos(rad) + right * Mathf.Sin(rad)).normalized;
                // クレーターは攻撃の向きに少し長い楕円
                float radius = Mathf.Sqrt(Sq(0.72f * w * Mathf.Sin(rad)) + Sq(0.84f * w * Mathf.Cos(rad)));
                SpawnRock(at + outward * radius * Random.Range(0.95f, 1.1f), outward, w * Random.Range(0.17f, 0.28f) * rockSize, hold0(s), Random.Range(0f, 0.08f));
                if (Random.value < 0.3f)
                    SpawnRock(at + outward * radius * Random.Range(1.2f, 1.38f), outward, w * Random.Range(0.1f, 0.16f) * rockSize, hold0(s), Random.Range(0.04f, 0.12f));
            }
        }

        float hold = Mathf.Max(0.5f, s.excaliburScarTime);
        const float fadeTime = 1.5f;
        for (float t = 0f; t < hold + fadeTime; t += Dt)
        {
            if (crater == null) break;   // シーンが変わって消えた
            float e = Mathf.Clamp01(t / 0.6f);
            float eo = 1f - (1f - e) * (1f - e);
            if (glow != null)
            {
                if (e < 1f) Billboard(glow, at + Vector3.up * 1.2f, w * (0.8f + 2.2f * eo), new Color(core.r, core.g, core.b, 0.8f * (1f - e) * (1f - e)));
                else { m_marks.Remove(glow); Destroy(glow); glow = null; }
            }
            float r1 = Mathf.Clamp01(t / 0.5f), r2 = Mathf.Clamp01((t - 0.08f) / 0.8f);
            if (ring != null) { ring.transform.localScale = Vector3.one * w * (0.5f + 3.5f * (1f - (1f - r1) * (1f - r1))); ringMat.color = new Color(core.r, core.g, core.b, 1f - r1); }
            if (ring2 != null) { ring2.transform.localScale = Vector3.one * w * (0.5f + 5.5f * (1f - (1f - r2) * (1f - r2))); ring2Mat.color = new Color(gold.r, gold.g, gold.b, 0.8f * (1f - r2)); }
            if (light != null) light.intensity = 40f * (1f - e);
            float grow = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(t / 0.12f));
            crater.transform.localScale = new Vector3(w * 2.6f, w * 3.0f, 1f) * grow;
            craterMat.color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01((t - hold) / fadeTime));
            // クレーターの底の溶けた溜まり（溝の底の光と同じ色・同じ冷え方）
            if (pool != null)
            {
                float heat = Mathf.Clamp01(1f - (t + waveTime) / 2.5f);
                var hot = Color.Lerp(new Color(0.9f, 0.18f, 0.02f), core, heat * heat);
                pool.transform.localScale = new Vector3(w * 1.1f, w * 1.3f, 1f) * grow;
                poolMat.color = new Color(hot.r, hot.g, hot.b, heat);
            }
            yield return null;
        }
        foreach (var go in new[] { crater, glow, ring, ring2, pool })
            if (go != null) { m_marks.Remove(go); Destroy(go); }
        if (light != null) Destroy(light.gameObject);
    }

    static float Sq(float x) => x * x;
    static float hold0(MatchVfxSettings s) => Mathf.Max(0.5f, s.excaliburScarTime) + 1.5f;

    // 溝の片側（side：-1 左 / 1 右）の、along の所に、押しのけられた土の岩を置く（内側の列は大きく、外側の列は小さく・まばら）
    void BermRocks(Vector3 start, Vector3 dir, float along, float side, float w, float rockSize, float hold)
    {
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        Vector3 outward = right * side;
        Vector3 c = start + dir * along;
        float life = hold + 1.5f;
        SpawnRock(c + outward * w * Random.Range(0.33f, 0.42f) + dir * Random.Range(-0.04f, 0.04f) * w, outward,
            w * Random.Range(0.15f, 0.25f) * rockSize, life, 0f);
        if (Random.value < 0.35f)
            SpawnRock(c + outward * w * Random.Range(0.47f, 0.58f) + dir * Random.Range(-0.08f, 0.08f) * w, outward,
                w * Random.Range(0.08f, 0.14f) * rockSize, life, Random.Range(0.03f, 0.1f));
    }

    // 岩を1つ、地面から突き上がるように出す。outward：押しのけられた向き（岩はそちらへ傾く）。delay 秒あとに出はじめる
    void SpawnRock(Vector3 ground, Vector3 outward, float size, float life, float delay)
    {
        if (s_rockMeshes == null)
        {
            s_rockMeshes = new Mesh[4];
            for (int i = 0; i < s_rockMeshes.Length; i++) s_rockMeshes[i] = RockMesh(31 + i * 17);
        }
        if (m_rockMat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            m_rockMat = new Material(shader != null ? shader : Shader.Find("Standard"));
            m_rockMat.color = Color.white;
            if (m_rockMat.HasProperty("_Smoothness")) m_rockMat.SetFloat("_Smoothness", 0.08f);
            m_rockBlock = new MaterialPropertyBlock();
        }
        var go = new GameObject("ExcaliburRock");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = s_rockMeshes[Random.Range(0, s_rockMeshes.Length)];
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m_rockMat;
        // 色は土〜岩のあいだでばらつかせる（表面は少し明るめの土、焦げて暗いものも混ぜる）
        var col = Color.Lerp(new Color(0.33f, 0.27f, 0.21f), new Color(0.58f, 0.5f, 0.4f), Random.value);
        if (Random.value < 0.2f) col *= 0.55f;
        m_rockBlock.SetColor("_BaseColor", col);
        m_rockBlock.SetColor("_Color", col);
        r.SetPropertyBlock(m_rockBlock);

        var scale = new Vector3(size * Random.Range(0.85f, 1.35f), size * Random.Range(0.55f, 0.9f), size * Random.Range(0.8f, 1.25f));
        // 外側へ傾ける（地面がめくれて押しのけられた感じ）＋向きのばらつき
        Vector3 axis = Vector3.Cross(Vector3.up, outward);
        var rot = Quaternion.AngleAxis(Random.Range(12f, 38f), axis) * Quaternion.LookRotation(outward) * Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f);
        var pos = ground + Vector3.up * (scale.y * Random.Range(0.0f, 0.15f));
        go.transform.SetPositionAndRotation(pos - Vector3.up * scale.y, rot);
        go.transform.localScale = Vector3.zero;
        m_rocks.Add(new Rock { tr = go.transform, pos = pos, scale = scale, life = Mathf.Max(0.5f, life), age = -delay });
    }

    // 岩：地面の下から勢いよく突き上がり（少し行きすぎて戻る）、最後は地面へ沈んで消える
    void UpdateRocks()
    {
        float dt = Dt;
        for (int i = m_rocks.Count - 1; i >= 0; i--)
        {
            var k = m_rocks[i];
            if (k.tr == null) { m_rocks.RemoveAt(i); continue; }
            k.age += dt;
            if (k.age >= k.life) { Destroy(k.tr.gameObject); m_rocks.RemoveAt(i); continue; }
            if (k.age < 0f) continue;
            float u = Mathf.Clamp01(k.age / 0.16f);
            // 行きすぎて戻る（EaseOutBack）
            const float c1 = 1.9f, c3 = c1 + 1f;
            float pop = 1f + c3 * Mathf.Pow(u - 1f, 3f) + c1 * Mathf.Pow(u - 1f, 2f);
            float sink = Mathf.Clamp01((k.life - k.age) / 1.2f);
            float rise = Mathf.Min(pop, 1.15f) * Mathf.SmoothStep(0f, 1f, sink);
            k.tr.position = k.pos - Vector3.up * (k.scale.y * (1f - rise));
            k.tr.localScale = new Vector3(k.scale.x * Mathf.Clamp01(u * 3f), k.scale.y * Mathf.Max(0.01f, pop), k.scale.z * Mathf.Clamp01(u * 3f));
        }
    }

    // 角ばった岩のメッシュ：正二十面体を1回分割して、頂点をでこぼこに動かし、底を平らにする。面ごとに法線を分けて角ばった見た目に
    static Mesh RockMesh(int seed)
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var verts = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
        var faces = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        // 1回分割（辺の真ん中に頂点を足す）
        var cache = new Dictionary<long, int>();
        System.Func<int, int, int> midpoint = (a, b) =>
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            cache[key] = verts.Count - 1;
            return verts.Count - 1;
        };
        var sub = new List<int>();
        for (int i = 0; i < faces.Count; i += 3)
        {
            int a = faces[i], b = faces[i + 1], c = faces[i + 2];
            int ab = midpoint(a, b), bc = midpoint(b, c), ca = midpoint(c, a);
            sub.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        // でこぼこ・底を平らに
        var rand = new System.Random(seed);
        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i] * (0.78f + 0.4f * (float)rand.NextDouble());
            if (v.y < -0.25f) v.y = -0.25f + (v.y + 0.25f) * 0.25f;
            verts[i] = v;
        }
        // 面ごとに頂点を分ける（角ばった陰影）。この並びのままで Unity の表（外向き）になる
        var outV = new Vector3[sub.Count];
        var outT = new int[sub.Count];
        for (int i = 0; i < sub.Count; i += 3)
        {
            outV[i] = verts[sub[i]];
            outV[i + 1] = verts[sub[i + 1]];
            outV[i + 2] = verts[sub[i + 2]];
            outT[i] = i; outT[i + 1] = i + 1; outT[i + 2] = i + 2;
        }
        var mesh = new Mesh { name = "ExcaliburRockMesh" };
        mesh.vertices = outV;
        mesh.triangles = outT;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // シーンが変わったら、残っている跡・瓦礫を消す（演出の係はシーンをまたいで残るため）
    void ClearMarksOnSceneChange()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene == m_scene) return;
        m_scene = scene;
        foreach (var go in m_marks) if (go != null) Destroy(go);
        m_marks.Clear();
        foreach (var k in m_rocks) if (k.tr != null) Destroy(k.tr.gameObject);
        m_rocks.Clear();
    }

    void EnsureScar()
    {
        if (s_trench == null) s_trench = TrenchTexture();
        if (s_crater == null) s_crater = CraterTexture();
        if (s_molten == null)
        {
            const int w = 64;
            s_molten = new Texture2D(w, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VfxMolten" };
            var px = new Color[w * 4];
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;
                for (int y = 0; y < 4; y++) px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Exp(-u * u * 9f));
            }
            s_molten.SetPixels(px);
            s_molten.Apply();
        }
        // 描く順：クレーター → 溝（溝をクレーターの上に重ねて、つなぎ目を隠す）
        if (m_trenchMat == null) { m_trenchMat = VfxKit.Mat(s_trench); m_trenchMat.renderQueue = 3001; }
        if (m_craterMat == null) { m_craterMat = VfxKit.Mat(s_crater); m_craterMat.renderQueue = 3000; }
        if (m_moltenMat == null) { m_moltenMat = VfxKit.AddMat(s_molten, 3f); m_moltenMat.renderQueue = 3002; }
        if (m_poolMat == null) { m_poolMat = VfxKit.AddMat(VfxKit.SoftDot, 3f); m_poolMat.renderQueue = 3002; }
        if (m_ringMat == null) m_ringMat = VfxKit.AddMat(VfxKit.Ring, 3f);
    }

    GameObject MakeStrip(string name, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var mesh = new Mesh { name = name };
        // 頂点は 始め・始めのぼかしの終わり・終わりのぼかしの始め・終わり の4か所 × 左右。始めと終わりは透明
        var clear = new Color(1f, 1f, 1f, 0f);
        mesh.vertices = new Vector3[8];
        mesh.uv = new Vector2[8];
        mesh.colors = new[] { clear, clear, Color.white, Color.white, Color.white, Color.white, clear, clear };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5, 4, 6, 5, 5, 6, 7 };
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.material = new Material(mat);
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        m_marks.Add(go);
        return go;
    }

    // 床に貼る帯（start から dir の向きへ length、幅 width）。画像は長さの向きに繰り返す（uvPerMeter）
    // 始めの fadeIn・終わりの fadeLength だけ、だんだん透明にする（端が四角く切れて見えないように）
    void SetStrip(Mesh mesh, Vector3 start, Vector3 dir, float length, float width, float uvPerMeter, float fadeLength, float fadeIn = 0f)
    {
        Vector3 r = Vector3.Cross(Vector3.up, dir) * (width * 0.5f);
        float a = Mathf.Min(Mathf.Max(0f, fadeIn), length * 0.5f);
        float b = Mathf.Max(a, length - fadeLength);
        float[] d = { 0f, a, b, length };
        var v = new Vector3[8];
        var uv = new Vector2[8];
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = start + dir * d[i];
            v[i * 2] = transform.InverseTransformPoint(p - r);
            v[i * 2 + 1] = transform.InverseTransformPoint(p + r);
            uv[i * 2] = new Vector2(0f, d[i] * uvPerMeter);
            uv[i * 2 + 1] = new Vector2(1f, d[i] * uvPerMeter);
        }
        mesh.vertices = v;
        mesh.uv = uv;
        mesh.RecalculateBounds();
    }

    // ---- 抉れた跡の画像 ----
    static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h & 0xffff) / 65535f;
        }
    }

    // 1 周期でつながる、ゆらぎ（-1〜1 くらい）
    static float Wobble(float v, float seed)
    {
        const float tau = Mathf.PI * 2f;
        return 0.5f * Mathf.Sin(tau * 3f * v + seed) + 0.3f * Mathf.Sin(tau * 7f * v + seed * 2.3f) + 0.2f * Mathf.Sin(tau * 17f * v + seed * 4.1f);
    }

    // 溝：横（u）は -1〜1、縦（v）は繰り返す。真ん中が深く暗く、壁の片側は明るく反対側は影、縁は盛り上がった土、外へひび
    static Texture2D TrenchTexture()
    {
        const int w = 128, h = 256;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapModeU = TextureWrapMode.Clamp, wrapModeV = TextureWrapMode.Repeat, name = "VfxTrench" };
        var rand = new System.Random(11);
        const int crackCount = 16;
        var cv = new float[crackCount]; var cs = new float[crackCount]; var cl = new float[crackCount]; var csl = new float[crackCount];
        for (int i = 0; i < crackCount; i++)
        {
            cv[i] = (float)rand.NextDouble();
            cs[i] = rand.NextDouble() < 0.5 ? -1f : 1f;
            cl[i] = 0.12f + (float)rand.NextDouble() * 0.22f;
            csl[i] = ((float)rand.NextDouble() - 0.5f) * 0.6f;
        }
        var bottom = new Color(0.06f, 0.045f, 0.035f);
        var lit = new Color(0.45f, 0.36f, 0.27f);
        var shade = new Color(0.1f, 0.075f, 0.06f);
        var crackColor = new Color(0.07f, 0.05f, 0.04f);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            float edge = 0.58f + 0.07f * Wobble(v, 1.3f);
            float lip = 0.15f + 0.05f * Wobble(v, 4.7f);
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;
                float ax = Mathf.Abs(u);
                float grain = Hash(x, y);
                Color c; float a;
                if (ax < edge)
                {
                    float d = ax / edge;
                    float wall = Mathf.SmoothStep(0.4f, 1f, d);
                    c = Color.Lerp(bottom, u > 0f ? lit : shade, wall) * (0.8f + 0.4f * grain);
                    a = 0.97f;
                }
                else if (ax < edge + lip)
                {
                    float k = (ax - edge) / lip;
                    c = (u > 0f ? new Color(0.62f, 0.52f, 0.4f) : new Color(0.48f, 0.4f, 0.31f)) * (0.85f + 0.3f * grain);
                    a = (1f - k) * (1f - k) * (0.55f + 0.45f * grain);
                }
                else { c = crackColor; a = 0f; }

                if (ax > edge)
                {
                    float along = ax - edge;
                    for (int i = 0; i < crackCount; i++)
                    {
                        if (Mathf.Sign(u) != cs[i] || along > cl[i]) continue;
                        float center = cv[i] + csl[i] * along + 0.01f * Mathf.Sin(along * 60f + i);
                        float dv = Mathf.Abs(Mathf.Repeat(v - center + 0.5f, 1f) - 0.5f);
                        float cw = 0.006f * (1f - along / cl[i]) + 0.0015f;
                        if (dv < cw)
                        {
                            float ca = 0.85f * (1f - dv / cw);
                            c = Color.Lerp(c, crackColor, ca);
                            a = Mathf.Max(a, ca);
                        }
                    }
                }
                px[y * w + x] = new Color(c.r, c.g, c.b, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // クレーター：まるい溝（光が来る向きの反対側の壁が明るい）、盛り上がった縁、外へ放射状のひび
    static Texture2D CraterTexture()
    {
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "VfxCrater" };
        var rand = new System.Random(23);
        const int crackCount = 14;
        var ca0 = new float[crackCount]; var cl = new float[crackCount];
        for (int i = 0; i < crackCount; i++)
        {
            ca0[i] = (float)rand.NextDouble() * Mathf.PI * 2f;
            cl[i] = 0.15f + (float)rand.NextDouble() * 0.3f;
        }
        var bottom = new Color(0.05f, 0.04f, 0.03f);
        var lit = new Color(0.45f, 0.36f, 0.27f);
        var shade = new Color(0.1f, 0.075f, 0.06f);
        var crackColor = new Color(0.07f, 0.05f, 0.04f);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f) / (n * 0.5f);
                float r = p.magnitude;
                float ang = Mathf.Atan2(p.y, p.x);
                float edge = 0.5f + 0.04f * Mathf.Sin(ang * 5f + 1f) + 0.025f * Mathf.Sin(ang * 11f + 2f) + 0.015f * Mathf.Sin(ang * 23f);
                float lip = 0.16f + 0.04f * Mathf.Sin(ang * 7f + 0.5f);
                float grain = Hash(x, y);
                Color c; float a;
                if (r < edge)
                {
                    float d = r / edge;
                    // 右側（+x）の壁が明るい（溝の明るい側とそろえる）
                    float facing = r > 0.001f ? 0.5f + 0.5f * p.x / r : 0.5f;
                    c = Color.Lerp(bottom, Color.Lerp(shade, lit, facing), Mathf.SmoothStep(0.3f, 1f, d)) * (0.8f + 0.4f * grain);
                    a = 0.97f;
                }
                else if (r < edge + lip)
                {
                    float k = (r - edge) / lip;
                    c = new Color(0.58f, 0.48f, 0.37f) * (0.85f + 0.3f * grain);
                    a = (1f - k) * (1f - k) * (0.55f + 0.45f * grain);
                }
                else { c = crackColor; a = 0f; }

                if (r > edge)
                {
                    float along = r - edge;
                    for (int i = 0; i < crackCount; i++)
                    {
                        if (along > cl[i]) continue;
                        float da = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, ca0[i] * Mathf.Rad2Deg + 8f * Mathf.Sin(along * 40f + i))) * Mathf.Deg2Rad * r;
                        float cw = 0.012f * (1f - along / cl[i]) + 0.003f;
                        if (da < cw)
                        {
                            float k = 0.85f * (1f - da / cw);
                            c = Color.Lerp(c, crackColor, k);
                            a = Mathf.Max(a, k);
                        }
                    }
                }
                px[y * n + x] = new Color(c.r, c.g, c.b, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // ---------------- 画面の色 ----------------
    void UpdateScreen(MatchVfxSettings s, float charge)
    {
        if (m_volume == null) return;
        m_fireLevel = Mathf.MoveTowards(m_fireLevel, 0f, Dt / Mathf.Max(0.1f, s.excaliburFireTime));
        bool charging = charge > 0.001f;
        if (charging && !m_chargePost) { AcquirePost(); m_chargePost = true; }
        if (!charging && m_chargePost) { ReleasePost(); m_chargePost = false; }
        float f = m_fireLevel;
        m_volume.weight = charging || f > 0.001f ? 1f : 0f;
        var warm = new Color(1f, 0.9f, 0.7f);
        m_color.colorFilter.Override(Color.Lerp(Color.white, warm, Mathf.Max(0.5f * charge, 0.6f * f)));
        // 斬った直後だけ一瞬白く光り、そのあとは画面を暗く締めて、光の波だけが明るく見えるようにする
        float spike = Mathf.Clamp01((f - 0.88f) / 0.12f);
        m_color.postExposure.Override(-0.5f * charge * (1f - f) - 0.9f * f * (1f - spike) + 1.2f * spike);
        m_color.contrast.Override(25f * Mathf.Max(charge, f));
        m_bloom.intensity.Override(1f + 3f * charge + 4f * f);
        m_vignette.intensity.Override(Mathf.Max(0.35f * charge, 0.45f * f));
        m_chroma.intensity.Override(0.15f * charge + 0.7f * f * f);
        m_lens.intensity.Override(0.3f * f * f);
    }
    bool m_chargePost;

    void AcquirePost() { VfxKit.AcquirePost(); m_postUsers++; }
    void ReleasePost() { if (m_postUsers <= 0) return; m_postUsers--; VfxKit.ReleasePost(); }

    void OnDisable()
    {
        while (m_postUsers > 0) ReleasePost();
        m_chargePost = false;
    }

    // ---------------- 部品 ----------------
    void Ensure(MatchVfxSettings s)
    {
        if (m_motes == null) m_motes = VfxKit.CreateSystem("ExcaliburMotes", transform, true, VfxKit.SoftDot, 1500, 0.3f, -0.05f, false, 0f, 2.5f);
        if (m_gather == null) m_gather = VfxKit.CreateSystem("ExcaliburGather", transform, true, VfxKit.SoftDot, 1500, 0.5f, 0f, false, 0.04f, 3f);
        if (m_wind == null) m_wind = VfxKit.CreateSystem("ExcaliburWind", transform, false, VfxKit.SoftDot, 800, 1f, 0f, false, 0.06f);
        if (m_embers == null) m_embers = VfxKit.CreateSystem("ExcaliburEmbers", transform, true, VfxKit.SoftDot, 2000, 0.4f, -0.1f, true, 0f, 3f);
        if (m_dust == null) m_dust = VfxKit.CreateSystem("ExcaliburDust", transform, false, VfxKit.SoftDot, 600, 2.5f, 0.3f, true);
        if (m_glowMat == null) { m_glowMat = VfxKit.AddMat(VfxKit.SoftDot, 3f); m_glowMat.SetFloat("_ZTest", (float)CompareFunction.Always); }
        if (m_frontMat == null) { m_frontMat = VfxKit.AddMat(VfxKit.SoftDot, 4f); m_frontMat.SetFloat("_ZTest", (float)CompareFunction.Always); }
        if (m_pillarMat == null) m_pillarMat = VfxKit.AddMat(VfxKit.Pillar, 3f);
        if (m_curtainMat == null) m_curtainMat = VfxKit.AddMat(VfxKit.Curtain, 2.5f);
        if (m_flash == null)
        {
            var canvas = ToyKit.CreateOverlayCanvas("ExcaliburFlash", 16);
            canvas.SetParent(transform, false);
            m_flash = ToyKit.UIImage("Flash", canvas, new Color(1f, 1f, 1f, 0f)).GetComponent<Image>();
            m_flash.raycastTarget = false;
            ToyKit.Stretch(m_flash.rectTransform);
        }
        if (m_volume == null)
        {
            var go = new GameObject("ExcaliburVolume");
            go.transform.SetParent(transform, false);
            m_volume = go.AddComponent<Volume>();
            m_volume.isGlobal = true;
            m_volume.priority = 105f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_color = profile.Add<ColorAdjustments>(true);
            m_bloom = profile.Add<Bloom>(true);
            m_vignette = profile.Add<Vignette>(true);
            m_chroma = profile.Add<ChromaticAberration>(true);
            m_lens = profile.Add<LensDistortion>(true);
            m_bloom.threshold.Override(1f);
            m_bloom.scatter.Override(0.75f);
            m_bloom.tint.Override(new Color(1f, 0.85f, 0.55f));
            m_vignette.color.Override(new Color(0.15f, 0.1f, 0f));
            m_volume.sharedProfile = profile;
            m_volume.weight = 0f;
        }
    }

    static void Emit(ParticleSystem ps, float amount, System.Action emitOne)
    {
        int n = Mathf.FloorToInt(amount) + (Random.value < amount - Mathf.Floor(amount) ? 1 : 0);
        for (int i = 0; i < n; i++) emitOne();
    }

    GameObject MakeQuad(string name, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        var r = go.GetComponent<MeshRenderer>();
        r.material = new Material(mat);
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    // カメラを向く板
    static void Billboard(GameObject quad, Vector3 at, float size, Color color)
    {
        var cam = Camera.main;
        quad.transform.position = at;
        if (cam != null) quad.transform.rotation = Quaternion.LookRotation(at - cam.transform.position, cam.transform.up);
        quad.transform.localScale = Vector3.one * size;
        quad.GetComponent<MeshRenderer>().material.color = color;
    }

    // 立ち上る柱：bottom から上へ height。縦の軸のまわりだけカメラを向く
    static void Column(GameObject quad, Vector3 bottom, float width, float height, Color color)
    {
        var cam = Camera.main;
        quad.transform.position = bottom + Vector3.up * (height * 0.5f);
        Vector3 to = cam != null ? cam.transform.position - quad.transform.position : Vector3.back;
        to.y = 0f;
        if (to.sqrMagnitude > 1e-4f) quad.transform.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up);
        quad.transform.localScale = new Vector3(Mathf.Max(0.01f, width), Mathf.Max(0.01f, height), 1f);
        quad.GetComponent<MeshRenderer>().material.color = color;
    }

    // 線に沿った縦の板（start から dir の向きへ length、高さ height）
    static void Wall(GameObject quad, Vector3 start, Vector3 dir, float length, float height, Color color)
    {
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        quad.transform.position = start + dir * (length * 0.5f) + Vector3.up * (height * 0.5f);
        quad.transform.rotation = Quaternion.LookRotation(right, Vector3.up);   // 板の横（X）が攻撃の向きになる
        quad.transform.localScale = new Vector3(Mathf.Max(0.01f, length), Mathf.Max(0.01f, height), 1f);
        quad.GetComponent<MeshRenderer>().material.color = color;
    }

    void Clear(Track t)
    {
        if (t.glow != null) Destroy(t.glow);
        if (t.glowCore != null) Destroy(t.glowCore);
        if (t.pillar != null) Destroy(t.pillar);
        if (t.light != null) Destroy(t.light.gameObject);
        t.glow = t.glowCore = t.pillar = null;
        t.light = null;
    }
}
