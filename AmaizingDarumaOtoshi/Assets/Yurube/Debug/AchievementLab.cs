#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
using Yurube.Achievements;

namespace Yurube.Lab
{
    // 実績ラボ本体。キャラクターラボと同じく、シーンにはこれを1つ置くだけでよい。
    // ・実績の一覧・進捗・解除状態を確認する
    // ・強制解除・進捗の変更・リセットをする
    // ・ゲーム本編と同じ入り口（AchievementEvents）から解除条件を試す
    // ・解除の通知・セーブ・ロードを試す
    // 実績ラボはデバッグ専用の保存先を使うので、本編のセーブデータとは混ざらない。
    // このスクリプトは UNITY_EDITOR / DEVELOPMENT_BUILD のときだけ入る（製品版には入らない）。
    [DefaultExecutionOrder(-400)]
    public class AchievementLab : MonoBehaviour
    {
        [Header("参照")]
        [Tooltip("キャラクター一覧（「みんなの力」の使用履歴テスト用）。未設定なら AchievementSettings のものを使う")]
        [SerializeField] SelectRoster m_roster;

        [Header("画面")]
        [SerializeField] Color m_background = new Color(0.97f, 0.92f, 0.86f);

        AchievementLabUI m_ui;

        public IReadOnlyList<CharacterData> Characters
        {
            get
            {
                var settings = AchievementSettings.Instance;
                var roster = m_roster != null ? m_roster : (settings != null ? settings.Roster : null);
                var list = new List<CharacterData>();
                var ids = new HashSet<string>();
                if (roster != null && roster.characters != null)
                {
                    foreach (var c in roster.characters)
                    {
                        // ゲーム本編で選べるキャラ（プレハブがある）だけ。ID の重複も除く
                        if (c == null || c.prefab == null || string.IsNullOrEmpty(c.id)) continue;
                        if (ids.Add(c.id)) list.Add(c);
                    }
                }
                return list;
            }
        }

        void Awake()
        {
            // 前のシーンでスローのまま終わっていても戻す
            Time.timeScale = 1f;

            AchievementManager.Initialize();
            // 本編のデータとは別の保存先にする（終わるときに戻す）
            AchievementManager.SetStorage(AchievementManager.DebugStorageKey, true);

            BuildCamera();

            var uiGo = new GameObject("AchievementLabUI");
            m_ui = uiGo.AddComponent<AchievementLabUI>();
            m_ui.Initialize(this);
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            // 本編のデータを読み直しておく（ラボのデータを残さない）
            AchievementManager.SetStorage(AchievementManager.NormalStorageKey, true);
        }

        void BuildCamera()
        {
            var go = new GameObject("Lab Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = m_background;
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            go.transform.position = new Vector3(0f, 0f, -10f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            RenderSettings.fog = false;
        }
    }
}
#endif
