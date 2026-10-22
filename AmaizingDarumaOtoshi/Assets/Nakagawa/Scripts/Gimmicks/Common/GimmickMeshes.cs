using UnityEngine;

namespace Nakagawa
{
    // 追加ギミックの見た目に使う簡単なメッシュ(頂点カラーは白。透明なものは頂点カラーのアルファで形を作る)
    // エディタの組み立て(AdditionalGimmickBuilder)がアセットとして保存してプレハブから参照する。実行中に作ることもできる
    public static class GimmickMeshes
    {
        const int kSegments = 48;

        // 床に平らな 1×1 の四角(中心が原点、上向き)
        public static Mesh BuildQuad()
        {
            var v = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) };
            var uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            var mesh = NewMesh("GimmickQuad", v, White(v.Length), new[] { 0, 1, 2, 0, 2, 3 });
            mesh.uv = uv;
            return mesh;
        }

        // 太い輪(フラフープ)。中心の円の半径1、太さの半径 tube。床に平ら
        public static Mesh BuildTorus(float tube = 0.16f, int tubeSegments = 12)
        {
            int rows = kSegments + 1, cols = tubeSegments + 1;
            var v = new Vector3[rows * cols];
            var n = new Vector3[v.Length];
            for (int i = 0; i < rows; i++)
            {
                float a = i * Mathf.PI * 2.0f / kSegments;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int j = 0; j < cols; j++)
                {
                    float b = j * Mathf.PI * 2.0f / tubeSegments;
                    Vector3 normal = dir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v[i * cols + j] = dir + normal * tube;
                    n[i * cols + j] = normal;
                }
            }
            var t = new int[kSegments * tubeSegments * 6];
            int k = 0;
            for (int i = 0; i < kSegments; i++)
            {
                for (int j = 0; j < tubeSegments; j++)
                {
                    int a = i * cols + j, b = (i + 1) * cols + j;
                    t[k++] = a; t[k++] = a + 1; t[k++] = b;
                    t[k++] = b; t[k++] = a + 1; t[k++] = b + 1;
                }
            }
            var mesh = NewMesh("GimmickTorus", v, White(v.Length), t, false);
            mesh.normals = n;
            return mesh;
        }

        // 渦を巻く光の面(半径1の円。腕の部分が明るく、縁に向かって薄くなる)
        public static Mesh BuildSwirl(int arms = 3, float twist = 1.4f, int rings = 10)
        {
            int cols = kSegments;
            var v = new Vector3[1 + rings * cols];
            var c = new Color[v.Length];
            v[0] = Vector3.zero;
            c[0] = new Color(1, 1, 1, 1.0f);
            for (int r = 1; r <= rings; r++)
            {
                float radius = (float)r / rings;
                for (int i = 0; i < cols; i++)
                {
                    float a = i * Mathf.PI * 2.0f / cols;
                    float arm = 0.5f + 0.5f * Mathf.Sin(a * arms + radius * twist * Mathf.PI * 2.0f);
                    float fade = 1.0f - Mathf.Pow(radius, 3.0f);
                    v[1 + (r - 1) * cols + i] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                    c[1 + (r - 1) * cols + i] = new Color(1, 1, 1, Mathf.Lerp(0.25f, 1.0f, arm * arm) * fade);
                }
            }
            var t = new int[cols * 3 + (rings - 1) * cols * 6];
            int k = 0;
            for (int i = 0; i < cols; i++)
            {
                t[k++] = 0; t[k++] = 1 + (i + 1) % cols; t[k++] = 1 + i;
            }
            for (int r = 0; r < rings - 1; r++)
            {
                for (int i = 0; i < cols; i++)
                {
                    int a0 = 1 + r * cols + i, a1 = 1 + r * cols + (i + 1) % cols;
                    int b0 = a0 + cols, b1 = a1 + cols;
                    t[k++] = a0; t[k++] = a1; t[k++] = b0;
                    t[k++] = a1; t[k++] = b1; t[k++] = b0;
                }
            }
            return NewMesh("GimmickSwirl", v, c, t);
        }

        // 半径1の光の輪(内側 inner〜外側1。真ん中が明るく、内外の縁がぼける)。床に平ら
        public static Mesh BuildRing(float inner = 0.82f)
        {
            float[] radii = { inner, (inner + 1.0f) * 0.5f, 1.0f };
            float[] alphas = { 0.0f, 1.0f, 0.0f };
            return BuildBands("GimmickRing", radii, alphas);
        }

        // 半径1の光の円(中心が明るく、縁に向かって消える)。床に平ら
        public static Mesh BuildDisc()
        {
            float[] radii = { 0.0f, 0.5f, 1.0f };
            float[] alphas = { 1.0f, 0.6f, 0.0f };
            return BuildBands("GimmickDisc", radii, alphas);
        }

        // 同心円の帯
        static Mesh BuildBands(string name, float[] radii, float[] alphas)
        {
            int rows = radii.Length;
            var v = new Vector3[kSegments * rows];
            var c = new Color[v.Length];
            for (int r = 0; r < rows; r++)
            {
                for (int i = 0; i < kSegments; i++)
                {
                    float a = i * Mathf.PI * 2.0f / kSegments;
                    v[r * kSegments + i] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radii[r];
                    c[r * kSegments + i] = new Color(1, 1, 1, alphas[r]);
                }
            }
            var t = new int[(rows - 1) * kSegments * 6];
            int k = 0;
            for (int r = 0; r < rows - 1; r++)
            {
                for (int i = 0; i < kSegments; i++)
                {
                    int a0 = r * kSegments + i, a1 = r * kSegments + (i + 1) % kSegments;
                    int b0 = a0 + kSegments, b1 = a1 + kSegments;
                    t[k++] = a0; t[k++] = a1; t[k++] = b0;
                    t[k++] = a1; t[k++] = b1; t[k++] = b0;
                }
            }
            return NewMesh(name, v, c, t);
        }

        // 進む向き(+Z)を指す「く」の字の矢印。幅1・奥行き0.5、床に平ら
        public static Mesh BuildChevron(float thickness = 0.18f)
        {
            var v = new[]
            {
                new Vector3(-0.5f, 0, -0.25f), new Vector3(-0.5f, 0, -0.25f + thickness), new Vector3(0, 0, 0.25f + thickness * 0.5f),
                new Vector3(0, 0, 0.25f - thickness * 0.5f), new Vector3(0.5f, 0, -0.25f + thickness), new Vector3(0.5f, 0, -0.25f),
            };
            var t = new[] { 0, 1, 2, 0, 2, 3, 3, 2, 4, 3, 4, 5 };
            return NewMesh("GimmickChevron", v, White(v.Length), t);
        }

        static Color[] White(int count)
        {
            var c = new Color[count];
            for (int i = 0; i < count; i++) c[i] = Color.white;
            return c;
        }

        static Mesh NewMesh(string name, Vector3[] v, Color[] c, int[] t, bool normals = true)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = v;
            mesh.colors = c;
            mesh.triangles = t;
            if (normals) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
