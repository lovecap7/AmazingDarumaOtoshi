using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 試合の仮の表示（ゲーム画面の UI ができるまでの、動作確認用）。MatchDirector と MatchPlayer を読むだけで、試合には影響しない。
//  ・頭の上に「1P」「CPU1」の名札（幽霊は薄く、復活待ち・脱落は出さない）
//  ・頭の色をキャラの色にする（キャラごとのモデルができるまでの目印）
//  ・時間制限があれば残り時間、試合が終わったら結果
// 本番の HUD を作ったら、MatchBridge でこれを付けるのをやめる（または HUD に置き換える）。
public class MatchDebugView : MonoBehaviour
{
    class Tag
    {
        public MatchPlayer player;
        public RectTransform rect;
        public CanvasGroup group;
        public Object tinted;   // 頭の色を付けたキャラクター（復活で作り直されたら付け直す）
    }

    MatchDirector m_director;
    readonly List<Tag> m_tags = new List<Tag>();
    RectTransform m_canvas;
    CanvasGroup m_finishGroup;
    TextMeshProUGUI m_finishText, m_timerText;
    TMP_FontAsset m_font;
    Material m_letterMat, m_popMat;

    void Start()
    {
        m_director = GetComponent<MatchDirector>();
        m_font = PresentationSettings.DefaultFont;
        m_letterMat = ToyKit.LetterMaterial(m_font);
        m_popMat = ToyKit.PopMaterial(m_letterMat);
        BuildUI();

        // MatchDirector の Start（プレイヤーを出す）がまだなら、出てから名札を作る
        if (m_director.Players.Count > 0) MakeTags();
        else m_director.Started += MakeTags;
        m_director.Finished += ShowResult;
    }

    void MakeTags()
    {
        foreach (var p in m_director.Players) m_tags.Add(MakeTag(p));
    }

    void Update()
    {
        foreach (var t in m_tags)
        {
            TintHead(t);
            UpdateTag(t);
        }

        if (m_director.HasTimeLimit)
        {
            int sec = Mathf.CeilToInt(m_director.TimeLeft);
            m_timerText.text = $"{sec / 60}:{sec % 60:00}";
            m_timerText.color = sec <= 10 ? ToyKit.Palette[0] : Color.white;
            m_timerText.rectTransform.localScale = Vector3.one * (sec <= 10 && !m_director.IsFinished ? 1f + 0.12f * Mathf.Pow(1f - (m_director.TimeLeft % 1f), 4f) : 1f);
        }
        if (m_director.IsFinished) m_finishGroup.alpha = Mathf.MoveTowards(m_finishGroup.alpha, 1f, Time.unscaledDeltaTime * 3f);
    }

    void ShowResult(MatchDirector.MatchResult result)
    {
        string title = result.Reason == MatchDirector.FinishReason.TimeUp ? "タイムアップ！" : "しあい しゅうりょう！";
        m_finishText.text = result.Winner != null
            ? $"{title}\n<size=70%>しょうしゃ：{result.Winner.Label}　{CharaName(result.Winner)}</size>"
            : $"{title}\n<size=70%>ひきわけ</size>";
    }

    // ---------------- 目印 ----------------
    static void TintHead(Tag t)
    {
        var c = t.player.Character;
        var data = t.player.CharacterData;
        if (c == null || c == t.tinted || data == null) return;
        t.tinted = c;
        foreach (var tr in c.GetComponentsInChildren<Transform>())
        {
            if (tr.name != "Head") continue;
            var r = tr.GetComponent<Renderer>();
            if (r == null) continue;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", data.color);
            r.SetPropertyBlock(mpb);
        }
    }

    void UpdateTag(Tag t)
    {
        Transform body = t.player.Body;
        var cam = Camera.main;
        if (body == null || cam == null) { t.rect.gameObject.SetActive(false); return; }

        float height = t.player.Character != null ? t.player.Character.Stack.Height + 0.6f : 1.8f;
        Vector3 sp = cam.WorldToScreenPoint(body.position + Vector3.up * height);
        bool visible = sp.z > 0f;
        t.rect.gameObject.SetActive(visible);
        if (!visible) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(m_canvas, sp, null, out var local);
        t.rect.anchoredPosition = local + new Vector2(0f, 24f);
        t.group.alpha = t.player.IsAlive ? 1f : 0.55f;   // 幽霊は薄く
    }

    // ---------------- UI ----------------
    void BuildUI()
    {
        m_canvas = ToyKit.CreateOverlayCanvas("MatchDebugUI", 20);

        var hint = ToyKit.UIText("Hint", m_canvas, m_font,
            "Select：キャラクターセレクトにもどる", 24f, Color.white);
        hint.fontSharedMaterial = m_letterMat;
        ToyKit.Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1800f, 40f));

        m_timerText = ToyKit.UIText("Timer", m_canvas, m_font, "", 72f, Color.white);
        m_timerText.fontSharedMaterial = m_popMat;
        ToyKit.Anchor(m_timerText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(400f, 100f));
        m_timerText.gameObject.SetActive(m_director.HasTimeLimit);

        var finish = ToyKit.UIImage("Finish", m_canvas, new Color(0f, 0f, 0f, 0.45f));
        ToyKit.Stretch(finish);
        m_finishGroup = finish.gameObject.AddComponent<CanvasGroup>();
        m_finishGroup.alpha = 0f;
        m_finishGroup.blocksRaycasts = false;
        m_finishText = ToyKit.UIText("Text", finish, m_font, "", 110f, Color.white);
        m_finishText.fontSharedMaterial = m_popMat;
        ToyKit.Stretch(m_finishText.rectTransform);
    }

    // 「1P」の名札（CPU は灰色で「CPU」、複数いるときは「CPU1」「CPU2」）
    Tag MakeTag(MatchPlayer p)
    {
        string label = p.Label;
        var bg = ToyKit.UIImage("Tag" + label, m_canvas, p.Color);
        ToyKit.Anchor(bg, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f + label.Length * 16f, 36f));
        bg.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
        var t = ToyKit.UIText("Text", bg, m_font, label, 24f, Color.white);
        t.fontSharedMaterial = m_letterMat;
        ToyKit.Stretch(t.rectTransform);
        return new Tag { player = p, rect = bg, group = bg.gameObject.AddComponent<CanvasGroup>() };
    }

    static string CharaName(MatchPlayer p) => p.CharacterData != null ? p.CharacterData.displayName : "-";
}
