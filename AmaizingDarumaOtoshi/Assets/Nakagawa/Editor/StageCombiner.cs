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
            // ステージのシーンがないステージ(まとめたシーンにだけあるステージ)は作り直すと消えるので確認する
            var missing = StagesWithoutScene(stagePaths);
            if (missing.Count > 0 && !EditorUtility.DisplayDialog("AllStages を作り直す",
                    $"次のステージは {kStageDir} にシーンがないため、作り直すと AllStages から消えます。\n{string.Join(", ", missing)}\n\n" +
                    "ステージを足すだけなら「Add Stage Scenes To AllStages (Keep Others)」を使ってください。続けますか?", "作り直す", "やめる"))
            {
                return;
            }

            Scene combined = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var stages = new List<StageEntry>();
            var report = new List<string>();
            var shared = new Shared();
            int spawnPointCount = 0;

            foreach (var path in stagePaths)
            {
                var entry = ImportStage(path, combined, shared, out int pointCount, out string line);
                spawnPointCount = Mathf.Max(spawnPointCount, pointCount);
                stages.Add(entry);
                report.Add(line);
            }

            DarumaMatch match = shared.Match;
            Camera camera = shared.Camera;
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

        // ステージのフォルダにあるステージのシーンを、今のまとめたシーンに足す(同じ名前のステージは入れ替える)
        // まとめたシーンにしかないステージ・手で直した共有の物(試合・カメラ・ライトなど)はそのまま残す
        [MenuItem("Nakagawa/Stages/Add Stage Scenes To AllStages (Keep Others)")]
        public static void AddAllStageScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AddOrUpdateStages(StageScenePaths());
        }

        // 指定したステージのシーンを、まとめたシーンに足す・入れ替える(呼ぶ前に開いているシーンの保存を確認しておく)
        public static void AddOrUpdateStages(List<string> stagePaths)
        {
            if (stagePaths == null || stagePaths.Count == 0) return;
            AddOrUpdateStages(stagePaths.Select(Path.GetFileNameWithoutExtension).ToList(),
                id => EditorSceneManager.OpenScene(stagePaths.First(p => Path.GetFileNameWithoutExtension(p) == id), OpenSceneMode.Additive));
        }

        // ids のステージを、まとめたシーンに足す・入れ替える
        // openSource: id のステージの中身が入ったシーンを開いて返す(ステージのシーンを開く・その場で組み立てる など)。取り込んだら閉じる
        public static void AddOrUpdateStages(List<string> ids, System.Func<string, Scene> openSource)
        {
            if (ids == null || ids.Count == 0) return;
            if (!File.Exists(kCombinedPath))
            {
                Debug.LogWarning($"[StageCombiner] {kCombinedPath} がないので、すべてのステージをまとめて作ります");
                Build();
                return;
            }

            Scene combined = EditorSceneManager.OpenScene(kCombinedPath, OpenSceneMode.Single);
            var switcher = FindInScene<StageSwitcher>(combined);
            if (switcher == null)
            {
                Debug.LogError($"[StageCombiner] {kCombinedPath} に StageSwitcher がありません");
                return;
            }
            var so = new SerializedObject(switcher);
            var stagesProp = so.FindProperty("m_stages");
            var stages = new List<StageEntry>();
            for (int i = 0; i < stagesProp.arraySize; i++)
            {
                var e = stagesProp.GetArrayElementAtIndex(i).objectReferenceValue as StageEntry;
                if (e != null) stages.Add(e);
            }
            var sharedPointsProp = so.FindProperty("m_spawnPoints");
            var sharedPoints = new Transform[sharedPointsProp.arraySize];
            for (int i = 0; i < sharedPoints.Length; i++) sharedPoints[i] = sharedPointsProp.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            var sharedArea = so.FindProperty("m_stageArea").objectReferenceValue as StageArea;
            var camera = so.FindProperty("m_camera").objectReferenceValue as Camera;

            // 共有の物はまとめたシーンにある物を使う(ステージのシーンにある物は捨てる)
            var shared = new Shared
            {
                Match = so.FindProperty("m_match").objectReferenceValue as DarumaMatch,
                Camera = camera,
                Light = FindInScene<Light>(combined),
                Toggle = FindInScene<TumikiImpactModeToggle>(combined),
                CatchEffect = FindInScene<TumikiCatchEffect>(combined),
            };

            var added = new List<StageEntry>();
            var report = new List<string>();
            foreach (var id in ids)
            {
                // 同じ名前のステージは入れ替える(並び順はそのまま)
                int index = stages.FindIndex(s => s.name == id);
                if (index >= 0)
                {
                    Object.DestroyImmediate(stages[index].gameObject);
                    stages.RemoveAt(index);
                }
                Scene src = openSource(id);
                SceneManager.SetActiveScene(combined);
                var entry = ImportStage(src, id, combined, shared, out int pointCount, out string line);
                if (pointCount > sharedPoints.Length) Debug.LogWarning($"[StageCombiner] {id} の出現位置({pointCount})が共有の出現ポイント({sharedPoints.Length})より多いので、余りは使われません");
                if (index >= 0) stages.Insert(index, entry);
                else stages.Add(entry);
                added.Add(entry);
                report.Add(line);
                foreach (var spawner in entry.GetComponentsInChildren<TumikiSpawner>(true)) SetMatch(spawner, shared.Match);
            }
            // 本編(Stage01, Stage02, ...)、デバッグステージ(DebugStage01, ...)の順に並べる
            SortStages(stages);

            stagesProp.arraySize = stages.Count;
            for (int i = 0; i < stages.Count; i++) stagesProp.GetArrayElementAtIndex(i).objectReferenceValue = stages[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            ApplyWallHeight(added);
            StageBuilder.SetupCameraAndDebugTools(combined);

            // 足したステージだけ写真を撮り、最初のステージに戻す
            if (camera != null && sharedArea != null)
            {
                // 足したステージ以外が写り込まないよう、先にすべて隠す
                foreach (var s in stages) s.gameObject.SetActive(false);
                CapturePreviews(added, sharedPoints, sharedArea, camera);
                for (int i = 0; i < stages.Count; i++) stages[i].gameObject.SetActive(i == 0);
                ApplySharedToStage(stages[0], sharedPoints, sharedArea, camera);
            }

            EditorSceneManager.SaveScene(combined);
            AssetDatabase.Refresh();
            Debug.Log($"[StageCombiner] {added.Count}ステージを {kCombinedPath} に足しました(全{stages.Count}ステージ)\n" + string.Join("\n", report));
        }

        // まとめたシーンで全ステージが共有する物(最初に見つけた物を使い、2つ目以降は捨てる)
        class Shared
        {
            public DarumaMatch Match;
            public Camera Camera;
            public Light Light;
            public TumikiImpactModeToggle Toggle;
            public TumikiCatchEffect CatchEffect;
        }

        // まとめたシーンのステージの id(名前)を変える。呼ぶ前に開いているシーンの保存を確認しておく
        //  ・ステージのルートの名前と StageInfo の id
        //  ・ステージの写真(StagePreviews/<id>.png。GUID はそのままなので、参照は切れない)
        //  ・ステージセレクトの StageData の id(Assets/Nakagawa にあるものはファイル名も)
        // 入れ替え(A→B と B→C を同時に行う など)でも名前がぶつからないよう、一時的な名前を経由する
        public static void RenameStages(IList<(string from, string to)> renames)
        {
            Scene combined = SceneManager.GetSceneByPath(kCombinedPath);
            if (!combined.IsValid() || !combined.isLoaded) combined = EditorSceneManager.OpenScene(kCombinedPath, OpenSceneMode.Single);

            const string kTemp = "__rename_";
            foreach (var (from, to) in renames) RenameOne(combined, from, kTemp + to);
            foreach (var (_, to) in renames) RenameOne(combined, kTemp + to, to);

            // 並び順を直す
            var switcher = FindInScene<StageSwitcher>(combined);
            if (switcher != null)
            {
                var so = new SerializedObject(switcher);
                var prop = so.FindProperty("m_stages");
                var stages = new List<StageEntry>();
                for (int i = 0; i < prop.arraySize; i++)
                {
                    if (prop.GetArrayElementAtIndex(i).objectReferenceValue is StageEntry e) stages.Add(e);
                }
                SortStages(stages);
                prop.arraySize = stages.Count;
                for (int i = 0; i < stages.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = stages[i];
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.SaveScene(combined);
            AssetDatabase.SaveAssets();
            Debug.Log($"[StageCombiner] {renames.Count}ステージの名前を変えました\n" + string.Join("\n", renames.Select(r => $"{r.from} → {r.to}")));
        }

        static void RenameOne(Scene combined, string from, string to)
        {
            foreach (var root in combined.GetRootGameObjects())
            {
                var entry = root.GetComponent<StageEntry>();
                if (entry == null || root.name != from) continue;
                root.name = to;
                if (entry.Info != null)
                {
                    var so = new SerializedObject(entry.Info);
                    so.FindProperty("m_id").stringValue = to;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            string preview = $"{kPreviewDir}/{from}.png";
            if (AssetDatabase.LoadMainAssetAtPath(preview) != null)
            {
                string error = AssetDatabase.MoveAsset(preview, $"{kPreviewDir}/{to}.png");
                if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"[StageCombiner] 写真の名前を変えられません: {error}");
            }

            foreach (var guid in AssetDatabase.FindAssets("t:StageData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<StageData>(path);
                if (data == null || data.id != from) continue;
                data.id = to;
                EditorUtility.SetDirty(data);
                if (path.StartsWith("Assets/Nakagawa/") && Path.GetFileNameWithoutExtension(path) == from) AssetDatabase.RenameAsset(path, to);
            }
        }

        // 本編のステージを先に、デバッグステージ(名前が DebugStage で始まる)を後に、それぞれ名前の順に並べる
        public static void SortStages(List<StageEntry> stages)
        {
            stages.Sort((a, b) =>
            {
                bool da = a.name.StartsWith(StageBuilder.kDebugPrefix), db = b.name.StartsWith(StageBuilder.kDebugPrefix);
                if (da != db) return da ? 1 : -1;
                return string.CompareOrdinal(a.name, b.name);
            });
        }

        static StageEntry ImportStage(string path, Scene combined, Shared shared, out int pointCount, out string report)
        {
            Scene src = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            return ImportStage(src, Path.GetFileNameWithoutExtension(path), combined, shared, out pointCount, out report);
        }

        // ステージ1つ分のシーン(src)の中身を、まとめたシーンの「id」の子へ移し、src を閉じる
        // ステージ固有の物(床・ギミック・生成エリア・TumikiSpawner・出現位置・StageInfo・背景)は移し、共有の物は shared になければ移す
        static StageEntry ImportStage(Scene src, string id, Scene combined, Shared shared, out int pointCount, out string report)
        {
            var root = new GameObject(id);
            SceneManager.MoveGameObjectToScene(root, combined);
            var entry = root.AddComponent<StageEntry>();

            StageInfo info = null;
            StageArea area = null;
            Transform spawnRoot = null;
            Transform cameraPoint = null;
            StageBackdrop backdrop = null;
            var spawners = new List<TumikiSpawner>();

            foreach (var go in src.GetRootGameObjects())
            {
                // 試合・ライト・カメラは最初のステージの物を共有し、残りは捨てる(カメラは位置だけ残す)
                var srcMatch = go.GetComponent<DarumaMatch>();
                if (srcMatch != null)
                {
                    if (shared.Match == null) shared.Match = MoveToRoot(srcMatch, combined);
                    else Object.DestroyImmediate(go);
                    continue;
                }
                var srcCamera = go.GetComponent<Camera>();
                if (srcCamera != null)
                {
                    cameraPoint = new GameObject("CameraPoint").transform;
                    cameraPoint.SetParent(root.transform, false);
                    cameraPoint.SetPositionAndRotation(go.transform.position, go.transform.rotation);
                    if (shared.Camera == null) shared.Camera = MoveToRoot(srcCamera, combined);
                    else Object.DestroyImmediate(go);
                    continue;
                }
                var srcLight = go.GetComponent<Light>();
                if (srcLight != null && srcLight.type == LightType.Directional)
                {
                    if (shared.Light == null) shared.Light = MoveToRoot(srcLight, combined);
                    else Object.DestroyImmediate(go);
                    continue;
                }
                // デバッグ用の切り替えは全ステージで1つ(ステージごとにあると切り替え状態が揃わない)
                var srcToggle = go.GetComponent<TumikiImpactModeToggle>();
                if (srcToggle != null)
                {
                    if (shared.Toggle == null) shared.Toggle = MoveToRoot(srcToggle, combined);
                    else Object.DestroyImmediate(go);
                    continue;
                }
                // 演出(成功エフェクト)も全ステージで1つ
                var srcEffect = go.GetComponent<TumikiCatchEffect>();
                if (srcEffect != null)
                {
                    if (shared.CatchEffect == null) shared.CatchEffect = MoveToRoot(srcEffect, combined);
                    else Object.DestroyImmediate(go);
                    continue;
                }

                SceneManager.MoveGameObjectToScene(go, combined);
                go.transform.SetParent(root.transform, true);

                if (info == null) info = go.GetComponentInChildren<StageInfo>(true);
                spawners.AddRange(go.GetComponentsInChildren<TumikiSpawner>(true));
                // 背景はステージと一緒に表示が切り替わる。紐づけはプレハブで覚えておく(Apply Backdrops で置き直せる)
                if (backdrop == null && go.TryGetComponent<StageBackdrop>(out var b)) backdrop = b;
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
            pointCount = points.Count;
            entry.Set(info, points.ToArray(), area, cameraPoint);
            if (backdrop != null) entry.SetBackdrop(PrefabUtility.GetCorrespondingObjectFromSource(backdrop));
            report = $"{entry.DisplayName}: 出現位置{points.Count} / 生成エリア{root.GetComponentsInChildren<TumikiSpawnArea>(true).Length} / TumikiSpawner{spawners.Count}" +
                     (backdrop != null ? $" / 背景 {backdrop.Id}" : "");
            return entry;
        }

        static List<string> StageScenePaths()
        {
            return Directory.GetFiles(kStageDir, "Stage*.unity")
                .Select(p => p.Replace('\\', '/'))
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .ToList();
        }

        // 今のまとめたシーンにあって、ステージのシーンがないステージの名前
        static List<string> StagesWithoutScene(List<string> stagePaths)
        {
            var result = new List<string>();
            if (!File.Exists(kCombinedPath)) return result;
            var names = new HashSet<string>(stagePaths.Select(Path.GetFileNameWithoutExtension));
            Scene scene = SceneManager.GetSceneByPath(kCombinedPath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(kCombinedPath, OpenSceneMode.Additive);
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<StageEntry>() != null && !names.Contains(root.name)) result.Add(root.name);
            }
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
            return result;
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var c = root.GetComponentInChildren<T>(true);
                if (c != null) return c;
            }
            return null;
        }

        // まとめたシーンの壁(ReflectWall)の高さを変える。底は床の高さのまま、上に伸ばす
        // (各ステージのシーンの壁はそのまま)
        // 伸ばした後、床から StageWallSplitter.kVisibleHeight までの見える壁と、その上の見えない壁(InvWall)に分ける
        static void ApplyWallHeight(List<StageEntry> stages)
        {
            int count = 0;
            foreach (var stage in stages)
            {
                // 既に分けてある壁は、見えない壁を取り除いてから伸ばし直す
                StageWallSplitter.RemoveInvisible(stage.transform);
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
                StageWallSplitter.Split(stage.transform);
            }
            Debug.Log($"[StageCombiner] 壁 {count} 枚の高さ(Yスケール)を {kWallHeight} にし、高さ {StageWallSplitter.kVisibleHeight} から上を見えない壁にしました");
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
