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
            };
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
