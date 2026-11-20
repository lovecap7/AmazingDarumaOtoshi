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
        StoryProgress.ResetAll();
    }

    [MenuItem("Yurube/ストーリー/全章を解放する")]
    static void UnlockAll()
    {
        StoryProgress.UnlockAll();
    }

    [MenuItem("Yurube/ストーリー/プロローグをクリア済みにする")]
    static void ClearPrelude()
    {
        StoryProgress.MarkCleared(StoryLauncher.PreludeId);
        Debug.Log("[Story] プロローグをクリア済みにしました");
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

    [MenuItem("Yurube/ストーリー/第2章をクリア済みにする")]
    static void ClearChapter2()
    {
        StoryProgress.MarkCleared("chapter2");
        Debug.Log("[Story] 第2章をクリア済みにしました");
    }

    [MenuItem("Yurube/ストーリー/最終章をクリア済みにする")]
    static void ClearChapter3()
    {
        StoryProgress.MarkCleared("chapter3");
        Debug.Log("[Story] 最終章をクリア済みにしました");
    }

    [MenuItem("Yurube/ストーリー/全章クリア済みにする")]
    static void ClearAll()
    {
        foreach (var chapter in StoryData.LoadAll())
        {
            if (chapter != null) StoryProgress.MarkCleared(chapter.id);
        }
        Debug.Log("[Story] すべての章をクリア済みにしました");
    }
}
#endif
