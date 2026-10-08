using UnityEngine;

// キャラクター1体分のデータ。Project ウィンドウで右クリック → Create → Daruma → Character で作り、
// SelectRoster の characters に追加するとキャラクターセレクトに並ぶ。
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
}
