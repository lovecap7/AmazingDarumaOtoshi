using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

// ネット対戦用の操作。キーボードとゲームパッドのどちらでも操作できる(同時に使ってもよい)
//   ゲームパッド(DarumaControls と同じ割り当て): 左スティック / 十字キー:移動  A(下):ジャンプ  X(左):薙ぎ払い  B(右):股抜きショット
//   キーボード: WASD / 矢印:移動  Space:ジャンプ  J:薙ぎ払い  K:股抜きショット
// 1台の画面に1人なので、つながっているゲームパッドはどれでも使う(最後に触ったもの = Gamepad.current)
// スティックの向きはカメラ基準に直す(DarumaInputHandler と同じ)
public class NetPadKeyboardInput : MonoBehaviour, IDarumaCommandSource
{
    const float kDeadZone = 0.2f;
    Transform m_camera;

    public DarumaCommand ReadCommand()
    {
        if (m_camera == null && Camera.main != null) m_camera = Camera.main.transform;
        // 同じPCで複数の画面を動かすと、ゲームパッドの入力は裏の画面にも届くので、選んでいる画面だけ入力を読む
        if (!Application.isFocused) return default;

        Vector2 stick = Vector2.zero;
        bool jump = false, sweep = false, shot = false;

        var k = Keyboard.current;
        if (k != null)
        {
            float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
            float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
            stick += new Vector2(x, y);
            jump |= k.spaceKey.wasPressedThisFrame;
            sweep |= k.jKey.wasPressedThisFrame;
            shot |= k.kKey.wasPressedThisFrame;
        }

        var pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 s = pad.leftStick.ReadValue();
            if (s.magnitude < kDeadZone) s = Vector2.zero;
            stick += s + pad.dpad.ReadValue();
            jump |= pad.buttonSouth.wasPressedThisFrame;
            sweep |= pad.buttonWest.wasPressedThisFrame;
            shot |= pad.buttonEast.wasPressedThisFrame;
        }

        return new DarumaCommand
        {
            Move = ToWorldMove(Vector2.ClampMagnitude(stick, 1f)),
            Jump = jump,
            Sweep = sweep,
            Shot = shot,
        };
    }

    Vector2 ToWorldMove(Vector2 stick)
    {
        if (m_camera == null) return stick;
        Vector3 forward = Vector3.ProjectOnPlane(m_camera.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(m_camera.right, Vector3.up).normalized;
        Vector3 dir = forward * stick.y + right * stick.x;
        return new Vector2(dir.x, dir.z);
    }
}
