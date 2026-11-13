using System.Collections.Generic;
using Nakahira;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ロビーで自分のキャラを変える画面(NetSessionMenu の「キャラクター」から開く)。見た目と操作はキャラクターセレクト(MatchSelect)と同じ:
//  ・キャラのマス(キャラクターセレクトと同じ並び。まだ選べないキャラ(だるきんぐ)は出さない。最後にランダム)
//  ・自分のカーソル(スティック・十字キー・WASD で動かす)、選んだマスにコイン、下に自分の枠(顔・名前・使いやすさの★)
//  ・A：マスを選ぶ(すぐにホストへ頼み、体が入れ替わる)/準備完了のボタンを押す　B：もどる　Start：準備完了の切り替え
//  ・枠の右に準備完了のボタン(ホスト以外で、試合に出る人だけ)。キャラを変えると準備完了は外れる(ホストが外す)
// 自分の画面にだけ出る(ほかの人はロビーの枠で「えらんでいます」と見える)
public class NetCharacterScreen : MonoBehaviour
{
    const float kCursorSpeed = 1300f;
    const int kSortingOrder = 55;   // メニュー(50)の上
    // キャラクターセレクト(SelectScene の MatchSelect)と同じ並べ方
    const int kColumns = 4, kRows = 2;
    static readonly Vector2 kPanelPos = new Vector2(-120f, -345f);
    const float kPanelWidth = 420f;

    class Tile
    {
        public RectTransform rect;
        public Vector2 size;
        public CharacterData character;
        public bool random;
    }

    public bool IsOpen { get; private set; }

    NetSelectLook m_look;
    RectTransform m_canvas, m_root, m_cursor, m_token, m_ready;
    readonly List<Tile> m_tiles = new List<Tile>();
    Image m_panelBg, m_portrait, m_portraitImage, m_readyBg;
    TextMeshProUGUI m_label, m_portraitLetter, m_name, m_easeLabel, m_easeUnknown, m_ok, m_readyText, m_hint;
    Image[] m_stars;
    Vector2 m_cursorPos;
    int m_openFrame;
    Tile m_placed;

    public void Open()
    {
        if (m_root == null) Build();
        IsOpen = true;
        m_openFrame = Time.frameCount;
        m_root.gameObject.SetActive(true);
        // カーソルはいま選んでいるマスから(なければ枠の上)
        var current = CurrentTile();
        m_cursorPos = current != null ? current.rect.anchoredPosition : kPanelPos + new Vector2(0f, 300f);
        m_placed = null;
        MenuSounds.Decide();
    }

    public void Close()
    {
        IsOpen = false;
        if (m_root != null) m_root.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (m_canvas != null) Destroy(m_canvas.gameObject);
    }

    void Update()
    {
        if (!IsOpen) return;
        var local = NetMatchPlayer.Local;
        if (local == null) { Close(); return; }
        float dt = Time.unscaledDeltaTime;

        if (m_openFrame != Time.frameCount)
        {
            var s = NetSelectLook.Read();
            m_cursorPos += s.move * kCursorSpeed * dt;
            m_cursorPos.x = Mathf.Clamp(m_cursorPos.x, -930f, 930f);
            m_cursorPos.y = Mathf.Clamp(m_cursorPos.y, -510f, 510f);

            if (s.b) { MenuSounds.Cancel(); Close(); return; }
            if (s.start && CanReady(local)) ToggleReady(local);
            else if (s.a)
            {
                if (m_ready.gameObject.activeSelf && Hit(m_ready, m_cursorPos)) ToggleReady(local);
                else
                {
                    var tile = TileAt(m_cursorPos);
                    if (tile != null) Pick(local, tile);
                }
            }
        }
        UpdateLook(local, dt);
    }

    // ホスト以外で、試合に出る人だけ準備完了にできる
    static bool CanReady(NetMatchPlayer local) => local != null && !local.IsHostPlayer && local.InMatch;

    void ToggleReady(NetMatchPlayer local)
    {
        bool ready = local.Status != NetMatchPlayer.NetStatus.Ready;
        local.RequestReady(ready);
        if (ready) MenuSounds.Decide(); else MenuSounds.Cancel();
        m_ready.localScale = Vector3.one * 1.15f;
    }

    void Pick(NetMatchPlayer local, Tile tile)
    {
        MenuSounds.Decide();
        if (tile.random) local.RequestRandomCharacter();
        else
        {
            var list = NetCharacterList.Instance;
            int index = list != null && tile.character != null ? list.IndexOf(tile.character.id) : NetCharacterList.None;
            if (index != NetCharacterList.None) local.RequestCharacter(index);
        }
    }

    // いま選んでいるマス(ランダムならランダムのマス)
    Tile CurrentTile()
    {
        var local = NetMatchPlayer.Local;
        var list = NetCharacterList.Instance;
        if (local == null || list == null) return null;
        if (NetMatchPlayer.IsRandomCharacter) return m_tiles.Find(t => t.random);
        var data = list.Character(local.CharacterIndex);
        return data != null ? m_tiles.Find(t => !t.random && t.character == data) : null;
    }

    Tile TileAt(Vector2 point) => m_tiles.Find(t => NetSelectLook.Contains(t.rect, t.size, point));

    bool Hit(RectTransform rect, Vector2 point) =>
        rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, m_canvas.TransformPoint(point), null);

    void UpdateLook(NetMatchPlayer local, float dt)
    {
        float k = 1f - Mathf.Exp(-14f * dt);
        Color color = local.Color;
        m_cursor.anchoredPosition = m_cursorPos;
        m_cursor.GetComponent<Image>().color = color;

        // マス: カーソルが乗っているものを少し大きく
        var hover = TileAt(m_cursorPos);
        foreach (var t in m_tiles) t.rect.localScale = Vector3.Lerp(t.rect.localScale, Vector3.one * (t == hover ? 1.08f : 1f), k);

        // コイン: いま選んでいるマスに置く(変わったら弾む)
        var current = CurrentTile();
        if (current != m_placed)
        {
            m_placed = current;
            if (current != null) NetSelectLook.PlaceToken(m_token, current.rect, current.size, local.Index);
            else m_token.gameObject.SetActive(false);
        }
        m_token.GetComponent<Image>().color = color;
        m_token.localScale = Vector3.Lerp(m_token.localScale, Vector3.one, k);

        // 枠: 選んでいるキャラ(まだならカーソルが乗っているキャラを薄く)
        m_panelBg.color = Color.Lerp(color, Color.white, 0.55f);
        m_label.text = local.Label;
        m_label.color = color;
        var texts = m_look.Texts;
        CharacterData shown = null;
        if (current != null && current.random) SetPortrait(null, texts.randomCharacter, NetSelectLook.RandomColor, texts.randomLetter);
        else if (current != null) { shown = current.character; SetPortrait(shown.portrait, shown.displayName, shown.IconColor, NetSelectLook.Initial(shown.displayName)); }
        else if (hover != null && !hover.random) { shown = hover.character; SetPortrait(shown.portrait, shown.displayName, shown.IconColor * 0.75f, NetSelectLook.Initial(shown.displayName)); }
        else SetPortrait(null, texts.chooseCharacter, new Color(0.92f, 0.9f, 0.88f), "");
        if (hover != null && !hover.random && current != hover) shown = hover.character;
        m_easeLabel.text = texts.easeLabel;
        int n = shown != null ? Mathf.Clamp(shown.ease, 1, m_stars.Length) : 0;
        for (int i = 0; i < m_stars.Length; i++)
        {
            m_stars[i].gameObject.SetActive(i < n);
            m_stars[i].rectTransform.anchoredPosition = new Vector2((i - (n - 1) * 0.5f) * 34f, 0f);
        }
        m_easeUnknown.text = shown == null ? texts.easeUnknown : "";

        // 準備完了(枠の OK! と、右のボタン)
        bool ready = local.Status == NetMatchPlayer.NetStatus.Ready;
        m_ok.gameObject.SetActive(ready);
        bool canReady = CanReady(local);
        m_ready.gameObject.SetActive(canReady);
        m_readyBg.color = ready ? ToyKit.Palette[0] : Color.white;
        m_readyText.color = ready ? Color.white : ToyKit.Ink;
        m_readyText.text = ready ? "じゅんびOK！\n<size=55%>もう一度で とりけし</size>" : "じゅんび完了\n<size=55%>Start でも おせる</size>";
        bool readyHover = canReady && Hit(m_ready, m_cursorPos);
        m_ready.localScale = Vector3.Lerp(m_ready.localScale, Vector3.one * (readyHover ? 1.06f : 1f), k);
        m_hint.text = canReady
            ? "スティック：うごかす　A：えらぶ　Start：じゅんび完了　B：もどる"
            : "スティック：うごかす　A：えらぶ　B：もどる";
    }

    void SetPortrait(Sprite sprite, string name, Color color, string letter)
    {
        m_name.text = name;
        m_portrait.color = color;
        m_portraitImage.sprite = sprite;
        m_portraitImage.enabled = sprite != null;
        m_portraitLetter.text = sprite != null ? "" : letter;
    }

    //========================================
    // 組み立て(キャラクターセレクトと同じ大きさ・並び)
    //========================================

    void Build()
    {
        m_look = new NetSelectLook();
        var look = m_look;
        m_canvas = ToyKit.CreateOverlayCanvas("NetCharacterScreen", kSortingOrder);
        m_root = NetSelectLook.Layer("Root", m_canvas);
        var back = ToyKit.UIImage("Backdrop", m_root, NetSelectLook.Backdrop);
        ToyKit.Stretch(back);
        look.Header(m_root, look.Texts.characterHeader, ToyKit.Palette[0]);

        // キャラのマス(キャラクターセレクトと同じく、まだ選べないキャラは出さない。最後にランダム)
        var list = NetCharacterList.Instance;
        var roster = list != null ? list.roster : null;
        var characters = roster != null
            ? Yurube.Achievements.AchievementRewards.AvailableCharacters(roster.OrderedCharacters())
            : new List<CharacterData>();
        // ネット対戦で出せない(NetCharacterList に無い)キャラは並べない
        characters.RemoveAll(c => c == null || list.IndexOf(c.id) == NetCharacterList.None);
        int count = characters.Count + 1;
        var grid = NetSelectLook.GridLayout(count, kColumns, new Vector2(1760f, 430f), new Vector2(205f, 205f), 16f, 310f, kRows, true, true);
        for (int i = 0; i < count; i++)
        {
            bool random = i == characters.Count;
            var c = random ? null : characters[i];
            var rect = look.MakeTile(m_root, grid[i].pos, grid[i].size, random ? NetSelectLook.RandomColor : c.IconColor,
                                     random ? null : c.portrait, random ? look.Texts.randomCharacter : c.displayName,
                                     random ? look.Texts.randomLetter : NetSelectLook.Initial(c.displayName));
            // キャラの顔は大きめに(キャラクターセレクトと同じ)
            var face = rect.Find("Image") as RectTransform;
            if (face != null)
            {
                face.offsetMin = new Vector2(3f, grid[i].size.y * 0.2f);
                face.offsetMax = new Vector2(-3f, -3f);
            }
            m_tiles.Add(new Tile { rect = rect, size = grid[i].size, character = c, random = random });
        }

        BuildPanel();
        BuildReadyButton();

        // 操作の説明(右上。見出しと同じ高さ)
        m_hint = ToyKit.UIText("Hint", m_root, look.Font, "", 30f, ToyKit.Ink);
        m_hint.alignment = TextAlignmentOptions.Right;
        ToyKit.Anchor(m_hint.rectTransform, new Vector2(1f, 1f), new Vector2(-560f, -85f), new Vector2(1040f, 44f));

        var cursors = NetSelectLook.Layer("Cursors", m_root);
        m_token = look.MakeToken(m_root, "", Color.white);
        m_cursor = look.MakeCursor(cursors, "", Color.white);
        var local = NetMatchPlayer.Local;
        string label = local != null ? local.Label : "1P";
        m_cursor.Find("Label").GetComponent<TextMeshProUGUI>().text = label;
        m_token.Find("Label").GetComponent<TextMeshProUGUI>().text = label;
        m_root.gameObject.SetActive(false);
    }

    // 自分の枠(キャラクターセレクトのプレイヤーの枠と同じ。CPU・チームの部分は無し)
    void BuildPanel()
    {
        var look = m_look;
        float w = kPanelWidth;
        var r = ToyKit.UIImage("Panel", m_root, Color.white, look.UiSprite);
        ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), kPanelPos, new Vector2(w, 270f));
        r.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        var ol = r.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3f, -3f);
        m_panelBg = r.GetComponent<Image>();

        m_label = ToyKit.UIText("Label", r, look.Font, "1P", 60f, Color.white);
        m_label.fontSharedMaterial = look.LetterMat;
        m_label.alignment = TextAlignmentOptions.TopLeft;
        ToyKit.Anchor(m_label.rectTransform, new Vector2(0f, 1f), new Vector2(88f, -48f), new Vector2(150f, 80f));

        m_portrait = ToyKit.UIImage("Portrait", r, Color.white, look.UiSprite).GetComponent<Image>();
        ToyKit.Anchor(m_portrait.rectTransform, new Vector2(0f, 0.5f), new Vector2(w * 0.27f, -30f), new Vector2(w * 0.38f, w * 0.38f));
        m_portraitImage = ToyKit.UIImage("Image", m_portrait.rectTransform, Color.white).GetComponent<Image>();
        m_portraitImage.preserveAspect = true;
        ToyKit.Stretch(m_portraitImage.rectTransform);
        m_portraitLetter = ToyKit.UIText("Letter", m_portrait.rectTransform, look.Font, "", w * 0.22f, Color.white);
        m_portraitLetter.fontSharedMaterial = look.LetterMat;
        ToyKit.Stretch(m_portraitLetter.rectTransform);

        m_name = ToyKit.UIText("Name", r, look.Font, "", 44f, ToyKit.Ink);
        m_name.enableAutoSizing = true; m_name.fontSizeMin = 18f; m_name.fontSizeMax = 44f;
        ToyKit.Anchor(m_name.rectTransform, new Vector2(1f, 0.5f), new Vector2(-w * 0.28f, -10f), new Vector2(w * 0.5f, 70f));
        m_easeLabel = ToyKit.UIText("Ease", r, look.Font, "", 24f, ToyKit.Ink);
        ToyKit.Anchor(m_easeLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(-w * 0.28f, -58f), new Vector2(w * 0.5f, 32f));
        var starRow = new GameObject("Stars", typeof(RectTransform)).GetComponent<RectTransform>();
        starRow.SetParent(m_easeLabel.rectTransform, false);
        ToyKit.Anchor(starRow, new Vector2(0.5f, 0f), new Vector2(0f, -22f), new Vector2(w * 0.5f, 34f));
        m_stars = new Image[5];
        for (int i = 0; i < m_stars.Length; i++)
        {
            var st = ToyKit.UIImage("Star" + i, starRow, new Color(0.96f, 0.68f, 0.1f), NetSelectLook.StarSprite);
            ToyKit.Anchor(st, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(32f, 32f));
            st.GetComponent<Image>().type = Image.Type.Simple;
            var so = st.gameObject.AddComponent<Outline>(); so.effectColor = ToyKit.Ink; so.effectDistance = new Vector2(1.5f, -1.5f);
            m_stars[i] = st.GetComponent<Image>();
        }
        m_easeUnknown = ToyKit.UIText("Unknown", starRow, look.Font, "", 30f, ToyKit.Ink);
        ToyKit.Stretch(m_easeUnknown.rectTransform);

        m_ok = ToyKit.UIText("OK", m_portrait.rectTransform, look.Font, look.Texts.ok, 56f, ToyKit.Palette[0]);
        m_ok.fontSharedMaterial = look.PopMat;
        m_ok.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -12f);
        ToyKit.Anchor(m_ok.rectTransform, new Vector2(1f, 1f), Vector2.zero, new Vector2(160f, 80f));
    }

    // 準備完了のボタン(枠の右。カーソルで押すか、Start)
    void BuildReadyButton()
    {
        var look = m_look;
        m_ready = ToyKit.UIImage("Ready", m_root, Color.white, look.UiSprite);
        m_ready.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        ToyKit.Anchor(m_ready, new Vector2(0.5f, 0.5f), kPanelPos + new Vector2(kPanelWidth * 0.5f + 190f, 0f), new Vector2(320f, 150f));
        var ol = m_ready.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3f, -3f);
        m_readyBg = m_ready.GetComponent<Image>();
        m_readyText = ToyKit.UIText("Text", m_ready, look.Font, "", 48f, ToyKit.Ink);
        m_readyText.fontSharedMaterial = look.LetterMat;
        m_readyText.enableAutoSizing = true; m_readyText.fontSizeMin = 24f; m_readyText.fontSizeMax = 48f;
        ToyKit.Stretch(m_readyText.rectTransform);
        m_readyText.rectTransform.offsetMin = new Vector2(12f, 8f);
        m_readyText.rectTransform.offsetMax = new Vector2(-12f, -8f);
    }
}
