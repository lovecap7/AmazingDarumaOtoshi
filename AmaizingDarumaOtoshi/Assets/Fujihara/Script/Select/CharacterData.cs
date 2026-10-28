using UnityEngine;

// キャラクター1体分のデータ。Project ウィンドウで右クリック → Create → Daruma → Character で作り、
// SelectRoster の characters に追加するとキャラクターセレクトに並ぶ。
// パッシブ・ハンマー性能・必殺技はキャラクターセレクトの枠で表示する（Y で切り替え）。今は表示だけで、ゲームの動きには関係しない。
[CreateAssetMenu(menuName = "Daruma/Character", fileName = "Character")]
public class CharacterData : ScriptableObject
{
    [Tooltip("保存や通信で使う、変わらない名前（英数字）")]
    public string id;
    public string displayName;
    [Tooltip("アイコンの画像が無いときの色")]
    public Color color = Color.white;
    [Tooltip("セレクト画面のアイコン。画像の Texture Type を「Sprite (2D and UI)」にしてここへドラッグ（未設定なら色と頭文字で仮表示）")]
    public Sprite portrait;
    [Tooltip("ゲーム本編で生成するプレハブ（未設定でも可）")]
    public GameObject prefab;

    [Header("パッシブ（枠では「[赤][赤][赤] → 効果」の形で出る）")]
    [Tooltip("条件になる積み木の色")]
    public Nakahira.TumikiColor passiveColor = Nakahira.TumikiColor.Red;
    [Tooltip("条件になる積み木の数（0 なら条件なし＝いつも）")]
    [Range(0, 9)] public int passiveCount = 3;
    [Tooltip("効果の短い名前（6文字くらいまで）")]
    public string passiveName = "";
    [Tooltip("1文の説明（30文字くらいまで）")]
    [TextArea(1, 3)] public string passiveText = "";

    [Header("ハンマー性能（1〜5）")]
    [Range(1, 5)] public int hammerPower = 3;
    [Range(1, 5)] public int hammerSpeed = 3;
    [Range(1, 5)] public int hammerReach = 3;

    [Header("必殺技（仮）")]
    public string specialName = "";
    [Tooltip("1文の説明（30文字くらいまで）")]
    [TextArea(1, 3)] public string specialText = "";
}
