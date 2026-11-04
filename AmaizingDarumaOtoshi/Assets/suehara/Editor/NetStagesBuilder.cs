using System.Collections.Generic;
using System.IO;
using Nakahira;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// NetStages(ネット対戦のテスト用シーン)を、今の AllStages から作り直す。
// NetStages は AllStages の複製なので、AllStages にステージや設定が足されたら、これで取り込む。
//  1. 今の NetStages を一時的にひな形として取っておく
//  2. NetStages の中身を AllStages で置き換える(シーンの GUID は NetStages のまま。ビルド設定もそのまま)
//  3. ひな形から、ネット対戦用のルート(AllStages に無いもの: NetworkManager・NetLobbyUI・NetStageSync・NetMatch)を写す。
//     写したものがひな形のシーンのものを参照していたら、同じ場所(名前の並び)にある新しいシーンのものにつなぎ直す
//  4. 試合(DarumaMatch)を非表示にする(接続したら NetMatch が表示する)
//  5. 積み木・キャラクター・幽霊のプレハブへの参照を、ネット用(*_Net)に差し替える
// AllStages 側の値(ステージ・カメラ・試合の設定など)はすべて AllStages のものになる。
public static class NetStagesBuilder
{
    const string kAllStages = "Assets/Nakagawa/Stages/AllStages.unity";
    const string kNetStages = "Assets/suehara/NetStages.unity";
    const string kTemplate = "Assets/suehara/_NetStagesTemplate.unity";

    // ローカル用プレハブ → ネット用プレハブ
    static readonly Dictionary<string, string> kNetPrefabs = new Dictionary<string, string>
    {
        { "Assets/Nakahira/Player/Prefabs/Tumiki.prefab", "Assets/suehara/Network/Tumiki_Net.prefab" },
        { "Assets/Nakahira/Player/Prefabs/DarumaCharacter.prefab", "Assets/suehara/Network/DarumaCharacter_Net.prefab" },
        { "Assets/Nakahira/Player/Prefabs/DarumaGhost.prefab", "Assets/suehara/Network/DarumaGhost_Net.prefab" },
    };

    [MenuItem("Tools/ネット対戦/NetStages を AllStages から作り直す")]
    static void RebuildFromMenu()
    {
        if (!EditorUtility.DisplayDialog("NetStages を作り直す",
            "NetStages を今の AllStages で置き換え、ネット対戦用の部品を入れ直します。\nNetStages だけに入れた変更(ネット用の部品以外)は消えます。", "作り直す", "やめる")) return;
        Rebuild();
    }

    // 作り直した内容をログに出し、まとめを返す
    public static string Rebuild()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "保存されていないシーンがあるので、やめました";
        var log = new System.Text.StringBuilder();

        // 1. ひな形を取っておく
        AssetDatabase.DeleteAsset(kTemplate);
        if (!AssetDatabase.CopyAsset(kNetStages, kTemplate)) return "NetStages をひな形として写せませんでした";

        try
        {
            // 2. 中身を AllStages で置き換える(.meta はそのままなので GUID は変わらない)
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            File.Copy(kAllStages, kNetStages, true);
            AssetDatabase.ImportAsset(kNetStages, ImportAssetOptions.ForceUpdate);

            var scene = EditorSceneManager.OpenScene(kNetStages, OpenSceneMode.Single);
            var template = EditorSceneManager.OpenScene(kTemplate, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            // 3. ネット対戦用のルートを写す
            var names = new HashSet<string>();
            foreach (var root in scene.GetRootGameObjects()) names.Add(root.name);
            var copied = new List<GameObject>();
            foreach (var root in template.GetRootGameObjects())
            {
                if (names.Contains(root.name)) continue;
                var copy = Object.Instantiate(root);
                copy.name = root.name;
                SceneManager.MoveGameObjectToScene(copy, scene);
                copied.Add(copy);
                log.AppendLine($"写した: {root.name}");
            }
            foreach (var copy in copied) Relink(copy, template, scene, log);
            EditorSceneManager.CloseScene(template, true);

            // 4. 試合は接続するまで出さない
            var match = Object.FindFirstObjectByType<DarumaMatch>(FindObjectsInactive.Include);
            if (match != null) match.gameObject.SetActive(false);
            else log.AppendLine("注意: DarumaMatch が見つかりません");

            // 5. ネット用プレハブに差し替える
            int swapped = SwapPrefabs(scene);
            log.AppendLine($"ネット用プレハブに差し替えた参照: {swapped}");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // シーンに置いた NetworkObject の識別番号(GlobalObjectIdHash)は、保存してシーン内の番号が決まってから計算し直す
            // (ひな形のシーンの値のままだと、クライアントでホストのものと対応が取れない)
            int rehashed = RefreshNetworkObjectHashes();
            log.AppendLine($"識別番号を計算し直した NetworkObject: {rehashed}");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            AssetDatabase.DeleteAsset(kTemplate);
        }

        Debug.Log("[NetStagesBuilder] NetStages を AllStages から作り直しました\n" + log);
        return log.ToString();
    }

    // 写したもののうち、ひな形のシーンを参照しているものを、新しいシーンの同じ場所のものにつなぎ直す
    static void Relink(GameObject copy, Scene template, Scene scene, System.Text.StringBuilder log)
    {
        foreach (var component in copy.GetComponentsInChildren<Component>(true))
        {
            if (component == null) continue;
            var so = new SerializedObject(component);
            var it = so.GetIterator();
            bool changed = false;
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                var target = it.objectReferenceValue;
                var go = target is GameObject g ? g : target is Component c ? c.gameObject : null;
                if (go == null || go.scene != template) continue;

                var found = FindByPath(scene, PathOf(go.transform));
                Object replacement = found == null ? null : target is GameObject ? found : (Object)found.GetComponent(target.GetType());
                it.objectReferenceValue = replacement;
                changed = true;
                log.AppendLine($"つなぎ直した: {component.GetType().Name}.{it.propertyPath} → {(replacement != null ? PathOf(found.transform) : "(見つからないので空)")}");
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static int SwapPrefabs(Scene scene)
    {
        var map = new Dictionary<GameObject, GameObject>();
        foreach (var pair in kNetPrefabs)
        {
            var local = AssetDatabase.LoadAssetAtPath<GameObject>(pair.Key);
            var net = AssetDatabase.LoadAssetAtPath<GameObject>(pair.Value);
            if (local != null && net != null) map[local] = net;
            else Debug.LogWarning($"[NetStagesBuilder] プレハブが見つかりません: {pair.Key} / {pair.Value}");
        }

        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var so = new SerializedObject(component);
                var it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    var target = it.objectReferenceValue;
                    var go = target is GameObject g ? g : target is Component c ? c.gameObject : null;
                    if (go == null || !EditorUtility.IsPersistent(go) || !map.TryGetValue(go.transform.root.gameObject, out var net)) continue;
                    it.objectReferenceValue = target is GameObject ? net : (Object)net.GetComponent(target.GetType());
                    changed = true;
                    count++;
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        return count;
    }

    // NetworkObject.OnValidate(NGO がエディタで識別番号を決める処理)を呼ぶ。変わった数を返す
    static int RefreshNetworkObjectHashes()
    {
        const System.Reflection.BindingFlags f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        var onValidate = typeof(Unity.Netcode.NetworkObject).GetMethod("OnValidate", f);
        if (onValidate == null)
        {
            Debug.LogWarning("[NetStagesBuilder] NetworkObject.OnValidate が見つかりません。NetStages を開いて NetMatch などの NetworkObject を一度触ってから保存してください");
            return 0;
        }
        int changed = 0;
        foreach (var no in Object.FindObjectsByType<Unity.Netcode.NetworkObject>(FindObjectsInactive.Include))
        {
            uint before = new SerializedObject(no).FindProperty("GlobalObjectIdHash").uintValue;
            onValidate.Invoke(no, null);
            if (new SerializedObject(no).FindProperty("GlobalObjectIdHash").uintValue != before) changed++;
            EditorUtility.SetDirty(no);
        }
        return changed;
    }

    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

    static GameObject FindByPath(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            Transform t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
            if (t != null) return t.gameObject;
        }
        return null;
    }
}
