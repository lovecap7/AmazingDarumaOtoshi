using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nakagawa
{
    // 再生中に、シーン遷移の積み木の演出（TumikiRollTransition）の調整値を画面上で変える（デバッグ用）
    //  ・キー（既定 F6）でパネルを開閉する。どのシーンでも使える（起動時に自動で作られ、シーンをまたいで残る）
    //  ・値はすぐ反映される（大きさ・向き・間隔は次に転がし始めるとき、厚み・色はメッシュを作り直して反映）
    //  ・「試す」で、シーンを切り替えずに選んだ組み合わせの演出だけを流す（Fader.PreviewTransition）
    //  ・値は再生を止めると元に戻る。「コードをコピー」で今の値を C# のコードとしてクリップボードとログに出すので、
    //    TumikiRollTransition の初期値に貼り付けて残す
    // エディタと Development Build のときだけ作られる
    public class TumikiRollTransitionTuner : MonoBehaviour
    {
        [SerializeField] Key m_toggleKey = Key.F6;
        [SerializeField] bool m_open = false;

        Rect m_window = new Rect(10, 100, 380, 0);
        // パネルのウィンドウの番号（他の OnGUI のウィンドウと重ならない値）
        const int kWindowId = 0x7A11;

        int m_routeIndex;
        float m_previewHold = 0.5f;
        bool m_showColors;
        Vector2 m_colorScroll;
        string m_message = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (!Debug.isDebugBuild) return;   // エディタと Development Build だけ
            if (FindAnyObjectByType<TumikiRollTransitionTuner>() != null) return;
            var go = new GameObject("TumikiRollTransitionTuner");
            DontDestroyOnLoad(go);
            go.AddComponent<TumikiRollTransitionTuner>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[m_toggleKey].wasPressedThisFrame) m_open = !m_open;
        }

        private void OnGUI()
        {
            if (!m_open) return;
            m_window = GUILayout.Window(kWindowId, m_window, DrawWindow, $"積み木の遷移演出  [{m_toggleKey}で閉じる]");
        }

        void DrawWindow(int id)
        {
            var t = TumikiRollTransition.Current;
            if (t == null)
            {
                GUILayout.Label("TumikiRollTransition が Fader に入っていません");
                GUI.DragWindow();
                return;
            }

            GUILayout.Label(t.IsPlaying ? "転がっています" : Fader.IsBusy ? "遷移中です" : "待機中");

            GUILayout.Label("― 大きさ・並び（次に転がし始めるときに反映） ―");
            Slider("直径(画面比)", ref t.diameterPerScreen, 0.05f, 1.0f, "0.00");
            Slider("横の間隔", ref t.spacingX, 0.1f, 2.0f, "0.00");
            Slider("回す角度", ref t.yaw, 0.0f, 80.0f, "0.0");
            if (Slider("厚み", ref t.thickness, 0.05f, 1.5f, "0.00")) t.MarkMeshesDirty();

            GUILayout.Label("― 速さ（画面の縦幅 / 秒）・時間（秒） ―");
            Slider("速い速さ", ref t.speedFast, 0.2f, 10.0f, "0.00");
            Slider("埋まった間の速さ", ref t.speedSlow, 0.0f, 3.0f, "0.00");
            Slider("切り替え時間", ref t.speedEase, 0.0f, 1.0f, "0.00");
            Slider("止まる最短時間", ref t.minHold, 0.0f, 2.0f, "0.00");

            DrawColors(t);

            GUILayout.Label("― 試す（シーンは切り替えない） ―");
            var routes = t.routes;
            if (routes.Count > 0)
            {
                m_routeIndex = Mathf.Clamp(m_routeIndex, 0, routes.Count - 1);
                var labels = new string[routes.Count];
                for (int i = 0; i < routes.Count; i++)
                    labels[i] = $"{Short(routes[i].from)}→{Short(routes[i].to)} {(routes[i].dir >= 0 ? "▶" : "◀")}";
                m_routeIndex = GUILayout.SelectionGrid(m_routeIndex, labels, 2);
                Slider("埋めておく時間", ref m_previewHold, 0.0f, 3.0f, "0.00");
                GUI.enabled = !Fader.IsBusy;
                if (GUILayout.Button("試す"))
                {
                    var r = routes[m_routeIndex];
                    m_message = Fader.PreviewTransition(r.from, r.to, m_previewHold) ? "" : "今は試せません";
                }
                GUI.enabled = true;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("既定値に戻す"))
            {
                t.CopyTuning(new TumikiRollTransition());
                m_message = "既定値に戻しました";
            }
            if (GUILayout.Button("コードをコピー"))
            {
                string code = ToCode(t);
                GUIUtility.systemCopyBuffer = code;
                Debug.Log("[TumikiRollTransitionTuner] 今の調整値\n" + code);
                m_message = "クリップボードとログに出しました";
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(m_message)) GUILayout.Label(m_message);
            GUI.DragWindow();
        }

        // 段の色（上から）。段の数は色の数
        void DrawColors(TumikiRollTransition t)
        {
            m_showColors = GUILayout.Toggle(m_showColors, $" 段の色（{t.rowColors.Length} 段）を開く");
            if (!m_showColors) return;

            bool changed = false;
            m_colorScroll = GUILayout.BeginScrollView(m_colorScroll, GUILayout.Height(220));
            for (int i = 0; i < t.rowColors.Length; i++)
            {
                var c = t.rowColors[i];
                GUILayout.BeginHorizontal();
                var prev = GUI.color;
                GUI.color = new Color(c.r, c.g, c.b, 1f);
                GUILayout.Label("■", GUILayout.Width(16));
                GUI.color = prev;
                GUILayout.Label($"{i + 1}段目", GUILayout.Width(50));
                GUILayout.EndHorizontal();
                changed |= Slider("  R", ref c.r, 0f, 1f, "0.00");
                changed |= Slider("  G", ref c.g, 0f, 1f, "0.00");
                changed |= Slider("  B", ref c.b, 0f, 1f, "0.00");
                t.rowColors[i] = c;
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("段を増やす"))
            {
                var list = new System.Collections.Generic.List<Color>(t.rowColors);
                list.Add(list.Count > 0 ? list[list.Count - 1] : Color.white);
                t.rowColors = list.ToArray();
                changed = true;
            }
            GUI.enabled = t.rowColors.Length > 1;
            if (GUILayout.Button("段を減らす"))
            {
                var list = new System.Collections.Generic.List<Color>(t.rowColors);
                list.RemoveAt(list.Count - 1);
                t.rowColors = list.ToArray();
                changed = true;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (changed) t.MarkMeshesDirty();
        }

        // ラベル・値・スライダーの1行。値が変わったら true
        static bool Slider(string label, ref float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            GUILayout.Label(value.ToString(format), GUILayout.Width(45));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            if (Mathf.Approximately(v, value)) return false;
            value = v;
            return true;
        }

        static string Short(string scene) => scene.EndsWith("Scene") ? scene.Substring(0, scene.Length - 5) : scene;

        // TumikiRollTransition の初期値にそのまま貼れる形
        static string ToCode(TumikiRollTransition t)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"public float diameterPerScreen = {F(t.diameterPerScreen)};");
            sb.AppendLine($"public float thickness = {F(t.thickness)};");
            sb.AppendLine($"public float yaw = {F(t.yaw)};");
            sb.AppendLine($"public float spacingX = {F(t.spacingX)};");
            sb.AppendLine($"public float speedFast = {F(t.speedFast)};");
            sb.AppendLine($"public float speedSlow = {F(t.speedSlow)};");
            sb.AppendLine($"public float speedEase = {F(t.speedEase)};");
            sb.AppendLine($"public float minHold = {F(t.minHold)};");
            sb.AppendLine("public Color[] rowColors =");
            sb.AppendLine("{");
            foreach (var c in t.rowColors)
                sb.AppendLine($"    new Color({F(c.r)}, {F(c.g)}, {F(c.b)}),");
            sb.AppendLine("};");
            return sb.ToString();
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture) + "f";
    }
}
