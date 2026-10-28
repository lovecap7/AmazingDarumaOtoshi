using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// MatchSounds（Resources/MatchSounds.asset）の Inspector。
//  ・上：4つの場面（振った / 積み木に当たった / 積み木を飛ばした / 撃墜）に AudioClip をドラッグして入れる。空欄なら仮の音（合成）
//        ▶ でいまその場面で鳴る音を試聴（ゲームを再生していなくても鳴る）
//  ・下：候補の音。▶ で試聴、1〜4 のボタンでその場面に入れる（入っている場面のボタンは押された見た目・名前が太字）
[CustomEditor(typeof(MatchSounds))]
public class MatchSoundsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var s = (MatchSounds)target;
        serializedObject.Update();
        EditorGUILayout.LabelField("場面ごとの音（空欄なら仮の音）", EditorStyles.boldLabel);
        Field("swing", "1. 振った音", () => s.Swing);
        Field("hitBlock", "2. 積み木に当たった音", () => s.HitBlock);
        Field("knock", "3. 積み木を飛ばした音", () => s.Knock);
        Field("ko", "4. 撃墜の音", () => s.KO);
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.HelpBox("1. ハンマーを振ったとき\n" +
                                "2. 振ったハンマーが積み木に当たったとき（打ち返しも。打ち返すほど高くなる）\n" +
                                "3. 飛ばした積み木が相手に当たったとき／ハンマーで相手の積み木を直接はじき出したとき\n" +
                                "4. 首を飛ばして撃墜したとき", MessageType.None);

        // ---- 打ち返しの音（回数で変わる） ----
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("打ち返しの音（1回目→①、2回目→②…、最後の段から先はずっと最後の音）", EditorStyles.boldLabel);
        serializedObject.Update();
        var rh = serializedObject.FindProperty("returnHits");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("段数", GUILayout.Width(40));
            rh.arraySize = Mathf.Clamp(EditorGUILayout.IntField(rh.arraySize, GUILayout.Width(50)), 1, 30);
            if (GUILayout.Button("▶ 1段目から順に鳴らす", GUILayout.Width(150))) PlaySequence(s.returnHits);
        }
        for (int i = 0; i < rh.arraySize; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var el = rh.GetArrayElementAtIndex(i);
                EditorGUILayout.PropertyField(el, new GUIContent((i + 1) + (i == rh.arraySize - 1 ? " 回目〜" : " 回目")));
                using (new EditorGUI.DisabledScope(el.objectReferenceValue == null))
                    if (GUILayout.Button("▶", GUILayout.Width(28))) Preview((AudioClip)el.objectReferenceValue);
            }
        }
        EditorGUILayout.PropertyField(serializedObject.FindProperty("returnPitchStep"), new GUIContent("最後の段から1回ごとに上げる（半音）"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("returnPitchMax"), new GUIContent("上げる上限（半音）"));
        EditorGUILayout.HelpBox("最後の段を超えた打ち返しは、最後の音を1回ごとに少しずつ高くして鳴らす（ゲーム中のみ。ここの ▶ では高さは変わらない）", MessageType.None);
        serializedObject.ApplyModifiedProperties();

        // 打ち返しの音のセット（Audio/SE/打ち返し候補_○○ フォルダ。ファイル名の順に①②③…）
        EditorGUILayout.LabelField("打ち返しの音のセット（▶ で順に試聴、「入れる」で上の段にまとめて入れる）", EditorStyles.miniBoldLabel);
        foreach (var folder in ReturnSetFolders())
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var clips = LoadClips(folder);
                if (GUILayout.Button("▶", GUILayout.Width(28))) PlaySequence(clips);
                EditorGUILayout.LabelField($"{System.IO.Path.GetFileName(folder)}（{clips.Length}段）");
                if (GUILayout.Button("入れる", GUILayout.Width(50)))
                {
                    Undo.RecordObject(s, "打ち返しの音のセットを入れる");
                    s.returnHits = clips;
                    EditorUtility.SetDirty(s);
                }
            }
        }

        // ---- 候補の音（聞き比べ） ----
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("候補の音（▶ で試聴、1〜4 でその場面に入れる）", EditorStyles.boldLabel);
        for (int i = 0; i < s.candidates.Count; i++)
        {
            var clip = s.candidates[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(clip == null))
                {
                    if (GUILayout.Button("▶", GUILayout.Width(28))) Preview(clip);
                    EditorGUILayout.LabelField(clip != null ? clip.name : "（空欄）", Used(s, clip) ? EditorStyles.boldLabel : EditorStyles.label);
                    if (Slot("1", clip != null && s.swing == clip)) Assign(s, ref s.swing, clip);
                    if (Slot("2", clip != null && s.hitBlock == clip)) Assign(s, ref s.hitBlock, clip);
                    if (Slot("3", clip != null && s.knock == clip)) Assign(s, ref s.knock, clip);
                    if (Slot("4", clip != null && s.ko == clip)) Assign(s, ref s.ko, clip);
                }
            }
        }
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("candidates"), new GUIContent("候補の一覧（ドラッグで追加）"), true);
        serializedObject.ApplyModifiedProperties();
        if (GUILayout.Button("■ 試聴を止める")) StopAll();
    }

    const string SeFolder = "Assets/Fujihara/Audio/SE";

    static string[] ReturnSetFolders()
    {
        if (!AssetDatabase.IsValidFolder(SeFolder)) return new string[0];
        return Array.FindAll(AssetDatabase.GetSubFolders(SeFolder), f => System.IO.Path.GetFileName(f).StartsWith("打ち返し候補_"));
    }

    static AudioClip[] LoadClips(string folder)
    {
        var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
        var paths = Array.ConvertAll(guids, AssetDatabase.GUIDToAssetPath);
        Array.Sort(paths, string.CompareOrdinal);
        return Array.ConvertAll(paths, AssetDatabase.LoadAssetAtPath<AudioClip>);
    }

    // 1段目から順に、少し間をあけて鳴らす（打ち返しが続いたときの上がり方を聞く）
    static void PlaySequence(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return;
        int index = 0;
        double next = EditorApplication.timeSinceStartup;
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            if (EditorApplication.timeSinceStartup < next) return;
            if (index >= clips.Length) { EditorApplication.update -= tick; return; }
            Preview(clips[index++]);
            next = EditorApplication.timeSinceStartup + 0.45;
        };
        EditorApplication.update += tick;
    }

    static bool Used(MatchSounds s, AudioClip c) => c != null && (s.swing == c || s.hitBlock == c || s.knock == c || s.ko == c);

    // 場面の番号ボタン（その場面に入っていれば押された見た目）。押したら true
    static bool Slot(string label, bool on)
    {
        var style = new GUIStyle(EditorStyles.miniButton) { fontStyle = on ? FontStyle.Bold : FontStyle.Normal };
        return GUILayout.Toggle(on, label, style, GUILayout.Width(26)) && !on;
    }

    static void Assign(MatchSounds s, ref AudioClip field, AudioClip clip)
    {
        Undo.RecordObject(s, "音を場面に入れる");
        field = clip;
        EditorUtility.SetDirty(s);
        Preview(clip);
    }

    void Field(string property, string label, Func<AudioClip> current)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(property), new GUIContent(label));
            if (GUILayout.Button("▶", GUILayout.Width(28)))
            {
                serializedObject.ApplyModifiedProperties();
                Preview(current());
            }
        }
    }

    // ---- 試聴（Unity の内部の AudioUtil を使う。ゲームを再生していなくても鳴る） ----
    static Type AudioUtil => typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

    static void Preview(AudioClip clip)
    {
        if (clip == null) return;
        StopAll();
        var play = AudioUtil?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public, null,
                                        new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        if (play != null) play.Invoke(null, new object[] { clip, 0, false });
        else Debug.LogWarning("[音] このバージョンの Unity では Inspector から試聴できません（ゲームを再生して確認してください）");
    }

    static void StopAll()
    {
        AudioUtil?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
    }
}
