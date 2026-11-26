using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube
{
    // デバッグ用: 試合中、ゲームパッドの LB を押した人の必殺ゲージを満タンにする(エディタと Development Build だけ)
    //  ・押したゲームパッドが割り当てられているプレイヤーだけが満タンになる(CPU・ほかの人は変わらない)
    //  ・LT を押すと、押した人のレベルが最大(Lv5)になる(suehara)。ネット対戦のクライアントでは効かない
    // シーンに置かなくても、起動時に自動で付く(試合中だけ働く)
    public class SpecialGaugeDebugButton : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static SpecialGaugeDebugButton s_instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("SpecialGaugeDebugButton");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<SpecialGaugeDebugButton>();
        }

        private void Update()
        {
            var match = DarumaMatch.Current;
            if (match == null || match.IsFinished) return;

            foreach (var p in match.Players)
            {
                if (p == null || p.IsCpu || p.Character == null) continue;
                // LT: 押した人のレベルを最大(Lv5)にする(suehara)
                if (Pressed(p, pad => pad.leftTrigger.wasPressedThisFrame) && DarumaLevel.DebugSetMaxLevel(p))
                    Debug.Log($"[デバッグ] {p.Label} のレベルを最大(Lv{LevelParams.MaxLevel})にした(LT)");
                if (!PressedLB(p)) continue;

                var gauge = p.Character.GetComponent<SpecialGauge>();
                // ネット対戦のクライアント側はホストの値で上書きされるので触らない
                if (gauge == null || gauge.NetDriven) continue;
                gauge.Fill();
                Debug.Log($"[デバッグ] {p.Label} の必殺ゲージを満タンにした(LB)");
            }
        }

        // そのプレイヤーに割り当てられたゲームパッドで、このフレームに LB が押されたか
        static bool PressedLB(MatchPlayer p) => Pressed(p, pad => pad.leftShoulder.wasPressedThisFrame);

        // そのプレイヤーに割り当てられたゲームパッドで、このフレームにボタンが押されたか(suehara: LT と共用にした)
        static bool Pressed(MatchPlayer p, System.Func<Gamepad, bool> pressed)
        {
            var input = p.GetComponent<PlayerInput>();
            if (input == null) return false;
            foreach (var device in input.devices)
            {
                if (device is Gamepad pad && pressed(pad)) return true;
            }
            return false;
        }
#endif
    }
}
