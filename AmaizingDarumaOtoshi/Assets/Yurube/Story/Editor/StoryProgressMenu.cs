#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Yurube.Story;

// 動作確認用。章の解放条件を試すために、セーブ（PlayerPrefs）だけを操作する。
// 製品版には入らない（Editor フォルダ＋UNITY_EDITOR）。
public static class StoryProgressMenu
{
    [MenuItem("Yurube/ストーリー/進行をリセット（全章）")]
    static void ResetAll()
    {
        foreach (var chapter in StoryData.LoadAll())
        {
            if (chapter != null) StoryProgress.Clear(chapter.id);
        }
        Debug.Log("[Story] 章の進行をリセットしました");
    }

    [MenuItem("Yurube/ストーリー/序章をクリア済みにする")]
    static void ClearPrologue()
    {
        StoryProgress.MarkCleared(StoryLauncher.PrologueId);
        Debug.Log("[Story] 序章をクリア済みにしました");
    }

    [MenuItem("Yurube/ストーリー/第1章をクリア済みにする")]
    static void ClearChapter1()
    {
        StoryProgress.MarkCleared("chapter1");
        Debug.Log("[Story] 第1章をクリア済みにしました");
    }
}
#endif
