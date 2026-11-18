using UnityEngine;

// ダッシュのスタミナの計算。キャラクターが物理ステップごとに Tick を呼ぶ
public sealed class DashStamina
{
    float m_recoverWait;

    // 残りのスタミナ(0〜1)
    public float Ratio { get; private set; } = 1.0f;
    // スタミナ切れ。一定量まで回復し、ボタンを離すまで走れない
    public bool IsExhausted { get; private set; }
    // このステップで走っているか
    public bool IsDashing { get; private set; }

    // wantDash: ダッシュボタンを押して移動している
    public bool Tick(bool wantDash, float dt, DashParams param)
    {
        // 押しっぱなしのまま回復と消費を細かく繰り返さないよう、離すまで再開しない
        if (IsExhausted && !wantDash && Ratio >= param.RestartRatio) IsExhausted = false;

        IsDashing = wantDash && !IsExhausted;
        if (IsDashing)
        {
            Ratio = Mathf.Max(0.0f, Ratio - dt / param.StaminaSeconds);
            m_recoverWait = param.RecoverDelay;
            if (Ratio <= 0.0f) IsExhausted = true;
            return true;
        }

        if (m_recoverWait > 0.0f)
        {
            m_recoverWait -= dt;
            return false;
        }
        Ratio = Mathf.Min(1.0f, Ratio + dt / param.RecoverSeconds);
        return false;
    }
}
