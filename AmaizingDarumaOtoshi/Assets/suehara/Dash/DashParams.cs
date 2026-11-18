using UnityEngine;

// ダッシュとスタミナの調整値(全キャラ共通)
// 実体は Assets/suehara/Dash/Resources/DashParams.asset。値は使うたびに読むので、再生中に変えてもすぐ反映される
[CreateAssetMenu(fileName = "DashParams", menuName = "suehara/Dash Params")]
public class DashParams : ScriptableObject
{
    [Tooltip("ダッシュ中の移動速度の倍率")]
    [Min(1.0f)] public float SpeedMultiplier = 1.6f;
    [Tooltip("スタミナ満タンから走り続けられる時間(秒)")]
    [Min(0.1f)] public float StaminaSeconds = 2.5f;
    [Tooltip("スタミナが空から満タンまで回復する時間(秒)")]
    [Min(0.1f)] public float RecoverSeconds = 3.0f;
    [Tooltip("走るのをやめてから回復が始まるまでの時間(秒)")]
    [Min(0.0f)] public float RecoverDelay = 0.5f;
    [Tooltip("スタミナ切れのあと、再び走れるようになるスタミナの割合(0〜1)")]
    [Range(0.0f, 1.0f)] public float RestartRatio = 0.3f;

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
