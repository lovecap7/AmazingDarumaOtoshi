using UnityEngine;
using UnityEngine.InputSystem;

// メニュー用の入力まとめ。キーボードと、つながっている全コントローラーを見る。
// Up / Down / Left / Right は押した瞬間だけ。UpRepeat などは長押しでくり返す
// （押した瞬間に1回 → RepeatDelay 秒後から RepeatInterval 秒ごと）。全体でくり返しを止めたいときは RepeatEnabled = false。
public static class MenuInput
{
    public enum Dir { Up, Down, Left, Right }

    // 長押しでくり返すか
    public static bool RepeatEnabled = true;
    // 押してからくり返し始めるまでの秒数
    public static float RepeatDelay = 0.4f;
    // くり返しの間隔（秒）。長く押すほど RepeatIntervalFast まで速くなる
    public static float RepeatInterval = 0.11f;
    public static float RepeatIntervalFast = 0.06f;
    // RepeatIntervalFast になるまでの、くり返し開始からの秒数
    public static float RepeatAccelerate = 1.2f;

    // 押した瞬間だけ（くり返さない。2択の切り替えなど、くり返すと困るところ用）
    public static bool Up() => Pressed(Dir.Up);
    public static bool Down() => Pressed(Dir.Down);
    public static bool Left() => Pressed(Dir.Left);
    public static bool Right() => Pressed(Dir.Right);

    // 長押しでくり返す（カーソル移動・値の切り替え用）
    public static bool UpRepeat() => Repeat(Dir.Up);
    public static bool DownRepeat() => Repeat(Dir.Down);
    public static bool LeftRepeat() => Repeat(Dir.Left);
    public static bool RightRepeat() => Repeat(Dir.Right);
    public static bool Repeat(Dir d, bool repeat) => repeat ? Repeat(d) : Pressed(d);

    // 押しているあいだ true
    public static bool Held(Dir d)
    {
        var k = Keyboard.current;
        if (k != null)
        {
            switch (d)
            {
                case Dir.Up: if (k.upArrowKey.isPressed || k.wKey.isPressed) return true; break;
                case Dir.Down: if (k.downArrowKey.isPressed || k.sKey.isPressed) return true; break;
                case Dir.Left: if (k.leftArrowKey.isPressed || k.aKey.isPressed) return true; break;
                case Dir.Right: if (k.rightArrowKey.isPressed || k.dKey.isPressed) return true; break;
            }
        }
        foreach (var g in Gamepad.all)
        {
            switch (d)
            {
                case Dir.Up: if (g.dpad.up.isPressed || g.leftStick.up.isPressed) return true; break;
                case Dir.Down: if (g.dpad.down.isPressed || g.leftStick.down.isPressed) return true; break;
                case Dir.Left: if (g.dpad.left.isPressed || g.leftStick.left.isPressed) return true; break;
                case Dir.Right: if (g.dpad.right.isPressed || g.leftStick.right.isPressed) return true; break;
            }
        }
        return false;
    }

    static bool Pressed(Dir d)
    {
        var k = Keyboard.current;
        if (k != null)
        {
            switch (d)
            {
                case Dir.Up: if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) return true; break;
                case Dir.Down: if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) return true; break;
                case Dir.Left: if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) return true; break;
                case Dir.Right: if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) return true; break;
            }
        }
        foreach (var g in Gamepad.all)
        {
            switch (d)
            {
                case Dir.Up: if (g.dpad.up.wasPressedThisFrame || g.leftStick.up.wasPressedThisFrame) return true; break;
                case Dir.Down: if (g.dpad.down.wasPressedThisFrame || g.leftStick.down.wasPressedThisFrame) return true; break;
                case Dir.Left: if (g.dpad.left.wasPressedThisFrame || g.leftStick.left.wasPressedThisFrame) return true; break;
                case Dir.Right: if (g.dpad.right.wasPressedThisFrame || g.leftStick.right.wasPressedThisFrame) return true; break;
            }
        }
        return false;
    }

    // ---- 長押しのくり返し（1フレームに何回呼んでも同じ答えになるよう、フレームごとに1回だけ計算する） ----
    static readonly float[] holdStart = new float[4];
    static readonly float[] nextRepeat = new float[4];
    static readonly bool[] wasHeld = new bool[4];
    static readonly bool[] result = new bool[4];
    static readonly int[] computedFrame = { -1, -1, -1, -1 };

    static bool Repeat(Dir d)
    {
        int i = (int)d;
        int frame = Time.frameCount;
        if (computedFrame[i] == frame) return result[i];
        float now = Time.unscaledTime;
        bool pressed = Pressed(d);
        bool held = pressed || Held(d);
        // しばらく見ていなかった（画面が切り替わった直後など）ときは、押しっぱなしの分をくり返さず、新しく押すのを待つ
        if (computedFrame[i] != frame - 1 && !pressed)
        {
            wasHeld[i] = held;
            nextRepeat[i] = float.MaxValue;
        }
        computedFrame[i] = frame;

        bool fire = false;
        if (pressed || (held && !wasHeld[i]))
        {
            fire = true;
            holdStart[i] = now;
            nextRepeat[i] = now + RepeatDelay;
        }
        else if (held && RepeatEnabled && now >= nextRepeat[i])
        {
            fire = true;
            float t = Mathf.Clamp01((now - holdStart[i] - RepeatDelay) / Mathf.Max(0.01f, RepeatAccelerate));
            nextRepeat[i] = now + Mathf.Lerp(RepeatInterval, RepeatIntervalFast, t);
        }
        wasHeld[i] = held;
        result[i] = fire;
        return fire;
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

    // B（キャンセル）を押し続けているあいだ true（早送りなど）
    public static bool CancelHeld()
    {
        var k = Keyboard.current;
        if (k != null && (k.escapeKey.isPressed || k.backspaceKey.isPressed)) return true;
        foreach (var g in Gamepad.all)
            if (g.buttonEast.isPressed) return true;
        var m = Mouse.current;
        return m != null && m.rightButton.isPressed;
    }

    public static bool AnyClick()
    {
        var m = Mouse.current;
        return m != null && m.leftButton.wasPressedThisFrame;
    }
}
