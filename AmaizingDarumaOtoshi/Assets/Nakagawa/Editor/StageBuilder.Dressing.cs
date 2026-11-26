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

            // 卓の縁を「内側の緑の縁取り + 外側の木目の枠」の二重にし、各辺の中央に点数表示とプレイヤー表記、右寄りに点棒入れを置く
            // w×d は遊べる範囲(壁の内側)。壁の厚さは 1m(Mahjong と同じ)
            public void DressMahjongTable(float w, float d)
            {
                var root = DressRoot();
                HideWallRenderers(Stage);

                float hx = w * 0.5f, hz = d * 0.5f;
                const float kGreen = 0.42f;   // 緑の縁取りの幅
                const float kOuter = 1.0f;    // 壁の厚さ
                // まとめたシーンでは、壁は見える高さ(1m)と、その上の見えない壁に分けられる。見える高さに合わせる
                float wall = StageWallSplitter.kVisibleHeight;

                var greenMat = BackdropBuilder.StageMaterial("MahjongRail", new Color(0.15f, 0.36f, 0.26f), 0.35f, 0, null, null, true);
                var woodTex = BackdropBuilder.StageTexture("MahjongWood", 256, (u, v) =>
                    BackdropBuilder.WoodPattern(u, v, 301, new Color(0.46f, 0.29f, 0.17f), new Color(0.3f, 0.18f, 0.1f)));
                var woodMat = BackdropBuilder.StageMaterial("MahjongWood", Color.white, 0.62f, 0, woodTex, Vector2.one, true);

                // 緑(内側): 南北は角まで、東西は南北の間だけ。木目(外側)も同じ分け方で、重なりを作らない
                float g = hz + kGreen, o = hz + kOuter;
                RailPiece(root, "GreenN", new Vector3(0, 0, hz + kGreen * 0.5f), new Vector3((hx + kGreen) * 2, wall - 0.1f, kGreen), 0.16f, greenMat, 2);
                RailPiece(root, "GreenS", new Vector3(0, 0, -hz - kGreen * 0.5f), new Vector3((hx + kGreen) * 2, wall - 0.1f, kGreen), 0.16f, greenMat, 2);
                RailPiece(root, "GreenE", new Vector3(hx + kGreen * 0.5f, 0, 0), new Vector3(kGreen, wall - 0.1f, hz * 2), 0.16f, greenMat, 2);
                RailPiece(root, "GreenW", new Vector3(-hx - kGreen * 0.5f, 0, 0), new Vector3(kGreen, wall - 0.1f, hz * 2), 0.16f, greenMat, 2);
                float wo = kOuter - kGreen;
                RailPiece(root, "WoodN", new Vector3(0, 0, (g + o) * 0.5f), new Vector3((hx + kOuter) * 2, wall, wo), 0.12f, woodMat, 6);
                RailPiece(root, "WoodS", new Vector3(0, 0, -(g + o) * 0.5f), new Vector3((hx + kOuter) * 2, wall, wo), 0.12f, woodMat, 6);
                RailPiece(root, "WoodE", new Vector3(hx + kGreen + wo * 0.5f, 0, 0), new Vector3(wo, wall, (hz + kGreen) * 2), 0.12f, woodMat, 6);
                RailPiece(root, "WoodW", new Vector3(-hx - kGreen - wo * 0.5f, 0, 0), new Vector3(wo, wall, (hz + kGreen) * 2), 0.12f, woodMat, 6);

                // 各辺の席(外から卓を見る向き): 南=1P・東=2P・北=3P・西=4P(東南西北の順に反時計回り)
                var seats = new[]
                {
                    (yaw: 0f, edge: new Vector3(0, 0, -hz - kOuter * 0.5f)),
                    (yaw: -90f, edge: new Vector3(hx + kOuter * 0.5f, 0, 0)),
                    (yaw: 180f, edge: new Vector3(0, 0, hz + kOuter * 0.5f)),
                    (yaw: 90f, edge: new Vector3(-hx - kOuter * 0.5f, 0, 0)),
                };
                var housing = BackdropBuilder.StageMaterial("MahjongPanel", new Color(0.07f, 0.07f, 0.08f), 0.7f);
                var screen = BackdropBuilder.StageMaterial("MahjongScreen", new Color(0.02f, 0.025f, 0.03f), 0.95f);
                var segOff = BackdropBuilder.StageMaterial("MahjongSegmentOff", new Color(0.045f, 0.05f, 0.055f), 0.6f);
                var digit = BackdropBuilder.StageEmissive("MahjongDigit", new Color(0.82f, 0.92f, 1.0f), 1.6f);
                for (int i = 0; i < seats.Length; i++)
                {
                    var (yaw, edge) = seats[i];
                    var rot = Quaternion.Euler(0, yaw, 0);
                    var seat = new GameObject($"Seat{i + 1}P").transform;
                    seat.SetParent(root, false);
                    seat.localPosition = edge + new Vector3(0, wall, 0);
                    seat.localRotation = rot;
                    // 点数表示(縁の上の中央。外側の席から読める向き)
                    var panel = DressBox(seat, "ScorePanel", new Vector3(0, 0.07f, 0), new Vector3(3.9f, 0.14f, 0.9f), 0.05f, housing);
                    DressBox(seat, "Screen", new Vector3(0, 0.142f, 0), new Vector3(3.5f, 0.01f, 0.62f), 0.004f, screen);
                    var label = BackdropBuilder.StageEmissive($"MahjongSeat{i + 1}", kSeatColors[i], 1.8f);
                    // 「1P」(プレイヤーの色) と「25000」(白)
                    float x = -1.5f;
                    Segments(seat, $"Label", (char)('1' + i), new Vector3(x, 0.15f, 0), label, segOff);
                    Segments(seat, $"LabelP", 'P', new Vector3(x + 0.3f, 0.15f, 0), label, segOff);
                    string score = "25000";
                    for (int k = 0; k < score.Length; k++) Segments(seat, $"Digit{k}", score[k], new Vector3(-0.42f + k * 0.34f, 0.15f, 0), digit, segOff);
                    _ = panel;
                    // 点棒入れ(席から見て右寄り。縁の上に置いた木の箱に、点棒を並べる)
                    TenboTray(seat, new Vector3(6.5f, 0, 0), woodMat);
                }
                SaveDress();
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

            // 点棒入れ: 浅い木の箱と、中に並べた白い点棒(黒い点)
            void TenboTray(Transform seat, Vector3 localPos, Material wood)
            {
                var tray = new GameObject("TenboTray").transform;
                tray.SetParent(seat, false);
                tray.localPosition = localPos;
                const float L = 2.2f, D = 0.78f, H = 0.16f, R = 0.05f;
                DressBox(tray, "Bottom", new Vector3(0, 0.02f, 0), new Vector3(L, 0.04f, D), 0.015f, wood);
                DressBox(tray, "RimFront", new Vector3(0, H * 0.5f, -D * 0.5f + R * 0.5f), new Vector3(L, H, R), 0.02f, wood);
                DressBox(tray, "RimBack", new Vector3(0, H * 0.5f, D * 0.5f - R * 0.5f), new Vector3(L, H, R), 0.02f, wood);
                DressBox(tray, "RimLeft", new Vector3(-L * 0.5f + R * 0.5f, H * 0.5f, 0), new Vector3(R, H, D - R * 2), 0.02f, wood);
                DressBox(tray, "RimRight", new Vector3(L * 0.5f - R * 0.5f, H * 0.5f, 0), new Vector3(R, H, D - R * 2), 0.02f, wood);
                var stick = BackdropBuilder.StageMaterial("Tenbo", new Color(0.93f, 0.92f, 0.88f), 0.6f);
                var dot = BackdropBuilder.StageMaterial("TenboDot", new Color(0.1f, 0.1f, 0.11f), 0.5f);
                for (int i = 0; i < 6; i++)
                {
                    float z = -0.27f + i * 0.108f;
                    var s = DressBox(tray, $"Tenbo_{i}", new Vector3(0, 0.075f, z), new Vector3(L - 0.3f, 0.05f, 0.075f), 0.02f, stick);
                    int dots = i < 2 ? 1 : i < 4 ? 2 : 4;
                    for (int k = 0; k < dots; k++)
                    {
                        float x = (k - (dots - 1) * 0.5f) * 0.12f;
                        var p = Primitive(PrimitiveType.Cylinder, s.transform, "Dot", Vector3.zero, Vector3.one, dot);
                        p.transform.localPosition = new Vector3(x, 0.03f, 0);
                        p.transform.localScale = new Vector3(0.045f, 0.004f, 0.045f);
                        p.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
