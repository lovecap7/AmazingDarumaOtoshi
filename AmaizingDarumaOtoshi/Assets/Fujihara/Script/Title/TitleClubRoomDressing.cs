using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 【試作】部室のタイトル。TitleClubRoomScene 専用。
// 汗と泥の運動部ふうの「だるま落とし部」の部室（熱血・GFF 向け）。窓の外は青空と校庭。だるま落としの演出（TitleScreen）はそのまま。
// TitleScreen が舞台を作ったあとに背景だけを部室に差し替える。
//  ・汚れ：泥としみと足跡の床、すり傷としみの壁、へこんでさびたロッカー
//  ・道具：壁のハンマー掛け（テーピングした練習用ハンマー）、立てかけた巨大ハンマー、欠けた練習用の積み木、×印の的
//  ・熱血：窓の上の「全国制覇」の横断幕、「根性」「一撃入魂」の貼り紙、練習メニューのホワイトボード
//  ・ほか：練習用のタイヤ、ベンチとタオル、給水ジャグ、スポーツバッグ、ほこりの舞う光の筋、ちらつく蛍光灯
//  ・だるま落としの演出は部屋の右寄りの作業台の上（部屋ごと roomOffset だけずらす）
//  ・部員：窓の外で素振り、その奥で走り込み、ロッカーの前でハンマー選び
//  ・最後のカメラは後ろに引いてロッカーまで映す（このシーンの TitleScreen の finalCamPos / finalLook / finalFov）
[DefaultExecutionOrder(100)]
public class TitleClubRoomDressing : MonoBehaviour
{
    [SerializeField] Color wallColor = new Color(0.88f, 0.84f, 0.74f);
    [SerializeField] Color floorColor = new Color(0.62f, 0.6f, 0.56f);
    [Header("貼り紙・ホワイトボードの文字")]
    [SerializeField] string banner = "全国制覇";
    [SerializeField] string[] slogans = { "根性", "一撃入魂" };
    [SerializeField] string boardTitle = "練習メニュー";
    [SerializeField] string[] boardNotes = { "素振り 1000回", "積み上げ 100段", "うち返し 500本", "走り込み！！" };
    [SerializeField] string boardGoal = "目指せ全国！";

    [Header("配置")]
    [Tooltip("部屋全体のずれ。だるま落としの演出（原点）が作業台の上に来るよう、部屋を下（台の高さ）と左へずらす")]
    [SerializeField] Vector3 roomOffset = new Vector3(-3f, -5f, 0f);
    [Tooltip("部員（頭＋積み木の体）の大きさ。演出のおもちゃの何倍か")]
    [SerializeField] float memberScale = 3.2f;

    const float BackZ = 26f, SideX = 26f, Height = 24f;
    const float WinLeft = -11f, WinRight = 23f, WinBottom = 5f, WinTop = 17f;
    const float YardY = 4.5f;

    Transform root;
    TMP_FontAsset font;
    readonly List<Transform> clouds = new List<Transform>();
    readonly List<Vector3> cloudBase = new List<Vector3>();
    Material flickerTube;
    Color flickerBase;

    static Color[] Palette => ToyKit.Palette;

    void Start()
    {
        root = new GameObject("ClubRoom").transform;
        font = TitleDressingKit.TitleFont();
        TitleDressingKit.RemoveToyBackground(gameObject.scene);
        SetupLight();
        BuildOutside();
        BuildRoom();
        BuildWindows();
        BuildBanners();
        BuildLockers();
        BuildWhiteboard();
        BuildHammers();
        BuildBlocks();
        BuildBenchAndGear();
        BuildCeiling();
        BuildDust();
        // 部屋ごと下・左へずらす：だるま落としの演出（原点）が、部屋の右寄りに置いた作業台の上になる
        root.position = roomOffset;
        BuildTable();
        BuildMembers();
    }

    // ================= 光 =================
    void SetupLight()
    {
        var cam = Camera.main;
        if (cam != null) cam.backgroundColor = new Color(0.55f, 0.78f, 1f);
        RenderSettings.fogColor = new Color(0.9f, 0.88f, 0.82f);
        RenderSettings.fogStartDistance = 60f;
        RenderSettings.fogEndDistance = 160f;
        RenderSettings.ambientSkyColor = new Color(0.78f, 0.8f, 0.85f);
        RenderSettings.ambientEquatorColor = new Color(0.8f, 0.74f, 0.65f);
        RenderSettings.ambientGroundColor = new Color(0.5f, 0.44f, 0.38f);
        // 夕方前の強い日ざし（窓のある右奥上から）
        var key = TitleDressingKit.FindLight("KeyLight");
        if (key != null) { key.color = new Color(1f, 0.93f, 0.8f); key.intensity = 1.55f; key.shadowStrength = 0.75f; key.transform.rotation = Quaternion.Euler(45f, -30f, 0f); }
        var p = TitleDressingKit.PostProfile();
        if (p != null)
        {
            if (p.TryGet<ColorAdjustments>(out var ca)) { ca.contrast.value = 20f; ca.saturation.value = 8f; ca.colorFilter.value = new Color(1f, 0.96f, 0.9f); }
            if (p.TryGet<Vignette>(out var vig)) { vig.intensity.value = 0.32f; vig.color.value = new Color(0.25f, 0.18f, 0.12f); }
        }
    }

    // ================= 窓の外（青空・雲・校庭） =================
    void BuildOutside()
    {
        TitleDressingKit.Quad(root, "Sky", new Vector3(6f, 16f, BackZ + 34f), new Vector3(150f, 70f, 1f), TitleDressingKit.Unlit(Color.white, SkyTexture()));
        var cloudMat = TitleDressingKit.UnlitTransparent(CloudTexture(), Color.white);
        var rng = new System.Random(4);
        for (int i = 0; i < 6; i++)
        {
            var pos = new Vector3(-20f + i * 9f + (float)rng.NextDouble() * 4f, 15f + (float)rng.NextDouble() * 6f, BackZ + 32f);
            float s = 6f + (float)rng.NextDouble() * 5f;
            clouds.Add(TitleDressingKit.Quad(root, "Cloud", pos, new Vector3(s, s * 0.5f, 1f), cloudMat));
            cloudBase.Add(pos);
        }
        // 校庭：土のグラウンド（向こうの木は木に見えなかったので一旦なし）
        // 窓枠の下の壁に隠れないよう、校庭は部屋の床より高くしてある（外の部員の全身が見えるように）
        var ground = TitleDressingKit.Quad(root, "Schoolyard", new Vector3(6f, YardY, BackZ + 15f), new Vector3(120f, 30f, 1f), TitleDressingKit.Unlit(new Color(0.78f, 0.66f, 0.5f)));
        ground.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    // ================= 部屋（汚れた床・壁） =================
    void BuildRoom()
    {
        var floor = ToyKit.Primitive(PrimitiveType.Plane, "Floor", root);
        floor.localScale = new Vector3(SideX * 0.2f + 1f, 1f, 6f);
        floor.position = new Vector3(0f, 0f, BackZ - 30f);
        var fm = ToyKit.Lit(Color.white, 0.25f);
        fm.SetTexture("_BaseMap", DirtyFloorTexture());
        fm.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
        floor.GetComponent<Renderer>().sharedMaterial = fm;

        var wall = ToyKit.Lit(Color.white, 0.15f);
        wall.SetTexture("_BaseMap", DirtyWallTexture());
        wall.SetTextureScale("_BaseMap", new Vector2(3f, 1.5f));
        var lower = ToyKit.Lit(new Color(0.45f, 0.52f, 0.48f), 0.3f);   // 腰壁（すすけた緑）
        lower.SetTexture("_BaseMap", DirtyWallTexture());
        // 奥の壁：窓の穴を残して組む
        Box(root, "BackWallLeft", new Vector3((-SideX + WinLeft) * 0.5f, Height * 0.5f, BackZ), new Vector3(WinLeft + SideX, Height, 0.4f), wall);
        Box(root, "BackWallRight", new Vector3((WinRight + SideX) * 0.5f, Height * 0.5f, BackZ), new Vector3(SideX - WinRight, Height, 0.4f), wall);
        Box(root, "BackWallBottom", new Vector3((WinLeft + WinRight) * 0.5f, WinBottom * 0.5f, BackZ), new Vector3(WinRight - WinLeft, WinBottom, 0.4f), wall);
        Box(root, "BackWallTop", new Vector3((WinLeft + WinRight) * 0.5f, (WinTop + Height) * 0.5f, BackZ), new Vector3(WinRight - WinLeft, Height - WinTop, 0.4f), wall);
        Box(root, "LeftWall", new Vector3(-SideX, Height * 0.5f, BackZ - 20f), new Vector3(0.4f, Height, 40f), wall);
        Box(root, "RightWall", new Vector3(SideX, Height * 0.5f, BackZ - 20f), new Vector3(0.4f, Height, 40f), wall);
        Box(root, "WainscotBack", new Vector3(0f, 1.6f, BackZ - 0.25f), new Vector3(SideX * 2f, 3.2f, 0.1f), lower);
        Box(root, "WainscotLeft", new Vector3(-SideX + 0.25f, 1.6f, BackZ - 20f), new Vector3(0.1f, 3.2f, 40f), lower);
        Box(root, "WainscotRight", new Vector3(SideX - 0.25f, 1.6f, BackZ - 20f), new Vector3(0.1f, 3.2f, 40f), lower);
    }

    // ================= 窓 =================
    void BuildWindows()
    {
        var alu = ToyKit.Lit(new Color(0.7f, 0.72f, 0.74f), 0.7f);
        float w = WinRight - WinLeft, h = WinTop - WinBottom;
        var center = new Vector3((WinLeft + WinRight) * 0.5f, (WinBottom + WinTop) * 0.5f, BackZ - 0.1f);
        Box(root, "WinFrameTop", center + new Vector3(0f, h * 0.5f, 0f), new Vector3(w, 0.4f, 0.5f), alu);
        Box(root, "WinFrameBottom", center + new Vector3(0f, -h * 0.5f, 0f), new Vector3(w, 0.4f, 0.5f), alu);
        for (int i = 0; i <= 3; i++)
            Box(root, "WinMullion", new Vector3(WinLeft + i * w / 3f, center.y, center.z), new Vector3(0.45f, h, 0.5f), alu);
        for (int i = 0; i < 3; i++)
        {
            float x = WinLeft + (i + 0.5f) * w / 3f;
            Box(root, "WinMeet", new Vector3(x, center.y, center.z - 0.08f), new Vector3(0.25f, h, 0.3f), alu);
        }
        Box(root, "WindowSill", center + new Vector3(0f, -h * 0.5f - 0.15f, -0.6f), new Vector3(w + 1f, 0.3f, 1.4f), ToyKit.Lit(new Color(0.75f, 0.72f, 0.66f), 0.3f));
        // 窓台に干したタオル
        var towel = ToyKit.Lit(new Color(0.95f, 0.95f, 0.9f), 0.2f);
        Box(root, "TowelOnSill", new Vector3(18f, WinBottom - 0.8f, BackZ - 0.9f), new Vector3(2.4f, 1.6f, 0.1f), towel);
        // 差し込む光の筋
        var tex = ShaftTexture();
        for (int i = 0; i < 4; i++)
        {
            var q = ToyKit.Primitive(PrimitiveType.Quad, "SunShaft", root);
            q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var m = ToyKit.ParticleMaterial(true, tex);
            m.SetColor("_BaseColor", new Color(1f, 0.9f, 0.7f, 0.09f + 0.03f * (i % 2)));
            q.GetComponent<Renderer>().sharedMaterial = m;
            q.localScale = new Vector3(4.5f + i * 0.8f, 20f, 1f);
            q.position = new Vector3(WinLeft + 5f + i * 8f, 9f, BackZ - 7f);
            q.rotation = Quaternion.Euler(35f, 0f, -12f);
        }
    }

    // ================= 横断幕・貼り紙 =================
    void BuildBanners()
    {
        if (font == null) return;
        // 窓の上の横断幕（白い布に墨の大きな字、四隅を留める）
        var cloth = ToyKit.Lit(new Color(0.96f, 0.94f, 0.88f), 0.1f);
        // 窓の上の壁に吊る（ロゴにかぶらない高さ）
        var bannerPos = new Vector3((WinLeft + WinRight) * 0.5f, 18.2f, BackZ - 0.6f);
        Box(root, "Banner", bannerPos, new Vector3(22f, 4.2f, 0.06f), cloth);
        Box(root, "BannerRed", bannerPos + new Vector3(0f, -1.85f, -0.02f), new Vector3(22f, 0.5f, 0.04f), ToyKit.Lit(new Color(0.8f, 0.12f, 0.1f), 0.2f));
        var big = TitleDressingKit.Text(root, font, banner, bannerPos + new Vector3(0f, 0.25f, -0.06f), new Vector2(20f, 3.4f), 40f,
                                        new Color(0.08f, 0.07f, 0.07f), new Color(0.08f, 0.07f, 0.07f), 0.1f, 0f);
        big.characterSpacing = 30f;
        // 貼り紙（左の壁の上と、窓の右）
        var spots = new[] { (new Vector3(-24.5f, 15.5f, BackZ - 0.3f), -3f), (new Vector3(24.8f, 12.5f, BackZ - 0.3f), 4f) };
        for (int i = 0; i < slogans.Length && i < spots.Length; i++)
        {
            var (pos, angle) = spots[i];
            var paper = Box(root, "Slogan", pos, new Vector3(2.6f, 5.5f, 0.04f), cloth);
            paper.rotation = Quaternion.Euler(0f, 0f, angle);
            var t = TitleDressingKit.Text(root, font, string.Join("\n", slogans[i].ToCharArray()), pos + new Vector3(0f, 0f, -0.04f), new Vector2(2.4f, 5.2f), 30f,
                                          new Color(0.75f, 0.08f, 0.06f), new Color(0.75f, 0.08f, 0.06f), 0.05f, angle);
            t.lineSpacing = -25f;
        }
    }

    // ================= へこんだロッカー（左） =================
    const int HammerLocker = 2;

    void BuildLockers()
    {
        var steel = ToyKit.Lit(new Color(0.5f, 0.56f, 0.58f), 0.45f);
        steel.SetTexture("_BaseMap", RustTexture());
        var dark = ToyKit.Lit(new Color(0.18f, 0.18f, 0.2f), 0.4f);
        var inside = ToyKit.Lit(new Color(0.2f, 0.2f, 0.22f), 0.3f);
        const int count = 4;
        const float w = 3.2f, h = 13f, d = 3f;
        var row = new GameObject("Lockers").transform;
        row.SetParent(root, false);
        // 左の壁にぴったり並べる（扉は部屋の内側 +X を向く）
        row.position = new Vector3(-SideX + 0.3f + d * 0.5f, 0f, 12f);
        for (int i = 0; i < count; i++)
        {
            var locker = new GameObject("Locker").transform;
            locker.SetParent(row, false);
            locker.localPosition = new Vector3(0f, 0f, i * (w + 0.1f));
            locker.localRotation = Quaternion.Euler(0f, -90f, (i == 2 ? 1.5f : 0f));   // 1つだけ少し傾いている
            bool open = i == 1 || i == HammerLocker;
            Box(locker, "Body", new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), open ? inside : steel);
            if (i == HammerLocker)
            {
                // ハンマーがずらっと入ったロッカー（扉は開いている）
                // 扉は奥側に開く（手前のカメラから中が見えるように）
                Box(locker, "Door", new Vector3(w * 0.5f + 0.6f, h * 0.5f, -d * 0.5f - 1.3f), new Vector3(0.12f, h - 0.3f, w - 0.2f), steel);
                var cols = new[] { Palette[0], Palette[4], Palette[3], Palette[2] };
                for (int k = 0; k < cols.Length; k++)
                {
                    var hm = ToyKit.CreateHammer();
                    hm.SetParent(locker, false);
                    hm.localScale = Vector3.one * 1.4f;
                    hm.localPosition = new Vector3(-1f + k * 0.65f, 1f, -0.2f);
                    hm.localRotation = Quaternion.Euler(-90f, 90f, 0f);   // 柄を下に、立てて入れる
                    foreach (var r in hm.GetComponentsInChildren<Renderer>())
                        if (r.name == "Head") r.sharedMaterial = ToyKit.Lit(cols[k], 0.5f);
                }
            }
            else if (open)
            {
                // 扉が開いて、中に部活Tシャツとタオル
                var door = Box(locker, "Door", new Vector3(w * 0.5f + 0.6f, h * 0.5f, -d * 0.5f - 1.3f), new Vector3(0.12f, h - 0.3f, w - 0.2f), steel);
                door.localRotation = Quaternion.Euler(0f, 0f, 0f);
                Box(locker, "Shirt", new Vector3(0f, h * 0.62f, -d * 0.2f), new Vector3(w * 0.75f, 3.2f, 0.2f), ToyKit.Lit(new Color(0.85f, 0.15f, 0.12f), 0.2f));
                Box(locker, "ShirtSleeves", new Vector3(0f, h * 0.62f + 1.2f, -d * 0.2f), new Vector3(w * 0.95f, 0.9f, 0.2f), ToyKit.Lit(new Color(0.85f, 0.15f, 0.12f), 0.2f));
                if (font != null)
                {
                    var num = TitleDressingKit.Text(locker, font, "7", Vector3.zero, new Vector2(1.5f, 1.5f), 6f, Color.white, Color.white, 0f, 0f);
                    num.transform.localPosition = new Vector3(0f, h * 0.6f, -d * 0.2f - 0.12f);
                    num.transform.localRotation = Quaternion.identity;
                }
                Box(locker, "Towel", new Vector3(w * 0.25f, h * 0.3f, -d * 0.3f), new Vector3(0.9f, 2.4f, 0.15f), ToyKit.Lit(new Color(0.9f, 0.88f, 0.8f), 0.2f));
            }
            else
            {
                Box(locker, "DoorLine", new Vector3(w * 0.5f - 0.05f, h * 0.5f, -d * 0.5f - 0.02f), new Vector3(0.06f, h - 0.4f, 0.04f), dark);
                foreach (float vy in new[] { h - 1.6f, 1.6f })
                    for (int k = 0; k < 4; k++)
                        Box(locker, "Vent", new Vector3(0f, vy - k * 0.3f, -d * 0.5f - 0.02f), new Vector3(w * 0.55f, 0.08f, 0.04f), dark);
                Box(locker, "Handle", new Vector3(w * 0.32f, h * 0.48f, -d * 0.5f - 0.12f), new Vector3(0.18f, 1.2f, 0.2f), dark);
                // へこみ（扉の暗い楕円）
                if (i % 2 == 0)
                {
                    var dent = ToyKit.Primitive(PrimitiveType.Sphere, "Dent", locker);
                    dent.localScale = new Vector3(1.2f, 0.9f, 0.05f);
                    dent.localPosition = new Vector3(-0.3f, h * 0.35f + i * 0.5f, -d * 0.5f - 0.01f);
                    dent.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.36f, 0.4f, 0.42f), 0.3f);
                }
            }
            // 「根性」のステッカー
            if (i == 3 && font != null)
            {
                Box(locker, "Sticker", new Vector3(-0.2f, h * 0.7f, -d * 0.5f - 0.03f), new Vector3(1.6f, 1.6f, 0.02f), ToyKit.Lit(new Color(0.95f, 0.85f, 0.2f), 0.4f));
                var st = TitleDressingKit.Text(locker, font, "根性", Vector3.zero, new Vector2(1.4f, 1.4f), 6f, new Color(0.7f, 0.08f, 0.06f), new Color(0.7f, 0.08f, 0.06f), 0f, 0f);
                st.transform.localPosition = new Vector3(-0.2f, h * 0.7f, -d * 0.5f - 0.06f);
                st.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            }
        }
        // ロッカーの上：スポーツバッグと段ボール
        var cardboard = ToyKit.Lit(new Color(0.7f, 0.54f, 0.36f), 0.2f);
        Box(row, "Cardboard", new Vector3(0f, h + 1f, 1.5f), new Vector3(2.8f, 2f, 2.6f), cardboard);
        var bag = ToyKit.Primitive(PrimitiveType.Capsule, "Bag", row);
        bag.localScale = new Vector3(1.6f, 2f, 1.6f);
        bag.localRotation = Quaternion.Euler(90f, 0f, 0f);
        bag.localPosition = new Vector3(0f, h + 0.8f, 6f);
        bag.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.15f, 0.2f, 0.45f), 0.4f);
    }

    // ================= ホワイトボード（練習メニュー） =================
    void BuildWhiteboard()
    {
        var center = new Vector3(-16.8f, 10.5f, BackZ - 0.35f);
        const float w = 12f, h = 7f;
        var alu = ToyKit.Lit(new Color(0.7f, 0.72f, 0.75f), 0.75f);
        Box(root, "BoardFrame", center + new Vector3(0f, 0f, 0.05f), new Vector3(w + 0.5f, h + 0.5f, 0.3f), alu);
        // 消し残しで少しくすんだ白
        var face = ToyKit.Lit(Color.white, 0.8f);
        face.SetTexture("_BaseMap", SmudgeTexture());
        Box(root, "BoardFace", center + new Vector3(0f, 0f, -0.12f), new Vector3(w, h, 0.05f), face);
        Box(root, "MarkerTray", center + new Vector3(0f, -h * 0.5f - 0.35f, -0.35f), new Vector3(w * 0.9f, 0.2f, 0.7f), alu);
        float z = center.z - 0.2f;
        if (font != null)
        {
            Write(boardTitle, new Vector3(center.x - 2.5f, center.y + 2.5f, z), new Vector2(6f, 1.2f), 10f, new Color(0.1f, 0.1f, 0.15f), -2f);
            for (int i = 0; i < boardNotes.Length; i++)
            {
                var t = Write("・" + boardNotes[i], new Vector3(center.x - 2.3f, center.y + 1.1f - i * 1f, z), new Vector2(6.5f, 0.9f), 7f, new Color(0.12f, 0.25f, 0.7f), 0f);
                t.alignment = TextAlignmentOptions.Left;
            }
            Write(boardGoal, new Vector3(center.x + 3.2f, center.y - 2.4f, z), new Vector2(5.2f, 1.2f), 9f, new Color(0.85f, 0.1f, 0.1f), 6f);
        }
        // 赤丸と「！」のなぐり書き
        var red = TitleDressingKit.Unlit(new Color(0.85f, 0.12f, 0.1f));
        Circle(new Vector3(center.x + 3.4f, center.y + 1.6f, z), 1.1f, red);
    }

    TextMeshPro Write(string text, Vector3 pos, Vector2 box, float size, Color c, float angle) =>
        TitleDressingKit.Text(root, font, text, pos, box, size, c, c, 0f, angle);

    void Circle(Vector3 center, float r, Material mat)
    {
        var l = new GameObject("Drawn").AddComponent<LineRenderer>();
        l.transform.SetParent(root, false);
        l.loop = true;
        l.positionCount = 32;
        for (int i = 0; i < 32; i++) { float a = i / 32f * Mathf.PI * 2f; l.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.8f, 0f)); }
        l.widthMultiplier = 0.12f;
        l.material = mat;
    }

    // ================= ハンマー =================
    void BuildHammers()
    {
        var wood = ToyKit.Lit(new Color(0.55f, 0.4f, 0.28f), 0.3f);
        var handle = ToyKit.Lit(new Color(0.85f, 0.72f, 0.5f), 0.35f);
        var tape = ToyKit.Lit(new Color(0.92f, 0.9f, 0.85f), 0.2f);
        // 右の壁のハンマー掛け（横木2本に、練習用ハンマーを立てて並べる）
        var rack = new Vector3(SideX - 0.6f, 0f, 14f);
        Box(root, "RackRailTop", rack + new Vector3(0f, 8.5f, 0f), new Vector3(0.6f, 0.4f, 11f), wood);
        Box(root, "RackRailBottom", rack + new Vector3(0f, 2.5f, 0f), new Vector3(0.6f, 0.4f, 11f), wood);
        var headColors = new[] { Palette[0], Palette[4], Palette[3], Palette[1], Palette[5] };
        for (int i = 0; i < 5; i++)
        {
            var hm = new GameObject("RackHammer").transform;
            hm.SetParent(root, false);
            hm.position = rack + new Vector3(-0.7f, 0f, -4.4f + i * 2.2f);
            hm.rotation = Quaternion.Euler(0f, 0f, i == 3 ? 4f : 0f);
            var stick = ToyKit.Primitive(PrimitiveType.Cylinder, "Handle", hm);
            stick.localScale = new Vector3(0.35f, 3.6f, 0.35f);
            stick.localPosition = new Vector3(0f, 4.4f, 0f);
            stick.GetComponent<Renderer>().sharedMaterial = handle;
            var grip = ToyKit.Primitive(PrimitiveType.Cylinder, "Tape", hm);
            grip.localScale = new Vector3(0.42f, 0.8f, 0.42f);
            grip.localPosition = new Vector3(0f, 1.6f, 0f);
            grip.GetComponent<Renderer>().sharedMaterial = tape;
            var head = ToyKit.Primitive(PrimitiveType.Cylinder, "Head", hm);
            head.localScale = new Vector3(1.3f, 1f, 1.3f);
            head.localRotation = Quaternion.Euler(0f, 0f, 90f);
            head.localPosition = new Vector3(0f, 8.3f, 0f);
            // 使い込んで色あせたヘッド
            head.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(Color.Lerp(headColors[i], new Color(0.5f, 0.45f, 0.4f), 0.3f), 0.3f);
        }
        // 立てかけた巨大ハンマー（奥の壁の右寄り、窓の脇）
        var giant = new GameObject("GiantHammer").transform;
        giant.SetParent(root, false);
        giant.position = new Vector3(14f, 0f, BackZ - 2.5f);
        giant.rotation = Quaternion.Euler(-14f, -20f, 8f);
        var gStick = ToyKit.Primitive(PrimitiveType.Cylinder, "Handle", giant);
        gStick.localScale = new Vector3(0.8f, 6f, 0.8f);
        gStick.localPosition = new Vector3(0f, 6f, 0f);
        gStick.GetComponent<Renderer>().sharedMaterial = handle;
        var gTape = ToyKit.Primitive(PrimitiveType.Cylinder, "Tape", giant);
        gTape.localScale = new Vector3(0.9f, 1.4f, 0.9f);
        gTape.localPosition = new Vector3(0f, 2f, 0f);
        gTape.GetComponent<Renderer>().sharedMaterial = tape;
        var gHead = ToyKit.Primitive(PrimitiveType.Cylinder, "Head", giant);
        gHead.localScale = new Vector3(3.2f, 2.6f, 3.2f);
        gHead.localRotation = Quaternion.Euler(0f, 0f, 90f);
        gHead.localPosition = new Vector3(0f, 12.5f, 0f);
        gHead.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.75f, 0.2f, 0.15f), 0.4f);
        foreach (float x in new[] { -2f, 2f })
        {
            var band = ToyKit.Primitive(PrimitiveType.Cylinder, "Band", giant);
            band.localScale = new Vector3(3.3f, 0.15f, 3.3f);
            band.localRotation = Quaternion.Euler(0f, 0f, 90f);
            band.localPosition = new Vector3(x, 12.5f, 0f);
            band.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.9f, 0.75f, 0.2f), 0.6f);
        }
        // 床に転がった練習用ハンマー
        var floorHammer = new GameObject("FloorHammer").transform;
        floorHammer.SetParent(root, false);
        floorHammer.position = new Vector3(-9f, 0.65f, 6f);
        floorHammer.rotation = Quaternion.Euler(0f, 40f, 90f);
        var fStick = ToyKit.Primitive(PrimitiveType.Cylinder, "Handle", floorHammer);
        fStick.localScale = new Vector3(0.35f, 2.6f, 0.35f);
        fStick.GetComponent<Renderer>().sharedMaterial = handle;
        var fHead = ToyKit.Primitive(PrimitiveType.Cylinder, "Head", floorHammer);
        fHead.localScale = new Vector3(1.3f, 0.6f, 1.3f);
        fHead.localPosition = new Vector3(0f, 2.8f, 0f);
        fHead.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(Color.Lerp(Palette[4], new Color(0.5f, 0.45f, 0.4f), 0.3f), 0.3f);
    }

    // ================= 練習用の積み木 =================
    void BuildBlocks()
    {
        var rng = new System.Random(7);
        // 角の積み木の山（欠けて色あせた）
        var pile = new Vector3(19f, 0f, 21f);
        for (int i = 0; i < 9; i++)
        {
            var b = Tumiki(root, "PracticeBlock", pile + new Vector3((float)(rng.NextDouble() - 0.5) * 4f, 0.45f + (i / 3) * 0.9f, (float)(rng.NextDouble() - 0.5) * 2.5f),
                        new Vector3(1.5f, 0.9f, 1.5f), ToyKit.Lit(Color.Lerp(Palette[i % Palette.Length], new Color(0.55f, 0.5f, 0.45f), 0.35f), 0.3f));
            b.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 60f - 30f, 0f);
        }
        // ×印のテープを貼った的（積み木を縦に積んだもの、左奥）
        var target = new Vector3(-2f, 0f, 18f);
        for (int i = 0; i < 5; i++)
        {
            var b = Tumiki(root, "TargetBlock", target + new Vector3(Mathf.Sin(i * 1.7f) * 0.15f, 0.45f + i * 0.9f, 0f), new Vector3(1.6f, 0.9f, 1.6f),
                        ToyKit.Lit(Color.Lerp(Palette[(i + 2) % Palette.Length], new Color(0.55f, 0.5f, 0.45f), 0.3f), 0.3f));
            b.rotation = Quaternion.Euler(0f, i * 6f - 10f, 0f);
        }
        var tapeMat = ToyKit.Lit(new Color(0.95f, 0.9f, 0.3f), 0.2f);
        foreach (float a in new[] { 40f, -40f })
        {
            var t = Box(root, "TargetTape", target + new Vector3(0f, 2.4f, -0.83f), new Vector3(0.25f, 4f, 0.02f), tapeMat);
            t.rotation = Quaternion.Euler(0f, 0f, a);
        }
        // 床に散らばった積み木
        for (int i = 0; i < 4; i++)
        {
            var b = Tumiki(root, "LooseBlock", new Vector3(-6f + i * 4.5f + (float)rng.NextDouble(), 0.45f, 9f + (float)rng.NextDouble() * 4f), new Vector3(1.5f, 0.9f, 1.5f),
                        ToyKit.Lit(Color.Lerp(Palette[(i * 2) % Palette.Length], new Color(0.55f, 0.5f, 0.45f), 0.35f), 0.3f));
            b.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 90f, i == 2 ? 90f : 0f);
        }
    }

    // ================= ベンチ・タイヤ・給水ジャグ =================
    void BuildBenchAndGear()
    {
        var wood = ToyKit.Lit(new Color(0.6f, 0.45f, 0.3f), 0.3f);
        var leg = ToyKit.Lit(new Color(0.3f, 0.3f, 0.32f), 0.6f);
        // 長いベンチ（右手前）
        var bench = new GameObject("Bench").transform;
        bench.SetParent(root, false);
        bench.position = new Vector3(21f, 0f, 16f);
        bench.rotation = Quaternion.Euler(0f, -20f, 0f);
        Box(bench, "Seat", new Vector3(0f, 2f, 0f), new Vector3(9f, 0.35f, 1.8f), wood);
        foreach (float x in new[] { -3.8f, 3.8f }) Box(bench, "Leg", new Vector3(x, 1f, 0f), new Vector3(0.3f, 2f, 1.4f), leg);
        Box(bench, "TowelOnBench", new Vector3(-2f, 2.25f, 0f), new Vector3(2.2f, 0.15f, 1.6f), ToyKit.Lit(new Color(0.95f, 0.92f, 0.85f), 0.2f));
        // 給水ジャグ（黄色い大きな水筒）
        var jug = ToyKit.Primitive(PrimitiveType.Cylinder, "WaterJug", bench);
        jug.localScale = new Vector3(1.4f, 0.9f, 1.4f);
        jug.localPosition = new Vector3(2.5f, 3.05f, 0f);
        jug.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.98f, 0.8f, 0.15f), 0.5f);
        var lid = ToyKit.Primitive(PrimitiveType.Cylinder, "JugLid", bench);
        lid.localScale = new Vector3(1.45f, 0.15f, 1.45f);
        lid.localPosition = new Vector3(2.5f, 4f, 0f);
        lid.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.85f, 0.2f, 0.15f), 0.5f);
        // 練習用のタイヤ（床に寝かせて、上にもう1本）
        var rubber = ToyKit.Lit(new Color(0.12f, 0.12f, 0.13f), 0.35f);
        foreach (var (pos, tilt) in new[] { (new Vector3(-5f, 0.5f, 2f), 0f), (new Vector3(-4.6f, 1.4f, 2.2f), 8f) })
        {
            var tire = ToyKit.Primitive(PrimitiveType.Cylinder, "Tire", root);
            tire.localScale = new Vector3(3.6f, 0.45f, 3.6f);
            tire.position = pos;
            tire.rotation = Quaternion.Euler(tilt, 0f, 0f);
            tire.GetComponent<Renderer>().sharedMaterial = rubber;
            var hole = ToyKit.Primitive(PrimitiveType.Cylinder, "TireHole", root);
            hole.localScale = new Vector3(1.9f, 0.47f, 1.9f);
            hole.position = pos;
            hole.rotation = tire.rotation;
            hole.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.05f, 0.05f, 0.05f), 0.2f);
        }
        // 泥だらけのスニーカー（ロッカーの前）
        var shoe = ToyKit.Lit(new Color(0.6f, 0.55f, 0.48f), 0.3f);
        foreach (var (x, z, yaw) in new[] { (-9f, 2f, 20f), (-7.8f, 2.6f, -10f) })
        {
            var s = Box(root, "Sneaker", new Vector3(x, 0.4f, z), new Vector3(1f, 0.8f, 2.2f), shoe);
            s.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }

    // ================= 天井の蛍光灯（1本だけちらつく） =================
    void BuildCeiling()
    {
        Box(root, "Ceiling", new Vector3(0f, Height + 0.2f, BackZ - 20f), new Vector3(SideX * 2f, 0.4f, 40f), ToyKit.Lit(new Color(0.85f, 0.83f, 0.78f), 0.2f));
        var holder = ToyKit.Lit(new Color(0.85f, 0.85f, 0.82f), 0.5f);
        int n = 0;
        foreach (float x in new[] { -12f, 0f, 12f })
            foreach (float z in new[] { 8f, 18f })
            {
                Box(root, "LampHolder", new Vector3(x, Height - 0.3f, z), new Vector3(1.4f, 0.4f, 7f), holder);
                var tube = TitleDressingKit.Unlit(new Color(1.25f, 1.25f, 1.15f));
                Box(root, "LampTube", new Vector3(x, Height - 0.6f, z), new Vector3(0.4f, 0.3f, 6.4f), tube);
                if (n++ == 4) { flickerTube = tube; flickerBase = tube.GetColor("_BaseColor"); }
            }
    }

    void BuildDust()
    {
        TitleDressingKit.Drift(root, "Dust", new Vector3(4f, 7f, 14f), new Vector3(34f, 12f, 18f), DotTexture(), true,
                               new Color(1f, 0.92f, 0.75f, 0.45f), new Color(1f, 0.85f, 0.65f, 0.25f), new Vector2(0.05f, 0.12f), 18f,
                               new Vector3(-0.15f, -0.05f, -0.1f), new Vector3(0.15f, 0.1f, 0.1f));
    }

    // ================= 作業台（だるま落としの演出の舞台） =================
    // 演出（積み木の山・ロゴ・ボタン）は原点まわり、高さ 0 で行われるので、台の上面を y = 0 にする
    void BuildTable()
    {
        var table = new GameObject("WorkTable").transform;
        float floorY = roomOffset.y;
        var wood = ToyKit.Lit(new Color(0.6f, 0.44f, 0.3f), 0.35f);
        var dark = ToyKit.Lit(new Color(0.4f, 0.3f, 0.22f), 0.3f);
        var top = Box(table, "Top", new Vector3(0.8f, -0.35f, 1.5f), new Vector3(24f, 0.7f, 15f), wood);
        top.position = new Vector3(0.8f, -0.35f, 1.5f);
        foreach (float x in new[] { -10.5f, 12f })
            foreach (float z in new[] { -5f, 8f })
            {
                var leg = Box(table, "Leg", Vector3.zero, new Vector3(1f, -floorY - 0.7f, 1f), dark);
                leg.position = new Vector3(x, (floorY - 0.7f) * 0.5f, z);
            }
        // 使い込んだ練習マット（緑、四隅がすり切れている）と、テープの跡
        var mat = TitleDressingKit.Quad(table, "PracticeMat", new Vector3(0.6f, 0.01f, 1f), new Vector3(15f, 9f, 1f), ToyKit.Lit(new Color(0.25f, 0.42f, 0.3f), 0.2f));
        mat.rotation = Quaternion.Euler(90f, 0f, 0f);
        mat.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        var tapeMat = ToyKit.Lit(new Color(0.92f, 0.88f, 0.6f), 0.2f);
        foreach (var (p, yaw) in new[] { (new Vector3(-8f, 0.02f, -3.5f), 20f), (new Vector3(9.5f, 0.02f, 6f), -35f) })
        {
            var tp = Box(table, "Tape", Vector3.zero, new Vector3(2.2f, 0.02f, 0.5f), tapeMat);
            tp.position = p;
            tp.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }

    // ================= 部員（頭＋積み木の体） =================
    // 窓の外で素振りする部員と、その奥で走り込みする部員
    class Member
    {
        public Transform root, head, hammerPivot, hammer;
        public Vector3 basePos;
    }

    Member swinger, runner;

    Member CreateMember(Vector3 pos, float yaw, Color bodyA, Color bodyB, float hammerScale)
    {
        float s = memberScale;
        var m = new Member { basePos = pos };
        m.root = new GameObject("Member").transform;
        m.root.position = pos;
        m.root.rotation = Quaternion.Euler(0f, yaw, 0f);
        float bh = ToyKit.BlockH * s;
        var cols = new[] { bodyA, bodyB };
        for (int i = 0; i < 2; i++)
        {
            var b = ToyKit.Primitive(PrimitiveType.Cylinder, "BodyBlock", m.root);   // 本編と同じ円柱の積み木
            b.localScale = new Vector3(ToyKit.BlockW * s * 0.85f, bh * 0.5f, ToyKit.BlockD * s * 0.85f);
            b.localPosition = new Vector3(0f, bh * (i + 0.5f), 0f);
            b.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(cols[i], 0.5f);
        }
        var face = ToyKit.CreateHead();
        m.head = face.root;
        m.head.SetParent(m.root, false);
        m.head.localScale = Vector3.one * s;
        m.head.localPosition = new Vector3(0f, bh * 2f + ToyKit.HeadR * s, 0f);
        m.head.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 顔（-Z）を体の前（+Z）へ
        // 手の位置にハンマーの根元（体の右側）
        m.hammerPivot = new GameObject("HammerPivot").transform;
        m.hammerPivot.SetParent(m.root, false);
        m.hammerPivot.localPosition = new Vector3(ToyKit.BlockW * s * 0.45f, bh * 1.4f, 0.2f * s);
        m.hammer = ToyKit.CreateHammer();
        m.hammer.SetParent(m.hammerPivot, false);
        m.hammer.localScale = Vector3.one * s * hammerScale;
        return m;
    }

    void BuildMembers()
    {
        float yard = roomOffset.y + YardY;   // 校庭の地面の高さ
        // 窓の外：素振り（こちらを向いて、横に振る）
        swinger = CreateMember(new Vector3(-3f, yard, BackZ + 10f), 180f, Palette[0], new Color(0.95f, 0.95f, 0.92f), 0.8f);
        // さらに奥：走り込み（左右に往復）
        runner = CreateMember(new Vector3(0f, yard, BackZ + 22f), 90f, Palette[4], new Color(0.95f, 0.95f, 0.92f), 0.6f);
        runner.hammer.gameObject.SetActive(false);
    }

    // 部員のアニメーション
    void UpdateMembers(float t)
    {
        if (swinger != null)
        {
            // 素振り：1.3 秒ごとに、構え → 一気に振り抜く → ゆっくり戻す
            float c = Mathf.Repeat(t, 1.3f);
            float yaw = c < 0.18f ? Mathf.Lerp(-110f, 80f, Mathf.SmoothStep(0f, 1f, c / 0.18f)) : Mathf.Lerp(80f, -110f, Mathf.SmoothStep(0f, 1f, (c - 0.18f) / 1.12f));
            swinger.hammerPivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            swinger.root.position = swinger.basePos + Vector3.up * (c < 0.25f ? Mathf.Sin(c / 0.25f * Mathf.PI) * 0.4f : 0f);
            swinger.head.localRotation = Quaternion.Euler(0f, 180f + yaw * 0.15f, 0f);
        }
        if (runner != null)
        {
            // 走り込み：左右に往復。走るリズムで上下にはずむ
            float span = 26f, speed = 7f;
            float p = Mathf.PingPong(t * speed, span);
            bool right = Mathf.Repeat(t * speed, span * 2f) < span;
            runner.root.position = runner.basePos + new Vector3(p - span * 0.5f, Mathf.Abs(Mathf.Sin(t * 9f)) * 0.8f, 0f);
            runner.root.rotation = Quaternion.Euler(0f, right ? 90f : -90f, Mathf.Sin(t * 9f) * 4f);
        }
    }

    // ================= 動き =================
    void Update()
    {
        float t = Time.unscaledTime;
        for (int i = 0; i < clouds.Count; i++)
            clouds[i].localPosition = cloudBase[i] + new Vector3(Mathf.Repeat(t * 0.4f + i * 3f, 30f) - 15f, 0f, 0f);
        UpdateMembers(t);
        if (flickerTube != null)
        {
            float n = Mathf.PerlinNoise(t * 6f, 0.3f);
            flickerTube.SetColor("_BaseColor", n < 0.25f ? flickerBase * 0.25f : flickerBase);
        }
    }

    // ================= 部品 =================
    // 本編と同じ円柱の積み木（size は 幅・高さ・奥行き。円柱の高さは 2 なので半分にする）
    Transform Tumiki(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        var b = ToyKit.Primitive(PrimitiveType.Cylinder, name, parent);
        if (parent == root) b.position = pos; else b.localPosition = pos;
        b.localScale = new Vector3(size.x, size.y * 0.5f, size.z);
        b.GetComponent<Renderer>().sharedMaterial = mat;
        return b;
    }

    Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        var b = ToyKit.Primitive(PrimitiveType.Cube, name, parent);
        if (parent == root) b.position = pos; else b.localPosition = pos;
        b.localScale = size;
        b.GetComponent<Renderer>().sharedMaterial = mat;
        return b;
    }

    // ================= テクスチャ（汚れ） =================
    Texture2D DirtyFloorTexture()
    {
        const int s = 512;
        var tex = TitleDressingKit.NewTexture(s, s, true);
        var px = new Color[s * s];
        var rng = new System.Random(3);
        // 板の継ぎ目とまだら
        for (int y = 0; y < s; y++)
        {
            int plank = y / 40;
            float tone = 0.88f + (float)new System.Random(plank * 13 + 2).NextDouble() * 0.14f;
            for (int x = 0; x < s; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.01f, y * 0.01f) * 0.18f + Mathf.PerlinNoise(x * 0.08f, y * 0.3f) * 0.06f;
                float joint = y % 40 < 2 || (x + plank * 97) % s < 2 ? 0.7f : 1f;
                var c = Color.Lerp(floorColor, new Color(0.7f, 0.55f, 0.4f), 0.4f) * (tone + n - 0.1f) * joint;
                c.a = 1f;
                px[y * s + x] = c;
            }
        }
        var mud = new Color(0.36f, 0.27f, 0.18f);
        // 泥のしみ
        for (int i = 0; i < 26; i++)
            TitleDressingKit.Blob(px, s, s, (float)rng.NextDouble() * s, (float)rng.NextDouble() * s, 8f + (float)rng.NextDouble() * 30f, mud, 0.35f, 0.1f);
        // 足跡（2列）
        for (int k = 0; k < 2; k++)
        {
            var p = new Vector2(80f + k * 260f, 40f);
            float dir = 1.2f + k * 0.4f;
            for (int i = 0; i < 9; i++)
            {
                var side = new Vector2(-Mathf.Sin(dir), Mathf.Cos(dir)) * (i % 2 == 0 ? 8f : -8f);
                var c = p + side;
                TitleDressingKit.Blob(px, s, s, c.x, c.y, 7f, mud, 0.55f, 0.6f);
                TitleDressingKit.Blob(px, s, s, c.x + Mathf.Cos(dir) * 11f, c.y + Mathf.Sin(dir) * 11f, 5f, mud, 0.55f, 0.6f);
                p += new Vector2(Mathf.Cos(dir), Mathf.Sin(dir)) * 26f;
                dir += ((float)rng.NextDouble() - 0.5f) * 0.3f;
            }
        }
        // すり傷
        for (int i = 0; i < 30; i++)
        {
            var a = new Vector2((float)rng.NextDouble() * s, (float)rng.NextDouble() * s);
            var b = a + new Vector2((float)rng.NextDouble() * 40f - 20f, (float)rng.NextDouble() * 10f - 5f);
            TitleDressingKit.Stroke(px, s, s, a, b, 1.5f, new Color(0.4f, 0.32f, 0.25f), 0.4f);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    Texture2D DirtyWallTexture()
    {
        const int s = 256;
        var tex = TitleDressingKit.NewTexture(s, s, true);
        var px = new Color[s * s];
        var rng = new System.Random(8);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.03f, y * 0.03f) * 0.1f;
                // 下ほどすすけている
                float grime = Mathf.Lerp(0.82f, 1f, y / (s - 1f));
                var c = wallColor * (0.95f + n) * grime;
                c.a = 1f;
                px[y * s + x] = c;
            }
        // しみ（雨じみのような縦の流れ）と、すり傷
        var stain = new Color(0.55f, 0.48f, 0.38f);
        for (int i = 0; i < 8; i++)
        {
            float x = (float)rng.NextDouble() * s, y = s * (0.5f + (float)rng.NextDouble() * 0.5f);
            TitleDressingKit.Blob(px, s, s, x, y, 10f + (float)rng.NextDouble() * 14f, stain, 0.25f, 0.1f);
            TitleDressingKit.Stroke(px, s, s, new Vector2(x, y), new Vector2(x + (float)rng.NextDouble() * 4f, y - 30f - (float)rng.NextDouble() * 50f), 4f, stain, 0.15f);
        }
        for (int i = 0; i < 14; i++)
        {
            var a = new Vector2((float)rng.NextDouble() * s, (float)rng.NextDouble() * s * 0.5f);
            TitleDressingKit.Stroke(px, s, s, a, a + new Vector2((float)rng.NextDouble() * 30f, (float)rng.NextDouble() * 6f - 3f), 1.2f, new Color(0.35f, 0.3f, 0.25f), 0.4f);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Texture2D RustTexture()
    {
        const int s = 128;
        var tex = TitleDressingKit.NewTexture(s, s, true);
        var px = new Color[s * s];
        var rng = new System.Random(5);
        for (int i = 0; i < px.Length; i++) { float n = 0.92f + (float)rng.NextDouble() * 0.08f; px[i] = new Color(n, n, n, 1f); }
        var rust = new Color(0.6f, 0.35f, 0.2f);
        for (int i = 0; i < 18; i++)
            TitleDressingKit.Blob(px, s, s, (float)rng.NextDouble() * s, (float)rng.NextDouble() * s * 0.4f, 3f + (float)rng.NextDouble() * 8f, rust, 0.5f, 0.2f);
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Texture2D SmudgeTexture()
    {
        const int s = 128;
        var tex = TitleDressingKit.NewTexture(s, s);
        var px = new Color[s * s];
        TitleDressingKit.Fill(px, Color.white);
        var rng = new System.Random(2);
        for (int i = 0; i < 10; i++)
            TitleDressingKit.Blob(px, s, s, (float)rng.NextDouble() * s, (float)rng.NextDouble() * s, 10f + (float)rng.NextDouble() * 20f, new Color(0.7f, 0.72f, 0.8f), 0.18f, 0f);
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Texture2D SkyTexture()
    {
        const int w = 64, h = 128;
        var tex = TitleDressingKit.NewTexture(w, h);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            var c = Color.Lerp(new Color(0.8f, 0.9f, 1f), new Color(0.25f, 0.55f, 0.98f), Mathf.Pow(y / (h - 1f), 0.8f));
            for (int x = 0; x < w; x++) px[y * w + x] = c;
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Texture2D CloudTexture()
    {
        const int w = 128, h = 64;
        var tex = TitleDressingKit.NewTexture(w, h);
        var px = new Color[w * h];
        TitleDressingKit.Fill(px, new Color(1f, 1f, 1f, 0f));
        foreach (var (cx, cy, r) in new[] { (30, 26, 18f), (55, 34, 24f), (82, 30, 20f), (100, 24, 14f), (64, 20, 18f) })
            TitleDressingKit.Blob(px, w, h, cx, cy, r, Color.white, 1f, 0.8f);
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Texture2D ShaftTexture()
    {
        const int w = 32, h = 128;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float sx = Mathf.Sin((x + 0.5f) / w * Mathf.PI), sy = Mathf.Sin((y + 0.5f) / h * Mathf.PI);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, sx * sx * sy));
            }
        tex.Apply();
        return tex;
    }

    static Texture2D DotTexture()
    {
        const int s = 16;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(8f, 8f)) / 8f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), 2f)));
            }
        tex.Apply();
        return tex;
    }
}
