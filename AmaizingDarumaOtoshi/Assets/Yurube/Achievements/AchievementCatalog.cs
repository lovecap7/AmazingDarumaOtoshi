using System.Collections.Generic;
using UnityEngine;

namespace Yurube.Achievements
{
    // 実績の一覧（40個）。
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
                case AchievementCategory.Story: return "ストーリー";
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
            AchievementCategory.Story,
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

        // ---------------- 20個の通常実績 ＋ 4個のメタ実績（レアリティ制覇）＋ 1個の最高位 ＋ 1個のシークレット ＋ 追加13個 ----------------
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
                    AchievementRarity.Silver),

                new AchievementDefinition("combo_10", "コンボの達人",
                    "1試合で10回命中。もう止まらない。",
                    AchievementCategory.Attack, "1試合中に10回攻撃を命中させる", AchievementMetric.HitInMatch, 10,
                    AchievementRarity.Silver),

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
                    AchievementRarity.Silver),

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
                    AchievementRarity.Silver),

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

                // ---- レアリティ制覇（メタ実績） ----
                // 目標の数は AchievementManager が「同じレアリティのほかの実績の数」から自動で決める。
                // レアリティごとの実績数が増減しても、そのレアリティをすべて解除したかどうかで判定される。
                new AchievementDefinition("bronze_master", "銅の覇者",
                    "銅の実績をぜんぶ制覇。最初の一歩を、ぜんぶ踏みしめた。",
                    AchievementCategory.Challenge, "すべての銅実績を解除する", AchievementMetric.AllRarityUnlocked, 1,
                    AchievementRarity.Bronze, true),

                new AchievementDefinition("silver_master", "銀の覇者",
                    "銀の実績をぜんぶ制覇。中級者の肩書き、しっかり手にした。",
                    AchievementCategory.Challenge, "すべての銀実績を解除する", AchievementMetric.AllRarityUnlocked, 1,
                    AchievementRarity.Silver, true),

                new AchievementDefinition("gold_master", "黄金の覇者",
                    "金の実績をぜんぶ制覇。黄金にふさわしい立ち回り、ここに極まれり。",
                    AchievementCategory.Challenge, "すべての金実績を解除する", AchievementMetric.AllRarityUnlocked, 1,
                    AchievementRarity.Gold, true),

                new AchievementDefinition("platinum_master", "白金の覇者",
                    "プラチナの実績をぜんぶ制覇。白銀の輝きは、熟練の証。",
                    AchievementCategory.Challenge, "すべてのプラチナ実績を解除する", AchievementMetric.AllRarityUnlocked, 1,
                    AchievementRarity.Platinum, true),

                // ---- 最高位（ほかのすべての実績を解除すると解除される） ----
                // 目標の数は AchievementManager が「自分以外の実績の数」から自動で決める。
                // ID は昔のセーブを消さないために変えない（名前だけダイヤモンドの「完全なる制覇」に変更）。
                new AchievementDefinition("all_non_platinum", "完全なる制覇",
                    "ほかの実績はぜんぶ制覇。最後に残ったのは、この紫の輝き。",
                    AchievementCategory.Challenge, "すべての実績を解除する", AchievementMetric.AllOtherUnlocked, 1,
                    AchievementRarity.Diamond),

                // ---- シークレット実績（未解除の間は名前・条件・レアリティを伏せる） ----
                // 未解除の間はしきい値まで何も出さず、超えたらヒントを1つだけ出す（AchievementHints）。
                // 引数の後ろ3つ：ヒント、ヒントを出し始める進捗、ヒントを出し始める試合数（0で既定）
                new AchievementDefinition("secret_1000_hits", "千本ノック",
                    "千の一撃。打ち続けた者だけが辿り着く、静かな境地。",
                    AchievementCategory.Attack, "累計1000回攻撃を命中させる", AchievementMetric.Hit, 1000,
                    AchievementRarity.Platinum, false, true,
                    "一撃、また一撃。数えきれないほど当てた先に、何かがある。",
                    500),

                // ---- 追加13件（2026/11/08 追加実績案） ----
                // 既存の番号を動かさないよう、必ず末尾に追加する。
                new AchievementDefinition("first_loss", "転んでも大丈夫",
                    "初めての敗北。ここからが、本当のだるま落とし。",
                    AchievementCategory.Match, "初めて試合に敗北する", AchievementMetric.MatchLost, 1,
                    AchievementRarity.Bronze),

                new AchievementDefinition("passive_first", "個性、発動！",
                    "キャラの個性が目を覚ました。この子、ただ者じゃない。",
                    AchievementCategory.Challenge, "初めてキャラクターのパッシブを発動する", AchievementMetric.PassiveActivated, 1,
                    AchievementRarity.Bronze),

                new AchievementDefinition("stack_variety", "いろいろ積んでみた",
                    "色とりどりの積み木タワー。バランスも、気持ちも、カラフル。",
                    AchievementCategory.Stack, "1試合中に3種類以上のブロックを同時に積む", AchievementMetric.BlockTypesInStack, 3,
                    AchievementRarity.Bronze),

                new AchievementDefinition("counter_5", "返し名人",
                    "5回の打ち返し。ハンマーさばき、見えてきたね。",
                    AchievementCategory.Attack, "累計5回カウンター・打ち返しを成功させる", AchievementMetric.Counter, 5,
                    AchievementRarity.Silver),

                new AchievementDefinition("win_streak_3", "三連勝！",
                    "3連勝。流れは完全にあなたのもの。",
                    AchievementCategory.Match, "3試合連続で勝利する", AchievementMetric.WinStreakBest, 3,
                    AchievementRarity.Silver),

                new AchievementDefinition("rally_3", "ピンポン名人",
                    "行って、返って、また来た。積み木が行き交う名勝負。",
                    AchievementCategory.Attack, "同じ積み木を打ち返し合って3回続ける", AchievementMetric.RallyBest, 3,
                    AchievementRarity.Silver),

                new AchievementDefinition("opportunist", "漁夫の利",
                    "弱ったところに、すかさず一撃。勝てば官軍！",
                    AchievementCategory.Attack, "他プレイヤーの攻撃を受けた直後（3秒以内）の相手を撃破する", AchievementMetric.OpportunistKO, 1,
                    AchievementRarity.Silver, mode: AchievementMode.Versus),

                new AchievementDefinition("hit_500", "素振り五百回",
                    "通算500発命中。千の道のりも、半分まで来た。",
                    AchievementCategory.Attack, "累計500回攻撃を命中させる", AchievementMetric.Hit, 500,
                    AchievementRarity.Gold),

                new AchievementDefinition("chain_ko", "ドミノ倒し",
                    "自分の一撃が、連鎖して相手を崩した。狙い通り！",
                    AchievementCategory.Chain, "連鎖によって相手を撃破する", AchievementMetric.ChainKO, 1,
                    AchievementRarity.Gold),

                new AchievementDefinition("multi_ko", "一網打尽",
                    "一撃で二人まとめて。会場が、どっと沸いた。",
                    AchievementCategory.Chain, "1回の攻撃・連鎖で2人以上を同時に撃破する", AchievementMetric.MultiKOBest, 2,
                    AchievementRarity.Platinum, mode: AchievementMode.Versus),

                new AchievementDefinition("no_damage_win", "無傷の王者",
                    "かすり傷ひとつなし。あなたの前に、道はなかった。",
                    AchievementCategory.Match, "一度も被弾せずに試合に勝利する", AchievementMetric.NoDamageWin, 1,
                    AchievementRarity.Platinum),

                new AchievementDefinition("secret_whiff", "空振り三振",
                    "ブンッ、ブンッ。風だけが、あなたに応えてくれた。",
                    AchievementCategory.Attack, "累計50回ハンマーを空振りする", AchievementMetric.HammerMiss, 50,
                    AchievementRarity.Silver, false, true,
                    "ハンマーを振っても、何も起きない日がある。それも、たまにはいい。",
                    25),

                new AchievementDefinition("secret_quick_win", "電光石火",
                    "瞬きの間の決着。観客はまだ席に着いていた。",
                    AchievementCategory.Match, "試合開始から30秒以内に勝利する", AchievementMetric.QuickWin, 1,
                    AchievementRarity.Gold, false, true,
                    "瞬きしたら、もう終わっている。そんな試合を狙ってみよう。",
                    0, 30),

                // ---- ストーリー専用（2026/11/12 追加） ----
                // 第1章以降の章クリア実績は、storyChapter を変えた定義をここに足すだけで追加できる。
                new AchievementDefinition("story_prologue", "はじまりは、おにぎり",
                    "序章をクリア。おにぎりと根性で、部活の第一歩。",
                    AchievementCategory.Story, "序章をクリアする（だるま新聞まで見る）", AchievementMetric.StoryChapterCleared, 1,
                    AchievementRarity.Bronze, mode: AchievementMode.Story, storyChapter: "prologue"),

                new AchievementDefinition("story_chapter1", "窓ガラスと走り込み",
                    "第1章をクリア。走り込みの先に、地区大会優勝があった。",
                    AchievementCategory.Story, "第1章をクリアする（だるま新聞まで見る）", AchievementMetric.StoryChapterCleared, 1,
                    AchievementRarity.Silver, mode: AchievementMode.Story, storyChapter: "chapter1"),

                new AchievementDefinition("story_chapter2", "まっすぐじゃない",
                    "第2章をクリア。曲がる弾を、まっすぐな一撃で越えた。",
                    AchievementCategory.Story, "第2章をクリアする（だるま新聞まで見る）", AchievementMetric.StoryChapterCleared, 1,
                    AchievementRarity.Silver, mode: AchievementMode.Story, storyChapter: "chapter2"),
            };
            return list;
        }
    }
}
