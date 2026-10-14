using System.Collections.Generic;
using Nakahira;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nakagawa
{
    // 試合中の画面下の表示(スマブラのように、参加している全員のパネルを横に並べる)
    // DarumaMatch と MatchPlayer を読むだけで、試合には影響しない。試合(DarumaMatch)と同じ GameObject に付ける
    // ゲームモードに応じて大きく出す数を変える
    //  ・ストック: 残りストック(アイコン)
    //  ・タイム  : 点数
    //  ・ゴースト: 撃墜数(相手の顔を飛ばした数)
    // どのモードでも、撃墜数と積んでいる積み木の数を小さく出す。復活待ち・幽霊・脱落も表示する
    // 途中参加(直接再生)した人のパネルもその場で増える
    [RequireComponent(typeof(DarumaMatch))]
    public class MatchHud : MonoBehaviour
    {
        [Header("配置(1920x1080 基準)")]
        [SerializeField] Vector2 m_panelSize = new Vector2(300.0f, 128.0f);
        [SerializeField] float m_spacing = 24.0f;
        // 画面下からパネルの中心までの高さ
        [SerializeField] float m_bottom = 120.0f;
        [SerializeField] int m_sortingOrder = 15;

        [Header("ストックのアイコン")]
        [SerializeField] float m_stockIconSize = 30.0f;
        [SerializeField] int m_maxStockIcons = 5;

        class Panel
        {
            public MatchPlayer Player;
            public RectTransform Root;
            public Image Background;
            public CanvasGroup Group;
            public TextMeshProUGUI Label, Name, Main, MainCaption, Sub, Status;
            public RectTransform StockRoot;
            public readonly List<Image> StockIcons = new List<Image>();
            public int LastMain = int.MinValue;
            public float Punch;
        }

        DarumaMatch m_match;
        RectTransform m_canvas;
        TMP_FontAsset m_font;
        Material m_letterMat, m_popMat;
        readonly List<Panel> m_panels = new List<Panel>();

        private void Start()
        {
            m_match = GetComponent<DarumaMatch>();
            m_font = PresentationSettings.DefaultFont;
            m_letterMat = ToyKit.LetterMaterial(m_font);
            m_popMat = ToyKit.PopMaterial(m_letterMat);
            m_canvas = ToyKit.CreateOverlayCanvas("MatchHud", m_sortingOrder);
            m_canvas.SetParent(transform, false);
        }

        private void OnDestroy()
        {
            if (m_canvas != null) Destroy(m_canvas.gameObject);
        }

        private void LateUpdate()
        {
            if (m_match == null || m_canvas == null) return;
            SyncPanels();
            float dt = Time.unscaledDeltaTime;
            foreach (var p in m_panels) UpdatePanel(p, dt);
        }

        // 参加者が増えたら(途中参加)パネルを足し、全員分を横に並べ直す
        private void SyncPanels()
        {
            var players = m_match.Players;
            bool changed = false;
            for (int i = m_panels.Count - 1; i >= 0; i--)
            {
                if (m_panels[i].Player != null) continue;
                Destroy(m_panels[i].Root.gameObject);
                m_panels.RemoveAt(i);
                changed = true;
            }
            foreach (var player in players)
            {
                if (player == null || m_panels.Exists(p => p.Player == player)) continue;
                m_panels.Add(CreatePanel(player));
                changed = true;
            }
            if (!changed) return;

            float total = m_panels.Count * m_panelSize.x + (m_panels.Count - 1) * m_spacing;
            for (int i = 0; i < m_panels.Count; i++)
            {
                float x = -total * 0.5f + m_panelSize.x * 0.5f + i * (m_panelSize.x + m_spacing);
                ToyKit.Anchor(m_panels[i].Root, new Vector2(0.5f, 0.0f), new Vector2(x, m_bottom), m_panelSize);
            }
        }

        private void UpdatePanel(Panel p, float dt)
        {
            var player = p.Player;
            var mode = m_match.Mode;

            // 大きく出す数(モードごと)
            int main;
            switch (mode)
            {
                case MatchSetup.MatchMode.Stock:
                    main = Mathf.Max(0, player.Stocks);
                    UpdateStocks(p, main);
                    p.Main.text = player.Stocks > m_maxStockIcons ? $"×{player.Stocks}" : "";
                    p.MainCaption.text = "ストック";
                    break;
                case MatchSetup.MatchMode.Time:
                    main = player.Score;
                    p.Main.text = main > 0 ? $"+{main}" : main.ToString();
                    p.MainCaption.text = "てん";
                    break;
                default:
                    main = player.KOs;
                    p.Main.text = main.ToString();
                    p.MainCaption.text = "げきつい";
                    break;
            }
            p.StockRoot.gameObject.SetActive(mode == MatchSetup.MatchMode.Stock);

            // 変わったら弾ませる
            if (p.LastMain != int.MinValue && p.LastMain != main) p.Punch = 1.0f;
            p.LastMain = main;
            p.Punch = Mathf.MoveTowards(p.Punch, 0.0f, dt * 3.0f);
            float s = 1.0f + 0.35f * Mathf.Sin(p.Punch * Mathf.PI) * p.Punch;
            p.Main.rectTransform.localScale = Vector3.one * s;
            p.StockRoot.localScale = Vector3.one * s;

            // 小さい数(撃墜・積み木)。ゴーストは撃墜数が大きい数なので出さない
            string kos = mode == MatchSetup.MatchMode.Stock || mode == MatchSetup.MatchMode.Time ? $"げきつい {player.KOs}　" : "";
            p.Sub.text = $"{kos}つみき {player.StackCount}";

            // 状態
            switch (player.State)
            {
                case MatchPlayer.PlayerState.Respawning:
                    p.Status.text = $"ふっかつまで {Mathf.CeilToInt(Mathf.Max(0.0f, player.RespawnTimeLeft))}";
                    p.Group.alpha = 0.75f;
                    break;
                case MatchPlayer.PlayerState.Ghost:
                    p.Status.text = "ゆうれい";
                    p.Group.alpha = 0.75f;
                    break;
                case MatchPlayer.PlayerState.Out:
                    p.Status.text = player.Rank > 0 ? $"{player.Rank}い" : "だつらく";
                    p.Group.alpha = 0.45f;
                    break;
                default:
                    p.Status.text = "";
                    p.Group.alpha = 1.0f;
                    break;
            }
        }

        // 残りストックの数だけアイコンを出す(多すぎるときは数字で出す)
        private void UpdateStocks(Panel p, int stocks)
        {
            int shown = stocks > m_maxStockIcons ? 1 : stocks;
            while (p.StockIcons.Count < shown)
            {
                var icon = ToyKit.UIImage("Stock", p.StockRoot, p.Player.Color);
                icon.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
                p.StockIcons.Add(icon.GetComponent<Image>());
            }
            for (int i = 0; i < p.StockIcons.Count; i++)
            {
                var icon = p.StockIcons[i];
                bool on = i < shown;
                icon.gameObject.SetActive(on);
                if (!on) continue;
                float x = (i - (shown - 1) * 0.5f) * (m_stockIconSize + 6.0f);
                ToyKit.Anchor(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, 0.0f), Vector2.one * m_stockIconSize);
                icon.rectTransform.localRotation = Quaternion.Euler(0, 0, 45.0f);
            }
        }

        // パネル1枚: 左に名札(1P)とキャラ名、右に大きな数(ストックはアイコン)、下に撃墜・積み木の数
        private Panel CreatePanel(MatchPlayer player)
        {
            var p = new Panel { Player = player };
            p.Root = ToyKit.UIImage($"Panel_{player.Label}", m_canvas, new Color(0.08f, 0.06f, 0.05f, 0.72f));
            p.Background = p.Root.GetComponent<Image>();
            p.Root.gameObject.AddComponent<Outline>().effectColor = player.Color;
            p.Group = p.Root.gameObject.AddComponent<CanvasGroup>();
            p.Group.blocksRaycasts = false;

            // 名札(プレイヤーの色)
            var tag = ToyKit.UIImage("Tag", p.Root, player.Color);
            ToyKit.Anchor(tag, new Vector2(0.0f, 1.0f), new Vector2(52.0f, -24.0f), new Vector2(88.0f, 36.0f));
            tag.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
            p.Label = Text("Label", tag, player.Label, 26.0f, Color.white, m_letterMat);
            ToyKit.Stretch(p.Label.rectTransform);

            // 顔(あれば)とキャラ名
            var data = player.CharacterData;
            if (data != null && data.portrait != null)
            {
                var face = ToyKit.UIImage("Portrait", p.Root, Color.white);
                var img = face.GetComponent<Image>();
                img.sprite = data.portrait;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                ToyKit.Anchor(face, new Vector2(0.0f, 0.0f), new Vector2(52.0f, 30.0f), new Vector2(48.0f, 48.0f));
            }
            p.Name = Text("Name", p.Root, data != null ? data.displayName : "", 18.0f, Color.white, m_letterMat);
            ToyKit.Anchor(p.Name.rectTransform, new Vector2(0.0f, 1.0f), new Vector2(56.0f, -56.0f), new Vector2(100.0f, 24.0f));
            p.Name.enableAutoSizing = true;
            p.Name.fontSizeMin = 10.0f;
            p.Name.fontSizeMax = 18.0f;

            // 大きな数
            p.Main = Text("Main", p.Root, "", 56.0f, Color.white, m_popMat);
            ToyKit.Anchor(p.Main.rectTransform, new Vector2(1.0f, 0.5f), new Vector2(-88.0f, 4.0f), new Vector2(160.0f, 70.0f));
            p.StockRoot = new GameObject("Stocks", typeof(RectTransform)).GetComponent<RectTransform>();
            p.StockRoot.SetParent(p.Root, false);
            ToyKit.Anchor(p.StockRoot, new Vector2(1.0f, 0.5f), new Vector2(-88.0f, 4.0f), new Vector2(160.0f, 40.0f));
            p.MainCaption = Text("Caption", p.Root, "", 18.0f, new Color(1.0f, 1.0f, 1.0f, 0.75f), m_letterMat);
            ToyKit.Anchor(p.MainCaption.rectTransform, new Vector2(1.0f, 1.0f), new Vector2(-88.0f, -16.0f), new Vector2(160.0f, 26.0f));

            // 撃墜・積み木
            p.Sub = Text("Sub", p.Root, "", 20.0f, Color.white, m_letterMat);
            ToyKit.Anchor(p.Sub.rectTransform, new Vector2(1.0f, 0.0f), new Vector2(-88.0f, 20.0f), new Vector2(170.0f, 28.0f));

            // 状態(復活待ち・幽霊・脱落)
            p.Status = Text("Status", p.Root, "", 22.0f, new Color(1.0f, 0.9f, 0.5f), m_letterMat);
            ToyKit.Anchor(p.Status.rectTransform, new Vector2(0.5f, 1.0f), new Vector2(0.0f, 18.0f), new Vector2(280.0f, 30.0f));
            return p;
        }

        private TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color, Material mat)
        {
            var t = ToyKit.UIText(name, parent, m_font, text, size, color);
            if (mat != null) t.fontSharedMaterial = mat;
            return t;
        }
    }
}
