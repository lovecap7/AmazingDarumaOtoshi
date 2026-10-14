using System.Collections;
using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nakagawa
{
    // ステージをまとめたシーンで、表示するステージを切り替える
    //  ・シーンを読み込む前に RequestStage でステージ(StageInfo の Id)を指定すると、そのステージから始まる(セレクト画面から使う)
    //  ・デバッグ用に、左右キーで前後のステージへ切り替え、参加中のプレイヤーのキャラクターを新しいステージの出現位置に置き直す
    //    (セレクト画面から始めた試合では、キーボードで操作する人の移動と重なるので切り替えない)
    // 試合(DarumaMatch)は全ステージで共有し、出現ポイント・ステージ範囲・カメラをステージの設定に合わせて動かす
    // DarumaMatch がプレイヤーを出すより先にステージを決めておくため、早めに実行する
    [DefaultExecutionOrder(-100)]
    public class StageSwitcher : MonoBehaviour
    {
        [SerializeField] StageEntry[] m_stages;
        [SerializeField] int m_startIndex = 0;
        [Tooltip("セレクト画面から始めた試合でも、左右キーでの切り替えとステージ名の表示を使う")]
        [SerializeField] bool m_debugInConfiguredMatch = false;

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
        // 次に読み込んだときに始めるステージの Id(StageInfo.Id)
        static string s_requestedId;

        // ドメインの再読み込みをしない設定でも、再生のたびに戻す
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_pendingIndex = -1;
            s_requestedId = null;
        }

        // 次にこのシーンを読み込んだとき、指定した Id(StageInfo.Id。例: "Stage01")のステージから始める
        // シーンを読み込む前に呼ぶ。見つからなければ最初のステージから始める
        public static void RequestStage(string stageId)
        {
            s_requestedId = stageId;
        }

        int m_index = -1;
        bool m_switching;

        public int CurrentIndex => m_index;
        public StageEntry Current => m_index >= 0 ? m_stages[m_index] : null;
        bool DebugEnabled => m_debugInConfiguredMatch || m_match == null || !m_match.IsConfigured;

        private void Awake()
        {
            int index = s_pendingIndex >= 0 ? s_pendingIndex : m_startIndex;
            if (s_pendingIndex < 0 && !string.IsNullOrEmpty(s_requestedId))
            {
                int found = IndexOf(s_requestedId);
                if (found >= 0) index = found;
                else Debug.LogWarning($"[StageSwitcher] ステージ {s_requestedId} が見つからないので、最初のステージから始めます");
            }
            s_pendingIndex = -1;
            s_requestedId = null;
            ApplyStage(index);
        }

        // 試合をやり直す(シーンを読み直す)ときは、今のステージから始める
        private void OnEnable() => DarumaMatch.BeforeRestart += KeepCurrentStage;
        private void OnDisable() => DarumaMatch.BeforeRestart -= KeepCurrentStage;
        private void KeepCurrentStage() => s_pendingIndex = m_index;

        // StageInfo の Id が一致するステージの番号(なければ -1)
        public int IndexOf(string stageId)
        {
            if (m_stages == null) return -1;
            for (int i = 0; i < m_stages.Length; i++)
            {
                if (m_stages[i] != null && m_stages[i].Info != null && m_stages[i].Info.Id == stageId) return i;
            }
            return -1;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || m_switching || !DebugEnabled) return;
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
                m_index = index;
                m_match.Restart();
                return;
            }
            StartCoroutine(SwitchRoutine(index));
        }

        private IEnumerator SwitchRoutine(int index)
        {
            m_switching = true;

            // 前のステージのキャラクター・幽霊・積み木を片付ける(破棄はフレームの終わりなので1フレーム待つ)
            if (m_match != null) m_match.ClearBodies();
            foreach (var block in FindObjectsByType<TumikiBlock>()) Destroy(block.gameObject);
            yield return null;

            ApplyStage(index);

            // 全員を新しいステージの出現ポイントに出し直す
            if (m_match != null) m_match.SpawnAllAtStart();
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
            // 床の配置が変わったので、継ぎ目の判定に使う床の一覧を作り直す
            FloorSeamFilter.MarkDirty();

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

        private int Wrap(int index)
        {
            int n = m_stages.Length;
            return ((index % n) + n) % n;
        }

        private void OnGUI()
        {
            if (!m_showLabel || Current == null || !DebugEnabled) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
            GUI.Box(new Rect(10, 10, 420, 34), $" {Current.DisplayName}  ({m_index + 1}/{m_stages.Length})  [{m_prevKey}/{m_nextKey}]", style);
        }
    }
}
