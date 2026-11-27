using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nakagawa.EditorTools
{
    // 冬のこたつ(Stage22 こたつの全自動卓)の背景
    //  ・全自動卓(ステージ。卓の本体は StageBuilder.Dressing.cs の DressMahjongTable)を、大きなこたつの天板の上に置く
    //  ・試合のカメラ(高さ 31.5m・55°で見下ろす・視野角 25〜39°)に映るのは、卓の中心から 15〜40m の輪(主に左右と奥の上の隅)だけ。
    //    小物は天板の上の左右と奥に集め、壁・窓など遠くの物は作らない(作っても映らない)。手前は HUD に隠れるので置かない
    //  ・畳は奈落の見た目(高さ -12m の黒い板)より上に置く(下にすると黒い板に隠れる)
    //  ・色は StageDesignPitfalls.md に合わせ、彩度を低くする。布団は卓の緑のフェルトと見分けやすい藍ねずの格子
    //  ・天板の上の小物は器・道具など形のはっきりした物だけにする(みかん・せんべいなど食べ物は、作りで質感が出にくいので置かない)
    //    湯呑みの位置は AllStages で手直しした位置(東 (20.8, -6.76)・西 (-21.1, 9.8))
    public static partial class BackdropBuilder
    {
        const string kKotatsuId = "Kotatsu";
        // 天板の上面の高さ(卓の本体の下端)と、天板の半分の大きさ
        internal const float kKotatsuTop = -5.0f;
        internal const float kKotatsuHalf = 25.0f;
        const float kKotatsuBoardThick = 1.4f;
        // 天板の中央の切り欠きの半分の大きさ(卓のふた 9m の穴の井戸の外側 4.7m より少し大きく)
        const float kKotatsuWellHalf = 4.8f;
        // 畳の上面(奈落の見た目 -12m より上)
        const float kKotatsuFloor = -11.5f;

        static void BuildKotatsu(Ctx c)
        {
            float T = kKotatsuTop, B = kKotatsuHalf, F = kKotatsuFloor;
            var d = new Detail(c);

            //---------- テクスチャ ----------
            var boardTex = d.Tex("Board", 1024, KotatsuBoard, false);
            var frameWoodTex = d.Tex("FrameWood", 256, (u, v) => WoodGrain(u, v, 741, new Color(0.3f, 0.19f, 0.11f), new Color(0.2f, 0.12f, 0.07f)));
            var futonTex = d.Tex("Futon", 512, KotatsuFuton);
            var rugTex = d.Tex("Rug", 512, KotatsuRug, false);
            var tatamiTex = d.Tex("Tatami", 256, (u, v) => Tatami(u, v, 751));
            var memoTex = d.Tex("Memo", 512, ScoreMemo, false);

            // ステージの壁(縁は卓の本体の黒い樹脂で作り直すので、元の壁は描かない。念のため黒に合わせる)
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.06f, 0.062f, 0.068f), 0.8f);

            //---------- こたつの天板 ----------
            {
                var g = c.Group("KotatsuBoard");
                var side = d.TexMat("BoardSide", frameWoodTex, new Vector2(4, 1), 0.55f, Color.white);
                // 中央は卓の穴(洗牌の井戸)の下なので切り欠く(井戸の中に天板が見えないように)。4枚の板で囲む
                float hole = kKotatsuWellHalf;
                float y = T - kKotatsuBoardThick * 0.5f - 0.005f;
                float band = B - hole;
                d.Obj(g, "BodyN", d.RoundedBox("BoardNS", new Vector3(B * 2, kKotatsuBoardThick, band), 0.45f, 4), new Vector3(0, y, (B + hole) * 0.5f), Quaternion.identity, Vector3.one, side);
                d.Obj(g, "BodyS", d.RoundedBox("BoardNS", new Vector3(B * 2, kKotatsuBoardThick, band), 0.45f, 4), new Vector3(0, y, -(B + hole) * 0.5f), Quaternion.identity, Vector3.one, side);
                d.Obj(g, "BodyE", d.RoundedBox("BoardEW", new Vector3(band, kKotatsuBoardThick - 0.01f, hole * 2 + 0.9f), 0.45f, 4), new Vector3((B + hole) * 0.5f, y - 0.005f, 0), Quaternion.identity, Vector3.one, side);
                d.Obj(g, "BodyW", d.RoundedBox("BoardEW", new Vector3(band, kKotatsuBoardThick - 0.01f, hole * 2 + 0.9f), 0.45f, 4), new Vector3(-(B + hole) * 0.5f, y - 0.005f, 0), Quaternion.identity, Vector3.one, side);
                // 上面は1枚の絵(縁の帯・木目)。丸めた角の内側だけに貼り、中央は切り欠く
                d.Obj(g, "Top", d.Custom("BoardTop", () => FlatFrame(B - 0.45f, hole, B)),
                    new Vector3(0, T, 0), Quaternion.identity, Vector3.one, d.TexMat("BoardTop", boardTex, Vector2.one, 0.62f, Color.white));
            }

            //---------- こたつ布団(天板の下から畳へ垂れる)と、布団の縁の玉縁 ----------
            {
                var g = c.Group("KotatsuFuton");
                List<Vector3> hem = null;
                var futon = d.Custom("Futon", () => BuildKotatsuFuton(B - 0.4f, T - kKotatsuBoardThick - 0.05f, F, out hem));
                d.Obj(g, "Futon", futon, Vector3.zero, Quaternion.identity, Vector3.one, d.TexMat("Futon", futonTex, Vector2.one, 0.08f, Color.white));
                d.Obj(g, "Hem", d.Tube("FutonHem", hem, 0.3f, 10), Vector3.zero, Quaternion.identity, Vector3.one,
                    c.Mat("FutonHem", new Color(0.13f, 0.13f, 0.17f), 0.15f));
            }

            //---------- こたつ敷き(布団の外にのぞく敷物)と畳 ----------
            {
                var g = c.Group("Floor");
                d.Obj(g, "Rug", d.RoundedBox("Rug", new Vector3(96, 0.16f, 96), 0.08f, 2), new Vector3(0, F + 0.08f, 0), Quaternion.identity, Vector3.one,
                    d.TexMat("Rug", rugTex, Vector2.one, 0.05f, Color.white));
                c.Box(g, "Tatami", new Vector3(0, F - 0.5f, 0), new Vector3(432, 1, 432), 0, d.TexMat("Tatami", tatamiTex, new Vector2(8, 16), 0.15f, Color.white));
                // 畳の縁(27×54m の畳を互い違いに敷いた線。現実の 0.9×1.8m を 30 倍)
                var edge = c.Mat("TatamiEdge", new Color(0.16f, 0.18f, 0.15f), 0.25f);
                for (int i = -4; i <= 4; i++)
                {
                    c.Box(g, $"EdgeX_{i}", new Vector3(0, F + 0.02f, i * 54), new Vector3(432, 0.05f, 1.2f), 0, edge);
                    for (int j = -8; j <= 8; j++)
                    {
                        float x = j * 27 + (i % 2 == 0 ? 0 : 13.5f);
                        c.Box(g, $"EdgeZ_{i}_{j}", new Vector3(x, F + 0.05f, i * 54 - 27), new Vector3(1.2f, 0.05f, 54), 0, edge);
                    }
                }
            }

            //---------- 天板の上の小物(左右と奥。手前は HUD に隠れるので置かない) ----------
            var steamMat = SteamMaterial(c);

            // 湯呑みの形(高さ 3.6m)と茶托
            var yunomi = d.Lathe("Yunomi", new[]
            {
                new Vector2(0, 0), new Vector2(1.0f, 0), new Vector2(1.05f, 0.3f), new Vector2(1.0f, 0.36f), new Vector2(1.15f, 0.5f),
                new Vector2(1.45f, 1.6f), new Vector2(1.6f, 3.3f), new Vector2(1.62f, 3.52f), new Vector2(1.55f, 3.6f),
                new Vector2(1.45f, 3.55f), new Vector2(1.42f, 3.3f), new Vector2(1.3f, 1.5f), new Vector2(0, 0.75f),
            }, 40);
            var tea = d.Lathe("Tea", new[] { new Vector2(1.37f, 0), new Vector2(0, 0) }, 40);
            var chataku = d.Lathe("Chataku", new[]
            {
                new Vector2(0, 0), new Vector2(1.9f, 0), new Vector2(2.2f, 0.15f), new Vector2(2.25f, 0.32f),
                new Vector2(2.0f, 0.38f), new Vector2(1.3f, 0.24f), new Vector2(0, 0.24f),
            }, 40);
            var teaMat = c.Mat("Tea", new Color(0.4f, 0.44f, 0.18f), 0.85f);
            var lacquerBrown = c.Mat("LacquerBrown", new Color(0.24f, 0.13f, 0.08f), 0.75f);
            void Yunomi(string name, Vector3 pos, float yaw, Material glaze)
            {
                var g = Place(c, name, pos, yaw);
                d.Obj(g, "Chataku", chataku, Vector3.zero, Quaternion.identity, Vector3.one, lacquerBrown, true);
                d.Obj(g, "Cup", yunomi, new Vector3(0, 0.24f, 0), Quaternion.identity, Vector3.one, glaze, true);
                d.Obj(g, "Tea", tea, new Vector3(0, 0.24f + 2.95f, 0), Quaternion.identity, Vector3.one, teaMat, true);
                AddSteam(g, "Steam", new Vector3(0, 3.9f, 0), 1.0f, steamMat);
            }
            Yunomi("YunomiEast", new Vector3(20.8f, T, -6.76f), 0, c.Mat("GlazeShino", new Color(0.78f, 0.73f, 0.64f), 0.82f));
            Yunomi("YunomiWest", new Vector3(-21.1f, T, 9.8f), 0, c.Mat("GlazeTenmoku", new Color(0.27f, 0.2f, 0.16f), 0.88f));

            // 急須(北東。横手の常滑焼。注ぎ口は卓の方へ)
            {
                var g = Place(c, "Kyusu", new Vector3(20.6f, T, 17.6f), 200);
                var clay = c.Mat("Tokoname", new Color(0.5f, 0.25f, 0.16f), 0.6f);
                var clayDark = c.Mat("TokonameDark", new Color(0.36f, 0.17f, 0.11f), 0.55f);
                d.Obj(g, "Body", d.Lathe("KyusuBody", new[]
                {
                    new Vector2(0, 0), new Vector2(1.6f, 0), new Vector2(1.75f, 0.15f), new Vector2(2.45f, 1.0f), new Vector2(2.6f, 1.8f),
                    new Vector2(2.4f, 2.7f), new Vector2(1.9f, 3.3f), new Vector2(1.55f, 3.42f), new Vector2(0, 3.42f),
                }, 40), Vector3.zero, Quaternion.identity, Vector3.one, clay, true);
                d.Obj(g, "Lid", d.Lathe("KyusuLid", new[]
                {
                    new Vector2(1.62f, 0), new Vector2(1.64f, 0.12f), new Vector2(1.4f, 0.45f), new Vector2(0.6f, 0.7f), new Vector2(0.35f, 0.75f), new Vector2(0, 0.76f),
                }, 40), new Vector3(0, 3.38f, 0), Quaternion.identity, Vector3.one, clay, true);
                d.Obj(g, "Knob", d.Lathe("KyusuKnob", new[]
                {
                    new Vector2(0, 0), new Vector2(0.35f, 0), new Vector2(0.45f, 0.25f), new Vector2(0.4f, 0.55f), new Vector2(0.2f, 0.65f), new Vector2(0, 0.66f),
                }, 24), new Vector3(0, 4.1f, 0), Quaternion.identity, Vector3.one, clayDark, true);
                // 注ぎ口(胴の横から斜め上へ)
                d.Obj(g, "Spout", d.Tube("KyusuSpout", new List<Vector3>
                {
                    new Vector3(2.0f, 1.2f, 0), new Vector3(2.9f, 1.7f, 0), new Vector3(3.6f, 2.5f, 0), new Vector3(4.0f, 3.3f, 0), new Vector3(4.15f, 3.55f, 0),
                }, 0.4f, 14), Vector3.zero, Quaternion.identity, Vector3.one, clay, true);
                // 横手(注ぎ口と直角。少し上向き)
                Vector3 dir = new Vector3(0, 0.25f, -1).normalized;
                d.Obj(g, "Handle", d.Lathe("KyusuHandle", new[] { new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(0.42f, 4.1f), new Vector2(0.3f, 4.25f), new Vector2(0, 4.28f) }, 20),
                    new Vector3(0, 1.9f, 0) + dir * 2.2f, Quaternion.FromToRotation(Vector3.up, dir), Vector3.one, clayDark, true);
                AddSteam(g, "Steam", new Vector3(4.15f, 3.9f, 0), 0.7f, steamMat);
            }

            // 点数を書くメモと鉛筆(奥の西寄り)
            {
                var g = Place(c, "ScoreMemo", new Vector3(-7.4f, T, 20.6f), 6);
                d.Obj(g, "Pad", d.RoundedBox("MemoPad", new Vector3(5.5f, 0.3f, 7.5f), 0.06f, 2), new Vector3(0, 0.15f, 0), Quaternion.identity, Vector3.one,
                    d.TexMat("Memo", memoTex, Vector2.one, 0.1f, Color.white), true);
            }
            {
                var g = Place(c, "Pencil", new Vector3(-2.2f, T, 20.2f), -24);
                const float r = 0.22f, len = 6.2f;
                d.Obj(g, "Body", d.Tube("PencilBody", new List<Vector3> { new Vector3(0, 0, -len * 0.5f), new Vector3(0, 0, len * 0.5f) }, r, 6),
                    new Vector3(0, r, 0), Quaternion.identity, Vector3.one, c.Mat("PencilPaint", new Color(0.13f, 0.14f, 0.19f), 0.75f), true);
                Quaternion fwd = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
                d.Obj(g, "Wood", d.Lathe("PencilWood", new[] { new Vector2(r, 0), new Vector2(0.07f, 0.95f), new Vector2(0.065f, 0.95f) }, 12),
                    new Vector3(0, r, len * 0.5f), fwd, Vector3.one, c.Mat("PencilWood", new Color(0.78f, 0.62f, 0.44f), 0.3f), true);
                d.Obj(g, "Lead", d.Lathe("PencilLead", new[] { new Vector2(0.068f, 0), new Vector2(0, 0.22f) }, 10),
                    new Vector3(0, r, len * 0.5f + 0.94f), fwd, Vector3.one, c.Mat("PencilLead", new Color(0.12f, 0.12f, 0.13f), 0.6f), true);
                d.Obj(g, "Band", d.Lathe("PencilBand", new[] { new Vector2(r + 0.01f, 0), new Vector2(r + 0.01f, 0.08f) }, 12),
                    new Vector3(0, r, -len * 0.5f + 0.25f), fwd, Vector3.one, c.Mat("PencilGold", new Color(0.62f, 0.52f, 0.3f), 0.7f, 0.7f), true);
            }

            // テレビのリモコン(奥の東寄り)
            {
                var g = Place(c, "Remote", new Vector3(7.6f, T, 20.4f), -14);
                d.Obj(g, "Body", d.RoundedBox("RemoteBody", new Vector3(2.4f, 0.55f, 8.2f), 0.25f, 3), new Vector3(0, 0.275f, 0), Quaternion.identity, Vector3.one,
                    c.Mat("RemoteBody", new Color(0.11f, 0.115f, 0.12f), 0.55f), true);
                var key = d.RoundedBox("RemoteKey", new Vector3(0.5f, 0.12f, 0.36f), 0.05f, 2);
                var keyMat = c.Mat("RemoteKey", new Color(0.4f, 0.41f, 0.43f), 0.4f);
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 3; col++)
                        d.Obj(g, $"Key_{row}_{col}", key, new Vector3((col - 1) * 0.68f, 0.56f, -2.8f + row * 0.62f), Quaternion.identity, Vector3.one, keyMat, true);
                d.Obj(g, "Pad", d.Lathe("RemotePad", new[] { new Vector2(0, 0), new Vector2(0.62f, 0), new Vector2(0.62f, 0.1f), new Vector2(0, 0.12f) }, 24),
                    new Vector3(0, 0.53f, 1.15f), Quaternion.identity, Vector3.one, keyMat, true);
                d.Obj(g, "Power", key, new Vector3(0.62f, 0.56f, 3.2f), Quaternion.identity, Vector3.one, c.Mat("RemotePower", new Color(0.55f, 0.17f, 0.13f), 0.4f), true);
            }

            d.Save();
        }

        //========================================
        // こたつの補助
        //========================================

        // ルート基準の位置・向きに置いたまとまり(中の物は local で置く)
        static Transform Place(Ctx c, string name, Vector3 pos, float yaw)
        {
            var g = c.Group(name);
            g.localPosition = pos;
            g.localRotation = Quaternion.Euler(0, yaw, 0);
            return g;
        }

        // 上向きの四角い枠(中心が原点。外 ±outer・内 ±inner の間)。UV は ±uvHalf を 0〜1 にした上から見た位置
        static Mesh FlatFrame(float outer, float inner, float uvHalf)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            // 外の4点と内の4点(上から見て時計回り: 南西・北西・北東・南東)
            Vector2[] corners = { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) };
            foreach (var c in corners) verts.Add(new Vector3(c.x * outer, 0, c.y * outer));
            foreach (var c in corners) verts.Add(new Vector3(c.x * inner, 0, c.y * inner));
            foreach (var v in verts) uvs.Add(new Vector2((v.x + uvHalf) / (uvHalf * 2), (v.z + uvHalf) / (uvHalf * 2)));
            for (int i = 0; i < 4; i++)
            {
                int o0 = i, o1 = (i + 1) % 4, i0 = 4 + i, i1 = 4 + (i + 1) % 4;
                tris.Add(o0); tris.Add(o1); tris.Add(i1);
                tris.Add(o0); tris.Add(i1); tris.Add(i0);
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            return mesh;
        }

        // こたつ布団。天板の下(半分の大きさ inner の角を丸めた四角)から外へ出て、肩を丸めて垂れ、畳の上に広がる
        // 角は布が余って長く垂れ、ゆるいしわが寄る。UV は上から見た位置(9m で1回)。hem: 布団の外の縁(玉縁の通り道)
        static Mesh BuildKotatsuFuton(float inner, float innerY, float floorY, out List<Vector3> hem)
        {
            const float kCorner = 2.0f;      // 天板の下の角の丸み
            const float kDrapeStart = 0.8f;  // 天板の下から出て、垂れ始めるまで
            const float kDrapeEnd = 7.0f;    // 畳に着くまでの横の距離
            const float kThick = 0.5f;       // 畳の上の布団の厚さ
            float straight = inner - kCorner;

            // 周の点(南の辺の中央から反時計回り。上から見て)
            var pts = new List<Vector2>();
            var nrm = new List<Vector2>();
            void Line(Vector2 a, Vector2 b, Vector2 n)
            {
                int k = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 0.6f));
                for (int i = 0; i < k; i++) { pts.Add(Vector2.Lerp(a, b, (float)i / k)); nrm.Add(n); }
            }
            void Arc(Vector2 center, float from, float to)
            {
                const int k = 14;
                for (int i = 0; i < k; i++)
                {
                    float a = Mathf.Lerp(from, to, (float)i / k) * Mathf.Deg2Rad;
                    var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    pts.Add(center + n * kCorner); nrm.Add(n);
                }
            }
            Line(new Vector2(0, -inner), new Vector2(straight, -inner), Vector2.down);
            Arc(new Vector2(straight, -straight), -90, 0);
            Line(new Vector2(inner, -straight), new Vector2(inner, straight), Vector2.right);
            Arc(new Vector2(straight, straight), 0, 90);
            Line(new Vector2(straight, inner), new Vector2(-straight, inner), Vector2.up);
            Arc(new Vector2(-straight, straight), 90, 180);
            Line(new Vector2(-inner, straight), new Vector2(-inner, -straight), Vector2.left);
            Arc(new Vector2(-straight, -straight), 180, 270);
            Line(new Vector2(-straight, -inner), new Vector2(0, -inner), Vector2.down);

            // 周に沿った長さと、角(円弧の中央)からの近さ
            int cols = pts.Count;
            var along = new float[cols + 1];
            for (int i = 1; i <= cols; i++) along[i] = along[i - 1] + Vector2.Distance(pts[i - 1], pts[i % cols]);
            float perimeter = along[cols];
            var cornerMid = new List<float>();
            float quarter = perimeter / 4;
            for (int q = 0; q < 4; q++) cornerMid.Add(quarter * 0.5f + q * quarter);
            float CornerWeight(float t)
            {
                float best = float.MaxValue;
                foreach (var m in cornerMid)
                {
                    float dd = Mathf.Abs(t - m);
                    dd = Mathf.Min(dd, perimeter - dd);
                    best = Mathf.Min(best, dd);
                }
                return Mathf.Exp(-(best / 6.0f) * (best / 6.0f));
            }

            // 外への距離の段(垂れる所は細かく、畳の上は粗く、縁は丸める)
            var drape = new List<float> { 0, 0.4f, kDrapeStart };
            for (int i = 1; i <= 12; i++) drape.Add(kDrapeStart + (kDrapeEnd - kDrapeStart) * i / 12f);
            const int kFlatRows = 7;
            int rows = drape.Count + kFlatRows + 3;

            // しわの波は、1周でちょうど整数回になる長さにする(継ぎ目で形がずれないように)
            float Wave(float t, float approx, float phase)
            {
                float cycles = Mathf.Max(1, Mathf.Round(perimeter / approx));
                return Mathf.Sin(t / perimeter * cycles * Mathf.PI * 2 + phase);
            }

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            hem = new List<Vector3>();
            for (int i = 0; i <= cols; i++)
            {
                int pi = i % cols;
                float t = along[i];
                float w = CornerWeight(t);
                // しわ(周に沿った波。角ほど細かく強い)
                float fold = 0.55f * Wave(t, 7.3f, 0.6f) + 0.35f * Wave(t, 3.1f, 2.1f) + w * 0.9f * Wave(t, 1.9f, 0.0f);
                float noise = Fbm(t / perimeter * 24, 0.5f, 24, 771) - 0.5f;
                float length = 13.0f + w * 3.5f + noise * 2.4f;
                Vector2 p0 = pts[pi], n = nrm[pi];

                for (int k = 0; k < rows; k++)
                {
                    float s;
                    if (k < drape.Count) s = drape[k];
                    else if (k < drape.Count + kFlatRows) s = kDrapeEnd + (length - 1.0f - kDrapeEnd) * (k - drape.Count + 1) / kFlatRows;
                    else s = length - 1.0f + new[] { 0.45f, 0.8f, 1.0f }[k - drape.Count - kFlatRows];

                    // 高さ: 天板の下 → 肩を丸めて垂れる → 畳の上(厚さ) → 縁で丸く下がる
                    float y;
                    if (s <= kDrapeStart) y = innerY;
                    else if (s <= kDrapeEnd)
                    {
                        float q = (s - kDrapeStart) / (kDrapeEnd - kDrapeStart);
                        y = Mathf.Lerp(innerY, floorY + kThick, q * q * (3 - 2 * q));
                    }
                    else
                    {
                        float e = Mathf.Clamp01((length - s) / 1.0f);
                        y = floorY + 0.12f + (kThick - 0.12f) * Mathf.Sqrt(e);
                    }
                    // しわの強さ(天板の下ではなし。垂れる所から効かせる)
                    float amp = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.2f, 4.0f, s));
                    float flat = Mathf.InverseLerp(kDrapeEnd - 1.0f, kDrapeEnd + 2.0f, s);
                    float out_ = s + fold * amp * 0.55f;
                    y += fold * amp * Mathf.Lerp(0.05f, 0.16f, flat);
                    Vector2 p = p0 + n * out_;
                    verts.Add(new Vector3(p.x, y, p.y));
                    uvs.Add(new Vector2(p.x / 9.0f, p.y / 9.0f));
                    if (k == rows - 1) hem.Add(new Vector3(p.x, floorY + 0.26f, p.y));
                }
            }

            var tris = new List<int>();
            for (int i = 0; i < cols; i++)
            {
                for (int k = 0; k < rows - 1; k++)
                {
                    int a = i * rows + k, b = (i + 1) * rows + k;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(b); tris.Add(b + 1); tris.Add(a + 1);
                }
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            // 1周の継ぎ目(最初と最後の列は同じ点)の法線をそろえる
            var normals = new List<Vector3>();
            mesh.GetNormals(normals);
            for (int k = 0; k < rows; k++)
            {
                Vector3 avg = (normals[k] + normals[cols * rows + k]).normalized;
                normals[k] = avg;
                normals[cols * rows + k] = avg;
            }
            mesh.SetNormals(normals);
            mesh.RecalculateTangents();
            return mesh;
        }

        // 湯気(ゆっくり立ちのぼって消える、白く淡い煙)
        static void AddSteam(Transform parent, string name, Vector3 localPos, float scale, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.rotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.duration = 4.0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.2f, 4.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * scale, 0.9f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f * scale, 1.5f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0.0f, Mathf.PI * 2.0f);
            main.startColor = new Color(1, 1, 1, 1);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.rateOverTime = 4.0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8.0f;
            shape.radius = 0.6f * scale;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.EaseInOut(0.0f, 0.6f, 1.0f, 2.6f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
                new[] { new GradientAlphaKey(0.0f, 0.0f), new GradientAlphaKey(0.22f, 0.25f), new GradientAlphaKey(0.0f, 1.0f) });
            col.color = gradient;

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.4f;
            noise.scrollSpeed = 0.3f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // 湯気のマテリアル(半透明。ふちのぼけた丸の絵)
        static Material SteamMaterial(Ctx c)
        {
            string texPath = $"{kTextureDir}/{c.Id}_SteamPuff.png";
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2 - 1, v = (y + 0.5f) / size * 2 - 1;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float n = Fbm((x + 0.5f) / size * 4, (y + 0.5f) / size * 4, 4, 781);
                    float a = Mathf.Clamp01(1 - r) * Mathf.Clamp01(1 - r) * (0.6f + n * 0.8f);
                    px[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            var puff = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            var mat = GetMaterial(c.Id, "Steam", Shader.Find("Universal Render Pipeline/Particles/Unlit"), Color.white, 0, 0);
            mat.SetFloat("_Surface", 1.0f);
            mat.SetFloat("_Blend", 0.0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0.0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetTexture("_BaseMap", puff);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        //---------- こたつの模様 ----------

        // 天板の上面(1枚の絵)。縁の帯は濃い色で長い向きに木目が流れ、角は斜めに合わせる。内側は明るい木目の板
        static Color KotatsuBoard(float u, float v)
        {
            const float kFrame = 0.07f;
            float eu = Mathf.Min(u, 1 - u), ev = Mathf.Min(v, 1 - v);
            float e = Mathf.Min(eu, ev);
            Color c;
            if (e < kFrame)
            {
                c = eu < ev
                    ? WoodGrain(u * 3.0f, v, 721, new Color(0.31f, 0.2f, 0.12f), new Color(0.21f, 0.13f, 0.08f))
                    : WoodGrain(v * 3.0f, u, 722, new Color(0.31f, 0.2f, 0.12f), new Color(0.21f, 0.13f, 0.08f));
                // 角の斜めの合わせ目
                if (Mathf.Abs(eu - ev) < 0.0016f) c *= 0.7f;
            }
            else
            {
                c = WoodGrain(u * 1.4f, v, 731, new Color(0.5f, 0.35f, 0.22f), new Color(0.39f, 0.26f, 0.15f));
            }
            // 縁と板の間の溝(暗い線と、内側の細い明るい線)
            float g = Mathf.Abs(e - kFrame);
            if (g < 0.0022f) c *= 0.5f;
            else if (e > kFrame && g < 0.0045f) c *= 1.06f;
            // ゆるいつやむら
            float n = Fbm(u * 5, v * 5, 5, 732);
            return c * (0.95f + n * 0.08f);
        }

        // こたつ布団の柄(藍ねずの地に、明るい帯と生成りの細い二本線の格子。綾織りの目)
        static Color KotatsuFuton(float u, float v)
        {
            Color baseC = new Color(0.2f, 0.215f, 0.29f);
            Color band = new Color(0.28f, 0.295f, 0.37f);
            Color line = new Color(0.66f, 0.6f, 0.48f);
            float Band(float x, float center, float half) => Mathf.Clamp01((half - Mathf.Abs(Frac(x - center + 0.5f) - 0.5f)) * 80.0f);
            float Line(float x, float center, float half) => Mathf.Clamp01((half - Mathf.Abs(Frac(x - center + 0.5f) - 0.5f)) * 600.0f);
            Color c = baseC;
            float bu = Band(u, 0.25f, 0.1f), bv = Band(v, 0.25f, 0.1f);
            c = Color.Lerp(c, band, bu * 0.85f);
            c = Color.Lerp(c, band, bv * 0.85f);
            // 帯の重なりは少し明るく
            c *= 1.0f + bu * bv * 0.06f;
            // 帯の中の二本線と、地の中央の細い暗い線
            float lines = Mathf.Max(Mathf.Max(Line(u, 0.225f, 0.005f), Line(u, 0.275f, 0.005f)), Mathf.Max(Line(v, 0.225f, 0.005f), Line(v, 0.275f, 0.005f)));
            c = Color.Lerp(c, line, lines * 0.8f);
            float dark = Mathf.Max(Line(u, 0.75f, 0.004f), Line(v, 0.75f, 0.004f));
            c = Color.Lerp(c, new Color(0.12f, 0.12f, 0.17f), dark * 0.7f);
            // 綾織りの斜めの目と、布のむら
            float twill = Mathf.Sin((u + v) * Mathf.PI * 2 * 96);
            float n = Fbm(u * 16, v * 16, 16, 761);
            return c * (0.97f + twill * 0.025f + (n - 0.5f) * 0.06f);
        }

        // こたつ敷き(1枚の絵)。生成りがかった茶の地に、濃い縁の帯と細い線、毛足のむら
        static Color KotatsuRug(float u, float v)
        {
            float e = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
            Color c = new Color(0.46f, 0.39f, 0.3f);
            if (e < 0.05f) c = new Color(0.3f, 0.23f, 0.17f);
            else if (e < 0.058f) c = new Color(0.56f, 0.49f, 0.38f);
            float pile = Fbm(u * 128, v * 128, 128, 791);
            float cloud = Fbm(u * 6, v * 6, 6, 792);
            return c * (0.92f + pile * 0.1f + (cloud - 0.5f) * 0.06f);
        }

        // 点数のメモ(1枚の絵)。u が紙の縦(上端が糊付けの帯)、v が横。罫線と4人分の列、鉛筆の走り書き
        static Color ScoreMemo(float u, float v)
        {
            Color paper = new Color(0.86f, 0.85f, 0.8f) * (0.97f + Fbm(u * 16, v * 16, 16, 831) * 0.05f);
            if (u > 0.93f) return new Color(0.32f, 0.26f, 0.22f);
            const float vMin = 0.14f, vMax = 0.86f;
            if (v < vMin || v > vMax) return paper;
            Color c = paper;
            Color rule = new Color(0.55f, 0.6f, 0.68f);
            // 横の罫線
            float row = Frac(u / 0.055f);
            if (row < 0.06f) c = Color.Lerp(c, rule, 0.6f);
            // 4人分の縦の線
            float col = (v - vMin) / (vMax - vMin) * 4;
            if (Mathf.Abs(col - Mathf.Round(col)) < 0.012f) c = Color.Lerp(c, rule, 0.7f);
            // 見出しの下の太い線
            if (Mathf.Abs(u - 0.83f) < 0.004f) c = Color.Lerp(c, new Color(0.3f, 0.3f, 0.32f), 0.8f);
            // 走り書き(行ごとの列の中に、短いぎざぎざの線)
            int ri = Mathf.FloorToInt(u / 0.055f), ci = Mathf.FloorToInt(col);
            if (u < 0.82f && ri >= 3 && Hash01(ri, ci, 832) > 0.25f)
            {
                float lx = Frac(col) , ly = row;
                float start = 0.2f + Hash01(ri, ci, 833) * 0.15f, end = 0.55f + Hash01(ri, ci, 834) * 0.25f;
                if (lx > start && lx < end)
                {
                    float wave = 0.5f + 0.28f * Mathf.Sin(lx * 60 + ri * 3 + ci);
                    if (Mathf.Abs(ly - wave) < 0.09f) c = Color.Lerp(c, new Color(0.25f, 0.25f, 0.27f), 0.85f);
                }
            }
            return c;
        }
    }
}
