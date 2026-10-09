using UnityEngine;

// ゲーム全体の演出設定。Assets/Fujihara/Resources/PresentationSettings.asset を Inspector で編集する。
// タイトル・メニュー・ゲーム本編のどこからでも PresentationSettings.ShowOnomatopoeia で参照できる。
[CreateAssetMenu(menuName = "Daruma/Presentation Settings", fileName = "PresentationSettings")]
public class PresentationSettings : ScriptableObject
{
    [Tooltip("「カコーン！」「ドンッ！！」などの擬音（効果音の字幕）を出す")]
    [SerializeField] bool showOnomatopoeia = true;

    [Tooltip("コードから作る UI（ゲーム中の名札など）に使うフォント")]
    [SerializeField] TMPro.TMP_FontAsset defaultFont;

    static PresentationSettings instance;

    static PresentationSettings Instance
    {
        get
        {
            if (instance == null) instance = Resources.Load<PresentationSettings>("PresentationSettings");
            return instance;
        }
    }

    // 設定ファイルが見つからないときは表示する
    public static bool ShowOnomatopoeia => Instance == null || Instance.showOnomatopoeia;

    // 未設定なら TextMesh Pro の既定のフォント
    public static TMPro.TMP_FontAsset DefaultFont =>
        Instance != null && Instance.defaultFont != null ? Instance.defaultFont : TMPro.TMP_Settings.defaultFontAsset;
}
