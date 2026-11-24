using UnityEngine;

namespace Yurube
{
    // レベルの調整値（2026-11-24 メンバー全員で決めた仕様）
    //  ・試合中に積んだ数（上から乗って拾った数の合計。やられても減らない）でレベルが上がる。最大 Lv5
    //  ・レベルが上がると全員共通で：攻撃速度・移動速度・弾速が上がる
    //  ・Lv2 から、キャラごとの能力（CharacterData.levelPerk）も上がる。Lv1 は全員同じ（無個性）
    // 実体は Assets/Yurube/Level/Resources/LevelParams.asset。値は使うたびに読むので、再生中に変えてもすぐ反映される
    [CreateAssetMenu(fileName = "LevelParams", menuName = "Yurube/Level Params")]
    public class LevelParams : ScriptableObject
    {
        public const int MaxLevel = 5;

        [Header("レベルアップに必要な、積んだ数の合計（Lv2・Lv3・Lv4・Lv5）")]
        public int[] stackedToLevel = { 3, 6, 10, 15 };

        [Header("全員共通（1レベル上がるごとに足す割合。0.05 なら +5%）")]
        [Tooltip("攻撃速度（ハンマーの薙ぎ払い・股抜きショットの振りが速くなる）")]
        [Min(0f)] public float attackSpeedPerLevel = 0.05f;
        [Tooltip("移動速度")]
        [Min(0f)] public float moveSpeedPerLevel = 0.05f;
        [Tooltip("弾速（股抜きショット・薙ぎ払いで飛ばす積み木の速さ）")]
        [Min(0f)] public float shotSpeedPerLevel = 0.05f;

        [Header("キャラごと（Lv2 から。1レベルごとに足す。Lv2 で1回分、Lv5 で4回分）")]
        [Tooltip("あかだる：弾速をさらに上げる割合")]
        [Min(0f)] public float perkShotSpeed = 0.1f;
        [Tooltip("あおだる：攻撃速度をさらに上げる割合")]
        [Min(0f)] public float perkAttackSpeed = 0.1f;
        [Tooltip("みだる：当てた相手の移動速度を下げる割合（0.1 なら Lv2 で 10% 遅く、Lv5 で 40% 遅く）")]
        [Range(0f, 0.25f)] public float perkSlow = 0.1f;
        [Tooltip("みだる：移動速度が下がっている時間（秒）")]
        [Min(0f)] public float perkSlowSeconds = 2f;
        [Tooltip("きだる：スタミナの効率を上げる割合（0.25 なら Lv5 で減り方が半分・回復が倍）")]
        [Min(0f)] public float perkStamina = 0.25f;
        [Tooltip("いわだる：撃った弾の耐久値を足す数（ほかの積み木に何回当たっても壊れずに進むか）")]
        [Min(0)] public int perkShotDurability = 1;
        [Tooltip("へびだる：Lv2 の、撃った弾のジグザグの角度（度。撃った向きから左右へこの角度で折れ曲がる。大きいほど横に大きく振れる）")]
        [Range(0f, 85f)] public float perkZigzagAngleBase = 35f;
        [Tooltip("へびだる：1レベルごとに足すジグザグの角度（度）。Lv5 = 基本 + 3回分")]
        [Min(0f)] public float perkZigzagAnglePerLevel = 13f;
        [Tooltip("へびだる：折れ曲がる間隔（秒）。短いほど細かいギザギザ、長いほど1本が長い大きなジグザグ")]
        [Min(0.02f)] public float perkZigzagInterval = 0.09f;
        [Tooltip("だるきんぐ：リーチを伸ばす割合")]
        [Min(0f)] public float perkReach = 0.1f;

        [Header("表示")]
        [Tooltip("頭の上に「Lv2」を出す")]
        public bool showLevel = true;

        static LevelParams s_instance;

        public static LevelParams Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                s_instance = Resources.Load<LevelParams>("LevelParams");
                if (s_instance == null) s_instance = CreateInstance<LevelParams>();
                return s_instance;
            }
        }

        // 積んだ数の合計からレベル（1〜5）
        public int LevelFor(int stacked)
        {
            int level = 1;
            if (stackedToLevel != null)
                for (int i = 0; i < stackedToLevel.Length && level < MaxLevel; i++)
                    if (stacked >= stackedToLevel[i]) level = i + 2;
            return level;
        }
    }
}
