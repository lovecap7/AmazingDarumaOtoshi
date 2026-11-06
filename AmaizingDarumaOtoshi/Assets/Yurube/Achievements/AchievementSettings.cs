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
