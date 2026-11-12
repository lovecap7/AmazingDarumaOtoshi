using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.UI;

namespace Nakagawa
{
    // 牌あつめと洗牌(Stage22「こたつの全自動卓」)
    //  ・一定の間隔で空から麻雀牌(東・南・西・北)が降る(落ちる場所に予告の円)。場に置ける牌には上限があり、一定時間で点滅して消える
    //  ・生きているプレイヤーが触れると取る。攻撃を受けると手持ちからランダムに1枚落とし(王冠と同じ。落ちた直後は拾えない)、やられると全部落とす
    //  ・場の牌は牌どうしで弾き合い、積み木に当たると一方的に弾かれる(積み木には影響しない)。壁で跳ね返る
    //  ・役(同じ牌3枚 = ポン / 4種を1枚ずつ)がそろうと自動で発動する(役の牌は手持ちから消える)
    //  ・発動の流れ:
    //     1. 飛び立つ: 「○P ポン！」。発動した人が回転しながら上空へ飛び、カメラのそばに寄る。同時に画面が少し暗くなる(手前に寄った人は暗くならない)
    //     2. ねらう  : 盤の上に、4倍の大きさのポンの形(縦2・横1。4種なら縦3・横1)の落下予告が出る。発動した人は移動入力で予告を動かせる
    //                  予告の牌は円形のゲージになっていて、時間がたつほど不透明な部分が時計回りに増える。満ちるか、ボタン(ジャンプ・攻撃・ショット)で落とす
    //                  CPU は近くの相手をねらって自動で動かす
    //     3. 落下    : 巨大な牌が、文字を上にして予告の位置に落ちる。着地の衝撃で近くのプレイヤー・積み木を外へはじく。着地した巨大な牌は壁と同じ障害物になる
    //     4. 帰還    : 発動した人は、上空から元の位置へ戻ってくる(元の位置が牌の下なら牌の外へずらす)。戻ったあと少しの間は無敵
    //                  巨大な牌の下敷きになったプレイヤーは即死。着地した巨大な牌は重い物理の箱になり、プレイヤーが押すとほんの少し動く
    //                  ねらっている間は、画面上部に「ボタンを押して落とせ！」と出す(人が発動したときだけ)
    //     5. ツモ    : 落としてから5秒後に「○P ツモ！」→ 中央のふたの縁が黄色く点滅 → ふたが開いて穴になり(巨大な牌も物理で動きながら中央へ吸い込まれる)、
    //                  発動した人以外のプレイヤー・積み木・牌が中央へ流される → ふたが閉じ、4辺の縁の内側から積み木がせり上がって補充される
    //  ・手持ちの牌は、画面下の各自の HUD パネルの上に並べる(MatchHud の IMatchHudPanelExtra)
    // 卓は全周が壁なので、落下はふたが開いている間だけ
    // 開始のカウントダウン中は止まる。ネット対戦ではホストが計算し、クライアントは状態を受け取って見た目だけ合わせる
    // 試合のカメラ(MatchCamera)が動いたあとに、カメラのそばに寄せた人の位置を合わせるため、後で動かす
    [DefaultExecutionOrder(100)]
    public class MahjongTable : MonoBehaviour, INetGimmickState, IMatchHudPanelExtra
    {
        enum Phase { Idle, Fly, Aim, GiantDrop, Settle, Announce, Open, Flow, Close, Refill }

        [Header("卓")]
        // 遊べる範囲(壁の内側)の大きさ。中心はこの Transform の位置
        [SerializeField] Vector2 m_tableSize = new Vector2(26.0f, 26.0f);
        // 中央のふた(閉じている間は床。開くと穴)
        [SerializeField] Transform m_lid;
        [SerializeField] Collider m_lidCollider;
        [SerializeField] Vector2 m_lidSize = new Vector2(6.0f, 6.0f);
        [SerializeField] Renderer[] m_lidEdges;
        [SerializeField] Color m_lidEdgeColor = new Color(0.32f, 0.3f, 0.26f, 1.0f);
        [SerializeField] Color m_warningColor = GimmickPalette.Warning;
        // ふたが開くときに下がる深さ(穴の底に床が見えて「床があるのに落ちる」ように見えないよう、深く沈めて開き切ったら隠す)
        [SerializeField] float m_lidDrop = 8.0f;

        [Header("牌")]
        [SerializeField, Min(0.5f)] float m_dropInterval = 4.0f;
        [SerializeField, Min(0.0f)] float m_dropWarningTime = 1.0f;
        [SerializeField, Min(1)] int m_maxFieldTiles = 6;
        [SerializeField, Min(1.0f)] float m_tileLifetime = 15.0f;
        // 消える前に点滅する時間
        [SerializeField, Min(0.0f)] float m_tileBlinkTime = 3.0f;
        // 降らせる場所の、壁からの距離
        [SerializeField] float m_dropMargin = 2.0f;
        [SerializeField] float m_pickupRadius = 1.0f;
        // 落ちた牌をだれも拾えない時間(秒)
        [SerializeField, Min(0.0f)] float m_pickupDelay = 0.7f;
        // 手持ちの上限(7枚目で必ず役ができるので、ふつうは届かない)
        [SerializeField, Min(1)] int m_maxHand = 6;
        [SerializeField] Material m_tileMaterial;   // 白い面
        [SerializeField] Material m_backMaterial;   // 背(緑)
        [SerializeField] float m_fallHeight = 9.0f;
        [SerializeField] float m_gravity = 30.0f;
        [SerializeField] float m_popUpSpeed = 5.5f;
        [SerializeField] float m_popSideSpeed = 2.2f;

        [Header("牌の当たり(重さ)")]
        // 牌の当たりの半径(上から見た円で近似)
        [SerializeField] float m_tileRadius = 0.52f;
        // 床の上で止まるまでの減速(m/秒²)。大きいほどすぐ止まる(重く見える)
        [SerializeField] float m_tileFriction = 12.0f;
        // 牌どうしの跳ね返りの強さ(0〜1)。小さいほど重く、ぶつかってもあまり弾まない
        [SerializeField, Range(0.0f, 1.0f)] float m_tileBounce = 0.35f;
        // 積み木に当たって弾かれるときの最低の速さ(m/秒)。積み木が速く向かってきたら、その速さの割合で弾かれる
        // (牌は重いので積み木より遅く押し出されるだけ。積み木の動きは変えない)
        [SerializeField] float m_blockKickMin = 1.2f;
        [SerializeField] float m_blockKickRate = 0.55f;
        // 弾かれた速さがこれ以上なら少し跳ねる(m/秒)
        [SerializeField] float m_hopSpeed = 7.0f;
        // ぶつかったときに回る勢い(度/秒)
        [SerializeField] float m_hitSpin = 90.0f;

        [Header("影")]
        // 空中の牌の下に出す丸い影の濃さ(本体の影は光源から落ちる)
        [SerializeField, Range(0.0f, 1.0f)] float m_blobShadowAlpha = 0.35f;

        [Header("ポン(飛び立つ → ねらう → 落とす → 帰還)")]
        // 飛び立ってカメラのそばに寄るまで(秒)
        [SerializeField, Min(0.1f)] float m_flyTime = 0.9f;
        // 飛ぶときの回転の速さ(度/秒)
        [SerializeField] float m_flySpin = 900.0f;
        // カメラのそばの位置(カメラから見た右・上・前。m)
        // (盤と画面の下の HUD にかぶらないよう、画面の左上寄り)
        [SerializeField] Vector3 m_cameraSideOffset = new Vector3(-4.2f, 0.2f, 10.0f);
        // ねらう時間の上限(秒。ゲージが満ちたら落とす)と、ボタンを受け付け始めるまで(秒。押しっぱなしで即落ちないように)
        [SerializeField, Min(0.5f)] float m_aimTime = 6.0f;
        [SerializeField, Min(0.0f)] float m_aimButtonDelay = 0.4f;
        // 予告を動かす速さ(m/秒)。CPU の速さ
        [SerializeField] float m_aimSpeed = 7.33f;
        [SerializeField] float m_cpuAimSpeed = 4.67f;
        // ねらっている間、画面上部に出す案内(人が発動したときだけ)
        [SerializeField] string m_aimMessage = "ボタンを押して落とせ！";
        // 予告の最初の位置(卓の中心からの相対)
        [SerializeField] Vector2 m_ponDropOffset = Vector2.zero;
        [SerializeField, Min(0.1f)] float m_giantDropTime = 0.55f;
        // 巨大な牌が落ちてくる高さ
        [SerializeField] float m_dropHeight = 14.0f;
        // 落としてから帰還し終えるまで(秒)と、帰還後の無敵の時間
        [SerializeField, Min(0.1f)] float m_returnTime = 1.0f;
        [SerializeField, Min(0.0f)] float m_returnInvincible = 0.5f;
        // 巨大な牌が着いてからツモ(洗牌)が始まるまで(秒)
        [SerializeField, Min(0.0f)] float m_settleTime = 5.0f;
        // 巨大な牌の大きさ(ふつうの牌の何倍か)
        [SerializeField] float m_giantScale = 2.667f;
        [SerializeField] float m_giantGap = 0.05f;
        // 着地した巨大な牌の物理(重さ・減速)。プレイヤー(重さ20)がほんの少し押せる程度の重さにする
        [SerializeField, Min(1.0f)] float m_giantMass = 400.0f;
        [SerializeField, Min(0.0f)] float m_giantDamping = 3.0f;
        [SerializeField, Min(0.0f)] float m_giantAngularDamping = 3.0f;
        // ツモ(洗牌)で中央へ吸い込む強さ(加速度 m/s²)と、その間の減速
        [SerializeField] float m_giantFlowAccel = 10.0f;
        // 接地している(卓の上に乗っている)ときの吸い込む強さの倍率。空中・落ちている間は 1 倍のまま
        [SerializeField, Min(0.0f)] float m_giantGroundedFlowScale = 2.0f;
        // 接地しているとみなす、卓の上に置いたときの高さからの下がり幅(m)
        [SerializeField, Min(0.0f)] float m_giantGroundedTolerance = 0.3f;
        [SerializeField, Min(0.0f)] float m_giantFlowDamping = 0.4f;
        // 下敷きにされたプレイヤーは即死する(牌の上面からこの高さより下にいたら)
        [SerializeField] float m_crushTopMargin = 0.3f;
        // 着地の衝撃ではじく範囲(牌の並びの外側への余裕)と強さ
        [SerializeField] float m_impactMargin = 1.5f;
        [SerializeField] float m_impactPower = 10.0f;
        [SerializeField] float m_impactUpPower = 4.0f;

        [Header("ポンの見た目")]
        // 画面を暗くする濃さと、暗くする板のカメラからの距離(カメラのそばに寄った人より奥・盤より手前)
        [SerializeField, Range(0.0f, 1.0f)] float m_darkAlpha = 0.45f;
        [SerializeField] float m_darkDistance = 12.0f;
        // 予告(円形のゲージ)の色・満ちた所と満ちていない所の不透明度・ゲージの細かさ(m)
        [SerializeField] Color m_gaugeColor = new Color(1.0f, 0.98f, 0.92f, 1.0f);
        [SerializeField, Range(0.0f, 1.0f)] float m_gaugeFilledAlpha = 0.85f;
        [SerializeField, Range(0.0f, 1.0f)] float m_gaugeEmptyAlpha = 0.2f;
        [SerializeField, Min(0.1f)] float m_gaugeCell = 0.3f;

        [Header("洗牌")]
        [SerializeField, Min(0.0f)] float m_announceTime = 1.5f;
        [SerializeField, Min(0.05f)] float m_openTime = 0.5f;
        [SerializeField, Min(0.0f)] float m_flowTime = 4.0f;
        [SerializeField, Min(0.05f)] float m_closeTime = 0.6f;
        // 中央へ流す強さ(加速度 m/s²)
        [SerializeField] float m_flowAccel = 14.0f;
        [SerializeField] float m_tileFlowAccel = 8.0f;
        [SerializeField] TumikiBlock m_tumikiPrefab;
        [SerializeField] TumikiSpawner m_spawner;
        // 補充する積み木の数(盤面の上限まで)と、出てくる場所の壁からの距離
        [SerializeField, Min(0)] int m_refillCount = 8;
        [SerializeField] float m_refillInset = 1.3f;
        [SerializeField, Min(0.0f)] float m_refillTime = 0.8f;

        [Header("HUD")]
        [SerializeField] Vector2 m_iconSize = new Vector2(30.0f, 40.0f);
        [SerializeField] float m_iconGap = 4.0f;
        // パネルの上端からの高さ(状態の文字の上)
        [SerializeField] float m_iconOffsetY = 58.0f;

        [Header("エフェクト")]
        [SerializeField] GimmickBurstSettings m_pickupEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 1.0f, 0.95f, 1.0f), ColorB = new Color(0.85f, 0.85f, 0.8f, 1.0f),
            Count = 12, Speed = new Vector2(1.0f, 2.5f), Size = new Vector2(0.05f, 0.12f), Lifetime = new Vector2(0.2f, 0.45f),
            Gravity = -0.3f, Shape = GimmickBurstShape.Ring, ShapeSize = new Vector3(0.4f, 0, 0),
        };
        [SerializeField] GimmickBurstSettings m_landEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.9f, 0.88f, 0.82f, 0.7f), ColorB = new Color(0.75f, 0.72f, 0.66f, 0.6f),
            Count = 8, Speed = new Vector2(0.5f, 1.5f), Size = new Vector2(0.06f, 0.14f), Lifetime = new Vector2(0.2f, 0.4f),
            Shape = GimmickBurstShape.Ring, ShapeSize = new Vector3(0.4f, 0, 0),
        };
        [SerializeField] GimmickBurstSettings m_gatherEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 0.97f, 0.85f, 1.0f), ColorB = new Color(1.0f, 0.85f, 0.5f, 1.0f),
            Count = 6, Speed = new Vector2(0.5f, 1.5f), Size = new Vector2(0.08f, 0.2f), Lifetime = new Vector2(0.3f, 0.6f),
            Gravity = -0.2f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.4f, 0, 0),
        };
        [SerializeField] GimmickBurstSettings m_flashEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 1.0f, 0.95f, 1.0f), ColorB = new Color(1.0f, 0.85f, 0.45f, 1.0f),
            Count = 70, Speed = new Vector2(4.0f, 10.0f), Size = new Vector2(0.1f, 0.3f), Lifetime = new Vector2(0.4f, 0.9f),
            Gravity = 0.3f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(1.0f, 0, 0), Stretch = true,
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.95f, 0.75f, 0.8f), ShockwaveRadius = 6.0f, ShockwaveTime = 0.4f,
        };
        [SerializeField] GimmickBurstSettings m_impactEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.9f, 0.86f, 0.78f, 0.9f), ColorB = new Color(0.7f, 0.66f, 0.58f, 0.8f),
            Count = 50, Speed = new Vector2(2.0f, 6.0f), Size = new Vector2(0.15f, 0.35f), Lifetime = new Vector2(0.4f, 0.8f),
            Gravity = 0.4f, Shape = GimmickBurstShape.Ring, ShapeSize = new Vector3(3.0f, 0, 0),
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.95f, 0.85f, 0.7f), ShockwaveRadius = 6.0f, ShockwaveTime = 0.35f,
        };
        [SerializeField] GimmickBurstSettings m_refillEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.85f, 0.8f, 0.7f, 0.9f), ColorB = new Color(0.65f, 0.6f, 0.52f, 0.8f),
            Count = 14, Speed = new Vector2(1.0f, 2.5f), Size = new Vector2(0.1f, 0.25f), Lifetime = new Vector2(0.3f, 0.6f),
            Shape = GimmickBurstShape.Ring, ShapeSize = new Vector3(0.5f, 0, 0),
        };
        [SerializeField, Range(0.0f, 1.0f)] float m_volume = 0.4f;

        static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");

        // 場の牌
        class FieldTile
        {
            public int Kind;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Yaw, YawSpeed;
            // 0: 降る予告 / 1: 落ちている(空中) / 2: 床にある
            public int State;
            public float Timer;
            public float Age;
            public float NoPickup;
            public bool Lost;
            public Transform Visual;
            public Renderer[] Renderers;
            public MeshRenderer WarnFill, WarnRing, Shadow;
        }

        // 巨大な牌(ポンの演出)
        class GiantTile
        {
            public Transform Visual;
            public BoxCollider Collider;
            // 着地したあとは物理で動く(押される・中央へ吸い込まれる)
            public Rigidbody Body;
            public bool Gone;
            // ネット対戦のクライアント用: ホストから受け取った位置と向き
            public Vector3 NetPos;
            public Quaternion NetRot = Quaternion.identity;
            public bool HasNet;
            // 並んだときの位置(卓の上面の高さ・ローカル)と向き
            public Vector3 Rest;
            public float RestYaw;
            public Vector3 Start;
        }

        class Hud
        {
            public RectTransform Root;
            // 牌の後ろの暗い下地(パネルと同じ色。床の牌と見分けるため)
            public RectTransform Back;
            public readonly List<RectTransform> Icons = new List<RectTransform>();
        }

        readonly List<FieldTile> m_field = new List<FieldTile>();
        readonly Dictionary<MatchPlayer, List<int>> m_hands = new Dictionary<MatchPlayer, List<int>>();
        readonly Dictionary<RectTransform, Hud> m_huds = new Dictionary<RectTransform, Hud>();
        readonly List<IGimmickAffectable> m_targets = new List<IGimmickAffectable>();
        readonly List<GiantTile> m_giants = new List<GiantTile>();
        // 発動した役の牌(並べる順)と、ポン(同じ牌3枚)か
        readonly List<int> m_ponKinds = new List<int>();
        // ネット対戦のクライアント用: 受け取った手持ち(プレイヤーの並び順)
        readonly List<int[]> m_netHands = new List<int[]>();
        Phase m_phase = Phase.Idle;
        float m_phaseTimer;
        float m_dropTimer;
        float m_time;
        int m_refillLeft;
        float m_refillTimer;
        MatchPlayer m_trigger;
        // 発動した人の体(止めて動かしている間だけ)と、飛び立った位置・帰る位置
        DarumaCharacter m_flyer;
        Vector3 m_flyFrom, m_returnTo, m_returnFrom;
        bool m_returned;
        // 予告の位置(卓の中心からの相対)と、落とした位置
        Vector2 m_aim, m_dropAt;
        float m_giantScaleNow = 1.0f;
        bool m_giantsSolid;
        // 予告(円形のゲージ)・画面を暗くする板・カメラに映しておく目印
        MeshRenderer m_gauge, m_dark;
        Mesh m_gaugeMesh;
        readonly List<Color> m_gaugeColors = new List<Color>();
        readonly List<float> m_gaugeAngles = new List<float>();
        readonly List<TMPro.TextMeshPro> m_gaugeTexts = new List<TMPro.TextMeshPro>();
        GameObject m_focus;
        Vector3 m_lidBase;
        float m_lidOpen;
        MaterialPropertyBlock m_mpb;
        Renderer[] m_lidRenderers;
        AudioSource m_rumble;
        MeshRenderer m_dropShadow, m_dropRing;
        DarumaMatch m_match;
        bool m_hooked;

        Vector3 Center => transform.position;
        // 巨大な牌を落とす(落とした)位置
        Vector3 PonCenter => Center + new Vector3(m_dropAt.x, 0.0f, m_dropAt.y);
        Vector3 AimCenter => Center + new Vector3(m_aim.x, 0.0f, m_aim.y);
        bool Aiming => m_phase == Phase.Fly || m_phase == Phase.Aim;
        bool LidOpen => m_phase == Phase.Open || m_phase == Phase.Flow || m_phase == Phase.Close;
        float TileRestY => Center.y + MahjongTileArt.HalfHeight;

        private void Awake()
        {
            m_mpb = new MaterialPropertyBlock();
            if (m_lid != null) m_lidBase = m_lid.localPosition;
            m_rumble = GimmickSound.CreateLoop(transform, GimmickSound.Rumble);
            m_dropShadow = EffectMeshes.Create("PonShadow", transform, EffectMeshes.Disc, false);
            m_dropRing = EffectMeshes.Create("PonRing", transform, EffectMeshes.Ring, true);
            m_dropShadow.enabled = m_dropRing.enabled = false;

            // 画面を暗くする板(カメラの前に置く。カメラのそばに寄った人より奥なので、その人は暗くならない)
            var quad = new Mesh { name = "PonDarkQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            quad.RecalculateBounds();
            m_dark = EffectMeshes.Create("PonDark", null, quad, false);
            m_dark.sortingOrder = 0;
            m_dark.enabled = false;

            // 予告(円形のゲージ)。暗くする板より後に描く(暗くならない)
            m_gaugeMesh = new Mesh { name = "PonGauge" };
            m_gaugeMesh.MarkDynamic();
            m_gauge = EffectMeshes.Create("PonGauge", transform, m_gaugeMesh, false);
            m_gauge.sortingOrder = 20;
            m_gauge.enabled = false;
            m_dropShadow.sortingOrder = 19;
            m_dropRing.sortingOrder = 21;
        }

        private void OnDestroy()
        {
            if (m_dark != null) Destroy(m_dark.gameObject);
        }

        private void OnEnable()
        {
            m_match = DarumaMatch.Current;
            if (m_match != null) m_match.Started += ResetTable;
            DarumaCharacter.AnyDamaged += OnAnyDamaged;
            DarumaCharacter.AnyEliminated += OnAnyEliminated;
            m_hooked = true;
            MatchHud.AddExtra(this);
            ResetTable();
        }

        private void OnDisable()
        {
            if (m_match != null) m_match.Started -= ResetTable;
            m_match = null;
            if (m_hooked)
            {
                DarumaCharacter.AnyDamaged -= OnAnyDamaged;
                DarumaCharacter.AnyEliminated -= OnAnyEliminated;
                m_hooked = false;
            }
            MatchHud.RemoveExtra(this);
            ReleaseFlyer(false);
            if (m_netFlyer != null) MatchCamera.HiddenBodies.Remove(m_netFlyer.transform);
            m_netFlyer = null;
            HidePonVisuals();
            ClearField();
            ClearGiants();
            m_hands.Clear();
            m_netHands.Clear();
            foreach (var h in m_huds.Values) if (h.Root != null) Destroy(h.Root.gameObject);
            m_huds.Clear();
            GimmickSound.SetLoopVolume(m_rumble, 0.0f);
        }

        public void ResetTable()
        {
            ReleaseFlyer(false);
            HidePonVisuals();
            m_aim = m_dropAt = m_ponDropOffset;
            ClearField();
            ClearGiants();
            m_hands.Clear();
            m_ponKinds.Clear();
            m_phase = Phase.Idle;
            m_phaseTimer = 0.0f;
            m_dropTimer = m_dropInterval;
            m_trigger = null;
            m_lidOpen = 0.0f;
            ApplyLid();
        }

        void ClearField()
        {
            foreach (var t in m_field) DestroyVisual(t);
            m_field.Clear();
        }

        void ClearGiants()
        {
            foreach (var g in m_giants)
            {
                if (g.Visual == null) continue;
                // 吸い込まれずに卓に残っていた牌は、煙を出して消す
                if (!g.Gone && g.Visual.gameObject.activeSelf && g.Visual.position.y > Center.y - 1.0f) GimmickBurst.Play(m_refillEffect, g.Visual.position, 3.0f);
                Destroy(g.Visual.gameObject);
            }
            m_giants.Clear();
            m_giantsSolid = false;
        }

        //========================================
        // 毎フレーム
        //========================================

        private void Update()
        {
            bool held = GimmickClock.IsHeld;
            float dt = held ? 0.0f : Time.deltaTime;
            m_time += dt;
            bool host = !GimmickClock.IsNetClient;
            if (host)
            {
                using var relay = GimmickNet.Relay();   // ネット対戦用(suehara)
                if (!held && !IsMatchOver())
                {
                    UpdateDrops(dt);
                    UpdatePickups();
                    UpdatePhase(dt);
                }
                // 試合が終わったら、飛んでいる人を元の位置に戻す
                else if (IsMatchOver() && m_flyer != null) ReleaseFlyer(true);
                UpdateField(dt);
            }
            UpdatePonVisuals();
            UpdateFieldVisuals();
            ApplyLid();
            GimmickSound.SetLoopVolume(m_rumble, m_phase == Phase.Flow && !held ? 0.45f : 0.0f);
        }

        private void FixedUpdate()
        {
            if (GimmickClock.IsNetClient || GimmickClock.IsHeld) return;
            if (m_phase == Phase.Flow)
            {
                ApplyFlow();
                PullGiants();
            }
            UpdateGiantFall();
        }

        // ツモ(洗牌)の間、巨大な牌も物理のまま中央の穴へ吸い込む
        void PullGiants()
        {
            foreach (var g in m_giants)
            {
                if (g.Body == null || g.Body.isKinematic || g.Gone) continue;
                Vector3 to = Center - g.Body.position;
                to.y = 0.0f;
                if (to.sqrMagnitude < 0.01f) continue;
                // 卓の上に接地している間だけ強く引く(穴へ落ち始めたら元の強さ)
                float restY = Center.y + MahjongTileArt.HalfHeight * m_giantScaleNow;
                bool grounded = g.Body.position.y >= restY - m_giantGroundedTolerance;
                float accel = m_giantFlowAccel * (grounded ? m_giantGroundedFlowScale : 1.0f);
                g.Body.AddForce(to.normalized * accel, ForceMode.Acceleration);
            }
        }

        // 穴に落ちた巨大な牌は止めて隠す
        void UpdateGiantFall()
        {
            foreach (var g in m_giants)
            {
                if (g.Gone || g.Body == null || g.Body.isKinematic) continue;
                if (g.Body.position.y > Center.y - 6.0f) continue;
                g.Gone = true;
                g.Body.isKinematic = true;
                g.Visual.gameObject.SetActive(false);
            }
        }

        // 発動した人の体は、カメラが動いたあとに合わせる(カメラのそばにぴったり付いていくように)
        private void LateUpdate()
        {
            if (GimmickClock.IsNetClient || GimmickClock.IsHeld) return;
            UpdateFlyer();
        }

        bool IsMatchOver()
        {
            var match = DarumaMatch.Current;
            return match != null && match.IsFinished;
        }

        //========================================
        // 牌が降る・拾う・落とす
        //========================================

        void UpdateDrops(float dt)
        {
            m_dropTimer -= dt;
            if (m_dropTimer > 0.0f) return;
            m_dropTimer = m_dropInterval;
            if (m_field.Count >= m_maxFieldTiles) return;
            Vector3 p = RandomDropPoint();
            var t = new FieldTile
            {
                Kind = Random.Range(0, MahjongTileArt.KindCount), Position = p, State = 0, Timer = m_dropWarningTime,
                Yaw = Random.Range(-25.0f, 25.0f),
            };
            m_field.Add(t);
        }

        Vector3 RandomDropPoint()
        {
            Vector2 half = m_tableSize * 0.5f - Vector2.one * m_dropMargin;
            for (int i = 0; i < 12; i++)
            {
                var p = new Vector3(Random.Range(-half.x, half.x), 0.0f, Random.Range(-half.y, half.y));
                if (InLid(p, 0.8f)) continue;
                bool near = false;
                foreach (var t in m_field) if ((t.Position - (Center + p)).sqrMagnitude < 2.0f * 2.0f) near = true;
                if (!near) return Center + p;
            }
            return Center + new Vector3(Random.Range(-half.x, half.x), 0.0f, half.y);
        }

        // 中央のふたの範囲か(local: 卓の中心からの位置)
        bool InLid(Vector3 local, float margin = 0.0f)
        {
            return Mathf.Abs(local.x) < m_lidSize.x * 0.5f + margin && Mathf.Abs(local.z) < m_lidSize.y * 0.5f + margin;
        }

        void UpdateField(float dt)
        {
            for (int i = m_field.Count - 1; i >= 0; i--)
            {
                var t = m_field[i];
                t.NoPickup = Mathf.Max(0.0f, t.NoPickup - dt);
                switch (t.State)
                {
                    case 0:
                        t.Timer -= dt;
                        if (t.Timer <= 0.0f)
                        {
                            t.State = 1;
                            t.Velocity = Vector3.zero;
                            t.Position.y = Center.y + m_fallHeight;
                        }
                        break;
                    case 1:
                        StepFalling(t, dt);
                        break;
                    default:
                        t.Age += dt;
                        StepSliding(t, dt);
                        if (LidOpen && InLid(t.Position - Center))
                        {
                            t.State = 1;
                            t.Velocity = new Vector3(t.Velocity.x, 0.0f, t.Velocity.z);
                        }
                        if (t.Age >= m_tileLifetime) t.Lost = true;
                        break;
                }
                if (t.Lost)
                {
                    DestroyVisual(t);
                    m_field.RemoveAt(i);
                }
            }
            if (dt > 0.0f) CollideTiles();
        }

        // 卓の壁(遊べる範囲の外)で跳ね返す
        void BounceWalls(ref Vector3 pos, ref Vector3 vel)
        {
            Vector3 local = pos - Center;
            Vector2 half = m_tableSize * 0.5f - Vector2.one * m_tileRadius;
            if (Mathf.Abs(local.x) > half.x) { local.x = Mathf.Sign(local.x) * half.x; vel.x = -vel.x * 0.5f; }
            if (Mathf.Abs(local.z) > half.y) { local.z = Mathf.Sign(local.z) * half.y; vel.z = -vel.z * 0.5f; }
            pos = Center + local;
        }

        // 落ちている牌: 床(卓の上・ふたが閉じている所)に着いたら止まる。壁で跳ね返る。穴なら下まで落ちて消える
        void StepFalling(FieldTile t, float dt)
        {
            t.Velocity.y -= m_gravity * dt;
            Vector3 next = t.Position + t.Velocity * dt;
            BounceWalls(ref next, ref t.Velocity);
            t.Yaw += t.YawSpeed * dt;
            bool overHole = LidOpen && InLid(next - Center);
            float floorY = TileRestY;
            if (!overHole && next.y <= floorY && t.Position.y >= floorY - 0.3f)
            {
                next.y = floorY;
                // 着地でほとんど止まる(重い牌はあまり滑らない)
                t.Velocity = new Vector3(t.Velocity.x * 0.25f, 0.0f, t.Velocity.z * 0.25f);
                t.YawSpeed *= 0.3f;
                t.State = 2;
                GimmickBurst.Play(m_landEffect, next);
                GimmickSound.Play(GimmickSound.Pop, m_volume * 0.6f, 1.4f);
            }
            t.Position = next;
            if (next.y < Center.y - 10.0f) t.Lost = true;
        }

        // 床の上の牌: 滑って止まる。洗牌の間は中央へ流れる
        void StepSliding(FieldTile t, float dt)
        {
            if (m_phase == Phase.Flow)
            {
                Vector3 to = Center - t.Position;
                to.y = 0.0f;
                if (to.sqrMagnitude > 0.01f) t.Velocity += to.normalized * (m_tileFlowAccel * dt);
            }
            else
            {
                Vector3 h = new Vector3(t.Velocity.x, 0.0f, t.Velocity.z);
                h = Vector3.MoveTowards(h, Vector3.zero, m_tileFriction * dt);
                t.Velocity = h;
            }
            t.YawSpeed = Mathf.MoveTowards(t.YawSpeed, 0.0f, 360.0f * dt);
            t.Yaw += t.YawSpeed * dt;
            Vector3 next = t.Position + new Vector3(t.Velocity.x, 0.0f, t.Velocity.z) * dt;
            BounceWalls(ref next, ref t.Velocity);
            next.y = TileRestY;
            t.Position = next;
        }

        // 牌どうし・牌と積み木・牌と巨大な牌の当たり(牌だけが動く。積み木には影響しない)
        void CollideTiles()
        {
            float r = m_tileRadius;
            // 牌どうし
            for (int i = 0; i < m_field.Count; i++)
            {
                var a = m_field[i];
                if (a.State == 0) continue;
                for (int j = i + 1; j < m_field.Count; j++)
                {
                    var b = m_field[j];
                    if (b.State == 0 || Mathf.Abs(a.Position.y - b.Position.y) > MahjongTileArt.Size.y) continue;
                    Vector3 d = b.Position - a.Position;
                    d.y = 0.0f;
                    float dist = d.magnitude;
                    if (dist >= r * 2.0f) continue;
                    Vector3 n = dist > 0.0001f ? d / dist : Quaternion.Euler(0, Random.Range(0.0f, 360.0f), 0) * Vector3.forward;
                    float push = (r * 2.0f - dist) * 0.5f;
                    a.Position -= n * push;
                    b.Position += n * push;
                    float va = Vector3.Dot(a.Velocity, n), vb = Vector3.Dot(b.Velocity, n);
                    if (va - vb > 0.0f)
                    {
                        float k = (va - vb) * (1.0f + m_tileBounce) * 0.5f;
                        a.Velocity -= n * k;
                        b.Velocity += n * k;
                        a.YawSpeed += Random.Range(-m_hitSpin, m_hitSpin) * 0.5f;
                        b.YawSpeed += Random.Range(-m_hitSpin, m_hitSpin) * 0.5f;
                        if (k > 1.5f) GimmickSound.Play(GimmickSound.Pop, m_volume * 0.4f, 1.8f);
                    }
                }
            }

            // 積み木: 牌だけが一方的に弾かれる
            var blocks = TumikiBlock.All;
            float br = TumikiBlock.kRadius;
            foreach (var t in m_field)
            {
                if (t.State == 0) continue;
                for (int i = 0; i < blocks.Count; i++)
                {
                    var block = blocks[i];
                    if (block == null || block.CurrentState == TumikiBlock.State.Stacked) continue;
                    Vector3 bp = block.transform.position;
                    if (Mathf.Abs(bp.y - t.Position.y) > TumikiBlock.kHeight * 0.5f + MahjongTileArt.HalfHeight + 0.05f) continue;
                    Vector3 d = t.Position - bp;
                    d.y = 0.0f;
                    float dist = d.magnitude;
                    if (dist >= r + br) continue;
                    Vector3 n = dist > 0.0001f ? d / dist : Quaternion.Euler(0, Random.Range(0.0f, 360.0f), 0) * Vector3.forward;
                    t.Position = new Vector3(bp.x, t.Position.y, bp.z) + n * (r + br + 0.01f);
                    Vector3 bv = block.Rigidbody != null ? block.Rigidbody.linearVelocity : Vector3.zero;
                    float approach = Mathf.Max(0.0f, Vector3.Dot(bv, n));
                    float want = Mathf.Max(m_blockKickMin, approach * m_blockKickRate);
                    float vn = Vector3.Dot(t.Velocity, n);
                    if (vn < want)
                    {
                        t.Velocity += n * (want - vn);
                        t.YawSpeed += Random.Range(-m_hitSpin, m_hitSpin);
                        if (want > m_hopSpeed && t.State == 2)
                        {
                            // とても速く弾かれたときだけ少し跳ねる
                            t.State = 1;
                            t.Velocity.y = Mathf.Min(3.0f, want * 0.25f);
                            t.Position.y += 0.02f;
                        }
                        GimmickSound.Play(GimmickSound.Pop, m_volume * 0.5f, 1.7f);
                    }
                }

                // 着地した巨大な牌: 外へ押し出して跳ね返す
                if (!m_giantsSolid) continue;
                foreach (var g in m_giants)
                {
                    if (g.Gone || g.Visual == null) continue;
                    Vector3 local = Quaternion.Inverse(g.Visual.rotation) * (t.Position - g.Visual.position);
                    Vector3 half = Vector3.Scale(MahjongTileArt.Size, g.Visual.localScale) * 0.5f + new Vector3(r, 0, r);
                    if (Mathf.Abs(local.x) >= half.x || Mathf.Abs(local.z) >= half.z || local.y > half.y + 0.2f) continue;
                    Vector3 lv = Quaternion.Inverse(g.Visual.rotation) * t.Velocity;
                    if (half.x - Mathf.Abs(local.x) < half.z - Mathf.Abs(local.z))
                    {
                        local.x = Mathf.Sign(local.x) * half.x;
                        if (lv.x * Mathf.Sign(local.x) < 0.0f) lv.x = -lv.x * 0.6f;
                    }
                    else
                    {
                        local.z = Mathf.Sign(local.z) * half.z;
                        if (lv.z * Mathf.Sign(local.z) < 0.0f) lv.z = -lv.z * 0.6f;
                    }
                    t.Position = g.Visual.position + g.Visual.rotation * local;
                    t.Position.y = Mathf.Max(t.Position.y, TileRestY);
                    t.Velocity = g.Visual.rotation * lv;
                }
            }
        }

        void UpdatePickups()
        {
            var match = DarumaMatch.Current;
            if (match == null) return;
            foreach (var p in match.Players)
            {
                if (p == null || p.Character == null || !p.Character.CanBeAffected) continue;
                Vector3 pos = p.Character.transform.position;
                for (int i = m_field.Count - 1; i >= 0; i--)
                {
                    var t = m_field[i];
                    if (t.State != 2 || t.NoPickup > 0.0f) continue;
                    Vector3 d = t.Position - pos;
                    if (Mathf.Abs(d.y) > 1.6f || new Vector2(d.x, d.z).sqrMagnitude > m_pickupRadius * m_pickupRadius) continue;
                    var hand = Hand(p);
                    if (hand.Count >= m_maxHand) continue;
                    hand.Add(t.Kind);
                    GimmickBurst.Play(m_pickupEffect, t.Position);
                    GimmickSound.Play(GimmickSound.Pop, m_volume, 1.8f);
                    DestroyVisual(t);
                    m_field.RemoveAt(i);
                }
            }
        }

        List<int> Hand(MatchPlayer p)
        {
            if (!m_hands.TryGetValue(p, out var hand))
            {
                hand = new List<int>();
                m_hands[p] = hand;
            }
            return hand;
        }

        MatchPlayer PlayerOf(DarumaCharacter c)
        {
            var match = DarumaMatch.Current;
            if (match == null || c == null) return null;
            foreach (var p in match.Players) if (p != null && p.Character == c) return p;
            return null;
        }

        // 攻撃を受けた: 手持ちからランダムに1枚落とす
        void OnAnyDamaged(DarumaCharacter c)
        {
            if (GimmickClock.IsNetClient) return;
            var p = PlayerOf(c);
            if (p == null || !m_hands.TryGetValue(p, out var hand) || hand.Count == 0) return;
            using var relay = GimmickNet.Relay();
            int i = Random.Range(0, hand.Count);
            int kind = hand[i];
            hand.RemoveAt(i);
            DropTile(kind, c.transform.position, -c.transform.forward);
        }

        // やられた: 手持ちを全部落とす
        void OnAnyEliminated(DarumaCharacter c)
        {
            if (GimmickClock.IsNetClient) return;
            var p = PlayerOf(c);
            if (p == null || !m_hands.TryGetValue(p, out var hand) || hand.Count == 0) return;
            using var relay = GimmickNet.Relay();
            Vector3 pos = c.EliminationPosition;
            for (int i = 0; i < hand.Count; i++)
            {
                float a = i * 360.0f / hand.Count;
                DropTile(hand[i], pos, Quaternion.Euler(0, a, 0) * Vector3.forward);
            }
            hand.Clear();
        }

        void DropTile(int kind, Vector3 from, Vector3 dir)
        {
            dir.y = 0.0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Random.insideUnitSphere;
            dir.y = 0.0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            var t = new FieldTile
            {
                Kind = kind, Position = from + Vector3.up * 1.2f, State = 1, NoPickup = m_pickupDelay,
                Velocity = dir * m_popSideSpeed + Vector3.up * m_popUpSpeed, Yaw = Random.Range(0.0f, 360.0f), YawSpeed = Random.Range(-360.0f, 360.0f),
            };
            m_field.Add(t);
        }

        //========================================
        // 役と、ポン → 巨大な牌 → ツモ(洗牌)
        //========================================

        // 役がそろっていれば、役の牌を手持ちから消し、並べる順に kinds に入れて true
        bool TakeYaku(List<int> hand, List<int> kinds)
        {
            var counts = new int[MahjongTileArt.KindCount];
            foreach (int k in hand) if (k >= 0 && k < counts.Length) counts[k]++;
            kinds.Clear();
            for (int k = 0; k < counts.Length; k++)
            {
                if (counts[k] < 3) continue;
                for (int n = 0; n < 3; n++) { hand.Remove(k); kinds.Add(k); }
                return true;
            }
            foreach (int c in counts) if (c == 0) return false;
            for (int k = 0; k < counts.Length; k++) { hand.Remove(k); kinds.Add(k); }
            return true;
        }

        bool IsPon => m_ponKinds.Count == 3;

        void UpdatePhase(float dt)
        {
            m_phaseTimer += dt;
            switch (m_phase)
            {
                case Phase.Idle:
                    // 役がそろった人がいれば発動(発動中にそろった人は、終わってから)
                    var match = DarumaMatch.Current;
                    if (match == null) break;
                    foreach (var p in match.Players)
                    {
                        if (p == null || !m_hands.TryGetValue(p, out var hand) || !TakeYaku(hand, m_ponKinds)) continue;
                        BeginPon(p);
                        break;
                    }
                    break;
                case Phase.Fly:
                    if (m_phaseTimer >= m_flyTime) SetPhase(Phase.Aim);
                    break;
                case Phase.Aim:
                    if (UpdateAim(dt)) DropGiants();
                    break;
                case Phase.GiantDrop:
                    if (m_phaseTimer >= m_giantDropTime)
                    {
                        LandGiants();
                        SetPhase(Phase.Settle);
                    }
                    break;
                case Phase.Settle:
                    // 落としたあと: 発動した人が上空から元の位置へ戻る(戻り終えたら止めるのをやめ、少しの間無敵)
                    if (!m_returned && m_phaseTimer >= m_returnTime) ReleaseFlyer(true);
                    if (m_phaseTimer >= m_settleTime) BeginTsumo();
                    break;
                case Phase.Announce:
                    if (m_phaseTimer >= m_announceTime) SetPhase(Phase.Open);
                    break;
                case Phase.Open:
                    m_lidOpen = Mathf.Clamp01(m_phaseTimer / m_openTime);
                    if (m_phaseTimer >= m_openTime) SetPhase(Phase.Flow);
                    break;
                case Phase.Flow:
                    m_lidOpen = 1.0f;
                    if (m_phaseTimer >= m_flowTime) SetPhase(Phase.Close);
                    break;
                case Phase.Close:
                    m_lidOpen = 1.0f - Mathf.Clamp01(m_phaseTimer / m_closeTime);
                    if (m_phaseTimer >= m_closeTime)
                    {
                        m_lidOpen = 0.0f;
                        SetPhase(Phase.Refill);
                        m_refillLeft = m_refillCount;
                        m_refillTimer = 0.0f;
                    }
                    break;
                case Phase.Refill:
                    m_refillTimer -= dt;
                    if (m_refillLeft > 0 && m_refillTimer <= 0.0f)
                    {
                        m_refillTimer = m_refillCount > 0 ? m_refillTime / m_refillCount : 0.0f;
                        RefillOne(m_refillCount - m_refillLeft);
                        m_refillLeft--;
                    }
                    if (m_refillLeft <= 0) SetPhase(Phase.Idle);
                    break;
            }
        }

        void SetPhase(Phase phase)
        {
            m_phase = phase;
            m_phaseTimer = 0.0f;
            // ねらう間は、画面上部に「ボタンを押して落とせ！」(人が発動したときだけ。落としたら消す)
            if (phase == Phase.Aim && m_trigger != null && !m_trigger.IsCpu)
            {
                m_aimMessageId = StageMessageUI.Show(m_aimMessage, Color.white, 1.0f, m_aimTime + 0.3f);
            }
            else if (phase != Phase.Aim) HideAimMessage();
            if (phase == Phase.Open)
            {
                if (m_lidCollider != null) m_lidCollider.enabled = false;
                // 巨大な牌は物理のまま、穴へ吸い込まれていく(減速を弱める)
                foreach (var g in m_giants)
                {
                    if (g.Body == null || g.Body.isKinematic) continue;
                    g.Body.linearDamping = m_giantFlowDamping;
                    g.Body.WakeUp();
                }
                GimmickSound.Play(GimmickSound.Launch, m_volume, 0.5f);
            }
            if (phase == Phase.Refill) ClearGiants();
            if (phase == Phase.Idle)
            {
                m_trigger = null;
                m_ponKinds.Clear();
            }
        }

        // 1. ポン: 発動した人が回転しながら上空へ飛び、カメラのそばに寄る(画面が少し暗くなる)
        void BeginPon(MatchPlayer p)
        {
            m_trigger = p;
            m_aim = ClampAim(m_ponDropOffset);
            m_dropAt = m_aim;
            BuildGiants();
            SetPhase(Phase.Fly);
            StageMessageUI.Show(IsPon ? $"{p.Label} ポン！" : $"{p.Label} そろった！", Color.white, 1.2f, 2.2f);
            GimmickSound.Play(GimmickSound.Launch, m_volume * 1.2f, 0.8f);

            m_returned = false;
            m_flyer = p.Character;
            if (m_flyer != null)
            {
                m_flyFrom = m_flyer.transform.position;
                m_returnTo = m_flyFrom;
                m_flyer.SetSuspended(true);
                MatchCamera.HiddenBodies.Add(m_flyer.transform);
                GimmickBurst.Play(m_impactEffect, m_flyFrom + Vector3.up * 0.1f, 0.4f);
            }
            // 予告の位置をカメラに映しておく
            if (m_focus == null)
            {
                m_focus = new GameObject("PonAimFocus");
                m_focus.transform.SetParent(transform, false);
                m_focus.AddComponent<CameraFocusTarget>();
            }
            m_focus.SetActive(true);
            m_focus.transform.position = AimCenter;
        }

        int m_aimMessageId;

        void HideAimMessage()
        {
            StageMessageUI.Hide(m_aimMessageId);
            m_aimMessageId = 0;
        }

        // 2. ねらう: 発動した人の移動入力で予告を動かす。ボタン(ジャンプ・攻撃・ショット)か時間切れで true(落とす)
        bool UpdateAim(float dt)
        {
            bool confirm = m_phaseTimer >= m_aimTime;
            Vector2 move = Vector2.zero;
            if (m_trigger != null && m_trigger.IsCpu)
            {
                // CPU: 近くの相手をねらう。重なったら(少し待ってから)落とす
                Vector2? target = NearestOpponent();
                if (target.HasValue)
                {
                    Vector2 to = ClampAim(target.Value) - m_aim;
                    float step = m_cpuAimSpeed * dt;
                    m_aim += to.magnitude <= step ? to : to.normalized * step;
                    if (to.magnitude < 0.6f && m_phaseTimer > m_aimTime * 0.4f) confirm = true;
                }
                else if (m_phaseTimer > m_aimTime * 0.4f) confirm = true;
            }
            else if (m_flyer != null && m_flyer.CommandSource != null)
            {
                DarumaCommand command = m_flyer.CommandSource.ReadCommand();
                move = Vector2.ClampMagnitude(command.Move, 1.0f);
                if (m_phaseTimer >= m_aimButtonDelay && (command.Jump || command.Sweep || command.Shot)) confirm = true;
            }
            m_aim = ClampAim(m_aim + move * (m_aimSpeed * dt));
            if (m_focus != null) m_focus.transform.position = AimCenter;
            return confirm;
        }

        // 予告の牌の並びが卓からはみ出さないように収める
        Vector2 ClampAim(Vector2 aim)
        {
            Vector2 half = m_tableSize * 0.5f - PonHalfSize - Vector2.one * 0.2f;
            half = Vector2.Max(half, Vector2.zero);
            return new Vector2(Mathf.Clamp(aim.x, -half.x, half.x), Mathf.Clamp(aim.y, -half.y, half.y));
        }

        // 発動した人以外で一番近い、生きている相手の位置(卓の中心からの相対)
        Vector2? NearestOpponent()
        {
            var match = DarumaMatch.Current;
            if (match == null) return null;
            Vector3 from = Center + new Vector3(m_aim.x, 0, m_aim.y);
            float best = float.MaxValue;
            Vector2? found = null;
            foreach (var p in match.Players)
            {
                if (p == null || p == m_trigger || p.Character == null || !p.Character.CanBeAffected) continue;
                Vector3 pos = p.Character.transform.position;
                float d = (pos - from).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                found = new Vector2(pos.x - Center.x, pos.z - Center.z);
            }
            return found;
        }

        // 3. 落とす: 予告の位置に巨大な牌を降らせる
        void DropGiants()
        {
            m_dropAt = m_aim;
            SetPhase(Phase.GiantDrop);
            GimmickBurst.Play(m_flashEffect, PonCenter + Vector3.up * m_dropHeight * 0.6f, 0.6f);
            GimmickSound.Play(GimmickSound.Launch, m_volume * 1.4f, 0.6f);
            if (m_focus != null) m_focus.SetActive(false);
            // 帰る位置が牌の下なら、牌の外へずらす
            if (m_flyer != null)
            {
                m_returnFrom = m_flyer.transform.position;
                m_returnTo = OutsidePon(m_returnTo, DarumaCharacter.kBodyRadius + 0.3f);
            }
        }

        // 位置が巨大な牌の並び(上から見た長方形)の中なら、一番近い辺の外へずらす
        Vector3 OutsidePon(Vector3 pos, float margin)
        {
            Vector2 half = PonHalfSize + Vector2.one * margin;
            Vector3 c = PonCenter + new Vector3(0, 0, m_ponFootprintOffsetZ);
            float dx = pos.x - c.x, dz = pos.z - c.z;
            if (Mathf.Abs(dx) >= half.x || Mathf.Abs(dz) >= half.y) return pos;
            if (half.x - Mathf.Abs(dx) < half.y - Mathf.Abs(dz)) pos.x = c.x + Mathf.Sign(dx == 0 ? 1 : dx) * half.x;
            else pos.z = c.z + Mathf.Sign(dz == 0 ? 1 : dz) * half.y;
            // 卓の外へ出たら、反対側へ
            Vector3 local = pos - Center;
            Vector2 table = m_tableSize * 0.5f - Vector2.one * 0.8f;
            if (Mathf.Abs(local.x) > table.x) pos.x = c.x - Mathf.Sign(local.x) * half.x;
            if (Mathf.Abs(local.z) > table.y) pos.z = c.z - Mathf.Sign(local.z) * half.y;
            return pos;
        }

        // 発動した人の体を、段階に合わせて動かす(ホストだけ。体の位置はネット対戦ではそのまま届く)
        void UpdateFlyer()
        {
            if (m_flyer == null || m_returned) return;
            var cam = Camera.main;
            Vector3 side = cam != null ? cam.transform.TransformPoint(m_cameraSideOffset) : m_flyFrom + Vector3.up * 8.0f;
            Vector3 pos;
            float spin;
            switch (m_phase)
            {
                case Phase.Fly:
                {
                    // 回転しながら真上へ跳び、カメラのそばへ
                    float t = Mathf.Clamp01(m_phaseTimer / m_flyTime);
                    Vector3 up = m_flyFrom + Vector3.up * 6.0f;
                    pos = Bezier(m_flyFrom, up, side + Vector3.up * 2.0f, side, Mathf.SmoothStep(0.0f, 1.0f, t));
                    spin = m_flySpin * Time.deltaTime;
                    break;
                }
                case Phase.Aim:
                case Phase.GiantDrop:
                    // カメラのそばでゆっくり回る(少し上下に揺れる)
                    m_returnFrom = side;
                    pos = side + Vector3.up * (Mathf.Sin(m_time * 3.0f) * 0.15f);
                    spin = 120.0f * Time.deltaTime;
                    break;
                default:
                {
                    // 上空から元の位置へ戻る
                    float t = Mathf.Clamp01(m_phaseTimer / m_returnTime);
                    Vector3 above = m_returnTo + Vector3.up * 9.0f;
                    pos = Bezier(m_returnFrom, m_returnFrom + Vector3.up * 2.0f, above, m_returnTo + Vector3.up * 0.05f, t * t);
                    spin = Mathf.Lerp(m_flySpin, 0.0f, t) * Time.deltaTime;
                    break;
                }
            }
            m_flyer.Teleport(pos, spin);
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1.0f - t;
            return u * u * u * a + 3.0f * u * u * t * b + 3.0f * u * t * t * c + t * t * t * d;
        }

        // 発動した人を元に戻す(atReturn: 帰還し終えた。少しの間無敵にする)
        void ReleaseFlyer(bool atReturn)
        {
            if (m_flyer != null)
            {
                MatchCamera.HiddenBodies.Remove(m_flyer.transform);
                // 途中で止めたとき(試合のやり直し・ステージの切り替え・試合の終わり)も、元の位置に戻してから放す
                m_flyer.Teleport(m_returnTo + Vector3.up * 0.05f, 0.0f);
                m_flyer.SetSuspended(false);
                if (atReturn)
                {
                    m_flyer.SetInvincible(m_returnInvincible);
                    GimmickBurst.Play(m_landEffect, m_returnTo, 2.0f);
                }
            }
            m_flyer = null;
            m_returned = true;
            if (m_focus != null) m_focus.SetActive(false);
        }

        // 巨大な牌の並びの、上から見た大きさの半分と、並びの中心の前後のずれ(横に倒した牌を手前にそろえるため)
        Vector2 PonHalfSize => m_ponHalf;
        Vector2 m_ponHalf = new Vector2(3.0f, 2.0f);
        float m_ponFootprintOffsetZ;

        // 巨大な牌を作る(並びの位置・向きを決める)。ポンは縦2・横1、4種は縦3・横1(最後の1枚を横に倒す)
        void BuildGiants()
        {
            ClearGiants();
            int n = m_ponKinds.Count;
            if (n == 0) return;
            Vector3 size = MahjongTileArt.Size;
            float rowAtOne = (n - 1) * (size.x + m_giantGap) + size.z;
            m_giantScaleNow = m_giantScale;
            float s = m_giantScaleNow;
            float width = rowAtOne * s;
            m_ponHalf = new Vector2(width * 0.5f, size.z * s * 0.5f);
            m_ponFootprintOffsetZ = 0.0f;
            float x = -width * 0.5f;
            for (int i = 0; i < n; i++)
            {
                bool sideways = i == n - 1;
                float w = (sideways ? size.z : size.x) * s;
                // 横に倒した牌は手前(画面の下)の縁をそろえる
                float z = sideways ? -(size.z - size.x) * s * 0.5f : 0.0f;
                var visual = MahjongTileArt.Create($"GiantTile_{MahjongTileArt.KindNames[m_ponKinds[i]]}", transform, m_ponKinds[i], 1.0f, m_tileMaterial, m_backMaterial);
                visual.gameObject.AddComponent<ReflectWall>();
                var col = visual.gameObject.AddComponent<BoxCollider>();
                col.size = size;
                col.enabled = false;
                // 落ちてくる間は動かさない(スクリプトで動かす)。着地したら物理で動かす
                var body = visual.gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.mass = m_giantMass;
                body.linearDamping = m_giantDamping;
                body.angularDamping = m_giantAngularDamping;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                visual.gameObject.SetActive(false);
                var g = new GiantTile
                {
                    Visual = visual, Collider = col, Body = body,
                    // 並びの中心からの位置(落とす位置は後で決まる)
                    Rest = new Vector3(x + w * 0.5f, MahjongTileArt.HalfHeight * s, z),
                    RestYaw = sideways ? 90.0f : 0.0f,
                };
                m_giants.Add(g);
                x += w + m_giantGap * s;
            }
            BuildGaugeMesh();
        }

        // 4. 巨大な牌が着地した: 近くのものを外へはじき、障害物にする
        void LandGiants()
        {
            Vector3 c = PonCenter;
            // 下敷きにされたプレイヤーは即死(体の中心が牌の下にあり、牌の上面より低い)
            var match = DarumaMatch.Current;
            if (match != null)
            {
                foreach (var p in match.Players)
                {
                    DarumaCharacter ch = p != null ? p.Character : null;
                    if (ch == null || !ch.CanBeAffected || !UnderGiant(ch.transform.position)) continue;
                    ch.Eliminate(EliminationCause.Other, Vector3.down, ch.transform.position);
                }
            }
            Vector3 half = new Vector3(PonHalfSize.x + m_impactMargin, 1.5f, PonHalfSize.y + m_impactMargin);
            GimmickUtil.OverlapBox(c + Vector3.up * 1.0f, half, transform.rotation, m_targets);
            foreach (var target in m_targets)
            {
                Vector3 away = target.Rigidbody.position - c;
                away.y = 0.0f;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                away.y = 0.0f;
                target.AddKnockback(away.normalized * m_impactPower + Vector3.up * m_impactUpPower);
            }
            GimmickBurst.Play(m_impactEffect, c + Vector3.up * 0.1f);
            GimmickSound.Play(GimmickSound.Launch, m_volume * 1.5f, 0.4f);
            BilliardFx.Shake(0.45f, 0.4f);
            SetGiantsSolid(true);
            // ここからは物理で動く(重いので、プレイヤーが押すとほんの少し動く)
            foreach (var g in m_giants)
            {
                if (g.Body == null) continue;
                Vector3 rest = PonCenter + g.Rest;
                g.Body.position = rest;
                g.Body.rotation = Quaternion.Euler(0, g.RestYaw, 0);
                g.Visual.SetPositionAndRotation(rest, g.Body.rotation);
                g.Body.isKinematic = false;
                g.Body.linearVelocity = Vector3.zero;
                g.Body.angularVelocity = Vector3.zero;
                g.Body.linearDamping = m_giantDamping;
            }
        }

        // 位置が、着地した巨大な牌(どれか1枚)の下か
        bool UnderGiant(Vector3 pos)
        {
            foreach (var g in m_giants)
            {
                Vector3 rest = PonCenter + g.Rest;
                Vector3 local = Quaternion.Inverse(Quaternion.Euler(0, g.RestYaw, 0)) * (pos - rest);
                Vector3 half = MahjongTileArt.Size * (m_giantScaleNow * 0.5f);
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z && local.y <= half.y + m_crushTopMargin) return true;
            }
            return false;
        }

        void SetGiantsSolid(bool solid)
        {
            m_giantsSolid = solid;
            foreach (var g in m_giants) if (g.Collider != null) g.Collider.enabled = solid;
        }

        // 物理で動いている巨大な牌か(ホスト: 着地してから片付けるまで)
        bool GiantsPhysical => m_giantsSolid && (m_phase == Phase.Settle || m_phase == Phase.Announce || m_phase == Phase.Open || m_phase == Phase.Flow || m_phase == Phase.Close);

        // 4. ツモ(洗牌)
        void BeginTsumo()
        {
            SetPhase(Phase.Announce);
            string label = m_trigger != null ? m_trigger.Label : "";
            StageMessageUI.Show($"{label} ツモ！", Color.white, 1.1f, 2.0f);
            AudioClip clip = GimmickSound.Charge;
            GimmickSound.Play(clip, m_volume, clip.length / Mathf.Max(0.1f, m_announceTime));
        }

        // 卓の上のプレイヤー(発動した人を除く)と積み木を、中央へ流す
        void ApplyFlow()
        {
            Vector3 half = new Vector3(m_tableSize.x * 0.5f, 1.5f, m_tableSize.y * 0.5f);
            GimmickUtil.OverlapBox(Center + Vector3.up * 1.5f, half, transform.rotation, m_targets);
            DarumaCharacter immune = m_trigger != null ? m_trigger.Character : null;
            foreach (var target in m_targets)
            {
                if (target is DarumaCharacter c && c == immune) continue;
                Vector3 to = Center - target.Rigidbody.position;
                to.y = 0.0f;
                if (to.sqrMagnitude < 0.04f) continue;
                target.AddForce(to.normalized * m_flowAccel);
            }
        }

        // 4辺の縁の内側から積み木をせり上げる(盤面の上限まで)
        void RefillOne(int index)
        {
            if (m_tumikiPrefab == null) return;
            if (m_spawner != null && TumikiSpawner.CountBlocksOnBoard() >= m_spawner.MaxBlocksOnBoard) return;
            int side = index % 4;
            int slot = index / 4;
            int perSide = Mathf.Max(1, Mathf.CeilToInt(m_refillCount / 4.0f));
            float along = (slot + 0.5f) / perSide - 0.5f;
            Vector2 half = m_tableSize * 0.5f - Vector2.one * m_refillInset;
            // 角を避けて辺の中ほどに並べる
            along *= 0.6f;
            Vector3 local;
            Vector3 inward;
            switch (side)
            {
                case 0: local = new Vector3(along * m_tableSize.x, 0, half.y); inward = Vector3.back; break;
                case 1: local = new Vector3(half.x, 0, -along * m_tableSize.y); inward = Vector3.left; break;
                case 2: local = new Vector3(-along * m_tableSize.x, 0, -half.y); inward = Vector3.forward; break;
                default: local = new Vector3(-half.x, 0, along * m_tableSize.y); inward = Vector3.right; break;
            }
            Vector3 pos = Center + local + Vector3.up * (TumikiBlock.kHeight * 0.5f + 0.05f);
            TumikiBlock block = Instantiate(m_tumikiPrefab, pos, Quaternion.LookRotation(inward));
            block.SetColor(PickColor());
            if (block.Rigidbody != null) block.Rigidbody.linearVelocity = Vector3.up * 4.0f + inward * 1.0f;
            GimmickBurst.Play(m_refillEffect, Center + local + Vector3.up * 0.05f);
            GimmickSound.Play(GimmickSound.Pop, m_volume, Random.Range(0.8f, 1.0f));
        }

        TumikiColor PickColor()
        {
            var pattern = m_spawner != null ? m_spawner.NextPattern : null;
            if (pattern != null)
            {
                var colors = pattern.CreateColors(1);
                if (colors != null && colors.Count > 0) return colors[0];
            }
            return (TumikiColor)Random.Range(0, 4);
        }

        //========================================
        // 見た目
        //========================================

        void ApplyLid()
        {
            if (m_lid != null)
            {
                // 開くときは速く沈み始める(すぐに穴に見えるように)
                float k = 1.0f - (1.0f - m_lidOpen) * (1.0f - m_lidOpen);
                m_lid.localPosition = m_lidBase + Vector3.down * (m_lidDrop * k);
                bool show = m_lidOpen < 0.98f;
                if (m_lidRenderers == null) m_lidRenderers = m_lid.GetComponentsInChildren<Renderer>(true);
                foreach (var r in m_lidRenderers) if (r != null && r.enabled != show) r.enabled = show;
            }
            if (m_lidCollider != null && !LidOpen && m_phase != Phase.Open) m_lidCollider.enabled = true;
            if (m_lidEdges == null) return;
            Color edge = m_lidEdgeColor;
            if (m_phase == Phase.Announce)
            {
                float progress = m_announceTime > 0.0f ? Mathf.Clamp01(m_phaseTimer / m_announceTime) : 1.0f;
                edge = Color.Lerp(m_lidEdgeColor, m_warningColor, GimmickPalette.Blink(m_time, Mathf.Lerp(3.0f, 9.0f, progress)));
            }
            foreach (var r in m_lidEdges)
            {
                if (r == null) continue;
                r.GetPropertyBlock(m_mpb);
                m_mpb.SetColor(s_baseColorId, edge);
                r.SetPropertyBlock(m_mpb);
            }
        }

        void UpdateFieldVisuals()
        {
            foreach (var t in m_field)
            {
                EnsureVisual(t);
                bool warning = t.State == 0;
                t.WarnFill.enabled = t.WarnRing.enabled = warning;
                t.Visual.gameObject.SetActive(!warning);
                Vector3 ground = new Vector3(t.Position.x, Center.y + 0.03f, t.Position.z);
                if (warning)
                {
                    t.Shadow.enabled = false;
                    float k = m_dropWarningTime > 0.0f ? 1.0f - Mathf.Clamp01(t.Timer / m_dropWarningTime) : 1.0f;
                    t.WarnFill.transform.position = ground;
                    t.WarnRing.transform.position = ground + Vector3.up * 0.005f;
                    t.WarnFill.transform.localScale = new Vector3(0.8f * k, 1.0f, 0.8f * k);
                    t.WarnRing.transform.localScale = new Vector3(0.9f, 1.0f, 0.9f);
                    EffectMeshes.SetColor(t.WarnFill, new Color(1.0f, 1.0f, 1.0f, 0.3f));
                    EffectMeshes.SetColor(t.WarnRing, new Color(1.0f, 1.0f, 1.0f, Mathf.Lerp(0.4f, 0.9f, GimmickPalette.Blink(m_time, 6.0f))));
                    continue;
                }
                // 寝かせて置く(文字が上)。空中では回る
                t.Visual.position = t.Position;
                t.Visual.rotation = t.State == 1
                    ? Quaternion.Euler(Mathf.Sin(m_time * 9.0f + t.Kind) * 25.0f, t.Yaw, Mathf.Cos(m_time * 7.0f + t.Kind) * 20.0f)
                    : Quaternion.Euler(0.0f, t.Yaw, 0.0f);
                // 空中の牌の下に丸い影(高いほど薄く大きく)。穴の上では出さない
                float height = t.Position.y - TileRestY;
                bool showShadow = t.State == 1 && height > 0.05f && !(LidOpen && InLid(t.Position - Center));
                t.Shadow.enabled = showShadow;
                if (showShadow)
                {
                    float k = Mathf.Clamp01(height / m_fallHeight);
                    float size = Mathf.Lerp(0.45f, 0.8f, k);
                    t.Shadow.transform.position = ground;
                    t.Shadow.transform.localScale = new Vector3(size, 1.0f, size);
                    EffectMeshes.SetColor(t.Shadow, new Color(0.0f, 0.0f, 0.0f, m_blobShadowAlpha * (1.0f - k * 0.6f)));
                }
                bool visible = true;
                float left = m_tileLifetime - t.Age;
                if (t.State == 2 && left < m_tileBlinkTime) visible = GimmickPalette.Blink(m_time, Mathf.Lerp(8.0f, 3.0f, left / Mathf.Max(0.01f, m_tileBlinkTime))) > 0.35f;
                foreach (var r in t.Renderers) if (r != null) r.enabled = visible;
            }
        }

        void EnsureVisual(FieldTile t)
        {
            if (t.Visual != null) return;
            t.Visual = MahjongTileArt.Create($"MahjongTile_{MahjongTileArt.KindNames[t.Kind]}", transform, t.Kind, 1.0f, m_tileMaterial, m_backMaterial);
            t.Renderers = t.Visual.GetComponentsInChildren<Renderer>(true);
            t.WarnFill = EffectMeshes.Create("DropWarnFill", transform, EffectMeshes.Disc, false);
            t.WarnRing = EffectMeshes.Create("DropWarnRing", transform, EffectMeshes.Ring, true);
            t.Shadow = EffectMeshes.Create("TileShadow", transform, EffectMeshes.Disc, false);
        }

        void DestroyVisual(FieldTile t)
        {
            if (t.Visual != null) Destroy(t.Visual.gameObject);
            if (t.WarnFill != null) Destroy(t.WarnFill.gameObject);
            if (t.WarnRing != null) Destroy(t.WarnRing.gameObject);
            if (t.Shadow != null) Destroy(t.Shadow.gameObject);
            t.Visual = null;
            t.WarnFill = t.WarnRing = t.Shadow = null;
        }

        // ポンの演出の見た目(暗転・予告のゲージ・落下 → 着地 → ツモで床に沈む)
        // 状態(段階・経過時間・予告の位置)だけから決まるので、ネット対戦のクライアントでも同じに見える
        void UpdatePonVisuals()
        {
            bool aiming = m_phase == Phase.Aim;
            bool dropping = m_phase == Phase.GiantDrop;
            float s = m_giantScaleNow;

            UpdateDark();

            // 予告(円形のゲージ)。ねらっている間
            m_gauge.enabled = aiming || (m_phase == Phase.Fly && m_phaseTimer > m_flyTime * 0.5f);
            foreach (var text in m_gaugeTexts) if (text != null) text.enabled = m_gauge.enabled;
            if (m_gauge.enabled) UpdateGauge(aiming ? Mathf.Clamp01(m_phaseTimer / m_aimTime) : 0.0f);

            // 落ちる場所の影と黄色の輪(ねらっている間と落ちてくる間)
            bool warn = m_gauge.enabled || dropping;
            m_dropShadow.enabled = m_dropRing.enabled = warn;
            if (warn)
            {
                Vector3 c = (dropping ? PonCenter : AimCenter) + Vector3.up * 0.03f;
                float k = dropping ? 0.5f + 0.5f * Mathf.Clamp01(m_phaseTimer / m_giantDropTime) : 0.35f;
                Vector2 half = PonHalfSize;
                m_dropShadow.transform.position = c;
                m_dropShadow.transform.localScale = new Vector3(half.x * 1.1f * k, 1.0f, half.y * 1.1f * k);
                EffectMeshes.SetColor(m_dropShadow, new Color(0.0f, 0.0f, 0.0f, 0.5f * k));
                m_dropRing.transform.position = c + Vector3.up * 0.005f;
                m_dropRing.transform.localScale = new Vector3(half.x * 1.25f + 0.4f, 1.0f, half.y * 1.25f + 0.4f);
                EffectMeshes.SetColor(m_dropRing, GimmickPalette.WithAlpha(m_warningColor, Mathf.Lerp(0.4f, 1.0f, GimmickPalette.Blink(m_time, dropping ? 10.0f : 3.0f))));
            }

            foreach (var g in m_giants)
            {
                if (g.Visual == null) continue;
                Vector3 rest = PonCenter + new Vector3(g.Rest.x, g.Rest.y, g.Rest.z);
                Quaternion restRot = Quaternion.Euler(0, g.RestYaw, 0);
                g.Visual.localScale = Vector3.one * s;
                switch (m_phase)
                {
                    case Phase.Fly:
                    case Phase.Aim:
                        g.Visual.gameObject.SetActive(false);
                        break;
                    case Phase.GiantDrop:
                    {
                        // 予告の位置の真上から、文字を上にして落ちる
                        float t = Mathf.Clamp01(m_phaseTimer / m_giantDropTime);
                        g.Visual.position = Vector3.LerpUnclamped(rest + Vector3.up * m_dropHeight, rest, t * t);
                        g.Visual.rotation = restRot;
                        g.Visual.gameObject.SetActive(true);
                        break;
                    }
                    case Phase.Settle:
                    case Phase.Announce:
                    case Phase.Open:
                    case Phase.Flow:
                    case Phase.Close:
                        // 着地したあとは物理で動く(押される・ツモで中央の穴へ吸い込まれる)
                        if (GimmickClock.IsNetClient)
                        {
                            // クライアント: ホストから届いた位置と向き
                            if (g.Body != null) g.Body.isKinematic = true;
                            if (g.HasNet) g.Visual.SetPositionAndRotation(g.NetPos, g.NetRot);
                            else g.Visual.SetPositionAndRotation(rest, restRot);
                        }
                        else if (g.Body == null || g.Body.isKinematic)
                        {
                            if (!g.Gone) g.Visual.SetPositionAndRotation(rest, restRot);
                        }
                        g.Visual.gameObject.SetActive(!g.Gone);
                        break;
                    default:
                        g.Visual.gameObject.SetActive(false);
                        break;
                }
            }
        }

        void HidePonVisuals()
        {
            HideAimMessage();
            if (m_dark != null) m_dark.enabled = false;
            if (m_gauge != null) m_gauge.enabled = false;
            foreach (var text in m_gaugeTexts) if (text != null) text.enabled = false;
            if (m_dropShadow != null) m_dropShadow.enabled = false;
            if (m_dropRing != null) m_dropRing.enabled = false;
            if (m_focus != null) m_focus.SetActive(false);
        }

        // 画面を少し暗くする(飛び立つ間に暗くなり、落ちてくる間に戻る)。カメラの前に板を置く
        void UpdateDark()
        {
            float a = 0.0f;
            if (m_phase == Phase.Fly) a = Mathf.Clamp01(m_phaseTimer / (m_flyTime * 0.6f));
            else if (m_phase == Phase.Aim) a = 1.0f;
            else if (m_phase == Phase.GiantDrop) a = 1.0f - Mathf.Clamp01(m_phaseTimer / m_giantDropTime);
            var cam = Camera.main;
            m_dark.enabled = a > 0.001f && cam != null;
            if (!m_dark.enabled) return;
            float d = m_darkDistance;
            float h = 2.0f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.3f;
            m_dark.transform.SetPositionAndRotation(cam.transform.position + cam.transform.forward * d, cam.transform.rotation);
            m_dark.transform.localScale = new Vector3(h * cam.aspect, h, 1.0f);
            EffectMeshes.SetColor(m_dark, new Color(0.0f, 0.0f, 0.0f, m_darkAlpha * a));
        }

        // 予告の牌(上から見た形)を細かいマスに分けたメッシュを作る。マスごとに、並びの中心から見た角度(真上が0、時計回りに1まで)を覚えておく
        void BuildGaugeMesh()
        {
            foreach (var text in m_gaugeTexts) if (text != null) Destroy(text.gameObject);
            m_gaugeTexts.Clear();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            m_gaugeAngles.Clear();
            m_gaugeColors.Clear();
            Vector3 size = MahjongTileArt.Size;
            float s = m_giantScaleNow;
            var font = PresentationSettings.DefaultFont;
            for (int i = 0; i < m_giants.Count; i++)
            {
                var g = m_giants[i];
                bool sideways = Mathf.Abs(g.RestYaw - 90.0f) < 1.0f;
                Vector2 footprint = sideways ? new Vector2(size.z * s, size.x * s) : new Vector2(size.x * s, size.z * s);
                footprint -= Vector2.one * 0.12f;   // 牌どうしの間を少し空けて、1枚ずつに見えるように
                int nx = Mathf.Max(1, Mathf.CeilToInt(footprint.x / m_gaugeCell));
                int nz = Mathf.Max(1, Mathf.CeilToInt(footprint.y / m_gaugeCell));
                float cw = footprint.x / nx, cd = footprint.y / nz;
                float x0 = g.Rest.x - footprint.x * 0.5f, z0 = g.Rest.z - footprint.y * 0.5f;
                for (int ix = 0; ix < nx; ix++)
                {
                    for (int iz = 0; iz < nz; iz++)
                    {
                        float xa = x0 + ix * cw, za = z0 + iz * cd;
                        float cx = xa + cw * 0.5f, cz = za + cd * 0.5f;
                        float angle = Mathf.Repeat(Mathf.Atan2(cx, cz) / (Mathf.PI * 2.0f), 1.0f);
                        int b = verts.Count;
                        verts.Add(new Vector3(xa, 0, za));
                        verts.Add(new Vector3(xa + cw, 0, za));
                        verts.Add(new Vector3(xa, 0, za + cd));
                        verts.Add(new Vector3(xa + cw, 0, za + cd));
                        tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                        tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                        for (int k = 0; k < 4; k++)
                        {
                            m_gaugeAngles.Add(angle);
                            m_gaugeColors.Add(m_gaugeColor);
                        }
                    }
                }
                // 牌の文字(上から読める向き。横に倒した牌は文字も倒す)
                if (font != null)
                {
                    var text = ToyKit.Text3D(font, font.material, MahjongTileArt.KindNames[m_ponKinds[i]], m_gauge.transform, new Vector2(size.x * s * 0.8f, size.z * s * 0.75f), 60.0f);
                    text.color = GimmickPalette.WithAlpha(MahjongTileArt.Ink, 0.8f);
                    text.fontStyle = TMPro.FontStyles.Bold;
                    text.transform.localPosition = new Vector3(g.Rest.x, 0.02f, g.Rest.z);
                    text.transform.localRotation = Quaternion.Euler(90.0f, g.RestYaw, 0.0f);
                    var r = text.GetComponent<MeshRenderer>();
                    if (r != null)
                    {
                        r.sortingOrder = 22;
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                    text.enabled = false;
                    m_gaugeTexts.Add(text);
                }
            }
            m_gaugeMesh.Clear();
            m_gaugeMesh.SetVertices(verts);
            m_gaugeMesh.SetColors(m_gaugeColors);
            m_gaugeMesh.SetTriangles(tris, 0);
            m_gaugeMesh.RecalculateBounds();
        }

        // 予告の牌を、円形のゲージとして塗る(progress まで時計回りに不透明、残りは薄く)
        void UpdateGauge(float progress)
        {
            m_gauge.transform.position = AimCenter + Vector3.up * 0.05f;
            m_gauge.transform.rotation = Quaternion.identity;
            float blink = 0.85f + 0.15f * GimmickPalette.Blink(m_time, 3.0f);
            for (int i = 0; i < m_gaugeAngles.Count; i++)
            {
                float a = m_gaugeAngles[i] <= progress ? m_gaugeFilledAlpha : m_gaugeEmptyAlpha * blink;
                m_gaugeColors[i] = GimmickPalette.WithAlpha(m_gaugeColor, a);
            }
            m_gaugeMesh.SetColors(m_gaugeColors);
            // 色は頂点の色で付けるので、マテリアルの色は白
            EffectMeshes.SetColor(m_gauge, Color.white);
        }

        //========================================
        // HUD(手持ちの牌)
        //========================================

        public void UpdatePanelExtra(MatchPlayer player, RectTransform panel)
        {
            if (panel == null || player == null) return;
            if (!m_huds.TryGetValue(panel, out var hud) || hud.Root == null)
            {
                hud = new Hud();
                hud.Root = new GameObject("MahjongHand", typeof(RectTransform)).GetComponent<RectTransform>();
                hud.Root.SetParent(panel, false);
                ToyKit.Anchor(hud.Root, new Vector2(0.5f, 1.0f), new Vector2(0.0f, m_iconOffsetY), new Vector2(260.0f, m_iconSize.y));
                hud.Back = ToyKit.UIImage("Back", hud.Root, new Color(0.08f, 0.06f, 0.05f, 0.72f));
                hud.Back.gameObject.AddComponent<Outline>().effectColor = player.Color;
                m_huds[panel] = hud;
            }
            List<int> hand = HandForHud(player);
            int count = hand != null ? hand.Count : 0;
            hud.Back.gameObject.SetActive(count > 0);
            if (count > 0)
            {
                float width = count * (m_iconSize.x + m_iconGap) + 10.0f;
                ToyKit.Anchor(hud.Back, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, m_iconSize.y + 8.0f));
            }
            while (hud.Icons.Count < count) hud.Icons.Add(MahjongTileArt.CreateIcon(hud.Root, m_iconSize));
            // 種類の順に並べる(そろい具合が分かるように)
            if (hand != null && hand.Count > 1)
            {
                s_sorted.Clear();
                s_sorted.AddRange(hand);
                s_sorted.Sort();
                hand = s_sorted;
            }
            for (int i = 0; i < hud.Icons.Count; i++)
            {
                var icon = hud.Icons[i];
                bool on = i < count;
                icon.gameObject.SetActive(on);
                if (!on) continue;
                MahjongTileArt.SetIcon(icon, hand[i]);
                float x = (i - (count - 1) * 0.5f) * (m_iconSize.x + m_iconGap);
                ToyKit.Anchor(icon, new Vector2(0.5f, 0.5f), new Vector2(x, 0.0f), m_iconSize);
            }
        }

        static readonly List<int> s_sorted = new List<int>();

        List<int> HandForHud(MatchPlayer player)
        {
            if (GimmickClock.IsNetClient)
            {
                var match = DarumaMatch.Current;
                if (match == null) return null;
                int index = -1;
                for (int i = 0; i < match.Players.Count; i++) if (match.Players[i] == player) index = i;
                if (index < 0 || index >= m_netHands.Count) return null;
                s_netHand.Clear();
                var counts = m_netHands[index];
                for (int k = 0; k < counts.Length; k++) for (int n = 0; n < counts[k]; n++) s_netHand.Add(k);
                return s_netHand;
            }
            return m_hands.TryGetValue(player, out var hand) ? hand : null;
        }

        static readonly List<int> s_netHand = new List<int>();

        //========================================
        // ネット対戦用(suehara): 場の牌・手持ち・ふた・ポンの演出の状態をホストから受け取る
        //========================================

        const int kMaxNetTiles = 12;
        const int kMaxNetPlayers = 8;
        // クライアント: カメラの対象から外している人
        DarumaCharacter m_netFlyer;

        public void WriteNetState(GimmickNetState s)
        {
            s.AddInt((int)m_phase);
            s.Add(m_phaseTimer);
            s.Add(m_lidOpen);
            // ポン
            s.AddInt(m_ponKinds.Count);
            foreach (int k in m_ponKinds) s.AddInt(k);
            // 予告の位置・落とした位置と、カメラのそばに寄せている人(プレイヤーの並び順。いなければ -1)
            s.Add(m_aim.x); s.Add(m_aim.y);
            s.Add(m_dropAt.x); s.Add(m_dropAt.y);
            int flyer = -1;
            var current = DarumaMatch.Current;
            if (m_flyer != null && current != null)
            {
                for (int i = 0; i < current.Players.Count; i++) if (current.Players[i] != null && current.Players[i].Character == m_flyer) flyer = i;
            }
            s.AddInt(flyer);
            // 巨大な牌の位置・向き(着地したあとは物理で動くので、そのまま送る)
            s.AddInt(m_giants.Count);
            foreach (var g in m_giants)
            {
                s.AddVec3(g.Visual != null ? g.Visual.position : Vector3.zero);
                s.AddRot(g.Visual != null ? g.Visual.rotation : Quaternion.identity);
                s.AddBool(g.Gone);
            }
            int n = Mathf.Min(m_field.Count, kMaxNetTiles);
            s.AddInt(n);
            for (int i = 0; i < n; i++)
            {
                var t = m_field[i];
                s.AddInt(t.Kind);
                s.AddInt(t.State);
                s.AddVec3(t.Position);
                s.Add(t.Yaw);
                s.Add(t.Timer);
                s.Add(t.Age);
            }
            var match = DarumaMatch.Current;
            int players = match != null ? Mathf.Min(match.Players.Count, kMaxNetPlayers) : 0;
            s.AddInt(players);
            for (int i = 0; i < players; i++)
            {
                var p = match.Players[i];
                var counts = new int[MahjongTileArt.KindCount];
                if (p != null && m_hands.TryGetValue(p, out var hand)) foreach (int k in hand) counts[k]++;
                foreach (int c in counts) s.AddInt(c);
            }
        }

        public void ReadNetState(GimmickNetState s)
        {
            var phase = (Phase)s.Int();
            float timer = s.Float();
            float lid = s.Float();
            int ponCount = Mathf.Clamp(s.Int(), 0, MahjongTileArt.KindCount);
            var kinds = new List<int>();
            for (int i = 0; i < ponCount; i++) kinds.Add(Mathf.Clamp(s.Int(), 0, MahjongTileArt.KindCount - 1));
            var aim = new Vector2(s.Float(), s.Float());
            var dropAt = new Vector2(s.Float(), s.Float());
            int flyer = s.Int();
            int giantCount = Mathf.Clamp(s.Int(), 0, 8);
            var giantPoses = new List<(Vector3 pos, Quaternion rot, bool gone)>();
            for (int i = 0; i < giantCount; i++) giantPoses.Add((s.Vec3(), s.Rot(), s.Bool()));
            int n = Mathf.Clamp(s.Int(), 0, kMaxNetTiles);
            if (!s.Ok) return;

            // ポンの牌が変わった(始まった・終わった)
            bool changed = kinds.Count != m_ponKinds.Count;
            for (int i = 0; !changed && i < kinds.Count; i++) changed = kinds[i] != m_ponKinds[i];
            m_ponKinds.Clear();
            m_ponKinds.AddRange(kinds);
            m_aim = aim;
            m_dropAt = dropAt;
            // カメラのそばに寄せている人は、このクライアントのカメラでも画面に収める対象から外す
            var current = DarumaMatch.Current;
            DarumaCharacter flyerBody = current != null && flyer >= 0 && flyer < current.Players.Count && current.Players[flyer] != null ? current.Players[flyer].Character : null;
            if (m_netFlyer != null && m_netFlyer != flyerBody) MatchCamera.HiddenBodies.Remove(m_netFlyer.transform);
            m_netFlyer = flyerBody;
            if (m_netFlyer != null) MatchCamera.HiddenBodies.Add(m_netFlyer.transform);
            if (changed)
            {
                if (m_ponKinds.Count > 0) BuildGiants();
                else ClearGiants();
            }
            if (phase == Phase.Refill || phase == Phase.Idle) ClearGiants();
            if (giantPoses.Count == m_giants.Count)
            {
                for (int i = 0; i < m_giants.Count; i++)
                {
                    m_giants[i].NetPos = giantPoses[i].pos;
                    m_giants[i].NetRot = giantPoses[i].rot;
                    m_giants[i].Gone = giantPoses[i].gone;
                    m_giants[i].HasNet = true;
                }
            }
            m_phase = phase;
            m_phaseTimer = timer;
            m_lidOpen = lid;

            while (m_field.Count > n)
            {
                DestroyVisual(m_field[m_field.Count - 1]);
                m_field.RemoveAt(m_field.Count - 1);
            }
            for (int i = 0; i < n; i++)
            {
                int kind = Mathf.Clamp(s.Int(), 0, MahjongTileArt.KindCount - 1);
                int state = s.Int();
                Vector3 pos = s.Vec3();
                float yaw = s.Float();
                float t = s.Float();
                float age = s.Float();
                if (!s.Ok) return;
                if (i >= m_field.Count) m_field.Add(new FieldTile());
                var f = m_field[i];
                if (f.Kind != kind && f.Visual != null) DestroyVisual(f);
                f.Kind = kind;
                f.State = state;
                f.Position = pos;
                f.Yaw = yaw;
                f.Timer = t;
                f.Age = age;
            }
            int players = Mathf.Clamp(s.Int(), 0, kMaxNetPlayers);
            while (m_netHands.Count < players) m_netHands.Add(new int[MahjongTileArt.KindCount]);
            for (int i = 0; i < players; i++)
            {
                for (int k = 0; k < MahjongTileArt.KindCount; k++) m_netHands[i][k] = Mathf.Max(0, s.Int());
            }
        }
    }
}
