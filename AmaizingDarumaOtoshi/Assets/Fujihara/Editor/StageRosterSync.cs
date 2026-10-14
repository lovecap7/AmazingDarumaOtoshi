using System.Collections.Generic;
using System.IO;
using Nakagawa;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Nakagawa のステージから、ステージセレクトの一覧（SelectRoster.stages）を作り直す。
// メニュー「Fujihara > ステージ一覧を更新」。ステージが増えたら実行し直す。
//  ・ステージは全ステージをまとめた AllStages の StageSwitcher に登録された順に並べる
//    （AllStages がなければ、Assets/Nakagawa/Stages の StageXX シーンから読む）
//  ・各ステージの StageInfo から id・名前・コンセプトを読み、StageData を作る / 更新する
//    （既にある StageData は id で探して使う。新しく作るものは NewDataFolder に置く）
//  ・遊ぶシーンは全ステージをまとめた AllStages（StageData.sceneName）。ステージセレクトで決まったら AllStages へ移動し、
//    StageSwitcher が id のステージに切り替える
//  ・Nakagawa が撮ったステージの写真（Assets/Nakagawa/StagePreviews/<id>.png）があれば、プレビュー画像にする
//    （手で別の画像を設定していればそのまま残す）。色はそのまま残す
public static class StageRosterSync
{
    const string StageFolder = "Assets/Nakagawa/Stages";
    const string CombinedSceneName = "AllStages";
    const string CombinedScenePath = StageFolder + "/" + CombinedSceneName + ".unity";
    const string PreviewFolder = "Assets/Nakagawa/StagePreviews";
    // 新しく作る StageData の置き場所
    const string NewDataFolder = "Assets/Nakagawa/StageData";
    const string RosterPath = "Assets/Fujihara/Data/SelectRoster.asset";

    // 新しく作るステージの色（淡い色を順番に）
    static readonly Color[] Colors =
    {
        new Color(0.95f, 0.75f, 0.60f), new Color(0.60f, 0.80f, 0.95f), new Color(0.70f, 0.90f, 0.65f),
        new Color(0.85f, 0.70f, 0.95f), new Color(0.98f, 0.88f, 0.55f), new Color(0.95f, 0.65f, 0.70f),
    };

    struct StageSource
    {
        public string Id, Title, Concept;
    }

    [MenuItem("Fujihara/ステージ一覧を更新")]
    public static void Sync()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("再生中は更新できません"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var roster = AssetDatabase.LoadAssetAtPath<SelectRoster>(RosterPath);
        if (roster == null) { Debug.LogError("SelectRoster が見つかりません：" + RosterPath); return; }

        var sources = File.Exists(CombinedScenePath) ? ReadCombinedScene() : ReadStageScenes();
        if (sources.Count == 0) { Debug.LogWarning("[StageRosterSync] ステージが見つからないので、一覧は変えません"); return; }

        var existing = FindExistingData();
        var stages = new List<StageData>();
        foreach (var s in sources)
        {
            stages.Add(CreateOrUpdate(s, existing, CombinedSceneName, stages.Count));
        }

        roster.stages = stages;
        EditorUtility.SetDirty(roster);
        AssetDatabase.SaveAssets();
        Debug.Log($"[StageRosterSync] ステージセレクトの一覧を {stages.Count} ステージで更新しました");
    }

    // まとめたシーン（AllStages）の StageSwitcher に登録された順に読む
    static List<StageSource> ReadCombinedScene()
    {
        var list = new List<StageSource>();
        Scene scene = SceneManager.GetSceneByPath(CombinedScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(CombinedScenePath, OpenSceneMode.Additive);

        StageSwitcher switcher = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            switcher = root.GetComponentInChildren<StageSwitcher>(true);
            if (switcher != null) break;
        }
        if (switcher != null)
        {
            var prop = new SerializedObject(switcher).FindProperty("m_stages");
            for (int i = 0; i < prop.arraySize; i++)
            {
                var entry = prop.GetArrayElementAtIndex(i).objectReferenceValue as StageEntry;
                if (entry != null) list.Add(ToSource(entry.Info, entry.name));
            }
        }

        if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        return list;
    }

    // ステージ1つ分のシーン（StageXX）から読む
    static List<StageSource> ReadStageScenes()
    {
        var list = new List<StageSource>();
        var paths = new List<string>();
        foreach (var g in AssetDatabase.FindAssets("t:Scene", new[] { StageFolder }))
        {
            // ステージ1つ分のシーン（StageXX）だけ。まとめたシーン（AllStages）は除く
            var p = AssetDatabase.GUIDToAssetPath(g);
            string name = Path.GetFileNameWithoutExtension(p);
            if (name.StartsWith("Stage") && name != CombinedSceneName) paths.Add(p);
        }
        paths.Sort();   // Stage01, Stage02, ... の順

        foreach (var path in paths)
        {
            // シーンを開かずに読むため、追加で開いて読んだらすぐ閉じる
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            StageInfo info = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                info = root.GetComponentInChildren<StageInfo>(true);
                if (info != null) break;
            }
            list.Add(ToSource(info, Path.GetFileNameWithoutExtension(path)));
            EditorSceneManager.CloseScene(scene, true);
        }
        return list;
    }

    static StageSource ToSource(StageInfo info, string fallbackName)
    {
        return new StageSource
        {
            Id = info != null && !string.IsNullOrEmpty(info.Id) ? info.Id : fallbackName,
            Title = info != null && !string.IsNullOrEmpty(info.Title) ? info.Title : fallbackName,
            Concept = info != null ? info.Concept : "",
        };
    }

    // プロジェクトにある StageData（id → データ）。id が空ならファイル名で探す
    static Dictionary<string, StageData> FindExistingData()
    {
        var dict = new Dictionary<string, StageData>();
        foreach (var g in AssetDatabase.FindAssets("t:StageData"))
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var data = AssetDatabase.LoadAssetAtPath<StageData>(path);
            if (data == null) continue;
            string id = !string.IsNullOrEmpty(data.id) ? data.id : Path.GetFileNameWithoutExtension(path);
            if (!dict.ContainsKey(id)) dict.Add(id, data);
        }
        return dict;
    }

    static StageData CreateOrUpdate(StageSource s, Dictionary<string, StageData> existing, string sceneName, int index)
    {
        if (!existing.TryGetValue(s.Id, out var data))
        {
            EnsureFolder(NewDataFolder);
            data = ScriptableObject.CreateInstance<StageData>();
            data.color = Colors[index % Colors.Length];
            AssetDatabase.CreateAsset(data, $"{NewDataFolder}/{s.Id}.asset");
            existing[s.Id] = data;
        }
        data.id = s.Id;
        data.displayName = s.Title;
        data.description = s.Concept;
        data.sceneName = sceneName;

        // ステージの写真をプレビュー画像にする（未設定か、前に自動で設定した写真のときだけ）
        var photo = AssetDatabase.LoadAssetAtPath<Sprite>($"{PreviewFolder}/{s.Id}.png");
        if (photo != null && (data.preview == null || AssetDatabase.GetAssetPath(data.preview).StartsWith(PreviewFolder)))
        {
            data.preview = photo;
        }
        EditorUtility.SetDirty(data);
        return data;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
