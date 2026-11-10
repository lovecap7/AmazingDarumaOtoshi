using System.Collections.Generic;
using Nakahira;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Nakagawa.EditorTools
{
    // Stage20「ルーレット盤」を、開いている AllStages(Assets/Nakagawa/Stages/AllStages.unity)の中に組み立てる
    //  ・同じ id のステージ(Stage20)があれば作り直す。他のステージ・共有の物はそのまま
    //  ・積み木の生成(TumikiSpawner)・ステージ範囲の見本は Stage18 の物を写して使う
    //  ・マテリアル・メッシュは Assets/Nakagawa/Roulette に作る(作り直すと値を上書き)
    // シーンは保存しない(保存は呼び出し側 / 手で行う)
    public static class RouletteStageBuilder
    {
        public const string kStageId = "Stage20";
        const string kTemplateId = "Stage18";
        const string kCombinedPath = "Assets/Nakagawa/Stages/AllStages.unity";
        const string kAssetDir = "Assets/Nakagawa/Roulette";
        const string kSpawnAreaPrefab = "Assets/Nakagawa/Prefabs/TumikiSpawnArea.prefab";
        const string kTumikiPrefab = "Assets/Nakahira/Player/Prefabs/Tumiki.prefab";
        const string kWallTemplateMaterial = "Assets/Nakagawa/Backdrops/Materials/Monja_StageWall.mat";
        const string kAbyssMaterial = "Assets/Nakagawa/Materials/Abyss.mat";

        // 盤の形(RouletteWheel の既定値と合わせる)
        const int kPockets = 17;
        const float kSpindle = 1.6f, kHub = 2.6f, kBet = 9.2f, kPocketIn = 9.4f, kPocketOut = 12.6f;
        // ポケットの中の区切り: 内側(球が入るくぼみ) kPocketIn〜kNumberIn / 番号の帯 kNumberIn〜kNumberOut / 外の金の輪 kNumberOut〜kPocketOut
        const float kNumberIn = 10.9f, kNumberOut = 12.15f;
        // 盤の外の賭けの表の位置と、ディーラーが賭ける下の段のマス(表のローカル。緑・赤・黒が並ぶ)
        static readonly Vector3 kLayoutPos = new Vector3(30.0f, kTableY + 0.01f, 3.0f);
        const float kBetRowZ = -18.4f, kBetBoxStep = 4.6f;
        static readonly Vector2 kBetBoxSize = new Vector2(4.4f, 4.0f);
        const float kThickness = 0.6f;
        const float kTrackIn = 12.62f, kTrackOut = 15.2f;
        const float kRimIn = 15.2f, kRimOut = 16.2f;
        const float kWallHeight = 5.0f;
        const float kBallRadius = 0.6f;
        const float kHallFloorY = -14.0f;
        const float kTableY = -0.45f;

        // ポケットの番号(並び順。0番目は緑の0、奇数番目が赤、偶数番目が黒)
        static readonly int[] kNumbers = { 0, 12, 15, 1, 4, 14, 2, 3, 13, 16, 6, 5, 11, 7, 8, 9, 10 };

        public const string kTitle = "ルーレット盤";
        const string kConcept =
            "カジノのルーレット盤の上。木の盤面の中央に金の取っ手、外周に番号付きの17のポケット(0と1〜16)、その外に球が回る木のレールと縁(壁)。" +
            "ときどきディーラーの大きな手が現れて軸をひねって盤を回し、球をレールへはじき入れる。";
        const string kFlow =
            "回転演出の間は盤が回り、乗っている全員が流される。レールを高速で回る重い球に当たると大きくはじかれる。" +
            "回し始めに盤の外でディーラーが赤・黒・緑の0のどれかに賭け(画面右上の窓に映る)、的中すると配当として盤全体に積み木が降る。" +
            "止まったポケットの色で空から物が降る(赤: 積み木の雨 / 黒: 重りの雨で周りが吹き飛ぶ / 0: ジャックポット)。" +
            "止まったポケットは奈落になり(既に奈落なら隣)、回すたびに穴が増えて外周が危険になる。回す間隔もだんだん短くなる。";

        [MenuItem("Nakagawa/Stages/Build Stage20 (Roulette) Into Open AllStages")]
        public static void BuildMenu()
        {
            Debug.Log(Build());
        }

        public static string Build()
        {
            Scene scene = SceneManager.GetSceneByPath(kCombinedPath);
            if (!scene.IsValid() || !scene.isLoaded) return $"[RouletteStageBuilder] {kCombinedPath} を開いてから実行してください";
            StageSwitcher switcher = null;
            StageEntry template = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (switcher == null) switcher = go.GetComponentInChildren<StageSwitcher>(true);
                if (go.name == kTemplateId) template = go.GetComponent<StageEntry>();
            }
            if (switcher == null || template == null) return $"[RouletteStageBuilder] StageSwitcher か {kTemplateId} が見つかりません";

            EnsureFolder(kAssetDir);
            var m = new Mats();
            foreach (var go in scene.GetRootGameObjects()) if (go.name == kStageId) Object.DestroyImmediate(go);

            var rootGo = new GameObject(kStageId);
            SceneManager.MoveGameObjectToScene(rootGo, scene);
            Transform root = rootGo.transform;
            var entry = rootGo.AddComponent<StageEntry>();

            // 動かない床(レール)・縁の壁・軸の当たり判定
            var stage = Child(root, "Stage");
            BuildStatic(stage, m);
            StageWallSplitter.Split(stage);

            var spawner = BuildSpawner(root, template);
            BuildGame(Child(root, "Gimmicks"), m, spawner);

            var areaGo = Object.Instantiate(template.StageArea.gameObject, root);
            areaGo.name = "StageArea_Template";
            areaGo.SetActive(false);
            areaGo.transform.localPosition = Vector3.zero;
            var areaSo = new SerializedObject(areaGo.GetComponent<StageArea>());
            areaSo.FindProperty("m_size").vector2Value = new Vector2(kRimOut * 2, kRimOut * 2);
            areaSo.ApplyModifiedPropertiesWithoutUndo();

            // 出現位置: 木の盤面の上の4か所
            var spawnRoot = Child(root, "SpawnPoints");
            float[] spawnAngles = { 22.5f, 157.5f, 202.5f, 337.5f };
            var spawns = new Transform[spawnAngles.Length];
            for (int i = 0; i < spawns.Length; i++)
            {
                spawns[i] = Child(spawnRoot, $"SpawnPoint{i + 1}");
                spawns[i].localPosition = RouletteMeshes.Point(6.3f, spawnAngles[i]);
            }

            var cameraPoint = Child(root, "CameraPoint");
            FrameCamera(cameraPoint, switcher);

            BuildHall(Child(root, "CasinoHall"), m);

            var info = Child(root, "StageInfo").gameObject.AddComponent<StageInfo>();
            info.Set(kStageId, kTitle, kConcept, kFlow,
                $"円形の盤 半径{kPocketOut}m(回る) + レール 半径{kTrackOut}m(動かない) / ポケット{kPockets}(緑1・赤8・黒8。止まると奈落、上限11) / " +
                "球1 / ディーラーの手 / ディーラーの賭け(盤の外。画面右上の窓に映す) / 出来事3(積み木の雨・重りの雨・ジャックポット) / 生成エリア7 / 出現位置4");
            entry.Set(info, spawns, areaGo.GetComponent<StageArea>(), cameraPoint);

            var sso = new SerializedObject(switcher);
            var prop = sso.FindProperty("m_stages");
            var stages = new List<StageEntry>();
            for (int i = 0; i < prop.arraySize; i++)
            {
                if (prop.GetArrayElementAtIndex(i).objectReferenceValue is StageEntry e && e != null && e != entry) stages.Add(e);
            }
            stages.Add(entry);
            StageCombiner.SortStages(stages);
            prop.arraySize = stages.Count;
            for (int i = 0; i < stages.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = stages[i];
            sso.ApplyModifiedPropertiesWithoutUndo();

            rootGo.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            return $"[RouletteStageBuilder] {kStageId} {kTitle} を {kCombinedPath} に作りました(全{stages.Count}ステージ。シーンは未保存)";
        }

        //========================================
        // 動かない部分
        //========================================

        static void BuildStatic(Transform stage, Mats m)
        {
            // レール(床)。内側(回る盤との境目)の上の縁には側面を付けない(上を滑る積み木が継ぎ目に引っかからないように)
            var trackMesh = SaveMesh(RouletteMeshes.Sector(kTrackIn, kTrackOut, 0, 360, 0.0f, -1.0f, false, true, false, false, 90), "RouletteTrack");
            var track = new GameObject("Track");
            track.transform.SetParent(stage, false);
            track.AddComponent<MeshFilter>().sharedMesh = trackMesh;
            track.AddComponent<MeshRenderer>().sharedMaterial = m.Track;
            track.AddComponent<MeshCollider>().sharedMesh = trackMesh;
            track.isStatic = true;
            // レールの内側の側面(上の縁から少し下げる)。ポケットが奈落になったとき、穴に落ちかけた積み木がここで跳ね返る
            var lipBuilder = new RouletteMeshes.Builder();
            for (int i = 0; i < 90; i++) lipBuilder.ArcSide(kTrackIn, i * 4.0f, (i + 1) * 4.0f, -0.12f, -1.0f, false);
            var lipMesh = SaveMesh(lipBuilder.ToMesh("RouletteTrackLip"), "RouletteTrackLip");
            var lip = new GameObject("TrackLip");
            lip.transform.SetParent(stage, false);
            lip.AddComponent<MeshFilter>().sharedMesh = lipMesh;
            lip.AddComponent<MeshRenderer>().sharedMaterial = m.WoodDark;
            lip.AddComponent<MeshCollider>().sharedMesh = lipMesh;
            lip.isStatic = true;

            // 縁の壁(円を箱で囲む)
            int n = 40;
            float rc = (kRimIn + kRimOut) * 0.5f;
            float len = 2.0f * Mathf.PI * rc / n * 1.06f;
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.5f) * 360.0f / n;
                var go = Prim(PrimitiveType.Cube, stage, "Rim", RouletteMeshes.Point(rc, a, kWallHeight * 0.5f), new Vector3(len, kWallHeight, kRimOut - kRimIn), new Vector3(0, a, 0), m.RimWall, true);
                go.AddComponent<ReflectWall>();
                go.isStatic = true;
            }

            // 軸の当たり判定(丸いので回さなくてよい)。飛んでいる積み木は動かない物の側面として跳ね返る
            var spindle = new GameObject("SpindleCollider");
            spindle.transform.SetParent(stage, false);
            var cap = spindle.AddComponent<CapsuleCollider>();
            cap.radius = kSpindle - 0.1f;
            cap.height = 8.0f;
            cap.center = new Vector3(0, 3.0f, 0);
            spindle.isStatic = true;

            // 奈落の見た目(盤の下の暗い井戸)
            Prim(PrimitiveType.Cylinder, stage, "Well", new Vector3(0, -6.5f, 0), new Vector3(kPocketOut * 2 + 0.6f, 0.5f, kPocketOut * 2 + 0.6f), Vector3.zero, AssetDatabase.LoadAssetAtPath<Material>(kAbyssMaterial));
            var wellWall = SaveMesh(RouletteMeshes.Sector(kPocketOut + 0.05f, kPocketOut + 0.3f, 0, 360, -0.6f, -6.0f, true, false, false, false, 90), "RouletteWell");
            MeshChild(stage, "WellWall", wellWall, Vector3.zero, Vector3.zero, m.Abyss);

            // 縁の上の濃い木のふち
            var rimTop = SaveMesh(RouletteMeshes.Sector(kRimIn - 0.05f, kRimOut + 0.05f, 0, 360, 1.03f, 0.98f, true, true, false, false, 90), "RouletteRimTop");
            MeshChild(stage, "RimTop", rimTop, Vector3.zero, Vector3.zero, m.WoodDark);
            // レールの上の金のひし形(飾り。縦長と横長を交互に)
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f;
                bool radial = i % 2 == 0;
                var d = Child(stage, "Deflector");
                d.localPosition = RouletteMeshes.Point(kTrackIn + 1.9f, a, 0.04f);
                d.localRotation = Quaternion.Euler(0, a + (radial ? 0 : 90), 0);
                d.localScale = radial ? new Vector3(0.55f, 1.0f, 1.5f) : new Vector3(0.45f, 1.0f, 1.0f);
                Prim(PrimitiveType.Cube, d, "Diamond", Vector3.zero, new Vector3(0.7f, 0.08f, 0.7f), new Vector3(0, 45, 0), m.Gold);
            }
        }

        //========================================
        // 盤・球・手・出来事
        //========================================

        static void BuildGame(Transform gimmicks, Mats m, TumikiSpawner spawner)
        {
            var gameGo = Child(gimmicks, "RouletteGame").gameObject;
            var game = gameGo.AddComponent<RouletteGame>();

            // 回る盤
            var wheelGo = Child(gameGo.transform, "Wheel").gameObject;
            var rb = wheelGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            wheelGo.AddComponent<MeshCollider>();
            var wheel = wheelGo.AddComponent<RouletteWheel>();
            var pocketVisuals = BuildWheelVisual(wheelGo.transform, m);
            var wso = new SerializedObject(wheel);
            wso.FindProperty("m_pocketCount").intValue = kPockets;
            SetInts(wso.FindProperty("m_numbers"), kNumbers);
            wso.FindProperty("m_spindleRadius").floatValue = kSpindle;
            wso.FindProperty("m_pocketInner").floatValue = kPocketIn;
            wso.FindProperty("m_pocketOuter").floatValue = kPocketOut;
            wso.FindProperty("m_thickness").floatValue = kThickness;
            SetArray(wso.FindProperty("m_pocketVisuals"), pocketVisuals);
            wso.ApplyModifiedPropertiesWithoutUndo();

            // 球
            var ballGo = new GameObject("Ball");
            ballGo.transform.SetParent(gameGo.transform, false);
            ballGo.transform.localPosition = new Vector3(0, 6, 0);
            var brb = ballGo.AddComponent<Rigidbody>();
            brb.isKinematic = true;
            brb.useGravity = false;
            var sc = ballGo.AddComponent<SphereCollider>();
            sc.radius = kBallRadius;
            var bvis = Child(ballGo.transform, "Visual");
            Prim(PrimitiveType.Sphere, bvis, "Shell", Vector3.zero, Vector3.one * (kBallRadius * 2), Vector3.zero, m.Ivory);
            var trail = ballGo.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.minVertexDistance = 0.15f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, kBallRadius * 1.6f), new Keyframe(1, 0));
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(new Color(1.0f, 0.95f, 0.8f), 0), new GradientColorKey(new Color(1.0f, 0.8f, 0.4f), 1) },
                new[] { new GradientAlphaKey(0.6f, 0), new GradientAlphaKey(0.0f, 1) });
            trail.colorGradient = grad;
            trail.sharedMaterial = m.Glow;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.emitting = false;
            var ball = ballGo.AddComponent<RouletteBall>();
            var bso = new SerializedObject(ball);
            bso.FindProperty("m_radius").floatValue = kBallRadius;
            bso.FindProperty("m_visual").objectReferenceValue = bvis;
            bso.FindProperty("m_trail").objectReferenceValue = trail;
            bso.ApplyModifiedPropertiesWithoutUndo();

            // ディーラーの手(指先が +Z、手のひらが下)
            var hand = BuildHand(gameGo.transform, m);

            // 出来事(緑・赤・黒の順)
            var evRoot = Child(gameGo.transform, "Events");
            var jackpot = Child(evRoot, "Event_Green_Jackpot").gameObject.AddComponent<RouletteJackpot>();
            var rain = Child(evRoot, "Event_Red_TumikiRain").gameObject.AddComponent<RouletteTumikiRain>();
            var weights = Child(evRoot, "Event_Black_WeightRain").gameObject.AddComponent<RouletteWeightRain>();
            var weightTemplate = BuildWeight(weights.transform, m);
            SetEvent(jackpot, "ジャックポット!!", new Color(0.3f, 1.0f, 0.45f));
            SetEvent(rain, "積み木の雨!", new Color(1.0f, 0.45f, 0.4f));
            SetEvent(weights, "重りの雨!", new Color(0.75f, 0.75f, 0.85f));
            {
                var so = new SerializedObject(rain);
                so.FindProperty("m_tumikiPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(kTumikiPrefab).GetComponent<TumikiBlock>();
                so.FindProperty("m_spawner").objectReferenceValue = spawner;
                so.ApplyModifiedPropertiesWithoutUndo();
                so = new SerializedObject(weights);
                so.FindProperty("m_template").objectReferenceValue = weightTemplate;
                so.ApplyModifiedPropertiesWithoutUndo();
                so = new SerializedObject(jackpot);
                so.FindProperty("m_tumikiRain").objectReferenceValue = rain;
                so.FindProperty("m_weightRain").objectReferenceValue = weights;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // ディーラーの賭け(盤の外の賭けの表に置くチップと、そこを映すサブカメラ)
            var bet = BuildBet(gameGo.transform, m, rain);

            // 縁の電飾
            var lightRoot = Child(gameGo.transform, "RimLights");
            var lights = new List<Renderer>();
            int count = 40;
            for (int i = 0; i < count; i++)
            {
                float a = i * 360.0f / count;
                var l = Prim(PrimitiveType.Sphere, lightRoot, $"Light_{i}", RouletteMeshes.Point((kRimIn + kRimOut) * 0.5f, a, 1.12f), Vector3.one * 0.38f, Vector3.zero, m.Glow);
                lights.Add(l.GetComponent<Renderer>());
            }

            var gso = new SerializedObject(game);
            gso.FindProperty("m_wheel").objectReferenceValue = wheel;
            gso.FindProperty("m_ball").objectReferenceValue = ball;
            gso.FindProperty("m_hand").objectReferenceValue = hand;
            gso.FindProperty("m_bet").objectReferenceValue = bet;
            SetArray(gso.FindProperty("m_events"), new List<Object> { jackpot, rain, weights });
            SetArray(gso.FindProperty("m_rimLights"), lights);
            gso.FindProperty("m_trackRadius").floatValue = (kTrackIn + kTrackOut) * 0.5f;
            gso.ApplyModifiedPropertiesWithoutUndo();
            hand.gameObject.SetActive(false);
        }

        // 盤の見た目(すべて Wheel の子。一緒に回る)。ポケットごとの見た目を返す
        // 中心から: 金の取っ手と台座 / 木の盤面(外へ行くほど濃い木) / 金の輪 / ポケット(くぼみ・番号の帯・外の金の輪)
        static List<GameObject> BuildWheelVisual(Transform wheel, Mats m)
        {
            float top = 0.01f, bottom = -kThickness;
            var vis = Child(wheel, "Visual");

            // 木の盤面(円すいのように、内側ほど明るく)
            float[] woodRings = { 0.0f, 5.2f, 7.4f, kBet };
            Material[] woodMats = { m.WoodLight, m.Wood, m.WoodDark };
            for (int i = 0; i < 3; i++)
            {
                var mesh = SaveMesh(RouletteMeshes.Sector(woodRings[i], woodRings[i + 1], 0, 360, top, bottom, false, false, false, false, 90), $"RouletteWood_{i}");
                MeshChild(vis, $"Wood_{i}", mesh, Vector3.zero, Vector3.zero, woodMats[i]);
            }
            // 木目(放射状の細い線)
            for (int i = 0; i < 24; i++)
            {
                float a = i * 15.0f + 7.5f;
                Prim(PrimitiveType.Cube, vis, "Grain", RouletteMeshes.Point((kHub + kBet) * 0.5f, a, top + 0.004f), new Vector3(0.05f, 0.01f, kBet - kHub - 0.4f), new Vector3(0, a, 0), m.WoodGrain);
            }
            // 中央の金の台座と縁
            MeshChild(vis, "Hub", SaveMesh(RouletteMeshes.Sector(0.0f, kHub, 0, 360, top + 0.03f, top, false, true, false, false, 90), "RouletteHub"), Vector3.zero, Vector3.zero, m.Gold);
            MeshChild(vis, "HubRing", SaveMesh(RouletteMeshes.Sector(kHub, kHub + 0.18f, 0, 360, top + 0.015f, top, false, false, false, false, 90), "RouletteHubRing"), Vector3.zero, Vector3.zero, m.GoldDark);
            // ポケットとの間の金の輪(穴の内側の壁にもなる)
            MeshChild(vis, "PocketBorder", SaveMesh(RouletteMeshes.Sector(kBet, kPocketIn, 0, 360, top + 0.04f, bottom, false, true, false, false, 120), "RoulettePocketBorder"), Vector3.zero, Vector3.zero, m.Gold);

            // 軸と取っ手(X字のタレット)
            Prim(PrimitiveType.Cylinder, vis, "Spindle", new Vector3(0, 0.55f, 0), new Vector3(kSpindle * 2, 0.55f, kSpindle * 2), Vector3.zero, m.Gold);
            Prim(PrimitiveType.Sphere, vis, "SpindleDome", new Vector3(0, 1.1f, 0), new Vector3(kSpindle * 1.9f, 1.3f, kSpindle * 1.9f), Vector3.zero, m.Gold);
            Prim(PrimitiveType.Cylinder, vis, "TurretPost", new Vector3(0, 2.0f, 0), new Vector3(0.6f, 0.5f, 0.6f), Vector3.zero, m.Gold);
            Prim(PrimitiveType.Sphere, vis, "TurretCap", new Vector3(0, 2.55f, 0), Vector3.one * 0.8f, Vector3.zero, m.GoldDark);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90.0f + 45.0f;
                Prim(PrimitiveType.Cylinder, vis, "TurretArm", RouletteMeshes.Point(1.25f, a, 2.3f), new Vector3(0.3f, 1.2f, 0.3f), new Vector3(90, a, 0), m.Gold);
                Prim(PrimitiveType.Capsule, vis, "TurretKnob", RouletteMeshes.Point(2.55f, a, 2.3f), new Vector3(0.42f, 0.35f, 0.42f), new Vector3(90, a, 0), m.Gold);
            }

            // ポケット: くぼみ(明るい木) / 番号の帯(赤・黒・緑) / 外の金の輪。仕切りは金の細い板(当たり判定なし)
            float w = 360.0f / kPockets;
            var cellMesh = SaveMesh(RouletteMeshes.Sector(kPocketIn, kNumberIn, -w * 0.5f, w * 0.5f, top - 0.02f, bottom), "RoulettePocketCell");
            var bandMesh = SaveMesh(RouletteMeshes.Sector(kNumberIn, kNumberOut, -w * 0.5f, w * 0.5f, top, bottom), "RoulettePocketBand");
            var ringMesh = SaveMesh(RouletteMeshes.Sector(kNumberOut, kPocketOut, -w * 0.5f, w * 0.5f, top + 0.03f, bottom), "RoulettePocketRing");
            var lineMesh = SaveMesh(RouletteMeshes.Sector(kNumberIn - 0.06f, kNumberIn + 0.06f, -w * 0.5f, w * 0.5f, top + 0.02f, top, false, false, false, false), "RoulettePocketLine");
            var pockets = new List<GameObject>();
            for (int i = 0; i < kPockets; i++)
            {
                float a = i * w;
                var p = Child(vis, $"Pocket_{i}_{kNumbers[i]}");
                p.localRotation = Quaternion.Euler(0, a, 0);
                Material band = i == 0 ? m.Green : (i % 2 == 1 ? m.Red : m.Black);
                MeshChild(p, "Cell", cellMesh, Vector3.zero, Vector3.zero, m.PocketWood);
                MeshChild(p, "Band", bandMesh, Vector3.zero, Vector3.zero, band);
                MeshChild(p, "Ring", ringMesh, Vector3.zero, Vector3.zero, m.Gold);
                MeshChild(p, "Line", lineMesh, Vector3.zero, Vector3.zero, m.Gold);
                // 仕切り: くぼみの間は高い板、番号の帯の間は細い線
                Prim(PrimitiveType.Cube, p, "Fret", RouletteMeshes.Point((kPocketIn + kNumberIn) * 0.5f, -w * 0.5f, top + 0.12f), new Vector3(0.14f, 0.26f, kNumberIn - kPocketIn), new Vector3(0, -w * 0.5f, 0), m.Gold);
                Prim(PrimitiveType.Cube, p, "BandLine", RouletteMeshes.Point((kNumberIn + kNumberOut) * 0.5f, -w * 0.5f, top + 0.01f), new Vector3(0.08f, 0.02f, kNumberOut - kNumberIn), new Vector3(0, -w * 0.5f, 0), m.Gold);
                // 番号(外側を上にして平らに置く)
                Text(p, "Number", kNumbers[i].ToString(), new Vector3(0, top + 0.02f, (kNumberIn + kNumberOut) * 0.5f), 0.0f, 0.7f, Color.white);
                pockets.Add(p.gameObject);
            }
            return pockets;
        }

        // ディーラーの賭け: 盤の外の賭けの表(BuildHall の BetLayout)の下の段に、緑・赤・黒のマスの位置を置き、
        // チップの山の見本と、そこを斜め上から映すサブカメラを作る
        static RouletteBet BuildBet(Transform parent, Mats m, RouletteTumikiRain payout)
        {
            var root = Child(parent, "DealerBet");
            var bet = root.gameObject.AddComponent<RouletteBet>();
            Vector3 rowCenter = kLayoutPos + new Vector3(0, 0, kBetRowZ);
            var spots = new List<Transform>();
            foreach (var (name, x) in new[] { ("Spot_Green", 0.0f), ("Spot_Red", -kBetBoxStep), ("Spot_Black", kBetBoxStep) })
            {
                var s = Child(root, name);
                s.position = rowCenter + new Vector3(x, 0.02f, 0);
                spots.Add(s);
            }

            // チップの山(赤・青・金のチップを重ねる)
            var chip = Child(root, "BetChipTemplate");
            Material[] chipMats = { m.ChipRed, m.ChipBlue, m.ChipRed, m.ChipBlue, m.Gold };
            for (int i = 0; i < chipMats.Length; i++)
            {
                Prim(PrimitiveType.Cylinder, chip, "Chip", new Vector3(0, 0.12f + i * 0.24f, 0), new Vector3(2.0f, 0.11f, 2.0f), Vector3.zero, chipMats[i]);
                Prim(PrimitiveType.Cylinder, chip, "Edge", new Vector3(0, 0.12f + i * 0.24f, 0), new Vector3(2.05f, 0.05f, 2.05f), Vector3.zero, m.Ivory);
            }
            chip.gameObject.SetActive(false);

            // サブカメラ(賭けの表の下の段を手前の斜め上から映す。画面の窓に出す間だけ有効)
            var camGo = new GameObject("BetCamera");
            camGo.transform.SetParent(root, false);
            camGo.transform.position = rowCenter + new Vector3(0, 12.0f, -5.0f);
            camGo.transform.LookAt(rowCenter + new Vector3(0, 0, 0.6f));
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 38.0f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200.0f;
            cam.enabled = false;

            var so = new SerializedObject(bet);
            SetArray(so.FindProperty("m_spots"), spots);
            so.FindProperty("m_spotSize").vector2Value = new Vector2(kBetBoxSize.x, kBetBoxSize.y);
            so.FindProperty("m_chipTemplate").objectReferenceValue = chip.gameObject;
            so.FindProperty("m_payoutRain").objectReferenceValue = payout;
            so.FindProperty("m_camera").objectReferenceValue = cam;
            so.ApplyModifiedPropertiesWithoutUndo();
            return bet;
        }
        static Transform BuildHand(Transform parent, Mats m)
        {
            var hand = Child(parent, "DealerHand");
            hand.localPosition = new Vector3(0, 8, 30);
            Prim(PrimitiveType.Sphere, hand, "Palm", new Vector3(0, 0, 0), new Vector3(1.9f, 0.7f, 2.0f), Vector3.zero, m.Glove);
            for (int i = 0; i < 4; i++)
            {
                float x = -0.66f + i * 0.44f;
                float len = i == 0 || i == 3 ? 0.42f : 0.5f;
                Prim(PrimitiveType.Capsule, hand, "Finger", new Vector3(x, -0.12f, 1.25f), new Vector3(0.38f, len, 0.38f), new Vector3(100, 0, 0), m.Glove);
            }
            Prim(PrimitiveType.Capsule, hand, "Thumb", new Vector3(0.95f, -0.15f, 0.5f), new Vector3(0.42f, 0.45f, 0.42f), new Vector3(80, 40, 0), m.Glove);
            Prim(PrimitiveType.Cylinder, hand, "Cuff", new Vector3(0, 0.05f, -1.15f), new Vector3(1.5f, 0.3f, 1.4f), new Vector3(90, 0, 0), m.Shirt);
            Prim(PrimitiveType.Cylinder, hand, "Button", new Vector3(0, 0.75f, -1.15f), new Vector3(0.25f, 0.05f, 0.25f), Vector3.zero, m.Gold);
            Prim(PrimitiveType.Cylinder, hand, "Sleeve", new Vector3(0, 0.6f, -5.0f), new Vector3(1.6f, 3.6f, 1.5f), new Vector3(80, 0, 0), m.Vest);
            return hand;
        }

        // 重りの見本(無効)。黒い鉄の円柱に取っ手と「1t」
        static GameObject BuildWeight(Transform parent, Mats m)
        {
            var w = Child(parent, "WeightTemplate");
            Prim(PrimitiveType.Cylinder, w, "Body", new Vector3(0, 0.65f, 0), new Vector3(2.0f, 0.65f, 2.0f), Vector3.zero, m.Iron);
            Prim(PrimitiveType.Cylinder, w, "Top", new Vector3(0, 1.32f, 0), new Vector3(1.6f, 0.05f, 1.6f), Vector3.zero, m.IronLight);
            Prim(PrimitiveType.Cube, w, "HandleL", new Vector3(-0.4f, 1.6f, 0), new Vector3(0.15f, 0.55f, 0.15f), Vector3.zero, m.IronLight);
            Prim(PrimitiveType.Cube, w, "HandleR", new Vector3(0.4f, 1.6f, 0), new Vector3(0.15f, 0.55f, 0.15f), Vector3.zero, m.IronLight);
            Prim(PrimitiveType.Cube, w, "HandleTop", new Vector3(0, 1.88f, 0), new Vector3(0.95f, 0.15f, 0.15f), Vector3.zero, m.IronLight);
            Text(w, "Label", "1t", new Vector3(0, 1.36f, -0.2f), 0.0f, 0.7f, Color.white);
            w.gameObject.SetActive(false);
            return w.gameObject;
        }

        static void SetEvent(RoulettePocketEvent ev, string label, Color color)
        {
            var so = new SerializedObject(ev);
            so.FindProperty("m_label").stringValue = label;
            so.FindProperty("m_color").colorValue = color;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        //========================================
        // 積み木の生成
        //========================================

        static TumikiSpawner BuildSpawner(Transform root, StageEntry template)
        {
            var src = template.GetComponentInChildren<TumikiSpawner>(true);
            var go = Object.Instantiate(src.gameObject, root);
            go.name = "TumikiSpawner";
            go.transform.localPosition = Vector3.zero;
            for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kSpawnAreaPrefab);
            int n = 0;
            // 賭けのエリア(赤・黒それぞれ2か所)、緑の0のそば、レールの左右
            foreach (float a in new[] { 67.5f, 112.5f, 247.5f, 292.5f })
            {
                Vector3 p = RouletteMeshes.Point(6.4f, a);
                Area(go.transform, prefab, ++n, p.x, p.z, TumikiSpawnArea.Shape.Circle, Vector2.zero, 1.8f);
            }
            Area(go.transform, prefab, ++n, 0.0f, -2.6f, TumikiSpawnArea.Shape.Rectangle, new Vector2(2.6f, 1.2f), 0);
            foreach (float a in new[] { 90.0f, 270.0f })
            {
                Vector3 p = RouletteMeshes.Point((kTrackIn + kTrackOut) * 0.5f, a);
                Area(go.transform, prefab, ++n, p.x, p.z, TumikiSpawnArea.Shape.Circle, Vector2.zero, 1.0f);
            }
            return go.GetComponent<TumikiSpawner>();
        }

        static void Area(Transform parent, GameObject prefab, int index, float x, float z, TumikiSpawnArea.Shape shape, Vector2 size, float radius)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = $"SpawnArea_{index}_{shape}";
            go.transform.localPosition = new Vector3(x, 0, z);
            var area = go.GetComponent<TumikiSpawnArea>();
            var so = new SerializedObject(area);
            so.FindProperty("m_shape").enumValueIndex = (int)shape;
            so.FindProperty("m_size").vector2Value = size;
            so.FindProperty("m_radius").floatValue = radius;
            so.ApplyModifiedPropertiesWithoutUndo();
            area.ApplyShape();
        }

        //========================================
        // 背景(カジノ)
        //========================================

        static void BuildHall(Transform hall, Mats m)
        {
            // ルーレットを埋め込んだ台(緑のラシャ)と木のふち
            // 台の天板は盤の部分(中央の四角)を抜いて4枚で作り、盤との間は輪で埋める(穴の下に天板が見えないように)
            const float hole = 18.0f;
            TableBox(hall, "TableTopN", -32, 48, hole, 29, kTableY - 1.0f, kTableY, m.TableFelt);
            TableBox(hall, "TableTopS", -32, 48, -21, -hole, kTableY - 1.0f, kTableY, m.TableFelt);
            TableBox(hall, "TableTopW", -32, -hole, -hole, hole, kTableY - 1.0f, kTableY, m.TableFelt);
            TableBox(hall, "TableTopE", hole, 48, -hole, hole, kTableY - 1.0f, kTableY, m.TableFelt);
            MeshChild(hall, "TableFill", SaveMesh(RouletteMeshes.Sector(kRimOut + 1.6f, 26.0f, 0, 360, kTableY - 0.005f, kTableY - 1.0f, false, false, false, false, 90), "RouletteTableFill"), Vector3.zero, Vector3.zero, m.TableFelt);
            Prim(PrimitiveType.Cube, hall, "TableRailN", new Vector3(8, kTableY + 0.2f, 29.5f), new Vector3(82, 1.4f, 1.6f), Vector3.zero, m.Wood);
            Prim(PrimitiveType.Cube, hall, "TableRailS", new Vector3(8, kTableY + 0.2f, -21.5f), new Vector3(82, 1.4f, 1.6f), Vector3.zero, m.Wood);
            Prim(PrimitiveType.Cube, hall, "TableRailW", new Vector3(-32.5f, kTableY + 0.2f, 4), new Vector3(1.6f, 1.4f, 52), Vector3.zero, m.Wood);
            Prim(PrimitiveType.Cube, hall, "TableRailE", new Vector3(48.5f, kTableY + 0.2f, 4), new Vector3(1.6f, 1.4f, 52), Vector3.zero, m.Wood);
            float by0 = kHallFloorY, by1 = kTableY - 1.0f;
            TableBox(hall, "TableBodyN", -31, 47, hole, 28, by0, by1, m.WoodDark);
            TableBox(hall, "TableBodyS", -31, 47, -20, -hole, by0, by1, m.WoodDark);
            TableBox(hall, "TableBodyW", -31, -hole, -hole, hole, by0, by1, m.WoodDark);
            TableBox(hall, "TableBodyE", hole, 47, -hole, hole, by0, by1, m.WoodDark);
            // 盤のまわりの木の鉢(縁の外側)
            MeshChild(hall, "Bowl", SaveMesh(RouletteMeshes.Sector(kRimOut, kRimOut + 1.6f, 0, 360, 0.6f, kTableY - 0.2f, false, true, false, false, 90), "RouletteBowl"), Vector3.zero, Vector3.zero, m.Wood);

            // 賭けの表(数字のマスと、下の段の 赤・0・黒 のマス。ディーラーは下の段に賭ける)
            var layout = Child(hall, "BetLayout");
            layout.localPosition = kLayoutPos;
            Prim(PrimitiveType.Cube, layout, "Border", new Vector3(0, 0, -2.8f), new Vector3(14.6f, 0.02f, 36.4f), Vector3.zero, m.Gold);
            foreach (var (name, x, mat, diamond) in new[] { ("BetRed", -kBetBoxStep, m.TableFeltDark, m.Red), ("BetZero", 0.0f, m.Green, (Material)null), ("BetBlack", kBetBoxStep, m.TableFeltDark, m.Black) })
            {
                Vector3 p = new Vector3(x, 0.02f, kBetRowZ);
                Prim(PrimitiveType.Cube, layout, name, p, new Vector3(kBetBoxSize.x, 0.02f, kBetBoxSize.y), Vector3.zero, mat);
                if (diamond != null) Prim(PrimitiveType.Cube, layout, "Diamond", p + new Vector3(0, 0.02f, 0), new Vector3(2.0f, 0.01f, 2.0f), new Vector3(0, 45, 0), diamond);
                else Text(layout, "ZeroBetText", "0", p + new Vector3(0, 0.05f, 0), 0.0f, 1.4f, Color.white);
            }
            Prim(PrimitiveType.Cube, layout, "Zero", new Vector3(0, 0.02f, 13.0f), new Vector3(14, 0.02f, 3.6f), Vector3.zero, m.Green);
            Text(layout, "ZeroText", "0", new Vector3(0, 0.05f, 13.0f), 0.0f, 1.4f, Color.white);
            for (int i = 0; i < 16; i++)
            {
                int row = i / 3, col = i % 3;
                int num = i + 1;
                bool red = System.Array.IndexOf(new[] { 1, 3, 5, 7, 9, 12, 14, 16 }, num) >= 0;
                Vector3 p = new Vector3(-4.6f + col * 4.6f, 0.02f, 9.4f - row * 4.6f);
                Prim(PrimitiveType.Cube, layout, $"Cell_{num}", p, new Vector3(4.4f, 0.02f, 4.4f), Vector3.zero, m.TableFeltDark);
                Prim(PrimitiveType.Cylinder, layout, "Dot", p + new Vector3(0, 0.02f, 0), new Vector3(3.2f, 0.01f, 3.2f), Vector3.zero, red ? m.Red : m.Black);
                Text(layout, "Num", num.ToString(), p + new Vector3(0, 0.06f, 0), 0.0f, 1.2f, Color.white);
            }

            // チップの山・サイコロ・トランプ
            var rng = new System.Random(20);
            Material[] chipMats = { m.ChipRed, m.ChipBlue, m.ChipGreen, m.ChipBlack, m.Ivory };
            Vector3[] stacks =
            {
                new Vector3(-22, 0, 16), new Vector3(-24, 0, 12), new Vector3(-20, 0, -14), new Vector3(21, 0, -12),
                new Vector3(22, 0, 20), new Vector3(-26, 0, -6), new Vector3(24, 0, 9), new Vector3(36, 0, -16),
            };
            foreach (var s in stacks)
            {
                int h = 3 + rng.Next(8);
                var mat = chipMats[rng.Next(chipMats.Length)];
                for (int k = 0; k < h; k++)
                {
                    Vector3 p = new Vector3(s.x + (float)(rng.NextDouble() - 0.5) * 0.15f, kTableY + 0.2f + k * 0.4f, s.z + (float)(rng.NextDouble() - 0.5) * 0.15f);
                    Prim(PrimitiveType.Cylinder, hall, "Chip", p, new Vector3(2.4f, 0.2f, 2.4f), Vector3.zero, mat);
                    Prim(PrimitiveType.Cylinder, hall, "ChipEdge", p, new Vector3(2.45f, 0.08f, 2.45f), Vector3.zero, m.Ivory);
                }
            }
            Prim(PrimitiveType.Cube, hall, "Dice", new Vector3(-21, kTableY + 0.8f, -8), Vector3.one * 1.6f, new Vector3(0, 25, 0), m.Ivory);
            Prim(PrimitiveType.Cube, hall, "Dice", new Vector3(-19, kTableY + 0.8f, -9.5f), Vector3.one * 1.6f, new Vector3(0, -15, 0), m.Ivory);
            for (int i = 0; i < 6; i++)
                Prim(PrimitiveType.Cube, hall, "Card", new Vector3(-14 + i * 0.3f, kTableY + 0.05f + i * 0.04f, -17 + i * 0.1f), new Vector3(3.2f, 0.04f, 4.6f), new Vector3(0, 10 + i * 8, 0), i == 5 ? m.Red : m.Ivory);

            // 床(赤い絨毯)と奥のスロットマシン
            Prim(PrimitiveType.Cube, hall, "Carpet", new Vector3(0, kHallFloorY - 0.5f, 20), new Vector3(400, 1, 300), Vector3.zero, m.Carpet);
            for (int i = 0; i < 6; i++)
            {
                var slot = Child(hall, $"Slot_{i}");
                slot.localPosition = new Vector3(-50 + i * 20, kHallFloorY, 62);
                Prim(PrimitiveType.Cube, slot, "Body", new Vector3(0, 9, 0), new Vector3(9, 18, 6), Vector3.zero, i % 2 == 0 ? m.ChipRed : m.ChipBlue);
                Prim(PrimitiveType.Cube, slot, "Screen", new Vector3(0, 11, -3.05f), new Vector3(6.5f, 4.5f, 0.1f), Vector3.zero, m.Lamp);
                Prim(PrimitiveType.Cube, slot, "Top", new Vector3(0, 19, 0), new Vector3(9.5f, 2, 6.5f), Vector3.zero, m.Gold);
                Prim(PrimitiveType.Cylinder, slot, "Lever", new Vector3(5.2f, 12, 0), new Vector3(0.4f, 3, 0.4f), Vector3.zero, m.Gold);
                Prim(PrimitiveType.Sphere, slot, "Knob", new Vector3(5.2f, 15.2f, 0), Vector3.one * 1.2f, Vector3.zero, m.ChipRed);
            }
            Prim(PrimitiveType.Cube, hall, "BackWall", new Vector3(0, kHallFloorY + 40, 80), new Vector3(400, 100, 2), Vector3.zero, m.WallPaper);
        }

        static void TableBox(Transform parent, string name, float x0, float x1, float z0, float z1, float y0, float y1, Material mat)
        {
            Prim(PrimitiveType.Cube, parent, name, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, y1 - y0, z1 - z0), Vector3.zero, mat);
        }

        //========================================
        // カメラ
        //========================================

        static void FrameCamera(Transform point, StageSwitcher switcher)
        {
            var shared = new SerializedObject(switcher).FindProperty("m_camera").objectReferenceValue as Camera;
            float fov = shared != null ? shared.fieldOfView : 45.0f;
            const float pitch = 56.0f;
            var go = new GameObject("TempCamera");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = fov;
                cam.aspect = 16.0f / 9.0f;
                Quaternion rot = Quaternion.Euler(pitch, 0, 0);
                Vector3 target = new Vector3(0, 0, -0.8f);
                var pts = new List<Vector3>();
                for (int i = 0; i < 16; i++)
                {
                    pts.Add(RouletteMeshes.Point(kRimOut + 0.3f, i * 22.5f, 0.0f));
                    pts.Add(RouletteMeshes.Point(kRimOut + 0.3f, i * 22.5f, 1.1f));
                }
                const float margin = 0.03f;
                for (float d = 10; d < 200; d += 0.25f)
                {
                    cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * d, rot);
                    bool fits = true;
                    foreach (var c in pts)
                    {
                        Vector3 v = cam.WorldToViewportPoint(c);
                        if (v.z <= 0 || v.x < margin || v.x > 1 - margin || v.y < margin || v.y > 1 - margin) { fits = false; break; }
                    }
                    if (fits) break;
                }
                point.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        //========================================
        // マテリアル・共通
        //========================================

        class Mats
        {
            public Material GoldDark, WoodLight, WoodGrain, PocketWood;
            public Material Red, Black, Green, Gold, Wood, WoodDark, Track, RimWall, Ivory, Glove, Shirt, Vest, Iron, IronLight;
            public Material TableFelt, TableFeltDark, Carpet, WallPaper, Lamp, ChipRed, ChipBlue, ChipGreen, ChipBlack, Glow, Abyss;

            public Mats()
            {
                Red = Lit("Red", new Color(0.78f, 0.08f, 0.08f), 0.6f);
                Black = Lit("Black", new Color(0.08f, 0.08f, 0.09f), 0.6f);
                Green = Lit("Green", new Color(0.05f, 0.55f, 0.25f), 0.6f);
                Gold = Lit("Gold", new Color(0.95f, 0.72f, 0.25f), 0.8f, 0.9f);
                GoldDark = Lit("GoldDark", new Color(0.75f, 0.52f, 0.15f), 0.8f, 0.9f);
                WoodLight = Lit("WoodLight", new Color(0.62f, 0.32f, 0.15f), 0.4f);
                Wood = Lit("Wood", new Color(0.46f, 0.2f, 0.09f), 0.45f);
                WoodDark = Lit("WoodDark", new Color(0.26f, 0.09f, 0.05f), 0.5f);
                WoodGrain = Lit("WoodGrain", new Color(0.3f, 0.12f, 0.06f), 0.5f);
                PocketWood = Lit("PocketWood", new Color(0.78f, 0.55f, 0.32f), 0.6f);
                Track = Lit("Track", new Color(0.38f, 0.11f, 0.06f), 0.6f);
                RimWall = SeeThrough("RimWall", new Color(0.3f, 0.09f, 0.05f));
                Ivory = Lit("Ivory", new Color(0.97f, 0.95f, 0.88f), 0.9f);
                Glove = Lit("Glove", new Color(0.98f, 0.98f, 0.96f), 0.3f);
                Shirt = Lit("Shirt", new Color(0.95f, 0.95f, 0.97f), 0.2f);
                Vest = Lit("Vest", new Color(0.1f, 0.1f, 0.12f), 0.3f);
                Iron = Lit("Iron", new Color(0.12f, 0.12f, 0.14f), 0.5f, 0.6f);
                IronLight = Lit("IronLight", new Color(0.3f, 0.3f, 0.33f), 0.6f, 0.7f);
                TableFelt = Lit("TableFelt", new Color(0.04f, 0.35f, 0.18f), 0.05f);
                TableFeltDark = Lit("TableFeltDark", new Color(0.03f, 0.27f, 0.14f), 0.05f);
                Carpet = Lit("Carpet", new Color(0.4f, 0.06f, 0.1f), 0.05f);
                WallPaper = Lit("WallPaper", new Color(0.18f, 0.06f, 0.1f), 0.2f);
                Lamp = Lit("Lamp", new Color(1.0f, 0.9f, 0.6f), 0.3f, 0.0f, new Color(1.0f, 0.8f, 0.4f) * 1.5f);
                ChipRed = Lit("ChipRed", new Color(0.8f, 0.12f, 0.12f), 0.5f);
                ChipBlue = Lit("ChipBlue", new Color(0.15f, 0.3f, 0.8f), 0.5f);
                ChipGreen = Lit("ChipGreen", new Color(0.12f, 0.6f, 0.3f), 0.5f);
                ChipBlack = Lit("ChipBlack", new Color(0.1f, 0.1f, 0.1f), 0.5f);
                Glow = Particle("Glow");
                Abyss = AssetDatabase.LoadAssetAtPath<Material>(kAbyssMaterial);
            }

            static Material Lit(string name, Color color, float smoothness, float metallic = 0.0f, Color? emission = null)
            {
                string path = $"{kAssetDir}/Roulette_{name}.mat";
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                else if (mat.shader != shader) mat.shader = shader;
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", metallic);
                if (emission.HasValue)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emission.Value);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                EditorUtility.SetDirty(mat);
                return mat;
            }

            static Material SeeThrough(string name, Color color)
            {
                string path = $"{kAssetDir}/Roulette_{name}.mat";
                var template = AssetDatabase.LoadAssetAtPath<Material>(kWallTemplateMaterial);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = template != null ? new Material(template) : new Material(Shader.Find("Nakagawa/SeeThroughWall"));
                    AssetDatabase.CreateAsset(mat, path);
                }
                else if (template != null)
                {
                    mat.shader = template.shader;
                    mat.CopyPropertiesFromMaterial(template);
                }
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            static Material Particle(string name)
            {
                string path = $"{kAssetDir}/Roulette_{name}.mat";
                var src = ToyKit.ParticleMaterial(true, null);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(src);
                    AssetDatabase.CreateAsset(mat, path);
                }
                else
                {
                    mat.shader = src.shader;
                    mat.CopyPropertiesFromMaterial(src);
                    mat.shaderKeywords = src.shaderKeywords;
                    mat.renderQueue = src.renderQueue;
                }
                Object.DestroyImmediate(src);
                EditorUtility.SetDirty(mat);
                return mat;
            }
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = $"{kAssetDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            mesh.name = name;
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.vertices = mesh.vertices;
            existing.normals = mesh.normals;
            existing.uv = mesh.uv;
            existing.triangles = mesh.triangles;
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        // 平らに置いた文字(上から読める。yaw: 文字の上が向く角度)
        static void Text(Transform parent, string name, string text, Vector3 localPos, float yaw, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90, yaw, 0);
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = size * 0.25f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = color;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = font.material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 localScale, Vector3 euler, Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = localScale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static void MeshChild(Transform parent, string name, Mesh mesh, Vector3 localPos, Vector3 euler, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void SetArray<T>(SerializedProperty prop, List<T> items) where T : Object
        {
            prop.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        static void SetInts(SerializedProperty prop, int[] values)
        {
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).intValue = values[i];
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
