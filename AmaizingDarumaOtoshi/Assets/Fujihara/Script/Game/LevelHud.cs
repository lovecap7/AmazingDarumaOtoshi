using System.Collections.Generic;
using Nakahira;
using TMPro;
using UnityEngine;

// 試合中のレベル（Yurube.DarumaLevel）の表示。だるまの頭の上（名札の下）に「Lv2」を出し、上がったときは大きく「LEVEL UP!」と跳ねる。
// 試合が始まると自動で1つ作られる（LevelParams の showLevel がオフなら出さない）。読むだけで、試合には影響しない
public class LevelHud : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<LevelHud>() != null) return;
        var go = new GameObject("LevelHud");
        DontDestroyOnLoad(go);
        go.AddComponent<LevelHud>();
    }

    class Label
    {
        public RectTransform rect;
        public TextMeshProUGUI text;
        public int shownLevel;
        public float popTime = -10f;
    }

    readonly Dictionary<MatchPlayer, Label> m_labels = new Dictionary<MatchPlayer, Label>();
    RectTransform m_canvas;
    TMP_FontAsset m_font;
    Material m_mat;
    DarumaMatch m_match;

    static readonly Color[] LevelColors =
    {
        new Color(0.85f, 0.85f, 0.85f),   // Lv1
        new Color(0.55f, 0.9f, 1f),       // Lv2
        new Color(0.55f, 1f, 0.55f),      // Lv3
        new Color(1f, 0.85f, 0.3f),       // Lv4
        new Color(1f, 0.45f, 0.35f),      // Lv5
    };

    void OnEnable() => Yurube.DarumaLevel.LevelUp += OnLevelUp;
    void OnDisable() => Yurube.DarumaLevel.LevelUp -= OnLevelUp;

    void OnLevelUp(MatchPlayer p, int level)
    {
        if (m_labels.TryGetValue(p, out var l)) l.popTime = Time.unscaledTime;
    }

    void LateUpdate()
    {
        var match = DarumaMatch.Current;
        if (match != m_match)
        {
            m_match = match;
            foreach (var l in m_labels.Values) if (l.rect != null) Destroy(l.rect.gameObject);
            m_labels.Clear();
        }
        bool show = match != null && Yurube.LevelParams.Instance.showLevel && !Yurube.DarumaLevel.LegacyPassives;
        if (m_canvas != null) m_canvas.gameObject.SetActive(show);
        if (!show) return;
        if (m_canvas == null) Build();

        var cam = Camera.main;
        foreach (var p in match.Players)
        {
            if (p == null) continue;
            if (!m_labels.TryGetValue(p, out var l)) m_labels[p] = l = MakeLabel(p);
            UpdateLabel(p, l, cam);
        }
    }

    void Build()
    {
        m_font = PresentationSettings.DefaultFont;
        m_mat = ToyKit.PopMaterial(ToyKit.LetterMaterial(m_font));
        m_canvas = ToyKit.CreateOverlayCanvas("LevelHudUI", 19);
        m_canvas.SetParent(transform, false);
    }

    Label MakeLabel(MatchPlayer p)
    {
        var t = ToyKit.UIText("Lv" + p.Label, m_canvas, m_font, "", 30f, Color.white);
        t.fontSharedMaterial = m_mat;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        ToyKit.Anchor(t.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 60f));
        return new Label { rect = t.rectTransform, text = t };
    }

    void UpdateLabel(MatchPlayer p, Label l, Camera cam)
    {
        Transform body = p.Body;
        if (body == null || cam == null || !p.IsAlive) { l.rect.gameObject.SetActive(false); return; }
        float height = p.Character != null ? p.Character.Stack.Height + 0.6f : 1.8f;
        Vector3 sp = cam.WorldToScreenPoint(body.position + Vector3.up * height);
        if (sp.z <= 0f) { l.rect.gameObject.SetActive(false); return; }
        l.rect.gameObject.SetActive(true);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(m_canvas, sp, null, out var local);

        int level = Yurube.DarumaLevel.LevelOf(p);
        float age = Time.unscaledTime - l.popTime;
        bool popping = age < 1.4f;
        if (level != l.shownLevel || popping)
        {
            l.shownLevel = level;
            string lv = level >= Yurube.LevelParams.MaxLevel ? "Lv MAX" : "Lv" + level;
            l.text.text = popping ? "LEVEL UP!\n<size=80%>" + lv + "</size>" : lv;
        }
        l.text.color = LevelColors[Mathf.Clamp(level - 1, 0, LevelColors.Length - 1)];
        // 名札（MatchDebugView）の下あたり。上がったときは上へ跳ねて大きくなり、少しずつ戻る
        float pop = popping ? Mathf.Sin(Mathf.Clamp01(age / 0.25f) * Mathf.PI * 0.5f) * Mathf.Clamp01((1.4f - age) / 0.5f) : 0f;
        l.rect.anchoredPosition = local + new Vector2(0f, -14f + pop * 40f);
        l.rect.localScale = Vector3.one * (0.8f + pop * 0.7f);
    }
}
