using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Yurube.Story.Vfx;

namespace Yurube.Story
{
    // ストーリーの映像演出（ムービー）。
    //  ・ゲームのステージから遠く離れた場所に「部室」のセットを作り、専用カメラで撮って全画面に映す
    //  ・序章のオープニング（4カット＋タイトル）と、会話中の背景（ゆっくり寄る）に使う
    //  ・ゲームを止めている間も動くように、時間はすべて unscaled
    //  ・A / B でオープニングを飛ばせる
    // セットはプリミティブとランタイムで作ったテクスチャだけで組む（外部アセット不要）。
    // キャラクターの Update より後に走らせて、走るポーズが上書きされないようにする
    [DefaultExecutionOrder(1000)]
    public class StoryMovie : MonoBehaviour
    {
        // セットの場所（ゲームのステージから遠く離す）
        static readonly Vector3 SetOrigin = new Vector3(5000f, 0f, 0f);
        // 第2章の全国大会会場（鳥居の前）のセット。部室とは別の場所に作る
        static readonly Vector3 VenueOrigin = new Vector3(5200f, 0f, 0f);
        // プロローグの8年前の王冠大会 決勝会場のセット
        static readonly Vector3 ArenaOrigin = new Vector3(5400f, 0f, 0f);
        // 第4章（頂の決勝戦）の貴賓席・観客席・階段降下セット
        static readonly Vector3 Ch4FinalsOrigin = new Vector3(5600f, 0f, 0f);
        // 最終章（二つで足りる王）のエンディング演出セット（パリィ崩壊・バッド闇落ち）
        static readonly Vector3 FinalEndingOrigin = new Vector3(5800f, 0f, 0f);
        // 画面の並び（HUD 15 < ムービー 44 < 実績カード 45 < 会話 60）
        const int CanvasOrder = 44;

        StoryChapter m_chapter;

        Transform m_root;
        Camera m_camera;
        RenderTexture m_rt;
        GameObject m_screen;
        RectTransform m_canvas;
        RawImage m_view;
        CanvasGroup m_fadeGroup, m_titleGroup, m_captionGroup, m_hintGroup;
        RectTransform m_hintPlate;
        TextMeshProUGUI m_titleMain, m_titleSub, m_caption;

        [Header("会話カメラ・演出")]
        [Tooltip("S_DOLL_FAR（窓際の人形）へ寄るときのブレンド秒数")]
        [SerializeField, Min(0.05f)] float m_dollFarBlendSeconds = 0.8f;
        [Tooltip("S_EMPTY_ROOM で無人の部室を見せてから人形へ寄り始めるまでの秒数")]
        [SerializeField, Min(0f)] float m_emptyRoomHoldSeconds = 0.35f;
        [Tooltip("だるま人形がカタと小さく揺れる往復の秒数")]
        [SerializeField, Min(0.05f)] float m_dollShakeSeconds = 0.5f;
        [Tooltip("だるま人形が揺れる最大角度（度）")]
        [SerializeField, Min(0.5f)] float m_dollShakeAngle = 8f;
        [Tooltip("だるま人形が揺れるときの効果音（未設定なら鳴らさない）")]
        [SerializeField] AudioClip m_dollShakeSe;
        [Tooltip("会話シーン（部室）での各キャラクターの積み木段数（仮の見た目。初期値は3段）")]
        [SerializeField] StorySettings.MemberStackConfig[] m_roomMemberStacks = new[]
        {
            new StorySettings.MemberStackConfig { id = "kidaru", stackCount = 3 },
            new StorySettings.MemberStackConfig { id = "midaru", stackCount = 3 },
            new StorySettings.MemberStackConfig { id = "akadaru", stackCount = 3 },
            new StorySettings.MemberStackConfig { id = "aodaru", stackCount = 3 },
        };

        // ショットIDをキーに、対象の Transform 基準でカメラ位置・注視点・画角を登録する
        [System.Serializable]
        public class ShotDefinition
        {
            public string id;
            public Transform target;
            public Vector3 positionOffset;
            public Vector3 lookOffset;
            public float fov = 40f;
            public bool useTargetRotation; // true なら target の向き基準でオフセットを計算
            public bool blend;             // true ならブレンド移動（S_DOLL_FAR）
            public bool sway;              // true なら全景のゆったりした呼吸揺れ
            public int memberVisibility;   // 1: 4人を表示 / -1: 4人を非表示 / 0: 直前の状態を維持
        }

        [SerializeField] List<ShotDefinition> m_shotList = new List<ShotDefinition>();
        readonly Dictionary<string, ShotDefinition> m_shots = new Dictionary<string, ShotDefinition>();
        string m_currentShotId = "";
        float m_blendDuration;
        float m_blendFromFov = 42f;
        Coroutine m_emptyRoomRoutine;
        float m_dollShakeStart = -1f;

        Material m_wallMat, m_floorMat, m_woodMat, m_whiteMat, m_inkMat;
        Material m_skyDay, m_skySunset, m_skyNight, m_sunPatchMat;
        Transform m_skyQuad;
        Renderer m_sunPatch;
        Light m_sun, m_fill;
        Transform m_doll;
        Transform m_whiteboard;
        Transform m_wallPaper;
        Transform m_oldHammer;
        Transform m_messenger;
        Transform m_longShadow;
        bool m_darkMood;
        bool m_nightRoomMode;
        bool m_nightMood;
        Coroutine m_dialogueFadeRoutine;

        bool IsPrologueChapter => m_chapter != null && m_chapter.id == StoryLauncher.PrologueId;
        bool ShouldHideDoll => IsPrologueChapter || m_nightRoomMode || m_ch4FinalMode || m_finalMode
            || (m_chapter != null && (m_chapter.id == "chapter3" || m_chapter.id == "chapter4" || m_chapter.id == StoryLauncher.FinalId));

        class Member
        {
            public Transform root, head;
            public float phase;
            public Vector3 basePos;
        }
        readonly List<Member> m_members = new List<Member>();

        bool m_built, m_visible, m_skipped, m_sunset;
        bool m_opening, m_advance;
        float m_time;
        Coroutine m_slowMo;   // 回想カットのスロー演出（Aで送ったら止める）

        // 直前のムービーを B でスキップしたか（章クリア済みの再プレイで、会話もまとめて飛ばすため）
        public bool WasSkipped { get; private set; }

        // 第1章以降：実際のステージを撮るカットシーン
        bool m_stageMode;
        Transform m_runner;
        Vector3 m_runnerHome, m_runnerA, m_runnerB;
        Quaternion m_runnerHomeRot;
        float m_runnerStart, m_runnerDuration;
        bool m_runnerActive;
        bool m_swingerActive;
        Nakahira.DarumaHammer m_swingerHammer;
        Vector3 m_swingerHammerBasePos;
        Quaternion m_swingerHammerBaseRot;
        bool m_swingerHammerWasEnabled;

        // 第2章以降：全国大会会場（鳥居の前）のセットを撮るカットシーン
        bool m_venueMode;
        bool m_venueBuilt;
        Transform m_venueRoot;
        Transform m_venueSky;
        readonly Dictionary<string, Member> m_venueMembers = new Dictionary<string, Member>();

        // プロローグ（八年前の槌音）：8年前の王冠大会 決勝会場
        bool m_arenaMode;
        bool m_arenaBuilt;
        Transform m_arenaRoot;
        Transform m_arenaFatherShadow, m_arenaKingShadow, m_arenaBoyShadow;
        Member m_arenaKingMember;
        Transform m_arenaShotBlock;
        Transform[] m_arenaKingBlocks;
        Vector3[] m_arenaKingBlockBasePos;
        Quaternion[] m_arenaKingBlockBaseRot;
        Transform m_arenaFloorHammer;
        readonly List<Light> m_arenaLights = new List<Light>();
        readonly List<float> m_arenaLightBase = new List<float>();
        float m_arenaCollapseStart = -1f;
        float m_arenaHammerRollStart = -1f;
        Coroutine m_arenaFadeRoutine;
        Coroutine m_preludeBlackoutCleanupRoutine;
        bool m_preludeFadeDelegatedToVfx;

        // 最終章：控室（部室セット）と決勝の舞台を行き来する
        bool m_finalMode;
        // 第4章（頂の決勝戦）：決勝ステージ＋貴賓席／客席セット
        bool m_ch4FinalMode;
        bool m_ch4SetBuilt;
        Transform m_ch4SetRoot;
        Transform m_ch4VipDoorL, m_ch4VipDoorR;
        Transform m_ch4SkyVip, m_ch4SkyAudience, m_ch4SkyDescend;
        readonly List<Light> m_ch4Lights = new List<Light>();
        readonly List<Color> m_ch4LightDayColors = new List<Color>();
        readonly List<Member> m_ch4AudMembers = new List<Member>();
        float m_audStandStart = -1f;

        // 最終章（トゥルー／バッド）の専用演出セット・部室追加小道具
        bool m_finalEndBuilt;
        Transform m_finalEndRoot;
        Member m_parryAkadaru, m_parryDaruking;
        Transform m_parrySword, m_parryFlash, m_parryThrone, m_parryShield;
        readonly List<Transform> m_parryDebris = new List<Transform>();
        readonly List<Vector3> m_parryDebrisBasePos = new List<Vector3>();
        readonly List<Vector3> m_parryDebrisVel = new List<Vector3>();
        float m_swordThrowStart = -1f;
        float m_parryImpactStart = -1f;
        float m_kingExitStart = -1f;
        bool m_parryShatterPlayed;

        Transform m_trueRoomPropsRoot;
        Member m_roomGuestIwadaru, m_roomGuestHebidaru;

        Transform m_badCorruptRoot, m_badCollapseRoot, m_badShadowRoot;
        Renderer m_badHeadRenderer;
        Material m_badHeadMat;
        readonly List<Transform> m_badSwirlShards = new List<Transform>();
        readonly List<Transform> m_badTowerBlocks = new List<Transform>();
        readonly List<Vector3> m_badTowerBasePos = new List<Vector3>();
        float m_badCorruptStart = -1f;
        float m_badCollapseStart = -1f;
        string m_finalScene = "";                 // "stage" / "room" / "ending"
        readonly Dictionary<string, Member> m_roomMembers = new Dictionary<string, Member>();
        // 差し替え前の古い会話用だるま（削除せず非表示で残す）
        readonly List<Member> m_legacyRoomMembers = new List<Member>();
        bool m_bannerBuilt;
        Transform m_bannerRoot, m_bannerCloth;
        TextMeshPro m_bannerText;
        float m_bannerStart = -1f, m_bannerLength = 1f, m_bannerSeconds;

        // 台本（タイトルのムービーと同じ書き方）
        System.Func<float, Vector3> m_camPos, m_camLook;
        float m_shotStart, m_shotFov = 42f;
        float m_shift, m_baseScale = 1f;
        float m_camShake, m_fovPunch;

        // ポストプロセス（回想・シネマ調）
        Volume m_volume;
        VolumeProfile m_profile;
        ColorAdjustments m_ca;
        Vignette m_vig;
        Bloom m_bloom;
        bool m_memory, m_cinema;

        // カットシーン用VFX再生プレイヤー（vfx:<key> キュー連携・オンデマンド生成）
        StoryEffectPlayer m_vfxPlayer;
        public StoryEffectPlayer VfxPlayer => EnsureVfxPlayer();

        //========================================
        // 作る
        //========================================

        public static StoryMovie Create(StoryChapter chapter)
        {
            var go = new GameObject("StoryMovie");
            var movie = go.AddComponent<StoryMovie>();
            movie.m_chapter = chapter;
            movie.Build();
            return movie;
        }

        void Build()
        {
            if (m_built) return;
            m_built = true;

            var settings = StorySettings.Instance;
            if (settings != null)
            {
                m_dollFarBlendSeconds = settings.DollFarBlendSeconds;
                m_dollShakeSeconds = settings.DollShakeSeconds;
            }

            m_root = new GameObject("StoryMovieSet").transform;
            m_root.position = SetOrigin;

            BuildMaterials();
            BuildRoom();
            BuildProps();
            BuildMembers();
            RegisterRoomShots();
            BuildLights();
            BuildDust();
            BuildCamera();
            BuildPost();
            BuildScreen();
            StoryData.EventPlayed -= OnStoryEventPlayed;
            StoryData.EventPlayed += OnStoryEventPlayed;
            Hide();
        }

        void OnDestroy()
        {
            StoryData.EventPlayed -= OnStoryEventPlayed;
            RestoreRunner();
            if (m_camera != null) m_camera.targetTexture = null;
            RenderTexture.active = null;
            if (m_rt != null)
            {
                m_rt.Release();
                Destroy(m_rt);
            }
            if (m_root != null) Destroy(m_root.gameObject);
            if (m_venueRoot != null) Destroy(m_venueRoot.gameObject);
            if (m_arenaRoot != null) Destroy(m_arenaRoot.gameObject);
            if (m_ch4SetRoot != null) Destroy(m_ch4SetRoot.gameObject);
            if (m_finalEndRoot != null) Destroy(m_finalEndRoot.gameObject);
            if (m_badHeadMat != null) Destroy(m_badHeadMat);
            if (m_camera != null) Destroy(m_camera.gameObject);
            if (m_screen != null) Destroy(m_screen);
            if (m_vfxPlayer != null) Destroy(m_vfxPlayer.gameObject);
            if (m_volume != null) Destroy(m_volume.gameObject);
            if (m_profile != null) Destroy(m_profile);
        }

        //========================================
        // テクスチャ・マテリアル
        //========================================

        static Texture2D NewTex(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }

        static Texture2D GradientTexture(Color bottom, Color top, int h = 128)
        {
            var t = NewTex(4, h);
            var px = new Color[4 * h];
            for (int y = 0; y < h; y++)
            {
                var c = Color.Lerp(bottom, top, y / (float)(h - 1));
                for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D FloorTexture()
        {
            const int s = 256;
            var t = NewTex(s, s);
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.07f, y * 0.11f) * 0.05f;
                    var c = new Color(0.55f + n, 0.42f + n * 0.8f, 0.30f + n * 0.6f);
                    // 板の継ぎ目
                    if (y % 42 < 2) c *= 0.72f;
                    if ((x + (y / 42) * 61) % 170 < 2) c *= 0.82f;
                    px[y * s + x] = c;
                }
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D SkyTexture(bool sunset)
        {
            const int s = 256;
            var t = NewTex(s, s);
            var px = new Color[s * s];
            Color top = sunset ? new Color(0.28f, 0.18f, 0.42f) : new Color(0.30f, 0.55f, 0.92f);
            Color bottom = sunset ? new Color(1.0f, 0.58f, 0.28f) : new Color(0.82f, 0.92f, 1.0f);
            Color glow = sunset ? new Color(1.0f, 0.75f, 0.4f) : new Color(1.0f, 0.95f, 0.75f);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float v = y / (float)(s - 1);
                    var c = Color.Lerp(bottom, top, Mathf.Pow(v, 0.85f));
                    // 太陽
                    float dx = (x - s * 0.72f) / (s * 0.12f);
                    float dy = (y - s * 0.38f) / (s * 0.12f);
                    float sun = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    c = Color.Lerp(c, glow, sun * 0.85f);
                    // 遠くの建物・木のシルエット（下の方）
                    float horizon = 0.16f + 0.05f * Mathf.PerlinNoise(x * 0.09f, 0.3f);
                    if (v < horizon)
                    {
                        float h = 0.3f + 0.5f * Mathf.PerlinNoise(x * 0.11f, 7.7f);
                        if (v < horizon * h) c = Color.Lerp(c, new Color(0.10f, 0.11f, 0.18f), 0.92f);
                    }
                    px[y * s + x] = c;
                }
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D SkyTextureNight()
        {
            const int s = 256;
            var t = NewTex(s, s);
            var px = new Color[s * s];
            Color top = new Color(0.03f, 0.05f, 0.13f);
            Color bottom = new Color(0.09f, 0.13f, 0.24f);
            Color moonGlow = new Color(0.82f, 0.88f, 1.0f);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float v = y / (float)(s - 1);
                    var c = Color.Lerp(bottom, top, Mathf.Pow(v, 0.75f));
                    float dx = (x - s * 0.72f) / (s * 0.08f);
                    float dy = (y - s * 0.68f) / (s * 0.08f);
                    float moon = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    c = Color.Lerp(c, moonGlow, moon * 0.75f);
                    float horizon = 0.16f + 0.05f * Mathf.PerlinNoise(x * 0.09f, 0.3f);
                    if (v < horizon)
                    {
                        float h = 0.3f + 0.5f * Mathf.PerlinNoise(x * 0.11f, 7.7f);
                        if (v < horizon * h) c = Color.Lerp(c, new Color(0.02f, 0.03f, 0.06f), 0.94f);
                    }
                    px[y * s + x] = c;
                }
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D SoftDotTexture()
        {
            const int s = 64;
            var t = NewTex(s, s);
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    px[y * s + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D VignetteTexture()
        {
            const int s = 256;
            var t = NewTex(s, s);
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.05f, d)) * 0.72f;
                    px[y * s + x] = new Color(0f, 0f, 0f, a);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Sprite SpriteOf(Texture2D tex)
        {
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        static Material Unlit(Color c, Texture tex = null)
        {
            return TitleDressingKit.Unlit(c, tex);
        }

        void BuildMaterials()
        {
            m_wallMat = Unlit(Color.white, GradientTexture(new Color(0.78f, 0.70f, 0.58f), new Color(0.97f, 0.93f, 0.85f)));
            m_floorMat = Unlit(Color.white, FloorTexture());
            m_woodMat = ToyKit.Lit(new Color(0.52f, 0.36f, 0.22f), 0.25f);
            m_whiteMat = Unlit(new Color(0.97f, 0.97f, 0.95f));
            m_inkMat = Unlit(new Color(0.12f, 0.11f, 0.13f));

            m_skyDay = Unlit(Color.white, SkyTexture(false));
            m_skySunset = Unlit(Color.white, SkyTexture(true));
            m_skyNight = Unlit(Color.white, SkyTextureNight());
            m_sunPatchMat = TitleDressingKit.UnlitTransparent(SoftDotTexture(), new Color(1f, 0.86f, 0.6f, 0.34f));
        }

        //========================================
        // 部屋
        //========================================

        Vector3 P(float x, float y, float z) => SetOrigin + new Vector3(x, y, z);
        // 会場セット（鳥居の前）のワールド座標
        Vector3 V(float x, float y, float z) => VenueOrigin + new Vector3(x, y, z);
        // ステージを撮るときは、部室のセットではなくワールド座標をそのまま使う
        Vector3 W(float x, float y, float z) => new Vector3(x, y, z);

        Transform Box(string name, Vector3 pos, Vector3 size, Material mat, float yaw = 0f)
        {
            var t = ToyKit.Primitive(PrimitiveType.Cube, name, m_root);
            t.localPosition = pos;
            t.localScale = size;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            t.GetComponent<Renderer>().sharedMaterial = mat;
            return t;
        }

        Transform Quad(string name, Vector3 pos, Vector2 size, Material mat, float yaw = 0f, float pitch = 0f)
        {
            var t = TitleDressingKit.Quad(m_root, name, P(pos.x, pos.y, pos.z), new Vector3(size.x, size.y, 1f), mat);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            return t;
        }

        void BuildRoom()
        {
            // 床・天井・壁（内側を向いた板）
            Quad("Floor", new Vector3(0f, 0f, 0f), new Vector2(12f, 10f), m_floorMat, 0f, 90f);
            Quad("Ceiling", new Vector3(0f, 5f, 0f), new Vector2(12f, 10f), m_wallMat, 0f, -90f);
            Quad("WallBack", new Vector3(0f, 2.5f, 4.9f), new Vector2(12f, 5f), m_wallMat);
            Quad("WallLeft", new Vector3(-5.9f, 2.5f, 0f), new Vector2(10f, 5f), m_wallMat, -90f);
            Quad("WallRight", new Vector3(5.9f, 2.5f, 0f), new Vector2(10f, 5f), m_wallMat, 90f);

            // 巾木
            Box("BaseBack", new Vector3(0f, 0.12f, 4.86f), new Vector3(12f, 0.24f, 0.06f), m_woodMat);
            Box("BaseLeft", new Vector3(-5.86f, 0.12f, 0f), new Vector3(0.06f, 0.24f, 10f), m_woodMat);

            // 窓（夕方の空）
            m_skyQuad = Quad("Window", new Vector3(1.9f, 2.9f, 4.86f), new Vector2(4.4f, 2.2f), m_skyDay);
            Box("WinTop", new Vector3(1.9f, 4.05f, 4.84f), new Vector3(4.7f, 0.18f, 0.08f), m_woodMat);
            Box("WinBottom", new Vector3(1.9f, 1.75f, 4.84f), new Vector3(4.7f, 0.18f, 0.08f), m_woodMat);
            Box("WinLeft", new Vector3(-0.35f, 2.9f, 4.84f), new Vector3(0.18f, 2.4f, 0.08f), m_woodMat);
            Box("WinRight", new Vector3(4.15f, 2.9f, 4.84f), new Vector3(0.18f, 2.4f, 0.08f), m_woodMat);
            Box("WinBar", new Vector3(1.9f, 2.9f, 4.83f), new Vector3(4.4f, 0.07f, 0.05f), m_woodMat);
            Box("WinSill", new Vector3(1.9f, 1.66f, 4.68f), new Vector3(4.9f, 0.12f, 0.34f), m_woodMat);
            // カーテン
            Box("CurtainL", new Vector3(-1.05f, 2.85f, 4.72f), new Vector3(0.75f, 2.5f, 0.12f), m_woodMat);
            Box("CurtainR", new Vector3(4.85f, 2.85f, 4.72f), new Vector3(0.75f, 2.5f, 0.12f), m_woodMat);

            // 床に落ちる窓の光
            var patch = Quad("SunPatch", new Vector3(1.7f, 0.02f, 2.6f), new Vector2(3.4f, 2.4f), m_sunPatchMat, 0f, 90f);
            m_sunPatch = patch.GetComponent<Renderer>();
            m_sunPatch.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void BuildProps()
        {
            // 白板（左の壁）。練習メニューを書いておく
            Box("BoardFrame", new Vector3(-5.87f, 2.55f, 0.3f), new Vector3(0.05f, 2.0f, 3.8f), m_inkMat);
            m_whiteboard = Box("Board", new Vector3(-5.83f, 2.55f, 0.3f), new Vector3(0.06f, 1.9f, 3.6f), m_whiteMat);
            // TMPの3D文字はfontSize 1 ≈ 0.1m なので、白板に読める大きさにするには3前後必要
            var boardText = TitleDressingKit.Text(m_root, PresentationSettings.DefaultFont,
                "練習メニュー\n素振り1000回\n積み上げ100段\n走り込み！",
                new Vector3(-5.79f, 2.55f, 0.3f), new Vector2(3.35f, 1.62f), 3.0f,
                new Color(0.1f, 0.11f, 0.18f), new Color(0.1f, 0.11f, 0.18f), 0.01f, 0f);
            boardText.transform.localPosition = new Vector3(-5.79f, 2.55f, 0.3f);
            boardText.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            boardText.textWrappingMode = TextWrappingModes.Normal;
            boardText.alignment = TextAlignmentOptions.TopLeft;
            boardText.lineSpacing = 8f;

            // 部旗（奥の壁）
            Box("Banner", new Vector3(-1.5f, 3.55f, 4.84f), new Vector3(1.1f, 2.2f, 0.04f), Unlit(new Color(0.78f, 0.14f, 0.12f)));
            Box("BannerBand", new Vector3(-1.5f, 3.55f, 4.80f), new Vector3(0.3f, 1.7f, 0.04f), m_whiteMat);

            // ポスター
            Box("PosterA", new Vector3(-3.7f, 3.2f, 4.85f), new Vector3(1.3f, 1.8f, 0.04f), Unlit(new Color(0.95f, 0.75f, 0.2f)));
            Box("PosterB", new Vector3(3.9f, 2.4f, 4.85f), new Vector3(1.1f, 1.5f, 0.04f), Unlit(new Color(0.25f, 0.5f, 0.8f)));

            // 時計
            var clock = ToyKit.Primitive(PrimitiveType.Cylinder, "Clock", m_root);
            clock.localPosition = new Vector3(3.9f, 4.15f, 4.82f);
            clock.localRotation = Quaternion.Euler(90f, 0f, 0f);
            clock.localScale = new Vector3(0.8f, 0.04f, 0.8f);
            clock.GetComponent<Renderer>().sharedMaterial = m_inkMat;
            var face = ToyKit.Primitive(PrimitiveType.Cylinder, "ClockFace", m_root);
            face.localPosition = new Vector3(3.9f, 4.15f, 4.78f);
            face.localRotation = Quaternion.Euler(90f, 0f, 0f);
            face.localScale = new Vector3(0.64f, 0.03f, 0.64f);
            face.GetComponent<Renderer>().sharedMaterial = m_whiteMat;

            // ロッカー（右の壁）
            Box("Locker", new Vector3(5.6f, 1.5f, 1.2f), new Vector3(0.5f, 2.8f, 1.6f), Unlit(new Color(0.55f, 0.58f, 0.6f)));
            Box("LockerLine", new Vector3(5.33f, 1.5f, 1.2f), new Vector3(0.03f, 2.6f, 0.04f), m_inkMat);

            // 長机
            Box("DeskTop", new Vector3(0f, 0.78f, 1.4f), new Vector3(4.6f, 0.1f, 1.4f), m_woodMat);
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    Box("DeskLeg", new Vector3(-2.05f + i * 4.1f, 0.38f, 1.0f + j * 0.9f), new Vector3(0.12f, 0.76f, 0.12f), m_woodMat);
            // 机の上のもの
            Box("Notebook", new Vector3(-1.2f, 0.85f, 1.3f), new Vector3(0.85f, 0.03f, 0.55f), m_whiteMat, 8f);
            var cup = ToyKit.Primitive(PrimitiveType.Cylinder, "Cup", m_root);
            cup.localPosition = new Vector3(1.5f, 0.92f, 1.15f);
            cup.localScale = new Vector3(0.18f, 0.18f, 0.18f);
            cup.GetComponent<Renderer>().sharedMaterial = m_whiteMat;

            // 窓際の棚と、片目のだるま
            Box("Shelf", new Vector3(2.2f, 1.35f, 4.55f), new Vector3(3.0f, 0.08f, 0.5f), m_woodMat);
            Box("ShelfBracketL", new Vector3(0.9f, 1.1f, 4.6f), new Vector3(0.08f, 0.5f, 0.4f), m_woodMat);
            Box("ShelfBracketR", new Vector3(3.5f, 1.1f, 4.6f), new Vector3(0.08f, 0.5f, 0.4f), m_woodMat);

            m_doll = new GameObject("Doll").transform;
            m_doll.SetParent(m_root, false);
            m_doll.localPosition = new Vector3(2.65f, 1.66f, 4.5f);
            var body = ToyKit.Primitive(PrimitiveType.Sphere, "Body", m_doll);
            body.localScale = new Vector3(0.6f, 0.55f, 0.55f);
            body.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.82f, 0.13f, 0.11f), 0.6f);
            var dollFace = ToyKit.Primitive(PrimitiveType.Sphere, "Face", m_doll);
            dollFace.localPosition = new Vector3(0f, 0.02f, -0.22f);
            dollFace.localScale = new Vector3(0.4f, 0.34f, 0.1f);
            dollFace.GetComponent<Renderer>().sharedMaterial = m_whiteMat;
            var eye = ToyKit.Primitive(PrimitiveType.Sphere, "Eye", m_doll);
            eye.localPosition = new Vector3(-0.1f, 0.06f, -0.29f);
            eye.localScale = new Vector3(0.07f, 0.09f, 0.04f);
            eye.GetComponent<Renderer>().sharedMaterial = m_inkMat;
            var dollBase = ToyKit.Primitive(PrimitiveType.Cylinder, "Base", m_doll);
            dollBase.localPosition = new Vector3(0f, -0.27f, 0f);
            dollBase.localScale = new Vector3(0.45f, 0.04f, 0.45f);
            dollBase.GetComponent<Renderer>().sharedMaterial = m_woodMat;

            // トロフィー
            var trophy = ToyKit.Primitive(PrimitiveType.Cylinder, "Trophy", m_root);
            trophy.localPosition = new Vector3(0.8f, 1.62f, 4.55f);
            trophy.localScale = new Vector3(0.16f, 0.12f, 0.16f);
            trophy.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.95f, 0.75f, 0.25f), 0.9f);
            var cupTop = ToyKit.Primitive(PrimitiveType.Sphere, "TrophyCup", m_root);
            cupTop.localPosition = new Vector3(0.8f, 1.82f, 4.55f);
            cupTop.localScale = new Vector3(0.28f, 0.24f, 0.28f);
            cupTop.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.95f, 0.75f, 0.25f), 0.9f);

            // 観葉植物
            var pot = ToyKit.Primitive(PrimitiveType.Cylinder, "Pot", m_root);
            pot.localPosition = new Vector3(-4.9f, 0.28f, 3.9f);
            pot.localScale = new Vector3(0.34f, 0.28f, 0.34f);
            pot.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.55f, 0.33f, 0.2f), 0.3f);
            var leafMat = ToyKit.Lit(new Color(0.25f, 0.55f, 0.3f), 0.4f);
            for (int i = 0; i < 4; i++)
            {
                var leaf = ToyKit.Primitive(PrimitiveType.Sphere, "Leaf", m_root);
                float a = i * Mathf.PI * 0.5f;
                leaf.localPosition = new Vector3(-4.9f + Mathf.Cos(a) * 0.22f, 0.8f + (i % 2) * 0.2f, 3.9f + Mathf.Sin(a) * 0.22f);
                leaf.localScale = Vector3.one * 0.42f;
                leaf.GetComponent<Renderer>().sharedMaterial = leafMat;
            }

            BuildPrologueProps();
        }

        void BuildPrologueProps()
        {
            // 壁の「全国制覇！」の殴り書きの紙（奥の壁、少し傾けて手書き感を出す）
            var paperRoot = new GameObject("WallPaper").transform;
            paperRoot.SetParent(m_root, false);
            paperRoot.localPosition = new Vector3(-3.3f, 2.7f, 4.82f);
            paperRoot.localRotation = Quaternion.Euler(0f, 0f, -2.5f);
            var paperSheet = ToyKit.Primitive(PrimitiveType.Cube, "Sheet", paperRoot);
            paperSheet.localScale = new Vector3(1.55f, 2.0f, 0.03f);
            paperSheet.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(0.97f, 0.94f, 0.85f));
            var tapeMat = Unlit(new Color(0.82f, 0.72f, 0.48f));
            var tapeL = ToyKit.Primitive(PrimitiveType.Cube, "TapeL", paperRoot);
            tapeL.localPosition = new Vector3(-0.62f, 0.92f, -0.02f);
            tapeL.localScale = new Vector3(0.28f, 0.12f, 0.02f);
            tapeL.localRotation = Quaternion.Euler(0f, 0f, 22f);
            tapeL.GetComponent<Renderer>().sharedMaterial = tapeMat;
            var tapeR = ToyKit.Primitive(PrimitiveType.Cube, "TapeR", paperRoot);
            tapeR.localPosition = new Vector3(0.62f, 0.92f, -0.02f);
            tapeR.localScale = new Vector3(0.28f, 0.12f, 0.02f);
            tapeR.localRotation = Quaternion.Euler(0f, 0f, -18f);
            tapeR.GetComponent<Renderer>().sharedMaterial = tapeMat;
            var paperText = TitleDressingKit.Text(paperRoot, PresentationSettings.DefaultFont,
                "全国\n制覇！", Vector3.zero, new Vector2(1.3f, 1.7f), 5.0f,
                new Color(0.11f, 0.09f, 0.08f), new Color(0.11f, 0.09f, 0.08f), 0.04f, 0f);
            paperText.transform.localPosition = new Vector3(0f, -0.04f, -0.03f);
            paperText.transform.localRotation = Quaternion.identity;
            paperText.fontStyle = FontStyles.Bold;
            paperText.alignment = TextAlignmentOptions.Center;
            paperText.lineSpacing = -8f;
            m_wallPaper = paperRoot;

            // 机の上の年季の入った古い槌（長机の上 y=0.83 付近）
            var hammerRoot = new GameObject("OldHammer").transform;
            hammerRoot.SetParent(m_root, false);
            hammerRoot.localPosition = new Vector3(0.15f, 0.90f, 1.35f);
            hammerRoot.localRotation = Quaternion.Euler(0f, -24f, 0f);
            var oldHandleMat = ToyKit.Lit(new Color(0.38f, 0.24f, 0.14f), 0.18f);
            var oldHeadMat = ToyKit.Lit(new Color(0.24f, 0.18f, 0.14f), 0.35f);
            var oldBandMat = ToyKit.Lit(new Color(0.14f, 0.12f, 0.11f), 0.45f);
            var handle = ToyKit.Primitive(PrimitiveType.Cylinder, "Handle", hammerRoot);
            handle.localPosition = new Vector3(-0.22f, -0.02f, 0f);
            handle.localRotation = Quaternion.Euler(0f, 0f, 90f);
            handle.localScale = new Vector3(0.085f, 0.42f, 0.085f);
            handle.GetComponent<Renderer>().sharedMaterial = oldHandleMat;
            var head = ToyKit.Primitive(PrimitiveType.Cylinder, "Head", hammerRoot);
            head.localPosition = new Vector3(0.24f, 0.04f, 0f);
            head.localRotation = Quaternion.Euler(90f, 0f, 0f);
            head.localScale = new Vector3(0.24f, 0.24f, 0.24f);
            head.GetComponent<Renderer>().sharedMaterial = oldHeadMat;
            var band = ToyKit.Primitive(PrimitiveType.Cylinder, "Band", hammerRoot);
            band.localPosition = new Vector3(0.24f, 0.04f, 0f);
            band.localRotation = Quaternion.Euler(90f, 0f, 0f);
            band.localScale = new Vector3(0.255f, 0.04f, 0.255f);
            band.GetComponent<Renderer>().sharedMaterial = oldBandMat;
            m_oldHammer = hammerRoot;

            // 黒い封筒を持った使者（#111827 系の暗色。S_MESSENGER のときだけ登場）
            var darkCoatMat = ToyKit.Lit(new Color(0.067f, 0.094f, 0.153f), 0.22f);
            var envelopeMat = ToyKit.Lit(new Color(0.08f, 0.10f, 0.16f), 0.35f);
            var sealMat = ToyKit.Lit(new Color(0.75f, 0.18f, 0.18f), 0.4f);
            var messenger = new GameObject("Messenger").transform;
            messenger.SetParent(m_root, false);
            messenger.localPosition = new Vector3(3.4f, 0f, -1.75f);
            messenger.localRotation = Quaternion.Euler(0f, -35f, 0f);
            for (int i = 0; i < 2; i++)
            {
                var mb = ToyKit.Primitive(PrimitiveType.Cylinder, "Body" + i, messenger);
                mb.localScale = new Vector3(0.72f, 0.24f, 0.72f);
                mb.localPosition = new Vector3(0f, 0.25f + i * 0.50f, 0f);
                mb.GetComponent<Renderer>().sharedMaterial = darkCoatMat;
            }
            var mHead = ToyKit.Primitive(PrimitiveType.Sphere, "Head", messenger);
            mHead.localPosition = new Vector3(0f, 1.42f, 0f);
            mHead.localScale = new Vector3(0.84f, 0.80f, 0.84f);
            mHead.GetComponent<Renderer>().sharedMaterial = darkCoatMat;
            var env = ToyKit.Primitive(PrimitiveType.Cube, "BlackEnvelope", messenger);
            env.localPosition = new Vector3(0f, 0.92f, 0.56f);
            env.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            env.localScale = new Vector3(0.46f, 0.30f, 0.04f);
            env.GetComponent<Renderer>().sharedMaterial = envelopeMat;
            var seal = ToyKit.Primitive(PrimitiveType.Cylinder, "WaxSeal", env);
            seal.localPosition = new Vector3(0f, 0f, 0.6f);
            seal.localRotation = Quaternion.Euler(90f, 0f, 0f);
            seal.localScale = new Vector3(0.18f, 0.4f, 0.26f);
            seal.GetComponent<Renderer>().sharedMaterial = sealMat;
            messenger.gameObject.SetActive(false);
            m_messenger = messenger;

            // 夕暮れの床に伸びるあかだるの長い影（S_EMPTY_ROOM で静かに揺れる）
            var shadowRoot = new GameObject("AkadaruLongShadow").transform;
            shadowRoot.SetParent(m_root, false);
            shadowRoot.localPosition = new Vector3(-0.35f, 0.018f, 0.65f);
            shadowRoot.localRotation = Quaternion.Euler(0f, 22f, 0f);
            var floorShadowMat = TitleDressingKit.UnlitTransparent(SoftDotTexture(), new Color(0.06f, 0.04f, 0.05f, 0.48f));
            var sBody = TitleDressingKit.Quad(shadowRoot, "ShadowBody", Vector3.zero, new Vector3(0.85f, 3.2f, 1f), floorShadowMat);
            sBody.localPosition = new Vector3(0f, 0f, -1.6f);
            sBody.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var sHead = TitleDressingKit.Quad(shadowRoot, "ShadowHead", Vector3.zero, new Vector3(0.95f, 1.1f, 1f), floorShadowMat);
            sHead.localPosition = new Vector3(0f, 0.002f, -3.15f);
            sHead.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadowRoot.gameObject.SetActive(false);
            m_longShadow = shadowRoot;

            if (m_doll != null && ShouldHideDoll) m_doll.gameObject.SetActive(false);
        }

        //========================================
        // 部員
        //========================================

        Member AddMember(Vector3 pos, float yaw, Color color, float scale, bool happy,
                         Transform parent = null, string id = null,
                         Dictionary<string, Member> registry = null)
        {
            var m = new Member { basePos = pos, phase = m_members.Count * 1.7f };
            var root = new GameObject("Member").transform;
            root.SetParent(parent != null ? parent : m_root, false);
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var bodyMat = ToyKit.Lit(color, 0.4f);
            float bh = ToyKit.BlockH * scale;
            for (int i = 0; i < 2; i++)
            {
                var b = ToyKit.Primitive(PrimitiveType.Cylinder, "Body", root);
                b.localScale = new Vector3(ToyKit.BlockW * scale * 0.8f, bh * 0.49f, ToyKit.BlockD * scale * 0.8f);
                b.localPosition = new Vector3(0f, bh * (i + 0.5f), 0f);
                b.GetComponent<Renderer>().sharedMaterial = bodyMat;
            }

            var head = ToyKit.CreateHead();
            head.root.SetParent(root, false);
            head.root.localPosition = new Vector3(0f, bh * 2f + ToyKit.HeadR * scale, 0f);
            head.root.localScale = Vector3.one * scale;
            head.SetHappy(happy);

            m.root = root;
            m.head = head.root;
            m_members.Add(m);
            if (!string.IsNullOrEmpty(id))
            {
                if (registry != null) registry[id] = m;
                else m_venueMembers[id] = m;
            }
            return m;
        }

        void BuildMembers()
        {
            // 差し替え前の古い会話用だるま（戻せるように削除せず非表示で残す）
            AddLegacyRoomMember(new Vector3(-1.9f, 0f, 0.05f), 14f, new Color(0.95f, 0.78f, 0.18f), 0.92f, true);
            AddLegacyRoomMember(new Vector3(1.4f, 0f, 0.35f), -14f, new Color(0.28f, 0.62f, 0.32f), 0.92f, false);
            AddLegacyRoomMember(new Vector3(-0.2f, 0f, 2.45f), 2f, new Color(0.85f, 0.22f, 0.18f), 0.95f, true);
            AddLegacyRoomMember(new Vector3(2.9f, 0f, 2.25f), -10f, new Color(0.2f, 0.42f, 0.85f), 0.95f, false);

            // 部室の4人（本編と同じ見た目のだるま・積み木・ハンマー。+Z が正面なので旧向き + 180°）
            AddGameRoomMember("kidaru", new Vector3(-1.9f, 0f, 0.05f), 194f, Nakahira.TumikiColor.Yellow, new Color(0.95f, 0.78f, 0.18f));
            AddGameRoomMember("midaru", new Vector3(1.4f, 0f, 0.35f), 166f, Nakahira.TumikiColor.Green, new Color(0.28f, 0.62f, 0.32f));
            AddGameRoomMember("akadaru", new Vector3(-0.2f, 0f, 2.45f), 182f, Nakahira.TumikiColor.Red, new Color(0.85f, 0.22f, 0.18f));
            AddGameRoomMember("aodaru", new Vector3(2.9f, 0f, 2.25f), 170f, Nakahira.TumikiColor.Blue, new Color(0.2f, 0.42f, 0.85f));
        }

        void AddLegacyRoomMember(Vector3 pos, float yaw, Color color, float scale, bool happy)
        {
            var m = AddMember(pos, yaw, color, scale, happy, m_root, null, m_roomMembers);
            m_members.Remove(m);
            m_legacyRoomMembers.Add(m);
            if (m.root != null)
            {
                m.root.name = "Member_Legacy";
                m.root.gameObject.SetActive(false);
            }
        }

        int GetMemberStackCount(string id)
        {
            var settings = StorySettings.Instance;
            if (settings != null) return settings.GetRoomMemberStackCount(id, 3);
            if (m_roomMemberStacks != null)
            {
                foreach (var c in m_roomMemberStacks)
                {
                    if (c.id == id) return Mathf.Max(0, c.stackCount);
                }
            }
            return 3;
        }

        static CharacterData FindCharacterData(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var modelSettings = GameModelSettings.Current;
            var roster = modelSettings != null ? modelSettings.roster : null;
            if (roster == null)
            {
                var ach = Yurube.Achievements.AchievementSettings.Instance;
                roster = ach != null ? ach.Roster : null;
            }
            if (roster == null || roster.characters == null) return null;
            foreach (var c in roster.characters)
            {
                if (c != null && c.id == id) return c;
            }
            return null;
        }

        static readonly Bounds s_unitBounds = new Bounds(Vector3.zero, Vector3.one);

        // 本編のだるまの見た目（頭・積み木・ハンマー）だけを、物理・AI・操作・ゲージなしで生成する
        Member AddGameRoomMember(string id, Vector3 pos, float yaw, Nakahira.TumikiColor tumikiColor, Color fallbackColor)
        {
            var m = AddGameVisualMember(id, pos, yaw, tumikiColor, fallbackColor, m_root, GetMemberStackCount(id), true);
            m_roomMembers[id] = m;
            return m;
        }

        // ゲーム本編（GameModelSwapper）と同じ仕様・見た目の積み木1個を作る
        Transform CreateMatchStyleBlock(string name, Transform parent, Vector3 localPos, Nakahira.TumikiColor tumikiColor, Color ownerColor)
        {
            var b = new GameObject(name);
            b.transform.SetParent(parent, false);
            b.transform.localPosition = localPos;
            b.transform.localScale = new Vector3(1f, 0.25f, 1f);
            var mf = b.AddComponent<MeshFilter>();
            var mr = b.AddComponent<MeshRenderer>();

            var gms = GameModelSettings.Current;
            var cylBounds = new Bounds(Vector3.zero, new Vector3(1f, 2f, 1f));
            Mesh fittedMesh = null;
            Material blockMat = null;

            if (gms != null && gms.UseWood)
            {
                fittedMesh = GameModelSettings.Fit(gms.woodMesh, cylBounds, false, 0f);
                blockMat = gms.woodMaterial;
                if (fittedMesh != null && blockMat != null)
                {
                    mf.sharedMesh = fittedMesh;
                    mr.sharedMaterial = blockMat;
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_BaseColor", gms.WoodTintFor(ownerColor));
                    mr.SetPropertyBlock(mpb, 0);
                    return b.transform;
                }
            }

            if (gms != null && gms.UseTsumiki)
            {
                fittedMesh = GameModelSettings.Fit(gms.tsumikiMesh, cylBounds, false, 0f);
                blockMat = gms.TsumikiMaterial(tumikiColor);
            }
            if (blockMat == null) blockMat = ToyKit.Lit(ownerColor, 0.4f);

            if (fittedMesh != null)
            {
                mf.sharedMesh = fittedMesh;
            }
            else
            {
                var tempCyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mf.sharedMesh = tempCyl.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tempCyl);
            }
            mr.sharedMaterial = blockMat;
            return b.transform;
        }

        Member AddGameVisualMember(string id, Vector3 pos, float yaw, Nakahira.TumikiColor tumikiColor, Color fallbackColor,
                                   Transform parent, int stackCount, bool withHammer,
                                   DarumaColor darumaColor = DarumaColor.Wood, bool withHachimaki = false)
        {
            var m = new Member { basePos = pos, phase = m_members.Count * 1.7f };
            var root = new GameObject("Member_" + id).transform;
            root.SetParent(parent != null ? parent : m_root, false);
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root, false);
            var stackRoot = new GameObject("Stack").transform;
            stackRoot.SetParent(visual, false);

            var gms = GameModelSettings.Current;
            var data = FindCharacterData(id);
            stackCount = Mathf.Max(0, stackCount);

            // 積み木（本編と同じサイズ・仕様：高さ 0.5m、幅・奥行 1.0m）
            for (int i = 0; i < stackCount; i++)
            {
                CreateMatchStyleBlock("Block_" + i, stackRoot,
                    new Vector3(0f, Nakahira.TumikiBlock.kHeight * (i + 0.5f), 0f),
                    tumikiColor, fallbackColor);
            }

            // 頭（本編の Head と同じ位置・サイズ：y = stackCount * 0.5 + 0.45, scale = 0.9）
            var head = new GameObject("Head").transform;
            head.SetParent(visual, false);
            head.localPosition = new Vector3(0f, stackCount * Nakahira.TumikiBlock.kHeight + 0.45f, 0f);
            head.localScale = Vector3.one * 0.9f;
            var headMf = head.gameObject.AddComponent<MeshFilter>();
            var headMr = head.gameObject.AddComponent<MeshRenderer>();

            bool headSwapped = false;
            GameObject modelObj = data != null ? data.ModelFor(darumaColor) : null;
            if (gms != null && modelObj != null && GameModelSettings.MeshOf(modelObj, out var headMesh, out var headMat))
            {
                var fittedHead = GameModelSettings.Fit(headMesh, s_unitBounds, true, gms.headYaw);
                if (fittedHead != null)
                {
                    headMf.sharedMesh = fittedHead;
                    headMr.sharedMaterial = headMat != null ? headMat : ToyKit.Lit(fallbackColor, 0.5f);
                    var whiteMpb = new MaterialPropertyBlock();
                    whiteMpb.SetColor("_BaseColor", Color.white);
                    headMr.SetPropertyBlock(whiteMpb, 0);
                    headSwapped = true;

                    // 装飾（マント・鉢巻など）があれば見た目だけ装着する
                    var extras = new List<GameObject>();
                    if (data.extraModels != null) extras.AddRange(data.extraModels);
                    if (withHachimaki)
                    {
                        var bandColor = darumaColor == DarumaColor.Wood ? DarumaColor.White : darumaColor;
                        var band = GameModelSettings.HachimakiFor(bandColor);
                        if (band != null) extras.Add(band);
                    }
                    if (extras.Count > 0
                        && GameModelSettings.FitTransform(headMesh, s_unitBounds, gms.headYaw, out var extraRot, out float extraScale, out var extraOffset))
                    {
                        foreach (var extra in extras)
                        {
                            if (extra == null) continue;
                            var ex = Instantiate(extra, head, false);
                            ex.name = "Extra_" + extra.name;
                            ex.transform.localRotation = extraRot;
                            ex.transform.localScale = Vector3.one * extraScale;
                            ex.transform.localPosition = extraOffset;
                            foreach (var smr in ex.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                                smr.updateWhenOffscreen = true;
                        }
                    }
                }
            }
            if (!headSwapped)
            {
                var tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                headMf.sharedMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tempSphere);
                headMr.sharedMaterial = ToyKit.Lit(fallbackColor, 0.5f);
            }

            // ハンマー（本編の HammerPivot と同じ位置・サイズ・weaponReach倍率）
            GameObject hammerSrc = withHammer
                ? (data != null ? (data.HammerFor(darumaColor) ?? GameModelSettings.HammerModel) : GameModelSettings.HammerModel)
                : null;
            if (hammerSrc != null)
            {
                var pivot = new GameObject("HammerPivot").transform;
                pivot.SetParent(root, false);
                pivot.localPosition = new Vector3(0.6f, 0.6f, 0f);
                pivot.localRotation = Quaternion.Euler(-30f, 0f, 0f);
                var hammerModel = ToyKit.CreateHammerModel("HammerModelHead", hammerSrc);
                if (hammerModel != null)
                {
                    hammerModel.name = "HammerModel";
                    hammerModel.SetParent(pivot, false);
                    hammerModel.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    float scale = 1.2f / ToyKit.HammerLength;
                    float reach = data != null ? Mathf.Max(0.5f, data.weaponReach) : 1f;
                    if (!Mathf.Approximately(reach, 1f) && GameModelSettings.MeshOf(hammerSrc, out var wMesh, out _))
                    {
                        float normalTop = GameModelSettings.MeshOf(GameModelSettings.HammerModel, out var hMesh, out _)
                            ? hMesh.bounds.max.y : ToyKit.HammerLength + 0.3f;
                        scale *= reach * normalTop / Mathf.Max(wMesh.bounds.max.y, 0.01f);
                    }
                    hammerModel.localScale = Vector3.one * scale;
                }
            }

            m.root = root;
            m.head = head;
            m_members.Add(m);
            return m;
        }

        //========================================
        // 会話ショットの登録・切り替え（Transform 参照）
        //========================================

        Transform RoomMemberTransform(string id)
        {
            var m = RoomMember(id);
            return m != null ? m.root : m_root;
        }

        void RegisterShot(ShotDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.id)) return;
            m_shots[def.id] = def;
            m_shotList.Add(def);
        }

        void RegisterRoomShots()
        {
            m_shots.Clear();
            m_shotList.Clear();

            // S_ROOM_WIDE：4人が並ぶ全景（本編サイズ3段＋頭に合わせて高さ・引きを調整）
            RegisterShot(new ShotDefinition
            {
                id = "S_ROOM_WIDE",
                target = m_root,
                positionOffset = new Vector3(0.1f, 2.15f, -5.0f),
                lookOffset = new Vector3(0f, 0.85f, 2.2f),
                fov = 44f,
                sway = true,
                memberVisibility = 1,
            });

            // S_WHITEBOARD：ホワイトボードのアップ（導入カットの位置をホワイトボード基準で流用）
            RegisterShot(new ShotDefinition
            {
                id = "S_WHITEBOARD",
                target = m_whiteboard != null ? m_whiteboard : m_root,
                positionOffset = new Vector3(3.43f, -0.35f, 1.5f),
                lookOffset = new Vector3(0.13f, -0.35f, 0.1f),
                fov = 38f,
                memberVisibility = 1,
            });

            // S_KIDARU：きだるのバストアップ（入口側。+Z 正面から撮る）
            RegisterShot(new ShotDefinition
            {
                id = "S_KIDARU",
                target = RoomMemberTransform("kidaru"),
                useTargetRotation = true,
                positionOffset = new Vector3(0f, 1.95f, 2.75f),
                lookOffset = new Vector3(0f, 1.45f, 0f),
                fov = 38f,
                memberVisibility = 1,
            });

            // S_AKA：あかだるのバストアップ（+Z 正面から撮る）
            RegisterShot(new ShotDefinition
            {
                id = "S_AKA",
                target = RoomMemberTransform("akadaru"),
                useTargetRotation = true,
                positionOffset = new Vector3(0f, 1.95f, 2.75f),
                lookOffset = new Vector3(0f, 1.45f, 0f),
                fov = 38f,
                memberVisibility = 1,
            });

            // S_AO：あおだるのバストアップ（+Z 正面から撮る）
            RegisterShot(new ShotDefinition
            {
                id = "S_AO",
                target = RoomMemberTransform("aodaru"),
                useTargetRotation = true,
                positionOffset = new Vector3(0f, 1.95f, 2.75f),
                lookOffset = new Vector3(0f, 1.45f, 0f),
                fov = 38f,
                memberVisibility = 1,
            });

            // S_MI：みだるのバストアップ（+Z 正面から撮る）
            RegisterShot(new ShotDefinition
            {
                id = "S_MI",
                target = RoomMemberTransform("midaru"),
                useTargetRotation = true,
                positionOffset = new Vector3(0f, 1.95f, 2.75f),
                lookOffset = new Vector3(0f, 1.45f, 0f),
                fov = 38f,
                memberVisibility = 1,
            });

            // S_WALL_PAPER：壁の「全国制覇！」の殴り書きの紙
            RegisterShot(new ShotDefinition
            {
                id = "S_WALL_PAPER",
                target = m_wallPaper != null ? m_wallPaper : m_root,
                positionOffset = new Vector3(0.15f, -0.10f, -2.85f),
                lookOffset = new Vector3(0f, -0.05f, 0f),
                fov = 38f,
                memberVisibility = 1,
            });

            // S_OLD_HAMMER：机の上の年季の入った古い槌
            RegisterShot(new ShotDefinition
            {
                id = "S_OLD_HAMMER",
                target = m_oldHammer != null ? m_oldHammer : m_root,
                positionOffset = new Vector3(-0.65f, 0.72f, -1.25f),
                lookOffset = new Vector3(0f, 0.0f, 0f),
                fov = 34f,
                memberVisibility = 1,
            });

            // S_MESSENGER：黒い封筒を持った使者（入口側）
            RegisterShot(new ShotDefinition
            {
                id = "S_MESSENGER",
                target = m_messenger != null ? m_messenger : m_root,
                useTargetRotation = true,
                positionOffset = new Vector3(-0.35f, 1.55f, 2.55f),
                lookOffset = new Vector3(0f, 0.95f, 0.1f),
                fov = 38f,
                memberVisibility = 1,
            });

            // S_DOLL_FAR：窓際の人形（引き気味。0.8秒程度のブレンドで寄る）
            RegisterShot(new ShotDefinition
            {
                id = "S_DOLL_FAR",
                target = m_doll != null ? m_doll : m_root,
                positionOffset = new Vector3(-1.35f, 0.35f, -2.6f),
                lookOffset = new Vector3(-0.2f, -0.22f, 0f),
                fov = 36f,
                blend = true,
                memberVisibility = 0,
            });

            // S_DOLL_EYE：人形の片目のアップ（導入カットの位置を人形基準で流用）
            RegisterShot(new ShotDefinition
            {
                id = "S_DOLL_EYE",
                target = m_doll != null ? m_doll : m_root,
                positionOffset = new Vector3(0f, 0.10f, -0.95f),
                lookOffset = new Vector3(0f, -0.08f, 0f),
                fov = 28f,
                memberVisibility = 0,
            });

            // S_EMPTY_ROOM：キャラクター非表示の部室（序章では夕暮れの古い槌と長い影を見せる）
            RegisterShot(new ShotDefinition
            {
                id = "S_EMPTY_ROOM",
                target = m_root,
                positionOffset = new Vector3(-1.35f, 1.65f, -3.2f),
                lookOffset = new Vector3(0.05f, 0.55f, 1.15f),
                fov = 42f,
                sway = true,
                memberVisibility = -1,
            });
        }

        void SetRoomMembersVisible(bool visible)
        {
            foreach (var kv in m_roomMembers)
            {
                if (kv.Value != null && kv.Value.root != null)
                    kv.Value.root.gameObject.SetActive(visible);
            }
        }

        void EvaluateShot(ShotDefinition def, float lt, out Vector3 pos, out Vector3 look)
        {
            Transform t = def.target != null ? def.target : m_root;
            Vector3 anchor = t != null ? t.position : SetOrigin;
            // 部員の呼吸アニメでカメラ自体が上下に揺れないよう、部室直下のキャラは床の高さを基準にする
            if (m_root != null && t != null && t != m_root && t.parent == m_root && def.useTargetRotation)
                anchor.y = m_root.position.y;

            if (def.useTargetRotation && t != null)
            {
                Vector3 fwd = t.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                fwd.Normalize();
                Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
                pos = anchor + rot * def.positionOffset;
                look = anchor + rot * def.lookOffset;
            }
            else
            {
                pos = anchor + def.positionOffset;
                look = anchor + def.lookOffset;
            }

            if (def.sway)
            {
                pos += new Vector3(0.09f * Mathf.Sin(lt * 0.11f), 0f, 0.05f * Mathf.Sin(lt * 0.07f));
            }

            // 壁の外へ出ないように部室内に収める（会場セット・ステージでは制限しない）
            if (!m_arenaMode && m_root != null && (def.target == null || def.target == m_root || def.target.IsChildOf(m_root)))
            {
                Vector3 local = pos - m_root.position;
                local.x = Mathf.Clamp(local.x, -5.4f, 5.4f);
                local.z = Mathf.Clamp(local.z, -4.65f, 4.4f);
                pos = m_root.position + local;
            }
        }

        void ApplyShotDefinition(ShotDefinition def, float blendSeconds = 0f)
        {
            if (def == null) return;
            m_currentShotId = def.id;

            if (m_ch4FinalMode)
            {
                UseCh4SetCamera();
            }
            else if (!m_arenaMode)
            {
                if (def.memberVisibility > 0) SetRoomMembersVisible(true);
                else if (def.memberVisibility < 0) SetRoomMembersVisible(false);
                UseRoomCamera();
            }
            else
            {
                UseArenaCamera();
            }

            if (blendSeconds > 0.01f && m_camera != null && m_camPos != null && m_camLook != null)
            {
                float curLt = m_time - m_shotStart;
                Vector3 fromPos = m_camPos(curLt);
                Vector3 fromLook = m_camLook(curLt);
                m_blendFromFov = m_camera.fieldOfView;
                m_blendDuration = blendSeconds;

                SetShot(
                    lt =>
                    {
                        EvaluateShot(def, lt, out var toPos, out _);
                        float u = m_blendDuration > 0.01f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(lt / m_blendDuration)) : 1f;
                        return Vector3.Lerp(fromPos, toPos, u);
                    },
                    lt =>
                    {
                        EvaluateShot(def, lt, out _, out var toLook);
                        float u = m_blendDuration > 0.01f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(lt / m_blendDuration)) : 1f;
                        return Vector3.Lerp(fromLook, toLook, u);
                    },
                    def.fov);
            }
            else
            {
                m_blendDuration = 0f;
                SetShot(
                    lt => { EvaluateShot(def, lt, out var p, out _); return p; },
                    lt => { EvaluateShot(def, lt, out _, out var l); return l; },
                    def.fov);
            }
        }

        // ショットIDでカメラを切り替える。空なら直前のショットを維持、未登録なら警告を出して維持する
        public void ApplyConversationShot(string shotId)
        {
            if (string.IsNullOrEmpty(shotId)) return;
            if (!m_shots.TryGetValue(shotId, out var def) || def == null)
            {
                Debug.LogWarning("[Story] 未登録のショットIDです: " + shotId);
                return;
            }

            StopEmptyRoomSequence();

            if (!m_arenaMode && !m_ch4FinalMode && !m_finalMode)
            {
                if (m_doll != null)
                    m_doll.gameObject.SetActive(!ShouldHideDoll);
                if (m_messenger != null)
                    m_messenger.gameObject.SetActive(shotId == "S_MESSENGER");
                if (m_longShadow != null)
                    m_longShadow.gameObject.SetActive(IsPrologueChapter && shotId == "S_EMPTY_ROOM");
                SetDarkMood(IsPrologueChapter && shotId == "S_OLD_HAMMER");
            }
            else if (m_doll != null && ShouldHideDoll)
            {
                m_doll.gameObject.SetActive(false);
            }

            if (shotId == "S_EMPTY_ROOM")
            {
                ApplyShotDefinition(def, 0f);
                if (!IsPrologueChapter)
                    m_emptyRoomRoutine = StartCoroutine(EmptyRoomToDollRoutine());
                return;
            }

            float blend = def.blend ? Mathf.Max(0.05f, m_dollFarBlendSeconds) : 0f;
            ApplyShotDefinition(def, blend);
        }

        // S_EMPTY_ROOM：4人を非表示にした部室を見せ、表示中に S_DOLL_FAR へ寄って人形を小さく揺らす
        IEnumerator EmptyRoomToDollRoutine()
        {
            float hold = Mathf.Max(0f, m_emptyRoomHoldSeconds);
            if (hold > 0f)
            {
                float until = m_time + hold;
                while (m_time < until) yield return null;
            }

            if (m_shots.TryGetValue("S_DOLL_FAR", out var dollFar) && dollFar != null)
            {
                float blend = Mathf.Max(0.05f, m_dollFarBlendSeconds);
                ApplyShotDefinition(dollFar, blend);
                // 人形へ寄り切るころに合わせて、カタ、と小さく揺らす
                float waitBeforeShake = blend * 0.65f;
                float shakeAt = m_time + waitBeforeShake;
                while (m_time < shakeAt) yield return null;
            }

            TriggerDollShake();
            m_emptyRoomRoutine = null;
        }

        void StopEmptyRoomSequence()
        {
            if (m_emptyRoomRoutine != null)
            {
                StopCoroutine(m_emptyRoomRoutine);
                m_emptyRoomRoutine = null;
            }
        }

        void OnStoryEventPlayed(string eventId)
        {
            if (eventId != "doll_shake") return;
            // S_EMPTY_ROOM の行では EmptyRoomToDollRoutine 内で寄りに合わせて揺らすので重複させない
            if (m_emptyRoomRoutine != null) return;
            TriggerDollShake();
        }

        public void TriggerDollShake()
        {
            m_dollShakeStart = m_time;
            if (m_dollShakeSe != null) MenuSounds.Play(m_dollShakeSe, 0.8f);
        }

        //========================================
        // 全国大会会場（鳥居の前）のセット（第2章）
        //========================================

        Transform VenueBox(string name, Vector3 pos, Vector3 size, Material mat)
        {
            var t = ToyKit.Primitive(PrimitiveType.Cube, name, m_venueRoot);
            t.localPosition = pos;
            t.localScale = size;
            t.GetComponent<Renderer>().sharedMaterial = mat;
            return t;
        }

        void BuildVenue()
        {
            if (m_venueBuilt) return;
            m_venueBuilt = true;

            m_venueRoot = new GameObject("StoryVenueSet").transform;
            m_venueRoot.position = VenueOrigin;

            var groundMat = Unlit(new Color(0.64f, 0.60f, 0.53f));
            var redMat = Unlit(new Color(0.78f, 0.13f, 0.10f));
            var redDarkMat = Unlit(new Color(0.52f, 0.08f, 0.06f));
            var stoneMat = Unlit(new Color(0.55f, 0.53f, 0.48f));
            var boardMat = Unlit(new Color(0.97f, 0.95f, 0.88f));

            // 床と空
            VenueBox("VenueGround", new Vector3(0f, -0.1f, 0f), new Vector3(26f, 0.2f, 26f), groundMat);
            m_venueSky = TitleDressingKit.Quad(m_venueRoot, "VenueSky", V(0f, 7f, 22f), new Vector3(60f, 30f, 1f), m_skyDay);

            // 鳥居（奥）
            VenueBox("ToriiBaseL", new Vector3(-3.0f, 0.15f, 6f), new Vector3(0.9f, 0.3f, 0.9f), stoneMat);
            VenueBox("ToriiBaseR", new Vector3(3.0f, 0.15f, 6f), new Vector3(0.9f, 0.3f, 0.9f), stoneMat);
            VenueBox("ToriiPillarL", new Vector3(-3.0f, 2.6f, 6f), new Vector3(0.45f, 5.2f, 0.45f), redMat);
            VenueBox("ToriiPillarR", new Vector3(3.0f, 2.6f, 6f), new Vector3(0.45f, 5.2f, 0.45f), redMat);
            VenueBox("ToriiTop", new Vector3(0f, 5.15f, 6f), new Vector3(8.2f, 0.5f, 0.72f), redDarkMat);
            VenueBox("ToriiBeam", new Vector3(0f, 4.25f, 6f), new Vector3(6.6f, 0.35f, 0.55f), redMat);
            VenueBox("ToriiPlaque", new Vector3(0f, 4.7f, 5.58f), new Vector3(1.3f, 1.0f, 0.1f), m_inkMat);

            // 看板「全国制覇！ だるま落とし部」（手前）
            VenueBox("SignPostL", new Vector3(-2.2f, 1.2f, 3.4f), new Vector3(0.18f, 2.4f, 0.18f), m_woodMat);
            VenueBox("SignPostR", new Vector3(2.2f, 1.2f, 3.4f), new Vector3(0.18f, 2.4f, 0.18f), m_woodMat);
            VenueBox("SignFrame", new Vector3(0f, 3.1f, 3.47f), new Vector3(5.2f, 2.0f, 0.12f), m_inkMat);
            VenueBox("SignBoard", new Vector3(0f, 3.1f, 3.36f), new Vector3(5.0f, 1.8f, 0.14f), boardMat);

            var signText = TitleDressingKit.Text(m_venueRoot, PresentationSettings.DefaultFont,
                "全国制覇！\nだるま落とし部", V(0f, 3.1f, 3.24f), new Vector2(4.6f, 1.5f), 3.2f,
                new Color(0.12f, 0.10f, 0.09f), new Color(1f, 1f, 1f), 0.08f, 0f);
            signText.transform.localRotation = Quaternion.identity;
            signText.textWrappingMode = TextWrappingModes.Normal;
            signText.alignment = TextAlignmentOptions.Center;
            signText.lineSpacing = 6f;

            // 約束する4人（あか・あお・み・き）。へびだるは画面の外に立たせ、会話で寄る
            AddMember(new Vector3(-2.4f, 0f, 0f), 0f, new Color(0.85f, 0.22f, 0.18f), 1.0f, true, m_venueRoot, "akadaru");
            AddMember(new Vector3(-0.8f, 0f, 0f), 0f, new Color(0.2f, 0.42f, 0.85f), 1.0f, false, m_venueRoot, "aodaru");
            AddMember(new Vector3(0.8f, 0f, 0f), 0f, new Color(0.28f, 0.62f, 0.32f), 1.0f, false, m_venueRoot, "midaru");
            AddMember(new Vector3(2.4f, 0f, 0f), 0f, new Color(0.95f, 0.78f, 0.18f), 0.96f, true, m_venueRoot, "kidaru");
            var hebi = AddMember(new Vector3(8.8f, 0f, 2.0f), -20f, new Color(0.35f, 0.62f, 0.22f), 1.0f, false, m_venueRoot, "hebidaru");
            // カットシーンでは4人だけを映す。へびだるは会話のときに出会うまで隠しておく
            if (hebi != null && hebi.root != null) hebi.root.gameObject.SetActive(false);

            // 会場の光（屋外なので、正面と上から）
            VenueLight("VenueSun", new Vector3(0f, 5.5f, -2f), new Color(1f, 0.9f, 0.74f), 12f, 22f);
            VenueLight("VenueFill", new Vector3(0f, 3.2f, 4.5f), new Color(0.8f, 0.88f, 1f), 7f, 18f);
            VenueLight("VenueFront", new Vector3(0f, 2.2f, -5f), new Color(1f, 0.95f, 0.88f), 9f, 16f);
        }

        void VenueLight(string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_venueRoot, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        void AnimateMembers()
        {
            for (int i = 0; i < m_members.Count; i++)
            {
                var m = m_members[i];
                if (m.root == null || !m.root.gameObject.activeInHierarchy) continue;
                m.root.localPosition = m.basePos + Vector3.up * (0.012f * Mathf.Sin(m_time * 1.7f + m.phase));
                if (m.head != null)
                    m.head.localRotation = Quaternion.Euler(0f, 0f, 1.2f * Mathf.Sin(m_time * 0.8f + m.phase));
            }

            // だるま人形の小さな往復揺れ（m_dollShakeSeconds 秒で往復して止まる）
            if (m_doll != null)
            {
                if (m_dollShakeStart >= 0f)
                {
                    float duration = Mathf.Max(0.05f, m_dollShakeSeconds);
                    float elapsed = m_time - m_dollShakeStart;
                    if (elapsed < duration)
                    {
                        float u = elapsed / duration;
                        float damp = 1f - u * 0.45f;
                        float angle = Mathf.Sin(u * Mathf.PI * 2f) * m_dollShakeAngle * damp;
                        m_doll.localRotation = Quaternion.Euler(0f, 0f, angle);
                    }
                    else
                    {
                        m_doll.localRotation = Quaternion.identity;
                        m_dollShakeStart = -1f;
                    }
                }
                else
                {
                    float z = m_doll.localEulerAngles.z;
                    if (z > 180f) z -= 360f;
                    if (Mathf.Abs(z) > 0.01f)
                        m_doll.localRotation = Quaternion.Euler(0f, 0f, Mathf.MoveTowards(z, 0f, Time.unscaledDeltaTime * 16f));
                }
            }

            // 夕暮れの長い影（静かに揺れる）
            if (m_longShadow != null && m_longShadow.gameObject.activeInHierarchy)
            {
                float yaw = 22f + Mathf.Sin(m_time * 0.85f) * 2.4f;
                float scaleZ = 1f + Mathf.Sin(m_time * 0.6f) * 0.03f;
                m_longShadow.localRotation = Quaternion.Euler(0f, yaw, 0f);
                m_longShadow.localScale = new Vector3(1f, 1f, scaleZ);
            }
        }

        //========================================
        // 光・空気
        //========================================

        void BuildLights()
        {
            m_sun = MakePointLight("StorySun", new Vector3(2.4f, 3.2f, 3.6f), new Color(1f, 0.8f, 0.58f), 10f, 10f);
            m_fill = MakePointLight("StoryFill", new Vector3(0f, 3.2f, -1.6f), new Color(0.75f, 0.82f, 1f), 6f, 13f);
            MakePointLight("StoryWarm", new Vector3(0f, 1.2f, 0.4f), new Color(1f, 0.75f, 0.5f), 4f, 8f);
            // 部員が暗くならないように、カメラ側からも光を当てる
            MakePointLight("StoryFront", new Vector3(0f, 2.4f, -3.4f), new Color(1f, 0.95f, 0.88f), 12f, 14f);
        }

        Light MakePointLight(string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_root, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        void BuildDust()
        {
            TitleDressingKit.Drift(m_root, "StoryDust", P(0f, 2.6f, 0.6f), new Vector3(9f, 3.5f, 8f),
                SoftDotTexture(), true,
                new Color(1f, 0.9f, 0.72f, 0.5f), new Color(1f, 0.82f, 0.55f, 0.25f),
                new Vector2(0.015f, 0.045f), 10f,
                new Vector3(-0.04f, -0.01f, -0.04f), new Vector3(0.04f, 0.02f, 0.04f));
        }

        //========================================
        // カメラ・画面
        //========================================

        void BuildCamera()
        {
            var go = new GameObject("StoryMovieCamera");
            m_camera = go.AddComponent<Camera>();
            m_rt = new RenderTexture(Mathf.Max(640, Screen.width), Mathf.Max(360, Screen.height), 24) { name = "StoryMovie" };
            m_camera.targetTexture = m_rt;
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = new Color(0.07f, 0.05f, 0.06f);
            m_camera.nearClipPlane = 0.05f;
            m_camera.farClipPlane = 60f;
            m_camera.fieldOfView = 42f;

            var data = go.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
        }

        // ムービーのためだけの Volume（回想のセピア・タイトルのシネマ調）。
        // 使わないときは GameObject ごと消しておくので、試合中の見た目には影響しない。
        void BuildPost()
        {
            var go = new GameObject("StoryMoviePost");
            m_volume = go.AddComponent<Volume>();
            m_volume.isGlobal = true;
            m_volume.priority = 1000f;

            m_profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_ca = m_profile.Add<ColorAdjustments>(true);
            m_vig = m_profile.Add<Vignette>(true);
            m_bloom = m_profile.Add<Bloom>(true);

            m_ca.postExposure.overrideState = true;
            m_ca.contrast.overrideState = true;
            m_ca.saturation.overrideState = true;
            m_ca.colorFilter.overrideState = true;
            m_vig.intensity.overrideState = true;
            m_vig.color.overrideState = true;
            m_bloom.intensity.overrideState = true;

            m_volume.sharedProfile = m_profile;
            go.SetActive(false);
        }

        void BuildScreen()
        {
            var font = PresentationSettings.DefaultFont;
            m_canvas = ToyKit.CreateOverlayCanvas("StoryMovieScreen", CanvasOrder);
            m_screen = m_canvas.gameObject;

            // 映像
            var viewGo = new GameObject("View", typeof(RawImage));
            viewGo.transform.SetParent(m_canvas, false);
            m_view = viewGo.GetComponent<RawImage>();
            m_view.texture = m_rt;
            m_view.raycastTarget = false;
            ToyKit.Stretch(m_view.rectTransform);

            // 周辺を暗くする
            var vig = ToyKit.UIImage("Vignette", m_canvas, Color.white);
            Image vigImage = vig.GetComponent<Image>();
            vigImage.sprite = SpriteOf(VignetteTexture());
            vigImage.type = Image.Type.Simple;
            ToyKit.Stretch(vig);

            // 上下の黒帯
            var top = ToyKit.UIImage("BarTop", m_canvas, Color.black);
            top.pivot = new Vector2(0.5f, 1f);
            ToyKit.Anchor(top, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1920f, 92f));
            var bottom = ToyKit.UIImage("BarBottom", m_canvas, Color.black);
            bottom.pivot = new Vector2(0.5f, 0f);
            ToyKit.Anchor(bottom, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1920f, 92f));

            // 場所の説明
            m_caption = ToyKit.UIText("Caption", m_canvas, font, "春 ― 七転高校 だるま落とし部", 30f, new Color(1f, 0.95f, 0.85f));
            m_caption.alignment = TextAlignmentOptions.Left;
            m_caption.outlineWidth = 0.2f;
            m_caption.outlineColor = new Color32(20, 14, 10, 255);
            m_caption.rectTransform.pivot = new Vector2(0f, 0f);
            ToyKit.Anchor(m_caption.rectTransform, new Vector2(0f, 0f), new Vector2(64f, 112f), new Vector2(1000f, 44f));
            m_captionGroup = m_caption.gameObject.AddComponent<CanvasGroup>();

            // スキップの案内（会話・実績と同じ表記）
            var hintPlate = ToyKit.UIRounded("MovieHint", m_canvas, new Color(0.05f, 0.04f, 0.03f, 0.72f), 14f);
            hintPlate.pivot = new Vector2(1f, 0f);
            ToyKit.Anchor(hintPlate, new Vector2(1f, 0f), new Vector2(-32f, 112f), new Vector2(540f, 50f));
            m_hintPlate = hintPlate;
            m_hintGroup = hintPlate.gameObject.AddComponent<CanvasGroup>();
            var hint = ToyKit.UIText("Text", hintPlate, font, StoryText.Hint, 24f, Color.white);
            ToyKit.Stretch(hint.rectTransform);
            hint.alignment = TextAlignmentOptions.Right;
            hint.rectTransform.offsetMin = new Vector2(12f, 0f);
            hint.rectTransform.offsetMax = new Vector2(-12f, 0f);
            hint.ForceMeshUpdate();
            // 文字の幅に合わせて背景を縮める
            m_hintPlate.sizeDelta = new Vector2(Mathf.Clamp(hint.preferredWidth + 28f, 260f, 540f), 50f);

            // タイトル
            var cardGo = new GameObject("TitleCard", typeof(RectTransform));
            cardGo.transform.SetParent(m_canvas, false);
            var card = (RectTransform)cardGo.transform;
            ToyKit.Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0f, 100f), new Vector2(1500f, 360f));
            m_titleGroup = cardGo.AddComponent<CanvasGroup>();

            string title = m_chapter != null && !string.IsNullOrEmpty(m_chapter.title) ? m_chapter.title : "序章";
            int quote = title.IndexOf('「');
            m_titleMain = ToyKit.UIText("Main", card, font, quote > 0 ? title.Substring(0, quote) : title, 104f, Color.white);
            m_titleMain.fontStyle = FontStyles.Bold;
            m_titleMain.outlineWidth = 0.28f;
            m_titleMain.outlineColor = new Color32(24, 14, 8, 255);
            ToyKit.Anchor(m_titleMain.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1400f, 140f));

            m_titleSub = ToyKit.UIText("Sub", card, font, quote > 0 ? title.Substring(quote).Trim('「', '」') : "", 44f, new Color(1f, 0.9f, 0.72f));
            m_titleSub.outlineWidth = 0.2f;
            m_titleSub.outlineColor = new Color32(24, 14, 8, 255);
            ToyKit.Anchor(m_titleSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -28f), new Vector2(1400f, 70f));

            var line = ToyKit.UIImage("Line", card, new Color(1f, 0.85f, 0.55f, 0.9f));
            ToyKit.Anchor(line, new Vector2(0.5f, 0.5f), new Vector2(0f, -88f), new Vector2(300f, 3f));

            // 暗転
            var fade = ToyKit.UIImage("Fade", m_canvas, Color.black);
            ToyKit.Stretch(fade);
            m_fadeGroup = fade.gameObject.AddComponent<CanvasGroup>();
            m_fadeGroup.alpha = 1f;

            SetCaption(false);
            SetTitle(false);
            SetHint(false);
        }

        //========================================
        // 表示・再生
        //========================================

        public void Show()
        {
            m_visible = true;
            if (m_view != null) m_view.enabled = true;
            if (m_screen != null) m_screen.SetActive(true);
            if (m_camera != null) m_camera.enabled = true;
        }

        public void Hide()
        {
            StopEmptyRoomSequence();
            m_blendDuration = 0f;
            m_dollShakeStart = -1f;
            if (m_doll != null) m_doll.localRotation = Quaternion.identity;
            if (m_messenger != null) m_messenger.gameObject.SetActive(false);
            if (m_longShadow != null) m_longShadow.gameObject.SetActive(false);
            m_darkMood = false;
            m_nightRoomMode = false;
            m_nightMood = false;
            StopDialogueFade();
            SetRoomMembersVisible(true);
            RestoreRunner();
            m_stageMode = false;
            m_venueMode = false;
            m_finalMode = false;
            m_ch4FinalMode = false;
            m_arenaMode = false;
            m_finalScene = "";
            m_arenaCollapseStart = -1f;
            m_arenaHammerRollStart = -1f;
            if (m_arenaFadeRoutine != null) { StopCoroutine(m_arenaFadeRoutine); m_arenaFadeRoutine = null; }
            if (m_preludeBlackoutCleanupRoutine != null) { StopCoroutine(m_preludeBlackoutCleanupRoutine); m_preludeBlackoutCleanupRoutine = null; }
            m_preludeFadeDelegatedToVfx = false;
            m_bannerStart = -1f;
            ResetFinalEndingVisuals();
            if (m_vfxPlayer != null) m_vfxPlayer.StopAll(true);
            m_visible = false;
            m_opening = false;
            m_advance = false;
            m_baseScale = 1f;
            SetMemory(false);
            SetCinema(false);
            if (m_view != null) m_view.enabled = true;
            if (m_screen != null) m_screen.SetActive(false);
            if (m_camera != null) m_camera.enabled = false;
            // 画面に直接描いていないことを保証する（撮影用の RenderTexture はそのまま）
            RenderTexture.active = null;
        }

        // 映像は止めるが、黒い画面は残す。
        // 会話（前）のあと、ステージが見えるまでのつなぎに使う（ステージが一瞬映るのを防ぐ）。
        public void HideViewKeepBlack()
        {
            StopEmptyRoomSequence();
            m_blendDuration = 0f;
            m_dollShakeStart = -1f;
            if (m_doll != null) m_doll.localRotation = Quaternion.identity;
            if (m_messenger != null) m_messenger.gameObject.SetActive(false);
            if (m_longShadow != null) m_longShadow.gameObject.SetActive(false);
            m_darkMood = false;
            m_nightRoomMode = false;
            m_nightMood = false;
            StopDialogueFade();
            SetRoomMembersVisible(true);
            RestoreRunner();
            m_stageMode = false;
            m_venueMode = false;
            m_finalMode = false;
            m_ch4FinalMode = false;
            m_arenaMode = false;
            m_finalScene = "";
            m_arenaCollapseStart = -1f;
            m_arenaHammerRollStart = -1f;
            ResetFinalEndingVisuals();
            if (m_vfxPlayer != null) m_vfxPlayer.StopAll(true);
            if (m_arenaFadeRoutine != null) { StopCoroutine(m_arenaFadeRoutine); m_arenaFadeRoutine = null; }
            if (m_preludeBlackoutCleanupRoutine != null) { StopCoroutine(m_preludeBlackoutCleanupRoutine); m_preludeBlackoutCleanupRoutine = null; }
            m_preludeFadeDelegatedToVfx = false;
            m_visible = false;
            m_opening = false;
            m_advance = false;
            m_baseScale = 1f;
            SetMemory(false);
            SetCinema(false);
            SetCaption(false);
            SetTitle(false);
            SetHint(false);
            if (m_camera != null) m_camera.enabled = false;
            if (m_view != null) m_view.enabled = false;
            if (m_screen != null) m_screen.SetActive(true);
            if (m_fadeGroup != null) m_fadeGroup.alpha = 1f;
            RenderTexture.active = null;
        }

        // 黒い画面だけを残す（エピローグのあと、メニューへ移るまでステージを見せない）
        public void ShowBlackCover()
        {
            if (m_screen == null) return;
            m_screen.SetActive(true);
            if (m_view != null) m_view.enabled = false;
            if (m_camera != null) m_camera.enabled = false;
            if (m_fadeGroup != null) m_fadeGroup.alpha = 1f;
            RenderTexture.active = null;
        }

        // 黒い画面を消して、下のステージを見せる
        public IEnumerator FadeOutBlack(float duration)
        {
            if (m_screen == null) yield break;
            m_screen.SetActive(true);
            if (m_view != null) m_view.enabled = false;
            if (m_camera != null) m_camera.enabled = false;
            yield return FadeTo(0f, duration);
            m_screen.SetActive(false);
            m_visible = false;
        }

        public void SetBlack()
        {
            if (m_fadeGroup != null) m_fadeGroup.alpha = 1f;
        }

        // 会話の背景にする（sunset: 夕方の光）
        public void ShowConversation(bool sunset)
        {
            Show();
            StopEmptyRoomSequence();
            m_blendDuration = 0f;
            m_dollShakeStart = -1f;
            if (m_doll != null)
            {
                m_doll.localRotation = Quaternion.identity;
                m_doll.gameObject.SetActive(!ShouldHideDoll);
            }
            if (m_messenger != null) m_messenger.gameObject.SetActive(false);
            if (m_longShadow != null) m_longShadow.gameObject.SetActive(false);
            m_darkMood = false;
            m_nightRoomMode = false;
            SetNightRoom(false);
            StopDialogueFade();
            SetRoomMembersVisible(true);
            m_stageMode = false;
            m_opening = false;
            m_advance = false;
            m_baseScale = 1f;
            SetSunset(sunset);
            SetCinema(true);
            SetCaption(false);
            SetTitle(false);
            SetHint(false);

            // 最初の行にショットIDがあれば最初からその画にしておく（暗転明けの一瞬の画飛びを防ぐ）
            string initialShot = "S_ROOM_WIDE";
            var lines = m_chapter != null ? (sunset ? m_chapter.after : m_chapter.before) : null;
            if (lines != null)
            {
                foreach (var l in lines)
                {
                    if (l == null) continue;
                    if (!string.IsNullOrEmpty(l.shot) && m_shots.ContainsKey(l.shot)) initialShot = l.shot;
                    break;
                }
            }
            if (m_shots.ContainsKey(initialShot))
            {
                ApplyConversationShot(initialShot);
            }
            else
            {
                SetShot(
                    lt => P(0.1f + 0.09f * Mathf.Sin(lt * 0.11f), 1.85f, -4.6f + 0.05f * Mathf.Sin(lt * 0.07f)),
                    lt => P(0f, 0.55f, 2.2f),
                    42f);
            }
        }

        // 会話が終わったとき（B でスキップされたときも含む）に、途中のブレンドや演出を正しい終端状態に整える
        public void EndConversation(bool afterConversation)
        {
            bool wasEmptyRoom = m_emptyRoomRoutine != null || m_currentShotId == "S_EMPTY_ROOM";
            StopEmptyRoomSequence();
            StopDialogueFade();
            m_dollShakeStart = -1f;
            if (m_doll != null) m_doll.localRotation = Quaternion.identity;
            SetDarkMood(false);

            if (!IsPrologueChapter && afterConversation && wasEmptyRoom && m_shots.TryGetValue("S_DOLL_FAR", out var dollFar) && dollFar != null)
            {
                SetRoomMembersVisible(false);
                ApplyShotDefinition(dollFar, 0f);
            }
            else
            {
                m_blendDuration = 0f;
                if (!afterConversation)
                {
                    if (m_messenger != null) m_messenger.gameObject.SetActive(false);
                    if (m_longShadow != null) m_longShadow.gameObject.SetActive(false);
                    SetRoomMembersVisible(true);
                }
                ApplyShotNow();
            }
        }

        public IEnumerator FadeIn(float duration)
        {
            yield return FadeTo(0f, duration);
        }

        public IEnumerator FadeToBlack(float duration)
        {
            yield return FadeTo(1f, duration);
        }

        IEnumerator FadeTo(float target, float duration)
        {
            if (m_fadeGroup == null) yield break;
            float from = m_fadeGroup.alpha;
            for (float t = 0f; t < 1f; t += RealDt / Mathf.Max(0.01f, duration))
            {
                m_fadeGroup.alpha = Mathf.Lerp(from, target, Mathf.Clamp01(t));
                yield return null;
            }
            m_fadeGroup.alpha = target;
            // フェード中のAは「次のカットへ」として持ち越さない
            // （持ち越すと、フェード明けの最初のカットが一瞬で飛ばされて別の画が見える）
            m_advance = false;
        }

        //========================================
        // 台本（タイトルのムービーと同じ書き方）
        //========================================

        float RealDt => Time.unscaledDeltaTime;

        // カットのカメラ。lt（カット開始からの経過）を受け取って位置・注視点を返す
        void SetShot(System.Func<float, Vector3> pos, System.Func<float, Vector3> look, float fov)
        {
            m_camPos = pos;
            m_camLook = look;
            m_shotStart = m_time;
            m_shotFov = fov;
            // コルーチンから切り替えた直後も、次のUpdateまで前の画を残さない
            ApplyShotNow();
        }

        // いまのカットのカメラをすぐ反映する（Aでカットを送った瞬間の1フレームの残像を防ぐ）
        void ApplyShotNow()
        {
            if (m_camera == null || m_camPos == null || m_camLook == null) return;
            Vector3 pos = m_camPos(0f);
            Vector3 look = m_camLook(0f);
            Vector3 dir = look - pos;
            if (dir.sqrMagnitude <= 0.0001f) return;
            m_camera.transform.position = pos;
            m_camera.transform.rotation = Quaternion.LookRotation(dir);
            m_camera.fieldOfView = Mathf.Max(1f, m_shotFov);
        }

        // A：つぎへ（次のカットへ進む）
        IEnumerator At(float time)
        {
            float target = time + m_shift;
            while (m_time < target && !m_skipped && !m_advance) yield return null;
            if (m_advance && !m_skipped)
            {
                // Aで早く送った分を、以降のカットの時刻から差し引く。
                // 差し引かないと、飛ばした時間を次のカット（最後はタイトル前）で待ち直してしまい、
                // 「Aで飛ばしたのにタイトルが出るのが遅い／長い」ことになる。
                m_shift -= Mathf.Max(0f, target - m_time);

                // 回想のスロー中にAで送った場合、次のカットまでスローのままにしない
                if (m_slowMo != null)
                {
                    StopCoroutine(m_slowMo);
                    m_slowMo = null;
                    m_baseScale = 1f;
                }
            }
            m_advance = false;
        }

        IEnumerator Tween(float duration, System.Action<float> step)
        {
            float start = m_time;
            while (m_time < start + duration && !m_skipped && !m_advance)
            {
                step(Mathf.Clamp01((m_time - start) / duration));
                yield return null;
            }
            if (!m_skipped && !m_advance) step(1f);
            m_advance = false;
        }

        IEnumerator Hold(float seconds)
        {
            float end = m_time + seconds;
            while (m_time < end && !m_skipped && !m_advance) yield return null;
            m_advance = false;
        }

        // 台本の時計だけを遅くする（ゲームの Time.timeScale は触らない：会話中は 0 のため）
        IEnumerator SlowMo(float scale, float holdReal)
        {
            m_baseScale = scale;
            for (float t = 0f; t < holdReal; t += Time.unscaledDeltaTime)
            {
                if (!m_opening) yield break;
                yield return null;
            }
            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
            {
                m_baseScale = Mathf.Lerp(scale, 1f, t / 0.3f);
                yield return null;
            }
            m_baseScale = 1f;
        }

        // オープニング（A：つぎへ / B：スキップ）
        public IEnumerator PlayOpening()
        {
            m_time = 0f;
            m_shift = 0f;
            m_slowMo = null;
            m_skipped = false;
            WasSkipped = false;
            m_advance = false;
            m_baseScale = 1f;
            m_opening = true;
            m_stageMode = false;
            m_venueMode = false;
            m_finalMode = false;
            m_ch4FinalMode = false;
            m_arenaMode = false;
            m_finalScene = "";
            m_arenaCollapseStart = -1f;
            m_arenaHammerRollStart = -1f;
            if (m_arenaFadeRoutine != null) { StopCoroutine(m_arenaFadeRoutine); m_arenaFadeRoutine = null; }
            if (m_preludeBlackoutCleanupRoutine != null) { StopCoroutine(m_preludeBlackoutCleanupRoutine); m_preludeBlackoutCleanupRoutine = null; }
            m_preludeFadeDelegatedToVfx = false;
            Show();
            m_nightRoomMode = false;
            SetNightRoom(false);
            if (m_doll != null) m_doll.gameObject.SetActive(!ShouldHideDoll);
            if (m_messenger != null) m_messenger.gameObject.SetActive(false);
            if (m_longShadow != null) m_longShadow.gameObject.SetActive(false);
            m_darkMood = false;
            m_fadeGroup.alpha = 1f;
            SetCaption(false);
            SetTitle(false);
            SetHint(true);
            SetMemory(false);
            SetCinema(false);
            // フェードインの前から、最初のカットのカメラにしておく（原点の映像を見せない）
            SetShot(lt => P(0f, 2.0f - lt * 0.05f, -4.8f + lt * 0.15f), lt => P(0f, 0.65f, 2.4f), 44f);
            yield return FadeTo(0f, 0.5f);
            SetCaption(true);

            // 1. 部室の全景
            yield return At(4.0f);
            if (m_skipped) { yield return FinishOpening(); yield break; }

            // 2. 壁の「全国制覇！」の殴り書きの紙と、机の上の年季の入った古い槌
            SetShot(
                lt =>
                {
                    float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(lt / 3.8f));
                    return Vector3.Lerp(P(-3.15f, 2.60f, 1.95f), P(-0.55f, 1.68f, 0.05f), u);
                },
                lt =>
                {
                    float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(lt / 3.8f));
                    return Vector3.Lerp(P(-3.30f, 2.68f, 4.82f), P(0.15f, 0.90f, 1.35f), u);
                },
                38f);
            yield return At(8.5f);
            if (m_skipped) { yield return FinishOpening(); yield break; }

            // 3. 部員たち → 4. 暗転 → タイトル
            SetCaption(false);
            SetCinema(true);
            SetShot(lt => P(0.1f, 1.85f - lt * 0.03f, -4.4f + lt * 0.12f), lt => P(0f, 0.5f, 2.2f), 43f);
            yield return At(12.5f);
            if (m_skipped) { yield return FinishOpening(); yield break; }

            yield return FadeTo(0.65f, 0.5f);
            var card = (RectTransform)m_titleGroup.transform;
            card.localScale = Vector3.one * 1.06f;
            SetTitle(true);
            yield return Tween(0.8f, u => card.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, u));
            yield return Hold(1.4f);
            SetTitle(false);
            if (m_skipped) { yield return FinishOpening(); yield break; }

            yield return FinishOpening();
        }

        IEnumerator FinishOpening()
        {
            m_opening = false;
            if (m_slowMo != null) { StopCoroutine(m_slowMo); m_slowMo = null; }
            SetHint(false);
            SetCaption(false);
            SetTitle(false);
            SetMemory(false);
            SetCinema(false);
            m_baseScale = 1f;
            m_advance = false;
            WasSkipped = m_skipped;
            yield return FadeTo(1f, 0.35f);
            ShowConversation(false);
            yield return FadeTo(0f, 0.6f);
            m_skipped = false;
        }

        //========================================
        // 夜の部室カットシーン（第3章：準決勝前夜）
        //========================================

        public IEnumerator PlayNightRoomIntro(StoryChapter chapter)
        {
            m_time = 0f;
            m_shift = 0f;
            m_skipped = false;
            WasSkipped = false;
            m_advance = false;
            m_baseScale = 1f;
            m_opening = true;
            m_stageMode = false;
            m_venueMode = false;
            m_finalMode = false;
            m_ch4FinalMode = false;
            m_arenaMode = false;
            m_nightRoomMode = true;
            m_finalScene = "";
            StopDialogueFade();

            Show();
            UseRoomCamera();
            SetNightRoom(true);
            if (m_doll != null) m_doll.gameObject.SetActive(false);
            if (m_messenger != null) m_messenger.gameObject.SetActive(false);
            if (m_longShadow != null) m_longShadow.gameObject.SetActive(false);
            SetRoomMembersVisible(true);
            m_darkMood = false;
            m_fadeGroup.alpha = 1f;
            SetCaption(false);
            SetTitle(false);
            SetHint(true);
            SetMemory(false);
            SetCinema(true);
            string caption = chapter != null && chapter.cutscene != null ? chapter.cutscene.caption : "準決勝前夜 ― 部室";
            SetCaptionText(caption);

            ApplyConversationShot("S_ROOM_WIDE");
            yield return FadeTo(0f, 0.5f);
            SetCaption(!string.IsNullOrEmpty(caption));

            var shots = chapter != null && chapter.cutscene != null ? chapter.cutscene.shots : null;
            if (shots == null || shots.Count == 0)
            {
                ApplyConversationShot("S_ROOM_WIDE");
                yield return Hold(3.0f);
                if (!m_skipped) { ApplyConversationShot("S_OLD_HAMMER"); yield return Hold(3.0f); }
            }
            else
            {
                foreach (var shot in shots)
                {
                    if (shot == null) continue;
                    string view = shot.view ?? "";
                    if (view == "old_hammer" || view == "S_OLD_HAMMER")
                        ApplyConversationShot("S_OLD_HAMMER");
                    else
                        ApplyConversationShot("S_ROOM_WIDE");
                    yield return Hold(Mathf.Max(0.5f, shot.seconds));
                    if (m_skipped) break;
                }
            }

            SetCaption(false);
            if (!m_skipped)
            {
                yield return FadeTo(0.65f, 0.5f);
                var card = (RectTransform)m_titleGroup.transform;
                card.localScale = Vector3.one * 1.06f;
                SetTitle(true);
                yield return Tween(0.8f, u => card.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, u));
                yield return Hold(1.4f);
                SetTitle(false);
            }

            yield return FinishNightRoomIntro();
        }

        IEnumerator FinishNightRoomIntro()
        {
            m_opening = false;
            SetHint(false);
            SetCaption(false);
            SetTitle(false);
            SetMemory(false);
            SetCinema(false);
            m_baseScale = 1f;
            m_advance = false;
            WasSkipped = m_skipped;
            yield return FadeTo(1f, 0.35f);
            ShowNightRoomConversation();
            yield return FadeTo(0f, 0.6f);
            m_skipped = false;
        }

        public void ShowNightRoomConversation()
        {
            Show();
            StopEmptyRoomSequence();
            StopDialogueFade();
            m_blendDuration = 0f;
            m_dollShakeStart = -1f;
            m_stageMode = false;
            m_venueMode = false;
            m_finalMode = false;
            m_ch4FinalMode = false;
            m_arenaMode = false;
            m_nightRoomMode = true;
            m_opening = false;
            m_advance = false;
            m_baseScale = 1f;
            UseRoomCamera();
            SetNightRoom(true);
            if (m_doll != null) m_doll.gameObject.SetActive(false);
            if (m_messenger != null) m_messenger.gameObject.SetActive(false);
            if (m_longShadow != null) m_longShadow.gameObject.SetActive(false);
            m_darkMood = false;
            SetRoomMembersVisible(true);
            SetCinema(true);
            SetCaption(false);
            SetTitle(false);
            SetHint(false);

            string initialShot = "S_ROOM_WIDE";
            var lines = m_chapter != null ? m_chapter.before : null;
            if (lines != null)
            {
                foreach (var l in lines)
                {
                    if (l == null) continue;
                    if (!string.IsNullOrEmpty(l.shot) && m_shots.ContainsKey(l.shot)) initialShot = l.shot;
                    break;
                }
            }
            ApplyConversationShot(initialShot);
        }

        //========================================
        // 実際のステージを撮るカットシーン（第1章以降）
        //========================================

        // 章データの cutscene.shots を順に撮る。A：つぎへ／B：スキップは序章と同じ。
        public IEnumerator PlayStageIntro(StoryChapter chapter)
        {
            if (chapter == null || chapter.cutscene == null || string.IsNullOrEmpty(chapter.cutscene.type))
            {
                yield return PlayOpening();
                yield break;
            }

            m_time = 0f;
            m_shift = 0f;
            m_skipped = false;
            WasSkipped = false;
            m_advance = false;
            m_baseScale = 1f;
            m_opening = true;
            m_stageMode = true;
            Show();
            m_camera.clearFlags = CameraClearFlags.Skybox;
            m_camera.farClipPlane = 120f;
            m_fadeGroup.alpha = 1f;
            SetCaption(false);
            SetTitle(false);
            SetHint(true);
            SetMemory(false);
            SetCinema(false);
            SetCaptionText(chapter.cutscene.caption);

            // フェードインの前から、最初のカットにしておく（原点の映像を見せない）
            SetStageWide();
            yield return FadeTo(0f, 0.5f);
            SetCaption(!string.IsNullOrEmpty(chapter.cutscene.caption));

            var shots = chapter.cutscene.shots;
            if (shots == null || shots.Count == 0)
            {
                SetStageWide();
                yield return Hold(3.0f);
                if (!m_skipped) { BeginRunner(chapter, 4.5f); SetStageRunner(); yield return Hold(4.5f); }
                if (!m_skipped) { SetStageRival(chapter); yield return Hold(3.0f); }
            }
            else
            {
                foreach (var shot in shots)
                {
                    if (shot == null) continue;
                    switch (shot.view)
                    {
                        case "runner":
                            BeginRunner(chapter, shot.seconds);
                            SetStageRunner();
                            break;
                        case "swing":
                            BeginSwinger(chapter);
                            SetStageSwinger();
                            break;
                        case "rival":
                            SetStageRival(chapter);
                            break;
                        default:
                            SetStageWide();
                            break;
                    }
                    yield return Hold(Mathf.Max(0.5f, shot.seconds));
                    if (m_skipped) break;
                }
            }

            SetCaption(false);
            if (!m_skipped)
            {
                SetCinema(true);
                yield return FadeTo(0.65f, 0.5f);
                var card = (RectTransform)m_titleGroup.transform;
                card.localScale = Vector3.one * 1.06f;
                SetTitle(true);
                yield return Tween(0.8f, u => card.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, u));
                yield return Hold(1.4f);
                SetTitle(false);
            }

            yield return FinishStageIntro();
        }

        IEnumerator FinishStageIntro()
        {
            m_opening = false;
            SetHint(false);
            SetCaption(false);
            SetTitle(false);
            SetMemory(false);
            SetCinema(false);
            m_baseScale = 1f;
            m_advance = false;
            WasSkipped = m_skipped;
            RestoreRunner();
            yield return FadeTo(1f, 0.35f);
            ShowStageConversation(false);
            yield return FadeTo(0f, 0.6f);
            m_skipped = false;
        }

        //========================================
        // 全国大会会場（鳥居の前）のカットシーン（第2章以降）
        //========================================

        public IEnumerator PlayVenueIntro(StoryChapter chapter)
        {
            BuildVenue();
            // カットシーンでは七転高校の4人だけを映す（へびだるは会話から）
            if (m_venueMembers.TryGetValue("hebidaru", out var hebiMember)
                && hebiMember != null && hebiMember.root != null)
                hebiMember.root.gameObject.SetActive(false);
            m_time = 0f;
            m_shift = 0f;
            m_skipped = false;
            WasSkipped = false;
            m_advance = false;
            m_baseScale = 1f;
            m_opening = true;
            m_stageMode = false;
            m_venueMode = true;
            Show();
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = new Color(0.55f, 0.74f, 0.94f);
            m_camera.farClipPlane = 120f;
            m_fadeGroup.alpha = 1f;
            SetCaption(false);
            SetTitle(false);
            SetHint(true);
            SetMemory(false);
            SetCinema(false);
            SetCaptionText(chapter != null && chapter.cutscene != null ? chapter.cutscene.caption : null);

            // フェードインの前から、最初のカットにしておく
            SetVenueWide();
            yield return FadeTo(0f, 0.5f);
            SetCaption(!string.IsNullOrEmpty(chapter != null && chapter.cutscene != null ? chapter.cutscene.caption : null));

            var shots = chapter != null && chapter.cutscene != null ? chapter.cutscene.shots : null;
            if (shots == null || shots.Count == 0)
            {
                SetVenueWide();
                yield return Hold(3.4f);
                if (!m_skipped) { SetVenueGroup(); yield return Hold(4.8f); }
            }
            else
            {
                foreach (var shot in shots)
                {
                    if (shot == null) continue;
                    if (shot.view == "group") SetVenueGroup();
                    else SetVenueWide();
                    yield return Hold(Mathf.Max(0.5f, shot.seconds));
                    if (m_skipped) break;
                }
            }

            SetCaption(false);
            if (!m_skipped)
            {
                SetCinema(true);
                yield return FadeTo(0.65f, 0.5f);
                var card = (RectTransform)m_titleGroup.transform;
                card.localScale = Vector3.one * 1.06f;
                SetTitle(true);
                yield return Tween(0.8f, u => card.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, u));
                yield return Hold(1.4f);
                SetTitle(false);
            }

            yield return FinishVenueIntro();
        }

        IEnumerator FinishVenueIntro()
        {
            m_opening = false;
            SetHint(false);
            SetCaption(false);
            SetTitle(false);
            SetMemory(false);
            SetCinema(false);
            m_baseScale = 1f;
            m_advance = false;
            WasSkipped = m_skipped;
            yield return FadeTo(1f, 0.35f);
            ShowVenueConversation(false);
            yield return FadeTo(0f, 0.6f);
            m_skipped = false;
        }

        // 会話の背景にする（4人＋看板。話者に合わせてカメラを寄せる）
        public void ShowVenueConversation(bool after, bool sunset = false, bool hideHebidaru = false)
        {
            BuildVenue();
            Show();
            StopDialogueFade();
            // 会話では、へびだるも姿を見せる（第3章の同校対決など不要な章では隠す）
            if (m_venueMembers.TryGetValue("hebidaru", out var hebiMember)
                && hebiMember != null && hebiMember.root != null)
                hebiMember.root.gameObject.SetActive(!hideHebidaru);
            m_stageMode = false;
            m_finalMode = false;
            m_nightRoomMode = false;
            m_venueMode = true;
            m_opening = false;
            m_advance = false;
            m_baseScale = 1f;
            SetNightRoom(false);
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = sunset
                ? new Color(0.76f, 0.46f, 0.30f)
                : new Color(0.55f, 0.74f, 0.94f);
            m_camera.farClipPlane = 120f;
            if (m_venueSky != null)
                m_venueSky.GetComponent<Renderer>().sharedMaterial = sunset ? m_skySunset : m_skyDay;
            SetSunset(sunset);
            SetCinema(true);
            SetCaption(false);
            SetTitle(false);
            SetHint(false);
            SetVenueGroup();
        }

        // 話者にカメラを寄せる（見つからなければ4人の引き画）
        public void FocusVenueSpeaker(string speakerId)
        {
            if (!m_venueMode || !m_visible || m_venueRoot == null) return;
            if (string.IsNullOrEmpty(speakerId) || !m_venueMembers.TryGetValue(speakerId, out var member)
                || member == null || member.root == null)
            {
                SetVenueGroup();
                return;
            }
            Transform t = member.root;
            // 会話ウィンドウ（画面下）に顔がかからないよう、顔を画面の上寄りに置く
            SetShot(lt => t.position + new Vector3(0f, 1.35f, -3.0f),
                    lt => t.position + new Vector3(0f, 1.0f, 0f), 40f);
        }

        void SetVenueWide()
        {
            SetShot(lt => V(0f, 3.0f, -11f), lt => V(0f, 2.4f, 1.5f), 50f);
        }

        void SetVenueGroup()
        {
            // 顔が会話ウィンドウより上に来るよう、少し下を注視する
            SetShot(lt => V(0f, 1.7f, -6.2f), lt => V(0f, 0.95f, 0.3f), 45f);
        }

        //========================================
        // 最終章：控室（部室セット）→ 決勝の舞台
        //========================================

        Transform Prop(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, float yaw = 0f)
        {
            var t = ToyKit.Primitive(PrimitiveType.Cube, name, parent);
            t.localPosition = localPos;
            t.localScale = size;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            t.GetComponent<Renderer>().sharedMaterial = mat;
            return t;
        }

        void UseRoomCamera()
        {
            if (m_camera == null) return;
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = new Color(0.09f, 0.07f, 0.08f);
            m_camera.farClipPlane = 120f;
        }

        void UseStageCamera()
        {
            if (m_camera == null) return;
            m_camera.clearFlags = CameraClearFlags.Skybox;
            m_camera.farClipPlane = 120f;
        }

        Member RoomMember(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return m_roomMembers.TryGetValue(id, out var m) && m != null && m.root != null ? m : null;
        }

        void PlaceRoomMember(string id, Vector3 localPos, float yaw)
        {
            var m = RoomMember(id);
            if (m == null) return;
            m.basePos = localPos;
            m.root.localPosition = localPos;
            m.root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void HideRoomMember(string id)
        {
            var m = RoomMember(id);
            if (m != null) m.root.gameObject.SetActive(false);
        }

        // 相手2人（いわ・へび）が待っている画
        void SetStageRivals(StoryChapter chapter)
        {
            UseStageCamera();
            string firstId = chapter != null && chapter.cutscene != null ? chapter.cutscene.rival : null;
            var cpu = chapter != null ? chapter.cpu : null;
            if (string.IsNullOrEmpty(firstId) && cpu != null && cpu.Count > 0) firstId = cpu[0].character;
            // 2人目は、章データの cpu の並びで rival の次の人（第1章の決勝なら、いわ→へび）
            string secondId = null;
            if (cpu != null && cpu.Count > 1)
            {
                for (int i = 0; i < cpu.Count; i++)
                {
                    if (cpu[i] == null || cpu[i].character != firstId) continue;
                    secondId = cpu[(i + 1) % cpu.Count].character;
                    break;
                }
            }
            var a = FindPlayer(firstId);
            var b = FindPlayer(secondId);
            if (a == null || a.Character == null || b == null || b.Character == null || b == a) { SetStageWide(); return; }

            Vector3 pa = a.Character.transform.position;
            Vector3 pb = b.Character.transform.position;
            Vector3 mid = (pa + pb) * 0.5f;
            Vector3 toCenter = W(0f, 0f, 0f) - mid;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.01f) toCenter = Vector3.forward;
            toCenter.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, toCenter).normalized;
            // 2人の間が離れていても、両方が入る距離にする
            float separation = Vector3.Distance(pa, pb);
            float distance = Mathf.Clamp(separation * 0.8f + 3.5f, 6f, 15f);
            Vector3 cameraPos = mid + toCenter * distance + side * (distance * 0.3f)
                + Vector3.up * (2.4f + distance * 0.12f);
            var match = Nakahira.DarumaMatch.Current;
            if (match != null && match.StageArea != null)
            {
                Vector3 clamped = match.StageArea.Clamp(cameraPos, 1.5f);
                clamped.y = cameraPos.y;
                cameraPos = clamped;
            }
            SetShot(lt => cameraPos, lt => mid + Vector3.up * 1.0f, 45f);
        }

        void ApplyFinalShot(StoryChapter chapter, StoryShot shot)
        {
            switch (shot.view)
            {
                case "faceoff":
                    SetStageFaceoff(chapter);
                    break;
                case "rivals":
                    SetStageRivals(chapter);
                    break;
                case "rival":
                    UseStageCamera();
                    SetStageRival(chapter);
                    break;
                case "runner":
                    UseStageCamera();
                    BeginRunner(chapter, shot.seconds);
                    SetStageRunner();
                    break;
                default:
                    UseStageCamera();
                    SetStageWide();
                    break;
            }
            string caption = string.IsNullOrEmpty(shot.caption)
                ? (chapter != null && chapter.cutscene != null ? chapter.cutscene.caption : null)
                : shot.caption;
            SetCaptionText(caption);
            SetCaption(!string.IsNullOrEmpty(caption));
        }

        // 王座決定戦：あかだるとだるきんぐが1対1で向かい合う画
        void SetStageFaceoff(StoryChapter chapter)
        {
            UseStageCamera();
            string playerId = chapter != null && !string.IsNullOrEmpty(chapter.playerCharacter) ? chapter.playerCharacter : "akadaru";
            string rivalId = chapter != null && chapter.cutscene != null && !string.IsNullOrEmpty(chapter.cutscene.rival)
                ? chapter.cutscene.rival
                : (chapter != null && chapter.cpu != null && chapter.cpu.Count > 0 ? chapter.cpu[0].character : "daruking");

            var a = FindPlayer(playerId);
            var b = FindPlayer(rivalId);
            if (a == null || a.Character == null || b == null || b.Character == null || a == b)
            {
                SetStageWide();
                return;
            }

            Vector3 pa = a.Character.transform.position;
            Vector3 pb = b.Character.transform.position;
            Vector3 mid = (pa + pb) * 0.5f;
            Vector3 ab = pb - pa;
            ab.y = 0f;
            if (ab.sqrMagnitude < 0.01f) ab = Vector3.right;
            ab.Normalize();
            // 2人を結ぶ線に対して少し斜め横から捉える（両方が見栄えよく収まるアングル）
            Vector3 side = Vector3.Cross(Vector3.up, ab).normalized;
            if (side.z > 0f) side = -side;
            float separation = Vector3.Distance(pa, pb);
            float distance = Mathf.Clamp(separation * 0.85f + 3.2f, 5.5f, 14.5f);
            Vector3 cameraPos = mid + side * distance - ab * (distance * 0.16f) + Vector3.up * (2.1f + distance * 0.10f);
            var match = Nakahira.DarumaMatch.Current;
            if (match != null && match.StageArea != null)
            {
                Vector3 clamped = match.StageArea.Clamp(cameraPos, 1.2f);
                clamped.y = cameraPos.y;
                cameraPos = clamped;
            }
            SetShot(lt => cameraPos, lt => mid + Vector3.up * 1.05f, 44f);
        }

        // 王座決定戦のカットシーン（決勝の舞台全景 → 向かい合うあかだるとだるきんぐ）。A：つぎへ／B：スキップ
        public IEnumerator PlayFinalIntro(StoryChapter chapter)
        {
            BuildCh4FinalsSet();
            m_time = 0f;
            m_shift = 0f;
            m_skipped = false;
            WasSkipped = false;
            m_advance = false;
            m_baseScale = 1f;
            m_opening = true;
            m_stageMode = true;
            m_venueMode = false;
            m_ch4FinalMode = false;
            m_arenaMode = false;
            m_nightRoomMode = false;
            m_finalMode = true;
            m_finalScene = "stage";
            StopDialogueFade();
            Show();
            UseStageCamera();
            SetDarkMood(false);
            SetCh4FinalsMood(false);
            m_fadeGroup.alpha = 1f;
            SetCaption(false);
            SetTitle(false);
            SetHint(true);
            SetMemory(false);
            SetCinema(false);
            SetStageWide();
            string defaultCaption = chapter != null && chapter.cutscene != null && !string.IsNullOrEmpty(chapter.cutscene.caption)
                ? chapter.cutscene.caption
                : "王座決定戦";
            SetCaptionText(defaultCaption);
            yield return FadeTo(0f, 0.5f);
            SetCaption(!string.IsNullOrEmpty(defaultCaption));

            var shots = chapter != null && chapter.cutscene != null ? chapter.cutscene.shots : null;
            if (shots == null || shots.Count == 0)
            {
                UseStageCamera();
                SetStageWide();
                yield return Hold(3.0f);
                if (!m_skipped) { SetStageFaceoff(chapter); yield return Hold(3.4f); }
            }
            else
            {
                foreach (var shot in shots)
                {
                    if (shot == null) continue;
                    ApplyFinalShot(chapter, shot);
                    yield return Hold(Mathf.Max(0.5f, shot.seconds));
                    if (m_skipped) break;
                }
            }

            SetCaption(false);
            if (!m_skipped)
            {
                SetCinema(true);
                yield return FadeTo(0.65f, 0.5f);
                var card = (RectTransform)m_titleGroup.transform;
                card.localScale = Vector3.one * 1.06f;
                SetTitle(true);
                yield return Tween(0.8f, u => card.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, u));
                yield return Hold(1.4f);
                SetTitle(false);
            }

            yield return FinishFinalIntro();
        }

        IEnumerator FinishFinalIntro()
        {
            m_opening = false;
            SetHint(false);
            SetCaption(false);
            SetTitle(false);
            SetMemory(false);
            SetCinema(false);
            m_baseScale = 1f;
            m_advance = false;
            WasSkipped = m_skipped;
            RestoreRunner();
            yield return FadeTo(1f, 0.35f);
            ShowFinalConversation(true, false);
            yield return FadeTo(0f, 0.6f);
            m_skipped = false;
        }

        // 最終章の会話の背景。stageSide=true で王座決定戦の舞台、false で部室セットへ切り替える
        public void ShowFinalConversation(bool stageSide, bool sunset = false)
        {
            BuildCh4FinalsSet();
            Show();
            StopDialogueFade();
            m_finalMode = true;
            m_venueMode = false;
            m_ch4FinalMode = false;
            m_arenaMode = false;
            m_nightRoomMode = false;
            m_stageMode = stageSide;
            m_finalScene = stageSide ? "stage" : "room";
            m_opening = false;
            m_advance = false;
            m_baseScale = 1f;
            SetDarkMood(false);
            SetNightRoom(false);
            SetCinema(true);
            SetCaption(false);
            SetTitle(false);
            SetHint(false);
            if (m_doll != null) m_doll.gameObject.SetActive(false);

            if (stageSide)
            {
                UseStageCamera();
                SetCh4FinalsMood(sunset);
                SetStageFaceoff(m_chapter);
            }
            else
            {
                UseRoomCamera();
                SetSunset(sunset);
                SetRoomMembersVisible(true);
                ApplyConversationShot("S_ROOM_WIDE");
            }
        }

        // 舞台 ⇔ 部室。暗転して切り替える
        public IEnumerator SwitchFinalScene(string scene, float fade = 0.4f)
        {
            if (scene == m_finalScene) yield break;
            yield return FadeTo(1f, fade);
            if (scene == "stage") ShowFinalConversation(true);
            else ShowFinalConversation(false);
            yield return FadeTo(0f, fade + 0.15f);
        }

        // 会話の1行が始まったときのカメラ処理。
        //  ・行に shot（ショットID）が指定されていれば、そのショットへ切り替える
        //  ・会場モード（m_venueMode）中に話者ショット（S_AKA / S_AO / S_MI / S_KIDARU）が指定された場合は会場側の該当キャラへ寄せる
        //  ・cue が指定されていれば、プロローグ／通常会話ともに暗転・明転などの演出キューを実行する
        //  ・shot が空なら、第2章・最終章は従来どおり話者へ寄せ、序章・第1章は直前のショットを維持する
        public void OnDialogueLine(StoryLine line)
        {
            if (!m_visible) return;
            // 前の行の未発火な遅延ワンショットVFXは、次の行へ進んだ時点でキャンセルする
            if (m_vfxPlayer != null) m_vfxPlayer.CancelPendingDelayed();

            // プロローグ等で fx_fade が vfxCues に指定されている場合は、既存の m_fadeGroup 暗転を委譲して二重暗転を防ぐ
            if (m_arenaMode && HasFadeVfxCue(line))
            {
                m_preludeFadeDelegatedToVfx = true;
            }

            OnDialogueLineCore(line);

            if (line != null && !string.IsNullOrEmpty(line.cue))
            {
                TryDispatchVfxCue(line.cue);
            }
            if (line != null && line.vfxCues != null && line.vfxCues.Count > 0)
            {
                DispatchStructuredVfxCues(line, "start");
            }
        }

        // 行の文字送り完了時に呼ぶ（vfxCues の timing="end" を発火）
        public void OnDialogueLineTextCompleted(StoryLine line)
        {
            if (!m_visible || line == null || line.vfxCues == null || line.vfxCues.Count == 0) return;
            DispatchStructuredVfxCues(line, "end");
        }

        // Bスキップ時：未発火の遅延キューと再生中エフェクトを止め、暗転が残っていれば解除する
        public void OnDialogueSkipped()
        {
            if (m_preludeBlackoutCleanupRoutine != null)
            {
                StopCoroutine(m_preludeBlackoutCleanupRoutine);
                m_preludeBlackoutCleanupRoutine = null;
            }
            if (m_vfxPlayer != null)
            {
                m_vfxPlayer.CancelPendingDelayed();
                m_vfxPlayer.ClearFadeIfActive(0.2f);
            }
        }

        // 会話終了後の後始末（未発火の遅延キャンセル＋残存暗転・パーティクルの整理）
        public void CleanupVfxAfterDialogue(bool clearImmediate = false)
        {
            if (m_preludeBlackoutCleanupRoutine != null)
            {
                StopCoroutine(m_preludeBlackoutCleanupRoutine);
                m_preludeBlackoutCleanupRoutine = null;
            }
            if (m_vfxPlayer != null)
            {
                m_vfxPlayer.StopAll(clearImmediate);
            }
            m_preludeFadeDelegatedToVfx = false;
        }

        public void SetVfxPaused(bool paused)
        {
            if (m_vfxPlayer != null) m_vfxPlayer.SetPaused(paused);
        }

        StoryEffectPlayer EnsureVfxPlayer()
        {
            if (m_vfxPlayer == null)
            {
                m_vfxPlayer = StoryEffectPlayer.Create(transform, this);
            }
            return m_vfxPlayer;
        }

        static bool HasFadeVfxCue(StoryLine line)
        {
            if (line == null || line.vfxCues == null) return false;
            for (int i = 0; i < line.vfxCues.Count; i++)
            {
                var c = line.vfxCues[i];
                if (c == null) continue;
                string act = string.IsNullOrEmpty(c.action) ? "play" : c.action.Trim().ToLowerInvariant();
                if (act == "fade_in" || act == "fade_out") return true;
                if (!string.IsNullOrEmpty(c.key) && (c.key == "fx_fade" || c.key == "vfx:fx_fade")) return true;
            }
            return false;
        }

        void DispatchStructuredVfxCues(StoryLine line, string targetTiming)
        {
            if (line == null || line.vfxCues == null) return;
            var player = EnsureVfxPlayer();
            for (int i = 0; i < line.vfxCues.Count; i++)
            {
                var c = line.vfxCues[i];
                if (c == null) continue;
                string timing = string.IsNullOrEmpty(c.timing) ? "start" : c.timing.Trim().ToLowerInvariant();
                if (timing != targetTiming) continue;
                player.ExecuteCue(c, ResolveVfxPosition);
            }
        }

        // 汎用位置解決：
        //  1) cue.useScreenPos == true → 画面正規化座標（左下0,0〜右上1,1）をカメラ前方へ投影
        //  2) 章データ vfxAnchors に同名定義があり useScreenPos == true → 正規化座標を投影（静止画差し替え用）
        //  3) 3Dセットの名前付きアンカー（king_blocks / stage_center 等）＋ offset
        Vector3 ResolveVfxPosition(StoryVfxCue cue)
        {
            if (cue == null) return ResolveCurrentVfxWorldPos();

            if (cue.useScreenPos && m_camera != null)
            {
                float depth = cue.screenDepth > 0.1f ? cue.screenDepth : 6.0f;
                return m_camera.ViewportToWorldPoint(new Vector3(cue.screenX, cue.screenY, depth));
            }

            StoryVfxAnchor chapterAnchor = m_chapter != null ? m_chapter.FindVfxAnchor(cue.anchor) : null;
            if (chapterAnchor != null && chapterAnchor.useScreenPos && m_camera != null)
            {
                float depth = chapterAnchor.screenDepth > 0.1f ? chapterAnchor.screenDepth : 6.0f;
                return m_camera.ViewportToWorldPoint(new Vector3(chapterAnchor.screenX, chapterAnchor.screenY, depth)) + cue.offset;
            }

            Vector3 basePos = ResolveNamedAnchorWorldPos(cue.anchor);
            if (chapterAnchor != null) basePos += chapterAnchor.offset;
            return basePos + cue.offset;
        }

        Vector3 ResolveNamedAnchorWorldPos(string anchorName)
        {
            if (string.IsNullOrEmpty(anchorName)) return ResolveCurrentVfxWorldPos();
            switch (anchorName.Trim())
            {
                case "king_blocks":
                    return ArenaOrigin + new Vector3(2.3f, 0.5f, 0f);
                case "stage_center":
                    return m_arenaMode
                        ? ArenaOrigin + new Vector3(-0.15f, 0.22f, 0.15f)
                        : Vector3.zero;
                case "arena_center":
                    return ArenaOrigin + new Vector3(0f, 0.5f, 0f);
                case "room_center":
                    return SetOrigin + new Vector3(0f, 1.0f, 1.2f);
                case "camera_look":
                    return ResolveCurrentVfxWorldPos();
                default:
                    Debug.LogWarning($"[StoryVfx] 未定義のアンカー名 '{anchorName}' です。現在のカメラ注視点を使用します。");
                    return ResolveCurrentVfxWorldPos();
            }
        }

        // 台本の cue に含まれる vfx:<key> / vfx_stop:<key> / vfx_stop_all を再生・停止する。
        // 既存の cue（blackout / tower_collapse 等）には一切影響しない。
        void TryDispatchVfxCue(string cue)
        {
            if (string.IsNullOrEmpty(cue) || !cue.Contains("vfx")) return;
            Vector3 spawnPos = ResolveCurrentVfxWorldPos();
            EnsureVfxPlayer().TryHandleCue(cue, null, spawnPos);
        }

        Vector3 ResolveCurrentVfxWorldPos()
        {
            if (m_camLook != null)
            {
                return m_camLook(Mathf.Max(0f, m_time - m_shotStart));
            }
            if (m_camera != null)
            {
                return m_camera.transform.position + m_camera.transform.forward * 3.5f;
            }
            return SetOrigin;
        }

        void OnDialogueLineCore(StoryLine line)
        {
            if (m_ch4FinalMode)
            {
                HandleCh4DialogueLine(line);
                return;
            }
            if (m_finalMode)
            {
                HandleFinalDialogueLine(line);
                return;
            }
            if (line != null && !string.IsNullOrEmpty(line.shot))
            {
                if (m_venueMode)
                {
                    if (TryResolveSpeakerFromShot(line.shot, out string shotSpeaker))
                        FocusVenueSpeaker(shotSpeaker);
                    else
                        FocusVenueSpeaker(line.speaker);
                }
                else
                {
                    ApplyConversationShot(line.shot);
                }
                if (!string.IsNullOrEmpty(line.cue))
                {
                    if (m_arenaMode) ApplyPreludeCue(line.cue);
                    else ApplyDialogueCue(line.cue);
                }
                return;
            }
            if (line != null && !string.IsNullOrEmpty(line.cue))
            {
                if (m_arenaMode) ApplyPreludeCue(line.cue);
                else ApplyDialogueCue(line.cue);
                if (m_arenaMode) return;
            }
            if (m_venueMode || m_finalMode)
            {
                FocusSpeaker(line != null ? line.speaker : null);
            }
        }

        static bool TryResolveSpeakerFromShot(string shotId, out string speakerId)
        {
            switch (shotId)
            {
                case "S_AKA": speakerId = "akadaru"; return true;
                case "S_AO": speakerId = "aodaru"; return true;
                case "S_MI": speakerId = "midaru"; return true;
                case "S_KIDARU": speakerId = "kidaru"; return true;
                default: speakerId = null; return false;
            }
        }

        void StopDialogueFade()
        {
            if (m_dialogueFadeRoutine != null)
            {
                StopCoroutine(m_dialogueFadeRoutine);
                m_dialogueFadeRoutine = null;
            }
        }

        void ApplyDialogueCue(string cue)
        {
            switch (cue)
            {
                case "blackout":
                    StopDialogueFade();
                    m_dialogueFadeRoutine = StartCoroutine(FadeDialogueTo(1f, 0.45f));
                    break;

                case "keep_blackout":
                    StopDialogueFade();
                    if (m_fadeGroup != null) m_fadeGroup.alpha = 1f;
                    break;

                case "lights_on":
                case "fade_in":
                case "lights_on_hammer":
                    StopDialogueFade();
                    m_dialogueFadeRoutine = StartCoroutine(FadeDialogueTo(0f, 0.55f));
                    break;
            }
        }

        IEnumerator FadeDialogueTo(float target, float duration)
        {
            yield return FadeTo(target, duration);
            m_dialogueFadeRoutine = null;
        }

        // 会話の話者へカメラを寄せる（章ごとの背景に合わせる）
        public void FocusSpeaker(string speakerId)
        {
            if (m_venueMode) { FocusVenueSpeaker(speakerId); return; }
            if (!m_finalMode || !m_visible) return;
            if (m_finalScene == "stage") FocusStageSpeaker(speakerId);
            else FocusRoomSpeaker(speakerId);
        }

        // 決勝の舞台：実際のキャラクターの顔が見える位置から撮る
        void FocusStageSpeaker(string speakerId)
        {
            var p = FindPlayer(speakerId);
            if (p == null || p.Character == null)
            {
                SetShot(lt => W(0f, 3.0f, -8.0f), lt => W(0f, 1.3f, 0f), 50f);
                return;
            }
            Transform t = p.Character.transform;
            Vector3 basePos = t.position;
            Vector3 forward = t.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.back;
            forward.Normalize();
            Vector3 cameraPos = basePos + forward * 3.2f + Vector3.up * 2.05f;
            // 壁の外へ出ると背景だけになるので、ステージの中へ収める。
            // 大きく動かすことになる（壁を背にしている）ときは、引きの画にする
            var match = Nakahira.DarumaMatch.Current;
            if (match != null && match.StageArea != null)
            {
                Vector3 clamped = match.StageArea.Clamp(cameraPos, 1.2f);
                clamped.y = cameraPos.y;
                if ((clamped - cameraPos).sqrMagnitude > 0.04f)
                {
                    SetShot(lt => W(0f, 3.0f, -8.0f), lt => W(0f, 1.3f, 0f), 50f);
                    return;
                }
                cameraPos = clamped;
            }
            // 会話ウィンドウ（画面下）に顔がかからないよう、少し下を注視する
            SetShot(lt => cameraPos, lt => basePos + Vector3.up * 1.10f, 40f);
        }

        // 部室：部室セットの部員の顔が見える位置から撮る
        void FocusRoomSpeaker(string speakerId)
        {
            var member = RoomMember(speakerId);
            if (member == null) { ApplyConversationShot("S_ROOM_WIDE"); return; }
            Transform t = member.root;
            Vector3 basePos = t.position;
            Vector3 forward = t.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.back;
            forward.Normalize();
            Vector3 cameraPos = basePos + forward * 2.7f + Vector3.up * 1.72f;
            // 壁の外へ出ないように、部屋の中へ収める
            Vector3 local = cameraPos - SetOrigin;
            local.x = Mathf.Clamp(local.x, -5.4f, 5.4f);
            local.z = Mathf.Clamp(local.z, -4.4f, 4.4f);
            cameraPos = SetOrigin + local;
            SetShot(lt => cameraPos, lt => basePos + Vector3.up * 1.02f, 38f);
        }

        //========================================
        // 横断幕（最終章・勝利後）
        //========================================

        void BannerBox(string name, Vector3 pos, Vector3 size, Material mat)
        {
            var t = ToyKit.Primitive(PrimitiveType.Cube, name, m_bannerRoot);
            t.localPosition = pos;
            t.localScale = size;
            t.GetComponent<Renderer>().sharedMaterial = mat;
        }

        void BuildBanner(string text)
        {
            if (m_bannerBuilt)
            {
                if (m_bannerText != null && m_bannerText.text != text) m_bannerText.text = text;
                return;
            }
            m_bannerBuilt = true;
            m_bannerRoot = new GameObject("StoryBanner").transform;

            var poleMat = ToyKit.Lit(new Color(0.45f, 0.42f, 0.4f), 0.3f);
            BannerBox("PoleL", new Vector3(-2.95f, 2.6f, 7.7f), new Vector3(0.16f, 5.2f, 0.16f), poleMat);
            BannerBox("PoleR", new Vector3(2.95f, 2.6f, 7.7f), new Vector3(0.16f, 5.2f, 0.16f), poleMat);
            BannerBox("Rod", new Vector3(0f, 5.15f, 7.7f), new Vector3(6.2f, 0.12f, 0.12f), poleMat);
            m_bannerCloth = TitleDressingKit.Quad(m_bannerRoot, "Cloth", W(0f, 4.52f, 7.66f),
                new Vector3(5.7f, 1.2f, 1f), Unlit(new Color(0.78f, 0.13f, 0.10f)));

            m_bannerText = TitleDressingKit.Text(m_bannerRoot, PresentationSettings.DefaultFont, text,
                W(0f, 4.52f, 7.60f), new Vector2(5.2f, 1.0f), 4.2f,
                new Color(1f, 0.96f, 0.86f), new Color(0.32f, 0.05f, 0.04f), 0.06f, 0f);
            m_bannerText.textWrappingMode = TextWrappingModes.Normal;
            m_bannerText.alignment = TextAlignmentOptions.Center;
            m_bannerText.lineSpacing = 4f;
        }

        // 横断幕を掲げる（上から垂れ下がる簡単な演出）。会話の最後のト書きに合わせて呼ぶ
        public void ShowBanner(string text, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            BuildBanner(text);
            if (m_bannerRoot != null) m_bannerRoot.gameObject.SetActive(true);
            if (m_bannerCloth != null) m_bannerCloth.localScale = new Vector3(1f, 0.04f, 1f);
            if (m_bannerText != null)
            {
                m_bannerText.text = text;
                var c = m_bannerText.color;
                c.a = 0f;
                m_bannerText.color = c;
            }
            m_bannerStart = m_time;
            m_bannerLength = 0.9f;
            m_bannerSeconds = Mathf.Max(1.5f, seconds);
            Show();
            UseStageCamera();
            SetShot(lt => W(0f, 4.4f, 1.6f), lt => W(0f, 4.75f, 7.7f), 42f);
        }

        void AnimateBanner()
        {
            if (m_bannerStart < 0f || m_bannerRoot == null) return;
            float u = Mathf.Clamp01((m_time - m_bannerStart) / m_bannerLength);
            float ease = 1f - Mathf.Pow(1f - u, 3f);
            if (m_bannerCloth != null)
                m_bannerCloth.localScale = new Vector3(1f, Mathf.Lerp(0.04f, 1f, ease), 1f);
            if (m_bannerText != null)
            {
                var c = m_bannerText.color;
                c.a = Mathf.SmoothStep(0f, 1f, ease);
                m_bannerText.color = c;
            }
            // 掲げたあとは、決めた時間だけゆっくり揺れる
            if (u >= 1f)
            {
                float elapsed = m_time - m_bannerStart;
                float sway = Mathf.Clamp01((m_bannerSeconds - elapsed) / 0.6f);
                m_bannerRoot.localRotation = Quaternion.Euler(0f, 0f, 0.5f * sway * Mathf.Sin(elapsed * 1.6f));
            }
        }

        // 会話の背景にする（実際のステージをゆっくり寄る）
        public void ShowStageConversation(bool after)
        {
            Show();
            m_stageMode = true;
            m_opening = false;
            m_advance = false;
            m_baseScale = 1f;
            m_camera.clearFlags = CameraClearFlags.Skybox;
            m_camera.farClipPlane = 120f;
            m_sunset = after;
            SetCinema(true);
            SetCaption(false);
            SetTitle(false);
            SetHint(false);

            if (after)
            {
                // 祝勝：引きの高い画にして、会話ウィンドウが顔にかからないようにする
                SetShot(lt => W(0f, 9.0f, -8.5f), lt => W(0f, 1.2f, 0f), 55f);
            }
            else
            {
                // 対戦前：盤面全体（左右の灰色の壁も見える高さ）を見渡す
                SetShot(lt => W(0f, 9.0f, -8.5f), lt => W(0f, 1.2f, 0f), 55f);
            }
        }

        public void SetCaptionText(string text)
        {
            if (m_caption != null) m_caption.text = text ?? "";
        }

        void SetStageWide()
        {
            // スタンドの中に入り込まないよう、競技場の内側から見下ろす
            SetShot(lt => W(0f, 15f, -8f), lt => W(0f, 1.2f, 0f), 50f);
        }

        void SetStageRunner()
        {
            Transform runner = m_runner;
            if (runner == null) { SetStageWide(); return; }
            // 走る人の顔が見えるよう、進行方向の前（北東）から斜めに撮る。全身が入る高さ・距離にする
            SetShot(lt => runner.position + new Vector3(1.8f, 2.6f, 3.0f),
                    lt => runner.position + new Vector3(0f, 1.0f, 0f), 45f);
        }

        // 黙々と素振りをするカット（第1章：あかだる）。中央側から斜め45度でハンマーの軌道と顔を見せる
        void SetStageSwinger()
        {
            Transform swinger = m_runner;
            if (swinger == null) { SetStageWide(); return; }
            Vector3 target = swinger.position;
            Vector3 toCenter = W(0f, 0f, 0f) - target;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.01f) toCenter = Vector3.forward;
            toCenter.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, toCenter);
            Vector3 viewDir = (toCenter + side * 0.75f).normalized;
            Vector3 cameraPos = target + viewDir * 4.6f + Vector3.up * 2.4f;
            SetShot(lt => cameraPos, lt => target + new Vector3(0f, 0.9f, 0f), 42f);
        }

        void BeginSwinger(StoryChapter chapter)
        {
            RestoreRunner();
            string id = chapter != null && chapter.cutscene != null && !string.IsNullOrEmpty(chapter.cutscene.runner)
                ? chapter.cutscene.runner : (chapter != null ? chapter.playerCharacter : null);
            var player = FindPlayer(id);
            if (player == null || player.Character == null) return;

            m_runner = player.Character.transform;
            m_runnerHome = m_runner.position;
            m_runnerHomeRot = m_runner.rotation;
            // ステージ中央側へ向けてその場で素振りさせる（初期位置のままなので床の段差でも浮き沈みしない）
            Vector3 toCenter = W(0f, 0f, 0f) - m_runnerHome;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 0.01f) m_runner.rotation = Quaternion.LookRotation(toCenter.normalized);

            m_swingerHammer = player.Character.GetComponentInChildren<Nakahira.DarumaHammer>(true);
            if (m_swingerHammer != null)
            {
                m_swingerHammerWasEnabled = m_swingerHammer.enabled;
                m_swingerHammerBasePos = m_swingerHammer.transform.localPosition;
                m_swingerHammerBaseRot = m_swingerHammer.transform.localRotation;
                m_swingerHammer.enabled = false;
            }
            m_runnerStart = m_time;
            m_swingerActive = true;
        }

        void SetStageRival(StoryChapter chapter)
        {
            var rival = FindPlayer(chapter.cutscene != null ? chapter.cutscene.rival : null);
            if (rival == null || rival.Character == null) rival = FindFirstCpu();
            Vector3 target = rival != null && rival.Character != null
                ? rival.Character.transform.position
                : W(0f, 0f, 0f);
            // 相手の顔が見えるよう、相手が向いている中央側から撮る。全身が入る高さ・距離にする
            Vector3 toCenter = W(0f, 0f, 0f) - target;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.01f) toCenter = Vector3.forward;
            toCenter.Normalize();
            // 真正面だと相手のハンマーが顔を隠すので、中央側から斜め 45 度へ回す
            Vector3 side = Vector3.Cross(Vector3.up, toCenter);
            Vector3 viewDir = (toCenter + side * 0.9f).normalized;
            Vector3 cameraPos = target + viewDir * 5.5f + Vector3.up * 3.0f;
            SetShot(lt => cameraPos, lt => target + new Vector3(0f, 1.0f, 0f), 45f);
        }

        // 走る人を、実際のキャラクターで走路に出す（終わったら元の位置へ戻す）
        void BeginRunner(StoryChapter chapter, float seconds)
        {
            RestoreRunner();
            string id = chapter.cutscene != null && !string.IsNullOrEmpty(chapter.cutscene.runner)
                ? chapter.cutscene.runner : chapter.playerCharacter;
            var player = FindPlayer(id);
            if (player == null || player.Character == null) return;

            m_runner = player.Character.transform;
            m_runnerHome = m_runner.position;
            m_runnerHomeRot = m_runner.rotation;
            m_runnerA = new Vector3(-12f, m_runnerHome.y, -5.5f);
            m_runnerB = new Vector3(12f, m_runnerHome.y, -5.5f);
            m_runner.position = m_runnerA;
            m_runner.rotation = Quaternion.LookRotation(Vector3.right);
            m_runnerStart = m_time;
            m_runnerDuration = Mathf.Max(1f, seconds);
            m_runnerActive = true;
        }

        void AnimateStage()
        {
            if (m_swingerActive && m_runner != null)
            {
                // 1.15秒周期で「構え → 鋭く薙ぎ払い → 戻り」を繰り返す素振りアニメーション
                float elapsed = Mathf.Max(0f, m_time - m_runnerStart);
                const float cycle = 1.15f;
                float phase = (elapsed % cycle) / cycle;
                float yaw;
                if (phase < 0.25f)
                {
                    float u = Mathf.SmoothStep(0f, 1f, phase / 0.25f);
                    yaw = Mathf.Lerp(90f, 112f, u);
                }
                else if (phase < 0.55f)
                {
                    float u = (phase - 0.25f) / 0.30f;
                    yaw = Mathf.Lerp(112f, -110f, Mathf.Pow(u, 1.8f));
                }
                else
                {
                    float u = Mathf.SmoothStep(0f, 1f, (phase - 0.55f) / 0.45f);
                    yaw = Mathf.Lerp(-110f, 90f, u);
                }
                if (m_swingerHammer != null)
                {
                    m_swingerHammer.transform.localPosition = new Vector3(0f, 0.28f, 0f);
                    m_swingerHammer.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, 90f);
                }
                return;
            }
            if (!m_runnerActive || m_runner == null) return;
            float cu = Mathf.Clamp01((m_time - m_runnerStart) / m_runnerDuration);
            Vector3 pos = Vector3.Lerp(m_runnerA, m_runnerB, cu);
            pos.y += Mathf.Abs(Mathf.Sin(cu * Mathf.PI * 8f)) * 0.06f;
            m_runner.position = pos;
            m_runner.rotation = Quaternion.LookRotation(Vector3.right);
            if (cu >= 1f) m_runnerActive = false;
        }

        void RestoreRunner()
        {
            if (m_swingerHammer != null)
            {
                m_swingerHammer.transform.localPosition = m_swingerHammerBasePos;
                m_swingerHammer.transform.localRotation = m_swingerHammerBaseRot;
                m_swingerHammer.enabled = m_swingerHammerWasEnabled;
                m_swingerHammer = null;
            }
            m_swingerActive = false;
            if (m_runner != null)
            {
                m_runner.position = m_runnerHome;
                m_runner.rotation = m_runnerHomeRot;
            }
            m_runner = null;
            m_runnerActive = false;
        }

        static MatchPlayer FindPlayer(string characterId)
        {
            var match = Nakahira.DarumaMatch.Current;
            if (match == null || string.IsNullOrEmpty(characterId)) return null;
            foreach (var p in match.Players)
            {
                if (p != null && p.CharacterData != null && p.CharacterData.id == characterId) return p;
            }
            return null;
        }

        static MatchPlayer FindFirstCpu()
        {
            var match = Nakahira.DarumaMatch.Current;
            if (match == null) return null;
            foreach (var p in match.Players)
            {
                if (p != null && p.IsCpu) return p;
            }
            return null;
        }

        //========================================
        // ポストプロセス（回想・シネマ調）
        //========================================

        void SetMemory(bool on)
        {
            m_memory = on;
            ApplyLook();
        }

        void SetCinema(bool on)
        {
            m_cinema = on;
            ApplyLook();
        }

        void SetDarkMood(bool on)
        {
            if (m_darkMood == on) return;
            m_darkMood = on;
            ApplyLook();
        }

        void SetNightRoom(bool on)
        {
            m_nightMood = on;
            if (m_skyQuad != null)
                m_skyQuad.GetComponent<Renderer>().sharedMaterial = on ? m_skyNight : (m_sunset ? m_skySunset : m_skyDay);
            if (m_sunPatch != null) m_sunPatch.gameObject.SetActive(!on);
            if (on)
            {
                if (m_sun != null)
                {
                    m_sun.color = new Color(0.50f, 0.62f, 0.88f);
                    m_sun.intensity = 4.5f;
                }
                if (m_fill != null)
                {
                    m_fill.color = new Color(0.70f, 0.76f, 0.90f);
                    m_fill.intensity = 4.5f;
                }
            }
            else
            {
                if (m_fill != null) m_fill.color = new Color(0.75f, 0.82f, 1f);
                SetSunset(m_sunset);
            }
            if (m_cinema) ApplyLook();
        }

        void ApplyLook()
        {
            if (m_volume == null || m_ca == null) return;
            bool on = m_memory || m_cinema;
            if (m_volume.gameObject.activeSelf != on) m_volume.gameObject.SetActive(on);
            if (!on) return;

            if (m_memory)
            {
                // セピア：彩度を落として暖色の膜、周辺を暗く、光をにじませる
                m_ca.saturation.value = -55f;
                m_ca.contrast.value = -5f;
                m_ca.postExposure.value = 0.2f;
                m_ca.colorFilter.value = new Color(1f, 0.78f, 0.5f);
                m_vig.intensity.value = 0.45f;
                m_vig.color.value = new Color(0.25f, 0.15f, 0.08f);
                m_bloom.intensity.value = 0.8f;
            }
            else if (m_darkMood)
            {
                // 視線が一瞬だけ暗くなる演出（古い槌のカット）
                m_ca.saturation.value = -24f;
                m_ca.contrast.value = 10f;
                m_ca.postExposure.value = -0.35f;
                m_ca.colorFilter.value = new Color(0.84f, 0.82f, 0.88f);
                m_vig.intensity.value = 0.58f;
                m_vig.color.value = new Color(0.05f, 0.04f, 0.06f);
                m_bloom.intensity.value = 0.25f;
            }
            else if (m_nightMood)
            {
                // 夜の部室：静かで落ち着いた青みのある夜トーン
                m_ca.saturation.value = -16f;
                m_ca.contrast.value = 8f;
                m_ca.postExposure.value = -0.22f;
                m_ca.colorFilter.value = new Color(0.84f, 0.88f, 0.98f);
                m_vig.intensity.value = 0.44f;
                m_vig.color.value = new Color(0.04f, 0.05f, 0.09f);
                m_bloom.intensity.value = 0.25f;
            }
            else
            {
                // シネマ調：少しコントラストを上げて、周辺を軽く落とす
                m_ca.saturation.value = -8f;
                m_ca.contrast.value = 6f;
                m_ca.postExposure.value = 0.05f;
                m_ca.colorFilter.value = m_sunset ? new Color(1f, 0.84f, 0.68f) : Color.white;
                m_vig.intensity.value = 0.32f;
                m_vig.color.value = new Color(0.12f, 0.08f, 0.06f);
                m_bloom.intensity.value = 0.35f;
            }
        }

        void SetSunset(bool on)
        {
            m_sunset = on;
            if (m_skyQuad != null)
                m_skyQuad.GetComponent<Renderer>().sharedMaterial = on ? m_skySunset : m_skyDay;
            if (m_sun != null)
            {
                m_sun.color = on ? new Color(1f, 0.62f, 0.34f) : new Color(1f, 0.8f, 0.58f);
                m_sun.intensity = on ? 13f : 10f;
            }
            if (m_fill != null) m_fill.intensity = on ? 4f : 6f;
            if (m_sunPatch != null)
                m_sunPatch.sharedMaterial.SetColor("_BaseColor", on ? new Color(1f, 0.7f, 0.4f, 0.4f) : new Color(1f, 0.86f, 0.6f, 0.34f));
            if (m_cinema) ApplyLook();
        }

        void SetCaption(bool on)
        {
            if (m_captionGroup != null) m_captionGroup.alpha = on ? 1f : 0f;
        }

        void SetTitle(bool on)
        {
            if (m_titleGroup == null) return;
            if (on) m_titleGroup.transform.SetAsLastSibling();   // 暗転より手前に出す
            m_titleGroup.alpha = on ? 1f : 0f;
        }

        void SetHint(bool on)
        {
            if (m_hintGroup == null) return;
            m_hintGroup.gameObject.SetActive(on);
            m_hintGroup.alpha = on ? 1f : 0f;
        }

        // 右下の操作ヒントを確実に消す（GO!やメニュー移行時に残らないように）
        public void HideHint()
        {
            SetHint(false);
        }

        void Update()
        {
            if (!m_visible) return;

            // ゲームを止めていても動くように unscaled。スローはこの時計にだけ掛ける
            m_time += Time.unscaledDeltaTime * m_baseScale;

            if (m_opening)
            {
                // B：スキップ（ムービー全体）／A：つぎへ（次のカット）
                if (MenuInput.Cancel()) m_skipped = true;
                if (MenuInput.Submit() || MenuInput.Start() || MenuInput.AnyClick()) m_advance = true;
            }
            else
            {
                m_advance = false;
            }

            AnimateMembers();
            if (m_stageMode) AnimateStage();
            if (m_arenaMode) AnimateArena();
            if (m_finalMode) AnimateFinalEnding();
            AnimateBanner();

            if (m_camera == null || m_camPos == null) return;
            float lt = m_time - m_shotStart;
            m_camShake = Mathf.MoveTowards(m_camShake, 0f, Time.unscaledDeltaTime * 2f);
            var shake = Random.insideUnitSphere * m_camShake * 0.15f;
            var pos = m_camPos(lt) + shake;
            var look = m_camLook(lt) + shake * 0.5f;
            Vector3 dir = look - pos;
            if (dir.sqrMagnitude > 0.0001f)
            {
                m_camera.transform.position = pos;
                m_camera.transform.rotation = Quaternion.LookRotation(dir);
            }
            m_fovPunch = Mathf.MoveTowards(m_fovPunch, 0f, Time.unscaledDeltaTime * 45f);
            float fov = m_shotFov;
            if (m_blendDuration > 0.01f)
            {
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(lt / m_blendDuration));
                fov = Mathf.Lerp(m_blendFromFov, m_shotFov, u);
            }
            m_camera.fieldOfView = Mathf.Max(1f, fov - m_fovPunch);
        }

        //========================================
        // プロローグ（八年前の槌音）：王冠大会 決勝会場
        //========================================

        Vector3 A(float x, float y, float z) => ArenaOrigin + new Vector3(x, y, z);

        Transform ArenaPrim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var t = ToyKit.Primitive(type, name, parent);
            t.localPosition = localPos;
            t.localScale = scale;
            t.localRotation = Quaternion.Euler(euler);
            t.GetComponent<Renderer>().sharedMaterial = mat;
            t.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        void UseArenaCamera()
        {
            if (m_camera == null) return;
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = new Color(0.025f, 0.025f, 0.04f);
            m_camera.farClipPlane = 120f;
        }

        void BuildArena()
        {
            if (m_arenaBuilt) return;
            m_arenaBuilt = true;

            m_arenaRoot = new GameObject("StoryArenaSet").transform;
            m_arenaRoot.position = ArenaOrigin;

            var darkFloorMat = ToyKit.Lit(new Color(0.28f, 0.24f, 0.20f), 0.25f);
            var towerBlockMat = ToyKit.Lit(new Color(0.36f, 0.27f, 0.19f), 0.25f);
            var towerBandMat = ToyKit.Lit(new Color(0.46f, 0.34f, 0.23f), 0.25f);
            var seatMat = ToyKit.Lit(new Color(0.12f, 0.12f, 0.16f), 0.1f);
            var audienceMat = Unlit(new Color(0.04f, 0.04f, 0.06f));
            var hammerWoodMat = ToyKit.Lit(new Color(0.45f, 0.28f, 0.15f), 0.3f);
            var hammerHeadMat = ToyKit.Lit(new Color(0.26f, 0.21f, 0.18f), 0.45f);
            Color fatherColor = new Color(0.93f, 0.27f, 0.25f);
            Color kingColor = new Color(0.55f, 0.35f, 0.70f);

            // 舞台：巨大な積み木の塔の頂上（上面 y = 0）
            ArenaPrim(PrimitiveType.Cylinder, "StageTop", m_arenaRoot, new Vector3(0f, -0.25f, 0f), new Vector3(9.6f, 0.25f, 9.6f), darkFloorMat);
            for (int tier = 0; tier < 4; tier++)
            {
                float y = -0.8f - tier * 0.9f;
                float r = 9.2f + tier * 0.6f;
                var mat = tier % 2 == 0 ? towerBlockMat : towerBandMat;
                ArenaPrim(PrimitiveType.Cylinder, "TowerTier_" + tier, m_arenaRoot, new Vector3(0f, y, 0f), new Vector3(r, 0.42f, r), mat);
            }

            // 周囲の暗い観客席段差（奥・左・右）
            for (int step = 0; step < 3; step++)
            {
                float y = 0.6f + step * 1.1f;
                float z = 8.4f + step * 1.6f;
                ArenaPrim(PrimitiveType.Cube, "StandBack_" + step, m_arenaRoot, new Vector3(0f, y, z), new Vector3(22f, 1.0f, 1.8f), seatMat);
                ArenaPrim(PrimitiveType.Cube, "StandLeft_" + step, m_arenaRoot, new Vector3(-9.2f - step * 1.5f, y, 1.5f), new Vector3(1.8f, 1.0f, 16f), seatMat);
            }

            // 客席の影（数人の観客シルエット）
            for (int i = -4; i <= 4; i++)
            {
                if (i == -3) continue;
                ArenaPrim(PrimitiveType.Sphere, "Audience_" + i, m_arenaRoot, new Vector3(i * 1.8f, 1.45f, 8.2f), new Vector3(0.75f, 0.85f, 0.75f), audienceMat);
            }

            // 客席の隅の幼きあかだる（父と同じ既存のあかだるモデル＋本編仕様の積み木＋赤鉢巻、少年サイズ）
            var boyMember = AddGameVisualMember("akadaru", new Vector3(-6.4f, 0.95f, -4.6f), 52f,
                Nakahira.TumikiColor.Red, fatherColor, m_arenaRoot, 1, false, DarumaColor.Red, true);
            boyMember.root.name = "BoySeat";
            boyMember.root.localScale = Vector3.one * 0.78f;
            m_arenaBoyShadow = boyMember.root;

            // 舞台上の父（既存のあかだるモデル＋本編仕様の積み木＋赤鉢巻＋ハンマー）
            var fatherMember = AddGameVisualMember("akadaru", new Vector3(-2.3f, 0f, 0f), 90f,
                Nakahira.TumikiColor.Red, fatherColor, m_arenaRoot, 2, true, DarumaColor.Red, true);
            m_arenaFatherShadow = fatherMember.root;

            // 舞台上のだるきんぐ（既存のだるきんぐモデル＋マント＋剣＋本編仕様のたった二つの積み木）
            var kingMember = AddGameVisualMember("daruking", new Vector3(2.3f, 0f, 0f), -90f,
                Nakahira.TumikiColor.Red, kingColor, m_arenaRoot, 2, true, DarumaColor.Wood, true);
            m_arenaKingMember = kingMember;
            m_arenaKingShadow = kingMember.root;
            // 足元の2つの積み木だけを連鎖崩壊アニメ用に切り離す
            var kBlock0 = kingMember.root.Find("Visual/Stack/Block_0");
            var kBlock1 = kingMember.root.Find("Visual/Stack/Block_1");
            if (kBlock0 != null) kBlock0.SetParent(m_arenaRoot, true);
            if (kBlock1 != null) kBlock1.SetParent(m_arenaRoot, true);
            m_arenaKingBlocks = new[] { kBlock0, kBlock1 };
            m_arenaKingBlockBasePos = new[]
            {
                kBlock0 != null ? kBlock0.localPosition : new Vector3(2.3f, 0.25f, 0f),
                kBlock1 != null ? kBlock1.localPosition : new Vector3(2.3f, 0.75f, 0f),
            };
            m_arenaKingBlockBaseRot = new[]
            {
                kBlock0 != null ? kBlock0.localRotation : Quaternion.identity,
                kBlock1 != null ? kBlock1.localRotation : Quaternion.identity,
            };

            // 父が放つ連鎖用積み木（本編仕様）
            m_arenaShotBlock = CreateMatchStyleBlock("FatherShotBlock", m_arenaRoot,
                new Vector3(-1.3f, 0.25f, 0f), Nakahira.TumikiColor.Red, fatherColor);
            m_arenaShotBlock.gameObject.SetActive(false);

            // 明転時に床を転がる一本の古い槌（あかだるの既存ハンマーモデルを使用。無ければ仮モデル）
            var floorHammer = new GameObject("FloorHammer").transform;
            floorHammer.SetParent(m_arenaRoot, false);
            floorHammer.localPosition = new Vector3(-0.15f, 0.22f, 0.15f);
            var akadaruData = FindCharacterData("akadaru");
            GameObject floorHammerSrc = akadaruData != null
                ? (akadaruData.HammerFor(DarumaColor.Red) ?? GameModelSettings.HammerModel)
                : GameModelSettings.HammerModel;
            Transform floorModel = floorHammerSrc != null ? ToyKit.CreateHammerModel("FloorHammerHead", floorHammerSrc) : null;
            if (floorModel != null)
            {
                floorModel.name = "HammerModel";
                floorModel.SetParent(floorHammer, false);
                float hScale = 1.2f / ToyKit.HammerLength;
                floorModel.localScale = Vector3.one * hScale;
                floorModel.localPosition = new Vector3(0f, 0f, -0.7f);
                floorModel.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            else
            {
                ArenaPrim(PrimitiveType.Cylinder, "Handle", floorHammer, new Vector3(0f, 0f, -0.45f), new Vector3(0.11f, 0.55f, 0.11f), hammerWoodMat, new Vector3(90f, 0f, 0f));
                ArenaPrim(PrimitiveType.Cylinder, "Head", floorHammer, new Vector3(0f, 0f, 0.18f), new Vector3(0.40f, 0.34f, 0.40f), hammerHeadMat, new Vector3(0f, 0f, 90f));
            }
            floorHammer.gameObject.SetActive(false);
            m_arenaFloorHammer = floorHammer;

            // 会場の照明（舞台の既存モデル・木の積み木・客席隅のコントラストが出る明るさ）
            m_arenaLights.Clear();
            m_arenaLightBase.Clear();
            AddArenaLight("ArenaSpotCenter", new Vector3(0f, 6.2f, 0f), new Color(1.0f, 0.92f, 0.78f), 26f, 22f);
            AddArenaLight("ArenaFrontFill", new Vector3(0f, 3.2f, -5.5f), new Color(0.95f, 0.90f, 0.85f), 14f, 18f);
            AddArenaLight("ArenaBackRim", new Vector3(0f, 4.2f, 6.5f), new Color(0.60f, 0.70f, 0.96f), 14f, 22f);
            AddArenaLight("ArenaBoyFill", new Vector3(-5.4f, 3.6f, -3.5f), new Color(0.95f, 0.88f, 0.80f), 11f, 14f);

            RegisterArenaShots();
        }

        void AddArenaLight(string name, Vector3 localPos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_arenaRoot, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            m_arenaLights.Add(l);
            m_arenaLightBase.Add(intensity);
        }

        void RegisterArenaShots()
        {
            if (m_shots.ContainsKey("S_ARENA_FAR")) return;

            // S_ARENA_FAR：巨大な積み木の塔の舞台と2つの影・客席隅の少年を、会話UIより上の領域に収めた会場全景
            RegisterShot(new ShotDefinition
            {
                id = "S_ARENA_FAR",
                target = m_arenaRoot,
                positionOffset = new Vector3(-2.6f, 5.2f, -12.5f),
                lookOffset = new Vector3(-1.0f, -1.9f, 0f),
                fov = 46f,
                sway = true,
            });

            // S_BOY_SEAT：客席の隅の赤い帽子の少年を手前に、奥の舞台を会話UIより上に映すショット
            RegisterShot(new ShotDefinition
            {
                id = "S_BOY_SEAT",
                target = m_arenaRoot,
                positionOffset = new Vector3(-8.2f, 2.7f, -6.8f),
                lookOffset = new Vector3(-1.2f, -0.75f, 0f),
                fov = 43f,
            });

            // S_BLACKOUT：暗転中のショット（映像は暗幕で完全に隠す）
            RegisterShot(new ShotDefinition
            {
                id = "S_BLACKOUT",
                target = m_arenaRoot,
                positionOffset = new Vector3(0f, 4.0f, -10f),
                lookOffset = new Vector3(0f, 0.5f, 0f),
                fov = 44f,
            });

            // S_HAMMER_FLOOR：床に転がる古い槌を会話UIより上に大きく映し、奥の客席隅に少年の影を残すショット
            RegisterShot(new ShotDefinition
            {
                id = "S_HAMMER_FLOOR",
                target = m_arenaRoot,
                positionOffset = new Vector3(2.1f, 1.65f, 2.3f),
                lookOffset = new Vector3(-1.2f, -0.55f, -0.6f),
                fov = 42f,
            });
        }

        void ResetArenaState()
        {
            BuildArena();
            m_arenaCollapseStart = -1f;
            m_arenaHammerRollStart = -1f;
            if (m_arenaFadeRoutine != null) { StopCoroutine(m_arenaFadeRoutine); m_arenaFadeRoutine = null; }
            if (m_preludeBlackoutCleanupRoutine != null) { StopCoroutine(m_preludeBlackoutCleanupRoutine); m_preludeBlackoutCleanupRoutine = null; }
            m_preludeFadeDelegatedToVfx = false;

            if (m_arenaFatherShadow != null) m_arenaFatherShadow.gameObject.SetActive(true);
            if (m_arenaKingShadow != null)
            {
                m_arenaKingShadow.gameObject.SetActive(true);
                m_arenaKingShadow.localPosition = new Vector3(2.3f, 0f, 0f);
            }
            if (m_arenaKingMember != null)
                m_arenaKingMember.basePos = new Vector3(2.3f, 0f, 0f);
            if (m_arenaBoyShadow != null) m_arenaBoyShadow.gameObject.SetActive(true);
            if (m_arenaShotBlock != null)
            {
                m_arenaShotBlock.gameObject.SetActive(false);
                m_arenaShotBlock.localPosition = new Vector3(-1.3f, 0.25f, 0f);
            }
            if (m_arenaKingBlocks != null)
            {
                for (int i = 0; i < m_arenaKingBlocks.Length; i++)
                {
                    if (m_arenaKingBlocks[i] == null) continue;
                    m_arenaKingBlocks[i].gameObject.SetActive(true);
                    m_arenaKingBlocks[i].localPosition = m_arenaKingBlockBasePos[i];
                    m_arenaKingBlocks[i].localRotation = m_arenaKingBlockBaseRot != null && i < m_arenaKingBlockBaseRot.Length
                        ? m_arenaKingBlockBaseRot[i]
                        : Quaternion.identity;
                }
            }
            if (m_arenaFloorHammer != null) m_arenaFloorHammer.gameObject.SetActive(false);
            SetArenaLightsScale(1f);
        }

        void SetArenaLightsScale(float scale)
        {
            for (int i = 0; i < m_arenaLights.Count; i++)
            {
                if (m_arenaLights[i] != null) m_arenaLights[i].intensity = m_arenaLightBase[i] * scale;
            }
        }

        // プロローグ開始時のテロップ導入（A：つぎへ / B：スキップ）
        public IEnumerator PlayPreludeIntro(StoryChapter chapter)
        {
            ResetArenaState();
            m_time = 0f;
            m_shift = 0f;
            m_skipped = false;
            WasSkipped = false;
            m_advance = false;
            m_baseScale = 1f;
            m_opening = true;
            m_stageMode = false;
            m_venueMode = false;
            m_finalMode = false;
            m_arenaMode = true;

            Show();
            UseArenaCamera();
            m_fadeGroup.alpha = 1f;
            SetMemory(false);
            SetCinema(false);
            SetCaption(false);
            SetTitle(false);
            SetHint(true);
            SetCaptionText(chapter != null && chapter.cutscene != null && !string.IsNullOrEmpty(chapter.cutscene.caption)
                ? chapter.cutscene.caption
                : "八年前 ― 王冠大会 決勝");

            if (m_shots.TryGetValue("S_ARENA_FAR", out var farShot))
                ApplyShotDefinition(farShot, 0f);

            yield return FadeTo(0f, 0.6f);
            SetCaption(true);
            yield return Hold(2.8f);

            if (!m_skipped)
            {
                yield return FadeTo(0.45f, 0.35f);
                var card = (RectTransform)m_titleGroup.transform;
                card.localScale = Vector3.one * 1.05f;
                SetTitle(true);
                yield return Tween(0.7f, u => card.localScale = Vector3.one * Mathf.Lerp(1.05f, 1f, u));
                yield return Hold(1.2f);
                SetTitle(false);
                if (!m_skipped) yield return FadeTo(0f, 0.35f);
            }

            m_opening = false;
            SetHint(false);
            SetCaption(false);
            SetTitle(false);
            m_advance = false;
            WasSkipped = m_skipped;
            if (m_skipped)
            {
                m_fadeGroup.alpha = 0f;
                m_skipped = false;
            }
        }

        void ApplyPreludeCue(string cue)
        {
            switch (cue)
            {
                case "tower_collapse":
                    m_arenaCollapseStart = m_time;
                    if (m_arenaShotBlock != null)
                    {
                        m_arenaShotBlock.gameObject.SetActive(true);
                        m_arenaShotBlock.localPosition = new Vector3(-1.3f, 0.25f, 0f);
                    }
                    break;

                case "blackout":
                case "keep_blackout":
                    if (m_arenaFadeRoutine != null) { StopCoroutine(m_arenaFadeRoutine); m_arenaFadeRoutine = null; }
                    if (m_preludeBlackoutCleanupRoutine != null) { StopCoroutine(m_preludeBlackoutCleanupRoutine); m_preludeBlackoutCleanupRoutine = null; }
                    if (m_preludeFadeDelegatedToVfx)
                    {
                        // 暗転ビジュアルは fx_fade（ScreenOverlayFx）1枚に統一し、m_fadeGroup との二重暗転を防ぐ。
                        // 暗転フェード完了後に舞台上の2つの影を退場させる（keep_blackout は即退場）。
                        if (cue == "blackout")
                            m_preludeBlackoutCleanupRoutine = StartCoroutine(CleanupArenaAfterVfxBlackout(0.38f));
                        else
                            ClearArenaStageActors();
                    }
                    else
                    {
                        SetArenaLightsScale(0f);
                        if (m_fadeGroup != null) m_fadeGroup.alpha = 1f;
                        ClearArenaStageActors();
                    }
                    break;

                case "lights_on_hammer":
                    if (m_preludeBlackoutCleanupRoutine != null) { StopCoroutine(m_preludeBlackoutCleanupRoutine); m_preludeBlackoutCleanupRoutine = null; }
                    if (m_arenaFatherShadow != null) m_arenaFatherShadow.gameObject.SetActive(false);
                    if (m_arenaKingShadow != null) m_arenaKingShadow.gameObject.SetActive(false);
                    if (m_arenaFloorHammer != null)
                    {
                        m_arenaFloorHammer.gameObject.SetActive(true);
                        m_arenaFloorHammer.localPosition = new Vector3(-1.25f, 0.22f, -0.35f);
                        m_arenaFloorHammer.localRotation = Quaternion.Euler(8f, 18f, 0f);
                    }
                    m_arenaHammerRollStart = m_time;
                    SetArenaLightsScale(0.85f);
                    if (!m_preludeFadeDelegatedToVfx)
                    {
                        if (m_arenaFadeRoutine != null) StopCoroutine(m_arenaFadeRoutine);
                        m_arenaFadeRoutine = StartCoroutine(FadeArenaIn(0.65f));
                    }
                    else
                    {
                        if (m_fadeGroup != null) m_fadeGroup.alpha = 0f;
                    }
                    break;
            }
        }

        void ClearArenaStageActors()
        {
            SetArenaLightsScale(0f);
            // 暗転中に舞台の上で何が起きたか見えないよう、2つの影をここで退場させておく
            if (m_arenaFatherShadow != null) m_arenaFatherShadow.gameObject.SetActive(false);
            if (m_arenaKingShadow != null) m_arenaKingShadow.gameObject.SetActive(false);
            if (m_arenaShotBlock != null) m_arenaShotBlock.gameObject.SetActive(false);
            if (m_arenaKingBlocks != null)
            {
                foreach (var b in m_arenaKingBlocks)
                    if (b != null) b.gameObject.SetActive(false);
            }
        }

        IEnumerator CleanupArenaAfterVfxBlackout(float delay)
        {
            float elapsed = 0f;
            while (elapsed < delay)
            {
                if (m_vfxPlayer != null && m_vfxPlayer.OverlayFx.IsPaused) { yield return null; continue; }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            ClearArenaStageActors();
            m_preludeBlackoutCleanupRoutine = null;
        }

        IEnumerator FadeArenaIn(float duration)
        {
            yield return FadeTo(0f, duration);
            m_arenaFadeRoutine = null;
        }

        void AnimateArena()
        {
            // 積み木の連鎖崩壊アニメーション
            if (m_arenaCollapseStart >= 0f)
            {
                float t = m_time - m_arenaCollapseStart;
                if (t < 0.26f)
                {
                    float u = Mathf.Clamp01(t / 0.26f);
                    if (m_arenaShotBlock != null)
                        m_arenaShotBlock.localPosition = Vector3.Lerp(new Vector3(-1.3f, 0.25f, 0f), new Vector3(1.55f, 0.25f, 0f), u);
                }
                else
                {
                    if (m_arenaShotBlock != null && m_arenaShotBlock.gameObject.activeSelf)
                        m_arenaShotBlock.gameObject.SetActive(false);

                    float u = Mathf.Clamp01((t - 0.26f) / 0.65f);
                    if (m_arenaKingBlocks != null && m_arenaKingBlocks.Length >= 2)
                    {
                        Quaternion rot0 = m_arenaKingBlockBaseRot != null && m_arenaKingBlockBaseRot.Length > 0 ? m_arenaKingBlockBaseRot[0] : Quaternion.identity;
                        Quaternion rot1 = m_arenaKingBlockBaseRot != null && m_arenaKingBlockBaseRot.Length > 1 ? m_arenaKingBlockBaseRot[1] : Quaternion.identity;
                        if (m_arenaKingBlocks[0] != null)
                        {
                            m_arenaKingBlocks[0].localPosition = m_arenaKingBlockBasePos[0] + new Vector3(u * 2.4f, -u * u * 1.4f, u * 1.8f);
                            m_arenaKingBlocks[0].localRotation = rot0 * Quaternion.Euler(u * 140f, 0f, -u * 180f);
                            if (u >= 1f) m_arenaKingBlocks[0].gameObject.SetActive(false);
                        }
                        if (m_arenaKingBlocks[1] != null)
                        {
                            m_arenaKingBlocks[1].localPosition = m_arenaKingBlockBasePos[1] + new Vector3(u * 1.8f, Mathf.Sin(u * Mathf.PI) * 0.8f - u * u * 1.9f, -u * 2.1f);
                            m_arenaKingBlocks[1].localRotation = rot1 * Quaternion.Euler(-u * 170f, u * 90f, u * 120f);
                            if (u >= 1f) m_arenaKingBlocks[1].gameObject.SetActive(false);
                        }
                    }
                    if (m_arenaKingShadow != null)
                    {
                        var droppedPos = new Vector3(2.3f, -1.0f * u, 0f);
                        m_arenaKingShadow.localPosition = droppedPos;
                        if (m_arenaKingMember != null) m_arenaKingMember.basePos = droppedPos;
                    }
                    if (u >= 1f) m_arenaCollapseStart = -1f;
                }
            }

            // 床に転がる一本の古い槌（転がりながら減速して止まる）
            if (m_arenaHammerRollStart >= 0f && m_arenaFloorHammer != null)
            {
                float u = Mathf.Clamp01((m_time - m_arenaHammerRollStart) / 1.35f);
                float ease = 1f - Mathf.Pow(1f - u, 3f);
                m_arenaFloorHammer.localPosition = Vector3.Lerp(new Vector3(-1.25f, 0.22f, -0.35f), new Vector3(-0.15f, 0.22f, 0.15f), ease);
                m_arenaFloorHammer.localRotation = Quaternion.Euler(8f, Mathf.Lerp(18f, 68f, ease), Mathf.Lerp(0f, 290f, ease));
                if (u >= 1f) m_arenaHammerRollStart = -1f;
            }
        }

        //========================================
        // 第4章（頂の決勝戦）：決勝ステージ＋貴賓席／観客席セット
        //========================================

        Transform Ch4Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var t = ToyKit.Primitive(type, name, parent);
            t.localPosition = localPos;
            t.localScale = scale;
            t.localRotation = Quaternion.Euler(euler);
            t.GetComponent<Renderer>().sharedMaterial = mat;
            t.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        void UseCh4SetCamera()
        {
            if (m_camera == null) return;
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = m_sunset
                ? new Color(0.28f, 0.16f, 0.14f)
                : new Color(0.18f, 0.24f, 0.36f);
            m_camera.farClipPlane = 120f;
        }

        void AddCh4Light(string name, Vector3 localPos, Color dayColor, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_ch4SetRoot, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = dayColor;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            m_ch4Lights.Add(l);
            m_ch4LightDayColors.Add(dayColor);
        }

        void BuildCh4FinalsSet()
        {
            if (m_ch4SetBuilt) return;
            m_ch4SetBuilt = true;

            m_ch4SetRoot = new GameObject("StoryCh4FinalsSet").transform;
            m_ch4SetRoot.position = Ch4FinalsOrigin;

            var carpetMat = ToyKit.Lit(new Color(0.62f, 0.10f, 0.12f), 0.25f);
            var darkWoodMat = ToyKit.Lit(new Color(0.24f, 0.15f, 0.10f), 0.3f);
            var goldMat = ToyKit.Lit(new Color(0.88f, 0.68f, 0.22f), 0.75f);
            var stoneMat = ToyKit.Lit(new Color(0.32f, 0.30f, 0.34f), 0.25f);
            var standMat = ToyKit.Lit(new Color(0.46f, 0.44f, 0.42f), 0.2f);
            var seatMat = ToyKit.Lit(new Color(0.76f, 0.30f, 0.22f), 0.25f);
            var stageFloorMat = ToyKit.Lit(new Color(0.62f, 0.52f, 0.40f), 0.25f);

            // ---- 1. 貴賓席エリア（x = 0 付近） ----
            Ch4Prim(PrimitiveType.Cube, "VipFloor", m_ch4SetRoot, new Vector3(0f, -0.25f, 0f), new Vector3(8.5f, 0.5f, 5.2f), stoneMat);
            Ch4Prim(PrimitiveType.Cube, "VipCarpet", m_ch4SetRoot, new Vector3(0f, 0.02f, 0.1f), new Vector3(4.2f, 0.04f, 4.4f), carpetMat);
            Ch4Prim(PrimitiveType.Cube, "VipWallBack", m_ch4SetRoot, new Vector3(0f, 2.4f, 2.4f), new Vector3(8.5f, 4.8f, 0.3f), darkWoodMat);
            Ch4Prim(PrimitiveType.Cube, "VipPillarL", m_ch4SetRoot, new Vector3(-3.2f, 2.4f, 2.15f), new Vector3(0.45f, 4.8f, 0.45f), goldMat);
            Ch4Prim(PrimitiveType.Cube, "VipPillarR", m_ch4SetRoot, new Vector3(3.2f, 2.4f, 2.15f), new Vector3(0.45f, 4.8f, 0.45f), goldMat);
            // 手すり（見下ろし感を強調）
            Ch4Prim(PrimitiveType.Cube, "VipRailTop", m_ch4SetRoot, new Vector3(0f, 0.92f, -2.15f), new Vector3(8.4f, 0.14f, 0.18f), goldMat);
            for (int i = -3; i <= 3; i++)
                Ch4Prim(PrimitiveType.Cube, "VipRailPost_" + i, m_ch4SetRoot, new Vector3(i * 1.25f, 0.46f, -2.15f), new Vector3(0.12f, 0.92f, 0.12f), goldMat);
            // 玉座風の椅子
            Ch4Prim(PrimitiveType.Cube, "ThroneBack", m_ch4SetRoot, new Vector3(0f, 1.55f, 0.85f), new Vector3(1.55f, 2.9f, 0.24f), carpetMat);
            Ch4Prim(PrimitiveType.Cube, "ThroneFrameL", m_ch4SetRoot, new Vector3(-0.82f, 1.55f, 0.82f), new Vector3(0.16f, 3.05f, 0.30f), goldMat);
            Ch4Prim(PrimitiveType.Cube, "ThroneFrameR", m_ch4SetRoot, new Vector3(0.82f, 1.55f, 0.82f), new Vector3(0.16f, 3.05f, 0.30f), goldMat);
            Ch4Prim(PrimitiveType.Cube, "ThroneCrown", m_ch4SetRoot, new Vector3(0f, 3.12f, 0.82f), new Vector3(1.75f, 0.22f, 0.30f), goldMat);
            // 貴賓席に座るだるきんぐ（2段積み木＋本編モデル、-Z 方向へ見下ろす）
            AddGameVisualMember("daruking", new Vector3(0f, 0f, 0.12f), 180f, Nakahira.TumikiColor.Red, new Color(0.55f, 0.35f, 0.70f), m_ch4SetRoot, 2, false);
            m_ch4SkyVip = TitleDressingKit.Quad(m_ch4SetRoot, "VipSky", Ch4FinalsOrigin + new Vector3(0f, 4.5f, 14f), new Vector3(36f, 18f, 1f), m_skyDay);

            // ---- 2. 観客席エリア（x = +30 付近） ----
            Vector3 audBase = new Vector3(30f, 0f, 0f);
            for (int step = 0; step < 3; step++)
            {
                float y = step * 0.7f;
                float z = -1.6f + step * 1.8f;
                Ch4Prim(PrimitiveType.Cube, "AudStep_" + step, m_ch4SetRoot, audBase + new Vector3(0f, y - 0.35f, z), new Vector3(11f, 0.7f, 1.8f), standMat);
                Ch4Prim(PrimitiveType.Cube, "AudBench_" + step, m_ch4SetRoot, audBase + new Vector3(0f, y + 0.14f, z + 0.45f), new Vector3(10.6f, 0.24f, 0.55f), seatMat);
            }
            Ch4Prim(PrimitiveType.Cube, "AudRail", m_ch4SetRoot, audBase + new Vector3(0f, 0.6f, -2.45f), new Vector3(11f, 0.12f, 0.12f), goldMat);
            // 客席の七転高校3人（きだる・あおだる・みだる）
            m_ch4AudMembers.Clear();
            m_ch4AudMembers.Add(AddGameVisualMember("kidaru", audBase + new Vector3(-1.7f, 0.7f, 0.15f), 186f, Nakahira.TumikiColor.Yellow, new Color(0.95f, 0.78f, 0.18f), m_ch4SetRoot, 2, false));
            m_ch4AudMembers.Add(AddGameVisualMember("aodaru", audBase + new Vector3(0.0f, 0.7f, 0.15f), 180f, Nakahira.TumikiColor.Blue, new Color(0.20f, 0.42f, 0.85f), m_ch4SetRoot, 2, false));
            m_ch4AudMembers.Add(AddGameVisualMember("midaru", audBase + new Vector3(1.7f, 0.7f, 0.15f), 174f, Nakahira.TumikiColor.Green, new Color(0.28f, 0.62f, 0.32f), m_ch4SetRoot, 2, false));
            m_ch4SkyAudience = TitleDressingKit.Quad(m_ch4SetRoot, "AudSky", Ch4FinalsOrigin + audBase + new Vector3(0f, 6.5f, 14f), new Vector3(44f, 22f, 1f), m_skyDay);

            // ---- 3. 貴賓席の扉・階段・舞台登場エリア（x = -30 付近） ----
            Vector3 descBase = new Vector3(-30f, 0f, 0f);
            // 決勝の舞台床
            Ch4Prim(PrimitiveType.Cube, "DescStageFloor", m_ch4SetRoot, descBase + new Vector3(0f, -0.25f, -1.2f), new Vector3(12f, 0.5f, 7.5f), stageFloorMat);
            // 貴賓席から舞台へ続く赤い絨毯の階段（6段）
            for (int s = 0; s < 6; s++)
            {
                float sy = (s + 1) * 0.48f;
                float sz = 1.6f + s * 0.68f;
                Ch4Prim(PrimitiveType.Cube, "DescStair_" + s, m_ch4SetRoot, descBase + new Vector3(0f, sy - 0.24f, sz), new Vector3(3.4f, 0.48f, 0.72f), stoneMat);
                Ch4Prim(PrimitiveType.Cube, "DescStairCarpet_" + s, m_ch4SetRoot, descBase + new Vector3(0f, sy + 0.01f, sz), new Vector3(2.2f, 0.04f, 0.70f), carpetMat);
            }
            // 上段の貴賓席バルコニーと両開き扉
            Ch4Prim(PrimitiveType.Cube, "DescBalcony", m_ch4SetRoot, descBase + new Vector3(0f, 2.75f, 6.4f), new Vector3(10f, 0.5f, 3.0f), stoneMat);
            Ch4Prim(PrimitiveType.Cube, "DescWallL", m_ch4SetRoot, descBase + new Vector3(-3.3f, 4.9f, 6.8f), new Vector3(3.4f, 4.0f, 0.35f), darkWoodMat);
            Ch4Prim(PrimitiveType.Cube, "DescWallR", m_ch4SetRoot, descBase + new Vector3(3.3f, 4.9f, 6.8f), new Vector3(3.4f, 4.0f, 0.35f), darkWoodMat);
            Ch4Prim(PrimitiveType.Cube, "DescHeader", m_ch4SetRoot, descBase + new Vector3(0f, 6.45f, 6.8f), new Vector3(3.6f, 0.9f, 0.4f), goldMat);
            var doorLPivot = new GameObject("VipDoorLPivot").transform;
            doorLPivot.SetParent(m_ch4SetRoot, false);
            doorLPivot.localPosition = descBase + new Vector3(-1.55f, 4.5f, 6.75f);
            Ch4Prim(PrimitiveType.Cube, "VipDoorL", doorLPivot, new Vector3(0.76f, 0f, 0f), new Vector3(1.5f, 3.0f, 0.14f), darkWoodMat);
            Ch4Prim(PrimitiveType.Cube, "VipDoorLTrim", doorLPivot, new Vector3(0.76f, 0f, -0.08f), new Vector3(1.25f, 2.65f, 0.04f), goldMat);
            m_ch4VipDoorL = doorLPivot;

            var doorRPivot = new GameObject("VipDoorRPivot").transform;
            doorRPivot.SetParent(m_ch4SetRoot, false);
            doorRPivot.localPosition = descBase + new Vector3(1.55f, 4.5f, 6.75f);
            Ch4Prim(PrimitiveType.Cube, "VipDoorR", doorRPivot, new Vector3(-0.76f, 0f, 0f), new Vector3(1.5f, 3.0f, 0.14f), darkWoodMat);
            Ch4Prim(PrimitiveType.Cube, "VipDoorRTrim", doorRPivot, new Vector3(-0.76f, 0f, -0.08f), new Vector3(1.25f, 2.65f, 0.04f), goldMat);
            m_ch4VipDoorR = doorRPivot;

            // 舞台へ降り立っただるきんぐ
            AddGameVisualMember("daruking", descBase + new Vector3(0f, 0f, -0.35f), 180f, Nakahira.TumikiColor.Red, new Color(0.55f, 0.35f, 0.70f), m_ch4SetRoot, 2, true);
            m_ch4SkyDescend = TitleDressingKit.Quad(m_ch4SetRoot, "DescSky", Ch4FinalsOrigin + descBase + new Vector3(0f, 7f, 16f), new Vector3(48f, 24f, 1f), m_skyDay);

            // ライティング（3エリアそれぞれに配置）
            m_ch4Lights.Clear();
            m_ch4LightDayColors.Clear();
            AddCh4Light("Ch4VipKey", new Vector3(-1.4f, 4.2f, -3.2f), new Color(1.0f, 0.94f, 0.82f), 18f, 18f);
            AddCh4Light("Ch4VipRim", new Vector3(1.8f, 3.4f, 1.4f), new Color(0.88f, 0.78f, 1.0f), 10f, 14f);
            AddCh4Light("Ch4AudKey", audBase + new Vector3(0f, 5.2f, -4.5f), new Color(1.0f, 0.95f, 0.86f), 20f, 22f);
            AddCh4Light("Ch4DescKey", descBase + new Vector3(0f, 5.6f, -4.2f), new Color(1.0f, 0.92f, 0.80f), 22f, 24f);
            AddCh4Light("Ch4DescTop", descBase + new Vector3(0f, 6.2f, 5.2f), new Color(0.95f, 0.86f, 0.74f), 14f, 18f);

            RegisterCh4FinalsShots();
        }

        void RegisterCh4FinalsShots()
        {
            if (m_shots.ContainsKey("S_DARUKING_VIP")) return;

            // S_DARUKING_VIP：最上段の貴賓席に座って見下ろすだるきんぐ（ローアングル）
            RegisterShot(new ShotDefinition
            {
                id = "S_DARUKING_VIP",
                target = m_ch4SetRoot,
                positionOffset = new Vector3(-1.15f, 0.95f, -3.55f),
                lookOffset = new Vector3(0f, 1.42f, 0.15f),
                fov = 38f,
                sway = true,
            });

            // S_AUDIENCE_MIDARU：客席のみだる（隣にあおだる・きだる）
            RegisterShot(new ShotDefinition
            {
                id = "S_AUDIENCE_MIDARU",
                target = m_ch4SetRoot,
                positionOffset = new Vector3(31.2f, 2.35f, -3.15f),
                lookOffset = new Vector3(31.45f, 2.00f, 0.15f),
                fov = 38f,
            });

            // S_AUDIENCE_KIDARU：客席のきだる（隣にあおだる）
            RegisterShot(new ShotDefinition
            {
                id = "S_AUDIENCE_KIDARU",
                target = m_ch4SetRoot,
                positionOffset = new Vector3(28.8f, 2.35f, -3.15f),
                lookOffset = new Vector3(28.55f, 2.00f, 0.15f),
                fov = 38f,
            });

            // S_AUDIENCE_AO：客席のあおだる（最終章の客席からの掛け合い用）
            RegisterShot(new ShotDefinition
            {
                id = "S_AUDIENCE_AO",
                target = m_ch4SetRoot,
                positionOffset = new Vector3(30.0f, 2.35f, -3.15f),
                lookOffset = new Vector3(30.0f, 2.00f, 0.15f),
                fov = 38f,
            });

            // S_AUDIENCE_TRIO：客席のきだる・あおだる・みだる3人全員（立ち上がり演出用）
            RegisterShot(new ShotDefinition
            {
                id = "S_AUDIENCE_TRIO",
                target = m_ch4SetRoot,
                positionOffset = new Vector3(30.0f, 2.65f, -4.75f),
                lookOffset = new Vector3(30.0f, 1.85f, 0.15f),
                fov = 44f,
            });

            // S_DARUKING_DESCEND：開いた貴賓席の扉と階段を背に、舞台へ立つだるきんぐ
            RegisterShot(new ShotDefinition
            {
                id = "S_DARUKING_DESCEND",
                target = m_ch4SetRoot,
                positionOffset = new Vector3(-30.0f, 2.15f, -6.4f),
                lookOffset = new Vector3(-30.0f, 1.95f, 1.6f),
                fov = 44f,
            });

            // S_DARUKING_STAGE：舞台上のだるきんぐ（正面寄り）
            RegisterShot(new ShotDefinition
            {
                id = "S_DARUKING_STAGE",
                target = m_ch4SetRoot,
                positionOffset = new Vector3(-30.0f, 1.72f, -3.35f),
                lookOffset = new Vector3(-30.0f, 1.42f, -0.35f),
                fov = 36f,
            });
        }

        void SetCh4VipDoorOpen(bool open)
        {
            if (m_ch4VipDoorL != null) m_ch4VipDoorL.localRotation = Quaternion.Euler(0f, open ? -78f : 0f, 0f);
            if (m_ch4VipDoorR != null) m_ch4VipDoorR.localRotation = Quaternion.Euler(0f, open ? 78f : 0f, 0f);
        }

        void SetCh4FinalsMood(bool sunset)
        {
            SetSunset(sunset);
            var sky = sunset ? m_skySunset : m_skyDay;
            if (m_ch4SkyVip != null) m_ch4SkyVip.GetComponent<Renderer>().sharedMaterial = sky;
            if (m_ch4SkyAudience != null) m_ch4SkyAudience.GetComponent<Renderer>().sharedMaterial = sky;
            if (m_ch4SkyDescend != null) m_ch4SkyDescend.GetComponent<Renderer>().sharedMaterial = sky;
            for (int i = 0; i < m_ch4Lights.Count && i < m_ch4LightDayColors.Count; i++)
            {
                if (m_ch4Lights[i] == null) continue;
                m_ch4Lights[i].color = sunset
                    ? Color.Lerp(m_ch4LightDayColors[i], new Color(1.0f, 0.56f, 0.30f), 0.65f)
                    : m_ch4LightDayColors[i];
            }
        }

        // 第4章のカットシーン（決勝全景 → 貴賓席のだるきんぐ → 相手2人）
        public IEnumerator PlayCh4FinalIntro(StoryChapter chapter)
        {
            BuildCh4FinalsSet();
            m_time = 0f;
            m_shift = 0f;
            m_skipped = false;
            WasSkipped = false;
            m_advance = false;
            m_baseScale = 1f;
            m_opening = true;
            m_stageMode = false;
            m_venueMode = false;
            m_finalMode = false;
            m_arenaMode = false;
            m_nightRoomMode = false;
            m_ch4FinalMode = true;
            m_finalScene = "stage";
            StopDialogueFade();

            Show();
            SetCh4FinalsMood(false);
            SetCh4VipDoorOpen(false);
            SetDarkMood(false);
            m_fadeGroup.alpha = 1f;
            SetCaption(false);
            SetTitle(false);
            SetHint(true);
            SetMemory(false);
            SetCinema(false);

            string defaultCaption = chapter != null && chapter.cutscene != null && !string.IsNullOrEmpty(chapter.cutscene.caption)
                ? chapter.cutscene.caption
                : "全国大会 決勝";
            SetCaptionText(defaultCaption);
            UseStageCamera();
            SetStageWide();
            yield return FadeTo(0f, 0.5f);
            SetCaption(!string.IsNullOrEmpty(defaultCaption));

            var shots = chapter != null && chapter.cutscene != null ? chapter.cutscene.shots : null;
            if (shots == null || shots.Count == 0)
            {
                UseStageCamera();
                SetStageWide();
                yield return Hold(3.0f);
                if (!m_skipped) { ApplyConversationShot("S_DARUKING_VIP"); yield return Hold(3.0f); }
                if (!m_skipped) { SetStageRivals(chapter); yield return Hold(3.4f); }
            }
            else
            {
                foreach (var shot in shots)
                {
                    if (shot == null) continue;
                    string caption = string.IsNullOrEmpty(shot.caption) ? defaultCaption : shot.caption;
                    SetCaptionText(caption);
                    SetCaption(!string.IsNullOrEmpty(caption));

                    switch (shot.view)
                    {
                        case "vip_daruking":
                        case "S_DARUKING_VIP":
                            ApplyConversationShot("S_DARUKING_VIP");
                            break;
                        case "rivals":
                            SetStageRivals(chapter);
                            break;
                        default:
                            UseStageCamera();
                            SetStageWide();
                            break;
                    }
                    yield return Hold(Mathf.Max(0.5f, shot.seconds));
                    if (m_skipped) break;
                }
            }

            SetCaption(false);
            if (!m_skipped)
            {
                SetCinema(true);
                yield return FadeTo(0.65f, 0.5f);
                var card = (RectTransform)m_titleGroup.transform;
                card.localScale = Vector3.one * 1.06f;
                SetTitle(true);
                yield return Tween(0.8f, u => card.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, u));
                yield return Hold(1.4f);
                SetTitle(false);
            }

            yield return FinishCh4FinalIntro();
        }

        IEnumerator FinishCh4FinalIntro()
        {
            m_opening = false;
            SetHint(false);
            SetCaption(false);
            SetTitle(false);
            SetMemory(false);
            SetCinema(false);
            m_baseScale = 1f;
            m_advance = false;
            WasSkipped = m_skipped;
            RestoreRunner();
            yield return FadeTo(1f, 0.35f);
            ShowCh4FinalConversation(false);
            yield return FadeTo(0f, 0.6f);
            m_skipped = false;
        }

        public void ShowCh4FinalConversation(bool sunset)
        {
            BuildCh4FinalsSet();
            Show();
            StopDialogueFade();
            m_stageMode = false;
            m_venueMode = false;
            m_finalMode = false;
            m_arenaMode = false;
            m_nightRoomMode = false;
            m_ch4FinalMode = true;
            m_finalScene = "stage";
            m_opening = false;
            m_advance = false;
            m_baseScale = 1f;
            SetDarkMood(false);
            SetCh4FinalsMood(sunset);
            SetCh4VipDoorOpen(false);
            SetCinema(true);
            SetCaption(false);
            SetTitle(false);
            SetHint(false);
            UseStageCamera();
            SetShot(lt => W(0f, 10.5f, -9.0f), lt => W(0f, 1.2f, 0f), 52f);
        }

        void HandleCh4DialogueLine(StoryLine line)
        {
            if (line == null) return;

            if (!string.IsNullOrEmpty(line.cue))
            {
                if (line.cue == "daruking_descend")
                {
                    SetCh4VipDoorOpen(true);
                    SetDarkMood(true);
                }
                else
                {
                    ApplyDialogueCue(line.cue);
                }
            }

            if (!string.IsNullOrEmpty(line.shot))
            {
                if (line.shot == "S_FINAL_WIDE")
                {
                    UseStageCamera();
                    SetShot(lt => W(0f, 10.5f, -9.0f), lt => W(0f, 1.2f, 0f), 52f);
                }
                else
                {
                    ApplyConversationShot(line.shot);
                }
                return;
            }

            UseStageCamera();
            FocusStageSpeaker(line.speaker);
        }

        //========================================
        // 最終章（二つで足りる王）：トゥルー／バッド専用演出セット
        //========================================

        Transform FinalPrim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var t = ToyKit.Primitive(type, name, parent);
            t.localPosition = localPos;
            t.localScale = scale;
            t.localRotation = Quaternion.Euler(euler);
            var r = t.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            return t;
        }

        void BuildFinalEndingSet()
        {
            if (m_finalEndBuilt) return;
            m_finalEndBuilt = true;

            m_finalEndRoot = new GameObject("StoryFinalEndingSet").transform;
            m_finalEndRoot.position = FinalEndingOrigin;

            var stageFloorMat = ToyKit.Lit(new Color(0.60f, 0.50f, 0.38f), 0.25f);
            var carpetMat = ToyKit.Lit(new Color(0.62f, 0.10f, 0.14f), 0.25f);
            var goldMat = ToyKit.Lit(new Color(0.92f, 0.72f, 0.24f), 0.75f);
            var purpleMat = ToyKit.Lit(new Color(0.45f, 0.22f, 0.62f), 0.5f);
            var silverMat = ToyKit.Lit(new Color(0.88f, 0.90f, 0.96f), 0.85f);
            var darkStoneMat = ToyKit.Lit(new Color(0.10f, 0.10f, 0.12f), 0.2f);
            var shadowMat = Unlit(new Color(0.05f, 0.05f, 0.07f));
            var shardMat = ToyKit.Lit(new Color(0.18f, 0.16f, 0.20f), 0.25f);
            var woodMat = ToyKit.Lit(new Color(0.42f, 0.28f, 0.16f), 0.25f);
            var ironMat = ToyKit.Lit(new Color(0.22f, 0.20f, 0.22f), 0.35f);

            // ---- 1. トゥルーエンド：王の剣投擲＆打ち返し［パリィ］＆王座・盾の崩壊エリア（x = 0） ----
            FinalPrim(PrimitiveType.Cube, "ParryFloor", m_finalEndRoot, new Vector3(0f, -0.25f, 0f), new Vector3(14f, 0.5f, 9f), stageFloorMat);
            FinalPrim(PrimitiveType.Cube, "ParryCarpet", m_finalEndRoot, new Vector3(0f, 0.02f, 0f), new Vector3(11f, 0.04f, 2.8f), carpetMat);
            TitleDressingKit.Quad(m_finalEndRoot, "ParrySky", FinalEndingOrigin + new Vector3(0f, 6f, 15f), new Vector3(44f, 22f, 1f), m_skySunset);

            // あかだる（左、+X 向き）とだるきんぐ（右、-X 向き）
            m_parryAkadaru = AddGameVisualMember("akadaru", new Vector3(-2.6f, 0f, 0f), 90f, Nakahira.TumikiColor.Red, new Color(0.85f, 0.22f, 0.18f), m_finalEndRoot, 3, true);
            m_parryDaruking = AddGameVisualMember("daruking", new Vector3(2.4f, 0f, 0f), -90f, Nakahira.TumikiColor.Red, new Color(0.55f, 0.35f, 0.70f), m_finalEndRoot, 2, false);

            // 王座と盾（競技・権威の象徴。パリィの衝撃でこれらが砕け散る。流血・負傷は一切描かない）
            m_parryThrone = new GameObject("KingThrone").transform;
            m_parryThrone.SetParent(m_finalEndRoot, false);
            m_parryThrone.localPosition = new Vector3(3.6f, 0f, 0f);
            m_parryDebris.Clear();
            m_parryDebrisBasePos.Clear();
            m_parryDebrisVel.Clear();

            AddParryDebris(FinalPrim(PrimitiveType.Cube, "ThroneSeat", m_parryThrone, new Vector3(0f, 0.45f, 0f), new Vector3(1.1f, 0.9f, 1.5f), carpetMat), new Vector3(1.8f, 1.4f, -1.2f));
            AddParryDebris(FinalPrim(PrimitiveType.Cube, "ThroneBack", m_parryThrone, new Vector3(0.45f, 1.65f, 0f), new Vector3(0.24f, 2.6f, 1.55f), carpetMat), new Vector3(2.8f, 2.4f, 0.6f));
            AddParryDebris(FinalPrim(PrimitiveType.Cube, "ThroneFrameL", m_parryThrone, new Vector3(0.42f, 1.65f, -0.82f), new Vector3(0.30f, 2.75f, 0.18f), goldMat), new Vector3(2.2f, 2.8f, -2.1f));
            AddParryDebris(FinalPrim(PrimitiveType.Cube, "ThroneFrameR", m_parryThrone, new Vector3(0.42f, 1.65f, 0.82f), new Vector3(0.30f, 2.75f, 0.18f), goldMat), new Vector3(2.4f, 2.5f, 2.2f));
            AddParryDebris(FinalPrim(PrimitiveType.Cube, "ThroneCrown", m_parryThrone, new Vector3(0.42f, 3.05f, 0f), new Vector3(0.32f, 0.24f, 1.85f), goldMat), new Vector3(1.9f, 3.6f, 0.2f));

            m_parryShield = new GameObject("KingShield").transform;
            m_parryShield.SetParent(m_finalEndRoot, false);
            m_parryShield.localPosition = new Vector3(1.55f, 1.15f, -0.45f);
            AddParryDebris(FinalPrim(PrimitiveType.Cube, "ShieldTop", m_parryShield, new Vector3(0f, 0.34f, 0f), new Vector3(0.14f, 0.62f, 0.92f), purpleMat), new Vector3(1.2f, 2.9f, -1.8f));
            AddParryDebris(FinalPrim(PrimitiveType.Cube, "ShieldBottom", m_parryShield, new Vector3(0f, -0.32f, 0f), new Vector3(0.14f, 0.58f, 0.72f), goldMat), new Vector3(1.6f, 1.9f, 1.5f));
            // 崩れる積み木破片（王の2段の象徴）
            AddParryDebris(FinalPrim(PrimitiveType.Cylinder, "KingBlockBreak0", m_finalEndRoot, new Vector3(2.4f, 0.25f, 0f), new Vector3(0.95f, 0.24f, 0.95f), purpleMat), new Vector3(2.5f, 2.1f, -2.4f));
            AddParryDebris(FinalPrim(PrimitiveType.Cylinder, "KingBlockBreak1", m_finalEndRoot, new Vector3(2.4f, 0.75f, 0f), new Vector3(0.95f, 0.24f, 0.95f), goldMat), new Vector3(2.1f, 3.0f, 2.3f));

            // 投擲される王の剣（仮モデル）
            m_parrySword = new GameObject("KingThrownSword").transform;
            m_parrySword.SetParent(m_finalEndRoot, false);
            FinalPrim(PrimitiveType.Cube, "Blade", m_parrySword, new Vector3(-0.65f, 0f, 0f), new Vector3(1.35f, 0.10f, 0.20f), silverMat);
            FinalPrim(PrimitiveType.Cube, "Guard", m_parrySword, new Vector3(0.05f, 0f, 0f), new Vector3(0.12f, 0.14f, 0.56f), goldMat);
            FinalPrim(PrimitiveType.Cylinder, "Grip", m_parrySword, new Vector3(0.32f, 0f, 0f), new Vector3(0.10f, 0.24f, 0.10f), purpleMat, new Vector3(0f, 0f, 90f));
            m_parrySword.gameObject.SetActive(false);

            // パリィの閃光球
            m_parryFlash = FinalPrim(PrimitiveType.Sphere, "ParryFlash", m_finalEndRoot, new Vector3(-1.3f, 1.35f, 0f), Vector3.one * 0.1f, Unlit(new Color(1f, 0.98f, 0.85f)));
            m_parryFlash.gameObject.SetActive(false);

            AddFinalLight("ParryKey", new Vector3(-1.5f, 5.2f, -4.5f), new Color(1.0f, 0.82f, 0.58f), 22f, 24f);
            AddFinalLight("ParryRim", new Vector3(3.0f, 4.0f, 3.0f), new Color(0.82f, 0.66f, 1.0f), 14f, 18f);

            // ---- 2. トゥルーエンド：部室の飾り（壁の父の槌＋「新生王冠大会 開幕！」横断幕＋ゲスト2人） ----
            BuildTrueRoomExtras(woodMat, ironMat, goldMat);

            // ---- 3. バッドエンド：闇落ち・闘技場崩壊・黒い影エリア（x = +40 / +80 / +120） ----
            BuildBadEndingAreas(darkStoneMat, shadowMat, shardMat, woodMat, ironMat);

            RegisterFinalEndingShots();
            ResetFinalEndingVisuals();
        }

        void AddParryDebris(Transform t, Vector3 vel)
        {
            m_parryDebris.Add(t);
            m_parryDebrisBasePos.Add(t.localPosition);
            m_parryDebrisVel.Add(vel);
        }

        void AddFinalLight(string name, Vector3 localPos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_finalEndRoot, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        void BuildTrueRoomExtras(Material woodMat, Material ironMat, Material goldMat)
        {
            // 部室の壁（WallBack z=4.84 付近）に飾る父の槌と「新生王冠大会 開幕！」の横断幕
            m_trueRoomPropsRoot = new GameObject("TrueEndRoomWallProps").transform;
            m_trueRoomPropsRoot.SetParent(m_root, false);

            // 左側：父の古い槌を飾る木製額＋槌
            FinalPrim(PrimitiveType.Cube, "HammerPlaque", m_trueRoomPropsRoot, new Vector3(-3.3f, 2.75f, 4.78f), new Vector3(1.55f, 1.05f, 0.06f), woodMat);
            FinalPrim(PrimitiveType.Cube, "HammerPlaqueTrim", m_trueRoomPropsRoot, new Vector3(-3.3f, 2.75f, 4.75f), new Vector3(1.42f, 0.92f, 0.03f), Unlit(new Color(0.95f, 0.91f, 0.82f)));
            var wallHammer = new GameObject("WallFatherHammer").transform;
            wallHammer.SetParent(m_trueRoomPropsRoot, false);
            wallHammer.localPosition = new Vector3(-3.3f, 2.75f, 4.65f);
            wallHammer.localRotation = Quaternion.Euler(0f, 0f, 18f);
            FinalPrim(PrimitiveType.Cylinder, "Handle", wallHammer, new Vector3(-0.22f, 0f, 0f), new Vector3(0.08f, 0.44f, 0.08f), woodMat, new Vector3(0f, 0f, 90f));
            FinalPrim(PrimitiveType.Cylinder, "Head", wallHammer, new Vector3(0.24f, 0f, 0f), new Vector3(0.24f, 0.24f, 0.24f), ironMat, new Vector3(90f, 0f, 0f));

            // 横：横断幕「新生王冠大会 開幕！」
            FinalPrim(PrimitiveType.Cube, "TrueBannerFrame", m_trueRoomPropsRoot, new Vector3(0.25f, 3.05f, 4.78f), new Vector3(4.3f, 1.35f, 0.05f), goldMat);
            FinalPrim(PrimitiveType.Cube, "TrueBannerCloth", m_trueRoomPropsRoot, new Vector3(0.25f, 3.05f, 4.74f), new Vector3(4.12f, 1.18f, 0.04f), Unlit(new Color(0.80f, 0.14f, 0.12f)));
            var bannerText = TitleDressingKit.Text(m_trueRoomPropsRoot, PresentationSettings.DefaultFont,
                "新生王冠大会 開幕！", Vector3.zero, new Vector2(3.9f, 1.0f), 3.6f,
                new Color(1f, 0.96f, 0.84f), new Color(0.28f, 0.04f, 0.03f), 0.06f, 0f);
            bannerText.transform.localPosition = new Vector3(0.25f, 3.05f, 4.70f);
            bannerText.transform.localRotation = Quaternion.identity;
            bannerText.fontStyle = FontStyles.Bold;
            bannerText.alignment = TextAlignmentOptions.Center;

            m_trueRoomPropsRoot.gameObject.SetActive(false);

            // 部室ゲスト（いわだる・へびだる）：トゥルーエンドの部室場面だけ表示
            m_roomGuestIwadaru = AddGameVisualMember("iwadaru", new Vector3(-3.35f, 0f, 1.85f), 145f, Nakahira.TumikiColor.Red, new Color(0.50f, 0.47f, 0.42f), m_root, 3, true);
            m_roomGuestHebidaru = AddGameVisualMember("hebidaru", new Vector3(3.35f, 0f, 0.75f), -145f, Nakahira.TumikiColor.Green, new Color(0.42f, 0.75f, 0.27f), m_root, 3, true);
            m_roomMembers["iwadaru"] = m_roomGuestIwadaru;
            m_roomMembers["hebidaru"] = m_roomGuestHebidaru;
            if (m_roomGuestIwadaru.root != null) m_roomGuestIwadaru.root.gameObject.SetActive(false);
            if (m_roomGuestHebidaru.root != null) m_roomGuestHebidaru.root.gameObject.SetActive(false);
        }

        void BuildBadEndingAreas(Material darkStoneMat, Material shadowMat, Material shardMat, Material woodMat, Material ironMat)
        {
            // A. 闇落ちエリア（x = +40）：あかだるの赤い頭が黒ずんでいき、積み木の欠片が全身にまとわりつく
            Vector3 corruptBase = new Vector3(40f, 0f, 0f);
            m_badCorruptRoot = new GameObject("BadCorruptArea").transform;
            m_badCorruptRoot.SetParent(m_finalEndRoot, false);
            m_badCorruptRoot.localPosition = corruptBase;
            FinalPrim(PrimitiveType.Cube, "DarkFloor", m_badCorruptRoot, new Vector3(0f, -0.25f, 0f), new Vector3(14f, 0.5f, 14f), darkStoneMat);

            // 影の体＋赤から黒へ変色する頭
            for (int i = 0; i < 2; i++)
                FinalPrim(PrimitiveType.Cylinder, "DarkBody" + i, m_badCorruptRoot, new Vector3(0f, 0.25f + i * 0.5f, 0f), new Vector3(0.92f, 0.24f, 0.92f), shadowMat);
            m_badHeadMat = new Material(ToyKit.Lit(new Color(0.88f, 0.22f, 0.18f), 0.35f));
            var badHead = FinalPrim(PrimitiveType.Sphere, "CorruptHead", m_badCorruptRoot, new Vector3(0f, 1.48f, 0f), new Vector3(0.95f, 0.90f, 0.95f), m_badHeadMat);
            m_badHeadRenderer = badHead.GetComponent<Renderer>();
            // 父の古い槌（右手に傾けて持つシルエット）
            var badHammer = new GameObject("CorruptHammer").transform;
            badHammer.SetParent(m_badCorruptRoot, false);
            badHammer.localPosition = new Vector3(0.75f, 0.72f, -0.1f);
            badHammer.localRotation = Quaternion.Euler(0f, 0f, -24f);
            FinalPrim(PrimitiveType.Cylinder, "Handle", badHammer, new Vector3(0f, 0.35f, 0f), new Vector3(0.08f, 0.45f, 0.08f), woodMat);
            FinalPrim(PrimitiveType.Cylinder, "Head", badHammer, new Vector3(0f, 0.80f, 0f), new Vector3(0.24f, 0.24f, 0.24f), ironMat, new Vector3(0f, 0f, 90f));

            // 全身にまとわりつく積み木の欠片（16個）
            m_badSwirlShards.Clear();
            for (int i = 0; i < 16; i++)
            {
                var shard = FinalPrim(PrimitiveType.Cube, "Shard_" + i, m_badCorruptRoot, Vector3.zero, new Vector3(0.22f, 0.14f, 0.22f), shardMat);
                m_badSwirlShards.Add(shard);
            }
            AddFinalLight("BadCorruptSpot", corruptBase + new Vector3(0f, 3.8f, -2.5f), new Color(0.68f, 0.62f, 0.74f), 9f, 12f);

            // B. 闘技場崩壊エリア（x = +80）：積み木の塔が次々と崩れていく
            Vector3 collapseBase = new Vector3(80f, 0f, 0f);
            m_badCollapseRoot = new GameObject("BadCollapseArea").transform;
            m_badCollapseRoot.SetParent(m_finalEndRoot, false);
            m_badCollapseRoot.localPosition = collapseBase;
            FinalPrim(PrimitiveType.Cube, "CollapseFloor", m_badCollapseRoot, new Vector3(0f, -0.25f, 0f), new Vector3(16f, 0.5f, 14f), darkStoneMat);
            m_badTowerBlocks.Clear();
            m_badTowerBasePos.Clear();
            float[] towerXs = { -3.2f, 0f, 3.2f };
            for (int tIdx = 0; tIdx < towerXs.Length; tIdx++)
            {
                for (int h = 0; h < 5; h++)
                {
                    Vector3 bp = new Vector3(towerXs[tIdx], 0.26f + h * 0.52f, (tIdx - 1) * 0.8f);
                    var blk = FinalPrim(PrimitiveType.Cylinder, $"Tower_{tIdx}_{h}", m_badCollapseRoot, bp, new Vector3(1.1f, 0.25f, 1.1f), shardMat);
                    m_badTowerBlocks.Add(blk);
                    m_badTowerBasePos.Add(bp);
                }
            }
            AddFinalLight("BadCollapseLight", collapseBase + new Vector3(0f, 5.2f, -4.0f), new Color(0.55f, 0.52f, 0.64f), 11f, 18f);

            // C. 静寂・崩れた積み木の山に一人立つ黒い影エリア（x = +120）
            Vector3 pileBase = new Vector3(120f, 0f, 0f);
            m_badShadowRoot = new GameObject("BadShadowPileArea").transform;
            m_badShadowRoot.SetParent(m_finalEndRoot, false);
            m_badShadowRoot.localPosition = pileBase;
            FinalPrim(PrimitiveType.Cube, "PileFloor", m_badShadowRoot, new Vector3(0f, -0.25f, 0f), new Vector3(16f, 0.5f, 14f), darkStoneMat);
            // 崩れた積み木の山
            var rng = new System.Random(20261127);
            for (int i = 0; i < 18; i++)
            {
                float px = (float)(rng.NextDouble() * 5.2 - 2.6);
                float pz = (float)(rng.NextDouble() * 3.0 - 1.5);
                float py = Mathf.Max(0.22f, 1.05f - (Mathf.Abs(px) * 0.28f + Mathf.Abs(pz) * 0.22f));
                FinalPrim(PrimitiveType.Cylinder, "PileBlock_" + i, m_badShadowRoot,
                    new Vector3(px, py, pz), new Vector3(1.0f, 0.24f, 1.0f), shardMat,
                    new Vector3((float)(rng.NextDouble() * 36 - 18), (float)(rng.NextDouble() * 360), (float)(rng.NextDouble() * 36 - 18)));
            }
            // 山の上に一人立つ黒い影（あかだる自身の影＋父の古い槌）
            var loneShadow = new GameObject("LoneDarkShadow").transform;
            loneShadow.SetParent(m_badShadowRoot, false);
            loneShadow.localPosition = new Vector3(0f, 1.15f, 0f);
            for (int i = 0; i < 2; i++)
                FinalPrim(PrimitiveType.Cylinder, "Body" + i, loneShadow, new Vector3(0f, 0.25f + i * 0.5f, 0f), new Vector3(0.86f, 0.24f, 0.86f), shadowMat);
            FinalPrim(PrimitiveType.Sphere, "Head", loneShadow, new Vector3(0f, 1.44f, 0f), new Vector3(0.92f, 0.88f, 0.92f), shadowMat);
            FinalPrim(PrimitiveType.Cylinder, "ShadowHammerHandle", loneShadow, new Vector3(0.68f, 0.55f, 0f), new Vector3(0.07f, 0.55f, 0.07f), shadowMat, new Vector3(0f, 0f, -15f));
            FinalPrim(PrimitiveType.Cylinder, "ShadowHammerHead", loneShadow, new Vector3(0.82f, 0.12f, 0f), new Vector3(0.22f, 0.22f, 0.22f), shadowMat, new Vector3(90f, 0f, 0f));
            // 後ろからの淡い逆光で黒い影の輪郭だけ浮かせる
            AddFinalLight("BadShadowRim", pileBase + new Vector3(0f, 3.2f, 3.8f), new Color(0.55f, 0.56f, 0.68f), 14f, 16f);
        }

        void RegisterFinalEndingShots()
        {
            if (m_shots.ContainsKey("S_FINAL_PARRY")) return;

            RegisterShot(new ShotDefinition
            {
                id = "S_FINAL_PARRY",
                target = m_finalEndRoot,
                positionOffset = new Vector3(-0.15f, 2.35f, -6.2f),
                lookOffset = new Vector3(0.2f, 1.25f, 0f),
                fov = 42f,
            });
            RegisterShot(new ShotDefinition
            {
                id = "S_FINAL_KING_EXIT",
                target = m_finalEndRoot,
                positionOffset = new Vector3(0.4f, 2.0f, -5.0f),
                lookOffset = new Vector3(2.4f, 1.15f, 0f),
                fov = 38f,
            });
            RegisterShot(new ShotDefinition
            {
                id = "S_ROOM_TRUE_WIDE",
                target = m_root,
                positionOffset = new Vector3(0f, 2.35f, -4.1f),
                lookOffset = new Vector3(0f, 1.35f, 1.5f),
                fov = 48f,
                sway = true,
                memberVisibility = 1,
            });
            RegisterShot(new ShotDefinition
            {
                id = "S_ROOM_TRUE_IWA",
                target = m_root,
                positionOffset = new Vector3(-1.35f, 1.75f, -0.35f),
                lookOffset = new Vector3(-3.35f, 1.35f, 1.85f),
                fov = 38f,
                memberVisibility = 1,
            });
            RegisterShot(new ShotDefinition
            {
                id = "S_ROOM_TRUE_HEBI",
                target = m_root,
                positionOffset = new Vector3(1.15f, 1.75f, -1.25f),
                lookOffset = new Vector3(3.35f, 1.35f, 0.75f),
                fov = 38f,
                memberVisibility = 1,
            });
            RegisterShot(new ShotDefinition
            {
                id = "S_ROOM_TRUE_WALL",
                target = m_root,
                positionOffset = new Vector3(-1.1f, 2.75f, 0.1f),
                lookOffset = new Vector3(-1.0f, 2.90f, 4.75f),
                fov = 44f,
                sway = true,
                memberVisibility = 1,
            });
            RegisterShot(new ShotDefinition
            {
                id = "S_BAD_CORRUPT",
                target = m_finalEndRoot,
                positionOffset = new Vector3(40.0f, 1.85f, -3.4f),
                lookOffset = new Vector3(40.0f, 1.25f, 0f),
                fov = 38f,
            });
            RegisterShot(new ShotDefinition
            {
                id = "S_BAD_COLLAPSE",
                target = m_finalEndRoot,
                positionOffset = new Vector3(80.0f, 3.1f, -7.2f),
                lookOffset = new Vector3(80.0f, 1.4f, 0f),
                fov = 46f,
            });
            RegisterShot(new ShotDefinition
            {
                id = "S_BAD_SHADOW_PILE",
                target = m_finalEndRoot,
                positionOffset = new Vector3(120.0f, 2.55f, -6.2f),
                lookOffset = new Vector3(120.0f, 1.65f, 0f),
                fov = 42f,
                sway = true,
            });
        }

        void ResetFinalEndingVisuals()
        {
            m_audStandStart = -1f;
            m_swordThrowStart = -1f;
            m_parryImpactStart = -1f;
            m_kingExitStart = -1f;
            m_parryShatterPlayed = false;
            m_badCorruptStart = -1f;
            m_badCollapseStart = -1f;
            if (m_trueRoomPropsRoot != null) m_trueRoomPropsRoot.gameObject.SetActive(false);
            if (m_wallPaper != null) m_wallPaper.gameObject.SetActive(true);
            if (m_roomGuestIwadaru != null && m_roomGuestIwadaru.root != null) m_roomGuestIwadaru.root.gameObject.SetActive(false);
            if (m_roomGuestHebidaru != null && m_roomGuestHebidaru.root != null) m_roomGuestHebidaru.root.gameObject.SetActive(false);
            if (m_parrySword != null) m_parrySword.gameObject.SetActive(false);
            if (m_parryFlash != null) m_parryFlash.gameObject.SetActive(false);
            if (m_parryDaruking != null && m_parryDaruking.root != null)
            {
                m_parryDaruking.root.gameObject.SetActive(true);
                m_parryDaruking.root.localPosition = new Vector3(2.4f, 0f, 0f);
                m_parryDaruking.root.localRotation = Quaternion.Euler(0f, -90f, 0f);
            }
            for (int i = 0; i < m_parryDebris.Count && i < m_parryDebrisBasePos.Count; i++)
            {
                if (m_parryDebris[i] == null) continue;
                m_parryDebris[i].gameObject.SetActive(true);
                m_parryDebris[i].localPosition = m_parryDebrisBasePos[i];
                m_parryDebris[i].localRotation = Quaternion.identity;
            }
            if (m_badHeadMat != null) m_badHeadMat.color = new Color(0.88f, 0.22f, 0.18f);
            for (int i = 0; i < m_badTowerBlocks.Count && i < m_badTowerBasePos.Count; i++)
            {
                if (m_badTowerBlocks[i] == null) continue;
                m_badTowerBlocks[i].gameObject.SetActive(true);
                m_badTowerBlocks[i].localPosition = m_badTowerBasePos[i];
                m_badTowerBlocks[i].localRotation = Quaternion.identity;
            }
            for (int i = 0; i < m_ch4AudMembers.Count; i++)
            {
                var m = m_ch4AudMembers[i];
                if (m != null && m.root != null) m.root.localPosition = m.basePos;
            }
        }

        // トゥルーエンド：暗転＋テロップ「数日後 ― 七転高校 部室」を挟んで部室へ切り替える
        public IEnumerator TransitionToTrueRoom(string captionText = "数日後 ― 七転高校 部室")
        {
            BuildFinalEndingSet();
            StopDialogueFade();
            yield return FadeTo(1f, 0.45f);

            m_finalScene = "room";
            m_stageMode = false;
            UseRoomCamera();
            SetDarkMood(false);
            SetNightRoom(false);
            SetSunset(false);
            if (m_doll != null) m_doll.gameObject.SetActive(false);
            if (m_wallPaper != null) m_wallPaper.gameObject.SetActive(false);
            if (m_trueRoomPropsRoot != null) m_trueRoomPropsRoot.gameObject.SetActive(true);
            SetRoomMembersVisible(true);
            if (m_roomGuestIwadaru != null && m_roomGuestIwadaru.root != null) m_roomGuestIwadaru.root.gameObject.SetActive(true);
            if (m_roomGuestHebidaru != null && m_roomGuestHebidaru.root != null) m_roomGuestHebidaru.root.gameObject.SetActive(true);
            ApplyConversationShot("S_ROOM_TRUE_WIDE");

            // 暗転中にテロップを静かに表示する
            SetCaptionText(captionText);
            SetCaption(true);
            yield return new WaitForSecondsRealtime(1.8f);
            SetCaption(false);

            yield return FadeTo(0f, 0.55f);
        }

        void HandleFinalDialogueLine(StoryLine line)
        {
            if (line == null) return;
            BuildCh4FinalsSet();
            BuildFinalEndingSet();

            if (!string.IsNullOrEmpty(line.cue))
                ApplyFinalCue(line.cue);

            if (!string.IsNullOrEmpty(line.shot))
            {
                if (line.shot == "S_FINAL_WIDE")
                {
                    m_finalScene = "stage";
                    m_stageMode = true;
                    UseStageCamera();
                    SetShot(lt => W(0f, 10.5f, -9.0f), lt => W(0f, 1.2f, 0f), 52f);
                    return;
                }
                if (line.shot == "S_FINAL_FACEOFF")
                {
                    m_finalScene = "stage";
                    m_stageMode = true;
                    SetStageFaceoff(m_chapter);
                    return;
                }
                if (line.shot.StartsWith("S_AUDIENCE_"))
                {
                    UseCh4SetCamera();
                    ApplyConversationShot(line.shot);
                    return;
                }
                if (line.shot.StartsWith("S_FINAL_") || line.shot.StartsWith("S_BAD_"))
                {
                    m_finalScene = "ending";
                    m_stageMode = false;
                    UseArenaCamera();
                    if (line.shot.StartsWith("S_FINAL_"))
                        m_camera.backgroundColor = new Color(0.24f, 0.14f, 0.16f);
                    ApplyConversationShot(line.shot);
                    return;
                }
                if (line.shot.StartsWith("S_ROOM_") || line.shot == "S_AKA" || line.shot == "S_AO" || line.shot == "S_MI" || line.shot == "S_KIDARU")
                {
                    m_finalScene = "room";
                    m_stageMode = false;
                    UseRoomCamera();
                    ApplyConversationShot(line.shot);
                    return;
                }
            }

            if (m_finalScene == "room")
            {
                UseRoomCamera();
                FocusRoomSpeaker(line.speaker);
            }
            else if (m_finalScene == "stage")
            {
                UseStageCamera();
                FocusStageSpeaker(line.speaker);
            }
        }

        void ApplyFinalCue(string cue)
        {
            switch (cue)
            {
                case "audience_stand":
                    m_audStandStart = m_time;
                    break;

                case "king_sword_throw":
                    ResetFinalEndingVisuals();
                    m_finalScene = "ending";
                    m_stageMode = false;
                    m_swordThrowStart = m_time;
                    if (m_parrySword != null)
                    {
                        m_parrySword.gameObject.SetActive(true);
                        m_parrySword.localPosition = new Vector3(1.8f, 1.35f, 0f);
                        m_parrySword.localRotation = Quaternion.identity;
                    }
                    break;

                case "parry_shatter":
                    m_finalScene = "ending";
                    m_stageMode = false;
                    m_swordThrowStart = -1f;
                    m_parryImpactStart = m_time;
                    m_parryShatterPlayed = false;
                    m_camShake = 0.42f;
                    m_fovPunch = 6f;
                    if (m_parrySword != null)
                    {
                        m_parrySword.gameObject.SetActive(true);
                        m_parrySword.localPosition = new Vector3(-1.35f, 1.35f, 0f);
                        m_parrySword.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    }
                    if (m_parryFlash != null)
                    {
                        m_parryFlash.gameObject.SetActive(true);
                        m_parryFlash.localScale = Vector3.one * 0.25f;
                    }
                    // ヒットストップ（一瞬だけ時間を遅らせてパリィの手応えを作る）
                    if (m_slowMo != null) StopCoroutine(m_slowMo);
                    m_slowMo = StartCoroutine(SlowMo(0.08f, 0.14f));
                    break;

                case "king_exit":
                    m_kingExitStart = m_time;
                    break;

                case "bad_strike_blackout":
                case "bad_blackout_only":
                case "bad_ending_card":
                    StopDialogueFade();
                    m_finalScene = "ending";
                    m_stageMode = false;
                    if (m_fadeGroup != null) m_fadeGroup.alpha = 1f;
                    break;

                case "bad_corrupt_start":
                    StopDialogueFade();
                    m_finalScene = "ending";
                    m_stageMode = false;
                    m_badCorruptStart = m_time;
                    if (m_badHeadMat != null) m_badHeadMat.color = new Color(0.88f, 0.22f, 0.18f);
                    m_dialogueFadeRoutine = StartCoroutine(FadeDialogueTo(0.12f, 0.7f));
                    break;

                case "bad_arena_collapse":
                    StopDialogueFade();
                    m_finalScene = "ending";
                    m_stageMode = false;
                    m_badCollapseStart = m_time;
                    m_camShake = 0.25f;
                    m_dialogueFadeRoutine = StartCoroutine(FadeDialogueTo(0.15f, 0.45f));
                    break;

                case "bad_shadow_pile":
                    StopDialogueFade();
                    m_finalScene = "ending";
                    m_stageMode = false;
                    m_dialogueFadeRoutine = StartCoroutine(FadeDialogueTo(0.18f, 0.65f));
                    break;

                default:
                    ApplyDialogueCue(cue);
                    break;
            }
        }

        void AnimateFinalEnding()
        {
            // 1. 客席3人の立ち上がりアニメーション
            if (m_audStandStart >= 0f)
            {
                float u = Mathf.Clamp01((m_time - m_audStandStart) / 0.45f);
                float lift = Mathf.Sin(u * Mathf.PI) * 0.35f + 0.18f * u;
                for (int i = 0; i < m_ch4AudMembers.Count; i++)
                {
                    var m = m_ch4AudMembers[i];
                    if (m != null && m.root != null)
                        m.root.localPosition = m.basePos + new Vector3(0f, lift, 0f);
                }
            }

            // 2. 王の剣の投擲アニメーション
            if (m_swordThrowStart >= 0f && m_parrySword != null)
            {
                float u = Mathf.Clamp01((m_time - m_swordThrowStart) / 0.55f);
                float ease = u * u;
                m_parrySword.localPosition = Vector3.Lerp(new Vector3(1.8f, 1.35f, 0f), new Vector3(-1.35f, 1.35f, 0f), ease);
                m_parrySword.localRotation = Quaternion.Euler(0f, 0f, u * 12f);
            }

            // 3. パリィ打ち返し → 王座と盾・積み木が砕け散るアニメーション（流血・負傷描写なし）
            if (m_parryImpactStart >= 0f)
            {
                float t = m_time - m_parryImpactStart;
                // 閃光
                if (m_parryFlash != null)
                {
                    float fu = Mathf.Clamp01(t / 0.28f);
                    m_parryFlash.gameObject.SetActive(fu < 1f);
                    float scale = Mathf.Lerp(0.25f, 2.8f, fu);
                    m_parryFlash.localScale = Vector3.one * scale;
                }
                // 跳ね返った剣が王座と盾へ向かう（0〜0.26秒）
                if (m_parrySword != null)
                {
                    float su = Mathf.Clamp01(t / 0.26f);
                    m_parrySword.localPosition = Vector3.Lerp(new Vector3(-1.35f, 1.35f, 0f), new Vector3(2.8f, 1.45f, 0f), su);
                    m_parrySword.localRotation = Quaternion.Euler(0f, 180f, -su * 360f);
                    if (su >= 1f) m_parrySword.gameObject.SetActive(false);
                }
                // 衝突後（0.26秒〜）：王座・盾・積み木が砕け散り、王が崩れ落ちる
                if (t >= 0.26f)
                {
                    if (!m_parryShatterPlayed)
                    {
                        m_parryShatterPlayed = true;
                        m_camShake = 0.52f;
                        PreludeSfx.Play(PreludeSfx.Shatter, this);
                    }
                    float du = Mathf.Clamp01((t - 0.26f) / 1.15f);
                    for (int i = 0; i < m_parryDebris.Count && i < m_parryDebrisBasePos.Count; i++)
                    {
                        var d = m_parryDebris[i];
                        if (d == null) continue;
                        var v = m_parryDebrisVel[i];
                        d.localPosition = m_parryDebrisBasePos[i] + new Vector3(v.x * du, v.y * du - 3.6f * du * du, v.z * du);
                        d.localRotation = Quaternion.Euler(du * 220f * (i % 2 == 0 ? 1 : -1), du * 160f, du * 190f);
                        if (du >= 1f) d.gameObject.SetActive(false);
                    }
                    if (m_parryDaruking != null && m_parryDaruking.root != null)
                    {
                        float ku = Mathf.Clamp01((t - 0.26f) / 0.65f);
                        m_parryDaruking.root.localPosition = new Vector3(2.4f + ku * 0.45f, -0.55f * ku, 0f);
                        m_parryDaruking.root.localRotation = Quaternion.Euler(0f, -90f, -22f * ku);
                    }
                }
            }

            // 4. 王の静かな退場
            if (m_kingExitStart >= 0f && m_parryDaruking != null && m_parryDaruking.root != null)
            {
                float u = Mathf.Clamp01((m_time - m_kingExitStart) / 1.1f);
                m_parryDaruking.root.localPosition = new Vector3(2.85f + u * 1.4f, -0.55f - u * 1.6f, 0f);
                if (u >= 1f) m_parryDaruking.root.gameObject.SetActive(false);
            }

            // 5. バッドエンド：あかだるの赤い頭が黒ずみ、積み木の欠片がまとわりつく
            if (m_badCorruptStart >= 0f)
            {
                float t = m_time - m_badCorruptStart;
                float cu = Mathf.Clamp01(t / 2.2f);
                if (m_badHeadMat != null)
                {
                    m_badHeadMat.color = Color.Lerp(new Color(0.88f, 0.22f, 0.18f), new Color(0.08f, 0.08f, 0.10f), cu);
                }
                for (int i = 0; i < m_badSwirlShards.Count; i++)
                {
                    var s = m_badSwirlShards[i];
                    if (s == null) continue;
                    float angle = t * (1.8f + (i % 3) * 0.45f) + i * (Mathf.PI * 2f / m_badSwirlShards.Count);
                    float radius = Mathf.Lerp(2.2f, 0.62f + (i % 4) * 0.12f, cu);
                    float y = 0.25f + (i / (float)m_badSwirlShards.Count) * 1.65f + Mathf.Sin(t * 2.2f + i) * 0.10f;
                    s.localPosition = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                    s.localRotation = Quaternion.Euler(t * 90f + i * 35f, t * 70f, i * 20f);
                }
            }

            // 6. バッドエンド：闘技場の積み木が次々と崩れていく
            if (m_badCollapseStart >= 0f)
            {
                float t = m_time - m_badCollapseStart;
                for (int i = 0; i < m_badTowerBlocks.Count && i < m_badTowerBasePos.Count; i++)
                {
                    var blk = m_badTowerBlocks[i];
                    if (blk == null) continue;
                    float delay = (i / 5) * 0.28f + (i % 5) * 0.08f;
                    float u = Mathf.Clamp01((t - delay) / 0.85f);
                    if (u <= 0f) continue;
                    float dirX = (i % 2 == 0 ? 1f : -1f) * (0.9f + (i % 3) * 0.45f);
                    float dirZ = (i % 3 == 0 ? 1f : -1f) * 0.85f;
                    Vector3 basePos = m_badTowerBasePos[i];
                    float y = Mathf.Max(0.22f, basePos.y + u * 0.4f - u * u * (basePos.y + 0.6f));
                    blk.localPosition = new Vector3(basePos.x + dirX * u, y, basePos.z + dirZ * u);
                    blk.localRotation = Quaternion.Euler(u * 130f * (i % 2 == 0 ? 1 : -1), u * 60f, u * 110f);
                }
            }
        }
    }
}
