using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Yurube.Story.Vfx
{
    // ストーリーカットシーン用の全画面UIオーバーレイ演出（fx_fade / fx_vignette / fx_flash）。
    //  ・既存の Fader（シーン遷移・order 32767）や StoryMovie.m_fadeGroup（order 44）を置き換えず共存／ラップする。
    //  ・暗転（fx_fade）とビネット（fx_vignette）は order 50（StoryMovieScreen=44 より前面、会話ウィンドウ StoryUI=60 より背面）。
    //  ・閃光（fx_flash）は order 65（会話ウィンドウ前面・光過敏配慮で連発禁止＆最小0.12秒フェードを強制）。
    //  ・すべて Time.unscaledDeltaTime で動作し、Time.timeScale == 0 でも正常に進行する。
    public class ScreenOverlayFx : MonoBehaviour
    {
        public const int BackOverlayOrder = 50;
        public const int FrontOverlayOrder = 65;

        // 光過敏への配慮：フラッシュは最小0.12秒以上の滑らかな減衰とし、短時間の連続発光を禁止する
        public const float MinFlashDuration = 0.12f;
        public const float FlashCooldownSeconds = 0.45f;

        [Header("fx_fade（暗転・明転）")]
        [SerializeField] Color m_defaultFadeColor = Color.black;
        [SerializeField, Min(0.05f)] float m_defaultFadeInSeconds = 0.45f;
        [SerializeField, Min(0f)] float m_defaultFadeHoldSeconds = 0.6f;
        [SerializeField, Min(0.05f)] float m_defaultFadeOutSeconds = 0.55f;
        [SerializeField] AnimationCurve m_fadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("fx_vignette（周縁の暗転）")]
        [SerializeField] Color m_defaultVignetteColor = new Color(0.03f, 0.02f, 0.06f, 1f);
        [SerializeField, Range(0f, 1f)] float m_defaultVignetteStrength = 0.82f;
        [SerializeField, Min(0.05f)] float m_defaultVignetteInSeconds = 0.55f;
        [SerializeField, Min(0f)] float m_defaultVignetteHoldSeconds = 1.4f;
        [SerializeField, Min(0.05f)] float m_defaultVignetteOutSeconds = 0.9f;
        [SerializeField] Sprite m_vignetteSprite;

        [Header("fx_flash（単発フラッシュ・連発防止付き）")]
        [SerializeField] Color m_defaultFlashColor = new Color(1f, 0.98f, 0.92f, 1f);
        [SerializeField, Range(0f, 1f)] float m_defaultFlashStrength = 0.78f;
        [SerializeField, Min(0.12f)] float m_defaultFlashDuration = 0.24f;

        Canvas m_backCanvas;
        Canvas m_frontCanvas;
        Image m_fadeImage;
        Image m_vignetteImage;
        Image m_flashImage;
        Texture2D m_runtimeVignetteTex;
        Sprite m_runtimeVignetteSprite;

        Coroutine m_fadeRoutine;
        Coroutine m_vignetteRoutine;
        Coroutine m_flashRoutine;
        float m_lastFlashTime = -999f;
        bool m_built;
        bool m_paused;

        // 既存 StoryMovie のフェードを任意でラップ連携するためのコールバック（未設定なら独自オーバーレイのみ）
        StoryMovie m_wrappedMovie;

        public float CurrentFadeAlpha => m_fadeImage != null ? m_fadeImage.color.a : 0f;
        public float CurrentVignetteAlpha => m_vignetteImage != null ? m_vignetteImage.color.a : 0f;
        public float CurrentFlashAlpha => m_flashImage != null ? m_flashImage.color.a : 0f;
        public bool IsPaused => m_paused;
        public bool HasActiveFade => CurrentFadeAlpha > 0.005f || m_fadeRoutine != null;

        // ポーズ中はフェードの進行だけを止め、現在表示中の暗転・ビネット状態を維持する
        public void SetPaused(bool paused) => m_paused = paused;

        public static ScreenOverlayFx Create(Transform parent = null, StoryMovie wrappedMovie = null)
        {
            var go = new GameObject("StoryScreenOverlayFx");
            if (parent != null) go.transform.SetParent(parent, false);
            var fx = go.AddComponent<ScreenOverlayFx>();
            fx.m_wrappedMovie = wrappedMovie;
            fx.EnsureBuilt();
            return fx;
        }

        public void BindStoryMovie(StoryMovie movie)
        {
            m_wrappedMovie = movie;
        }

        public void SetVignetteSprite(Sprite sprite)
        {
            m_vignetteSprite = sprite;
            if (m_vignetteImage != null && sprite != null)
            {
                m_vignetteImage.sprite = sprite;
            }
        }

        void Awake()
        {
            EnsureBuilt();
        }

        void OnDestroy()
        {
            if (m_runtimeVignetteSprite != null) Destroy(m_runtimeVignetteSprite);
            if (m_runtimeVignetteTex != null) Destroy(m_runtimeVignetteTex);
        }

        public void EnsureBuilt()
        {
            if (m_built) return;
            m_built = true;

            m_backCanvas = CreateCanvas("StoryOverlayBack", BackOverlayOrder);
            m_frontCanvas = CreateCanvas("StoryOverlayFront", FrontOverlayOrder);

            m_vignetteImage = CreateFullImage("Vignette", m_backCanvas.transform, new Color(0f, 0f, 0f, 0f));
            m_vignetteImage.sprite = ResolveVignetteSprite();
            m_vignetteImage.type = Image.Type.Simple;
            m_vignetteImage.enabled = false;

            m_fadeImage = CreateFullImage("Fade", m_backCanvas.transform, new Color(0f, 0f, 0f, 0f));
            m_fadeImage.enabled = false;

            m_flashImage = CreateFullImage("Flash", m_frontCanvas.transform, new Color(1f, 1f, 1f, 0f));
            m_flashImage.enabled = false;
        }

        Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        static Image CreateFullImage(string name, Transform parent, Color initialColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = initialColor;
            img.raycastTarget = false;
            return img;
        }

        Sprite ResolveVignetteSprite()
        {
            if (m_vignetteSprite != null) return m_vignetteSprite;
            if (m_runtimeVignetteSprite != null) return m_runtimeVignetteSprite;

            const int size = 256;
            m_runtimeVignetteTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "StoryOverlayVignetteRuntime"
            };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 1.12f, d));
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            m_runtimeVignetteTex.SetPixels(px);
            m_runtimeVignetteTex.Apply();
            m_runtimeVignetteSprite = Sprite.Create(
                m_runtimeVignetteTex,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f);
            return m_runtimeVignetteSprite;
        }

        //========================================
        // fx_fade（暗転・明転）
        //========================================

        public void PlayFade(Color? color = null, float? inSeconds = null, float? holdSeconds = null,
                             float? outSeconds = null, AnimationCurve curve = null, bool loop = false)
        {
            EnsureBuilt();
            StopFade(false);
            Color c = color ?? m_defaultFadeColor;
            float inSec = Mathf.Max(0.01f, inSeconds ?? m_defaultFadeInSeconds);
            float holdSec = Mathf.Max(0f, holdSeconds ?? m_defaultFadeHoldSeconds);
            float outSec = Mathf.Max(0.01f, outSeconds ?? m_defaultFadeOutSeconds);
            AnimationCurve cv = curve ?? m_fadeCurve;
            m_fadeRoutine = StartCoroutine(FadeCycleRoutine(c, inSec, holdSec, outSec, cv, loop));
        }

        // 指定の濃さまでフェードして保持する（場面転換の暗転／明転用）
        public void FadeTo(float targetAlpha, float duration, Color? color = null, AnimationCurve curve = null)
        {
            EnsureBuilt();
            StopFade(false);
            Color c = color ?? m_defaultFadeColor;
            m_fadeRoutine = StartCoroutine(FadeSingleRoutine(c, Mathf.Clamp01(targetAlpha), Mathf.Max(0.01f, duration), curve ?? m_fadeCurve));
        }

        // 既存 StoryMovie のフェードをラップして呼ぶ（StoryMovie が無ければ独自フェードへフォールバック）
        public Coroutine FadeStoryMovieBlack(bool toBlack, float duration)
        {
            if (m_wrappedMovie != null)
            {
                return StartCoroutine(toBlack ? m_wrappedMovie.FadeToBlack(duration) : m_wrappedMovie.FadeOutBlack(duration));
            }
            FadeTo(toBlack ? 1f : 0f, duration, Color.black);
            return m_fadeRoutine;
        }

        public void StopFade(bool clearAlpha = true)
        {
            if (m_fadeRoutine != null)
            {
                StopCoroutine(m_fadeRoutine);
                m_fadeRoutine = null;
            }
            if (clearAlpha && m_fadeImage != null)
            {
                var c = m_fadeImage.color;
                c.a = 0f;
                m_fadeImage.color = c;
                m_fadeImage.enabled = false;
            }
        }

        IEnumerator FadeCycleRoutine(Color color, float inSec, float holdSec, float outSec, AnimationCurve curve, bool loop)
        {
            do
            {
                yield return AnimateImageAlpha(m_fadeImage, color, 0f, 1f, inSec, curve);
                if (holdSec > 0f) yield return WaitUnscaled(holdSec);
                yield return AnimateImageAlpha(m_fadeImage, color, 1f, 0f, outSec, curve);
                if (loop) yield return WaitUnscaled(0.35f);
            }
            while (loop);

            m_fadeImage.enabled = false;
            m_fadeRoutine = null;
        }

        IEnumerator FadeSingleRoutine(Color color, float targetAlpha, float duration, AnimationCurve curve)
        {
            float from = m_fadeImage.enabled ? m_fadeImage.color.a : 0f;
            yield return AnimateImageAlpha(m_fadeImage, color, from, targetAlpha, duration, curve);
            if (targetAlpha <= 0.001f) m_fadeImage.enabled = false;
            m_fadeRoutine = null;
        }

        //========================================
        // fx_vignette（視界の周縁が一瞬暗くなる）
        //========================================

        public void PlayVignette(float? strength = null, float? inSeconds = null, float? holdSeconds = null,
                                 float? outSeconds = null, Color? color = null, bool loop = false)
        {
            EnsureBuilt();
            StopVignette(false);
            float str = Mathf.Clamp01(strength ?? m_defaultVignetteStrength);
            float inSec = Mathf.Max(0.05f, inSeconds ?? m_defaultVignetteInSeconds);
            float holdSec = Mathf.Max(0f, holdSeconds ?? m_defaultVignetteHoldSeconds);
            float outSec = Mathf.Max(0.05f, outSeconds ?? m_defaultVignetteOutSeconds);
            Color c = color ?? m_defaultVignetteColor;
            m_vignetteRoutine = StartCoroutine(VignetteRoutine(c, str, inSec, holdSec, outSec, loop));
        }

        public void StopVignette(bool clearAlpha = true)
        {
            if (m_vignetteRoutine != null)
            {
                StopCoroutine(m_vignetteRoutine);
                m_vignetteRoutine = null;
            }
            if (clearAlpha && m_vignetteImage != null)
            {
                var c = m_vignetteImage.color;
                c.a = 0f;
                m_vignetteImage.color = c;
                m_vignetteImage.enabled = false;
            }
        }

        IEnumerator VignetteRoutine(Color color, float strength, float inSec, float holdSec, float outSec, bool loop)
        {
            m_vignetteImage.sprite = ResolveVignetteSprite();
            do
            {
                yield return AnimateImageAlpha(m_vignetteImage, color, 0f, strength, inSec, m_fadeCurve);
                if (holdSec > 0f) yield return WaitUnscaled(holdSec);
                yield return AnimateImageAlpha(m_vignetteImage, color, strength, 0f, outSec, m_fadeCurve);
                if (loop) yield return WaitUnscaled(0.45f);
            }
            while (loop);

            m_vignetteImage.enabled = false;
            m_vignetteRoutine = null;
        }

        //========================================
        // fx_flash（パリィ衝撃の単発閃光・点滅連発防止）
        //========================================

        public bool PlayFlash(float? strength = null, float? duration = null, Color? color = null)
        {
            EnsureBuilt();
            // 光過敏への配慮：連続発光の抑制（クールダウン未満の呼び出しは無視）
            if (Time.unscaledTime - m_lastFlashTime < FlashCooldownSeconds)
            {
                return false;
            }
            m_lastFlashTime = Time.unscaledTime;

            StopFlash(false);
            float str = Mathf.Clamp(strength ?? m_defaultFlashStrength, 0f, 0.9f);
            // 0.12秒以上の滑らかなフェードを強制（急激な1フレーム明滅を避ける）
            float dur = Mathf.Max(MinFlashDuration, duration ?? m_defaultFlashDuration);
            Color c = color ?? m_defaultFlashColor;
            m_flashRoutine = StartCoroutine(FlashRoutine(c, str, dur));
            return true;
        }

        public void StopFlash(bool clearAlpha = true)
        {
            if (m_flashRoutine != null)
            {
                StopCoroutine(m_flashRoutine);
                m_flashRoutine = null;
            }
            if (clearAlpha && m_flashImage != null)
            {
                var c = m_flashImage.color;
                c.a = 0f;
                m_flashImage.color = c;
                m_flashImage.enabled = false;
            }
        }

        IEnumerator FlashRoutine(Color color, float strength, float duration)
        {
            float rise = Mathf.Max(0.03f, duration * 0.22f);
            float fall = Mathf.Max(MinFlashDuration, duration - rise);
            yield return AnimateImageAlpha(m_flashImage, color, 0f, strength, rise, null);
            yield return AnimateImageAlpha(m_flashImage, color, strength, 0f, fall, m_fadeCurve);
            m_flashImage.enabled = false;
            m_flashRoutine = null;
        }

        //========================================
        // 共通停止・補助
        //========================================

        public void StopByKey(string key)
        {
            switch (key)
            {
                case "fx_fade": StopFade(true); break;
                case "fx_vignette": StopVignette(true); break;
                case "fx_flash": StopFlash(true); break;
            }
        }

        public void StopAll()
        {
            StopFade(true);
            StopVignette(true);
            StopFlash(true);
        }

        IEnumerator AnimateImageAlpha(Image image, Color rgb, float from, float to, float duration, AnimationCurve curve)
        {
            if (image == null) yield break;
            image.enabled = true;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (m_paused) { yield return null; continue; }
                elapsed += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                float eased = curve != null ? curve.Evaluate(u) : u;
                float a = Mathf.Lerp(from, to, eased);
                image.color = new Color(rgb.r, rgb.g, rgb.b, a);
                yield return null;
            }
            image.color = new Color(rgb.r, rgb.g, rgb.b, to);
        }

        IEnumerator WaitUnscaled(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                if (m_paused) { yield return null; continue; }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
