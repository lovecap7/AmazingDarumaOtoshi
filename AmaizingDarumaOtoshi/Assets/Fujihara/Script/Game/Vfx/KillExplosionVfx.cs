using System.Collections;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// 4. 即死の大爆発（イメージ：呪術廻戦の「術式反転・赫」）
//    打ち返して攻撃力が上がった積み木（AttackPower が設定以上）が当たって、そのままやられた（即死）とき：
//    1. 溜め（収束）：ステージ全体から赤い光の筋が吸い込まれ、光の輪が何重にも縮んで集まる。
//       真ん中に赤い光の玉ができて脈打ちながらふくらみ、画面は暗く赤くなって、中心へ引き込まれるようにゆがむ。地鳴りのようにカメラが震える
//    2. 爆発（発散）：画面がはじけるように光り、赤い光の球・衝撃波の輪・床の輪が一気に広がり、光の筋と火の粉が四方へ飛び散る。カメラが大きく揺れる
//    撃墜のスロー（中川さんの EliminationCinematic）の間も動くよう、実時間で動かす。
//    画面の色は、演出の間だけ試合のカメラのポストエフェクトを入れて、この演出用の Volume で変える（ふだんの見た目は変えない）
public class KillExplosionVfx : MonoBehaviour
{
    struct Pending { public int frame; public int power; }
    readonly Dictionary<DarumaCharacter, Pending> m_pending = new Dictionary<DarumaCharacter, Pending>();

    ParticleSystem m_streak, m_glow, m_spark, m_smoke;
    Material m_coreMat, m_ringMat;
    Image m_flash;

    // 画面の色
    Volume m_volume;
    ColorAdjustments m_color;
    Vignette m_vignette;
    Bloom m_bloom;
    ChromaticAberration m_chroma;
    LensDistortion m_lens;
    bool m_postOwned;
    int m_running;

    void OnEnable()
    {
        TumikiBlock.Hit += OnBlockHit;
        DarumaCharacter.AnyEliminated += OnEliminated;
    }

    void OnDisable()
    {
        TumikiBlock.Hit -= OnBlockHit;
        DarumaCharacter.AnyEliminated -= OnEliminated;
        ReleasePost();
    }

    // 当たった（この直後に、当たった人の処理で即死することがある）
    void OnBlockHit(TumikiHitInfo info)
    {
        var s = MatchVfxSettings.Current;
        if (!s.killExplosion || !info.IsHarmful || info.Block == null) return;
        if (!(info.Target is DarumaCharacter victim)) return;
        if (info.Block.AttackPower < s.minAttackPower) return;
        m_pending[victim] = new Pending { frame = Time.frameCount, power = info.Block.AttackPower };
    }

    void OnEliminated(DarumaCharacter c)
    {
        if (c == null || !m_pending.TryGetValue(c, out var p)) return;
        m_pending.Remove(c);
        if (c.EliminationCause != EliminationCause.Projectile || Time.frameCount - p.frame > 2) return;
        Vector3 at = c.EliminationPosition + Vector3.up * 0.7f;
        Debug.Log($"[演出] 即死の大爆発：{c.name}（攻撃力 {p.power} の積み木）");
        StartCoroutine(Explode(at, p.power));
    }

    void Ensure(MatchVfxSettings s)
    {
        if (m_streak == null) m_streak = VfxKit.CreateSystem("KillStreak", transform, true, VfxKit.SoftDot, 3000, 0.6f, 0f, true, 0.05f, s.glowIntensity);
        if (m_glow == null) m_glow = VfxKit.CreateSystem("KillGlow", transform, true, VfxKit.SoftDot, 600, 1.8f, 0f, true, 0f, s.glowIntensity);
        if (m_spark == null) m_spark = VfxKit.CreateSystem("KillSpark", transform, true, VfxKit.SoftDot, 1500, 0.4f, 0.3f, true, 0.035f, s.glowIntensity);
        if (m_smoke == null) m_smoke = VfxKit.CreateSystem("KillSmoke", transform, false, VfxKit.SoftDot, 300, 2.6f, -0.05f, true);
        // 光の玉・輪は、床や物にめり込んで隠れないよう、いつも手前に描く
        if (m_coreMat == null) { m_coreMat = VfxKit.AddMat(VfxKit.SoftDot, s.glowIntensity); m_coreMat.SetFloat("_ZTest", (float)CompareFunction.Always); }
        if (m_ringMat == null) { m_ringMat = VfxKit.AddMat(VfxKit.Ring, s.glowIntensity); m_ringMat.SetFloat("_ZTest", (float)CompareFunction.Always); }
        if (m_flash == null)
        {
            var canvas = ToyKit.CreateOverlayCanvas("KillFlash", 16);
            canvas.SetParent(transform, false);
            m_flash = ToyKit.UIImage("Flash", canvas, new Color(1f, 1f, 1f, 0f)).GetComponent<Image>();
            m_flash.raycastTarget = false;
            ToyKit.Stretch(m_flash.rectTransform);
        }
        if (m_volume == null)
        {
            var go = new GameObject("KillVolume");
            go.transform.SetParent(transform, false);
            m_volume = go.AddComponent<Volume>();
            m_volume.isGlobal = true;
            m_volume.priority = 110f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_color = profile.Add<ColorAdjustments>(true);
            m_vignette = profile.Add<Vignette>(true);
            m_bloom = profile.Add<Bloom>(true);
            m_chroma = profile.Add<ChromaticAberration>(true);
            m_lens = profile.Add<LensDistortion>(true);
            m_vignette.smoothness.Override(0.6f);
            m_bloom.threshold.Override(1f);
            m_bloom.scatter.Override(0.75f);
            m_volume.sharedProfile = profile;
            m_volume.weight = 0f;
        }
    }

    // 画面の色・ゆがみ（演出の各時点で呼ぶ）
    void SetScreen(float darken, Color filter, float exposure, float vignette, float bloom, float chroma, float lens)
    {
        m_volume.weight = 1f;
        m_color.colorFilter.Override(Color.Lerp(Color.white, filter, darken));
        m_color.postExposure.Override(exposure);
        m_color.saturation.Override(-30f * darken);
        m_vignette.color.Override(new Color(0.25f, 0f, 0f));
        m_vignette.intensity.Override(vignette);
        m_bloom.intensity.Override(bloom);
        m_chroma.intensity.Override(chroma);
        m_lens.intensity.Override(lens);
    }

    IEnumerator Explode(Vector3 at, int power)
    {
        var s = MatchVfxSettings.Current;
        Ensure(s);
        m_running++;
        TakePost();
        float size = s.explosionSize * (1f + 0.1f * Mathf.Max(0, power - 2));
        Color core = s.coreColor;
        Color red = s.outerColor;
        var cam = Camera.main;
        var shaker = cam != null ? cam.GetComponent<Nakagawa.CameraShake>() : null;

        var orb = MakeQuad("KillOrb", m_coreMat);
        var orbCore = MakeQuad("KillOrbCore", m_coreMat);
        var light = new GameObject("KillLight").AddComponent<Light>();
        light.transform.SetParent(transform, false);
        light.type = LightType.Point;
        light.color = red;
        light.range = size * 2.5f;
        light.transform.position = at;
        var rings = new List<GameObject>();

        // ---- 1. 溜め（収束） ----
        float charge = Mathf.Max(0.1f, s.chargeTime);
        float emitAcc = 0f, ringTimer = 0f, rumble = 0f;
        for (float t = 0f; t < charge; t += Time.unscaledDeltaTime)
        {
            float dt = Time.unscaledDeltaTime;
            float u = t / charge;
            // ステージ全体から光の筋：遠くの床の上に出して、寿命の終わりにちょうど真ん中へ着くように飛ばす
            emitAcc += dt * Mathf.Lerp(s.gatherRateStart, s.gatherRateEnd, u * u);
            while (emitAcc >= 1f)
            {
                emitAcc -= 1f;
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(3f, s.gatherRadius);
                Vector3 from = at + new Vector3(Mathf.Cos(ang) * r, Random.Range(-0.6f, 5f), Mathf.Sin(ang) * r);
                float life = Random.Range(0.25f, 0.55f) * Mathf.Lerp(1f, 0.6f, u);
                Vector3 v = (at - from) / life;
                Color c = Random.value < 0.75f ? red : Color.Lerp(red, core, 0.7f);
                VfxKit.Emit(m_streak, from, v, Random.Range(0.06f, 0.16f), life, c);
            }
            // 光の輪が何重にも、外から真ん中へ縮んでくる
            ringTimer -= dt;
            if (ringTimer <= 0f)
            {
                ringTimer = Mathf.Lerp(0.2f, 0.08f, u);
                StartCoroutine(ContractRing(at, size * 0.9f, 0.35f, red));
            }
            // 真ん中の光の玉：脈打ちながらふくらみ、だんだん速く脈打つ
            float pulse = 1f + 0.2f * Mathf.Sin(t * Mathf.Lerp(20f, 70f, u));
            SetQuad(orb, at, Mathf.Lerp(0.4f, 2.2f, u * u) * pulse * s.chargeScale, red);
            SetQuad(orbCore, at, Mathf.Lerp(0.15f, 0.9f, u * u) * pulse * s.chargeScale, core);
            light.intensity = Mathf.Lerp(1f, 10f, u) * pulse;
            // 画面：暗く赤く、中心へ引き込まれるようにゆがむ
            SetScreen(s.chargeDarken * Mathf.Clamp01(u * 1.5f), new Color(1f, 0.45f, 0.45f), -1.2f * s.chargeDarken * u,
                0.5f * u, 1.5f + 3f * u, 0.5f * u, -0.35f * u * u);
            // 地鳴り
            rumble -= dt;
            if (shaker != null && rumble <= 0f) { rumble = 0.1f; shaker.Shake(new Nakagawa.CameraShake.ShakeSettings(0.03f + 0.15f * u, 0.15f)); }
            yield return null;
        }

        // ---- 2. 爆発（発散） ----
        if (shaker != null) shaker.Shake(new Nakagawa.CameraShake.ShakeSettings(1.2f, 1.0f));
        // 光の筋：四方へ（横に広く）
        for (int i = 0; i < 420; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            dir.y = dir.y * 0.45f + 0.1f;
            dir.Normalize();
            VfxKit.Emit(m_spark, at, dir * Random.Range(18f, 55f) * (size / 9f), Random.Range(0.08f, 0.2f), Random.Range(0.3f, 0.7f),
                Random.value < 0.7f ? red : core);
        }
        // 大きな光のかたまり
        for (int i = 0; i < 70; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            VfxKit.Emit(m_glow, at + dir * 0.3f, dir * Random.Range(3f, 12f), Random.Range(1f, 2.6f) * (size / 9f), Random.Range(0.3f, 0.6f),
                Color.Lerp(red, core, Random.Range(0f, 0.5f)));
        }
        // 煙
        for (int i = 0; i < 40; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.5f;
            VfxKit.Emit(m_smoke, at + dir * 0.8f, dir * Random.Range(2f, 7f), Random.Range(1.2f, 2.4f) * (size / 9f), Random.Range(1f, 1.8f),
                new Color(0.3f, 0.12f, 0.1f, 0.5f));
        }
        var shell = MakeQuad("KillShell", m_ringMat);    // 衝撃波（カメラを向く輪）
        var floorRing = MakeQuad("KillFloorRing", m_ringMat);
        Vector3 floor = new Vector3(at.x, at.y - 0.65f, at.z);

        float boom = Mathf.Max(0.1f, s.explosionTime);
        for (float t = 0f; t < boom; t += Time.unscaledDeltaTime)
        {
            float u = t / boom;
            float grow = 1f - (1f - u) * (1f - u) * (1f - u);
            float fade = (1f - u) * (1f - u);
            // 光の球：一気にふくらんで消える
            SetQuad(orb, at, Mathf.Lerp(2.2f * s.chargeScale, size * 2.4f, grow), new Color(red.r, red.g, red.b, fade));
            SetQuad(orbCore, at, Mathf.Lerp(0.9f * s.chargeScale, size * 1.2f, grow), new Color(core.r, core.g, core.b, fade * fade));
            SetQuad(shell, at, Mathf.Lerp(1f, size * 3.2f, grow), new Color(red.r, red.g, red.b, fade));
            floorRing.transform.position = floor;
            floorRing.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floorRing.transform.localScale = Vector3.one * Mathf.Lerp(1f, size * 4.5f, grow);
            floorRing.GetComponent<MeshRenderer>().material.color = new Color(red.r, red.g, red.b, fade);
            light.intensity = Mathf.Lerp(30f, 0f, u);
            light.range = size * 2.5f + size * 3f * grow;
            // 画面：はじけるように明るく → 戻る。ゆがみは外へ押し出してから戻る
            float hit = Mathf.Clamp01(1f - u * 4f);
            SetScreen(s.chargeDarken * (1f - u), new Color(1f, 0.45f, 0.45f), 2.2f * hit - 0.4f * (1f - u),
                0.45f * (1f - u), 2f + 10f * fade, 1f * fade, 0.45f * hit);
            m_flash.color = new Color(1f, Mathf.Lerp(0.6f, 0.95f, hit), Mathf.Lerp(0.55f, 0.9f, hit), 0.85f * hit);
            yield return null;
        }

        m_flash.color = new Color(1f, 1f, 1f, 0f);
        Destroy(orb); Destroy(orbCore); Destroy(shell); Destroy(floorRing);
        Destroy(light.gameObject);
        m_running--;
        if (m_running <= 0) { m_volume.weight = 0f; ReleasePost(); }
    }

    // 光の輪が外から真ん中へ縮んで消える
    IEnumerator ContractRing(Vector3 at, float from, float time, Color color)
    {
        var ring = MakeQuad("KillGatherRing", m_ringMat);
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            float u = t / time;
            SetQuad(ring, at, Mathf.Lerp(from, 0.2f, u * u), new Color(color.r, color.g, color.b, Mathf.Sin(u * Mathf.PI)));
            yield return null;
        }
        Destroy(ring);
    }

    GameObject MakeQuad(string name, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        var r = go.GetComponent<MeshRenderer>();
        r.material = new Material(mat);
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    // カメラを向く板を、その位置・大きさ・色にする
    static void SetQuad(GameObject quad, Vector3 at, float size, Color color)
    {
        var cam = Camera.main;
        quad.transform.position = at;
        if (cam != null) quad.transform.rotation = Quaternion.LookRotation(quad.transform.position - cam.transform.position, cam.transform.up);
        quad.transform.localScale = Vector3.one * size;
        quad.GetComponent<MeshRenderer>().material.color = color;
    }

    // 演出の間だけ、試合のカメラのポストエフェクトを入れる（ほかの演出と一緒に数える：VfxKit.AcquirePost）
    void TakePost()
    {
        if (m_postOwned) return;
        VfxKit.AcquirePost();
        m_postOwned = true;
    }

    void ReleasePost()
    {
        if (!m_postOwned) return;
        VfxKit.ReleasePost();
        m_postOwned = false;
    }
}
