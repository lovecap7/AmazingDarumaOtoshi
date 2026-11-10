using System.Collections.Generic;
using UnityEngine;

namespace Yurube.Achievements
{
    // 実績システムの設定。Assets/Yurube/Achievements/Resources/AchievementSettings.asset を Inspector で編集する。
    // 無くても動く（そのときは既定値）。
    [CreateAssetMenu(menuName = "Daruma/Achievement Settings", fileName = "AchievementSettings")]
    public class AchievementSettings : ScriptableObject
    {
        [Header("キャラクター")]
        [Tooltip("「みんなの力」で使うキャラクター一覧（セレクト画面と同じ SelectRoster）。未設定なら達成できない")]
        [SerializeField] SelectRoster m_roster;

        [Header("解除の通知")]
        [Tooltip("通知を出す")]
        [SerializeField] bool m_notifications = true;
        [Tooltip("1件の通知を出しておく秒数")]
        [SerializeField, Min(0.5f)] float m_notificationSeconds = 3.5f;
        [Tooltip("解除したときの音。未設定ならメニューの決定音")]
        [SerializeField] AudioClip m_unlockSound;
        [Tooltip("アイコンの画像が無い実績に使う画像。未設定ならカテゴリーの色で代用")]
        [SerializeField] Sprite m_defaultIcon;
        [Tooltip("通知の飾りに使う色")]
        [SerializeField] Color m_accentColor = new Color(0.96f, 0.60f, 0.18f);

        [Header("リザルトで見せる実績")]
        [Tooltip("解除をゲーム中に通知せず、試合後のリザルトでまとめて見せる（オフにすると今までの右上の通知に戻る）")]
        [SerializeField] bool m_resultPresentation = true;
        [Tooltip("リザルトで個別に見せる最大件数。多いぶんは「ほか○件」にまとめる")]
        [SerializeField, Min(1)] int m_resultMaxCards = 3;
        [Tooltip("カードが右から入ってくる秒数")]
        [SerializeField, Min(0.05f)] float m_resultSlideSeconds = 0.45f;
        [Tooltip("カードを出しておく秒数（この間に入力が無ければ次へ）")]
        [SerializeField, Min(0.2f)] float m_resultHoldSeconds = 2.2f;
        [Tooltip("最後の進捗ゲージが伸びる秒数")]
        [SerializeField, Min(0.2f)] float m_resultProgressSeconds = 1.8f;
        [Tooltip("レアリティ別の演出。空なら既定の演出で動く（差し替えたいときだけ入れる）")]
        [SerializeField] List<AchievementRevealStyle> m_revealStyles = new List<AchievementRevealStyle>();

        static AchievementSettings s_instance;

        public static AchievementSettings Instance
        {
            get
            {
                if (s_instance == null) s_instance = Resources.Load<AchievementSettings>("AchievementSettings");
                return s_instance;
            }
        }

        // ドメインの再読み込みをしない設定でも、再生のたびに読み直す
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_instance = null;
        }

        public SelectRoster Roster => m_roster;
        public bool NotificationsEnabled => m_notifications;
        public float NotificationSeconds => m_notificationSeconds;
        public AudioClip UnlockSound => m_unlockSound;
        public Sprite DefaultIcon => m_defaultIcon;
        public Color AccentColor => m_accentColor;

        // ---- リザルトで見せる実績 ----
        public bool ResultPresentation => m_resultPresentation;
        public int ResultMaxCards => Mathf.Max(1, m_resultMaxCards);
        public float ResultSlideSeconds => Mathf.Max(0.05f, m_resultSlideSeconds);
        public float ResultHoldSeconds => Mathf.Max(0.2f, m_resultHoldSeconds);
        public float ResultProgressSeconds => Mathf.Max(0.2f, m_resultProgressSeconds);

        // レアリティ別の演出。入っていなければ既定の演出を作って返す（無くても動く）
        public AchievementRevealStyle StyleFor(AchievementRarity rarity)
        {
            if (m_revealStyles != null)
            {
                foreach (var style in m_revealStyles)
                {
                    if (style != null && style.Rarity == rarity) return style;
                }
            }
            return AchievementRevealStyle.GetDefault(rarity);
        }

        // 実際に遊べるキャラクターの数（「みんなの力」の目標）。
        // セレクトの一覧には、まだプレハブが無いキャラも並んでいるので、遊べるものだけ数える
        public int CharacterCount
        {
            get
            {
                if (m_roster == null || m_roster.characters == null) return 0;
                var ids = new HashSet<string>();
                foreach (var c in m_roster.characters)
                {
                    if (IsPlayable(c)) ids.Add(c.id);
                }
                return ids.Count;
            }
        }

        // 遊べるキャラクター（ラボの「使用履歴を記録」で使う）
        public IReadOnlyList<CharacterData> Characters
        {
            get
            {
                var list = new List<CharacterData>();
                var ids = new HashSet<string>();
                if (m_roster == null || m_roster.characters == null) return list;
                foreach (var c in m_roster.characters)
                {
                    if (IsPlayable(c) && ids.Add(c.id)) list.Add(c);
                }
                return list;
            }
        }

        static bool IsPlayable(CharacterData data) =>
            data != null && data.prefab != null && !string.IsNullOrEmpty(data.id);
    }
}
