using Nakahira;
using UnityEditor;
using UnityEngine;

namespace Nakagawa.EditorTools
{
    // 追加ギミック(せり上がり塀・加速床/減速床・移動床・ワープゲート・シューターレーン・回転砲台)の
    // メッシュ・マテリアル・プレハブを組み立て直す。保存先はすべて Assets/Nakagawa の中
    // (大きさ・色などの値はプレハブを置いた後にインスペクターで変えられる。見た目の部品の並びは各ギミックの ApplyShape が合わせる)
    public static class AdditionalGimmickBuilder
    {
        const string kRoot = "Assets/Nakagawa";
        const string kMeshDir = kRoot + "/Meshes";
        const string kMaterialDir = kRoot + "/Materials";
        const string kPrefabDir = kRoot + "/Prefabs";

        // 参照するだけのマテリアル
        const string kStageMaterial = "Assets/Nakahira/Player/Materials/Stage.mat";
        const string kAdditiveTemplate = kMaterialDir + "/EffectAdditive.mat";
        const string kAlphaTemplate = kMaterialDir + "/GimmickParticle.mat";

        // プレハブの名前(StageBuilder から置くときにも使う)
        public const string kRisingWall = "RisingWall";
        public const string kAccelerateFloor = "SpeedFloor_Accelerate";
        public const string kDecelerateFloor = "SpeedFloor_Decelerate";
        public const string kMovingFloor = "MovingFloor";
        public const string kWarpGate = "WarpGate";
        public const string kShooterLane = "ShooterLane";
        public const string kCannon = "TumikiCannon";

        public static readonly string[] kAllPrefabs = { kRisingWall, kAccelerateFloor, kDecelerateFloor, kMovingFloor, kWarpGate, kShooterLane, kCannon };

        struct Meshes
        {
            public Mesh Quad, Torus, Swirl, Chevron, Ring, Disc;
        }

        struct Glow
        {
            public Material Additive, Alpha;
        }

        // プレハブがすべてそろっているか
        public static bool PrefabsExist()
        {
            foreach (var name in kAllPrefabs)
            {
                if (GimmickTestFieldBuilder.LoadPrefab(name) == null) return false;
            }
            return true;
        }

        // ないプレハブだけを作り、あるプレハブは参照の修復だけを行う(インスペクターで調整した値はそのまま残る)
        [MenuItem("Nakagawa/Gimmick/Build Additional Gimmick Prefabs")]
        public static void BuildAll() => Build(false);

        // すべてのプレハブを作り直す(調整した値も既定に戻る。シーンに置いたギミックの上書きが外れることがあるので、ステージも作り直す)
        [MenuItem("Nakagawa/Gimmick/Rebuild Additional Gimmick Prefabs (Reset Values)")]
        public static void RebuildAll() => Build(true);

        static void Build(bool rebuild)
        {
            GimmickTestFieldBuilder.EnsureFolder(kMeshDir);
            GimmickTestFieldBuilder.EnsureFolder(kMaterialDir);
            GimmickTestFieldBuilder.EnsureFolder(kPrefabDir);

            var meshes = new Meshes
            {
                Quad = SaveMesh(GimmickMeshes.BuildQuad()),
                Torus = SaveMesh(GimmickMeshes.BuildTorus()),
                Swirl = SaveMesh(GimmickMeshes.BuildSwirl()),
                Chevron = SaveMesh(GimmickMeshes.BuildChevron()),
                Ring = SaveMesh(GimmickMeshes.BuildRing()),
                Disc = SaveMesh(GimmickMeshes.BuildDisc()),
            };
            var glow = new Glow
            {
                Additive = GlowMaterial("GimmickGlow", kAdditiveTemplate),
                Alpha = GlowMaterial("GimmickGlowAlpha", kAlphaTemplate),
            };

            bool Need(string name) => rebuild || GimmickTestFieldBuilder.LoadPrefab(name) == null;
            if (Need(kRisingWall)) BuildRisingWall();
            if (Need(kAccelerateFloor)) BuildSpeedFloor(kAccelerateFloor, SpeedFloorGimmick.Mode.Accelerate, meshes, glow);
            if (Need(kDecelerateFloor)) BuildSpeedFloor(kDecelerateFloor, SpeedFloorGimmick.Mode.Decelerate, meshes, glow);
            if (Need(kMovingFloor)) BuildMovingFloor();
            if (Need(kWarpGate)) BuildWarpGate(meshes, glow);
            if (Need(kShooterLane)) BuildShooterLane(meshes, glow);
            if (Need(kCannon)) BuildCannon(meshes, glow);

            RepairAll();
            AssetDatabase.SaveAssets();
            Debug.Log($"[AdditionalGimmickBuilder] 追加ギミックのプレハブを{(rebuild ? "作り直しました" : "そろえました(あるものは参照の修復のみ)")}");
        }

        //========================================
        // 参照の修復
        //========================================

        // 見た目の部品への参照が外れていたら、部品の名前からつなぎ直す(値はそのまま)
        // プレハブを編集中に作り直すと、古い内容のまま保存されて参照が外れることがあるため
        [MenuItem("Nakagawa/Gimmick/Repair Additional Gimmick Prefabs")]
        public static void RepairAll()
        {
            var repaired = new System.Collections.Generic.List<string>();
            Repair(kRisingWall, root => Wire(root, typeof(RisingWallGimmick),
                ("m_panel", "Panel", typeof(Transform)),
                ("m_edges", "Edge_North|Edge_South|Edge_East|Edge_West", typeof(Renderer))), repaired);
            foreach (var name in new[] { kAccelerateFloor, kDecelerateFloor })
            {
                Repair(name, root => Wire(root, typeof(SpeedFloorGimmick),
                    ("m_base", "Base", typeof(Renderer)),
                    ("m_rings", "Ring#", typeof(Renderer))), repaired);
            }
            Repair(kMovingFloor, root => Wire(root, typeof(MovingFloorGimmick),
                ("m_visual", "Visual", typeof(Transform))), repaired);
            Repair(kWarpGate, root => Wire(root, typeof(WarpGateGimmick),
                ("m_ring", "Ring", typeof(Transform)),
                ("m_ringRenderer", "Ring/Hoop", typeof(Renderer)),
                ("m_swirl", "Swirl", typeof(Renderer)),
                ("m_faceMarks", "FaceMark#", typeof(GameObject)),
                ("m_blockMarks", "BlockMark#", typeof(GameObject))), repaired);
            Repair(kShooterLane, root => Wire(root, typeof(ShooterLaneGimmick),
                ("m_belt", "Belt", typeof(Transform)),
                ("m_rollers", "Roller#", typeof(Transform)),
                ("m_lip", "Lip", typeof(Transform)),
                ("m_arrows", "Arrow#", typeof(Renderer))), repaired);
            Repair(kCannon, root => Wire(root, typeof(TumikiCannon),
                ("m_turret", "Turret", typeof(Transform)),
                ("m_barrel", "Turret/Barrel", typeof(Transform)),
                ("m_muzzleGlow", "Turret/MuzzleGlow", typeof(Renderer)),
                ("m_guideDashes", "Turret/GuideDash#", typeof(Renderer))), repaired);

            if (repaired.Count > 0) Debug.Log($"[AdditionalGimmickBuilder] 外れていた参照をつなぎ直しました: {string.Join(", ", repaired)}");
        }

        static void Repair(string name, System.Func<GameObject, bool> wire, System.Collections.Generic.List<string> repaired)
        {
            string path = $"{kPrefabDir}/{name}.prefab";
            if (Load<GameObject>(path) == null) return;
            // 編集中のプレハブは、閉じたときに古い内容で保存し直されてしまうので触らない
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == path)
            {
                Debug.LogWarning($"[AdditionalGimmickBuilder] {name} を編集中なので修復しませんでした。プレハブの編集を閉じてからもう一度実行してください");
                return;
            }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!wire(root)) return;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                repaired.Add(name);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // fields: (フィールド名, 部品の場所, 部品の型)。場所の「#」は 0, 1, 2 … と続く部品の並び(配列)、「|」は名前の並び(配列)
        // 外れている(null を含む・空の)ものだけをつなぎ直し、つなぎ直したら true
        static bool Wire(GameObject root, System.Type component, params (string field, string path, System.Type type)[] fields)
        {
            var c = root.GetComponent(component);
            if (c == null) return false;
            var so = new SerializedObject(c);
            bool changed = false;
            foreach (var (field, path, type) in fields)
            {
                var prop = so.FindProperty(field);
                if (prop == null) continue;
                if (!prop.isArray)
                {
                    if (prop.objectReferenceValue != null) continue;
                    var obj = Part(root.transform, path, type);
                    if (obj == null) continue;
                    prop.objectReferenceValue = obj;
                    changed = true;
                    continue;
                }

                bool broken = prop.arraySize == 0;
                for (int i = 0; i < prop.arraySize; i++) broken |= prop.GetArrayElementAtIndex(i).objectReferenceValue == null;
                if (!broken) continue;
                var parts = new System.Collections.Generic.List<Object>();
                if (path.Contains("|"))
                {
                    foreach (var p in path.Split('|')) parts.Add(Part(root.transform, p, type));
                }
                else
                {
                    for (int i = 0; ; i++)
                    {
                        var obj = Part(root.transform, path.Replace("#", i.ToString()), type);
                        if (obj == null) break;
                        parts.Add(obj);
                    }
                }
                if (parts.Count == 0) continue;
                SetArray(prop, parts.ToArray());
                changed = true;
            }
            if (!changed) return false;
            so.ApplyModifiedPropertiesWithoutUndo();
            // 大きさ・並びを合わせ直す
            c.GetType().GetMethod("ApplyShape")?.Invoke(c, null);
            return true;
        }

        static Object Part(Transform root, string path, System.Type type)
        {
            Transform t = root.Find(path);
            if (t == null) return null;
            if (type == typeof(Transform)) return t;
            if (type == typeof(GameObject)) return t.gameObject;
            return t.GetComponent(type);
        }

        //========================================
        // せり上がり塀
        //========================================

        static void BuildRisingWall()
        {
            var root = new GameObject(kRisingWall);
            root.AddComponent<Rigidbody>().isKinematic = true;

            // 当たり判定(壁として積み木を反射し、幽霊も通さない)。大きさは RisingWallGimmick が合わせる
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<BoxCollider>();
            body.AddComponent<ReflectWall>();

            // 床と同じ見た目のパネル
            var panel = GimmickTestFieldBuilder.Visual(PrimitiveType.Cube, "Panel", root.transform, Vector3.zero, Vector3.zero, Vector3.one, Load<Material>(kStageMaterial));

            var edgeMat = GimmickTestFieldBuilder.OpaqueMaterial("RisingWallEdge", new Color(0.35f, 0.33f, 0.3f));
            var edges = new Renderer[4];
            string[] names = { "Edge_North", "Edge_South", "Edge_East", "Edge_West" };
            for (int i = 0; i < 4; i++)
            {
                var e = GimmickTestFieldBuilder.Visual(PrimitiveType.Cube, names[i], panel.transform.parent, Vector3.zero, Vector3.zero, Vector3.one, edgeMat);
                // 縁はパネルと一緒に上下する(パネルの子にすると大きさが掛かるので、root の子で上面に置く)
                edges[i] = e.GetComponent<Renderer>();
            }

            var gimmick = root.AddComponent<RisingWallGimmick>();
            var so = new SerializedObject(gimmick);
            so.FindProperty("m_panel").objectReferenceValue = panel.transform;
            var edgesProp = so.FindProperty("m_edges");
            edgesProp.arraySize = edges.Length;
            for (int i = 0; i < edges.Length; i++) edgesProp.GetArrayElementAtIndex(i).objectReferenceValue = edges[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            gimmick.ApplyShape();

            GimmickTestFieldBuilder.SavePrefab(root, kRisingWall);
        }

        //========================================
        // 加速床 / 減速床
        //========================================

        static void BuildSpeedFloor(string name, SpeedFloorGimmick.Mode mode, Meshes meshes, Glow glow)
        {
            var root = new GameObject(name);
            var floor = MeshVisual("Base", root.transform, meshes.Quad, glow.Additive);
            var rings = new Renderer[3];
            for (int i = 0; i < rings.Length; i++) rings[i] = MeshVisual($"Ring{i}", root.transform, meshes.Ring, glow.Additive);

            var gimmick = root.AddComponent<SpeedFloorGimmick>();
            var so = new SerializedObject(gimmick);
            so.FindProperty("m_mode").enumValueIndex = (int)mode;
            so.FindProperty("m_base").objectReferenceValue = floor;
            var ringsProp = so.FindProperty("m_rings");
            ringsProp.arraySize = rings.Length;
            for (int i = 0; i < rings.Length; i++) ringsProp.GetArrayElementAtIndex(i).objectReferenceValue = rings[i];
            var particle = Load<Material>(kMaterialDir + "/GimmickParticle.mat");
            so.FindProperty("m_decelerateEffect.Material").objectReferenceValue = particle;
            so.ApplyModifiedPropertiesWithoutUndo();
            gimmick.ApplyShape();

            GimmickTestFieldBuilder.SavePrefab(root, name);
        }

        //========================================
        // 移動床(木のパレット)
        //========================================

        static void BuildMovingFloor()
        {
            var root = new GameObject(kMovingFloor);
            root.AddComponent<Rigidbody>().isKinematic = true;
            root.AddComponent<BoxCollider>();

            var wood = GimmickTestFieldBuilder.OpaqueMaterial("PalletWood", new Color(0.82f, 0.64f, 0.42f));
            var woodDark = GimmickTestFieldBuilder.OpaqueMaterial("PalletWoodDark", new Color(0.6f, 0.44f, 0.28f));

            // 1×1×1 の箱に収まる形で作り、MovingFloorGimmick が大きさに合わせて拡大する
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            for (int i = 0; i < 5; i++)
            {
                float z = Mathf.Lerp(-0.4f, 0.4f, i / 4.0f);
                GimmickTestFieldBuilder.Visual(PrimitiveType.Cube, $"Board{i}", visual, new Vector3(0, 0.35f, z), Vector3.zero, new Vector3(1.0f, 0.3f, 0.17f), wood);
            }
            for (int i = 0; i < 3; i++)
            {
                float x = Mathf.Lerp(-0.42f, 0.42f, i / 2.0f);
                GimmickTestFieldBuilder.Visual(PrimitiveType.Cube, $"Stringer{i}", visual, new Vector3(x, -0.15f, 0), Vector3.zero, new Vector3(0.14f, 0.7f, 1.0f), woodDark);
            }

            var gimmick = root.AddComponent<MovingFloorGimmick>();
            var so = new SerializedObject(gimmick);
            so.FindProperty("m_visual").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
            gimmick.ApplyShape();

            GimmickTestFieldBuilder.SavePrefab(root, kMovingFloor);
        }

        //========================================
        // ワープゲート
        //========================================

        static void BuildWarpGate(Meshes meshes, Glow glow)
        {
            var root = new GameObject(kWarpGate);

            var ring = new GameObject("Ring").transform;
            ring.SetParent(root.transform, false);
            var ringRenderer = MeshVisual("Hoop", ring, meshes.Torus, GimmickTestFieldBuilder.OpaqueMaterial("WarpRing", Color.white));
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var swirl = MeshVisual("Swirl", root.transform, meshes.Swirl, glow.Additive);
            swirl.transform.localPosition = Vector3.up * 0.015f;
            // 下地(渦の後ろを暗くして、穴のように見せる)
            var hole = MeshVisual("Hole", ring, meshes.Disc, GlowMaterial("WarpHole", kAlphaTemplate, new Color(0.05f, 0.03f, 0.12f, 0.85f)));
            hole.transform.localPosition = Vector3.up * 0.008f;
            hole.transform.localScale = new Vector3(0.95f, 1.0f, 0.95f);

            var faceMat = GimmickTestFieldBuilder.OpaqueMaterial("WarpMarkFace", new Color(1.0f, 0.97f, 0.9f));
            var eyeMat = GimmickTestFieldBuilder.OpaqueMaterial("WarpMarkEye", new Color(0.1f, 0.1f, 0.12f));
            var blockMat = GimmickTestFieldBuilder.OpaqueMaterial("WarpMarkBlock", new Color(0.95f, 0.8f, 0.45f));
            var faces = new GameObject[4];
            var blocks = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                // 顔マーク: 白い玉に目を2つ(上から見える)
                var face = GimmickTestFieldBuilder.Visual(PrimitiveType.Sphere, $"FaceMark{i}", root.transform, Vector3.zero, Vector3.zero, new Vector3(0.34f, 0.2f, 0.34f), faceMat);
                GimmickTestFieldBuilder.Visual(PrimitiveType.Sphere, "EyeL", face.transform, new Vector3(-0.2f, 0.42f, 0.12f), Vector3.zero, new Vector3(0.18f, 0.3f, 0.18f), eyeMat);
                GimmickTestFieldBuilder.Visual(PrimitiveType.Sphere, "EyeR", face.transform, new Vector3(0.2f, 0.42f, 0.12f), Vector3.zero, new Vector3(0.18f, 0.3f, 0.18f), eyeMat);
                faces[i] = face;
                // 積み木マーク: 小さな円柱
                blocks[i] = GimmickTestFieldBuilder.Visual(PrimitiveType.Cylinder, $"BlockMark{i}", root.transform, Vector3.zero, Vector3.zero, new Vector3(0.3f, 0.1f, 0.3f), blockMat);
            }

            var gimmick = root.AddComponent<WarpGateGimmick>();
            var so = new SerializedObject(gimmick);
            so.FindProperty("m_ring").objectReferenceValue = ring;
            so.FindProperty("m_ringRenderer").objectReferenceValue = ringRenderer;
            so.FindProperty("m_swirl").objectReferenceValue = swirl;
            SetArray(so.FindProperty("m_faceMarks"), faces);
            SetArray(so.FindProperty("m_blockMarks"), blocks);
            so.ApplyModifiedPropertiesWithoutUndo();
            gimmick.ApplyShape();

            GimmickTestFieldBuilder.SavePrefab(root, kWarpGate);
        }

        //========================================
        // シューターレーン
        //========================================

        static void BuildShooterLane(Meshes meshes, Glow glow)
        {
            var root = new GameObject(kShooterLane);
            var belt = GimmickTestFieldBuilder.Visual(PrimitiveType.Cube, "Belt", root.transform, Vector3.zero, Vector3.zero, Vector3.one,
                GimmickTestFieldBuilder.OpaqueMaterial("LaneBelt", new Color(0.22f, 0.22f, 0.25f)));
            var rollerMat = GimmickTestFieldBuilder.OpaqueMaterial("LaneRoller", new Color(0.7f, 0.72f, 0.75f));
            var rollers = new Transform[7];
            for (int i = 0; i < rollers.Length; i++)
            {
                rollers[i] = GimmickTestFieldBuilder.Visual(PrimitiveType.Cylinder, $"Roller{i}", root.transform, Vector3.zero, Vector3.zero, Vector3.one, rollerMat).transform;
            }
            var lip = GimmickTestFieldBuilder.Visual(PrimitiveType.Cube, "Lip", root.transform, Vector3.zero, Vector3.zero, Vector3.one,
                GimmickTestFieldBuilder.OpaqueMaterial("LaneLip", new Color(0.95f, 0.75f, 0.3f)));
            var arrows = new Renderer[3];
            for (int i = 0; i < arrows.Length; i++) arrows[i] = MeshVisual($"Arrow{i}", root.transform, meshes.Chevron, glow.Additive);

            var gimmick = root.AddComponent<ShooterLaneGimmick>();
            var so = new SerializedObject(gimmick);
            so.FindProperty("m_belt").objectReferenceValue = belt.transform;
            SetArray(so.FindProperty("m_rollers"), rollers);
            so.FindProperty("m_lip").objectReferenceValue = lip.transform;
            SetArray(so.FindProperty("m_arrows"), arrows);
            so.ApplyModifiedPropertiesWithoutUndo();
            gimmick.ApplyShape();

            GimmickTestFieldBuilder.SavePrefab(root, kShooterLane);
        }

        //========================================
        // 回転砲台
        //========================================

        static void BuildCannon(Meshes meshes, Glow glow)
        {
            var root = new GameObject(kCannon);
            root.AddComponent<Rigidbody>().isKinematic = true;
            root.AddComponent<CapsuleCollider>();

            // 土台: 積み木を2段重ねたもの(積み木の色)
            GimmickTestFieldBuilder.Visual(PrimitiveType.Cylinder, "Base0", root.transform, new Vector3(0, 0.25f, 0), Vector3.zero, new Vector3(1.6f, 0.25f, 1.6f),
                GimmickTestFieldBuilder.OpaqueMaterial("CannonBaseRed", TumikiColorUtil.ToUnityColor(TumikiColor.Red)));
            GimmickTestFieldBuilder.Visual(PrimitiveType.Cylinder, "Base1", root.transform, new Vector3(0, 0.75f, 0), Vector3.zero, new Vector3(1.4f, 0.25f, 1.4f),
                GimmickTestFieldBuilder.OpaqueMaterial("CannonBaseBlue", TumikiColorUtil.ToUnityColor(TumikiColor.Blue)));

            // 水平に回る部分(床の高さを回転の中心にする。ガイド線もこの子)
            var turret = new GameObject("Turret").transform;
            turret.SetParent(root.transform, false);
            var bodyMat = GimmickTestFieldBuilder.OpaqueMaterial("CannonBody", new Color(0.25f, 0.25f, 0.3f));
            GimmickTestFieldBuilder.Visual(PrimitiveType.Sphere, "Body", turret, new Vector3(0, 1.15f, 0), Vector3.zero, new Vector3(0.85f, 0.7f, 0.85f), bodyMat);

            var barrel = new GameObject("Barrel").transform;
            barrel.SetParent(turret, false);
            barrel.localPosition = new Vector3(0, 1.2f, 0.1f);
            var barrelMat = GimmickTestFieldBuilder.OpaqueMaterial("CannonBarrel", new Color(0.35f, 0.35f, 0.42f));
            GimmickTestFieldBuilder.Visual(PrimitiveType.Cylinder, "Tube", barrel, new Vector3(0, 0, 0.42f), new Vector3(90, 0, 0), new Vector3(0.42f, 0.42f, 0.42f), barrelMat);
            GimmickTestFieldBuilder.Visual(PrimitiveType.Cylinder, "MuzzleRing", barrel, new Vector3(0, 0, 0.82f), new Vector3(90, 0, 0), new Vector3(0.52f, 0.06f, 0.52f),
                GimmickTestFieldBuilder.OpaqueMaterial("CannonMuzzle", new Color(0.95f, 0.75f, 0.3f)));

            var glowDisc = MeshVisual("MuzzleGlow", turret, meshes.Disc, glow.Additive);
            glowDisc.transform.localPosition = new Vector3(0, 1.25f, 1.05f);

            var dashes = new Renderer[8];
            for (int i = 0; i < dashes.Length; i++) dashes[i] = MeshVisual($"GuideDash{i}", turret, meshes.Quad, glow.Alpha);

            var cannon = root.AddComponent<TumikiCannon>();
            var so = new SerializedObject(cannon);
            so.FindProperty("m_turret").objectReferenceValue = turret;
            so.FindProperty("m_barrel").objectReferenceValue = barrel;
            so.FindProperty("m_muzzleGlow").objectReferenceValue = glowDisc;
            SetArray(so.FindProperty("m_guideDashes"), dashes);
            so.ApplyModifiedPropertiesWithoutUndo();
            cannon.ApplyShape();

            GimmickTestFieldBuilder.SavePrefab(root, kCannon);
        }

        //========================================
        // 補助
        //========================================

        // メッシュを表示するだけの部品(当たり判定・影なし)
        static MeshRenderer MeshVisual(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        static void SetArray(SerializedProperty prop, Object[] values)
        {
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        // メッシュをアセットとして保存する(既にあれば中身だけ入れ替えて、参照を保つ)
        static Mesh SaveMesh(Mesh mesh)
        {
            string path = $"{kMeshDir}/{mesh.name}.asset";
            var existing = Load<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = mesh.name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // 光る部品のマテリアル(テクスチャなし。頂点カラーの透明度で形を作り、両面を描く)
        static Material GlowMaterial(string name, string templatePath) => GlowMaterial(name, templatePath, Color.white);

        static Material GlowMaterial(string name, string templatePath, Color color)
        {
            var mat = GimmickTestFieldBuilder.DerivedMaterial(name, templatePath, color);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
    }
}
