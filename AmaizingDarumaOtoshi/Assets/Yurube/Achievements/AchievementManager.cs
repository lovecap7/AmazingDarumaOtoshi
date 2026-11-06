using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yurube.Achievements
{
    // 実績システムの中心。実績の一覧・進捗・解除状態を持ち、セーブと読み込みをする。
    // 解除の判定はここだけで行う。ゲーム本編も実績ラボも「AchievementEvents」に出来事を流すだけ。
    //
    // ・累計の実績（試合数・勝利数・命中数など）は試合をまたいで残る
    // ・1試合ぶんの実績（コンボの達人）は試合が始まるたびに 0 に戻る
    // ・連鎖・積み上げは「いままでで一番よい記録」を残す
    // ・解除済みの実績は、条件を満たし直しても通知しない
    public static class AchievementManager
    {
        // ふだんのセーブ先（ゲーム本編）
        public const string NormalStorageKey = "Achievements.Save.v1";
        // 実績ラボ専用のセーブ先（本編のデータと混ざらないように別にする）
        public const string DebugStorageKey = "Achievements.LabSave.v1";

        static readonly List<AchievementData> s_all = new List<AchievementData>();
        static readonly Dictionary<AchievementMetric, int> s_counters = new Dictionary<AchievementMetric, int>();
        static readonly HashSet<string> s_usedCharacters = new HashSet<string>();
        static string s_storageKey = NormalStorageKey;
        static bool s_ready;

        // 実績を解除した（通知・メニューの更新用）
        public static event Action<AchievementData> Unlocked;
        // 進捗や解除状態が変わった（一覧の更新用）
        public static event Action Changed;

        public static IReadOnlyList<AchievementData> All
        {
            get
            {
                Initialize();
                return s_all;
            }
        }

        public static string StorageKey => s_storageKey;
        public static int UnlockedCount
        {
            get
            {
                int n = 0;
                foreach (var a in All) if (a.IsUnlocked) n++;
                return n;
            }
        }

        // すべての実績を解除したか（報酬キャラクター「だるきんぐ」の解放条件）
        public static bool AllUnlocked
        {
            get
            {
                var all = All;
                if (all.Count == 0) return false;
                for (int i = 0; i < all.Count; i++)
                {
                    if (!all[i].IsUnlocked) return false;
                }
                return true;
            }
        }

        //========================================
        // 初期化・保存先
        //========================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_all.Clear();
            s_counters.Clear();
            s_usedCharacters.Clear();
            s_storageKey = NormalStorageKey;
            s_ready = false;
            Unlocked = null;
            Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() => Initialize();

        public static void Initialize()
        {
            if (s_ready) return;
            s_ready = true;
            BuildEntries();
            AchievementEvents.MatchStarted += OnMatchStarted;
            AchievementEvents.MatchFinished += OnMatchFinished;
            AchievementEvents.Hit += count => Report(AchievementMetric.Hit, count);
            AchievementEvents.Counter += () => Report(AchievementMetric.Counter, 1);
            AchievementEvents.CounterKO += () => Report(AchievementMetric.CounterKO, 1);
            AchievementEvents.Chain += length => Report(AchievementMetric.ChainBest, length);
            AchievementEvents.Stack += count => Report(AchievementMetric.StackBest, count);
            AchievementEvents.Special += () => Report(AchievementMetric.SpecialActivated, 1);
            AchievementEvents.CharacterUsed += ReportCharacterUsed;
            Load();
        }

        // 実績ラボはデバッグ専用の保存先に切り替えて使う。終わったら NormalStorageKey に戻す
        public static void SetStorage(string key, bool loadNow)
        {
            Initialize();
            if (string.IsNullOrEmpty(key)) key = NormalStorageKey;
            s_storageKey = key;
            if (loadNow) Load();
            else
            {
                ResetState();
                Changed?.Invoke();
            }
        }

        static void BuildEntries()
        {
            s_all.Clear();
            var ids = new HashSet<string>();
            foreach (var definition in AchievementCatalog.Definitions)
            {
                if (!ids.Add(definition.id))
                {
                    Debug.LogError("[Achievement] 実績IDが重複しています: " + definition.id);
                }
                s_all.Add(new AchievementData(definition));
            }
            ApplyDynamicTargets();
        }

        // 「みんなの力」の目標を、いま使えるキャラクターの数に合わせる
        static void ApplyDynamicTargets()
        {
            var settings = AchievementSettings.Instance;
            int characters = settings != null ? settings.CharacterCount : 0;
            foreach (var data in s_all)
            {
                if (data.Definition.metric != AchievementMetric.CharacterUsedDistinct) continue;
                data.Definition.target = Mathf.Max(1, characters > 0 ? characters : 7);
            }
        }

        //========================================
        // 読む
        //========================================

        public static AchievementData Get(string id)
        {
            Initialize();
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var data in s_all)
            {
                if (data.Id == id) return data;
            }
            return null;
        }

        public static bool IsUnlocked(string id)
        {
            var data = Get(id);
            return data != null && data.IsUnlocked;
        }

        //========================================
        // 進捗を数える
        //========================================

        public static void Report(AchievementMetric metric, int value = 1)
        {
            Initialize();
            if (value <= 0) return;

            s_counters.TryGetValue(metric, out int current);
            // 連鎖・積み上げは「一番よい記録」、それ以外は累計
            s_counters[metric] = IsBest(metric) ? Mathf.Max(current, value) : current + value;
            Refresh(metric);
        }

        public static void ReportCharacterUsed(string characterId)
        {
            Initialize();
            if (string.IsNullOrEmpty(characterId)) return;
            if (!s_usedCharacters.Add(characterId)) return;
            s_counters[AchievementMetric.CharacterUsedDistinct] = s_usedCharacters.Count;
            Refresh(AchievementMetric.CharacterUsedDistinct);
        }

        static bool IsBest(AchievementMetric metric) =>
            metric == AchievementMetric.ChainBest || metric == AchievementMetric.StackBest;

        static int CountOf(AchievementMetric metric)
        {
            s_counters.TryGetValue(metric, out int value);
            return value;
        }

        // 数えた種類の実績だけ、進捗と解除を見直す
        static void Refresh(AchievementMetric metric)
        {
            int count = CountOf(metric);
            foreach (var data in s_all)
            {
                if (data.Definition.metric != metric) continue;
                data.Progress = Mathf.Min(count, Mathf.Max(1, data.Target));
                if (!data.IsUnlocked && data.Target > 0 && count >= data.Target) Unlock(data);
            }
            Changed?.Invoke();
        }

        static void RefreshAll()
        {
            foreach (var data in s_all)
            {
                data.Progress = Mathf.Min(CountOf(data.Definition.metric), Mathf.Max(1, data.Target));
            }
            Changed?.Invoke();
        }

        static void Unlock(AchievementData data)
        {
            if (data.IsUnlocked) return;
            data.IsUnlocked = true;
            data.UnlockedAt = DateTime.Now;
            Save();
            Debug.Log("[Achievement] 解除: " + data.Id + " " + data.Name);
            Unlocked?.Invoke(data);
            AchievementNotification.Enqueue(data);
            if (AllUnlocked)
            {
                Debug.Log("[Achievement] 全実績解除。報酬キャラクター「" + AchievementRewards.RewardCharacterName + "」が選べるようになりました");
            }
        }

        //========================================
        // 試合の区切り
        //========================================

        static void OnMatchStarted()
        {
            Initialize();
            // 1試合ぶんの進捗を戻す
            s_counters[AchievementMetric.HitInMatch] = 0;
            Refresh(AchievementMetric.HitInMatch);
            ApplyDynamicTargets();
            Report(AchievementMetric.MatchPlayed, 1);
        }

        static void OnMatchFinished(bool won, bool lowStackWin)
        {
            Initialize();
            if (!won) return;
            Report(AchievementMetric.MatchWon, 1);
            if (lowStackWin) Report(AchievementMetric.WinWithLowStack, 1);
        }

        //========================================
        // セーブ・ロード
        //========================================

        [Serializable]
        class SaveBlob
        {
            public List<string> unlocked = new List<string>();
            public List<string> unlockedAt = new List<string>();
            public List<string> counters = new List<string>();
            public List<string> characters = new List<string>();
        }

        public static void Save()
        {
            Initialize();
            var blob = new SaveBlob();
            foreach (var data in s_all)
            {
                if (!data.IsUnlocked) continue;
                blob.unlocked.Add(data.Id);
                blob.unlockedAt.Add(data.UnlockedAt.ToString("o"));
            }
            foreach (var pair in s_counters)
            {
                blob.counters.Add((int)pair.Key + "=" + pair.Value);
            }
            foreach (var id in s_usedCharacters)
            {
                blob.characters.Add(id);
            }
            PlayerPrefs.SetString(s_storageKey, JsonUtility.ToJson(blob));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void Load()
        {
            Initialize();
            ResetState();
            string json = PlayerPrefs.GetString(s_storageKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                var blob = JsonUtility.FromJson<SaveBlob>(json);
                if (blob != null)
                {
                    if (blob.counters != null)
                    {
                        foreach (var line in blob.counters)
                        {
                            int split = line.IndexOf('=');
                            if (split <= 0) continue;
                            if (int.TryParse(line.Substring(0, split), out int metric) &&
                                int.TryParse(line.Substring(split + 1), out int value))
                            {
                                s_counters[(AchievementMetric)metric] = Mathf.Max(0, value);
                            }
                        }
                    }
                    if (blob.characters != null)
                    {
                        foreach (var id in blob.characters)
                        {
                            if (!string.IsNullOrEmpty(id)) s_usedCharacters.Add(id);
                        }
                    }
                    if (blob.unlocked != null)
                    {
                        foreach (var id in blob.unlocked)
                        {
                            var data = FindInList(id);
                            if (data == null) continue;
                            data.IsUnlocked = true;
                            data.UnlockedAt = DateTime.Now;
                        }
                    }
                    if (blob.unlockedAt != null)
                    {
                        // unlocked と同じ並びで日時が入っている
                        int n = Mathf.Min(blob.unlocked.Count, blob.unlockedAt.Count);
                        for (int i = 0; i < n; i++)
                        {
                            var data = FindInList(blob.unlocked[i]);
                            if (data != null && DateTime.TryParse(blob.unlockedAt[i], out var time)) data.UnlockedAt = time;
                        }
                    }
                }
            }
            RefreshAll();
        }

        static AchievementData FindInList(string id)
        {
            foreach (var data in s_all)
            {
                if (data.Id == id) return data;
            }
            return null;
        }

        static void ResetState()
        {
            s_counters.Clear();
            s_usedCharacters.Clear();
            foreach (var data in s_all)
            {
                data.Progress = 0;
                data.IsUnlocked = false;
                data.UnlockedAt = DateTime.MinValue;
            }
        }

        public static bool HasSave() => PlayerPrefs.HasKey(s_storageKey);

        // 指定した保存先にデータがあるか（ラボから本編のデータを確かめる用）
        public static bool HasSave(string key) => !string.IsNullOrEmpty(key) && PlayerPrefs.HasKey(key);

        //========================================
        // デバッグ用（製品版には入らない）
        //========================================

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 解除済み・進捗・使用キャラをすべて消す
        public static void DebugResetAll()
        {
            Initialize();
            ResetState();
            RefreshAll();
            Save();
        }

        // 解除状態だけを戻す（進捗は残す）
        public static void DebugResetUnlocked()
        {
            Initialize();
            foreach (var data in s_all)
            {
                data.IsUnlocked = false;
                data.UnlockedAt = DateTime.MinValue;
            }
            Save();
            Changed?.Invoke();
        }

        // 進捗だけを戻す（解除状態は残す）
        public static void DebugResetProgress()
        {
            Initialize();
            s_counters.Clear();
            s_usedCharacters.Clear();
            RefreshAll();
            Save();
        }

        // 保存先を指定してリセットする。実績ラボから「本編のデータ」も消せるようにするためのもの。
        // いま使っている保存先（ラボならラボのデータ）は、画面の表示もその場で合わせる。
        // 別の保存先（ラボから見た本編のデータ）は、保存されている内容だけを書き換える。
        // resetUnlocked：解除状態を戻す　resetProgress：進捗（累計・使用キャラ）を戻す
        public static void DebugResetStorage(string key, bool resetUnlocked, bool resetProgress)
        {
            Initialize();
            if (string.IsNullOrEmpty(key)) return;

            if (key == s_storageKey)
            {
                if (resetUnlocked)
                {
                    foreach (var data in s_all)
                    {
                        data.IsUnlocked = false;
                        data.UnlockedAt = DateTime.MinValue;
                    }
                }
                if (resetProgress)
                {
                    s_counters.Clear();
                    s_usedCharacters.Clear();
                }
                RefreshAll();
                Save();
                return;
            }

            // いま表示していない保存先：保存されている中身だけを書き換える
            string json = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(json)) return;
            var blob = JsonUtility.FromJson<SaveBlob>(json);
            if (blob == null) return;
            if (resetUnlocked)
            {
                blob.unlocked?.Clear();
                blob.unlockedAt?.Clear();
            }
            if (resetProgress)
            {
                blob.counters?.Clear();
                blob.characters?.Clear();
            }
            bool empty = (blob.unlocked == null || blob.unlocked.Count == 0)
                      && (blob.counters == null || blob.counters.Count == 0)
                      && (blob.characters == null || blob.characters.Count == 0);
            if (empty) PlayerPrefs.DeleteKey(key);
            else PlayerPrefs.SetString(key, JsonUtility.ToJson(blob));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // 保存先を指定して、その保存データを全解除にする（報酬キャラの解放を試す用）。
        // いま使っている保存先なら、ふつうの解除処理（通知あり）で全解除する。
        public static void DebugUnlockAllStorage(string key)
        {
            Initialize();
            if (string.IsNullOrEmpty(key)) return;
            if (key == s_storageKey)
            {
                DebugUnlockAll();
                return;
            }

            // 別の保存先：保存データを全解除の内容で作り直す
            var blob = new SaveBlob();
            var counters = new Dictionary<AchievementMetric, int>();
            var usedCharacters = new List<string>();
            foreach (var data in s_all)
            {
                blob.unlocked.Add(data.Id);
                blob.unlockedAt.Add(DateTime.Now.ToString("o"));
                int target = Mathf.Max(1, data.Target);

                var metric = data.Definition.metric;
                counters.TryGetValue(metric, out int current);
                if (target > current) counters[metric] = target;

                if (metric == AchievementMetric.CharacterUsedDistinct)
                {
                    for (int i = usedCharacters.Count; i < target; i++) usedCharacters.Add("debug_" + i);
                }
            }
            foreach (var pair in counters) blob.counters.Add((int)pair.Key + "=" + pair.Value);
            blob.characters.AddRange(usedCharacters);

            PlayerPrefs.SetString(key, JsonUtility.ToJson(blob));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // 保存先のデータを丸ごと消す（消したあとは「保存データなし」に戻る）
        public static void DebugClearStorage(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            Initialize();
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            if (key == s_storageKey)
            {
                ResetState();
                RefreshAll();
            }
            Changed?.Invoke();
        }

        // 選択中の実績を解除する（ふつうの解除と同じ道を通るので、通知も出る）
        public static void DebugUnlock(AchievementData data)
        {
            Initialize();
            if (data == null) return;
            int target = Mathf.Max(1, data.Target);
            EnsureCounterAtLeast(data.Definition.metric, target);
            data.Progress = target;
            Unlock(data);
            Changed?.Invoke();
        }

        public static void DebugUnlockAll()
        {
            Initialize();
            foreach (var data in s_all) DebugUnlock(data);
        }

        // 進捗を直接書き換える（UI と進捗バーの見た目を確かめるとき用）
        public static void DebugSetProgress(AchievementData data, int progress)
        {
            Initialize();
            if (data == null) return;
            progress = Mathf.Max(0, progress);
            var metric = data.Definition.metric;
            if (metric == AchievementMetric.CharacterUsedDistinct)
            {
                s_usedCharacters.Clear();
                for (int i = 0; i < progress; i++) s_usedCharacters.Add("debug_" + i);
                s_counters[metric] = s_usedCharacters.Count;
            }
            else
            {
                s_counters[metric] = progress;
            }
            // 同じ条件を共有する実績（試合数など）もまとめて表示を合わせる
            RefreshAll();
            // 直接書き換えでは解除しない（解除は「解除する」ボタンで、ふつうの解除を通す）
        }

        static void EnsureCounterAtLeast(AchievementMetric metric, int value)
        {
            s_counters.TryGetValue(metric, out int current);
            if (current >= value) return;
            if (metric == AchievementMetric.CharacterUsedDistinct)
            {
                for (int i = s_usedCharacters.Count; i < value; i++) s_usedCharacters.Add("debug_" + i);
                s_counters[metric] = s_usedCharacters.Count;
                return;
            }
            s_counters[metric] = value;
        }
#endif
    }
}
