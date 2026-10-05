using UnityEngine;
using UnityEngine.InputSystem;

namespace Nakahira
{
    // PlayerInput(ゲームパッド)から操作命令を作る
    [RequireComponent(typeof(PlayerInput))]
    public class DarumaInputHandler : MonoBehaviour, IDarumaCommandSource
    {
        // スティックの向きの基準にするカメラ。未設定ならMainCamera
        [SerializeField] Transform m_camera;

        InputAction m_move;
        InputAction m_jump;
        InputAction m_sweep;
        InputAction m_shot;

        private void Awake()
        {
            var actions = GetComponent<PlayerInput>().actions;
            m_move = actions.FindAction("Move", true);
            m_jump = actions.FindAction("Jump", true);
            m_sweep = actions.FindAction("Sweep", true);
            m_shot = actions.FindAction("Shot", true);
        }

        private void Start()
        {
            if (m_camera == null && Camera.main != null) m_camera = Camera.main.transform;
        }

        public DarumaCommand ReadCommand()
        {
            return new DarumaCommand
            {
                Move = ToWorldMove(m_move.ReadValue<Vector2>()),
                Jump = m_jump.WasPressedThisFrame(),
                Sweep = m_sweep.WasPressedThisFrame(),
                Shot = m_shot.WasPressedThisFrame(),
            };
        }

        // スティック入力を、カメラから見た向きのワールド方向(XZ)に変換
        private Vector2 ToWorldMove(Vector2 stick)
        {
            if (m_camera == null) return stick;

            Vector3 forward = Vector3.ProjectOnPlane(m_camera.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(m_camera.right, Vector3.up).normalized;
            Vector3 dir = forward * stick.y + right * stick.x;
            return new Vector2(dir.x, dir.z);
        }
    }
}
