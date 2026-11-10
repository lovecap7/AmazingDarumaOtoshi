using System;
using System.Collections.Generic;
using System.Globalization;
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
        // メタ実績の数え直し中か（解除→数え直し→解除…の繰り返しを防ぐ）
        static bool s_refreshingMeta;
        // いまの連勝数（敗北で0。引き分けでは維持）
        static int s_winStreak;

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

        // データは変えずに「表示を作り直して」と知らせる（ヒントの確認など、見た目だけを更新したいとき）
        public static void NotifyChanged()
        {
            Initialize();
            Changed?.Invoke();
        }

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
            s_refreshingMeta = false;
            s_winStreak = 0;
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
            AchievementEvents.MatchLost += OnMatchLost;
            AchievementEvents.PassiveActivated += () => Report(AchievementMetric.PassiveActivated, 1);
            AchievementEvents.BlockTypesInStack += count => Report(AchievementMetric.BlockTypesInStack, count);
            AchievementEvents.Rally += count => Report(AchievementMetric.RallyBest, count);
            AchievementEvents.OpportunistKO += () => Report(AchievementMetric.OpportunistKO, 1);
            AchievementEvents.ChainKO += () => Report(AchievementMetric.ChainKO, 1);
            AchievementEvents.MultiKO += count => Report(AchievementMetric.MultiKOBest, count);
            AchievementEvents.NoDamageWin += () => Report(AchievementMetric.NoDamageWin, 1);
            AchievementEvents.HammerMiss += () => Report(AchievementMetric.HammerMiss, 1);
            AchievementEvents.QuickWin += () => Report(AchievementMetric.QuickWin, 1);
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
            int number = 0;
            foreach (var definition in AchievementCatalog.Definitions)
            {
                number++;
                if (!ids.Add(definition.id))
                {
                    Debug.LogError("[Achievement] 実績IDが重複しています: " + definition.id);
                }
                s_all.Add(new AchievementData(definition, number));
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
            metric == AchievementMetric.ChainBest
            || metric == AchievementMetric.StackBest
            || metric == AchievementMetric.BlockTypesInStack
            || metric == AchievementMetric.WinStreakBest
            || metric == AchievementMetric.RallyBest
            || metric == AchievementMetric.MultiKOBest;

        // 数の種類ごとの累計（進捗の上限で止めていない、生の回数）。シークレット実績のヒントの切り替えにも使う
        public static int CountOf(AchievementMetric metric)
        {
            Initialize();
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
                // メタ実績（ほかの実績から決まるもの）は RefreshMetaAchievements で数える
                if (IsMetaMetric(data.Definition.metric)) continue;
                data.Progress = Mathf.Min(CountOf(data.Definition.metric), Mathf.Max(1, data.Target));
            }
            RefreshMetaAchievements();
            Changed?.Invoke();
        }

        static bool IsMetaMetric(AchievementMetric metric) =>
            metric == AchievementMetric.AllOtherUnlocked || metric == AchievementMetric.AllRarityUnlocked;

        // 「ほかのすべてを解除」「同じレアリティをすべて解除」のような、ほかの実績の状態から決まる実績を数え直す。
        // 解除済みの場合は進捗を満タンで見せる（条件を満たして解除されたため）
        static void RefreshMetaAchievements()
        {
            if (s_refreshingMeta) return;
            s_refreshingMeta = true;
            try
            {
                // レアリティ制覇が解除されると「完全なる制覇」の条件も満たす、のように連鎖するので、
                // 新しく解除される実績がなくなるまで繰り返す。
                bool unlockedAny = true;
                while (unlockedAny)
                {
                    unlockedAny = false;

                    // 自分（同じ条件の実績）以外の、すべての実績の数
                    int allTarget = 0, allCount = 0;
                    foreach (var data in s_all)
                    {
                        if (data.Definition.metric == AchievementMetric.AllOtherUnlocked) continue;
                        allTarget++;
                        if (data.IsUnlocked) allCount++;
                    }

                    s_counters[AchievementMetric.AllOtherUnlocked] = allCount;
                    foreach (var data in s_all)
                    {
                        if (data.Definition.metric != AchievementMetric.AllOtherUnlocked) continue;
                        data.Definition.target = Mathf.Max(1, allTarget);
                        data.Progress = data.IsUnlocked
                            ? data.Definition.target
                            : Mathf.Min(allCount, data.Definition.target);
                        if (!data.IsUnlocked && allTarget > 0 && allCount >= allTarget)
                        {
                            Unlock(data);
                            unlockedAny = true;
                        }
                    }

                    // レアリティ制覇：自分と同じレアリティの、自分以外の実績を数える。
                    // 実績の数が増減しても、そのレアリティの実績をすべて解除したかで判定できる。
                    foreach (var meta in s_all)
                    {
                        if (meta.Definition.metric != AchievementMetric.AllRarityUnlocked) continue;
                        int target = 0, count = 0;
                        foreach (var other in s_all)
                        {
                            if (other == meta) continue;
                            if (other.Definition.rarity != meta.Definition.rarity) continue;
                            if (other.Definition.metric == AchievementMetric.AllRarityUnlocked) continue;
                            target++;
                            if (other.IsUnlocked) count++;
                        }

                        meta.Definition.target = Mathf.Max(1, target);
                        meta.Progress = meta.IsUnlocked
                            ? meta.Definition.target
                            : Mathf.Min(count, meta.Definition.target);
                        if (!meta.IsUnlocked && target > 0 && count >= target)
                        {
                            Unlock(meta);
                            unlockedAny = true;
                        }
                    }
                }
            }
            finally
            {
                s_refreshingMeta = false;
            }
        }

        static void Unlock(AchievementData data)
        {
            if (data.IsUnlocked) return;
            data.IsUnlocked = true;
            // 解除した瞬間に日時を確定して保存する（表示するときに日時を入れ直さない）
            data.UnlockedAt = DateTime.UtcNow;
            // まだリザルトで見せていない、という印。リザルトで見せたときに MarkNotified で消す
            data.IsNotified = false;
            Save();
            Debug.Log("[Achievement] 解除: " + data.Id + " " + data.Name);
            Unlocked?.Invoke(data);
            // 解除のお知らせ。リザルトでまとめて見せる設定のときは、ゲーム中の通知は出さない
            // （通知そのものは残してあるので、設定を切り替えれば元に戻る）
            if (!ShowInResult) AchievementNotification.Enqueue(data);
            if (AllUnlocked)
            {
                Debug.Log("[Achievement] 全実績解除。報酬キャラクター「" + AchievementRewards.RewardCharacterName + "」が選べるようになりました");
            }
            // ほかの実績の解除で条件を満たす実績（「ほかのすべてを解除」など）を見直す
            RefreshMetaAchievements();
        }

        // 解除をゲーム中に通知せず、リザルトでまとめて見せるか（AchievementSettings で切り替える）
        public static bool ShowInResult
        {
            get
            {
                var settings = AchievementSettings.Instance;
                return settings == null || settings.ResultPresentation;
            }
        }

        //========================================
        // リザルトで見せる実績（未表示の解除済み）
        //========================================

        // まだリザルトで見せていない実績。解除した順（古い順）に返す。
        // リザルトの前にアプリを終了しても、保存した印が残っているので次回に出せる。
        public static List<AchievementData> PendingAchievements()
        {
            Initialize();
            var list = new List<AchievementData>();
            foreach (var data in s_all)
            {
                if (data.IsUnlocked && !data.IsNotified) list.Add(data);
            }
            list.Sort((a, b) =>
            {
                int byTime = a.UnlockedAt.CompareTo(b.UnlockedAt);
                return byTime != 0 ? byTime : a.Number.CompareTo(b.Number);
            });
            return list;
        }

        public static int PendingCount
        {
            get
            {
                int n = 0;
                foreach (var a in All) if (a.IsUnlocked && !a.IsNotified) n++;
                return n;
            }
        }

        // リザルトで見せ終わった印をつける（表示が終わってから呼ぶ）
        public static void MarkNotified(IEnumerable<AchievementData> shown)
        {
            Initialize();
            bool changed = false;
            foreach (var data in shown)
            {
                if (data == null || data.IsNotified) continue;
                data.IsNotified = true;
                changed = true;
            }
            if (changed) Save();
        }

        // 未表示をすべて「見せた」ことにする（デバッグ用）
        public static void MarkAllNotified() => MarkNotified(PendingAchievements());

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
            // 連勝は「人間側が勝った試合」で伸びる。引き分けでは維持し、敗北は OnMatchLost で0に戻す
            s_winStreak++;
            Report(AchievementMetric.WinStreakBest, s_winStreak);
            Save();
        }

        static void OnMatchLost()
        {
            Initialize();
            if (s_winStreak == 0) return;
            s_winStreak = 0;
            Save();
            Changed?.Invoke();
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
            public int winStreak;
            // リザルトで見せた実績のID。notifyVersion が 0 の古いセーブ（この項目が無い）は、
            // すべて見せた扱いにして、更新直後に昔の実績がまとめて流れないようにする
            public List<string> notified = new List<string>();
            public int notifyVersion;
        }

        // 保存形式の版。1 から「リザルトで見せたか」を持つ
        const int NotifyVersion = 1;

        public static void Save()
        {
            Initialize();
            var blob = new SaveBlob();
            foreach (var data in s_all)
            {
                if (!data.IsUnlocked) continue;
                blob.unlocked.Add(data.Id);
                blob.unlockedAt.Add(data.UnlockedAt.ToString("o"));
                if (data.IsNotified) blob.notified.Add(data.Id);
            }
            blob.notifyVersion = NotifyVersion;
            foreach (var pair in s_counters)
            {
                blob.counters.Add((int)pair.Key + "=" + pair.Value);
            }
            foreach (var id in s_usedCharacters)
            {
                blob.characters.Add(id);
            }
            blob.winStreak = s_winStreak;
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
                    s_winStreak = Mathf.Max(0, blob.winStreak);
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
                            // 日時が無い古いセーブは「不明」のまま。読み込み時刻で埋めない
                            data.UnlockedAt = DateTime.MinValue;
                        }
                    }
                    if (blob.unlockedAt != null)
                    {
                        // unlocked と同じ並びで日時が入っている
                        int n = Mathf.Min(blob.unlocked.Count, blob.unlockedAt.Count);
                        for (int i = 0; i < n; i++)
                        {
                            var data = FindInList(blob.unlocked[i]);
                            if (data != null && DateTime.TryParse(blob.unlockedAt[i], CultureInfo.InvariantCulture,
                                                                  DateTimeStyles.RoundtripKind, out var time))
                            {
                                data.UnlockedAt = time;
                            }
                        }
                    }

                    // リザルトで見せたか。古いセーブ（この項目が無い）は、すべて見せた扱いにする
                    if (blob.notifyVersion <= 0 || blob.notified == null)
                    {
                        foreach (var data in s_all)
                        {
                            if (data.IsUnlocked) data.IsNotified = true;
                        }
                    }
                    else
                    {
                        var notified = new HashSet<string>(blob.notified);
                        foreach (var data in s_all)
                        {
                            data.IsNotified = data.IsUnlocked && notified.Contains(data.Id);
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
            s_winStreak = 0;
            foreach (var data in s_all)
            {
                data.Progress = 0;
                data.IsUnlocked = false;
                data.UnlockedAt = DateTime.MinValue;
                data.IsNotified = false;
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
                data.IsNotified = false;
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
            s_winStreak = 0;
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
                        data.IsNotified = false;
                    }
                }
                if (resetProgress)
                {
                    s_counters.Clear();
                    s_usedCharacters.Clear();
                    s_winStreak = 0;
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
                blob.winStreak = 0;
            }
            bool empty = (blob.unlocked == null || blob.unlocked.Count == 0)
                      && (blob.counters == null || blob.counters.Count == 0)
                      && (blob.characters == null || blob.characters.Count == 0)
                      && blob.winStreak == 0;
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
                blob.unlockedAt.Add(DateTime.UtcNow.ToString("o"));
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
            // デバッグの全解除。リザルトで大量に流れないよう、見せた扱いにしておく
            foreach (var data in s_all) blob.notified.Add(data.Id);
            blob.notifyVersion = NotifyVersion;

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
            // 連勝は「現在の連勝数」も合わせる（このあと勝利したときに続きから数えられるように）
            if (metric == AchievementMetric.WinStreakBest) s_winStreak = progress;
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
