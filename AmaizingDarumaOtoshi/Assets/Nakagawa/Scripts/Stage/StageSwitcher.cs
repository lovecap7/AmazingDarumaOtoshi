using System.Collections;
using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Nakagawa
{
    // ステージをまとめたシーンで、表示するステージを切り替える(デバッグ用)
    // 左右キーで前後のステージへ切り替え、参加中のプレイヤーのキャラクターを新しいステージの出現位置に置き直す
    // 試合(DarumaMatch)は全ステージで共有し、出現ポイント・ステージ範囲・カメラをステージの設定に合わせて動かす
    // DarumaPlayer.Start より先にステージを決めておくため、早めに実行する
    [DefaultExecutionOrder(-100)]
    public class StageSwitcher : MonoBehaviour
    {
        [SerializeField] StageEntry[] m_stages;
        [SerializeField] int m_startIndex = 0;

        [Header("共有")]
        [SerializeField] DarumaMatch m_match;
        // DarumaMatch に登録してある出現ポイント(ステージの出現位置へ動かす)
        [SerializeField] Transform[] m_spawnPoints;
        // DarumaMatch に登録してあるステージ範囲(ステージの見本から設定を写す)
        [SerializeField] StageArea m_stageArea;
        [SerializeField] Camera m_camera;

        [Header("デバッグ入力")]
        [SerializeField] Key m_prevKey = Key.LeftArrow;
        [SerializeField] Key m_nextKey = Key.RightArrow;
        [SerializeField] bool m_showLabel = true;

        // 試合終了後に切り替えたときは、シーンを読み直してこのステージから始める
        static int s_pendingIndex = -1;

        // ドメインの再読み込みをしない設定でも、再生のたびに戻す
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_pendingIndex = -1;

        int m_index = -1;
        bool m_switching;

        public int CurrentIndex => m_index;
        public StageEntry Current => m_index >= 0 ? m_stages[m_index] : null;

        private void Awake()
        {
            int index = s_pendingIndex >= 0 ? s_pendingIndex : m_startIndex;
            s_pendingIndex = -1;
            ApplyStage(index);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || m_switching) return;
            if (keyboard[m_prevKey].wasPressedThisFrame) SwitchTo(m_index - 1);
            else if (keyboard[m_nextKey].wasPressedThisFrame) SwitchTo(m_index + 1);
        }

        // ステージを切り替え、キャラクターを初期配置に戻す(範囲外は端から反対の端へ回り込む)
        public void SwitchTo(int index)
        {
            if (m_stages == null || m_stages.Length == 0 || m_switching) return;
            index = Wrap(index);

            // 試合が終わっていると DarumaMatch は再開できないので、シーンごと読み直す(プレイヤーは参加し直し)
            if (m_match != null && m_match.IsFinished)
            {
                s_pendingIndex = index;
                ReloadScene();
                return;
            }
            StartCoroutine(SwitchRoutine(index));
        }

        private IEnumerator SwitchRoutine(int index)
        {
            m_switching = true;

            // 前のステージのキャラクター・幽霊・積み木を片付ける(破棄はフレームの終わりなので1フレーム待つ)
            if (m_match != null)
            {
                foreach (var player in m_match.Players)
                {
                    if (player.Character != null) Destroy(player.Character.gameObject);
                    if (player.Ghost != null) Destroy(player.Ghost.gameObject);
                }
            }
            foreach (var block in FindObjectsByType<TumikiBlock>()) Destroy(block.gameObject);
            yield return null;

            ApplyStage(index);

            // 参加した順に、他のキャラクターから最も遠い出現ポイントへ出す(参加したときと同じ決め方)
            if (m_match != null)
            {
                foreach (var player in m_match.Players)
                {
                    player.Possess(m_match.SpawnCharacter(player, false));
                }
            }
            m_switching = false;
        }

        // 指定したステージだけを有効にし、共有の出現ポイント・ステージ範囲・カメラを合わせる
        private void ApplyStage(int index)
        {
            if (m_stages == null || m_stages.Length == 0) return;
            m_index = Wrap(index);
            for (int i = 0; i < m_stages.Length; i++)
            {
                if (m_stages[i] != null) m_stages[i].gameObject.SetActive(i == m_index);
            }

            StageEntry stage = m_stages[m_index];
            var points = stage.SpawnPoints;
            if (points != null && points.Length > 0)
            {
                // 数が違うときは足りない分をステージの出現位置から順に使い回す
                for (int i = 0; i < m_spawnPoints.Length; i++)
                {
                    Transform src = points[i % points.Length];
                    m_spawnPoints[i].SetPositionAndRotation(src.position, src.rotation);
                }
            }

            if (stage.StageArea != null && m_stageArea != null)
            {
                m_stageArea.transform.SetPositionAndRotation(stage.StageArea.transform.position, stage.StageArea.transform.rotation);
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(stage.StageArea), m_stageArea);
            }

            if (stage.CameraPoint != null && m_camera != null)
            {
                // プレイヤーを追うカメラなら、追う基準の位置を変える
                var follow = m_camera.GetComponent<MatchCamera>();
                if (follow != null) follow.SetBasePose(stage.CameraPoint.position, stage.CameraPoint.rotation);
                else m_camera.transform.SetPositionAndRotation(stage.CameraPoint.position, stage.CameraPoint.rotation);
            }

            Debug.Log($"[StageSwitcher] {stage.DisplayName} ({m_index + 1}/{m_stages.Length})");
        }

        private void ReloadScene()
        {
            Scene scene = gameObject.scene;
            if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
                return;
            }
#if UNITY_EDITOR
            // ビルド設定に登録していないシーンはエディタからだけ読み直せる
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Debug.LogWarning("[StageSwitcher] ビルド設定にないシーンは読み直せません");
#endif
        }

        private int Wrap(int index)
        {
            int n = m_stages.Length;
            return ((index % n) + n) % n;
        }

        private void OnGUI()
        {
            if (!m_showLabel || Current == null) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
            GUI.Box(new Rect(10, 10, 420, 34), $" {Current.DisplayName}  ({m_index + 1}/{m_stages.Length})  [{m_prevKey}/{m_nextKey}]", style);
        }
    }
}
