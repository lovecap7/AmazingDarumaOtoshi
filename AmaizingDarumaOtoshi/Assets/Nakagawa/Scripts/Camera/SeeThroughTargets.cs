using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 壁の裏に隠れた積み木・プレイヤーが見えるように、カメラから見て手前にある壁を円形に透過させる(カメラに付ける)
    // 対象のカメラ空間での位置・奥行き・円の大きさをシェーダーのグローバル変数に渡し、
    // 「Nakagawa/SeeThroughWall」シェーダーの壁が、円の中で対象より手前にある部分を描かない
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(200)]
    public class SeeThroughTargets : MonoBehaviour
    {
        // シェーダー側の配列の大きさと同じにする
        public const int kMaxTargets = 64;

        [SerializeField] bool m_enabled = true;

        [Header("プレイヤー(幽霊を含む)")]
        [SerializeField] bool m_includePlayers = true;
        // 円の半径(m。対象の位置での大きさ)
        [SerializeField, Min(0.0f)] float m_playerRadius = 1.3f;
        // 円の中心にする高さ(足元から)
        [SerializeField] float m_playerCenterHeight = 1.0f;

        [Header("積み木")]
        [SerializeField] bool m_includeBlocks = true;
        [SerializeField, Min(0.0f)] float m_blockRadius = 0.7f;

        [Header("見た目")]
        // 円の縁をぼかす幅(半径に対する割合)
        [SerializeField, Range(0.0f, 1.0f)] float m_feather = 0.3f;
        // 対象よりこれだけ手前(m)の壁から透過する(対象を囲む壁が透けないように)
        [SerializeField, Min(0.0f)] float m_depthMargin = 0.6f;

        [Header("参照(未設定ならシーンから探す)")]
        [SerializeField] DarumaMatch m_match;

        static readonly int kPointsId = Shader.PropertyToID("_SeeThroughPoints");
        static readonly int kCountId = Shader.PropertyToID("_SeeThroughCount");
        static readonly int kParamsId = Shader.PropertyToID("_SeeThroughParams");
        static readonly int kViewId = Shader.PropertyToID("_SeeThroughView");

        readonly Vector4[] m_points = new Vector4[kMaxTargets];
        Camera m_camera;
        int m_count;

        private void Awake()
        {
            m_camera = GetComponent<Camera>();
        }

        private void OnDisable()
        {
            Shader.SetGlobalInt(kCountId, 0);
        }

        private void LateUpdate()
        {
            m_count = 0;
            if (m_enabled)
            {
                if (m_includePlayers) AddPlayers();
                if (m_includeBlocks) AddBlocks();
            }
            // 配列の大きさは最初に渡したときに決まるので、常に最大の大きさで渡す
            Shader.SetGlobalVectorArray(kPointsId, m_points);
            Shader.SetGlobalInt(kCountId, m_count);
            Shader.SetGlobalVector(kParamsId, new Vector4(m_feather, m_depthMargin, 0.0f, 0.0f));
            // 透過する範囲はこのカメラから見た位置で決める(シーンビュー等の他のカメラで見ても同じ場所が透ける)
            Shader.SetGlobalMatrix(kViewId, m_camera.worldToCameraMatrix);
        }

        private void AddPlayers()
        {
            if (m_match == null) m_match = FindAnyObjectByType<DarumaMatch>();
            if (m_match == null) return;
            m_match.GetBodies(m_bodies);
            foreach (var body in m_bodies) Add(body.position + Vector3.up * m_playerCenterHeight, m_playerRadius);
        }

        readonly System.Collections.Generic.List<Transform> m_bodies = new System.Collections.Generic.List<Transform>();

        private void AddBlocks()
        {
            foreach (var block in FindObjectsByType<TumikiBlock>())
            {
                // 積まれている積み木はプレイヤーの円に含まれる
                if (block.CurrentState == TumikiBlock.State.Stacked) continue;
                Add(block.transform.position, m_blockRadius);
            }
        }

        // カメラ空間での位置(x, y)、カメラからの奥行き(m)、円の半径(m)
        // シェーダーでは壁の各点を対象の奥行きまで視線に沿って延ばし、対象との距離で円の内側かを判定する
        private void Add(Vector3 worldPos, float radius)
        {
            if (m_count >= kMaxTargets) return;
            Vector3 v = m_camera.worldToCameraMatrix.MultiplyPoint(worldPos);
            float depth = -v.z;
            if (depth <= m_camera.nearClipPlane) return;
            m_points[m_count++] = new Vector4(v.x, v.y, depth, radius);
        }
    }
}
