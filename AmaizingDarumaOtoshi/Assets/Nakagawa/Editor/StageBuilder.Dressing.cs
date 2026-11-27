using System;
using System.Collections.Generic;
using Nakahira;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Nakagawa.EditorTools
{
    // ステージ本体の見た目の作り込み(枠の飾り・表示・照明など)。当たり判定は足さず、元の壁の当たり判定・跳ね返りはそのまま使う
    //  ・壁の見た目を置き換えるときは、元の壁の描画だけを消して、角を丸めた箱などを重ねる
    //  ・作ったメッシュはステージごとに1つのアセット(Assets/Nakagawa/Meshes/<ステージ>_Dress.asset)にまとめる
    //  ・色は StageDesignPitfalls.md に合わせ、プレイヤーの4色・ギミックの色(橙=加速・青緑=減速・黄=予兆)を大きな面に使わない
    public static partial class StageBuilder
    {
        const string kDressRootName = "Dress";

        partial class Ctx
        {
            MeshAssetStore m_dressStore;

            // 作り込みの物を入れる親(ステージの「Stage」の下)
            Transform DressRoot()
            {
                var t = Stage.Find(kDressRootName);
                if (t != null) return t;
                t = new GameObject(kDressRootName).transform;
                t.SetParent(Stage, false);
                return t;
            }

            MeshAssetStore DressStore() => m_dressStore ??= new MeshAssetStore($"{kMeshDir}/{Id}_Dress.asset");

            Mesh Keep(Mesh mesh, string name)
            {
                mesh.name = name;
                DressStore().Add(mesh);
                return mesh;
            }

            void SaveDress() => m_dressStore?.Save();

            //----------------------------------------
            // Stage22 こたつの全自動卓
            //----------------------------------------

            // 卓の本体を、全自動卓(上だけ。支柱と脚はなし)の形で作る。卓はこたつの天板(背景 Kotatsu)の上に置く
            //  ・縁: 黒い樹脂(元の壁の当たり判定・跳ね返りはそのまま。見た目だけ置き換える)
            //  ・上の本体: 縁の下から外へ少し張り出した、角の丸い黒い胴。四隅は丸く張り出した耳(上にカップ置きのくぼみ)
            //  ・横のトレー: 各辺の胴から張り出した、すのこ状の小物入れ
            //  ・下の本体: 床の下からこたつの天板まで。中央の穴(ふた)のまわりだけの枠にして、穴の中は暗い井戸にする
            //  ・各辺の中央に点数表示(赤い LED)とプレイヤー表記、右寄りに点棒入れ
            // 試合のカメラからは、胴や耳の側面はほとんど見えない(横は裏側、手前は HUD の下)。見える上面の形を作り込む
            // w×d は遊べる範囲(壁の内側)。壁の厚さは 1m(Mahjong と同じ)。lid はふた(穴)の一辺
            public void DressMahjongTable(float w, float d, float lid)
            {
                var root = DressRoot();
                HideWallRenderers(Stage);

                float hx = w * 0.5f, hz = d * 0.5f;
                const float kOuter = 1.0f;    // 壁の厚さ
                // まとめたシーンでは、壁は見える高さ(1m)と、その上の見えない壁に分けられる。見える高さに合わせる
                float wall = StageWallSplitter.kVisibleHeight;
                float ox = hx + kOuter, oz = hz + kOuter;   // 縁の外側
                const float kShellOut = 1.0f;               // 縁の外へ張り出す胴の幅
                const float kShellTop = 0.3f, kShellBottom = -2.4f;
                float bottom = BackdropBuilder.kKotatsuTop + 0.02f;   // こたつの天板の少し上

                // 黒い樹脂(縁は壁用の透けるシェーダー、胴はふつうの Lit)。真っ黒にせず、つやで形が分かるようにする
                var rail = BackdropBuilder.StageMaterial("RexxRail", new Color(0.075f, 0.078f, 0.085f), 0.82f, 0, null, null, true);
                var shell = BackdropBuilder.StageMaterial("RexxShell", new Color(0.075f, 0.078f, 0.085f), 0.8f);
                var matte = BackdropBuilder.StageMaterial("RexxMatte", new Color(0.04f, 0.041f, 0.045f), 0.3f);
                var rubber = BackdropBuilder.StageMaterial("RexxRubber", new Color(0.025f, 0.025f, 0.028f), 0.1f);
                var slat = BackdropBuilder.StageMaterial("RexxSlat", new Color(0.13f, 0.135f, 0.145f), 0.55f, 0.3f);
                var well = BackdropBuilder.StageMaterial("RexxWell", new Color(0.05f, 0.052f, 0.058f), 0.25f);
                var rib = BackdropBuilder.StageMaterial("RexxWellRib", new Color(0.11f, 0.115f, 0.125f), 0.5f, 0.4f);

                // 縁: 南北は角まで、東西は南北の間だけ(重なりを作らない)。角は四隅の耳で隠れる
                RailPiece(root, "RailN", new Vector3(0, 0, hz + kOuter * 0.5f), new Vector3(ox * 2, wall, kOuter), 0.28f, rail, 4);
                RailPiece(root, "RailS", new Vector3(0, 0, -hz - kOuter * 0.5f), new Vector3(ox * 2, wall, kOuter), 0.28f, rail, 4);
                RailPiece(root, "RailE", new Vector3(hx + kOuter * 0.5f, 0, 0), new Vector3(kOuter, wall - 0.012f, hz * 2), 0.28f, rail, 4);
                RailPiece(root, "RailW", new Vector3(-hx - kOuter * 0.5f, 0, 0), new Vector3(kOuter, wall - 0.012f, hz * 2), 0.28f, rail, 4);

                // 上の本体(縁の下から外へ張り出す胴)。遊べる範囲(床)の上には出さないよう、内側は壁の下から
                float inner = hz + 0.05f, outer = ox + kShellOut;
                float depth = outer - inner, height = kShellTop - kShellBottom, midY = (kShellTop + kShellBottom) * 0.5f;
                ShellPiece(root, "ShellN", new Vector3(0, midY, (inner + outer) * 0.5f), new Vector3(outer * 2, height, depth), 0.55f, shell);
                ShellPiece(root, "ShellS", new Vector3(0, midY, -(inner + outer) * 0.5f), new Vector3(outer * 2, height, depth), 0.55f, shell);
                // 東西は少し低く・少し内側までにして、南北と重なる所で同じ平面の面ができないようにする
                ShellPiece(root, "ShellE", new Vector3((inner + outer - 0.02f) * 0.5f, midY - 0.006f, 0), new Vector3(depth - 0.02f, height - 0.012f, (oz - 0.2f) * 2), 0.55f, shell);
                ShellPiece(root, "ShellW", new Vector3(-(inner + outer - 0.02f) * 0.5f, midY - 0.006f, 0), new Vector3(depth - 0.02f, height - 0.012f, (oz - 0.2f) * 2), 0.55f, shell);

                // 下の本体(床の下からこたつの天板まで)。中央の穴のまわりだけの枠
                float lh = lid * 0.5f + 0.25f, lo = ox + 0.3f;
                float lowTop = -1.05f, lowH = lowTop - bottom, lowY = (lowTop + bottom) * 0.5f;
                ShellPiece(root, "BodyN", new Vector3(0, lowY, (lh + lo) * 0.5f), new Vector3(lo * 2, lowH, lo - lh), 0.3f, matte);
                ShellPiece(root, "BodyS", new Vector3(0, lowY, -(lh + lo) * 0.5f), new Vector3(lo * 2, lowH, lo - lh), 0.3f, matte);
                ShellPiece(root, "BodyE", new Vector3((lh + lo) * 0.5f, lowY, 0), new Vector3(lo - lh, lowH - 0.01f, lh * 2 - 0.02f), 0.3f, matte);
                ShellPiece(root, "BodyW", new Vector3(-(lh + lo) * 0.5f, lowY, 0), new Vector3(lo - lh, lowH - 0.01f, lh * 2 - 0.02f), 0.3f, matte);

                // 四隅の耳(丸く張り出した角。上にカップ置きのくぼみ)
                var podMesh = Keep(BackdropBuilder.NewLathe(new[]
                {
                    new Vector2(0, kShellBottom), new Vector2(2.3f, kShellBottom), new Vector2(2.6f, kShellBottom + 0.6f), new Vector2(2.6f, 0.4f),
                    new Vector2(2.5f, 0.85f), new Vector2(2.25f, 1.06f), new Vector2(1.8f, 1.12f), new Vector2(0.95f, 1.12f), new Vector2(0.95f, 1.12f),
                    new Vector2(0.88f, 0.72f), new Vector2(0.88f, 0.72f), new Vector2(0, 0.72f),
                }, 48), "CornerPod");
                for (int i = 0; i < 4; i++)
                {
                    float sx = i % 2 == 0 ? -1 : 1, sz = i < 2 ? -1 : 1;
                    var pod = MeshObject(root, $"CornerPod_{i}", podMesh, new Vector3(sx * (ox + 1.2f), 0, sz * (oz + 1.2f)), Quaternion.identity, shell);
                    Primitive(PrimitiveType.Cylinder, pod.transform, "CupMat", new Vector3(0, 0.73f, 0), new Vector3(1.74f, 0.01f, 1.74f), rubber);
                }

                // 各辺の席(外から卓を見る向き): 南=1P・東=2P・北=3P・西=4P(東南西北の順に反時計回り)。席の外向きは -Z
                var seats = new[]
                {
                    (yaw: 0f, edge: new Vector3(0, 0, -hz - kOuter * 0.5f)),
                    (yaw: -90f, edge: new Vector3(hx + kOuter * 0.5f, 0, 0)),
                    (yaw: 180f, edge: new Vector3(0, 0, hz + kOuter * 0.5f)),
                    (yaw: 90f, edge: new Vector3(-hx - kOuter * 0.5f, 0, 0)),
                };
                var housing = BackdropBuilder.StageMaterial("MahjongPanel", new Color(0.05f, 0.052f, 0.058f), 0.85f);
                var screen = BackdropBuilder.StageMaterial("MahjongScreen", new Color(0.03f, 0.012f, 0.012f), 0.95f);
                var segOff = BackdropBuilder.StageMaterial("MahjongSegmentOff", new Color(0.07f, 0.035f, 0.035f), 0.6f);
                var digit = BackdropBuilder.StageEmissive("MahjongDigitRed", new Color(1.0f, 0.2f, 0.14f), 1.5f);
                for (int i = 0; i < seats.Length; i++)
                {
                    var (yaw, edge) = seats[i];
                    var rot = Quaternion.Euler(0, yaw, 0);
                    var seat = new GameObject($"Seat{i + 1}P").transform;
                    seat.SetParent(root, false);
                    seat.localPosition = edge + new Vector3(0, wall, 0);
                    seat.localRotation = rot;
                    // 点数表示: 縁の外側の胴の上面(縁と胴の外の端の間)に埋め込んだ、外へ少し傾けた LED の窓
                    // (本物は胴の正面にあるが、試合のカメラからは胴の側面が見えないので上面に置く)。外側の席から読める向き
                    var panel = new GameObject("ScorePanel").transform;
                    panel.SetParent(seat, false);
                    panel.localPosition = new Vector3(0, kShellTop - wall, -(kOuter * 0.5f + kShellOut * 0.5f));
                    panel.localRotation = Quaternion.Euler(-12, 0, 0);
                    DressBox(panel, "Bezel", new Vector3(0, 0.07f, 0), new Vector3(4.3f, 0.3f, 0.86f), 0.08f, housing);
                    DressBox(panel, "Screen", new Vector3(0, 0.222f, 0), new Vector3(3.9f, 0.01f, 0.62f), 0.004f, screen);
                    var label = BackdropBuilder.StageEmissive($"MahjongSeat{i + 1}", kSeatColors[i], 1.8f);
                    // 「1P」(プレイヤーの色) と「25000」(赤)
                    float x = -1.6f;
                    Segments(panel, "Label", (char)('1' + i), new Vector3(x, 0.23f, 0), label, segOff);
                    Segments(panel, "LabelP", 'P', new Vector3(x + 0.3f, 0.23f, 0), label, segOff);
                    string score = "25000";
                    for (int k = 0; k < score.Length; k++) Segments(panel, $"Digit{k}", score[k], new Vector3(-0.42f + k * 0.34f, 0.23f, 0), digit, segOff);

                    // 点棒入れ: 胴の外側から張り出す黒いトレー。4つの仕切りに、種類ごとの点棒を並べる。
                    // 席の外向き(-Z)の、席から見て右寄り(点数表示と重ならない所)に置く
                    TenboCase(seat, new Vector3(5.4f, -wall - 0.75f, -(kOuter * 0.5f + kShellOut + 0.9f)), shell, slat);
                }

                // 中央の穴の中(洗牌の機械の井戸)。4枚の内壁と、深さの分かる横の桟、暗い底
                float half = lid * 0.5f;
                const float kWellTop = -1.02f, kWellBottom = -10.9f;
                float wellH = kWellTop - kWellBottom, wellY = (kWellTop + kWellBottom) * 0.5f;
                ShellPiece(root, "WellN", new Vector3(0, wellY, half + 0.1f), new Vector3(lid + 0.4f, wellH, 0.2f), 0.04f, well);
                ShellPiece(root, "WellS", new Vector3(0, wellY, -half - 0.1f), new Vector3(lid + 0.4f, wellH, 0.2f), 0.04f, well);
                ShellPiece(root, "WellE", new Vector3(half + 0.1f, wellY - 0.005f, 0), new Vector3(0.2f, wellH - 0.01f, lid), 0.04f, well);
                ShellPiece(root, "WellW", new Vector3(-half - 0.1f, wellY - 0.005f, 0), new Vector3(0.2f, wellH - 0.01f, lid), 0.04f, well);
                float[] ribYs = { -2.8f, -5.6f, -8.4f };
                for (int k = 0; k < ribYs.Length; k++)
                {
                    float y = ribYs[k];
                    ShellPiece(root, $"RibN_{k}", new Vector3(0, y, half - 0.04f), new Vector3(lid - 0.1f, 0.16f, 0.12f), 0.03f, rib);
                    ShellPiece(root, $"RibS_{k}", new Vector3(0, y, -half + 0.04f), new Vector3(lid - 0.1f, 0.16f, 0.12f), 0.03f, rib);
                    ShellPiece(root, $"RibE_{k}", new Vector3(half - 0.04f, y - 0.004f, 0), new Vector3(0.12f, 0.16f, lid - 0.36f), 0.03f, rib);
                    ShellPiece(root, $"RibW_{k}", new Vector3(-half + 0.04f, y - 0.004f, 0), new Vector3(0.12f, 0.16f, lid - 0.36f), 0.03f, rib);
                }
                Primitive(PrimitiveType.Cube, root, "WellBottom", new Vector3(0, kWellBottom - 0.1f, 0), new Vector3(lid + 0.4f, 0.2f, lid + 0.4f), AbyssMaterial());
                SaveDress();
            }

            // 角を丸めた箱(位置は中心)。卓の胴・井戸の壁など
            void ShellPiece(Transform parent, string name, Vector3 center, Vector3 size, float bevel, Material mat)
            {
                var mesh = Keep(BackdropBuilder.NewRoundedBox(size, bevel, 3, 4.0f), $"{name}_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}");
                MeshObject(parent, name, mesh, center, Quaternion.identity, mat);
            }

            // 縁の部品(下端が床の高さ 0)。texel は木目の繰り返しの長さ(m)
            void RailPiece(Transform parent, string name, Vector3 center, Vector3 size, float bevel, Material mat, float texel)
            {
                var mesh = Keep(BackdropBuilder.NewRoundedBox(size, bevel, 3, texel), $"{name}_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}");
                MeshObject(parent, name, mesh, center + new Vector3(0, size.y * 0.5f, 0), Quaternion.identity, mat);
            }

            GameObject DressBox(Transform parent, string name, Vector3 localPos, Vector3 size, float bevel, Material mat)
            {
                var mesh = Keep(BackdropBuilder.NewRoundedBox(size, bevel, 2, 1.0f), $"{name}_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}");
                return MeshObject(parent, name, mesh, localPos, Quaternion.identity, mat);
            }

            // 7セグメントの文字(1文字分。幅 0.2m・高さ 0.38m。文字の上は親の +Z)
            void Segments(Transform parent, string name, char ch, Vector3 center, Material on, Material off)
            {
                const float W = 0.2f, H = 0.38f, T = 0.045f;
                string lit = ch switch
                {
                    '0' => "abcdef", '1' => "bc", '2' => "abdeg", '3' => "abcdg", '4' => "bcfg",
                    '5' => "acdfg", '6' => "acdefg", '7' => "abc", '8' => "abcdefg", '9' => "abcdfg", 'P' => "abefg", _ => "",
                };
                var seg = new GameObject(name).transform;
                seg.SetParent(parent, false);
                seg.localPosition = center;
                var defs = new (char id, Vector3 pos, Vector3 size)[]
                {
                    ('a', new Vector3(0, 0, H * 0.5f), new Vector3(W, 0.012f, T)),
                    ('g', new Vector3(0, 0, 0), new Vector3(W, 0.012f, T)),
                    ('d', new Vector3(0, 0, -H * 0.5f), new Vector3(W, 0.012f, T)),
                    ('f', new Vector3(-W * 0.5f, 0, H * 0.25f), new Vector3(T, 0.012f, H * 0.5f)),
                    ('b', new Vector3(W * 0.5f, 0, H * 0.25f), new Vector3(T, 0.012f, H * 0.5f)),
                    ('e', new Vector3(-W * 0.5f, 0, -H * 0.25f), new Vector3(T, 0.012f, H * 0.5f)),
                    ('c', new Vector3(W * 0.5f, 0, -H * 0.25f), new Vector3(T, 0.012f, H * 0.5f)),
                };
                foreach (var (id, pos, size) in defs)
                {
                    var r = Primitive(PrimitiveType.Cube, seg, id.ToString(), pos, size, lit.IndexOf(id) >= 0 ? on : off).GetComponent<Renderer>();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            // 点棒入れ: 胴の外側から張り出す黒いトレー(長さ 5.6m・奥行き 1.9m)。仕切りで4つに分け、
            // 種類ごと(1万点: 点1つ / 5千点: 点2つ / 千点: 点4つ / 百点: 点8つ)の白い点棒を、席から取りやすい向き(奥行きの向き)に並べる
            void TenboCase(Transform seat, Vector3 localPos, Material shell, Material divider)
            {
                var tray = new GameObject("TenboCase").transform;
                tray.SetParent(seat, false);
                tray.localPosition = localPos;
                const float TL = 5.6f, TD = 1.9f, R = 0.16f;
                DressBox(tray, "Base", new Vector3(0, -0.3f, 0), new Vector3(TL, 0.12f, TD), 0.05f, shell);
                DressBox(tray, "RimOuter", new Vector3(0, -0.03f, -TD * 0.5f + R * 0.5f), new Vector3(TL, 0.42f, R), 0.07f, shell);
                DressBox(tray, "RimInner", new Vector3(0, -0.03f, TD * 0.5f - R * 0.5f), new Vector3(TL, 0.42f, R), 0.07f, shell);
                DressBox(tray, "RimLeft", new Vector3(-TL * 0.5f + R * 0.5f, -0.03f, 0), new Vector3(R, 0.42f, TD - R * 2 - 0.02f), 0.07f, shell);
                DressBox(tray, "RimRight", new Vector3(TL * 0.5f - R * 0.5f, -0.03f, 0), new Vector3(R, 0.42f, TD - R * 2 - 0.02f), 0.07f, shell);

                var stick = BackdropBuilder.StageMaterial("Tenbo", new Color(0.9f, 0.89f, 0.85f), 0.6f);
                var dot = BackdropBuilder.StageMaterial("TenboDot", new Color(0.1f, 0.1f, 0.11f), 0.5f);
                float inner = TL - R * 2;                  // 仕切りの内側の長さ
                float cell = inner / 4;
                int[] dotCounts = { 1, 2, 4, 8 };
                int[] stickCounts = { 3, 4, 6, 6 };
                const float kStickLen = TD - R * 2 - 0.2f, kPitch = 0.2f;
                for (int c = 0; c < 4; c++)
                {
                    float left = -inner * 0.5f + c * cell;
                    if (c > 0) DressBox(tray, $"Divider_{c}", new Vector3(left, -0.08f, 0), new Vector3(0.07f, 0.32f, TD - R * 2 - 0.02f), 0.025f, divider);
                    float start = left + cell * 0.5f - (stickCounts[c] - 1) * kPitch * 0.5f;
                    for (int s = 0; s < stickCounts[c]; s++)
                    {
                        var stickGo = DressBox(tray, $"Tenbo_{c}_{s}", new Vector3(start + s * kPitch, -0.205f, 0), new Vector3(0.13f, 0.07f, kStickLen), 0.03f, stick);
                        // 細い点棒の影は、こたつの天板の上に粗い点の模様になって出るので落とさない
                        stickGo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        int dots = dotCounts[c];
                        for (int k = 0; k < dots; k++)
                        {
                            // 8つの点は2列に、それ以外は中央に縦に並べる
                            int row = dots == 8 ? k % 2 : 0, col = dots == 8 ? k / 2 : k;
                            int perCol = dots == 8 ? 4 : dots;
                            float z = (col - (perCol - 1) * 0.5f) * 0.16f;
                            float x = dots == 8 ? (row - 0.5f) * 0.05f : 0;
                            float size = dots == 1 ? 0.085f : dots == 8 ? 0.035f : 0.05f;
                            var p = Primitive(PrimitiveType.Cylinder, stickGo.transform, "Dot", Vector3.zero, Vector3.one, dot);
                            p.transform.localPosition = new Vector3(x, 0.037f, z);
                            p.transform.localScale = new Vector3(size, 0.003f, size);
                            p.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        }
                    }
                }
            }

            //----------------------------------------
            // Stage23 決勝の特設ステージ
            //----------------------------------------

            // 床の縁の LED の帯(床の側面と、壁の内側の足元)と、四隅の照明トラス
            public void DressFinalStage()
            {
                var root = DressRoot();
                Rect floor = FloorRect();
                var led = BackdropBuilder.StageEmissive("StageLed", new Color(0.8f, 0.9f, 1.0f), 3.0f);
                var ledHousing = BackdropBuilder.StageMaterial("StageLedHousing", new Color(0.12f, 0.13f, 0.15f), 0.5f, 0.4f);

                // 床の側面(崖になっている東西と、壁の外側の南北)。上面のすぐ下に、黒い枠と光る帯を少しはみ出させて貼る
                void SideStrip(string name, Vector3 center, Vector3 size)
                {
                    var h = Primitive(PrimitiveType.Cube, root, name + "Housing", center, size + new Vector3(size.x < 0.1f ? 0 : 0.0f, 0.06f, size.z < 0.1f ? 0 : 0.0f), ledHousing);
                    h.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var l = Primitive(PrimitiveType.Cube, root, name, center + new Vector3(size.x < 0.1f ? Mathf.Sign(center.x) * 0.012f : 0, 0, size.z < 0.1f ? Mathf.Sign(center.z) * 0.012f : 0), size, led);
                    l.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                const float y = -0.16f;
                SideStrip("LedEast", new Vector3(floor.xMax + 0.02f, y, floor.center.y), new Vector3(0.04f, 0.12f, floor.height));
                SideStrip("LedWest", new Vector3(floor.xMin - 0.02f, y, floor.center.y), new Vector3(0.04f, 0.12f, floor.height));
                SideStrip("LedNorth", new Vector3(floor.center.x, y, floor.yMax + 0.02f), new Vector3(floor.width, 0.12f, 0.04f));
                SideStrip("LedSouth", new Vector3(floor.center.x, y, floor.yMin - 0.02f), new Vector3(floor.width, 0.12f, 0.04f));
                // 壁の内側の足元(遊ぶ床の縁どり)
                foreach (var wall in AxisWalls(Stage))
                {
                    Bounds b = BoxBounds(wall);
                    if (b.size.x < b.size.z) continue;   // 南北の壁だけ
                    // 白い壁の上では白い光が見えないので、黒い帯(器具)の中に光る線を入れる
                    float sign = b.center.z > floor.center.y ? -1 : 1;
                    float face = b.center.z > floor.center.y ? b.min.z : b.max.z;
                    var hb = Primitive(PrimitiveType.Cube, root, "LedWallFootHousing", new Vector3(b.center.x, 0.11f, face + sign * 0.02f), new Vector3(b.size.x, 0.22f, 0.04f), ledHousing);
                    hb.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var l = Primitive(PrimitiveType.Cube, root, "LedWallFoot", new Vector3(b.center.x, 0.11f, face + sign * 0.045f), new Vector3(b.size.x, 0.08f, 0.02f), led);
                    l.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                // 四隅の照明トラス。ステージは台の上にあるので、芝生(背景の机の天板の高さ)から立てる
                // (奥の2本は床から 9m、手前の2本はステージが隠れないよう床から 5.5m で、外側に)
                const float kGround = -10.5f;
                var alu = BackdropBuilder.StageMaterial("TrussAluminium", new Color(0.72f, 0.74f, 0.78f), 0.7f, 0.8f);
                var lampBody = BackdropBuilder.StageMaterial("SpotBody", new Color(0.08f, 0.08f, 0.09f), 0.5f, 0.3f);
                var lens = BackdropBuilder.StageEmissive("SpotLens", new Color(1.0f, 0.97f, 0.9f), 3.0f);
                var corners = new[]
                {
                    (pos: new Vector2(floor.xMin - 2.2f, floor.yMax + 1.8f), height: 9.0f),
                    (pos: new Vector2(floor.xMax + 2.2f, floor.yMax + 1.8f), height: 9.0f),
                    (pos: new Vector2(floor.xMin - 3.2f, floor.yMin - 1.2f), height: 5.5f),
                    (pos: new Vector2(floor.xMax + 3.2f, floor.yMin - 1.2f), height: 5.5f),
                };
                for (int i = 0; i < corners.Length; i++)
                {
                    var (p, height) = corners[i];
                    Truss(root, $"Truss{i}", new Vector3(p.x, kGround, p.y), height - kGround, new Vector3(floor.center.x, 0, floor.center.y), alu, lampBody, lens);
                }
            }

            // 角柱のトラス(4本の柱と、段ごとの横材・斜材)。上に照明を2つ載せ、ステージの中心へ向ける
            void Truss(Transform parent, string name, Vector3 basePos, float height, Vector3 target, Material alu, Material body, Material lens)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.localPosition = basePos;
                Vector3 to = target - basePos;
                to.y = 0;
                t.localRotation = Quaternion.LookRotation(to.normalized);
                const float S = 0.55f, R = 0.05f;
                // 足元の台座(高いトラスを支える重り)
                Primitive(PrimitiveType.Cube, t, "Base", new Vector3(0, 0.1f, 0), new Vector3(S + 1.0f, 0.2f, S + 1.0f), body);
                for (int k = 0; k < 4; k++)
                {
                    float x = (k % 2 == 0 ? -1 : 1) * S * 0.5f, z = (k < 2 ? -1 : 1) * S * 0.5f;
                    Primitive(PrimitiveType.Cylinder, t, $"Chord{k}", new Vector3(x, height * 0.5f, z), new Vector3(R * 2, height * 0.5f, R * 2), alu);
                }
                int levels = Mathf.Max(2, Mathf.RoundToInt(height / 0.9f));
                float step = height / levels;
                for (int lv = 0; lv <= levels; lv++)
                {
                    float y = lv * step;
                    // 横材(4辺)
                    Primitive(PrimitiveType.Cube, t, $"H{lv}a", new Vector3(0, y, -S * 0.5f), new Vector3(S, 0.04f, 0.04f), alu);
                    Primitive(PrimitiveType.Cube, t, $"H{lv}b", new Vector3(0, y, S * 0.5f), new Vector3(S, 0.04f, 0.04f), alu);
                    Primitive(PrimitiveType.Cube, t, $"H{lv}c", new Vector3(-S * 0.5f, y, 0), new Vector3(0.04f, 0.04f, S), alu);
                    Primitive(PrimitiveType.Cube, t, $"H{lv}d", new Vector3(S * 0.5f, y, 0), new Vector3(0.04f, 0.04f, S), alu);
                    if (lv == levels) break;
                    // 斜材(段ごとに向きを変える)
                    float diag = Mathf.Sqrt(S * S + step * step);
                    float ang = Mathf.Atan2(step, S) * Mathf.Rad2Deg * (lv % 2 == 0 ? 1 : -1);
                    var a = Primitive(PrimitiveType.Cube, t, $"D{lv}a", new Vector3(0, y + step * 0.5f, -S * 0.5f), new Vector3(diag, 0.03f, 0.03f), alu);
                    a.transform.localRotation = Quaternion.Euler(0, 0, ang);
                    var b = Primitive(PrimitiveType.Cube, t, $"D{lv}b", new Vector3(0, y + step * 0.5f, S * 0.5f), new Vector3(diag, 0.03f, 0.03f), alu);
                    b.transform.localRotation = Quaternion.Euler(0, 0, -ang);
                    var c = Primitive(PrimitiveType.Cube, t, $"D{lv}c", new Vector3(-S * 0.5f, y + step * 0.5f, 0), new Vector3(0.03f, 0.03f, diag), alu);
                    c.transform.localRotation = Quaternion.Euler(-ang, 0, 0);
                    var d = Primitive(PrimitiveType.Cube, t, $"D{lv}d", new Vector3(S * 0.5f, y + step * 0.5f, 0), new Vector3(0.03f, 0.03f, diag), alu);
                    d.transform.localRotation = Quaternion.Euler(ang, 0, 0);
                }
                // 上の横棒と、ステージへ向けた照明2つ
                Primitive(PrimitiveType.Cube, t, "Bar", new Vector3(0, height + 0.1f, 0), new Vector3(2.2f, 0.12f, 0.12f), alu);
                for (int k = 0; k < 2; k++)
                {
                    var lamp = new GameObject($"Spot{k}").transform;
                    lamp.SetParent(t, false);
                    lamp.localPosition = new Vector3(k == 0 ? -0.75f : 0.75f, height - 0.15f, 0.1f);
                    Vector3 aim = t.InverseTransformPoint(t.parent.TransformPoint(target)) - lamp.localPosition;
                    lamp.localRotation = Quaternion.LookRotation(aim.normalized);
                    var bodyGo = Primitive(PrimitiveType.Cylinder, lamp, "Body", new Vector3(0, 0, 0), new Vector3(0.42f, 0.28f, 0.42f), body);
                    bodyGo.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    var lensGo = Primitive(PrimitiveType.Cylinder, lamp, "Lens", new Vector3(0, 0, 0.29f), new Vector3(0.34f, 0.01f, 0.34f), lens);
                    lensGo.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    lensGo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    Primitive(PrimitiveType.Cube, lamp, "Yoke", new Vector3(0, 0.26f, -0.05f), new Vector3(0.5f, 0.04f, 0.04f), alu);
                }
            }

            //----------------------------------------
            // Stage25 書き初めの半紙
            //----------------------------------------

            // 北と南の壁を文鎮に見立てる(黒い鉄の棒。上につまみと、端に細い帯)
            public void DressBunchinWalls()
            {
                var root = DressRoot();
                var ironTex = BackdropBuilder.StageTexture("BunchinIron", 256, (u, v) =>
                {
                    float n = BackdropBuilder.NoiseFbm(u * 8, v * 8, 8, 401) * 0.6f + BackdropBuilder.NoiseFbm(u * 40, v * 40, 40, 402) * 0.4f;
                    float k = 0.13f + n * 0.06f;
                    return new Color(k, k * 0.97f, k * 0.93f);
                });
                var iron = BackdropBuilder.StageMaterial("Bunchin", Color.white, 0.62f, 0.55f, ironTex, Vector2.one, true);
                var band = BackdropBuilder.StageMaterial("BunchinBand", new Color(0.36f, 0.33f, 0.29f), 0.7f, 0.7f);
                foreach (var wall in AxisWalls(Stage))
                {
                    Bounds b = BoxBounds(wall);
                    if (b.size.x < b.size.z) continue;
                    wall.GetComponent<Renderer>().enabled = false;
                    // まとめたシーンでの見える壁の高さ(1m)に合わせる
                    float hVis = StageWallSplitter.kVisibleHeight;
                    b = new Bounds(new Vector3(b.center.x, b.min.y + hVis * 0.5f, b.center.z), new Vector3(b.size.x, hVis, b.size.z));
                    string side = b.center.z > 0 ? "North" : "South";
                    var mesh = Keep(BackdropBuilder.NewRoundedBox(new Vector3(b.size.x - 0.1f, b.size.y, b.size.z - 0.08f), 0.32f, 4, 3.0f), $"Bunchin_{b.size.x:0.##}");
                    var bar = MeshObject(root, $"Bunchin{side}", mesh, b.center, Quaternion.identity, iron);
                    // 上のつまみ(中央の細長いふくらみ)と、両端近くの帯
                    var knob = Keep(BackdropBuilder.NewRoundedBox(new Vector3(5.0f, 0.3f, 0.42f), 0.14f, 3, 1.0f), "BunchinKnob");
                    MeshObject(root, $"Bunchin{side}Knob", knob, new Vector3(b.center.x, b.max.y + 0.08f, b.center.z), Quaternion.identity, iron);
                    var bandMesh = Keep(BackdropBuilder.NewRoundedBox(new Vector3(0.18f, b.size.y + 0.02f, b.size.z - 0.04f), 0.06f, 2, 1.0f), "BunchinBand");
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        MeshObject(root, $"Bunchin{side}Band", bandMesh, new Vector3(b.center.x + sx * (b.size.x * 0.5f - 1.6f), b.center.y, b.center.z), Quaternion.identity, band);
                    }
                    _ = bar;
                }
                SaveDress();
            }

            //----------------------------------------
            // 共通
            //----------------------------------------

            // 床(箱)の範囲をすべて合わせた長方形
            Rect FloorRect()
            {
                bool any = false;
                Rect r = default;
                foreach (var f in Floors)
                {
                    r = any ? Rect.MinMaxRect(Mathf.Min(r.xMin, f.xMin), Mathf.Min(r.yMin, f.yMin), Mathf.Max(r.xMax, f.xMax), Mathf.Max(r.yMax, f.yMax)) : f;
                    any = true;
                }
                return r;
            }
        }

        // プレイヤーの色(DarumaMatch の 1P〜4P と同じ)
        static readonly Color[] kSeatColors =
        {
            new Color(0.93f, 0.27f, 0.25f), new Color(0.22f, 0.55f, 0.95f), new Color(0.98f, 0.76f, 0.12f), new Color(0.30f, 0.74f, 0.35f),
        };

        static GameObject MeshObject(Transform parent, string name, Mesh mesh, Vector3 localPos, Quaternion localRot, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
            return go;
        }

        // stageRoot の下の、表示されている壁(回転していない箱の ReflectWall)
        static List<Transform> AxisWalls(Transform stageRoot)
        {
            var list = new List<Transform>();
            foreach (var w in stageRoot.GetComponentsInChildren<ReflectWall>(true))
            {
                var r = w.GetComponent<Renderer>();
                if (r == null || !r.enabled || w.GetComponent<BoxCollider>() == null) continue;
                if (Quaternion.Angle(w.transform.rotation, Quaternion.identity) > 0.1f) continue;
                list.Add(w.transform);
            }
            return list;
        }

        // 箱の範囲(stageRoot の親からの相対ではなく、親 Stage からの相対。Stage は回転・拡大しない前提)
        static Bounds BoxBounds(Transform box) => new Bounds(box.localPosition, box.localScale);

        static void HideWallRenderers(Transform stageRoot)
        {
            foreach (var w in AxisWalls(stageRoot)) w.GetComponent<Renderer>().enabled = false;
        }

        // 特設ステージの床: 2m 角のパネルの継ぎ目と、パネルごとのわずかな濃淡(遠近感が分かる程度の控えめな模様)
        static Material StadiumStageFloorMaterial(float width, float depth)
        {
            var tex = BackdropBuilder.StageTexture("StadiumPanels", 512, (u, v) =>
            {
                const int kPanels = 4;
                float pu = u * kPanels, pv = v * kPanels;
                int iu = Mathf.FloorToInt(pu), iv = Mathf.FloorToInt(pv);
                float fu = pu - iu, fv = pv - iv;
                float shade = 0.95f + BackdropBuilder.NoiseHash(iu, iv, 501) * 0.05f;
                float n = BackdropBuilder.NoiseFbm(u * 16, v * 16, 16, 502);
                float k = shade + (n - 0.5f) * 0.03f;
                // 継ぎ目(細い暗い線と、その横の細い明るい線)
                float e = Mathf.Min(Mathf.Min(fu, 1 - fu), Mathf.Min(fv, 1 - fv));
                if (e < 0.008f) k *= 0.78f;
                else if (e < 0.016f) k *= 1.03f;
                return new Color(k, k, k);
            });
            return BackdropBuilder.StageMaterial("StadiumFloor", new Color(0.62f, 0.74f, 0.92f), 0.4f, 0, tex, new Vector2(width / 8.0f, depth / 8.0f));
        }

        //========================================
        // Stage18 もんじゃの鉄板(手で作ったステージなので、まとめたシーンの中で直接作り込む)
        //========================================

        // 枠(壁)を鉄板のふち(焦げた黒い鉄。角を丸め、上に少し張り出したへり)にする。何度実行しても同じ結果になる
        [MenuItem("Nakagawa/Stages/Stage18 の縁を鉄板のふちにする")]
        public static void DressStage18Rim()
        {
            const string kId = "Stage18";
            Scene scene = SceneManager.GetActiveScene();
            GameObject stageGo = null;
            foreach (var root in scene.GetRootGameObjects()) if (root.name == kId) stageGo = root;
            if (stageGo == null)
            {
                Debug.LogError($"[StageBuilder] {kId} が開いているシーンにありません(AllStages を開いてください)");
                return;
            }
            Transform stage = stageGo.transform.Find("Stage");
            var old = stage.Find(kDressRootName);
            if (old != null)
            {
                // 前回の作り込みを消し、隠していた壁の描画を戻してから作り直す
                Object.DestroyImmediate(old.gameObject);
                foreach (var w in stage.GetComponentsInChildren<ReflectWall>(true))
                {
                    if (w.name != "Wall") continue;
                    var r = w.GetComponent<Renderer>();
                    if (r != null) r.enabled = true;
                }
            }
            var dress = new GameObject(kDressRootName).transform;
            dress.SetParent(stage, false);
            var store = new MeshAssetStore($"{kMeshDir}/{kId}_Dress.asset");

            var tex = BackdropBuilder.StageTexture("ScorchedIron", 256, (u, v) => BackdropBuilder.ScorchedIron(u, v, 601));
            var iron = BackdropBuilder.StageMaterial("MonjaRim", Color.white, 0.5f, 0.6f, tex, Vector2.one, true);
            // へりは、こすれて少し明るくなった鉄(床の鉄板と枠の境目が分かるように)
            var lip = BackdropBuilder.StageMaterial("MonjaRimLip", new Color(0.32f, 0.31f, 0.3f), 0.6f, 0.75f, null, null, true);

            var walls = new List<Transform>();
            foreach (var w in AxisWalls(stage)) if (w.name == "Wall") walls.Add(w);
            for (int i = 0; i < walls.Count; i++)
            {
                Bounds b = BoxBounds(walls[i]);
                walls[i].GetComponent<Renderer>().enabled = false;
                // 角で重なる壁どうしの上面がちらつかないよう、長い向きが Z の壁(東西)を少し低くする
                bool alongZ = b.size.z > b.size.x;
                float drop = alongZ ? 0.012f : 0.0f;
                var size = new Vector3(b.size.x, b.size.y - drop, b.size.z);
                var body = BackdropBuilder.NewRoundedBox(size, 0.14f, 3, 2.5f);
                body.name = $"Rim_{i}";
                store.Add(body);
                MeshObject(dress, $"Rim_{i}", body, new Vector3(b.center.x, b.min.y + size.y * 0.5f, b.center.z), Quaternion.identity, iron);
                // 上のへり(内側・外側へ少し張り出した薄い板)
                var lipSize = new Vector3(b.size.x + (alongZ ? 0.24f : 0.0f), 0.07f, b.size.z + (alongZ ? 0.0f : 0.24f));
                var lipMesh = BackdropBuilder.NewRoundedBox(lipSize, 0.03f, 2, 2.5f);
                lipMesh.name = $"RimLip_{i}";
                store.Add(lipMesh);
                MeshObject(dress, $"RimLip_{i}", lipMesh, new Vector3(b.center.x, b.min.y + size.y - 0.03f - drop, b.center.z), Quaternion.identity, lip);
            }
            store.Save();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[StageBuilder] {kId} の枠を鉄板のふちにしました(壁 {walls.Count} 本)。シーンを保存してください");
        }
    }
}
