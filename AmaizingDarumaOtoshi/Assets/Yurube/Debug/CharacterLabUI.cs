using System.Collections;
using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube.Lab
{
    // ラボの操作パネル。キーボード中心なので、どのキーで何が起きるかを常に画面に出す。
    // マウスでも同じことができるようにボタンも並べる(色の追加・プリセット・各種切替)。
    public class CharacterLabUI : MonoBehaviour
    {
        CharacterLab m_lab;
        Font m_font;
        bool m_show = true;
        bool m_addToDummy;
        Vector2 m_scroll;
        int m_flying;
        GUIStyle m_title, m_section, m_label, m_button;
        bool m_stylesReady;
        Rect m_panelRect, m_helpRect;

        public void Initialize(CharacterLab lab)
        {
            m_lab = lab;
            // OnGUIが走る前の1フレームでも判定できるように、だいたいの位置を入れておく
            m_panelRect = new Rect(10.0f, 10.0f, 400.0f, Mathf.Clamp(Screen.height - 220.0f, 240.0f, 680.0f));
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
            if (m_lab != null && m_lab.Blocks != null) m_flying = m_lab.Blocks.CountFlying();
        }

        private void OnGUI()
        {
            if (!m_show || m_lab == null) return;
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
            m_stylesReady = true;
        }

        void DrawMainPanel()
        {
            float height = Mathf.Clamp(Screen.height - 220.0f, 240.0f, 680.0f);
            m_panelRect = new Rect(10.0f, 10.0f, 400.0f, height);
            GUILayout.BeginArea(m_panelRect, GUI.skin.box);
            m_scroll = GUILayout.BeginScrollView(m_scroll, false, true);

            GUILayout.Label("キャラクターラボ", m_title);
            GUILayout.Label("CPUなし。色付き積み木を自由に用意してパッシブを確認", m_label);
            GUILayout.Space(6.0f);

            // ---- キャラクター ----
            for (int i = 0; i < m_lab.Characters.Count; i++)
            {
                var data = m_lab.Characters[i];
                string name = data != null ? data.displayName : "?";
                string label = (i == m_lab.CurrentIndex ? "● " : "　") + (i + 1) + ". " + name;
                if (GUILayout.Button(label, m_button, GUILayout.Height(27.0f))) m_lab.SelectCharacter(i);
            }

            GUILayout.Space(6.0f);
            DrawStats();
            DrawSpecialPanel();

            // ---- 積み木 ----
            GUILayout.Space(6.0f);
            GUILayout.Label("積み木を追加", m_section);
            m_addToDummy = GUILayout.Toggle(m_addToDummy, "追加先をダミーにする");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("赤+1", m_button)) Add(TumikiColor.Red, 1);
            if (GUILayout.Button("青+1", m_button)) Add(TumikiColor.Blue, 1);
            if (GUILayout.Button("緑+1", m_button)) Add(TumikiColor.Green, 1);
            if (GUILayout.Button("黄+1", m_button)) Add(TumikiColor.Yellow, 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("赤10", m_button)) Add(TumikiColor.Red, 10);
            if (GUILayout.Button("青10", m_button)) Add(TumikiColor.Blue, 10);
            if (GUILayout.Button("緑10", m_button)) Add(TumikiColor.Green, 10);
            if (GUILayout.Button("黄10", m_button)) Add(TumikiColor.Yellow, 10);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("赤-1", m_button)) Remove(TumikiColor.Red);
            if (GUILayout.Button("青-1", m_button)) Remove(TumikiColor.Blue);
            if (GUILayout.Button("緑-1", m_button)) Remove(TumikiColor.Green);
            if (GUILayout.Button("黄-1", m_button)) Remove(TumikiColor.Yellow);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("選択先の積み木を全部消す", m_button)) ClearSelected();

            GUILayout.Space(4.0f);
            GUILayout.Label("選択色: " + ColorName(m_lab.SelectedColor), m_label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("赤", m_button)) m_lab.SelectedColor = TumikiColor.Red;
            if (GUILayout.Button("青", m_button)) m_lab.SelectedColor = TumikiColor.Blue;
            if (GUILayout.Button("緑", m_button)) m_lab.SelectedColor = TumikiColor.Green;
            if (GUILayout.Button("黄", m_button)) m_lab.SelectedColor = TumikiColor.Yellow;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("選択色をダミーへ発射 (V)", m_button)) m_lab.Launch(m_lab.SelectedColor, 20.0f, 0.0f);

            // ---- プリセット ----
            GUILayout.Space(6.0f);
            GUILayout.Label("プリセット", m_section);
            if (GUILayout.Button("あかだる: 赤10個", m_button)) PresetAkadaru();
            if (GUILayout.Button("あおだる: 青10個", m_button)) PresetAodaru();
            if (GUILayout.Button("みだる: 緑1個＋わざと外して発射", m_button)) PresetMidaru();
            if (GUILayout.Button("いわだる: ダミー5個＋発射", m_button)) PresetIwa();
            if (GUILayout.Button("きだる: 黄色1個", m_button)) PresetKidaru();
            if (GUILayout.Button("きだる: 必殺技", m_button)) PresetKidaruSpecial();
            if (GUILayout.Button("へびだる: 積んでショット", m_button)) PresetHebi();
            if (GUILayout.Button("だるきんぐ: 積み木3個", m_button)) PresetDaruKing();
            if (GUILayout.Button("あかだる: 必殺技", m_button)) PresetAkadaruSpecial();
            if (GUILayout.Button("あおだる: 必殺技", m_button)) PresetAodaruSpecial();
            if (GUILayout.Button("みだる: 必殺技", m_button)) PresetMidaruSpecial();

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
            if (GUILayout.Button("全部リセット(積み木も消す)", m_button)) m_lab.ResetAll();

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

            var stack = character.Stack;
            int count = stack != null ? stack.Count : 0;
            int red = stack != null ? stack.CountColor(TumikiColor.Red) : 0;
            int blue = stack != null ? stack.CountColor(TumikiColor.Blue) : 0;
            int green = stack != null ? stack.CountColor(TumikiColor.Green) : 0;
            int yellow = stack != null ? stack.CountColor(TumikiColor.Yellow) : 0;

            GUILayout.Label($"積み木: {count}段   赤{red} 青{blue} 緑{green} 黄{yellow}", m_label);

            float displaySpeed = character.MoveSpeed;
            var kidaru = character.GetComponent<KidaruSpecial>();
            if (kidaru != null) displaySpeed = kidaru.EffectiveSpeed;
            string speedSuffix = kidaru != null && kidaru.IsSpeedBoosted
                ? $" (必殺技 x{kidaru.CurrentSpeedMultiplier:0.0})"
                : "";
            GUILayout.Label($"弾速: {m_lab.ShotSpeed:0.0} m/s   移動速度: {displaySpeed:0.0} m/s{speedSuffix}", m_label);

            var aodaru = character.GetComponent<AodaruPassive>();
            if (aodaru != null)
                GUILayout.Label($"あおだる実効速度: {aodaru.EffectiveSpeed:0.0} m/s (青{aodaru.BlueCount}個)", m_label);

            GUILayout.Label($"接地: {(character.IsGrounded ? "はい" : "いいえ")}   ショット可: {(character.CanStartShot ? "はい" : "いいえ")}", m_label);
            if (m_lab.Dummy != null && m_lab.Dummy.Character != null)
                GUILayout.Label($"ダミー: {m_lab.Dummy.Character.Stack.Count}段 / 無敵 {(m_lab.DummyInvincible ? "ON" : "OFF")}", m_label);
            else
                GUILayout.Label("ダミー: 復活待ち...", m_label);
            GUILayout.Label($"飛んでいる積み木: {m_flying}", m_label);
        }

        void DrawSpecialPanel()
        {
            var character = m_lab.Character;
            if (character == null) return;
            var gauge = character.GetComponent<SpecialGauge>();
            if (gauge == null) return;

            GUILayout.Space(6.0f);
            GUILayout.Label("必殺技", m_section);
            GUILayout.Label($"ゲージ: {gauge.Blocks}/{gauge.BlocksToFill}" + (gauge.IsFull ? "  READY" : ""), m_label);
            if (GUILayout.Button("ゲージ満タン", m_button)) gauge.Fill();

            if (character.GetComponent<AkadaruSpecial>() != null)
            {
                bool showHitbox = TronHitboxView.Visible;
                bool newShowHitbox = GUILayout.Toggle(showHitbox, "当たり判定を表示 (F3)");
                if (newShowHitbox != showHitbox) TronHitboxView.Visible = newShowHitbox;
            }

            var special = character.GetComponent<ISpecialAbility>();
            if (special != null && GUILayout.Button("必殺技 (L/Y)", m_button)) special.TryActivate();

            var kidaru = character.GetComponent<KidaruSpecial>();
            if (kidaru != null)
            {
                string state = kidaru.IsActive ? $"強化中 残り{kidaru.RemainingSeconds:F1}秒" : "強化待機";
                GUILayout.Label($"{state}  前回: {kidaru.LastDuration:F1}秒 ({kidaru.LastBlocks}個)", m_label);
            }
        }

        void DrawHelp()
        {
            string[] lines =
            {
                "1-9: キャラ切替   R/G/B/Y: 積み木追加   Shift+R/G/B/Y: ダミーに追加   V: 選択色を発射",
                "WASD: 移動   Space: ジャンプ   J: 薙ぎ払い   K: ショット   L/Y: 必殺技",
                "右ドラッグ: 視点回転   ホイール/Z/X: ズーム   Q/E: 回転   F: 追従   T: スロー   P: 位置リセット   H: UI非表示   F3: 判定表示",
                "積み木の−1／全消し／ダミー復活は左パネルのボタン",
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

        void Add(TumikiColor color, int count)
        {
            m_lab.AddBlocks(Target(), color, count);
            m_lab.SelectedColor = color;
        }

        void Remove(TumikiColor color)
        {
            m_lab.RemoveBlocks(Target(), color);
            m_lab.SelectedColor = color;
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

        void PresetAkadaru()
        {
            m_lab.SelectCharacter("akadaru");
            var character = m_lab.Character;
            m_lab.ClearStack(character);
            m_lab.AddBlocks(character, TumikiColor.Red, 10);
            m_lab.SelectedColor = TumikiColor.Red;
        }

        void PresetAodaru()
        {
            m_lab.SelectCharacter("aodaru");
            var character = m_lab.Character;
            m_lab.ClearStack(character);
            m_lab.AddBlocks(character, TumikiColor.Blue, 10);
            m_lab.SelectedColor = TumikiColor.Blue;
        }

        void PresetMidaru()
        {
            m_lab.SelectCharacter("midaru");
            var character = m_lab.Character;
            m_lab.ClearStack(character);
            m_lab.AddBlocks(character, TumikiColor.Green, 1);
            m_lab.SelectedColor = TumikiColor.Green;
            // まっすぐ当てず、わざと25度外して曲がりを見る
            m_lab.Launch(TumikiColor.Green, 20.0f, 25.0f);
        }

        void PresetIwa()
        {
            m_lab.SelectCharacter("iwadaru");
            var character = m_lab.Character;
            var dummy = m_lab.Dummy != null ? m_lab.Dummy.Character : null;
            m_lab.ClearStack(character);
            m_lab.ClearStack(dummy);
            m_lab.AddBlocks(dummy, TumikiColor.Red, 5);
            m_lab.SelectedColor = TumikiColor.Red;
            m_lab.Launch(TumikiColor.Red, 20.0f, 0.0f);
        }

        void PresetKidaru()
        {
            m_lab.SelectCharacter("kidaru");
            var character = m_lab.Character;
            m_lab.ClearStack(character);
            // 黄色を1個積むと、パッシブでもう1個増えて2段になる
            m_lab.AddBlocks(character, TumikiColor.Yellow, 1);
            m_lab.SelectedColor = TumikiColor.Yellow;
        }

        void PresetKidaruSpecial()
        {
            m_lab.SelectCharacter("kidaru");
            var character = m_lab.Character;
            m_lab.ClearStack(character);
            // 5個積んでゲージを満タンにし、そのまま発動
            m_lab.AddBlocks(character, TumikiColor.Yellow, 5);
            m_lab.SelectedColor = TumikiColor.Yellow;

            var special = character != null ? character.GetComponent<ISpecialAbility>() : null;
            if (special != null) special.TryActivate();
        }

        void PresetHebi()
        {
            m_lab.SelectCharacter("hebidaru");
            var character = m_lab.Character;
            m_lab.ClearStack(character);
            m_lab.AddBlocks(character, TumikiColor.Green, 1);
            m_lab.SelectedColor = TumikiColor.Green;
            // 生成直後は接地判定がまだなので、動けるようになってからショットする
            StartCoroutine(ShotWhenReady());
        }

        IEnumerator ShotWhenReady()
        {
            var character = m_lab.Character;
            float timeout = 1.0f;
            while (character != null && !character.CanStartShot && timeout > 0.0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }
            if (character != null && character.Hammer != null) character.Hammer.TryShot();
        }

        void PresetDaruKing()
        {
            m_lab.SelectCharacter("daruking");
            var character = m_lab.Character;
            m_lab.ClearStack(character);
            // 3個積もうとしても、パッシブで2段までに抑えられる
            m_lab.AddBlocks(character, TumikiColor.Red, 3);
            m_lab.SelectedColor = TumikiColor.Red;
        }

        void PresetAkadaruSpecial()
        {
            m_lab.SelectCharacter("akadaru");
            var character = m_lab.Character;
            m_lab.ClearStack(character);
            // 5個積むとゲージが満タンになる
            m_lab.AddBlocks(character, TumikiColor.Red, 5);
            m_lab.SelectedColor = TumikiColor.Red;

            var special = character != null ? character.GetComponent<AkadaruSpecial>() : null;
            if (special != null) special.TryActivate();
        }

        void PresetAodaruSpecial()
        {
            m_lab.SelectCharacter("aodaru");
            var character = m_lab.Character;
            m_lab.ClearStack(character);
            // 5個積むとゲージが満タンになる
            m_lab.AddBlocks(character, TumikiColor.Blue, 5);
            m_lab.SelectedColor = TumikiColor.Blue;

            var special = character != null ? character.GetComponent<ISpecialAbility>() : null;
            if (special != null) special.TryActivate();
        }

        void PresetMidaruSpecial()
        {
            m_lab.SelectCharacter("midaru");
            var character = m_lab.Character;
            m_lab.ClearStack(character);
            // 5個積むとゲージが満タン。押した瞬間に撃って5発に分裂する
            m_lab.AddBlocks(character, TumikiColor.Red, 5);
            m_lab.SelectedColor = TumikiColor.Red;

            var special = character != null ? character.GetComponent<ISpecialAbility>() : null;
            if (special != null) special.TryActivate();
        }

        static string ColorName(TumikiColor color)
        {
            switch (color)
            {
                case TumikiColor.Red: return "赤";
                case TumikiColor.Blue: return "青";
                case TumikiColor.Green: return "緑";
                case TumikiColor.Yellow: return "黄";
                default: return "?";
            }
        }
    }
}
