#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yurube.Achievements;

namespace Yurube.Achievements
{
    // 本編の実績をデバッグするための画面。「Yurube」メニューから開く（ビルドには入らない）。
    //
    // ・本編（MenuScene）を開いて再生し、じっせきのヒントを確かめる
    // ・ヒントの出る／出ないを、しきい値の前後で切り替える（元に戻せる）
    // ・本編の実績データを、全解除・リセット・初期化する（実績ラボから移したもの）
    // ・シークレット実績ごとの進捗・しきい値・表示状態を見る
    public class AchievementHintDebugWindow : EditorWindow
    {
        const string MenuScenePath = "Assets/Fujihara/Scene/MenuScene.unity";
        // 書き換える前の値（"種類=値;種類=値" の形）。1回だけ取る
        const string BackupKey = "Achievements.HintDebug.Backup.v1";

        Vector2 m_scroll;
        // 本編のデータを操作してよいか（誤操作防止）
        bool m_allowNormalEdit;

        [MenuItem("Yurube/実績ヒントの確認（デバッグ）")]
        static void OpenWindow()
        {
            var window = GetWindow<AchievementHintDebugWindow>("実績ヒント確認");
            window.minSize = new Vector2(540f, 520f);
        }

        void OnEnable() => EditorApplication.update += OnEditorUpdate;
        void OnDisable() => EditorApplication.update -= OnEditorUpdate;

        void OnEditorUpdate()
        {
            // 再生中は進捗が動くので、値を出し直す
            if (Application.isPlaying) Repaint();
        }

        void OnGUI()
        {
            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);

            DrawPlaySection();
            DrawHintSection();
            DrawMainGameDataSection();
            DrawSecretList();

            EditorGUILayout.EndScrollView();
        }

        // ---------------- 本編で見る ----------------

        void DrawPlaySection()
        {
            EditorGUILayout.LabelField("本編で確かめる", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("MenuScene を開いて再生（じっせきで確認）"))
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                    if (!System.IO.File.Exists(MenuScenePath))
                    {
                        EditorUtility.DisplayDialog("実績ヒント", "MenuScene が見つかりません: " + MenuScenePath, "OK");
                        return;
                    }
                    EditorSceneManager.OpenScene(MenuScenePath);
                    EditorApplication.isPlaying = true;
                }
                if (GUILayout.Button("状態をログに出す", GUILayout.Width(140f))) LogState();
            }
            EditorGUILayout.Space(6f);
        }

        // ---------------- ヒントの確認 ----------------

        void DrawHintSection()
        {
            EditorGUILayout.LabelField("ヒントの確認", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "シークレット実績のヒントは、関連する累計が半分に達するか、試合を一定回数プレイすると出ます。\n" +
                "「直前／直後」は、いまの累計を書き換えて境目を試します（「元に戻す」で戻せます）。",
                MessageType.Info);

            bool force = AchievementHints.DebugForceReveal;
            bool newForce = EditorGUILayout.ToggleLeft("ヒントを強制表示（しきい値を無視・データは変更しない）", force);
            if (newForce != force)
            {
                AchievementHints.DebugForceReveal = newForce;
                AchievementManager.NotifyChanged();   // 開いている「じっせき」を作り直す
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("しきい値の直前（伏せる）")) ApplyThresholds(false);
                    if (GUILayout.Button("しきい値の直後（出す）")) ApplyThresholds(true);
                    if (GUILayout.Button("元に戻す", GUILayout.Width(110f))) RestoreBackup();
                }
            }
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("累計の書き換えは再生中だけ使えます（停止中はゲーム側の値が動かないため）。",
                                        MessageType.None);
            }
            EditorGUILayout.Space(6f);
        }

        // ---------------- 本編の実績データ（実績ラボから移したもの） ----------------

        void DrawMainGameDataSection()
        {
            EditorGUILayout.LabelField("本編の実績データ", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(AchievementManager.NormalStorageKey
                                       + "　保存：" + (AchievementManager.HasSave(AchievementManager.NormalStorageKey) ? "あり" : "なし")
                                       + (AchievementManager.StorageKey == AchievementManager.NormalStorageKey
                                           ? "　解除：" + AchievementManager.UnlockedCount + " / " + AchievementManager.All.Count
                                           : "　（いまは実績ラボのデータを表示中）"));
            EditorGUILayout.HelpBox(
                "ゲーム本編で解除した実績です。ここから消すと、次に遊ぶときは最初からになります。",
                MessageType.None);

            m_allowNormalEdit = EditorGUILayout.ToggleLeft(
                "本編のデータを操作する（チェックすると下のボタンが使えます）", m_allowNormalEdit);

            using (new EditorGUI.DisabledScope(!m_allowNormalEdit))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("全解除（報酬キャラの解放テスト）")) UnlockAllOfMainGame();
                    if (GUILayout.Button("解除だけリセット")) ResetMainGame(unlocked: true, progress: false);
                    if (GUILayout.Button("進捗だけリセット")) ResetMainGame(unlocked: false, progress: true);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("全部初期化（本編の実績を消す）")) ClearMainGame();
                    if (GUILayout.Button("保存データを読み直す（いまの値に戻す）"))
                    {
                        ReloadIfMainGame();
                        Flash("本編の保存データを読み直しました");
                    }
                }
            }

            // ---- リザルトで見せる実績（解除しても、ゲーム中の通知は出ない） ----
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("リザルトで見せる実績：" + AchievementManager.PendingCount + " 件（まだ見せていない解除）");
            using (new EditorGUI.DisabledScope(!m_allowNormalEdit))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("未解除を3件解除（リザルト確認用）")) UnlockFewForResult();
                    if (GUILayout.Button("未表示を「見せた」にする"))
                    {
                        int count = AchievementManager.PendingCount;
                        AchievementManager.MarkAllNotified();
                        ReloadIfMainGame();
                        Flash(count + " 件を表示済みにしました（次のリザルトには出ません）");
                    }
                }
            }

            EditorGUILayout.HelpBox(
                "※ 解除・リセットはすぐ保存されます。再生を止めて開始し直すと、状態が残っているか確認できます。\n" +
                "※ 再生中に操作した場合、ゲーム側の表示はこの画面の「読み直す」か、じっせきを開き直すと合います。",
                MessageType.None);
            EditorGUILayout.Space(6f);
        }

        // リザルトのカードを試す用：未解除を3件だけ解除して、未表示にする
        void UnlockFewForResult()
        {
            if (!Confirm("本編の未解除の実績を3件解除します。よろしいですか？\n（次の試合のリザルトでカードとして出ます）")) return;

            AchievementNotification.BeginSuppress();
            int count = 0;
            foreach (var data in AchievementManager.All)
            {
                if (data.IsUnlocked) continue;
                AchievementManager.DebugUnlock(data);
                count++;
                if (count >= 3) break;
            }
            AchievementNotification.EndSuppress();

            ReloadIfMainGame();
            Flash("本編の実績を " + count + " 件解除しました（未表示 " + AchievementManager.PendingCount + " 件）");
        }

        void UnlockAllOfMainGame()
        {            if (!Confirm("本編の実績を全解除します。よろしいですか？\n（報酬キャラクター「"
                         + AchievementRewards.RewardCharacterName + "」が選べるようになります）")) return;

            // まとめて解除するので、通知は出さない（解除そのものは通常どおり）
            AchievementNotification.BeginSuppress();
            AchievementManager.DebugUnlockAllStorage(AchievementManager.NormalStorageKey);
            AchievementNotification.EndSuppress();

            ReloadIfMainGame();
            Flash("本編の実績を全解除しました");        }

        void ResetMainGame(bool unlocked, bool progress)
        {
            if (!Confirm("本編の実績を" + (unlocked ? "解除だけ" : "進捗だけ") + "リセットします。よろしいですか？")) return;
            AchievementManager.DebugResetStorage(AchievementManager.NormalStorageKey, unlocked, progress);
            ReloadIfMainGame();
            Flash("本編の実績をリセットしました");
        }

        void ClearMainGame()
        {
            if (!Confirm("本編の実績データを全部消します。元に戻せません。よろしいですか？")) return;
            AchievementManager.DebugClearStorage(AchievementManager.NormalStorageKey);
            ReloadIfMainGame();
            EditorPrefs.DeleteKey(BackupKey);   // 戻す先も消えるので、バックアップも捨てる
            Flash("本編の実績データを全部消しました");
        }

        // いま本編のデータを見ているときだけ読み直す（実績ラボを見ている間は、その表示を崩さない）
        static void ReloadIfMainGame()
        {
            if (AchievementManager.StorageKey != AchievementManager.NormalStorageKey) return;
            AchievementManager.SetStorage(AchievementManager.NormalStorageKey, true);
        }

        static bool Confirm(string message) =>
            EditorUtility.DisplayDialog("実績ヒント", message, "実行する", "やめる");

        static void Flash(string message) => Debug.Log("[実績ヒント] " + message);

        // ---------------- シークレット実績の一覧 ----------------

        void DrawSecretList()
        {
            EditorGUILayout.LabelField("シークレット実績のヒント", EditorStyles.boldLabel);

            bool any = false;
            foreach (var data in AchievementManager.All)
            {
                if (!data.IsSecret || !data.HasHint) continue;
                any = true;
                DrawEntry(data);
            }
            if (!any) EditorGUILayout.LabelField("ヒントを持つシークレット実績がありません。");
        }

        void DrawEntry(AchievementData data)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(data.Id + "（" + data.Name + "）", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    bool revealed = AchievementHints.IsRevealed(data);
                    EditorGUILayout.LabelField(revealed ? "ヒント表示中" : "伏せている", GUILayout.Width(110f));
                }

                int progress = AchievementManager.CountOf(data.Definition.metric);
                int progressAt = AchievementHints.ProgressThreshold(data);
                int matches = AchievementManager.CountOf(AchievementMetric.MatchPlayed);
                int matchAt = AchievementHints.MatchThreshold(data);
                EditorGUILayout.LabelField("累計", progress + " / " + progressAt + "（" + MetricName(data.Definition.metric) + "）");
                EditorGUILayout.LabelField("試合数", matches + " / " + matchAt);
                EditorGUILayout.LabelField("解除", data.IsUnlocked ? "解除済み（ヒントは出さない）" : "未解除");
                EditorGUILayout.LabelField("ヒント", data.Hint);
            }
            EditorGUILayout.Space(2f);
        }

        // ---------------- 累計の書き換え ----------------

        // 「しきい値の直前（伏せる）／直後（出す）」に、シークレット実績の累計を合わせる。
        // 1回きりの実績（目標が1）は累計では判定できないので、試合数で合わせる。
        void ApplyThresholds(bool above)
        {
            if (!Application.isPlaying) return;
            AchievementManager.Initialize();
            EnsureBackup();

            var done = new HashSet<AchievementMetric>();
            int matchThreshold = int.MaxValue;

            foreach (var data in AchievementManager.All)
            {
                if (!data.IsSecret || !data.HasHint) continue;
                matchThreshold = Mathf.Min(matchThreshold, AchievementHints.MatchThreshold(data));

                var metric = data.Definition.metric;
                if (metric == AchievementMetric.MatchPlayed) continue;
                if (data.Target <= 1) continue;
                if (!done.Add(metric)) continue;

                int threshold = AchievementHints.ProgressThreshold(data);
                SetCounter(metric, above ? threshold : Mathf.Max(0, threshold - 1));
            }

            if (matchThreshold != int.MaxValue)
            {
                SetCounter(AchievementMetric.MatchPlayed, above ? matchThreshold : Mathf.Max(0, matchThreshold - 1));
            }

            AchievementManager.Save();
            LogState();
        }

        static void SetCounter(AchievementMetric metric, int value)
        {
            foreach (var data in AchievementManager.All)
            {
                if (data.Definition.metric != metric) continue;
                AchievementManager.DebugSetProgress(data, value);
                return;
            }
        }

        // 書き換える前に、いまの値を1度だけ控える
        void EnsureBackup()
        {
            if (EditorPrefs.HasKey(BackupKey)) return;
            var sb = new StringBuilder();
            foreach (var metric in MetricsToTouch())
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append((int)metric).Append('=').Append(AchievementManager.CountOf(metric));
            }
            EditorPrefs.SetString(BackupKey, sb.ToString());
        }

        static IEnumerable<AchievementMetric> MetricsToTouch()
        {
            var seen = new HashSet<AchievementMetric>();
            foreach (var data in AchievementManager.All)
            {
                if (!data.IsSecret || !data.HasHint) continue;
                if (data.Target <= 1 && data.Definition.metric != AchievementMetric.MatchPlayed) continue;
                if (seen.Add(data.Definition.metric)) yield return data.Definition.metric;
            }
            if (seen.Add(AchievementMetric.MatchPlayed)) yield return AchievementMetric.MatchPlayed;
        }

        void RestoreBackup()
        {
            if (!Application.isPlaying) return;
            string text = EditorPrefs.GetString(BackupKey, "");
            if (string.IsNullOrEmpty(text))
            {
                EditorUtility.DisplayDialog("実績ヒント", "戻す値がありません（まだ書き換えていないか、もう戻しています）。", "OK");
                return;
            }

            foreach (var line in text.Split(';'))
            {
                int split = line.IndexOf('=');
                if (split <= 0) continue;
                if (!int.TryParse(line.Substring(0, split), out int metric)) continue;
                if (!int.TryParse(line.Substring(split + 1), out int value)) continue;
                SetCounter((AchievementMetric)metric, value);
            }

            EditorPrefs.DeleteKey(BackupKey);
            AchievementManager.Save();
            LogState();
        }

        // ---------------- ログ ----------------

        void LogState()
        {
            foreach (var data in AchievementManager.All)
            {
                if (!data.IsSecret || !data.HasHint) continue;
                Debug.Log("[実績ヒント] " + data.Id + " / " + MetricName(data.Definition.metric)
                          + " " + AchievementManager.CountOf(data.Definition.metric) + "/" + AchievementHints.ProgressThreshold(data)
                          + "　試合 " + AchievementManager.CountOf(AchievementMetric.MatchPlayed) + "/" + AchievementHints.MatchThreshold(data)
                          + "　解除=" + data.IsUnlocked
                          + "　ヒント=" + (AchievementHints.IsRevealed(data) ? "表示" : "伏せる")
                          + "　文=" + data.Hint);
            }
        }

        static string MetricName(AchievementMetric metric)
        {
            switch (metric)
            {
                case AchievementMetric.Hit: return "命中";
                case AchievementMetric.HammerMiss: return "ハンマーの空振り";
                case AchievementMetric.QuickWin: return "30秒以内の勝利";
                case AchievementMetric.MatchPlayed: return "試合数";
                default: return metric.ToString();
            }
        }
    }
}
#endif
