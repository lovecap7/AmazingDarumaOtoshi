using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nakagawa
{
    // ギミックの効果音。音のファイルが指定されていなければ、プログラムで作った仮の音を使う(一度作った音は使い回す)
    // 音量は設定画面の効果音の音量(GameSettings.SeVolume)に従う
    public static class GimmickSound
    {
        const int kRate = 44100;
        static readonly Dictionary<string, AudioClip> s_cache = new Dictionary<string, AudioClip>();
        static readonly System.Random s_rng = new System.Random(20261022);

        // ゴトゴト(せり上がり塀の予兆。ループして鳴らす)
        public static AudioClip Rumble => Cached("gimmick_rumble", 1.0f, b =>
        {
            float lp = 0.0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / kRate;
                lp += (Noise() - lp) * 0.04f;
                // 1秒に6回ゴトッと揺れる(ループの継ぎ目がずれないよう整数回)
                float knock = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(t * 6.0f * Mathf.PI * 2.0f), 4.0f);
                b[i] = lp * 3.0f * (0.35f + 0.65f * knock) + Mathf.Sin(t * 55.0f * Mathf.PI * 2.0f) * 0.25f * knock;
            }
        });

        // ポンッ(砲台の発射)
        public static AudioClip Pop => Cached("gimmick_pop", 0.25f, b =>
        {
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / kRate;
                b[i] = Mathf.Sin(PhaseSweep(t, 420.0f, 110.0f, 0.12f)) * Mathf.Exp(-t * 22.0f)
                     + Noise() * 0.5f * Mathf.Exp(-t * 60.0f);
            }
        });

        // ヒュンッ(ワープ)
        public static AudioClip Warp => Cached("gimmick_warp", 0.35f, b =>
        {
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / kRate;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.35f));
                b[i] = Mathf.Sin(PhaseSweep(t, 300.0f, 1600.0f, 0.35f) + Mathf.Sin(t * 40.0f) * 2.0f) * env * 0.7f;
            }
        });

        // シュッ(シューターの発射)
        public static AudioClip Launch => Cached("gimmick_launch", 0.25f, b =>
        {
            float lp = 0.0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / kRate;
                float u = t / 0.25f;
                lp += (Noise() - lp) * Mathf.Lerp(0.05f, 0.5f, u);
                b[i] = (Noise() - lp) * Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.5f)) * 0.8f;
            }
        });

        // キュイーン(砲台のため。1秒)
        public static AudioClip Charge => Cached("gimmick_charge", 1.0f, b =>
        {
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / kRate;
                b[i] = Mathf.Sin(PhaseSweep(t, 220.0f, 900.0f, 1.0f)) * Mathf.Clamp01(t * 3.0f) * (0.6f + 0.4f * Mathf.Sin(t * 60.0f)) * 0.35f;
            }
        });

        // 指定がなければ仮の音
        public static AudioClip Or(AudioClip clip, AudioClip fallback) => clip != null ? clip : fallback;

        // 1回だけ鳴らす(画面全体に聞こえる。鳴り終わったら消える)
        public static void Play(AudioClip clip, float volume, float pitch = 1.0f)
        {
            if (clip == null || volume <= 0.0f) return;
            var go = new GameObject("GimmickSound");
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume * GameSettings.SeVolume;
            source.pitch = pitch;
            source.spatialBlend = 0.0f;
            source.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.01f, pitch) + 0.1f);
        }

        // 鳴らし続ける音の音源(止めておく。音量は呼ぶ側で SetLoopVolume する)
        public static AudioSource CreateLoop(Transform parent, AudioClip clip)
        {
            var go = new GameObject("GimmickLoopSound");
            go.transform.SetParent(parent, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0.0f;
            return source;
        }

        // volume が0なら止め、0より大きければ鳴らす
        public static void SetLoopVolume(AudioSource source, float volume)
        {
            if (source == null) return;
            source.volume = volume * GameSettings.SeVolume;
            if (volume > 0.0f && !source.isPlaying) source.Play();
            else if (volume <= 0.0f && source.isPlaying) source.Stop();
        }

        static AudioClip Cached(string name, float seconds, Action<float[]> build)
        {
            if (s_cache.TryGetValue(name, out var clip) && clip != null) return clip;
            var buffer = new float[Mathf.CeilToInt(seconds * kRate)];
            build(buffer);
            Normalize(buffer, 0.9f);
            clip = AudioClip.Create(name, buffer.Length, 1, kRate, false);
            clip.SetData(buffer, 0);
            s_cache[name] = clip;
            return clip;
        }

        // from → to へ duration 秒かけて高さが変わるときの位相
        static float PhaseSweep(float t, float from, float to, float duration)
        {
            float u = Mathf.Min(t, duration);
            float k = (to - from) / duration;
            float phase = from * u + 0.5f * k * u * u + to * Mathf.Max(0.0f, t - duration);
            return phase * Mathf.PI * 2.0f;
        }

        static float Noise() => (float)(s_rng.NextDouble() * 2.0 - 1.0);

        static void Normalize(float[] b, float peak)
        {
            float max = 0.0001f;
            foreach (var s in b) max = Mathf.Max(max, Mathf.Abs(s));
            float k = peak / max;
            for (int i = 0; i < b.Length; i++) b[i] *= k;
        }
    }
}
