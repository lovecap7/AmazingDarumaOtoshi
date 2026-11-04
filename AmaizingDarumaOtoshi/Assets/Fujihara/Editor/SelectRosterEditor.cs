using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// SelectRoster（Data/SelectRoster.asset）の Inspector。
// 上はいつもの一覧。その下に「ずかんの中身」として、キャラごとに CharacterData の中身（名前・色・顔の画像・パッシブ・ハンマー性能・必殺技）を
// その場で書き換えられる欄を並べる（▶ で開く）。書き換えると、そのキャラの CharacterData（Data/Characters）が変わる
[CustomEditor(typeof(SelectRoster))]
public class SelectRosterEditor : Editor
{
    readonly Dictionary<CharacterData, bool> open = new Dictionary<CharacterData, bool>();
    readonly Dictionary<CharacterData, Editor> editors = new Dictionary<CharacterData, Editor>();

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var roster = (SelectRoster)target;
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("ずかんの中身（キャラごと。上の characters の順に、メニューの「ずかん」に並ぶ）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("▶ で開いて書き換えると、そのキャラの CharacterData が変わります（セレクト画面の名前・色・顔もここと同じ）。", MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("すべて開く")) foreach (var c in roster.characters) if (c != null) open[c] = true;
            if (GUILayout.Button("すべて閉じる")) foreach (var c in roster.characters) if (c != null) open[c] = false;
        }

        foreach (var c in roster.characters)
        {
            if (c == null) continue;
            open.TryGetValue(c, out bool isOpen);
            string title = string.IsNullOrEmpty(c.displayName) ? c.name : c.displayName;
            isOpen = EditorGUILayout.Foldout(isOpen, title + "　（" + c.name + "）", true, EditorStyles.foldoutHeader);
            open[c] = isOpen;
            if (!isOpen) continue;

            if (!editors.TryGetValue(c, out var ed) || ed == null)
            {
                ed = CreateEditor(c);
                editors[c] = ed;
            }
            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                ed.OnInspectorGUI();
                if (GUILayout.Button("このキャラのファイルを選ぶ", GUILayout.Width(200))) EditorGUIUtility.PingObject(c);
            }
            EditorGUILayout.Space(4);
        }
    }

    void OnDisable()
    {
        foreach (var ed in editors.Values) if (ed != null) DestroyImmediate(ed);
        editors.Clear();
    }
}
