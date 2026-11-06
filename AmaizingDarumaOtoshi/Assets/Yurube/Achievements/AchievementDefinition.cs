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

        [Tooltip("解除に必要な数。CharacterUsedDistinct は 0 にすると全キャラクター数になる")]
        [Min(1)] public int target = 1;

        [Tooltip("レアリティ（銅・銀・金・プラチナ）。表示だけに使う")]
        public AchievementRarity rarity = AchievementRarity.Bronze;

        [Tooltip("通知・一覧に出すアイコン（未設定ならカテゴリーの色で代用）")]
        public Sprite icon;

        public AchievementDefinition() { }

        public AchievementDefinition(string id, string name, string poem, AchievementCategory category,
                                     string condition, AchievementMetric metric, int target,
                                     AchievementRarity rarity = AchievementRarity.Bronze)
        {
            this.id = id;
            this.name = name;
            this.poem = poem;
            this.category = category;
            this.condition = condition;
            this.metric = metric;
            this.target = target;
            this.rarity = rarity;
        }

        public string CategoryName => AchievementCatalog.CategoryName(category);
    }
}
