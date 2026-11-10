using System.Collections.Generic;
using UnityEngine;

namespace Nakagawa
{
    // ビリヤード台(Stage19)の演出で共通に使う物
    //  ・効果音(玉どうしが当たる「カコン」、ポケットに落ちる「ゴトン」)。GimmickSound と同じく実行中に波形から作る
    //  ・ギミックの名前などの文章を画面に出す(StageMessageUI に任せる)
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
            // ネット対戦用(suehara): クライアントで二重に揺らさない / ホストの揺れをクライアントにも送る(GimmickNet)
            if (GimmickNet.Muted) return;
            if (GimmickNet.Relaying) GimmickNet.ShakeSent?.Invoke(amplitude, duration);
            if (s_shake == null)
            {
                var cam = Camera.main;
                if (cam != null) s_shake = cam.GetComponent<CameraShake>();
            }
            if (s_shake != null) s_shake.Shake(new CameraShake.ShakeSettings(amplitude, duration));
        }

        // ギミックの名前などの文章を、画面の上の方に大きく出す(StageMessageUI)
        // position は以前の名残(その場に出していた位置)。今は使わない
        public static void Popup(Vector3 position, string text, Color color, float size = 1.0f, float duration = 1.6f)
        {
            StageMessageUI.Show(text, color, size, duration);
        }
    }
}