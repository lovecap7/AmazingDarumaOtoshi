using System.Collections.Generic;
using UnityEngine;

namespace Nakagawa
{
    // ビリヤード台(Stage19)の演出で共通に使う物
    //  ・効果音(玉どうしが当たる「カコン」、ポケットに落ちる「ゴトン」)。GimmickSound と同じく実行中に波形から作る
    //  ・ギミックの名前などを浮かび上がらせる文字(BilliardPopupText)
    //  ・カメラを揺らす(CameraShake があれば)
    public static class BilliardFx
    {
        const int kRate = 44100;
        static readonly Dictionary<string, AudioClip> s_cache = new Dictionary<string, AudioClip>();
        static CameraShake s_shake;

        // 玉どうしが当たる硬い音(短い高音の減衰 + 少しの雑音)
        public static AudioClip Clack => Cached("billiard_clack", 0.12f, b =>
        {
            var rng = new System.Random(7);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)kRate;
                float env = Mathf.Exp(-t * 55.0f);
                float tone = Mathf.Sin(2.0f * Mathf.PI * 2350.0f * t) * 0.6f + Mathf.Sin(2.0f * Mathf.PI * 3900.0f * t) * 0.3f;
                float noise = ((float)rng.NextDouble() * 2.0f - 1.0f) * Mathf.Exp(-t * 160.0f);
                b[i] = (tone + noise * 0.7f) * env;
            }
        });

        // ポケットに落ちる低い音(落ちて転がる「ゴトン、ゴロゴロ」)
        public static AudioClip Drop => Cached("billiard_drop", 0.7f, b =>
        {
            var rng = new System.Random(11);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)kRate;
                float thud = Mathf.Sin(2.0f * Mathf.PI * (140.0f - t * 60.0f) * t) * Mathf.Exp(-t * 14.0f);
                float roll = ((float)rng.NextDouble() * 2.0f - 1.0f) * 0.25f * Mathf.Exp(-t * 4.0f) * (0.6f + 0.4f * Mathf.Sin(t * 70.0f));
                b[i] = thud + roll;
            }
        });

        // キューで手玉を突く音(「コンッ」。カコンより低く強い)
        public static AudioClip Strike => Cached("billiard_strike", 0.2f, b =>
        {
            var rng = new System.Random(3);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)kRate;
                float env = Mathf.Exp(-t * 30.0f);
                float tone = Mathf.Sin(2.0f * Mathf.PI * 1250.0f * t) * 0.7f + Mathf.Sin(2.0f * Mathf.PI * 620.0f * t) * 0.5f;
                float noise = ((float)rng.NextDouble() * 2.0f - 1.0f) * Mathf.Exp(-t * 120.0f);
                b[i] = (tone + noise) * env;
            }
        });

        static AudioClip Cached(string name, float seconds, System.Action<float[]> build)
        {
            if (s_cache.TryGetValue(name, out var clip) && clip != null) return clip;
            var buffer = new float[Mathf.CeilToInt(seconds * kRate)];
            build(buffer);
            float max = 0.0001f;
            foreach (float v in buffer) max = Mathf.Max(max, Mathf.Abs(v));
            for (int i = 0; i < buffer.Length; i++) buffer[i] = buffer[i] / max * 0.9f;
            clip = AudioClip.Create(name, buffer.Length, 1, kRate, false);
            clip.SetData(buffer, 0);
            s_cache[name] = clip;
            return clip;
        }

        // カメラを揺らす(試合のカメラに CameraShake があれば)
        public static void Shake(float amplitude, float duration)
        {
            if (s_shake == null)
            {
                var cam = Camera.main;
                if (cam != null) s_shake = cam.GetComponent<CameraShake>();
            }
            if (s_shake != null) s_shake.Shake(new CameraShake.ShakeSettings(amplitude, duration));
        }

        // position の上に文字を浮かび上がらせる(ポンと出て、上へ浮かびながら消える)
        public static void Popup(Vector3 position, string text, Color color, float size = 1.0f, float duration = 1.6f)
        {
            if (string.IsNullOrEmpty(text)) return;
            var go = new GameObject("BilliardPopup");
            go.transform.position = position;
            go.AddComponent<BilliardPopupText>().Setup(text, color, size, duration);
        }
    }

    // 浮かび上がる文字(BilliardFx.Popup で作る)。常にカメラの方を向く
    public class BilliardPopupText : MonoBehaviour
    {
        TextMesh m_text, m_shadow;
        Transform m_root;
        Color m_color;
        float m_duration = 1.6f;
        float m_size = 1.0f;
        float m_time;
        Vector3 m_base;

        public void Setup(string text, Color color, float size, float duration)
        {
            m_color = color;
            m_duration = Mathf.Max(0.2f, duration);
            m_size = size;
            m_base = transform.position;
            m_root = new GameObject("Text").transform;
            m_root.SetParent(transform, false);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_shadow = CreateText("Shadow", text, font, new Color(0, 0, 0, 0.6f), new Vector3(0.06f, -0.06f, 0.02f));
            m_text = CreateText("Main", text, font, color, Vector3.zero);
            Update();
        }

        TextMesh CreateText(string name, string text, Font font, Color color, Vector3 offset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_root, false);
            go.transform.localPosition = offset;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = font;
            tm.fontSize = 64;
            tm.characterSize = 0.06f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = color;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = font.material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return tm;
        }

        private void Update()
        {
            if (m_root == null) return;
            m_time += Time.deltaTime;
            float t = Mathf.Clamp01(m_time / m_duration);
            // ポンと大きく出てから少し縮む
            float pop = t < 0.12f ? Mathf.Lerp(0.0f, 1.3f, t / 0.12f) : Mathf.Lerp(1.3f, 1.0f, Mathf.Clamp01((t - 0.12f) / 0.15f));
            m_root.localScale = Vector3.one * (pop * m_size);
            float rise = 1.0f - (1.0f - t) * (1.0f - t);
            transform.position = m_base + Vector3.up * (1.2f * rise);
            var cam = Camera.main;
            if (cam != null) m_root.rotation = cam.transform.rotation;
            float alpha = t < 0.7f ? 1.0f : 1.0f - (t - 0.7f) / 0.3f;
            m_text.color = GimmickPalette.WithAlpha(m_color, m_color.a * alpha);
            m_shadow.color = new Color(0, 0, 0, 0.6f * alpha);
            if (m_time >= m_duration) Destroy(gameObject);
        }
    }
}
