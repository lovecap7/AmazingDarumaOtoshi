using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// テスト用：スペースキーで、積み木の「任意のタイミングで発動する効果」を呼ぶ
// 本実装では、プレイヤー側の入力が BlockController.Activate() を呼ぶ想定
public class DebugActivateInput : MonoBehaviour
{
    BlockController controller;

    void Awake()
    {
        controller = GetComponent<BlockController>();
    }

    void Update()
    {
        bool pressed;
#if ENABLE_INPUT_SYSTEM
        pressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        pressed = Input.GetKeyDown(KeyCode.Space);
#endif
        if (pressed) controller.Activate();
    }
}