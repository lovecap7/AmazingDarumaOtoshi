using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// テスト用の入力。
//   スペースキー：積み木の「任意のタイミングで発動する効果」を呼ぶ(テレポートなど)
//   Hキー      ：飛んでいる積み木を打ち返す(進行方向を反転する。偶数回目の打ち返しで攻撃力+1)
// 本実装では、プレイヤー側の入力が BlockController.Activate() と HitBack() を呼ぶ想定
public class DebugActivateInput : MonoBehaviour
{
    BlockController controller;

    void Awake()
    {
        controller = GetComponent<BlockController>();
    }

    void Update()
    {
        if (WasPressed(KeyCode.Space)) controller.Activate();
        if (WasPressed(KeyCode.H)) controller.HitBack(-controller.Direction);
    }

    // 入力の設定(Input Manager / Input System)のどちらでも、同じキー指定で押した瞬間を取れるようにする
    static bool WasPressed(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;

        switch (key)
        {
            case KeyCode.Space: return kb.spaceKey.wasPressedThisFrame;
            case KeyCode.H: return kb.hKey.wasPressedThisFrame;
            default: return false;
        }
#else
        return Input.GetKeyDown(key);
#endif
    }
}