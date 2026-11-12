using System;
using UnityEngine;

namespace Yurube.Achievements
{
    // 実績の分類。ラボの絞り込みと、通知アイコンの色に使う
    public enum AchievementCategory
    {
        Match,      // 試合
        Attack,     // 攻撃
        Chain,      // 連鎖
        Stack,      // 積み木
        Challenge,  // 挑戦
        Story,      // ストーリー
    }

    // 実績を解除できるモード。既定は「両方」
    public enum AchievementMode
    {
        Both,     // 対戦でもストーリーでも解除できる
        Versus,   // 対戦でのみ解除できる（人数条件など）
        Story,    // ストーリーでのみ解除できる
    }

    // 実績の解除条件の種類。
    // 実績を増やすときは、この中から選んで目標の数(target)を決めるだけでよい（判定のコードは増やさない）。
    // 新しい種類の条件を足すときだけ、ここに1行足して AchievementManager に数え方を書く。
    public enum AchievementMetric
    {
        MatchPlayed,          // 試合をプレイした回数（累計）
        MatchWon,             // 勝利した回数（累計）
        Hit,                  // 攻撃を命中させた回数（累計）
        HitInMatch,           // 1試合中に攻撃を命中させた回数（試合開始でリセット）
        Counter,              // カウンター・打ち返しの成功回数（累計）
        CounterKO,            // カウンター・打ち返しによる撃破（累計）
        ChainBest,            // いままでで一番長い連鎖（最大値）
        StackBest,            // いままでで一番高く積んだ積み木の数（最大値）
        WinWithLowStack,      // 積み木1個以下での勝利（累計）
        SpecialActivated,     // 必殺技を発動した回数（累計）
        CharacterUsedDistinct,// 使ったことのあるキャラクターの種類数
        AllOtherUnlocked,     // ほかのすべての実績を解除した数（メタ実績用。目標は自動で決まる）
        AllRarityUnlocked,    // 同じレアリティの実績をすべて解除した数（メタ実績用。目標は自動で決まる）

        // ---- 追加実績用（セーブの数値が変わらないよう、必ず末尾に足す） ----
        MatchLost,            // 敗北した回数（累計。引き分けは含めない）
        PassiveActivated,     // パッシブが初めて発動した回数（1回きり）
        BlockTypesInStack,    // 1つの山に同時に積まれている積み木の種類数（最大値）
        WinStreakBest,        // 連勝数の最高記録（敗北で現在値は0に戻る）
        RallyBest,            // 同じ積み木を打ち返した最大ラリー数
        OpportunistKO,        // 他の人に傷つけられた相手を撃破した回数（累計）
        ChainKO,              // 連鎖によって相手を撃破した回数（累計）
        MultiKOBest,          // 1回の攻撃・連鎖で同時に撃破した最大人数
        NoDamageWin,          // 一度も被弾せずに勝利した回数（累計）
        HammerMiss,           // ハンマーを空振りした回数（累計）
        QuickWin,             // 試合開始から一定時間以内に勝利した回数（累計）
        StoryChapterCleared,  // 指定した章をクリアしたか（storyChapter と組み合わせる）
    }

    // 実績1つ分の「決まった内容」。進捗や解除状態は AchievementData が持つ。
    // 実績を追加するときは AchievementCatalog の一覧に1つ足す（ID は重複させない）。
    [Serializable]
    public class AchievementDefinition
    {
        [Tooltip("セーブに使う、変わらない名前（英数字）。重複させない")]
        public string id;

        [Tooltip("プレイヤーに見せる実績名")]
        public string name;

        [Tooltip("解除したときのひとこと（ポエム）。メニューの「じっせき」でだけ見せる。ゲーム中の通知には出さない")]
        [TextArea(1, 3)] public string poem;

        public AchievementCategory category;

        [Tooltip("一覧に出す解除条件の文。ゲーム中の通知にもこれを出す")]
        [TextArea(1, 3)] public string condition;

        [Tooltip("解除条件の種類")]
        public AchievementMetric metric;

        [Tooltip("解除に必要な数。CharacterUsedDistinct とメタ実績（AllOtherUnlocked / AllRarityUnlocked）は自動で決まる")]
        [Min(1)] public int target = 1;

        [Tooltip("レアリティ（銅・銀・金・プラチナ・ダイヤモンド）。表示だけに使う")]
        public AchievementRarity rarity = AchievementRarity.Bronze;

        [Tooltip("通常のレアリティ別集計に含めない「特別実績」（レアリティ制覇など）")]
        public bool special;

        [Tooltip("非公開実績。未解除の間は名前・説明・条件・アイコンを隠す")]
        public bool secret;

        [Tooltip("解除できるモード（既定は両方）")]
        public AchievementMode mode = AchievementMode.Both;

        [Tooltip("章クリア実績の章ID（StoryChapterCleared のときだけ使う）")]
        public string storyChapter;

        [Tooltip("通知・一覧に出すアイコン（未設定ならカテゴリーの色で代用）")]
        public Sprite icon;

        // ---- シークレット実績のヒント ----
        // 未解除のシークレット実績では、しきい値に達するまで条件もヒントも伏せる。
        // しきい値を超えると、条件の代わりにこのヒントだけを出す（AchievementHints が見る）。

        [Tooltip("ヒント。シークレット実績で、しきい値を超えたときだけ出す（それまでは完全に伏せる）")]
        [TextArea(1, 3)] public string hint;

        [Tooltip("ヒントを出し始める進捗の数。0 なら目標の半分（千本ノックなら500）")]
        [Min(0)] public int hintAtProgress;

        [Tooltip("ヒントを出し始める試合数のしきい値。0 なら既定値（AchievementHints.DefaultMatchThreshold）")]
        [Min(0)] public int hintAtMatches;

        public AchievementDefinition() { }

        public AchievementDefinition(string id, string name, string poem, AchievementCategory category,
                                     string condition, AchievementMetric metric, int target,
                                     AchievementRarity rarity = AchievementRarity.Bronze,
                                     bool special = false, bool secret = false,
                                     string hint = null,
                                     int hintAtProgress = 0, int hintAtMatches = 0,
                                     AchievementMode mode = AchievementMode.Both,
                                     string storyChapter = null)
        {
            this.id = id;
            this.name = name;
            this.poem = poem;
            this.category = category;
            this.condition = condition;
            this.metric = metric;
            this.target = target;
            this.rarity = rarity;
            this.special = special;
            this.secret = secret;
            this.hint = hint;
            this.hintAtProgress = hintAtProgress;
            this.hintAtMatches = hintAtMatches;
            this.mode = mode;
            this.storyChapter = storyChapter;
        }

        public string CategoryName => AchievementCatalog.CategoryName(category);
    }
}
