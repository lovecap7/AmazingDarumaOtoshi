using System.Collections.Generic;
using UnityEngine;
using Yurube;

namespace Nakahira
{
    // 試合中の必殺技ゲージの表示(Canvas)。画面下の各プレイヤーのパネル(中川さんの MatchHud)のすぐ上に細いバーを出す
    // 人も CPU も出す。必殺技を持たないキャラの人には出さない。体がない(幽霊・復活待ち)間は、次の体が受け取る値を薄く出す
    // プレハブ SpecialGaugeHud(Canvas)を SpecialGaugeParams.HudPrefab に入れておくと、試合で最初にゲージを持つキャラが出たときに 1 つ作る
    [RequireComponent(typeof(Canvas))]
    public class SpecialGaugeHud : MonoBehaviour
    {
        [Header("部品")]
        [Tooltip("1 人分のバー")]
        [SerializeField] SpecialGaugeBar m_barPrefab;
        [Tooltip("バーを並べる親(空ならこの Canvas の直下)")]
        [SerializeField] RectTransform m_root;

        [Header("配置(1920x1080 基準)")]
        [Tooltip("パネルの上の端からバーまでのすき間")]
        [SerializeField] float m_gapAbovePanel = 6.0f;
        [Tooltip("バーの幅をパネルの幅に合わせる(オフならプレハブの幅のまま)")]
        [SerializeField] bool m_matchPanelWidth = true;
        [Tooltip("MatchHud がないときに使うパネルの大きさ・間隔・画面下からパネルの中心までの高さ(MatchHud の初期値と同じ)")]
        [SerializeField] Vector2 m_fallbackPanelSize = new Vector2(300.0f, 128.0f);
        [SerializeField] float m_fallbackSpacing = 24.0f;
        [SerializeField] float m_fallbackBottom = 120.0f;

        public static SpecialGaugeHud Instance { get; private set; }

        class Entry
        {
            public MatchPlayer Player;
            public SpecialGaugeBar Bar;
            public SpecialGauge Gauge;          // いまの体のゲージ(体がなければ null)
            public SpecialGaugeParams Params;   // キャラのプレハブのゲージの調整値(体がない間の割合の計算用)
            public bool HasGauge;               // このキャラは必殺技ゲージを持つ
            public float LastRatio;
            public bool LastFull;
        }

        readonly List<Entry> m_entries = new List<Entry>();
        Nakagawa.MatchHud m_matchHud;
        DarumaMatch m_match;

        // 試合に表示が 1 つもなければ作る
        public static void Ensure(SpecialGaugeHud prefab)
        {
            if (Instance != null || prefab == null) return;
            Instance = Instantiate(prefab);
            Instance.name = prefab.name;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (m_root == null) m_root = (RectTransform)transform;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            var match = DarumaMatch.Current;
            if (match == null)
            {
                // 試合のシーンを出たら消す(試合の外では出さない)
                Destroy(gameObject);
                return;
            }
            if (match != m_match)
            {
                m_match = match;
                m_matchHud = match.GetComponent<Nakagawa.MatchHud>();
            }

            Sync(match);
            bool hidden = SpecialGaugeHUD.Suppressed;
            int index = 0;
            int count = m_entries.Count;
            foreach (var e in m_entries)
            {
                UpdateEntry(match, e, index, count, hidden);
                index++;
            }
        }

        // 試合の人とバーを合わせる(途中参加・抜けにも合わせる)
        void Sync(DarumaMatch match)
        {
            var players = match.Players;
            bool changed = false;
            for (int i = m_entries.Count - 1; i >= 0; i--)
            {
                if (IndexOf(players, m_entries[i].Player) >= 0) continue;
                if (m_entries[i].Bar != null) Destroy(m_entries[i].Bar.gameObject);
                m_entries.RemoveAt(i);
                changed = true;
            }
            foreach (var p in players)
            {
                if (p == null || HasEntry(p)) continue;
                var bar = Instantiate(m_barPrefab, m_root);
                bar.name = $"{m_barPrefab.name}_{p.Label}";
                m_entries.Add(new Entry { Player = p, Bar = bar });
                changed = true;
            }
            // MatchHud と同じ並び(試合の人の順)
            if (changed) m_entries.Sort((a, b) => IndexOf(players, a.Player).CompareTo(IndexOf(players, b.Player)));
        }

        bool HasEntry(MatchPlayer p)
        {
            foreach (var e in m_entries) if (e.Player == p) return true;
            return false;
        }

        static int IndexOf(IReadOnlyList<MatchPlayer> players, MatchPlayer p)
        {
            for (int i = 0; i < players.Count; i++) if (players[i] == p) return i;
            return -1;
        }

        void UpdateEntry(DarumaMatch match, Entry e, int index, int count, bool hidden)
        {
            var p = e.Player;
            var character = p.Character;
            if (character != null && (e.Gauge == null || e.Gauge.Character != character))
            {
                e.Gauge = character.GetComponent<SpecialGauge>();
            }
            if (character == null) e.Gauge = null;

            // キャラが必殺技ゲージを持つか(プレハブで決める。体が出ていればその体で)
            if (e.Gauge != null)
            {
                e.HasGauge = true;
                e.Params = e.Gauge.Params;
            }
            else if (character != null)
            {
                e.HasGauge = false;
            }
            else if (e.Params == null && p.CharacterData != null && p.CharacterData.prefab != null)
            {
                var prefabGauge = p.CharacterData.prefab.GetComponent<SpecialGauge>();
                if (prefabGauge != null)
                {
                    e.HasGauge = true;
                    e.Params = prefabGauge.Params;
                }
            }

            bool visible = !hidden && e.HasGauge;
            if (e.Bar.gameObject.activeSelf != visible) e.Bar.gameObject.SetActive(visible);
            if (!visible) return;

            float ratio;
            bool full;
            string status = null;
            bool present = e.Gauge != null;
            if (present)
            {
                ratio = e.Gauge.Normalized;
                full = e.Gauge.IsFull;
                status = e.Gauge.StatusOverride;
            }
            else if (e.Params != null && match.PeekSpecialGaugeCarry(p, out float carry))
            {
                // 体がない間: 次の体が受け取る値(溜まり具合 + やられたおまけ)
                float max = Mathf.Max(1.0f, e.Params.MaxPoints);
                full = carry >= max;
                ratio = Mathf.Clamp01(carry / max);
            }
            else
            {
                ratio = e.LastRatio;
                full = e.LastFull;
            }
            e.LastRatio = ratio;
            e.LastFull = full;
            int percent = full ? 100 : Mathf.Min(99, Mathf.FloorToInt(ratio * 100.0f));

            // パネルの真上に置き、パネルの半透明に合わせる
            Vector2 panelPos, panelSize;
            float alpha = 1.0f;
            if (m_matchHud == null || !m_matchHud.TryGetPanel(p, out panelPos, out panelSize, out alpha))
            {
                panelSize = m_fallbackPanelSize;
                float total = count * panelSize.x + (count - 1) * m_fallbackSpacing;
                panelPos = new Vector2(-total * 0.5f + panelSize.x * 0.5f + index * (panelSize.x + m_fallbackSpacing), m_fallbackBottom);
                alpha = 1.0f;
            }
            var rect = e.Bar.Rect;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.0f);
            rect.pivot = new Vector2(0.5f, 0.0f);
            if (m_matchPanelWidth) rect.sizeDelta = new Vector2(panelSize.x, rect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(panelPos.x, panelPos.y + panelSize.y * 0.5f + m_gapAbovePanel);

            e.Bar.Show(p.Color, ratio, full, percent, status, present, alpha);
        }
    }
}
