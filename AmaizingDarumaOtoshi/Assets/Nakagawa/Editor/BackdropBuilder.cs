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
    // ステージの背景(StageBackdrop のプレハブ)を Assets/Nakagawa/Backdrops に作る。背景を増やすときは Definitions に足す
    // 背景は当たり判定のない飾りだけで作り、ステージの外(床の外側・下)に置く
    // ステージへの紐づけは StageEntry の「Backdrop」(まとめたシーン)か、StageBuilder の定義(ステージのシーン)で行う
    public static class BackdropBuilder
    {
        public const string kBackdropDir = "Assets/Nakagawa/Backdrops";
        const string kMaterialDir = kBackdropDir + "/Materials";

        [MenuItem("Nakagawa/Backdrops/Build All Backdrops")]
        public static void BuildAll()
        {
            var names = new List<string>();
            foreach (var def in Definitions())
            {
                Build(def);
                names.Add($"{def.Id} {def.Title}");
            }
            AssetDatabase.SaveAssets();
            // 作り直したプレハブは中の物が入れ替わるので、置いてある背景はステージに合わせ直す必要がある
            Debug.Log($"[BackdropBuilder] 背景を {names.Count} 個作りました({kBackdropDir})。" +
                      "AllStages を開いて「Nakagawa > Backdrops > Apply Backdrops To Open Scene」を実行し、置いてある背景を置き直してください\n" + string.Join("\n", names));
        }

        public static string PrefabPath(string id) => $"{kBackdropDir}/Backdrop_{id}.prefab";

        // id の背景のプレハブ。なければ定義から作る(定義もなければ null)
        public static StageBackdrop LoadOrBuild(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var prefab = AssetDatabase.LoadAssetAtPath<StageBackdrop>(PrefabPath(id));
            if (prefab != null) return prefab;
            foreach (var def in Definitions())
            {
                if (def.Id == id) return Build(def);
            }
            Debug.LogError($"[BackdropBuilder] 背景 {id} の定義がありません");
            return null;
        }

        //========================================
        // ステージへの配置
        //========================================

        // まとめたシーンなど、開いているシーンの各ステージ(StageEntry)に、紐づけた背景を置き直す
        // 紐づけを外したステージからは背景を取り除く
        [MenuItem("Nakagawa/Backdrops/Apply Backdrops To Open Scene")]
        public static void ApplyToOpenScene()
        {
            int placed = 0, removed = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var entry in root.GetComponentsInChildren<StageEntry>(true))
                    {
                        if (ApplyToEntry(entry)) placed++;
                        else removed++;
                    }
                }
                EditorSceneManager.MarkSceneDirty(scene);
            }
            Debug.Log($"[BackdropBuilder] 背景を {placed} ステージに置きました(背景なし {removed} ステージ)。シーンを保存してください");
        }

        // ステージ1つ分。背景を置いたら true
        public static bool ApplyToEntry(StageEntry entry)
        {
            var area = entry.StageArea;
            Vector3 center = area != null ? area.transform.position : entry.transform.position;
            Vector2 size = area != null ? AreaSize(area) : new Vector2(20, 20);
            return Place(entry.gameObject.scene, entry.transform, entry.Backdrop, center, size) != null;
        }

        // parent(ステージのルート。null ならシーンの直下)に背景を置き、stageRoot の床に合わせる
        // 置いてあった背景は取り除く。prefab が null なら取り除くだけ
        public static StageBackdrop Place(Scene scene, Transform parent, StageBackdrop prefab, Vector3 center, Vector2 size, Transform stageRoot = null)
        {
            foreach (var old in FindPlaced(scene, parent)) Object.DestroyImmediate(old.gameObject);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, scene);
            go.name = "Backdrop";
            if (parent != null) go.transform.SetParent(parent, false);
            var backdrop = go.GetComponent<StageBackdrop>();
            backdrop.Fit(stageRoot != null ? stageRoot : parent, center, size);
            return backdrop;
        }

        // parent の直下(null ならシーンの直下)に置かれた背景
        static List<StageBackdrop> FindPlaced(Scene scene, Transform parent)
        {
            var list = new List<StageBackdrop>();
            if (parent != null)
            {
                foreach (Transform child in parent)
                {
                    if (child.TryGetComponent<StageBackdrop>(out var b)) list.Add(b);
                }
            }
            else
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.TryGetComponent<StageBackdrop>(out var b)) list.Add(b);
                }
            }
            return list;
        }

        public static Vector2 AreaSize(StageArea area)
        {
            var so = new SerializedObject(area);
            return so.FindProperty("m_size").vector2Value;
        }

        //========================================
        // 背景の定義
        //========================================

        class BackdropDef
        {
            public string Id, Title, Description;
            public Action<Ctx> Build;
        }

        static List<BackdropDef> Definitions()
        {
            return new List<BackdropDef>
            {
                new BackdropDef
                {
                    Id = "Chanoma",
                    Title = "お茶の間の机の上",
                    Description = "畳の部屋のちゃぶ台の上。ステージは天板の上に積んだ本の上にあり、まわりには湯呑み・急須・みかん・せんべい・新聞・リモコンが置いてある。",
                    Build = BuildChanoma,
                },
                new BackdropDef
                {
                    Id = "Kitchen",
                    Title = "台所の調理台",
                    Description = "台所の大理石の調理台の上。ステージは重ねたまな板の上にあり、まわりには流し・鍋・トマト・にんじん・キャベツ・塩こしょう・おたま・ふきんが置いてある。",
                    Build = BuildKitchen,
                },
                new BackdropDef
                {
                    Id = "Desk",
                    Title = "子ども部屋の勉強机",
                    Description = "勉強机に敷いたカッティングマットの上。ステージは積んだノートの上にあり、まわりには鉛筆・消しゴム・定規・筆箱・電気スタンド・目覚まし時計・付箋が置いてある。",
                    Build = BuildDesk,
                },
                new BackdropDef
                {
                    Id = "Engawa",
                    Title = "夏の縁側",
                    Description = "庭に面した縁側の板の間。ステージは積んだ座布団の上にあり、まわりにはスイカ・蚊遣り豚・蚊取り線香・うちわ・麦茶・朝顔の鉢が置いてある。",
                    Build = BuildEngawa,
                },
                new BackdropDef
                {
                    Id = "Bath",
                    Title = "お風呂場",
                    Description = "タイル張りのお風呂場。ステージは重ねた風呂ふたの上にあり、まわりには湯船・洗面器・風呂いす・アヒル・石けん・シャンプー・スポンジが置いてある。",
                    Build = BuildBath,
                },
                new BackdropDef
                {
                    Id = "Monja",
                    Title = "もんじゃ焼き屋の鉄板",
                    Description = "下町のもんじゃ焼き屋のテーブル。ステージは大きな鉄板に重ねた鉄の板の上にあり、まわりには土手を作ったもんじゃ・小さなヘラ・具の入ったボウル・ソース・青のり・ラムネ・取り皿が置いてあり、奥の壁には品書きの札と提灯が下がっている。",
                    Build = BuildMonja,
                },
                new BackdropDef
                {
                    Id = "Clubroom",
                    Title = "だるま落とし部の部室",
                    Description = "部室の長机の上。ステージは積んだスコアブックの上にあり、まわりには練習用のだるま落とし・木槌・優勝カップ・スポーツドリンク・タオル・記録用のバインダー・ストップウォッチが置いてあり、奥の壁にはホワイトボードと部旗が掛かっている。",
                    Build = BuildClubroom,
                },
                new BackdropDef
                {
                    Id = "Ground",
                    Title = "放課後の運動場",
                    Description = "石灰でトラックの線を引いた運動場の土の上。ステージは跳び箱の上にあり、まわりにはサッカーボール・コーン・ハードル・ライン引き・バットとボール・ベースが置いてあり、奥にはバックネットが立っている。",
                    Build = BuildGround,
                },
                new BackdropDef
                {
                    Id = "Dojo",
                    Title = "老舗の道場",
                    Description = "板張りの道場。ステージは積んだ畳の上にあり、まわりには大きなだるま・太鼓・竹刀掛け・水桶・木刀・座布団・書道の半紙が置いてあり、奥の壁には掛け軸と名札掛けが掛かっている。",
                    Build = BuildDojo,
                },
                new BackdropDef
                {
                    Id = "Stadium",
                    Title = "満員のスタジアム",
                    Description = "芝生のスタジアムの中央に組んだ特設ステージ。まわりにはテレビカメラ・表彰台・コーナーフラッグ・応援幕があり、奥には観客で埋まったスタンドと照明塔が並んでいる。",
                    Build = BuildStadium,
                },
                new BackdropDef
                {
                    Id = "Slope",
                    Title = "雪のゲレンデ",
                    Description = "雪の積もったゲレンデ。ステージは積んだ雪と氷のブロックの上にあり、まわりにはもみの木・雪だるま・雪に刺したスキー板・旗門・そりがあり、奥には雪山が連なっている。",
                    Build = BuildSlope,
                },
                new BackdropDef
                {
                    Id = "Cruise",
                    Title = "豪華客船のデッキ",
                    Description = "チーク材を張った豪華客船のデッキ。ステージは積んだ船体の板の上にあり、まわりにはプール・デッキチェア・パラソル・トロピカルドリンクのテーブル・浮き輪・ロープがあり、奥の手すりの先には海と島が見える。",
                    Build = BuildCruise,
                },
                new BackdropDef
                {
                    Id = "Ruins",
                    Title = "ジャングルの遺跡",
                    Description = "密林に眠る石畳の遺跡。ステージは積んだ石のブロックの上にあり、まわりには石の頭像・たいまつ・折れた柱・倒れた柱・宝箱・密林の草があり、奥には階段状の神殿がそびえている。",
                    Build = BuildRuins,
                },
                new BackdropDef
                {
                    Id = "Space",
                    Title = "宇宙ステーション",
                    Description = "金属のパネルを張った宇宙ステーションの区画。ステージは積んだ金属の板の上にあり、まわりには操作盤・酸素ボンベ・ロボットアーム・宇宙服のヘルメット・コンテナ・ケーブルがあり、奥の大窓からは星空と地球が見える。",
                    Build = BuildSpace,
                },
            };
        }

        // 円すい(半径を少しずつ細くした円柱を積む)。位置は底面の中心。色は段ごとに順に使う
        static void Cone(Ctx c, Transform g, string name, Vector3 basePos, float radius, float height, int steps, params Material[] mats)
        {
            float h = height / steps;
            for (int i = 0; i < steps; i++)
            {
                float r = radius * (1.0f - i / (float)steps * 0.85f);
                c.Cylinder(g, $"{name}_{i}", basePos + new Vector3(0, h * (i + 0.5f), 0), r, h, mats[i % mats.Length]);
            }
        }

        // 輪(箱を円周に並べる)。位置は輪の中心。色は輪を mats の数で等分して順に使う
        static void Ring(Ctx c, Transform g, string name, Vector3 center, float radius, int count, float width, float height, params Material[] mats)
        {
            float len = 2 * Mathf.PI * radius / count * 1.08f;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2 / count;
                Vector3 p = center + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                var rot = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)));
                c.Prim(PrimitiveType.Cube, g, $"{name}_{i}", p, new Vector3(width, height, len), rot, mats[i * mats.Length / count]);
            }
        }

        // 床の下の支え(板・本を積んだ山)の見た目を設定する
        static void SetSupports(Ctx c, StageBackdrop.SupportStyle style, float min, float max, Material page, params Material[] covers)
        {
            var s = c.Backdrop.Supports;
            s.Style = style;
            s.BottomY = kTableTop;
            s.MinPieceHeight = min;
            s.MaxPieceHeight = max;
            s.CoverMaterials = covers;
            s.PageMaterial = page;
        }

        // 遠くの床(落ちた物の先に見える部屋の床)
        static void FarFloor(Ctx c, string name, Material mat)
        {
            var g = c.Group("FarFloor");
            c.Box(g, name, new Vector3(0, kTableTop - 37.5f, 0), new Vector3(720, 1, 720), 0, mat);
        }

        //----------------------------------------
        // 台所の調理台
        //----------------------------------------
        static void BuildKitchen(Ctx c)
        {
            float T = kTableTop;
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 0.8f, 1.6f, null,
                c.Mat("BoardLight", new Color(0.86f, 0.72f, 0.52f), 0.3f),
                c.Mat("BoardDark", new Color(0.66f, 0.47f, 0.3f), 0.3f),
                c.Mat("BoardWhite", new Color(0.93f, 0.93f, 0.9f), 0.4f));
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.9f, 0.8f, 0.62f), 0.3f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.55f, 0.6f, 0.66f), 0.7f);

            // 調理台(大理石)と台所の床
            var counter = c.Group("Counter");
            c.Box(counter, "Top", new Vector3(0, T - 2, 20), new Vector3(220, 4, 160), 0, c.Mat("Marble", new Color(0.86f, 0.86f, 0.84f), 0.75f));
            c.Box(counter, "Front", new Vector3(0, T - 20, -60.5f), new Vector3(220, 32, 1), 0, c.Mat("Cabinet", new Color(0.93f, 0.9f, 0.84f), 0.4f));
            FarFloor(c, "KitchenFloor", c.Mat("KitchenFloor", new Color(0.58f, 0.42f, 0.28f), 0.35f));

            var steel = c.Mat("Steel", new Color(0.72f, 0.74f, 0.77f), 0.85f, 0.8f);
            var steelDark = c.Mat("SteelDark", new Color(0.42f, 0.44f, 0.48f), 0.8f, 0.8f);

            // 流し(北西)
            {
                var g = c.Anchored("Sink", new Vector2(-0.8f, 1), 14);
                c.Box(g, "Frame", new Vector3(0, T + 0.3f, 0), new Vector3(22, 0.6f, 16), 0, steel);
                c.Box(g, "Basin", new Vector3(0, T + 0.63f, 0), new Vector3(18, 0.06f, 12), 0, steelDark);
                c.Cylinder(g, "Drain", new Vector3(0, T + 0.7f, 0), 1.2f, 0.1f, c.Mat("Drain", new Color(0.2f, 0.2f, 0.22f), 0.6f, 0.6f));
                c.Cylinder(g, "FaucetBase", new Vector3(0, T + 2.6f, 9.5f), 0.8f, 4.6f, steel);
                c.Prim(PrimitiveType.Capsule, g, "Spout", new Vector3(0, T + 4.8f, 7.2f), new Vector3(0.8f, 2.6f, 0.8f), Quaternion.Euler(90, 0, 0), steel);
                c.Box(g, "Lever", new Vector3(0, T + 5.2f, 9.8f), new Vector3(0.6f, 0.4f, 2.4f), 0, steelDark);
            }

            // 鍋(北東)
            {
                var g = c.Anchored("Pot", new Vector2(0.8f, 1), 14);
                c.Cylinder(g, "Body", new Vector3(0, T + 3.5f, 0), 6, 7, steel);
                c.Cylinder(g, "Rim", new Vector3(0, T + 6.9f, 0), 6.25f, 0.4f, steelDark);
                c.Cylinder(g, "Lid", new Vector3(0, T + 7.35f, 0), 6.1f, 0.5f, steel);
                c.Sphere(g, "Knob", new Vector3(0, T + 8.1f, 0), new Vector3(1.6f, 1.0f, 1.6f), c.Mat("Knob", new Color(0.15f, 0.12f, 0.1f), 0.5f));
                c.Box(g, "HandleL", new Vector3(-7, T + 5.6f, 0), new Vector3(2, 0.6f, 1.6f), 0, steelDark);
                c.Box(g, "HandleR", new Vector3(7, T + 5.6f, 0), new Vector3(2, 0.6f, 1.6f), 0, steelDark);
            }

            // トマト(東)
            {
                var g = c.Anchored("Tomatoes", new Vector2(1, 0.15f), 8);
                var tomato = c.Mat("Tomato", new Color(0.88f, 0.16f, 0.1f), 0.75f);
                var calyx = c.Mat("Calyx", new Color(0.2f, 0.5f, 0.18f), 0.4f);
                var pts = new[] { new Vector3(0, 0, 0), new Vector3(4.6f, 0, 2.2f), new Vector3(1.2f, 0, -4.6f) };
                for (int i = 0; i < pts.Length; i++)
                {
                    Vector3 p = pts[i] + new Vector3(0, T + 2.1f, 0);
                    c.Sphere(g, $"Tomato_{i}", p, new Vector3(5, 4.3f, 5), tomato);
                    c.Sphere(g, $"Calyx_{i}", p + new Vector3(0, 2.05f, 0), new Vector3(2.2f, 0.35f, 2.2f), calyx);
                }
            }

            // にんじん(南東。寝かせてあるので低い)
            {
                var g = c.Anchored("Carrots", new Vector2(1, -0.7f), 6);
                var carrot = c.Mat("Carrot", new Color(0.95f, 0.5f, 0.1f), 0.45f);
                var leaf = c.Mat("CarrotLeaf", new Color(0.25f, 0.55f, 0.2f), 0.3f);
                for (int i = 0; i < 2; i++)
                {
                    float z = i * 3.2f;
                    c.Prim(PrimitiveType.Capsule, g, $"Carrot_{i}", new Vector3(0, T + 0.8f, z), new Vector3(1.6f, 4.5f, 1.6f), Quaternion.Euler(0, 15 * i, 90), carrot);
                    c.Sphere(g, $"Leaves_{i}", new Vector3(-5, T + 0.8f, z), new Vector3(2.6f, 1.4f, 2), leaf);
                }
            }

            // キャベツ(西)
            {
                var g = c.Anchored("Cabbage", new Vector2(-1, 0), 10);
                c.Sphere(g, "Outer", new Vector3(0, T + 4.3f, 0), new Vector3(10, 8.8f, 10), c.Mat("Cabbage", new Color(0.55f, 0.76f, 0.36f), 0.35f));
                c.Sphere(g, "Leaf", new Vector3(1.5f, T + 6.5f, -1.2f), new Vector3(6, 4.5f, 6), c.Mat("CabbageLight", new Color(0.72f, 0.86f, 0.5f), 0.35f));
            }

            // 塩とこしょう(北)
            {
                var g = c.Anchored("Shakers", new Vector2(-0.15f, 1), 20);
                c.Cylinder(g, "Salt", new Vector3(-2, T + 2.5f, 0), 1.5f, 5, c.Mat("Salt", new Color(0.96f, 0.96f, 0.95f), 0.6f));
                c.Cylinder(g, "SaltCap", new Vector3(-2, T + 5.3f, 0), 1.55f, 0.6f, steel);
                c.Cylinder(g, "Pepper", new Vector3(2, T + 2.5f, 0), 1.5f, 5, c.Mat("Pepper", new Color(0.12f, 0.1f, 0.09f), 0.6f));
                c.Cylinder(g, "PepperCap", new Vector3(2, T + 5.3f, 0), 1.55f, 0.6f, steel);
            }

            // おたま(南。寝かせてある)
            {
                var g = c.Anchored("Ladle", new Vector2(0.3f, -1), 5);
                c.Prim(PrimitiveType.Capsule, g, "Handle", new Vector3(0, T + 0.4f, 0), new Vector3(0.7f, 6, 0.7f), Quaternion.Euler(0, 10, 90), steel);
                c.Sphere(g, "Bowl", new Vector3(-7, T + 0.7f, -1.2f), new Vector3(3.2f, 1.4f, 3.2f), steel);
            }

            // ふきん(南西)
            {
                var g = c.Anchored("DishTowel", new Vector2(-0.9f, -1), 6);
                var cloth = c.Box(g, "Cloth", new Vector3(0, T + 0.1f, 0), new Vector3(14, 0.2f, 9), -12, c.Mat("Towel", new Color(0.95f, 0.95f, 0.92f), 0.1f));
                var stripe = c.Mat("TowelStripe", new Color(0.2f, 0.35f, 0.7f), 0.1f);
                for (int i = 0; i < 3; i++)
                {
                    c.Prim(PrimitiveType.Cube, cloth.transform, $"Stripe_{i}", new Vector3(0, 0.55f, -0.3f + i * 0.3f), new Vector3(1.0f, 0.1f, 0.06f), Quaternion.identity, stripe, true);
                }
            }
        }

        //----------------------------------------
        // 子ども部屋の勉強机
        //----------------------------------------
        static void BuildDesk(Ctx c)
        {
            float T = kTableTop;
            SetSupports(c, StageBackdrop.SupportStyle.BookStack, 1.0f, 1.8f,
                c.Mat("NotePage", new Color(0.97f, 0.97f, 0.95f), 0.1f),
                c.Mat("NoteYellow", new Color(0.95f, 0.76f, 0.3f), 0.3f),
                c.Mat("NoteBlue", new Color(0.38f, 0.62f, 0.9f), 0.3f),
                c.Mat("NotePink", new Color(0.95f, 0.52f, 0.58f), 0.3f),
                c.Mat("NoteGreen", new Color(0.48f, 0.78f, 0.52f), 0.3f));
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.96f, 0.95f, 0.9f), 0.2f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.3f, 0.5f, 0.85f), 0.6f);

            // 机(木の天板)とカッティングマット、部屋の床
            var desk = c.Group("Desk");
            c.Box(desk, "Top", new Vector3(0, T - 2, 20), new Vector3(220, 4, 160), 0, c.Mat("DeskWood", new Color(0.66f, 0.48f, 0.32f), 0.5f));
            c.Box(desk, "Mat", new Vector3(0, T + 0.05f, 0), new Vector3(96, 0.1f, 72), 0, c.Mat("CuttingMat", new Color(0.18f, 0.45f, 0.36f), 0.3f));
            var grid = c.Mat("MatGrid", new Color(0.55f, 0.75f, 0.65f), 0.3f);
            for (int i = -9; i <= 9; i++)
            {
                c.Box(desk, $"GridX_{i}", new Vector3(0, T + 0.11f, i * 4), new Vector3(96, 0.02f, 0.12f), 0, grid);
                c.Box(desk, $"GridZ_{i}", new Vector3(i * 5, T + 0.12f, 0), new Vector3(0.12f, 0.02f, 72), 0, grid);
            }
            FarFloor(c, "Carpet", c.Mat("Carpet", new Color(0.55f, 0.6f, 0.72f), 0.1f));

            // 鉛筆(南東。寝かせてある)
            {
                var g = c.Anchored("Pencils", new Vector2(0.9f, -0.9f), 6);
                var colors = new[] { new Color(0.95f, 0.8f, 0.2f), new Color(0.2f, 0.45f, 0.8f), new Color(0.85f, 0.2f, 0.2f) };
                var wood = c.Mat("PencilWood", new Color(0.92f, 0.8f, 0.6f), 0.3f);
                var lead = c.Mat("PencilLead", new Color(0.15f, 0.15f, 0.15f), 0.6f);
                var eraser = c.Mat("PencilEraser", new Color(0.95f, 0.6f, 0.65f), 0.3f);
                for (int i = 0; i < colors.Length; i++)
                {
                    float z = i * 2.0f;
                    c.Prim(PrimitiveType.Cylinder, g, $"Body_{i}", new Vector3(0, T + 0.5f, z), new Vector3(1, 9, 1), Quaternion.Euler(0, 0, 90), c.Mat($"Pencil{i}", colors[i], 0.5f));
                    c.Prim(PrimitiveType.Capsule, g, $"Tip_{i}", new Vector3(-9.6f, T + 0.5f, z), new Vector3(0.9f, 1.0f, 0.9f), Quaternion.Euler(0, 0, 90), wood);
                    c.Sphere(g, $"Lead_{i}", new Vector3(-10.5f, T + 0.5f, z), new Vector3(0.35f, 0.35f, 0.35f), lead);
                    c.Prim(PrimitiveType.Cylinder, g, $"Eraser_{i}", new Vector3(9.5f, T + 0.5f, z), new Vector3(1, 0.5f, 1), Quaternion.Euler(0, 0, 90), eraser);
                }
            }

            // 消しゴム(西)
            {
                var g = c.Anchored("Eraser", new Vector2(-1, -0.5f), 6);
                c.Box(g, "Body", new Vector3(0, T + 1, 0), new Vector3(6, 2, 3.4f), 20, c.Mat("Eraser", new Color(0.97f, 0.97f, 0.96f), 0.3f));
                c.Box(g, "Sleeve", new Vector3(0.5f, T + 1, 0.18f), new Vector3(3.4f, 2.08f, 3.48f), 20, c.Mat("EraserSleeve", new Color(0.25f, 0.45f, 0.85f), 0.4f));
            }

            // 定規(南西。寝かせてある)
            {
                var g = c.Anchored("Ruler", new Vector2(-0.9f, -1), 4);
                var ruler = c.Box(g, "Body", new Vector3(0, T + 0.15f, 0), new Vector3(30, 0.3f, 4), 8, c.Mat("Ruler", new Color(0.7f, 0.85f, 0.95f), 0.85f));
                var tick = c.Mat("RulerTick", new Color(0.15f, 0.2f, 0.3f), 0.3f);
                for (int i = 0; i <= 30; i++)
                {
                    float len = i % 5 == 0 ? 0.35f : 0.18f;
                    c.Prim(PrimitiveType.Cube, ruler.transform, $"Tick_{i}", new Vector3(-0.48f + i * 0.032f, 0.55f, 0.5f - len * 0.5f), new Vector3(0.004f, 0.1f, len), Quaternion.identity, tick, true);
                }
            }

            // 電気スタンド(北東)
            {
                var g = c.Anchored("DeskLamp", new Vector2(0.8f, 1), 14);
                var lamp = c.Mat("Lamp", new Color(0.2f, 0.45f, 0.35f), 0.6f, 0.3f);
                c.Cylinder(g, "Base", new Vector3(0, T + 0.6f, 0), 4, 1.2f, lamp);
                c.Prim(PrimitiveType.Capsule, g, "Arm1", new Vector3(-1.5f, T + 6, 0), new Vector3(0.7f, 6, 0.7f), Quaternion.Euler(0, 0, 25), lamp);
                c.Prim(PrimitiveType.Capsule, g, "Arm2", new Vector3(-5.5f, T + 13, 0), new Vector3(0.7f, 5, 0.7f), Quaternion.Euler(0, 0, -50), lamp);
                c.Prim(PrimitiveType.Cylinder, g, "Head", new Vector3(-9.5f, T + 14, 0), new Vector3(5, 1.8f, 5), Quaternion.Euler(0, 0, -20), lamp);
                c.Sphere(g, "Bulb", new Vector3(-9.2f, T + 12.3f, 0), new Vector3(2.2f, 1.6f, 2.2f), c.Mat("Bulb", new Color(1.0f, 0.95f, 0.75f), 0.9f));
            }

            // 筆箱(北西)
            {
                var g = c.Anchored("PencilCase", new Vector2(-0.8f, 1), 12);
                c.Box(g, "Case", new Vector3(0, T + 2, 0), new Vector3(15, 4, 6), -10, c.Mat("PencilCase", new Color(0.85f, 0.25f, 0.3f), 0.4f));
                c.Box(g, "Zipper", new Vector3(0, T + 4.02f, 0), new Vector3(14, 0.06f, 0.5f), -10, c.Mat("Zipper", new Color(0.85f, 0.85f, 0.8f), 0.8f, 0.6f));
            }

            // ノートの山(東)
            {
                var g = c.Anchored("Notebooks", new Vector2(1, 0.25f), 10);
                var cols = new[] { new Color(0.3f, 0.6f, 0.9f), new Color(0.95f, 0.85f, 0.3f), new Color(0.9f, 0.45f, 0.5f) };
                for (int i = 0; i < cols.Length; i++)
                {
                    c.Box(g, $"Notebook_{i}", new Vector3(0, T + 0.4f + i * 0.8f, 0), new Vector3(10, 0.8f, 14), i * 7 - 7, c.Mat($"Notebook{i}", cols[i], 0.3f));
                }
            }

            // 目覚まし時計(北)
            {
                var g = c.Anchored("AlarmClock", new Vector2(-0.15f, 1), 18);
                var red = c.Mat("Clock", new Color(0.85f, 0.2f, 0.2f), 0.7f);
                c.Prim(PrimitiveType.Cylinder, g, "Body", new Vector3(0, T + 4, 0), new Vector3(7, 1.2f, 7), Quaternion.Euler(90, 0, 0), red);
                c.Prim(PrimitiveType.Cylinder, g, "Face", new Vector3(0, T + 4, -1.25f), new Vector3(6, 0.05f, 6), Quaternion.Euler(90, 0, 0), c.Mat("ClockFace", new Color(0.97f, 0.96f, 0.92f), 0.5f));
                c.Sphere(g, "BellL", new Vector3(-2.4f, T + 7.8f, 0), new Vector3(2.4f, 2, 2.4f), red);
                c.Sphere(g, "BellR", new Vector3(2.4f, T + 7.8f, 0), new Vector3(2.4f, 2, 2.4f), red);
            }

            // 付箋(南)
            {
                var g = c.Anchored("StickyNotes", new Vector2(0.2f, -1), 4);
                c.Box(g, "Pad", new Vector3(0, T + 0.3f, 0), new Vector3(4, 0.6f, 4), 15, c.Mat("Sticky", new Color(1.0f, 0.92f, 0.4f), 0.2f));
            }
        }

        //----------------------------------------
        // 夏の縁側
        //----------------------------------------
        static void BuildEngawa(Ctx c)
        {
            float T = kTableTop;
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 0.9f, 1.4f, null,
                c.Mat("ZabutonPurple", new Color(0.45f, 0.22f, 0.42f), 0.2f),
                c.Mat("ZabutonRed", new Color(0.6f, 0.16f, 0.16f), 0.2f),
                c.Mat("ZabutonIndigo", new Color(0.18f, 0.24f, 0.45f), 0.2f));
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.84f, 0.8f, 0.56f), 0.2f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.62f, 0.52f, 0.28f), 0.5f);

            // 縁側の板(東西に長い板を南北に並べる)と、南の庭
            var boards = c.Group("Veranda");
            var woodA = c.Mat("BoardA", new Color(0.6f, 0.42f, 0.26f), 0.55f);
            var woodB = c.Mat("BoardB", new Color(0.52f, 0.36f, 0.22f), 0.55f);
            for (int i = 0; i < 16; i++)
            {
                float z = -6 + i * 6 + 3;
                c.Box(boards, $"Board_{i}", new Vector3(0, T - 1.5f, z), new Vector3(240, 3, 6), 0, i % 2 == 0 ? woodA : woodB);
            }
            c.Box(boards, "Edge", new Vector3(0, T - 2, -6.75f), new Vector3(240, 4, 1.5f), 0, c.Mat("BoardEdge", new Color(0.4f, 0.27f, 0.16f), 0.5f));
            var garden = c.Group("Garden");
            c.Box(garden, "Ground", new Vector3(0, T - 12.5f, -60), new Vector3(400, 1, 110), 0, c.Mat("Gravel", new Color(0.7f, 0.68f, 0.62f), 0.1f));
            c.Box(garden, "Moss", new Vector3(30, T - 11.9f, -30), new Vector3(60, 0.2f, 30), 0, c.Mat("Moss", new Color(0.35f, 0.5f, 0.25f), 0.1f));
            FarFloor(c, "Earth", c.Mat("Earth", new Color(0.4f, 0.33f, 0.25f), 0.1f));

            // スイカ(北西)
            {
                var g = c.Anchored("Watermelon", new Vector2(-0.8f, 1), 12);
                c.Sphere(g, "Body", new Vector3(0, T + 4.5f, 0), new Vector3(10, 9, 10), c.Mat("Watermelon", new Color(0.12f, 0.38f, 0.16f), 0.6f));
                var stripe = c.Mat("WatermelonStripe", new Color(0.05f, 0.18f, 0.07f), 0.6f);
                for (int i = 0; i < 6; i++)
                {
                    c.Sphere(g, $"Stripe_{i}", new Vector3(0, T + 4.5f, 0), new Vector3(10.08f, 9.08f, 1.0f), stripe).transform.rotation = Quaternion.Euler(0, i * 30, 0);
                }
                c.Cylinder(g, "Stem", new Vector3(0, T + 9.2f, 0), 0.25f, 0.8f, c.Mat("Stem", new Color(0.4f, 0.3f, 0.15f), 0.3f));
            }

            // 切ったスイカ(東)。お皿に立てた半月
            {
                var g = c.Anchored("WatermelonSlices", new Vector2(1, 0.3f), 10);
                c.Cylinder(g, "Plate", new Vector3(0, T + 0.3f, 0), 7, 0.6f, c.Mat("Plate", new Color(0.95f, 0.94f, 0.9f), 0.8f));
                var red = c.Mat("WatermelonRed", new Color(0.9f, 0.22f, 0.25f), 0.5f);
                var rind = c.Mat("WatermelonRind", new Color(0.15f, 0.45f, 0.18f), 0.5f);
                var seed = c.Mat("Seed", new Color(0.08f, 0.06f, 0.05f), 0.6f);
                for (int i = 0; i < 3; i++)
                {
                    float x = -3.2f + i * 3.2f;
                    c.Prim(PrimitiveType.Cylinder, g, $"Rind_{i}", new Vector3(x, T + 0.6f, 0), new Vector3(7.6f, 0.7f, 7.6f), Quaternion.Euler(90, 90, 0), rind);
                    c.Prim(PrimitiveType.Cylinder, g, $"Flesh_{i}", new Vector3(x, T + 0.6f, 0), new Vector3(7.0f, 0.8f, 7.0f), Quaternion.Euler(90, 90, 0), red);
                    // 種は手前の切り口(西向きの面)に付ける
                    c.Sphere(g, $"Seed_{i}", new Vector3(x - 0.82f, T + 2.2f, 0.6f), new Vector3(0.2f, 0.35f, 0.25f), seed);
                }
            }

            // 蚊遣り豚(北東)
            {
                var g = c.Anchored("Kayaributa", new Vector2(0.8f, 1), 12);
                var clay = c.Mat("Kayaributa", new Color(0.62f, 0.36f, 0.22f), 0.4f);
                c.Sphere(g, "Body", new Vector3(0, T + 3, 0), new Vector3(6, 5.5f, 7), clay);
                c.Prim(PrimitiveType.Cylinder, g, "Snout", new Vector3(0, T + 3, -3.6f), new Vector3(2.6f, 0.5f, 2.6f), Quaternion.Euler(90, 0, 0), clay);
                c.Prim(PrimitiveType.Cylinder, g, "Mouth", new Vector3(0, T + 3, -4.1f), new Vector3(2.0f, 0.05f, 2.0f), Quaternion.Euler(90, 0, 0), c.Mat("KayaributaInside", new Color(0.12f, 0.08f, 0.06f), 0.2f));
                c.Box(g, "EarL", new Vector3(-1.6f, T + 5.6f, -1.8f), new Vector3(1, 1.2f, 0.4f), 0, clay);
                c.Box(g, "EarR", new Vector3(1.6f, T + 5.6f, -1.8f), new Vector3(1, 1.2f, 0.4f), 0, clay);
            }

            // 蚊取り線香(南。平たい)
            {
                var g = c.Anchored("MosquitoCoil", new Vector2(0.2f, -1), 5);
                c.Cylinder(g, "Tray", new Vector3(0, T + 0.1f, 0), 4.5f, 0.2f, c.Mat("CoilTray", new Color(0.5f, 0.5f, 0.52f), 0.6f, 0.6f));
                var coil = c.Mat("Coil", new Color(0.18f, 0.32f, 0.18f), 0.2f);
                const int kSegments = 44;
                for (int i = 0; i < kSegments; i++)
                {
                    float t = i / (float)kSegments;
                    float a = t * Mathf.PI * 2 * 3.3f;
                    float r = 0.7f + t * 3.2f;
                    float a2 = (i + 1) / (float)kSegments * Mathf.PI * 2 * 3.3f;
                    float r2 = 0.7f + (i + 1) / (float)kSegments * 3.2f;
                    Vector3 p1 = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                    Vector3 p2 = new Vector3(Mathf.Cos(a2) * r2, 0, Mathf.Sin(a2) * r2);
                    Vector3 mid = (p1 + p2) * 0.5f;
                    float len = Vector3.Distance(p1, p2);
                    c.Prim(PrimitiveType.Cube, g, $"Coil_{i}", mid + new Vector3(0, T + 0.4f, 0), new Vector3(0.35f, 0.3f, len + 0.15f), Quaternion.LookRotation(p2 - p1), coil);
                }
            }

            // うちわ(西)
            {
                var g = c.Anchored("Uchiwa", new Vector2(-1, -0.4f), 7);
                c.Prim(PrimitiveType.Cylinder, g, "Fan", new Vector3(0, T + 0.1f, 0), new Vector3(8, 0.1f, 7), Quaternion.Euler(0, 20, 0), c.Mat("Uchiwa", new Color(0.97f, 0.97f, 0.95f), 0.2f));
                c.Prim(PrimitiveType.Cylinder, g, "Pattern", new Vector3(0.3f, T + 0.21f, 0.2f), new Vector3(4, 0.02f, 4), Quaternion.Euler(0, 20, 0), c.Mat("UchiwaPattern", new Color(0.2f, 0.4f, 0.8f), 0.2f));
                c.Prim(PrimitiveType.Capsule, g, "Handle", new Vector3(5.2f, T + 0.4f, -1.9f), new Vector3(0.7f, 2.5f, 0.7f), Quaternion.Euler(0, 20, 90), c.Mat("UchiwaHandle", new Color(0.75f, 0.6f, 0.35f), 0.4f));
            }

            // 麦茶(北)
            {
                var g = c.Anchored("BarleyTea", new Vector2(-0.2f, 1), 18);
                c.Cylinder(g, "Coaster", new Vector3(0, T + 0.15f, 0), 2.8f, 0.3f, c.Mat("Coaster", new Color(0.75f, 0.62f, 0.4f), 0.2f));
                c.Cylinder(g, "Glass", new Vector3(0, T + 3.3f, 0), 2.1f, 6, c.Mat("BarleyTea", new Color(0.55f, 0.32f, 0.12f), 0.9f));
                c.Box(g, "Ice", new Vector3(0.5f, T + 6.35f, 0.3f), new Vector3(1.4f, 0.3f, 1.4f), 30, c.Mat("Ice", new Color(0.85f, 0.93f, 0.97f), 0.95f));
            }

            // 朝顔の鉢(南東)
            {
                var g = c.Anchored("MorningGlory", new Vector2(1, -0.6f), 8);
                c.Cylinder(g, "Pot", new Vector3(0, T + 2, 0), 3, 4, c.Mat("FlowerPot", new Color(0.7f, 0.38f, 0.25f), 0.3f));
                var leaf = c.Mat("GloryLeaf", new Color(0.25f, 0.55f, 0.22f), 0.3f);
                var flower = c.Mat("GloryFlower", new Color(0.5f, 0.3f, 0.85f), 0.4f);
                var pts = new[] { new Vector3(-1.5f, 5, 0.5f), new Vector3(1.2f, 5.6f, -0.8f), new Vector3(0.2f, 6.6f, 1.0f) };
                for (int i = 0; i < pts.Length; i++)
                {
                    c.Sphere(g, $"Leaf_{i}", pts[i] + new Vector3(0, T, 0), new Vector3(2.6f, 1.4f, 2.2f), leaf);
                    c.Sphere(g, $"Flower_{i}", pts[i] + new Vector3(0.6f, T + 0.9f, -0.4f), new Vector3(1.8f, 0.5f, 1.8f), flower);
                }
            }
        }

        //----------------------------------------
        // お風呂場
        //----------------------------------------
        static void BuildBath(Ctx c)
        {
            float T = kTableTop;
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 0.6f, 1.0f, null,
                c.Mat("LidWhite", new Color(0.95f, 0.95f, 0.95f), 0.6f),
                c.Mat("LidAqua", new Color(0.7f, 0.87f, 0.92f), 0.6f));
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.76f, 0.9f, 0.93f), 0.6f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.92f, 0.93f, 0.95f), 0.8f);

            // タイルの床(目地の線は縦横で高さをずらす)
            var floor = c.Group("TileFloor");
            c.Box(floor, "Tiles", new Vector3(0, T - 1, 0), new Vector3(320, 2, 320), 0, c.Mat("Tile", new Color(0.88f, 0.92f, 0.93f), 0.7f));
            var grout = c.Mat("Grout", new Color(0.66f, 0.72f, 0.74f), 0.4f);
            for (int i = -15; i <= 15; i++)
            {
                c.Box(floor, $"GroutX_{i}", new Vector3(0, T + 0.01f, i * 6), new Vector3(184, 0.02f, 0.25f), 0, grout);
                c.Box(floor, $"GroutZ_{i}", new Vector3(i * 6, T + 0.02f, 0), new Vector3(0.25f, 0.02f, 184), 0, grout);
            }

            // 湯船(北。縁と湯)
            {
                var g = c.Anchored("Bathtub", new Vector2(0, 1), 16);
                var tub = c.Mat("Bathtub", new Color(0.62f, 0.8f, 0.9f), 0.8f);
                c.Box(g, "RimS", new Vector3(0, T + 6, -11), new Vector3(60, 12, 2), 0, tub);
                c.Box(g, "RimN", new Vector3(0, T + 6, 11), new Vector3(60, 12, 2), 0, tub);
                c.Box(g, "RimW", new Vector3(-29, T + 6, 0), new Vector3(2, 12, 20), 0, tub);
                c.Box(g, "RimE", new Vector3(29, T + 6, 0), new Vector3(2, 12, 20), 0, tub);
                c.Box(g, "Water", new Vector3(0, T + 9.5f, 0), new Vector3(56, 1, 20), 0, c.Mat("Water", new Color(0.35f, 0.65f, 0.85f), 0.95f));
                // アヒル(湯に浮かぶ)
                var duck = c.Mat("Duck", new Color(1.0f, 0.85f, 0.15f), 0.6f);
                var beak = c.Mat("Beak", new Color(0.95f, 0.5f, 0.1f), 0.6f);
                for (int i = 0; i < 2; i++)
                {
                    Vector3 p = new Vector3(-10 + i * 18, T + 11, -2 + i * 3);
                    c.Sphere(g, $"DuckBody_{i}", p, new Vector3(4, 3, 5), duck);
                    c.Sphere(g, $"DuckHead_{i}", p + new Vector3(0, 2.2f, -1.6f), new Vector3(2.6f, 2.6f, 2.6f), duck);
                    c.Sphere(g, $"DuckBeak_{i}", p + new Vector3(0, 2, -3.0f), new Vector3(1.4f, 0.6f, 1.4f), beak);
                }
            }

            // 洗面器(西)
            {
                var g = c.Anchored("Washbowl", new Vector2(-1, 0.3f), 9);
                var yellow = c.Mat("Washbowl", new Color(0.98f, 0.8f, 0.2f), 0.7f);
                c.Cylinder(g, "Body", new Vector3(0, T + 2.5f, 0), 4.5f, 5, yellow);
                c.Cylinder(g, "Rim", new Vector3(0, T + 5.2f, 0), 4.8f, 0.4f, yellow);
                c.Cylinder(g, "Inside", new Vector3(0, T + 5.45f, 0), 4.0f, 0.1f, c.Mat("WashbowlInside", new Color(0.95f, 0.72f, 0.15f), 0.7f));
            }

            // 風呂いす(東)
            {
                var g = c.Anchored("BathStool", new Vector2(1, 0.3f), 10);
                var pink = c.Mat("Stool", new Color(0.95f, 0.65f, 0.7f), 0.7f);
                c.Box(g, "Seat", new Vector3(0, T + 5, 0), new Vector3(10, 1.2f, 8), 0, pink);
                for (int i = 0; i < 4; i++)
                {
                    float x = (i % 2 == 0 ? -1 : 1) * 3.8f, z = (i < 2 ? -1 : 1) * 2.8f;
                    c.Box(g, $"Leg_{i}", new Vector3(x, T + 2.2f, z), new Vector3(1.2f, 4.4f, 1.2f), 0, pink);
                }
            }

            // 石けん(南西。平たい)
            {
                var g = c.Anchored("Soap", new Vector2(-0.6f, -1), 5);
                c.Prim(PrimitiveType.Cylinder, g, "Dish", new Vector3(0, T + 0.15f, 0), new Vector3(7, 0.15f, 5), Quaternion.identity, c.Mat("SoapDish", new Color(0.6f, 0.8f, 0.95f), 0.8f));
                c.Box(g, "Soap", new Vector3(0, T + 0.9f, 0), new Vector3(3.6f, 1.2f, 2.3f), 15, c.Mat("SoapBar", new Color(0.97f, 0.85f, 0.88f), 0.5f));
            }

            // シャンプー(北西)
            {
                var g = c.Anchored("Shampoo", new Vector2(-0.7f, 1), 14);
                var cols = new[] { new Color(0.97f, 0.97f, 0.96f), new Color(0.95f, 0.7f, 0.75f) };
                for (int i = 0; i < cols.Length; i++)
                {
                    float x = i * 5;
                    c.Cylinder(g, $"Bottle_{i}", new Vector3(x, T + 4.5f, 0), 2, 9, c.Mat($"Shampoo{i}", cols[i], 0.7f));
                    c.Cylinder(g, $"Pump_{i}", new Vector3(x, T + 9.8f, 0), 0.6f, 1.6f, c.Mat("Pump", new Color(0.3f, 0.3f, 0.32f), 0.6f));
                    c.Box(g, $"Nozzle_{i}", new Vector3(x, T + 10.5f, -1), new Vector3(0.6f, 0.5f, 2), 0, c.Mat("Pump", new Color(0.3f, 0.3f, 0.32f), 0.6f));
                }
            }

            // スポンジ(南東)
            {
                var g = c.Anchored("Sponge", new Vector2(0.9f, -1), 5);
                c.Box(g, "Sponge", new Vector3(0, T + 0.9f, 0), new Vector3(5, 1.8f, 3.4f), -20, c.Mat("Sponge", new Color(1.0f, 0.88f, 0.3f), 0.1f));
                c.Box(g, "Scrub", new Vector3(0, T + 2.0f, 0), new Vector3(5, 0.4f, 3.4f), -20, c.Mat("Scrub", new Color(0.2f, 0.55f, 0.3f), 0.1f));
            }
        }

        //----------------------------------------
        // もんじゃ焼き屋の鉄板
        //----------------------------------------
        static void BuildMonja(Ctx c)
        {
            float T = kTableTop;
            var iron = c.Mat("Iron", new Color(0.2f, 0.2f, 0.22f), 0.65f, 0.6f);
            var ironDark = c.Mat("IronDark", new Color(0.12f, 0.12f, 0.13f), 0.55f, 0.6f);
            var steel = c.Mat("Steel", new Color(0.7f, 0.72f, 0.75f), 0.85f, 0.8f);
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 1.0f, 2.0f, null, iron, ironDark);
            // ステージの床は油の引かれた鉄板(まわりの大きな鉄板と見分けられるよう少し明るく)、縁(壁)は鉄板の縁の金枠
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.36f, 0.34f, 0.33f), 0.7f, 0.5f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.62f, 0.64f, 0.68f), 0.8f);

            // テーブル(木の天板に大きな鉄板が埋め込んである)と店の床
            var table = c.Group("Table");
            c.Box(table, "Top", new Vector3(0, T - 2, 10), new Vector3(240, 4, 180), 0, c.Mat("TableWood", new Color(0.5f, 0.32f, 0.18f), 0.45f));
            c.Box(table, "Teppan", new Vector3(0, T + 0.15f, 0), new Vector3(130, 0.3f, 96), 0, iron);
            // 鉄板の縁の金枠(上面どうしが同じ高さにならないよう、少し高くする)
            c.Box(table, "RimN", new Vector3(0, T + 0.4f, 48.5f), new Vector3(132, 0.8f, 1), 0, steel);
            c.Box(table, "RimS", new Vector3(0, T + 0.4f, -48.5f), new Vector3(132, 0.8f, 1), 0, steel);
            c.Box(table, "RimW", new Vector3(-65.5f, T + 0.4f, 0), new Vector3(1, 0.8f, 96), 0, steel);
            c.Box(table, "RimE", new Vector3(65.5f, T + 0.4f, 0), new Vector3(1, 0.8f, 96), 0, steel);
            // 焦げ跡(鉄板の上の黒いしみ)
            var scorch = c.Mat("Scorch", new Color(0.08f, 0.07f, 0.06f), 0.3f);
            var scorches = new[] { new Vector3(-40, 0, -30), new Vector3(35, 0, 32), new Vector3(48, 0, -20), new Vector3(-52, 0, 25) };
            for (int i = 0; i < scorches.Length; i++)
            {
                c.Prim(PrimitiveType.Cylinder, table, $"Scorch_{i}", scorches[i] + new Vector3(0, T + 0.31f, 0), new Vector3(9 + i * 2, 0.01f, 6 + i), Quaternion.Euler(0, i * 37, 0), scorch);
            }
            c.Box(table, "Front", new Vector3(0, T - 20, -80.5f), new Vector3(240, 32, 1), 0, c.Mat("TableFront", new Color(0.38f, 0.24f, 0.13f), 0.4f));
            FarFloor(c, "ShopFloor", c.Mat("ShopFloor", new Color(0.3f, 0.27f, 0.24f), 0.25f));

            // 奥の壁と品書きの札・提灯(ステージより奥・上にあるので、遊ぶ邪魔にならない)
            {
                var g = c.Group("BackWall");
                c.Box(g, "Wall", new Vector3(0, T + 20, 100), new Vector3(260, 100, 2), 0, c.Mat("ShopWall", new Color(0.82f, 0.74f, 0.58f), 0.2f));
                c.Box(g, "Beam", new Vector3(0, T + 44, 98.5f), new Vector3(260, 3, 1.5f), 0, c.Mat("Beam", new Color(0.3f, 0.18f, 0.1f), 0.4f));
                var tag = c.Mat("MenuTag", new Color(0.95f, 0.9f, 0.76f), 0.15f);
                var tagEdge = c.Mat("MenuTagEdge", new Color(0.55f, 0.35f, 0.18f), 0.3f);
                var ink = c.Mat("Ink", new Color(0.12f, 0.1f, 0.1f), 0.1f);
                var red = c.Mat("InkRed", new Color(0.75f, 0.12f, 0.1f), 0.2f);
                for (int i = 0; i < 12; i++)
                {
                    float x = -66 + i * 12;
                    var t = c.Box(g, $"MenuTag_{i}", new Vector3(x, T + 32, 98.4f), new Vector3(7, 18, 0.4f), 0, tag);
                    c.Prim(PrimitiveType.Cube, t.transform, "Edge", new Vector3(0, 0, 0.6f), new Vector3(1.12f, 1.06f, 0.5f), Quaternion.identity, tagEdge, true);
                    // 品名の文字の代わりの縦の線(値段のところは赤)
                    c.Prim(PrimitiveType.Cube, t.transform, "Name", new Vector3(0, 0.12f, -0.6f), new Vector3(0.22f, 0.6f, 0.5f), Quaternion.identity, ink, true);
                    c.Prim(PrimitiveType.Cube, t.transform, "Price", new Vector3(0, -0.36f, -0.6f), new Vector3(0.3f, 0.14f, 0.5f), Quaternion.identity, i % 3 == 0 ? red : ink, true);
                }
                var lantern = c.Mat("Lantern", new Color(0.88f, 0.18f, 0.12f), 0.45f);
                var lanternCap = c.Mat("LanternCap", new Color(0.12f, 0.1f, 0.1f), 0.4f);
                foreach (float x in new[] { -95f, 95f })
                {
                    c.Sphere(g, $"Lantern_{x}", new Vector3(x, T + 28, 92), new Vector3(12, 16, 12), lantern);
                    c.Cylinder(g, $"LanternTop_{x}", new Vector3(x, T + 36.5f, 92), 3.5f, 1.5f, lanternCap);
                    c.Cylinder(g, $"LanternBottom_{x}", new Vector3(x, T + 19.5f, 92), 3.5f, 1.5f, lanternCap);
                }
            }

            // もんじゃ(西)。キャベツの土手の中に生地を流し込んだところ
            {
                var g = c.Anchored("Monja", new Vector2(-1, 0.1f), 14);
                var batter = c.Mat("Batter", new Color(0.62f, 0.45f, 0.24f), 0.75f);
                var cabbage = c.Mat("Cabbage", new Color(0.82f, 0.88f, 0.6f), 0.4f);
                var cabbageGreen = c.Mat("CabbageGreen", new Color(0.55f, 0.74f, 0.35f), 0.4f);
                c.Cylinder(g, "Batter", new Vector3(0, T + 0.45f, 0), 7.5f, 0.3f, batter);
                for (int i = 0; i < 14; i++)
                {
                    float a = i * Mathf.PI * 2.0f / 14;
                    Vector3 p = new Vector3(Mathf.Cos(a) * 8.5f, T + 1.0f, Mathf.Sin(a) * 8.5f);
                    c.Sphere(g, $"Dote_{i}", p, new Vector3(4.2f, 1.8f, 3.2f), i % 3 == 0 ? cabbageGreen : cabbage);
                }
                // 具(切りイカ・桜えび・天かす)
                var shrimp = c.Mat("Sakuraebi", new Color(0.95f, 0.45f, 0.35f), 0.5f);
                var tenkasu = c.Mat("Tenkasu", new Color(0.93f, 0.8f, 0.5f), 0.3f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 2.4f;
                    float r = 1.5f + (i % 3) * 1.8f;
                    c.Sphere(g, $"Gu_{i}", new Vector3(Mathf.Cos(a) * r, T + 0.65f, Mathf.Sin(a) * r), new Vector3(1.2f, 0.5f, 0.9f), i % 2 == 0 ? shrimp : tenkasu);
                }
                // 小さなヘラ(はがし)が2本刺さっている
                for (int i = 0; i < 2; i++)
                {
                    float x = 3 + i * 2.5f;
                    c.Box(g, $"HeraBlade_{i}", new Vector3(x, T + 0.7f, -2), new Vector3(1.6f, 0.15f, 2.2f), 20 + i * 15, steel);
                    c.Prim(PrimitiveType.Cylinder, g, $"HeraHandle_{i}", new Vector3(x + 0.6f, T + 2.6f, -4.2f), new Vector3(0.5f, 2.2f, 0.5f), Quaternion.Euler(-55, 20 + i * 15, 0), c.Mat("HeraHandle", new Color(0.35f, 0.2f, 0.1f), 0.5f));
                }
            }

            // 具の入ったボウルと大きなヘラ(北東)
            {
                var g = c.Anchored("Bowl", new Vector2(0.75f, 1), 12);
                c.Cylinder(g, "Bowl", new Vector3(0, T + 3, 0), 6, 6, steel);
                c.Cylinder(g, "Rim", new Vector3(0, T + 6.1f, 0), 6.3f, 0.3f, steel);
                c.Cylinder(g, "Batter", new Vector3(0, T + 5.5f, 0), 5.6f, 0.4f, c.Mat("BowlBatter", new Color(0.78f, 0.62f, 0.38f), 0.6f));
                var cabbage = c.Mat("Cabbage", new Color(0.82f, 0.88f, 0.6f), 0.4f);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 1.05f;
                    c.Sphere(g, $"Cabbage_{i}", new Vector3(Mathf.Cos(a) * 3, T + 6.2f, Mathf.Sin(a) * 3), new Vector3(3, 1.4f, 2.4f), cabbage);
                }
                c.Sphere(g, "Yolk", new Vector3(0.5f, T + 6.4f, -0.5f), new Vector3(2.2f, 1.1f, 2.2f), c.Mat("Yolk", new Color(1.0f, 0.68f, 0.1f), 0.8f));
                // 大きなヘラ(コテ)
                c.Box(g, "KoteBlade", new Vector3(-10, T + 0.5f, -3), new Vector3(5, 0.2f, 4), 30, steel);
                c.Prim(PrimitiveType.Cylinder, g, "KoteHandle", new Vector3(-13.8f, T + 0.9f, -5.2f), new Vector3(0.9f, 3.2f, 0.9f), Quaternion.Euler(0, 30, 90), c.Mat("KoteHandle", new Color(0.3f, 0.18f, 0.09f), 0.5f));
            }

            // ソース・青のり・かつお節(北)
            {
                var g = c.Anchored("Condiments", new Vector2(-0.2f, 1), 16);
                c.Cylinder(g, "Sauce", new Vector3(-5, T + 5, 0), 2.2f, 10, c.Mat("Sauce", new Color(0.25f, 0.12f, 0.06f), 0.75f));
                c.Cylinder(g, "SauceLabel", new Vector3(-5, T + 4.5f, 0), 2.25f, 4, c.Mat("SauceLabel", new Color(0.95f, 0.85f, 0.2f), 0.4f));
                c.Cylinder(g, "SauceCap", new Vector3(-5, T + 10.6f, 0), 1.0f, 1.2f, c.Mat("SauceCap", new Color(0.85f, 0.15f, 0.1f), 0.5f));
                c.Cylinder(g, "Aonori", new Vector3(0, T + 3, 0), 1.8f, 6, c.Mat("Aonori", new Color(0.2f, 0.42f, 0.18f), 0.4f));
                c.Cylinder(g, "AonoriCap", new Vector3(0, T + 6.3f, 0), 1.85f, 0.6f, c.Mat("ShakerCap", new Color(0.95f, 0.95f, 0.92f), 0.6f));
                c.Box(g, "Katsuobushi", new Vector3(5.5f, T + 3, 0), new Vector3(5, 6, 3.4f), -8, c.Mat("KatsuoBag", new Color(0.95f, 0.75f, 0.45f), 0.3f));
            }

            // ラムネとウーロン茶(東)
            {
                var g = c.Anchored("Drinks", new Vector2(1, 0.2f), 9);
                var glass = c.Mat("RamuneGlass", new Color(0.45f, 0.75f, 0.85f), 0.95f);
                c.Cylinder(g, "Ramune", new Vector3(0, T + 5, 0), 2.2f, 10, glass);
                c.Cylinder(g, "RamuneNeck", new Vector3(0, T + 11.2f, 0), 1.2f, 2.4f, glass);
                c.Sphere(g, "Marble", new Vector3(0, T + 10.2f, 0), new Vector3(1.4f, 1.4f, 1.4f), c.Mat("Marble", new Color(0.85f, 0.95f, 1.0f), 0.95f));
                c.Cylinder(g, "RamuneCap", new Vector3(0, T + 12.6f, 0), 1.3f, 0.5f, c.Mat("RamuneCap", new Color(0.2f, 0.35f, 0.8f), 0.6f));
                c.Cylinder(g, "Glass", new Vector3(6, T + 3.5f, 3), 2.6f, 7, c.Mat("Glass", new Color(0.85f, 0.9f, 0.92f), 0.95f));
                c.Cylinder(g, "Oolong", new Vector3(6, T + 3, 3), 2.3f, 5.6f, c.Mat("Oolong", new Color(0.55f, 0.3f, 0.1f), 0.8f));
            }

            // 取り皿(南東。平たいので手前でも邪魔にならない)
            {
                var g = c.Anchored("Plates", new Vector2(0.8f, -1), 5);
                var plate = c.Mat("Plate", new Color(0.96f, 0.95f, 0.92f), 0.85f);
                var plateRim = c.Mat("PlateRim", new Color(0.2f, 0.3f, 0.6f), 0.8f);
                for (int i = 0; i < 2; i++)
                {
                    Vector3 p = new Vector3(i * 9, T + 0.3f, i * 1.5f);
                    c.Cylinder(g, $"PlateRim_{i}", p, 3.8f, 0.5f, plateRim);
                    c.Cylinder(g, $"Plate_{i}", p + new Vector3(0, 0.05f, 0), 3.4f, 0.52f, plate);
                }
            }

            // 小さなヘラの束と伝票(南西)
            {
                var g = c.Anchored("Heras", new Vector2(-0.8f, -1), 5);
                var handle = c.Mat("HeraHandle", new Color(0.35f, 0.2f, 0.1f), 0.5f);
                for (int i = 0; i < 4; i++)
                {
                    float z = i * 1.4f;
                    c.Box(g, $"Blade_{i}", new Vector3(0, T + 0.25f, z), new Vector3(1.8f, 0.12f, 1.2f), 5 * i, steel);
                    c.Prim(PrimitiveType.Cylinder, g, $"Handle_{i}", new Vector3(2.8f, T + 0.35f, z), new Vector3(0.45f, 2, 0.45f), Quaternion.Euler(0, 5 * i, 90), handle);
                }
                c.Box(g, "Denpyo", new Vector3(-8, T + 0.1f, 1), new Vector3(4.5f, 0.2f, 9), 12, c.Mat("Denpyo", new Color(0.95f, 0.93f, 0.85f), 0.1f));
            }
        }

        //----------------------------------------
        // だるま落とし部の部室
        //----------------------------------------
        static void BuildClubroom(Ctx c)
        {
            float T = kTableTop;
            SetSupports(c, StageBackdrop.SupportStyle.BookStack, 1.0f, 1.8f,
                c.Mat("ScorePage", new Color(0.96f, 0.95f, 0.9f), 0.1f),
                c.Mat("ScoreRed", new Color(0.72f, 0.18f, 0.16f), 0.3f),
                c.Mat("ScoreBlack", new Color(0.16f, 0.16f, 0.18f), 0.3f),
                c.Mat("ScoreBlue", new Color(0.2f, 0.32f, 0.62f), 0.3f));
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.88f, 0.84f, 0.74f), 0.3f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.7f, 0.2f, 0.18f), 0.5f);

            // 長机(白い天板と金属の縁)と部室の床
            var desk = c.Group("LongDesk");
            c.Box(desk, "Top", new Vector3(0, T - 2, 20), new Vector3(220, 4, 160), 0, c.Mat("DeskTop", new Color(0.9f, 0.89f, 0.85f), 0.4f));
            c.Box(desk, "Frame", new Vector3(0, T - 5, -60.5f), new Vector3(220, 6, 1), 0, c.Mat("DeskFrame", new Color(0.5f, 0.52f, 0.55f), 0.7f, 0.6f));
            FarFloor(c, "Linoleum", c.Mat("Linoleum", new Color(0.42f, 0.55f, 0.48f), 0.4f));

            // 奥の壁とホワイトボード・部旗・賞状(ステージの奥に合わせて動く。机の奥の端に立てた低い壁)
            {
                var g = c.Anchored("BackWall", new Vector2(0, 1), 22);
                c.Box(g, "Wall", new Vector3(0, T + 14, 1), new Vector3(170, 28, 2), 0, c.Mat("Wall", new Color(0.9f, 0.87f, 0.78f), 0.2f));
                c.Box(g, "BoardFrame", new Vector3(-24, T + 15, -0.3f), new Vector3(44, 20, 0.6f), 0, c.Mat("BoardFrame", new Color(0.7f, 0.72f, 0.75f), 0.8f, 0.7f));
                c.Box(g, "Board", new Vector3(-24, T + 15, -0.8f), new Vector3(42, 18, 0.4f), 0, c.Mat("Whiteboard", new Color(0.97f, 0.97f, 0.97f), 0.9f));
                // 練習メニューの板書の代わりの線
                var markerBlue = c.Mat("MarkerBlue", new Color(0.15f, 0.3f, 0.75f), 0.3f);
                var markerRed = c.Mat("MarkerRed", new Color(0.8f, 0.15f, 0.12f), 0.3f);
                for (int i = 0; i < 5; i++)
                {
                    float len = 12 + (i * 7) % 16;
                    c.Box(g, $"Line_{i}", new Vector3(-42 + len * 0.5f, T + 21.5f - i * 3.2f, -1.05f), new Vector3(len, 0.8f, 0.1f), 0, i == 0 ? markerRed : markerBlue);
                }
                // 部旗(赤地に白の帯)
                c.Box(g, "Banner", new Vector3(16, T + 15, -0.3f), new Vector3(12, 20, 0.4f), 0, c.Mat("Banner", new Color(0.78f, 0.12f, 0.12f), 0.3f));
                c.Box(g, "BannerBand", new Vector3(16, T + 15, -0.6f), new Vector3(3, 15, 0.3f), 0, c.Mat("BannerBand", new Color(0.97f, 0.95f, 0.9f), 0.2f));
                // 賞状
                var certFrame = c.Mat("CertFrame", new Color(0.3f, 0.18f, 0.1f), 0.5f);
                var cert = c.Mat("Certificate", new Color(0.97f, 0.95f, 0.86f), 0.2f);
                for (int i = 0; i < 2; i++)
                {
                    float x = 36 + i * 15;
                    c.Box(g, $"CertFrame_{i}", new Vector3(x, T + 17, -0.25f), new Vector3(12, 9, 0.5f), 0, certFrame);
                    c.Box(g, $"Cert_{i}", new Vector3(x, T + 17, -0.6f), new Vector3(10.6f, 7.6f, 0.3f), 0, cert);
                }
            }

            // 練習用のだるま落とし(北西)。色の段の上にだるまの頭
            {
                var g = c.Anchored("PracticeDaruma", new Vector2(-0.8f, 1), 12);
                var cols = new[] { new Color(0.85f, 0.2f, 0.18f), new Color(0.2f, 0.4f, 0.8f), new Color(0.95f, 0.8f, 0.2f), new Color(0.25f, 0.6f, 0.3f), new Color(0.95f, 0.95f, 0.92f) };
                for (int i = 0; i < cols.Length; i++)
                {
                    c.Cylinder(g, $"Block_{i}", new Vector3(0, T + 1.1f + i * 2.2f, 0), 3, 2.2f, c.Mat($"Block{i}", cols[i], 0.5f));
                }
                c.Sphere(g, "Head", new Vector3(0, T + 14, 0), new Vector3(6.4f, 6.4f, 6.4f), c.Mat("DarumaRed", new Color(0.85f, 0.12f, 0.1f), 0.7f));
                c.Sphere(g, "Face", new Vector3(0, T + 14.4f, -2.5f), new Vector3(3.8f, 3.2f, 1.6f), c.Mat("DarumaFace", new Color(0.98f, 0.94f, 0.86f), 0.5f));
                var eye = c.Mat("DarumaEye", new Color(0.08f, 0.08f, 0.08f), 0.5f);
                c.Sphere(g, "EyeL", new Vector3(-0.8f, T + 14.8f, -3.2f), new Vector3(0.7f, 0.7f, 0.3f), eye);
                c.Sphere(g, "EyeR", new Vector3(0.8f, T + 14.8f, -3.2f), new Vector3(0.7f, 0.7f, 0.3f), eye);
            }

            // 木槌(南。寝かせてある)
            {
                var g = c.Anchored("Mallet", new Vector2(0.3f, -1), 5);
                c.Prim(PrimitiveType.Cylinder, g, "Head", new Vector3(0, T + 1.6f, 0), new Vector3(3.2f, 2.5f, 3.2f), Quaternion.Euler(0, 0, 90), c.Mat("MalletHead", new Color(0.72f, 0.52f, 0.3f), 0.4f));
                c.Prim(PrimitiveType.Cylinder, g, "Handle", new Vector3(0, T + 0.6f, -5.5f), new Vector3(1, 5, 1), Quaternion.Euler(90, 0, 0), c.Mat("MalletHandle", new Color(0.5f, 0.32f, 0.18f), 0.4f));
            }

            // 優勝カップ(北東)
            {
                var g = c.Anchored("Trophy", new Vector2(0.8f, 1), 12);
                var gold = c.Mat("Gold", new Color(0.95f, 0.75f, 0.25f), 0.85f, 0.9f);
                c.Box(g, "Base", new Vector3(0, T + 1.2f, 0), new Vector3(6, 2.4f, 6), 0, c.Mat("TrophyBase", new Color(0.2f, 0.14f, 0.1f), 0.6f));
                c.Cylinder(g, "Stem", new Vector3(0, T + 4.4f, 0), 0.8f, 4, gold);
                c.Sphere(g, "Cup", new Vector3(0, T + 8.4f, 0), new Vector3(6, 5.2f, 6), gold);
                c.Cylinder(g, "Rim", new Vector3(0, T + 10.6f, 0), 2.8f, 0.5f, gold);
                c.Prim(PrimitiveType.Capsule, g, "HandleL", new Vector3(-3.4f, T + 8.6f, 0), new Vector3(0.8f, 1.8f, 0.8f), Quaternion.Euler(0, 0, 20), gold);
                c.Prim(PrimitiveType.Capsule, g, "HandleR", new Vector3(3.4f, T + 8.6f, 0), new Vector3(0.8f, 1.8f, 0.8f), Quaternion.Euler(0, 0, -20), gold);
            }

            // スポーツドリンク(東)
            {
                var g = c.Anchored("SportsDrink", new Vector2(1, 0.2f), 9);
                c.Cylinder(g, "Bottle", new Vector3(0, T + 5.5f, 0), 2.2f, 11, c.Mat("Bottle", new Color(0.82f, 0.9f, 0.97f), 0.9f));
                c.Cylinder(g, "Label", new Vector3(0, T + 5, 0), 2.25f, 4.5f, c.Mat("DrinkLabel", new Color(0.15f, 0.4f, 0.85f), 0.4f));
                c.Cylinder(g, "Cap", new Vector3(0, T + 11.5f, 0), 1.1f, 1.2f, c.Mat("BottleCap", new Color(0.15f, 0.4f, 0.85f), 0.5f));
                c.Cylinder(g, "Cup", new Vector3(5, T + 2.5f, 2), 1.8f, 5, c.Mat("PaperCup", new Color(0.97f, 0.97f, 0.95f), 0.3f));
            }

            // タオル(西。畳んで置いてある)
            {
                var g = c.Anchored("Towel", new Vector2(-1, -0.3f), 7);
                var towel = c.Box(g, "Towel", new Vector3(0, T + 0.6f, 0), new Vector3(8, 1.2f, 12), 10, c.Mat("Towel", new Color(0.2f, 0.35f, 0.7f), 0.1f));
                c.Prim(PrimitiveType.Cube, towel.transform, "Band", new Vector3(0, 0.55f, 0), new Vector3(1.02f, 0.1f, 0.18f), Quaternion.identity, c.Mat("TowelBand", new Color(0.97f, 0.97f, 0.95f), 0.1f), true);
            }

            // 記録用のバインダー(南西。平たい)
            {
                var g = c.Anchored("Clipboard", new Vector2(-0.8f, -1), 5);
                var board = c.Box(g, "Board", new Vector3(0, T + 0.15f, 0), new Vector3(10, 0.3f, 14), -12, c.Mat("Clipboard", new Color(0.55f, 0.38f, 0.22f), 0.4f));
                c.Prim(PrimitiveType.Cube, board.transform, "Paper", new Vector3(0, 0.6f, -0.04f), new Vector3(0.88f, 0.2f, 0.82f), Quaternion.identity, c.Mat("Paper", new Color(0.97f, 0.97f, 0.95f), 0.1f), true);
                c.Prim(PrimitiveType.Cube, board.transform, "Clip", new Vector3(0, 1.5f, 0.44f), new Vector3(0.4f, 2.5f, 0.08f), Quaternion.identity, c.Mat("Clip", new Color(0.75f, 0.77f, 0.8f), 0.8f, 0.8f), true);
                var ink = c.Mat("Ink", new Color(0.2f, 0.2f, 0.25f), 0.1f);
                for (int i = 0; i < 6; i++)
                {
                    c.Prim(PrimitiveType.Cube, board.transform, $"Row_{i}", new Vector3(0, 0.75f, 0.28f - i * 0.11f), new Vector3(0.76f, 0.1f, 0.012f), Quaternion.identity, ink, true);
                }
            }

            // ストップウォッチ(南東。平たい)
            {
                var g = c.Anchored("Stopwatch", new Vector2(0.9f, -1), 5);
                var silver = c.Mat("WatchBody", new Color(0.78f, 0.8f, 0.83f), 0.85f, 0.8f);
                c.Cylinder(g, "Body", new Vector3(0, T + 0.5f, 0), 3, 1, silver);
                c.Cylinder(g, "Face", new Vector3(0, T + 0.55f, 0), 2.5f, 1.02f, c.Mat("WatchFace", new Color(0.97f, 0.97f, 0.95f), 0.6f));
                c.Cylinder(g, "Button", new Vector3(0, T + 0.5f, 3.4f), 0.6f, 0.8f, silver);
                c.Box(g, "Hand", new Vector3(0.6f, T + 1.08f, 0.6f), new Vector3(0.2f, 0.04f, 2), 45, c.Mat("WatchHand", new Color(0.85f, 0.15f, 0.12f), 0.4f));
            }
        }

        //----------------------------------------
        // 放課後の運動場
        //----------------------------------------
        static void BuildGround(Ctx c)
        {
            float T = kTableTop;
            // 床の下の支えは跳び箱の段、ステージの床は跳び箱の一番上の白い布
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 1.6f, 2.2f, null,
                c.Mat("VaultLight", new Color(0.86f, 0.68f, 0.44f), 0.35f),
                c.Mat("VaultDark", new Color(0.72f, 0.52f, 0.3f), 0.35f));
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.93f, 0.91f, 0.84f), 0.15f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.25f, 0.45f, 0.78f), 0.4f);

            // グラウンドの土
            var ground = c.Group("Ground");
            c.Box(ground, "Soil", new Vector3(0, T - 1, 0), new Vector3(720, 2, 720), 0, c.Mat("Soil", new Color(0.76f, 0.62f, 0.45f), 0.1f));
            var chalk = c.Mat("Chalk", new Color(0.97f, 0.97f, 0.95f), 0.1f);

            // 石灰で引いたトラックの線(ステージの南北)
            foreach (var (name, dir) in new[] { ("TrackSouth", -1f), ("TrackNorth", 1f) })
            {
                var g = c.Anchored(name, new Vector2(0, dir), 3);
                for (int i = 0; i < 4; i++) c.Box(g, $"Lane_{i}", new Vector3(0, T + 0.01f, dir * i * 6), new Vector3(400, 0.02f, 0.5f), 0, chalk);
            }

            // サッカーボール(東)
            {
                var g = c.Anchored("SoccerBall", new Vector2(1, 0.2f), 9);
                Vector3 center = new Vector3(0, T + 4.5f, 0);
                c.Sphere(g, "Ball", center, new Vector3(9, 9, 9), c.Mat("BallWhite", new Color(0.97f, 0.97f, 0.96f), 0.6f));
                var black = c.Mat("BallBlack", new Color(0.1f, 0.1f, 0.12f), 0.6f);
                var dirs = new[] { new Vector3(0, 1, 0), new Vector3(0.9f, 0.3f, 0.3f), new Vector3(-0.6f, 0.4f, -0.7f), new Vector3(0.2f, 0.2f, -0.95f), new Vector3(-0.8f, 0.2f, 0.6f), new Vector3(0.4f, -0.1f, 0.9f), new Vector3(0.8f, -0.3f, -0.5f) };
                for (int i = 0; i < dirs.Length; i++)
                {
                    c.Sphere(g, $"Patch_{i}", center + dirs[i].normalized * 3.5f, new Vector3(2.6f, 2.6f, 2.6f), black);
                }
            }

            // コーン(北西)
            {
                var g = c.Anchored("Cones", new Vector2(-0.8f, 1), 12);
                var orange = c.Mat("Cone", new Color(1.0f, 0.45f, 0.1f), 0.5f);
                var band = c.Mat("ConeBand", new Color(0.97f, 0.97f, 0.95f), 0.5f);
                var pts = new[] { new Vector3(-6, 0, 2), new Vector3(0, 0, 0), new Vector3(6, 0, -2) };
                for (int i = 0; i < pts.Length; i++)
                {
                    c.Box(g, $"Plate_{i}", pts[i] + new Vector3(0, T + 0.2f, 0), new Vector3(5.5f, 0.4f, 5.5f), 0, orange);
                    Cone(c, g, $"Cone_{i}", pts[i] + new Vector3(0, T + 0.4f, 0), 2.2f, 8, 6, orange, orange, orange, band, orange, orange);
                }
            }

            // ハードル(北東)
            {
                var g = c.Anchored("Hurdle", new Vector2(0.8f, 1), 14);
                var frame = c.Mat("HurdleFrame", new Color(0.75f, 0.77f, 0.8f), 0.8f, 0.7f);
                foreach (float x in new[] { -7f, 7f })
                {
                    c.Box(g, $"Leg_{x}", new Vector3(x, T + 4, 0), new Vector3(0.6f, 8, 0.6f), 0, frame);
                    c.Box(g, $"Foot_{x}", new Vector3(x, T + 0.2f, 1.5f), new Vector3(0.6f, 0.4f, 5), 0, frame);
                }
                var white = c.Mat("HurdleWhite", new Color(0.97f, 0.97f, 0.95f), 0.5f);
                var black = c.Mat("HurdleBlack", new Color(0.12f, 0.12f, 0.14f), 0.5f);
                for (int i = 0; i < 5; i++)
                {
                    c.Box(g, $"Bar_{i}", new Vector3(-6 + i * 3, T + 7.6f, 0), new Vector3(3, 1.6f, 0.4f), 0, i % 2 == 0 ? white : black);
                }
            }

            // ライン引き(北)。後ろに引いた線が伸びている
            {
                var g = c.Anchored("LineMarker", new Vector2(-0.15f, 1), 16);
                var green = c.Mat("MarkerBody", new Color(0.2f, 0.55f, 0.35f), 0.5f);
                c.Box(g, "Body", new Vector3(0, T + 3.5f, 0), new Vector3(5, 5, 7), 0, green);
                var wheel = c.Mat("Wheel", new Color(0.15f, 0.15f, 0.17f), 0.4f);
                c.Prim(PrimitiveType.Cylinder, g, "WheelL", new Vector3(-2.8f, T + 1.5f, -2), new Vector3(3, 0.3f, 3), Quaternion.Euler(0, 0, 90), wheel);
                c.Prim(PrimitiveType.Cylinder, g, "WheelR", new Vector3(2.8f, T + 1.5f, -2), new Vector3(3, 0.3f, 3), Quaternion.Euler(0, 0, 90), wheel);
                c.Prim(PrimitiveType.Capsule, g, "Handle", new Vector3(0, T + 8, 5), new Vector3(0.6f, 4, 0.6f), Quaternion.Euler(35, 0, 0), c.Mat("MarkerHandle", new Color(0.7f, 0.72f, 0.75f), 0.7f, 0.6f));
                c.Box(g, "Line", new Vector3(0, T + 0.012f, 18), new Vector3(1, 0.02f, 30), 0, chalk);
            }

            // バットとボール(南。寝かせてある)
            {
                var g = c.Anchored("Bat", new Vector2(0.2f, -1), 5);
                var wood = c.Mat("BatWood", new Color(0.85f, 0.7f, 0.48f), 0.6f);
                c.Prim(PrimitiveType.Capsule, g, "Barrel", new Vector3(-3, T + 0.9f, 0), new Vector3(1.8f, 4, 1.8f), Quaternion.Euler(0, 10, 90), wood);
                c.Prim(PrimitiveType.Cylinder, g, "Grip", new Vector3(4.5f, T + 0.5f, -1.3f), new Vector3(0.9f, 4, 0.9f), Quaternion.Euler(0, 10, 90), wood);
                c.Sphere(g, "Ball", new Vector3(-4, T + 1.2f, -4), new Vector3(2.4f, 2.4f, 2.4f), c.Mat("Baseball", new Color(0.97f, 0.96f, 0.92f), 0.4f));
            }

            // ベース(南西。平たい)
            {
                var g = c.Anchored("Base", new Vector2(-0.8f, -1), 5);
                c.Box(g, "Base", new Vector3(0, T + 0.15f, 0), new Vector3(6, 0.3f, 6), 45, chalk);
            }

            // バックネット(北の奥)
            {
                var g = c.Anchored("Backstop", new Vector2(0, 1), 30);
                var metal = c.Mat("Fence", new Color(0.55f, 0.58f, 0.6f), 0.7f, 0.7f);
                for (int i = 0; i <= 8; i++) c.Cylinder(g, $"Post_{i}", new Vector3(-80 + i * 20, T + 15, 0), 0.8f, 30, metal);
                for (int i = 0; i <= 10; i++) c.Box(g, $"WireH_{i}", new Vector3(0, T + 1 + i * 2.8f, 0), new Vector3(160, 0.2f, 0.2f), 0, metal);
                for (int i = 0; i <= 53; i++) c.Box(g, $"WireV_{i}", new Vector3(-79.5f + i * 3, T + 15, 0.1f), new Vector3(0.2f, 29, 0.2f), 0, metal);
            }
        }

        //----------------------------------------
        // 老舗の道場
        //----------------------------------------
        static void BuildDojo(Ctx c)
        {
            float T = kTableTop;
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 1.2f, 1.6f, null,
                c.Mat("TatamiA", new Color(0.66f, 0.66f, 0.4f), 0.15f),
                c.Mat("TatamiB", new Color(0.58f, 0.6f, 0.36f), 0.15f));
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.78f, 0.75f, 0.5f), 0.15f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.32f, 0.2f, 0.1f), 0.5f);

            // 板の間(東西に長い板を南北に並べる)
            var floor = c.Group("WoodFloor");
            var boardA = c.Mat("BoardA", new Color(0.72f, 0.55f, 0.35f), 0.6f);
            var boardB = c.Mat("BoardB", new Color(0.66f, 0.49f, 0.3f), 0.6f);
            for (int i = -20; i < 20; i++)
            {
                c.Box(floor, $"Board_{i + 20}", new Vector3(0, T - 1, i * 8 + 4), new Vector3(400, 2, 8), 0, i % 2 == 0 ? boardA : boardB);
            }

            // 奥の壁(漆喰と柱)と掛け軸・名札掛け
            {
                var g = c.Anchored("BackWall", new Vector2(0, 1), 20);
                var wood = c.Mat("Pillar", new Color(0.32f, 0.2f, 0.1f), 0.5f);
                c.Box(g, "Wall", new Vector3(0, T + 15, 1), new Vector3(200, 30, 2), 0, c.Mat("Plaster", new Color(0.93f, 0.91f, 0.85f), 0.15f));
                c.Box(g, "Beam", new Vector3(0, T + 28.5f, -0.5f), new Vector3(200, 2, 1.5f), 0, wood);
                c.Box(g, "Baseboard", new Vector3(0, T + 1, -0.5f), new Vector3(200, 2, 1.5f), 0, wood);
                for (int i = 0; i < 6; i++) c.Box(g, $"Pillar_{i}", new Vector3(-100 + i * 40, T + 15, -0.8f), new Vector3(2.4f, 30, 2), 0, wood);
                // 掛け軸
                c.Box(g, "Scroll", new Vector3(0, T + 16, -0.3f), new Vector3(8, 16, 0.3f), 0, c.Mat("ScrollPaper", new Color(0.95f, 0.92f, 0.82f), 0.1f));
                c.Box(g, "ScrollEdge", new Vector3(0, T + 16, -0.1f), new Vector3(9.5f, 19, 0.2f), 0, c.Mat("ScrollCloth", new Color(0.3f, 0.35f, 0.25f), 0.2f));
                var rod = c.Mat("ScrollRod", new Color(0.15f, 0.1f, 0.08f), 0.5f);
                c.Prim(PrimitiveType.Cylinder, g, "RodBottom", new Vector3(0, T + 6.4f, -0.6f), new Vector3(0.8f, 5.5f, 0.8f), Quaternion.Euler(0, 0, 90), rod);
                var ink = c.Mat("Ink", new Color(0.08f, 0.08f, 0.08f), 0.1f);
                c.Box(g, "Stroke_0", new Vector3(0, T + 20.5f, -0.5f), new Vector3(1.3f, 4.5f, 0.1f), 0, ink);
                c.Box(g, "Stroke_1", new Vector3(0.3f, T + 15.5f, -0.5f), new Vector3(4, 1.1f, 0.1f), 0, ink);
                c.Box(g, "Stroke_2", new Vector3(-0.3f, T + 11.5f, -0.5f), new Vector3(1.1f, 4.5f, 0.1f), 0, ink);
                // 名札掛け(門下生の木札)
                var board = c.Mat("NameBoard", new Color(0.55f, 0.38f, 0.2f), 0.4f);
                var tag = c.Mat("NameTag", new Color(0.88f, 0.78f, 0.58f), 0.3f);
                c.Box(g, "NameBoard", new Vector3(38, T + 17, -0.4f), new Vector3(28, 13, 0.5f), 0, board);
                for (int row = 0; row < 2; row++)
                {
                    for (int col = 0; col < 7; col++)
                    {
                        var t = c.Box(g, $"Tag_{row}_{col}", new Vector3(27.2f + col * 3.6f, T + 20 - row * 6, -0.8f), new Vector3(2.6f, 5, 0.3f), 0, tag);
                        c.Prim(PrimitiveType.Cube, t.transform, "Name", new Vector3(0, 0.05f, -0.6f), new Vector3(0.18f, 0.65f, 0.4f), Quaternion.identity, ink, true);
                    }
                }
                // 神棚
                c.Box(g, "Shelf", new Vector3(-36, T + 21, -2.5f), new Vector3(16, 0.8f, 5), 0, c.Mat("Shelf", new Color(0.86f, 0.76f, 0.56f), 0.4f));
                c.Box(g, "Shrine", new Vector3(-36, T + 23.9f, -2.5f), new Vector3(7, 5, 3), 0, c.Mat("Shrine", new Color(0.9f, 0.82f, 0.62f), 0.4f));
                c.Box(g, "ShrineRoof", new Vector3(-36, T + 26.9f, -2.5f), new Vector3(9, 1, 4.2f), 0, wood);
            }

            // 大きなだるま(北西)
            {
                var g = c.Anchored("Daruma", new Vector2(-0.8f, 1), 10);
                c.Sphere(g, "Body", new Vector3(0, T + 5, 0), new Vector3(10, 10.5f, 9), c.Mat("DarumaRed", new Color(0.82f, 0.1f, 0.08f), 0.75f));
                c.Sphere(g, "Face", new Vector3(0, T + 6.5f, -3.4f), new Vector3(5.6f, 4.6f, 2.4f), c.Mat("DarumaFace", new Color(0.98f, 0.94f, 0.86f), 0.5f));
                var eye = c.Mat("DarumaEye", new Color(0.08f, 0.08f, 0.08f), 0.5f);
                c.Sphere(g, "EyeL", new Vector3(-1.2f, T + 7, -4.5f), new Vector3(1.1f, 1.1f, 0.4f), eye);
                c.Sphere(g, "EyeR", new Vector3(1.2f, T + 7, -4.5f), new Vector3(1.1f, 1.1f, 0.4f), eye);
                c.Box(g, "Belly", new Vector3(0, T + 3, -4.2f), new Vector3(3, 2, 0.4f), 0, c.Mat("DarumaGold", new Color(0.95f, 0.75f, 0.25f), 0.8f, 0.8f));
            }

            // 太鼓(北東)。台に横向きに載せてある
            {
                var g = c.Anchored("Taiko", new Vector2(0.8f, 1), 12);
                var stand = c.Mat("TaikoStand", new Color(0.3f, 0.18f, 0.1f), 0.5f);
                c.Box(g, "StandL", new Vector3(-3.5f, T + 2.5f, 0), new Vector3(1.2f, 5, 8), 0, stand);
                c.Box(g, "StandR", new Vector3(3.5f, T + 2.5f, 0), new Vector3(1.2f, 5, 8), 0, stand);
                c.Prim(PrimitiveType.Cylinder, g, "Body", new Vector3(0, T + 9, 0), new Vector3(10, 5, 10), Quaternion.Euler(0, 0, 90), c.Mat("TaikoBody", new Color(0.55f, 0.28f, 0.14f), 0.6f));
                var skin = c.Mat("TaikoSkin", new Color(0.92f, 0.86f, 0.7f), 0.3f);
                c.Prim(PrimitiveType.Cylinder, g, "SkinL", new Vector3(-5.05f, T + 9, 0), new Vector3(9.4f, 0.1f, 9.4f), Quaternion.Euler(0, 0, 90), skin);
                c.Prim(PrimitiveType.Cylinder, g, "SkinR", new Vector3(5.05f, T + 9, 0), new Vector3(9.4f, 0.1f, 9.4f), Quaternion.Euler(0, 0, 90), skin);
                c.Prim(PrimitiveType.Cylinder, g, "Bachi", new Vector3(0, T + 0.4f, -7), new Vector3(0.7f, 4, 0.7f), Quaternion.Euler(0, 15, 90), c.Mat("Bachi", new Color(0.8f, 0.66f, 0.45f), 0.4f));
            }

            // 竹刀掛け(西)
            {
                var g = c.Anchored("ShinaiRack", new Vector2(-1, 0.1f), 7);
                var wood = c.Mat("RackWood", new Color(0.45f, 0.3f, 0.16f), 0.5f);
                c.Box(g, "Base", new Vector3(0, T + 0.5f, 0), new Vector3(4, 1, 14), 0, wood);
                c.Box(g, "Bar", new Vector3(0, T + 9, 0), new Vector3(1.5f, 1, 14), 0, wood);
                c.Box(g, "PostN", new Vector3(0, T + 5, 6.5f), new Vector3(1, 9, 1), 0, wood);
                c.Box(g, "PostS", new Vector3(0, T + 5, -6.5f), new Vector3(1, 9, 1), 0, wood);
                var bamboo = c.Mat("Bamboo", new Color(0.88f, 0.78f, 0.5f), 0.4f);
                var grip = c.Mat("ShinaiGrip", new Color(0.95f, 0.93f, 0.88f), 0.2f);
                for (int i = 0; i < 4; i++)
                {
                    float z = -4.2f + i * 2.8f;
                    c.Cylinder(g, $"Shinai_{i}", new Vector3(0, T + 6, z), 0.45f, 10, bamboo);
                    c.Cylinder(g, $"Grip_{i}", new Vector3(0, T + 11.5f, z), 0.55f, 3, grip);
                }
            }

            // 水桶(東)
            {
                var g = c.Anchored("Bucket", new Vector2(1, 0.2f), 8);
                c.Cylinder(g, "Bucket", new Vector3(0, T + 2.5f, 0), 3.5f, 5, c.Mat("Bucket", new Color(0.78f, 0.62f, 0.4f), 0.4f));
                var hoop = c.Mat("Hoop", new Color(0.25f, 0.2f, 0.15f), 0.5f);
                c.Cylinder(g, "HoopTop", new Vector3(0, T + 4, 0), 3.55f, 0.5f, hoop);
                c.Cylinder(g, "HoopBottom", new Vector3(0, T + 1, 0), 3.55f, 0.5f, hoop);
                c.Cylinder(g, "Water", new Vector3(0, T + 4.6f, 0), 3.1f, 0.5f, c.Mat("Water", new Color(0.4f, 0.6f, 0.75f), 0.95f));
                c.Box(g, "Tenugui", new Vector3(5, T + 0.2f, 1), new Vector3(4, 0.4f, 8), 15, c.Mat("Tenugui", new Color(0.25f, 0.3f, 0.6f), 0.1f));
            }

            // 木刀(南。寝かせてある)
            {
                var g = c.Anchored("Bokken", new Vector2(0.2f, -1), 4);
                var wood = c.Mat("Bokken", new Color(0.62f, 0.4f, 0.22f), 0.6f);
                for (int i = 0; i < 2; i++)
                {
                    c.Box(g, $"Blade_{i}", new Vector3(0, T + 0.4f, i * 2.4f), new Vector3(22, 0.8f, 1.2f), 4 - i * 6, wood);
                    c.Cylinder(g, $"Tsuba_{i}", new Vector3(-5, T + 0.6f, i * 2.4f + (i == 0 ? 0.35f : -0.5f)), 1.2f, 0.4f, c.Mat("Tsuba", new Color(0.1f, 0.1f, 0.1f), 0.5f));
                }
            }

            // 座布団(南東。平たい)
            {
                var g = c.Anchored("Zabuton", new Vector2(0.9f, -1), 5);
                c.Box(g, "Zabuton", new Vector3(0, T + 0.5f, 0), new Vector3(9, 1, 9), 8, c.Mat("Zabuton", new Color(0.45f, 0.18f, 0.35f), 0.2f));
            }

            // 書道の半紙と筆(南西。平たい)
            {
                var g = c.Anchored("Shodo", new Vector2(-0.8f, -1), 5);
                var paper = c.Box(g, "Hanshi", new Vector3(0, T + 0.1f, 0), new Vector3(8, 0.2f, 11), -8, c.Mat("Hanshi", new Color(0.97f, 0.96f, 0.93f), 0.1f));
                var ink = c.Mat("Ink", new Color(0.08f, 0.08f, 0.08f), 0.1f);
                c.Prim(PrimitiveType.Cube, paper.transform, "Stroke_0", new Vector3(0, 0.55f, 0.2f), new Vector3(0.6f, 0.1f, 0.08f), Quaternion.identity, ink, true);
                c.Prim(PrimitiveType.Cube, paper.transform, "Stroke_1", new Vector3(0, 0.55f, -0.1f), new Vector3(0.1f, 0.1f, 0.6f), Quaternion.identity, ink, true);
                c.Prim(PrimitiveType.Cylinder, g, "Brush", new Vector3(6, T + 0.4f, 2), new Vector3(0.6f, 4, 0.6f), Quaternion.Euler(0, 20, 90), c.Mat("Brush", new Color(0.55f, 0.4f, 0.2f), 0.4f));
            }
        }

        //----------------------------------------
        // 満員のスタジアム
        //----------------------------------------
        static void BuildStadium(Ctx c)
        {
            float T = kTableTop;
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 1.2f, 1.8f, null,
                c.Mat("RigGray", new Color(0.78f, 0.8f, 0.83f), 0.6f, 0.4f),
                c.Mat("RigNavy", new Color(0.18f, 0.24f, 0.45f), 0.5f, 0.3f));
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.62f, 0.74f, 0.92f), 0.4f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.92f, 0.94f, 0.98f), 0.7f);

            // 芝生(刈り目の縞)とセンターライン・センターサークル
            var field = c.Group("Field");
            var turfA = c.Mat("TurfA", new Color(0.3f, 0.6f, 0.25f), 0.2f);
            var turfB = c.Mat("TurfB", new Color(0.25f, 0.52f, 0.21f), 0.2f);
            for (int i = 0; i < 36; i++)
            {
                c.Box(field, $"Turf_{i}", new Vector3(0, T - 1, -350 + i * 20), new Vector3(720, 2, 20), 0, i % 2 == 0 ? turfA : turfB);
            }
            var line = c.Mat("Line", new Color(0.97f, 0.97f, 0.95f), 0.2f);
            c.Box(field, "HalfwayLine", new Vector3(0, T + 0.015f, 0), new Vector3(720, 0.02f, 0.8f), 0, line);
            Ring(c, field, "CenterCircle", new Vector3(0, T + 0.01f, 0), 34, 72, 0.8f, 0.02f, line);

            // 北のスタンド(段の上に観客が並ぶ)と、手前の広告板
            {
                var g = c.Anchored("NorthStand", new Vector2(0, 1), 18);
                var concrete = c.Mat("Concrete", new Color(0.6f, 0.6f, 0.62f), 0.2f);
                var adCols = new[] { new Color(0.85f, 0.15f, 0.15f), new Color(0.15f, 0.35f, 0.8f), new Color(0.98f, 0.8f, 0.15f), new Color(0.97f, 0.97f, 0.95f) };
                for (int i = 0; i < 13; i++)
                {
                    c.Box(g, $"Ad_{i}", new Vector3(-120 + i * 20, T + 2, -1), new Vector3(20, 4, 1), 0, c.Mat($"Ad{i % adCols.Length}", adCols[i % adCols.Length], 0.5f));
                }
                var fanCols = new[] { new Color(0.85f, 0.15f, 0.15f), new Color(0.15f, 0.35f, 0.8f), new Color(0.97f, 0.97f, 0.95f), new Color(0.95f, 0.75f, 0.15f), new Color(0.2f, 0.2f, 0.22f) };
                var skin = c.Mat("Skin", new Color(0.93f, 0.76f, 0.6f), 0.3f);
                for (int i = 0; i < 5; i++)
                {
                    float h = 4 * (i + 1);
                    float z = i * 8 + 5;
                    c.Box(g, $"Tier_{i}", new Vector3(0, T + h * 0.5f, z), new Vector3(260, h, 8), 0, concrete);
                    for (int j = 0; j < 26; j++)
                    {
                        float x = -125 + j * 10 + ((i * 7 + j * 3) % 5 - 2) * 0.6f;
                        int col = (i * 5 + j * 3) % fanCols.Length;
                        c.Box(g, $"Fan_{i}_{j}", new Vector3(x, T + h + 2, z), new Vector3(4, 4, 3), 0, c.Mat($"Fan{col}", fanCols[col], 0.3f));
                        c.Sphere(g, $"FanHead_{i}_{j}", new Vector3(x, T + h + 5.2f, z), new Vector3(2.4f, 2.4f, 2.4f), skin);
                    }
                }
            }

            // 照明塔(北西・北東の奥)
            {
                var steel = c.Mat("LightTower", new Color(0.7f, 0.72f, 0.75f), 0.7f, 0.7f);
                var lamp = c.Mat("Lamp", new Color(1.0f, 0.98f, 0.85f), 0.9f);
                foreach (var (name, dir) in new[] { ("LightTowerW", new Vector2(-1, 1)), ("LightTowerE", new Vector2(1, 1)) })
                {
                    var g = c.Anchored(name, dir, 40);
                    c.Cylinder(g, "Pole", new Vector3(0, T + 35, 0), 1.5f, 70, steel);
                    c.Box(g, "Panel", new Vector3(0, T + 72, 0), new Vector3(24, 12, 2), 0, steel);
                    for (int row = 0; row < 3; row++)
                    {
                        for (int col = 0; col < 5; col++)
                        {
                            c.Sphere(g, $"Lamp_{row}_{col}", new Vector3(-9.6f + col * 4.8f, T + 68 + row * 4, -1.2f), new Vector3(3.4f, 3.4f, 1.2f), lamp);
                        }
                    }
                }
            }

            // テレビカメラ(西)。三脚に載せてある
            {
                var g = c.Anchored("TvCamera", new Vector2(-1, 0.15f), 9);
                var black = c.Mat("CameraBody", new Color(0.15f, 0.15f, 0.17f), 0.6f);
                for (int i = 0; i < 3; i++)
                {
                    float a = (90 + i * 120) * Mathf.Deg2Rad;
                    c.Prim(PrimitiveType.Capsule, g, $"Leg_{i}", new Vector3(Mathf.Cos(a) * 1.6f, T + 4, Mathf.Sin(a) * 1.6f), new Vector3(0.5f, 4.2f, 0.5f), Quaternion.Euler(Mathf.Sin(a) * 20, 0, -Mathf.Cos(a) * 20), black);
                }
                c.Box(g, "Body", new Vector3(0, T + 10, 0), new Vector3(4, 4, 7), 0, black);
                c.Prim(PrimitiveType.Cylinder, g, "Lens", new Vector3(0, T + 10, -4.6f), new Vector3(2.6f, 1.2f, 2.6f), Quaternion.Euler(90, 0, 0), c.Mat("Lens", new Color(0.1f, 0.15f, 0.3f), 0.95f));
                c.Box(g, "Logo", new Vector3(2.02f, T + 10.5f, 0), new Vector3(0.05f, 1.2f, 3), 0, c.Mat("CameraLogo", new Color(0.85f, 0.15f, 0.15f), 0.5f));
            }

            // 表彰台(東)
            {
                var g = c.Anchored("Podium", new Vector2(1, 0.2f), 9);
                var white = c.Mat("Podium", new Color(0.95f, 0.95f, 0.95f), 0.5f);
                c.Box(g, "First", new Vector3(0, T + 3, 0), new Vector3(5, 6, 5), 0, white);
                c.Box(g, "Second", new Vector3(0, T + 2, 5), new Vector3(5, 4, 5), 0, white);
                c.Box(g, "Third", new Vector3(0, T + 1.5f, -5), new Vector3(5, 3, 5), 0, white);
                c.Box(g, "Mark1", new Vector3(-2.52f, T + 3.5f, 0), new Vector3(0.05f, 2, 1.2f), 0, c.Mat("MarkGold", new Color(0.95f, 0.75f, 0.25f), 0.8f, 0.8f));
                c.Box(g, "Mark2", new Vector3(-2.52f, T + 2.3f, 5), new Vector3(0.05f, 1.6f, 1.2f), 0, c.Mat("MarkSilver", new Color(0.78f, 0.8f, 0.83f), 0.8f, 0.8f));
                c.Box(g, "Mark3", new Vector3(-2.52f, T + 1.7f, -5), new Vector3(0.05f, 1.4f, 1.2f), 0, c.Mat("MarkBronze", new Color(0.72f, 0.45f, 0.25f), 0.8f, 0.8f));
            }

            // コーナーフラッグ(北西・北東)
            {
                var pole = c.Mat("FlagPole", new Color(0.97f, 0.97f, 0.95f), 0.5f);
                var flag = c.Mat("CornerFlag", new Color(0.98f, 0.82f, 0.15f), 0.3f);
                foreach (var (name, dir) in new[] { ("CornerFlagW", new Vector2(-0.9f, 1)), ("CornerFlagE", new Vector2(0.9f, 1)) })
                {
                    var g = c.Anchored(name, dir, 8);
                    c.Cylinder(g, "Pole", new Vector3(0, T + 6, 0), 0.3f, 12, pole);
                    c.Box(g, "Flag", new Vector3(1.8f, T + 10.5f, 0), new Vector3(3.6f, 2.6f, 0.1f), 0, flag);
                }
            }

            // 応援幕(南。芝生に広げてある)
            {
                var g = c.Anchored("CheerBanner", new Vector2(0, -1), 3);
                var banner = c.Box(g, "Banner", new Vector3(0, T + 0.05f, 0), new Vector3(36, 0.1f, 5), 0, c.Mat("CheerBanner", new Color(0.97f, 0.97f, 0.95f), 0.2f));
                var red = c.Mat("CheerInk", new Color(0.82f, 0.12f, 0.12f), 0.2f);
                for (int i = 0; i < 6; i++)
                {
                    c.Prim(PrimitiveType.Cube, banner.transform, $"Letter_{i}", new Vector3(-0.38f + i * 0.152f, 0.55f, 0), new Vector3(0.09f, 0.1f, 0.6f), Quaternion.identity, red, true);
                }
            }
        }

        //----------------------------------------
        // 雪のゲレンデ
        //----------------------------------------
        static void BuildSlope(Ctx c)
        {
            float T = kTableTop;
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 1.0f, 1.6f, null,
                c.Mat("SnowBlock", new Color(0.94f, 0.96f, 0.98f), 0.3f),
                c.Mat("IceBlock", new Color(0.72f, 0.85f, 0.95f), 0.85f));
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.88f, 0.93f, 0.98f), 0.5f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.55f, 0.75f, 0.95f), 0.9f);

            // 雪原と、南に残ったスキーの跡
            var snow = c.Mat("Snow", new Color(0.95f, 0.96f, 0.98f), 0.3f);
            c.Box(c.Group("Snowfield"), "Snow", new Vector3(0, T - 1, 0), new Vector3(720, 2, 720), 0, snow);
            {
                var g = c.Anchored("SkiTracks", new Vector2(0, -1), 3);
                var track = c.Mat("SkiTrack", new Color(0.8f, 0.85f, 0.92f), 0.3f);
                for (int i = 0; i < 3; i++)
                {
                    float x = -30 + i * 30;
                    c.Box(g, $"TrackL_{i}", new Vector3(x - 0.8f, T + 0.01f, 0), new Vector3(0.6f, 0.02f, 40), 25 - i * 15, track);
                    c.Box(g, $"TrackR_{i}", new Vector3(x + 0.8f, T + 0.01f, 0), new Vector3(0.6f, 0.02f, 40), 25 - i * 15, track);
                }
            }

            // もみの木(雪をかぶっている)
            var trunk = c.Mat("Trunk", new Color(0.4f, 0.27f, 0.16f), 0.3f);
            var needle = c.Mat("Needle", new Color(0.16f, 0.38f, 0.24f), 0.3f);
            void Tree(Transform g, string name, Vector3 p, float s)
            {
                c.Cylinder(g, $"{name}_Trunk", p + new Vector3(0, T + 2 * s, 0), 1.0f * s, 4 * s, trunk);
                Cone(c, g, $"{name}_Low", p + new Vector3(0, T + 3 * s, 0), 7 * s, 7 * s, 4, needle);
                Cone(c, g, $"{name}_Mid", p + new Vector3(0, T + 8 * s, 0), 5.5f * s, 6 * s, 4, needle);
                Cone(c, g, $"{name}_Top", p + new Vector3(0, T + 12.5f * s, 0), 4 * s, 5 * s, 4, needle, needle, snow, snow);
            }
            {
                var g = c.Anchored("TreesNW", new Vector2(-0.8f, 1), 14);
                Tree(g, "Tree0", new Vector3(0, 0, 0), 1.0f);
                Tree(g, "Tree1", new Vector3(-12, 0, 6), 1.3f);
                Tree(g, "Tree2", new Vector3(10, 0, 10), 0.8f);
            }
            {
                var g = c.Anchored("TreesNE", new Vector2(0.8f, 1), 16);
                Tree(g, "Tree0", new Vector3(0, 0, 0), 1.2f);
                Tree(g, "Tree1", new Vector3(12, 0, 8), 0.9f);
            }
            {
                var g = c.Anchored("TreeW", new Vector2(-1, -0.2f), 12);
                Tree(g, "Tree0", new Vector3(0, 0, 0), 0.9f);
            }

            // 雪だるま(東)
            {
                var g = c.Anchored("Snowman", new Vector2(1, 0.2f), 9);
                c.Sphere(g, "Bottom", new Vector3(0, T + 3.6f, 0), new Vector3(8, 7.4f, 8), snow);
                c.Sphere(g, "Middle", new Vector3(0, T + 9, 0), new Vector3(6, 5.6f, 6), snow);
                c.Sphere(g, "Head", new Vector3(0, T + 13.3f, 0), new Vector3(4.4f, 4.2f, 4.4f), snow);
                var coal = c.Mat("Coal", new Color(0.1f, 0.1f, 0.1f), 0.4f);
                c.Sphere(g, "EyeL", new Vector3(-0.8f, T + 13.9f, -2), new Vector3(0.6f, 0.6f, 0.6f), coal);
                c.Sphere(g, "EyeR", new Vector3(0.8f, T + 13.9f, -2), new Vector3(0.6f, 0.6f, 0.6f), coal);
                c.Prim(PrimitiveType.Capsule, g, "Nose", new Vector3(0, T + 13.2f, -2.8f), new Vector3(0.6f, 1.2f, 0.6f), Quaternion.Euler(90, 0, 0), c.Mat("Carrot", new Color(0.95f, 0.5f, 0.1f), 0.4f));
                var red = c.Mat("Scarf", new Color(0.82f, 0.15f, 0.15f), 0.2f);
                c.Cylinder(g, "Scarf", new Vector3(0, T + 11.4f, 0), 2.6f, 0.9f, red);
                c.Cylinder(g, "Bucket", new Vector3(0, T + 16.2f, 0), 1.8f, 2.4f, red);
            }

            // 雪に刺したスキー板とストック(北)
            {
                var g = c.Anchored("Skis", new Vector2(-0.1f, 1), 18);
                var ski = c.Mat("Ski", new Color(0.15f, 0.4f, 0.85f), 0.7f);
                c.Prim(PrimitiveType.Cube, g, "SkiL", new Vector3(-1, T + 7, 0), new Vector3(1.4f, 16, 0.3f), Quaternion.Euler(0, 0, 5), ski);
                c.Prim(PrimitiveType.Cube, g, "SkiR", new Vector3(1, T + 7, 0), new Vector3(1.4f, 16, 0.3f), Quaternion.Euler(0, 0, -5), ski);
                var pole = c.Mat("SkiPole", new Color(0.75f, 0.77f, 0.8f), 0.8f, 0.7f);
                var basket = c.Mat("PoleBasket", new Color(0.15f, 0.15f, 0.17f), 0.4f);
                foreach (float x in new[] { 5f, 7f })
                {
                    c.Prim(PrimitiveType.Cylinder, g, $"Pole_{x}", new Vector3(x, T + 6, 0), new Vector3(0.4f, 6, 0.4f), Quaternion.Euler(0, 0, (x - 6) * 6), pole);
                    c.Cylinder(g, $"Basket_{x}", new Vector3(x + (x - 6) * 0.3f, T + 1.5f, 0), 1.0f, 0.2f, basket);
                }
            }

            // 旗門(南)。低い旗
            {
                var g = c.Anchored("SlalomGates", new Vector2(0.2f, -1), 5);
                var red = c.Mat("GateRed", new Color(0.85f, 0.15f, 0.15f), 0.4f);
                var blue = c.Mat("GateBlue", new Color(0.15f, 0.3f, 0.8f), 0.4f);
                for (int i = 0; i < 2; i++)
                {
                    var mat = i == 0 ? red : blue;
                    float x = -6 + i * 12;
                    c.Cylinder(g, $"Pole_{i}", new Vector3(x, T + 3, 0), 0.25f, 6, mat);
                    c.Box(g, $"Flag_{i}", new Vector3(x + 1.5f, T + 4.6f, 0), new Vector3(3, 2.4f, 0.1f), 0, mat);
                }
            }

            // そり(南東。平たい)
            {
                var g = c.Anchored("Sled", new Vector2(0.9f, -1), 5);
                var red = c.Mat("Sled", new Color(0.85f, 0.2f, 0.15f), 0.6f);
                c.Box(g, "Body", new Vector3(0, T + 1, 0), new Vector3(6, 0.8f, 10), -15, red);
                var runner = c.Mat("Runner", new Color(0.3f, 0.3f, 0.32f), 0.7f, 0.6f);
                c.Box(g, "RunnerL", new Vector3(-2.6f, T + 0.3f, -0.7f), new Vector3(0.4f, 0.6f, 10.5f), -15, runner);
                c.Box(g, "RunnerR", new Vector3(2.6f, T + 0.3f, 0.7f), new Vector3(0.4f, 0.6f, 10.5f), -15, runner);
            }

            // 奥の雪山(とがった山。上の方は雪)
            {
                var g = c.Anchored("Mountains", new Vector2(0, 1), 70);
                var rock = c.Mat("Rock", new Color(0.5f, 0.56f, 0.66f), 0.2f);
                var peaks = new[] { (new Vector3(-65, 0, 20), 34f, 40f), (new Vector3(5, 0, 40), 44f, 56f), (new Vector3(75, 0, 15), 30f, 36f) };
                for (int i = 0; i < peaks.Length; i++)
                {
                    var (p, radius, height) = peaks[i];
                    Cone(c, g, $"Mountain_{i}", p + new Vector3(0, T, 0), radius, height, 10, rock, rock, rock, rock, rock, rock, snow, snow, snow, snow);
                }
            }
        }

        //----------------------------------------
        // 豪華客船のデッキ
        //----------------------------------------
        static void BuildCruise(Ctx c)
        {
            float T = kTableTop;
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 1.0f, 1.6f, null,
                c.Mat("HullWhite", new Color(0.95f, 0.95f, 0.95f), 0.6f),
                c.Mat("HullNavy", new Color(0.15f, 0.22f, 0.42f), 0.6f));
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.82f, 0.68f, 0.48f), 0.55f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.92f, 0.94f, 0.96f), 0.8f);

            // チーク材のデッキ(東西に長い板を南北に並べる)と海。デッキは北の手すりの少し先で終わり、その先は海
            var deck = c.Group("Deck");
            var teakA = c.Mat("TeakA", new Color(0.62f, 0.42f, 0.26f), 0.55f);
            var teakB = c.Mat("TeakB", new Color(0.56f, 0.37f, 0.22f), 0.55f);
            for (int i = -32; i < 10; i++)
            {
                c.Box(deck, $"Plank_{i + 32}", new Vector3(0, T - 1, i * 5 + 2.5f), new Vector3(400, 2, 5), 0, i % 2 == 0 ? teakA : teakB);
            }
            c.Box(deck, "Hull", new Vector3(0, T - 14, 50.5f), new Vector3(400, 26, 1), 0, c.Mat("Hull", new Color(0.95f, 0.95f, 0.95f), 0.6f));
            FarFloor(c, "Sea", c.Mat("Sea", new Color(0.1f, 0.35f, 0.6f), 0.9f));

            // プール(北)
            {
                var g = c.Anchored("Pool", new Vector2(0, 1), 14);
                var tile = c.Mat("PoolTile", new Color(0.95f, 0.96f, 0.97f), 0.7f);
                c.Box(g, "RimS", new Vector3(0, T + 0.6f, -13), new Vector3(64, 1.2f, 2), 0, tile);
                c.Box(g, "RimN", new Vector3(0, T + 0.6f, 13), new Vector3(64, 1.2f, 2), 0, tile);
                c.Box(g, "RimW", new Vector3(-31, T + 0.6f, 0), new Vector3(2, 1.2f, 24), 0, tile);
                c.Box(g, "RimE", new Vector3(31, T + 0.6f, 0), new Vector3(2, 1.2f, 24), 0, tile);
                c.Box(g, "Water", new Vector3(0, T + 0.4f, 0), new Vector3(60, 0.6f, 24), 0, c.Mat("PoolWater", new Color(0.3f, 0.75f, 0.9f), 0.95f));
                var steel = c.Mat("Ladder", new Color(0.8f, 0.82f, 0.85f), 0.9f, 0.9f);
                c.Prim(PrimitiveType.Capsule, g, "LadderL", new Vector3(24, T + 3, -13), new Vector3(0.5f, 3, 0.5f), Quaternion.identity, steel);
                c.Prim(PrimitiveType.Capsule, g, "LadderR", new Vector3(27, T + 3, -13), new Vector3(0.5f, 3, 0.5f), Quaternion.identity, steel);
            }

            // デッキチェア(東)
            {
                var g = c.Anchored("DeckChairs", new Vector2(1, 0.1f), 8);
                var frame = c.Mat("ChairFrame", new Color(0.95f, 0.95f, 0.95f), 0.6f);
                var cushion = c.Mat("Cushion", new Color(0.15f, 0.3f, 0.6f), 0.2f);
                for (int i = 0; i < 2; i++)
                {
                    float x = i * 8;
                    c.Box(g, $"Seat_{i}", new Vector3(x, T + 2.5f, -1), new Vector3(5, 0.8f, 10), 0, frame);
                    c.Box(g, $"Cushion_{i}", new Vector3(x, T + 3.1f, -1), new Vector3(4.4f, 0.4f, 9.4f), 0, cushion);
                    c.Prim(PrimitiveType.Cube, g, $"Back_{i}", new Vector3(x, T + 5, 5.8f), new Vector3(5, 0.8f, 6), Quaternion.Euler(-50, 0, 0), frame);
                    for (int k = 0; k < 4; k++)
                    {
                        float lx = x + (k % 2 == 0 ? -2 : 2), lz = k < 2 ? -5 : 3;
                        c.Box(g, $"Leg_{i}_{k}", new Vector3(lx, T + 1.05f, lz), new Vector3(0.6f, 2.1f, 0.6f), 0, frame);
                    }
                }
            }

            // パラソル(西)
            {
                var g = c.Anchored("Parasol", new Vector2(-1, 0.2f), 9);
                c.Cylinder(g, "Pole", new Vector3(0, T + 8, 0), 0.4f, 16, c.Mat("ParasolPole", new Color(0.85f, 0.85f, 0.85f), 0.7f, 0.5f));
                c.Cylinder(g, "Base", new Vector3(0, T + 0.5f, 0), 2.5f, 1, c.Mat("ParasolBase", new Color(0.3f, 0.3f, 0.32f), 0.5f));
                Cone(c, g, "Canopy", new Vector3(0, T + 13, 0), 10, 4.5f, 6,
                    c.Mat("CanopyWhite", new Color(0.97f, 0.97f, 0.95f), 0.3f),
                    c.Mat("CanopyRed", new Color(0.85f, 0.2f, 0.2f), 0.3f));
            }

            // トロピカルドリンクのテーブル(北東)
            {
                var g = c.Anchored("DrinkTable", new Vector2(0.8f, 1), 12);
                var white = c.Mat("TableWhite", new Color(0.95f, 0.95f, 0.95f), 0.6f);
                c.Cylinder(g, "Top", new Vector3(0, T + 7.25f, 0), 4.5f, 0.5f, white);
                c.Cylinder(g, "Leg", new Vector3(0, T + 3.5f, 0), 0.5f, 7, white);
                c.Cylinder(g, "Foot", new Vector3(0, T + 0.2f, 0), 2.5f, 0.4f, white);
                var glass = c.Mat("DrinkGlass", new Color(0.85f, 0.92f, 0.95f), 0.95f);
                var cols = new[] { new Color(1.0f, 0.55f, 0.15f), new Color(0.95f, 0.3f, 0.45f) };
                for (int i = 0; i < 2; i++)
                {
                    float x = -1.8f + i * 3.6f;
                    c.Cylinder(g, $"Glass_{i}", new Vector3(x, T + 9.3f, 0), 1.1f, 3.6f, glass);
                    c.Cylinder(g, $"Juice_{i}", new Vector3(x, T + 9.1f, 0), 0.95f, 3.0f, c.Mat($"Juice{i}", cols[i], 0.7f));
                    c.Prim(PrimitiveType.Cylinder, g, $"Straw_{i}", new Vector3(x + 0.4f, T + 11.5f, 0), new Vector3(0.2f, 1.5f, 0.2f), Quaternion.Euler(0, 0, -15), c.Mat("Straw", new Color(0.2f, 0.7f, 0.4f), 0.5f));
                    c.Sphere(g, $"Fruit_{i}", new Vector3(x - 0.9f, T + 11.2f, 0), new Vector3(1.2f, 1.2f, 0.4f), c.Mat("Lime", new Color(0.55f, 0.85f, 0.25f), 0.5f));
                }
            }

            // 浮き輪(南東。平たい)
            {
                var g = c.Anchored("Lifebuoy", new Vector2(0.9f, -1), 5);
                Ring(c, g, "Ring", new Vector3(0, T + 0.7f, 0), 3.2f, 16, 1.8f, 1.4f,
                    c.Mat("BuoyRed", new Color(0.9f, 0.2f, 0.15f), 0.6f),
                    c.Mat("BuoyWhite", new Color(0.97f, 0.97f, 0.95f), 0.6f),
                    c.Mat("BuoyRed", new Color(0.9f, 0.2f, 0.15f), 0.6f),
                    c.Mat("BuoyWhite", new Color(0.97f, 0.97f, 0.95f), 0.6f));
            }

            // 巻いたロープ(南西。平たい)
            {
                var g = c.Anchored("Rope", new Vector2(-0.8f, -1), 5);
                var rope = c.Mat("Rope", new Color(0.82f, 0.72f, 0.52f), 0.2f);
                const int kSegments = 40;
                for (int i = 0; i < kSegments; i++)
                {
                    float a = i / (float)kSegments * Mathf.PI * 2 * 3;
                    float a2 = (i + 1) / (float)kSegments * Mathf.PI * 2 * 3;
                    float r = 0.8f + i / (float)kSegments * 3.2f;
                    float r2 = 0.8f + (i + 1) / (float)kSegments * 3.2f;
                    Vector3 p1 = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                    Vector3 p2 = new Vector3(Mathf.Cos(a2) * r2, 0, Mathf.Sin(a2) * r2);
                    c.Prim(PrimitiveType.Cube, g, $"Rope_{i}", (p1 + p2) * 0.5f + new Vector3(0, T + 0.5f, 0), new Vector3(0.9f, 0.8f, Vector3.Distance(p1, p2) + 0.2f), Quaternion.LookRotation(p2 - p1), rope);
                }
            }

            // 北の手すり(この先は海)
            {
                var g = c.Anchored("Railing", new Vector2(0, 1), 32);
                var white = c.Mat("Railing", new Color(0.96f, 0.96f, 0.96f), 0.7f);
                for (int i = 0; i <= 40; i++) c.Cylinder(g, $"Post_{i}", new Vector3(-120 + i * 6, T + 3.5f, 0), 0.4f, 7, white);
                c.Box(g, "RailTop", new Vector3(0, T + 7.2f, 0), new Vector3(242, 0.7f, 0.9f), 0, white);
                c.Box(g, "RailMid", new Vector3(0, T + 4, 0), new Vector3(242, 0.4f, 0.5f), 0, white);
            }

            // 遠くの島(海の上)
            {
                var g = c.Anchored("Island", new Vector2(0.4f, 1), 120);
                c.Sphere(g, "Island", new Vector3(0, T - 37, 0), new Vector3(90, 30, 50), c.Mat("Island", new Color(0.3f, 0.55f, 0.3f), 0.2f));
                c.Sphere(g, "Beach", new Vector3(0, T - 37.3f, 0), new Vector3(104, 8, 60), c.Mat("Beach", new Color(0.93f, 0.85f, 0.62f), 0.2f));
            }
        }

        //----------------------------------------
        // ジャングルの遺跡
        //----------------------------------------
        static void BuildRuins(Ctx c)
        {
            float T = kTableTop;
            var stoneGray = c.Mat("StoneGray", new Color(0.55f, 0.53f, 0.48f), 0.2f);
            var stoneMoss = c.Mat("StoneMoss", new Color(0.46f, 0.5f, 0.4f), 0.2f);
            var stoneSand = c.Mat("StoneSand", new Color(0.66f, 0.6f, 0.47f), 0.2f);
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 1.2f, 2.0f, null, stoneGray, stoneMoss, stoneSand);
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.72f, 0.66f, 0.52f), 0.2f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.38f, 0.45f, 0.32f), 0.4f);

            // 密林の土と石畳(石の隙間から土が見える)
            var ground = c.Group("Ground");
            c.Box(ground, "Soil", new Vector3(0, T - 2, 0), new Vector3(720, 2, 720), 0, c.Mat("JungleSoil", new Color(0.28f, 0.32f, 0.2f), 0.1f));
            for (int i = -6; i <= 6; i++)
            {
                for (int j = -6; j <= 6; j++)
                {
                    var mat = ((i * 3 + j * 5) % 3 + 3) % 3 == 0 ? stoneMoss : (((i + j) & 1) == 0 ? stoneGray : stoneSand);
                    c.Box(ground, $"Flag_{i + 6}_{j + 6}", new Vector3(i * 16, T - 0.5f, j * 16), new Vector3(15.4f, 1, 15.4f), 0, mat);
                }
            }

            // 石の頭像(北西)
            {
                var g = c.Anchored("StoneHead", new Vector2(-0.8f, 1), 12);
                c.Box(g, "Head", new Vector3(0, T + 6, 0), new Vector3(8, 12, 7), 0, stoneGray);
                c.Box(g, "Brow", new Vector3(0, T + 8.6f, -3.8f), new Vector3(7, 1.2f, 1), 0, stoneGray);
                var dark = c.Mat("Carving", new Color(0.2f, 0.2f, 0.18f), 0.1f);
                c.Box(g, "EyeL", new Vector3(-1.8f, T + 7.4f, -3.55f), new Vector3(1.8f, 1, 0.2f), 0, dark);
                c.Box(g, "EyeR", new Vector3(1.8f, T + 7.4f, -3.55f), new Vector3(1.8f, 1, 0.2f), 0, dark);
                c.Box(g, "Nose", new Vector3(0, T + 5.6f, -4), new Vector3(1.6f, 3, 1.2f), 0, stoneGray);
                c.Box(g, "Mouth", new Vector3(0, T + 2.8f, -3.55f), new Vector3(4, 0.8f, 0.2f), 0, dark);
                c.Sphere(g, "Moss", new Vector3(0, T + 12, 0), new Vector3(8.4f, 2, 7.4f), c.Mat("Moss", new Color(0.3f, 0.48f, 0.2f), 0.1f));
            }

            // たいまつ(北東・北)
            {
                var flame = c.Mat("Flame", new Color(1.0f, 0.5f, 0.1f), 0.9f);
                var core = c.Mat("FlameCore", new Color(1.0f, 0.85f, 0.3f), 0.9f);
                var bowl = c.Mat("TorchBowl", new Color(0.3f, 0.25f, 0.2f), 0.5f, 0.4f);
                foreach (var (name, dir, margin) in new[] { ("TorchNE", new Vector2(0.8f, 1), 12f), ("TorchN", new Vector2(-0.15f, 1), 16f) })
                {
                    var g = c.Anchored(name, dir, margin);
                    c.Cylinder(g, "Pillar", new Vector3(0, T + 5, 0), 1.2f, 10, stoneSand);
                    c.Cylinder(g, "Bowl", new Vector3(0, T + 10.6f, 0), 2.2f, 1.2f, bowl);
                    c.Sphere(g, "Flame", new Vector3(0, T + 12.4f, 0), new Vector3(3, 4, 3), flame);
                    c.Sphere(g, "Core", new Vector3(0, T + 12, -0.4f), new Vector3(1.8f, 2.4f, 1.8f), core);
                }
            }

            // 折れた柱(東)
            {
                var g = c.Anchored("Columns", new Vector2(1, 0.2f), 9);
                c.Cylinder(g, "Column", new Vector3(0, T + 6, 0), 2.4f, 12, stoneSand);
                c.Box(g, "Capital", new Vector3(0, T + 12.5f, 0), new Vector3(6, 1, 6), 0, stoneSand);
                c.Cylinder(g, "Broken", new Vector3(3, T + 2.5f, 7), 2.4f, 5, stoneMoss);
                c.Box(g, "Rubble_0", new Vector3(-4, T + 0.8f, -4), new Vector3(2.6f, 1.6f, 2), 25, stoneGray);
                c.Box(g, "Rubble_1", new Vector3(5, T + 0.6f, -2), new Vector3(1.8f, 1.2f, 2.2f), -15, stoneSand);
            }

            // 倒れた柱(南。寝かせてある)
            {
                var g = c.Anchored("FallenColumn", new Vector2(0.2f, -1), 5);
                c.Prim(PrimitiveType.Cylinder, g, "Column", new Vector3(0, T + 2, 0), new Vector3(4, 8, 4), Quaternion.Euler(0, 12, 90), stoneGray);
                c.Box(g, "Rubble", new Vector3(10, T + 0.7f, 1), new Vector3(2.4f, 1.4f, 2.4f), 30, stoneMoss);
            }

            // 密林の草(西)
            {
                var g = c.Anchored("Plants", new Vector2(-1, -0.1f), 8);
                var leafDark = c.Mat("LeafDark", new Color(0.16f, 0.38f, 0.15f), 0.4f);
                var leafLight = c.Mat("LeafLight", new Color(0.3f, 0.55f, 0.2f), 0.4f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 45;
                    var leaf = c.Sphere(g, $"Leaf_{i}", new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * 3, T + 2.5f + (i % 3), Mathf.Sin(a * Mathf.Deg2Rad) * 3), new Vector3(2.6f, 0.4f, 7), i % 2 == 0 ? leafDark : leafLight);
                    leaf.transform.rotation = Quaternion.Euler(-25, 90 - a, 0);
                }
                c.Sphere(g, "Bush", new Vector3(-3, T + 2, 6), new Vector3(8, 5, 7), leafDark);
            }

            // 宝箱(南東)
            {
                var g = c.Anchored("Chest", new Vector2(0.9f, -1), 5);
                var wood = c.Mat("ChestWood", new Color(0.45f, 0.26f, 0.12f), 0.4f);
                var gold = c.Mat("Gold", new Color(0.95f, 0.75f, 0.25f), 0.85f, 0.9f);
                c.Box(g, "Body", new Vector3(0, T + 1.75f, 0), new Vector3(6, 3.5f, 4), -10, wood);
                c.Prim(PrimitiveType.Cylinder, g, "Lid", new Vector3(0, T + 3.5f, 0), new Vector3(4, 3, 3), Quaternion.Euler(0, -10, 90), wood);
                c.Box(g, "BandL", new Vector3(-1.9f, T + 2.6f, -0.33f), new Vector3(0.5f, 5.4f, 4.1f), -10, gold);
                c.Box(g, "BandR", new Vector3(1.9f, T + 2.6f, 0.33f), new Vector3(0.5f, 5.4f, 4.1f), -10, gold);
                for (int i = 0; i < 4; i++)
                {
                    c.Cylinder(g, $"Coin_{i}", new Vector3(-4 + i * 1.6f, T + 0.1f, -3 + (i % 2) * 1.2f), 0.6f, 0.2f, gold);
                }
            }

            // 階段状の神殿(北の奥)
            {
                var g = c.Anchored("Temple", new Vector2(0, 1), 40);
                for (int i = 0; i < 6; i++)
                {
                    float w = 90 - i * 14;
                    c.Box(g, $"Step_{i}", new Vector3(0, T + i * 8 + 4, 20), new Vector3(w, 8, w * 0.7f), 0, i % 2 == 0 ? stoneGray : stoneMoss);
                }
                c.Box(g, "Shrine", new Vector3(0, T + 54, 20), new Vector3(14, 12, 10), 0, stoneSand);
            }
        }

        //----------------------------------------
        // 宇宙ステーション
        //----------------------------------------
        static void BuildSpace(Ctx c)
        {
            float T = kTableTop;
            SetSupports(c, StageBackdrop.SupportStyle.SlabStack, 0.8f, 1.4f, null,
                c.Mat("PlateLight", new Color(0.7f, 0.72f, 0.76f), 0.7f, 0.6f),
                c.Mat("PlateDark", new Color(0.35f, 0.37f, 0.42f), 0.7f, 0.6f));
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.82f, 0.85f, 0.9f), 0.6f, 0.3f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.3f, 0.85f, 0.95f), 0.8f);

            // 金属のパネルの床(継ぎ目の線は縦横で高さをずらす)
            var floor = c.Group("PanelFloor");
            c.Box(floor, "Panels", new Vector3(0, T - 1, 0), new Vector3(400, 2, 400), 0, c.Mat("Panel", new Color(0.5f, 0.53f, 0.58f), 0.6f, 0.6f));
            var seam = c.Mat("Seam", new Color(0.22f, 0.24f, 0.28f), 0.4f);
            for (int i = -16; i <= 16; i++)
            {
                c.Box(floor, $"SeamX_{i + 16}", new Vector3(0, T + 0.01f, i * 12), new Vector3(392, 0.02f, 0.3f), 0, seam);
                c.Box(floor, $"SeamZ_{i + 16}", new Vector3(i * 12, T + 0.02f, 0), new Vector3(0.3f, 0.02f, 392), 0, seam);
            }

            // 奥の大窓(星空と地球)
            {
                var g = c.Anchored("Window", new Vector2(0, 1), 22);
                var frame = c.Mat("WindowFrame", new Color(0.3f, 0.32f, 0.36f), 0.6f, 0.6f);
                c.Box(g, "Wall", new Vector3(0, T + 30, 2), new Vector3(240, 60, 2), 0, frame);
                c.Box(g, "Space", new Vector3(0, T + 30, 0.8f), new Vector3(150, 44, 0.4f), 0, c.Mat("SpaceBlack", new Color(0.02f, 0.02f, 0.05f), 0.9f));
                for (int i = 0; i < 4; i++) c.Box(g, $"Mullion_{i}", new Vector3(-56 + i * 37.5f, T + 30, 0.3f), new Vector3(2, 44, 1), 0, frame);
                var star = c.Mat("Star", new Color(0.97f, 0.97f, 1.0f), 0.9f);
                var rng = new System.Random(7);
                for (int i = 0; i < 50; i++)
                {
                    float x = (float)rng.NextDouble() * 146 - 73;
                    float y = (float)rng.NextDouble() * 40 + 10;
                    float s = 0.4f + (float)rng.NextDouble() * 0.6f;
                    c.Sphere(g, $"Star_{i}", new Vector3(x, T + y, 0.5f), new Vector3(s, s, 0.1f), star);
                }
                c.Sphere(g, "Earth", new Vector3(30, T + 24, 0.45f), new Vector3(28, 28, 0.2f), c.Mat("EarthOcean", new Color(0.15f, 0.4f, 0.85f), 0.8f));
                var land = c.Mat("EarthLand", new Color(0.3f, 0.6f, 0.3f), 0.4f);
                c.Sphere(g, "Land_0", new Vector3(24, T + 28, 0.35f), new Vector3(9, 7, 0.1f), land);
                c.Sphere(g, "Land_1", new Vector3(36, T + 20, 0.35f), new Vector3(7, 9, 0.1f), land);
                c.Sphere(g, "Cloud", new Vector3(28, T + 18, 0.3f), new Vector3(12, 3, 0.1f), c.Mat("Cloud", new Color(0.97f, 0.97f, 0.98f), 0.5f));
            }

            // 操作盤(北東)
            {
                var g = c.Anchored("Console", new Vector2(0.8f, 1), 12);
                var body = c.Mat("ConsoleBody", new Color(0.82f, 0.84f, 0.88f), 0.6f, 0.4f);
                c.Box(g, "Base", new Vector3(0, T + 3, 0), new Vector3(14, 6, 7), 0, body);
                var panel = c.Prim(PrimitiveType.Cube, g, "Panel", new Vector3(0, T + 7, 1.5f), new Vector3(14, 0.8f, 6), Quaternion.Euler(-30, 0, 0), body);
                c.Prim(PrimitiveType.Cube, panel.transform, "Screen", new Vector3(-0.2f, 0.55f, 0.05f), new Vector3(0.5f, 0.1f, 0.75f), Quaternion.identity, c.Mat("Screen", new Color(0.1f, 0.35f, 0.55f), 0.95f), true);
                var cols = new[] { new Color(0.95f, 0.25f, 0.2f), new Color(0.3f, 0.9f, 0.4f), new Color(0.98f, 0.8f, 0.2f) };
                for (int i = 0; i < 6; i++)
                {
                    c.Prim(PrimitiveType.Cube, panel.transform, $"Button_{i}", new Vector3(0.18f + (i % 2) * 0.13f, 0.55f, 0.25f - (i / 2) * 0.25f), new Vector3(0.07f, 0.3f, 0.15f), Quaternion.identity, c.Mat($"Button{i % 3}", cols[i % 3], 0.8f), true);
                }
            }

            // 酸素ボンベ(西)
            {
                var g = c.Anchored("OxygenTanks", new Vector2(-1, 0.2f), 8);
                var white = c.Mat("Tank", new Color(0.95f, 0.95f, 0.96f), 0.7f);
                var band = c.Mat("TankBand", new Color(1.0f, 0.5f, 0.1f), 0.5f);
                var valve = c.Mat("Valve", new Color(0.6f, 0.62f, 0.66f), 0.8f, 0.8f);
                for (int i = 0; i < 3; i++)
                {
                    float z = -5 + i * 5;
                    c.Cylinder(g, $"Tank_{i}", new Vector3(0, T + 6, z), 2, 12, white);
                    c.Sphere(g, $"Dome_{i}", new Vector3(0, T + 12, z), new Vector3(4, 2.4f, 4), white);
                    c.Cylinder(g, $"Band_{i}", new Vector3(0, T + 8, z), 2.05f, 1, band);
                    c.Cylinder(g, $"Valve_{i}", new Vector3(0, T + 13.6f, z), 0.6f, 1.2f, valve);
                }
            }

            // ロボットアーム(北西)
            {
                var g = c.Anchored("RobotArm", new Vector2(-0.8f, 1), 12);
                var arm = c.Mat("Arm", new Color(0.95f, 0.95f, 0.96f), 0.6f, 0.3f);
                var joint = c.Mat("Joint", new Color(0.3f, 0.32f, 0.36f), 0.7f, 0.6f);
                c.Cylinder(g, "Base", new Vector3(0, T + 1, 0), 3.5f, 2, joint);
                c.Prim(PrimitiveType.Capsule, g, "Arm1", new Vector3(0, T + 6, 0), new Vector3(2, 4.5f, 2), Quaternion.identity, arm);
                c.Sphere(g, "Joint1", new Vector3(0, T + 10.5f, 0), new Vector3(2.6f, 2.6f, 2.6f), joint);
                c.Prim(PrimitiveType.Capsule, g, "Arm2", new Vector3(3.2f, T + 12.5f, 0), new Vector3(1.6f, 4, 1.6f), Quaternion.Euler(0, 0, -60), arm);
                c.Sphere(g, "Joint2", new Vector3(6.2f, T + 14.2f, 0), new Vector3(2, 2, 2), joint);
                c.Box(g, "ClawL", new Vector3(6.8f, T + 12.6f, -0.7f), new Vector3(0.5f, 2.4f, 0.5f), 0, joint);
                c.Box(g, "ClawR", new Vector3(6.8f, T + 12.6f, 0.7f), new Vector3(0.5f, 2.4f, 0.5f), 0, joint);
            }

            // 宇宙服のヘルメット(東)
            {
                var g = c.Anchored("Helmet", new Vector2(1, 0.15f), 8);
                c.Cylinder(g, "NeckRing", new Vector3(0, T + 0.6f, 0), 3, 1.2f, c.Mat("NeckRing", new Color(0.55f, 0.57f, 0.62f), 0.8f, 0.7f));
                c.Sphere(g, "Shell", new Vector3(0, T + 4.6f, 0), new Vector3(8, 8, 8), c.Mat("HelmetShell", new Color(0.96f, 0.96f, 0.97f), 0.7f));
                c.Sphere(g, "Visor", new Vector3(0, T + 4.6f, -2.4f), new Vector3(6, 4.4f, 4), c.Mat("Visor", new Color(0.85f, 0.65f, 0.2f), 0.95f, 0.9f));
            }

            // コンテナ(南東)。黄色と黒の縞
            {
                var g = c.Anchored("Crate", new Vector2(0.9f, -1), 5);
                var crate = c.Box(g, "Crate", new Vector3(0, T + 2, 0), new Vector3(6, 4, 6), 15, c.Mat("Crate", new Color(0.4f, 0.42f, 0.46f), 0.5f, 0.4f));
                var yellow = c.Mat("Hazard", new Color(0.98f, 0.8f, 0.1f), 0.5f);
                var black = c.Mat("HazardBlack", new Color(0.1f, 0.1f, 0.12f), 0.5f);
                for (int i = 0; i < 6; i++)
                {
                    c.Prim(PrimitiveType.Cube, crate.transform, $"Stripe_{i}", new Vector3(-0.42f + i * 0.168f, 0.35f, 0), new Vector3(0.168f, 0.15f, 1.02f), Quaternion.identity, i % 2 == 0 ? yellow : black, true);
                }
            }

            // 床をはうケーブル(南)
            {
                var g = c.Anchored("Cables", new Vector2(0.2f, -1), 4);
                var cols = new[] { c.Mat("CableRed", new Color(0.85f, 0.2f, 0.15f), 0.6f), c.Mat("CableBlue", new Color(0.2f, 0.4f, 0.85f), 0.6f) };
                for (int k = 0; k < 2; k++)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        float x1 = -14 + i * 3.5f, x2 = x1 + 3.5f;
                        float z1 = Mathf.Sin(i * 0.9f + k) * 1.5f + k * 2.5f, z2 = Mathf.Sin((i + 1) * 0.9f + k) * 1.5f + k * 2.5f;
                        Vector3 p1 = new Vector3(x1, 0, z1), p2 = new Vector3(x2, 0, z2);
                        c.Prim(PrimitiveType.Cube, g, $"Cable_{k}_{i}", (p1 + p2) * 0.5f + new Vector3(0, T + 0.35f, 0), new Vector3(0.7f, 0.7f, Vector3.Distance(p1, p2) + 0.3f), Quaternion.LookRotation(p2 - p1), cols[k]);
                    }
                }
            }
        }

        // 机の天板の高さ。積み木が消える高さ(TumikiBlock の -10)のすぐ下にして、落ちた物が天板に着くあたりで消えるようにする
        const float kTableTop = -10.5f;

        static void BuildChanoma(Ctx c)
        {
            float T = kTableTop;

            // ステージの床・壁・床の下の支え(本の山)
            c.Backdrop.Supports.Style = StageBackdrop.SupportStyle.BookStack;
            c.Backdrop.Supports.BottomY = T;
            c.Backdrop.Supports.CoverMaterials = new[]
            {
                c.Mat("BookRed", new Color(0.62f, 0.16f, 0.14f), 0.3f),
                c.Mat("BookNavy", new Color(0.14f, 0.2f, 0.38f), 0.3f),
                c.Mat("BookGreen", new Color(0.2f, 0.38f, 0.26f), 0.3f),
                c.Mat("BookMustard", new Color(0.78f, 0.6f, 0.2f), 0.3f),
                c.Mat("BookBrown", new Color(0.42f, 0.27f, 0.17f), 0.3f),
            };
            c.Backdrop.Supports.PageMaterial = c.Mat("BookPage", new Color(0.94f, 0.9f, 0.8f), 0.1f);
            c.FloorMaterial = c.Mat("StageFloor", new Color(0.86f, 0.75f, 0.56f), 0.35f);
            c.WallMaterial = c.SeeThroughMat("StageWall", new Color(0.45f, 0.26f, 0.15f), 0.5f);

            // ちゃぶ台(丸い天板と脚)と畳の床
            var wood = c.Mat("TableWood", new Color(0.5f, 0.3f, 0.16f), 0.6f);
            var woodDark = c.Mat("TableWoodDark", new Color(0.32f, 0.18f, 0.09f), 0.5f);
            var table = c.Group("Chabudai");
            c.Cylinder(table, "Top", new Vector3(0, T - 1.5f, 0), 85, 3, wood);
            c.Cylinder(table, "Edge", new Vector3(0, T - 2.6f, 0), 85.6f, 1.2f, woodDark);
            for (int i = 0; i < 4; i++)
            {
                float a = (45 + i * 90) * Mathf.Deg2Rad;
                c.Box(table, $"Leg_{i}", new Vector3(Mathf.Cos(a) * 58, T - 3 - 17, Mathf.Sin(a) * 58), new Vector3(6, 34, 6), 0, woodDark);
            }
            // 天板の木目(細い線)
            var grain = c.Mat("TableGrain", new Color(0.42f, 0.24f, 0.12f), 0.6f);
            for (int i = -5; i <= 5; i++)
            {
                float z = i * 14 + 3;
                float half = Mathf.Sqrt(Mathf.Max(0, 84 * 84 - z * z));
                if (half < 5) continue;
                c.Box(table, $"Grain_{i}", new Vector3(0, T + 0.01f, z), new Vector3(half * 2 * 0.96f, 0.02f, 0.25f), 0, grain);
            }

            var tatami = c.Mat("Tatami", new Color(0.72f, 0.7f, 0.42f), 0.15f);
            var tatamiEdge = c.Mat("TatamiEdge", new Color(0.18f, 0.25f, 0.16f), 0.2f);
            var floor = c.Group("TatamiFloor");
            float floorY = T - 3 - 34;
            c.Box(floor, "Tatami", new Vector3(0, floorY - 0.5f, 0), new Vector3(720, 1, 720), 0, tatami);
            // 畳の縁(90×180 の畳を互い違いに敷いた線)
            for (int i = -4; i <= 4; i++)
            {
                c.Box(floor, $"EdgeX_{i}", new Vector3(0, floorY + 0.02f, i * 90 + 45), new Vector3(720, 0.05f, 2), 0, tatamiEdge);
                for (int j = -2; j <= 2; j++)
                {
                    float x = j * 180 + (i % 2 == 0 ? 0 : 90);
                    // 交差する縁どうしの上面が同じ高さにならないよう、少し高くする
                    c.Box(floor, $"EdgeZ_{i}_{j}", new Vector3(x, floorY + 0.05f, i * 90), new Vector3(2, 0.05f, 90), 0, tatamiEdge);
                }
            }

            // 湯呑み(北西)
            {
                var g = c.Anchored("Yunomi", new Vector2(-0.75f, 1), 9);
                var celadon = c.Mat("Yunomi", new Color(0.56f, 0.66f, 0.58f), 0.85f);
                var celadonDark = c.Mat("YunomiDark", new Color(0.38f, 0.48f, 0.42f), 0.85f);
                c.Cylinder(g, "Foot", new Vector3(0, T + 0.3f, 0), 3.8f, 0.6f, celadonDark);
                c.Cylinder(g, "Body", new Vector3(0, T + 6, 0), 5, 11, celadon);
                // 上面どうしが同じ高さだとちらつくので、縁・本体・お茶の上面の高さはそれぞれずらす
                c.Cylinder(g, "Rim", new Vector3(0, T + 11.2f, 0), 5.08f, 0.5f, celadonDark);
                c.Cylinder(g, "Tea", new Vector3(0, T + 11.42f, 0), 4.5f, 0.5f, c.Mat("Tea", new Color(0.44f, 0.52f, 0.16f), 0.3f));
            }

            // 急須(北東)。注ぎ口は西、横手は手前
            {
                var g = c.Anchored("Kyusu", new Vector2(0.8f, 1), 12);
                var clay = c.Mat("Kyusu", new Color(0.6f, 0.28f, 0.18f), 0.55f);
                var clayDark = c.Mat("KyusuDark", new Color(0.4f, 0.17f, 0.1f), 0.5f);
                c.Sphere(g, "Body", new Vector3(0, T + 5, 0), new Vector3(13, 10, 13), clay);
                c.Cylinder(g, "Lid", new Vector3(0, T + 9.9f, 0), 3.4f, 0.8f, clay);
                c.Sphere(g, "Knob", new Vector3(0, T + 10.8f, 0), new Vector3(1.6f, 1.2f, 1.6f), clayDark);
                c.Prim(PrimitiveType.Capsule, g, "Spout", new Vector3(-7.4f, T + 7, 0), new Vector3(1.6f, 3.2f, 1.6f), Quaternion.Euler(0, 0, 55), clay);
                c.Prim(PrimitiveType.Capsule, g, "Handle", new Vector3(0, T + 6, -9.5f), new Vector3(2, 4.5f, 2), Quaternion.Euler(-75, 0, 0), clayDark);
            }

            // 茶筒(北)
            {
                var g = c.Anchored("Chazutsu", new Vector2(-0.1f, 1), 16);
                var tin = c.Mat("Chazutsu", new Color(0.17f, 0.36f, 0.27f), 0.7f, 0.5f);
                var tinBand = c.Mat("ChazutsuBand", new Color(0.75f, 0.6f, 0.3f), 0.8f, 0.8f);
                c.Cylinder(g, "Body", new Vector3(0, T + 6.5f, 0), 3.5f, 13, tin);
                c.Cylinder(g, "Lid", new Vector3(0, T + 13.4f, 0), 3.6f, 1.8f, tin);
                c.Cylinder(g, "Band", new Vector3(0, T + 12.4f, 0), 3.62f, 0.4f, tinBand);
            }

            // ティッシュの箱(東の奥)
            {
                var g = c.Anchored("TissueBox", new Vector2(1, 0.45f), 14);
                c.Box(g, "Box", new Vector3(0, T + 3, 0), new Vector3(7, 6, 14), 0, c.Mat("TissueBox", new Color(0.62f, 0.78f, 0.86f), 0.3f));
                c.Box(g, "Slot", new Vector3(0, T + 6.01f, 0), new Vector3(2, 0.05f, 8), 0, c.Mat("TissueSlot", new Color(0.35f, 0.5f, 0.6f), 0.3f));
                c.Sphere(g, "Tissue", new Vector3(0, T + 7.2f, 0), new Vector3(3.5f, 3, 4.5f), c.Mat("Tissue", new Color(0.97f, 0.97f, 0.95f), 0.1f));
            }

            // みかんの籠(東)
            {
                var g = c.Anchored("MikanBasket", new Vector2(1, -0.2f), 11);
                var basket = c.Mat("Basket", new Color(0.78f, 0.6f, 0.36f), 0.2f);
                c.Cylinder(g, "Basket", new Vector3(0, T + 1.6f, 0), 7.5f, 3.2f, basket);
                c.Cylinder(g, "BasketRim", new Vector3(0, T + 3.1f, 0), 7.7f, 0.4f, c.Mat("BasketRim", new Color(0.6f, 0.44f, 0.24f), 0.2f));
                var mikan = c.Mat("Mikan", new Color(1.0f, 0.55f, 0.08f), 0.45f);
                var leaf = c.Mat("Leaf", new Color(0.2f, 0.45f, 0.15f), 0.4f);
                var points = new[] { new Vector3(-2.6f, 4.6f, -2.2f), new Vector3(2.7f, 4.6f, -1.8f), new Vector3(-2.2f, 4.6f, 2.8f), new Vector3(2.5f, 4.6f, 2.6f), new Vector3(0.2f, 8.2f, 0.3f) };
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 p = points[i] + new Vector3(0, T, 0);
                    c.Sphere(g, $"Mikan_{i}", p, new Vector3(5, 4.2f, 5), mikan);
                    c.Sphere(g, $"Leaf_{i}", p + new Vector3(0.6f, 2.1f, 0.3f), new Vector3(1.6f, 0.3f, 0.8f), leaf);
                }
            }

            // せんべいの皿(西)
            {
                var g = c.Anchored("SenbeiPlate", new Vector2(-1, -0.15f), 10);
                c.Cylinder(g, "PlateRim", new Vector3(0, T + 0.3f, 0), 7.2f, 0.6f, c.Mat("PlateRim", new Color(0.2f, 0.32f, 0.6f), 0.8f));
                c.Cylinder(g, "Plate", new Vector3(0, T + 0.35f, 0), 6.6f, 0.62f, c.Mat("Plate", new Color(0.95f, 0.94f, 0.9f), 0.85f));
                var senbei = c.Mat("Senbei", new Color(0.78f, 0.52f, 0.25f), 0.35f);
                var nori = c.Mat("Nori", new Color(0.08f, 0.12f, 0.08f), 0.4f);
                var pieces = new[] { (new Vector3(-1.8f, 1.0f, -1.2f), 8f), (new Vector3(2.0f, 1.0f, 0.4f), -6f), (new Vector3(-0.4f, 1.0f, 2.4f), 4f), (new Vector3(0.2f, 1.6f, -0.2f), -10f) };
                for (int i = 0; i < pieces.Length; i++)
                {
                    var (p, tilt) = pieces[i];
                    var s = c.Prim(PrimitiveType.Cylinder, g, $"Senbei_{i}", p + new Vector3(0, T, 0), new Vector3(5.2f, 0.25f, 5.2f), Quaternion.Euler(tilt, i * 40, 0), senbei);
                    if (i % 2 == 1) c.Prim(PrimitiveType.Cube, s.transform, "Nori", new Vector3(0, 1.05f, 0), new Vector3(0.55f, 0.1f, 1.02f), Quaternion.identity, nori, true);
                }
            }

            // 新聞(南西。平たいので手前でも邪魔にならない)
            {
                var g = c.Anchored("Newspaper", new Vector2(-0.9f, -1), 6);
                var paper = c.Mat("Newspaper", new Color(0.86f, 0.85f, 0.8f), 0.1f);
                var ink = c.Mat("Ink", new Color(0.25f, 0.25f, 0.25f), 0.1f);
                var sheet = c.Box(g, "Sheet", new Vector3(0, T + 0.1f, 0), new Vector3(22, 0.2f, 15), 15, paper);
                c.Prim(PrimitiveType.Cube, sheet.transform, "Headline", new Vector3(-0.25f, 0.55f, 0.36f), new Vector3(0.42f, 0.1f, 0.12f), Quaternion.identity, ink, true);
                c.Prim(PrimitiveType.Cube, sheet.transform, "Photo", new Vector3(0.25f, 0.55f, 0.22f), new Vector3(0.36f, 0.1f, 0.4f), Quaternion.identity, ink, true);
                for (int i = 0; i < 6; i++)
                {
                    float x = -0.42f + i * 0.085f;
                    c.Prim(PrimitiveType.Cube, sheet.transform, $"Column_{i}", new Vector3(x, 0.55f, -0.18f), new Vector3(0.06f, 0.1f, 0.5f), Quaternion.identity, ink, true);
                }
            }

            // テレビのリモコン(南東)
            {
                var g = c.Anchored("Remote", new Vector2(0.9f, -1), 5);
                var body = c.Box(g, "Body", new Vector3(0, T + 0.6f, 0), new Vector3(3.6f, 1.2f, 11), -20, c.Mat("Remote", new Color(0.12f, 0.12f, 0.13f), 0.6f));
                var red = c.Mat("ButtonRed", new Color(0.85f, 0.15f, 0.12f), 0.5f);
                var gray = c.Mat("ButtonGray", new Color(0.55f, 0.56f, 0.6f), 0.5f);
                c.Prim(PrimitiveType.Cylinder, body.transform, "Power", new Vector3(0.25f, 0.55f, 0.4f), new Vector3(0.22f, 0.1f, 0.07f), Quaternion.identity, red, true);
                for (int row = 0; row < 4; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        c.Prim(PrimitiveType.Cylinder, body.transform, $"Button_{row}_{col}", new Vector3(-0.28f + col * 0.28f, 0.55f, 0.18f - row * 0.11f), new Vector3(0.18f, 0.1f, 0.055f), Quaternion.identity, gray, true);
                    }
                }
            }

            // お箸と箸置き(南)
            {
                var g = c.Anchored("Hashi", new Vector2(0.3f, -1), 4);
                var lacquer = c.Mat("Hashi", new Color(0.55f, 0.1f, 0.08f), 0.8f);
                c.Prim(PrimitiveType.Capsule, g, "HashiokI", new Vector3(-5.5f, T + 0.5f, 0), new Vector3(1, 1.4f, 1), Quaternion.Euler(90, 0, 0), c.Mat("Hashioki", new Color(0.92f, 0.9f, 0.85f), 0.7f));
                c.Prim(PrimitiveType.Cylinder, g, "Hashi_0", new Vector3(0, T + 1.1f, -0.5f), new Vector3(0.45f, 10, 0.45f), Quaternion.Euler(0, 2, 88), lacquer);
                c.Prim(PrimitiveType.Cylinder, g, "Hashi_1", new Vector3(0, T + 1.1f, 0.5f), new Vector3(0.45f, 10, 0.45f), Quaternion.Euler(0, -2, 88), lacquer);
            }
        }

        //========================================
        // 組み立て
        //========================================

        class Ctx
        {
            public string Id;
            public StageBackdrop Backdrop;
            public Transform Root;
            public Material FloorMaterial, WallMaterial;

            public Transform Group(string name)
            {
                var t = new GameObject(name).transform;
                t.SetParent(Root, false);
                return t;
            }

            // ステージの広さに合わせて外側へ動く小物のまとまり
            public Transform Anchored(string name, Vector2 direction, float margin)
            {
                var t = Group(name);
                t.gameObject.AddComponent<BackdropAnchor>().Set(direction, margin);
                return t;
            }

            public Material Mat(string name, Color color, float smoothness, float metallic = 0.0f)
            {
                return GetMaterial(Id, name, Shader.Find("Universal Render Pipeline/Lit"), color, smoothness, metallic);
            }

            // 壁用(裏が透けて見える壁のシェーダー)
            public Material SeeThroughMat(string name, Color color, float smoothness)
            {
                return GetMaterial(Id, name, Shader.Find("Nakagawa/SeeThroughWall"), color, smoothness, 0.0f);
            }

            public GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, float yaw, Material mat)
            {
                return Prim(PrimitiveType.Cube, parent, name, pos, size, Quaternion.Euler(0, yaw, 0), mat);
            }

            // 半径・高さで指定する円柱(位置は中心)
            public GameObject Cylinder(Transform parent, string name, Vector3 pos, float radius, float height, Material mat)
            {
                return Prim(PrimitiveType.Cylinder, parent, name, pos, new Vector3(radius * 2, height * 0.5f, radius * 2), Quaternion.identity, mat);
            }

            public GameObject Sphere(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
            {
                return Prim(PrimitiveType.Sphere, parent, name, pos, size, Quaternion.identity, mat);
            }

            // 当たり判定のない形。local なら親からの相対(大きさも親に対する割合)
            public GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material mat, bool local = false)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                if (local)
                {
                    go.transform.localPosition = pos;
                    go.transform.localRotation = rot;
                }
                else
                {
                    // 位置はルートからの相対(グループは原点に置くので、ルート基準の座標で書ける)
                    go.transform.position = Root.TransformPoint(pos);
                    go.transform.rotation = rot;
                }
                go.transform.localScale = scale;
                go.GetComponent<Renderer>().sharedMaterial = mat;
                go.isStatic = true;
                return go;
            }
        }

        static StageBackdrop Build(BackdropDef def)
        {
            EnsureFolder(kBackdropDir);
            EnsureFolder(kMaterialDir);

            // 開いているシーンを汚さないよう、プレビュー用のシーンで組み立てる
            Scene temp = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject($"Backdrop_{def.Id}");
                SceneManager.MoveGameObjectToScene(root, temp);
                var backdrop = root.AddComponent<StageBackdrop>();
                var c = new Ctx { Id = def.Id, Backdrop = backdrop, Root = root.transform };
                def.Build(c);
                backdrop.Set(def.Id, def.Title, def.Description, backdrop.Supports, c.FloorMaterial, c.WallMaterial);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(def.Id));
                return prefab.GetComponent<StageBackdrop>();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(temp);
            }
        }

        // 背景ごとのマテリアル(Backdrops/Materials/<背景>_<名前>.mat)。あれば色だけ合わせる
        static Material GetMaterial(string backdropId, string name, Shader shader, Color color, float smoothness, float metallic)
        {
            string path = $"{kMaterialDir}/{backdropId}_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = mat == null;
            if (created) mat = new Material(shader);
            else if (mat.shader != shader) mat.shader = shader;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (created) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
