using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube
{
    // 必殺技ボタンの入力を、そのプレイヤーに割り当てられた機器だけで判定する。
    // これまでは Gamepad.current / Keyboard.current を全員が読んでいたため、
    // 2Pで1人が押すと全員の必殺技が発動していた。
    public static class SpecialInput
    {
        // ストーリー用(Yurube): 会話中など、必殺技を受け付けない
        public static bool Suspended;

        public static bool WasPressed(DarumaCharacter character)
        {
            if (character == null) return false;
            if (Suspended) return false;
            // ポーズ中は必殺技を出さない（ポーズ画面の後ろで発動してしまうため）
            if (DarumaMatch.Current != null && DarumaMatch.Current.IsPaused) return false;

            if (DarumaMatch.Current != null)
            {
                MatchPlayer me = null;
                foreach (var p in DarumaMatch.Current.Players)
                {
                    if (p.Character == character)
                    {
                        me = p;
                        break;
                    }
                }

                if (me == null || me.IsCpu) return false;

                // ゲームパッド参加: その人に割り当てられたパッドだけを見る
                var playerInput = me.GetComponent<PlayerInput>();
                if (playerInput != null)
                {
                    bool hasGamepad = false;
                    foreach (var device in playerInput.devices)
                    {
                        if (device is Gamepad pad)
                        {
                            hasGamepad = true;
                            if (pad.buttonNorth.wasPressedThisFrame) return true;
                        }
                    }

                    if (hasGamepad) return false;

                    // キーボード参加: 複数キーボード参加時に全員が反応しないよう、
                    // 最も小さいプレイヤー番号のキーボード担当だけ L を受け付ける
                    bool hasKeyboard = false;
                    foreach (var device in playerInput.devices)
                    {
                        if (device is Keyboard)
                        {
                            hasKeyboard = true;
                            break;
                        }
                    }

                    if (hasKeyboard && IsKeyboardSpecialOwner(me))
                        return Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame;

                    return false;
                }

                if (me.GetComponent<KeyboardDarumaInput>() != null)
                    return IsKeyboardSpecialOwner(me)
                        && Keyboard.current != null
                        && Keyboard.current.lKey.wasPressedThisFrame;

                // ネット対戦など、機器が特定できない入力元はここでは拾わない
                return false;
            }

            // ラボなど試合外の1人確認用
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.lKey.wasPressedThisFrame) return true;
            var gamepad = Gamepad.current;
            return gamepad != null && gamepad.buttonNorth.wasPressedThisFrame;
        }

        // 詠唱完了用。移動スティック／移動キーは除外し、行動ボタンだけを見る。
        public static bool WasAnyActionPressed(DarumaCharacter character)
        {
            if (character == null) return false;
            if (Suspended) return false;
            if (DarumaMatch.Current != null && DarumaMatch.Current.IsPaused) return false;

            if (DarumaMatch.Current != null)
            {
                MatchPlayer me = null;
                foreach (var p in DarumaMatch.Current.Players)
                {
                    if (p.Character == character)
                    {
                        me = p;
                        break;
                    }
                }

                if (me == null || me.IsCpu) return false;

                var playerInput = me.GetComponent<PlayerInput>();
                if (playerInput != null)
                {
                    bool hasGamepad = false;
                    foreach (var device in playerInput.devices)
                    {
                        if (device is Gamepad pad)
                        {
                            hasGamepad = true;
                            if (AnyGamepadAction(pad)) return true;
                        }
                    }

                    if (hasGamepad) return false;

                    bool hasKeyboard = false;
                    foreach (var device in playerInput.devices)
                    {
                        if (device is Keyboard) hasKeyboard = true;
                    }

                    if (hasKeyboard && IsKeyboardSpecialOwner(me))
                        return AnyKeyboardAction(Keyboard.current);

                    return false;
                }

                if (me.GetComponent<KeyboardDarumaInput>() != null)
                    return IsKeyboardSpecialOwner(me) && AnyKeyboardAction(Keyboard.current);

                return false;
            }

            // ラボなど試合外
            if (AnyKeyboardAction(Keyboard.current)) return true;
            return AnyGamepadAction(Gamepad.current);
        }

        static bool AnyGamepadAction(Gamepad pad)
        {
            if (pad == null) return false;
            return pad.buttonSouth.wasPressedThisFrame
                || pad.buttonEast.wasPressedThisFrame
                || pad.buttonWest.wasPressedThisFrame
                || pad.buttonNorth.wasPressedThisFrame
                || pad.leftShoulder.wasPressedThisFrame
                || pad.rightShoulder.wasPressedThisFrame
                || pad.leftTrigger.wasPressedThisFrame
                || pad.rightTrigger.wasPressedThisFrame
                || pad.startButton.wasPressedThisFrame
                || pad.selectButton.wasPressedThisFrame;
        }

        static bool AnyKeyboardAction(Keyboard keyboard)
        {
            if (keyboard == null) return false;
            return keyboard.spaceKey.wasPressedThisFrame
                || keyboard.jKey.wasPressedThisFrame
                || keyboard.kKey.wasPressedThisFrame
                || keyboard.lKey.wasPressedThisFrame
                || keyboard.enterKey.wasPressedThisFrame;
        }

        static bool IsKeyboardSpecialOwner(MatchPlayer me)        {
            if (DarumaMatch.Current == null) return true;

            int ownerIndex = int.MaxValue;
            foreach (var p in DarumaMatch.Current.Players)
            {
                if (p.IsCpu || p.Character == null) continue;

                bool keyboard = false;
                var playerInput = p.GetComponent<PlayerInput>();
                if (playerInput != null)
                {
                    bool hasGamepad = false;
                    foreach (var device in playerInput.devices)
                    {
                        if (device is Gamepad)
                        {
                            hasGamepad = true;
                            break;
                        }
                    }

                    if (!hasGamepad)
                    {
                        foreach (var device in playerInput.devices)
                        {
                            if (device is Keyboard)
                            {
                                keyboard = true;
                                break;
                            }
                        }
                    }
                }
                else if (p.GetComponent<KeyboardDarumaInput>() != null)
                {
                    keyboard = true;
                }

                if (keyboard) ownerIndex = Mathf.Min(ownerIndex, p.Index);
            }

            return ownerIndex != int.MaxValue && me.Index == ownerIndex;
        }
    }
}
