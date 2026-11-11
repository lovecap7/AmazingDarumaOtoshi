using System.Collections.Generic;
using System.IO;
using System.Text;
using Nakahira;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

// キャラクターセレクトに並ぶキャラ(SelectRoster)の、ネット対戦用プレハブと対応表を作る。
//  1. 専用のプレハブがあるキャラごとに、そのプレハブの Variant を Assets/suehara/Network/Characters/<名前>_Net.prefab に作る
//     (すでにあれば作り直さず、足りない部品だけ足す)
//  2. Variant に、基本のだるまのネット用プレハブ(DarumaCharacter_Net)と同じネット部品を、同じ設定で付ける
//     (NetworkObject・NetworkTransform・NetworkRigidbody・NetworkDarumaCharacter・NetworkHammerView)
//  3. Assets/DefaultNetworkPrefabs.asset に登録する(ネットで出せるようにする)
//  4. 対応表 Resources/NetCharacters.asset(NetCharacterList)を SelectRoster の並びで作り直す
// 元のプレハブ(Yurube さんなど)は変えない。キャラが増えた・プレハブが変わったら、もう一度実行する
public static class NetCharacterBuilder
{
    const string kBaseNet = "Assets/suehara/Network/DarumaCharacter_Net.prefab";
    const string kFolder = "Assets/suehara/Network/Characters";
    const string kListPath = "Assets/suehara/Resources/NetCharacters.asset";
    const string kNetworkPrefabs = "Assets/DefaultNetworkPrefabs.asset";

    [MenuItem("Tools/ネット対戦/キャラのネット用プレハブを作り直す")]
    public static void Build()
    {
        var log = new StringBuilder("[NetCharacterBuilder] キャラのネット用プレハブを作り直しました\n");
        var baseNet = AssetDatabase.LoadAssetAtPath<GameObject>(kBaseNet);
        var roster = FindRoster();
        var prefabsList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(kNetworkPrefabs);
        if (baseNet == null || roster == null || prefabsList == null)
        {
            Debug.LogError($"[NetCharacterBuilder] 見つかりません: {(baseNet == null ? kBaseNet + " " : "")}{(roster == null ? "SelectRoster " : "")}{(prefabsList == null ? kNetworkPrefabs : "")}");
            return;
        }
        Directory.CreateDirectory(kFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(kListPath));

        var list = AssetDatabase.LoadAssetAtPath<NetCharacterList>(kListPath);
        if (list == null)
        {
            list = ScriptableObject.CreateInstance<NetCharacterList>();
            AssetDatabase.CreateAsset(list, kListPath);
        }
        list.entries.Clear();

        foreach (var data in roster.characters)
        {
            if (data == null) continue;
            DarumaCharacter net = null;
            if (data.prefab != null && data.prefab.GetComponent<DarumaCharacter>() != null)
            {
                net = BuildVariant(data.prefab, baseNet);
                if (net != null && Register(prefabsList, net.gameObject)) log.Append($"登録した: {net.name}\n");
            }
            list.entries.Add(new NetCharacterList.Entry { character = data, netPrefab = net });
            log.Append($"{data.displayName}({data.id}) → {(net != null ? AssetDatabase.GetAssetPath(net) : "基本のだるま")}\n");
        }

        EditorUtility.SetDirty(list);
        EditorUtility.SetDirty(prefabsList);
        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    static SelectRoster FindRoster()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:SelectRoster"))
        {
            var r = AssetDatabase.LoadAssetAtPath<SelectRoster>(AssetDatabase.GUIDToAssetPath(guid));
            if (r != null) return r;
        }
        return null;
    }

    // 元のプレハブの Variant を作り(あれば読み)、ネット部品を付ける
    static DarumaCharacter BuildVariant(GameObject source, GameObject baseNet)
    {
        string path = $"{kFolder}/{source.name}_Net.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        GameObject root;
        if (existing != null)
        {
            root = PrefabUtility.LoadPrefabContents(path);
        }
        else
        {
            root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = source.name + "_Net";
        }

        // NetworkObject は一番上に(識別番号はプレハブを保存したときに作られる)
        if (root.GetComponent<NetworkObject>() == null) root.AddComponent<NetworkObject>();
        CopyComponent<NetworkTransform>(baseNet, root);
        CopyComponent<NetworkRigidbody>(baseNet, root);
        CopyComponent<NetworkDarumaCharacter>(baseNet, root);
        CopyComponent<NetworkHammerView>(baseNet, root);

        GameObject saved;
        if (existing != null)
        {
            saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }
        else
        {
            saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }
        return saved != null ? saved.GetComponent<DarumaCharacter>() : null;
    }

    // 基本のだるまのネット用プレハブと同じ設定で付ける(すでにあれば設定だけそろえる)
    static void CopyComponent<T>(GameObject from, GameObject to) where T : Component
    {
        var src = from.GetComponent<T>();
        if (src == null) return;
        var dst = to.GetComponent<T>();
        if (dst == null) dst = to.AddComponent<T>();
        EditorUtility.CopySerialized(src, dst);
    }

    static bool Register(NetworkPrefabsList list, GameObject prefab)
    {
        if (list.Contains(prefab)) return false;
        list.Add(new NetworkPrefab { Prefab = prefab });
        return true;
    }
}
