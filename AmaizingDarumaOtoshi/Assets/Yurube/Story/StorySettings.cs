using UnityEngine;

namespace Yurube.Story
{
    // ストーリーの調整値。Assets/Yurube/Story/Resources/StorySettings.asset（無くても動く）
    // メニューの「Assets > Create > Daruma > Story Settings」で作り、Resources フォルダに置くと読み込まれる。
    // 会話の文字送り速度・小さいウィンドウの表示時間・背景の暗さを、コードを変えずに調整できる。
    [CreateAssetMenu(menuName = "Daruma/Story Settings", fileName = "StorySettings")]
    public class StorySettings : ScriptableObject
    {
        [Header("会話")]
        [Tooltip("1秒あたりに表示する文字数（大きいほど速い）")]
        [SerializeField, Min(1f)] float m_charsPerSecond = 34f;
        [Tooltip("対戦中の小さいウィンドウを自動で閉じるまでの秒数")]
        [SerializeField, Min(0.5f)] float m_smallSeconds = 4.5f;
        [Tooltip("大きいウィンドウのとき、背景を暗くする濃さ")]
        [SerializeField, Range(0f, 0.9f)] float m_dimAlpha = 0.62f;
        [Tooltip("必殺技の覚醒イベントが、対戦開始から自動で起きるまでの秒数（保険。ストックが残り1になれば早く起きる）")]
        [SerializeField, Min(5f)] float m_awakeningSeconds = 90f;
        [Tooltip("チュートリアルCPUの積み木の上限（0 なら章データの cpuBlockLimit を使う）")]
        [SerializeField, Min(0)] int m_tutorialCpuBlockLimit;

        [Header("打ち返しチュートリアル")]
        [Tooltip("プレイヤーへ向かう積み木が来ないとき、補助で飛ばすまでの秒数")]
        [SerializeField, Min(3f)] float m_reflectAssistSeconds = 20f;
        [Tooltip("補助で飛ばす積み木の速さ（m/s）")]
        [SerializeField, Min(1f)] float m_reflectAssistSpeed = 18f;

        [Header("章の上書き（0 なら章データ）")]
        [Tooltip("章データのストック数の既定値を上書きする（0 なら章データ。CPU個別の stocks は優先）")]
        [SerializeField, Min(0)] int m_overrideStocks;
        [Tooltip("CPUの強さを上書きする（0 なら章データ。1〜3）")]
        [SerializeField, Min(0)] int m_overrideCpuLevel;
        [Tooltip("CPUの積み木の上限を上書きする（0 なら章データ）")]
        [SerializeField, Min(0)] int m_overrideCpuBlockLimit;

        public float CharsPerSecond => Mathf.Max(1f, m_charsPerSecond);
        public float SmallSeconds => Mathf.Max(0.5f, m_smallSeconds);
        public float DimAlpha => Mathf.Clamp(m_dimAlpha, 0f, 0.9f);
        public float AwakeningSeconds => Mathf.Max(5f, m_awakeningSeconds);
        public int TutorialCpuBlockLimit => Mathf.Max(0, m_tutorialCpuBlockLimit);
        public float ReflectAssistSeconds => Mathf.Max(3f, m_reflectAssistSeconds);
        public float ReflectAssistSpeed => Mathf.Max(1f, m_reflectAssistSpeed);
        public int OverrideStocks => Mathf.Max(0, m_overrideStocks);
        public int OverrideCpuLevel => Mathf.Max(0, m_overrideCpuLevel);
        public int OverrideCpuBlockLimit => Mathf.Max(0, m_overrideCpuBlockLimit);

        static StorySettings s_instance;

        public static StorySettings Instance
        {
            get
            {
                if (s_instance == null) s_instance = Resources.Load<StorySettings>("StorySettings");
                return s_instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_instance = null;
    }
}
