using System;
using System.Collections;
using System.Collections.Generic;
using Nakagawa;
using Nakahira;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Yurube.Story.Vfx
{
    // ストーリー演出エフェクトの実行時再生マネージャ。
    //  ・Play(key, anchor, tint, scale) / Stop(key) / StopAll() でワールド・前面・画面効果を統一再生。
    //  ・fx_sparkle_burst のような UI 前面演出は、隔離座標の専用カメラ＋透過 RenderTexture を
    //    ScreenSpaceOverlay Canvas (sortingOrder=65) の RawImage に重ねて表示し、既存 UI（order 60）より前面に出す。
    //  ・既存の cue から vfx:<key> / vfx_stop:<key> / vfx_stop_all を解釈して再生できる。
    public class StoryEffectPlayer : MonoBehaviour
    {
        public const int ScreenFrontCanvasOrder = 65;
        static readonly Vector3 FrontStageOrigin = new Vector3(6200f, 0f, 0f);
        static readonly Vector3 FrontEffectOffset = new Vector3(0f, 0.35f, 4.8f);

        [Header("カタログ・画面効果（未設定なら自動取得／生成）")]
        [SerializeField] StoryVfxCatalog m_catalog;
        [SerializeField] ScreenOverlayFx m_overlayFx;
        [Tooltip("アンカー未指定時の既定生成位置（通常はカメラ前方が渡される）")]
        [SerializeField] Transform m_defaultAnchor;

        readonly Dictionary<string, List<StoryVfxInstance>> m_activeByKey = new Dictionary<string, List<StoryVfxInstance>>(StringComparer.Ordinal);
        readonly List<StoryVfxInstance> m_frontActive = new List<StoryVfxInstance>();
        readonly List<string> m_tempKeys = new List<string>();
        readonly List<Coroutine> m_pendingDelayed = new List<Coroutine>();
        bool m_explicitPaused;
        bool m_effectivePaused;

        // UI前面表示用ミニステージ（オンデマンド生成・未使用時は自動停止）
        Transform m_frontStageRoot;
        Camera m_frontCamera;
        RenderTexture m_frontRt;
        GameObject m_frontCanvasGo;
        RawImage m_frontView;

        public StoryVfxCatalog Catalog
        {
            get
            {
                if (m_catalog == null)
                {
                    var settings = StorySettings.Instance;
                    if (settings != null) m_catalog = settings.VfxCatalog;
                }
                return m_catalog;
            }
            set => m_catalog = value;
        }

        public ScreenOverlayFx OverlayFx
        {
            get
            {
                if (m_overlayFx == null)
                {
                    m_overlayFx = GetComponentInChildren<ScreenOverlayFx>(true);
                    if (m_overlayFx == null) m_overlayFx = ScreenOverlayFx.Create(transform);
                }
                return m_overlayFx;
            }
        }

        public static StoryEffectPlayer Create(Transform parent = null, StoryMovie wrappedMovie = null, StoryVfxCatalog catalog = null)
        {
            var go = new GameObject("StoryEffectPlayer");
            if (parent != null) go.transform.SetParent(parent, false);
            var player = go.AddComponent<StoryEffectPlayer>();
            if (catalog != null) player.m_catalog = catalog;
            player.m_overlayFx = ScreenOverlayFx.Create(go.transform, wrappedMovie);
            return player;
        }

        void OnDestroy()
        {
            StopAll(true);
            if (m_frontCamera != null) m_frontCamera.targetTexture = null;
            if (m_frontRt != null)
            {
                m_frontRt.Release();
                Destroy(m_frontRt);
                m_frontRt = null;
            }
            if (m_frontStageRoot != null) Destroy(m_frontStageRoot.gameObject);
            if (m_frontCanvasGo != null) Destroy(m_frontCanvasGo);
        }

        void Update()
        {
            // 対戦中のポーズ画面が開いている間は自動でパーティクルを停止し、画面効果の状態を維持する
            // （ストーリー会話中の DarumaMatch.Pause は SuppressForStory=true のため対象外）
            bool matchPauseMenuOpen = !PauseMenu.SuppressForStory
                && DarumaMatch.Current != null
                && DarumaMatch.Current.IsPaused;
            ApplyEffectivePause(m_explicitPaused || matchPauseMenuOpen);
        }

        void LateUpdate()
        {
            CleanupDeadFrontInstances();
        }

        public void SetPaused(bool paused)
        {
            m_explicitPaused = paused;
            bool matchPauseMenuOpen = !PauseMenu.SuppressForStory
                && DarumaMatch.Current != null
                && DarumaMatch.Current.IsPaused;
            ApplyEffectivePause(m_explicitPaused || matchPauseMenuOpen);
        }

        void ApplyEffectivePause(bool paused)
        {
            if (m_effectivePaused == paused) return;
            m_effectivePaused = paused;

            if (m_overlayFx != null) m_overlayFx.SetPaused(paused);
            foreach (var kv in m_activeByKey)
            {
                var list = kv.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] == null) { list.RemoveAt(i); continue; }
                    list[i].SetPaused(paused);
                }
            }
        }

        //========================================
        // 再生・停止 API
        //========================================

        public StoryVfxInstance Play(string key, Transform anchor = null, Color? tint = null, float scale = 1f, bool? loopOverride = null)
        {
            if (string.IsNullOrEmpty(key)) return null;

            // 画面効果（UIオーバーレイ）はカタログ未設定でも再生可能にする
            if (TryPlayOverlayKey(key, tint, scale, loopOverride))
            {
                return null;
            }

            var catalog = Catalog;
            var entry = catalog != null ? catalog.Find(key) : null;
            if (entry == null)
            {
                Debug.LogWarning($"[StoryVfx] 未登録のエフェクトキーです: '{key}'（Yurube/ストーリーエフェクト/一括生成（既存は上書きしない）でカタログを生成してください）");
                return null;
            }

            if (entry.category == StoryVfxCategory.ScreenOverlay)
            {
                TryPlayOverlayKey(key, tint ?? entry.defaultTint, scale * entry.defaultScale, loopOverride ?? entry.looping);
                return null;
            }

            if (entry.prefab == null)
            {
                Debug.LogWarning($"[StoryVfx] エフェクト '{key}' のプレハブが未設定です。");
                return null;
            }

            if (entry.category == StoryVfxCategory.ScreenFront)
            {
                return SpawnScreenFront(entry, tint, scale, loopOverride);
            }

            Transform targetAnchor = anchor != null ? anchor : m_defaultAnchor;
            Vector3 pos = targetAnchor != null ? targetAnchor.position : Vector3.zero;
            Quaternion rot = targetAnchor != null ? targetAnchor.rotation : Quaternion.identity;
            return SpawnWorld(entry, pos, rot, targetAnchor, tint, scale, loopOverride);
        }

        public StoryVfxInstance PlayAt(string key, Vector3 worldPos, Quaternion rotation, Color? tint = null, float scale = 1f, bool? loopOverride = null)
        {
            if (string.IsNullOrEmpty(key)) return null;

            if (TryPlayOverlayKey(key, tint, scale, loopOverride))
            {
                return null;
            }

            var catalog = Catalog;
            var entry = catalog != null ? catalog.Find(key) : null;
            if (entry == null)
            {
                Debug.LogWarning($"[StoryVfx] 未登録のエフェクトキーです: '{key}'");
                return null;
            }

            if (entry.prefab == null)
            {
                Debug.LogWarning($"[StoryVfx] エフェクト '{key}' のプレハブが未設定です。");
                return null;
            }

            if (entry.category == StoryVfxCategory.ScreenFront)
            {
                return SpawnScreenFront(entry, tint, scale, loopOverride);
            }

            return SpawnWorld(entry, worldPos, rotation, null, tint, scale, loopOverride);
        }

        public void SetIntensity(string key, float intensity)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!m_activeByKey.TryGetValue(key, out var list)) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null) { list.RemoveAt(i); continue; }
                list[i].SetIntensity(intensity);
            }
        }

        public void Stop(string key, bool clearImmediate = false)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (m_overlayFx != null)
            {
                m_overlayFx.StopByKey(key);
            }

            if (!m_activeByKey.TryGetValue(key, out var list)) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var inst = list[i];
                if (inst != null) inst.Stop(clearImmediate);
            }
            if (clearImmediate) list.Clear();
            CleanupDeadFrontInstances();
        }

        public void StopAll(bool clearImmediate = false)
        {
            CancelPendingDelayed();

            if (m_overlayFx != null)
            {
                m_overlayFx.StopAll();
            }

            m_tempKeys.Clear();
            foreach (var k in m_activeByKey.Keys) m_tempKeys.Add(k);
            for (int k = 0; k < m_tempKeys.Count; k++)
            {
                var list = m_activeByKey[m_tempKeys[k]];
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] != null) list[i].Stop(clearImmediate);
                }
                if (clearImmediate) list.Clear();
            }

            if (clearImmediate)
            {
                m_frontActive.Clear();
                SetFrontStageActive(false);
            }
            else
            {
                CleanupDeadFrontInstances();
            }
        }

        public int ActiveParticleInstanceCount()
        {
            int count = 0;
            foreach (var kv in m_activeByKey)
            {
                var list = kv.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] == null) list.RemoveAt(i);
                    else count++;
                }
            }
            return count;
        }

        //========================================
        // 汎用 JSON VFX キュー実行（遅延・位置・色・大きさ・画面効果・キャンセル）
        //========================================

        public void CancelPendingDelayed()
        {
            for (int i = 0; i < m_pendingDelayed.Count; i++)
            {
                if (m_pendingDelayed[i] != null) StopCoroutine(m_pendingDelayed[i]);
            }
            m_pendingDelayed.Clear();
        }

        // Bスキップ時に残った暗転を解除する（暗転中でなければ何もしない）
        public void ClearFadeIfActive(float fadeOutDuration = 0.25f)
        {
            CancelPendingDelayed();
            if (m_overlayFx == null || !m_overlayFx.HasActiveFade) return;
            if (fadeOutDuration <= 0.01f) m_overlayFx.StopFade(true);
            else m_overlayFx.FadeTo(0f, fadeOutDuration);
        }

        public void ExecuteCue(StoryVfxCue cue, Func<StoryVfxCue, Vector3> positionResolver = null)
        {
            if (cue == null) return;
            if (cue.delay > 0.001f)
            {
                Coroutine co = null;
                co = StartCoroutine(ExecuteCueDelayedRoutine(cue, positionResolver, () => m_pendingDelayed.Remove(co)));
                m_pendingDelayed.Add(co);
            }
            else
            {
                ExecuteCueImmediate(cue, positionResolver);
            }
        }

        IEnumerator ExecuteCueDelayedRoutine(StoryVfxCue cue, Func<StoryVfxCue, Vector3> positionResolver, Action onDone)
        {
            float elapsed = 0f;
            while (elapsed < cue.delay)
            {
                if (m_effectivePaused) { yield return null; continue; }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            ExecuteCueImmediate(cue, positionResolver);
            onDone?.Invoke();
        }

        void ExecuteCueImmediate(StoryVfxCue cue, Func<StoryVfxCue, Vector3> positionResolver)
        {
            string action = string.IsNullOrEmpty(cue.action) ? "play" : cue.action.Trim().ToLowerInvariant();
            string key = NormalizeKey(cue.key);

            if (action == "stop_all")
            {
                StopAll(false);
                return;
            }
            if (action == "stop")
            {
                if (!string.IsNullOrEmpty(key)) Stop(key, false);
                return;
            }

            if (action == "fade_in" || action == "fade_out")
            {
                Color? col = TryParseHexColor(cue.color) ?? TryParseHexColor(cue.tint);
                float dur = cue.duration > 0.001f ? cue.duration : (action == "fade_in" ? 0.4f : 0.6f);
                if (action == "fade_in")
                {
                    if (cue.hold) OverlayFx.FadeTo(1f, dur, col);
                    else OverlayFx.PlayFade(color: col, inSeconds: dur);
                }
                else
                {
                    OverlayFx.FadeTo(0f, dur, col);
                }
                return;
            }

            if (string.IsNullOrEmpty(key)) return;

            Color? tint = TryParseHexColor(cue.tint) ?? TryParseHexColor(cue.color);
            float scale = cue.scale > 0.001f ? cue.scale : 1f;

            if (key == "fx_fade")
            {
                float dur = cue.duration > 0.001f ? cue.duration : 0.45f;
                if (cue.hold) OverlayFx.FadeTo(1f, dur, tint);
                else OverlayFx.PlayFade(color: tint, inSeconds: dur);
                return;
            }
            if (key == "fx_vignette")
            {
                float? inSec = cue.duration > 0.001f ? cue.duration : (float?)null;
                OverlayFx.PlayVignette(strength: Mathf.Clamp01(0.82f * scale), inSeconds: inSec, color: tint);
                return;
            }
            if (key == "fx_flash")
            {
                float? dur = cue.duration > 0.001f ? cue.duration : (float?)null;
                OverlayFx.PlayFlash(strength: Mathf.Clamp(0.78f * scale, 0.1f, 0.9f), duration: dur, color: tint);
                return;
            }

            Vector3 worldPos = positionResolver != null
                ? positionResolver(cue)
                : (m_defaultAnchor != null ? m_defaultAnchor.position : Vector3.zero);
            var inst = PlayAt(key, worldPos, Quaternion.identity, tint, scale, null);
            if (inst != null && m_effectivePaused)
            {
                inst.SetPaused(true);
            }
        }

        static string NormalizeKey(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string k = raw.Trim();
            if (k.StartsWith("vfx:", StringComparison.Ordinal))
                k = k.Substring("vfx:".Length).Trim();
            return k;
        }

        static Color? TryParseHexColor(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            return ColorUtility.TryParseHtmlString(html.Trim(), out var c) ? c : (Color?)null;
        }

        //========================================
        // 既存 cue 連携（vfx:<key> / vfx_stop:<key> / vfx_stop_all）
        //========================================

        // カンマ区切りで複数指定にも対応（例: "vfx:fx_parry_impact,vfx:fx_flash"）。
        // 1つでも vfx 系コマンドを処理した場合に true を返す。
        public bool TryHandleCue(string cueSpec, Transform defaultAnchor = null, Vector3? defaultWorldPos = null)
        {
            if (string.IsNullOrEmpty(cueSpec)) return false;
            bool handled = false;
            var parts = cueSpec.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i] != null ? parts[i].Trim() : "";
                if (string.IsNullOrEmpty(token)) continue;

                if (token == "vfx_stop_all")
                {
                    StopAll(false);
                    handled = true;
                }
                else if (token.StartsWith("vfx_stop:", StringComparison.Ordinal))
                {
                    string key = token.Substring("vfx_stop:".Length).Trim();
                    Stop(key, false);
                    handled = true;
                }
                else if (token.StartsWith("vfx:", StringComparison.Ordinal))
                {
                    string key = token.Substring("vfx:".Length).Trim();
                    if (defaultAnchor != null)
                        Play(key, defaultAnchor, null, 1f, null);
                    else if (defaultWorldPos.HasValue)
                        PlayAt(key, defaultWorldPos.Value, Quaternion.identity, null, 1f, null);
                    else
                        Play(key, null, null, 1f, null);
                    handled = true;
                }
            }
            return handled;
        }

        //========================================
        // 内部生成
        //========================================

        bool TryPlayOverlayKey(string key, Color? tint, float scale, bool? loopOverride)
        {
            switch (key)
            {
                case "fx_fade":
                    OverlayFx.PlayFade(color: tint, loop: loopOverride ?? false);
                    return true;

                case "fx_vignette":
                    OverlayFx.PlayVignette(
                        strength: Mathf.Clamp01(0.82f * scale),
                        color: tint,
                        loop: loopOverride ?? false);
                    return true;

                case "fx_flash":
                    OverlayFx.PlayFlash(
                        strength: Mathf.Clamp(0.78f * scale, 0.1f, 0.9f),
                        color: tint);
                    return true;

                default:
                    return false;
            }
        }

        StoryVfxInstance SpawnWorld(StoryVfxEntry entry, Vector3 worldPos, Quaternion rotation, Transform followAnchor,
                                    Color? tint, float scale, bool? loopOverride)
        {
            var go = Instantiate(entry.prefab, worldPos, rotation, followAnchor);
            go.name = "StoryVfx_" + entry.key;

            var inst = go.GetComponent<StoryVfxInstance>();
            if (inst == null) inst = go.AddComponent<StoryVfxInstance>();

            Color finalTint = tint ?? entry.defaultTint;
            float finalScale = Mathf.Max(0.05f, scale * entry.defaultScale);
            bool finalLoop = loopOverride ?? entry.looping;

            inst.Play(finalTint, finalScale, finalLoop, entry.useUnscaledTime);
            RegisterActive(entry.key, inst);
            return inst;
        }

        StoryVfxInstance SpawnScreenFront(StoryVfxEntry entry, Color? tint, float scale, bool? loopOverride)
        {
            EnsureFrontStage();
            SetFrontStageActive(true);

            Vector3 spawnPos = FrontStageOrigin + FrontEffectOffset;
            var go = Instantiate(entry.prefab, spawnPos, Quaternion.identity, m_frontStageRoot);
            go.name = "StoryVfxFront_" + entry.key;

            var inst = go.GetComponent<StoryVfxInstance>();
            if (inst == null) inst = go.AddComponent<StoryVfxInstance>();

            Color finalTint = tint ?? entry.defaultTint;
            float finalScale = Mathf.Max(0.05f, scale * entry.defaultScale);
            bool finalLoop = loopOverride ?? entry.looping;

            inst.Play(finalTint, finalScale, finalLoop, entry.useUnscaledTime);
            RegisterActive(entry.key, inst);
            m_frontActive.Add(inst);
            return inst;
        }

        void RegisterActive(string key, StoryVfxInstance inst)
        {
            if (!m_activeByKey.TryGetValue(key, out var list))
            {
                list = new List<StoryVfxInstance>();
                m_activeByKey[key] = list;
            }
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null) list.RemoveAt(i);
            }
            list.Add(inst);
        }

        void EnsureFrontStage()
        {
            if (m_frontStageRoot != null) return;

            m_frontStageRoot = new GameObject("StoryVfxFrontStage").transform;
            m_frontStageRoot.position = FrontStageOrigin;

            var camGo = new GameObject("StoryVfxFrontCamera");
            camGo.transform.SetParent(m_frontStageRoot, false);
            camGo.transform.localPosition = Vector3.zero;
            camGo.transform.localRotation = Quaternion.identity;

            m_frontCamera = camGo.AddComponent<Camera>();
            m_frontCamera.clearFlags = CameraClearFlags.SolidColor;
            m_frontCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            m_frontCamera.nearClipPlane = 0.1f;
            m_frontCamera.farClipPlane = 20f;
            m_frontCamera.fieldOfView = 40f;
            m_frontCamera.depth = 50;

            var camData = camGo.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null) camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false;

            int w = Mathf.Clamp(Screen.width, 640, 1920);
            int h = Mathf.Clamp(Screen.height, 360, 1080);
            m_frontRt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
            {
                name = "StoryVfxFrontRT",
            };
            m_frontCamera.targetTexture = m_frontRt;

            m_frontCanvasGo = new GameObject("StoryVfxFrontCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            m_frontCanvasGo.transform.SetParent(transform, false);
            var canvas = m_frontCanvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = ScreenFrontCanvasOrder;

            var scaler = m_frontCanvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var viewGo = new GameObject("FrontParticlesView", typeof(RectTransform), typeof(RawImage));
            viewGo.transform.SetParent(m_frontCanvasGo.transform, false);
            var rect = (RectTransform)viewGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            m_frontView = viewGo.GetComponent<RawImage>();
            m_frontView.texture = m_frontRt;
            m_frontView.color = Color.white;
            m_frontView.raycastTarget = false;
        }

        void SetFrontStageActive(bool active)
        {
            if (m_frontCamera != null) m_frontCamera.enabled = active;
            if (m_frontView != null) m_frontView.enabled = active;
        }

        void CleanupDeadFrontInstances()
        {
            if (m_frontActive.Count == 0) return;
            for (int i = m_frontActive.Count - 1; i >= 0; i--)
            {
                if (m_frontActive[i] == null) m_frontActive.RemoveAt(i);
            }
            if (m_frontActive.Count == 0)
            {
                SetFrontStageActive(false);
            }
        }
    }
}
