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
        [Tooltip("ステージごとのカメラの設定(StageCameraTuner で保存した物)。設定のあるステージは CameraPoint の代わりに使う")]
        [SerializeField] StageCameraPresets m_cameraPresets;

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
        // カメラの既定の視野角(ステージごとの設定に視野角がないときに使う)
        float m_defaultFov = -1.0f;

        // ステージの切り替えを始める(前のステージの片付けの前)。ネット対戦などの拡張用
        public event System.Action SwitchStarting;
        // ステージを表示した(有効にしたステージの番号)。ネット対戦などの拡張用
        public event System.Action<int> StageApplied;
        public int StageCount => m_stages != null ? m_stages.Length : 0;

        // 片付けやキャラクターの出し直しをせず、表示するステージだけを切り替える(ネット対戦のクライアント用)
        public void ShowStage(int index) => ApplyStage(index);

        public int CurrentIndex => m_index;
        public StageEntry Current => m_index >= 0 ? m_stages[m_index] : null;
        public StageCameraPresets CameraPresets => m_cameraPresets;
        bool DebugEnabled => m_debugInConfiguredMatch || m_match == null || !m_match.IsConfigured;

        private void Awake()
        {
            if (m_camera != null) m_defaultFov = m_camera.fieldOfView;
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
            SwitchStarting?.Invoke();

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
            // CPU が迂回する道のナビメッシュも作り直す
            CpuNavMesh.MarkDirty();

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

            if (GetCameraPose(stage, out Vector3 cameraPosition, out Quaternion cameraRotation, out float cameraFov))
            {
                ApplyCameraPose(cameraPosition, cameraRotation, cameraFov);
            }

            Debug.Log($"[StageSwitcher] {stage.DisplayName} ({m_index + 1}/{m_stages.Length})");
            StageApplied?.Invoke(m_index);
        }

        // ステージを映すカメラの位置・向き・視野角。保存した設定(StageCameraPresets)があればそれを、なければステージの CameraPoint を使う
        // usePreset: false なら保存した設定を使わず、ステージの CameraPoint(ステージを作ったときの値)を返す
        public bool GetCameraPose(StageEntry stage, out Vector3 position, out Quaternion rotation, out float fov, bool usePreset = true)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            fov = DefaultFov;
            if (stage == null) return false;
            var preset = usePreset && m_cameraPresets != null && stage.Info != null ? m_cameraPresets.Find(stage.Info.Id) : null;
            if (preset != null)
            {
                position = preset.Position;
                rotation = preset.Rotation;
                if (preset.Fov > 0.0f) fov = preset.Fov;
                return true;
            }
            if (stage.CameraPoint == null) return false;
            position = stage.CameraPoint.position;
            rotation = stage.CameraPoint.rotation;
            return true;
        }

        // カメラを指定の位置・向き・視野角にする(プレイヤーを追うカメラなら、追う基準を変える)
        public void ApplyCameraPose(Vector3 position, Quaternion rotation, float fov)
        {
            if (m_camera == null) return;
            var follow = m_camera.GetComponent<MatchCamera>();
            if (follow != null)
            {
                follow.SetBasePose(position, rotation);
                if (fov > 0.0f) follow.SetBaseFov(fov);
            }
            else
            {
                m_camera.transform.SetPositionAndRotation(position, rotation);
                if (fov > 0.0f) m_camera.fieldOfView = fov;
            }
        }

        // カメラの既定の視野角(シーンに置いたカメラの値)
        public float DefaultFov
        {
            get
            {
                if (m_defaultFov <= 0.0f && m_camera != null) m_defaultFov = m_camera.fieldOfView;
                return m_defaultFov;
            }
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
