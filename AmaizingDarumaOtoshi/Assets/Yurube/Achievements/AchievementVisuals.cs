using System.Collections.Generic;
using UnityEngine;

namespace Yurube.Achievements
{
    // 実績UIの見た目（金属のような光沢）。
    // 上を明るく・下を暗くした縦グラデーションに、上部のツヤ（白い帯）を重ねたテクスチャを作る。
    // 色ごとに作って使い回す（実績の数だけ作るのではない）。
    public static class AchievementVisuals
    {
        const int PlateSize = 128;
        const int PlateRadius = 60;   // ToyKit の角丸スプライトと同じ形（差し替えても角の大きさが変わらない）
        const int BarHeight = 64;
        const int BarWidth = 8;

        static readonly Dictionary<string, Sprite> s_sprites = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Texture2D> s_textures = new Dictionary<string, Texture2D>();

        static string Key(Color c) => ColorUtility.ToHtmlStringRGBA(c);

        // 金属の陰影。v: 0（下）〜1（上）
        static Color Shade(Color c, float v)
        {
            Color top = Color.Lerp(c, Color.white, 0.62f);
            Color bottom = Color.Lerp(c, Color.black, 0.30f);
            Color col = Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, v));
            // 上部のツヤ（白い帯）。光沢感を出す
            float shine = Mathf.Clamp01(1f - Mathf.Abs(v - 0.80f) / 0.16f);
            return Color.Lerp(col, Color.white, shine * 0.45f);
        }

        // 角丸の金属板（通知のパネル・アイコン）。9スライスで使う
        public static Sprite Plate(Color color)
        {
            string key = "plate|" + Key(color);
            if (s_sprites.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = new Texture2D(PlateSize, PlateSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[PlateSize * PlateSize];
            for (int y = 0; y < PlateSize; y++)
            {
                Color row = Shade(color, y / (float)(PlateSize - 1));
                for (int x = 0; x < PlateSize; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, PlateRadius, PlateSize - PlateRadius);
                    float cy = Mathf.Clamp(y + 0.5f, PlateRadius, PlateSize - PlateRadius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(PlateRadius + 0.5f - d);
                    pixels[y * PlateSize + x] = new Color(row.r, row.g, row.b, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, PlateSize, PlateSize), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect,
                                       new Vector4(PlateRadius + 2, PlateRadius + 2, PlateRadius + 2, PlateRadius + 2));
            sprite.name = key;
            s_sprites[key] = sprite;
            return sprite;
        }

        // 細い帯（レアリティの帯）。角なしで縦に伸ばして使う
        public static Sprite Bar(Color color)
        {
            string key = "bar|" + Key(color);
            if (s_sprites.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = new Texture2D(BarWidth, BarHeight, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[BarWidth * BarHeight];
            for (int y = 0; y < BarHeight; y++)
            {
                var row = (Color32)Shade(color, y / (float)(BarHeight - 1));
                for (int x = 0; x < BarWidth; x++) pixels[y * BarWidth + x] = row;
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, BarWidth, BarHeight), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = key;
            s_sprites[key] = sprite;
            return sprite;
        }

        // 実績カードの下地。左上を明るく、右下を暗くした斜めグラデーション。
        // 未解除は同じ色相のまま、明度・彩度を落としたものを使う。
        // 角丸は呼び出し側のマスクで付けるので、ここでは四角いテクスチャのまま返す。
        public static Sprite CardGradient(AchievementRarity rarity, bool unlocked)
        {
            string key = "card|" + (int)rarity + (unlocked ? "|u" : "|l");
            if (s_sprites.TryGetValue(key, out var cached) && cached != null) return cached;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            Color start = AchievementRarityInfo.GradientStartOf(rarity);
            Color end = AchievementRarityInfo.GradientEndOf(rarity);
            if (!unlocked)
            {
                start = AchievementRarityInfo.Muted(start);
                end = AchievementRarityInfo.Muted(end);
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = y / (size - 1f);
                for (int x = 0; x < size; x++)
                {
                    float u = x / (size - 1f);
                    // 左上（u=0, v=1）が開始色、右下（u=1, v=0）が終了色
                    float t = (u + (1f - v)) * 0.5f;
                    pixels[y * size + x] = Color.Lerp(start, end, t);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = key;
            s_sprites[key] = sprite;
            return sprite;
        }

        // カードの外側に出す光彩。白いアルファだけの画像で、色は Image.color でレアリティ色を付ける。
        // 行の間隔（10px）に収まるよう、外側 8px までなだらかに薄くする。
        public static Sprite CardGlow()
        {
            const string key = "cardglow";
            if (s_sprites.TryGetValue(key, out var cached) && cached != null) return cached;

            const int w = 836, h = 62, pad = 4, radius = 8;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float d = RoundedRectDistance(x + 0.5f, y + 0.5f, w * 0.5f, h * 0.5f,
                                                  w * 0.5f - pad, h * 0.5f - pad, radius);
                    float a = d <= 0f ? 0f : Mathf.Clamp01(1f - d / pad);
                    a *= a;
                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = key;
            s_sprites[key] = sprite;
            return sprite;
        }

        static float RoundedRectDistance(float px, float py, float cx, float cy,
                                         float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(px - cx) - (halfW - radius);
            float qy = Mathf.Abs(py - cy) - (halfH - radius);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        // カードを横切る斜めの光の帯。白いアルファだけの画像で、色は Image.color で付ける。
        // 帯の中央が明るく、左右の端に向かって消える。上下の端も少しだけぼかす。
        public static Sprite CardShine()
        {
            const string key = "cardshine";
            if (s_sprites.TryGetValue(key, out var cached) && cached != null) return cached;

            const int w = 128, h = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                float edge = Mathf.Clamp01(Mathf.Min(v, 1f - v) * 8f);
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float a = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 2f);
                    a = a * a * edge;
                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = key;
            s_sprites[key] = sprite;
            return sprite;
        }

        // 未解除の実績に出す鍵アイコン（白いアルファだけの画像。色は Image.color で付ける）。
        public static Sprite LockIcon()
        {
            const string key = "lockicon";
            if (s_sprites.TryGetValue(key, out var cached) && cached != null) return cached;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    // 本体（角丸の四角）
                    bool body = RoundedRectDistance(px, py, 64f, 49f, 34f, 31f, 12f) <= 0f;
                    // 上の取っ手（リングの上半分。下半分は本体に隠れる）
                    float ring = Mathf.Abs(Mathf.Sqrt((px - 64f) * (px - 64f) + (py - 80f) * (py - 80f)) - 21f) - 5f;
                    bool shackle = ring <= 0f;
                    // 鍵穴（透明に抜く）
                    float keyDx = px - 64f, keyDy = py - 52f;
                    bool keyhole = keyDx * keyDx + keyDy * keyDy <= 36f;
                    if (px >= 61f && px <= 67f && py >= 34f && py <= 52f) keyhole = true;

                    float a = body || shackle ? 1f : 0f;
                    if (keyhole) a = 0f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = key;
            s_sprites[key] = sprite;
            return sprite;
        }

        // 進捗バー用の、影も縁もない白い角丸バー。9スライスの角丸を保ったまま幅を変える。
        public static Sprite BarSprite()
        {
            const string key = "barsprite";
            if (s_sprites.TryGetValue(key, out var cached) && cached != null) return cached;

            const int w = 64, h = 16, r = 8;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float d = RoundedRectDistance(x + 0.5f, y + 0.5f, w * 0.5f, h * 0.5f,
                                                  w * 0.5f, h * 0.5f, r);
                    float a = d <= 0f ? 1f : 0f;
                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            sprite.name = key;
            s_sprites[key] = sprite;
            return sprite;
        }

        // IMGUI（実績ラボ）用の縦グラデーション。GUI.DrawTexture で引き伸ばして使う
        public static Texture2D Gradient(Color color)
        {
            string key = "grad|" + Key(color);
            if (s_textures.TryGetValue(key, out var cached) && cached != null) return cached;

            const int height = 64;
            var tex = new Texture2D(1, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[height];
            for (int y = 0; y < height; y++) pixels[y] = Shade(color, y / (float)(height - 1));
            tex.SetPixels32(pixels);
            tex.Apply();
            s_textures[key] = tex;
            return tex;
        }

        // ドメインの再読み込みをしない設定でも、再生のたびに作り直す
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_sprites.Clear();
            s_textures.Clear();
        }
    }
}
