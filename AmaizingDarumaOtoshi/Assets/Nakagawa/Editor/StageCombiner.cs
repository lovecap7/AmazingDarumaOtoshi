using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nakahira;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nakagawa.EditorTools
{
    // Assets/Nakagawa/Stages の各ステージ(StageXX.unity)を、1つのシーン(AllStages.unity)にまとめる
    // 各ステージのシーンを開いて中身をそのまま移すので、ステージのシーンを手で直した内容も引き継ぐ(元のシーンは変更しない)
    //  ・ステージ固有の物(床・ギミック・生成エリア・TumikiSpawner・出現位置・StageInfo)は「StageXX」の子にまとめる
    //  ・試合(Match)・ライト・カメラ・デバッグ用の切り替えは最初のステージの物を全ステージで共有する
    //  ・StageSwitcher が左右キーでステージを切り替える
    public static class StageCombiner
    {
        const string kStageDir = "Assets/Nakagawa/Stages";
        const string kCombinedPath = kStageDir + "/AllStages.unity";
        public const string kPreviewDir = "Assets/Nakagawa/StagePreviews";
        const int kPreviewWidth = 760;
        const int kPreviewHeight = 480;
        // まとめたシーンの壁の高さ(Yスケール)
        const float kWallHeight = 5.0f;

        [MenuItem("Nakagawa/Stages/Build Combined Scene (AllStages)")]
        public static void Build()
        {
            var stagePaths = Directory.GetFiles(kStageDir, "Stage*.unity")
                .Select(p => p.Replace('\\', '/'))
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .ToList();
            if (stagePaths.Count == 0)
            {
                Debug.LogWarning($"[StageCombiner] {kStageDir} にステージのシーンがありません");
                return;
            }
            // 開いているシーンを閉じるので、未保存の変更があれば確認する
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene combined = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var stages = new List<StageEntry>();
            var report = new List<string>();
            DarumaMatch match = null;
            Camera camera = null;
            Light light = null;
            TumikiImpactModeToggle toggle = null;
            TumikiCatchEffect catchEffect = null;
            int spawnPointCount = 0;

            foreach (var path in stagePaths)
            {
                Scene src = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                string id = Path.GetFileNameWithoutExtension(path);
                var root = new GameObject(id);
                SceneManager.MoveGameObjectToScene(root, combined);
                var entry = root.AddComponent<StageEntry>();

                StageInfo info = null;
                StageArea area = null;
                Transform spawnRoot = null;
                Transform cameraPoint = null;
                var spawners = new List<TumikiSpawner>();

                foreach (var go in src.GetRootGameObjects())
                {
                    // 試合・ライト・カメラは最初のステージの物を共有し、残りは捨てる(カメラは位置だけ残す)
                    var srcMatch = go.GetComponent<DarumaMatch>();
                    if (srcMatch != null)
                    {
                        if (match == null) match = MoveToRoot(srcMatch, combined);
                        else Object.DestroyImmediate(go);
                        continue;
                    }
                    var srcCamera = go.GetComponent<Camera>();
                    if (srcCamera != null)
                    {
                        cameraPoint = new GameObject("CameraPoint").transform;
                        cameraPoint.SetParent(root.transform, false);
                        cameraPoint.SetPositionAndRotation(go.transform.position, go.transform.rotation);
                        if (camera == null) camera = MoveToRoot(srcCamera, combined);
                        else Object.DestroyImmediate(go);
                        continue;
                    }
                    var srcLight = go.GetComponent<Light>();
                    if (srcLight != null && srcLight.type == LightType.Directional)
                    {
                        if (light == null) light = MoveToRoot(srcLight, combined);
                        else Object.DestroyImmediate(go);
                        continue;
                    }
                    // デバッグ用の切り替えは全ステージで1つ(ステージごとにあると切り替え状態が揃わない)
                    var srcToggle = go.GetComponent<TumikiImpactModeToggle>();
                    if (srcToggle != null)
                    {
                        if (toggle == null) toggle = MoveToRoot(srcToggle, combined);
                        else Object.DestroyImmediate(go);
                        continue;
                    }
                    // 演出(成功エフェクト)も全ステージで1つ
                    var srcEffect = go.GetComponent<TumikiCatchEffect>();
                    if (srcEffect != null)
                    {
                        if (catchEffect == null) catchEffect = MoveToRoot(srcEffect, combined);
                        else Object.DestroyImmediate(go);
                        continue;
                    }

                    SceneManager.MoveGameObjectToScene(go, combined);
                    go.transform.SetParent(root.transform, true);

                    if (info == null) info = go.GetComponentInChildren<StageInfo>(true);
                    spawners.AddRange(go.GetComponentsInChildren<TumikiSpawner>(true));
                    var srcArea = go.GetComponent<StageArea>();
                    if (srcArea != null && area == null)
                    {
                        // 範囲は共有のStageAreaへ写す見本として残す(有効だと FindAnyObjectByType 等で見つかってしまうので無効にする)
                        area = srcArea;
                        go.name = "StageArea_Template";
                        go.SetActive(false);
                    }
                    if (go.name == "SpawnPoints") spawnRoot = go.transform;
                }
                EditorSceneManager.CloseScene(src, true);

                var points = new List<Transform>();
                if (spawnRoot != null)
                {
                    foreach (Transform t in spawnRoot) points.Add(t);
                }
                spawnPointCount = Mathf.Max(spawnPointCount, points.Count);
                entry.Set(info, points.ToArray(), area, cameraPoint);
                stages.Add(entry);
                report.Add($"{entry.DisplayName}: 出現位置{points.Count} / 生成エリア{root.GetComponentsInChildren<TumikiSpawnArea>(true).Length} / TumikiSpawner{spawners.Count}");
            }

            if (match == null || camera == null)
            {
                Debug.LogError("[StageCombiner] ステージのシーンに Match(DarumaMatch) か Main Camera がありません");
                return;
            }

            // 共有の出現ポイントとステージ範囲を作り、試合に登録する
            var sharedSpawnRoot = new GameObject("SpawnPoints").transform;
            var sharedPoints = new Transform[Mathf.Max(1, spawnPointCount)];
            for (int i = 0; i < sharedPoints.Length; i++)
            {
                sharedPoints[i] = new GameObject($"SpawnPoint{i + 1}").transform;
                sharedPoints[i].SetParent(sharedSpawnRoot, false);
            }
            var sharedArea = new GameObject("StageArea").AddComponent<StageArea>();
            if (stages[0].StageArea != null) EditorUtility.CopySerialized(stages[0].StageArea, sharedArea);

            var matchSo = new SerializedObject(match);
            var pointsProp = matchSo.FindProperty("m_spawnPoints");
            pointsProp.arraySize = sharedPoints.Length;
            for (int i = 0; i < sharedPoints.Length; i++) pointsProp.GetArrayElementAtIndex(i).objectReferenceValue = sharedPoints[i];
            matchSo.FindProperty("m_stageArea").objectReferenceValue = sharedArea;
            matchSo.ApplyModifiedPropertiesWithoutUndo();

            foreach (var entry in stages)
            {
                foreach (var spawner in entry.GetComponentsInChildren<TumikiSpawner>(true)) SetMatch(spawner, match);
            }

            var switcher = new GameObject("StageSwitcher").AddComponent<StageSwitcher>();
            var so = new SerializedObject(switcher);
            var stagesProp = so.FindProperty("m_stages");
            stagesProp.arraySize = stages.Count;
            for (int i = 0; i < stages.Count; i++) stagesProp.GetArrayElementAtIndex(i).objectReferenceValue = stages[i];
            so.FindProperty("m_match").objectReferenceValue = match;
            var switcherPoints = so.FindProperty("m_spawnPoints");
            switcherPoints.arraySize = sharedPoints.Length;
            for (int i = 0; i < sharedPoints.Length; i++) switcherPoints.GetArrayElementAtIndex(i).objectReferenceValue = sharedPoints[i];
            so.FindProperty("m_stageArea").objectReferenceValue = sharedArea;
            so.FindProperty("m_camera").objectReferenceValue = camera;
            so.ApplyModifiedPropertiesWithoutUndo();

            // エディタ上でも最初のステージだけが見えるようにしておく
            for (int i = 0; i < stages.Count; i++) stages[i].gameObject.SetActive(i == 0);
            ApplySharedToStage(stages[0], sharedPoints, sharedArea, camera);
            // ステージのシーンに付いていなければ、プレイヤーを追うカメラとデバッグ用の切り替えを付ける
            StageBuilder.SetupCameraAndDebugTools(combined);
            ApplyWallHeight(stages);

            // ステージセレクト用の写真を撮る(撮り終わったら最初のステージに戻す)
            CapturePreviews(stages, sharedPoints, sharedArea, camera);
            for (int i = 0; i < stages.Count; i++) stages[i].gameObject.SetActive(i == 0);
            ApplySharedToStage(stages[0], sharedPoints, sharedArea, camera);

            EditorSceneManager.SaveScene(combined, kCombinedPath);
            AssetDatabase.Refresh();
            Debug.Log($"[StageCombiner] {stages.Count}ステージを {kCombinedPath} にまとめました\n" + string.Join("\n", report));
        }

        // まとめたシーンの壁(ReflectWall)の高さを変える。底は床の高さのまま、上に伸ばす
        // (各ステージのシーンの壁はそのまま)
        static void ApplyWallHeight(List<StageEntry> stages)
        {
            int count = 0;
            foreach (var stage in stages)
            {
                foreach (var wall in stage.GetComponentsInChildren<ReflectWall>(true))
                {
                    Transform t = wall.transform;
                    Vector3 scale = t.localScale;
                    Vector3 pos = t.localPosition;
                    float bottom = pos.y - scale.y * 0.5f;
                    scale.y = kWallHeight;
                    pos.y = bottom + kWallHeight * 0.5f;
                    t.localScale = scale;
                    t.localPosition = pos;
                    count++;
                }
            }
            Debug.Log($"[StageCombiner] 壁 {count} 枚の高さ(Yスケール)を {kWallHeight} にしました");
        }

        // 各ステージを、そのステージのカメラ位置から撮って PNG で保存し、スプライトとして読み込めるようにする
        // (ステージセレクトの StageData.preview に使う。Fujihara > ステージ一覧を更新 で設定される)
        public static void CapturePreviews(List<StageEntry> stages, Transform[] sharedPoints, StageArea sharedArea, Camera camera)
        {
            if (!AssetDatabase.IsValidFolder(kPreviewDir)) AssetDatabase.CreateFolder(Path.GetDirectoryName(kPreviewDir).Replace('\\', '/'), Path.GetFileName(kPreviewDir));

            var rt = new RenderTexture(kPreviewWidth, kPreviewHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(kPreviewWidth, kPreviewHeight, TextureFormat.RGB24, false);
            var prevTarget = camera.targetTexture;
            var prevActive = RenderTexture.active;
            var paths = new List<string>();
            try
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    for (int j = 0; j < stages.Count; j++) stages[j].gameObject.SetActive(j == i);
                    ApplySharedToStage(stages[i], sharedPoints, sharedArea, camera);

                    camera.targetTexture = rt;
                    camera.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, kPreviewWidth, kPreviewHeight), 0, 0);
                    tex.Apply();

                    string id = stages[i].Info != null && !string.IsNullOrEmpty(stages[i].Info.Id) ? stages[i].Info.Id : stages[i].name;
                    string path = $"{kPreviewDir}/{id}.png";
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    paths.Add(path);
                }
            }
            finally
            {
                camera.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }

            AssetDatabase.Refresh();
            foreach (var path in paths)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer == null || importer.textureType == TextureImporterType.Sprite) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            Debug.Log($"[StageCombiner] ステージの写真を {paths.Count} 枚撮りました({kPreviewDir})");
        }

        static T MoveToRoot<T>(T component, Scene scene) where T : Component
        {
            SceneManager.MoveGameObjectToScene(component.gameObject, scene);
            return component;
        }

        static void SetMatch(TumikiSpawner spawner, DarumaMatch match)
        {
            var so = new SerializedObject(spawner);
            so.FindProperty("m_match").objectReferenceValue = match;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 実行時に StageSwitcher が行うのと同じ配置を、エディタ上の見た目のために行う
        static void ApplySharedToStage(StageEntry stage, Transform[] sharedPoints, StageArea sharedArea, Camera camera)
        {
            var points = stage.SpawnPoints;
            if (points != null && points.Length > 0)
            {
                for (int i = 0; i < sharedPoints.Length; i++)
                {
                    Transform src = points[i % points.Length];
                    sharedPoints[i].SetPositionAndRotation(src.position, src.rotation);
                }
            }
            if (stage.StageArea != null)
            {
                sharedArea.transform.SetPositionAndRotation(stage.StageArea.transform.position, stage.StageArea.transform.rotation);
            }
            if (stage.CameraPoint != null)
            {
                camera.transform.SetPositionAndRotation(stage.CameraPoint.position, stage.CameraPoint.rotation);
            }
        }
    }
}
