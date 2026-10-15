using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 壁の裏に隠れた積み木・プレイヤーが見えるように、カメラから見て手前にある壁を円形に透過させる(カメラに付ける)
    // 対象のカメラ空間での位置・奥行き・円の大きさと、体の縦の軸をシェーダーのグローバル変数に渡し、
    // 「Nakagawa/SeeThroughWall」シェーダーの壁が、円の中で視線上の体より手前にある部分を描かない
    // 床など透過しない物にカメラからの視線がすべて遮られている対象は渡さない(床が残るのに壁だけ透けないように)
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
        // 体の高さ(足元から。この範囲より手前の壁を透過する)
        [SerializeField, Min(0.0f)] float m_playerHeight = 2.0f;

        [Header("積み木")]
        [SerializeField] bool m_includeBlocks = true;
        [SerializeField, Min(0.0f)] float m_blockRadius = 0.7f;

        [Header("透過しない物に隠れた対象")]
        [Tooltip("カメラからの視線が床など透過しない物にすべて遮られている対象は、壁も透過しない")]
        [SerializeField] bool m_skipOccludedTargets = true;
        [Tooltip("視線を遮るか調べるレイヤー")]
        [SerializeField] LayerMask m_occluderMask = Physics.DefaultRaycastLayers;

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
        static readonly int kAxesId = Shader.PropertyToID("_SeeThroughAxes");
        static readonly int kCameraPosId = Shader.PropertyToID("_SeeThroughCameraPos");

        readonly Vector4[] m_points = new Vector4[kMaxTargets];
        readonly Vector4[] m_axes = new Vector4[kMaxTargets];
        readonly RaycastHit[] m_hits = new RaycastHit[16];
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
            Shader.SetGlobalVectorArray(kAxesId, m_axes);
            Shader.SetGlobalInt(kCountId, m_count);
            Shader.SetGlobalVector(kParamsId, new Vector4(m_feather, m_depthMargin, 0.0f, 0.0f));
            // 透過する範囲はこのカメラから見た位置で決める(シーンビュー等の他のカメラで見ても同じ場所が透ける)
            Shader.SetGlobalMatrix(kViewId, m_camera.worldToCameraMatrix);
            Shader.SetGlobalVector(kCameraPosId, transform.position);
        }

        private void AddPlayers()
        {
            if (m_match == null) m_match = FindAnyObjectByType<DarumaMatch>();
            if (m_match == null) return;
            m_match.GetBodies(m_bodies);
            foreach (var body in m_bodies) Add(body.position, m_playerHeight, m_playerCenterHeight, m_playerRadius);
        }

        readonly System.Collections.Generic.List<Transform> m_bodies = new System.Collections.Generic.List<Transform>();

        private void AddBlocks()
        {
            foreach (var block in FindObjectsByType<TumikiBlock>())
            {
                // 積まれている積み木はプレイヤーの円に含まれる
                if (block.CurrentState == TumikiBlock.State.Stacked) continue;
                Vector3 feet = block.transform.position - Vector3.up * (TumikiBlock.kHeight * 0.5f);
                Add(feet, TumikiBlock.kHeight, TumikiBlock.kHeight * 0.5f, m_blockRadius);
            }
        }

        // feet: 足元(ワールド座標) / height: 体の高さ / centerHeight: 円の中心の高さ(足元から) / radius: 円の半径(m)
        // 円はカメラ空間での位置(x, y)・カメラからの奥行き・半径で渡す。シェーダーでは壁の各点を対象の奥行きまで
        // 視線に沿って延ばし、対象との距離で円の内側かを判定する。体の縦の軸(足元・高さ)は、壁が視線上で体より手前かの判定に使う
        private void Add(Vector3 feet, float height, float centerHeight, float radius)
        {
            if (m_count >= kMaxTargets) return;
            Vector3 center = feet + Vector3.up * centerHeight;
            Vector3 v = m_camera.worldToCameraMatrix.MultiplyPoint(center);
            float depth = -v.z;
            if (depth <= m_camera.nearClipPlane) return;
            if (m_skipOccludedTargets && IsOccluded(feet, height)) return;
            m_points[m_count] = new Vector4(v.x, v.y, depth, radius);
            m_axes[m_count] = new Vector4(feet.x, feet.y, feet.z, height);
            m_count++;
        }

        // 体の下・中・上の3点への視線が、すべて透過しない物(床など)に遮られているか
        private bool IsOccluded(Vector3 feet, float height)
        {
            Vector3 eye = transform.position;
            for (int i = 0; i < 3; i++)
            {
                // 足元ちょうどは立っている床に触れるので、少し上から調べる
                Vector3 p = feet + Vector3.up * Mathf.Lerp(height * 0.15f, height * 0.85f, i * 0.5f);
                if (!IsLineBlocked(eye, p)) return false;
            }
            return true;
        }

        private bool IsLineBlocked(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float distance = d.magnitude;
            if (distance < 0.001f) return false;
            int n = Physics.RaycastNonAlloc(from, d / distance, m_hits, distance, m_occluderMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (IsOpaqueOccluder(m_hits[i].collider)) return true;
            }
            return false;
        }

        // 視線を遮る透過しない物: 動かない・見えている当たり判定で、壁(透過する壁・見えない壁)ではない物(床など)
        // 積み木・キャラクター・ギミックなど動く物は遮る物とみなさない
        private static bool IsOpaqueOccluder(Collider col)
        {
            if (col.attachedRigidbody != null) return false;
            if (col.GetComponentInParent<ReflectWall>() != null) return false;
            var r = col.GetComponent<Renderer>();
            return r != null && r.enabled && r.gameObject.activeInHierarchy;
        }
    }
}
