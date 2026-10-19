using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 試合のカメラ。ステージ全体を映す基準の位置から、プレイヤー達(幽霊・復活の出現演出を含む)の中点の方へ少しだけ動く
    // 視野角は、中点から最も遠いプレイヤーまでの距離に応じて変える(集まっていれば寄り、散らばっていれば引く)
    // 向きは試合中に勝手には変えない(移動の入力がカメラの向きを基準にしているため)
    // 「位置・角度の調整」は全ステージ共通で、再生中にインスペクターから変えてもその場で反映される
    //  (再生をやめると戻るので、決まった値は再生中にコンポーネントの「Copy Component」で写して、停止後に貼り付ける)
    [RequireComponent(typeof(Camera))]
    public class MatchCamera : MonoBehaviour
    {
        // 未設定ならシーンから探す
        [SerializeField] DarumaMatch m_match;

        [Header("位置・角度の調整(全ステージ共通。再生中に変えても即座に反映される)")]
        [Tooltip("基準の位置(ステージのカメラ位置)をワールド座標でずらす量。画面下のプレイヤーの状態表示とステージが重なる場合などに使う")]
        [SerializeField] Vector3 m_worldOffset = Vector3.zero;
        [Tooltip("基準の位置をカメラの向き基準でずらす量(X: 右, Y: 上, Z: 前 = 寄る)")]
        [SerializeField] Vector3 m_localOffset = Vector3.zero;
        [Tooltip("基準の角度に足す角度(X: 見下ろす角度(+で下向き), Y: 左右の回転, Z: 傾き)")]
        [SerializeField] Vector3 m_angleOffset = Vector3.zero;
        [Tooltip("オンなら、角度を変えたときに床の注視点を中心に回り込む(ステージが画面の中央に残る)。オフならその場で向きだけ変える")]
        [SerializeField] bool m_orbitAroundFocus = true;
        [Tooltip("基準の視野角に足す角度(+で引く)")]
        [SerializeField] float m_fovOffset = 0.0f;

        [Header("移動")]
        // 基準の注視点から中点までのずれのうち、カメラが追う割合
        [SerializeField, Range(0.0f, 1.0f)] float m_followRatio = 0.25f;
        // 基準の位置から動ける最大の距離
        [SerializeField, Min(0.0f)] float m_maxOffset = 3.0f;
        [SerializeField, Min(0.0f)] float m_moveSmoothTime = 0.4f;

        [Header("視野角")]
        // 最も寄ったときの視野角(最も引いたときは基準の視野角 = ステージ全体が映る角度)
        [SerializeField] float m_minFov = 30.0f;
        // 最も遠いプレイヤーまでの距離がこれ以下なら最も寄る
        [SerializeField, Min(0.0f)] float m_nearDistance = 3.0f;
        // 最も遠いプレイヤーまでの距離がこれ以上なら最も引く
        [SerializeField, Min(0.0f)] float m_farDistance = 14.0f;
        [SerializeField, Min(0.0f)] float m_fovSmoothTime = 0.5f;
        // 寄りすぎてプレイヤーが画面外に出る場合は、全員が映るまで引く
        [SerializeField] bool m_keepPlayersInView = true;
        // 画面の端からの余白(画面の幅・高さに対する割合)
        [SerializeField, Range(0.0f, 0.4f)] float m_viewMargin = 0.1f;
        // 画面内に収める体の高さ
        [SerializeField, Min(0.0f)] float m_bodyHeight = 2.0f;

        Camera m_camera;
        // ステージのカメラ位置(ずらす前)
        Vector3 m_basePosition;
        Quaternion m_baseRotation;
        float m_baseFov;

        Vector3 m_offset;
        Vector3 m_offsetVelocity;
        float m_fov;
        float m_fovVelocity;
        Vector3 m_overridePosition;
        float m_overrideFov;
        float m_overrideWeight;
        readonly List<Vector3> m_bodies = new List<Vector3>();
        readonly List<Transform> m_bodyTransforms = new List<Transform>();

        private void Awake()
        {
            m_camera = GetComponent<Camera>();
            m_baseFov = m_camera.fieldOfView;
            m_fov = m_baseFov;
            SetBasePose(transform.position, transform.rotation);
        }

        // 基準の位置・向きを変える(ステージ切り替えなど)。すぐにその位置へ移る
        public void SetBasePose(Vector3 position, Quaternion rotation)
        {
            if (m_camera == null) m_camera = GetComponent<Camera>();
            m_basePosition = position;
            m_baseRotation = rotation;
            m_offset = Vector3.zero;
            m_offsetVelocity = Vector3.zero;
            m_fovVelocity = 0.0f;
            GetAdjustedBase(out Vector3 adjustedPosition, out Quaternion adjustedRotation);
            transform.SetPositionAndRotation(adjustedPosition, adjustedRotation);
            if (m_baseFov > 0.0f)
            {
                m_fov = BaseFov;
                m_camera.fieldOfView = m_fov;
            }
        }

        private void LateUpdate()
        {
            CollectBodies();

            // 調整した基準の位置・向きと、そこから見ている床の上の点
            GetAdjustedBase(out Vector3 basePosition, out Quaternion baseRotation);
            Vector3 baseFocus = FocusOnFloor(basePosition, baseRotation);
            float baseFov = BaseFov;

            Vector3 targetOffset = Vector3.zero;
            float targetFov = baseFov;
            if (m_bodies.Count > 0)
            {
                Vector3 mid = Vector3.zero;
                foreach (var p in m_bodies) mid += p;
                mid /= m_bodies.Count;

                Vector3 toMid = mid - baseFocus;
                toMid.y = 0.0f;
                targetOffset = Vector3.ClampMagnitude(toMid * m_followRatio, m_maxOffset);

                float farthest = 0.0f;
                foreach (var p in m_bodies)
                {
                    Vector3 d = p - mid;
                    d.y = 0.0f;
                    farthest = Mathf.Max(farthest, d.magnitude);
                }
                float t = Mathf.InverseLerp(m_nearDistance, m_farDistance, farthest);
                targetFov = Mathf.Lerp(Mathf.Min(m_minFov, baseFov), baseFov, t);
                if (m_keepPlayersInView) targetFov = Mathf.Clamp(Mathf.Max(targetFov, RequiredFov(basePosition + targetOffset, baseRotation)), 1.0f, baseFov);
            }

            // スロー演出中も実時間でなめらかに追う
            float dt = Time.unscaledDeltaTime;
            m_offset = Vector3.SmoothDamp(m_offset, targetOffset, ref m_offsetVelocity, m_moveSmoothTime, Mathf.Infinity, dt);
            m_fov = Mathf.SmoothDamp(m_fov, targetFov, ref m_fovVelocity, m_fovSmoothTime, Mathf.Infinity, dt);

            // 演出による寄りを重ねる(向きは変えない)
            Vector3 position = basePosition + m_offset;
            float fov = m_fov;
            if (m_overrideWeight > 0.0f)
            {
                position = Vector3.Lerp(position, m_overridePosition, m_overrideWeight);
                fov = Mathf.Lerp(fov, m_overrideFov, m_overrideWeight);
            }
            transform.SetPositionAndRotation(position, baseRotation);
            m_camera.fieldOfView = fov;
        }

        // 基準の位置・向きに「位置・角度の調整」を加えたもの
        private void GetAdjustedBase(out Vector3 position, out Quaternion rotation)
        {
            position = m_basePosition;
            rotation = m_baseRotation;
            if (m_angleOffset != Vector3.zero)
            {
                Vector3 e = m_baseRotation.eulerAngles;
                rotation = Quaternion.Euler(e.x + m_angleOffset.x, e.y + m_angleOffset.y, e.z + m_angleOffset.z);
                if (m_orbitAroundFocus)
                {
                    // 床の注視点からの距離を保って回り込む
                    Vector3 focus = FocusOnFloor(m_basePosition, m_baseRotation);
                    float distance = Vector3.Distance(m_basePosition, focus);
                    position = focus - rotation * Vector3.forward * distance;
                }
            }
            position += m_worldOffset + rotation * m_localOffset;
        }

        // 調整した基準の視野角
        float BaseFov => Mathf.Clamp(m_baseFov + m_fovOffset, 1.0f, 179.0f);

        // 演出(EliminationCinematic など)で一時的に寄せる。weight: 0で通常、1で指定の位置・視野角
        // 向きは基準のまま(BaseRotation)なので、寄せる位置は「見たい点 - BaseRotation の前方 × 距離」で求める
        public void SetOverride(Vector3 position, float fov, float weight)
        {
            m_overridePosition = position;
            m_overrideFov = fov;
            m_overrideWeight = Mathf.Clamp01(weight);
        }

        public void ClearOverride()
        {
            m_overrideWeight = 0.0f;
        }

        // 今の向き(角度の調整を含む)
        public Quaternion BaseRotation
        {
            get
            {
                GetAdjustedBase(out _, out Quaternion rotation);
                return rotation;
            }
        }

        // 基準の位置をずらす量(ワールド座標)。演出やデバッグから変えてもよい
        public Vector3 WorldOffset
        {
            get => m_worldOffset;
            set => m_worldOffset = value;
        }

        // 基準の位置をカメラの向き基準でずらす量
        public Vector3 LocalOffset
        {
            get => m_localOffset;
            set => m_localOffset = value;
        }

        // 基準の角度に足す角度
        public Vector3 AngleOffset
        {
            get => m_angleOffset;
            set => m_angleOffset = value;
        }

        public float FovOffset
        {
            get => m_fovOffset;
            set => m_fovOffset = value;
        }

        // 位置・角度の調整をすべて元に戻す
        [ContextMenu("位置・角度の調整をリセット")]
        public void ResetAdjustments()
        {
            m_worldOffset = Vector3.zero;
            m_localOffset = Vector3.zero;
            m_angleOffset = Vector3.zero;
            m_fovOffset = 0.0f;
        }

        // 参加中のプレイヤーが操作している体(生存キャラクター・幽霊)と、カメラの目印(CameraFocusTarget。復活の出現演出など)の位置
        private void CollectBodies()
        {
            m_bodies.Clear();
            foreach (var target in CameraFocusTarget.All)
            {
                if (target != null) m_bodies.Add(target.transform.position);
            }
            if (m_match == null) m_match = FindAnyObjectByType<DarumaMatch>();
            if (m_match == null) return;
            // 自由参加・セレクトから、どちらの試合でも全員の体を取れる
            m_match.GetBodies(m_bodyTransforms);
            foreach (var body in m_bodyTransforms) m_bodies.Add(body.position);
        }

        // この位置・向きから、すべての体(足元〜頭)が余白付きで映る視野角
        private float RequiredFov(Vector3 position, Quaternion rotation)
        {
            Quaternion inv = Quaternion.Inverse(rotation);
            float aspect = m_camera.aspect;
            float tanHalf = 0.0f;
            foreach (var p in m_bodies)
            {
                for (int i = 0; i < 2; i++)
                {
                    Vector3 local = inv * (p + Vector3.up * (m_bodyHeight * i) - position);
                    if (local.z <= 0.01f) continue;
                    tanHalf = Mathf.Max(tanHalf, Mathf.Abs(local.y) / local.z, Mathf.Abs(local.x) / (local.z * aspect));
                }
            }
            tanHalf /= Mathf.Max(0.01f, 1.0f - m_viewMargin * 2.0f);
            return Mathf.Atan(tanHalf) * 2.0f * Mathf.Rad2Deg;
        }

        // カメラの正面の先にある床(高さ0)の点。下を向いていなければ少し先の点
        private static Vector3 FocusOnFloor(Vector3 position, Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            if (forward.y < -0.01f) return position + forward * (-position.y / forward.y);
            return position + forward * 10.0f;
        }
    }
}
