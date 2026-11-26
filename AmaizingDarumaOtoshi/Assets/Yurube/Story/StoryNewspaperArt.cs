using UnityEngine;

namespace Yurube.Story
{
    // だるま新聞の写真欄に使う、新聞風のイラストをコードで描く。
    // 外部アセットを使わず、紙の質感・だるま・トロフィー・紙吹雪をテクスチャに描く。
    public static class StoryNewspaperArt
    {
        static Sprite s_sprite;
        static Sprite s_prologueSprite;
        static Sprite s_preludeSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            s_sprite = null;
            s_prologueSprite = null;
            s_preludeSprite = null;
        }

        public static Sprite Create() => Create(null);

        public static Sprite Create(string artId)
        {
            if (artId == "prelude") return CreatePreludeArt();
            bool twoEyes = artId == "prologue";
            if (twoEyes && s_prologueSprite != null) return s_prologueSprite;
            if (!twoEyes && s_sprite != null) return s_sprite;

            const int w = 1180, h = 320;
            var px = new Color[w * h];
            var paper = new Color(0.93f, 0.90f, 0.82f);
            for (int i = 0; i < px.Length; i++) px[i] = paper;

            var rng = new System.Random(20261112);
            // 紙の斑点
            for (int i = 0; i < 2800; i++)
            {
                int x = rng.Next(w), y = rng.Next(h);
                int at = y * w + x;
                px[at] = Color.Lerp(px[at], new Color(0.52f, 0.46f, 0.36f), 0.07f);
            }

            var ink = new Color(0.12f, 0.10f, 0.09f);
            var red = new Color(0.82f, 0.16f, 0.12f);
            var cream = new Color(0.98f, 0.93f, 0.82f);
            var gold = new Color(0.95f, 0.76f, 0.18f);
            var darkGold = new Color(0.45f, 0.32f, 0.08f);

            // 紙吹雪（奥に薄く）
            Color[] confetti =
            {
                new Color(0.86f, 0.25f, 0.22f), new Color(0.25f, 0.55f, 0.85f),
                new Color(0.95f, 0.78f, 0.20f), new Color(0.30f, 0.68f, 0.35f),
            };
            for (int i = 0; i < 90; i++)
            {
                int x = rng.Next(40, w - 40), y = rng.Next(40, h - 50);
                int cw = rng.Next(4, 9), ch = rng.Next(8, 18);
                Global(px, w, h, x, y, cw, ch, confetti[i % confetti.Length], 0.45f);
            }

            // だるま（左）
            Disc(px, w, h, 330, 160, 116, ink);
            Disc(px, w, h, 330, 160, 107, red);
            Ellipse(px, w, h, 330, 166, 76, 86, cream);
            // 頭の上のちょんまげ
            Disc(px, w, h, 330, 276, 27, ink);
            Disc(px, w, h, 330, 276, 21, gold);
            // 目（序章では両目を描く。第1章以降は従来のデザインを維持）
            Disc(px, w, h, 304, 186, 11, ink);
            if (twoEyes) Disc(px, w, h, 356, 186, 11, ink);
            else Ring(px, w, h, 356, 186, 14, 5, ink);
            // 眉
            Ellipse(px, w, h, 304, 211, 20, 4, ink);
            Ellipse(px, w, h, 356, 211, 20, 4, ink);
            // ひげ・口
            Ellipse(px, w, h, 330, 144, 36, 8, ink);
            Global(px, w, h, 312, 143, 36, 2, cream, 1f);
            Ellipse(px, w, h, 330, 126, 12, 5, red);
            // ほお
            Ellipse(px, w, h, 270, 150, 18, 9, new Color(0.94f, 0.55f, 0.45f));
            Ellipse(px, w, h, 390, 150, 18, 9, new Color(0.94f, 0.55f, 0.45f));
            // 体の陰
            Ellipse(px, w, h, 330, 70, 70, 18, new Color(0.55f, 0.08f, 0.06f));

            // トロフィー（右）
            Rect(px, w, h, 786, 146, 108, 98, darkGold);
            Rect(px, w, h, 790, 150, 100, 90, gold);
            Ellipse(px, w, h, 840, 240, 56, 16, gold);
            Ellipse(px, w, h, 840, 240, 50, 12, new Color(0.99f, 0.88f, 0.42f));
            Ring(px, w, h, 770, 210, 38, 13, darkGold);
            Ring(px, w, h, 770, 210, 34, 8, gold);
            Ring(px, w, h, 910, 210, 38, 13, darkGold);
            Ring(px, w, h, 910, 210, 34, 8, gold);
            Rect(px, w, h, 830, 92, 20, 50, darkGold);
            Rect(px, w, h, 832, 94, 16, 46, gold);
            Rect(px, w, h, 794, 74, 92, 20, darkGold);
            Rect(px, w, h, 798, 78, 84, 14, gold);
            // トロフィーの星
            Star(px, w, h, 840, 200, 18, 9, cream);

            // 新聞の写真らしい枠
            Frame(px, w, h, 6, 2, ink);
            // ハーフトーン
            for (int y = 0; y < h; y += 5)
                for (int x = 0; x < w; x += 5)
                {
                    int at = y * w + x;
                    px[at] = Color.Lerp(px[at], ink, 0.045f);
                }

            // 新聞の写真なので白黒にする（コントラストを少し強めて印刷っぽく）
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                lum = Mathf.Clamp01((lum - 0.5f) * 1.22f + 0.5f);
                px[i] = new Color(lum, lum, lum, 1f);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = twoEyes ? "StoryNewspaperArt_Prologue" : "StoryNewspaperArt",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels(px);
            tex.Apply();
            var created = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            created.name = tex.name;
            if (twoEyes) s_prologueSprite = created;
            else s_sprite = created;
            return created;
        }

        // プロローグ用：「8年前 王冠大会 決勝会場」のモノクロ写真風（暗い会場と舞台上の古い槌）
        static Sprite CreatePreludeArt()
        {
            if (s_preludeSprite != null) return s_preludeSprite;

            const int w = 1180, h = 320;
            var px = new Color[w * h];
            var darkBg = new Color(0.14f, 0.14f, 0.15f);
            for (int i = 0; i < px.Length; i++) px[i] = darkBg;

            var rng = new System.Random(20261120);
            var ink = new Color(0.08f, 0.08f, 0.09f);
            var mid = new Color(0.32f, 0.31f, 0.30f);
            var floorLight = new Color(0.56f, 0.54f, 0.50f);
            var wood = new Color(0.46f, 0.42f, 0.36f);
            var woodDark = new Color(0.24f, 0.21f, 0.18f);

            // 奥の観客席の段差（暗いグラデーション）
            for (int row = 0; row < 5; row++)
            {
                int y = 170 + row * 24;
                Rect(px, w, h, 20, y, w - 40, 14, Color.Lerp(mid, ink, 0.55f + row * 0.08f));
            }

            // 競技場の舞台床（中央にスポットの残照）
            Rect(px, w, h, 80, 12, w - 160, 150, mid);
            Ellipse(px, w, h, w / 2, 92, 420, 62, floorLight);
            Ellipse(px, w, h, w / 2, 92, 260, 38, new Color(0.74f, 0.72f, 0.67f));

            // 舞台の縁（積み木の塔の台座ライン）
            Rect(px, w, h, 80, 156, w - 160, 8, ink);
            for (int x = 140; x < w - 140; x += 120)
            {
                Rect(px, w, h, x, 146, 60, 12, Color.Lerp(mid, ink, 0.4f));
            }

            // 床に転がる一本の古い槌（柄と槌頭を斜めに配置）
            ThickLine(px, w, h, 495, 76, 655, 114, 14, ink);
            ThickLine(px, w, h, 495, 76, 655, 114, 9, wood);
            // 槌の頭（円柱風の横倒し）
            ThickLine(px, w, h, 636, 144, 678, 84, 34, ink);
            ThickLine(px, w, h, 636, 144, 678, 84, 26, woodDark);
            ThickLine(px, w, h, 642, 136, 672, 92, 16, wood);
            // 影
            Ellipse(px, w, h, 590, 74, 95, 12, new Color(0.16f, 0.16f, 0.17f));

            // 新聞写真の枠・ハーフトーン・粒子
            for (int i = 0; i < 3200; i++)
            {
                int x = rng.Next(w), y = rng.Next(h);
                int at = y * w + x;
                px[at] = Color.Lerp(px[at], i % 2 == 0 ? Color.white : ink, 0.08f);
            }
            for (int y = 0; y < h; y += 5)
                for (int x = 0; x < w; x += 5)
                {
                    int at = y * w + x;
                    px[at] = Color.Lerp(px[at], ink, 0.05f);
                }
            Frame(px, w, h, 6, 2, ink);

            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                lum = Mathf.Clamp01((lum - 0.5f) * 1.18f + 0.5f);
                px[i] = new Color(lum, lum, lum, 1f);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "StoryNewspaperArt_Prelude",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels(px);
            tex.Apply();
            s_preludeSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            s_preludeSprite.name = "StoryNewspaperArt_Prelude";
            return s_preludeSprite;
        }

        static void ThickLine(Color[] px, int w, int h, float x0, float y0, float x1, float y1, int diameter, Color c)
        {
            int r = Mathf.Max(1, diameter / 2);
            float dist = Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1));
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist));
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps;
                Disc(px, w, h, Mathf.RoundToInt(Mathf.Lerp(x0, x1, u)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, u)), r, c);
            }
        }

        // 半透明で重ねる（紙吹雪用）
        static void Global(Color[] px, int w, int h, int x0, int y0, int rw, int rh, Color c, float alpha)
        {
            for (int y = y0; y < y0 + rh; y++)
            {
                if (y < 0 || y >= h) continue;
                for (int x = x0; x < x0 + rw; x++)
                {
                    if (x < 0 || x >= w) continue;
                    int at = y * w + x;
                    px[at] = Color.Lerp(px[at], c, alpha);
                }
            }
        }

        static void Rect(Color[] px, int w, int h, int x0, int y0, int rw, int rh, Color c)
        {
            for (int y = y0; y < y0 + rh; y++)
            {
                if (y < 0 || y >= h) continue;
                for (int x = x0; x < x0 + rw; x++)
                {
                    if (x < 0 || x >= w) continue;
                    px[y * w + x] = c;
                }
            }
        }

        static void Disc(Color[] px, int w, int h, int cx, int cy, int r, Color c)
        {
            int r2 = r * r;
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= h) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= w) continue;
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= r2) px[y * w + x] = c;
                }
            }
        }

        static void Ellipse(Color[] px, int w, int h, int cx, int cy, int rx, int ry, Color c)
        {
            for (int y = cy - ry; y <= cy + ry; y++)
            {
                if (y < 0 || y >= h) continue;
                for (int x = cx - rx; x <= cx + rx; x++)
                {
                    if (x < 0 || x >= w) continue;
                    float dx = (x - cx) / (float)rx, dy = (y - cy) / (float)ry;
                    if (dx * dx + dy * dy <= 1f) px[y * w + x] = c;
                }
            }
        }

        static void Ring(Color[] px, int w, int h, int cx, int cy, int r, int thickness, Color c)
        {
            int outer = r * r, inner = (r - thickness) * (r - thickness);
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= h) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= w) continue;
                    int dx = x - cx, dy = y - cy;
                    int d = dx * dx + dy * dy;
                    if (d <= outer && d >= inner) px[y * w + x] = c;
                }
            }
        }

        static void Star(Color[] px, int w, int h, int cx, int cy, int outer, int inner, Color c)
        {
            for (int y = cy - outer; y <= cy + outer; y++)
            {
                if (y < 0 || y >= h) continue;
                for (int x = cx - outer; x <= cx + outer; x++)
                {
                    if (x < 0 || x >= w) continue;
                    float dx = x - cx, dy = y - cy;
                    float angle = Mathf.Atan2(dy, dx);
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = Mathf.Lerp(inner, outer, Mathf.Abs(Mathf.Cos(angle * 2.5f)));
                    if (radius <= edge) px[y * w + x] = c;
                }
            }
        }

        static void Frame(Color[] px, int w, int h, int thickness, int gap, Color c)
        {
            Rect(px, w, h, 0, 0, w, thickness, c);
            Rect(px, w, h, 0, h - thickness, w, thickness, c);
            Rect(px, w, h, 0, 0, thickness, h, c);
            Rect(px, w, h, w - thickness, 0, thickness, h, c);
            int inner = thickness + gap;
            Rect(px, w, h, inner, inner, w - inner * 2, 1, c);
            Rect(px, w, h, inner, h - inner - 1, w - inner * 2, 1, c);
            Rect(px, w, h, inner, inner, 1, h - inner * 2, c);
            Rect(px, w, h, w - inner - 1, inner, 1, h - inner * 2, c);
        }
    }
}
