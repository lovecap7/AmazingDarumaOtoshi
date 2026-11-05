using Nakahira;
using UnityEngine;

namespace Yurube
{
    // 試合中の必殺技ゲージをプレイヤーごとに表示する共通HUD。
    // 2P以上ではプレイヤー番号ぶん上へずらして重ならないようにする。
    // 満タン時は枠・バー・READY表示を発光させ、溜まったことが一目で分かるようにする。
    public static class SpecialGaugeHUD
    {
        static GUIStyle s_nameStyle;
        static GUIStyle s_statusStyle;
        static GUIStyle s_readyStyle;
        static bool s_stylesReady;

        public static void Draw(SpecialGauge gauge, DarumaCharacter character, string statusOverride = null)
        {
            if (gauge == null || character == null || DarumaMatch.Current == null) return;

            // カットイン中はゲージを描かない。
            // IMGUIの描画順やカットインの初回生成フレームに依存せず、
            // 必ずカットインが最前面になるようにする。
            if (SpecialCutIn.IsPlaying || IwadaruCutIn.IsPlaying
                || HebidaruCutIn.IsPlaying || DaruKingCutIn.IsPlaying) return;

            MatchPlayer me = null;
            foreach (var p in DarumaMatch.Current.Players)
            {
                if (p.Character == character)
                {
                    me = p;
                    break;
                }
            }

            if (me == null || me.IsCpu) return;

            EnsureStyles();

            int listIndex = 0;
            int playerCount = DarumaMatch.Current.Players.Count;
            for (int i = 0; i < playerCount; i++)
            {
                if (DarumaMatch.Current.Players[i] == me)
                {
                    listIndex = i;
                    break;
                }
            }

            // MatchHud と同じ 1920x1080 基準の配置。
            // 各プレイヤーのストックパネルの真上に必殺技ゲージを置く。
            const float panelWidth = 300.0f;
            const float panelHeight = 128.0f;
            const float panelSpacing = 24.0f;
            const float panelBottom = 120.0f;
            const float width = 300.0f;
            const float height = 54.0f;

            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            float hudScale = Mathf.Max(0.5f, Screen.height / 1080.0f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                new Vector3(hudScale, hudScale, 1.0f));

            float virtualWidth = Screen.width / hudScale;
            float virtualHeight = Screen.height / hudScale;
            float totalWidth = playerCount * panelWidth + (playerCount - 1) * panelSpacing;
            float panelCenterX = virtualWidth * 0.5f - totalWidth * 0.5f
                + panelWidth * 0.5f + listIndex * (panelWidth + panelSpacing);
            float x = panelCenterX - width * 0.5f;
            float gaugeBottom = panelBottom + panelHeight * 0.5f + 10.0f;
            float y = virtualHeight - gaugeBottom - height;

            bool full = gauge.IsFull;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7.0f);
            Color playerColor = me.Color;
            Color accent = full
                ? Color.Lerp(new Color(1.0f, 0.74f, 0.12f), Color.white, pulse)
                : playerColor;

            // パネル
            GUI.color = new Color(0.02f, 0.02f, 0.03f, 0.78f);
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);

            // 満タン時の外側グロー
            if (full)
            {
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.18f + 0.22f * pulse);
                GUI.DrawTexture(new Rect(x - 4.0f, y - 4.0f, width + 8.0f, height + 8.0f),
                    Texture2D.whiteTexture);
                GUI.color = new Color(0.02f, 0.02f, 0.03f, 0.9f);
                GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
            }

            // 枠
            GUI.color = accent;
            GUI.DrawTexture(new Rect(x, y, width, 2.0f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x, y + height - 2.0f, width, 2.0f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x, y, 2.0f, height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + width - 2.0f, y, 2.0f, height), Texture2D.whiteTexture);

            // 名前
            GUI.color = Color.white;
            string name = $"P{me.Index + 1} SPECIAL";
            GUI.Label(new Rect(x + 10.0f, y + 3.0f, 150.0f, 20.0f), name, s_nameStyle);

            // 状態表示
            string status;
            if (!string.IsNullOrEmpty(statusOverride))
                status = statusOverride;
            else if (full)
                status = "READY!  [L / Y]";
            else
                status = $"{gauge.Blocks}/{gauge.BlocksToFill}";

            GUI.color = full ? accent : new Color(0.82f, 0.86f, 0.92f);
            GUI.Label(new Rect(x + 150.0f, y + 3.0f, width - 160.0f, 20.0f), status,
                full ? s_readyStyle : s_statusStyle);

            // ゲージ背景
            var bar = new Rect(x + 10.0f, y + 29.0f, width - 20.0f, 14.0f);
            GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.75f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);

            // ゲージ本体
            float fillWidth = bar.width * gauge.Normalized;
            if (fillWidth > 0.0f)
            {
                GUI.color = full
                    ? Color.Lerp(new Color(1.0f, 0.78f, 0.20f), Color.white, pulse)
                    : playerColor;
                GUI.DrawTexture(new Rect(bar.x, bar.y, fillWidth, bar.height),
                    Texture2D.whiteTexture);
            }

            // 5分割の目盛り
            GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.55f);
            for (int i = 1; i < 5; i++)
            {
                float tickX = bar.x + bar.width * (i / 5.0f);
                GUI.DrawTexture(new Rect(tickX, bar.y, 2.0f, bar.height), Texture2D.whiteTexture);
            }

            // 満タン時は光が流れる
            if (full)
            {
                float shine = Mathf.Repeat(Time.unscaledTime * 0.9f, 1.0f);
                GUI.color = new Color(1.0f, 1.0f, 1.0f, 0.28f);
                GUI.DrawTexture(new Rect(bar.x + bar.width * shine - 12.0f, bar.y, 24.0f, bar.height),
                    Texture2D.whiteTexture);
            }

            GUI.color = Color.white;
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        static void EnsureStyles()
        {
            if (s_stylesReady) return;
            s_stylesReady = true;

            s_nameStyle = new GUIStyle(GUI.skin.label);
            s_nameStyle.fontSize = 18;
            s_nameStyle.fontStyle = FontStyle.Bold;
            s_nameStyle.alignment = TextAnchor.MiddleLeft;
            s_nameStyle.normal.textColor = Color.white;

            s_statusStyle = new GUIStyle(s_nameStyle);
            s_statusStyle.fontSize = 16;
            s_statusStyle.alignment = TextAnchor.MiddleRight;

            s_readyStyle = new GUIStyle(s_nameStyle);
            s_readyStyle.fontSize = 20;
            s_readyStyle.fontStyle = FontStyle.Bold;
            s_readyStyle.alignment = TextAnchor.MiddleRight;
        }
    }
}
