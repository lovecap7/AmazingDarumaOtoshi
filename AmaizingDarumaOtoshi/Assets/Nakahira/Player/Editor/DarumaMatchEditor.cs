using UnityEditor;
using UnityEngine;

namespace Nakahira
{
    // DarumaMatch のインスペクター。再生中は、出ている人の必殺技ゲージを満タンにするボタンを出す(デバッグ用)
    [CustomEditor(typeof(DarumaMatch))]
    public class DarumaMatchEditor : Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!Application.isPlaying) return;

            var match = (DarumaMatch)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("デバッグ(再生中)", EditorStyles.boldLabel);
            if (GUILayout.Button("全員の必殺技ゲージを満タンにする"))
            {
                foreach (var p in match.Players) Fill(p);
            }
            foreach (var p in match.Players)
            {
                if (p == null) continue;
                var gauge = GaugeOf(p);
                using (new EditorGUILayout.HorizontalScope())
                {
                    string name = p.Label + "  " + (p.CharacterData != null ? p.CharacterData.displayName : "-");
                    EditorGUILayout.LabelField(name, gauge != null ? "ゲージ " + gauge.Percent + "%" : "(体なし・幽霊)");
                    using (new EditorGUI.DisabledScope(gauge == null || gauge.IsFull))
                    {
                        if (GUILayout.Button("満タン", GUILayout.Width(60))) Fill(p);
                    }
                }
            }
        }

        static Yurube.SpecialGauge GaugeOf(MatchPlayer p) =>
            p != null && p.Character != null ? p.Character.GetComponent<Yurube.SpecialGauge>() : null;

        static void Fill(MatchPlayer p)
        {
            var gauge = GaugeOf(p);
            if (gauge != null) gauge.Fill();
        }
    }
}
