using UnityEngine;
using UnityEngine.Rendering;

namespace Nakagawa
{
    // 実行中に組み立てる演出(予告・復活など)で使う、簡単なメッシュとマテリアル
    //  ・メッシュは床に平らな円・輪と、上に向かって透明になる筒。どれも半径1(筒は高さ1)なので、拡大して使う
    //  ・マテリアルは両面を描く透明(通常 / 加算)のパーティクル用シェーダー。頂点カラーと _BaseColor が掛け合わされる
    //  ・色は Create で作った描画物ごとに SetColor で変える(マテリアルは全体で共有する)
    public static class EffectMeshes
    {
        const int kSegments = 48;
        // 輪の内側の半径(外側を1として)
        const float kRingInner = 0.82f;

        static Mesh s_disc, s_ring, s_tube;
        static Material s_alpha, s_additive;
        static MaterialPropertyBlock s_mpb;
        static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");

        // 半径1の円(中心から縁に向かって少し薄くなる)
        public static Mesh Disc => s_disc != null ? s_disc : s_disc = BuildDisc();
        // 内径0.82〜外径1の輪(縁がぼける)
        public static Mesh Ring => s_ring != null ? s_ring : s_ring = BuildRing();
        // 半径1・高さ1の筒(ふたなし。上に向かって透明になる)
        public static Mesh Tube => s_tube != null ? s_tube : s_tube = BuildTube();

        public static Material AlphaMaterial => s_alpha != null ? s_alpha : s_alpha = CreateMaterial(false);
        public static Material AdditiveMaterial => s_additive != null ? s_additive : s_additive = CreateMaterial(true);

        // 描画物を作る(当たり判定なし・影なし)
        public static MeshRenderer Create(string name, Transform parent, Mesh mesh, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = additive ? AdditiveMaterial : AlphaMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        public static void SetColor(Renderer r, Color color)
        {
            if (r == null) return;
            s_mpb ??= new MaterialPropertyBlock();
            s_mpb.Clear();
            s_mpb.SetColor(s_baseColorId, color);
            r.SetPropertyBlock(s_mpb);
        }

        // 明るさ(0〜1)を足して白に寄せた色
        public static Color Brighten(Color c, float amount)
        {
            Color b = Color.Lerp(c, Color.white, amount);
            b.a = c.a;
            return b;
        }

        static Material CreateMaterial(bool additive)
        {
            var m = ToyKit.ParticleMaterial(additive, null);
            if (additive)
            {
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            }
            // 輪・円は上からも下からも、筒は外からも内からも見えるように
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)CullMode.Off);
            m.name = additive ? "EffectMeshes_Additive" : "EffectMeshes_Alpha";
            return m;
        }

        static Mesh BuildDisc()
        {
            var v = new Vector3[kSegments + 1];
            var c = new Color[kSegments + 1];
            var t = new int[kSegments * 3];
            v[0] = Vector3.zero;
            c[0] = Color.white;
            for (int i = 0; i < kSegments; i++)
            {
                float a = i * Mathf.PI * 2.0f / kSegments;
                v[i + 1] = new Vector3(Mathf.Cos(a), 0.0f, Mathf.Sin(a));
                c[i + 1] = new Color(1.0f, 1.0f, 1.0f, 0.6f);
                t[i * 3] = 0;
                t[i * 3 + 1] = (i + 1) % kSegments + 1;
                t[i * 3 + 2] = i + 1;
            }
            return NewMesh("EffectDisc", v, c, t);
        }

        static Mesh BuildRing()
        {
            // 内側の縁(透明)→ 真ん中(不透明)→ 外側の縁(透明)の3周
            float[] radii = { kRingInner, (kRingInner + 1.0f) * 0.5f, 1.0f };
            float[] alphas = { 0.0f, 1.0f, 0.0f };
            return BuildBands("EffectRing", radii, alphas, false);
        }

        static Mesh BuildTube()
        {
            // 下(不透明)から上(透明)へ
            float[] heights = { 0.0f, 0.25f, 1.0f };
            float[] alphas = { 1.0f, 0.7f, 0.0f };
            return BuildBands("EffectTube", heights, alphas, true);
        }

        // 同心円の帯(tube なら同じ半径で高さ方向に重ねた帯)
        static Mesh BuildBands(string name, float[] steps, float[] alphas, bool tube)
        {
            int rows = steps.Length;
            var v = new Vector3[kSegments * rows];
            var c = new Color[v.Length];
            for (int r = 0; r < rows; r++)
            {
                for (int i = 0; i < kSegments; i++)
                {
                    float a = i * Mathf.PI * 2.0f / kSegments;
                    var dir = new Vector3(Mathf.Cos(a), 0.0f, Mathf.Sin(a));
                    v[r * kSegments + i] = tube ? dir + Vector3.up * steps[r] : dir * steps[r];
                    c[r * kSegments + i] = new Color(1.0f, 1.0f, 1.0f, alphas[r]);
                }
            }
            var t = new int[(rows - 1) * kSegments * 6];
            int k = 0;
            for (int r = 0; r < rows - 1; r++)
            {
                for (int i = 0; i < kSegments; i++)
                {
                    int i0 = r * kSegments + i;
                    int i1 = r * kSegments + (i + 1) % kSegments;
                    int j0 = i0 + kSegments;
                    int j1 = i1 + kSegments;
                    t[k++] = i0; t[k++] = j0; t[k++] = i1;
                    t[k++] = i1; t[k++] = j0; t[k++] = j1;
                }
            }
            return NewMesh(name, v, c, t);
        }

        static Mesh NewMesh(string name, Vector3[] v, Color[] c, int[] t)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = v;
            mesh.colors = c;
            mesh.triangles = t;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
