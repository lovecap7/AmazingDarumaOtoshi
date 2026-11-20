using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nakagawa.EditorTools
{
    // ステージ本体の床・壁の作り込みに使う、テクスチャ・マテリアル・メッシュの補助(StageBuilder.Dressing.cs から使う)と、
    // 冷たい海の背景(IceSea。Stage21 氷割りゲーム)
    //  ・テクスチャ: Backdrops/Textures/Stage_<名前>.png
    //  ・マテリアル: Backdrops/Materials/Stage_<名前>.mat
    public static partial class BackdropBuilder
    {
        const string kStageSurfaceId = "Stage";
        const string kIceSeaId = "IceSea";

        //========================================
        // ステージ本体用の補助
        //========================================

        // つなぎ目の出ないテクスチャ(u, v は 0〜1)を PNG に保存して読み込む
        internal static Texture2D StageTexture(string name, int size, Func<float, float, Color> pixel, bool repeat = true)
        {
            EnsureFolder(kBackdropDir);
            EnsureFolder(kTextureDir);
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = pixel((x + 0.5f) / size, (y + 0.5f) / size);
            tex.SetPixels(px);
            tex.Apply();
            string path = $"{kTextureDir}/{kStageSurfaceId}_{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ステージ本体用のマテリアル。seeThrough なら壁用(裏が透けて見える壁のシェーダー)
        internal static Material StageMaterial(string name, Color tint, float smoothness, float metallic = 0.0f, Texture2D tex = null, Vector2? tiling = null, bool seeThrough = false)
        {
            EnsureFolder(kBackdropDir);
            EnsureFolder(kMaterialDir);
            var shader = Shader.Find(seeThrough ? "Nakagawa/SeeThroughWall" : "Universal Render Pipeline/Lit");
            var mat = GetMaterial(kStageSurfaceId, name, shader, tint, smoothness, metallic);
            Vector2 t = tiling ?? Vector2.one;
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetTextureScale("_BaseMap", t);
            }
            if (mat.HasProperty("_MainTex"))
            {
                mat.SetTexture("_MainTex", tex);
                mat.SetTextureScale("_MainTex", t);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // 光って見える素材(LED・表示の数字など)。光の当たり方に関係なく同じ明るさで描く(URP の Unlit)
        // Lit の発光(_EMISSION のキーワード)は、エディタで作り直しや再コンパイルをするとキーワードが外れて光らなくなることがあるため使わない
        // intensity は明るさの倍率(1 を超えた分は白に近づける)
        internal static Material StageEmissive(string name, Color color, float intensity)
        {
            EnsureFolder(kBackdropDir);
            EnsureFolder(kMaterialDir);
            float over = Mathf.Clamp01((intensity - 1.0f) * 0.15f);
            Color c = Color.Lerp(color, Color.white, over);
            c.a = 1.0f;
            var mat = GetMaterial(kStageSurfaceId, name, Shader.Find("Universal Render Pipeline/Unlit"), c, 0.0f, 0.0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // 光る色を設定してあるのに _EMISSION のキーワードが外れているマテリアル(Backdrops/Materials)を直す
        // (組み立ての途中の取り込み直しで、キーワードが外れることがあるため、組み立ての最後に呼ぶ)。直した数を返す
        internal static int RestoreEmissionKeywords()
        {
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { kMaterialDir }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || !mat.HasProperty("_EmissionColor") || mat.IsKeywordEnabled("_EMISSION")) continue;
                if (mat.GetColor("_EmissionColor").maxColorComponent <= 0.01f) continue;
                if ((mat.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) != 0) continue;
                mat.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);
                count++;
            }
            return count;
        }

        // 角を丸めた箱(中心が原点)。UV は texelPeriod(m)ごとに 1 回繰り返す(長さの違う箱でも模様の細かさがそろう)
        internal static Mesh NewRoundedBox(Vector3 size, float radius, int segments, float texelPeriod)
        {
            var mesh = BuildRoundedBox(size, radius, segments);
            float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            var uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);
            for (int i = 0; i < uvs.Count; i++) uvs[i] = (uvs[i] - new Vector2(0.5f, 0.5f)) * (maxDim / Mathf.Max(0.01f, texelPeriod));
            mesh.SetUVs(0, uvs);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // 回転体(profile は (半径, 高さ) を下から上へ)
        internal static Mesh NewLathe(Vector2[] profile, int segments)
        {
            var mesh = BuildLathe(profile, segments);
            mesh.RecalculateBounds();
            return mesh;
        }

        // 凸多角形(XZ。上から見て反時計回り)を上へ押し出す
        internal static Mesh NewExtrude(List<Vector2> poly, float height)
        {
            var mesh = BuildExtrude(poly, height);
            mesh.RecalculateBounds();
            return mesh;
        }

        // 模様の部品(StageBuilder から使う)
        internal static float NoiseFbm(float x, float y, int period, int seed) => Fbm(x, y, period, seed);
        internal static float NoiseHash(int x, int y, int seed) => Hash01(x, y, seed);
        internal static float Fract(float x) => Frac(x);
        internal static Color WoodPattern(float u, float v, int seed, Color light, Color dark) => WoodGrain(u, v, seed, light, dark);

        // 焦げた黒い鉄(鉄板のふち)。油の焼けた茶色のむらと、こすれて明るくなった細かい点
        internal static Color ScorchedIron(float u, float v, int seed)
        {
            float n = Fbm(u * 6, v * 6, 6, seed) * 0.65f + Fbm(u * 24, v * 24, 24, seed + 1) * 0.35f;
            float burn = Mathf.Clamp01((Fbm(u * 3, v * 3, 3, seed + 2) - 0.48f) * 4.0f);
            float speck = Hash01(Mathf.FloorToInt(u * 256), Mathf.FloorToInt(v * 256), seed + 3) > 0.985f ? 0.05f : 0.0f;
            Color iron = Color.Lerp(new Color(0.1f, 0.1f, 0.1f), new Color(0.17f, 0.165f, 0.16f), n);
            Color scorch = Color.Lerp(new Color(0.2f, 0.12f, 0.07f), new Color(0.12f, 0.08f, 0.05f), n);
            Color c = Color.Lerp(iron, scorch, burn * 0.75f);
            return c + new Color(speck, speck, speck);
        }

        // 焦げ茶の板張り(縁側の床)。継ぎ目を太く暗くする
        internal static Color DarkPlanks(float u, float v, int count, int seed)
        {
            float pv = v * count;
            int plank = Mathf.FloorToInt(pv);
            float within = pv - plank;
            float shade = 0.85f + Hash01(plank, 0, seed) * 0.22f;
            // 木目は薄く(細かい模様の上では積み木が見えにくくなる)
            Color baseC = WoodGrain(Frac(u + Hash01(plank, 1, seed)), within / count + plank * 0.37f, seed + plank,
                new Color(0.33f, 0.22f, 0.14f), new Color(0.26f, 0.16f, 0.1f));
            // 板の端の木口(縦の継ぎ目)。板ごとに位置をずらす
            float endU = Frac(u + Hash01(plank, 2, seed));
            const float kSeam = 0.07f;
            float seam = 1.0f;
            if (within < kSeam || within > 1.0f - kSeam)
            {
                float t = Mathf.Min(within, 1.0f - within) / kSeam;
                seam = Mathf.Lerp(0.22f, 0.75f, t * t);
            }
            if (endU < 0.012f) seam = Mathf.Min(seam, 0.3f);
            return baseC * shade * seam;
        }

        //========================================
        // 冷たい海(Stage21 氷割りゲーム)
        //========================================

        // 海面の高さ(ステージの床の上面が 0)。氷のタイル(厚さ 1m)の半分ほどが水に沈んで見える
        const float kSeaLevel = -0.55f;

        static void BuildIceSea(Ctx c)
        {
            var d = new Detail(c);

            var rockTex = d.Tex("Rock", 256, (u, v) =>
            {
                float n = Fbm(u * 8, v * 8, 8, 201) * 0.6f + Fbm(u * 32, v * 32, 32, 202) * 0.4f;
                // 岩の割れ目(尾根の形の雑音を細い暗い線にする)
                float ridge = 1.0f - Mathf.Abs(Fbm(u * 5, v * 5, 5, 203) * 2.0f - 1.0f);
                float crack = Mathf.Pow(ridge, 14.0f);
                float frost = Mathf.Clamp01((Fbm(u * 4, v * 4, 4, 204) - 0.6f) * 3.0f) * 0.12f;
                Color rock = Color.Lerp(new Color(0.24f, 0.25f, 0.27f), new Color(0.38f, 0.39f, 0.41f), n);
                rock = Color.Lerp(rock, new Color(0.12f, 0.12f, 0.13f), crack * 0.8f);
                return rock + new Color(frost, frost * 1.02f, frost * 1.06f);
            });
            var seaTex = d.Tex("Sea", 256, (u, v) =>
            {
                // 細かいさざ波(横に流れる筋)と、大きなうねりの明暗
                float swell = Fbm(u * 4, v * 4, 4, 211);
                float ripple = Fbm(u * 48, v * 12, 48, 212);
                float k = 0.85f + swell * 0.2f + (ripple - 0.5f) * 0.12f;
                return new Color(k, k, k);
            });
            var floeTex = d.Tex("Floe", 256, (u, v) =>
            {
                float n = Fbm(u * 8, v * 8, 8, 221) * 0.7f + Fbm(u * 32, v * 32, 32, 222) * 0.3f;
                return Color.Lerp(new Color(0.78f, 0.86f, 0.9f), new Color(0.93f, 0.96f, 0.98f), n);
            });

            // ステージの床(割れない床 = 岩)・壁(使わないが、岩に合わせる)。支えはなし(氷の盤は海に浮かぶ)
            c.FloorMaterial = d.TexMat("StageFloor", rockTex, Vector2.one, 0.25f, Color.white);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.3f, 0.31f, 0.33f), 0.3f);
            c.Backdrop.Supports.Style = StageBackdrop.SupportStyle.None;

            // 海(暗い紺。チームの色・ギミックの色と重ならないよう、青緑は避けて彩度を落とす)
            {
                var g = c.Group("Sea");
                var sea = d.TexMat("Sea", seaTex, new Vector2(300, 300), 0.92f, new Color(0.07f, 0.13f, 0.2f));
                c.Box(g, "Water", new Vector3(0, kSeaLevel - 0.5f, 0), new Vector3(3000, 1, 3000), 0, sea);
            }

            // 盤のまわりに浮かぶ小さな氷のかけら(六角形を崩した形。割れた氷のタイルと同じ素材感)
            {
                var g = c.Group("Floes");
                var floe = d.TexMat("Floe", floeTex, new Vector2(0.5f, 0.5f), 0.75f, Color.white);
                var rng = new System.Random(2101);
                for (int i = 0; i < 28; i++)
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2);
                    float r = 17.5f + (float)rng.NextDouble() * 26.0f;
                    float size = 0.6f + (float)rng.NextDouble() * 1.6f;
                    var poly = new List<Vector2>();
                    int sides = 5 + rng.Next(3);
                    for (int k = 0; k < sides; k++)
                    {
                        float ka = k * Mathf.PI * 2 / sides + (float)(rng.NextDouble() - 0.5) * 0.5f;
                        float kr = size * (0.7f + (float)rng.NextDouble() * 0.3f);
                        poly.Add(new Vector2(Mathf.Cos(ka) * kr, Mathf.Sin(ka) * kr));
                    }
                    var mesh = d.Extrude($"Floe_{i}", poly, 0.5f);
                    d.Obj(g, $"Floe_{i}", mesh, new Vector3(Mathf.Cos(a) * r, kSeaLevel - 0.35f, Mathf.Sin(a) * r * 0.85f),
                        Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0), Vector3.one, floe);
                }
            }

            // 遠くの氷山(北と東西の水平線。角を丸めた箱を積んだ形)
            {
                var g = c.Group("Icebergs");
                var berg = d.TexMat("Iceberg", floeTex, new Vector2(4, 4), 0.55f, new Color(0.86f, 0.92f, 0.96f));
                var bergShade = d.TexMat("IcebergShade", floeTex, new Vector2(4, 4), 0.55f, new Color(0.62f, 0.72f, 0.8f));
                var bergs = new[]
                {
                    (new Vector3(-150, 0, 260), 46f, 0.9f), (new Vector3(-40, 0, 330), 70f, 1.3f), (new Vector3(110, 0, 280), 52f, 1.0f),
                    (new Vector3(260, 0, 140), 40f, 0.8f), (new Vector3(-280, 0, 90), 36f, 0.7f), (new Vector3(220, 0, 330), 30f, 0.6f),
                };
                // 氷山: 角ばった塊(多角形の押し出し)を、傾けながら細く高く積む
                var rngB = new System.Random(2102);
                for (int i = 0; i < bergs.Length; i++)
                {
                    var (p, w, h) = bergs[i];
                    int tiers = 4;
                    float y = kSeaLevel - w * 0.1f;
                    for (int k = 0; k < tiers; k++)
                    {
                        float rad = w * 0.5f * (1.0f - k * 0.2f);
                        float th = w * 0.32f * h * (1.0f - k * 0.12f);
                        var poly = new List<Vector2>();
                        int sides = 6 + rngB.Next(3);
                        for (int s = 0; s < sides; s++)
                        {
                            float a = s * Mathf.PI * 2 / sides + (float)(rngB.NextDouble() - 0.5) * 0.4f;
                            float r = rad * (0.75f + (float)rngB.NextDouble() * 0.25f);
                            poly.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.7f));
                        }
                        var mesh = d.Extrude($"Berg_{i}_{k}", poly, th);
                        Vector3 off = new Vector3((float)(rngB.NextDouble() - 0.5) * rad * 0.35f, y, (float)(rngB.NextDouble() - 0.5) * rad * 0.2f);
                        var rot = Quaternion.Euler((float)(rngB.NextDouble() - 0.5) * 8, (float)rngB.NextDouble() * 360, (float)(rngB.NextDouble() - 0.5) * 8);
                        d.Obj(g, $"Berg_{i}_{k}", mesh, p + off, rot, Vector3.one, k == 0 ? bergShade : berg);
                        y += th * 0.8f;
                    }
                }
                // 水平線の先の雪の山並み(低く、色を空に近づける)
                var far = c.Mat("FarRidge", new Color(0.7f, 0.76f, 0.82f), 0.2f);
                for (int i = 0; i < 9; i++)
                {
                    float x = -800 + i * 200;
                    c.Prim(PrimitiveType.Sphere, g, $"Ridge_{i}", new Vector3(x, kSeaLevel - 10, 900 + (i % 3) * 60), new Vector3(320, 140 + (i * 53 % 4) * 30, 120), Quaternion.identity, far);
                }
            }
            d.Save();
        }
    }
}
