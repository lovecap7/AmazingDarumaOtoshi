using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

// キーボードで遊ぶプレイヤーの操作（本編の操作設定はゲームパッドのみなので、セレクトでキーボード参加した人用）
//   WASD / 矢印：移動　Space：ジャンプ　J：薙ぎ払い　K：股抜きショット
// DarumaInputHandler と同じく、スティック（キー）の向きはカメラ基準に直す。
public class KeyboardDarumaInput : MonoBehaviour, IDarumaCommandSource
{
    Transform m_camera;

    public DarumaCommand ReadCommand()
    {
        var k = Keyboard.current;
        if (k == null) return default;
        if (m_camera == null && Camera.main != null) m_camera = Camera.main.transform;

        float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
        float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
        Vector2 stick = Vector2.ClampMagnitude(new Vector2(x, y), 1f);

        return new DarumaCommand
        {
            Move = ToWorldMove(stick),
            Jump = k.spaceKey.wasPressedThisFrame,
            Sweep = k.jKey.wasPressedThisFrame,
            Shot = k.kKey.wasPressedThisFrame,
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
