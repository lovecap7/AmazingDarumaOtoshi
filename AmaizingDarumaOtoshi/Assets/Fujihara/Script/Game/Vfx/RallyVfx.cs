using Nakahira;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// 2. ラリー（打ち返し）の演出。参考：Lethal League の「打ち返すたびに止まる時間が長くなり、画面が大きく変わる」
//    打ち返した瞬間（ハンマーのヒットストップが始まる）に：
//     ・パッと白く光る
//     ・止まっている間、コントラスト・色ずれが上がり、当たった所を中心に集中線、画面のふちが暗くなる（回数が増えるほど強く）
//     ・打ち返した回数が InvertAt 回以上なら、止まっている間、弾を中心に円が広がり、円の中がネガポジ反転する（ふちは白く光る）
//       円は弾を追いかけ、止まり終わったらしぼんで消える
//    止まる時間は試合と同じ計算（HammerParams のヒットストップ）で求める
//    画面の色は、試合のカメラのポストエフェクトを演出の間だけ入れて、この演出用の Volume で変える（ふだんの見た目は変えない）。
//    反転の円は、カメラの前の板（シェーダー Fujihara/RallyInvertCircle）がカメラの写った画面を読んで描く（演出の間だけ requiresColorTexture を入れる）
public class RallyVfx : MonoBehaviour
{
    Volume m_volume;
    VolumeProfile m_profile;
    ColorAdjustments m_color;
    Vignette m_vignette;
    ChromaticAberration m_chroma;
    RectTransform m_canvas;
    RawImage m_lines;
    Image m_flash;

    // 反転の円
    bool m_invert;
    float m_invertStart;
    float m_radius;
    Transform m_ball;
    GameObject m_circle;
    Material m_circleMat;
    static readonly int CenterId = Shader.PropertyToID("_Center");
    static readonly int RadiusId = Shader.PropertyToID("_Radius");

    float m_holdUntil;      // この時刻（実時間）まで止まっている（強さを保つ）
    float m_strength;       // 今の強さ（0〜1）
    float m_target;
    float m_flashLevel;
    Vector2 m_center;       // 集中線・円の中心（画面の割合）
    Camera m_camera;
    UniversalAdditionalCameraData m_cameraData;
    bool m_postOwned;
    bool m_colorTexOwned;

    void OnEnable() => DarumaHammer.Hit += OnHammerHit;
    void OnDisable()
    {
        DarumaHammer.Hit -= OnHammerHit;
        ReleasePost();
        ReleaseColorTexture();
    }

    void OnHammerHit(HammerHitInfo info)
    {
        var s = MatchVfxSettings.Current;
        if (!s.rally || info.ReturnCount <= 0 || !(info.Target is TumikiBlock block)) return;
        var p = info.Hammer != null ? info.Hammer.Params : null;
        // 止まる時間：DarumaHammer.BeginHitStop と同じ計算
        float baseDuration = p != null ? p.HitStopDuration : 0.1f;
        float duration = p != null
            ? Mathf.Min(baseDuration * Mathf.Pow(p.ReturnHitStopGrowth, info.ReturnCount), Mathf.Max(baseDuration, p.ReturnHitStopMax))
            : baseDuration;
        m_holdUntil = Time.unscaledTime + duration;
        m_target = Mathf.Clamp01(info.ReturnCount / (float)Mathf.Max(1, s.invertAt));
        m_strength = Mathf.Max(m_strength, m_target);
        m_flashLevel = 1f;
        m_ball = block.transform;
        m_returns = info.ReturnCount;
        m_nextPulse = Time.unscaledTime + s.shockwavePulse;
        if (s.invert && info.ReturnCount >= s.invertAt)
        {
            // 新しく反転が始まるときだけ、円を小さい所から広げ直す（反転したまま次の打ち返しが来たら、広がった円のまま続ける）
            if (!m_invert || m_radius <= 0.001f) m_invertStart = Time.unscaledTime;
            m_invert = true;
        }

        Ensure();
        UpdateCenter(info.Point);
        Shake(s, info.ReturnCount, duration);
        // 打った瞬間：回数に応じて 2〜4 重の輪を、少しずつずらして出す
        if (s.shockwave)
        {
            int rings = 2 + Mathf.Min(2, info.ReturnCount / 2);
            for (int i = 0; i < rings; i++) SpawnRing(s, i * 0.06f, 1f - i * 0.18f);
        }
    }

    // ---- 衝撃波（画面の上で広がる輪） ----
    class Ring { public RawImage image; public float start; public float scale; public Vector2 center; public bool used; }
    readonly System.Collections.Generic.List<Ring> m_rings = new System.Collections.Generic.List<Ring>();
    int m_returns;
    float m_nextPulse;

    // delay 秒後に、今の中心から輪を出す。scale：大きさの倍率
    void SpawnRing(MatchVfxSettings s, float delay, float scale)
    {
        Ring r = m_rings.Find(x => !x.used);
        if (r == null)
        {
            var img = new GameObject("Shockwave", typeof(RectTransform)).AddComponent<RawImage>();
            img.texture = VfxKit.Ring;
            img.raycastTarget = false;
            img.rectTransform.SetParent(m_canvas, false);
            img.rectTransform.SetAsFirstSibling();   // 白い光より下
            r = new Ring { image = img };
            m_rings.Add(r);
        }
        r.used = true;
        r.start = Time.unscaledTime + delay;
        r.scale = scale;
        r.center = m_center;
        r.image.enabled = false;
    }

    void UpdateRings(MatchVfxSettings s, bool holding)
    {
        // 止まっている間は、弾から輪を出し続ける
        if (s.shockwave && holding && s.shockwavePulse > 0f && Time.unscaledTime >= m_nextPulse)
        {
            m_nextPulse = Time.unscaledTime + s.shockwavePulse;
            SpawnRing(s, 0f, 0.8f);
        }
        float canvasScale = Mathf.Max(0.01f, m_canvas.localScale.x);
        float full = (s.shockwaveSize + s.shockwaveSizePerReturn * Mathf.Max(0, m_returns - 1)) * Screen.height / canvasScale;
        foreach (var r in m_rings)
        {
            if (!r.used) continue;
            float u = (Time.unscaledTime - r.start) / Mathf.Max(0.01f, s.shockwaveTime);
            if (u < 0f) { r.image.enabled = false; continue; }
            if (u >= 1f) { r.used = false; r.image.enabled = false; continue; }
            float grow = 1f - (1f - u) * (1f - u) * (1f - u);   // 速く広がって、ゆっくり止まる
            float size = full * r.scale * Mathf.Lerp(0.05f, 1f, grow) * 2f;
            var rt = r.image.rectTransform;
            rt.anchorMin = rt.anchorMax = r.center;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
            var c = s.shockwaveColor;
            r.image.color = new Color(c.r, c.g, c.b, c.a * (1f - u) * (1f - u));
            r.image.enabled = true;
        }
    }

    // ---- カメラの揺れ ----
    // 試合のカメラは中川さんの CameraShake で揺らす。無いカメラ（RallyScene など）は、ここでカメラの位置をずらして揺らす
    float m_shakeAmp, m_shakeTime, m_shakeLength;
    Vector3 m_shakeOffset;
    Camera m_shakeCam;

    void Shake(MatchVfxSettings s, int returns, float hold)
    {
        if (s.shake <= 0f) return;
        float amp = Mathf.Min(s.shake + s.shakePerReturn * (returns - 1), s.shakeMax);
        float length = hold + 0.2f;
        var cam = Camera.main;
        var shaker = cam != null ? cam.GetComponent<Nakagawa.CameraShake>() : null;
        if (shaker != null) { shaker.Shake(new Nakagawa.CameraShake.ShakeSettings(amp, length)); return; }
        if (amp >= m_shakeAmp * Mathf.Clamp01(1f - m_shakeTime / Mathf.Max(0.01f, m_shakeLength)))
        {
            m_shakeAmp = amp;
            m_shakeLength = length;
            m_shakeTime = 0f;
        }
    }

    // CameraShake の無いカメラの揺れ：前のフレームのずれを戻してから、新しいずれを足す
    void UpdateOwnShake(float dt)
    {
        var cam = Camera.main;
        if (m_shakeCam != null && m_shakeCam != cam) { m_shakeCam.transform.position -= m_shakeOffset; m_shakeOffset = Vector3.zero; }
        m_shakeCam = cam;
        if (cam == null) return;
        cam.transform.position -= m_shakeOffset;
        m_shakeOffset = Vector3.zero;
        if (m_shakeAmp <= 0f) return;
        m_shakeTime += dt;
        float k = 1f - m_shakeTime / Mathf.Max(0.01f, m_shakeLength);
        if (k <= 0f) { m_shakeAmp = 0f; return; }
        Vector2 r = Random.insideUnitCircle * m_shakeAmp * k;
        m_shakeOffset = cam.transform.right * r.x + cam.transform.up * r.y;
        cam.transform.position += m_shakeOffset;
    }

    void UpdateCenter(Vector3 world)
    {
        var cam = Camera.main;
        if (cam == null) return;
        var vp = cam.WorldToViewportPoint(world);
        if (vp.z > 0f) m_center = new Vector2(vp.x, vp.y);
    }

    void Ensure()
    {
        if (m_volume == null)
        {
            var go = new GameObject("RallyVolume");
            go.transform.SetParent(transform, false);
            m_volume = go.AddComponent<Volume>();
            m_volume.isGlobal = true;
            m_volume.priority = 100f;
            m_profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_color = m_profile.Add<ColorAdjustments>(true);
            m_vignette = m_profile.Add<Vignette>(true);
            m_chroma = m_profile.Add<ChromaticAberration>(true);
            m_vignette.color.Override(Color.black);
            m_vignette.smoothness.Override(0.5f);
            m_volume.sharedProfile = m_profile;
            m_volume.weight = 0f;
        }
        if (m_canvas == null)
        {
            m_canvas = ToyKit.CreateOverlayCanvas("RallyFx", 15);
            m_canvas.SetParent(transform, false);
            var lines = new GameObject("SpeedLines", typeof(RectTransform)).AddComponent<RawImage>();
            lines.texture = VfxKit.SpeedLines;
            lines.raycastTarget = false;
            lines.rectTransform.SetParent(m_canvas, false);
            m_lines = lines;
            var flash = ToyKit.UIImage("Flash", m_canvas, new Color(1f, 1f, 1f, 0f)).GetComponent<Image>();
            flash.raycastTarget = false;
            ToyKit.Stretch(flash.rectTransform);
            m_flash = flash;
        }
    }

    void LateUpdate()
    {
        var s = MatchVfxSettings.Current;
        float dt = Time.unscaledDeltaTime;
        bool holding = Time.unscaledTime < m_holdUntil;
        if (!holding) m_strength = Mathf.MoveTowards(m_strength, 0f, dt / Mathf.Max(0.01f, s.recover));
        m_flashLevel = Mathf.MoveTowards(m_flashLevel, 0f, dt / 0.12f);
        bool active = m_strength > 0.001f || m_flashLevel > 0.001f;
        UpdateOwnShake(dt);

        if (m_volume == null) return;
        if (active) TakePost(); else ReleasePost();

        // 弾を追いかける（集中線・円の中心）
        if (m_ball != null) UpdateCenter(m_ball.position);

        // 画面の色：止まっている間は強さを保ち、白い光は打った瞬間だけ
        float k = m_strength;
        m_volume.weight = active ? 1f : 0f;
        m_color.saturation.Override(0f);
        m_color.contrast.Override(s.contrast * k);
        m_color.postExposure.Override(s.flash * m_flashLevel * 0.6f);
        m_vignette.intensity.Override(0.15f + 0.3f * k);
        m_chroma.intensity.Override(0.6f * m_flashLevel + 0.25f * k);

        UpdateInvertCircle(s, holding, dt);
        UpdateRings(s, holding);

        // 集中線：当たった所を中心に、回数が増えるほど濃く。止まっている間は少し脈打つ
        if (m_lines != null) m_lines.enabled = s.speedLines > 0f;
        if (m_lines != null && m_lines.enabled)
        {
            float a = s.speedLines * Mathf.Clamp01(k * 1.4f) * (holding ? 1f : Mathf.Clamp01(k));
            m_lines.color = new Color(1f, 1f, 1f, a);
            float size = Mathf.Max(Screen.width, Screen.height) * 2.2f * (1f + 0.04f * Mathf.Sin(Time.unscaledTime * 40f));
            var rt = m_lines.rectTransform;
            rt.anchorMin = rt.anchorMax = m_center;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size) / Mathf.Max(0.01f, m_canvas.localScale.x);
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Floor(Time.unscaledTime * 30f) * 7f);   // 線がちらちら動く
        }
        if (m_flash != null) m_flash.color = new Color(1f, 1f, 1f, 0.35f * m_flashLevel * m_flashLevel);
    }

    // 反転の円：止まっている間は弾を中心に広がり、止まり終わったらしぼむ
    void UpdateInvertCircle(MatchVfxSettings s, bool holding, float dt)
    {
        if (!holding) m_invert = false;
        // 画面のどの角まででも届く半径（画面の高さを 1 としたとき）
        float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 1.78f;
        float far = new Vector2(Mathf.Max(m_center.x, 1f - m_center.x) * aspect, Mathf.Max(m_center.y, 1f - m_center.y)).magnitude + 0.05f;
        if (m_invert)
        {
            float u = Mathf.Clamp01((Time.unscaledTime - m_invertStart) / Mathf.Max(0.01f, s.invertExpand));
            m_radius = far * (1f - (1f - u) * (1f - u) * (1f - u));   // 速く広がって、ゆっくり端まで
        }
        else m_radius = Mathf.MoveTowards(m_radius, 0f, dt * far / Mathf.Max(0.01f, s.invertShrink));

        bool show = m_radius > 0.001f && s.invertShader != null;
        if (!show)
        {
            if (m_circle != null && m_circle.activeSelf) m_circle.SetActive(false);
            ReleaseColorTexture();
            return;
        }
        var cam = Camera.main;
        if (cam == null) return;
        if (m_circle == null) MakeCircle(s);
        if (m_circle.transform.parent != cam.transform) m_circle.transform.SetParent(cam.transform, false);
        m_circle.SetActive(true);
        TakeColorTexture(cam);

        // カメラのすぐ前に、画面いっぱいの板を置く
        float d = cam.nearClipPlane + 0.05f;
        float h = cam.orthographic ? cam.orthographicSize * 2f : 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        m_circle.transform.localPosition = new Vector3(0f, 0f, d);
        m_circle.transform.localRotation = Quaternion.identity;
        m_circle.transform.localScale = new Vector3(h * cam.aspect * 1.05f, h * 1.05f, 1f);
        m_circleMat.SetVector(CenterId, new Vector4(m_center.x, m_center.y, 0f, 0f));
        m_circleMat.SetFloat(RadiusId, m_radius);
    }

    void MakeCircle(MatchVfxSettings s)
    {
        m_circle = GameObject.CreatePrimitive(PrimitiveType.Quad);
        m_circle.name = "RallyInvertCircle";
        Destroy(m_circle.GetComponent<Collider>());
        m_circleMat = new Material(s.invertShader);
        var r = m_circle.GetComponent<MeshRenderer>();
        r.sharedMaterial = m_circleMat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    // 演出の間だけ、試合のカメラのポストエフェクトを入れる（ほかの演出と一緒に数える：VfxKit.AcquirePost）
    void TakePost()
    {
        var cam = Camera.main;
        if (cam != m_camera)
        {
            ReleasePost();
            ReleaseColorTexture();
            m_camera = cam;
            m_cameraData = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
        }
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

    // 反転の円の間だけ、カメラの写った画面（Opaque Texture）を作らせる
    void TakeColorTexture(Camera cam)
    {
        if (cam != m_camera) { m_camera = cam; m_cameraData = cam.GetComponent<UniversalAdditionalCameraData>(); }
        if (m_cameraData == null || m_colorTexOwned) return;
        m_colorOption = m_cameraData.requiresColorOption;
        m_cameraData.requiresColorOption = CameraOverrideOption.On;
        m_colorTexOwned = true;
    }

    void ReleaseColorTexture()
    {
        if (m_colorTexOwned && m_cameraData != null) m_cameraData.requiresColorOption = m_colorOption;
        m_colorTexOwned = false;
    }

    CameraOverrideOption m_colorOption;
}
