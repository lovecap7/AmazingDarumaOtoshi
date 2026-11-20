using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Yurube.Story
{
    // プロローグ（「八年前の槌音」）で使う効果音のキー名・読み込み・再生を1か所に集約する。
    // 素材が未用意（null）でもコンパイル・実行でき、無音のまま進行する。
    public static class PreludeSfx
    {
        // ---- 台本 JSON の "se" で使うキー名 ----
        public const string Cheer = "cheer";           // 大歓声（台本1）
        public const string Silence = "silence";       // 歓声の停止・静寂（台本3）
        public const string Collapse = "collapse";     // 積み木が崩れる音（台本3）
        public const string Murmur = "murmur";         // 観客席のどよめき・ざわめき（台本5・7）
        public const string MetalHit = "metal_hit";    // 鋭い金属音（台本5）

        // TODO: 本番SE素材を用意したら、Resources/Audio/Story/ に下記ファイル名で配置するか、
        //       ここで直接 AudioClip を参照するように差し替える。
        static readonly Dictionary<string, string> ResourcePaths = new Dictionary<string, string>
        {
            { Cheer, "Audio/Story/prelude_cheer" },
            { Collapse, "Audio/Story/prelude_collapse" },
            { Murmur, "Audio/Story/prelude_murmur" },
            { MetalHit, "Audio/Story/prelude_metal_hit" },
        };

        // 必要であれば外部からクリップを上書き登録できるマップ（未設定なら Resources を探す）
        static readonly Dictionary<string, AudioClip> s_overrides = new Dictionary<string, AudioClip>();
        static AudioSource s_ambientSource;
        static AudioSource s_oneShotSource;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_overrides.Clear();
            s_ambientSource = null;
            s_oneShotSource = null;
        }

        public static void RegisterClip(string key, AudioClip clip)
        {
            if (string.IsNullOrEmpty(key)) return;
            s_overrides[key] = clip;
        }

        public static AudioClip Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (s_overrides.TryGetValue(key, out var clip)) return clip;
            if (ResourcePaths.TryGetValue(key, out var path))
            {
                // TODO: 素材未用意のあいだは null が返り、無音のまま進行する
                return Resources.Load<AudioClip>(path);
            }
            return null;
        }

        // 台本行の "se" 指定を再生する。カンマ区切りで複数キー指定可（例："murmur,metal_hit"）
        public static void Play(string seSpec, MonoBehaviour host = null)
        {
            if (string.IsNullOrEmpty(seSpec)) return;
            var parts = seSpec.Split(',');
            float delay = 0f;
            foreach (var raw in parts)
            {
                string key = raw != null ? raw.Trim() : "";
                if (string.IsNullOrEmpty(key)) continue;
                if (key == Silence)
                {
                    StopAmbient();
                    continue;
                }
                if (delay <= 0f || host == null)
                {
                    PlayKey(key);
                }
                else
                {
                    host.StartCoroutine(PlayDelayed(key, delay));
                }
                delay += 0.35f;
            }
        }

        public static void StopAll()
        {
            StopAmbient();
            if (s_oneShotSource != null) s_oneShotSource.Stop();
        }

        static void StopAmbient()
        {
            if (s_ambientSource != null && s_ambientSource.isPlaying) s_ambientSource.Stop();
        }

        static IEnumerator PlayDelayed(string key, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            PlayKey(key);
        }

        static void PlayKey(string key)
        {
            var clip = Resolve(key);
            if (clip == null) return;

            EnsureSources();
            float volume = 0.9f * GameSettings.SeVolume;
            if (key == Cheer || key == Murmur)
            {
                if (s_ambientSource != null)
                {
                    s_ambientSource.Stop();
                    s_ambientSource.clip = clip;
                    s_ambientSource.volume = volume * 0.75f;
                    s_ambientSource.loop = false;
                    s_ambientSource.Play();
                }
            }
            else if (s_oneShotSource != null)
            {
                s_oneShotSource.PlayOneShot(clip, volume);
            }
        }

        static void EnsureSources()
        {
            if (s_ambientSource != null && s_oneShotSource != null) return;
            var go = new GameObject("PreludeSfxPlayer");
            Object.DontDestroyOnLoad(go);
            s_ambientSource = go.AddComponent<AudioSource>();
            s_ambientSource.playOnAwake = false;
            s_ambientSource.spatialBlend = 0f;
            s_ambientSource.ignoreListenerPause = true;

            s_oneShotSource = go.AddComponent<AudioSource>();
            s_oneShotSource.playOnAwake = false;
            s_oneShotSource.spatialBlend = 0f;
            s_oneShotSource.ignoreListenerPause = true;
        }
    }
}
