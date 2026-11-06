using System.Collections.Generic;
using UnityEngine;

namespace Yurube.Achievements
{
    // 実績の一覧（最初に入れる21個）。
    // 実績を増やすときは CreateDefaults() に1行足すだけでよい。判定は AchievementMetric を見て
    // AchievementManager が共通で行うので、ほかのコードを直す必要はない。
    // ID はセーブに残るので、一度出したら変えない（変えると解除済みが消える）。
    // レアリティは表示だけに使う（解除条件には影響しない）。
    public static class AchievementCatalog
    {
        static List<AchievementDefinition> s_definitions;

        public static IReadOnlyList<AchievementDefinition> Definitions
        {
            get
            {
                if (s_definitions == null) s_definitions = CreateDefaults();
                return s_definitions;
            }
        }

        public static string CategoryName(AchievementCategory category)
        {
            switch (category)
            {
                case AchievementCategory.Match: return "試合";
                case AchievementCategory.Attack: return "攻撃";
                case AchievementCategory.Chain: return "連鎖";
                case AchievementCategory.Stack: return "積み木";
                default: return "挑戦";
            }
        }

        // ラボの絞り込みに使う並び
        public static readonly AchievementCategory[] Categories =
        {
            AchievementCategory.Match,
            AchievementCategory.Attack,
            AchievementCategory.Chain,
            AchievementCategory.Stack,
            AchievementCategory.Challenge,
        };

        // 実績IDから決まった内容を探す（ラボやデバッグ用）
        public static AchievementDefinition Find(string id)
        {
            foreach (var d in Definitions)
            {
                if (d.id == id) return d;
            }
            return null;
        }

        // ---------------- 20個の実績 ----------------
        // 引数：ID、名前、ポエム、カテゴリー、解除条件、条件の種類、目標数、レアリティ
        static List<AchievementDefinition> CreateDefaults()
        {
            var list = new List<AchievementDefinition>
            {
                // ---- 試合 ----
                new AchievementDefinition("first_match", "はじめの一歩",
                    "だるま落としデビュー。まずは一戦、転がしてみよう。",
                    AchievementCategory.Match, "初めて試合をプレイする", AchievementMetric.MatchPlayed, 1,
                    AchievementRarity.Bronze),

                new AchievementDefinition("play_10", "常連さん",
                    "10試合をプレイ。道場の常連さん、今日も元気。",
                    AchievementCategory.Match, "累計10試合プレイする", AchievementMetric.MatchPlayed, 10,
                    AchievementRarity.Bronze),

                new AchievementDefinition("play_50", "歴戦の猛者",
                    "50試合をプレイ。積み木の山もあなたを見上げている。",
                    AchievementCategory.Match, "累計50試合プレイする", AchievementMetric.MatchPlayed, 50,
                    AchievementRarity.Gold),

                new AchievementDefinition("first_win", "初勝利！",
                    "初めての勝利。この味、忘れない。",
                    AchievementCategory.Match, "初めて試合に勝利する", AchievementMetric.MatchWon, 1,
                    AchievementRarity.Bronze),

                new AchievementDefinition("win_10", "勝利の味",
                    "通算10勝。勝ち方はもう、体が覚えている。",
                    AchievementCategory.Match, "累計10回勝利する", AchievementMetric.MatchWon, 10,
                    AchievementRarity.Gold),

                // ---- 攻撃 ----
                new AchievementDefinition("first_hit", "初ヒット！",
                    "初めて相手に一撃。手ごたえ、あったでしょ？",
                    AchievementCategory.Attack, "初めて相手に攻撃を命中させる", AchievementMetric.Hit, 1,
                    AchievementRarity.Bronze),

                new AchievementDefinition("hit_100", "百発百中？",
                    "通算100発命中。はずす気、ありますか？",
                    AchievementCategory.Attack, "累計100回攻撃を命中させる", AchievementMetric.Hit, 100,
                    AchievementRarity.Gold),

                new AchievementDefinition("combo_10", "コンボの達人",
                    "1試合で10回命中。もう止まらない。",
                    AchievementCategory.Attack, "1試合中に10回攻撃を命中させる", AchievementMetric.HitInMatch, 10,
                    AchievementRarity.Gold),

                new AchievementDefinition("counter_first", "打ち返し入門",
                    "飛んできた積み木を、ハンマーでおかえし。",
                    AchievementCategory.Attack, "初めてカウンター・打ち返しを成功させる", AchievementMetric.Counter, 1,
                    AchievementRarity.Bronze),

                new AchievementDefinition("counter_20", "反撃の達人",
                    "通算20回の打ち返し。受け身はもう得意技。",
                    AchievementCategory.Attack, "累計20回カウンター・打ち返しを成功させる", AchievementMetric.Counter, 20,
                    AchievementRarity.Gold),

                new AchievementDefinition("counter_ko", "完璧な反撃",
                    "打ち返した積み木で相手の頭を撃破。",
                    AchievementCategory.Attack, "カウンター・打ち返しによって相手を撃破する", AchievementMetric.CounterKO, 1,
                    AchievementRarity.Platinum),

                // ---- 連鎖 ----
                new AchievementDefinition("chain_first", "連鎖の始まり",
                    "積み木が積み木を巻き込んだ。連鎖の幕開け。",
                    AchievementCategory.Chain, "初めて連鎖を発生させる", AchievementMetric.ChainBest, 1,
                    AchievementRarity.Bronze),

                new AchievementDefinition("chain_3", "連鎖のプロ",
                    "3個以上の連鎖。もう職人の域。",
                    AchievementCategory.Chain, "3個以上のブロックを連鎖させる", AchievementMetric.ChainBest, 3,
                    AchievementRarity.Bronze),

                new AchievementDefinition("chain_5", "連鎖マスター",
                    "5個以上の連鎖。見ているだけで気持ちいい。",
                    AchievementCategory.Chain, "5個以上のブロックを連鎖させる", AchievementMetric.ChainBest, 5,
                    AchievementRarity.Gold),

                new AchievementDefinition("chain_10", "大連鎖！",
                    "10個以上の大連鎖。もはやお祭り。",
                    AchievementCategory.Chain, "10個以上のブロックを連鎖させる", AchievementMetric.ChainBest, 10,
                    AchievementRarity.Platinum),

                // ---- 積み木 ----
                new AchievementDefinition("stack_5", "高く積み上げろ",
                    "自分の積み木を5個。いい調子！",
                    AchievementCategory.Stack, "自分のブロックを5個積み上げる", AchievementMetric.StackBest, 5,
                    AchievementRarity.Bronze),

                new AchievementDefinition("stack_10", "天まで届け",
                    "自分の積み木を10個。天まで届け！",
                    AchievementCategory.Stack, "自分のブロックを10個積み上げる", AchievementMetric.StackBest, 10,
                    AchievementRarity.Gold),

                new AchievementDefinition("clutch_win", "崖っぷちの勝利",
                    "積み木1個以下で勝利。よくぞ耐えた！",
                    AchievementCategory.Stack, "ブロックが1個以下の状態で試合に勝利する", AchievementMetric.WinWithLowStack, 1,
                    AchievementRarity.Platinum),

                // ---- 挑戦 ----
                new AchievementDefinition("first_special", "必殺技解禁！",
                    "初めて必殺技を発動。合図はボタンひとつ。",
                    AchievementCategory.Challenge, "初めて必殺技を発動する", AchievementMetric.SpecialActivated, 1,
                    AchievementRarity.Bronze),

                new AchievementDefinition("all_characters", "みんなの力",
                    "全キャラクターを一度ずつ使用。だるまの輪、完成。",
                    AchievementCategory.Challenge, "全キャラクターを最低1回ずつ使用する", AchievementMetric.CharacterUsedDistinct, 1,
                    AchievementRarity.Gold),

                // ---- メタ実績（ほかのすべての実績を解除すると解除される） ----
                // 目標の数は AchievementManager が「自分以外の実績の数」から自動で決める
                new AchievementDefinition("all_non_platinum", "やりこみの達人",
                    "ほかの実績はぜんぶ制覇。最後に残ったのは、この輝き。",
                    AchievementCategory.Challenge, "「やりこみの達人」以外のすべての実績を解除する", AchievementMetric.AllOtherUnlocked, 1,
                    AchievementRarity.Platinum),
            };
            return list;
        }
    }
}
