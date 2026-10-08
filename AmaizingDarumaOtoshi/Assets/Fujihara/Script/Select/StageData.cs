using UnityEngine;

// ステージ1つ分のデータ。Project ウィンドウで右クリック → Create → Daruma → Stage で作り、
// SelectRoster の stages に追加するとステージセレクトに並ぶ。
[CreateAssetMenu(menuName = "Daruma/Stage", fileName = "Stage")]
public class StageData : ScriptableObject
{
    [Tooltip("保存や通信で使う、変わらない名前（英数字）")]
    public string id;
    public string displayName;
    [Tooltip("プレビュー画像が無いときの色")]
    public Color color = Color.white;
    [Tooltip("セレクト画面のプレビュー。画像の Texture Type を「Sprite (2D and UI)」にしてここへドラッグ（未設定なら色と頭文字で仮表示）")]
    public Sprite preview;
    [Tooltip("このステージで遊ぶシーン名（未設定なら MatchSelect の gameSceneName を使う）")]
    public string sceneName;
}
