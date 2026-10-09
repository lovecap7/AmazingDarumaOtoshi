using Nakahira;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Nakagawa.EditorTools
{
    // ギミックのプレハブ・マテリアルと、テストフィールド(GimmickTest.unity)を組み立て直す
    // 保存先はすべて Assets/Nakagawa の中。Nakahira のプレハブ・マテリアルは参照するだけ
    public static class GimmickTestFieldBuilder
    {
        const string kRoot = "Assets/Nakagawa";
        const string kMaterialDir = kRoot + "/Materials";
        const string kPrefabDir = kRoot + "/Prefabs";
        const string kScenePath = kRoot + "/GimmickTest.unity";
        const string kPatternDir = kRoot + "/SpawnPatterns";
        const string kRandomPatternPath = kPatternDir + "/SpawnPattern_Random.asset";
        const string kEqualPatternPath = kPatternDir + "/SpawnPattern_Equal.asset";
        const string kSpawnerName = "TumikiSpawner";

        // 参照するだけの Nakahira のアセット
        const string kNakahira = "Assets/Nakahira/Player";
        const string kCharacterPrefab = kNakahira + "/Prefabs/DarumaCharacter.prefab";
        const string kGhostPrefab = kNakahira + "/Prefabs/DarumaGhost.prefab";
        const string kPlayerPrefab = kNakahira + "/Prefabs/DarumaPlayer.prefab";
        const string kTumikiPrefab = kNakahira + "/Prefabs/Tumiki.prefab";
        const string kOpaqueTemplate = kNakahira + "/Materials/Stage.mat";
        const string kTransparentTemplate = kNakahira + "/Materials/Ghost.mat";
        const string kParticleTemplate = kNakahira + "/Effects/SmokeParticle.mat";
        const string kStageMaterial = kNakahira + "/Materials/Stage.mat";
        const string kWallMaterial = kNakahira + "/Materials/Wall.mat";

        [MenuItem("Nakagawa/Gimmick/Build Prefabs And Test Field")]
        public static void BuildAll()
        {
            BuildPrefabs();
            BuildTestField();
        }

        [MenuItem("Nakagawa/Gimmick/Build Prefabs")]
        public static void BuildPrefabs()
        {
            EnsureFolder(kMaterialDir);
            EnsureFolder(kPrefabDir);

            var fanBody = OpaqueMaterial("FanBody", new Color(0.35f, 0.55f, 0.8f));
            var fanBlade = OpaqueMaterial("FanBlade", new Color(0.9f, 0.95f, 1.0f));
            var bumper = OpaqueMaterial("Bumper", new Color(1.0f, 0.3f, 0.45f));
            var bumperRing = OpaqueMaterial("BumperRing", new Color(1.0f, 0.85f, 0.3f));
            var turntable = OpaqueMaterial("Turntable", new Color(0.95f, 0.6f, 0.25f));
            var turntableLine = OpaqueMaterial("TurntableLine", new Color(1.0f, 0.95f, 0.85f));
            var lowGravity = TransparentMaterial("LowGravity", new Color(0.6f, 0.2f, 1.0f, 0.22f));
            var particle = ParticleMaterial("GimmickParticle");

            BuildFanPrefab(fanBody, fanBlade, particle);
            BuildBumperPrefab(bumper, bumperRing);
            BuildTurntablePrefab(turntable, turntableLine);
            BuildLowGravityPrefab(lowGravity, particle);
            BuildSpawnAssets();

            AssetDatabase.SaveAssets();
            Debug.Log("[GimmickTestFieldBuilder] プレハブとマテリアルを作成しました");
        }

        //========================================
        // プレハブ
        //========================================

        static void BuildFanPrefab(Material body, Material blade, Material particle)
        {
            var root = new GameObject("Fan");
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0.0f, 0.85f, 0.0f);
            box.size = new Vector3(1.3f, 1.7f, 0.6f);

            Visual(PrimitiveType.Cylinder, "Base", root.transform, new Vector3(0.0f, 0.08f, 0.0f), Vector3.zero, new Vector3(0.9f, 0.08f, 0.9f), body);
            Visual(PrimitiveType.Cylinder, "Pole", root.transform, new Vector3(0.0f, 0.6f, 0.0f), Vector3.zero, new Vector3(0.15f, 0.5f, 0.15f), body);
            Visual(PrimitiveType.Cylinder, "Head", root.transform, new Vector3(0.0f, 1.15f, -0.05f), new Vector3(90.0f, 0.0f, 0.0f), new Vector3(1.3f, 0.12f, 1.3f), body);

            var blades = new GameObject("Blades").transform;
            blades.SetParent(root.transform, false);
            blades.localPosition = new Vector3(0.0f, 1.15f, 0.1f);
            for (int i = 0; i < 3; i++)
            {
                Quaternion r = Quaternion.Euler(0.0f, 0.0f, 120.0f * i);
                Visual(PrimitiveType.Cube, $"Blade{i}", blades, r * new Vector3(0.0f, 0.3f, 0.0f), r.eulerAngles, new Vector3(0.2f, 0.55f, 0.03f), blade);
            }
            Visual(PrimitiveType.Sphere, "Hub", blades, Vector3.zero, Vector3.zero, Vector3.one * 0.2f, body);

            var wind = new GameObject("WindParticles").AddComponent<ParticleSystem>();
            wind.transform.SetParent(root.transform, false);
            var main = wind.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = new Color(0.85f, 0.95f, 1.0f, 0.6f);
            main.maxParticles = 400;
            var emission = wind.emission;
            emission.rateOverTime = 40.0f;
            var col = wind.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
                new[] { new GradientAlphaKey(0.0f, 0.0f), new GradientAlphaKey(1.0f, 0.2f), new GradientAlphaKey(0.0f, 1.0f) });
            col.color = gradient;
            var pr = wind.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Stretch;
            pr.lengthScale = 4.0f;
            pr.sharedMaterial = particle;

            var fan = root.AddComponent<FanGimmick>();
            var so = new SerializedObject(fan);
            so.FindProperty("m_blades").objectReferenceValue = blades;
            so.FindProperty("m_windParticles").objectReferenceValue = wind;
            so.ApplyModifiedPropertiesWithoutUndo();
            fan.ApplyParticleSettings();

            SavePrefab(root, "Fan");
        }

        static void BuildBumperPrefab(Material bumper, Material ring)
        {
            var root = new GameObject("Bumper");
            root.AddComponent<Rigidbody>().isKinematic = true;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var disc = Visual(PrimitiveType.Cylinder, "Disc", visual, new Vector3(0.0f, 0.3f, 0.0f), Vector3.zero, new Vector3(1.6f, 0.3f, 1.6f), bumper);
            var mc = disc.AddComponent<MeshCollider>();
            mc.sharedMesh = disc.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = true;
            Visual(PrimitiveType.Cylinder, "Ring", visual, new Vector3(0.0f, 0.06f, 0.0f), Vector3.zero, new Vector3(1.8f, 0.06f, 1.8f), ring);
            Visual(PrimitiveType.Cylinder, "Top", visual, new Vector3(0.0f, 0.61f, 0.0f), Vector3.zero, new Vector3(0.9f, 0.02f, 0.9f), ring);

            var gimmick = root.AddComponent<BumperGimmick>();
            var so = new SerializedObject(gimmick);
            so.FindProperty("m_visual").objectReferenceValue = visual;
            so.FindProperty("m_renderer").objectReferenceValue = disc.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, "Bumper");
        }

        static void BuildTurntablePrefab(Material table, Material line)
        {
            var root = new GameObject("Turntable");
            root.AddComponent<Rigidbody>().isKinematic = true;

            // 見た目だけ(乗るのは床。表面が床より少し高いので、当たり判定があると縁が段差になって積み木が縁に沿って滑る)
            var disc = Visual(PrimitiveType.Cylinder, "Disc", root.transform, Vector3.zero, Vector3.zero, Vector3.one, table);

            var line1 = Visual(PrimitiveType.Cube, "Line1", root.transform, Vector3.zero, Vector3.zero, new Vector3(1.0f, 0.02f, 0.18f), line);
            var line2 = Visual(PrimitiveType.Cube, "Line2", root.transform, Vector3.zero, new Vector3(0.0f, 90.0f, 0.0f), new Vector3(1.0f, 0.02f, 0.18f), line);

            var gimmick = root.AddComponent<TurntableGimmick>();
            var so = new SerializedObject(gimmick);
            so.FindProperty("m_disc").objectReferenceValue = disc.transform;
            var lines = so.FindProperty("m_lines");
            lines.arraySize = 2;
            lines.GetArrayElementAtIndex(0).objectReferenceValue = line1.transform;
            lines.GetArrayElementAtIndex(1).objectReferenceValue = line2.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            gimmick.ApplyShape();

            SavePrefab(root, "Turntable");
        }

        static void BuildLowGravityPrefab(Material volumeMat, Material particle)
        {
            var root = new GameObject("LowGravityArea");
            var volume = Visual(PrimitiveType.Cube, "Volume", root.transform, Vector3.zero, Vector3.zero, Vector3.one, volumeMat);
            var r = volume.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var area = root.AddComponent<LowGravityArea>();
            var so = new SerializedObject(area);
            so.FindProperty("m_volume").objectReferenceValue = volume.transform;
            so.FindProperty("m_effectMaterial").objectReferenceValue = particle;
            so.ApplyModifiedPropertiesWithoutUndo();
            area.ApplyShape();

            SavePrefab(root, "LowGravityArea");
        }

        // 積み木の生成に使うもの(エリアのプレハブ・マテリアル・生成パターン)だけを作る。ギミックのプレハブには触れない
        [MenuItem("Nakagawa/Gimmick/Build Tumiki Spawn Assets")]
        public static void BuildSpawnAssets()
        {
            EnsureFolder(kMaterialDir);
            EnsureFolder(kPrefabDir);
            BuildSpawnAreaPrefab(TransparentMaterial("SpawnArea", new Color(0.3f, 1.0f, 0.55f, 0.25f)));
            EnsureSpawnPatterns();
            AssetDatabase.SaveAssets();
        }

        static void BuildSpawnAreaPrefab(Material markerMat)
        {
            var root = new GameObject("TumikiSpawnArea");
            // 床の目印(床とちらつかないよう少し浮かせる。当たり判定なし)
            var rect = Visual(PrimitiveType.Cube, "RectMarker", root.transform, new Vector3(0.0f, 0.012f, 0.0f), Vector3.zero, new Vector3(1.0f, 0.01f, 1.0f), markerMat);
            var circle = Visual(PrimitiveType.Cylinder, "CircleMarker", root.transform, new Vector3(0.0f, 0.012f, 0.0f), Vector3.zero, new Vector3(1.0f, 0.005f, 1.0f), markerMat);
            foreach (var r in new[] { rect.GetComponent<MeshRenderer>(), circle.GetComponent<MeshRenderer>() })
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            var area = root.AddComponent<TumikiSpawnArea>();
            var so = new SerializedObject(area);
            so.FindProperty("m_rectMarker").objectReferenceValue = rect.transform;
            so.FindProperty("m_circleMarker").objectReferenceValue = circle.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            area.ApplyShape();

            SavePrefab(root, "TumikiSpawnArea");
        }

        // 生成パターンのアセット(既にあれば調整済みの値を残すため作り直さない)
        static void EnsureSpawnPatterns()
        {
            EnsureFolder(kPatternDir);
            if (Load<RandomColorSpawnPattern>(kRandomPatternPath) == null)
            {
                var p = ScriptableObject.CreateInstance<RandomColorSpawnPattern>();
                AssetDatabase.CreateAsset(p, kRandomPatternPath);
            }
            if (Load<EqualColorSpawnPattern>(kEqualPatternPath) == null)
            {
                var p = ScriptableObject.CreateInstance<EqualColorSpawnPattern>();
                p.MinCount = 8;
                p.MaxCount = 12;
                AssetDatabase.CreateAsset(p, kEqualPatternPath);
            }
        }

        // 開いているシーンに積み木の生成(司令塔 + 矩形・円形のエリア)を置く。既にあれば置き直す
        // シーンの保存はしない(手で編集中の内容を勝手に保存しないため)
        [MenuItem("Nakagawa/Gimmick/Add Tumiki Spawner To Open Scene")]
        public static void AddSpawnerToOpenScene()
        {
            var areaPrefab = LoadPrefab("TumikiSpawnArea");
            if (areaPrefab == null || Load<RandomColorSpawnPattern>(kRandomPatternPath) == null
                || Load<EqualColorSpawnPattern>(kEqualPatternPath) == null)
            {
                BuildSpawnAssets();
                areaPrefab = LoadPrefab("TumikiSpawnArea");
            }

            Scene scene = SceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == kSpawnerName) Undo.DestroyObjectImmediate(go);
            }

            var root = new GameObject(kSpawnerName);
            Undo.RegisterCreatedObjectUndo(root, "Add Tumiki Spawner");

            var areas = new[]
            {
                PlaceArea(areaPrefab, root.transform, "SpawnArea_NorthEast_Rect", new Vector3(10.0f, 0.0f, 10.0f), TumikiSpawnArea.Shape.Rectangle, new Vector2(8.0f, 8.0f), 0.0f),
                PlaceArea(areaPrefab, root.transform, "SpawnArea_NorthWest_Rect", new Vector3(-10.0f, 0.0f, 10.0f), TumikiSpawnArea.Shape.Rectangle, new Vector2(8.0f, 6.0f), 0.0f),
                PlaceArea(areaPrefab, root.transform, "SpawnArea_SouthEast_Circle", new Vector3(9.0f, 0.0f, -9.0f), TumikiSpawnArea.Shape.Circle, Vector2.zero, 4.0f),
                PlaceArea(areaPrefab, root.transform, "SpawnArea_SouthWest_Circle", new Vector3(-7.0f, 0.0f, -7.5f), TumikiSpawnArea.Shape.Circle, Vector2.zero, 3.5f),
            };

            var spawner = root.AddComponent<TumikiSpawner>();
            var so = new SerializedObject(spawner);
            so.FindProperty("m_tumikiPrefab").objectReferenceValue = Load<GameObject>(kTumikiPrefab).GetComponent<TumikiBlock>();
            var patterns = so.FindProperty("m_patterns");
            patterns.arraySize = 2;
            patterns.GetArrayElementAtIndex(0).objectReferenceValue = Load<TumikiSpawnPattern>(kRandomPatternPath);
            patterns.GetArrayElementAtIndex(1).objectReferenceValue = Load<TumikiSpawnPattern>(kEqualPatternPath);
            // 生成エリアは TumikiSpawner がゲーム開始時に盤面から探す
            so.FindProperty("m_match").objectReferenceValue = Object.FindAnyObjectByType<DarumaMatch>();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[GimmickTestFieldBuilder] 積み木の生成エリアを配置しました(シーンは未保存です)");
        }

        static TumikiSpawnArea PlaceArea(GameObject prefab, Transform parent, string name, Vector3 position,
            TumikiSpawnArea.Shape shape, Vector2 size, float radius)
        {
            var area = Place<TumikiSpawnArea>(prefab, parent, position);
            area.name = name;
            var so = new SerializedObject(area);
            so.FindProperty("m_shape").enumValueIndex = (int)shape;
            if (shape == TumikiSpawnArea.Shape.Rectangle) so.FindProperty("m_size").vector2Value = size;
            else so.FindProperty("m_radius").floatValue = radius;
            so.ApplyModifiedPropertiesWithoutUndo();
            area.ApplyShape();
            return area;
        }

        //========================================
        // テストフィールド
        //========================================

        [MenuItem("Nakagawa/Gimmick/Build Test Field")]
        public static void BuildTestField()
        {
            var fanPrefab = LoadPrefab("Fan");
            var bumperPrefab = LoadPrefab("Bumper");
            var turntablePrefab = LoadPrefab("Turntable");
            var lowGravityPrefab = LoadPrefab("LowGravityArea");
            if (fanPrefab == null || bumperPrefab == null || turntablePrefab == null || lowGravityPrefab == null)
            {
                BuildPrefabs();
                BuildTestField();
                return;
            }

            Scene scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(kScenePath) != null
                ? EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects()) Object.DestroyImmediate(go);

            // 照明・カメラ
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.gameObject.AddComponent<AudioListener>();
            camera.fieldOfView = 50.0f;
            camera.transform.SetPositionAndRotation(new Vector3(0.0f, 30.0f, -26.0f), Quaternion.Euler(52.0f, 0.0f, 0.0f));

            // ステージ(32m四方。壁は積み木が反射する)
            var stage = new GameObject("Stage").transform;
            var floor = Solid(PrimitiveType.Cube, "Floor", stage, new Vector3(0.0f, -0.5f, 0.0f), new Vector3(34.0f, 1.0f, 34.0f), Load<Material>(kStageMaterial));
            floor.isStatic = true;
            var wallMat = Load<Material>(kWallMaterial);
            Wall("Wall_North", stage, new Vector3(0.0f, 0.75f, 16.5f), new Vector3(34.0f, 1.5f, 1.0f), wallMat);
            Wall("Wall_South", stage, new Vector3(0.0f, 0.75f, -16.5f), new Vector3(34.0f, 1.5f, 1.0f), wallMat);
            Wall("Wall_East", stage, new Vector3(16.5f, 0.75f, 0.0f), new Vector3(1.0f, 1.5f, 32.0f), wallMat);
            Wall("Wall_West", stage, new Vector3(-16.5f, 0.75f, 0.0f), new Vector3(1.0f, 1.5f, 32.0f), wallMat);

            var stageArea = new GameObject("StageArea").AddComponent<StageArea>();
            SetValue(stageArea, "m_size", new Vector2(32.0f, 32.0f));

            // 出現ポイント(中央付近。ギミックは四隅に置く)
            var spawnRoot = new GameObject("SpawnPoints").transform;
            var spawns = new Transform[4];
            Vector3[] spawnPos = { new Vector3(-3, 0, 3), new Vector3(3, 0, 3), new Vector3(-3, 0, -3), new Vector3(3, 0, -3) };
            for (int i = 0; i < spawns.Length; i++)
            {
                spawns[i] = new GameObject($"SpawnPoint{i + 1}").transform;
                spawns[i].SetParent(spawnRoot, false);
                spawns[i].localPosition = spawnPos[i];
            }

            BuildMatch(spawns, stageArea);

            // 北西: 扇風機(南北に往復しながら東へ風を吹かせる)
            var fanGroup = new GameObject("Fan_Group").transform;
            var pointA = Point("PointA", fanGroup, new Vector3(-14.0f, 0.0f, 13.0f));
            var pointB = Point("PointB", fanGroup, new Vector3(-14.0f, 0.0f, 3.0f));
            var fan = Place<FanGimmick>(fanPrefab, fanGroup, pointA.position);
            var fanSo = new SerializedObject(fan);
            fanSo.FindProperty("m_pointA").objectReferenceValue = pointA;
            fanSo.FindProperty("m_pointB").objectReferenceValue = pointB;
            fanSo.FindProperty("m_windDirection").vector3Value = Vector3.right;
            fanSo.FindProperty("m_windLength").floatValue = 12.0f;
            fanSo.ApplyModifiedPropertiesWithoutUndo();
            fan.transform.rotation = Quaternion.LookRotation(Vector3.right);
            fan.ApplyParticleSettings();

            // 北東: バンパー
            var bumpers = new GameObject("Bumpers").transform;
            Place<BumperGimmick>(bumperPrefab, bumpers, new Vector3(6.0f, 0.0f, 6.5f));
            Place<BumperGimmick>(bumperPrefab, bumpers, new Vector3(11.0f, 0.0f, 6.5f));
            Place<BumperGimmick>(bumperPrefab, bumpers, new Vector3(8.5f, 0.0f, 10.5f));
            Place<BumperGimmick>(bumperPrefab, bumpers, new Vector3(13.0f, 0.0f, 12.5f));

            // 南東: 回転台(時計回り・反時計回り)
            var turntables = new GameObject("Turntables").transform;
            SetupTurntable(Place<TurntableGimmick>(turntablePrefab, turntables, new Vector3(6.0f, 0.0f, -9.0f)),
                "Turntable_Clockwise", TurntableGimmick.Direction.Clockwise, 3.0f, 45.0f);
            SetupTurntable(Place<TurntableGimmick>(turntablePrefab, turntables, new Vector3(12.5f, 0.0f, -3.5f)),
                "Turntable_CounterClockwise", TurntableGimmick.Direction.CounterClockwise, 2.5f, 60.0f);

            // 南西: 低重力エリア
            var area = Place<LowGravityArea>(lowGravityPrefab, null, new Vector3(-9.0f, 0.0f, -9.0f));
            SetValue(area, "m_size", new Vector3(10.0f, 6.0f, 10.0f));
            area.ApplyShape();

            BuildLooseTumikis();
            AddSpawnerToOpenScene();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, kScenePath);
            Debug.Log($"[GimmickTestFieldBuilder] テストフィールドを作成しました: {kScenePath}");
        }

        static void BuildMatch(Transform[] spawns, StageArea stageArea)
        {
            var go = new GameObject("Match");
            go.SetActive(false); // 設定が揃う前にPlayerInputManagerが動かないようにする

            var pim = go.AddComponent<PlayerInputManager>();
            var pimSo = new SerializedObject(pim);
            pimSo.FindProperty("m_NotificationBehavior").intValue = 3; // C#イベント
            pimSo.FindProperty("m_JoinBehavior").intValue = 0;         // ボタンを押したら参加
            pimSo.FindProperty("m_MaxPlayerCount").intValue = -1;
            pimSo.FindProperty("m_PlayerPrefab").objectReferenceValue = Load<GameObject>(kPlayerPrefab);
            pimSo.ApplyModifiedPropertiesWithoutUndo();

            var match = go.AddComponent<DarumaMatch>();
            var so = new SerializedObject(match);
            var points = so.FindProperty("m_spawnPoints");
            points.arraySize = spawns.Length;
            for (int i = 0; i < spawns.Length; i++) points.GetArrayElementAtIndex(i).objectReferenceValue = spawns[i];
            so.FindProperty("m_stageArea").objectReferenceValue = stageArea;
            so.FindProperty("m_characterPrefab").objectReferenceValue = Load<GameObject>(kCharacterPrefab).GetComponent<DarumaCharacter>();
            so.FindProperty("m_ghostPrefab").objectReferenceValue = Load<GameObject>(kGhostPrefab).GetComponent<DarumaGhost>();
            so.FindProperty("m_tumikiPrefab").objectReferenceValue = Load<GameObject>(kTumikiPrefab).GetComponent<TumikiBlock>();
            // ゲームパッドなしでもギミックを確かめられるよう、操作なしのダミーを1体置く
            so.FindProperty("m_dummyCount").intValue = 1;
            so.FindProperty("m_dummyStartBlocks").intValue = 3;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(true);
        }

        static void BuildLooseTumikis()
        {
            var prefab = Load<GameObject>(kTumikiPrefab);
            var root = new GameObject("LooseTumikis").transform;
            Vector3[] positions =
            {
                // 中央
                new Vector3(0, 0, 0), new Vector3(-1.5f, 0, -1), new Vector3(1.5f, 0, 1),
                // 扇風機の風の通り道
                new Vector3(-10, 0, 10), new Vector3(-7, 0, 7), new Vector3(-4, 0, 11),
                // バンパーの間
                new Vector3(8.5f, 0, 8), new Vector3(10, 0, 12), new Vector3(4, 0, 9),
                // 回転台の上
                new Vector3(6, 0, -7), new Vector3(8, 0, -9.5f), new Vector3(4.5f, 0, -10.5f), new Vector3(12.5f, 0, -2),
                // 低重力エリア
                new Vector3(-9, 0, -9), new Vector3(-6.5f, 0, -11), new Vector3(-11.5f, 0, -6.5f),
            };
            for (int i = 0; i < positions.Length; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
                go.transform.SetParent(root, false);
                go.transform.position = positions[i] + Vector3.up * (TumikiBlock.kHeight * 0.5f + 0.05f);
                go.name = $"Tumiki_{i:00}";
                var so = new SerializedObject(go.GetComponent<TumikiBlock>());
                so.FindProperty("m_color").intValue = i % 4;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void SetupTurntable(TurntableGimmick table, string name, TurntableGimmick.Direction direction, float radius, float speed)
        {
            table.name = name;
            var so = new SerializedObject(table);
            so.FindProperty("m_direction").enumValueIndex = (int)direction;
            so.FindProperty("m_radius").floatValue = radius;
            so.FindProperty("m_speed").floatValue = speed;
            so.ApplyModifiedPropertiesWithoutUndo();
            table.ApplyShape();
        }

        //========================================
        // 補助
        //========================================

        static T Place<T>(GameObject prefab, Transform parent, Vector3 position) where T : Component
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = position;
            return go.GetComponent<T>();
        }

        static Transform Point(string name, Transform parent, Vector3 position)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = position;
            return t;
        }

        static void Wall(string name, Transform parent, Vector3 position, Vector3 scale, Material mat)
        {
            var go = Solid(PrimitiveType.Cube, name, parent, position, scale, mat);
            go.AddComponent<ReflectWall>();
            go.isStatic = true;
        }

        static GameObject Solid(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        // 当たり判定のない見た目だけの部品
        static GameObject Visual(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static void SetValue(Object target, string property, Vector2 value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).vector2Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetValue(Object target, string property, Vector3 value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SavePrefab(GameObject root, string name)
        {
            PrefabUtility.SaveAsPrefabAsset(root, $"{kPrefabDir}/{name}.prefab");
            Object.DestroyImmediate(root);
        }

        static GameObject LoadPrefab(string name) => Load<GameObject>($"{kPrefabDir}/{name}.prefab");

        static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        // Nakahira のマテリアルを元に色だけ変えたものを Nakagawa に作る(既にあれば上書き)
        static Material OpaqueMaterial(string name, Color color) => DerivedMaterial(name, kOpaqueTemplate, color);
        static Material TransparentMaterial(string name, Color color) => DerivedMaterial(name, kTransparentTemplate, color);
        static Material ParticleMaterial(string name) => DerivedMaterial(name, kParticleTemplate, Color.white);

        static Material DerivedMaterial(string name, string templatePath, Color color)
        {
            string path = $"{kMaterialDir}/{name}.mat";
            var template = Load<Material>(templatePath);
            var mat = Load<Material>(path);
            if (mat == null)
            {
                mat = new Material(template);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = template.shader;
                mat.CopyPropertiesFromMaterial(template);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            EditorUtility.SetDirty(mat);
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
