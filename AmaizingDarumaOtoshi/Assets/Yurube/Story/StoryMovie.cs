using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
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
    public class StoryMovie : MonoBehaviour
    {
        // セットの場所（ゲームのステージから遠く離す）
        static readonly Vector3 SetOrigin = new Vector3(5000f, 0f, 0f);
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

        bool m_built, m_visible, m_conversation, m_skipped, m_sunset;
        float m_time, m_conversationStart;

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
            BuildScreen();
            Hide();
        }

        void OnDestroy()
        {
            if (m_rt != null)
            {
                m_rt.Release();
                Destroy(m_rt);
            }
            if (m_root != null) Destroy(m_root.gameObject);
            if (m_camera != null) Destroy(m_camera.gameObject);
            if (m_screen != null) Destroy(m_screen);
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
            // 白板（左の壁）
            Box("BoardFrame", new Vector3(-5.87f, 2.55f, 0.3f), new Vector3(0.05f, 2.0f, 3.8f), m_inkMat);
            Box("Board", new Vector3(-5.83f, 2.55f, 0.3f), new Vector3(0.06f, 1.9f, 3.6f), m_whiteMat);
            var blue = Unlit(new Color(0.16f, 0.32f, 0.75f));
            var red = Unlit(new Color(0.82f, 0.16f, 0.12f));
            for (int i = 0; i < 6; i++)
            {
                float len = 1.1f + (i * 0.37f) % 1.4f;
                Box("BoardLine" + i, new Vector3(-5.79f, 3.25f - i * 0.27f, -1.0f + (i % 2) * 0.3f),
                    new Vector3(0.01f, 0.035f, len), i == 0 ? red : blue);
            }

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

        Member AddMember(Vector3 pos, float yaw, Color color, float scale, bool happy)
        {
            var m = new Member { basePos = pos, phase = m_members.Count * 1.7f };
            var root = new GameObject("Member").transform;
            root.SetParent(m_root, false);
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
            return m;
        }

        void BuildMembers()
        {
            AddMember(new Vector3(-1.9f, 0f, 0.05f), 14f, new Color(0.95f, 0.78f, 0.18f), 0.92f, true);   // きだる
            AddMember(new Vector3(1.4f, 0f, 0.35f), -14f, new Color(0.28f, 0.62f, 0.32f), 0.92f, false);  // みだる
            AddMember(new Vector3(-0.2f, 0f, 2.45f), 2f, new Color(0.85f, 0.22f, 0.18f), 0.95f, true);    // あかだる
            AddMember(new Vector3(2.9f, 0f, 2.25f), -10f, new Color(0.2f, 0.42f, 0.85f), 0.95f, false);   // あおだる
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
            data.renderPostProcessing = false;
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

            // スキップの案内
            var hintPlate = ToyKit.UIRounded("MovieHint", m_canvas, new Color(0.05f, 0.04f, 0.03f, 0.72f), 14f);
            hintPlate.pivot = new Vector2(1f, 0f);
            ToyKit.Anchor(hintPlate, new Vector2(1f, 0f), new Vector2(-32f, 112f), new Vector2(300f, 50f));
            m_hintGroup = hintPlate.gameObject.AddComponent<CanvasGroup>();
            var hint = ToyKit.UIText("Text", hintPlate, font, "A：スキップ", 26f, Color.white);
            ToyKit.Stretch(hint.rectTransform);
            hint.alignment = TextAlignmentOptions.Right;
            hint.rectTransform.offsetMin = new Vector2(12f, 0f);
            hint.rectTransform.offsetMax = new Vector2(-12f, 0f);

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
            if (m_screen != null) m_screen.SetActive(true);
            if (m_camera != null) m_camera.enabled = true;
        }

        public void Hide()
        {
            m_visible = false;
            m_conversation = false;
            if (m_screen != null) m_screen.SetActive(false);
            if (m_camera != null) m_camera.enabled = false;
        }

        public void SetBlack()
        {
            if (m_fadeGroup != null) m_fadeGroup.alpha = 1f;
        }

        // 会話の背景にする（sunset: 夕方の光）
        public void ShowConversation(bool sunset)
        {
            Show();
            m_conversation = true;
            m_conversationStart = m_time;
            SetSunset(sunset);
            SetCaption(false);
            SetTitle(false);
            SetHint(false);
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
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration))
            {
                m_fadeGroup.alpha = Mathf.Lerp(from, target, Mathf.Clamp01(t));
                yield return null;
            }
            m_fadeGroup.alpha = target;
        }

        // オープニング（A / B で飛ばせる）
        public IEnumerator PlayOpening()
        {
            m_time = 0f;
            m_skipped = false;
            m_conversation = false;
            Show();
            m_fadeGroup.alpha = 1f;
            SetCaption(false);
            SetTitle(false);
            SetHint(true);
            yield return FadeTo(0f, 0.5f);
            SetCaption(true);

            // 1. 部室の全景
            yield return Shot(4.0f, () => P(0f, 1.95f, -4.7f + m_time * 0.10f), () => P(0f, 0.55f, 2.4f), 42f);
            if (m_skipped) { yield return FinishOpening(); yield break; }

            // 2. 窓と、片目のだるま
            yield return Shot(4.0f, () => P(1.1f, 2.05f, 1.9f + (m_time - 4f) * 0.05f), () => P(2.3f, 2.0f, 4.8f), 34f);
            if (m_skipped) { yield return FinishOpening(); yield break; }

            // 3. 白板と練習メニュー
            yield return Shot(4.0f, () => P(-2.5f + (m_time - 8f) * 0.06f, 2.15f, 1.9f), () => P(-5.7f, 2.45f, 0.4f), 38f);
            if (m_skipped) { yield return FinishOpening(); yield break; }

            // 4. 部員たち → 暗転 → タイトル
            SetCaption(false);
            yield return Shot(3.4f, () => P(0.1f, 1.8f, -4.3f + (m_time - 12f) * 0.02f), () => P(0f, 0.5f, 2.2f), 42f);
            yield return FadeTo(1f, 0.5f);
            SetTitle(true);
            yield return Hold(2.2f);
            SetTitle(false);
            ShowConversation(false);
            yield return FadeTo(0f, 0.6f);
        }

        IEnumerator Hold(float seconds)
        {
            float end = m_time + seconds;
            while (m_time < end)
            {
                if (MenuInput.Submit() || MenuInput.Cancel() || MenuInput.AnyClick()) yield break;
                yield return null;
            }
        }

        IEnumerator FinishOpening()
        {
            SetCaption(false);
            SetTitle(false);
            SetHint(false);
            yield return FadeTo(1f, 0.3f);
            ShowConversation(false);
            yield return FadeTo(0f, 0.3f);
        }

        IEnumerator Shot(float duration, System.Func<Vector3> pos, System.Func<Vector3> look, float fov)
        {
            float end = m_time + duration;
            while (m_time < end && m_visible)
            {
                if (MenuInput.Submit() || MenuInput.Cancel() || MenuInput.AnyClick())
                {
                    m_skipped = true;
                    yield break;
                }
                ApplyCamera(pos(), look(), fov);
                yield return null;
            }
        }

        void ApplyCamera(Vector3 pos, Vector3 look, float fov)
        {
            if (m_camera == null) return;
            m_camera.transform.position = pos;
            Vector3 dir = look - pos;
            if (dir.sqrMagnitude > 0.0001f) m_camera.transform.rotation = Quaternion.LookRotation(dir);
            m_camera.fieldOfView = fov;
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
        }

        void SetCaption(bool on)
        {
            if (m_captionGroup != null) m_captionGroup.alpha = on ? 1f : 0f;
        }

        void SetTitle(bool on)
        {
            if (m_titleGroup != null) m_titleGroup.alpha = on ? 1f : 0f;
        }

        void SetHint(bool on)
        {
            if (m_hintGroup != null) m_hintGroup.alpha = on ? 1f : 0f;
        }

        void Update()
        {
            if (!m_visible) return;
            m_time += Time.unscaledDeltaTime;
            AnimateMembers();
            if (m_conversation)
            {
                float t = m_time - m_conversationStart;
                var pos = P(0.1f + 0.09f * Mathf.Sin(t * 0.11f), 1.85f, -4.6f + 0.05f * Mathf.Sin(t * 0.07f));
                var look = P(0f, 0.55f, 2.2f);
                ApplyCamera(pos, look, 42f + 1.2f * Mathf.Sin(t * 0.05f));
            }
        }
    }
}
