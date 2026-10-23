using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 【試作】タイトルの世界観を見比べるための背景差し替え（TitleSF / TitleGraffiti / TitleSumi）で使う共通部品。
// どれも TitleScreen が作った舞台のあとから動き、元の背景を片づけて新しい背景を置く。
public static class TitleDressingKit
{
    // ToyKit.CreateEnvironment が作った背景（空・床・遠くのおもちゃ・光の筋）を消す
    public static void RemoveToyBackground(UnityEngine.SceneManagement.Scene scene)
    {
        var names = new HashSet<string> { "Floor", "Sky", "BgBlock", "BgToy", "LightShaft" };
        foreach (var go in scene.GetRootGameObjects())
            if (names.Contains(go.name)) Object.Destroy(go);
    }

    public static VolumeProfile PostProfile()
    {
        var v = Object.FindAnyObjectByType<Volume>();
        return v != null ? v.sharedProfile : null;
    }

    public static Light FindLight(string name)
    {
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.name == name) return l;
        return null;
    }

    public static Transform Quad(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var q = ToyKit.Primitive(PrimitiveType.Quad, name, parent);
        q.position = pos;
        q.localScale = scale;
        var r = q.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return q;
    }

    public static Material Unlit(Color c, Texture tex = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", c);
        if (tex != null) m.SetTexture("_BaseMap", tex);
        return m;
    }

    // 透明の Unlit（アルファ付きのテクスチャを貼る）
    public static Material UnlitTransparent(Texture tex, Color c)
    {
        var m = ToyKit.ParticleMaterial(false, tex);
        m.SetColor("_BaseColor", c);
        return m;
    }

    public static Texture2D NewTexture(int w, int h, bool repeat = false)
    {
        return new Texture2D(w, h, TextureFormat.RGBA32, true)
        {
            wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
        };
    }

    // ふわっとした丸を px に重ねる（a = 中心の濃さ、hard = 0 で柔らか〜1 でくっきり）
    public static void Blob(Color[] px, int w, int h, float cx, float cy, float r, Color c, float a, float hard)
    {
        int x0 = Mathf.Max(0, (int)(cx - r)), x1 = Mathf.Min(w - 1, (int)(cx + r));
        int y0 = Mathf.Max(0, (int)(cy - r)), y1 = Mathf.Min(h - 1, (int)(cy + r));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                if (d >= 1f) continue;
                float k = hard >= 1f ? 1f : Mathf.Clamp01((1f - d) / Mathf.Max(0.001f, 1f - hard));
                Over(px, y * w + x, c, a * k);
            }
    }

    // 太さのある線（端は丸い）
    public static void Stroke(Color[] px, int w, int h, Vector2 a, Vector2 b, float width, Color c, float alpha)
    {
        float len = Vector2.Distance(a, b);
        int steps = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(1f, width * 0.35f)));
        for (int i = 0; i <= steps; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)steps);
            Blob(px, w, h, p.x, p.y, width * 0.5f, c, alpha, 0.85f);
        }
    }

    // 上に重ねる（アルファを足していく）
    public static void Over(Color[] px, int i, Color c, float a)
    {
        if (a <= 0f) return;
        var d = px[i];
        float outA = a + d.a * (1f - a);
        if (outA <= 0f) return;
        px[i] = new Color(
            (c.r * a + d.r * d.a * (1f - a)) / outA,
            (c.g * a + d.g * d.a * (1f - a)) / outA,
            (c.b * a + d.b * d.a * (1f - a)) / outA,
            outA);
    }

    public static void Fill(Color[] px, Color c)
    {
        for (int i = 0; i < px.Length; i++) px[i] = c;
    }

    // 文字を立てて置く（看板・落書きの文字）
    public static TextMeshPro Text(Transform parent, TMP_FontAsset font, string text, Vector3 pos, Vector2 box, float maxSize,
                                   Color face, Color outline, float outlineWidth, float angle)
    {
        var t = ToyKit.Text3D(font, new Material(font.material), text, parent, box, maxSize);
        var m = t.fontSharedMaterial;
        m.SetColor("_FaceColor", face);
        m.SetColor("_OutlineColor", outline);
        m.SetFloat("_OutlineWidth", outlineWidth);
        m.EnableKeyword("OUTLINE_ON");
        t.transform.position = pos;
        t.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        return t;
    }

    // TitleScreen が使っているフォント（積み木の文字と同じ）
    public static TMP_FontAsset TitleFont()
    {
        var title = Object.FindAnyObjectByType<TitleScreen>();
        if (title != null)
        {
            var f = typeof(TitleScreen).GetField("font", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null && f.GetValue(title) is TMP_FontAsset tf) return tf;
        }
        return PresentationSettings.DefaultFont;
    }

    // サブタイトルと操作ヒントの文字色
    public static void RecolorTexts(Color subtitle, Color hint)
    {
        foreach (var t in Object.FindObjectsByType<TextMeshPro>(FindObjectsInactive.Include))
            if (t.transform.parent == null && t.name == "Text")
            {
                float a = t.alpha;
                t.color = new Color(subtitle.r, subtitle.g, subtitle.b, a);
            }
        var h = GameObject.Find("Hint");
        if (h != null && h.TryGetComponent<TextMeshProUGUI>(out var ht)) ht.color = hint;
    }

    // 舞い散るもの（桜・紙吹雪・塗料のしぶきなど）
    public static ParticleSystem Drift(Transform parent, string name, Vector3 center, Vector3 box, Texture tex, bool additive,
                                       Color c0, Color c1, Vector2 size, float rate, Vector3 velMin, Vector3 velMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 5f;
        main.startLifetime = 10f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 600;
        main.prewarm = true;
        main.useUnscaledTime = true;
        var em = ps.emission; em.rateOverTime = rate;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = box;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(velMin.x, velMax.x);
        vel.y = new ParticleSystem.MinMaxCurve(velMin.y, velMax.y);
        vel.z = new ParticleSystem.MinMaxCurve(velMin.z, velMax.z);
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.8f; noise.frequency = 0.4f;
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = ToyKit.ParticleMaterial(additive, tex);
        r.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    // 花びらの形（縦長のしずく）
    public static Texture2D PetalTexture()
    {
        const int s = 32;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = (x + 0.5f) / s * 2f - 1f, v = (y + 0.5f) / s * 2f - 1f;
                float w = 0.55f * (1f - v * v) * (v < 0f ? 1f : 1f - v * 0.4f);
                float d = Mathf.Abs(u) / Mathf.Max(0.01f, w);
                float a = Mathf.Clamp01((1f - d) * 3f) * Mathf.Clamp01((1f - Mathf.Abs(v)) * 4f);
                px[y * s + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // 「墨の輪郭」：おもちゃの各部品に、裏返した少し大きいコピーを重ねて縁取りにする（インバーテッドハル）
    public static void InkOutline(Transform target, Color ink, float scale, params string[] partNames)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", ink);
        mat.SetFloat("_Cull", 1f);   // 表を消して裏だけ描く
        var parts = new HashSet<string>(partNames);
        foreach (var mf in target.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!parts.Contains(mf.name) || mf.transform.Find("InkOutline") != null) continue;
            var o = new GameObject("InkOutline");
            o.transform.SetParent(mf.transform, false);
            o.transform.localScale = Vector3.one * scale;
            o.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = o.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    // 簡単な円錐のメッシュ（コーンなど）
    public static Mesh Cone(int sides)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        verts.Add(new Vector3(0f, 1f, 0f));
        for (int i = 0; i <= sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f));
        }
        for (int i = 1; i <= sides; i++) { tris.Add(0); tris.Add(i + 1); tris.Add(i); }
        var m = new Mesh();
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        return m;
    }
}
