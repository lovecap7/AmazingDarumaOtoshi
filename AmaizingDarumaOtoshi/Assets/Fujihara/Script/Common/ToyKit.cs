using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// タイトル・メニュー・セレクト画面で共通の「おもちゃの世界」の部品づくり。
// 背景は彩度を落とした淡い色、ゲームのオブジェクト（積み木・顔・ハンマー）は鮮やかな色。
// 素材は使わず、生成したメッシュ・ライト・パーティクル・ポストエフェクトで実行時に組み立てる。
public static class ToyKit
{
    // ---- 寸法（大きさ1のとき） ----
    public const float BlockW = 1.2f, BlockH = 0.6f, BlockD = 1.2f;
    public const float HeadR = 0.45f;
    public const float HammerLength = 2.2f;     // 根元から頭の中心まで
    public const float HammerHeadHalf = 0.55f;  // 頭の長さの半分
    public const float HammerHeadR = 0.3f;

    public static readonly Color[] Palette =
    {
        new Color(0.91f, 0.29f, 0.25f),  // 赤
        new Color(0.96f, 0.60f, 0.18f),  // 橙
        new Color(0.97f, 0.82f, 0.24f),  // 黄
        new Color(0.42f, 0.75f, 0.27f),  // 緑
        new Color(0.25f, 0.65f, 0.88f),  // 青
        new Color(0.60f, 0.40f, 0.75f),  // 紫
    };
    public static readonly Color Ink = new Color(0.27f, 0.15f, 0.08f);
    public static readonly Color Horizon = new Color(0.97f, 0.92f, 0.86f);
    public static readonly Color SkyTop = new Color(0.78f, 0.86f, 0.95f);

    // ---------------- 舞台 ----------------
    public class Post
    {
        public Bloom bloom;
        public ChromaticAberration chroma;
        public DepthOfField dof;
        public LensDistortion lens;
    }

    public static Camera CreateCamera()
    {
        var go = new GameObject("Camera");
        go.tag = "MainCamera";
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Horizon;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 120f;
        go.AddComponent<AudioListener>();
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        return cam;
    }

    public static void CreateLights()
    {
        // キーライト：暖かい色、柔らかい影（接地影）
        var key = new GameObject("KeyLight").AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(1f, 0.95f, 0.86f);
        key.intensity = 1.5f;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.55f;
        key.transform.rotation = Quaternion.Euler(52f, -32f, 0f);

        // リムライト：後ろから青白く輪郭を立てる
        var rim = new GameObject("RimLight").AddComponent<Light>();
        rim.type = LightType.Directional;
        rim.color = new Color(0.8f, 0.9f, 1f);
        rim.intensity = 1.1f;
        rim.shadows = LightShadows.None;
        rim.transform.rotation = Quaternion.Euler(28f, 165f, 0f);
    }

    public static Post CreatePostProcess(float bloomBase)
    {
        var post = new Post();
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var tone = profile.Add<Tonemapping>(true); tone.mode.value = TonemappingMode.Neutral;   // 色を濁らせない
        post.bloom = profile.Add<Bloom>(true); post.bloom.threshold.value = 0.95f; post.bloom.intensity.value = bloomBase; post.bloom.scatter.value = 0.65f;
        var vig = profile.Add<Vignette>(true); vig.intensity.value = 0.2f; vig.smoothness.value = 0.5f; vig.color.value = new Color(0.35f, 0.25f, 0.3f);
        var ca = profile.Add<ColorAdjustments>(true); ca.saturation.value = 12f; ca.contrast.value = 12f;
        post.dof = profile.Add<DepthOfField>(true); post.dof.mode.value = DepthOfFieldMode.Bokeh; post.dof.focusDistance.value = 5f; post.dof.aperture.value = 2.8f; post.dof.focalLength.value = 50f;
        post.chroma = profile.Add<ChromaticAberration>(true); post.chroma.intensity.value = 0f;
        post.lens = profile.Add<LensDistortion>(true); post.lens.intensity.value = 0f;

        var volume = new GameObject("PostProcess").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
        return post;
    }

    // 淡い空・床・遠くのおもちゃ・光の筋
    public static void CreateEnvironment(Mesh blockMesh)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 16f;
        RenderSettings.fogEndDistance = 50f;
        RenderSettings.fogColor = Horizon;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.72f, 0.78f, 0.9f);
        RenderSettings.ambientEquatorColor = new Color(0.8f, 0.76f, 0.74f);
        RenderSettings.ambientGroundColor = new Color(0.62f, 0.52f, 0.5f);
        RenderSettings.skybox = null;

        var floor = Primitive(PrimitiveType.Plane, "Floor", null);
        floor.localScale = new Vector3(12f, 1f, 12f);
        floor.position = new Vector3(0f, 0f, 15f);
        floor.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.95f, 0.89f, 0.84f), 0.25f);

        var sky = Primitive(PrimitiveType.Quad, "Sky", null);
        sky.localScale = new Vector3(140f, 60f, 1f);
        sky.position = new Vector3(0f, 22f, 55f);
        var skyMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        skyMat.SetTexture("_BaseMap", MakeGradient(SkyTop, Horizon));
        skyMat.SetColor("_BaseColor", Color.white);
        sky.GetComponent<Renderer>().sharedMaterial = skyMat;
        sky.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        // 遠くに置く大きなおもちゃ（彩度を落とした色）。被写界深度でボケて奥行きが出る
        var rng = new System.Random(3);
        for (int i = 0; i < 12; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float x = side * (10f + (float)rng.NextDouble() * 16f);
            float z = 18f + (float)rng.NextDouble() * 16f;
            int kind = rng.Next(3);
            float s = 2f + (float)rng.NextDouble() * 2.5f;
            if (kind == 0)
            {
                int n = 2 + rng.Next(3);
                for (int k = 0; k < n; k++)
                {
                    var b = new GameObject("BgBlock").transform;
                    b.gameObject.AddComponent<MeshFilter>().sharedMesh = blockMesh;
                    b.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Lit(Pastel(Palette[(i + k) % Palette.Length]), 0.3f);
                    b.localScale = Vector3.one * s;
                    b.position = new Vector3(x, (k + 0.5f) * BlockH * s, z);
                    b.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 40f - 20f, 0f);
                }
            }
            else
            {
                var p = Primitive(kind == 1 ? PrimitiveType.Sphere : PrimitiveType.Cylinder, "BgToy", null);
                p.localScale = kind == 1 ? Vector3.one * s : new Vector3(s, s * 0.5f, s);
                p.position = new Vector3(x, s * 0.5f, z);
                p.GetComponent<Renderer>().sharedMaterial = Lit(Pastel(Palette[i % Palette.Length]), 0.3f);
            }
        }

        // 軽いボリューメトリックライト風の光の筋
        var tex = MakeShaftTexture(32, 128);
        for (int i = 0; i < 5; i++)
        {
            var q = Primitive(PrimitiveType.Quad, "LightShaft", null);
            q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var m = ParticleMaterial(true, tex);
            m.SetColor("_BaseColor", new Color(1f, 0.95f, 0.85f, 0.07f + 0.03f * (i % 2)));
            q.GetComponent<Renderer>().sharedMaterial = m;
            q.localScale = new Vector3(1.4f + i * 0.4f, 16f, 1f);
            q.position = new Vector3(-5f + i * 2.6f, 6f, 6f + i * 0.8f);
            q.rotation = Quaternion.Euler(0f, 0f, -28f);
        }
    }

    public static Color Pastel(Color c) => Color.Lerp(Color.Lerp(c, Color.white, 0.68f), new Color(0.85f, 0.82f, 0.8f), 0.3f);

    // ---------------- キャラクター・道具 ----------------
    public class Face
    {
        public Transform root, eyesNormal, eyesHappy, cheeks;
        public Material body;

        public void SetHappy(bool happy)
        {
            eyesNormal.gameObject.SetActive(!happy);
            eyesHappy.gameObject.SetActive(happy);
            cheeks.gameObject.SetActive(happy);
        }
    }

    // 黄色いスマイルの顔（-Z が正面）
    public static Face CreateHead()
    {
        var f = new Face();
        f.root = new GameObject("Head").transform;
        var body = Primitive(PrimitiveType.Sphere, "Body", f.root);
        body.localScale = Vector3.one * HeadR * 2f;
        f.body = Lit(new Color(0.99f, 0.85f, 0.18f), 0.78f);
        body.GetComponent<Renderer>().sharedMaterial = f.body;

        var face = new GameObject("Face").transform;
        face.SetParent(f.root, false);
        var black = Lit(new Color(0.12f, 0.08f, 0.06f), 0.85f);
        var white = Lit(Color.white, 0.9f);

        f.eyesNormal = new GameObject("EyesNormal").transform;
        f.eyesNormal.SetParent(face, false);
        foreach (float x in new[] { -0.14f, 0.14f })
        {
            var e = Primitive(PrimitiveType.Sphere, "Eye", f.eyesNormal);
            e.localScale = new Vector3(0.08f, 0.13f, 0.06f);
            e.localPosition = OnSphere(x, 0.08f, 0.0f);
            e.GetComponent<Renderer>().sharedMaterial = black;
            var hl = Primitive(PrimitiveType.Sphere, "Highlight", f.eyesNormal);
            hl.localScale = Vector3.one * 0.028f;
            hl.localPosition = OnSphere(x + 0.015f, 0.115f, 0.012f);
            hl.GetComponent<Renderer>().sharedMaterial = white;
        }

        // ニコッとしたときの目（∩の形）
        f.eyesHappy = new GameObject("EyesHappy").transform;
        f.eyesHappy.SetParent(face, false);
        foreach (float x in new[] { -0.14f, 0.14f })
            for (int i = 0; i < 7; i++)
            {
                float u = i / 6f * 2f - 1f;
                var p = Primitive(PrimitiveType.Sphere, "HappyEye", f.eyesHappy);
                p.localScale = Vector3.one * 0.038f;
                p.localPosition = OnSphere(x + u * 0.06f, 0.07f + 0.045f * (1f - u * u), 0f);
                p.GetComponent<Renderer>().sharedMaterial = black;
            }

        for (int i = 0; i < 11; i++)
        {
            float u = i / 10f * 2f - 1f;
            var m = Primitive(PrimitiveType.Sphere, "Mouth", face);
            m.localScale = Vector3.one * 0.042f;
            m.localPosition = OnSphere(u * 0.15f, -0.1f - 0.07f * (1f - u * u), 0f);
            m.GetComponent<Renderer>().sharedMaterial = black;
        }

        f.cheeks = new GameObject("Cheeks").transform;
        f.cheeks.SetParent(face, false);
        var pink = Lit(new Color(1f, 0.55f, 0.6f), 0.5f);
        foreach (float x in new[] { -0.26f, 0.26f })
        {
            var c = Primitive(PrimitiveType.Sphere, "Cheek", f.cheeks);
            c.localScale = new Vector3(0.12f, 0.07f, 0.03f);
            c.localPosition = OnSphere(x, -0.04f, -0.005f);
            c.LookAt(c.position + c.localPosition.normalized);
            c.GetComponent<Renderer>().sharedMaterial = pink;
        }
        f.SetHappy(false);
        return f;
    }

    // 顔の表面上の点（顔は -Z を向く）
    static Vector3 OnSphere(float x, float y, float push)
    {
        float z = -Mathf.Sqrt(Mathf.Max(0.001f, HeadR * HeadR - x * x - y * y));
        return new Vector3(x, y, z) * (1f + push / HeadR);
    }

    // 根元（持ち手の端）が原点。柄は +Z 方向へ伸び、頭は X 方向を向く
    public static Transform CreateHammer()
    {
        var root = new GameObject("Hammer").transform;
        var handle = Primitive(PrimitiveType.Cylinder, "Handle", root);
        handle.localScale = new Vector3(0.14f, HammerLength * 0.5f, 0.14f);
        handle.localRotation = Quaternion.Euler(90f, 0f, 0f);
        handle.localPosition = new Vector3(0f, 0f, HammerLength * 0.5f);
        handle.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.96f, 0.86f, 0.66f), 0.45f);

        var hd = Primitive(PrimitiveType.Cylinder, "Head", root);
        hd.localScale = new Vector3(HammerHeadR * 2f, HammerHeadHalf, HammerHeadR * 2f);
        hd.localRotation = Quaternion.Euler(0f, 0f, 90f);
        hd.localPosition = new Vector3(0f, 0f, HammerLength);
        hd.GetComponent<Renderer>().sharedMaterial = Lit(Palette[0], 0.6f);

        var band = Lit(Palette[2], 0.6f);
        foreach (float x in new[] { -0.42f, 0.42f })
        {
            var r = Primitive(PrimitiveType.Cylinder, "Band", root);
            r.localScale = new Vector3(HammerHeadR * 2f + 0.04f, 0.05f, HammerHeadR * 2f + 0.04f);
            r.localRotation = Quaternion.Euler(0f, 0f, 90f);
            r.localPosition = new Vector3(x, 0f, HammerLength);
            r.GetComponent<Renderer>().sharedMaterial = band;
        }
        return root;
    }

    // 角度0のとき、ハンマーの頭の右面がちょうど contact に当たる根元の位置
    public static Vector3 HammerPivot(Vector3 contact, float hammerScale) =>
        contact + new Vector3(-HammerHeadHalf * hammerScale, 0f, -HammerLength * hammerScale);

    // ---------------- 文字 ----------------
    // 白い文字にこげ茶の縁取り（積み木に書く文字）
    public static Material LetterMaterial(TMP_FontAsset font)
    {
        var m = new Material(font.material);
        m.SetColor("_FaceColor", Color.white);
        m.SetColor("_OutlineColor", Ink);
        m.SetFloat("_OutlineWidth", 0.28f);
        m.EnableKeyword("OUTLINE_ON");
        return m;
    }

    // 擬音用（太め）
    public static Material PopMaterial(Material letter)
    {
        var m = new Material(letter);
        m.SetFloat("_OutlineWidth", 0.4f);
        m.SetFloat("_FaceDilate", 0.3f);
        return m;
    }

    public static TextMeshPro Text3D(TMP_FontAsset font, Material mat, string text, Transform parent, Vector2 box, float maxSize)
    {
        var go = new GameObject("Text");
        if (parent != null) go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshPro>();
        t.font = font;
        t.fontSharedMaterial = mat;
        t.text = text;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.1f;
        t.fontSizeMax = maxSize;
        t.rectTransform.sizeDelta = box;
        return t;
    }

    // 文字を書いた積み木。本体のマテリアルは個別（光らせたり暗くしたりするため）
    public static Transform CreateBlock(string label, Color color, Mesh mesh, Vector2 textBox, float depth,
                                        TMP_FontAsset font, Material letterMat, out Material bodyMat)
    {
        var root = new GameObject("Block_" + label).transform;
        var body = new GameObject("Body");
        body.transform.SetParent(root, false);
        body.AddComponent<MeshFilter>().sharedMesh = mesh;
        bodyMat = Lit(color, 0.55f);
        bodyMat.EnableKeyword("_EMISSION");
        bodyMat.SetColor("_EmissionColor", Color.black);
        body.AddComponent<MeshRenderer>().sharedMaterial = bodyMat;

        var text = Text3D(font, letterMat, label, root, textBox, 14f);
        text.transform.localPosition = new Vector3(0f, 0f, -depth * 0.5f - 0.006f);
        return root;
    }

    // ---------------- エフェクト ----------------
    public class Fx
    {
        public ParticleSystem confetti, puff, sparkle;
        public Transform shockwave, groundRing;
        public Material shockMat, ringMat;
    }

    public static Fx CreateFx()
    {
        var fx = new Fx();
        var softDot = MakeRadialTexture(64, false);
        var ringTex = MakeRadialTexture(128, true);

        fx.confetti = CreateParticles("Confetti", false, null, 0.06f, 0.14f, 4f, 10f, 1.4f, 2.4f, 1.1f);
        var main = fx.confetti.main;
        var g = new Gradient { mode = GradientMode.Fixed };
        var keys = new GradientColorKey[Palette.Length];
        for (int i = 0; i < Palette.Length; i++) keys[i] = new GradientColorKey(Palette[i], (i + 1f) / Palette.Length);
        g.colorKeys = keys;
        main.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
        var rot = fx.confetti.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

        fx.puff = CreateParticles("Puff", false, softDot, 0.3f, 0.7f, 0.6f, 2.2f, 0.6f, 1.1f, -0.03f);
        var pm = fx.puff.main; pm.startColor = new Color(1f, 0.98f, 0.95f, 0.75f);
        var sol = fx.puff.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.5f));
        var col = fx.puff.colorOverLifetime; col.enabled = true; col.color = FadeOut();

        fx.sparkle = CreateParticles("Sparkle", true, softDot, 0.08f, 0.2f, 1.5f, 4f, 0.3f, 0.7f, 0f);
        var sm = fx.sparkle.main; sm.startColor = new Color(1.6f, 1.4f, 1.0f, 1f);
        var scol = fx.sparkle.colorOverLifetime; scol.enabled = true; scol.color = FadeOut();

        CreateMotes(softDot);

        fx.shockwave = Billboard("Shockwave", ringTex, out fx.shockMat);
        fx.groundRing = Billboard("GroundRing", ringTex, out fx.ringMat);
        fx.groundRing.rotation = Quaternion.Euler(90f, 0f, 0f);
        return fx;
    }

    static ParticleSystem CreateParticles(string name, bool additive, Texture tex, float sizeMin, float sizeMax,
                                          float speedMin, float speedMax, float lifeMin, float lifeMax, float gravity)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // 再生しっぱなしにして、必要なときに Emit で出す（止まっていると粒子が動かない）
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 500;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.15f;

        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMaterial(additive, tex);
        ps.Play();
        return ps;
    }

    // 空気中をただようほこり
    static void CreateMotes(Texture softDot)
    {
        var go = new GameObject("DustMotes");
        go.transform.position = new Vector3(0f, 3f, 1f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = 9f;
        main.startSpeed = 0.05f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startColor = new Color(1.3f, 1.25f, 1.1f, 0.6f);
        main.maxParticles = 400;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission; emission.rateOverTime = 35f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(12f, 7f, 8f);
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.1f; noise.frequency = 0.3f;
        var col = ps.colorOverLifetime; col.enabled = true;
        col.color = new Gradient
        {
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) },
            colorKeys = new[] { new GradientColorKey(Color.white, 0f) }
        };
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMaterial(true, softDot);
        ps.Play();
    }

    static Transform Billboard(string name, Texture tex, out Material mat)
    {
        var q = Primitive(PrimitiveType.Quad, name, null);
        q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        mat = ParticleMaterial(true, tex);
        mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
        q.GetComponent<Renderer>().sharedMaterial = mat;
        q.localScale = Vector3.zero;
        return q;
    }

    static Gradient FadeOut() => new Gradient
    {
        alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
        colorKeys = new[] { new GradientColorKey(Color.white, 0f) }
    };

    public static void Emit(ParticleSystem ps, Vector3 pos, Quaternion rot, int count)
    {
        ps.transform.SetPositionAndRotation(pos, rot);
        ps.Emit(count);
    }

    // ---------------- UI ----------------
    public static RectTransform CreateOverlayCanvas(string name, int order)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return (RectTransform)go.transform;
    }

    public static RectTransform UIImage(string name, Transform parent, Color c, Sprite sprite = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
        return (RectTransform)go.transform;
    }

    public static TextMeshProUGUI UIText(string name, Transform parent, TMP_FontAsset font, string text, float size, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = c;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        return t;
    }

    public static void Anchor(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor;
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }

    public static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    // 擬音（ポンと膨らんで、少し浮いて消える）。local は canvas 上の位置（中央が原点、1920x1080 基準）
    // PresentationSettings で擬音をオフにしているときは何も出さず null を返す
    public static GameObject Pop(MonoBehaviour host, RectTransform canvas, TMP_FontAsset font, Material popMat,
                                 string text, Vector2 local, float size, Color color, float angle, float hold, List<GameObject> live)
    {
        if (!PresentationSettings.ShowOnomatopoeia) return null;
        var t = UIText("Pop", canvas, font, text, size, color);
        t.fontSharedMaterial = popMat;
        Anchor(t.rectTransform, new Vector2(0.5f, 0.5f), local, new Vector2(700f, 220f));
        t.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        live?.Add(t.gameObject);
        host.StartCoroutine(PopRoutine(t, hold, live));
        return t.gameObject;
    }

    public static Vector2 WorldToCanvas(Camera cam, RectTransform canvas, Vector3 world)
    {
        Vector2 sp = cam.WorldToScreenPoint(world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, sp, null, out var local);
        return local;
    }

    static IEnumerator PopRoutine(TextMeshProUGUI t, float hold, List<GameObject> live)
    {
        var r = t.rectTransform;
        var basePos = r.anchoredPosition;
        float total = 0.18f + hold + 0.3f;
        for (float time = 0f; time < total && t != null; time += Time.unscaledDeltaTime)
        {
            float s = time < 0.08f ? Mathf.Lerp(0f, 1.3f, time / 0.08f) : time < 0.18f ? Mathf.Lerp(1.3f, 1f, (time - 0.08f) / 0.1f) : 1f;
            r.localScale = Vector3.one * s;
            r.anchoredPosition = basePos + Vector2.up * 30f * (time / total);
            float fadeT = (time - 0.18f - hold) / 0.3f;
            t.alpha = fadeT > 0f ? 1f - fadeT : 1f;
            yield return null;
        }
        if (t != null)
        {
            live?.Remove(t.gameObject);
            Object.Destroy(t.gameObject);
        }
    }

    // ---------------- 動きの補助 ----------------
    public static float Squash(float t, float amount)
    {
        if (t <= 0f || t >= 1f) return 0f;
        return amount * Mathf.Sin(t * Mathf.PI * 2f) * (1f - t);
    }

    public static Vector3 SquashVec(float a) => new Vector3(1f + a * 0.6f, 1f - a, 1f + a * 0.6f);

    // ぽよんと弾む（0→膨らむ→戻る）
    public static float Punch(float t, float amount)
    {
        if (t <= 0f || t >= 1f) return 0f;
        return amount * Mathf.Sin(t * Mathf.PI * 3f) * (1f - t) * (1f - t);
    }

    public static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float m = 1f - t;
        return m * m * m * a + 3f * m * m * t * b + 3f * m * t * t * c + t * t * t * d;
    }

    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    // ---------------- 汎用 ----------------
    public static Transform Primitive(PrimitiveType type, string name, Transform parent)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        if (parent != null) go.transform.SetParent(parent, false);
        return go.transform;
    }

    public static Material Lit(Color c, float smoothness)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    public static Material ParticleMaterial(bool additive, Texture tex)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        if (tex != null) m.SetTexture("_BaseMap", tex);
        return m;
    }

    // 角の丸い箱のメッシュ。立方体の各面を格子に割り、内側の箱からの距離で丸める
    public static Mesh RoundedBox(Vector3 size, float radius, int n)
    {
        Vector3 h = size * 0.5f;
        radius = Mathf.Min(radius, Mathf.Min(h.x, Mathf.Min(h.y, h.z)));
        Vector3 inner = h - Vector3.one * radius;
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                var faceNormal = Vector3.zero; faceNormal[axis] = sign;
                int start = verts.Count;
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++)
                    {
                        var p = Vector3.zero;
                        p[axis] = sign * h[axis];
                        p[a1] = Mathf.Lerp(-h[a1], h[a1], i / (float)n);
                        p[a2] = Mathf.Lerp(-h[a2], h[a2], j / (float)n);
                        var c = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        var nrm = (p - c).normalized;
                        verts.Add(c + nrm * radius);
                        norms.Add(nrm);
                        uvs.Add(new Vector2(i / (float)n, j / (float)n));
                    }
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int v0 = start + j * (n + 1) + i, v1 = v0 + 1, v2 = v0 + n + 1, v3 = v2 + 1;
                        AddTri(verts, tris, v0, v2, v1, faceNormal);
                        AddTri(verts, tris, v1, v2, v3, faceNormal);
                    }
            }

        var mesh = new Mesh { name = "RoundedBox" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // 外向きが表になるように向きを揃えて三角形を足す
    static void AddTri(List<Vector3> v, List<int> tris, int a, int b, int c, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outward) < 0f) { int t = b; b = c; c = t; }
        tris.Add(a); tris.Add(b); tris.Add(c);
    }

    static Texture2D MakeRadialTexture(int size, bool ring)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = ring ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.12f) : Mathf.Clamp01(1f - d);
                px[y * size + x] = new Color(1f, 1f, 1f, a * a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeGradient(Color top, Color bottom)
    {
        var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, y / 63f)));
        tex.Apply();
        return tex;
    }

    static Texture2D MakeShaftTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float sx = Mathf.Sin((x + 0.5f) / w * Mathf.PI);
                float sy = Mathf.Sin((y + 0.5f) / h * Mathf.PI);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, sx * sx * sy));
            }
        tex.Apply();
        return tex;
    }
}
