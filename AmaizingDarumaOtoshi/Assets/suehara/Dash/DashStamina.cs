using UnityEngine;

// ダッシュのスタミナの計算。キャラクターが物理ステップごとに Tick を呼ぶ
public sealed class DashStamina
{
    const float kEpsilon = 0.0001f;

    float m_recoverWait;

    // 残りのスタミナ(0〜1)
    public float Ratio { get; private set; } = 1.0f;
    // スタミナ切れ。一定量まで回復し、ボタンを離すまで走れない
    public bool IsExhausted { get; private set; }
    // このステップで走っているか
    public bool IsDashing { get; private set; }

    // ジャンプなどで一度にスタミナを使う。足りなければ何もせず false
    public bool TryConsume(float cost, DashParams param)
    {
        // 小数の誤差で最後の1回分が足りなくならないように、わずかな差は足りているとみなす
        if (Ratio + kEpsilon < cost) return false;
        Ratio -= cost;
        if (Ratio <= kEpsilon) Ratio = 0.0f;
        m_recoverWait = param.RecoverDelay;
        if (Ratio <= 0.0f) IsExhausted = true;
        return true;
    }

    // 積み木に乗ったときなどに、一度にスタミナを回復する
    public void Recover(float amount)
    {
        Ratio = Mathf.Min(1.0f, Ratio + amount);
    }

    // wantDash: ダッシュボタンを押して移動している / canRecover: 回復できる状態か(空中では回復しない)
    public bool Tick(bool wantDash, bool canRecover, float dt, DashParams param)
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

        // 着地してから回復の待ち時間を数え始める
        if (!canRecover)
        {
            m_recoverWait = param.RecoverDelay;
            return false;
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
