using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nakagawa
{
    // 再生中に、表示しているステージのカメラの位置・角度・視野角を画面上で調整し、ステージごとに保存する(デバッグ用)
    //  ・キー(既定 F2)でパネルを開閉する。床の注視点を中心に、見下ろす角度・左右の向き・距離・注視点・視野角を変える
    //  ・「保存」で StageCameraPresets(ステージの id ごと)に書き込む。エディタで再生しているときだけ保存できる
    //    保存した値は StageSwitcher がステージを表示するたびに使い、ステージを作り直しても残る
    //  ・「元に戻す」で保存済みの値(なければステージの CameraPoint)に戻し、「保存を消す」でステージの CameraPoint の値に戻す
    //  ・ステージを切り替えると、そのステージの値を読み直す(保存していない変更は捨てる)
    // 調整するのは基準の位置・向きで、MatchCamera の「位置・角度の調整」(全ステージ共通のずらし)はこの上に重なる
    // StageSwitcher と同じシーンのデバッグ用の物(DebugTools など)に付ける
    public class StageCameraTuner : MonoBehaviour
    {
        [SerializeField] Key m_toggleKey = Key.F2;
        [SerializeField] bool m_open = false;
        [Tooltip("セレクト画面から始めた試合でも使う(オフなら直接再生のときだけ)")]
        [SerializeField] bool m_showInConfiguredMatch = false;

        [Header("調整できる範囲")]
        [SerializeField] Vector2 m_pitchRange = new Vector2(10.0f, 89.0f);
        [SerializeField] Vector2 m_distanceRange = new Vector2(5.0f, 150.0f);
        [Tooltip("注視点を動かせる範囲(ステージを作ったときの注視点からの距離)")]
        [SerializeField, Min(1.0f)] float m_focusRange = 30.0f;
        [SerializeField] Vector2 m_fovRange = new Vector2(10.0f, 90.0f);

        [Header("参照(未設定ならシーンから探す)")]
        [SerializeField] StageSwitcher m_switcher;

        // 調整中の値(床の注視点を中心にした向きと距離)
        Vector3 m_focus;
        float m_pitch, m_yaw, m_roll, m_distance, m_fov;
        // ステージを作ったときの注視点(注視点のスライダーの範囲の中心)
        Vector3 m_baseFocus;
        // 保存していない変更がある
        bool m_dirty;
        string m_message = "";
        // ステージ名・積み木の衝突モードの表示(画面左上)と重ならない位置
        Rect m_window = new Rect(10, 100, 360, 0);
        // パネルのウィンドウの番号(他の OnGUI のウィンドウと重ならない値)
        const int kWindowId = 0x5CA3;
        StageEntry m_loadedStage;

        bool Usable
        {
            get
            {
                if (m_switcher == null) m_switcher = FindAnyObjectByType<StageSwitcher>();
                if (m_switcher == null || m_switcher.Current == null) return false;
                var match = DarumaMatch.Current != null ? DarumaMatch.Current : FindAnyObjectByType<DarumaMatch>();
                return m_showInConfiguredMatch || match == null || !match.IsConfigured;
            }
        }

        private void Update()
        {
            if (!Usable) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[m_toggleKey].wasPressedThisFrame) m_open = !m_open;
            // ステージが切り替わったら、そのステージの値を読み直す
            if (m_loadedStage != m_switcher.Current) Load();
        }

        // 今のステージのカメラの値を読み込む(保存した設定があればそれを、なければステージの CameraPoint)
        void Load(bool usePreset = true)
        {
            m_loadedStage = m_switcher.Current;
            m_dirty = false;
            if (m_switcher.GetCameraPose(m_loadedStage, out Vector3 basePos, out Quaternion baseRot, out _, false))
            {
                m_baseFocus = FocusOnFloor(basePos, baseRot);
            }
            if (!m_switcher.GetCameraPose(m_loadedStage, out Vector3 pos, out Quaternion rot, out float fov, usePreset)) return;

            Vector3 e = rot.eulerAngles;
            m_pitch = Mathf.DeltaAngle(0.0f, e.x);
            m_yaw = Mathf.DeltaAngle(0.0f, e.y);
            m_roll = Mathf.DeltaAngle(0.0f, e.z);
            m_focus = FocusOnFloor(pos, rot);
            m_distance = Vector3.Distance(pos, m_focus);
            m_fov = fov;
        }

        // 調整中の値をカメラへ反映する
        void Apply()
        {
            GetPose(out Vector3 pos, out Quaternion rot);
            m_switcher.ApplyCameraPose(pos, rot, m_fov);
        }

        void GetPose(out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.Euler(m_pitch, m_yaw, m_roll);
            position = m_focus - rotation * Vector3.forward * m_distance;
        }

        void Save()
        {
            var presets = m_switcher.CameraPresets;
            string id = StageId;
            if (presets == null || string.IsNullOrEmpty(id))
            {
                m_message = "StageSwitcher に StageCameraPresets が設定されていません";
                return;
            }
#if UNITY_EDITOR
            GetPose(out Vector3 pos, out Quaternion rot);
            presets.Set(id, pos, rot, m_fov);
            UnityEditor.EditorUtility.SetDirty(presets);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(presets);
            m_dirty = false;
            m_message = $"{id} のカメラを保存しました";
            Debug.Log($"[StageCameraTuner] {id} のカメラを保存しました(位置 {pos}, 角度 {rot.eulerAngles}, 視野角 {m_fov:0.#})");
#else
            m_message = "保存はエディタで再生しているときだけできます";
#endif
        }

        void RemovePreset()
        {
            var presets = m_switcher.CameraPresets;
            string id = StageId;
            if (presets != null && presets.Remove(id))
            {
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(presets);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(presets);
#endif
                m_message = $"{id} の保存を消しました(ステージの初期値に戻ります)";
            }
            else
            {
                m_message = "保存した値はありません";
            }
            Load(false);
            Apply();
        }

        string StageId => m_loadedStage != null && m_loadedStage.Info != null ? m_loadedStage.Info.Id : null;

        bool HasPreset => m_switcher.CameraPresets != null && m_switcher.CameraPresets.Find(StageId) != null;

        private void OnGUI()
        {
            if (!m_open || !Usable || m_loadedStage == null) return;
            m_window = GUILayout.Window(kWindowId,m_window, DrawWindow, $"カメラ調整  [{m_toggleKey}で閉じる]");
        }

        void DrawWindow(int id)
        {
            GUILayout.Label(m_loadedStage.DisplayName);
            GUILayout.Label(m_dirty ? "保存していない変更があります" : HasPreset ? "保存した値を使っています" : "ステージの初期値を使っています");

            bool changed = false;
            changed |= Slider("見下ろす角度", ref m_pitch, m_pitchRange.x, m_pitchRange.y);
            changed |= Slider("左右の向き", ref m_yaw, -180.0f, 180.0f);
            changed |= Slider("距離", ref m_distance, m_distanceRange.x, m_distanceRange.y);
            changed |= Slider("注視点 X", ref m_focus.x, m_baseFocus.x - m_focusRange, m_baseFocus.x + m_focusRange);
            changed |= Slider("注視点 Z", ref m_focus.z, m_baseFocus.z - m_focusRange, m_baseFocus.z + m_focusRange);
            changed |= Slider("視野角", ref m_fov, m_fovRange.x, m_fovRange.y);
            if (changed)
            {
                m_dirty = true;
                m_message = "";
                Apply();
            }

            GetPose(out Vector3 pos, out _);
            GUILayout.Label($"カメラの位置 ({pos.x:0.00}, {pos.y:0.00}, {pos.z:0.00})");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存")) Save();
            if (GUILayout.Button("元に戻す"))
            {
                Load();
                Apply();
                m_message = "保存済みの値に戻しました";
            }
            if (GUILayout.Button("保存を消す")) RemovePreset();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(m_message)) GUILayout.Label(m_message);
            GUI.DragWindow();
        }

        // ラベル・値・スライダーの1行。値が変わったら true
        static bool Slider(string label, ref float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90));
            GUILayout.Label(value.ToString("0.0"), GUILayout.Width(50));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            if (Mathf.Approximately(v, value)) return false;
            value = v;
            return true;
        }

        // カメラの正面の先にある床(高さ0)の点。下を向いていなければ少し先の点
        static Vector3 FocusOnFloor(Vector3 position, Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            if (forward.y < -0.01f) return position + forward * (-position.y / forward.y);
            return position + forward * 10.0f;
        }
    }
}
