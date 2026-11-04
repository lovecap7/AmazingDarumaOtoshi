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
        public static bool WasPressed(DarumaCharacter character)
        {
            if (character == null) return false;

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

        static bool IsKeyboardSpecialOwner(MatchPlayer me)
        {
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
