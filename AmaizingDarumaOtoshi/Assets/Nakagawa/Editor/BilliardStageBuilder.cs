using System.Collections.Generic;
using Nakahira;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Nakagawa.EditorTools
{
    // Stage19「ビリヤード台」を、開いている AllStages(Assets/Nakagawa/Stages/AllStages.unity)の中に組み立てる
    //  ・同じ id のステージ(Stage19)があれば作り直す。他のステージ・共有の物はそのまま
    //  ・積み木の生成(TumikiSpawner)・ステージ範囲の見本は Stage18 の物を写して使う(生成パターン・試合の参照を引き継ぐ)
    //  ・マテリアル・メッシュ・物理マテリアルは Assets/Nakagawa/Billiard に作る(作り直すと値を上書き)
    // 数値はこのファイルの定数・定義を直して作り直せる。シーンは保存しない(保存は呼び出し側 / 手で行う)
    public static class BilliardStageBuilder
    {
        public const string kStageId = "Stage19";
        const string kTemplateId = "Stage18";
        const string kCombinedPath = "Assets/Nakagawa/Stages/AllStages.unity";
        const string kAssetDir = "Assets/Nakagawa/Billiard";
        const string kSpawnAreaPrefab = "Assets/Nakagawa/Prefabs/TumikiSpawnArea.prefab";
        const string kTumikiPrefab = "Assets/Nakahira/Player/Prefabs/Tumiki.prefab";
        const string kWallTemplateMaterial = "Assets/Nakagawa/Backdrops/Materials/Monja_StageWall.mat";

        // 台の大きさ(クッションの内側)。ポケットは四隅と長い辺の中央
        const float kHalfX = 17.0f, kHalfZ = 8.5f;
        const float kCornerPocket = 2.6f;          // 四隅の穴(正方形)の一辺
        const float kSidePocketW = 2.6f, kSidePocketD = 2.0f;  // 長い辺の中央の穴の幅・奥行き
        const float kRail = 1.0f;                  // クッション(壁)の厚さ
        const float kWallHeight = 5.0f;            // 壁の高さ(見える1mと、見えない残りに分ける)
        const float kBallRadius = 0.9f;
        const float kHallFloorY = -11.0f;

        public const string kTitle = "ビリヤード台";
        const string kConcept =
            "ビリヤード台の上。緑のラシャの床を木枠とクッション(壁)が囲み、四隅と長い辺の中央に6つのポケット(穴)が空いている。" +
            "開始時は手玉と、3種類×2個の的玉を三角に並べたラック。ときどき台の外から大きな手とキューが現れ、狙いを定めて手玉を的玉へ突く。";
        const string kFlow =
            "転がってくる玉は重く、速いうちに当たるとプレイヤーは大きくはじかれる。ポケットは奈落なので、はじかれた先が穴なら脱落。" +
            "キューが構えると手玉から狙いの点線が出るので、射線から逃げるか、的玉の陰に相手を誘い込むかの駆け引きになる。" +
            "的玉がポケットに入ると種類ごとのギミックが発動する(赤: 台がポケットの方へ傾き全員が流される / 青: プレイヤーの位置をぐるっと入れ替える / 黄: ポケットから積み木が扇状に噴き出す)。" +
            "的玉は約25秒後に元の位置へ戻る。積み木の弾で玉を押してポケットへ落とし、ギミックを自分から起こすこともできる。";

        // 玉の種類(ギミックの番号)。0: 赤=傾き / 1: 青=入れ替え / 2: 黄=積み木噴出
        static readonly Color[] kTypeColors =
        {
            new Color(0.85f, 0.12f, 0.12f), new Color(0.15f, 0.3f, 0.85f), new Color(0.98f, 0.8f, 0.12f),
        };
        static readonly string[] kTypeLabels = { "台が傾く!", "位置シャッフル!", "積み木噴水!" };

        // ラックの並び(手前の頂点から奥へ)。type: 種類, stripe: 縞玉か, number: 番号
        struct RackBall { public int Type; public bool Stripe; public int Number; }
        static readonly RackBall[] kRack =
        {
            new RackBall { Type = 2, Stripe = false, Number = 1 },
            new RackBall { Type = 1, Stripe = true, Number = 10 }, new RackBall { Type = 0, Stripe = false, Number = 3 },
            new RackBall { Type = 0, Stripe = true, Number = 11 }, new RackBall { Type = 1, Stripe = false, Number = 2 }, new RackBall { Type = 2, Stripe = true, Number = 9 },
        };
        static readonly Vector3 kHeadSpot = new Vector3(-8.5f, 0, 0);
        static readonly Vector3 kRackApex = new Vector3(7.5f, 0, 0);

        [MenuItem("Nakagawa/Stages/Build Stage19 (Billiard) Into Open AllStages")]
        public static void BuildMenu()
        {
            string report = Build();
            Debug.Log(report);
        }

        // 開いている AllStages に Stage19 を作る(作り直す)。結果の説明を返す
        public static string Build()
        {
            Scene scene = SceneManager.GetSceneByPath(kCombinedPath);
            if (!scene.IsValid() || !scene.isLoaded) return $"[BilliardStageBuilder] {kCombinedPath} を開いてから実行してください";

            StageSwitcher switcher = null;
            StageEntry template = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (switcher == null) switcher = go.GetComponentInChildren<StageSwitcher>(true);
                if (go.name == kTemplateId) template = go.GetComponent<StageEntry>();
            }
            if (switcher == null || template == null) return $"[BilliardStageBuilder] StageSwitcher か {kTemplateId} が見つかりません";

            EnsureFolder(kAssetDir);
            var mats = new Mats();

            // 前に作ったものは消す
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == kStageId) Object.DestroyImmediate(go);
            }

            var rootGo = new GameObject(kStageId);
            SceneManager.MoveGameObjectToScene(rootGo, scene);
            Transform root = rootGo.transform;
            var entry = rootGo.AddComponent<StageEntry>();

            // 床・壁(当たり判定あり)
            var stage = Child(root, "Stage");
            BuildFloorsAndWalls(stage, mats);
            StageWallSplitter.Split(stage);

            // 台の見た目(当たり判定なし)
            BuildTableDecor(Child(stage, "TableDecor"), mats);

            // ギミック(玉・キュー・ポケットのギミック)
            var gimmicks = Child(root, "Gimmicks");

            // 積み木の生成(Stage18 の TumikiSpawner を写し、生成エリアを置き直す)
            var spawner = BuildSpawner(root, template);

            BuildTable(gimmicks, mats, spawner);

            // ステージ範囲の見本(無効)
            var areaSrc = template.StageArea;
            var areaGo = Object.Instantiate(areaSrc.gameObject, root);
            areaGo.name = "StageArea_Template";
            areaGo.SetActive(false);
            areaGo.transform.localPosition = Vector3.zero;
            var areaSo = new SerializedObject(areaGo.GetComponent<StageArea>());
            areaSo.FindProperty("m_size").vector2Value = new Vector2(kHalfX * 2 + 2, kHalfZ * 2 + 2);
            areaSo.ApplyModifiedPropertiesWithoutUndo();

            // 出現位置(手玉・ラックから離した四隅寄り)
            var spawnRoot = Child(root, "SpawnPoints");
            var spawnPositions = new[] { new Vector3(-11, 0, 4.8f), new Vector3(12.5f, 0, 5.0f), new Vector3(-11, 0, -4.8f), new Vector3(12.5f, 0, -5.0f) };
            var spawns = new Transform[spawnPositions.Length];
            for (int i = 0; i < spawns.Length; i++)
            {
                spawns[i] = Child(spawnRoot, $"SpawnPoint{i + 1}");
                spawns[i].localPosition = spawnPositions[i];
            }

            // カメラ
            var cameraPoint = Child(root, "CameraPoint");
            FrameCamera(cameraPoint, switcher);

            // 背景(ビリヤード場)
            var hall = BuildHall(root, mats);
            var tilt = root.GetComponentInChildren<BilliardTiltGimmick>(true);
            if (tilt != null)
            {
                var so = new SerializedObject(tilt);
                so.FindProperty("m_tiltVisual").objectReferenceValue = hall;
                // 部屋をポケットと反対へ傾けて、台がポケットの方へ傾いたように見せる
                so.FindProperty("m_tiltAngle").floatValue = -2.0f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var info = Child(root, "StageInfo").gameObject.AddComponent<StageInfo>();
            info.Set(kStageId, kTitle, kConcept, kFlow,
                $"台 {kHalfX * 2}×{kHalfZ * 2}m(クッションの内側) / ポケット6(四隅{kCornerPocket}m・中央{kSidePocketW}×{kSidePocketD}m) / " +
                "手玉1 + 的玉6(赤・青・黄×2) / キュー1 / ギミック3(傾き・入れ替え・積み木噴出) / 生成エリア7 / 出現位置4");

            entry.Set(info, spawns, areaGo.GetComponent<StageArea>(), cameraPoint);

            // StageSwitcher に登録して並べ直す
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
            return $"[BilliardStageBuilder] {kStageId} {kTitle} を {kCombinedPath} に作りました(全{stages.Count}ステージ。シーンは未保存)";
        }

        //========================================
        // 床・壁
        //========================================

        static void BuildFloorsAndWalls(Transform stage, Mats m)
        {
            float cx = kHalfX - kCornerPocket;     // 四隅の穴の内側の端(X)
            float cz = kHalfZ - kCornerPocket;     // 四隅の穴の内側の端(Z)
            float sx = kSidePocketW * 0.5f;        // 中央の穴の半幅
            float sz = kHalfZ - kSidePocketD;      // 中央の穴の内側の端(Z)

            int n = 0;
            // 西・東の大きな床(長い辺の端から端まで。四隅の穴の分だけ短い)
            Floor(stage, m, ref n, -(cx + sx) * 0.5f, 0, cx - sx, kHalfZ * 2);
            Floor(stage, m, ref n, (cx + sx) * 0.5f, 0, cx - sx, kHalfZ * 2);
            // 中央の細い床(中央の穴の間)
            Floor(stage, m, ref n, 0, 0, sx * 2, sz * 2);
            // 短い辺の床(四隅の穴の間)
            Floor(stage, m, ref n, -(kHalfX + cx) * 0.5f, 0, kHalfX - cx, cz * 2);
            Floor(stage, m, ref n, (kHalfX + cx) * 0.5f, 0, kHalfX - cx, cz * 2);

            // クッション(ポケットの間)
            float rz = kHalfZ + kRail * 0.5f, rx = kHalfX + kRail * 0.5f;
            foreach (float s in new[] { -1.0f, 1.0f })
            {
                Wall(stage, "Cushion", m.Cushion, -(cx + sx) * 0.5f, s * rz, cx - sx, kRail);
                Wall(stage, "Cushion", m.Cushion, (cx + sx) * 0.5f, s * rz, cx - sx, kRail);
                Wall(stage, "Cushion", m.Cushion, s * rx, 0, kRail, cz * 2);
            }
            // ポケットの奥(革の囲い)
            foreach (float s in new[] { -1.0f, 1.0f })
            {
                foreach (float t in new[] { -1.0f, 1.0f })
                {
                    // 四隅: L字
                    Wall(stage, "PocketBack", m.Leather, s * (cx + (kHalfX + kRail - cx) * 0.5f), t * rz, kHalfX + kRail - cx, kRail);
                    Wall(stage, "PocketBack", m.Leather, s * rx, t * (cz + kCornerPocket * 0.5f), kRail, kCornerPocket);
                }
                // 長い辺の中央
                Wall(stage, "PocketBack", m.Leather, 0, s * rz, kSidePocketW, kRail);
            }

            // 床の線(ヘッドストリング)と、手玉・ラックの置き場所の印(見た目だけ)
            Prim(PrimitiveType.Cube, stage, "HeadString", new Vector3(kHeadSpot.x, 0.004f, 0), new Vector3(0.08f, 0.01f, kHalfZ * 2 - 0.2f), Vector3.zero, m.Line);
            Prim(PrimitiveType.Cylinder, stage, "HeadSpot", kHeadSpot + Vector3.up * 0.006f, new Vector3(0.35f, 0.006f, 0.35f), Vector3.zero, m.Line);
            Prim(PrimitiveType.Cylinder, stage, "FootSpot", kRackApex + Vector3.up * 0.006f, new Vector3(0.35f, 0.006f, 0.35f), Vector3.zero, m.Line);
        }

        static void Floor(Transform parent, Mats m, ref int index, float x, float z, float w, float d)
        {
            var go = Prim(PrimitiveType.Cube, parent, $"Floor_{index++}", new Vector3(x, -0.5f, z), new Vector3(w, 1.0f, d), Vector3.zero, m.Felt, true);
            go.isStatic = true;
        }

        static void Wall(Transform parent, string name, Material mat, float x, float z, float w, float d)
        {
            var go = Prim(PrimitiveType.Cube, parent, name, new Vector3(x, kWallHeight * 0.5f, z), new Vector3(w, kWallHeight, d), Vector3.zero, mat, true);
            go.AddComponent<ReflectWall>();
            go.isStatic = true;
        }

        //========================================
        // 台の飾り(木枠・ダイヤモンド・ポケットの穴・脚)
        //========================================

        static void BuildTableDecor(Transform decor, Mats m)
        {
            float frameW = 1.6f;
            float outerX = kHalfX + kRail, outerZ = kHalfZ + kRail;
            float topY = 1.06f, bottomY = -2.2f;
            float h = topY - bottomY, cy = (topY + bottomY) * 0.5f;
            // 木枠(クッションの外側)
            foreach (float s in new[] { -1.0f, 1.0f })
            {
                Prim(PrimitiveType.Cube, decor, "FrameLong", new Vector3(0, cy, s * (outerZ + frameW * 0.5f)), new Vector3((outerX + frameW) * 2, h, frameW), Vector3.zero, m.Wood);
                Prim(PrimitiveType.Cube, decor, "FrameShort", new Vector3(s * (outerX + frameW * 0.5f), cy, 0), new Vector3(frameW, h, outerZ * 2), Vector3.zero, m.Wood);
            }
            // 木枠の上の縁取り(少し明るい木)
            foreach (float s in new[] { -1.0f, 1.0f })
            {
                Prim(PrimitiveType.Cube, decor, "FrameTrimLong", new Vector3(0, topY + 0.03f, s * (outerZ + frameW - 0.12f)), new Vector3((outerX + frameW) * 2, 0.06f, 0.24f), Vector3.zero, m.WoodLight);
                Prim(PrimitiveType.Cube, decor, "FrameTrimShort", new Vector3(s * (outerX + frameW - 0.12f), topY + 0.03f, 0), new Vector3(0.24f, 0.06f, (outerZ + frameW) * 2), Vector3.zero, m.WoodLight);
            }
            // ダイヤモンド(目印)。ポケットの間を4等分した位置
            float zRow = outerZ + frameW * 0.5f, xRow = outerX + frameW * 0.5f;
            for (int i = 1; i <= 7; i++)
            {
                if (i == 4) continue;
                float x = -kHalfX + kHalfX * 2 * i / 8.0f;
                foreach (float s in new[] { -1.0f, 1.0f }) Diamond(decor, m, new Vector3(x, topY + 0.02f, s * zRow));
            }
            for (int i = 1; i <= 3; i++)
            {
                float z = -kHalfZ + kHalfZ * 2 * i / 4.0f;
                foreach (float s in new[] { -1.0f, 1.0f }) Diamond(decor, m, new Vector3(s * xRow, topY + 0.02f, z));
            }
            // 台の胴(床の下)とポケットの穴の中(黒い筒)
            Prim(PrimitiveType.Cube, decor, "Body", new Vector3(0, -2.6f, 0), new Vector3((outerX + frameW) * 2 - 0.2f, 3.0f, (outerZ + frameW) * 2 - 0.2f), Vector3.zero, m.WoodDark);
            foreach (var p in PocketCenters())
            {
                bool corner = Mathf.Abs(p.x) > 1.0f;
                float r = corner ? kCornerPocket * 0.62f : kSidePocketW * 0.55f;
                Prim(PrimitiveType.Cylinder, decor, "PocketCup", new Vector3(p.x, -1.06f, p.z), new Vector3(r * 2, 1.0f, r * 2), Vector3.zero, m.PocketBlack);
            }
            // 脚
            foreach (float x in new[] { -15.0f, 0.0f, 15.0f })
            {
                foreach (float z in new[] { -7.5f, 7.5f })
                {
                    Prim(PrimitiveType.Cylinder, decor, "Leg", new Vector3(x, (kHallFloorY - 4.1f) * 0.5f, z), new Vector3(2.4f, (-4.1f - kHallFloorY) * 0.5f, 2.4f), Vector3.zero, m.WoodDark);
                }
            }
            // 木枠の上の小物: チョーク(青い角)と三角ラック
            Prim(PrimitiveType.Cube, decor, "Chalk", new Vector3(-outerX - 0.8f, topY + 0.35f, outerZ + 0.8f), new Vector3(0.7f, 0.7f, 0.7f), new Vector3(0, 20, 0), m.Chalk);
            Prim(PrimitiveType.Cube, decor, "Chalk", new Vector3(outerX + 0.8f, topY + 0.35f, -outerZ - 0.8f), new Vector3(0.7f, 0.7f, 0.7f), new Vector3(0, -35, 0), m.Chalk);
            var rack = Child(decor, "TriangleRack");
            rack.localPosition = new Vector3(9.0f, topY + 0.2f, outerZ + frameW * 0.5f + 0.1f);
            rack.localRotation = Quaternion.Euler(0, 0, 0);
            float side = 5.0f;
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120.0f;
                Vector3 mid = Quaternion.Euler(0, a, 0) * new Vector3(0, 0, -side * 0.2887f);
                Prim(PrimitiveType.Cube, rack, "Bar", mid, new Vector3(side, 0.35f, 0.25f), new Vector3(0, a, 0), m.WoodLight);
            }
        }

        static void Diamond(Transform parent, Mats m, Vector3 pos)
        {
            Prim(PrimitiveType.Cube, parent, "Diamond", pos, new Vector3(0.28f, 0.03f, 0.28f), new Vector3(0, 45, 0), m.Pearl);
        }

        static IEnumerable<Vector3> PocketCenters()
        {
            float cxp = kHalfX - kCornerPocket * 0.5f, czp = kHalfZ - kCornerPocket * 0.5f;
            foreach (float s in new[] { -1.0f, 1.0f })
            {
                foreach (float t in new[] { -1.0f, 1.0f }) yield return new Vector3(s * cxp, 0, t * czp);
                yield return new Vector3(0, 0, s * (kHalfZ - kSidePocketD * 0.5f));
            }
        }

        //========================================
        // 玉・キュー・ギミック
        //========================================

        static void BuildTable(Transform gimmicks, Mats m, TumikiSpawner spawner)
        {
            var tableGo = Child(gimmicks, "BilliardTable").gameObject;
            var table = tableGo.AddComponent<BilliardTable>();

            // ポケット
            var pocketRoot = Child(tableGo.transform, "Pockets");
            var pockets = new List<Transform>();
            int pi = 0;
            foreach (var p in PocketCenters())
            {
                var t = Child(pocketRoot, $"Pocket_{pi++}");
                t.localPosition = p;
                pockets.Add(t);
            }

            // 玉
            var ballRoot = Child(tableGo.transform, "Balls");
            var physMat = BallPhysicsMaterial();
            var bandMesh = SaveMesh(BuildSphereBand(kBallRadius * 1.005f, 28.0f), "BilliardBallBand");
            var capMesh = SaveMesh(BuildSphereCap(kBallRadius * 1.006f, 24.0f), "BilliardBallSpot");
            var dotMesh = SaveMesh(BuildSphereCap(kBallRadius * 1.006f, 9.0f), "BilliardBallDot");

            var cue = CreateBall(ballRoot, "CueBall", BilliardBall.Kind.Cue, 0, 0, Color.white, false, kHeadSpot, m, physMat, bandMesh, capMesh, dotMesh);
            var objects = new List<BilliardBall>();
            float rowStep = kBallRadius * 2.0f * 0.866f + 0.1f;
            int k = 0;
            for (int row = 0; row < 3; row++)
            {
                for (int i = 0; i <= row; i++)
                {
                    var rb = kRack[k++];
                    float z = (i - row * 0.5f) * (kBallRadius * 2.0f + 0.1f);
                    Vector3 pos = kRackApex + new Vector3(row * rowStep, 0, z);
                    objects.Add(CreateBall(ballRoot, $"Ball_{rb.Number}", BilliardBall.Kind.Object, rb.Type, rb.Number, kTypeColors[rb.Type], rb.Stripe, pos, m, physMat, bandMesh, capMesh, dotMesh));
                }
            }

            // キュー
            var cueRig = BuildCueRig(tableGo.transform, m);

            // ギミック(種類の番号順)
            var gimmickRoot = Child(tableGo.transform, "PocketGimmicks");
            var tilt = Child(gimmickRoot, "Gimmick_Tilt").gameObject.AddComponent<BilliardTiltGimmick>();
            var shuffle = Child(gimmickRoot, "Gimmick_Shuffle").gameObject.AddComponent<BilliardShuffleGimmick>();
            var fountain = Child(gimmickRoot, "Gimmick_Fountain").gameObject.AddComponent<BilliardFountainGimmick>();
            var list = new BilliardPocketGimmick[] { tilt, shuffle, fountain };
            for (int i = 0; i < list.Length; i++)
            {
                var so = new SerializedObject(list[i]);
                so.FindProperty("m_label").stringValue = kTypeLabels[i];
                so.FindProperty("m_color").colorValue = Color.Lerp(kTypeColors[i], Color.white, 0.15f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            {
                var so = new SerializedObject(fountain);
                so.FindProperty("m_tumikiPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(kTumikiPrefab).GetComponent<TumikiBlock>();
                so.FindProperty("m_spawner").objectReferenceValue = spawner;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var tso = new SerializedObject(table);
            tso.FindProperty("m_size").vector2Value = new Vector2(kHalfX * 2, kHalfZ * 2);
            SetArray(tso.FindProperty("m_pockets"), pockets);
            tso.FindProperty("m_cueBall").objectReferenceValue = cue;
            SetArray(tso.FindProperty("m_objectBalls"), objects);
            SetArray(tso.FindProperty("m_gimmicks"), new List<Object>(list));
            tso.FindProperty("m_cue").objectReferenceValue = cueRig;
            tso.ApplyModifiedPropertiesWithoutUndo();
        }

        static BilliardBall CreateBall(Transform parent, string name, BilliardBall.Kind kind, int type, int number, Color color, bool stripe,
            Vector3 floorPos, Mats m, PhysicsMaterial physMat, Mesh band, Mesh cap, Mesh dot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = floorPos + Vector3.up * kBallRadius;
            var col = go.AddComponent<SphereCollider>();
            col.radius = kBallRadius;
            col.sharedMaterial = physMat;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 4.0f;
            rb.linearDamping = 0.4f;
            rb.angularDamping = 0.05f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var visual = Child(go.transform, "Visual");
            // 置くたびに同じ向きにならないよう、少し傾けておく
            visual.localRotation = Quaternion.Euler(20.0f + number * 37.0f, number * 53.0f, 10.0f);
            Material ballMat = kind == BilliardBall.Kind.Cue || stripe ? m.BallWhite : m.BallColor(color);
            Prim(PrimitiveType.Sphere, visual, "Shell", Vector3.zero, Vector3.one * (kBallRadius * 2), Vector3.zero, ballMat);
            if (kind == BilliardBall.Kind.Cue)
            {
                // 手玉: 転がりが分かるよう小さな赤い点を3つ
                foreach (var e in new[] { new Vector3(0, 0, 0), new Vector3(110, 0, 0), new Vector3(110, 120, 0), new Vector3(110, 240, 0) })
                    MeshChild(visual, "Dot", dot, e, m.BallDot);
            }
            else if (stripe)
            {
                MeshChild(visual, "Stripe", band, Vector3.zero, m.BallColor(color));
            }
            else
            {
                // 番号の白い丸(表と裏)
                MeshChild(visual, "Spot", cap, new Vector3(90, 0, 0), m.BallWhite);
                MeshChild(visual, "Spot", cap, new Vector3(-90, 0, 0), m.BallWhite);
            }

            // 速く転がるときの軌跡
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.22f;
            trail.minVertexDistance = 0.2f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, kBallRadius * 1.6f), new Keyframe(1, 0));
            var grad = new Gradient();
            Color tc = kind == BilliardBall.Kind.Cue ? new Color(0.8f, 0.9f, 1.0f) : Color.Lerp(color, Color.white, 0.3f);
            grad.SetKeys(new[] { new GradientColorKey(tc, 0), new GradientColorKey(tc, 1) }, new[] { new GradientAlphaKey(0.55f, 0), new GradientAlphaKey(0.0f, 1) });
            trail.colorGradient = grad;
            trail.sharedMaterial = m.Trail;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.emitting = false;

            var ball = go.AddComponent<BilliardBall>();
            var so = new SerializedObject(ball);
            so.FindProperty("m_kind").enumValueIndex = (int)kind;
            so.FindProperty("m_type").intValue = type;
            so.FindProperty("m_number").intValue = number;
            so.FindProperty("m_color").colorValue = color;
            so.FindProperty("m_radius").floatValue = kBallRadius;
            so.FindProperty("m_visual").objectReferenceValue = visual;
            so.FindProperty("m_trail").objectReferenceValue = trail;
            so.ApplyModifiedPropertiesWithoutUndo();
            return ball;
        }

        // 手とキュー
        static BilliardCue BuildCueRig(Transform parent, Mats m)
        {
            var rigGo = Child(parent, "CueRig").gameObject;
            var cue = rigGo.AddComponent<BilliardCue>();
            var visual = Child(rigGo.transform, "Visual");

            // キュー(先が原点、後ろ(-Z)へ伸びる)
            var pivot = Child(visual, "CuePivot");
            CylZ(pivot, "Tip", 0.0f, 0.14f, 0.13f, m.Chalk);
            CylZ(pivot, "Ferrule", 0.14f, 0.4f, 0.145f, m.Pearl);
            CylZ(pivot, "Shaft", 0.54f, 6.6f, 0.17f, m.Maple);
            CylZ(pivot, "Joint", 7.14f, 0.3f, 0.21f, m.Pearl);
            CylZ(pivot, "Butt", 7.44f, 3.2f, 0.25f, m.WoodDark);
            CylZ(pivot, "Wrap", 10.64f, 1.8f, 0.27f, m.Wrap);
            CylZ(pivot, "Bumper", 12.44f, 0.2f, 0.28f, m.Wrap);
            // 後ろの手(キューを握る)と腕
            var grip = Child(pivot, "GripHand");
            grip.localPosition = new Vector3(0, 0, -11.4f);
            Prim(PrimitiveType.Sphere, grip, "Palm", new Vector3(0, -0.1f, 0), new Vector3(1.1f, 1.0f, 1.3f), Vector3.zero, m.Glove);
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Capsule, grip, "Finger", new Vector3(-0.15f + i * 0.0f, -0.45f, 0.42f - i * 0.28f), new Vector3(0.38f, 0.42f, 0.38f), new Vector3(0, 0, 90), m.Glove);
            Prim(PrimitiveType.Capsule, grip, "Thumb", new Vector3(0.45f, 0.25f, 0.35f), new Vector3(0.36f, 0.4f, 0.36f), new Vector3(60, 0, 20), m.Glove);
            Prim(PrimitiveType.Cylinder, grip, "Cuff", new Vector3(0, 0.55f, -0.2f), new Vector3(1.0f, 0.18f, 1.0f), new Vector3(-20, 0, 0), m.Cuff);
            Prim(PrimitiveType.Cylinder, grip, "Arm", new Vector3(0, 3.6f, -1.3f), new Vector3(0.95f, 3.2f, 0.95f), new Vector3(-20, 0, 0), m.Sleeve);
            // キューの先の光
            var glow = Prim(PrimitiveType.Sphere, pivot, "TipGlow", new Vector3(0, 0, 0.05f), Vector3.one * 0.4f, Vector3.zero, m.Glow);

            // 前の手(台の上に置くブリッジ)と腕
            var bridge = Child(visual, "BridgeHand");
            Prim(PrimitiveType.Sphere, bridge, "Palm", new Vector3(0, 0.35f, -0.2f), new Vector3(1.7f, 0.6f, 1.6f), Vector3.zero, m.Glove);
            for (int i = 0; i < 4; i++)
            {
                float x = -0.9f + i * 0.45f;
                if (i >= 2) x += 0.15f;
                Prim(PrimitiveType.Capsule, bridge, "Finger", new Vector3(x, 0.2f, 0.75f), new Vector3(0.36f, 0.55f, 0.36f), new Vector3(80, (i - 1.5f) * 12.0f, 0), m.Glove);
            }
            // 親指と人差し指の付け根で作る、キューを乗せるV字の山
            Prim(PrimitiveType.Capsule, bridge, "Knuckle", new Vector3(-0.32f, 0.75f, 0.1f), new Vector3(0.42f, 0.38f, 0.42f), new Vector3(0, 0, 35), m.Glove);
            Prim(PrimitiveType.Capsule, bridge, "Thumb", new Vector3(0.32f, 0.75f, 0.1f), new Vector3(0.4f, 0.38f, 0.4f), new Vector3(0, 0, -35), m.Glove);
            Prim(PrimitiveType.Cylinder, bridge, "Cuff", new Vector3(0, 0.55f, -1.1f), new Vector3(1.1f, 0.2f, 1.1f), new Vector3(-75, 0, 0), m.Cuff);
            Prim(PrimitiveType.Cylinder, bridge, "Arm", new Vector3(0.0f, 1.6f, -4.6f), new Vector3(1.0f, 3.6f, 1.0f), new Vector3(-75, 0, 0), m.Sleeve);

            var so = new SerializedObject(cue);
            so.FindProperty("m_visualRoot").objectReferenceValue = visual.gameObject;
            so.FindProperty("m_cuePivot").objectReferenceValue = pivot;
            so.FindProperty("m_bridge").objectReferenceValue = bridge;
            so.FindProperty("m_tipGlow").objectReferenceValue = glow.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
            visual.gameObject.SetActive(false);
            return cue;
        }

        // Z軸方向の円柱(start: キューの先からの距離、length: 長さ。-Z へ伸ばす)
        static void CylZ(Transform parent, string name, float start, float length, float radius, Material mat)
        {
            Prim(PrimitiveType.Cylinder, parent, name, new Vector3(0, 0, -(start + length * 0.5f)), new Vector3(radius * 2, length * 0.5f, radius * 2), new Vector3(90, 0, 0), mat);
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
            void Rect(float x, float z, float w, float d) => Area(go.transform, prefab, ++n, x, z, TumikiSpawnArea.Shape.Rectangle, new Vector2(w, d), 0);
            void Circle(float x, float z, float r) => Area(go.transform, prefab, ++n, x, z, TumikiSpawnArea.Shape.Circle, Vector2.zero, r);
            // 手玉側(西)の左右、ラック側(東)の左右、中央、短い辺の前
            Rect(-6.0f, 5.0f, 6.0f, 3.5f);
            Rect(-6.0f, -5.0f, 6.0f, 3.5f);
            Rect(5.0f, 5.5f, 5.0f, 3.0f);
            Rect(5.0f, -5.5f, 5.0f, 3.0f);
            Circle(0.0f, 0.0f, 2.2f);
            Rect(-14.5f, 0.0f, 3.0f, 6.0f);
            Rect(15.0f, 0.0f, 2.5f, 6.0f);
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
        // 背景(ビリヤード場)
        //========================================

        static Transform BuildHall(Transform root, Mats m)
        {
            var hall = Child(root, "BilliardHall");
            float y = kHallFloorY;
            Prim(PrimitiveType.Cube, hall, "Carpet", new Vector3(0, y - 0.5f, 10), new Vector3(400, 1, 300), Vector3.zero, m.Carpet);
            // 絨毯の模様(台の下の一回り大きな敷物)
            Prim(PrimitiveType.Cube, hall, "Rug", new Vector3(0, y + 0.01f, 0), new Vector3(60, 0.02f, 40), Vector3.zero, m.Rug);
            Prim(PrimitiveType.Cube, hall, "RugBorder", new Vector3(0, y + 0.005f, 0), new Vector3(63, 0.02f, 43), Vector3.zero, m.WoodLight);

            // 奥の壁(板張り)と腰板
            Prim(PrimitiveType.Cube, hall, "BackWall", new Vector3(0, y + 45, 75), new Vector3(400, 110, 2), Vector3.zero, m.WallPaper);
            Prim(PrimitiveType.Cube, hall, "Wainscot", new Vector3(0, y + 6, 73.8f), new Vector3(400, 12, 0.6f), Vector3.zero, m.WoodDark);
            // キュー立て(壁に並ぶキュー)
            var rack = Child(hall, "CueRack");
            rack.localPosition = new Vector3(-30, y, 73);
            Prim(PrimitiveType.Cube, rack, "Board", new Vector3(0, 14, 0.2f), new Vector3(22, 1.2f, 1.2f), Vector3.zero, m.WoodLight);
            Prim(PrimitiveType.Cube, rack, "Base", new Vector3(0, 0.8f, -0.4f), new Vector3(22, 1.6f, 2.2f), Vector3.zero, m.WoodLight);
            for (int i = 0; i < 9; i++)
            {
                float x = -9.6f + i * 2.4f;
                Prim(PrimitiveType.Cylinder, rack, "Cue", new Vector3(x, 8.5f, -0.2f), new Vector3(0.45f, 8.0f, 0.45f), new Vector3(-4, 0, 0), i % 2 == 0 ? m.Maple : m.WoodDark);
                Prim(PrimitiveType.Cylinder, rack, "Grip", new Vector3(x, 2.6f, -0.6f), new Vector3(0.55f, 1.2f, 0.55f), new Vector3(-4, 0, 0), m.Wrap);
            }
            // 点数板(玉を通したそろばん)
            var board = Child(hall, "ScoreBoard");
            board.localPosition = new Vector3(28, y + 22, 73.5f);
            Prim(PrimitiveType.Cube, board, "Frame", Vector3.zero, new Vector3(26, 7, 0.8f), Vector3.zero, m.WoodLight);
            for (int row = 0; row < 2; row++)
            {
                float ry = row == 0 ? 1.4f : -1.4f;
                Prim(PrimitiveType.Cylinder, board, "Wire", new Vector3(0, ry, -0.5f), new Vector3(0.1f, 12, 0.1f), new Vector3(0, 0, 90), m.Pearl);
                for (int i = 0; i < 10; i++)
                {
                    float x = -10 + i * 1.3f + (i >= (row == 0 ? 6 : 3) ? 6.5f : 0);
                    Prim(PrimitiveType.Sphere, board, "Bead", new Vector3(x, ry, -0.5f), new Vector3(1.1f, 1.1f, 1.0f), Vector3.zero, row == 0 ? m.BallColor(kTypeColors[0]) : m.BallColor(kTypeColors[1]));
                }
            }
            // 壁の照明(光る板)
            foreach (float x in new[] { -70.0f, 0.0f, 70.0f })
            {
                Prim(PrimitiveType.Cube, hall, "WallLamp", new Vector3(x, y + 30, 73.5f), new Vector3(6, 3, 0.6f), Vector3.zero, m.LampGlow);
            }
            // 台の周りの丸いす・隣の台
            foreach (var p in new[] { new Vector3(-34, 0, 22), new Vector3(36, 0, 24), new Vector3(-40, 0, 2), new Vector3(42, 0, 6) })
            {
                Prim(PrimitiveType.Cylinder, hall, "StoolLeg", new Vector3(p.x, y + 2.5f, p.z), new Vector3(0.8f, 2.5f, 0.8f), Vector3.zero, m.Wrap);
                Prim(PrimitiveType.Cylinder, hall, "StoolSeat", new Vector3(p.x, y + 5.2f, p.z), new Vector3(3.6f, 0.4f, 3.6f), Vector3.zero, m.Leather);
            }
            var other = Child(hall, "OtherTable");
            other.localPosition = new Vector3(-70, y, 40);
            Prim(PrimitiveType.Cube, other, "Body", new Vector3(0, 7, 0), new Vector3(40, 3, 22), Vector3.zero, m.WoodDark);
            Prim(PrimitiveType.Cube, other, "Felt", new Vector3(0, 8.55f, 0), new Vector3(36, 0.1f, 18), Vector3.zero, m.Felt);
            foreach (float lx in new[] { -16.0f, 16.0f }) foreach (float lz in new[] { -8.0f, 8.0f })
                Prim(PrimitiveType.Cylinder, other, "Leg", new Vector3(lx, 2.8f, lz), new Vector3(2.4f, 2.8f, 2.4f), Vector3.zero, m.WoodDark);
            return hall;
        }

        //========================================
        // カメラ
        //========================================

        // 台の木枠の四隅がすべて余白付きで 16:9 の画面に収まるまで引いた、見下ろしのカメラ
        static void FrameCamera(Transform point, StageSwitcher switcher)
        {
            var so = new SerializedObject(switcher);
            var shared = so.FindProperty("m_camera").objectReferenceValue as Camera;
            float fov = shared != null ? shared.fieldOfView : 45.0f;
            const float pitch = 52.0f;
            var go = new GameObject("TempCamera");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = fov;
                cam.aspect = 16.0f / 9.0f;
                Quaternion rot = Quaternion.Euler(pitch, 0, 0);
                Vector3 target = new Vector3(0, 0, -0.6f);
                float ex = kHalfX + kRail + 1.6f, ez = kHalfZ + kRail + 1.6f;
                var corners = new List<Vector3>();
                foreach (float yy in new[] { 0.0f, 1.1f })
                {
                    corners.Add(new Vector3(-ex, yy, -ez));
                    corners.Add(new Vector3(ex, yy, -ez));
                    corners.Add(new Vector3(-ex, yy, ez));
                    corners.Add(new Vector3(ex, yy, ez));
                }
                const float margin = 0.03f;
                for (float d = 10; d < 200; d += 0.25f)
                {
                    cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * d, rot);
                    bool fits = true;
                    foreach (var c in corners)
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
        // マテリアル・メッシュ
        //========================================

        class Mats
        {
            public Material Felt, Cushion, Leather, Wood, WoodLight, WoodDark, Pearl, Line, PocketBlack, Chalk;
            public Material BallWhite, BallDot, Trail, Glow, Glove, Cuff, Sleeve, Maple, Wrap;
            public Material Carpet, Rug, WallPaper, LampGlow;
            readonly Dictionary<Color, Material> m_ballColors = new Dictionary<Color, Material>();

            public Mats()
            {
                Felt = Lit("Felt", new Color(0.06f, 0.46f, 0.24f), 0.05f);
                Cushion = SeeThrough("Cushion", new Color(0.05f, 0.38f, 0.2f));
                Leather = SeeThrough("Leather", new Color(0.16f, 0.09f, 0.06f));
                Wood = Lit("Wood", new Color(0.45f, 0.22f, 0.1f), 0.55f);
                WoodLight = Lit("WoodLight", new Color(0.72f, 0.45f, 0.22f), 0.5f);
                WoodDark = Lit("WoodDark", new Color(0.24f, 0.12f, 0.07f), 0.45f);
                Pearl = Lit("Pearl", new Color(0.95f, 0.93f, 0.85f), 0.8f);
                Line = Lit("Line", new Color(0.85f, 0.95f, 0.88f), 0.1f);
                PocketBlack = Lit("PocketBlack", new Color(0.02f, 0.02f, 0.02f), 0.1f);
                Chalk = Lit("Chalk", new Color(0.25f, 0.55f, 0.95f), 0.1f);
                BallWhite = Lit("BallWhite", new Color(0.97f, 0.96f, 0.9f), 0.9f);
                BallDot = Lit("BallDot", new Color(0.85f, 0.1f, 0.1f), 0.9f);
                Glove = Lit("Glove", new Color(0.98f, 0.98f, 0.96f), 0.3f);
                Cuff = Lit("Cuff", new Color(0.12f, 0.15f, 0.35f), 0.3f);
                Sleeve = Lit("Sleeve", new Color(0.12f, 0.12f, 0.14f), 0.25f);
                Maple = Lit("Maple", new Color(0.93f, 0.8f, 0.58f), 0.6f);
                Wrap = Lit("Wrap", new Color(0.08f, 0.08f, 0.09f), 0.3f);
                Carpet = Lit("Carpet", new Color(0.3f, 0.07f, 0.09f), 0.05f);
                Rug = Lit("Rug", new Color(0.12f, 0.22f, 0.3f), 0.05f);
                WallPaper = Lit("WallPaper", new Color(0.22f, 0.13f, 0.1f), 0.2f);
                LampGlow = Lit("LampGlow", new Color(1.0f, 0.85f, 0.55f), 0.2f, new Color(1.0f, 0.8f, 0.5f) * 2.0f);
                Trail = Particle("Trail", true);
                Glow = Particle("TipGlow", true);
            }

            public Material BallColor(Color c)
            {
                if (m_ballColors.TryGetValue(c, out var mat)) return mat;
                string n = $"Ball_{ColorUtility.ToHtmlStringRGB(c)}";
                mat = Lit(n, c, 0.9f);
                m_ballColors[c] = mat;
                return mat;
            }

            static Material Lit(string name, Color color, float smoothness, Color? emission = null)
            {
                var mat = LoadOrCreate(name, Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                if (emission.HasValue)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emission.Value);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                EditorUtility.SetDirty(mat);
                return mat;
            }

            // 壁(クッション)は他のステージと同じ「裏が透けて見える壁」の設定を写す
            static Material SeeThrough(string name, Color color)
            {
                string path = $"{kAssetDir}/Billiard_{name}.mat";
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

            static Material Particle(string name, bool additive)
            {
                string path = $"{kAssetDir}/Billiard_{name}.mat";
                var src = ToyKit.ParticleMaterial(additive, null);
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

            static Material LoadOrCreate(string name, Shader shader)
            {
                string path = $"{kAssetDir}/Billiard_{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                else if (mat.shader != shader) mat.shader = shader;
                return mat;
            }
        }

        static PhysicsMaterial BallPhysicsMaterial()
        {
            string path = $"{kAssetDir}/Billiard_Ball.physicMaterial";
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (mat == null)
            {
                mat = new PhysicsMaterial("Billiard_Ball");
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.dynamicFriction = 0.0f;
            mat.staticFriction = 0.0f;
            mat.bounciness = 0.88f;
            mat.frictionCombine = PhysicsMaterialCombine.Minimum;
            mat.bounceCombine = PhysicsMaterialCombine.Maximum;
            EditorUtility.SetDirty(mat);
            return mat;
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
            existing.vertices = mesh.vertices;
            existing.normals = mesh.normals;
            existing.triangles = mesh.triangles;
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        // 赤道のまわりの帯(緯度 -halfAngle〜+halfAngle の球面)
        static Mesh BuildSphereBand(float radius, float halfAngle)
        {
            return BuildSphereSection(radius, -halfAngle, halfAngle);
        }

        // +Y の極を中心にした丸い面(極から angle 度まで)
        static Mesh BuildSphereCap(float radius, float angle)
        {
            return BuildSphereSection(radius, 90.0f - angle, 90.0f);
        }

        static Mesh BuildSphereSection(float radius, float latMin, float latMax)
        {
            const int lon = 48, lat = 10;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            for (int i = 0; i <= lat; i++)
            {
                float a = Mathf.Lerp(latMin, latMax, (float)i / lat) * Mathf.Deg2Rad;
                for (int j = 0; j <= lon; j++)
                {
                    float b = j * Mathf.PI * 2.0f / lon;
                    var dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Sin(b));
                    v.Add(dir * radius);
                    n.Add(dir);
                }
            }
            var t = new List<int>();
            for (int i = 0; i < lat; i++)
            {
                for (int j = 0; j < lon; j++)
                {
                    int a0 = i * (lon + 1) + j, a1 = a0 + 1, b0 = a0 + lon + 1, b1 = b0 + 1;
                    t.Add(a0); t.Add(b0); t.Add(a1);
                    t.Add(a1); t.Add(b0); t.Add(b1);
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        //========================================
        // 共通
        //========================================

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

        static void MeshChild(Transform parent, string name, Mesh mesh, Vector3 euler, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(euler);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void SetArray<T>(SerializedProperty prop, List<T> items) where T : Object
        {
            prop.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
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
