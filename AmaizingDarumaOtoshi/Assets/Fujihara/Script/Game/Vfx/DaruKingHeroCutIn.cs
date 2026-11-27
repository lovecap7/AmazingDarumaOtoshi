using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// だるきんぐの必殺技「エクスカリバー」のカットイン（イメージ：イナズマイレブン 英雄たちのヴィクトリーロードの必殺技）。
// ゆるべさんの DaruKingCutIn の差し替え口（DaruKingCutIn.Override）から呼ばれる。必殺技の動き・当たり判定は変えない。
//  ・ゲームは止まる（Nakahira.TimeStop。前のカットインと同じ）。止まっている間もこの演出は進む（unscaledTime）
//  ・試合中のだるきんぐ（頭・マント・鉢巻・剣）をそのまま複製して、専用の舞台で動かす
//  ・技名は画面の下に、効果音と一緒に1文字ずつ叩きつけるように出て、最後まで出しっぱなし（筆文字・二重の縁取り・少し傾ける）
//  ・カット割り（約 3.5 秒）
//    A 0.00〜0.55 構え　　：下からあおる。紺の背景に集中線。だるきんぐが跳ねて構える
//    B 0.55〜1.75 風の解放：まわりを回るカメラ。剣を掲げると風が渦巻き、星の光が剣に集まる。背景に金の光線
//    C 1.75〜2.70 聖剣顕現：真下から見上げる。剣から天へ光の柱、背後に巨大な光の王が現れる。背景は金色の空
//    D 2.70〜3.50 一閃　　：正面のアップ。剣を振り下ろして画面を斜めに斬る閃光・衝撃波、最後は白く飛んで試合へ戻る
public class DaruKingHeroCutIn : MonoBehaviour
{
    const int Layer = 30;
    const float Total = 3.5f;
    const float ShotB = 0.55f, ShotC = 1.75f, ShotD = 2.7f, SlashAt = 2.84f;
    const float LetterStart = 0.62f;

    // 確認用：0 以上ならこの時刻で止めて見せる（エディタでの撮影用）
    public static float DebugFreezeAt = -1f;

    static DaruKingHeroCutIn s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_instance = null; DebugFreezeAt = -1f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        Yurube.DaruKingCutIn.Override = TryShow;
        Yurube.DaruKingCutIn.OverridePlaying = () => s_instance != null && s_instance.m_active;
    }

    static bool TryShow(string specialName, GameObject subject)
    {
        if (!MatchVfxSettings.Current.heroCutIn) return false;
        if (s_instance == null)
        {
            var go = new GameObject("DaruKingHeroCutIn");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<DaruKingHeroCutIn>();
        }
        return s_instance.Play(specialName, subject);
    }

    // ---------------- 状態 ----------------
    bool m_active;
    float m_start;
    string m_name = "";
    int m_lettersShown;
    bool m_nameSe, m_chargeSe, m_slashDone, m_cutB, m_cutC, m_cutD;
    float m_shake;
    float m_lastAge, m_age;

    GameObject m_stage;
    Camera m_camera;
    RenderTexture m_rt;
    Transform m_hero, m_heroPivot, m_giant, m_giantPivot;
    float m_swordLen = 2.4f;
    Transform m_bgBase, m_bgRays;
    Material m_bgBaseMat, m_bgRaysMat, m_ghostMat, m_glowMat, m_pillarMat, m_ringMat;
    Transform m_glowA, m_glowB, m_pillar, m_blade;
    Material m_bladeMat;
    readonly List<Transform> m_rings = new List<Transform>();
    readonly List<float> m_ringStart = new List<float>();
    ParticleSystem m_motes, m_wind, m_sparks;
    Volume m_volume;
    AudioSource m_audio;
    readonly List<Object> m_owned = new List<Object>();

    static Texture2D s_rays, s_gradient;
    static AudioClip s_letterSe, s_nameSe, s_chargeSe, s_slashSe, s_whooshSe;

    static readonly Color Navy = new Color(0.04f, 0.06f, 0.2f);
    static readonly Color Royal = new Color(0.16f, 0.07f, 0.32f);
    static readonly Color GoldSky = new Color(1f, 0.72f, 0.3f);
    static readonly Color Gold = new Color(1f, 0.8f, 0.3f);
    static readonly Color Core = new Color(1f, 0.97f, 0.85f);

    // ---------------- 始める・終わる ----------------
    bool Play(string specialName, GameObject subject)
    {
        var caster = subject != null ? subject.GetComponentInParent<Nakahira.DarumaCharacter>() : null;
        Transform visual = caster != null ? caster.transform.Find("Visual") : null;
        if (visual == null && subject != null) visual = subject.transform;
        if (visual == null) return false;

        if (m_active) Cleanup();
        else Nakahira.TimeStop.Begin(this, 0f);

        m_active = true;
        m_start = Time.unscaledTime;
        m_age = 0f;
        m_name = string.IsNullOrEmpty(specialName) ? "エクスカリバー" : specialName;
        m_lettersShown = 0;
        m_nameSe = m_chargeSe = m_slashDone = m_cutB = m_cutC = m_cutD = false;
        m_shake = 0.6f;
        m_lastAge = 0f;
        Build(visual);
        PlaySe(Clip(MatchVfxSettings.Current.cutInChargeSe, ref s_whooshSe, MakeWhoosh), 0.8f);
        return true;
    }

    void End()
    {
        if (!m_active) return;
        m_active = false;
        Cleanup();
        Nakahira.TimeStop.End(this);
    }

    void OnDisable() => End();

    void Cleanup()
    {
        if (m_camera != null) m_camera.targetTexture = null;
        if (m_stage != null) Destroy(m_stage);
        foreach (var o in m_owned) if (o != null) Destroy(o);
        m_owned.Clear();
        if (m_rt != null) { m_rt.Release(); Destroy(m_rt); }
        m_stage = null; m_camera = null; m_rt = null;
        m_hero = m_heroPivot = m_giant = m_giantPivot = null;
        m_rings.Clear(); m_ringStart.Clear();
    }

    // ---------------- 舞台を作る ----------------
    void Build(Transform visual)
    {
        m_stage = new GameObject("HeroCutInStage");
        m_stage.transform.position = new Vector3(20000f, 20000f, 20000f);

        m_rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
        m_rt.Create();

        var camGo = new GameObject("HeroCutInCamera");
        camGo.transform.SetParent(m_stage.transform, false);
        m_camera = camGo.AddComponent<Camera>();
        m_camera.clearFlags = CameraClearFlags.SolidColor;
        m_camera.backgroundColor = Color.black;
        m_camera.cullingMask = 1 << Layer;
        m_camera.nearClipPlane = 0.05f;
        m_camera.farClipPlane = 80f;
        m_camera.targetTexture = m_rt;
        var camData = m_camera.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;
        camData.volumeLayerMask = 1 << Layer;   // この舞台の光のにじみだけ（試合の画面の色調整は使わない）
        camData.renderShadows = false;

        // 光のにじみ（この舞台だけの Volume）
        var volGo = new GameObject("HeroCutInVolume") { layer = Layer };
        volGo.transform.SetParent(m_stage.transform, false);
        m_volume = volGo.AddComponent<Volume>();
        m_volume.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        m_owned.Add(profile);
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(1.6f);
        bloom.threshold.Override(0.9f);
        bloom.scatter.Override(0.7f);
        bloom.tint.Override(new Color(1f, 0.9f, 0.7f));
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.32f);
        vignette.color.Override(new Color(0.05f, 0.02f, 0f));
        m_volume.sharedProfile = profile;

        AddLight(new Vector3(-1.5f, 2.6f, -2.5f), new Color(1f, 0.86f, 0.55f), 5f, 9f);
        AddLight(new Vector3(2.2f, 1.6f, 1.5f), new Color(0.5f, 0.6f, 1f), 6f, 7f);

        // 背景（カメラの子。遠くに置いた大きな板）
        m_bgBaseMat = VfxKit.Mat(Gradient());
        m_bgBaseMat.renderQueue = 1000;
        m_owned.Add(m_bgBaseMat);
        m_bgBase = MakeQuad("Bg", m_bgBaseMat, camGo.transform).transform;
        m_bgRaysMat = VfxKit.AddMat(Rays(), 1.4f);
        m_bgRaysMat.renderQueue = 1001;
        m_bgRaysMat.SetFloat("_ZTest", (float)CompareFunction.Always);
        m_owned.Add(m_bgRaysMat);
        m_bgRays = MakeQuad("BgRays", m_bgRaysMat, camGo.transform).transform;

        // だるきんぐ（試合のものを複製）
        m_hero = Clone(visual, "Hero");
        m_heroPivot = FindChild(m_hero, "HammerPivot");
        m_swordLen = SwordLength(m_heroPivot);

        // 背後の巨大な光の王（同じ形を、光る材質で）
        m_ghostMat = VfxKit.AddMat(Texture2D.whiteTexture, 0.7f);
        m_owned.Add(m_ghostMat);
        m_giant = Clone(visual, "Giant");
        foreach (var r in m_giant.GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = m_ghostMat;
            r.sharedMaterials = mats;
        }
        m_giantPivot = FindChild(m_giant, "HammerPivot");
        m_giant.gameObject.SetActive(false);

        // 光のもの
        m_glowMat = VfxKit.AddMat(VfxKit.SoftDot, 3f);
        m_pillarMat = VfxKit.AddMat(VfxKit.Pillar, 3f);
        m_ringMat = VfxKit.AddMat(VfxKit.Ring, 3f);
        m_owned.Add(m_glowMat); m_owned.Add(m_pillarMat); m_owned.Add(m_ringMat);
        var glowB = new Material(m_glowMat);
        m_owned.Add(glowB);
        m_glowA = MakeQuad("SwordGlow", m_glowMat, m_stage.transform).transform;
        m_glowB = MakeQuad("SwordGlowCore", glowB, m_stage.transform).transform;
        m_pillar = MakeQuad("Pillar", m_pillarMat, m_stage.transform).transform;
        m_bladeMat = VfxKit.AddMat(VfxKit.Beam, 3f);
        m_owned.Add(m_bladeMat);
        m_blade = MakeQuad("BladeGlow", m_bladeMat, m_stage.transform).transform;

        m_motes = Particles("HeroMotes", true, VfxKit.SoftDot, 0.4f, 0f, 0f, 3f);
        m_wind = Particles("HeroWind", false, VfxKit.SoftDot, 1f, 0f, 0.05f, 1f);
        m_sparks = Particles("HeroSparks", true, VfxKit.SoftDot, 0.3f, 0.5f, 0.03f, 3f);

        if (m_audio == null)
        {
            m_audio = gameObject.AddComponent<AudioSource>();
            m_audio.playOnAwake = false;
            m_audio.spatialBlend = 0f;
            m_audio.ignoreListenerPause = true;
        }
    }

    Transform Clone(Transform source, string name)
    {
        // 止めた入れ物の中で複製して、動き出す前にスクリプトを外す（マント・鉢巻などのスクリプトが、複製の瞬間に動いてしまわないように）
        var holder = new GameObject("CloneHolder");
        holder.SetActive(false);
        var go = Instantiate(source.gameObject, holder.transform, false);
        go.name = name;
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
        foreach (var b in go.GetComponentsInChildren<Behaviour>(true)) if (!(b is Animator)) b.enabled = false;
        go.transform.SetParent(m_stage.transform, false);
        Destroy(holder);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.detectCollisions = false; }
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        // 頭のモデルの色（試合では MaterialPropertyBlock で白にしている）を写す。点滅で消えていても出す
        var src = source.GetComponentsInChildren<Renderer>(true);
        var dst = go.GetComponentsInChildren<Renderer>(true);
        var block = new MaterialPropertyBlock();
        for (int i = 0; i < dst.Length; i++)
        {
            dst[i].enabled = true;
            dst[i].shadowCastingMode = ShadowCastingMode.Off;
            if (dst[i] is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            if (i >= src.Length || !src[i].HasPropertyBlock()) continue;
            src[i].GetPropertyBlock(block); dst[i].SetPropertyBlock(block);
            for (int m = 0; m < src[i].sharedMaterials.Length; m++) { src[i].GetPropertyBlock(block, m); dst[i].SetPropertyBlock(block, m); }
        }
        // 積み木の数で頭の高さが変わるので、積み木が無いときの高さにそろえる（積み木は複製しない。頭＝王様だけ）
        var head = FindChild(go.transform, "Head");
        if (head != null) head.localPosition = new Vector3(0f, 0.45f, 0f);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 顔（+Z）をカメラ（-Z 側）へ向ける
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    static float SwordLength(Transform pivot)
    {
        if (pivot == null) return 2.4f;
        float tip = 0f;
        foreach (var mf in pivot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                tip = Mathf.Max(tip, pivot.InverseTransformPoint(mf.transform.TransformPoint(corner)).z);
            }
        }
        return tip > 0.3f ? tip : 2.4f;
    }

    void AddLight(Vector3 local, Color color, float intensity, float range)
    {
        var go = new GameObject("HeroLight");
        go.transform.SetParent(m_stage.transform, false);
        go.transform.localPosition = local;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
    }

    GameObject MakeQuad(string name, Material mat, Transform parent)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        DestroyImmediate(go.GetComponent<Collider>());
        go.layer = Layer;
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    ParticleSystem Particles(string name, bool additive, Texture tex, float grow, float gravity, float stretch, float intensity)
    {
        var ps = VfxKit.CreateSystem(name, m_stage.transform, additive, tex, 1500, grow, gravity, true, stretch, intensity);
        ps.gameObject.layer = Layer;
        m_owned.Add(ps.GetComponent<ParticleSystemRenderer>().sharedMaterial);
        return ps;
    }

    // ---------------- 毎フレーム ----------------
    void Update()
    {
        if (!m_active) return;
        // 時間は1フレームずつ（最大 1/20 秒）進める。始めの1フレームで準備に時間がかかっても、演出が飛ばないように
        float dt = DebugFreezeAt >= 0f ? 1f / 60f : Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (DebugFreezeAt < 0f) m_age += dt;
        float age = DebugFreezeAt >= 0f ? DebugFreezeAt : m_age;
        if (age >= Total) { End(); return; }
        m_lastAge = age;
        m_shake = Mathf.MoveTowards(m_shake, 0f, dt * 2.2f);

        Events(age);
        AnimateHero(m_hero, m_heroPivot, age);
        AnimateCamera(age);
        AnimateBackground(age);
        AnimateLight(age, dt);
    }

    void Events(float age)
    {
        var s = MatchVfxSettings.Current;
        if (!m_cutB && age >= ShotB) { m_cutB = true; m_shake = 0.4f; }
        if (!m_chargeSe && age >= ShotB + 0.05f) { m_chargeSe = true; PlaySe(Clip(s.cutInChargeSe, ref s_chargeSe, MakeCharge), 0.7f); }
        // 技名を1文字ずつ（効果音つき）
        float interval = Mathf.Max(0.03f, s.cutInLetterInterval);
        int want = Mathf.Clamp(Mathf.FloorToInt((age - LetterStart) / interval) + 1, 0, m_name.Length);
        while (m_lettersShown < want)
        {
            m_lettersShown++;
            if (char.IsWhiteSpace(m_name[m_lettersShown - 1])) continue;
            m_audio.pitch = 1f + 0.03f * m_lettersShown;
            PlaySe(Clip(s.cutInLetterSe, ref s_letterSe, MakeLetter), 0.9f);
            m_shake = Mathf.Max(m_shake, 0.22f);
        }
        if (!m_nameSe && m_lettersShown >= m_name.Length && age >= LetterStart + interval * m_name.Length)
        {
            m_nameSe = true;
            m_audio.pitch = 1f;
            PlaySe(Clip(s.cutInNameSe, ref s_nameSe, MakeName), 0.85f);
            m_shake = Mathf.Max(m_shake, 0.35f);
        }
        if (!m_cutC && age >= ShotC) { m_cutC = true; m_shake = 0.5f; m_giant.gameObject.SetActive(true); SpawnRing(age); }
        if (!m_cutD && age >= ShotD) { m_cutD = true; m_shake = 0.3f; m_giant.gameObject.SetActive(false); }
        if (!m_slashDone && age >= SlashAt)
        {
            m_slashDone = true;
            m_audio.pitch = 1f;
            PlaySe(Clip(s.cutInSlashSe, ref s_slashSe, MakeSlash), 1f);
            m_shake = 1f;
            SpawnRing(age); SpawnRing(age + 0.06f);
            Vector3 tip = m_heroPivot != null ? m_heroPivot.TransformPoint(0f, 0f, m_swordLen) : m_stage.transform.position;
            for (int i = 0; i < 120; i++)
                VfxKit.Emit(m_sparks, tip, Random.onUnitSphere * Random.Range(3f, 12f), Random.Range(0.05f, 0.14f), Random.Range(0.3f, 0.8f), Random.value < 0.6f ? Gold : Core);
        }
    }

    // だるきんぐの動き（構え → 跳ねる → 剣を掲げる → 振り下ろす）。pivot は剣（ハンマー）の支点
    void AnimateHero(Transform hero, Transform pivot, float age)
    {
        if (hero == null) return;
        Vector3 pos = Vector3.zero;
        Vector3 scale = Vector3.one;
        float leanX = 0f, yaw = 0f;
        if (age < ShotB)
        {
            // 縮んでから跳ねて、着地で少しつぶれる
            float u = age / ShotB;
            float hop = Mathf.Sin(Mathf.Clamp01((u - 0.15f) / 0.6f) * Mathf.PI);
            pos.y = 0.45f * hop;
            float squash = u < 0.15f ? Mathf.Sin(u / 0.15f * Mathf.PI) : (u > 0.75f ? Mathf.Sin((u - 0.75f) / 0.25f * Mathf.PI) * 0.7f : 0f);
            scale = new Vector3(1f + 0.12f * squash, 1f - 0.16f * squash + 0.08f * hop, 1f + 0.12f * squash);
            yaw = Mathf.Lerp(-35f, 0f, Ease(u));
        }
        else if (age < ShotD)
        {
            // 剣を掲げて、力がたまるほど少し震える
            float u = Mathf.Clamp01((age - ShotB) / 0.45f);
            leanX = -8f * Ease(u);
            float tremble = Mathf.Clamp01((age - ShotB) / (ShotD - ShotB));
            pos.x = Mathf.Sin(age * 70f) * 0.012f * tremble;
            pos.y = 0.03f * Mathf.Sin(age * 6f);
        }
        else
        {
            // 振り下ろし：前へ踏み込んで沈み、戻る
            float u = Mathf.Clamp01((age - ShotD) / (SlashAt - ShotD));
            float after = Mathf.Clamp01((age - SlashAt) / 0.4f);
            leanX = Mathf.Lerp(-10f, 10f, Ease(u)) * (1f - 0.4f * after);
            pos.z = -0.15f * Ease(u);   // カメラへ踏み込む（だるきんぐは 180 度回っているので -Z が前）
            pos.y = -0.08f * Mathf.Sin(Mathf.Clamp01(u + after) * Mathf.PI);
            scale = new Vector3(1f + 0.06f * (1f - after) * u, 1f - 0.08f * (1f - after) * u, 1f);
        }
        hero.localPosition = pos;
        hero.localScale = scale;
        hero.localRotation = Quaternion.Euler(0f, 180f + yaw, 0f) * Quaternion.Euler(leanX, 0f, 0f);

        if (pivot == null) return;
        // 剣の向き（だるきんぐから見た向き）：構え → 掲げる（真上）→ 振り下ろす（前の下）
        var ready = ReadyPose;
        var raised = RaisedPose;
        // 振りかぶり：真上から少し後ろへ
        var windup = new Pose(new Vector3(0f, 1.0f, -0.15f), Quaternion.LookRotation(new Vector3(0f, 0.85f, -0.5f), Vector3.back));
        // 振り下ろし：正面（真上 → 真下、カメラの方へ）に斬り下ろす
        var swung = new Pose(new Vector3(0f, 0.6f, 0.3f), Quaternion.LookRotation(new Vector3(0f, -0.5f, 0.87f), Vector3.up));
        Pose p;
        if (age < ShotB) p = ready;
        else if (age < ShotD) p = Blend(ready, raised, Ease(Mathf.Clamp01((age - ShotB) / 0.45f)));
        else if (age < SlashAt - 0.06f) p = Blend(raised, windup, Ease(Mathf.Clamp01((age - ShotD) / 0.08f)));
        else p = Blend(windup, swung, Mathf.Clamp01((age - (SlashAt - 0.06f)) / 0.06f));
        pivot.localPosition = p.position;
        pivot.localRotation = p.rotation;
    }

    // 剣の構え（だるきんぐから見た向き。+Z が前、+X が右）
    static readonly Pose ReadyPose = new Pose(new Vector3(0.6f, 0.5f, 0.1f), Quaternion.LookRotation(new Vector3(0.55f, 0.75f, 0.35f), Vector3.up));
    static readonly Pose RaisedPose = new Pose(new Vector3(0.18f, 0.95f, -0.05f), Quaternion.LookRotation(new Vector3(0.03f, 1f, -0.06f), Vector3.back));

    static Pose Blend(Pose a, Pose b, float t) => new Pose(Vector3.Lerp(a.position, b.position, t), Quaternion.Slerp(a.rotation, b.rotation, t));
    static float Ease(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }

    void AnimateCamera(float age)
    {
        if (m_camera == null) return;
        Vector3 pos, look;
        float fov;
        if (age < ShotB)
        {
            float u = age / ShotB;
            // 跳ねるだるきんぐを追いかける
            float follow = m_hero != null ? m_hero.localPosition.y * 0.8f : 0f;
            pos = Vector3.Lerp(new Vector3(1.5f, 0.15f, -3.1f), new Vector3(1.1f, 0.2f, -2.6f), Ease(u)) + Vector3.up * follow * 0.5f;
            look = new Vector3(-0.3f, 0.75f + follow, 0f);
            fov = 50f;
        }
        else if (age < ShotC)
        {
            float u = (age - ShotB) / (ShotC - ShotB);
            float ang = Mathf.Lerp(-50f, 30f, u) * Mathf.Deg2Rad;
            float r = Mathf.Lerp(3.4f, 2.8f, u);
            pos = new Vector3(Mathf.Sin(ang) * r, Mathf.Lerp(0.5f, 1.0f, u), -Mathf.Cos(ang) * r);
            look = new Vector3(0f, 1.05f, 0f);
            fov = 46f;
        }
        else if (age < ShotD)
        {
            float u = (age - ShotC) / (ShotD - ShotC);
            pos = Vector3.Lerp(new Vector3(0.6f, 0.05f, -3.6f), new Vector3(0.45f, 0.08f, -3.2f), u);
            look = new Vector3(0f, 2.6f, 1f);
            fov = 66f;
        }
        else
        {
            float u = Mathf.Clamp01((age - ShotD) / (Total - ShotD));
            // 正面から（剣を真っすぐ振り下ろすのが見えるように）
            pos = Vector3.Lerp(new Vector3(0f, 0.35f, -3.6f), new Vector3(0f, 0.35f, -3.1f), Ease(u));
            look = new Vector3(0f, 0.85f, 0f);
            fov = age < SlashAt ? 50f : Mathf.Lerp(42f, 50f, Mathf.Clamp01((age - SlashAt) / 0.4f));
        }
        var t = m_camera.transform;
        t.localPosition = pos;
        t.LookAt(m_stage.transform.TransformPoint(look));
        m_camera.fieldOfView = fov;

        // 背景の板をカメラの視界いっぱいに
        float d = 40f;
        float h = 2f * d * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 1.25f;
        if (m_bgBase != null) { m_bgBase.localPosition = new Vector3(0f, 0f, d); m_bgBase.localRotation = Quaternion.identity; m_bgBase.localScale = new Vector3(h * 16f / 9f, h, 1f); }
        if (m_bgRays != null) { m_bgRays.localPosition = new Vector3(0f, 0f, d - 1f); m_bgRays.localScale = new Vector3(h * 1.9f, h * 1.9f, 1f); }
    }

    void AnimateBackground(float age)
    {
        Color baseCol, raysCol;
        float spin;
        if (age < ShotB) { baseCol = Navy; raysCol = new Color(0.4f, 0.55f, 1f, 0.25f); spin = 20f; }
        else if (age < ShotC)
        {
            float u = Mathf.Clamp01((age - ShotB) / 0.6f);
            baseCol = Color.Lerp(Navy, Royal, u);
            raysCol = Color.Lerp(new Color(0.4f, 0.55f, 1f, 0.25f), new Color(Gold.r, Gold.g, Gold.b, 0.55f), u);
            spin = 35f;
        }
        // C：夕暮れのような深い紅紫の空に、金の光（光の王と柱が映えるように）
        else if (age < ShotD) { baseCol = new Color(0.32f, 0.09f, 0.22f); raysCol = new Color(1f, 0.78f, 0.35f, 0.5f); spin = 12f; }
        else
        {
            float flash = Mathf.Clamp01(1f - Mathf.Abs(age - SlashAt) / 0.25f);
            baseCol = Color.Lerp(new Color(0.12f, 0.06f, 0.2f), Core, flash);
            raysCol = new Color(1f, 0.9f, 0.6f, 0.4f + 0.5f * flash);
            spin = 60f;
        }
        if (m_bgBaseMat != null) m_bgBaseMat.color = baseCol;
        if (m_bgRaysMat != null) m_bgRaysMat.color = raysCol;
        if (m_bgRays != null) m_bgRays.localRotation = Quaternion.Euler(0f, 0f, age * spin);
    }

    void AnimateLight(float age, float dt)
    {
        if (m_heroPivot == null) return;
        var camRot = m_camera.transform.rotation;
        Vector3 basePt = m_heroPivot.TransformPoint(0f, 0f, m_swordLen * 0.15f);
        Vector3 tip = m_heroPivot.TransformPoint(0f, 0f, m_swordLen);
        Vector3 mid = (basePt + tip) * 0.5f;
        float charge = age < ShotB ? 0f : Mathf.Clamp01((age - ShotB) / (ShotC - ShotB));
        bool afterSlash = age >= SlashAt;
        float pulse = 1f + 0.12f * Mathf.Sin(age * 40f);

        // 剣の輝き
        float glow = afterSlash ? Mathf.Clamp01(1f - (age - SlashAt) / 0.3f) : Mathf.Lerp(0.15f, 1f, charge);
        SetBillboard(m_glowA, mid, camRot, (0.8f + 2.6f * glow) * pulse, new Color(Gold.r, Gold.g, Gold.b, 0.8f * glow));
        SetBillboard(m_glowB, tip, camRot, (0.4f + 1.4f * glow) * pulse, new Color(Core.r, Core.g, Core.b, glow));

        // 刀身そのものが光る（剣に沿った光の帯。カメラの方を向ける）
        if (m_blade != null)
        {
            Vector3 axis = (tip - basePt).normalized;
            Vector3 toCam = m_camera.transform.position - mid;
            Vector3 n = (toCam - axis * Vector3.Dot(toCam, axis)).normalized;
            if (n.sqrMagnitude > 1e-4f)
                m_blade.rotation = Quaternion.LookRotation(-n, Vector3.Cross(-n, axis));
            m_blade.position = Vector3.Lerp(basePt, tip, 0.55f);
            float bladeGlow = afterSlash ? 0.6f + glow : Mathf.Lerp(0.25f, 1f, charge);
            m_blade.localScale = new Vector3(Vector3.Distance(basePt, tip) * 0.95f, (0.18f + 0.3f * bladeGlow) * pulse, 1f);
            m_bladeMat.color = new Color(Core.r, Core.g, Core.b, Mathf.Clamp01(bladeGlow));
        }

        // 光の柱（C の間、剣先から天へ）
        bool pillarOn = age >= ShotC && age < ShotD;
        m_pillar.gameObject.SetActive(pillarOn);
        if (pillarOn)
        {
            float u = Mathf.Clamp01((age - ShotC) / 0.25f);
            float height = 40f * Ease(u);
            float width = Mathf.Lerp(0.3f, 0.9f, u) * pulse;
            m_pillar.position = tip + Vector3.up * (height * 0.5f);
            Vector3 to = m_camera.transform.position - m_pillar.position; to.y = 0f;
            if (to.sqrMagnitude > 1e-4f) m_pillar.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up);
            m_pillar.localScale = new Vector3(width, height, 1f);
            m_pillar.GetComponent<MeshRenderer>().sharedMaterial.color = new Color(Core.r, Core.g, Core.b, 0.75f);
        }

        // 巨大な光の王（C の間、背後でせり上がる）
        if (m_giant != null && m_giant.gameObject.activeSelf)
        {
            float u = Ease(Mathf.Clamp01((age - ShotC) / 0.5f));
            m_giant.localPosition = new Vector3(0f, Mathf.Lerp(-6f, -0.2f, u), 8f);
            m_giant.localScale = Vector3.one * 6f;
            m_giant.localRotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(-6f, 0f, 0f);
            if (m_giantPivot != null) { m_giantPivot.localPosition = RaisedPose.position; m_giantPivot.localRotation = RaisedPose.rotation; }
            m_ghostMat.color = new Color(1f, 0.7f, 0.25f, 0.4f * u * (0.9f + 0.1f * Mathf.Sin(age * 20f)));
        }

        // 星の光が剣へ集まる（B〜C）・風の渦（B）
        if (age >= ShotB && age < ShotD && dt > 0f)
        {
            int n = Mathf.RoundToInt(dt * Mathf.Lerp(80f, 260f, charge));
            for (int i = 0; i < n; i++)
            {
                Vector3 from = mid + Random.onUnitSphere * Random.Range(2.5f, 5f);
                float life = Random.Range(0.25f, 0.45f);
                VfxKit.Emit(m_motes, from, (Vector3.Lerp(basePt, tip, Random.value) - from) / life, Random.Range(0.05f, 0.12f), life, Random.value < 0.6f ? Gold : Core);
            }
        }
        if (age >= ShotB && age < ShotC && dt > 0f)
        {
            int n = Mathf.RoundToInt(dt * 160f);
            Vector3 c = m_hero.position - Vector3.up * 0.3f;
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(0.9f, 1.8f);
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var tangent = Vector3.Cross(Vector3.up, radial);
                VfxKit.Emit(m_wind, c + radial * r + Vector3.up * Random.Range(0f, 2.2f), tangent * Random.Range(9f, 15f) + Vector3.up * Random.Range(1f, 4f),
                    Random.Range(0.04f, 0.08f), Random.Range(0.15f, 0.3f), new Color(1f, 1f, 1f, 0.55f));
            }
        }

        // 衝撃波の輪
        for (int i = 0; i < m_rings.Count; i++)
        {
            float e = age - m_ringStart[i];
            var ring = m_rings[i];
            if (ring == null) continue;
            if (e < 0f) { ring.gameObject.SetActive(false); continue; }
            float u = Mathf.Clamp01(e / 0.45f);
            ring.gameObject.SetActive(u < 1f);
            SetBillboard(ring, m_hero.position + Vector3.up * 0.5f, camRot, Mathf.Lerp(0.5f, 9f, Ease(u)), new Color(Core.r, Core.g, Core.b, 1f - u));
        }
    }

    void SpawnRing(float at)
    {
        var ringMat = new Material(m_ringMat);
        m_owned.Add(ringMat);
        var ring = MakeQuad("HeroRing", ringMat, m_stage.transform).transform;
        m_rings.Add(ring);
        m_ringStart.Add(at);
    }

    static void SetBillboard(Transform t, Vector3 at, Quaternion camRot, float size, Color color)
    {
        if (t == null) return;
        t.position = at;
        t.rotation = camRot;
        t.localScale = Vector3.one * Mathf.Max(0.001f, size);
        t.GetComponent<MeshRenderer>().sharedMaterial.color = color;   // 板ごとに別のマテリアル
    }

    // ---------------- 画面（2D）：カメラの絵・集中線・技名・閃光 ----------------
    GUIStyle m_letterStyle, m_plateStyle;
    Font m_font;

    void OnGUI()
    {
        if (!m_active || Event.current.type != EventType.Repaint) return;
        float age = DebugFreezeAt >= 0f ? DebugFreezeAt : m_age;
        if (age >= Total) return;
        EnsureStyles();

        GUI.depth = -2000;
        var oldMatrix = GUI.matrix;
        var oldColor = GUI.color;
        float scale = Mathf.Max(0.4f, Screen.height / 1080f);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        float w = Screen.width / scale, h = Screen.height / scale;

        // カメラの絵（揺れ）
        float sh = m_shake * m_shake * 28f;
        var shake = new Vector2(Mathf.Sin(age * 97f) * sh, Mathf.Cos(age * 131f) * sh);
        if (m_rt != null)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(-30f + shake.x, -20f + shake.y, w + 60f, h + 40f), m_rt, ScaleMode.ScaleAndCrop);
        }

        // 集中線（ずっと。カット C は控えめ）
        float lines = age < ShotC ? 0.55f : age < ShotD ? 0.3f : 0.65f;
        GUI.color = new Color(1f, 1f, 1f, lines);
        var jitter = Mathf.Floor(age * 24f);
        float ls = 1.45f + 0.05f * Mathf.Sin(jitter * 2.3f);
        Matrix4x4 m0 = GUI.matrix;
        GUIUtility.RotateAroundPivot(jitter * 37f % 360f, new Vector2(w * 0.5f, h * 0.45f) * scale);
        GUI.DrawTexture(new Rect(w * 0.5f - w * ls * 0.5f, h * 0.45f - w * ls * 0.5f, w * ls, w * ls), VfxKit.SpeedLines);
        GUI.matrix = m0;

        // 上下の黒い帯（入ってくる）
        float bar = Ease(Mathf.Clamp01(age / 0.18f)) * h * 0.085f;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0f, 0f, w, bar), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0f, h - bar, w, bar), Texture2D.whiteTexture);

        // 左上の名札
        float plateIn = Ease(Mathf.Clamp01((age - 0.08f) / 0.25f));
        if (plateIn > 0f)
        {
            float px = Mathf.Lerp(-420f, 40f, plateIn);
            GUI.color = new Color(0.08f, 0.05f, 0.15f, 0.85f);
            GUI.DrawTexture(new Rect(px - 20f, bar + 18f, 380f, 64f), Texture2D.whiteTexture);
            GUI.color = Gold;
            GUI.DrawTexture(new Rect(px - 20f, bar + 18f, 10f, 64f), Texture2D.whiteTexture);
            DrawOutlined(new Rect(px + 6f, bar + 18f, 360f, 64f), "だるきんぐ", m_plateStyle, Color.white, new Color(0.1f, 0.05f, 0f), 3f, 1f);
        }

        // 技名（1文字ずつ叩きつける）
        DrawName(age, w, h);

        // 斬撃の閃光：画面を斜めに斬る白い線
        if (age >= SlashAt - 0.02f)
        {
            float u = Mathf.Clamp01((age - (SlashAt - 0.02f)) / 0.12f);
            float fade = 1f - Mathf.Clamp01((age - SlashAt - 0.15f) / 0.35f);
            Matrix4x4 m1 = GUI.matrix;
            GUIUtility.RotateAroundPivot(90f, new Vector2(w * 0.5f, h * 0.5f) * scale);   // 剣の振りに合わせて、真上 → 真下
            float len = w * 1.6f * u;
            float thick = Mathf.Lerp(6f, 70f, u) * fade;
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.9f * fade);
            GUI.DrawTexture(new Rect(w * 0.5f - w * 0.8f, h * 0.5f - thick, len, thick * 2f), VfxKit.Beam);
            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.DrawTexture(new Rect(w * 0.5f - w * 0.8f, h * 0.5f - thick * 0.3f, len, thick * 0.6f), Texture2D.whiteTexture);
            GUI.matrix = m1;
        }

        // カットの切り替わりと、斬った瞬間の白い光
        float flash = Mathf.Max(Spike(age, 0f, 0.12f), Mathf.Max(Spike(age, ShotB, 0.1f), Mathf.Max(Spike(age, ShotC, 0.14f), Spike(age, ShotD, 0.08f))));
        flash = Mathf.Max(flash, 0.75f * Spike(age, SlashAt, 0.22f));
        // 最後は白く飛んで、試合の画面へ戻る
        float outro = Mathf.Clamp01((age - (Total - 0.3f)) / 0.2f) * (1f - Mathf.Clamp01((age - (Total - 0.1f)) / 0.1f));
        flash = Mathf.Max(flash, outro);
        if (flash > 0.001f)
        {
            GUI.color = new Color(1f, 0.97f, 0.88f, flash);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
        }

        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
    }

    static float Spike(float age, float at, float len) => age >= at && age < at + len ? 1f - (age - at) / len : 0f;

    void DrawName(float age, float w, float h)
    {
        if (m_name.Length == 0 || age < LetterStart) return;
        var s = MatchVfxSettings.Current;
        float interval = Mathf.Max(0.03f, s.cutInLetterInterval);
        float scale = GUI.matrix.m00;

        // 1文字ずつの幅
        float total = 0f;
        var widths = new float[m_name.Length];
        for (int i = 0; i < m_name.Length; i++)
        {
            widths[i] = m_letterStyle.CalcSize(new GUIContent(m_name[i].ToString())).x * 0.86f;
            total += widths[i];
        }
        float x = w * 0.5f - total * 0.5f;
        float baseY = h * 0.655f;
        float letterH = m_letterStyle.fontSize * 1.35f;

        Matrix4x4 m0 = GUI.matrix;
        GUIUtility.RotateAroundPivot(-6f, new Vector2(w * 0.5f, baseY + letterH * 0.5f) * scale);
        bool flashWhite = age >= SlashAt && age < SlashAt + 0.18f;
        for (int i = 0; i < m_name.Length; i++)
        {
            float t0 = LetterStart + i * interval;
            float e = age - t0;
            if (e < 0f) break;
            // 叩きつける：大きく出て、行きすぎて戻る
            float u = Mathf.Clamp01(e / 0.12f);
            float pop = u < 1f ? Mathf.Lerp(2.6f, 0.92f, Ease(u)) : Mathf.Lerp(0.92f, 1f, Mathf.Clamp01((e - 0.12f) / 0.06f));
            float alpha = Mathf.Clamp01(e / 0.04f);
            // 文字ごとに少しずつ高さ・角度をずらす（筆で書いたような勢い）
            float jy = Mathf.Sin(i * 2.7f) * 10f;
            float rot = Mathf.Sin(i * 1.9f + 0.5f) * 5f;
            var center = new Vector2(x + widths[i] * 0.5f, baseY + letterH * 0.5f + jy);
            Matrix4x4 m1 = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), center * scale);
            GUIUtility.RotateAroundPivot(rot, center * scale);
            var rect = new Rect(center.x - widths[i], center.y - letterH * 0.5f, widths[i] * 2f, letterH);
            string ch = m_name[i].ToString();
            Color fill = flashWhite ? Color.white : new Color(1f, 0.55f, 0.08f);
            Color top = flashWhite ? Color.white : new Color(1f, 0.93f, 0.35f);
            // 外の縁（こげ茶）→ 内の縁（白）→ 文字（下は橙、上は黄色のグラデーション）
            DrawOutlined(rect, ch, m_letterStyle, fill, new Color(0.16f, 0.04f, 0f, alpha), 16f, alpha, Color.white, 8f, top);
            GUI.matrix = m1;
            x += widths[i];
        }
        GUI.matrix = m0;
    }

    // 縁取りした文字。outer の縁、（あれば）inner の縁、文字（topColor があれば上半分をその色に）
    void DrawOutlined(Rect r, string text, GUIStyle style, Color fill, Color outer, float outerWidth, float alpha,
        Color? inner = null, float innerWidth = 0f, Color? topColor = null)
    {
        var oldColor = style.normal.textColor;
        GUI.color = Color.white;
        style.normal.textColor = new Color(outer.r, outer.g, outer.b, alpha);
        int n = 16;
        for (int k = 0; k < n; k++)
        {
            float a = k * Mathf.PI * 2f / n;
            GUI.Label(new Rect(r.x + Mathf.Cos(a) * outerWidth, r.y + Mathf.Sin(a) * outerWidth + 3f, r.width, r.height), text, style);
        }
        if (inner.HasValue)
        {
            var c = inner.Value;
            style.normal.textColor = new Color(c.r, c.g, c.b, alpha);
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                GUI.Label(new Rect(r.x + Mathf.Cos(a) * innerWidth, r.y + Mathf.Sin(a) * innerWidth, r.width, r.height), text, style);
            }
        }
        style.normal.textColor = new Color(fill.r, fill.g, fill.b, alpha);
        GUI.Label(r, text, style);
        if (topColor.HasValue)
        {
            var c = topColor.Value;
            // 上を明るい色、間をその中間色にして、なめらかに見えるグラデーション
            var midC = Color.Lerp(c, fill, 0.5f);
            style.normal.textColor = new Color(midC.r, midC.g, midC.b, alpha);
            GUI.BeginGroup(new Rect(r.x, r.y, r.width, r.height * 0.58f));
            GUI.Label(new Rect(0f, 0f, r.width, r.height), text, style);
            GUI.EndGroup();
            style.normal.textColor = new Color(c.r, c.g, c.b, alpha);
            GUI.BeginGroup(new Rect(r.x, r.y, r.width, r.height * 0.49f));
            GUI.Label(new Rect(0f, 0f, r.width, r.height), text, style);
            GUI.EndGroup();
        }
        style.normal.textColor = oldColor;
    }

    void EnsureStyles()
    {
        var font = MatchVfxSettings.Current.cutInFont;
        if (m_letterStyle != null && m_font == font) return;
        m_font = font;
        m_letterStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 168, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow, wordWrap = false };
        m_plateStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 40, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow, wordWrap = false };
        if (font != null) { m_letterStyle.font = font; m_plateStyle.font = font; }
    }

    // ---------------- 画像 ----------------
    static Texture2D Rays()
    {
        if (s_rays != null) return s_rays;
        const int n = 512;
        s_rays = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HeroRays" };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f) / (n * 0.5f);
                float r = p.magnitude;
                float a = Mathf.Atan2(p.y, p.x);
                float ray = Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 9f)), 6f) * 0.8f + Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 23f + 1f)), 14f) * 0.5f;
                float center = Mathf.Exp(-r * r * 6f);
                float alpha = Mathf.Clamp01((ray * Mathf.Clamp01(r * 3f) + center) * Mathf.Clamp01(1.05f - r));
                px[y * n + x] = new Color(1f, 1f, 1f, alpha);
            }
        s_rays.SetPixels(px);
        s_rays.Apply();
        return s_rays;
    }

    // 縦のグラデーション（上が明るく、下が暗い）
    static Texture2D Gradient()
    {
        if (s_gradient != null) return s_gradient;
        const int n = 64;
        s_gradient = new Texture2D(2, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HeroGradient" };
        var px = new Color[2 * n];
        for (int y = 0; y < n; y++)
        {
            float v = y / (n - 1f);
            float k = Mathf.Lerp(0.45f, 1.25f, v);
            px[y * 2] = px[y * 2 + 1] = new Color(Mathf.Min(1f, k), Mathf.Min(1f, k), Mathf.Min(1f, k), 1f);
        }
        s_gradient.SetPixels(px);
        s_gradient.Apply();
        return s_gradient;
    }

    // ---------------- 効果音（音のファイルが無ければ、ここで作った仮の音） ----------------
    void PlaySe(AudioClip clip, float volume)
    {
        if (clip == null || m_audio == null) return;
        m_audio.PlayOneShot(clip, volume * GameSettings.SeVolume);
    }

    static AudioClip Clip(AudioClip custom, ref AudioClip cache, System.Func<AudioClip> make)
    {
        if (custom != null) return custom;
        if (cache == null) cache = make();
        return cache;
    }

    const int Rate = 44100;

    static AudioClip Synth(string name, float seconds, System.Func<float, System.Random, float> f)
    {
        int n = Mathf.CeilToInt(seconds * Rate);
        var data = new float[n];
        var rng = new System.Random(name.GetHashCode());
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate, rng), -1f, 1f);
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Noise(System.Random r) => (float)r.NextDouble() * 2f - 1f;

    // 1文字：太鼓のような「ドンッ」＋ 筆を叩きつける「バシッ」
    static AudioClip MakeLetter()
    {
        float lp = 0f;
        return Synth("cutin_letter", 0.32f, (t, r) =>
        {
            float f = 95f + 140f * Mathf.Exp(-t * 35f);
            float body = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 14f) * 0.9f;
            float n = Noise(r);
            lp += 0.35f * (n - lp);
            float slap = (n - lp) * Mathf.Exp(-t * 55f) * 0.55f;
            float click = Mathf.Sin(2f * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 120f) * 0.25f;
            return body + slap + click;
        });
    }

    // 技名がそろった：「ジャーン！」（明るい和音 ＋ シンバルのきらめき）
    static AudioClip MakeName()
    {
        float hp = 0f, prev = 0f;
        return Synth("cutin_name", 1.5f, (t, r) =>
        {
            float env = Mathf.Exp(-t * 2.4f) * Mathf.Clamp01(t / 0.006f);
            float chord = (Mathf.Sin(2f * Mathf.PI * 523.3f * t) + Mathf.Sin(2f * Mathf.PI * 659.3f * t) * 0.8f + Mathf.Sin(2f * Mathf.PI * 784f * t) * 0.7f
                + Mathf.Sin(2f * Mathf.PI * 1046.5f * t) * 0.5f) * 0.22f * env;
            float n = Noise(r);
            hp = 0.9f * (hp + n - prev); prev = n;
            float cymbal = hp * Mathf.Exp(-t * 3.5f) * 0.35f;
            return chord + cymbal;
        });
    }

    // 力をためる：「キュイイイン」と上がっていく音
    static AudioClip MakeCharge()
    {
        float phase = 0f;
        return Synth("cutin_charge", 1.2f, (t, r) =>
        {
            float u = t / 1.2f;
            float f = Mathf.Lerp(260f, 1500f, u * u);
            phase += 2f * Mathf.PI * f / Rate;
            float env = Mathf.Clamp01(t / 0.1f) * (1f - Mathf.Clamp01((t - 1.05f) / 0.15f));
            float trem = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 18f * t);
            return (Mathf.Sin(phase) * 0.35f + Mathf.Sin(phase * 2.01f) * 0.12f + Noise(r) * 0.06f) * env * trem;
        });
    }

    // カットインに入る：「シュバッ」
    static AudioClip MakeWhoosh()
    {
        float lp = 0f;
        return Synth("cutin_whoosh", 0.4f, (t, r) =>
        {
            float u = t / 0.4f;
            float k = Mathf.Lerp(0.05f, 0.6f, u);
            lp += k * (Noise(r) - lp);
            return lp * Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.5f)) * 0.8f;
        });
    }

    // 斬る：「ズバァン！」（鋭い裂ける音 ＋ 重い衝撃 ＋ 金属の響き）
    static AudioClip MakeSlash()
    {
        float lp = 0f;
        return Synth("cutin_slash", 1.3f, (t, r) =>
        {
            float n = Noise(r);
            lp += 0.25f * (n - lp);
            float tear = (n - lp * 0.5f) * Mathf.Exp(-t * 16f) * 0.7f;
            float boom = Mathf.Sin(2f * Mathf.PI * (48f + 60f * Mathf.Exp(-t * 8f)) * t) * Mathf.Exp(-t * 4f) * 0.9f;
            float ring = (Mathf.Sin(2f * Mathf.PI * 2350f * t) + Mathf.Sin(2f * Mathf.PI * 2410f * t)) * Mathf.Exp(-t * 6f) * 0.12f;
            return tear + boom + ring;
        });
    }
}
