using UnityEngine;

// 試合の演出（MatchVfx）で使う部品：やわらかい丸・輪・集中線の画像、パーティクルの作り方
public static class VfxKit
{
    static Texture2D s_softDot, s_ring, s_lines;

    // ふちがぼけた丸（煙・光の粒）
    public static Texture2D SoftDot
    {
        get
        {
            if (s_softDot != null) return s_softDot;
            const int n = 64;
            s_softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VfxSoftDot" };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f).magnitude / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
                }
            s_softDot.SetPixels(px);
            s_softDot.Apply();
            return s_softDot;
        }
    }

    static Texture2D s_curtain, s_pillar;

    // 光の幕：下が明るく、上へ行くほど消える。横は両端だけ少しぼかす（攻撃の線に沿って立てる板）
    public static Texture2D Curtain
    {
        get
        {
            if (s_curtain != null) return s_curtain;
            const int w = 64, h = 128;
            s_curtain = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VfxCurtain" };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float edge = Mathf.Clamp01(Mathf.Min(u, 1f - u) / 0.08f);
                    float fade = Mathf.Pow(1f - v, 1.6f);
                    px[y * w + x] = new Color(1f, 1f, 1f, edge * fade);
                }
            s_curtain.SetPixels(px);
            s_curtain.Apply();
            return s_curtain;
        }
    }

    // 光の柱：横は真ん中が明るく両側へぼけ、縦は下から上へだんだん薄くなる
    public static Texture2D Pillar
    {
        get
        {
            if (s_pillar != null) return s_pillar;
            const int w = 64, h = 128;
            s_pillar = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VfxPillar" };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;
                    float across = Mathf.Exp(-u * u * 6f);
                    float fade = Mathf.Clamp01(v / 0.04f) * Mathf.Pow(1f - v, 0.8f);
                    px[y * w + x] = new Color(1f, 1f, 1f, across * fade);
                }
            s_pillar.SetPixels(px);
            s_pillar.Apply();
            return s_pillar;
        }
    }

    static Texture2D s_beam;

    // 光の線（LineRenderer 用）：線の向き（横）はそのまま、太さの向き（縦）は真ん中が明るく両側へぼける
    public static Texture2D Beam
    {
        get
        {
            if (s_beam != null) return s_beam;
            const int w = 4, h = 64;
            s_beam = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VfxBeam" };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h * 2f - 1f;
                float a = Mathf.Exp(-v * v * 5f);
                for (int x = 0; x < w; x++) px[y * w + x] = new Color(1f, 1f, 1f, a);
            }
            s_beam.SetPixels(px);
            s_beam.Apply();
            return s_beam;
        }
    }

    static Texture2D s_band;

    // 太い帯（LineRenderer 用）：太さの向きは、真ん中から 6 割くらいまで濃く、そこから外へやわらかく消える（影・黒い縁どり用）
    public static Texture2D Band
    {
        get
        {
            if (s_band != null) return s_band;
            const int w = 4, h = 64;
            s_band = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VfxBand" };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, v));
                for (int x = 0; x < w; x++) px[y * w + x] = new Color(1f, 1f, 1f, a);
            }
            s_band.SetPixels(px);
            s_band.Apply();
            return s_band;
        }
    }

    // 細い輪（衝撃波）
    public static Texture2D Ring
    {
        get
        {
            if (s_ring != null) return s_ring;
            const int n = 128;
            s_ring = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VfxRing" };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f).magnitude / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.12f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a);
                }
            s_ring.SetPixels(px);
            s_ring.Apply();
            return s_ring;
        }
    }

    // 集中線（真ん中が空いていて、外へ向かって細い線が放射状に伸びる）
    public static Texture2D SpeedLines
    {
        get
        {
            if (s_lines != null) return s_lines;
            const int n = 512;
            s_lines = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VfxSpeedLines" };
            var rand = new System.Random(7);
            const int count = 90;
            var angle = new float[count];
            var width = new float[count];
            var start = new float[count];
            for (int i = 0; i < count; i++)
            {
                angle[i] = (float)rand.NextDouble() * Mathf.PI * 2f;
                width[i] = 0.004f + (float)rand.NextDouble() * 0.012f;
                start[i] = 0.35f + (float)rand.NextDouble() * 0.35f;
            }
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f) / (n * 0.5f);
                    float r = p.magnitude;
                    float a = 0f;
                    if (r > 0.3f)
                    {
                        float ang = Mathf.Atan2(p.y, p.x);
                        for (int i = 0; i < count; i++)
                        {
                            if (r < start[i]) continue;
                            float da = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, angle[i] * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                            // 外へ行くほど太く
                            float w = width[i] * (0.4f + r);
                            if (da < w) a = Mathf.Max(a, (1f - da / w) * Mathf.Clamp01((r - start[i]) * 5f));
                        }
                    }
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            s_lines.SetPixels(px);
            s_lines.Apply();
            return s_lines;
        }
    }

    // 演出用のマテリアル（頂点の色×画像、半透明。両面）。色は頂点の色か material.color で付ける
    // ※ URP の Particles/Unlit を実行中に加算で作ると、画像の透明な所が効かず四角く見えたので、Sprites/Default を使う
    public static Material Mat(Texture tex)
    {
        var m = new Material(Shader.Find("Sprites/Default"));
        if (tex != null) m.mainTexture = tex;
        return m;
    }

    // ---- 演出の間だけ、カメラのポストエフェクトを入れる（いくつもの演出から使えるよう、使っている数を数える） ----
    static UnityEngine.Rendering.Universal.UniversalAdditionalCameraData s_postCamera;
    static bool s_postWasOn;
    static int s_postUsers;

    public static void AcquirePost()
    {
        var cam = Camera.main;
        var data = cam != null ? cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() : null;
        if (data == null) return;
        if (data != s_postCamera)
        {
            // カメラが替わった（シーンが変わった）：前のカメラの数え方は捨てる
            s_postCamera = data;
            s_postUsers = 0;
        }
        if (s_postUsers == 0) s_postWasOn = data.renderPostProcessing;
        s_postUsers++;
        data.renderPostProcessing = true;
    }

    public static void ReleasePost()
    {
        if (s_postUsers <= 0) return;
        s_postUsers--;
        if (s_postUsers == 0 && s_postCamera != null && !s_postWasOn) s_postCamera.renderPostProcessing = false;
    }

    // 光る（加算）マテリアル。intensity を 1 より大きくするとブルームでにじむ（シェーダー Fujihara/VfxAdditive。設定 MatchVfxSettings から参照）
    public static Material AddMat(Texture tex, float intensity)
    {
        var shader = MatchVfxSettings.Current.additiveShader;
        if (shader == null) shader = Shader.Find("Fujihara/VfxAdditive");
        if (shader == null) return Mat(tex);
        var m = new Material(shader);
        if (tex != null) m.SetTexture("_MainTex", tex);
        m.SetFloat("_Intensity", intensity);
        return m;
    }

    // 粒を Emit だけで出すパーティクル（自分では出さない）。色・大きさ・寿命・速さは Emit のときに渡す
    // sizeGrow：寿命の間に大きさが何倍になるか / unscaled：時間が止まっていても（スロー中も）動く
    // stretch > 0：速さの向きに伸ばして「光の筋」にする（速さ × stretch の長さ）。additive のときの明るさは intensity
    public static ParticleSystem CreateSystem(string name, Transform parent, bool additive, Texture tex, int max, float sizeGrow, float gravity, bool unscaled,
        float stretch = 0f, float intensity = 2f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        main.useUnscaledTime = unscaled;
        main.startLifetime = 1f;
        main.startSpeed = 0f;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, sizeGrow));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = additive ? AddMat(tex, intensity) : Mat(tex);
        if (stretch > 0f)
        {
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = stretch;
            renderer.lengthScale = 1f;
        }
        else renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        ps.Play();
        return ps;
    }

    public static void Emit(ParticleSystem ps, Vector3 pos, Vector3 velocity, float size, float life, Color color)
    {
        if (ps == null) return;
        var e = new ParticleSystem.EmitParams
        {
            position = pos,
            velocity = velocity,
            startSize = size,
            startLifetime = life,
            startColor = color,
            rotation = Random.Range(0f, 360f),
        };
        ps.Emit(e, 1);
    }
}
