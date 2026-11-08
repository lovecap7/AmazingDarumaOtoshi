using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yurube.Achievements
{
    // レアリティ専用のアイコンをコードで描く。
    // 銅・銀・金・プラチナは星の数が違うトロフィー、ダイヤモンドは宝石＋王冠、特別は専用の王冠。
    // 画像アセットに頼らないので、ドメインの再読み込みやビルドでも同じ絵が出る。
    public static class AchievementRarityIcons
    {
        const int OutputSize = 256;          // 実際に使う画像の大きさ
        const int Supersample = 2;           // 輪郭をなめらかにするため2倍で描いて縮小する
        const int CanvasSize = OutputSize * Supersample;
        const float DesignSize = 1024f;      // 図形の座標は 1024×1024 で考える

        static readonly Dictionary<string, Sprite> s_icons = new Dictionary<string, Sprite>();

        // ドメインの再読み込みをしない設定でも、再生のたびに作り直す
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_icons.Clear();
        }

        // そのレアリティ専用のアイコン
        public static Sprite IconOf(AchievementRarity rarity)
        {
            if (rarity == AchievementRarity.Diamond) return Get("diamond", BuildDiamondIcon);
            return Get("rank_" + rarity, () => BuildTrophyIcon(rarity));
        }

        // 特別実績用の王冠アイコン
        public static Sprite SpecialIcon() => Get("special", BuildSpecialIcon);

        public static Sprite IconOf(AchievementRarity rarity, bool special)
            => special ? SpecialIcon() : IconOf(rarity);

        // 未取得用のシルエット（白いアルファだけ。Image.color でインク色に染める）
        public static Sprite SilhouetteOf(AchievementRarity rarity, bool special)
        {
            string key = "sil_" + (special ? "special" : rarity.ToString());
            return Get(key, () =>
            {
                Sprite source = special ? SpecialIcon() : IconOf(rarity);
                return BuildSilhouette(source, key);
            });
        }

        static Sprite Get(string key, Func<Sprite> build)
        {
            if (s_icons.TryGetValue(key, out var cached) && cached != null) return cached;
            var sprite = build();
            s_icons[key] = sprite;
            return sprite;
        }

        //========================================
        // ダイヤモンドのアイコン
        //========================================

        // 王冠（台座の下から3本のツノが立つ形）
        static readonly Vector2[] Crown =
        {
            new Vector2(232f, 560f), new Vector2(232f, 745f), new Vector2(368f, 655f),
            new Vector2(512f, 800f), new Vector2(656f, 655f), new Vector2(792f, 745f),
            new Vector2(792f, 560f),
        };
        static readonly Vector2[] CrownTips =
        {
            new Vector2(232f, 750f), new Vector2(512f, 806f), new Vector2(792f, 750f),
        };

        // 宝石（ブリリアントカットのシルエット）
        static readonly Vector2[] Gem =
        {
            new Vector2(330f, 440f), new Vector2(694f, 440f), new Vector2(790f, 340f),
            new Vector2(512f, 140f), new Vector2(234f, 340f),
        };
        static readonly Vector2[] GemFacets =
        {
            new Vector2(330f, 440f), new Vector2(512f, 330f),
            new Vector2(694f, 440f), new Vector2(512f, 330f),
            new Vector2(234f, 340f), new Vector2(512f, 330f),
            new Vector2(790f, 340f), new Vector2(512f, 330f),
            new Vector2(512f, 330f), new Vector2(512f, 140f),
        };
        static readonly Vector2[] GemOutline =
        {
            new Vector2(330f, 440f), new Vector2(694f, 440f),
            new Vector2(694f, 440f), new Vector2(790f, 340f),
            new Vector2(790f, 340f), new Vector2(512f, 140f),
            new Vector2(512f, 140f), new Vector2(234f, 340f),
            new Vector2(234f, 340f), new Vector2(330f, 440f),
        };

        // きらめき（4方向の星）
        static readonly Vector2[] SparkleBig = MakeStar(836f, 842f, 72f);
        static readonly Vector2[] SparkleSmall = MakeStar(182f, 690f, 46f);

        static Vector2[] MakeStar(float cx, float cy, float size)
        {
            var points = new Vector2[8];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = Mathf.PI * 0.25f * i;
                float radius = i % 2 == 0 ? size : size * 0.20f;
                points[i] = new Vector2(cx + Mathf.Cos(angle) * radius, cy + Mathf.Sin(angle) * radius);
            }
            return points;
        }

        static Sprite BuildDiamondIcon()
        {
            var pixels = new Color[CanvasSize * CanvasSize];
            float step = DesignSize / CanvasSize;

            for (int y = 0; y < CanvasSize; y++)
            {
                float py = (y + 0.5f) * step;
                for (int x = 0; x < CanvasSize; x++)
                {
                    float px = (x + 0.5f) * step;
                    var c = new Color(0f, 0f, 0f, 0f);

                    // 角丸の下地（外側はうっすら紫に光らせる）
                    float tile = RoundedRect(px, py, 512f, 512f, 472f, 472f, 210f);
                    if (tile > 0f)
                    {
                        float glow = Mathf.Clamp01(1f - tile / 42f);
                        c = Blend(c, new Color(0.55f, 0.36f, 0.90f), glow * glow * 0.55f);
                    }
                    else
                    {
                        float t = Mathf.Clamp01((py - 40f) / 944f);
                        var tileColor = Color.Lerp(new Color(0.35f, 0.20f, 0.64f),
                                                   new Color(0.70f, 0.58f, 0.94f), t);
                        c = Blend(c, tileColor, 1f);

                        // 上部のツヤと、ふちの明るいライン
                        if (Mathf.Abs(py - 810f) < 55f) c = Blend(c, Color.white, 0.16f);
                        if (tile > -14f) c = Blend(c, new Color(0.82f, 0.74f, 0.98f), 0.55f);
                    }

                    // 王冠
                    bool crown = InPolygon(px, py, Crown);
                    if (!crown)
                    {
                        for (int i = 0; i < CrownTips.Length; i++)
                        {
                            if (InCircle(px, py, CrownTips[i], 24f)) { crown = true; break; }
                        }
                    }
                    if (!crown) crown = RoundedRect(px, py, 512f, 542f, 307f, 42f, 28f) <= 0f;
                    if (crown)
                    {
                        float t = Mathf.Clamp01((py - 500f) / 300f);
                        c = Blend(c, Color.Lerp(new Color(0.88f, 0.82f, 1f), Color.white, t), 1f);
                    }

                    // 王冠の台座に埋まった紫の宝石
                    if (InCircle(px, py, new Vector2(512f, 542f), 23f))
                    {
                        c = Blend(c, new Color(0.55f, 0.35f, 0.86f), 1f);
                    }
                    if (InCircle(px, py, new Vector2(505f, 549f), 8f))
                    {
                        c = Blend(c, Color.white, 0.9f);
                    }

                    // 宝石
                    bool gem = InPolygon(px, py, Gem);
                    if (gem)
                    {
                        float t = Mathf.Clamp01((py - 140f) / 300f);
                        c = Blend(c, Color.Lerp(new Color(0.45f, 0.28f, 0.80f),
                                                new Color(0.97f, 0.94f, 1f), t), 1f);
                    }

                    // 宝石のカット（面の線）と輪郭
                    for (int i = 0; i + 1 < GemFacets.Length; i += 2)
                    {
                        if (gem && SegmentDistance(px, py, GemFacets[i], GemFacets[i + 1]) <= 4f)
                        {
                            c = Blend(c, Color.white, 0.55f);
                        }
                    }
                    for (int i = 0; i + 1 < GemOutline.Length; i += 2)
                    {
                        if (SegmentDistance(px, py, GemOutline[i], GemOutline[i + 1]) <= 6.5f)
                        {
                            c = Blend(c, Color.white, 0.85f);
                        }
                    }

                    // きらめき
                    if (InPolygon(px, py, SparkleBig)) c = Blend(c, Color.white, 0.95f);
                    if (InPolygon(px, py, SparkleSmall)) c = Blend(c, Color.white, 0.75f);

                    pixels[y * CanvasSize + x] = c;
                }
            }

            return FinishIcon(pixels, "AchievementRarity_Diamond");
        }

        // スーパーサンプリングを縮小してスプライトにする（全アイコン共通）
        static Sprite FinishIcon(Color[] pixels, string name)
        {
            var texture = new Texture2D(OutputSize, OutputSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var output = new Color32[OutputSize * OutputSize];
            int samples = Supersample * Supersample;
            for (int y = 0; y < OutputSize; y++)
            {
                for (int x = 0; x < OutputSize; x++)
                {
                    var sum = new Color(0f, 0f, 0f, 0f);
                    for (int sy = 0; sy < Supersample; sy++)
                    {
                        int row = (y * Supersample + sy) * CanvasSize + x * Supersample;
                        for (int sx = 0; sx < Supersample; sx++) sum += pixels[row + sx];
                    }
                    float a = sum.a / samples;
                    float inv = a > 0.0001f ? 1f / (samples * a) : 0f;
                    output[y * OutputSize + x] = new Color32(
                        ToByte(sum.r * inv), ToByte(sum.g * inv), ToByte(sum.b * inv), ToByte(a));
                }
            }
            texture.SetPixels32(output);
            texture.Apply(false, false);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, OutputSize, OutputSize),
                                       new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }

        // アイコンのアルファだけを白で塗ったシルエット
        static Sprite BuildSilhouette(Sprite source, string name)
        {
            if (source == null || source.texture == null) return null;
            var src = source.texture;
            var pixels = src.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, pixels[i].a);
            }
            var texture = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, src.width, src.height),
                                       new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }

        //========================================
        // 銅・銀・金・プラチナのトロフィー
        //========================================

        static readonly Vector2[] CupBody =
        {
            new Vector2(300f, 716f), new Vector2(724f, 716f), new Vector2(700f, 600f),
            new Vector2(640f, 452f), new Vector2(384f, 452f), new Vector2(324f, 600f),
        };

        static Sprite BuildTrophyIcon(AchievementRarity rarity)
        {
            int stars = rarity == AchievementRarity.Bronze ? 1
                      : rarity == AchievementRarity.Silver ? 2
                      : rarity == AchievementRarity.Gold ? 3 : 4;
            Color light = AchievementRarityInfo.GradientStartOf(rarity);
            Color dark = AchievementRarityInfo.GradientEndOf(rarity);
            Color glow = AchievementRarityInfo.GlowOf(rarity, true);

            var starPositions = new Vector2[4];
            switch (stars)
            {
                case 1:
                    starPositions[0] = new Vector2(512f, 590f);
                    break;
                case 2:
                    starPositions[0] = new Vector2(430f, 590f);
                    starPositions[1] = new Vector2(594f, 590f);
                    break;
                case 3:
                    starPositions[0] = new Vector2(512f, 625f);
                    starPositions[1] = new Vector2(430f, 575f);
                    starPositions[2] = new Vector2(594f, 575f);
                    break;
                default:
                    starPositions[0] = new Vector2(430f, 610f);
                    starPositions[1] = new Vector2(594f, 610f);
                    starPositions[2] = new Vector2(512f, 555f);
                    starPositions[3] = new Vector2(512f, 668f);
                    break;
            }

            var pixels = new Color[CanvasSize * CanvasSize];
            float step = DesignSize / CanvasSize;
            for (int y = 0; y < CanvasSize; y++)
            {
                float py = (y + 0.5f) * step;
                for (int x = 0; x < CanvasSize; x++)
                {
                    float px = (x + 0.5f) * step;
                    var c = new Color(0f, 0f, 0f, 0f);

                    // 後ろの光
                    float gd = Vector2.Distance(new Vector2(px, py), new Vector2(512f, 520f));
                    float ga = Mathf.Clamp01(1f - gd / 470f);
                    if (ga > 0f) c = Blend(c, glow, ga * ga * 0.22f);

                    // 取っ手（胴の後ろ）
                    if (InEllipseRing(px, py, 250f, 600f, 86f, 118f, 26f) ||
                        InEllipseRing(px, py, 774f, 600f, 86f, 118f, 26f))
                    {
                        c = Blend(c, Metal(px, py, light, dark), 1f);
                    }

                    // カップの胴
                    if (InPolygon(px, py, CupBody))
                    {
                        c = Blend(c, Metal(px, py, light, dark), 1f);
                        if (px > 350f && px < 430f && py > 470f && py < 690f)
                        {
                            c = Blend(c, Color.white, 0.16f * Mathf.Clamp01((py - 470f) / 220f));
                        }
                        if (py < 500f) c = Blend(c, dark, 0.18f);
                    }

                    // 縁と飲み口
                    if (InEllipseRing(px, py, 512f, 716f, 212f, 40f, 15f))
                    {
                        c = Blend(c, light, 1f);
                    }
                    if (InEllipse(px, py, 512f, 716f, 196f, 27f))
                    {
                        c = Blend(c, Color.Lerp(dark, Color.black, 0.3f), 1f);
                    }

                    // 脚と台座
                    if (RoundedRect(px, py, 512f, 365f, 56f, 82f, 22f) <= 0f)
                    {
                        c = Blend(c, Metal(px, py, light, dark), 1f);
                    }
                    if (RoundedRect(px, py, 512f, 300f, 128f, 26f, 18f) <= 0f)
                    {
                        c = Blend(c, Metal(px, py, light, dark), 1f);
                    }
                    if (RoundedRect(px, py, 512f, 248f, 192f, 34f, 16f) <= 0f)
                    {
                        c = Blend(c, Metal(px, py, light, dark), 1f);
                    }
                    if (Mathf.Abs(py - 268f) < 5f && Mathf.Abs(px - 512f) < 190f)
                    {
                        c = Blend(c, dark, 0.5f);
                    }

                    // レアリティの星（銅1・銀2・金3・白金4）
                    for (int s = 0; s < stars; s++)
                    {
                        if (InPolygon(px, py, MakeStar(starPositions[s].x, starPositions[s].y, 38f)))
                        {
                            c = Blend(c, new Color(1f, 0.97f, 0.90f), 1f);
                        }
                    }

                    // 縁のハイライト
                    if (InEllipseRing(px, py, 512f, 716f, 212f, 40f, 5f) && py > 716f)
                    {
                        c = Blend(c, Color.white, 0.55f);
                    }

                    pixels[y * CanvasSize + x] = c;
                }
            }

            return FinishIcon(pixels, "AchievementRarity_" + rarity);
        }

        //========================================
        // 特別実績の王冠
        //========================================

        static Sprite BuildSpecialIcon()
        {
            Color light = AchievementRarityInfo.SpecialColor;
            Color dark = Color.Lerp(light, Color.black, 0.35f);
            var pixels = new Color[CanvasSize * CanvasSize];
            float step = DesignSize / CanvasSize;

            for (int y = 0; y < CanvasSize; y++)
            {
                float py = (y + 0.5f) * step;
                for (int x = 0; x < CanvasSize; x++)
                {
                    float px = (x + 0.5f) * step;
                    var c = new Color(0f, 0f, 0f, 0f);

                    float gd = Vector2.Distance(new Vector2(px, py), new Vector2(512f, 560f));
                    float ga = Mathf.Clamp01(1f - gd / 450f);
                    if (ga > 0f) c = Blend(c, light, ga * ga * 0.20f);

                    bool crown = InPolygon(px, py, Crown);
                    if (!crown)
                    {
                        for (int i = 0; i < CrownTips.Length; i++)
                        {
                            if (InCircle(px, py, CrownTips[i], 24f)) { crown = true; break; }
                        }
                    }
                    if (!crown) crown = RoundedRect(px, py, 512f, 542f, 307f, 42f, 28f) <= 0f;

                    if (crown)
                    {
                        float t = Mathf.Clamp01((py - 500f) / 300f);
                        c = Blend(c, Color.Lerp(dark, light, t), 1f);
                    }

                    // 台座
                    if (RoundedRect(px, py, 512f, 542f, 307f, 42f, 28f) <= 0f)
                    {
                        c = Blend(c, Color.Lerp(dark, Color.black, 0.3f), 1f);
                    }

                    // 中央の星
                    if (InPolygon(px, py, MakeStar(512f, 620f, 58f)))
                    {
                        c = Blend(c, new Color(1f, 0.96f, 0.80f), 1f);
                    }

                    pixels[y * CanvasSize + x] = c;
                }
            }

            return FinishIcon(pixels, "AchievementRarity_Special");
        }

        static Color Metal(float px, float py, Color light, Color dark)
        {
            float t = Mathf.Clamp01((py - 400f) / 340f);
            var c = Color.Lerp(dark, light, t);
            if (px < 430f && py > 450f && py < 700f)
            {
                c = Color.Lerp(c, Color.white, 0.12f);
            }
            return c;
        }

        static bool InEllipse(float px, float py, float cx, float cy, float rx, float ry)
        {
            float nx = (px - cx) / rx, ny = (py - cy) / ry;
            return nx * nx + ny * ny <= 1f;
        }

        static bool InEllipseRing(float px, float py, float cx, float cy, float rx, float ry, float thickness)
        {
            float nx = (px - cx) / rx, ny = (py - cy) / ry;
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float inner = 1f - thickness / Mathf.Min(rx, ry);
            return r <= 1f && r >= inner;
        }

        static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);

        // 乗算済みアルファで重ねる（縮小したときに縁が黒ずまないように）
        static Color Blend(Color dst, Color src, float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            return new Color(
                src.r * alpha + dst.r * (1f - alpha),
                src.g * alpha + dst.g * (1f - alpha),
                src.b * alpha + dst.b * (1f - alpha),
                alpha + dst.a * (1f - alpha));
        }

        static bool InCircle(float px, float py, Vector2 center, float radius)
        {
            float dx = px - center.x, dy = py - center.y;
            return dx * dx + dy * dy <= radius * radius;
        }

        static bool InPolygon(float px, float py, Vector2[] points)
        {
            bool inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                Vector2 a = points[i], b = points[j];
                if ((a.y > py) != (b.y > py))
                {
                    float xOnEdge = a.x + (py - a.y) / (b.y - a.y) * (b.x - a.x);
                    if (px < xOnEdge) inside = !inside;
                }
            }
            return inside;
        }

        static float SegmentDistance(float px, float py, Vector2 a, Vector2 b)
        {
            float dx = b.x - a.x, dy = b.y - a.y;
            float lengthSq = dx * dx + dy * dy;
            float t = lengthSq <= 0f ? 0f : Mathf.Clamp01(((px - a.x) * dx + (py - a.y) * dy) / lengthSq);
            float qx = px - (a.x + t * dx);
            float qy = py - (a.y + t * dy);
            return Mathf.Sqrt(qx * qx + qy * qy);
        }

        static float RoundedRect(float px, float py, float cx, float cy, float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(px - cx) - (halfW - radius);
            float qy = Mathf.Abs(py - cy) - (halfH - radius);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }
    }
}
