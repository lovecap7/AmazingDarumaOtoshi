using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yurube.Story.Vfx
{
    public enum StoryVfxCategory
    {
        World = 0,
        ScreenFront = 1,
        ScreenOverlay = 2,
    }

    public enum StoryVfxPriority
    {
        P1 = 1,
        P2 = 2,
        P3 = 3,
    }

    [Serializable]
    public class StoryVfxEntry
    {
        [Tooltip("演出キー名（英数字とアンダースコア。例: fx_block_collapse_s）")]
        public string key;
        [Tooltip("プレビュー画面に表示する日本語説明")]
        public string label;
        [Tooltip("実装優先度")]
        public StoryVfxPriority priority = StoryVfxPriority.P1;
        [Tooltip("描画種別（World=3D空間 / ScreenFront=UI前面 / ScreenOverlay=画面効果）")]
        public StoryVfxCategory category = StoryVfxCategory.World;
        [Tooltip("再生するプレハブ（ScreenOverlay の場合は null）")]
        public GameObject prefab;
        [Tooltip("既定のループ有無")]
        public bool looping;
        [Tooltip("既定スケール")]
        [Min(0.05f)] public float defaultScale = 1f;
        [Tooltip("既定ティント色")]
        public Color defaultTint = Color.white;
        [Tooltip("Time.timeScale == 0 でも動作させるか")]
        public bool useUnscaledTime = true;
        [Tooltip("プレビュー時に自動で対象を動かすか（fx_sword_trail などの軌跡確認用）")]
        public bool previewOrbitMotion;
    }

    // ストーリー演出キー → プレハブ・既定パラメータの対応表。
    // Assets/Yurube/Story/Vfx/StoryVfxCatalog.asset に自動生成され、StorySettings から参照される。
    [CreateAssetMenu(menuName = "Daruma/Story VFX Catalog", fileName = "StoryVfxCatalog")]
    public class StoryVfxCatalog : ScriptableObject
    {
        [SerializeField] List<StoryVfxEntry> m_entries = new List<StoryVfxEntry>();

        Dictionary<string, StoryVfxEntry> m_lookup;

        public IReadOnlyList<StoryVfxEntry> Entries => m_entries;

        public StoryVfxEntry Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            EnsureLookup();
            m_lookup.TryGetValue(key, out var entry);
            return entry;
        }

        public void SetEntries(List<StoryVfxEntry> entries)
        {
            m_entries = entries ?? new List<StoryVfxEntry>();
            m_lookup = null;
        }

        public void UpsertEntry(StoryVfxEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.key)) return;
            for (int i = 0; i < m_entries.Count; i++)
            {
                if (m_entries[i] != null && m_entries[i].key == entry.key)
                {
                    m_entries[i] = entry;
                    m_lookup = null;
                    return;
                }
            }
            m_entries.Add(entry);
            m_lookup = null;
        }

        void OnEnable() => m_lookup = null;
        void OnValidate() => m_lookup = null;

        void EnsureLookup()
        {
            if (m_lookup != null) return;
            m_lookup = new Dictionary<string, StoryVfxEntry>(StringComparer.Ordinal);
            if (m_entries == null) return;
            for (int i = 0; i < m_entries.Count; i++)
            {
                var e = m_entries[i];
                if (e != null && !string.IsNullOrEmpty(e.key))
                    m_lookup[e.key] = e;
            }
        }
    }
}
