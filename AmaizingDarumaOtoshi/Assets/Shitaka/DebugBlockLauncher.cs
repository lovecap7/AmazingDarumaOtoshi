using UnityEngine;
using UnityEngine.InputSystem;


// プロト確認用のテスト入力。テンキー(5を除く1~9)を押した方向へ、発射する
// BlockControllerのLaunchを呼ぶ
// プレイヤー担当のハンマーの処理が完成したら不要になるため、本実装時に削除
public class DebugBlockLauncher:MonoBehaviour
{
    BlockController controller;

    // テンキーの入力に応じて、8方向にブロックを発射する
    static readonly (Key key, Vector3 dir)[] keyDirections =
    {
        (Key.Numpad8, new Vector3( 0, 0,  1)),
        (Key.Numpad2, new Vector3( 0, 0, -1)),
        (Key.Numpad4, new Vector3(-1, 0,  0)),
        (Key.Numpad6, new Vector3( 1, 0,  0)),
        (Key.Numpad7, new Vector3(-1, 0,  1)),
        (Key.Numpad9, new Vector3( 1, 0,  1)),
        (Key.Numpad1, new Vector3(-1, 0, -1)),
        (Key.Numpad3, new Vector3( 1, 0, -1)),
    };

    void Awake()
    {
        controller = GetComponent<BlockController>();
    }

    void Update()
    {
        if (Keyboard.current == null) return; // キーボード未接続時のnull対策

        // 登録されたキーと方向の組み合わせを順番に確認
        foreach (var (key, dir) in keyDirections)
        {
            // 対応しているテンキーが押された場合
            if (Keyboard.current[key].wasPressedThisFrame)
            {
                // 方向を正規化して発射する
                controller.Launch(dir.normalized);
                break;
            }
        }
    }
}
