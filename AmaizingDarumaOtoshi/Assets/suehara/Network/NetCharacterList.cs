using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;

// ネット対戦で選べるキャラクターと、そのネット用プレハブ(Assets/suehara/Network/Characters/*_Net.prefab)の対応。
// 中身はメニューの「Tools → ネット対戦 → キャラのネット用プレハブを作り直す」(NetCharacterBuilder)が作る。置き場所は Resources/NetCharacters.asset
//  ・キャラの番号 = キャラクターセレクトの並び(SelectRoster.characters)の順。通信ではこの番号を送る
//  ・ネット用プレハブが無いキャラ(専用のプレハブが無いキャラ)は、NetStages の Match に設定した基本のだるま(DarumaCharacter_Net)で出る
[CreateAssetMenu(menuName = "Daruma/Net Character List", fileName = "NetCharacters")]
public class NetCharacterList : ScriptableObject
{
    public const int None = 255;

    [Serializable]
    public class Entry
    {
        public CharacterData character;
        [Tooltip("ネット用プレハブ(NetworkObject 付き)。無ければ基本のだるまで出る")]
        public DarumaCharacter netPrefab;
    }

    public List<Entry> entries = new List<Entry>();

    static NetCharacterList s_instance;
    // ホストで使う、プレハブをネット用に差し替えたキャラのデータ(番号ごと。シーンをまたいで使い回す)
    readonly Dictionary<int, CharacterData> m_hostData = new Dictionary<int, CharacterData>();

    public static NetCharacterList Instance
    {
        get
        {
            if (s_instance == null) s_instance = Resources.Load<NetCharacterList>("NetCharacters");
            return s_instance;
        }
    }

    public int Count => entries.Count;

    public CharacterData Character(int index) => index >= 0 && index < entries.Count ? entries[index].character : null;

    public int IndexOf(string id)
    {
        if (string.IsNullOrEmpty(id)) return None;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].character != null && entries[i].character.id == id) return i;
        }
        return None;
    }

    // この PC で選べるか(だるきんぐは実績をすべて解除するまで選べない。キャラクターセレクトと同じ)
    public bool IsAvailable(int index) => Yurube.Achievements.AchievementRewards.IsAvailable(Character(index));

    // ホスト: 試合で使うキャラのデータ。DarumaMatch はデータの prefab を出すので、prefab をネット用に差し替えた複製を返す
    // (名前・顔のモデル・id などはそのまま。ネット用プレハブが無いキャラは prefab を空にして基本のだるまで出す)
    public CharacterData HostData(int index)
    {
        var source = Character(index);
        if (source == null) return null;
        if (m_hostData.TryGetValue(index, out var data) && data != null) return data;
        data = Instantiate(source);
        data.name = source.name;
        data.prefab = entries[index].netPrefab != null ? entries[index].netPrefab.gameObject : null;
        m_hostData[index] = data;
        return data;
    }
}
