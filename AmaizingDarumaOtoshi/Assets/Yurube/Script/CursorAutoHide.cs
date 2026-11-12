using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube
{
    // ゲームパッド／キーボードで操作している間はマウスカーソルを隠し、
    // マウスを動かす・クリックしたら戻す。メニューはマウスでも操作できるので、
    // マウスを使う人は今までどおりカーソルが見える。
    public class CursorAutoHide : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            if (FindAnyObjectByType<CursorAutoHide>() != null) return;
            var go = new GameObject("CursorAutoHide");
            DontDestroyOnLoad(go);
            go.AddComponent<CursorAutoHide>();
        }

        Vector2 m_lastMouse;
        bool m_hidden;

        void Awake()
        {
            m_lastMouse = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 position = mouse.position.ReadValue();
                bool moved = (position - m_lastMouse).sqrMagnitude > 4f;
                m_lastMouse = position;
                if (moved || mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                {
                    Show();
                    return;
                }
            }

            if (AnyGamepadInput() || AnyKeyboardInput()) Hide();
        }

        static bool AnyGamepadInput()
        {
            foreach (var pad in Gamepad.all)
            {
                if (pad.leftStick.ReadValue().sqrMagnitude > 0.04f) return true;
                if (pad.rightStick.ReadValue().sqrMagnitude > 0.04f) return true;
                if (pad.dpad.up.isPressed || pad.dpad.down.isPressed
                    || pad.dpad.left.isPressed || pad.dpad.right.isPressed) return true;
                if (pad.buttonSouth.isPressed || pad.buttonEast.isPressed
                    || pad.buttonWest.isPressed || pad.buttonNorth.isPressed) return true;
                if (pad.leftShoulder.isPressed || pad.rightShoulder.isPressed
                    || pad.leftTrigger.isPressed || pad.rightTrigger.isPressed) return true;
                if (pad.startButton.isPressed || pad.selectButton.isPressed) return true;
            }
            return false;
        }

        static bool AnyKeyboardInput()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.anyKey.wasPressedThisFrame;
        }

        static void Hide()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = false;
        }

        static void Show()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
