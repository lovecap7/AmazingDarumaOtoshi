using UnityEngine;

// だるまの色（キャラのモデル・鉢巻の色違い）。Wood は色のついていない木（セレクト・ずかん・メニューなどのいつもの色）
// 試合では 1P 赤・2P 青・3P 黄・4P 緑・CPU 白（GameModelSettings.CharacterColorOf）
public enum DarumaColor { Wood, Red, Blue, Yellow, Green, White }

// レベルで上がる、キャラごとの能力（パッシブ。2026-11-24 メンバー全員で決めた仕様）
public enum LevelPerk
{
    None,
    ShotSpeed,       // あかだる：弾速
    AttackSpeed,     // あおだる：攻撃速度
    SlowOnHit,       // みだる：当てた相手の移動速度ダウン
    Stamina,         // きだる：スタミナ（減りにくく、回復が速い）
    ShotDurability,  // いわだる：撃った弾の耐久値
    Zigzag,          // へびだる：撃った弾の蛇行の振れ幅
    Reach,           // だるきんぐ：リーチ
}

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
    [Tooltip("色違いのモデル（Model/Daruma/Colors/<色>/<id>/）。並びは DarumaColor（木・赤・青・黄・緑・白）。空の色は model を使う")]
    public GameObject[] colorModels = new GameObject[6];

    // その色のモデル（無ければいつもの model）
    public GameObject ModelFor(DarumaColor c)
    {
        int i = (int)c;
        return colorModels != null && i >= 0 && i < colorModels.Length && colorModels[i] != null ? colorModels[i] : model;
    }
    [Tooltip("キャラのハンマーのモデル（Model/Hammer/hammer_<id>.fbx）。試合・タイトル・ムービーで、このキャラが持つハンマーになる（空なら共通のハンマー）")]
    public GameObject hammerModel;
    [Tooltip("色違いのハンマー（Model/Hammer/Colors/<色>/）。並びは DarumaColor（木・赤・青・黄・緑・白）。試合ではプレイヤーの色のハンマーを持つ。" +
             "その色が無ければ木、木も無ければ hammerModel")]
    public GameObject[] hammerColorModels = new GameObject[6];

    // その色のハンマー（無ければ木の色、それも無ければいつもの hammerModel）
    public GameObject HammerFor(DarumaColor c)
    {
        int i = (int)c;
        if (hammerColorModels != null)
        {
            if (i >= 0 && i < hammerColorModels.Length && hammerColorModels[i] != null) return hammerColorModels[i];
            if (hammerColorModels.Length > 0 && hammerColorModels[0] != null) return hammerColorModels[0];
        }
        return hammerModel;
    }
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

    [Header("レベル（試合中に積んだ数で上がる。Lv2 から、このキャラだけ下の能力も上がる）")]
    [Tooltip("レベルで上がる、このキャラだけの能力（値は Yurube/Level/Resources/LevelParams）")]
    public LevelPerk levelPerk = LevelPerk.None;

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
