using System.Collections.Generic;
using UnityEngine;

// めくれる紙（メニューの看板の得点板・ノート用）。
// 上の綴じ目（このオブジェクトの位置）から紙が垂れ下がっていて、下の端を持って真上に持ち上げるようにめくる：
//   1. 持ち上げ（0〜liftPortion）：下の端が手前へ回り込みながら真上へ上がり、紙はゆるいU字に曲がる
//      （少し硬めの紙なので、曲がりはきつく折れず、半径 curl の丸みを保つ）
//   2. 送り（liftPortion〜1）：持ち上げた紙を綴じ目を軸に後ろへ倒し、丸みを伸ばしながら看板の裏へ回す
// SetProgress(0) で垂れ下がったまま、SetProgress(1) で裏へ回りきる。逆に動かせば前のページが戻ってくる。
// 紙は表と裏の2枚の面（法線が逆）で作るので、片面描画のマテリアルのままで両面が正しく照らされる。
public class PaperFlipPage : MonoBehaviour
{
    [Tooltip("曲がりの半径（紙の高さに対する割合）。大きいほど硬い紙に見える")]
    [Range(0.05f, 0.3f)] public float curl = 0.15f;
    [Tooltip("全体のうち、持ち上げにかける割合（残りで裏へ送る）")]
    [Range(0.3f, 0.9f)] public float liftPortion = 0.6f;
    [Tooltip("送りで後ろへ倒す角度")]
    [Range(90f, 180f)] public float overAngle = 178f;
    [Tooltip("送りの終わりの曲がりの半径（curl に対する割合。小さいほど平らに伸びる）")]
    [Range(0f, 1f)] public float relaxedCurl = 0.15f;

    const int Segments = 40;
    const float Thickness = 0.008f;

    float width, height, topGap;
    Mesh mesh;
    MeshRenderer meshRenderer;
    MaterialPropertyBlock props;
    readonly List<Vector3> verts = new List<Vector3>();
    readonly List<Vector3> normals = new List<Vector3>();

    // 紙のもとの色（マテリアルの _BaseColor）
    public Color BaseColor { get; private set; } = Color.white;

    // hinge：綴じ目（このオブジェクトになる）。topGap：綴じ目から紙の上の端までの距離（リングの分）
    public static PaperFlipPage Create(Transform hinge, float width, float height, float topGap, Material material)
    {
        var p = hinge.gameObject.AddComponent<PaperFlipPage>();
        p.Setup(width, height, topGap, material);
        return p;
    }

    void Setup(float width, float height, float topGap, Material material)
    {
        this.width = width;
        this.height = height;
        this.topGap = topGap;

        var page = new GameObject("FlipPage");
        page.transform.SetParent(transform, false);
        page.transform.localPosition = new Vector3(0f, -topGap, 0f);
        mesh = new Mesh { name = "PaperFlipPage" };
        mesh.MarkDynamic();
        page.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = page.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        if (material != null && material.HasProperty("_BaseColor")) BaseColor = material.GetColor("_BaseColor");
        props = new MaterialPropertyBlock();

        BuildTriangles();
        SetProgress(0f);
    }

    public void SetColor(Color c)
    {
        if (meshRenderer == null) return;
        props.SetColor("_BaseColor", c);
        meshRenderer.SetPropertyBlock(props);
    }

    // 0：垂れ下がったまま　1：裏へ回りきった
    public void SetProgress(float u)
    {
        u = Mathf.Clamp01(u);
        float r0 = curl * height;
        float lifted, radius, angle;
        if (u < liftPortion)
        {
            // 持ち上げ：下から順に紙が持ち上がっていく
            float e = Mathf.SmoothStep(0f, 1f, u / liftPortion);
            lifted = height * e;
            radius = r0;
            angle = 0f;
        }
        else
        {
            // 送り：綴じ目を軸に後ろへ倒しながら、丸みを伸ばす
            float e = Mathf.SmoothStep(0f, 1f, (u - liftPortion) / (1f - liftPortion));
            lifted = height;
            radius = Mathf.Lerp(r0, r0 * relaxedCurl, e);
            angle = overAngle * e;
        }
        transform.localRotation = Quaternion.Euler(angle, 0f, 0f);
        Deform(lifted, Mathf.Max(0.001f, radius));
    }

    // 紙の上の端から s の位置（y, z）と進む向き。lifted：持ち上がった長さ　radius：曲がりの半径
    void Curve(float s, float lifted, float radius, out Vector2 pos, out Vector2 tangent)
    {
        float hang = height - lifted;              // まだ垂れ下がっている長さ
        if (s <= hang) { pos = new Vector2(-s, 0f); tangent = new Vector2(-1f, 0f); return; }
        float t = s - hang;
        float arc = Mathf.PI * radius;
        if (t <= arc)
        {
            // 下をくぐって手前へ回り込む丸み（y, z）
            float phi = t / radius;
            pos = new Vector2(-hang - radius * Mathf.Sin(phi), -radius + radius * Mathf.Cos(phi));
            tangent = new Vector2(-Mathf.Cos(phi), -Mathf.Sin(phi));
            return;
        }
        // 手前で真上へ立ち上がる
        float up = t - arc;
        pos = new Vector2(-hang + up, -2f * radius);
        tangent = new Vector2(1f, 0f);
    }

    void Deform(float lifted, float radius)
    {
        verts.Clear();
        normals.Clear();
        float hw = width * 0.5f;
        // 表の面（垂れ下がっているとき手前を向く面）と裏の面
        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? 1f : -1f;
            for (int i = 0; i <= Segments; i++)
            {
                float s = height * i / Segments;
                Curve(s, lifted, radius, out var p, out var tan);
                // 表の法線：垂れ下がっているとき (0, 0, -1)
                var n = new Vector3(0f, -tan.y, tan.x) * sign;
                var c = new Vector3(0f, p.x, p.y) + n * (Thickness * 0.5f);
                verts.Add(new Vector3(-hw, c.y, c.z));
                verts.Add(new Vector3(hw, c.y, c.z));
                normals.Add(n);
                normals.Add(n);
            }
        }
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.RecalculateBounds();
    }

    void BuildTriangles()
    {
        var tris = new List<int>();
        int row = 2, layer = (Segments + 1) * 2;
        for (int i = 0; i < Segments; i++)
        {
            int a = i * row;
            // 表（手前から見て時計回り）
            tris.Add(a); tris.Add(a + 1); tris.Add(a + 2);
            tris.Add(a + 1); tris.Add(a + 3); tris.Add(a + 2);
            // 裏（逆回り）
            int b = layer + a;
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
        }
        // 頂点を先に用意しておく（Deform で上書き）
        Deform(0f, Mathf.Max(0.001f, curl * height));
        mesh.SetTriangles(tris, 0);
    }
}
