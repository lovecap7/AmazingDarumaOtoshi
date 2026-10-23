using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// MenuSounds（Resources/MenuSounds.asset）の Inspector。
//  ・上：3つの場面（カーソル移動 / 決定 / キャンセル）に AudioClip を入れる。▶ で試聴
//  ・下：候補の音。▶ で試聴、1〜3 のボタンでその場面に入れる
[CustomEditor(typeof(MenuSounds))]
public class MenuSoundsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var s = (MenuSounds)target;
        serializedObject.Update();
        EditorGUILayout.LabelField("場面ごとの音", EditorStyles.boldLabel);
        Field("cursor", "1. カーソル移動", () => s.cursor);
        Field("decide", "2. 決定", () => s.decide);
        Field("cancel", "3. キャンセル", () => s.cancel);
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("候補の音（▶ で試聴、1〜3 でその場面に入れる）", EditorStyles.boldLabel);
        for (int i = 0; i < s.candidates.Count; i++)
        {
            var clip = s.candidates[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(clip == null))
                {
                    if (GUILayout.Button("▶", GUILayout.Width(28))) Preview(clip);
                    bool used = clip != null && (s.cursor == clip || s.decide == clip || s.cancel == clip);
                    EditorGUILayout.LabelField(clip != null ? clip.name : "（空欄）", used ? EditorStyles.boldLabel : EditorStyles.label);
                    if (Slot("1", clip != null && s.cursor == clip)) Assign(s, ref s.cursor, clip);
                    if (Slot("2", clip != null && s.decide == clip)) Assign(s, ref s.decide, clip);
                    if (Slot("3", clip != null && s.cancel == clip)) Assign(s, ref s.cancel, clip);
                }
            }
        }
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("candidates"), new GUIContent("候補の一覧（ドラッグで追加）"), true);
        serializedObject.ApplyModifiedProperties();

        // ---- BGM ----
        EditorGUILayout.Space(14);
        EditorGUILayout.LabelField("画面ごとの BGM", EditorStyles.boldLabel);
        serializedObject.Update();
        Field("titleBgm", "T. タイトル", () => s.titleBgm);
        Field("menuBgm", "M. メニュー", () => s.menuBgm);
        Field("selectBgm", "S. セレクト", () => s.selectBgm);
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("BGM の候補（▶ で試聴、T / M / S でその画面に入れる）", EditorStyles.boldLabel);
        for (int i = 0; i < s.bgmCandidates.Count; i++)
        {
            var clip = s.bgmCandidates[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(clip == null))
                {
                    if (GUILayout.Button("▶", GUILayout.Width(28))) Preview(clip);
                    bool used = clip != null && (s.titleBgm == clip || s.menuBgm == clip || s.selectBgm == clip);
                    EditorGUILayout.LabelField(clip != null ? clip.name : "（空欄）", used ? EditorStyles.boldLabel : EditorStyles.label);
                    if (Slot("T", clip != null && s.titleBgm == clip)) Assign(s, ref s.titleBgm, clip);
                    if (Slot("M", clip != null && s.menuBgm == clip)) Assign(s, ref s.menuBgm, clip);
                    if (Slot("S", clip != null && s.selectBgm == clip)) Assign(s, ref s.selectBgm, clip);
                }
            }
        }
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("bgmCandidates"), new GUIContent("BGM 候補の一覧（ドラッグで追加）"), true);
        serializedObject.ApplyModifiedProperties();

        if (GUILayout.Button("■ 試聴を止める")) StopAll();
    }

    static bool Slot(string label, bool on)
    {
        var style = new GUIStyle(EditorStyles.miniButton) { fontStyle = on ? FontStyle.Bold : FontStyle.Normal };
        return GUILayout.Toggle(on, label, style, GUILayout.Width(26)) && !on;
    }

    static void Assign(MenuSounds s, ref AudioClip field, AudioClip clip)
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

    // ---- 試聴（MatchSoundsEditor と同じく Unity 内部の AudioUtil を使う） ----
    static Type AudioUtil => typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

    static void Preview(AudioClip clip)
    {
        if (clip == null) return;
        StopAll();
        var play = AudioUtil?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public, null,
                                        new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        if (play != null) play.Invoke(null, new object[] { clip, 0, false });
    }

    static void StopAll()
    {
        AudioUtil?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
    }
}
