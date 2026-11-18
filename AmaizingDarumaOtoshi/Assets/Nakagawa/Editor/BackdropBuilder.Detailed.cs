using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nakagawa.EditorTools
{
    // 作り込んだ背景(Stage25〜)。3D モデルは使わず、ビルダーの中で次のものを作って質感を上げる
    //  ・メッシュ: 回転体(器・筆など)・角を丸めた箱(硯・墨など)・押し出し(すいかなど)・管(蚊取り線香)。
    //    背景ごとに1つのアセット(Backdrops/Meshes/<背景>.asset)にまとめて保存する(作り直すと中身を入れ替える)
    //  ・テクスチャ: 木目・紙の繊維・畳・フェルト・障子など、繰り返してもつなぎ目の出ない模様を計算で作り、PNG で保存する(Backdrops/Textures)
    //  ・光: ろうそく・電球など、小物の明かりを点の光で置く(ステージの上は暗くしない)
    // 色は StageDesignPitfalls.md に合わせて、彩度を低く・積み木の赤・青・黄・緑と同じ色の物を置かない
    public static partial class BackdropBuilder
    {
        const string kKakizomeId = "Kakizome";
        const string kHanabiEngawaId = "HanabiEngawa";
        const string kDetailMeshDir = kBackdropDir + "/Meshes";
        const string kTextureDir = kBackdropDir + "/Textures";

        //========================================
        // 書き初めの座卓(Stage25)
        //========================================
        static void BuildKakizome(Ctx c)
        {
            float T = kTableTop;
            var d = new Detail(c);

            // テクスチャ
            var hanshiTex = d.Tex("Hanshi", 256, (u, v) =>
            {
                float n = Fbm(u * 8, v * 8, 8, 11) * 0.5f + Fbm(u * 32, v * 32, 32, 12) * 0.5f;
                float fiber = Mathf.Pow(Fbm(u * 6, v * 64, 6, 13), 3.0f);
                // 明るすぎると輪郭やエフェクトが飛ぶので、生成りの中くらいの明るさにする
                float k = 0.84f + (n - 0.5f) * 0.05f - fiber * 0.05f;
                return new Color(k, k * 0.985f, k * 0.95f);
            });
            var woodTex = d.Tex("ZatakuWood", 512, (u, v) => WoodGrain(u, v, 21, new Color(0.36f, 0.22f, 0.13f), new Color(0.24f, 0.14f, 0.08f)));
            var feltTex = d.Tex("Felt", 256, (u, v) =>
            {
                float n = Fbm(u * 64, v * 64, 64, 31) * 0.6f + Fbm(u * 8, v * 8, 8, 32) * 0.4f;
                return Color.Lerp(new Color(0.17f, 0.2f, 0.29f), new Color(0.25f, 0.28f, 0.38f), n);
            });
            var tatamiTex = d.Tex("Tatami", 256, (u, v) => Tatami(u, v, 41));
            var stoneTex = d.Tex("Suzuri", 256, (u, v) =>
            {
                float n = Fbm(u * 16, v * 16, 16, 51);
                float speck = Hash01(Mathf.FloorToInt(u * 256), Mathf.FloorToInt(v * 256), 52) > 0.97f ? 0.06f : 0.0f;
                float k = 0.16f + n * 0.07f + speck;
                return new Color(k, k * 1.02f, k * 1.06f);
            });
            var plasterTex = d.Tex("Plaster", 256, (u, v) =>
            {
                float n = Fbm(u * 12, v * 12, 12, 61) * 0.7f + Fbm(u * 48, v * 48, 48, 62) * 0.3f;
                return Color.Lerp(new Color(0.62f, 0.57f, 0.47f), new Color(0.72f, 0.67f, 0.56f), n);
            });
            var otehonTex = d.Tex("Otehon", 512, (u, v) => Calligraphy(u, v, 71, false));
            var kakejikuTex = d.Tex("Kakejiku", 512, (u, v) => Calligraphy(u, v, 81, true), false);

            // ステージの床(半紙)・壁(文鎮)・床の下の支え(半紙の束)
            // 太陽の光が強く当たると白く飛ぶので、色を少し落とす
            c.FloorMaterial = d.TexMat("StageFloor", hanshiTex, new Vector2(3, 2), 0.15f, new Color(0.88f, 0.87f, 0.84f));
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.2f, 0.19f, 0.18f), 0.75f);
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 0.5f, 0.9f, null,
                d.TexMat("PaperBundleA", hanshiTex, new Vector2(2, 2), 0.1f, new Color(0.98f, 0.97f, 0.94f)),
                d.TexMat("PaperBundleB", hanshiTex, new Vector2(2, 2), 0.1f, new Color(0.9f, 0.88f, 0.82f)),
                d.TexMat("PaperBundleC", hanshiTex, new Vector2(2, 2), 0.1f, new Color(0.94f, 0.92f, 0.87f)));

            var wood = d.TexMat("ZatakuWood", woodTex, new Vector2(3, 2), 0.55f, Color.white);
            var felt = d.TexMat("Felt", feltTex, new Vector2(6, 4), 0.05f, Color.white);
            var stone = d.TexMat("Suzuri", stoneTex, new Vector2(1, 1), 0.35f, Color.white);
            var ink = c.Mat("Ink", new Color(0.04f, 0.04f, 0.045f), 0.92f);
            var paperMat = d.TexMat("Hanshi", hanshiTex, new Vector2(1, 1), 0.1f, Color.white);

            // 座卓(角を丸めた天板と脚)・下敷き・畳の床
            {
                var g = c.Group("Zataku");
                d.Obj(g, "Top", d.RoundedBox("ZatakuTop", new Vector3(230, 5, 170), 1.6f, 4), new Vector3(0, T - 2.5f, 10), Quaternion.identity, Vector3.one, wood);
                for (int i = 0; i < 4; i++)
                {
                    float x = i % 2 == 0 ? -100 : 100, z = i < 2 ? -65 : 85;
                    d.Obj(g, $"Leg_{i}", d.RoundedBox("ZatakuLeg", new Vector3(10, 30, 10), 1.0f, 3), new Vector3(x, T - 20, z), Quaternion.identity, Vector3.one, wood);
                }
                d.Obj(g, "Shitajiki", d.RoundedBox("Shitajiki", new Vector3(64, 0.4f, 46), 0.15f, 2), new Vector3(0, T + 0.2f, 0), Quaternion.identity, Vector3.one, felt);
            }
            {
                float floorY = T - 37.5f;
                var g = c.Group("TatamiFloor");
                c.Box(g, "Tatami", new Vector3(0, floorY - 0.5f, 0), new Vector3(720, 1, 720), 0, d.TexMat("Tatami", tatamiTex, new Vector2(8, 16), 0.15f, Color.white));
                var edge = c.Mat("TatamiEdge", new Color(0.14f, 0.15f, 0.13f), 0.25f);
                for (int i = -4; i <= 4; i++)
                {
                    c.Box(g, $"EdgeX_{i}", new Vector3(0, floorY + 0.02f, i * 90 + 45), new Vector3(720, 0.05f, 2.2f), 0, edge);
                    for (int j = -2; j <= 2; j++)
                    {
                        float x = j * 180 + (i % 2 == 0 ? 0 : 90);
                        c.Box(g, $"EdgeZ_{i}_{j}", new Vector3(x, floorY + 0.05f, i * 90), new Vector3(2.2f, 0.05f, 90), 0, edge);
                    }
                }
            }

            // 硯(北西)と墨
            {
                var g = c.Anchored("Suzuri", new Vector2(-0.75f, 1), 12);
                d.Obj(g, "Body", d.RoundedBox("SuzuriBody", new Vector3(16, 3, 25), 1.0f, 4), new Vector3(0, T + 1.5f, 0), Quaternion.Euler(0, 12, 0), Vector3.one, stone);
                // 陸(墨をする所)と海(墨がたまる所)。上面より少し下げて、濡れたつやを出す
                var wet = c.Mat("SuzuriWet", new Color(0.07f, 0.07f, 0.08f), 0.85f);
                d.Obj(g, "Oka", d.RoundedBox("SuzuriOka", new Vector3(12, 0.3f, 15), 0.12f, 2), new Vector3(0.95f, T + 2.92f, -2.6f), Quaternion.Euler(0, 12, 0), Vector3.one, wet);
                d.Obj(g, "Umi", d.RoundedBox("SuzuriUmi", new Vector3(11, 0.3f, 4.6f), 0.12f, 2), new Vector3(-1.6f, T + 2.94f, 7.6f), Quaternion.Euler(0, 12, 0), Vector3.one, ink);
                // 墨(硯の東に寝かせる)。金の文字の帯はくすませる
                var sumi = d.Obj(g, "Sumi", d.RoundedBox("Sumi", new Vector3(2.4f, 1.3f, 10), 0.3f, 3), new Vector3(11, T + 0.65f, 1), Quaternion.Euler(0, -8, 0), Vector3.one, c.Mat("SumiStick", new Color(0.06f, 0.06f, 0.06f), 0.5f));
                d.Obj(sumi.transform, "Label", d.RoundedBox("SumiLabel", new Vector3(1.0f, 0.06f, 4.0f), 0.02f, 1), new Vector3(0, 0.66f, 0.5f), Quaternion.identity, Vector3.one, c.Mat("SumiGold", new Color(0.62f, 0.53f, 0.32f), 0.7f, 0.6f), true);
            }

            // 筆置きと筆(北東)。筆先はステージを指す
            {
                Vector2 dir = new Vector2(0.6f, 1).normalized;
                var g = c.Anchored("Fude", dir, 9);
                var celadon = c.Mat("Celadon", new Color(0.62f, 0.7f, 0.64f), 0.85f);
                var stand = d.Lathe("Fudeoki", new[] { new Vector2(0, 0), new Vector2(1.6f, 0), new Vector2(1.6f, 0.2f), new Vector2(1.2f, 1.2f), new Vector2(0.5f, 2.2f), new Vector2(0, 2.4f) }, 24);
                Vector3 axis = new Vector3(-dir.x, 0, -dir.y);
                Vector3 side = Vector3.Cross(Vector3.up, axis).normalized;
                d.Obj(g, "StandA", stand, new Vector3(0, T, 0) + side * 0 - axis * 4, Quaternion.identity, new Vector3(1.6f, 1, 1.0f), celadon);
                d.Obj(g, "StandB", stand, new Vector3(0, T, 0) + axis * 6, Quaternion.identity, new Vector3(1.6f, 1, 1.0f), celadon);
                // 筆: 竹の軸(節のふくらみ)・穂(白から先が黒)
                var bamboo = c.Mat("Bamboo", new Color(0.68f, 0.58f, 0.38f), 0.55f);
                var shaft = d.Lathe("FudeShaft", new[]
                {
                    new Vector2(0, 0), new Vector2(0.55f, 0), new Vector2(0.55f, 4.0f), new Vector2(0.62f, 4.3f), new Vector2(0.55f, 4.6f),
                    new Vector2(0.52f, 10.0f), new Vector2(0.6f, 10.3f), new Vector2(0.52f, 10.6f), new Vector2(0.5f, 17.5f), new Vector2(0, 17.6f),
                }, 20);
                var hair = d.Lathe("FudeHair", new[]
                {
                    new Vector2(0, 0), new Vector2(0.62f, 0), new Vector2(0.78f, 1.0f), new Vector2(0.7f, 2.6f), new Vector2(0.4f, 4.0f), new Vector2(0.08f, 5.0f), new Vector2(0, 5.1f),
                }, 20);
                var tip = d.Lathe("FudeTip", new[] { new Vector2(0, 0), new Vector2(0.6f, 0), new Vector2(0.42f, 1.0f), new Vector2(0.1f, 2.0f), new Vector2(0, 2.1f) }, 20);
                Quaternion lie = Quaternion.FromToRotation(Vector3.up, axis);
                Vector3 baseP = new Vector3(0, T + 2.35f, 0) - axis * 9.0f;
                d.Obj(g, "Shaft", shaft, baseP, lie, Vector3.one, bamboo);
                d.Obj(g, "Hair", hair, baseP + axis * 17.4f, lie, Vector3.one, c.Mat("FudeHair", new Color(0.86f, 0.82f, 0.72f), 0.2f));
                d.Obj(g, "Tip", tip, baseP + axis * 20.2f, lie, Vector3.one, ink);
            }

            // 水滴(東)
            {
                var g = c.Anchored("Suiteki", new Vector2(1, 0.3f), 8);
                var celadon = c.Mat("SuitekiGlaze", new Color(0.55f, 0.64f, 0.66f), 0.9f);
                d.Obj(g, "Body", d.Lathe("Suiteki", new[]
                {
                    new Vector2(0, 0), new Vector2(2.4f, 0), new Vector2(3.2f, 0.8f), new Vector2(3.4f, 2.0f), new Vector2(2.6f, 3.2f), new Vector2(0.6f, 3.7f), new Vector2(0.6f, 4.1f), new Vector2(0, 4.1f),
                }, 32), new Vector3(0, T, 0), Quaternion.identity, Vector3.one, celadon);
                d.Obj(g, "Spout", d.Lathe("SuitekiSpout", new[] { new Vector2(0, 0), new Vector2(0.45f, 0), new Vector2(0.3f, 2.0f), new Vector2(0, 2.0f) }, 12),
                    new Vector3(-2.7f, T + 2.4f, 0), Quaternion.Euler(0, 0, 55), Vector3.one, celadon);
            }

            // お手本(南西。平たい紙)と、書き終えた半紙(南東)
            {
                var g = c.Anchored("Otehon", new Vector2(-0.85f, -1), 6);
                d.Obj(g, "Sheet", d.RoundedBox("Sheet", new Vector3(20, 0.12f, 28), 0.04f, 1), new Vector3(0, T + 0.06f, 0), Quaternion.Euler(0, -10, 0), Vector3.one,
                    d.TexMat("Otehon", otehonTex, Vector2.one, 0.1f, Color.white));
            }
            {
                var g = c.Anchored("Written", new Vector2(1, -0.6f), 6);
                var sheet = d.RoundedBox("Sheet", new Vector3(20, 0.12f, 28), 0.04f, 1);
                var written = d.TexMat("Written", otehonTex, new Vector2(-1, 1), 0.1f, new Color(0.97f, 0.96f, 0.93f));
                for (int i = 0; i < 3; i++)
                {
                    d.Obj(g, $"Sheet_{i}", sheet, new Vector3(i * 3.0f, T + 0.06f + i * 0.13f, -i * 2.0f), Quaternion.Euler(0, 15 - i * 14, 0), Vector3.one, i == 2 ? written : paperMat);
                }
            }

            // 床の間(北の奥): 壁・掛け軸・三方に乗せた鏡餅
            {
                var g = c.Anchored("Tokonoma", new Vector2(0, 1), 70);
                float floorY = T - 37.5f;
                var plaster = d.TexMat("Plaster", plasterTex, new Vector2(4, 2), 0.05f, Color.white);
                c.Box(g, "Wall", new Vector3(0, floorY + 55, 12), new Vector3(150, 110, 2), 0, plaster);
                c.Box(g, "Toko", new Vector3(0, floorY + 3, 0), new Vector3(110, 6, 24), 0, wood);
                d.Obj(g, "Scroll", d.RoundedBox("Scroll", new Vector3(22, 60, 0.4f), 0.1f, 1), new Vector3(0, floorY + 58, 10.6f), Quaternion.identity, Vector3.one,
                    d.TexMat("Kakejiku", kakejikuTex, Vector2.one, 0.1f, Color.white));
                var rod = c.Mat("ScrollRod", new Color(0.18f, 0.12f, 0.08f), 0.6f);
                c.Prim(PrimitiveType.Cylinder, g, "RodTop", new Vector3(0, floorY + 88.5f, 10.4f), new Vector3(1.2f, 12.5f, 1.2f), Quaternion.Euler(0, 0, 90), rod);
                c.Prim(PrimitiveType.Cylinder, g, "RodBottom", new Vector3(0, floorY + 27.5f, 10.4f), new Vector3(1.6f, 13, 1.6f), Quaternion.Euler(0, 0, 90), rod);
                // 鏡餅(三方の上)。橙はくすませる
                var sanbo = c.Mat("Sanbo", new Color(0.78f, 0.68f, 0.5f), 0.35f);
                d.Obj(g, "Sanbo", d.RoundedBox("Sanbo", new Vector3(14, 6, 14), 0.3f, 2), new Vector3(30, floorY + 9, 0), Quaternion.identity, Vector3.one, sanbo);
                var mochi = c.Mat("Mochi", new Color(0.95f, 0.94f, 0.9f), 0.25f);
                var mochiMesh = d.Lathe("Mochi", new[] { new Vector2(0, 0), new Vector2(4.6f, 0), new Vector2(5.4f, 0.8f), new Vector2(5.2f, 2.2f), new Vector2(3.6f, 3.4f), new Vector2(0, 3.8f) }, 32);
                d.Obj(g, "MochiBottom", mochiMesh, new Vector3(30, floorY + 12, 0), Quaternion.identity, Vector3.one, mochi);
                d.Obj(g, "MochiTop", mochiMesh, new Vector3(30, floorY + 15.6f, 0), Quaternion.identity, new Vector3(0.75f, 0.8f, 0.75f), mochi);
                c.Sphere(g, "Daidai", new Vector3(30, floorY + 19.8f, 0), new Vector3(3.6f, 3.2f, 3.6f), c.Mat("Daidai", new Color(0.78f, 0.52f, 0.28f), 0.45f));
            }
            d.Save();
        }

        //========================================
        // 花火の夜の縁側(Stage26)
        //========================================
        static void BuildHanabiEngawa(Ctx c)
        {
            float T = kTableTop;
            var d = new Detail(c);

            var plankTex = d.Tex("Planks", 512, (u, v) => Planks(u, v, 5, 101));
            var boxTex = d.Tex("BoxPlanks", 256, (u, v) => Planks(u, v, 3, 111));
            var shojiTex = d.Tex("Shoji", 256, (u, v) =>
            {
                // 障子の桟(格子)と、明かりの透ける紙
                float gx = Mathf.Abs(Frac(u * 3) - 0.5f) * 2.0f, gy = Mathf.Abs(Frac(v * 4) - 0.5f) * 2.0f;
                bool frame = gx > 0.94f || gy > 0.95f;
                float n = Fbm(u * 16, v * 16, 16, 121);
                return frame ? new Color(0.36f, 0.27f, 0.18f) : Color.Lerp(new Color(0.88f, 0.8f, 0.64f), new Color(0.95f, 0.88f, 0.72f), n);
            });
            var groundTex = d.Tex("Garden", 256, (u, v) =>
            {
                float n = Fbm(u * 10, v * 10, 10, 131) * 0.6f + Fbm(u * 40, v * 40, 40, 132) * 0.4f;
                return Color.Lerp(new Color(0.06f, 0.08f, 0.06f), new Color(0.13f, 0.16f, 0.11f), n);
            });
            var uchiwaTex = d.Tex("Uchiwa", 256, (u, v) =>
            {
                // くすんだ藍の地に、白い波の模様
                float wave = Mathf.Sin((u * 4 + Mathf.Sin(v * 12.566f) * 0.15f) * 6.283f);
                float band = Mathf.Abs(Frac(v * 5 + wave * 0.06f) - 0.5f);
                return band < 0.06f ? new Color(0.86f, 0.86f, 0.82f) : new Color(0.24f, 0.3f, 0.42f);
            });
            var bagTex = d.Tex("HanabiBag", 256, (u, v) =>
            {
                float s = Frac((u + v) * 6);
                return s < 0.5f ? new Color(0.78f, 0.72f, 0.62f) : new Color(0.6f, 0.62f, 0.68f);
            });

            // ステージの床(縁側の板)・壁(柱)・床の下の支え(木の箱)
            c.FloorMaterial = d.TexMat("StageFloor", plankTex, new Vector2(2, 3), 0.3f, new Color(0.95f, 0.9f, 0.84f));
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.3f, 0.22f, 0.15f), 0.45f);
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 1.2f, 2.0f, null,
                d.TexMat("BoxA", boxTex, new Vector2(3, 2), 0.25f, new Color(0.62f, 0.5f, 0.38f)),
                d.TexMat("BoxB", boxTex, new Vector2(3, 2), 0.25f, new Color(0.52f, 0.41f, 0.3f)));

            // 夜の縁側(大きな板の間)と、暗い庭
            var nightBoards = d.TexMat("NightBoards", plankTex, new Vector2(6, 4), 0.35f, new Color(0.42f, 0.36f, 0.3f));
            {
                var g = c.Group("Engawa");
                d.Obj(g, "Boards", d.RoundedBox("Boards", new Vector3(230, 5, 120), 0.8f, 2), new Vector3(0, T - 2.5f, 25), Quaternion.identity, Vector3.one, nightBoards);
                // 縁側の縁(庭側の框)
                c.Box(g, "Kamachi", new Vector3(0, T - 1.2f, -35.5f), new Vector3(230, 7.6f, 3), 0, c.Mat("Kamachi", new Color(0.22f, 0.16f, 0.11f), 0.4f));
                c.Box(g, "Garden", new Vector3(0, T - 38, 0), new Vector3(720, 1, 720), 0, d.TexMat("Garden", groundTex, new Vector2(12, 12), 0.05f, Color.white));
                // 沓脱ぎ石
                d.Obj(g, "Kutsunugi", d.RoundedBox("Kutsunugi", new Vector3(26, 8, 12), 2.0f, 3), new Vector3(-20, T - 33, -50), Quaternion.Euler(0, 6, 0), Vector3.one,
                    c.Mat("Stone", new Color(0.28f, 0.28f, 0.27f), 0.3f));
            }

            // 明かりのともる障子(北の奥)と柱
            {
                var g = c.Anchored("Shoji", new Vector2(0, 1), 26);
                var shoji = d.TexMat("Shoji", shojiTex, new Vector2(6, 1), 0.1f, Color.white);
                shoji.EnableKeyword("_EMISSION");
                shoji.SetTexture("_EmissionMap", shojiTex);
                shoji.SetTextureScale("_EmissionMap", new Vector2(6, 1));
                shoji.SetColor("_EmissionColor", new Color(0.9f, 0.68f, 0.42f) * 0.75f);
                shoji.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                c.Box(g, "Shoji", new Vector3(0, T + 30, 0), new Vector3(200, 60, 1), 0, shoji);
                var post = c.Mat("Post", new Color(0.2f, 0.15f, 0.1f), 0.35f);
                for (int i = -2; i <= 2; i++) c.Box(g, $"Post_{i}", new Vector3(i * 50, T + 30, -1.5f), new Vector3(4, 60, 4), 0, post);
                c.Box(g, "Kamoi", new Vector3(0, T + 61, -1.5f), new Vector3(204, 3, 4), 0, post);
            }

            // 軒の電球(北東。ステージの上を暖かく照らす)
            {
                var g = c.Anchored("Bulb", new Vector2(0.55f, 1), 4);
                c.Box(g, "Cord", new Vector3(0, 26, 0), new Vector3(0.25f, 12, 0.25f), 0, c.Mat("Cord", new Color(0.1f, 0.1f, 0.1f), 0.3f));
                c.Cylinder(g, "Socket", new Vector3(0, 19.6f, 0), 0.7f, 1.4f, c.Mat("Socket", new Color(0.3f, 0.28f, 0.25f), 0.5f, 0.4f));
                c.Sphere(g, "Bulb", new Vector3(0, 18.2f, 0), new Vector3(2.0f, 2.4f, 2.0f), d.Emissive("Bulb", new Color(1.0f, 0.86f, 0.6f), 2.0f));
                d.PointLight(g, "BulbLight", new Vector3(0, 17.0f, 0), new Color(1.0f, 0.82f, 0.58f), 1.1f, 55);
            }

            // ろうそく(南西。花火に火をつける)
            {
                var g = c.Anchored("Candle", new Vector2(-0.9f, -0.8f), 6);
                var brass = c.Mat("Brass", new Color(0.55f, 0.46f, 0.3f), 0.7f, 0.8f);
                d.Obj(g, "Holder", d.Lathe("CandleHolder", new[]
                {
                    new Vector2(0, 0), new Vector2(3.0f, 0), new Vector2(3.0f, 0.3f), new Vector2(2.6f, 0.5f), new Vector2(0.8f, 0.8f), new Vector2(0.6f, 3.5f), new Vector2(1.6f, 3.9f), new Vector2(1.6f, 4.2f), new Vector2(0, 4.2f),
                }, 32), new Vector3(0, T, 0), Quaternion.identity, Vector3.one, brass);
                c.Cylinder(g, "Candle", new Vector3(0, T + 7.2f, 0), 0.9f, 6, c.Mat("Wax", new Color(0.9f, 0.88f, 0.82f), 0.4f));
                c.Sphere(g, "Flame", new Vector3(0, T + 11.0f, 0), new Vector3(0.7f, 1.4f, 0.7f), d.Emissive("Flame", new Color(1.0f, 0.82f, 0.5f), 3.0f));
                d.PointLight(g, "CandleLight", new Vector3(0, T + 12, 0), new Color(1.0f, 0.75f, 0.45f), 0.9f, 22);
            }

            // 蚊取り線香(西)
            {
                var g = c.Anchored("Katori", new Vector2(-1, 0.2f), 8);
                var path = new List<Vector3>();
                for (float a = 0; a < Mathf.PI * 2 * 3.2f; a += 0.12f)
                {
                    float r = 1.2f + a * 0.32f;
                    path.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
                }
                d.Obj(g, "Coil", d.Tube("KatoriCoil", path, 0.38f, 8), new Vector3(0, T + 2.3f, 0), Quaternion.identity, Vector3.one, c.Mat("Katori", new Color(0.24f, 0.32f, 0.22f), 0.2f));
                d.Obj(g, "Stand", d.Lathe("KatoriStand", new[] { new Vector2(0, 0), new Vector2(8.6f, 0), new Vector2(8.8f, 0.8f), new Vector2(8.4f, 1.0f), new Vector2(0, 1.0f) }, 40),
                    new Vector3(0, T, 0), Quaternion.identity, Vector3.one, c.Mat("KatoriTin", new Color(0.32f, 0.33f, 0.3f), 0.6f, 0.6f));
                c.Box(g, "Pin", new Vector3(0, T + 1.9f, 0), new Vector3(0.3f, 1.8f, 0.3f), 0, c.Mat("KatoriPin", new Color(0.4f, 0.4f, 0.38f), 0.6f, 0.8f));
                // 火の付いた先(小さく、暗めに)
                Vector3 end = path[path.Count - 1];
                c.Sphere(g, "Ember", end + new Vector3(0, T + 2.3f, 0), new Vector3(0.6f, 0.6f, 0.6f), d.Emissive("Ember", new Color(0.8f, 0.4f, 0.25f), 1.2f));
            }

            // うちわ(東)
            {
                var g = c.Anchored("Uchiwa", new Vector2(1, 0.25f), 8);
                var face = d.Obj(g, "Face", d.Lathe("UchiwaFace", new[] { new Vector2(0, 0), new Vector2(7.0f, 0), new Vector2(7.0f, 0.12f), new Vector2(0, 0.12f) }, 48),
                    new Vector3(0, T + 0.3f, 0), Quaternion.Euler(0, 20, 0), new Vector3(1, 1, 0.92f), d.TexMat("Uchiwa", uchiwaTex, Vector2.one, 0.25f, Color.white));
                c.Prim(PrimitiveType.Cylinder, g, "Handle", new Vector3(-8.2f, T + 0.3f, -3.0f), new Vector3(0.7f, 5.0f, 0.7f), Quaternion.Euler(0, 20, 90), c.Mat("UchiwaHandle", new Color(0.62f, 0.52f, 0.34f), 0.5f));
            }

            // すいかの皿(南東)
            {
                var g = c.Anchored("Suika", new Vector2(0.9f, -0.9f), 6);
                d.Obj(g, "Plate", d.Lathe("SuikaPlate", new[]
                {
                    new Vector2(0, 0), new Vector2(5.5f, 0), new Vector2(5.5f, 0.2f), new Vector2(8.4f, 1.0f), new Vector2(8.8f, 1.4f), new Vector2(8.3f, 1.4f), new Vector2(5.6f, 0.6f), new Vector2(0, 0.6f),
                }, 48), new Vector3(0, T, 0), Quaternion.identity, Vector3.one, c.Mat("Plate", new Color(0.9f, 0.9f, 0.87f), 0.85f));
                var rind = c.Mat("SuikaRind", new Color(0.2f, 0.3f, 0.2f), 0.5f);
                var white = c.Mat("SuikaWhite", new Color(0.82f, 0.84f, 0.72f), 0.4f);
                var flesh = c.Mat("SuikaFlesh", new Color(0.66f, 0.38f, 0.35f), 0.45f);
                for (int i = 0; i < 2; i++)
                {
                    float yaw = 20 + i * 150;
                    Vector3 p = new Vector3(i == 0 ? -1.5f : 1.8f, T + 0.65f, i == 0 ? 0.6f : -0.8f);
                    var rot = Quaternion.Euler(0, yaw, 0);
                    d.Obj(g, $"Rind_{i}", d.Sector("SuikaRind", 6.2f, 70, 1.6f), p, rot, Vector3.one, rind);
                    d.Obj(g, $"White_{i}", d.Sector("SuikaWhite", 5.7f, 70, 1.7f), p + Vector3.up * 0.02f, rot, Vector3.one, white);
                    d.Obj(g, $"Flesh_{i}", d.Sector("SuikaFlesh", 5.3f, 70, 1.8f), p + Vector3.up * 0.04f, rot, Vector3.one, flesh);
                }
            }

            // 花火の袋と手持ち花火(北西)
            {
                var g = c.Anchored("HanabiBag", new Vector2(-0.8f, 1), 8);
                d.Obj(g, "Bag", d.RoundedBox("HanabiBag", new Vector3(12, 1.2f, 16), 0.5f, 2), new Vector3(0, T + 0.6f, 0), Quaternion.Euler(0, -15, 0), Vector3.one,
                    d.TexMat("HanabiBag", bagTex, Vector2.one, 0.3f, Color.white));
                var paper = new[] { c.Mat("StickA", new Color(0.62f, 0.55f, 0.66f), 0.3f), c.Mat("StickB", new Color(0.55f, 0.62f, 0.6f), 0.3f), c.Mat("StickC", new Color(0.7f, 0.64f, 0.52f), 0.3f) };
                for (int i = 0; i < 5; i++)
                {
                    c.Prim(PrimitiveType.Cylinder, g, $"Stick_{i}", new Vector3(8 + i * 1.2f, T + 0.35f, -3 + i * 1.6f), new Vector3(0.6f, 7.0f, 0.6f), Quaternion.Euler(0, 30 + i * 8, 90), paper[i % paper.Length]);
                }
            }
            d.Save();
        }

        //========================================
        // 作り込み用の補助
        //========================================

        // 背景1つ分のメッシュ・テクスチャを作って保存する
        sealed class Detail
        {
            readonly Ctx m_c;
            readonly Mesh m_store;
            readonly Dictionary<string, Mesh> m_meshes = new Dictionary<string, Mesh>();

            public Detail(Ctx c)
            {
                m_c = c;
                EnsureFolder(kDetailMeshDir);
                EnsureFolder(kTextureDir);
                string path = $"{kDetailMeshDir}/{c.Id}.asset";
                m_store = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (m_store == null)
                {
                    m_store = new Mesh { name = c.Id };
                    AssetDatabase.CreateAsset(m_store, path);
                }
                else
                {
                    foreach (var sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                    {
                        if (sub == null || sub == m_store) continue;
                        AssetDatabase.RemoveObjectFromAsset(sub);
                        Object.DestroyImmediate(sub, true);
                    }
                }
            }

            public void Save()
            {
                EditorUtility.SetDirty(m_store);
                AssetDatabase.SaveAssets();
            }

            Mesh Keep(string name, Func<Mesh> build)
            {
                if (m_meshes.TryGetValue(name, out var cached)) return cached;
                Mesh mesh = build();
                mesh.name = name;
                mesh.RecalculateBounds();
                AssetDatabase.AddObjectToAsset(mesh, m_store);
                m_meshes[name] = mesh;
                return mesh;
            }

            // メッシュの描画物(当たり判定なし)。local でなければ位置はルート基準
            public GameObject Obj(Transform parent, string name, Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, Material mat, bool local = false)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                if (local)
                {
                    go.transform.localPosition = pos;
                    go.transform.localRotation = rot;
                }
                else
                {
                    go.transform.position = m_c.Root.TransformPoint(pos);
                    go.transform.rotation = rot;
                }
                go.transform.localScale = scale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.isStatic = true;
                return go;
            }

            public void PointLight(Transform parent, string name, Vector3 pos, Color color, float intensity, float range)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.position = m_c.Root.TransformPoint(pos);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.intensity = intensity;
                light.range = range;
                light.shadows = LightShadows.None;
            }

            public Material TexMat(string name, Texture2D tex, Vector2 tiling, float smoothness, Color tint)
            {
                var mat = m_c.Mat(name, tint, smoothness);
                mat.SetTexture("_BaseMap", tex);
                mat.SetTextureScale("_BaseMap", tiling);
                if (mat.HasProperty("_MainTex"))
                {
                    mat.SetTexture("_MainTex", tex);
                    mat.SetTextureScale("_MainTex", tiling);
                }
                EditorUtility.SetDirty(mat);
                return mat;
            }

            public Material Emissive(string name, Color color, float intensity)
            {
                var mat = m_c.Mat(name, color, 0.5f);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * intensity);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                EditorUtility.SetDirty(mat);
                return mat;
            }

            // つなぎ目の出ないテクスチャ(u, v は 0〜1)を PNG に保存して読み込む
            public Texture2D Tex(string name, int size, Func<float, float, Color> pixel, bool repeat = true)
            {
                var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        px[y * size + x] = pixel((x + 0.5f) / size, (y + 0.5f) / size);
                tex.SetPixels(px);
                tex.Apply();
                string path = $"{kTextureDir}/{m_c.Id}_{name}.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.sRGBTexture = true;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            // 回転体。profile は (半径, 高さ) を下から上へ。同じ点を2つ続けると、そこで角になる
            public Mesh Lathe(string name, Vector2[] profile, int segments) => Keep(name, () => BuildLathe(profile, segments));

            // 角を丸めた箱(中心が原点)
            public Mesh RoundedBox(string name, Vector3 size, float radius, int segments) => Keep($"{name}_{size.x}x{size.y}x{size.z}", () => BuildRoundedBox(size, radius, segments));

            // 扇形(原点が要。+Z を中心に angle 度)を上へ height 押し出す
            public Mesh Sector(string name, float radius, float angle, float height) => Keep(name, () =>
            {
                var poly = new List<Vector2> { Vector2.zero };
                int n = Mathf.Max(3, Mathf.CeilToInt(angle / 6));
                for (int i = 0; i <= n; i++)
                {
                    float a = (90 - angle * 0.5f + angle * i / n) * Mathf.Deg2Rad;
                    poly.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
                return BuildExtrude(poly, height);
            });

            // 点の列に沿った管
            public Mesh Tube(string name, List<Vector3> path, float radius, int sides) => Keep(name, () => BuildTube(path, radius, sides));
        }

        static Mesh BuildLathe(Vector2[] p, int seg)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float total = 0;
            var len = new float[p.Length];
            for (int i = 1; i < p.Length; i++)
            {
                total += Vector2.Distance(p[i], p[i - 1]);
                len[i] = total;
            }
            int ring = seg + 1;
            for (int i = 0; i < p.Length; i++)
            {
                // 輪郭の法線(隣の辺の平均。長さ0の辺は角なので使わない)
                Vector2 n = Vector2.zero;
                if (i > 0 && (p[i] - p[i - 1]).sqrMagnitude > 1e-8f) { Vector2 t = (p[i] - p[i - 1]).normalized; n += new Vector2(t.y, -t.x); }
                if (i < p.Length - 1 && (p[i + 1] - p[i]).sqrMagnitude > 1e-8f) { Vector2 t = (p[i + 1] - p[i]).normalized; n += new Vector2(t.y, -t.x); }
                n = n.sqrMagnitude > 1e-8f ? n.normalized : Vector2.up;
                for (int j = 0; j <= seg; j++)
                {
                    float a = j * Mathf.PI * 2 / seg;
                    float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                    verts.Add(new Vector3(cs * p[i].x, p[i].y, sn * p[i].x));
                    normals.Add(new Vector3(cs * n.x, n.y, sn * n.x).normalized);
                    uvs.Add(new Vector2((float)j / seg, total > 0 ? len[i] / total : 0));
                }
            }
            for (int i = 0; i < p.Length - 1; i++)
            {
                if ((p[i + 1] - p[i]).sqrMagnitude <= 1e-8f) continue;
                for (int j = 0; j < seg; j++)
                {
                    int a = i * ring + j, b = (i + 1) * ring + j;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            return mesh;
        }

        static Mesh BuildRoundedBox(Vector3 size, float radius, int seg)
        {
            Vector3 h = size * 0.5f;
            radius = Mathf.Min(radius, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.99f);
            float[] Coords(float half)
            {
                var list = new List<float>();
                for (int k = 0; k <= seg; k++) list.Add(-half + radius * k / Mathf.Max(1, seg));
                for (int k = seg; k >= 0; k--) list.Add(half - radius * k / Mathf.Max(1, seg));
                return list.ToArray();
            }
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            Vector3 inner = new Vector3(h.x - radius, h.y - radius, h.z - radius);
            float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            void Face(Vector3 n, Vector3 u, Vector3 v, float hu, float hv, float hn)
            {
                float[] cu = Coords(hu), cv = Coords(hv);
                int start = verts.Count;
                for (int i = 0; i < cu.Length; i++)
                {
                    for (int j = 0; j < cv.Length; j++)
                    {
                        Vector3 s = n * hn + u * cu[i] + v * cv[j];
                        Vector3 cl = new Vector3(Mathf.Clamp(s.x, -inner.x, inner.x), Mathf.Clamp(s.y, -inner.y, inner.y), Mathf.Clamp(s.z, -inner.z, inner.z));
                        Vector3 dir = s - cl;
                        Vector3 nn = dir.sqrMagnitude > 1e-10f ? dir.normalized : n;
                        verts.Add(cl + nn * radius);
                        normals.Add(nn);
                        uvs.Add(new Vector2(cu[i] / maxDim + 0.5f, cv[j] / maxDim + 0.5f));
                    }
                }
                int cols = cv.Length;
                for (int i = 0; i < cu.Length - 1; i++)
                {
                    for (int j = 0; j < cols - 1; j++)
                    {
                        int a = start + i * cols + j;
                        int b = a + cols;   // u の次
                        int c2 = a + 1;     // v の次
                        tris.Add(a); tris.Add(b); tris.Add(c2);
                        tris.Add(b); tris.Add(b + 1); tris.Add(c2);
                    }
                }
            }
            Face(Vector3.right, Vector3.up, Vector3.forward, h.y, h.z, h.x);
            Face(Vector3.left, Vector3.forward, Vector3.up, h.z, h.y, h.x);
            Face(Vector3.up, Vector3.forward, Vector3.right, h.z, h.x, h.y);
            Face(Vector3.down, Vector3.right, Vector3.forward, h.x, h.z, h.y);
            Face(Vector3.forward, Vector3.right, Vector3.up, h.x, h.y, h.z);
            Face(Vector3.back, Vector3.up, Vector3.right, h.y, h.x, h.z);
            var mesh = new Mesh();
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            return mesh;
        }

        // 凸多角形(XZ。上から見て反時計回り)を上へ押し出す。底面は y = 0
        static Mesh BuildExtrude(List<Vector2> poly, float height)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int n = poly.Count;
            // 上面
            int top = verts.Count;
            foreach (var p in poly) { verts.Add(new Vector3(p.x, height, p.y)); normals.Add(Vector3.up); uvs.Add(p * 0.1f); }
            for (int i = 1; i < n - 1; i++) { tris.Add(top); tris.Add(top + i + 1); tris.Add(top + i); }
            // 底面
            int bottom = verts.Count;
            foreach (var p in poly) { verts.Add(new Vector3(p.x, 0, p.y)); normals.Add(Vector3.down); uvs.Add(p * 0.1f); }
            for (int i = 1; i < n - 1; i++) { tris.Add(bottom); tris.Add(bottom + i); tris.Add(bottom + i + 1); }
            // 側面
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                Vector2 e = (b - a).normalized;
                Vector3 nn = new Vector3(e.y, 0, -e.x);
                int s = verts.Count;
                verts.Add(new Vector3(a.x, 0, a.y)); verts.Add(new Vector3(b.x, 0, b.y));
                verts.Add(new Vector3(a.x, height, a.y)); verts.Add(new Vector3(b.x, height, b.y));
                for (int k = 0; k < 4; k++) normals.Add(nn);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1));
                tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
                tris.Add(s + 1); tris.Add(s + 2); tris.Add(s + 3);
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            return mesh;
        }

        static Mesh BuildTube(List<Vector3> path, float radius, int sides)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int ring = sides + 1;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 t = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, t).normalized;
                if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
                Vector3 up = Vector3.Cross(t, side).normalized;
                for (int j = 0; j <= sides; j++)
                {
                    float a = j * Mathf.PI * 2 / sides;
                    Vector3 n = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    verts.Add(path[i] + n * radius);
                    normals.Add(n);
                    uvs.Add(new Vector2((float)j / sides, i * 0.1f));
                }
            }
            for (int i = 0; i < path.Count - 1; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = i * ring + j, b = (i + 1) * ring + j;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                }
            }
            var mesh = new Mesh();
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            return mesh;
        }

        //---------- テクスチャの模様 ----------

        static float Frac(float x) => x - Mathf.Floor(x);

        static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)int.MaxValue;
            }
        }

        // 周期 period(格子の数)で繰り返す値の雑音
        static float VNoise(float x, float y, int period, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            float ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy);
            int Wrap(int v) => ((v % period) + period) % period;
            float a = Hash01(Wrap(ix), Wrap(iy), seed), b = Hash01(Wrap(ix + 1), Wrap(iy), seed);
            float c = Hash01(Wrap(ix), Wrap(iy + 1), seed), d = Hash01(Wrap(ix + 1), Wrap(iy + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
        }

        static float Fbm(float x, float y, int period, int seed)
        {
            float v = 0, amp = 0.5f, sum = 0;
            for (int i = 0; i < 4; i++)
            {
                v += VNoise(x, y, period, seed + i * 17) * amp;
                sum += amp;
                x *= 2; y *= 2; period *= 2; amp *= 0.5f;
            }
            return v / sum;
        }

        // 木目(縦に流れる年輪の筋)
        static Color WoodGrain(float u, float v, int seed, Color light, Color dark)
        {
            float warp = Fbm(u * 4, v * 2, 4, seed) * 1.5f;
            float rings = Frac((u * 10 + warp * 2.2f + Fbm(u * 2, v * 1, 2, seed + 1) * 0.6f));
            float line = Mathf.Pow(Mathf.Abs(rings - 0.5f) * 2, 6);
            float fine = Fbm(u * 64, v * 8, 64, seed + 2);
            return Color.Lerp(light, dark, Mathf.Clamp01(line * 0.7f + fine * 0.35f));
        }

        // 板張り(横に並んだ板。板ごとに色と木目が少し違い、継ぎ目は暗い)
        static Color Planks(float u, float v, int count, int seed)
        {
            float pv = v * count;
            int plank = Mathf.FloorToInt(pv);
            float within = pv - plank;
            float shade = 0.85f + Hash01(plank, 0, seed) * 0.2f;
            // 床に使うので、木目は薄く(細かい模様の上では積み木が見えにくくなる)
            Color baseC = WoodGrain(Frac(u + Hash01(plank, 1, seed)), within / count + plank * 0.37f, seed + plank, new Color(0.64f, 0.51f, 0.37f), new Color(0.57f, 0.45f, 0.32f));
            float seam = within < 0.025f || within > 0.975f ? 0.7f : 1.0f;
            return baseC * shade * seam;
        }

        // 畳(い草の目と、ところどころの色むら)
        static Color Tatami(float u, float v, int seed)
        {
            float weave = 0.5f + 0.5f * Mathf.Sin(v * 256 * Mathf.PI);
            float n = Fbm(u * 8, v * 8, 8, seed);
            Color a = new Color(0.62f, 0.6f, 0.4f), b = new Color(0.7f, 0.68f, 0.47f);
            return Color.Lerp(a, b, weave * 0.6f + n * 0.4f);
        }

        // 書の紙(読める文字ではなく、太い筆の線を何本か)。vertical なら掛け軸(縦長・上下に表装)
        static Color Calligraphy(float u, float v, int seed, bool vertical)
        {
            float paperN = Fbm(u * 16, v * 16, 16, seed);
            Color paper = Color.Lerp(new Color(0.9f, 0.88f, 0.82f), new Color(0.95f, 0.93f, 0.88f), paperN);
            if (vertical && (v < 0.12f || v > 0.88f || u < 0.06f || u > 0.94f)) return new Color(0.42f, 0.4f, 0.32f);   // 表装の布
            // 筆の線: 乱数で決めた線分(太さが変わる)。点と線分の距離で塗る
            float ink = 0;
            for (int i = 0; i < 9; i++)
            {
                float cx = vertical ? 0.5f : 0.3f + 0.4f * (i % 3) / 2.0f;
                float cy = vertical ? 0.2f + 0.6f * i / 8.0f : 0.25f + 0.5f * (i / 3) / 2.0f;
                Vector2 a = new Vector2(cx + (Hash01(i, 1, seed) - 0.5f) * 0.18f, cy + (Hash01(i, 2, seed) - 0.5f) * 0.1f);
                Vector2 b = a + new Vector2((Hash01(i, 3, seed) - 0.5f) * 0.22f, (Hash01(i, 4, seed) - 0.5f) * 0.16f);
                Vector2 p = new Vector2(u, v);
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float w = Mathf.Lerp(0.028f, 0.012f, t) * (vertical ? 0.8f : 1.0f);
                float dist = Vector2.Distance(p, a + ab * t);
                float edge = Fbm(u * 64, v * 64, 64, seed + 5) * 0.008f;
                ink = Mathf.Max(ink, 1.0f - Mathf.Clamp01((dist - w + edge) / 0.004f));
            }
            return Color.Lerp(paper, new Color(0.06f, 0.06f, 0.07f), ink);
        }
    }
}
