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
        // (氷のタイルのマテリアル(StageBuilder の IceTileMaterial)も、この高さから下を水の中として描く)
        internal const float kSeaLevel = -0.55f;
        // さざ波の法線のテクスチャ1枚が覆う広さ(m。Nakagawa/Water の _WaveScale と同じにする)
        const float kWaveTextureSize = 14.0f;
        // うねり(IceSeaWaves.hlsl)の高さ・速さの倍率。海(Nakagawa/Water)と氷(Nakagawa/IceTile)のマテリアルに同じ値を入れる
        internal const float kWaveHeight = 0.2f;
        internal const float kWaveTimeScale = 0.8f;

        // 氷のシェーダー(Nakagawa/IceTile)のマテリアルに、海と同じ海面の高さ・うねりを入れる
        internal static void ApplySeaToIce(Material mat)
        {
            mat.SetFloat("_SeaLevel", kSeaLevel);
            mat.SetFloat("_WaveHeight", kWaveHeight);
            mat.SetFloat("_WaveTimeScale", kWaveTimeScale);
        }

        // 氷の中の模様(R: 白く曇った霜 / G: 小さな泡 / B: 中のひび)。氷のタイル・浮いている氷で使う
        internal static Texture2D IceInnerTexture()
        {
            return StageTexture("IceInner", 512, (u, v) =>
            {
                // 霜: まだらに白く曇った所
                float frost = Mathf.Clamp01((Fbm(u * 4, v * 4, 4, 301) - 0.42f) * 2.2f) * 0.8f + Fbm(u * 24, v * 24, 24, 302) * 0.2f;
                // 泡: 格子のところどころに、小さな丸
                const int kCells = 40;
                float gx = u * kCells, gy = v * kCells;
                int ix = Mathf.FloorToInt(gx), iy = Mathf.FloorToInt(gy);
                float bubble = 0.0f;
                if (Hash01(ix, iy, 303) > 0.86f)
                {
                    var center = new Vector2(0.3f + 0.4f * Hash01(ix, iy, 304), 0.3f + 0.4f * Hash01(ix, iy, 305));
                    float radius = 0.1f + 0.18f * Hash01(ix, iy, 306);
                    float dist = Vector2.Distance(new Vector2(gx - ix, gy - iy), center) / radius;
                    bubble = dist < 1.0f ? Mathf.Lerp(0.5f, 1.0f, Mathf.Clamp01((dist - 0.6f) * 3.0f)) : 0.0f;
                }
                // 中のひび: 細い面の筋
                float ridge = 1.0f - Mathf.Abs(Fbm(u * 3, v * 3, 3, 307) * 2.0f - 1.0f);
                float crack = Mathf.Pow(ridge, 18.0f) * 0.9f;
                return new Color(Mathf.Clamp01(frost), bubble, crack);
            });
        }

        static void BuildIceSea(Ctx c)
        {
            var d = new Detail(c);

            // 割れない床: 黒い石灰岩(黒に近い灰色の地に、うっすらした濃淡、白い方解石の細い筋、化石のような小さな白い点)
            // 筋と点は細く少なくして、上に乗る積み木・プレイヤーが見えにくくならないようにする
            var rockTex = d.Tex("Rock", 512, (u, v) =>
            {
                float n = Fbm(u * 6, v * 6, 6, 201) * 0.6f + Fbm(u * 24, v * 24, 24, 202) * 0.4f;
                Color stone = Color.Lerp(new Color(0.07f, 0.07f, 0.075f), new Color(0.15f, 0.15f, 0.155f), n);
                float vein = Mathf.Pow(1.0f - Mathf.Abs(Fbm(u * 4, v * 4, 4, 203) * 2.0f - 1.0f), 22.0f);
                float fine = Mathf.Pow(1.0f - Mathf.Abs(Fbm(u * 9, v * 9, 9, 204) * 2.0f - 1.0f), 30.0f) * 0.5f;
                float speck = Hash01(Mathf.FloorToInt(u * 512), Mathf.FloorToInt(v * 512), 205) > 0.996f ? 0.25f : 0.0f;
                float white = Mathf.Clamp01(vein * 0.55f + fine * 0.35f + speck);
                return Color.Lerp(stone, new Color(0.62f, 0.62f, 0.6f), white);
            });
            // 波の高さ(m)。とがった波頭のうねり + 中くらいの波 + さざ波
            float WaveHeight(float u, float v)
            {
                float swell = 1.0f - Mathf.Abs(Fbm(u * 5, v * 5, 5, 211) * 2.0f - 1.0f);
                return Mathf.Pow(swell, 1.5f) * 0.3f + Fbm(u * 11, v * 11, 11, 212) * 0.08f + Fbm(u * 29, v * 29, 29, 213) * 0.015f;
            }
            var seaNormal = NormalTex(d, "SeaNormal", 512, WaveHeight, kWaveTextureSize);
            // 泡の模様(筋状に集まった泡と、細かい粒)
            var foamTex = d.Tex("SeaFoam", 256, (u, v) =>
            {
                float streak = Mathf.Pow(1.0f - Mathf.Abs(Fbm(u * 6, v * 6, 6, 214) * 2.0f - 1.0f), 4.0f);
                float grain = Fbm(u * 40, v * 40, 40, 215);
                float k = Mathf.Clamp01(streak * 0.8f + (grain - 0.5f) * 0.6f + 0.1f);
                return new Color(k, k, k);
            });
            // 浮いている氷(流氷・氷山)は、割れる氷のタイルと同じ透明感のある氷のシェーダー
            var iceInner = IceInnerTexture();
            Material IceMat(string name, Color tint, float opacity, float frostOpacity, float bob)
            {
                var mat = GetMaterial(c.Id, name, Shader.Find("Nakagawa/IceTile"), tint, 0.88f, 0.0f);
                mat.SetTexture("_BaseMap", iceInner);
                mat.SetFloat("_Opacity", opacity);
                mat.SetFloat("_FrostOpacity", frostOpacity);
                mat.SetFloat("_Bob", bob);
                ApplySeaToIce(mat);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            // ステージの床(割れない床 = 黒い石灰岩)・壁(使わないが、床に合わせる)。支えはなし(氷の盤は海に浮かぶ)
            c.FloorMaterial = d.TexMat("StageFloor", rockTex, Vector2.one, 0.45f, Color.white);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.12f, 0.12f, 0.13f), 0.4f);
            c.Backdrop.Supports.Style = StageBackdrop.SupportStyle.None;

            // 海(彩度を落とした青。チームの色・ギミックの色と重ならないよう、青緑は避ける)
            //  ・水面は半透明(Nakagawa/Water)。うねりで動く格子・流れるさざ波・空の映り込み・照り返し・物のまわりの泡
            //  ・その下の海底が、深い水の色として透けて見える(落ちた物は水の中に沈んで見える)
            {
                var g = c.Group("Sea");
                var sea = GetMaterial(c.Id, "Sea", Shader.Find("Nakagawa/Water"), new Color(0.06f, 0.13f, 0.2f), 0.94f, 0.0f);
                sea.SetTexture("_NormalMap", seaNormal);
                sea.SetTexture("_FoamMap", foamTex);
                sea.SetColor("_ShallowColor", new Color(0.2f, 0.33f, 0.43f));
                sea.SetColor("_ScatterColor", new Color(0.22f, 0.36f, 0.46f));
                sea.SetFloat("_WaveHeight", kWaveHeight);
                sea.SetFloat("_WaveTimeScale", kWaveTimeScale);
                sea.SetFloat("_WaveScale", kWaveTextureSize);
                sea.SetFloat("_NormalStrength", 0.35f);
                sea.SetFloat("_WaveSpeed", 0.04f);
                sea.SetFloat("_Opacity", 0.82f);
                sea.SetFloat("_ShallowOpacity", 0.45f);
                EditorUtility.SetDirty(sea);
                // 水面は、うねりで頂点を動かすので細かい格子(中心は 0.35m ごと、外へ行くほど粗く。端は 1500m 先)
                var grid = d.Grid("WaterGrid", 1500.0f, 0.35f, 0.1f);
                var water = d.Obj(g, "Water", grid, new Vector3(0, kSeaLevel, 0), Quaternion.identity, Vector3.one, sea);
                water.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var seabed = c.Mat("Seabed", new Color(0.035f, 0.07f, 0.1f), 0.05f);
                c.Box(g, "Seabed", new Vector3(0, kSeaLevel - 6.5f, 0), new Vector3(3000, 1, 3000), 0, seabed);
            }

            // 盤のまわりに浮かぶ小さな氷のかけら(六角形を崩した形)。波に合わせて上下する
            {
                var g = c.Group("Floes");
                var floe = IceMat("Floe", new Color(0.82f, 0.9f, 0.94f), 0.45f, 0.45f, 1.0f);
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
                    var go = d.Obj(g, $"Floe_{i}", mesh, new Vector3(Mathf.Cos(a) * r, kSeaLevel - 0.35f, Mathf.Sin(a) * r * 0.85f),
                        Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0), Vector3.one, floe);
                    // 波で上下させる位置(物の中心)を使うので、静的なまとめ描き(Static Batching)の対象から外す
                    go.isStatic = false;
                    go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            // 遠くの氷山(北と東西の水平線。角を丸めた箱を積んだ形)。大きな氷の塊なので、流氷より白く曇らせる
            {
                var g = c.Group("Icebergs");
                var berg = IceMat("Iceberg", new Color(0.86f, 0.92f, 0.96f), 0.7f, 0.3f, 0.0f);
                var bergShade = IceMat("IcebergShade", new Color(0.62f, 0.72f, 0.8f), 0.75f, 0.25f, 0.0f);
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

        // XZ の平らな格子(上向き。中心ほど細かく、外ほど粗い)。halfSize: 中心から端まで(m) / cell: 中心のマス目(m)
        // growth: 中心から 1m 離れるごとに、マス目が広がる割合
        static Mesh BuildGrid(float halfSize, float cell, float growth)
        {
            var side = new List<float> { 0.0f };
            float x = 0.0f;
            while (x < halfSize)
            {
                x = Mathf.Min(halfSize, x + cell * (1.0f + x * growth));
                side.Add(x);
            }
            var axis = new List<float>();
            for (int i = side.Count - 1; i > 0; i--) axis.Add(-side[i]);
            axis.AddRange(side);

            int n = axis.Count;
            var vertices = new Vector3[n * n];
            var normals = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (int z = 0; z < n; z++)
            {
                for (int i = 0; i < n; i++)
                {
                    vertices[z * n + i] = new Vector3(axis[i], 0.0f, axis[z]);
                    normals[z * n + i] = Vector3.up;
                    uvs[z * n + i] = new Vector2(axis[i], axis[z]);
                }
            }
            var triangles = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int z = 0; z < n - 1; z++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    int a = z * n + i, b = a + 1, c = a + n, d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            }
            var mesh = new Mesh { indexFormat = vertices.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            return mesh;
        }

        // 高さ(m)の関数から、法線のテクスチャ(つなぎ目なし)を作る。worldSize はテクスチャ1枚が覆う広さ(m)
        static Texture2D NormalTex(Detail d, string name, int size, Func<float, float, float> height, float worldSize)
        {
            float e = 1.0f / size;
            var tex = d.Tex(name, size, (u, v) =>
            {
                // 坂の傾き(高さの差 ÷ 距離)
                float dx = (height(u + e, v) - height(u - e, v)) / (2.0f * e * worldSize);
                float dy = (height(u, v + e) - height(u, v - e)) / (2.0f * e * worldSize);
                var n = new Vector3(-dx, -dy, 1.0f).normalized;
                return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f);
            });
            string path = AssetDatabase.GetAssetPath(tex);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
