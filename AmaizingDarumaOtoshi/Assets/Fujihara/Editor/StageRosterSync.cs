using System.Collections.Generic;
using System.IO;
using Nakagawa;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Nakagawa のステージ（Assets/Nakagawa/Stages の各シーン）から、ステージセレクトの一覧（SelectRoster.stages）を作り直す。
// メニュー「Fujihara > ステージ一覧を更新」。ステージが増えたら実行し直す。
//  ・各シーンの StageInfo から id・名前・コンセプトを読み、StageData（Assets/Fujihara/Data/Stages/<id>.asset）を作る / 更新する
//  ・シーン名を StageData.sceneName に入れる（ステージセレクトで決まったらそのシーンへ移動する）
//  ・既にある StageData の色・プレビュー画像はそのまま残す
public static class StageRosterSync
{
    const string StageFolder = "Assets/Nakagawa/Stages";
    const string DataFolder = "Assets/Fujihara/Data/Stages";
    const string RosterPath = "Assets/Fujihara/Data/SelectRoster.asset";

    // 新しく作るステージの色（淡い色を順番に）
    static readonly Color[] Colors =
    {
        new Color(0.95f, 0.75f, 0.60f), new Color(0.60f, 0.80f, 0.95f), new Color(0.70f, 0.90f, 0.65f),
        new Color(0.85f, 0.70f, 0.95f), new Color(0.98f, 0.88f, 0.55f), new Color(0.95f, 0.65f, 0.70f),
    };

    [MenuItem("Fujihara/ステージ一覧を更新")]
    public static void Sync()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("再生中は更新できません"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var roster = AssetDatabase.LoadAssetAtPath<SelectRoster>(RosterPath);
        if (roster == null) { Debug.LogError("SelectRoster が見つかりません：" + RosterPath); return; }

        var stages = new List<StageData>();
        var guids = AssetDatabase.FindAssets("t:Scene", new[] { StageFolder });
        var paths = new List<string>();
        foreach (var g in guids) paths.Add(AssetDatabase.GUIDToAssetPath(g));
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
            string sceneName = Path.GetFileNameWithoutExtension(path);
            string id = info != null && !string.IsNullOrEmpty(info.Id) ? info.Id : sceneName;
            string title = info != null && !string.IsNullOrEmpty(info.Title) ? info.Title : sceneName;
            string concept = info != null ? info.Concept : "";
            EditorSceneManager.CloseScene(scene, true);

            stages.Add(CreateOrUpdate(id, title, concept, sceneName, stages.Count));
        }

        roster.stages = stages;
        EditorUtility.SetDirty(roster);
        AssetDatabase.SaveAssets();
        Debug.Log($"[StageRosterSync] ステージセレクトの一覧を {stages.Count} ステージで更新しました");
    }

    static StageData CreateOrUpdate(string id, string title, string concept, string sceneName, int index)
    {
        if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets/Fujihara/Data", "Stages");
        string path = $"{DataFolder}/{id}.asset";
        var data = AssetDatabase.LoadAssetAtPath<StageData>(path);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<StageData>();
            data.color = Colors[index % Colors.Length];
            AssetDatabase.CreateAsset(data, path);
        }
        data.id = id;
        data.displayName = title;
        data.description = concept;
        data.sceneName = sceneName;
        EditorUtility.SetDirty(data);
        return data;
    }
}
