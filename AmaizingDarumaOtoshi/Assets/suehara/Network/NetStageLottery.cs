using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ネット対戦のステージの抽選の演出(ホストが試合を始めるときに、全員の画面で同じものを出す。NetMatch.LotteryRpc から)
// 見た目と流れはキャラクターセレクトの抽選(藤原さんの MatchSelect.Lottery)と同じ:
//  札が並ぶ(上に選んだ人) → 枠が札を巡り、だんだん遅くなって止まる → おまかせの札ならめくる → 「〇〇に 決定！」
// どの札が当たるかはホストが決めて送る。流れの長さは札の数と当たりだけで決まるので、どの画面でもほぼ同時に終わる
public class NetStageLottery : MonoBehaviour
{
    const int kSortingOrder = 60;   // ネット対戦のメニュー(50)より上
    static readonly Color kHiddenColor = new Color(0.55f, 0.45f, 0.75f);

    public struct Card
    {
        public string Label;     // 選んだ人(1P など)
        public Color Color;      // 選んだ人の色
        public StageData Stage;  // 札のステージ(おまかせなら、当たるまで伏せておく中身)
        public bool Hidden;      // おまかせ
    }

    class CardView
    {
        public RectTransform rect;
        public Image bg;
        public Image image;
        public TextMeshProUGUI letter, name;
    }

    // 抽選の演出を出しているか(出している間はメニューを開かない・キャラクターを動かさない)
    public static bool IsPlaying { get; private set; }

    TMP_FontAsset m_font;
    Material m_letterMat, m_popMat;
    RectTransform m_canvas;
    CanvasGroup m_group;
    // 演出の間止めている開発用の表示(OnGUI はどの UI よりも上に出るため。メニューと同じもの)
    List<MonoBehaviour> m_hidden;

    // 抽選を出す。onFinished: 終わったら呼ぶ(ホストはここで試合を始める)
    public static void Play(List<Card> cards, int winner, Action onFinished)
    {
        var go = new GameObject("NetStageLottery");
        var lottery = go.AddComponent<NetStageLottery>();
        lottery.StartCoroutine(lottery.Run(cards, winner, onFinished));
    }

    void OnDestroy()
    {
        IsPlaying = false;
        if (m_hidden != null) NetSessionMenu.RestoreDevViews(m_hidden);
        if (m_canvas != null) Destroy(m_canvas.gameObject);
    }

    IEnumerator Run(List<Card> cards, int winner, Action onFinished)
    {
        IsPlaying = true;
        m_hidden = NetSessionMenu.HideDevViews();
        m_font = PresentationSettings.DefaultFont;
        m_letterMat = ToyKit.LetterMaterial(m_font);
        m_popMat = ToyKit.PopMaterial(m_letterMat);
        m_canvas = ToyKit.CreateOverlayCanvas("NetStageLottery", kSortingOrder);
        m_group = m_canvas.gameObject.AddComponent<CanvasGroup>();
        m_group.blocksRaycasts = false;
        m_group.alpha = 0.0f;

        var dim = ToyKit.UIImage("Dim", m_canvas, new Color(0.15f, 0.1f, 0.1f, 0.55f));
        ToyKit.Stretch(dim);
        var title = Text("Title", m_canvas, "ステージ ちゅうせん！", 90.0f, Color.white, m_popMat);
        ToyKit.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, 330.0f), new Vector2(1400.0f, 140.0f));

        int n = cards.Count;
        winner = Mathf.Clamp(winner, 0, n - 1);
        const float gap = 30.0f;
        var size = new Vector2(Mathf.Min(380.0f, (1760.0f - gap * (n - 1)) / n), 0.0f);
        size.y = size.x * 0.68f;

        // 当たりを示す枠(札の後ろに置いて、縁だけが見えるようにする)
        var frame = ToyKit.UIRounded("Frame", m_canvas, Color.white, 26.0f);
        frame.sizeDelta = size + new Vector2(40.0f, 120.0f);
        var views = new List<CardView>();
        for (int i = 0; i < n; i++)
        {
            var pos = new Vector2((i - (n - 1) * 0.5f) * (size.x + gap), 0.0f);
            var v = MakeCard(pos, size, cards[i]);
            views.Add(v);

            // 札の上に選んだ人
            var chip = ToyKit.UIRounded("Voter", v.rect, cards[i].Color, 16.0f);
            ToyKit.Anchor(chip, new Vector2(0.5f, 1.0f), new Vector2(0.0f, 34.0f), new Vector2(120.0f, 56.0f));
            chip.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
            var ct = Text("Text", chip, cards[i].Label, 34.0f, Color.white, m_letterMat);
            ToyKit.Stretch(ct.rectTransform);
            v.rect.localScale = Vector3.zero;
        }
        frame.anchoredPosition = views[0].rect.anchoredPosition + new Vector2(0.0f, 20.0f);
        frame.gameObject.SetActive(false);

        // 出てくる
        for (float t = 0.0f; t < 1.0f; t += Time.unscaledDeltaTime / 0.5f)
        {
            m_group.alpha = t;
            for (int i = 0; i < n; i++) views[i].rect.localScale = Vector3.one * ToyKit.EaseOutBack(Mathf.Clamp01(t * 1.6f - i * 0.15f));
            yield return null;
        }
        m_group.alpha = 1.0f;
        foreach (var v in views) v.rect.localScale = Vector3.one;
        yield return Wait(0.4f);

        // 枠が札を巡り、だんだん遅くなって止まる
        int laps = Mathf.Max(2, Mathf.CeilToInt(12.0f / n));
        int steps = laps * n + winner;
        frame.gameObject.SetActive(true);
        for (int s = 0; s <= steps; s++)
        {
            int idx = s % n;
            frame.anchoredPosition = views[idx].rect.anchoredPosition + new Vector2(0.0f, 20.0f);
            MenuSounds.Cursor();
            float progress = s / (float)Mathf.Max(1, steps);
            float interval = Mathf.Lerp(0.06f, 0.5f, progress * progress * progress);
            for (float t = 0.0f; t < interval; t += Time.unscaledDeltaTime)
            {
                views[idx].rect.localScale = Vector3.one * Mathf.Lerp(1.06f, 1.0f, t / interval);
                yield return null;
            }
        }

        // 当たったのがおまかせ札なら、ここで初めてめくる
        var won = cards[winner];
        if (won.Hidden)
        {
            yield return Wait(0.3f);
            var c = views[winner];
            bool shown = false;
            for (float t = 0.0f; t < 1.0f; t += Time.unscaledDeltaTime / 0.5f)
            {
                c.rect.localScale = new Vector3(Mathf.Abs(Mathf.Cos(t * Mathf.PI)), 1.0f, 1.0f) * 1.1f;
                frame.localScale = c.rect.localScale;
                if (t >= 0.5f && !shown)
                {
                    ShowStage(c, won.Stage);
                    shown = true;
                }
                yield return null;
            }
            ShowStage(c, won.Stage);
            c.rect.localScale = Vector3.one;
            frame.localScale = Vector3.one;
        }

        // 決定！
        MenuSounds.Decide();
        string name = won.Stage != null ? won.Stage.displayName : "？？？";
        var pop = Text("Decided", m_canvas, $"{name} に 決定！", 110.0f, ToyKit.Palette[0], m_popMat);
        ToyKit.Anchor(pop.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.0f, -300.0f), new Vector2(1600.0f, 180.0f));
        for (float t = 0.0f; t < 1.0f; t += Time.unscaledDeltaTime / 0.5f)
        {
            float e = ToyKit.EaseOutBack(t);
            for (int i = 0; i < n; i++)
            {
                if (i == winner) views[i].rect.localScale = Vector3.one * Mathf.Lerp(1.0f, 1.25f, e);
                else views[i].bg.color *= new Color(0.98f, 0.98f, 0.98f, 1.0f);
            }
            frame.localScale = Vector3.one * Mathf.Lerp(1.0f, 1.25f, e);
            pop.rectTransform.localScale = Vector3.one * e;
            yield return null;
        }
        yield return Wait(1.2f);

        // 消える
        for (float t = 0.0f; t < 1.0f; t += Time.unscaledDeltaTime / 0.3f)
        {
            m_group.alpha = 1.0f - t;
            yield return null;
        }
        IsPlaying = false;
        NetSessionMenu.RestoreDevViews(m_hidden);
        onFinished?.Invoke();
        Destroy(gameObject);
    }

    //========================================
    // 札
    //========================================

    CardView MakeCard(Vector2 pos, Vector2 size, Card card)
    {
        var v = new CardView();
        v.rect = ToyKit.UIRounded("Card", m_canvas, card.Hidden || card.Stage == null ? kHiddenColor : card.Stage.color, 22.0f);
        ToyKit.Anchor(v.rect, new Vector2(0.5f, 0.5f), pos, size);
        v.rect.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
        v.bg = v.rect.GetComponent<Image>();

        v.letter = Text("Letter", v.rect, card.Hidden ? "？" : Initial(card.Stage), size.y * 0.5f, Color.white, m_popMat);
        ToyKit.Stretch(v.letter.rectTransform);
        v.letter.rectTransform.offsetMin = new Vector2(0.0f, size.y * 0.22f);

        // 名前の帯
        var strip = ToyKit.UIImage("NameStrip", v.rect, new Color(0.0f, 0.0f, 0.0f, 0.45f));
        strip.anchorMin = new Vector2(0.0f, 0.0f);
        strip.anchorMax = new Vector2(1.0f, 0.22f);
        strip.offsetMin = strip.offsetMax = Vector2.zero;
        v.name = Text("Name", strip, card.Hidden ? "おまかせ" : card.Stage != null ? card.Stage.displayName : "", 30.0f, Color.white, m_letterMat);
        v.name.enableAutoSizing = true; v.name.fontSizeMin = 16.0f; v.name.fontSizeMax = 30.0f;
        ToyKit.Stretch(v.name.rectTransform);

        if (!card.Hidden) ShowStage(v, card.Stage);
        return v;
    }

    // 札にステージを出す(プレビューの画像があれば画像、なければ色と頭文字)
    void ShowStage(CardView v, StageData stage)
    {
        if (stage == null) return;
        v.bg.color = stage.color;
        v.name.text = stage.displayName;
        v.letter.text = stage.preview != null ? "" : Initial(stage);
        if (stage.preview == null || v.image != null) return;
        v.image = ToyKit.UIImage("Image", v.rect, Color.white).GetComponent<Image>();
        v.image.sprite = stage.preview;
        v.image.preserveAspect = true;
        ToyKit.Stretch(v.image.rectTransform);
        v.image.rectTransform.offsetMin = new Vector2(8.0f, v.rect.sizeDelta.y * 0.22f);
        v.image.rectTransform.offsetMax = new Vector2(-8.0f, -8.0f);
    }

    static string Initial(StageData stage) => stage != null && !string.IsNullOrEmpty(stage.displayName) ? stage.displayName.Substring(0, 1) : "?";

    static IEnumerator Wait(float seconds)
    {
        for (float t = 0.0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
    }

    TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color, Material mat)
    {
        var t = ToyKit.UIText(name, parent, m_font, text, size, color);
        if (mat != null) t.fontSharedMaterial = mat;
        return t;
    }
}
