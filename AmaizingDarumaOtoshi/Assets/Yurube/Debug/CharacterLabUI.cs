using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube.Lab
{
    // ラボの操作パネル。現行仕様の必殺技・レベル・ダッシュ・積み木・F3 当たり判定可視化を操作/表示する。
    public class CharacterLabUI : MonoBehaviour
    {
        CharacterLab m_lab;
        Font m_font;
        bool m_show = true;
        bool m_addToDummy;
        Vector2 m_scroll;
        int m_flying;
        GUIStyle m_title, m_section, m_label, m_button, m_guideStyle, m_warnStyle;
        bool m_stylesReady;
        Rect m_panelRect, m_helpRect;

        public void Initialize(CharacterLab lab)
        {
            m_lab = lab;
            // OnGUIが走る前の1フレームでも判定できるように、だいたいの位置を入れておく
            m_panelRect = new Rect(10.0f, 10.0f, 430.0f, Mathf.Clamp(Screen.height - 150.0f, 260.0f, 760.0f));
            m_helpRect = new Rect(10.0f, Screen.height - 120.0f, Mathf.Max(200.0f, Screen.width - 40.0f) + 20.0f, 110.0f);
        }

        // マウスがUIの上にあるか。カメラのズーム・回転を止めるために使う
        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (!m_show) return false;
            // InputSystem は左下原点、IMGUI は左上原点なので変換する
            Vector2 gui = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return m_panelRect.Contains(gui) || m_helpRect.Contains(gui);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame) m_show = !m_show;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame && m_lab != null && m_lab.Character != null)
            {
                // SpecialHitboxDebug.Toggle は1フレーム1回だけ反転するため、AkadaruSpecial 等と競合しない
                SpecialHitboxDebug.Toggle();
            }
            if (m_lab != null && m_lab.Blocks != null) m_flying = m_lab.Blocks.CountFlying();
        }

        private void OnGUI()
        {
            if (!m_show || m_lab == null) return;
            // カットイン中はラボUIも描かない（必殺技ゲージ等が前面に残らないようにする）
            if (IsCutInPlaying()) return;
            EnsureStyles();
            DrawMainPanel();
            DrawHelp();
        }

        void EnsureStyles()
        {
            if (m_stylesReady) return;
            if (m_font == null)
            {
                // IMGUIの既定フォントでは日本語が出ないことがあるため、OSのフォントを使う
                m_font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo", "MS Gothic" }, 16);
                if (m_font == null) m_font = GUI.skin.font;
            }
            m_title = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 20, fontStyle = FontStyle.Bold };
            m_section = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 15, fontStyle = FontStyle.Bold };
            m_label = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 14 };
            m_button = new GUIStyle(GUI.skin.button) { font = m_font, fontSize = 14 };
            m_guideStyle = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 14, fontStyle = FontStyle.Bold };
            m_guideStyle.normal.textColor = new Color(0.45f, 0.95f, 1.0f);
            m_warnStyle = new GUIStyle(GUI.skin.label) { font = m_font, fontSize = 14, fontStyle = FontStyle.Bold };
            m_warnStyle.normal.textColor = new Color(1.0f, 0.78f, 0.25f);
            m_stylesReady = true;
        }

        void DrawMainPanel()
        {
            float height = Mathf.Clamp(Screen.height - 150.0f, 260.0f, 760.0f);
            m_panelRect = new Rect(10.0f, 10.0f, 430.0f, height);
            GUILayout.BeginArea(m_panelRect, GUI.skin.box);
            m_scroll = GUILayout.BeginScrollView(m_scroll, false, true);

            GUILayout.Label("キャラクターラボ", m_title);
            GUILayout.Label("CPUなし。積み木・レベル・ダッシュ・必殺技をその場で確認", m_label);
            GUILayout.Space(6.0f);

            // ---- キャラクター ----
            for (int i = 0; i < m_lab.Characters.Count; i++)
            {
                var data = m_lab.Characters[i];
                string name = data != null ? data.displayName : "?";
                string label = (i == m_lab.CurrentIndex ? "● " : "　") + (i + 1) + ". " + name;
                if (GUILayout.Button(label, m_button, GUILayout.Height(26.0f))) m_lab.SelectCharacter(i);
            }

            GUILayout.Space(6.0f);
            DrawStats();
            DrawLevelPanel();
            DrawSpecialPanel();

            // ---- 積み木 ----
            GUILayout.Space(6.0f);
            GUILayout.Label("積み木操作", m_section);
            m_addToDummy = GUILayout.Toggle(m_addToDummy, "追加先をダミーにする");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1", m_button)) Add(1);
            if (GUILayout.Button("+5", m_button)) Add(5);
            if (GUILayout.Button("+10", m_button)) Add(10);
            if (GUILayout.Button("-1", m_button)) RemoveOne();
            if (GUILayout.Button("全消し", m_button)) ClearSelected();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("積み木をダミーへ発射 (V)", m_button)) m_lab.Launch(TumikiColor.Red, 20.0f, 0.0f);

            // ---- プリセット ----
            GUILayout.Space(6.0f);
            GUILayout.Label("必殺技プリセット（ダミー積載＋無敵OFF）", m_section);
            if (GUILayout.Button("あかだる: 紅蓮乱舞", m_button)) RunSpecialPreset("akadaru", 0, 5);
            if (GUILayout.Button("あおだる: 疾風連撃", m_button)) RunSpecialPreset("aodaru", 0, 5);
            if (GUILayout.Button("みだる: マシンガン", m_button)) RunSpecialPreset("midaru", 0, 5);
            if (GUILayout.Button("きだる: 震脚", m_button)) RunSpecialPreset("kidaru", 0, 5);
            if (GUILayout.Button("いわだる: いわだる崩し", m_button)) RunSpecialPreset("iwadaru", 5, 5);
            if (GUILayout.Button("へびだる: 反転毒沼", m_button)) RunSpecialPreset("hebidaru", 5, 5);
            if (GUILayout.Button("だるきんぐ: エクスカリバー", m_button)) RunSpecialPreset("daruking", 0, 5);

            GUILayout.Space(4.0f);
            GUILayout.Label("レベル・ダミー補助", m_section);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("現在キャラを Lv5 に", m_button)) m_lab.SetPlayerLevel(LevelParams.MaxLevel);
            if (GUILayout.Button("Lv1 に戻す", m_button)) m_lab.SetPlayerLevel(1);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("ダミーに積み木5個＋無敵OFF", m_button)) PrepareDummy(5);

            // ---- 切替 ----
            GUILayout.Space(6.0f);
            GUILayout.Label("表示・カメラ", m_section);
            bool invincible = GUILayout.Toggle(m_lab.DummyInvincible, "ダミー無敵 (I)");
            if (invincible != m_lab.DummyInvincible) m_lab.SetDummyInvincible(invincible);
            if (GUILayout.Button("ダミーを復活", m_button)) m_lab.RespawnDummyNow();
            bool slow = GUILayout.Toggle(m_lab.SlowMotion, "スローモーション (T)");
            if (slow != m_lab.SlowMotion) m_lab.ToggleSlow();
            m_lab.Follow = GUILayout.Toggle(m_lab.Follow, "カメラ追従 (F)");

            GUILayout.Space(6.0f);
            if (GUILayout.Button("位置リセット (P)", m_button)) m_lab.ResetPositions();
            if (GUILayout.Button("全部リセット（初期化）", m_button)) m_lab.ResetAll();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawStats()
        {
            var character = m_lab.Character;
            if (character == null)
            {
                GUILayout.Label("キャラクターがいません(CharacterData の prefab を確認)", m_label);
                return;
            }

            var data = m_lab.Current;
            int level = m_lab.PlayerLevel;
            LevelPerk perk = data != null ? data.levelPerk : LevelPerk.None;
            float weaponReach = data != null ? data.weaponReach : 1.0f;
            DarumaLevel.ComputeMultipliers(level, perk, weaponReach,
                out float attackMul, out _, out float moveMul, out float reachMul, out _);

            var stack = character.Stack;
            int count = stack != null ? stack.Count : 0;
            GUILayout.Label($"積み木: {count}段   接地: {(character.IsGrounded ? "はい" : "いいえ")}   ショット可: {(character.CanStartShot ? "はい" : "いいえ")}", m_label);

            DashParams dp = DashParams.Instance;
            float dashMul = character.IsDashing ? dp.SpeedMultiplier : 1.0f;
            float actualMoveSpeed = character.MoveSpeed * moveMul * dashMul * Mathf.Max(0.0f, character.MoveSpeedMultiplier);
            string dashTag = character.IsDashing ? $" [ダッシュ x{dp.SpeedMultiplier:0.0}]" : "";
            GUILayout.Label($"移動速度: {actualMoveSpeed:0.0} m/s (基本{character.MoveSpeed:0.0} × Lv{moveMul:0.00}){dashTag}", m_label);

            float staminaRatio = Mathf.Clamp01(character.StaminaRatio);
            int staminaPct = Mathf.RoundToInt(staminaRatio * 100.0f);
            string staminaNote = staminaRatio < dp.JumpCostRatio ? " [ジャンプ不可]" : "";
            string effNote = character.StaminaEfficiency > 1.001f ? $"  ジャンプ消費 ÷{character.StaminaEfficiency:0.00}" : "";
            GUILayout.Label($"スタミナ: {staminaPct}%{staminaNote}{effNote}", m_label);
            DrawProgressBar(staminaRatio, staminaRatio < dp.JumpCostRatio
                ? new Color(0.95f, 0.35f, 0.25f)
                : new Color(0.35f, 0.90f, 0.45f));

            GUILayout.Label($"弾速: {m_lab.ShotSpeed:0.0} m/s   攻撃速度: ×{attackMul:0.00}   リーチ: ×{reachMul:0.00}", m_label);

            if (m_lab.Dummy != null && m_lab.Dummy.Character != null)
            {
                var dc = m_lab.Dummy.Character;
                string debuff = "";
                if (DarumaLevel.IsLabSlowed(dc)) debuff += $" [スロウ×{DarumaLevel.GetLabSlowScale(dc):0.00}]";
                if (dc.MovementInverted) debuff += " [操作反転]";
                GUILayout.Label($"ダミー: {dc.Stack.Count}段 / 無敵 {(m_lab.DummyInvincible ? "ON" : "OFF")}{debuff}", m_label);
            }
            else
            {
                GUILayout.Label("ダミー: 復活待ち...", m_label);
            }
            GUILayout.Label($"飛んでいる積み木: {m_flying}", m_label);
        }

        void DrawLevelPanel()
        {
            var character = m_lab.Character;
            if (character == null) return;

            var data = m_lab.Current;
            int level = m_lab.PlayerLevel;
            int stacked = DarumaLevel.GetLabStacked(character);

            GUILayout.Space(6.0f);
            GUILayout.Label($"レベル（Lv{level} / {LevelParams.MaxLevel}   累計積載: {stacked}）", m_section);

            GUILayout.BeginHorizontal();
            for (int lv = 1; lv <= LevelParams.MaxLevel; lv++)
            {
                string label = lv == level ? $"[Lv{lv}]" : $"Lv{lv}";
                if (GUILayout.Button(label, m_button)) m_lab.SetPlayerLevel(lv);
            }
            GUILayout.EndHorizontal();

            if (data != null && !string.IsNullOrEmpty(data.passiveName))
            {
                string activeTag = level >= 2 ? "発動中" : "Lv2で解放";
                GUILayout.Label($"固有能力: {data.passiveName} ({activeTag})", m_label);
                if (!string.IsNullOrEmpty(data.passiveText)) GUILayout.Label($"  {data.passiveText}", m_label);
            }
        }

        void DrawSpecialPanel()
        {
            var character = m_lab.Character;
            if (character == null) return;
            var gauge = character.GetComponent<SpecialGauge>();
            if (gauge == null) return;

            GUILayout.Space(6.0f);
            GUILayout.Label("必殺技", m_section);
            GUILayout.Label($"ゲージ: {gauge.Percent}%" + (gauge.IsFull ? "  READY" : ""), m_label);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ゲージ満タン", m_button)) gauge.Fill();
            var special = character.GetComponent<ISpecialAbility>();
            if (special != null && GUILayout.Button("必殺技 (L/Y)", m_button)) special.TryActivate();
            GUILayout.EndHorizontal();

            bool showHitbox = SpecialHitboxDebug.Visible;
            bool newShowHitbox = GUILayout.Toggle(showHitbox, "当たり判定を表示 (F3)");
            if (newShowHitbox != showHitbox) SpecialHitboxDebug.Visible = newShowHitbox;

            DrawCharacterSpecialStatus(character);
        }

        void DrawCharacterSpecialStatus(DarumaCharacter character)
        {
            if (character.GetComponent<AkadaruSpecial>() is AkadaruSpecial aka)
            {
                if (aka.IsActive)
                {
                    float rem = aka.RemainingSeconds;
                    GUILayout.Label($"紅蓮乱舞 発動中: 残り {rem:0.0}s", m_guideStyle);
                    DrawProgressBar(rem / Mathf.Max(0.1f, aka.StormDuration + 0.25f), new Color(0.95f, 0.35f, 0.25f));
                }
                else
                {
                    GUILayout.Label("紅蓮乱舞: 周囲に巨大ハンマー5本を回転（5.0秒・積み木不要）", m_label);
                }
                return;
            }

            if (character.GetComponent<AodaruSpecial>() is AodaruSpecial ao)
            {
                if (ao.IsActive)
                {
                    float rem = ao.RemainingSeconds;
                    GUILayout.Label($"疾風連撃 強化中: 残り {rem:0.0}s", m_guideStyle);
                    DrawProgressBar(rem / Mathf.Max(0.1f, ao.GaleDuration), new Color(0.35f, 0.85f, 1.0f));
                    GUILayout.Label("→ J キーで薙ぎ払いすると前方へ斬撃が飛びます！", m_warnStyle);
                }
                else
                {
                    GUILayout.Label("疾風連撃: 発動後5秒間、薙ぎ払い(J)で前方へ斬撃", m_label);
                }
                return;
            }

            if (character.GetComponent<MidaruSpecial>() is MidaruSpecial mi)
            {
                if (mi.IsActive)
                {
                    float rem = mi.RemainingSeconds;
                    GUILayout.Label($"マシンガン 稼働中: 残り {rem:0.0}s", m_guideStyle);
                    DrawProgressBar(rem / Mathf.Max(0.1f, mi.MachineGunDuration + 0.25f), new Color(0.35f, 0.88f, 0.45f));
                }
                else
                {
                    GUILayout.Label("マシンガン: 自動追尾の固定砲台を設置（6.0秒・積み木不要）", m_label);
                }
                return;
            }

            if (character.GetComponent<KidaruSpecial>() is KidaruSpecial ki)
            {
                if (ki.IsActive)
                {
                    float rem = ki.RemainingSeconds;
                    float total = Mathf.Max(0.1f, ki.LastDuration);
                    GUILayout.Label($"震脚 強化中: 残り {rem:0.0}s (衝撃波半径 {ki.QuakeRadius:0.0}m)", m_guideStyle);
                    DrawProgressBar(rem / total, new Color(1.0f, 0.82f, 0.22f));
                    GUILayout.Label("→ Space キーでジャンプ着地すると周囲に衝撃波が出ます！", m_warnStyle);
                }
                else
                {
                    GUILayout.Label($"震脚: 発動後5秒間、ジャンプ着地ごとに衝撃波(半径{ki.QuakeRadius:0.0}m)", m_label);
                }
                return;
            }

            if (character.GetComponent<IwadaruSpecial>() is IwadaruSpecial iwa)
            {
                if (iwa.IsActive)
                {
                    float rem = iwa.PhaseRemaining;
                    string remText = rem > 0.0f ? $" 残り {rem:0.0}s" : "";
                    GUILayout.Label($"いわだる崩し [{iwa.PhaseLabel}]{remText} (半径 {iwa.AttackRadius:0.0}m)", m_guideStyle);
                }
                else
                {
                    GUILayout.Label("いわだる崩し: 積み木1個以上で発動。段数で範囲拡大・着弾即死", m_label);
                }
                return;
            }

            if (character.GetComponent<HebidaruSpecial>() is HebidaruSpecial hebi)
            {
                if (hebi.IsActive)
                {
                    string text = hebi.IsInCutIn
                        ? "反転毒沼: カットイン中..."
                        : $"反転毒沼 展開中: 残り {hebi.RemainingSeconds:0.0}s (半径 {hebi.ActiveSwampRadius:0.0}m)";
                    GUILayout.Label(text, m_guideStyle);
                }
                else
                {
                    GUILayout.Label("反転毒沼: 周囲に毒沼を展開（8.0秒・移動低下＋反転）", m_label);
                }
                return;
            }

            if (character.GetComponent<DaruKingSpecial>() is DaruKingSpecial king)
            {
                if (king.IsActive)
                {
                    float rem = king.PhaseRemaining;
                    string remText = rem > 0.0f ? $" 残り {rem:0.0}s" : "";
                    GUILayout.Label($"エクスカリバー [{king.PhaseLabel}]{remText}", m_guideStyle);
                    if (king.IsCharging)
                        GUILayout.Label("→ WASDで向き調整 / 行動ボタン(J/K/Space等)で即確定", m_warnStyle);
                }
                else
                {
                    GUILayout.Label("エクスカリバー: 2.0秒詠唱（移動で向き調整）→ 直線即死", m_label);
                }
            }
        }

        static void DrawProgressBar(float normalized, Color fillColor)
        {
            Rect rect = GUILayoutUtility.GetRect(10.0f, 10.0f, GUILayout.ExpandWidth(true));
            Color prev = GUI.color;
            GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.65f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(rect.x + 1.0f, rect.y + 1.0f,
                Mathf.Max(0.0f, (rect.width - 2.0f) * Mathf.Clamp01(normalized)), rect.height - 2.0f),
                Texture2D.whiteTexture);
            GUI.color = prev;
        }

        void DrawHelp()
        {
            string[] lines =
            {
                "1-7: キャラ切替   R/G/B/Y: 積み木+1   右Shift+R/G/B/Y: ダミー+1   V: 積み木発射",
                "WASD: 移動   左Shift(長押し): ダッシュ   左Shift(2回): ブリンク   Space: ジャンプ",
                "J: 薙ぎ払い   K: ショット   L/Y: 必殺技   F3: 当たり判定表示",
                "右ドラッグ: 回転   ホイール/Z/X: ズーム   Q/E: 回転   F: 追従   T: スロー   P: 位置リセット   H: UI非表示",
            };
            float width = Mathf.Max(200.0f, Screen.width - 40.0f);
            float height = 12.0f;
            foreach (var line in lines) height += m_label.CalcHeight(new GUIContent(line), width);
            height += 8.0f;
            m_helpRect = new Rect(10.0f, Screen.height - height - 10.0f, width + 20.0f, height);
            GUILayout.BeginArea(m_helpRect, GUI.skin.box);
            foreach (var line in lines) GUILayout.Label(line, m_label);
            GUILayout.EndArea();
        }

        void Add(int count)
        {
            m_lab.AddBlocks(Target(), TumikiColor.Red, count);
        }

        void RemoveOne()
        {
            var target = Target();
            if (target == null || target.Stack == null || target.Stack.Count == 0) return;
            var block = target.Stack.Get(target.Stack.Count - 1);
            if (block != null) m_lab.RemoveBlocks(target, block.Color);
        }

        void ClearSelected()
        {
            m_lab.ClearStack(Target());
        }

        DarumaCharacter Target()
        {
            if (m_addToDummy) return m_lab.Dummy != null ? m_lab.Dummy.Character : null;
            return m_lab.Character;
        }

        void PrepareDummy(int dummyBlocks)
        {
            m_lab.EnsureDummyAlive();
            var dummy = m_lab.Dummy != null ? m_lab.Dummy.Character : null;
            m_lab.ClearStack(dummy);
            if (dummyBlocks > 0) m_lab.AddBlocks(dummy, TumikiColor.Red, dummyBlocks);
            m_lab.SetDummyInvincible(false);
        }

        void RunSpecialPreset(string id, int playerBlocks, int dummyBlocks)
        {
            if (!CanStartSpecialPreset()) return;

            m_lab.CleanupSpecialObjects();
            m_lab.SelectCharacter(id);
            m_lab.EnsureDummyAlive();
            if (m_lab.Blocks != null) m_lab.Blocks.DestroyLoose();
            m_lab.ResetPositions();

            var character = m_lab.Character;
            m_lab.ClearStack(character);
            if (playerBlocks > 0) m_lab.AddBlocks(character, TumikiColor.Red, playerBlocks);

            PrepareDummy(dummyBlocks);

            if (character == null) return;
            var gauge = character.GetComponent<SpecialGauge>();
            if (gauge != null) gauge.Fill();

            var special = character.GetComponent<ISpecialAbility>();
            if (special != null) special.TryActivate();
        }

        bool CanStartSpecialPreset()
        {
            if (IsCutInPlaying()) return false;
            var current = m_lab.Character;
            if (current == null) return true;
            var special = current.GetComponent<ISpecialAbility>();
            return special == null || !special.IsActive;
        }

        static bool IsCutInPlaying()
        {
            return SpecialCutIn.IsPlaying || IwadaruCutIn.IsPlaying
                || HebidaruCutIn.IsPlaying || DaruKingCutIn.IsPlaying
                || AodaruCutIn.IsPlaying || MidaruCutIn.IsPlaying
                || KidaruCutIn.IsPlaying;
        }
    }
}
