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

            EditorSceneManager.SaveScene(combined, kCombinedPath);
            AssetDatabase.Refresh();
            Debug.Log($"[StageCombiner] {stages.Count}ステージを {kCombinedPath} にまとめました\n" + string.Join("\n", report));
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
