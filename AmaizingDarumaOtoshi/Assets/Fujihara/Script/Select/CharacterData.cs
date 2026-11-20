using UnityEngine;

// キャラクター1体分のデータ。Project ウィンドウで右クリック → Create → Daruma → Character で作り、
// SelectRoster の characters に追加するとキャラクターセレクトに並ぶ。
// パッシブ・必殺技はメニューの「ずかん」（CharacterBook）で表示する（ハンマー性能は今は性能差を作らないので、ずかんには出さない）。
// 今は表示だけで、ゲームの動きには関係しない。
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
    [Tooltip("キャラの3Dモデル（Blender で作っただるま。足元が原点）。メニュー「Fujihara > キャラの顔写真を撮る」で、このモデルを撮って portrait に入れる")]
    public GameObject model;
    [Tooltip("キャラのハンマーのモデル（Model/Hammer/hammer_<id>.fbx）。試合・タイトル・ムービーで、このキャラが持つハンマーになる（空なら共通のハンマー）")]
    public GameObject hammerModel;
    [Tooltip("頭と一緒に付けるモデル（マント・アクセサリーなど。頭のモデルと同じ原点・大きさで書き出したもの）。試合の頭と顔写真に付く。" +
             "骨の名前が Cape_ で始まるモデル（マント）は、試合で CapeSway で揺れる")]
    public GameObject[] extraModels;
    [Tooltip("ハンマー（武器）の届く距離の倍率。1 = ふつうのハンマー。2 なら試合での見た目の長さも当たる距離も倍（だるきんぐの剣）")]
    [Min(0.5f)] public float weaponReach = 1f;
    [Tooltip("メニュー画面のランダムなだるま（頭とカーソルのハンマーが組で出る）に出すか。だるきんぐは剣がカーソルに合わないのでオフ")]
    public bool showInMenu = true;

    // セレクト画面・ずかんのアイコンの背景。どのキャラも薄い水色（だるまの色が映えるように。color は使わない）
    public static readonly Color IconBackground = new Color(0.78f, 0.9f, 0.98f);
    public Color IconColor => IconBackground;
    [Tooltip("ゲーム本編で生成するプレハブ（未設定でも可）")]
    public GameObject prefab;

    [Tooltip("使いやすさ（★の数）。キャラクターセレクトの枠に出る")]
    [Range(1, 5)] public int ease = 3;

    [Header("パッシブ")]
    [Tooltip("（今は使っていない）条件になる積み木の色。積み木の色の性能は撤廃した（TumikiColorUtil.IgnoreColors）")]
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
