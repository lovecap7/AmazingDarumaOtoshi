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
