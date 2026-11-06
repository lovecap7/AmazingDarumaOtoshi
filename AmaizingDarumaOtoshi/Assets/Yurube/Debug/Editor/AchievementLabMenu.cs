#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Yurube.Achievements
{
    // 実績ラボを開くショートカット（メニューの「Yurube」から）。
    // ビルドには入らないので、製品版には残らない。
    public static class AchievementLabMenu
    {
        const string ScenePath = "Assets/Yurube/Debug/AchievementLab.unity";

        [MenuItem("Yurube/実績ラボを開く")]
        static void OpenLab()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
#endif
