using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 試合のカメラ。ステージ全体を映す基準の位置から、プレイヤー達(幽霊を含む)の中点の方へ少しだけ動く
    // 視野角は、中点から最も遠いプレイヤーまでの距離に応じて変える(集まっていれば寄り、散らばっていれば引く)
    // 向きは変えない(移動の入力がカメラの向きを基準にしているため)
    [RequireComponent(typeof(Camera))]
    public class MatchCamera : MonoBehaviour
    {
        // 未設定ならシーンから探す
        [SerializeField] DarumaMatch m_match;

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
        Vector3 m_basePosition;
        Quaternion m_baseRotation;
        float m_baseFov;
        // 基準の位置から見ている床の上の点
        Vector3 m_baseFocus;

        Vector3 m_offset;
        Vector3 m_offsetVelocity;
        float m_fovVelocity;
        readonly List<Vector3> m_bodies = new List<Vector3>();

        private void Awake()
        {
            m_camera = GetComponent<Camera>();
            m_baseFov = m_camera.fieldOfView;
            SetBasePose(transform.position, transform.rotation);
        }

        // 基準の位置・向きを変える(ステージ切り替えなど)。すぐにその位置へ移る
        public void SetBasePose(Vector3 position, Quaternion rotation)
        {
            if (m_camera == null) m_camera = GetComponent<Camera>();
            m_basePosition = position;
            m_baseRotation = rotation;
            m_baseFocus = FocusOnFloor(position, rotation);
            m_offset = Vector3.zero;
            m_offsetVelocity = Vector3.zero;
            m_fovVelocity = 0.0f;
            transform.SetPositionAndRotation(position, rotation);
            if (m_baseFov > 0.0f) m_camera.fieldOfView = m_baseFov;
        }

        private void LateUpdate()
        {
            CollectBodies();

            Vector3 targetOffset = Vector3.zero;
            float targetFov = m_baseFov;
            if (m_bodies.Count > 0)
            {
                Vector3 mid = Vector3.zero;
                foreach (var p in m_bodies) mid += p;
                mid /= m_bodies.Count;

                Vector3 toMid = mid - m_baseFocus;
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
                targetFov = Mathf.Lerp(Mathf.Min(m_minFov, m_baseFov), m_baseFov, t);
                if (m_keepPlayersInView) targetFov = Mathf.Clamp(Mathf.Max(targetFov, RequiredFov(m_basePosition + targetOffset)), 1.0f, m_baseFov);
            }

            m_offset = Vector3.SmoothDamp(m_offset, targetOffset, ref m_offsetVelocity, m_moveSmoothTime);
            transform.SetPositionAndRotation(m_basePosition + m_offset, m_baseRotation);
            m_camera.fieldOfView = Mathf.SmoothDamp(m_camera.fieldOfView, targetFov, ref m_fovVelocity, m_fovSmoothTime);
        }

        // 参加中のプレイヤーが操作している体(生存キャラクター・幽霊)の位置
        private void CollectBodies()
        {
            m_bodies.Clear();
            if (m_match == null) m_match = FindAnyObjectByType<DarumaMatch>();
            if (m_match == null) return;
            foreach (var player in m_match.Players)
            {
                if (player == null) continue;
                Transform body = player.CurrentBody;
                if (body != null) m_bodies.Add(body.position);
            }
        }

        // この位置から、すべての体(足元〜頭)が余白付きで映る視野角
        private float RequiredFov(Vector3 position)
        {
            Quaternion inv = Quaternion.Inverse(m_baseRotation);
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
