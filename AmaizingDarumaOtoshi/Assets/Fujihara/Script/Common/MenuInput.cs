using UnityEngine;
using UnityEngine.InputSystem;

// メニュー用の入力まとめ。キーボードと、つながっている全コントローラーを見る。
public static class MenuInput
{
    public static bool Up()
    {
        var k = Keyboard.current;
        if (k != null && (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame)) return true;
        foreach (var g in Gamepad.all)
            if (g.dpad.up.wasPressedThisFrame || g.leftStick.up.wasPressedThisFrame) return true;
        return false;
    }

    public static bool Down()
    {
        var k = Keyboard.current;
        if (k != null && (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame)) return true;
        foreach (var g in Gamepad.all)
            if (g.dpad.down.wasPressedThisFrame || g.leftStick.down.wasPressedThisFrame) return true;
        return false;
    }

    public static bool Left()
    {
        var k = Keyboard.current;
        if (k != null && (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame)) return true;
        foreach (var g in Gamepad.all)
            if (g.dpad.left.wasPressedThisFrame || g.leftStick.left.wasPressedThisFrame) return true;
        return false;
    }

    public static bool Right()
    {
        var k = Keyboard.current;
        if (k != null && (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame)) return true;
        foreach (var g in Gamepad.all)
            if (g.dpad.right.wasPressedThisFrame || g.leftStick.right.wasPressedThisFrame) return true;
        return false;
    }

    // Start ボタン / Enter（「決定して次へ」）
    public static bool Start()
    {
        var k = Keyboard.current;
        if (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) return true;
        foreach (var g in Gamepad.all)
            if (g.startButton.wasPressedThisFrame) return true;
        return false;
    }

    public static bool Submit()
    {
        var k = Keyboard.current;
        if (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame)) return true;
        foreach (var g in Gamepad.all)
            if (g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame) return true;
        return false;
    }

    public static bool Cancel()
    {
        var k = Keyboard.current;
        if (k != null && (k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame)) return true;
        foreach (var g in Gamepad.all)
            if (g.buttonEast.wasPressedThisFrame) return true;
        var m = Mouse.current;
        if (m != null && m.rightButton.wasPressedThisFrame) return true;
        return false;
    }

    public static bool AnyClick()
    {
        var m = Mouse.current;
        return m != null && m.leftButton.wasPressedThisFrame;
    }
}
