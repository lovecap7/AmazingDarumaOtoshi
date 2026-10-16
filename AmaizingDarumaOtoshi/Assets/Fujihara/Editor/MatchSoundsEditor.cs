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
