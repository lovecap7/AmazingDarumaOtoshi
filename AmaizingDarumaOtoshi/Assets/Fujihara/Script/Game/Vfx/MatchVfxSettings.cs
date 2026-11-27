using UnityEngine;

// 試合の演出（MatchVfx）の設定。実体は Resources/MatchVfxSettings.asset（無ければ初期値で動く）
//  1. 土埃：走る・ダッシュ・ジャンプ・着地で足元に白い煙
//  2. ラリー：打ち返すたびに、止まっている間に集中線・光が強くなり、回数が多いとネガポジ反転する（Lethal League のような）
//  3. ハンマーの軌跡：振ったハンマーの通り道に、プレイヤーの色の弧
//  4. 即死の大爆発：打ち返して攻撃力が上がった積み木でやられたとき、光が集まってから大爆発
[CreateAssetMenu(menuName = "Daruma/Match Vfx Settings", fileName = "MatchVfxSettings")]
public class MatchVfxSettings : ScriptableObject
{
    [Header("1. 土埃")]
    public bool dust = true;
    [Tooltip("煙の色")]
    public Color dustColor = new Color(0.96f, 0.94f, 0.9f, 0.7f);
    [Tooltip("走っているとき、この距離（m）ごとに煙を出す")]
    public float runSpacing = 0.7f;
    [Tooltip("ダッシュしているとき、この距離（m）ごとに煙を出す")]
    public float dashSpacing = 0.3f;
    [Tooltip("煙の大きさ（走る・ダッシュ）")]
    public float runSize = 0.35f, dashSize = 0.55f;
    [Tooltip("ジャンプ・着地で、足元に輪のように出す煙の数")]
    public int jumpPuffs = 10, landPuffs = 14;
    [Tooltip("これより速く落ちて着地したら、着地の煙を出す（m/s）")]
    public float landSpeed = 4f;

    [Header("2. ラリー（打ち返したとき）")]
    public bool rally = true;
    [Tooltip("弾を中心に、画面の上で輪（衝撃波）が広がる")]
    public bool shockwave = true;
    [Tooltip("衝撃波の輪の色")]
    public Color shockwaveColor = new Color(1f, 1f, 1f, 0.9f);
    [Tooltip("輪が広がりきったときの大きさ（画面の高さに対する割合）。回数が増えるほど、ここから大きくなる")]
    public float shockwaveSize = 0.5f;
    [Tooltip("打ち返した回数 1 回ごとに、大きさに足す量")]
    public float shockwaveSizePerReturn = 0.15f;
    [Tooltip("輪が広がって消えるまでの秒数")]
    public float shockwaveTime = 0.35f;
    [Tooltip("止まっている間、この秒数ごとに弾から輪を出し続ける（0 で出さない）")]
    public float shockwavePulse = 0.22f;

    [Tooltip("弾を中心に円が広がり、その中をネガポジ反転する（今は使わない）")]
    public bool invert = false;
    [Tooltip("打ち返した回数がこの回数以上になると、止まっている間、弾を中心に円が広がって、その中がネガポジ反転する（Invert がオンのとき）")]
    [Min(1)] public int invertAt = 4;
    [Tooltip("反転の円が画面いっぱいに広がるまでの秒数")]
    public float invertExpand = 0.35f;
    [Tooltip("止まり終わってから、反転の円がしぼんで消えるまでの秒数")]
    public float invertShrink = 0.15f;
    [Tooltip("反転の円のシェーダー（Shaders/RallyInvertCircle）")]
    public Shader invertShader;
    [Tooltip("止まっている間のコントラスト（回数が増えるほど、ここまで上がる）")]
    public float contrast = 25f;
    [Tooltip("打った瞬間の白い光（明るさ）")]
    public float flash = 1.6f;
    [Tooltip("止まり終わってから、色が戻るまでの秒数")]
    public float recover = 0.25f;
    [Tooltip("打ち返したときのカメラの揺れ幅（m）。回数が増えるほど、ここから大きくなる。0 で揺らさない")]
    public float shake = 0.35f;
    [Tooltip("打ち返した回数 1 回ごとに、揺れ幅に足す量（m）")]
    public float shakePerReturn = 0.15f;
    [Tooltip("揺れ幅の上限（m）")]
    public float shakeMax = 1.2f;
    [Tooltip("集中線の濃さ（最大）。0 で出さない（今は出さない）")]
    [Range(0f, 1f)] public float speedLines = 0f;

    [Header("3. ハンマーの軌跡")]
    public bool swingTrail = true;
    [Tooltip("軌跡が消えるまでの秒数")]
    public float trailLife = 0.14f;
    [Tooltip("軌跡の、柄の側の端（ハンマーの長さに対する割合。0 で根元、1 で先）")]
    [Range(0f, 1f)] public float trailInner = 0.45f;
    [Tooltip("軌跡の濃さ")]
    [Range(0f, 1f)] public float trailAlpha = 0.75f;

    [Header("4. 即死の大爆発（イメージ：呪術廻戦の「術式反転・赫」）")]
    public bool killExplosion = true;
    [Tooltip("この攻撃力以上の積み木（打ち返して上がったもの）でやられたら爆発する")]
    [Min(2)] public int minAttackPower = 2;
    [Tooltip("溜め：ステージ全体から光が集まってくる時間（秒）")]
    public float chargeTime = 1.1f;
    [Tooltip("溜め：光の筋が集まってくる範囲（中心からの距離 m。ステージ全体くらい）")]
    public float gatherRadius = 16f;
    [Tooltip("溜め：1秒あたりに集まる光の筋の数（最初・最後）")]
    public float gatherRateStart = 150f, gatherRateEnd = 900f;
    [Tooltip("爆発：広がって消えるまでの秒数")]
    public float explosionTime = 0.9f;
    [Tooltip("爆発：大きさ（光の球が広がる半径 m）")]
    public float explosionSize = 9f;
    [Tooltip("溜めの光の玉の大きさの倍率（試合のカメラは遠いので大きめ）")]
    public float chargeScale = 1.6f;
    [Tooltip("光の明るさ（1 より大きいとブルームでにじむ）")]
    public float glowIntensity = 3f;
    [Tooltip("溜めの間、画面を暗く・赤くする強さ（0〜1）")]
    [Range(0f, 1f)] public float chargeDarken = 0.7f;
    [Tooltip("爆発の色：真ん中（白に近い）と、まわり（赤）")]
    public Color coreColor = new Color(1f, 0.85f, 0.8f), outerColor = new Color(1f, 0.08f, 0.05f);
    [Tooltip("光るシェーダー（Shaders/VfxAdditive）")]
    public Shader additiveShader;

    [Header("5. だるきんぐの必殺技「エクスカリバー」（イメージ：Fate のセイバーの約束された勝利の剣）")]
    public bool excalibur = true;
    [Tooltip("光の色：金（まわり）と、白に近い金（芯）")]
    public Color excaliburGold = new Color(1f, 0.78f, 0.25f), excaliburCore = new Color(1f, 0.97f, 0.82f);
    [Tooltip("詠唱：光の粒が地面から浮かび上がる範囲（m）")]
    public float excaliburMoteRadius = 14f;
    [Tooltip("詠唱：1秒あたりに浮かび上がる光の粒の数（最初・最後）")]
    public float excaliburMoteRateStart = 60f, excaliburMoteRateEnd = 260f;
    [Tooltip("詠唱：剣へ吸い込まれる光の筋の数（最大、1秒あたり）")]
    public float excaliburGatherRate = 320f;
    [Tooltip("詠唱：剣から空へ立ち上る光の柱の高さ（m）")]
    public float excaliburPillarHeight = 26f;
    [Tooltip("放つ：光の波が攻撃の線の端まで走る秒数")]
    public float excaliburWaveTime = 0.22f;
    [Tooltip("放つ：攻撃の線に沿って立ち上がる光の幕の高さ（m）")]
    public float excaliburCurtainHeight = 9f;
    [Tooltip("放つ：演出が消えるまでの秒数")]
    public float excaliburFireTime = 1.3f;
    [Tooltip("放つ：終点の爆発を、ステージの端からこの距離（m）だけ手前（ステージの内側）で起こす。光の波もそこまで走る")]
    public float excaliburBlastInset = 4.5f;
    [Tooltip("抉れた跡：溝の幅（攻撃の太さに対する倍率）")]
    public float excaliburScarWidth = 1.3f;
    [Tooltip("抉れた跡：残っている秒数（このあと 1.5 秒で消える）")]
    public float excaliburScarTime = 8f;
    [Tooltip("抉れた跡：溝の両側・クレーターのまわりに盛り上がる岩の大きさ（倍率）")]
    public float excaliburRockSize = 0.65f;
    [Tooltip("抉れた跡：岩の並ぶ密度（倍率。大きいほどすき間なく並ぶ）")]
    public float excaliburRockDensity = 0.7f;

    [Header("6. あおだるの必殺技「疾風連撃」の斬撃（稲妻をまとったプラズマの斬撃。色はプレイヤーの色）")]
    public bool aodaruPlasma = true;
    [Tooltip("もとの斬撃の見た目（水色の三日月）を隠して、プラズマの見た目だけにする")]
    public bool aodaruHideOriginal = true;
    [Tooltip("斬撃にまとわりつく稲妻の本数")]
    [Range(1, 8)] public int aodaruBolts = 4;
    [Tooltip("斬撃から外へ飛び出す枝分かれの稲妻の数（1秒あたり）")]
    public float aodaruForkRate = 26f;
    [Tooltip("稲妻がバチバチと形を変える間隔（秒）")]
    public float aodaruFlicker = 0.035f;
    [Tooltip("光のにじみの太さ（倍率）")]
    public float aodaruGlowWidth = 1f;
    [Tooltip("光の明るさ（1 より大きいとブルームでにじむ）")]
    public float aodaruIntensity = 3f;
    [Tooltip("火花の数（1秒あたり）")]
    public float aodaruSparkRate = 120f;
    [Tooltip("黒い稲妻の濃さ（0 で出さない）。白い床でも稲妻が見えやすくなる")]
    [Range(0f, 1f)] public float aodaruDark = 0.8f;

    [Header("7. 必殺技カットイン（イメージ：イナズマイレブンの必殺技）")]
    [Tooltip("だるきんぐの必殺技で、新しいカットイン（だるきんぐが動く・背景が変わる・技名が1文字ずつ出る）を使う。オフで前のカットイン")]
    public bool heroCutIn = true;
    [Tooltip("技名の文字（筆文字。Font/YujiSyuku-Regular）")]
    public Font cutInFont;
    [Tooltip("技名が1文字ずつ出る間隔（秒）")]
    public float cutInLetterInterval = 0.1f;
    [Tooltip("効果音（空ならプログラムで作った仮の音）：1文字ごと・技名がそろったとき・力をためる・斬る")]
    public AudioClip cutInLetterSe, cutInNameSe, cutInChargeSe, cutInSlashSe;

    static MatchVfxSettings s_current;
    public static MatchVfxSettings Current
    {
        get
        {
            if (s_current != null) return s_current;
            s_current = Resources.Load<MatchVfxSettings>("MatchVfxSettings");
            if (s_current == null) s_current = CreateInstance<MatchVfxSettings>();
            return s_current;
        }
    }
}
