using UnityEngine;

namespace Yurube.Achievements
{
    // レアリティごとの「リザルトでの見せ方」。演出の強さをここだけで決める。
    //  ・Assets に作って AchievementSettings の「レアリティ別の演出」に入れると、その設定が使われる
    //  ・入れなければ GetDefault(rarity) の既定値で動く（作らなくても動く）
    // 演出の中身（スライド・光の帯・フラッシュ・きらめき・音）は AchievementResultPresenter が行う。
    [CreateAssetMenu(menuName = "Daruma/Achievement Reveal Style", fileName = "AchievementRevealStyle")]
    public class AchievementRevealStyle : ScriptableObject
    {
        [Tooltip("どのレアリティの演出か")]
        [SerializeField] AchievementRarity m_rarity = AchievementRarity.Bronze;

        [Header("動き")]
        [Tooltip("右から入ってくる秒数（0 なら AchievementSettings の値）")]
        [SerializeField, Min(0f)] float m_slideSeconds;
        [Tooltip("出しておく秒数（0 なら AchievementSettings の値）")]
        [SerializeField, Min(0f)] float m_holdSeconds;
        [Tooltip("出るときの拡大（1 で拡大なし）")]
        [SerializeField, Range(1f, 1.4f)] float m_punch = 1.08f;

        [Header("光の演出")]
        [Tooltip("カードを横切る光の回数（0 で光なし）")]
        [SerializeField, Min(0)] int m_shineCount;
        [Tooltip("光が1回横切る秒数")]
        [SerializeField, Min(0.1f)] float m_shineSeconds = 0.6f;
        [Tooltip("画面全体を一瞬白くする")]
        [SerializeField] bool m_flash;
        [Tooltip("カードのまわりに出すきらめきの数（0 で出さない）")]
        [SerializeField, Min(0)] int m_sparkles;

        [Header("音")]
        [Tooltip("出るときの音。未設定なら鳴らさない")]
        [SerializeField] AudioClip m_sound;
        [SerializeField, Range(0f, 1f)] float m_volume = 0.8f;
        [SerializeField, Range(0.3f, 2f)] float m_pitch = 1f;

        public AchievementRarity Rarity => m_rarity;
        public float SlideSeconds => m_slideSeconds;
        public float HoldSeconds => m_holdSeconds;
        public float Punch => m_punch;
        public int ShineCount => m_shineCount;
        public float ShineSeconds => m_shineSeconds;
        public bool Flash => m_flash;
        public int Sparkles => m_sparkles;
        public AudioClip Sound => m_sound;
        public float Volume => m_volume;
        public float Pitch => m_pitch;

        // ---- 既定の演出（ScriptableObject を作らなくても、これで動く） ----
        // 銅・銀：スライドだけ／金：光＋音／プラチナ以上：フラッシュ・きらめき・音まで

        static readonly AchievementRevealStyle[] s_defaults = new AchievementRevealStyle[5];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            for (int i = 0; i < s_defaults.Length; i++) s_defaults[i] = null;
        }

        public static AchievementRevealStyle GetDefault(AchievementRarity rarity)
        {
            int index = Mathf.Clamp((int)rarity, 0, s_defaults.Length - 1);
            if (s_defaults[index] == null) s_defaults[index] = CreateDefault(rarity);
            return s_defaults[index];
        }

        static AchievementRevealStyle CreateDefault(AchievementRarity rarity)
        {
            var style = CreateInstance<AchievementRevealStyle>();
            style.name = "AchievementRevealStyle_" + rarity;
            style.hideFlags = HideFlags.HideAndDontSave;
            style.m_rarity = rarity;

            switch (rarity)
            {
                case AchievementRarity.Bronze:
                case AchievementRarity.Silver:
                    // 軽いスライドインだけ
                    style.m_slideSeconds = 0f;
                    style.m_holdSeconds = 0f;
                    style.m_punch = 1.05f;
                    style.m_shineCount = 0;
                    style.m_flash = false;
                    style.m_sparkles = 0;
                    style.m_volume = 0f;
                    break;

                case AchievementRarity.Gold:
                    // 光の帯と音を足す
                    style.m_punch = 1.10f;
                    style.m_shineCount = 2;
                    style.m_flash = false;
                    style.m_sparkles = 6;
                    style.m_sound = SoundSynth.ScoreUp;
                    style.m_volume = 0.75f;
                    style.m_pitch = 1f;
                    break;

                case AchievementRarity.Platinum:
                    // 上位：フラッシュ・きらめき・音
                    style.m_punch = 1.14f;
                    style.m_shineCount = 3;
                    style.m_flash = true;
                    style.m_sparkles = 12;
                    style.m_sound = SoundSynth.Respawn;
                    style.m_volume = 0.85f;
                    style.m_pitch = 1.05f;
                    break;

                default:
                    // 最高位（ダイヤモンド）／特別（王冠）：いちばん派手に
                    style.m_punch = 1.18f;
                    style.m_shineCount = 4;
                    style.m_flash = true;
                    style.m_sparkles = 18;
                    style.m_sound = SoundSynth.Victory;
                    style.m_volume = 0.9f;
                    style.m_pitch = 1f;
                    break;
            }
            return style;
        }
    }
}
