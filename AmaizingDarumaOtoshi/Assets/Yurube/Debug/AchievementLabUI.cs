#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using Yurube.Achievements;

namespace Yurube.Lab
{
    // 実績ラボの画面（IMGUI）。キャラクターラボと同じ作りで、シーンに置いた AchievementLab から作られる。
    // 左：実績の一覧（絞り込み・検索）　右：選択中の詳細と、テスト操作・通知・セーブの確認
    // デバッグ専用（UNITY_EDITOR / DEVELOPMENT_BUILD のときだけ入る）。
    public class AchievementLabUI : MonoBehaviour
    {
        enum Filter { All, Locked, Unlocked }

        const float LeftWidth = 470f;
        const float RowHeight = 54f;

        AchievementLab m_lab;
        Font m_font;
        GUIStyle m_title, m_section, m_label, m_small, m_button, m_field, m_rowName, m_rowInfo, m_progressText, m_state, m_flashStyle;
        bool m_stylesReady;

        AchievementData m_selected;
        Filter m_filter = Filter.All;
        AchievementCategory? m_category;
        string m_search = "";
        Vector2 m_listScroll, m_detailScroll;

        string m_chainText = "3";
        string m_stackText = "5";
        string m_progressText2 = "0";
        float m_duration;
        string m_flash = "";
        float m_flashTime;

        static Texture2D s_white;

        public void Initialize(AchievementLab lab)
        {
            m_lab = lab;
            m_duration = AchievementNotification.Duration;
            var all = AchievementManager.All;
            if (all.Count > 0) m_selected = all[0];
        }

        //========================================
        // 見た目
        //========================================

        void EnsureStyles()
        {
            if (m_stylesReady) return;
            if (m_font == null)
            {
                // IMGUI の既定フォントでは日本語が出ないことがあるため、OS のフォントを使う
                m_font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo", "MS Gothic" }, 16);
                if (m_font == null) m_font = GUI.skin.font;
            }
            // ラボの画面は明るい色で塗るので、文字はすべて黒に統一する。
            // エディタのスキン（ダーク／ライト）で既定の文字色が変わるため、状態ごとに明示する。
            m_title = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 24, fontStyle = FontStyle.Bold };
            m_section = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 16, fontStyle = FontStyle.Bold };
            m_label = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 14, wordWrap = true };
            m_small = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 12, wordWrap = true };
            m_rowName = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 16, fontStyle = FontStyle.Bold };
            m_rowInfo = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 12, wordWrap = false };
            m_progressText = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 12, alignment = TextAnchor.MiddleRight };
            m_state = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 15, alignment = TextAnchor.MiddleCenter };
            m_flashStyle = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 14, wordWrap = false };

            SetTextColor(m_title);
            SetTextColor(m_section);
            SetTextColor(m_label);
            SetTextColor(m_small);
            SetTextColor(m_rowName);
            SetTextColor(m_rowInfo);
            SetTextColor(m_progressText);
            SetTextColor(m_state);
            SetTextColor(m_flashStyle);

            // ボタン・入力欄も、スキンのままだとダークスキンで「濃い背景＋白文字」になる。
            // ラボは明るい背景なので、明るい下地＋黒文字の自前スタイルにする
            m_button = MakeButtonStyle();
            m_field = MakeFieldStyle();
            m_stylesReady = true;
        }

        // 文字色（ほぼ黒）。選択・ホバー・押したときも同じ色にして、白くならないようにする
        static readonly Color TextBlack = new Color(0.06f, 0.05f, 0.04f);

        static void SetTextColor(GUIStyle style)
        {
            style.normal.textColor = TextBlack;
            style.hover.textColor = TextBlack;
            style.active.textColor = TextBlack;
            style.focused.textColor = TextBlack;
            style.onNormal.textColor = TextBlack;
            style.onHover.textColor = TextBlack;
            style.onActive.textColor = TextBlack;
            style.onFocused.textColor = TextBlack;
        }

        static Texture2D s_buttonNormal, s_buttonHover, s_buttonActive, s_field;

        static Texture2D Solid(ref Texture2D cache, Color color)
        {
            if (cache != null) return cache;
            cache = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            cache.SetPixel(0, 0, color);
            cache.Apply();
            return cache;
        }

        GUIStyle MakeButtonStyle()
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                font = m_font,
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(8, 8, 4, 4),
            };
            style.normal.background = Solid(ref s_buttonNormal, new Color(0.93f, 0.91f, 0.86f));
            style.hover.background = Solid(ref s_buttonHover, new Color(0.86f, 0.83f, 0.76f));
            style.active.background = Solid(ref s_buttonActive, new Color(0.78f, 0.74f, 0.66f));
            style.focused.background = style.normal.background;
            SetTextColor(style);
            return style;
        }

        GUIStyle MakeFieldStyle()
        {
            var style = new GUIStyle(GUI.skin.textField)
            {
                font = m_font,
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(8, 8, 4, 4),
            };
            style.normal.background = Solid(ref s_field, Color.white);
            style.hover.background = style.normal.background;
            style.active.background = style.normal.background;
            style.focused.background = style.normal.background;
            SetTextColor(style);
            return style;
        }

        static Texture2D White
        {
            get
            {
                if (s_white == null)
                {
                    s_white = new Texture2D(1, 1);
                    s_white.SetPixel(0, 0, Color.white);
                    s_white.Apply();
                    s_white.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_white;
            }
        }

        static void Fill(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, White);
            GUI.color = old;
        }

        static void DrawBar(Rect rect, float ratio, Color color)
        {
            Fill(rect, new Color(0f, 0f, 0f, 0.16f));
            float w = Mathf.Clamp01(ratio) * Mathf.Max(0f, rect.width - 2f);
            Fill(new Rect(rect.x + 1f, rect.y + 1f, w, rect.height - 2f), color);
        }

        static Color CategoryColor(AchievementCategory category) => ToyKit.Palette[(int)category % ToyKit.Palette.Length];

        void Flash(string message)
        {
            m_flash = message;
            m_flashTime = Time.unscaledTime;
        }

        //========================================
        // 画面
        //========================================

        void OnGUI()
        {
            if (m_lab == null) return;
            EnsureStyles();
            // 前のフレームの色が残らないように、毎回まっさらにしてから描く
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.backgroundColor = Color.white;
            float h = Screen.height;
            // 上は通知（実物のポップアップ）が隠れずに出るように空けておく
            const float top = 200f;
            var left = new Rect(10f, top, LeftWidth, h - top - 10f);
            var right = new Rect(LeftWidth + 20f, top, Screen.width - LeftWidth - 30f, h - top - 10f);
            GUI.Label(new Rect(LeftWidth + 20f, 6f, Screen.width - LeftWidth - 30f, 28f),
                      "解除通知は画面の右上に出ます（実物）", m_small);
            DrawLeft(left);
            DrawRight(right);
            if (!string.IsNullOrEmpty(m_flash) && Time.unscaledTime - m_flashTime < 3f)
            {
                // 明るい帯＋黒文字（まわりのパネルと同じ見た目）
                GUI.backgroundColor = new Color(0.95f, 0.88f, 0.70f);
                GUI.Box(new Rect(right.x, right.yMax - 40f, Mathf.Min(600f, right.width), 30f), "");
                GUI.backgroundColor = Color.white;
                GUI.Label(new Rect(right.x + 12f, right.yMax - 38f, right.width, 26f), m_flash, m_flashStyle);
            }
        }

        void DrawLeft(Rect rect)
        {
            GUI.backgroundColor = new Color(1f, 0.98f, 0.93f);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = Color.white;

            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f));
            GUILayout.Label("実績ラボ", m_title);
            GUILayout.Label("ゲーム本編と同じ入り口から解除条件を試せます", m_small);
            GUILayout.Space(4f);

            // 絞り込み
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(FilterLabel(Filter.All), m_button)) m_filter = Filter.All;
            if (GUILayout.Button(FilterLabel(Filter.Locked), m_button)) m_filter = Filter.Locked;
            if (GUILayout.Button(FilterLabel(Filter.Unlocked), m_button)) m_filter = Filter.Unlocked;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(CategoryLabel(null), m_button)) m_category = null;
            if (GUILayout.Button(CategoryLabel(AchievementCategory.Match), m_button)) m_category = AchievementCategory.Match;
            if (GUILayout.Button(CategoryLabel(AchievementCategory.Attack), m_button)) m_category = AchievementCategory.Attack;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(CategoryLabel(AchievementCategory.Chain), m_button)) m_category = AchievementCategory.Chain;
            if (GUILayout.Button(CategoryLabel(AchievementCategory.Stack), m_button)) m_category = AchievementCategory.Stack;
            if (GUILayout.Button(CategoryLabel(AchievementCategory.Challenge), m_button)) m_category = AchievementCategory.Challenge;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("検索", m_label, GUILayout.Width(36f));
            m_search = GUILayout.TextField(m_search ?? "", m_field);
            if (GUILayout.Button("クリア", m_button, GUILayout.Width(64f))) m_search = "";
            GUILayout.EndHorizontal();

            var visible = VisibleAchievements();
            GUILayout.Label("表示 " + visible.Count + " 件 / 全 " + AchievementManager.All.Count + " 件 ・ 解除 "
                            + AchievementManager.UnlockedCount + " 件", m_small);
            GUILayout.Label("報酬キャラ「" + AchievementRewards.RewardCharacterName + "」："
                            + (AchievementRewards.IsRewardUnlocked ? "解放済み" : "未解放")
                            + "（ラボのデータで " + AchievementManager.UnlockedCount + "/"
                            + AchievementManager.All.Count + "）", m_small);
            GUILayout.Space(2f);

            m_listScroll = GUILayout.BeginScrollView(m_listScroll, false, true);
            foreach (var data in visible) DrawRow(data);
            if (visible.Count == 0) GUILayout.Label("条件に合う実績がありません", m_label);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        string FilterLabel(Filter filter)
        {
            string mark = m_filter == filter ? "● " : "";
            switch (filter)
            {
                case Filter.Locked: return mark + "未解除";
                case Filter.Unlocked: return mark + "解除済み";
                default: return mark + "すべて";
            }
        }

        string CategoryLabel(AchievementCategory? category)
        {
            string mark = m_category == category ? "● " : "";
            return mark + (category.HasValue ? AchievementCatalog.CategoryName(category.Value) : "全部");
        }

        List<AchievementData> VisibleAchievements()
        {
            var list = new List<AchievementData>();
            string search = (m_search ?? "").Trim();
            foreach (var data in AchievementManager.All)
            {
                if (m_filter == Filter.Locked && data.IsUnlocked) continue;
                if (m_filter == Filter.Unlocked && !data.IsUnlocked) continue;
                if (m_category.HasValue && data.Category != m_category.Value) continue;
                if (search.Length > 0 &&
                    data.Name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    data.Poem.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    data.ConditionText.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(data);
            }
            return list;
        }

        void DrawRow(AchievementData data)
        {
            Rect row = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
            bool selected = data == m_selected;
            bool hover = row.Contains(Event.current.mousePosition);

            Color background = selected
                ? new Color(0.98f, 0.80f, 0.38f, 0.95f)
                : data.IsUnlocked ? new Color(0.78f, 0.92f, 0.72f, 0.95f)
                : hover ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.70f);
            Fill(row, background);
            Fill(new Rect(row.x, row.y, 6f, row.height), CategoryColor(data.Category));

            var stateRect = new Rect(row.x + 10f, row.y + 6f, 26f, 22f);
            GUI.Label(stateRect, data.IsUnlocked ? "★" : "☆", m_state);

            GUI.Label(new Rect(row.x + 40f, row.y + 5f, row.width - 150f, 22f), data.Name, m_rowName);
            GUI.Label(new Rect(row.x + 40f, row.y + 27f, row.width - 160f, 18f),
                      data.CategoryName + " ・ "
                      + (data.IsUnlocked ? "解除済み" : data.ConditionText), m_rowInfo);
            // レアリティは文字ではなく色で示す（右端の光沢つきの帯）
            GUI.DrawTexture(new Rect(row.xMax - 8f, row.y, 8f, row.height),
                            AchievementVisuals.Gradient(data.RarityColor), ScaleMode.StretchToFill);
            GUI.Label(new Rect(row.xMax - 110f, row.y + 6f, 100f, 18f), data.ProgressText, m_progressText);

            if (data.HasProgress)
            {
                DrawBar(new Rect(row.x + 40f, row.yMax - 9f, row.width - 60f, 6f), data.Ratio,
                        data.IsUnlocked ? new Color(0.35f, 0.72f, 0.25f) : CategoryColor(data.Category));
            }

            if (GUI.Button(row, GUIContent.none, GUIStyle.none))
            {
                m_selected = data;
                m_progressText2 = data.Progress.ToString();
            }
        }

        void DrawRight(Rect rect)
        {
            GUI.backgroundColor = new Color(1f, 0.98f, 0.93f);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = Color.white;

            GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f));
            m_detailScroll = GUILayout.BeginScrollView(m_detailScroll, false, true);

            DrawDetail();
            GUILayout.Space(8f);
            DrawTestActions();
            GUILayout.Space(8f);
            DrawNotificationTests();
            GUILayout.Space(8f);
            DrawSaveTests();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawDetail()
        {
            var data = m_selected;
            if (data == null)
            {
                GUILayout.Label("左の一覧から実績を選んでください", m_label);
                return;
            }

            GUILayout.Label("選択中の実績", m_section);
            GUILayout.Label(data.Name, m_title);
            GUILayout.Label("カテゴリー：" + data.CategoryName + "　ID：" + data.Id, m_small);
            GUILayout.BeginHorizontal();
            GUILayout.Label("レアリティ：", m_label, GUILayout.Width(84f));
            Rect swatch = GUILayoutUtility.GetRect(20f, 20f, GUILayout.Width(20f), GUILayout.Height(20f), GUILayout.ExpandWidth(false));
            GUI.DrawTexture(swatch, AchievementVisuals.Gradient(data.RarityColor), ScaleMode.StretchToFill);
            GUILayout.Label("（色だけ。ダイヤモンドだけ専用アイコン）", m_small);
            GUILayout.EndHorizontal();
            GUILayout.Label("　" + data.RarityName + "（" + data.RarityEnglishName + "）："
                            + AchievementRarityInfo.DescriptionOf(data.Rarity)
                            + (AchievementRarityInfo.IsHighest(data.Rarity) ? "　最高レアリティ" : ""), m_small);

            // ダイヤモンドの専用アイコン（通知と同じ絵）をここでも確かめられる
            Sprite rarityIcon = AchievementRarityIcons.IconOf(data.Rarity);
            if (rarityIcon != null)
            {
                GUILayout.Label("専用アイコン", m_small, GUILayout.Width(120f));
                Rect iconRect = GUILayoutUtility.GetRect(96f, 96f, GUILayout.Width(96f), GUILayout.Height(96f), GUILayout.ExpandWidth(false));
                GUI.DrawTexture(iconRect, rarityIcon.texture, ScaleMode.ScaleToFit, true);
            }
            GUILayout.Label("解除条件：" + data.ConditionText, m_label);
            GUILayout.Label("ポエム（メニューの「じっせき」で表示）：" + data.Poem, m_label);
            GUILayout.Space(4f);

            if (data.HasProgress)
            {
                Rect bar = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
                DrawBar(bar, data.Ratio, data.IsUnlocked ? new Color(0.35f, 0.72f, 0.25f) : CategoryColor(data.Category));
                GUI.Label(new Rect(bar.x, bar.y, bar.width, bar.height), "  " + data.ProgressText, m_label);
            }
            else
            {
                GUILayout.Label("状態：" + (data.IsUnlocked ? "解除済み" : "未解除"), m_label);
            }

            GUILayout.Label(data.IsUnlocked
                ? "解除日時：" + (string.IsNullOrEmpty(data.UnlockedText) ? "(不明)" : data.UnlockedText)
                : "まだ解除されていません", m_label);

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("この実績を解除", m_button)) UnlockSelected();
            if (GUILayout.Button("進捗 +1", m_button)) ChangeProgress(1);
            if (GUILayout.Button("進捗 -1", m_button)) ChangeProgress(-1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("進捗を直接入力", m_label, GUILayout.Width(120f));
            m_progressText2 = GUILayout.TextField(m_progressText2 ?? "0", m_field, GUILayout.Width(70f));
            if (GUILayout.Button("その値にする", m_button)) SetProgressFromField();
            if (GUILayout.Button("0 に戻す", m_button))
            {
                AchievementManager.DebugSetProgress(data, 0);
                m_progressText2 = "0";
                Flash(data.Name + " の進捗を 0 にしました（解除状態はそのまま）");
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("※ 進捗は条件の種類ごとに共有です（例：試合数を変えると「常連さん」も動きます）。進捗の直接変更では解除されません。", m_small);
        }

        void UnlockSelected()
        {
            if (m_selected == null) return;
            bool already = m_selected.IsUnlocked;
            AchievementManager.DebugUnlock(m_selected);
            Flash(already ? m_selected.Name + " はすでに解除済みです" : m_selected.Name + " を解除しました（通知も出ます）");
        }

        void ChangeProgress(int delta)
        {
            if (m_selected == null) return;
            int next = Mathf.Max(0, m_selected.Progress + delta);
            AchievementManager.DebugSetProgress(m_selected, next);
            m_progressText2 = next.ToString();
            Flash(m_selected.Name + " の進捗を " + next + " にしました");
        }

        void SetProgressFromField()
        {
            if (m_selected == null) return;
            if (!int.TryParse(m_progressText2, out int value)) { Flash("数字を入れてください"); return; }
            AchievementManager.DebugSetProgress(m_selected, value);
            Flash(m_selected.Name + " の進捗を " + value + " にしました");
        }

        //========================================
        // 解除条件のテスト
        //========================================

        void DrawTestActions()
        {
            GUILayout.Label("解除条件のテスト（ゲームと同じ入り口）", m_section);
            GUILayout.Label("どの操作も AchievementEvents を通ります。実際のゲームで起きるのと同じ手順です。", m_small);
            GUILayout.Space(2f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("試合開始（1試合ぶんをリセット）", m_button))
            {
                AchievementEvents.RaiseMatchStarted();
                Flash("試合開始を通知しました（コンボなどの1試合ぶんの進捗が 0 に戻ります）");
            }
            if (GUILayout.Button("勝利（積み木2個以上）", m_button))
            {
                AchievementEvents.RaiseMatchFinished(true, false);
                Flash("勝利を通知しました");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("崖っぷちの勝利（積み木1個）", m_button))
            {
                AchievementEvents.RaiseMatchFinished(true, true);
                Flash("積み木1個以下での勝利を通知しました");
            }
            if (GUILayout.Button("引き分け", m_button))
            {
                AchievementEvents.RaiseMatchFinished(false, false);
                Flash("引き分けを通知しました（連勝は維持されます）");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("攻撃を命中 +1", m_button)) { AchievementEvents.RaiseHit(1); Flash("命中を通知しました（累計と1試合中の両方に加算）"); }
            GUILayout.EndHorizontal();
            GUILayout.Label("※ 1回の命中は「累計」と「1試合中」の両方に数えます（ゲーム本編と同じ）。"
                            + "1試合中の数を戻すときは上の「試合開始」を押してください。", m_small);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("打ち返し成功 +1", m_button)) { AchievementEvents.RaiseCounter(); Flash("カウンターを通知しました"); }
            if (GUILayout.Button("打ち返しで撃破", m_button)) { AchievementEvents.RaiseCounterKO(); Flash("カウンター撃破を通知しました"); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("連鎖の数", m_label, GUILayout.Width(70f));
            m_chainText = GUILayout.TextField(m_chainText ?? "3", m_field, GUILayout.Width(60f));
            if (GUILayout.Button("この連鎖を発生", m_button)) RaiseNumber(AchievementEvents.RaiseChain, m_chainText, "連鎖");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("積み木の数", m_label, GUILayout.Width(70f));
            m_stackText = GUILayout.TextField(m_stackText ?? "5", m_field, GUILayout.Width(60f));
            if (GUILayout.Button("この数まで積む", m_button)) RaiseNumber(AchievementEvents.RaiseStack, m_stackText, "積み上げ");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("必殺技を発動", m_button)) { AchievementEvents.RaiseSpecial(); Flash("必殺技の発動を通知しました"); }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.Label("追加実績のテスト", m_section);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("敗北（CPU勝利）", m_button))
            {
                AchievementEvents.RaiseMatchFinished(false, false);
                AchievementEvents.RaiseMatchLost();
                Flash("敗北を通知しました");
            }
            if (GUILayout.Button("パッシブ発動", m_button)) { AchievementEvents.RaisePassiveActivated(); Flash("パッシブ発動を通知しました"); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("積み木3種類", m_button)) { AchievementEvents.RaiseBlockTypesInStack(3); Flash("積み木3種類を通知しました"); }
            if (GUILayout.Button("ラリー3", m_button)) { AchievementEvents.RaiseRally(3); Flash("ラリー3を通知しました"); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("漁夫の利", m_button)) { AchievementEvents.RaiseOpportunistKO(); Flash("漁夫の利を通知しました"); }
            if (GUILayout.Button("連鎖撃破", m_button)) { AchievementEvents.RaiseChainKO(); Flash("連鎖撃破を通知しました"); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("2人同時撃破", m_button)) { AchievementEvents.RaiseMultiKO(2); Flash("同時撃破2人を通知しました"); }
            if (GUILayout.Button("無傷勝利", m_button)) { AchievementEvents.RaiseNoDamageWin(); Flash("無傷勝利を通知しました"); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ハンマー空振り", m_button)) { AchievementEvents.RaiseHammerMiss(); Flash("ハンマー空振りを通知しました"); }
            if (GUILayout.Button("30秒以内勝利", m_button)) { AchievementEvents.RaiseQuickWin(); Flash("30秒以内勝利を通知しました"); }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.Label("使用キャラクターを記録（みんなの力）", m_label);
            var characters = m_lab != null ? m_lab.Characters : null;
            if (characters == null || characters.Count == 0)
            {
                GUILayout.Label("キャラクター一覧が未設定です（AchievementLab の参照を設定してください）", m_small);
            }
            else
            {
                int columns = 3;
                int i = 0;
                while (i < characters.Count)
                {
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < columns && i < characters.Count; c++, i++)
                    {
                        var character = characters[i];
                        string label = character != null && !string.IsNullOrEmpty(character.displayName)
                            ? character.displayName : "(名前なし)";
                        if (GUILayout.Button(label, m_button))
                        {
                            AchievementEvents.RaiseCharacterUsed(character != null ? character.id : "");
                            Flash(label + " を使ったことを記録しました");
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
        }

        void RaiseNumber(System.Action<int> raise, string text, string name)
        {
            if (!int.TryParse(text, out int value) || value <= 0)
            {
                Flash(name + "の数は1以上の数字で入れてください");
                return;
            }
            raise(value);
            Flash(name + " " + value + " を通知しました");
        }

        //========================================
        // 通知のテスト
        //========================================

        void DrawNotificationTests()
        {
            GUILayout.Label("解除通知のテスト", m_section);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("選択中の通知を出す", m_button))
            {
                if (m_selected != null) AchievementNotification.Preview(m_selected);
            }
            if (GUILayout.Button("3件を続けて出す", m_button)) PreviewThree();
            if (GUILayout.Button("まとめて2件解除", m_button)) UnlockTwo();
            if (GUILayout.Button("通知を止める", m_button)) AchievementNotification.Clear();
            GUILayout.EndHorizontal();

            GUILayout.Label("表示時間：" + m_duration.ToString("0.0") + " 秒（実際の通知にすぐ反映）", m_label);
            m_duration = GUILayout.HorizontalSlider(m_duration, 0.5f, 8f);
            AchievementNotification.Duration = m_duration;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("3.5秒に戻す", m_button)) m_duration = 3.5f;
            if (GUILayout.Button("効果音を鳴らす", m_button)) MenuSounds.Decide();
            GUILayout.EndHorizontal();
            GUILayout.Label("※ 「解除する」でも通知が出ます（解除済みの実績は出ません）。", m_small);

            // ---- リザルトで見せる実績（解除しても、ゲーム中の通知は出さない） ----
            GUILayout.Space(8f);
            GUILayout.Label("リザルトで見せる実績：" + AchievementManager.PendingCount + " 件（まだ見せていない解除）", m_label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("未表示を「見せた」ことにする", m_button))
            {
                int count = AchievementManager.PendingCount;
                AchievementManager.MarkAllNotified();
                Flash(count + " 件を表示済みにしました（次のリザルトには出ません）");
            }
            if (GUILayout.Button("未解除を3件解除して未表示にする", m_button)) UnlockThreeForResult();
            GUILayout.EndHorizontal();
            GUILayout.Label("※ リザルトのカードは試合のあとの ResultTowerScene で出ます。このラボでは出ません。", m_small);
        }

        // リザルトのカードを試す用：未解除を3件解除する（ラボの保存先に入る）
        void UnlockThreeForResult()
        {
            int count = 0;
            foreach (var data in AchievementManager.All)
            {
                if (data.IsUnlocked) continue;
                AchievementManager.DebugUnlock(data);
                count++;
                if (count >= 3) break;
            }
            Flash(count > 0 ? "未解除を " + count + " 件解除しました（未表示 " + AchievementManager.PendingCount + " 件）"
                            : "未解除の実績がありません");
        }

        void PreviewThree()
        {
            var all = AchievementManager.All;
            int shown = 0;
            foreach (var data in all)
            {
                AchievementNotification.Preview(data);
                shown++;
                if (shown >= 3) break;
            }
            Flash("通知を3件キューに入れました（順番に出ます）");
        }

        // 未解除の実績を2件まとめて解除する（同時解除の確認用）
        void UnlockTwo()
        {
            int count = 0;
            foreach (var data in AchievementManager.All)
            {
                if (data.IsUnlocked) continue;
                AchievementManager.DebugUnlock(data);
                count++;
                if (count >= 2) break;
            }
            Flash(count > 0 ? "未解除の実績を " + count + " 件まとめて解除しました（同時解除）"
                            : "未解除の実績がありません");
        }

        //========================================
        // セーブ・ロード
        //========================================

        void DrawSaveTests()
        {
            GUILayout.Label("セーブ・ロード", m_section);

            // ================= ラボのデータ =================
            GUILayout.Label("【ラボのデータ】" + AchievementManager.StorageKey
                            + "　保存：" + (AchievementManager.HasSave() ? "あり" : "なし"), m_label);
            GUILayout.Label("ラボで解除・変更したデータ。本編のデータとは別に保存されます。", m_small);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("いまの状態を保存", m_button)) { AchievementManager.Save(); Flash("ラボのデータを保存しました"); }
            if (GUILayout.Button("保存データを読み直す", m_button)) { AchievementManager.Load(); Flash("ラボの保存データを読み込みました"); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("全解除（通知は省略）", m_button)) UnlockAll();
            if (GUILayout.Button("ラボの解除をリセット", m_button))
            {
                AchievementManager.DebugResetUnlocked();
                Flash("ラボの解除状態をリセットしました（進捗は残ります）");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ラボの進捗をリセット", m_button))
            {
                AchievementManager.DebugResetProgress();
                Flash("ラボの進捗をリセットしました（解除状態は残ります）");
            }
            if (GUILayout.Button("ラボを全部初期化", m_button))
            {
                AchievementManager.DebugClearStorage(AchievementManager.DebugStorageKey);
                m_progressText2 = "0";
                Flash("ラボのデータを全部消しました（本編のデータはそのまま）");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            // 本編のデータ（全解除・リセット・初期化）は「実績ヒントの確認」に移した
            // （メニュー「Yurube > 実績ヒントの確認（デバッグ）」）
            GUILayout.Label("【本編のデータ】", m_label);
            GUILayout.Label("本編の全解除・リセット・初期化は、メニューの「Yurube > 実績ヒントの確認（デバッグ）」で行います。", m_small);

            GUILayout.Label("※ 保存したあと再生を止めて開始し直すと、解除状態が残っているか確認できます。", m_small);
        }

        void UnlockAll()
        {
            AchievementNotification.BeginSuppress();
            AchievementManager.DebugUnlockAll();
            AchievementNotification.EndSuppress();
            Flash("全実績を解除しました（通知は省略）");
        }
    }
}
#endif
