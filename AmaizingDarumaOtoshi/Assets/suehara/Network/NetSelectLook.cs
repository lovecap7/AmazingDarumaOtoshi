using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ロビーのキャラ・ステージを選ぶ画面(NetCharacterScreen・NetStageScreen)を、キャラクターセレクト(藤原さんの MatchSelect)と
// 同じ見た目にするための部品。作り方(大きさ・色・縁)は MatchSelect と同じ(藤原さんのファイルは変えずに、こちらで同じものを作る)。
// 素材(キャラの並び・角丸と丸の画像・フォント)は NetCharacterList に設定してある。1920x1080 基準
public class NetSelectLook
{
    public static readonly Color RandomColor = new Color(0.7f, 0.7f, 0.72f);
    public static readonly Color Backdrop = new Color(0.96f, 0.93f, 0.87f, 0.97f);

    public readonly TMP_FontAsset Font;
    public readonly Sprite UiSprite, KnobSprite;
    public readonly Material LetterMat, PopMat;
    public readonly SelectTexts Texts = new SelectTexts();

    public NetSelectLook()
    {
        var list = NetCharacterList.Instance;
        Font = list != null && list.font != null ? list.font : PresentationSettings.DefaultFont;
        UiSprite = list != null ? list.uiSprite : null;
        KnobSprite = list != null ? list.knobSprite : null;
        LetterMat = ToyKit.LetterMaterial(Font);
        PopMat = ToyKit.PopMaterial(LetterMat);
    }

    public static RectTransform Layer(string name, Transform parent)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent, false);
        ToyKit.Stretch(r);
        return r;
    }

    // 左上の見出し(MatchSelect.Header と同じ)
    public TextMeshProUGUI Header(Transform parent, string text, Color accent)
    {
        var bar = ToyKit.UIImage("HeaderBar", parent, accent, UiSprite);
        bar.anchorMin = bar.anchorMax = new Vector2(0f, 1f);
        bar.pivot = new Vector2(0f, 1f);
        bar.anchoredPosition = new Vector2(-30f, -30f);
        bar.sizeDelta = new Vector2(760f, 110f);
        var t = ToyKit.UIText("Header", bar, Font, text, 66f, Color.white);
        t.fontSharedMaterial = PopMat;
        t.alignment = TextAlignmentOptions.Left;
        ToyKit.Anchor(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(400f, 0f), new Vector2(700f, 110f));
        return t;
    }

    public struct Cell { public Vector2 pos, size; }

    // 数に合わせてマスの大きさと並びを決める(MatchSelect.GridLayout と同じ)
    public static Cell[] GridLayout(int count, int maxCols, Vector2 area, Vector2 maxSize, float gap, float top, int fixedRows = 0, bool centerLast = true, bool keepCols = false)
    {
        int cols = fixedRows > 0 || keepCols ? Mathf.Max(1, maxCols) : Mathf.Max(1, Mathf.Min(count, maxCols));
        int rows = Mathf.Max(fixedRows, Mathf.CeilToInt(count / (float)cols));
        float scale = Mathf.Min(1f,
            (area.x - gap * (cols - 1)) / (cols * maxSize.x),
            (area.y - gap * (rows - 1)) / (rows * maxSize.y));
        var size = maxSize * scale;
        var cells = new Cell[count];
        for (int i = 0; i < count; i++)
        {
            int col = i % cols, row = i / cols;
            int inRow = centerLast ? Mathf.Min(cols, count - row * cols) : cols;
            cells[i].pos = new Vector2((col - (inRow - 1) * 0.5f) * (size.x + gap), top - size.y * 0.5f - row * (size.y + gap));
            cells[i].size = size;
        }
        return cells;
    }

    // マス1つ(MatchSelect.MakeTile と同じ)
    public RectTransform MakeTile(Transform parent, Vector2 pos, Vector2 size, Color color, Sprite sprite, string name, string letter)
    {
        var r = ToyKit.UIImage("Tile_" + name, parent, color, UiSprite);
        r.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), pos, size);
        var ol = r.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3f, -3f);

        if (sprite != null)
        {
            var img = ToyKit.UIImage("Image", r, Color.white).GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            ToyKit.Stretch(img.rectTransform);
            img.rectTransform.offsetMin = new Vector2(8f, size.y * 0.22f);
            img.rectTransform.offsetMax = new Vector2(-8f, -8f);
        }
        var l = ToyKit.UIText("Letter", r, Font, sprite != null ? "" : letter, Mathf.Min(size.x, size.y) * 0.5f, Color.white);
        l.fontSharedMaterial = LetterMat;
        ToyKit.Stretch(l.rectTransform);
        l.rectTransform.offsetMin = new Vector2(0f, size.y * 0.18f);

        var strip = ToyKit.UIImage("NameStrip", r, new Color(1f, 1f, 1f, 0.85f), UiSprite);
        strip.anchorMin = new Vector2(0f, 0f); strip.anchorMax = new Vector2(1f, 0f); strip.pivot = new Vector2(0.5f, 0f);
        strip.sizeDelta = new Vector2(-12f, size.y * 0.22f);
        strip.anchoredPosition = new Vector2(0f, 6f);
        var n = ToyKit.UIText("Name", strip, Font, name, size.y * 0.13f, ToyKit.Ink);
        n.enableAutoSizing = true; n.fontSizeMin = 10f; n.fontSizeMax = size.y * 0.14f;
        ToyKit.Stretch(n.rectTransform);
        return r;
    }

    // プレイヤーのカーソル(色付きの丸に「1P」。MatchSelect.MakeCursor と同じ)
    public RectTransform MakeCursor(Transform parent, string label, Color color)
    {
        var r = ToyKit.UIImage("Cursor" + label, parent, color, KnobSprite);
        r.GetComponent<Image>().type = Image.Type.Simple;
        ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 84f));
        var ol = r.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3f, -3f);
        var t = ToyKit.UIText("Label", r, Font, label, 34f, Color.white);
        t.fontSharedMaterial = LetterMat;
        ToyKit.Stretch(t.rectTransform);
        return r;
    }

    // 選んだマスに置くコイン(MatchSelect.MakeToken と同じ)
    public RectTransform MakeToken(Transform parent, string label, Color color)
    {
        var r = ToyKit.UIImage("Token" + label, parent, color, KnobSprite);
        r.GetComponent<Image>().type = Image.Type.Simple;
        ToyKit.Anchor(r, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f));
        var ol = r.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(2f, -2f);
        var t = ToyKit.UIText("Label", r, Font, label, 24f, Color.white);
        t.fontSharedMaterial = LetterMat;
        ToyKit.Stretch(t.rectTransform);
        r.gameObject.SetActive(false);
        return r;
    }

    // マスにコインを置く(1P は左上。MatchSelect.PlaceToken と同じ)
    public static void PlaceToken(RectTransform token, RectTransform tile, Vector2 tileSize, int index)
    {
        token.SetParent(tile, false);
        var corner = new Vector2(index % 2 == 0 ? -1f : 1f, index / 2 % 2 == 0 ? 1f : -1f);
        token.anchoredPosition = Vector2.Scale(corner, tileSize * 0.36f);
        token.gameObject.SetActive(true);
        token.localScale = Vector3.one * 1.4f;
    }

    public static bool Contains(RectTransform tile, Vector2 tileSize, Vector2 point)
    {
        var d = point - tile.anchoredPosition;
        return Mathf.Abs(d.x) <= tileSize.x * 0.5f && Mathf.Abs(d.y) <= tileSize.y * 0.5f;
    }

    public static string Initial(string name) => string.IsNullOrEmpty(name) ? "" : name.Substring(0, 1);

    // ★の画像(MatchSelect と同じ5つの角の星)
    static Sprite s_star;
    public static Sprite StarSprite
    {
        get
        {
            if (s_star != null) return s_star;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Star", wrapMode = TextureWrapMode.Clamp };
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float rad = (i % 2 == 0 ? 0.48f : 0.2f) * size;
                pts[i] = new Vector2(size * 0.5f + Mathf.Cos(a) * rad, size * 0.47f + Mathf.Sin(a) * rad);
            }
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (InPolygon(pts, new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f))) hit++;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 16));
                }
            tex.SetPixels32(px);
            tex.Apply();
            s_star = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return s_star;
        }
    }

    static bool InPolygon(Vector2[] poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        return inside;
    }

    //========================================
    // 入力(キャラクターセレクトと同じボタン。この画面を選んでいるときだけ)
    //========================================

    public struct Pad
    {
        public Vector2 move, look;
        public bool a, b, start;
    }

    // キーボード・コントローラーをまとめて読む(MatchSelect.Read と同じボタン。Enter は Start)
    public static Pad Read()
    {
        var s = new Pad();
        if (!Application.isFocused) return s;
        foreach (var g in Gamepad.all)
        {
            var m = g.leftStick.ReadValue();
            var dp = g.dpad.ReadValue();
            if (dp.sqrMagnitude > m.sqrMagnitude) m = dp;
            if (m.magnitude >= 0.2f && m.sqrMagnitude > s.move.sqrMagnitude) s.move = m;
            var look = g.rightStick.ReadValue();
            if (look.magnitude >= 0.2f) s.look = look;
            s.a |= g.buttonSouth.wasPressedThisFrame;
            s.b |= g.buttonEast.wasPressedThisFrame;
            s.start |= g.startButton.wasPressedThisFrame;
        }
        var k = Keyboard.current;
        if (k != null)
        {
            float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
            float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
            var km = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            if (km.sqrMagnitude > s.move.sqrMagnitude) s.move = km;
            var kl = new Vector2((k.lKey.isPressed ? 1f : 0f) - (k.jKey.isPressed ? 1f : 0f), (k.iKey.isPressed ? 1f : 0f) - (k.kKey.isPressed ? 1f : 0f));
            if (kl != Vector2.zero) s.look = kl;
            s.a |= k.spaceKey.wasPressedThisFrame || k.zKey.wasPressedThisFrame;
            s.b |= k.escapeKey.wasPressedThisFrame || k.xKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame;
            s.start |= k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame;
        }
        return s;
    }
}
