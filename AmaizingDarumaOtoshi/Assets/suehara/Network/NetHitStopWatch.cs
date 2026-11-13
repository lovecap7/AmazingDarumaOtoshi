using System.Reflection;
using Nakahira;
using UnityEngine;

// ヒットストップ(中平さんの HitStop。当たった瞬間に少し止まって震える)を、クライアントでも出すための見張り。
// HitStop.Begin はホストの当たりの処理から呼ばれるので、クライアントでは起きない。
// ホストで、HitStop の残り時間が増えた(新しく始まった・延びた)フレームを見つけて、その長さ・時間の倍率・震えの強さを返す。
// 送るのは NetworkHammerView(キャラ・幽霊)と NetworkTumiki(積み木)。クライアントは同じ HitStop.Begin を呼ぶ
// (震えは見た目だけ、時間の倍率はクライアントのハンマーの振りの見た目が読む)
public class NetHitStopWatch
{
    const BindingFlags kPrivate = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly FieldInfo s_timer = typeof(HitStop).GetField("m_timer", kPrivate);
    static readonly FieldInfo s_timeScale = typeof(HitStop).GetField("m_timeScale", kPrivate);
    static readonly FieldInfo s_shake = typeof(HitStop).GetField("m_shake", kPrivate);

    readonly HitStop m_hitStop;
    float m_lastTimer;

    public NetHitStopWatch(HitStop hitStop)
    {
        m_hitStop = hitStop;
    }

    // ホスト: 新しく始まった(延びた)なら true と、その中身
    public bool Poll(out float duration, out float timeScale, out float shake)
    {
        duration = timeScale = shake = 0.0f;
        if (m_hitStop == null || s_timer == null) return false;
        float timer = (float)s_timer.GetValue(m_hitStop);
        bool started = timer > m_lastTimer + 0.0001f;
        m_lastTimer = timer;
        if (!started) return false;
        duration = timer;
        timeScale = s_timeScale != null ? (float)s_timeScale.GetValue(m_hitStop) : 1.0f;
        shake = s_shake != null ? (float)s_shake.GetValue(m_hitStop) : 0.0f;
        return true;
    }
}
