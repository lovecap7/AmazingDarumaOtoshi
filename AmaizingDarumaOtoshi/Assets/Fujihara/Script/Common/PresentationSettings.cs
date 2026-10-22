using UnityEngine;

// ゲーム全体の演出設定。Assets/Fujihara/Resources/PresentationSettings.asset を Inspector で編集する。
// タイトル・メニュー・ゲーム本編のどこからでも PresentationSettings.DefaultFont で参照できる。
[CreateAssetMenu(menuName = "Daruma/Presentation Settings", fileName = "PresentationSettings")]
public class PresentationSettings : ScriptableObject
{
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

    // 未設定なら TextMesh Pro の既定のフォント
    public static TMPro.TMP_FontAsset DefaultFont =>
        Instance != null && Instance.defaultFont != null ? Instance.defaultFont : TMPro.TMP_Settings.defaultFontAsset;
}
