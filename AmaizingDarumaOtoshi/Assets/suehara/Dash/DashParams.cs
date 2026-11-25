using UnityEngine;

// ダッシュとスタミナの調整値(全キャラ共通)
// 実体は Assets/suehara/Dash/Resources/DashParams.asset。値は使うたびに読むので、再生中に変えてもすぐ反映される
[CreateAssetMenu(fileName = "DashParams", menuName = "suehara/Dash Params")]
public class DashParams : ScriptableObject
{
    [Tooltip("ダッシュ中の移動速度の倍率")]
    [Min(1.0f)] public float SpeedMultiplier = 1.6f;
    [Tooltip("スタミナ満タンから走り続けられる時間(秒)")]
    [Min(0.1f)] public float StaminaSeconds = 3.0f;
    [Tooltip("スタミナが空から満タンまで回復する時間(秒)")]
    [Min(0.1f)] public float RecoverSeconds = 3.0f;
    [Tooltip("走るのをやめてから回復が始まるまでの時間(秒)")]
    [Min(0.0f)] public float RecoverDelay = 0.5f;
    [Tooltip("スタミナ切れのあと、再び走れるようになるスタミナの割合(0〜1)")]
    [Range(0.0f, 1.0f)] public float RestartRatio = 0.3f;
    [Tooltip("ジャンプ1回で消費するスタミナの割合(0〜1)。残りがこれより少ないとジャンプできない")]
    [Range(0.0f, 1.0f)] public float JumpCostRatio = 0.125f;
    [Tooltip("積み木に上から乗って拾ったときに回復するスタミナの割合(0〜1)")]
    [Range(0.0f, 1.0f)] public float PickupRecoverRatio = 0.25f;

    [Header("ブリンク(ダッシュボタンを素早く2回押す)")]
    [Tooltip("瞬間移動する距離(m)。壁もすり抜けるが、壁の中・ステージの範囲の外に出る場合は手前で止まる")]
    [Min(0.5f)] public float BlinkDistance = 6.0f;
    [Tooltip("ブリンク1回で消費するスタミナの割合(0〜1)。残りがこれより少ないと出ない")]
    [Range(0.0f, 1.0f)] public float BlinkCostRatio = 0.5f;
    [Tooltip("ブリンクしてから次に使えるまでの時間(秒)")]
    [Min(0.0f)] public float BlinkCooldown = 0.5f;
    [Tooltip("2回押しとみなす、1回目から2回目までの時間(秒)")]
    [Min(0.05f)] public float BlinkDoubleTapWindow = 0.25f;

    static DashParams s_instance;

    public static DashParams Instance
    {
        get
        {
            if (s_instance != null) return s_instance;
            s_instance = Resources.Load<DashParams>("DashParams");
            // アセットが無くても初期値で動くようにする
            if (s_instance == null) s_instance = CreateInstance<DashParams>();
            return s_instance;
        }
    }
}
