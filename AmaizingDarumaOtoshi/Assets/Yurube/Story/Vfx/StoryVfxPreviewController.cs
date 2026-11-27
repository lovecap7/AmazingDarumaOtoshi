using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Yurube.Story.Vfx
{
    // ストーリー演出エフェクトの単体プレビュー確認ツール。
    //  ・左ドック: 優先度フィルタ付きエフェクト一覧（クリック即再生対応・スクロールバー付き）
    //  ・右ドック: 選択中エフェクト詳細、再生/ループ/停止、Scale/Intensity/Tint(RGB+プリセット)、検証ツール
    //  ・すべて VerticalLayoutGroup / HorizontalLayoutGroup で自動配置し、UIや文字の重なりを防止。
    //  ・Dynamic TTF（AchievementBodyFont）を優先使用し、日本語の文字化け（□）を防止。
    public class StoryVfxPreviewController : MonoBehaviour
    {
        const int PreviewUiOrder = 100;
        const int MockDialogueOrder = 60;

        // 既存シーン参照互換のためフィールド名は維持
        [Header("参照（プレビューシーン生成で自動設定）")]
        [SerializeField] StoryEffectPlayer m_player;
        [SerializeField] StoryVfxCatalog m_catalog;
        [SerializeField] Transform m_spawnAnchor;
        [SerializeField] Transform m_orbitAnchor;
        [SerializeField] Camera m_camera;

        [Header("再生動作")]
        [Tooltip("ONのとき、次のエフェクトを再生すると直前まで再生していたエフェクトを自動で停止・消去する")]
        [SerializeField] bool m_stopPreviousOnPlay = true;

        [Header("カメラ操作")]
        [SerializeField] Vector3 m_cameraTarget = new Vector3(0f, 1.05f, 0f);
        [SerializeField] float m_cameraDistance = 7.4f;
        [SerializeField] float m_cameraYaw = 18f;
        [SerializeField] float m_cameraPitch = 15f;

        // カラーテーマ（ダークモダン・高コントラスト）
        static readonly Color ColPanelBg     = new Color(0.07f, 0.08f, 0.11f, 0.94f);
        static readonly Color ColCardBg      = new Color(0.11f, 0.13f, 0.18f, 0.96f);
        static readonly Color ColDivider     = new Color(1f, 1f, 1f, 0.09f);
        static readonly Color ColRowNormal   = new Color(0.13f, 0.15f, 0.20f, 0.92f);
        static readonly Color ColRowSelected = new Color(0.24f, 0.21f, 0.12f, 0.98f);
        static readonly Color ColAccentGold  = new Color(0.96f, 0.75f, 0.22f, 1f);
        static readonly Color ColTextMain    = new Color(0.96f, 0.97f, 0.99f, 1f);
        static readonly Color ColTextSub     = new Color(0.72f, 0.76f, 0.84f, 1f);
        static readonly Color ColP1          = new Color(0.20f, 0.68f, 0.42f, 1f);
        static readonly Color ColP2          = new Color(0.24f, 0.52f, 0.88f, 1f);
        static readonly Color ColP3          = new Color(0.58f, 0.36f, 0.80f, 1f);

        static readonly (string Name, Color Color)[] TintPresets =
        {
            ("通常(白)", Color.white),
            ("8年前",    StoryVfxPalette.PastMutedTint),
            ("暗闇",     StoryVfxPalette.DarkMoodTint),
            ("金色",     StoryVfxPalette.GoldBright),
            ("あかだる", StoryVfxPalette.Akadaru),
            ("あおだる", StoryVfxPalette.Aodaru),
            ("みだる",   StoryVfxPalette.Midaru),
            ("だるきんぐ", StoryVfxPalette.DaruKing),
        };

        static readonly (string Name, Color BgColor, float KeyIntensity)[] BackgroundModes =
        {
            ("暗",   new Color(0.05f, 0.06f, 0.09f, 1f), 0.85f),
            ("標準", new Color(0.16f, 0.18f, 0.23f, 1f), 1.40f),
            ("明",   new Color(0.58f, 0.62f, 0.68f, 1f), 1.80f),
        };

        // 短い日本語名（一覧行の視認性用）
        static readonly Dictionary<string, string> ShortNames = new Dictionary<string, string>
        {
            { "fx_fade",             "暗転・明転（画面フェード）" },
            { "fx_vignette",         "視界周縁の暗転（ビネット）" },
            { "fx_block_collapse_s", "積み木崩壊（小・2-3個）" },
            { "fx_dust_puff_small",  "小さな床のほこり" },
            { "fx_confetti",         "七色の紙吹雪" },
            { "fx_block_collapse_l", "大きな塔・舞台の崩壊" },
            { "fx_throne_shatter",   "王座と盾の粉砕（金・石）" },
            { "fx_parry_impact",     "パリィ衝撃リング＋火花" },
            { "fx_sword_trail",      "王の剣の飛翔軌跡" },
            { "fx_darkness_aura",    "闇落ちオーラ（黒紫もや）" },
            { "fx_shard_swirl",      "七色欠片の螺旋旋回" },
            { "fx_arena_collapse",   "闘技場の時間差崩落" },
            { "fx_sparkle_burst",    "金色のきらめき（UI前面）" },
            { "fx_flash",            "単発フラッシュ（連発防止）" },
            { "fx_dust_motes",       "光の中を舞うほこり" },
        };

        class EntryRowView
        {
            public int EntryIndex;
            public GameObject Root;
            public Image Bg;
            public Image AccentBar;
            public TextMeshProUGUI KeyText;
        }

        readonly List<StoryVfxEntry> m_allEntries = new List<StoryVfxEntry>();
        readonly List<StoryVfxEntry> m_filteredEntries = new List<StoryVfxEntry>();
        readonly List<EntryRowView> m_rows = new List<EntryRowView>();
        readonly List<Image> m_filterTabBgs = new List<Image>();
        readonly List<TextMeshProUGUI> m_filterTabLabels = new List<TextMeshProUGUI>();
        readonly List<Image> m_bgModeBgs = new List<Image>();
        readonly List<TextMeshProUGUI> m_bgModeLabels = new List<TextMeshProUGUI>();

        int m_priorityFilter; // 0=All, 1=P1, 2=P2, 3=P3
        int m_selectedIndex;
        int m_bgModeIndex = 1;
        Color m_tint = Color.white;
        float m_scale = 1f;
        float m_intensity = 1f;
        bool m_autoPlayOnSelect = true;
        bool m_paused;
        bool m_showMockDialogue = true;
        bool m_showDummy = true;
        bool m_uiHidden;
        bool m_orbitRunning;
        float m_orbitPhase;
        bool m_suppressSliderEvents;

        TMP_FontAsset m_font;
        TMP_FontAsset m_runtimeFont;
        Light m_keyLight;
        GameObject m_dummyObject;

        GameObject m_dockRoot;
        GameObject m_showUiChip;
        GameObject m_mockDialogueCanvas;
        RectTransform m_listContent;

        TextMeshProUGUI m_detailKey;
        TextMeshProUGUI m_detailTags;
        TextMeshProUGUI m_detailDesc;
        TextMeshProUGUI m_autoPlayLabel;
        Image m_autoPlayBg;

        TextMeshProUGUI m_scaleValText;
        TextMeshProUGUI m_intensityValText;
        TextMeshProUGUI m_rValText, m_gValText, m_bValText;
        Image m_tintSwatch;
        Slider m_sliderScale, m_sliderIntensity, m_sliderR, m_sliderG, m_sliderB;

        TextMeshProUGUI m_pauseBtnLabel;
        Image m_pauseBtnBg;
        TextMeshProUGUI m_dialogueBtnLabel;
        Image m_dialogueBtnBg;
        TextMeshProUGUI m_dummyBtnLabel;
        Image m_dummyBtnBg;
        TextMeshProUGUI m_statusText;

        StoryVfxInstance m_lastInstance;

        void Awake()
        {
            EnsureEventSystem();
            EnsurePlayer();
            if (m_camera == null) m_camera = Camera.main;
            m_dummyObject = GameObject.Find("ReferenceDarumaSilhouette");
            var lightGo = GameObject.Find("Key Light");
            if (lightGo != null) m_keyLight = lightGo.GetComponent<Light>();

            ResolveFont();
            CollectAllEntries();
            BuildMockDialogueCanvas();
            BuildPreviewUi();
            ApplyBackgroundMode(1);
            SetPriorityFilter(0, playIfAuto: false);
            UpdateCameraTransform();
        }

        void OnDestroy()
        {
            if (m_paused || Mathf.Abs(Time.timeScale - 1f) > 0.001f)
            {
                Time.timeScale = 1f;
            }
            if (m_runtimeFont != null)
            {
                Destroy(m_runtimeFont);
                m_runtimeFont = null;
            }
        }

        void Update()
        {
            HandleKeyboardShortcuts();
            HandleCameraInput();
            UpdateOrbitAnchor();
            UpdateStatusText();
        }

        void ResolveFont()
        {
            // Dynamic TTF を優先利用（漢字・記号の □ 文字化けを完全に防ぐ）
            var ttf = Resources.Load<Font>("AchievementBodyFont");
            if (ttf != null)
            {
                m_runtimeFont = TMP_FontAsset.CreateFontAsset(ttf);
                if (m_runtimeFont != null)
                {
                    m_font = m_runtimeFont;
                    return;
                }
            }
            m_font = PresentationSettings.DefaultFont;
        }

        void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        void EnsurePlayer()
        {
            if (m_player == null) m_player = FindAnyObjectByType<StoryEffectPlayer>();
            if (m_player == null) m_player = StoryEffectPlayer.Create(transform, null, m_catalog);
            if (m_catalog != null && m_player.Catalog == null) m_player.Catalog = m_catalog;
            if (m_catalog == null) m_catalog = m_player.Catalog;

            if (m_spawnAnchor == null)
            {
                var anchorGo = new GameObject("VfxSpawnAnchor");
                anchorGo.transform.position = new Vector3(0f, 0.85f, 0f);
                m_spawnAnchor = anchorGo.transform;
            }
            if (m_orbitAnchor == null)
            {
                var orbitGo = new GameObject("VfxOrbitAnchor");
                orbitGo.transform.position = new Vector3(-2.5f, 1.3f, 0f);
                m_orbitAnchor = orbitGo.transform;
            }
        }

        void CollectAllEntries()
        {
            m_allEntries.Clear();
            if (m_catalog != null && m_catalog.Entries != null && m_catalog.Entries.Count > 0)
            {
                for (int i = 0; i < m_catalog.Entries.Count; i++)
                {
                    if (m_catalog.Entries[i] != null) m_allEntries.Add(m_catalog.Entries[i]);
                }
                m_allEntries.Sort((a, b) =>
                {
                    int p = ((int)a.priority).CompareTo((int)b.priority);
                    if (p != 0) return p;
                    return string.CompareOrdinal(a.key, b.key);
                });
            }
            else
            {
                m_allEntries.Add(new StoryVfxEntry { key = "fx_fade", label = "暗転・明転（カタログ未生成）", priority = StoryVfxPriority.P1, category = StoryVfxCategory.ScreenOverlay, defaultTint = Color.black });
                m_allEntries.Add(new StoryVfxEntry { key = "fx_vignette", label = "視界周縁の暗転（カタログ未生成）", priority = StoryVfxPriority.P1, category = StoryVfxCategory.ScreenOverlay, defaultTint = new Color(0.03f, 0.02f, 0.06f, 1f) });
                m_allEntries.Add(new StoryVfxEntry { key = "fx_flash", label = "単発フラッシュ（カタログ未生成）", priority = StoryVfxPriority.P3, category = StoryVfxCategory.ScreenOverlay, defaultTint = new Color(1f, 0.98f, 0.92f, 1f) });
            }
        }

        //========================================
        // フィルタ・選択・再生
        //========================================

        void SetPriorityFilter(int filter, bool playIfAuto)
        {
            m_priorityFilter = filter;
            for (int i = 0; i < m_filterTabBgs.Count; i++)
            {
                bool active = i == filter;
                m_filterTabBgs[i].color = active ? ColAccentGold : new Color(0.16f, 0.18f, 0.24f, 1f);
                if (i < m_filterTabLabels.Count)
                    m_filterTabLabels[i].color = active ? new Color(0.08f, 0.08f, 0.10f, 1f) : ColTextMain;
            }

            m_filteredEntries.Clear();
            for (int i = 0; i < m_allEntries.Count; i++)
            {
                var e = m_allEntries[i];
                if (filter == 0 || (int)e.priority == filter)
                    m_filteredEntries.Add(e);
            }

            RebuildListRows();
            if (m_filteredEntries.Count > 0)
            {
                SelectEntry(0, playIfAuto);
            }
        }

        void RebuildListRows()
        {
            if (m_listContent == null) return;
            for (int i = m_listContent.childCount - 1; i >= 0; i--)
            {
                Destroy(m_listContent.GetChild(i).gameObject);
            }
            m_rows.Clear();

            for (int i = 0; i < m_filteredEntries.Count; i++)
            {
                int idx = i;
                var entry = m_filteredEntries[i];
                var row = CreateEntryRow(m_listContent, entry, () => SelectEntry(idx, m_autoPlayOnSelect));
                row.EntryIndex = idx;
                m_rows.Add(row);
            }
        }

        void SelectEntry(int index, bool autoPlay)
        {
            if (m_filteredEntries.Count == 0) return;
            m_selectedIndex = Mathf.Clamp(index, 0, m_filteredEntries.Count - 1);
            var entry = m_filteredEntries[m_selectedIndex];

            for (int i = 0; i < m_rows.Count; i++)
            {
                bool sel = i == m_selectedIndex;
                m_rows[i].Bg.color = sel ? ColRowSelected : ColRowNormal;
                m_rows[i].AccentBar.enabled = sel;
                m_rows[i].KeyText.color = sel ? ColAccentGold : ColTextMain;
            }

            UpdateDetailCard(entry);

            if (autoPlay)
            {
                PlaySelected(entry.looping);
            }
        }

        void UpdateDetailCard(StoryVfxEntry entry)
        {
            if (entry == null) return;
            if (m_detailKey != null) m_detailKey.text = entry.key;
            if (m_detailTags != null)
            {
                string cat = entry.category == StoryVfxCategory.ScreenOverlay ? "画面効果 (UI)"
                    : entry.category == StoryVfxCategory.ScreenFront ? "UI前面 (order 65)"
                    : "3Dワールド";
                string loopText = entry.looping ? "既定: ループ" : "既定: 1回再生";
                m_detailTags.text = $"[{entry.priority}]  {cat}  |  {loopText}";
            }
            if (m_detailDesc != null)
            {
                m_detailDesc.text = string.IsNullOrEmpty(entry.label) ? GetShortName(entry.key) : entry.label;
            }
        }

        public void PlaySelected(bool loop)
        {
            if (m_filteredEntries.Count == 0 || m_selectedIndex < 0 || m_selectedIndex >= m_filteredEntries.Count) return;

            if (m_stopPreviousOnPlay)
            {
                m_player.StopAll(clearImmediate: true);
                m_orbitRunning = false;
                m_lastInstance = null;
            }

            var entry = m_filteredEntries[m_selectedIndex];
            Transform anchor = entry.previewOrbitMotion ? m_orbitAnchor : m_spawnAnchor;
            if (entry.previewOrbitMotion)
            {
                m_orbitRunning = true;
                m_orbitPhase = 0f;
                if (m_orbitAnchor != null) m_orbitAnchor.position = new Vector3(-2.8f, 1.3f, 0f);
            }

            Color? overrideTint = m_tint;
            if (entry.category == StoryVfxCategory.ScreenOverlay && ApproximatelyColor(m_tint, Color.white))
            {
                overrideTint = null;
            }

            m_lastInstance = m_player.Play(entry.key, anchor, overrideTint, m_scale, loop);
            if (m_lastInstance != null)
            {
                m_lastInstance.SetIntensity(m_intensity);
            }
        }

        public void StopSelected()
        {
            if (m_filteredEntries.Count == 0 || m_selectedIndex < 0 || m_selectedIndex >= m_filteredEntries.Count) return;
            var entry = m_filteredEntries[m_selectedIndex];
            m_player.Stop(entry.key, false);
            if (entry.previewOrbitMotion) m_orbitRunning = false;
        }

        public void StopAllEffects()
        {
            m_player.StopAll(clearImmediate: true);
            m_orbitRunning = false;
            m_lastInstance = null;
        }

        //========================================
        // パラメータ・検証トグル
        //========================================

        void ApplyLiveParameters()
        {
            if (m_lastInstance != null)
            {
                m_lastInstance.SetTint(m_tint);
                m_lastInstance.SetScale(m_scale);
                m_lastInstance.SetIntensity(m_intensity);
            }
            if (m_scaleValText != null) m_scaleValText.text = $"{m_scale:F2}x";
            if (m_intensityValText != null) m_intensityValText.text = $"{m_intensity:F2}";
            if (m_rValText != null) m_rValText.text = $"{m_tint.r:F2}";
            if (m_gValText != null) m_gValText.text = $"{m_tint.g:F2}";
            if (m_bValText != null) m_bValText.text = $"{m_tint.b:F2}";
            if (m_tintSwatch != null) m_tintSwatch.color = m_tint;
        }

        void SetTintPreset(Color c)
        {
            m_tint = c;
            m_suppressSliderEvents = true;
            if (m_sliderR != null) m_sliderR.value = c.r;
            if (m_sliderG != null) m_sliderG.value = c.g;
            if (m_sliderB != null) m_sliderB.value = c.b;
            m_suppressSliderEvents = false;
            ApplyLiveParameters();
        }

        void ResetScaleAndIntensity()
        {
            m_scale = 1f;
            m_intensity = 1f;
            m_suppressSliderEvents = true;
            if (m_sliderScale != null) m_sliderScale.value = 1f;
            if (m_sliderIntensity != null) m_sliderIntensity.value = 1f;
            m_suppressSliderEvents = false;
            ApplyLiveParameters();
        }

        void ToggleAutoPlay()
        {
            m_autoPlayOnSelect = !m_autoPlayOnSelect;
            if (m_autoPlayLabel != null)
                m_autoPlayLabel.text = m_autoPlayOnSelect ? "選択したら自動再生: ON" : "選択したら自動再生: OFF";
            if (m_autoPlayBg != null)
                m_autoPlayBg.color = m_autoPlayOnSelect ? new Color(0.18f, 0.44f, 0.30f, 1f) : new Color(0.18f, 0.20f, 0.26f, 1f);
        }

        void ApplyBackgroundMode(int index)
        {
            m_bgModeIndex = Mathf.Clamp(index, 0, BackgroundModes.Length - 1);
            var mode = BackgroundModes[m_bgModeIndex];
            if (m_camera != null) m_camera.backgroundColor = mode.BgColor;
            if (m_keyLight != null) m_keyLight.intensity = mode.KeyIntensity;
            for (int i = 0; i < m_bgModeBgs.Count; i++)
            {
                bool active = i == m_bgModeIndex;
                m_bgModeBgs[i].color = active ? ColAccentGold : new Color(0.17f, 0.19f, 0.25f, 1f);
                if (i < m_bgModeLabels.Count)
                    m_bgModeLabels[i].color = active ? new Color(0.08f, 0.08f, 0.10f, 1f) : ColTextMain;
            }
        }

        void TogglePause()
        {
            m_paused = !m_paused;
            Time.timeScale = m_paused ? 0f : 1f;
            if (m_pauseBtnLabel != null)
                m_pauseBtnLabel.text = m_paused ? "timeScale: 0 (ポーズ中)" : "timeScale: 1 (通常)";
            if (m_pauseBtnBg != null)
                m_pauseBtnBg.color = m_paused ? new Color(0.68f, 0.32f, 0.18f, 1f) : new Color(0.20f, 0.22f, 0.30f, 1f);
        }

        void ToggleMockDialogue()
        {
            m_showMockDialogue = !m_showMockDialogue;
            if (m_mockDialogueCanvas != null) m_mockDialogueCanvas.SetActive(m_showMockDialogue);
            if (m_dialogueBtnLabel != null)
                m_dialogueBtnLabel.text = m_showMockDialogue ? "擬似会話枠(order60): 表示" : "擬似会話枠(order60): 非表示";
            if (m_dialogueBtnBg != null)
                m_dialogueBtnBg.color = m_showMockDialogue ? new Color(0.20f, 0.42f, 0.44f, 1f) : new Color(0.20f, 0.22f, 0.30f, 1f);
        }

        void ToggleDummy()
        {
            m_showDummy = !m_showDummy;
            if (m_dummyObject != null) m_dummyObject.SetActive(m_showDummy);
            if (m_dummyBtnLabel != null)
                m_dummyBtnLabel.text = m_showDummy ? "基準だるま: 表示" : "基準だるま: 非表示";
            if (m_dummyBtnBg != null)
                m_dummyBtnBg.color = m_showDummy ? new Color(0.32f, 0.26f, 0.34f, 1f) : new Color(0.20f, 0.22f, 0.30f, 1f);
        }

        void SetUiHidden(bool hidden)
        {
            m_uiHidden = hidden;
            if (m_dockRoot != null) m_dockRoot.SetActive(!hidden);
            if (m_showUiChip != null) m_showUiChip.SetActive(hidden);
        }

        //========================================
        // 入力・カメラ・ステータス更新
        //========================================

        void HandleKeyboardShortcuts()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.hKey.wasPressedThisFrame)
            {
                SetUiHidden(!m_uiHidden);
            }
            if (kb.spaceKey.wasPressedThisFrame)
            {
                if (m_filteredEntries.Count > 0 && m_selectedIndex < m_filteredEntries.Count)
                    PlaySelected(m_filteredEntries[m_selectedIndex].looping);
            }
            if (kb.escapeKey.wasPressedThisFrame)
            {
                StopAllEffects();
            }
            if (kb.upArrowKey.wasPressedThisFrame && m_filteredEntries.Count > 0)
            {
                int next = (m_selectedIndex - 1 + m_filteredEntries.Count) % m_filteredEntries.Count;
                SelectEntry(next, m_autoPlayOnSelect);
            }
            if (kb.downArrowKey.wasPressedThisFrame && m_filteredEntries.Count > 0)
            {
                int next = (m_selectedIndex + 1) % m_filteredEntries.Count;
                SelectEntry(next, m_autoPlayOnSelect);
            }
            if (kb.rKey.wasPressedThisFrame)
            {
                m_cameraYaw = 18f;
                m_cameraPitch = 15f;
                m_cameraDistance = 7.4f;
                UpdateCameraTransform();
            }
        }

        void HandleCameraInput()
        {
            if (m_camera == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                m_cameraYaw += delta.x * 0.25f;
                m_cameraPitch = Mathf.Clamp(m_cameraPitch - delta.y * 0.2f, 4f, 65f);
                UpdateCameraTransform();
            }

            // UI上でのホイール操作（一覧スクロール）とカメラズームが競合しないようにする
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!overUi)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    m_cameraDistance = Mathf.Clamp(m_cameraDistance - scroll * 0.004f, 3.0f, 18.0f);
                    UpdateCameraTransform();
                }
            }
        }

        void UpdateCameraTransform()
        {
            if (m_camera == null) return;
            Quaternion rot = Quaternion.Euler(m_cameraPitch, m_cameraYaw, 0f);
            Vector3 pos = m_cameraTarget + rot * new Vector3(0f, 0f, -m_cameraDistance);
            m_camera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(m_cameraTarget - pos, Vector3.up));
        }

        void UpdateOrbitAnchor()
        {
            if (!m_orbitRunning || m_orbitAnchor == null) return;
            m_orbitPhase += Time.unscaledDeltaTime * 2.4f;
            float x = Mathf.Sin(m_orbitPhase) * 2.6f;
            float y = 1.25f + Mathf.Sin(m_orbitPhase * 2f) * 0.42f;
            float z = Mathf.Cos(m_orbitPhase) * 1.3f;
            m_orbitAnchor.position = new Vector3(x, y, z);
        }

        void UpdateStatusText()
        {
            if (m_statusText == null || m_player == null) return;
            int activeCount = m_player.ActiveParticleInstanceCount();
            string bgName = BackgroundModes[m_bgModeIndex].Name;
            m_statusText.text = $"稼働VFX数: {activeCount}   |   timeScale: {Time.timeScale:F1}   |   背景: {bgName}";
        }

        static string GetShortName(string key)
        {
            return ShortNames.TryGetValue(key, out var name) ? name : key;
        }

        static bool ApproximatelyColor(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
        }

        //========================================
        // 擬似会話ウィンドウ（order 60・左右ドックに被らない中央配置）
        //========================================

        void BuildMockDialogueCanvas()
        {
            var go = new GameObject("MockDialogueCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = MockDialogueOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 下部の擬似会話枠（中央領域内に収める）
            var box = CreateImageRect("MockDialogueBox", go.transform, new Color(0.99f, 0.97f, 0.92f, 0.92f));
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0f);
            box.pivot = new Vector2(0.5f, 0f);
            box.anchoredPosition = new Vector2(0f, 24f);
            box.sizeDelta = new Vector2(920f, 108f);
            box.gameObject.AddComponent<Outline>().effectColor = new Color(0.22f, 0.14f, 0.08f, 0.85f);

            var label = CreateText("Text", box,
                "【擬似会話ウィンドウ（order 60）】\nfx_fade / fx_vignette はこの背面（order 50）、fx_sparkle_burst / fx_flash はこの前面（order 65）に描画されます。",
                20f, new Color(0.18f, 0.13f, 0.09f, 1f), TextAlignmentOptions.MidlineLeft);
            Stretch(label.rectTransform, 22f, 10f);

            // 上部の擬似解放バナー（fx_sparkle_burst の前面確認用）
            var banner = CreateImageRect("MockUnlockBanner", go.transform, new Color(0.14f, 0.09f, 0.22f, 0.92f));
            banner.anchorMin = banner.anchorMax = new Vector2(0.5f, 1f);
            banner.pivot = new Vector2(0.5f, 1f);
            banner.anchoredPosition = new Vector2(0f, -34f);
            banner.sizeDelta = new Vector2(700f, 66f);
            var outline = banner.gameObject.AddComponent<Outline>();
            outline.effectColor = ColAccentGold;
            outline.effectDistance = new Vector2(2f, -2f);

            var bText = CreateText("Text", banner, "だるきんぐが使えるようになった！（order 60 確認枠）", 23f, new Color(1f, 0.94f, 0.72f, 1f), TextAlignmentOptions.Center);
            bText.textWrappingMode = TextWrappingModes.NoWrap;
            bText.enableAutoSizing = true;
            bText.fontSizeMin = 15f;
            bText.fontSizeMax = 23f;
            Stretch(bText.rectTransform, 18f, 8f);

            m_mockDialogueCanvas = go;
            m_mockDialogueCanvas.SetActive(m_showMockDialogue);
        }

        //========================================
        // プレビューツール UI（左右2ドック・完全自動レイアウト）
        //========================================

        void BuildPreviewUi()
        {
            var canvasGo = new GameObject("StoryVfxPreviewUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = PreviewUiOrder;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // ドック全体の親（Hキーで一括表示・非表示）
            var dockRoot = new GameObject("DockRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            dockRoot.SetParent(canvasGo.transform, false);
            Stretch(dockRoot, 0f, 0f);
            m_dockRoot = dockRoot.gameObject;

            BuildLeftDock(dockRoot);
            BuildRightDock(dockRoot);

            // UI非表示中に出す復帰チップ（左上）
            var chip = CreateButton(canvasGo.transform, "ツールUIを表示 (H)", 38f, new Color(0.10f, 0.12f, 0.17f, 0.88f), () => SetUiHidden(false), out _, out var chipLabel);
            chipLabel.fontSize = 16f;
            var chipRect = (RectTransform)chip.transform;
            chipRect.anchorMin = chipRect.anchorMax = new Vector2(0f, 1f);
            chipRect.pivot = new Vector2(0f, 1f);
            chipRect.anchoredPosition = new Vector2(16f, -16f);
            chipRect.sizeDelta = new Vector2(190f, 38f);
            m_showUiChip = chip.gameObject;
            m_showUiChip.SetActive(false);
        }

        // ---- 左ドック：エフェクト一覧（幅 380px） ----
        void BuildLeftDock(Transform parent)
        {
            var panel = CreateImageRect("LeftDock", parent, ColPanelBg);
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(380f, 0f);

            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 14, 12);
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // 1. ヘッダー行（タイトル＋UI非表示ボタン）
            var headerRow = CreateHRow("HeaderRow", panel, 36f, 8f);
            var title = CreateText("Title", headerRow, "ストーリーエフェクト確認", 20f, ColTextMain, TextAlignmentOptions.MidlineLeft);
            title.fontStyle = FontStyles.Bold;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.enableAutoSizing = true;
            title.fontSizeMin = 15f;
            title.fontSizeMax = 20f;
            var titleLe = title.gameObject.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f;
            titleLe.preferredHeight = 36f;

            var hideBtn = CreateButton(headerRow, "隠す(H)", 32f, new Color(0.20f, 0.22f, 0.30f, 1f), () => SetUiHidden(true), out _, out var hideLbl);
            hideLbl.fontSize = 14f;
            var hideLe = hideBtn.GetComponent<LayoutElement>();
            hideLe.preferredWidth = 76f;
            hideLe.flexibleWidth = 0f;

            // 2. 優先度フィルタタブ
            var filterRow = CreateHRow("FilterRow", panel, 32f, 6f);
            m_filterTabBgs.Clear();
            m_filterTabLabels.Clear();
            string[] tabLabels =
            {
                $"すべて({m_allEntries.Count})",
                $"P1({CountPriority(StoryVfxPriority.P1)})",
                $"P2({CountPriority(StoryVfxPriority.P2)})",
                $"P3({CountPriority(StoryVfxPriority.P3)})",
            };
            for (int i = 0; i < tabLabels.Length; i++)
            {
                int filterIdx = i;
                var btn = CreateButton(filterRow, tabLabels[i], 32f, new Color(0.16f, 0.18f, 0.24f, 1f),
                    () => SetPriorityFilter(filterIdx, playIfAuto: false), out var bg, out var lbl);
                lbl.fontSize = 14f;
                lbl.fontStyle = FontStyles.Bold;
                btn.GetComponent<LayoutElement>().flexibleWidth = 1f;
                m_filterTabBgs.Add(bg);
                m_filterTabLabels.Add(lbl);
            }

            // 3. 選択時自動再生トグル
            var autoBtn = CreateButton(panel, "選択したら自動再生: ON", 30f, new Color(0.18f, 0.44f, 0.30f, 1f), ToggleAutoPlay, out m_autoPlayBg, out m_autoPlayLabel);
            m_autoPlayLabel.fontSize = 14f;
            _ = autoBtn;

            // 4. スクロール一覧（残りの高さをすべて使う）
            var listFrame = CreateImageRect("ListFrame", panel, new Color(0.05f, 0.06f, 0.08f, 0.90f));
            var listLe = listFrame.gameObject.AddComponent<LayoutElement>();
            listLe.flexibleHeight = 1f;
            listLe.minHeight = 200f;

            var scroll = listFrame.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(listFrame, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(4f, 4f);
            viewport.offsetMax = new Vector2(-16f, -4f);
            scroll.viewport = viewport;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;

            var contentVlg = content.GetComponent<VerticalLayoutGroup>();
            contentVlg.spacing = 4f;
            contentVlg.padding = new RectOffset(2, 2, 2, 2);
            contentVlg.childControlWidth = true;
            contentVlg.childControlHeight = true; // ここを true にして preferredHeight を厳密に適用
            contentVlg.childForceExpandWidth = true;
            contentVlg.childForceExpandHeight = false;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            m_listContent = content;

            // 右端スクロールバー
            var sbGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            sbGo.transform.SetParent(listFrame, false);
            var sbRect = (RectTransform)sbGo.transform;
            sbRect.anchorMin = new Vector2(1f, 0f);
            sbRect.anchorMax = new Vector2(1f, 1f);
            sbRect.pivot = new Vector2(1f, 0.5f);
            sbRect.offsetMin = new Vector2(-12f, 4f);
            sbRect.offsetMax = new Vector2(-3f, -4f);
            sbGo.GetComponent<Image>().color = new Color(0.14f, 0.16f, 0.22f, 0.8f);

            var sbHandle = CreateImageRect("Handle", sbGo.transform, new Color(0.45f, 0.50f, 0.62f, 0.9f));
            Stretch(sbHandle, 1f, 1f);
            var sb = sbGo.GetComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.handleRect = sbHandle;
            sb.targetGraphic = sbHandle.GetComponent<Image>();
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            // 5. ショートカット案内フッター
            var hint = CreateText("Shortcuts", panel,
                "Space: 再生  /  ↑↓: 切替  /  Esc: 全停止\n右ドラッグ: カメラ回転  /  ホイール: 拡縮  /  R: 視点戻す",
                13f, ColTextSub, TextAlignmentOptions.MidlineLeft);
            hint.lineSpacing = 4f;
            var hintLe = hint.gameObject.AddComponent<LayoutElement>();
            hintLe.preferredHeight = 40f;
        }

        // ---- 右ドック：詳細・操作・パラメータ・検証（幅 390px） ----
        void BuildRightDock(Transform parent)
        {
            var panel = CreateImageRect("RightDock", parent, ColPanelBg);
            panel.anchorMin = new Vector2(1f, 0f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(390f, 0f);

            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 14, 12);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // 1. 選択中エフェクト詳細カード
            var card = CreateImageRect("DetailCard", panel, ColCardBg);
            var cardVlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            cardVlg.padding = new RectOffset(12, 12, 10, 10);
            cardVlg.spacing = 5f;
            cardVlg.childControlWidth = true;
            cardVlg.childControlHeight = true;
            cardVlg.childForceExpandWidth = true;
            cardVlg.childForceExpandHeight = false;
            var cardLe = card.gameObject.AddComponent<LayoutElement>();
            cardLe.preferredHeight = 112f;

            m_detailKey = CreateText("Key", card, "", 20f, ColAccentGold, TextAlignmentOptions.MidlineLeft);
            m_detailKey.fontStyle = FontStyles.Bold;
            m_detailKey.textWrappingMode = TextWrappingModes.NoWrap;
            m_detailKey.enableAutoSizing = true;
            m_detailKey.fontSizeMin = 15f;
            m_detailKey.fontSizeMax = 20f;
            m_detailKey.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            m_detailTags = CreateText("Tags", card, "", 14f, ColTextSub, TextAlignmentOptions.MidlineLeft);
            m_detailTags.textWrappingMode = TextWrappingModes.NoWrap;
            m_detailTags.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            m_detailDesc = CreateText("Desc", card, "", 15f, ColTextMain, TextAlignmentOptions.TopLeft);
            m_detailDesc.lineSpacing = 4f;
            var descLe = m_detailDesc.gameObject.AddComponent<LayoutElement>();
            descLe.flexibleHeight = 1f;
            descLe.minHeight = 36f;

            // 2. 再生コントロール
            AddSectionHeader(panel, "再生コントロール");
            var playRow1 = CreateHRow("PlayRow1", panel, 42f, 8f);
            var bOnce = CreateButton(playRow1, "1回再生", 42f, new Color(0.18f, 0.56f, 0.34f, 1f), () => PlaySelected(false), out _, out var lOnce);
            lOnce.fontSize = 17f; lOnce.fontStyle = FontStyles.Bold;
            bOnce.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var bLoop = CreateButton(playRow1, "ループ再生", 42f, new Color(0.20f, 0.44f, 0.78f, 1f), () => PlaySelected(true), out _, out var lLoop);
            lLoop.fontSize = 17f; lLoop.fontStyle = FontStyles.Bold;
            bLoop.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var playRow2 = CreateHRow("PlayRow2", panel, 36f, 8f);
            var bStop = CreateButton(playRow2, "選択を停止", 36f, new Color(0.68f, 0.36f, 0.18f, 1f), StopSelected, out _, out var lStop);
            lStop.fontSize = 15f;
            bStop.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var bAll = CreateButton(playRow2, "すべて停止", 36f, new Color(0.56f, 0.18f, 0.22f, 1f), StopAllEffects, out _, out var lAll);
            lAll.fontSize = 15f;
            bAll.GetComponent<LayoutElement>().flexibleWidth = 1f;

            AddDivider(panel);

            // 3. パラメータ調整（サイズ・強さ）
            var paramHeader = CreateHRow("ParamHeader", panel, 24f, 6f);
            var pTitle = CreateText("Label", paramHeader, "サイズ・強さ", 15f, ColTextSub, TextAlignmentOptions.MidlineLeft);
            pTitle.fontStyle = FontStyles.Bold;
            pTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var resetBtn = CreateButton(paramHeader, "初期値へ", 24f, new Color(0.20f, 0.22f, 0.28f, 1f), ResetScaleAndIntensity, out _, out var rLbl);
            rLbl.fontSize = 12f;
            resetBtn.GetComponent<LayoutElement>().preferredWidth = 76f;

            m_sliderScale = CreateLabeledSlider(panel, "サイズ", 0.3f, 2.5f, 1f, ColAccentGold, out m_scaleValText, v =>
            {
                if (m_suppressSliderEvents) return;
                m_scale = v;
                ApplyLiveParameters();
            });
            m_sliderIntensity = CreateLabeledSlider(panel, "強さ", 0.1f, 2.0f, 1f, new Color(0.45f, 0.78f, 0.96f, 1f), out m_intensityValText, v =>
            {
                if (m_suppressSliderEvents) return;
                m_intensity = v;
                ApplyLiveParameters();
            });

            AddDivider(panel);

            // 4. 色合い (Tint) + プリセット
            var tintHeader = CreateHRow("TintHeader", panel, 26f, 8f);
            var tTitle = CreateText("Label", tintHeader, "色合い (Tint)", 15f, ColTextSub, TextAlignmentOptions.MidlineLeft);
            tTitle.fontStyle = FontStyles.Bold;
            tTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var swatchFrame = CreateImageRect("SwatchFrame", tintHeader, new Color(0.85f, 0.85f, 0.90f, 0.9f));
            var sfLe = swatchFrame.gameObject.AddComponent<LayoutElement>();
            sfLe.preferredWidth = 54f;
            sfLe.preferredHeight = 22f;
            var swatchInner = CreateImageRect("Swatch", swatchFrame, Color.white);
            Stretch(swatchInner, 2f, 2f);
            m_tintSwatch = swatchInner.GetComponent<Image>();

            var whiteBtn = CreateButton(tintHeader, "白に戻す", 24f, new Color(0.20f, 0.22f, 0.28f, 1f), () => SetTintPreset(Color.white), out _, out var wLbl);
            wLbl.fontSize = 12f;
            whiteBtn.GetComponent<LayoutElement>().preferredWidth = 76f;

            m_sliderR = CreateLabeledSlider(panel, "R (赤)", 0f, 1f, 1f, new Color(0.92f, 0.30f, 0.28f, 1f), out m_rValText, v =>
            {
                if (m_suppressSliderEvents) return;
                m_tint.r = v;
                ApplyLiveParameters();
            });
            m_sliderG = CreateLabeledSlider(panel, "G (緑)", 0f, 1f, 1f, new Color(0.28f, 0.80f, 0.36f, 1f), out m_gValText, v =>
            {
                if (m_suppressSliderEvents) return;
                m_tint.g = v;
                ApplyLiveParameters();
            });
            m_sliderB = CreateLabeledSlider(panel, "B (青)", 0f, 1f, 1f, new Color(0.26f, 0.52f, 0.95f, 1f), out m_bValText, v =>
            {
                if (m_suppressSliderEvents) return;
                m_tint.b = v;
                ApplyLiveParameters();
            });

            // 配色プリセット（4列 × 2段）
            for (int rowIdx = 0; rowIdx < 2; rowIdx++)
            {
                var pRow = CreateHRow("PresetRow" + rowIdx, panel, 30f, 6f);
                for (int colIdx = 0; colIdx < 4; colIdx++)
                {
                    int idx = rowIdx * 4 + colIdx;
                    if (idx >= TintPresets.Length) break;
                    var preset = TintPresets[idx];
                    Color bgCol = Color.Lerp(preset.Color, new Color(0.14f, 0.16f, 0.20f, 1f), 0.45f);
                    var btn = CreateButton(pRow, preset.Name, 30f, bgCol, () => SetTintPreset(preset.Color), out _, out var lbl);
                    lbl.fontSize = 13f;
                    lbl.textWrappingMode = TextWrappingModes.NoWrap;
                    lbl.enableAutoSizing = true;
                    lbl.fontSizeMin = 10f;
                    lbl.fontSizeMax = 13f;
                    btn.GetComponent<LayoutElement>().flexibleWidth = 1f;
                }
            }

            AddDivider(panel);

            // 5. 確認・検証ツール
            AddSectionHeader(panel, "環境・重ね順の確認");

            // 背景の明るさ 3択
            var bgRow = CreateHRow("BgRow", panel, 32f, 6f);
            var bgLabel = CreateText("BgLabel", bgRow, "背景:", 14f, ColTextSub, TextAlignmentOptions.MidlineLeft);
            bgLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 46f;
            m_bgModeBgs.Clear();
            m_bgModeLabels.Clear();
            for (int i = 0; i < BackgroundModes.Length; i++)
            {
                int idx = i;
                var btn = CreateButton(bgRow, BackgroundModes[i].Name, 32f, new Color(0.17f, 0.19f, 0.25f, 1f),
                    () => ApplyBackgroundMode(idx), out var bg, out var lbl);
                lbl.fontSize = 14f;
                lbl.fontStyle = FontStyles.Bold;
                btn.GetComponent<LayoutElement>().flexibleWidth = 1f;
                m_bgModeBgs.Add(bg);
                m_bgModeLabels.Add(lbl);
            }

            // ポーズ・擬似会話枠・基準だるま
            var pauseBtn = CreateButton(panel, "timeScale: 1 (通常)", 34f, new Color(0.20f, 0.22f, 0.30f, 1f), TogglePause, out m_pauseBtnBg, out m_pauseBtnLabel);
            m_pauseBtnLabel.fontSize = 14f;
            _ = pauseBtn;

            var visRow = CreateHRow("VisRow", panel, 34f, 6f);
            var dBtn = CreateButton(visRow, "擬似会話枠(order60): 表示", 34f, new Color(0.20f, 0.42f, 0.44f, 1f), ToggleMockDialogue, out m_dialogueBtnBg, out m_dialogueBtnLabel);
            m_dialogueBtnLabel.fontSize = 13f;
            dBtn.GetComponent<LayoutElement>().flexibleWidth = 1.35f;

            var mBtn = CreateButton(visRow, "基準だるま: 表示", 34f, new Color(0.32f, 0.26f, 0.34f, 1f), ToggleDummy, out m_dummyBtnBg, out m_dummyBtnLabel);
            m_dummyBtnLabel.fontSize = 13f;
            mBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            // 余白スペーサー
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(panel, false);
            spacer.GetComponent<LayoutElement>().flexibleHeight = 1f;

            // 6. ステータスバー
            var statusCard = CreateImageRect("StatusCard", panel, ColCardBg);
            statusCard.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            m_statusText = CreateText("Status", statusCard, "", 14f, ColTextMain, TextAlignmentOptions.Center);
            Stretch(m_statusText.rectTransform, 8f, 4f);

            ApplyLiveParameters();
        }

        //========================================
        // UI パーツ生成ヘルパー
        //========================================

        int CountPriority(StoryVfxPriority p)
        {
            int c = 0;
            for (int i = 0; i < m_allEntries.Count; i++)
                if (m_allEntries[i].priority == p) c++;
            return c;
        }

        EntryRowView CreateEntryRow(Transform parent, StoryVfxEntry entry, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Row_" + entry.key, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 46f;
            le.minHeight = 46f;

            var bg = go.GetComponent<Image>();
            bg.color = ColRowNormal;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(onClick);

            // 左端の選択アクセントバー
            var accent = CreateImageRect("Accent", go.transform, ColAccentGold);
            accent.anchorMin = new Vector2(0f, 0f);
            accent.anchorMax = new Vector2(0f, 1f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.offsetMin = Vector2.zero;
            accent.offsetMax = new Vector2(4f, 0f);
            var accentImg = accent.GetComponent<Image>();
            accentImg.enabled = false;

            // 優先度バッジ
            Color pCol = entry.priority == StoryVfxPriority.P1 ? ColP1
                : entry.priority == StoryVfxPriority.P2 ? ColP2 : ColP3;
            var badge = CreateImageRect("Badge", go.transform, pCol);
            badge.anchorMin = badge.anchorMax = new Vector2(0f, 0.5f);
            badge.pivot = new Vector2(0f, 0.5f);
            badge.anchoredPosition = new Vector2(10f, 0f);
            badge.sizeDelta = new Vector2(34f, 22f);
            var badgeLbl = CreateText("P", badge, entry.priority.ToString(), 12f, Color.white, TextAlignmentOptions.Center);
            badgeLbl.fontStyle = FontStyles.Bold;
            Stretch(badgeLbl.rectTransform, 0f, 0f);

            // 中央テキスト（上：キー名、下：短い日本語名）
            var keyText = CreateText("Key", go.transform, entry.key, 15f, ColTextMain, TextAlignmentOptions.MidlineLeft);
            keyText.fontStyle = FontStyles.Bold;
            keyText.textWrappingMode = TextWrappingModes.NoWrap;
            keyText.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            keyText.rectTransform.anchorMax = new Vector2(1f, 1f);
            keyText.rectTransform.offsetMin = new Vector2(52f, 0f);
            keyText.rectTransform.offsetMax = new Vector2(-54f, -2f);

            var subText = CreateText("Sub", go.transform, GetShortName(entry.key), 12f, ColTextSub, TextAlignmentOptions.MidlineLeft);
            subText.textWrappingMode = TextWrappingModes.NoWrap;
            subText.rectTransform.anchorMin = new Vector2(0f, 0f);
            subText.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            subText.rectTransform.offsetMin = new Vector2(52f, 2f);
            subText.rectTransform.offsetMax = new Vector2(-54f, 0f);

            // 右端カテゴリ小ラベル
            string catShort = entry.category == StoryVfxCategory.ScreenOverlay ? "画面"
                : entry.category == StoryVfxCategory.ScreenFront ? "前面" : "3D";
            var catPill = CreateImageRect("Cat", go.transform, new Color(1f, 1f, 1f, 0.08f));
            catPill.anchorMin = catPill.anchorMax = new Vector2(1f, 0.5f);
            catPill.pivot = new Vector2(1f, 0.5f);
            catPill.anchoredPosition = new Vector2(-8f, 0f);
            catPill.sizeDelta = new Vector2(40f, 20f);
            var catLbl = CreateText("CatTxt", catPill, catShort, 11f, ColTextSub, TextAlignmentOptions.Center);
            Stretch(catLbl.rectTransform, 0f, 0f);

            return new EntryRowView
            {
                Root = go,
                Bg = bg,
                AccentBar = accentImg,
                KeyText = keyText,
            };
        }

        Slider CreateLabeledSlider(Transform parent, string label, float min, float max, float initial, Color fillColor,
                                   out TextMeshProUGUI valueText, UnityEngine.Events.UnityAction<float> onChanged)
        {
            var row = CreateHRow("SliderRow_" + label, parent, 26f, 8f);

            var lbl = CreateText("Label", row, label, 14f, ColTextMain, TextAlignmentOptions.MidlineLeft);
            lbl.textWrappingMode = TextWrappingModes.NoWrap;
            var lblLe = lbl.gameObject.AddComponent<LayoutElement>();
            lblLe.preferredWidth = 60f;
            lblLe.flexibleWidth = 0f;

            var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
            sliderGo.transform.SetParent(row, false);
            var sLe = sliderGo.GetComponent<LayoutElement>();
            sLe.flexibleWidth = 1f;
            sLe.preferredHeight = 24f;

            var track = CreateImageRect("Track", sliderGo.transform, new Color(0.20f, 0.22f, 0.29f, 1f));
            track.anchorMin = new Vector2(0f, 0.5f);
            track.anchorMax = new Vector2(1f, 0.5f);
            track.sizeDelta = new Vector2(0f, 8f);

            var fillArea = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(sliderGo.transform, false);
            fillArea.anchorMin = Vector2.zero;
            fillArea.anchorMax = Vector2.one;
            fillArea.offsetMin = new Vector2(6f, 8f);
            fillArea.offsetMax = new Vector2(-6f, -8f);

            var fill = CreateImageRect("Fill", fillArea, fillColor);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;

            var handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(sliderGo.transform, false);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(7f, 4f);
            handleArea.offsetMax = new Vector2(-7f, -4f);

            // Unity Slider は handleRect を縦ストレッチするため sizeDelta.y = 0 で handleArea の高さ(16px)にぴったり収める
            var handle = CreateImageRect("Handle", handleArea, Color.white);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            handle.sizeDelta = new Vector2(12f, 0f);

            var slider = sliderGo.GetComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = initial;
            slider.onValueChanged.AddListener(onChanged);

            valueText = CreateText("Val", row, $"{initial:F2}", 14f, ColTextSub, TextAlignmentOptions.MidlineRight);
            valueText.textWrappingMode = TextWrappingModes.NoWrap;
            var vLe = valueText.gameObject.AddComponent<LayoutElement>();
            vLe.preferredWidth = 48f;
            vLe.flexibleWidth = 0f;

            return slider;
        }

        void AddSectionHeader(Transform parent, string title)
        {
            var txt = CreateText("Sec_" + title, parent, title, 14f, ColTextSub, TextAlignmentOptions.MidlineLeft);
            txt.fontStyle = FontStyles.Bold;
            txt.textWrappingMode = TextWrappingModes.NoWrap;
            txt.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
        }

        void AddDivider(Transform parent)
        {
            var div = CreateImageRect("Divider", parent, ColDivider);
            div.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
        }

        RectTransform CreateHRow(string name, Transform parent, float height, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return (RectTransform)go.transform;
        }

        RectTransform CreateImageRect(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (m_font != null) tmp.font = m_font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        Button CreateButton(Transform parent, string label, float height, Color bgColor, UnityEngine.Events.UnityAction onClick,
                            out Image bgImage, out TextMeshProUGUI labelTmp)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            bgImage = go.GetComponent<Image>();
            bgImage.color = bgColor;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = bgImage;
            btn.onClick.AddListener(onClick);

            labelTmp = CreateText("Label", go.transform, label, 15f, Color.white, TextAlignmentOptions.Center);
            labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
            labelTmp.enableAutoSizing = true;
            labelTmp.fontSizeMin = 11f;
            labelTmp.fontSizeMax = 17f;
            Stretch(labelTmp.rectTransform, 6f, 2f);
            return btn;
        }

        static void Stretch(RectTransform rect, float padX, float padY)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padX, padY);
            rect.offsetMax = new Vector2(-padX, -padY);
        }
    }
}
