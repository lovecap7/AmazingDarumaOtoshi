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
        public const string Cheer = "cheer";           // 大歓声（台本1・第4章勝利後）
        public const string CheerFade = "cheer_fade";  // 歓声を絞って静かにする（第4章だるきんぐ登場）
        public const string Silence = "silence";       // 歓声の停止・静寂（台本3）
        public const string Collapse = "collapse";     // 積み木が崩れる音（台本3）
        public const string Murmur = "murmur";             // 観客席のどよめき・ざわめき（台本5・7）
        public const string MetalHit = "metal_hit";        // 鋭い金属音（台本5）
        public const string Parry = "parry";               // 王の剣の打ち返し［パリィ］（最終章トゥルー）
        public const string Shatter = "shatter";           // 王座と盾が砕ける音（最終章トゥルー）
        public const string Unlock = "unlock";             // だるきんぐ解放通知の音（最終章トゥルー）
        public const string HeavyHammer = "heavy_hammer";  // 重い槌音（最終章バッド）
        public const string HammerDrag = "hammer_drag";    // 暗闇で重い槌を引きずる音（最終章バッド）

        // TODO: 本番SE素材を用意したら、Resources/Audio/Story/ に下記ファイル名で配置するか、
        //       ここで直接 AudioClip を参照するように差し替える。
        static readonly Dictionary<string, string> ResourcePaths = new Dictionary<string, string>
        {
            { Cheer, "Audio/Story/prelude_cheer" },
            { Collapse, "Audio/Story/prelude_collapse" },
            { Murmur, "Audio/Story/prelude_murmur" },
            { MetalHit, "Audio/Story/prelude_metal_hit" },
            { Parry, "Audio/Story/final_parry" },
            { Shatter, "Audio/Story/final_shatter" },
            { Unlock, "Audio/Story/final_unlock" },
            { HeavyHammer, "Audio/Story/final_heavy_hammer" },
            { HammerDrag, "Audio/Story/final_hammer_drag" },
        };

        // 必要であれば外部からクリップを上書き登録できるマップ（未設定なら Resources を探す）
        static readonly Dictionary<string, AudioClip> s_overrides = new Dictionary<string, AudioClip>();
        static AudioSource s_ambientSource;
        static AudioSource s_oneShotSource;
        static Coroutine s_ambientFadeRoutine;
        static MonoBehaviour s_ambientFadeHost;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_overrides.Clear();
            s_ambientSource = null;
            s_oneShotSource = null;
            s_ambientFadeRoutine = null;
            s_ambientFadeHost = null;
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
                if (key == CheerFade)
                {
                    FadeOutAmbient(host, 1.0f);
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
            StopAmbientFade();
            if (s_ambientSource != null && s_ambientSource.isPlaying) s_ambientSource.Stop();
        }

        static void StopAmbientFade()
        {
            if (s_ambientFadeRoutine != null && s_ambientFadeHost != null)
            {
                s_ambientFadeHost.StopCoroutine(s_ambientFadeRoutine);
            }
            s_ambientFadeRoutine = null;
            s_ambientFadeHost = null;
        }

        static void FadeOutAmbient(MonoBehaviour host, float duration)
        {
            if (s_ambientSource == null || !s_ambientSource.isPlaying) return;
            if (host == null || duration <= 0.01f)
            {
                StopAmbient();
                return;
            }
            StopAmbientFade();
            s_ambientFadeHost = host;
            s_ambientFadeRoutine = host.StartCoroutine(FadeAmbientRoutine(duration));
        }

        static IEnumerator FadeAmbientRoutine(float duration)
        {
            float startVol = s_ambientSource != null ? s_ambientSource.volume : 0f;
            float elapsed = 0f;
            while (elapsed < duration && s_ambientSource != null && s_ambientSource.isPlaying)
            {
                elapsed += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                s_ambientSource.volume = Mathf.Lerp(startVol, 0f, u);
                yield return null;
            }
            if (s_ambientSource != null && s_ambientSource.isPlaying) s_ambientSource.Stop();
            s_ambientFadeRoutine = null;
            s_ambientFadeHost = null;
        }

        static IEnumerator PlayDelayed(string key, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            PlayKey(key);
        }

        static void PlayKey(string key)
        {
            var clip = Resolve(key);
            float pitch = 1f;
            float volScale = 1f;
            if (clip == null)
            {
                // 最終章用の仮SEフォールバック（本番素材が Resources に置かれればそちらを優先する）
                var ms = MatchSounds.Instance;
                if (ms != null)
                {
                    switch (key)
                    {
                        case Parry: clip = ms.HitBlock; pitch = 1.35f; volScale = 1.0f; break;
                        case Shatter: clip = ms.KO; pitch = 0.85f; volScale = 1.0f; break;
                        case Unlock: clip = ms.Victory; pitch = 1.0f; volScale = 0.85f; break;
                        case HeavyHammer: clip = ms.Knock; pitch = 0.52f; volScale = 1.15f; break;
                        case HammerDrag: clip = ms.BlockHitBlock; pitch = 0.42f; volScale = 0.7f; break;
                    }
                }
            }
            if (clip == null) return;

            EnsureSources();
            float volume = 0.9f * volScale * GameSettings.SeVolume;
            if (key == Cheer || key == Murmur)
            {
                if (s_ambientSource != null)
                {
                    StopAmbientFade();
                    s_ambientSource.Stop();
                    s_ambientSource.clip = clip;
                    s_ambientSource.volume = volume * 0.75f;
                    s_ambientSource.loop = false;
                    s_ambientSource.Play();
                }
            }
            else if (s_oneShotSource != null)
            {
                s_oneShotSource.pitch = pitch;
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
