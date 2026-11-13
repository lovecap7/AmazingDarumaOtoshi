using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

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

        Material m_wallMat, m_floorMat, m_woodMat, m_whiteMat, m_inkMat;
        Material m_skyDay, m_skySunset, m_sunPatchMat;
        Transform m_skyQuad;
        Renderer m_sunPatch;
        Light m_sun, m_fill;
        Transform m_doll;

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

        // 直前のムービーを B でスキップしたか（章クリア済みの再プレイで、会話もまとめて飛ばすため）
        public bool WasSkipped { get; private set; }

        // 第1章以降：実際のステージを撮るカットシーン
        bool m_stageMode;
        Transform m_runner;
        Vector3 m_runnerHome, m_runnerA, m_runnerB;
        Quaternion m_runnerHomeRot;
        float m_runnerStart, m_runnerDuration;
        bool m_runnerActive;

        // 第2章以降：全国大会会場（鳥居の前）のセットを撮るカットシーン
        bool m_venueMode;
        bool m_venueBuilt;
        Transform m_venueRoot;
        readonly Dictionary<string, Member> m_venueMembers = new Dictionary<string, Member>();

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

            m_root = new GameObject("StoryMovieSet").transform;
            m_root.position = SetOrigin;

            BuildMaterials();
            BuildRoom();
            BuildProps();
            BuildMembers();
            BuildLights();
            BuildDust();
            BuildCamera();
            BuildPost();
            BuildScreen();
            Hide();
        }

        void OnDestroy()
        {
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
            if (m_camera != null) Destroy(m_camera.gameObject);
            if (m_screen != null) Destroy(m_screen);
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
            Box("Board", new Vector3(-5.83f, 2.55f, 0.3f), new Vector3(0.06f, 1.9f, 3.6f), m_whiteMat);
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
        }

        //========================================
        // 部員
        //========================================

        Member AddMember(Vector3 pos, float yaw, Color color, float scale, bool happy,
                         Transform parent = null, string id = null)
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
            if (!string.IsNullOrEmpty(id)) m_venueMembers[id] = m;
            return m;
        }

        void BuildMembers()
        {
            AddMember(new Vector3(-1.9f, 0f, 0.05f), 14f, new Color(0.95f, 0.78f, 0.18f), 0.92f, true);   // きだる
            AddMember(new Vector3(1.4f, 0f, 0.35f), -14f, new Color(0.28f, 0.62f, 0.32f), 0.92f, false);  // みだる
            AddMember(new Vector3(-0.2f, 0f, 2.45f), 2f, new Color(0.85f, 0.22f, 0.18f), 0.95f, true);    // あかだる
            AddMember(new Vector3(2.9f, 0f, 2.25f), -10f, new Color(0.2f, 0.42f, 0.85f), 0.95f, false);   // あおだる
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
            TitleDressingKit.Quad(m_venueRoot, "VenueSky", V(0f, 7f, 22f), new Vector3(60f, 30f, 1f), m_skyDay);

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
                if (m.root == null) continue;
                m.root.localPosition = m.basePos + Vector3.up * (0.012f * Mathf.Sin(m_time * 1.7f + m.phase));
                if (m.head != null)
                    m.head.localRotation = Quaternion.Euler(0f, 0f, 1.2f * Mathf.Sin(m_time * 0.8f + m.phase));
            }

            // だるま人形が揺れたら、ゆっくり戻す
            if (m_doll != null)
            {
                float z = m_doll.localEulerAngles.z;
                if (z > 180f) z -= 360f;
                if (Mathf.Abs(z) > 0.01f)
                    m_doll.localRotation = Quaternion.Euler(0f, 0f, Mathf.MoveTowards(z, 0f, Time.unscaledDeltaTime * 16f));
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
            RestoreRunner();
            m_stageMode = false;
            m_venueMode = false;
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
            RestoreRunner();
            m_stageMode = false;
            m_venueMode = false;
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
            m_stageMode = false;
            m_opening = false;
            m_advance = false;
            m_baseScale = 1f;
            SetSunset(sunset);
            SetCinema(true);
            SetCaption(false);
            SetTitle(false);
            SetHint(false);

            // 会話中は、部員たちを写したまま、ゆっくり深呼吸するようにカメラを動かす
            SetShot(
                lt => P(0.1f + 0.09f * Mathf.Sin(lt * 0.11f), 1.85f, -4.6f + 0.05f * Mathf.Sin(lt * 0.07f)),
                lt => P(0f, 0.55f, 2.2f),
                42f);
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
            while (m_time < time + m_shift && !m_skipped && !m_advance) yield return null;
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
            m_skipped = false;
            WasSkipped = false;
            m_advance = false;
            m_baseScale = 1f;
            m_opening = true;
            m_stageMode = false;
            Show();
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

            // 2. 窓と、片目のだるま
            SetShot(lt => P(1.0f, 2.15f, 1.6f + lt * 0.12f), lt => P(2.3f, 2.05f, 4.8f), 36f);
            yield return At(8.0f);
            if (m_skipped) { yield return FinishOpening(); yield break; }

            // 3. 回想：片目のだるま（セピア・スロー）
            SetMemory(true);
            m_camShake = 0.12f;
            StartCoroutine(SlowMo(0.35f, 1.0f));
            SetShot(lt => P(2.65f, 1.76f, 3.55f - lt * 0.05f), lt => P(2.65f, 1.64f, 4.5f), 28f);
            yield return At(11.2f);
            SetMemory(false);
            if (m_skipped) { yield return FinishOpening(); yield break; }

            // 4. 白板と練習メニュー
            SetShot(lt => P(-2.4f - lt * 0.05f, 2.2f, 1.8f), lt => P(-5.7f, 2.5f, 0.4f), 38f);
            yield return At(15.2f);
            if (m_skipped) { yield return FinishOpening(); yield break; }

            // 5. 部員たち → 暗転 → タイトル
            SetCaption(false);
            SetCinema(true);
            SetShot(lt => P(0.1f, 1.85f - lt * 0.03f, -4.4f + lt * 0.12f), lt => P(0f, 0.5f, 2.2f), 43f);
            yield return At(19.4f);
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
        public void ShowVenueConversation(bool after)
        {
            BuildVenue();
            Show();
            // 会話では、へびだるも姿を見せる（話者に寄るカメラで使う）
            if (m_venueMembers.TryGetValue("hebidaru", out var hebiMember)
                && hebiMember != null && hebiMember.root != null)
                hebiMember.root.gameObject.SetActive(true);
            m_stageMode = false;
            m_venueMode = true;
            m_opening = false;
            m_advance = false;
            m_baseScale = 1f;
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = new Color(0.55f, 0.74f, 0.94f);
            m_camera.farClipPlane = 120f;
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
            if (!m_runnerActive || m_runner == null) return;
            float u = Mathf.Clamp01((m_time - m_runnerStart) / m_runnerDuration);
            Vector3 pos = Vector3.Lerp(m_runnerA, m_runnerB, u);
            pos.y += Mathf.Abs(Mathf.Sin(u * Mathf.PI * 8f)) * 0.06f;
            m_runner.position = pos;
            m_runner.rotation = Quaternion.LookRotation(Vector3.right);
            if (u >= 1f) m_runnerActive = false;
        }

        void RestoreRunner()
        {
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
            m_camera.fieldOfView = Mathf.Max(1f, m_shotFov - m_fovPunch);
        }
    }
}
