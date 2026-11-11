using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;
using Yurube.Achievements;

namespace Yurube.Story
{
    // ストーリーの章を始める係。メニューの「ストーリーモード」から呼ばれる。
    //  ・章データ（JSON）を読み、いまの章として覚える
    //  ・プレイヤー1人＋CPUの設定を MatchSetup に入れ、ステージを指定してシーンを読み込む
    // 対戦の開始そのものは既存の DarumaMatch / MatchSetup の仕組みをそのまま使う。
    public static class StoryLauncher
    {
        public const string PrologueId = "prologue";

        // 序章を最初から始める
        public static void StartPrologue() => Start(PrologueId);

        public static void Start(string chapterId)
        {
            var chapter = StoryData.Load(chapterId);
            if (chapter == null) return;

            StorySession.Begin(chapter);

            var entries = new List<MatchSetup.Entry>();
            entries.Add(new MatchSetup.Entry
            {
                playerIndex = 0,
                isCpu = false,
                deviceId = PickDeviceId(),
                deviceName = PickDeviceName(),
                character = FindCharacter(chapter.playerCharacter),
            });

            int cpuCount = chapter.cpu != null ? chapter.cpu.Count : 0;
            for (int i = 0; i < cpuCount; i++)
            {
                var cpu = chapter.cpu[i];
                if (cpu == null) continue;
                entries.Add(new MatchSetup.Entry
                {
                    playerIndex = i + 1,
                    isCpu = true,
                    cpuLevel = Mathf.Clamp(cpu.level <= 0 ? 1 : cpu.level, 1, MatchSetup.MaxCpuLevel),
                    deviceName = "CPU",
                    character = FindCharacter(cpu.character),
                    cpuNumber = cpuCount > 1 ? i + 1 : 0,
                });
            }

            // ルール（章データで変えられる）
            var rules = MatchSetup.Rules;
            rules.mode = ParseMode(chapter.rules);
            rules.stocks = Mathf.Max(1, chapter.rules.stocks);
            rules.stockTimeLimit = rules.mode == MatchSetup.MatchMode.Stock ? Mathf.Max(0f, chapter.rules.timeLimit) : 0f;
            rules.timeLength = rules.mode == MatchSetup.MatchMode.Time ? Mathf.Max(30f, chapter.rules.timeLimit) : 120f;
            rules.ghostTimeLimit = rules.mode == MatchSetup.MatchMode.Ghost ? Mathf.Max(0f, chapter.rules.timeLimit) : 0f;
            rules.teams = false;

            MatchSetup.Set(entries, null);
            Debug.Log("[Story] 開始: " + chapterId + " → " + MatchSetup.Describe());

            // ストーリーでは既存のリザルト画面へ行かせない（勝敗は StoryMatchDirector が扱う）
            DarumaMatch.AutoReturnToSelect = false;

            Nakagawa.StageSwitcher.RequestStage(chapter.stageId);
            Fader.Load(string.IsNullOrEmpty(chapter.sceneName) ? "AllStages" : chapter.sceneName);
        }

        static MatchSetup.MatchMode ParseMode(StoryRules rules)
        {
            string mode = rules != null ? rules.mode : null;
            if (mode == "Time") return MatchSetup.MatchMode.Time;
            if (mode == "Ghost") return MatchSetup.MatchMode.Ghost;
            return MatchSetup.MatchMode.Stock;
        }

        // 章データのキャラIDから CharacterData を探す（見つからなければ遊べる最初のキャラ）
        static CharacterData FindCharacter(string id)
        {
            var settings = AchievementSettings.Instance;
            var roster = settings != null ? settings.Roster : null;
            if (roster != null && roster.characters != null)
            {
                foreach (var c in roster.characters)
                {
                    if (c == null || string.IsNullOrEmpty(c.id)) continue;
                    if (c.id == id) return c;
                }
                foreach (var c in roster.characters)
                {
                    if (c != null && c.prefab != null) return c;
                }
            }
            Debug.LogWarning("[Story] キャラクターが見つかりません: " + id);
            return null;
        }

        // プレイヤーの入力機器（パッドがあればパッド、無ければキーボード）
        static int PickDeviceId()
        {
            if (Gamepad.all.Count > 0) return Gamepad.all[0].deviceId;
            if (Keyboard.current != null) return Keyboard.current.deviceId;
            return -1;
        }

        static string PickDeviceName()
        {
            if (Gamepad.all.Count > 0) return Gamepad.all[0].displayName;
            if (Keyboard.current != null) return Keyboard.current.displayName;
            return "なし";
        }
    }
}
