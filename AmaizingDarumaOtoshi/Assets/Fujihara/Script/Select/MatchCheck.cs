using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 確認用シーン。キャラクターセレクトで決まった内容（MatchSetup）が、シーンをまたいでも正しく届いているかを見る。
// ・「1P - あかだる（Gamepad #18）」「3P - CPU Lv3 - へびだる」のように1人1行で表示する
// ・各プレイヤーが自分のコントローラーで A（キーボードは Space）を押すと、その人の行が光る
//   → 押したコントローラーと光った行（プレイヤー番号・キャラ）が一致していれば、取り違えは起きていない
// ・Start / Enter でキャラクターセレクトにもどる
public class MatchCheck : MonoBehaviour
{
    [SerializeField] TMP_FontAsset font;
    [Tooltip("角丸の画像（UI/Skin/UISprite）")]
    [SerializeField] Sprite uiSprite;
    [SerializeField] string selectSceneName = "SelectScene";

    static readonly Color[] PlayerColors =
    {
        new Color(0.93f, 0.27f, 0.25f), new Color(0.22f, 0.55f, 0.95f), new Color(0.98f, 0.76f, 0.12f), new Color(0.30f, 0.74f, 0.35f),
        new Color(0.96f, 0.55f, 0.15f), new Color(0.40f, 0.85f, 0.85f), new Color(0.85f, 0.45f, 0.80f), new Color(0.55f, 0.45f, 0.35f),
    };

    class Row
    {
        public MatchSetup.Entry entry;
        public RectTransform rect;
        public TextMeshProUGUI pressed;
        public float flash = -100f;
    }

    readonly List<Row> rows = new List<Row>();
    Material letterMat, popMat;
    bool leaving;

    void Start()
    {
        var blockMesh = ToyKit.RoundedBox(new Vector3(ToyKit.BlockW, ToyKit.BlockH, ToyKit.BlockD), 0.14f, 10);
        var cam = ToyKit.CreateCamera();
        cam.transform.position = new Vector3(0f, 3f, -10f);
        cam.transform.LookAt(new Vector3(0f, 3f, 5f));
        ToyKit.CreateLights();
        var post = ToyKit.CreatePostProcess(0.35f);
        post.dof.focusDistance.value = 3f;
        post.dof.aperture.value = 2f;
        ToyKit.CreateEnvironment(blockMesh);

        letterMat = ToyKit.LetterMaterial(font);
        popMat = ToyKit.PopMaterial(letterMat);
        var canvas = ToyKit.CreateOverlayCanvas("UI", 10);

        var title = ToyKit.UIText("Title", canvas, font, "試合の設定（確認用）", 76f, Color.white);
        title.fontSharedMaterial = popMat;
        ToyKit.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1600f, 120f));

        var players = MatchSetup.Players;
        if (players.Count == 0)
        {
            var none = ToyKit.UIText("None", canvas, font, "データがありません\n<size=60%>キャラクターセレクトから来てください</size>", 60f, ToyKit.Ink);
            ToyKit.Anchor(none.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 300f));
        }

        float top = 280f, step = Mathf.Min(130f, 600f / Mathf.Max(1, players.Count));
        for (int i = 0; i < players.Count; i++)
        {
            var e = players[i];
            var color = e.isCpu ? new Color(0.55f, 0.55f, 0.58f) : PlayerColors[e.playerIndex % PlayerColors.Length];
            var bar = ToyKit.UIImage("Row" + e.PlayerLabel, canvas, color, uiSprite);
            ToyKit.Anchor(bar, new Vector2(0.5f, 0.5f), new Vector2(0f, top - i * step), new Vector2(1500f, step - 16f));
            bar.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;

            string who = e.isCpu ? "CPU Lv" + e.cpuLevel : e.deviceName + " #" + e.deviceId;
            string chara = e.character != null ? e.character.displayName : "(なし)";
            var t = ToyKit.UIText("Text", bar, font, e.PlayerLabel + "　-　" + chara + "　<size=70%>（" + who + "）</size>", 58f, Color.white);
            t.fontSharedMaterial = letterMat;
            t.alignment = TextAlignmentOptions.Left;
            ToyKit.Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(50f, 0f);

            var pressed = ToyKit.UIText("Pressed", bar, font, "← 押した！", 50f, Color.white);
            pressed.fontSharedMaterial = popMat;
            pressed.alignment = TextAlignmentOptions.Right;
            ToyKit.Stretch(pressed.rectTransform);
            pressed.rectTransform.offsetMax = new Vector2(-40f, 0f);
            pressed.alpha = 0f;

            rows.Add(new Row { entry = e, rect = bar, pressed = pressed });
        }

        var stage = ToyKit.UIText("Stage", canvas, font, "ステージ：" + (MatchSetup.Stage != null ? MatchSetup.Stage.displayName : "(なし)"), 56f, ToyKit.Ink);
        ToyKit.Anchor(stage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -320f), new Vector2(1500f, 90f));

        var hint = ToyKit.UIText("Hint", canvas, font,
            "自分のコントローラーで A（キーボードは Space）を押すと、自分の行が光ります　　Start / Enter：キャラクターセレクトにもどる", 30f, ToyKit.Ink);
        ToyKit.Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1800f, 60f));
    }

    void Update()
    {
        // その人の機器で押されたら、その人の行を光らせる
        foreach (var r in rows)
        {
            if (r.entry.isCpu) continue;
            if (PressedA(r.entry.Device)) r.flash = Time.unscaledTime;
            float f = Mathf.Clamp01(1f - (Time.unscaledTime - r.flash) / 0.8f);
            r.pressed.alpha = f;
            r.rect.localScale = Vector3.one * (1f + 0.04f * f);
        }

        if (leaving) return;
        if ((Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            || AnyStart())
        {
            leaving = true;
            Fader.Load(selectSceneName);
        }
    }

    static bool PressedA(InputDevice device)
    {
        if (device is Gamepad g) return g.buttonSouth.wasPressedThisFrame;
        if (device is Keyboard k) return k.spaceKey.wasPressedThisFrame || k.zKey.wasPressedThisFrame;
        return false;
    }

    static bool AnyStart()
    {
        foreach (var g in Gamepad.all) if (g.startButton.wasPressedThisFrame) return true;
        return false;
    }
}
