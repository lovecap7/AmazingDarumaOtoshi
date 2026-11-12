using System.Reflection;
using Nakagawa;
using Nakahira;
using Unity.Netcode;
using UnityEngine;

// 脱落した瞬間の演出(中川さんの EliminationCinematic: カメラが寄る・K.O. のエフェクト)を、クライアントでも出す。
// NetMatch と同じ GameObject(シーンの NetworkObject)に付ける。
//  ・ホスト      : 脱落(DarumaCharacter.AnyEliminated。ホストでしか起きない)を、原因と場所といっしょにクライアントへ送る。
//                  ホストの演出は EliminationCinematic がそのまま出す。演出を止めているとき(いわだるの隕石など)は送らない
//  ・クライアント: 届いた脱落で、EliminationCinematic と同じ設定・同じ流れでエフェクトを出し、カメラを寄せ、スローにする。
//                  (位置はホストの遅くなった動きが届くが、ハンマーの振り・エフェクトなどクライアントで動かす見た目は
//                   クライアントの時間で進むので、クライアントの Time.timeScale も同じように落とす)
// EliminationCinematic の設定はシーンのものをそのまま読む(中川さんのファイルは変えない)
public class NetEliminationSync : NetworkBehaviour
{
    const BindingFlags kPrivate = BindingFlags.NonPublic | BindingFlags.Instance;

    EliminationCinematic m_cinematic;
    MatchCamera m_camera;

    // クライアント: カメラの寄りの進み具合(実時間)
    bool m_playing;
    float m_time;
    Vector3 m_focus;

    // クライアント: 時間の速さを管理しているか(EliminationCinematic と同じ。他が変えたら手を引く)
    bool m_ownsTime;
    float m_baseTimeScale = 1.0f;
    float m_baseFixedDeltaTime = 0.02f;
    float m_appliedTimeScale = -1.0f;

    public override void OnNetworkSpawn()
    {
        if (IsServer) DarumaCharacter.AnyEliminated += OnEliminated;
    }

    public override void OnNetworkDespawn()
    {
        DarumaCharacter.AnyEliminated -= OnEliminated;
        StopZoom();
    }

    public override void OnDestroy()
    {
        DarumaCharacter.AnyEliminated -= OnEliminated;
        StopZoom();
        base.OnDestroy();
    }

    EliminationCinematic Cinematic
    {
        get
        {
            if (m_cinematic == null) m_cinematic = FindAnyObjectByType<EliminationCinematic>();
            return m_cinematic;
        }
    }

    T Setting<T>(string field)
    {
        var f = typeof(EliminationCinematic).GetField(field, kPrivate);
        return f != null && Cinematic != null ? (T)f.GetValue(Cinematic) : default;
    }

    // ホスト: 脱落をクライアントへ送る
    void OnEliminated(DarumaCharacter character)
    {
        if (!IsSpawned || character == null) return;
        var cinematic = Cinematic;
        if (cinematic == null || !cinematic.isActiveAndEnabled) return;
        EliminatedRpc((byte)character.EliminationCause, character.transform.position, character.EliminationPosition);
    }

    // クライアント: EliminationCinematic.OnEliminated と同じ流れ(時間の速さには触れない)
    [Rpc(SendTo.NotServer)]
    void EliminatedRpc(byte causeValue, Vector3 position, Vector3 eliminationPosition)
    {
        if (Cinematic == null || !Setting<bool>("m_enabled")) return;
        var cause = (EliminationCause)causeValue;
        float focusHeight = Setting<float>("m_focusHeight");
        if (cause == EliminationCause.Fall) SpawnFallEffect(eliminationPosition);
        if (!ShouldPlay(cause)) return;
        if (cause == EliminationCause.Fall)
        {
            StartZoom(eliminationPosition + Vector3.up * focusHeight);
            return;
        }
        var effect = Setting<GameObject>("m_effectPrefab");
        if (effect != null) Destroy(Instantiate(effect, position, Quaternion.identity), Setting<float>("m_effectLifetime"));
        StartZoom(position + Vector3.up * focusHeight);
    }

    void SpawnFallEffect(Vector3 position)
    {
        var fall = Setting<GameObject>("m_fallEffectPrefab");
        var prefab = fall != null ? fall : Setting<GameObject>("m_effectPrefab");
        if (!Setting<bool>("m_effectOnFall") || prefab == null) return;
        var effect = Instantiate(prefab, position, Quaternion.identity);
        if (Setting<bool>("m_hideTextOnFall"))
        {
            foreach (var popup in effect.GetComponentsInChildren<EffectPopup>(true)) popup.HideText();
        }
        Destroy(effect, Setting<float>("m_effectLifetime"));
    }

    bool ShouldPlay(EliminationCause cause)
    {
        switch (cause)
        {
            case EliminationCause.Projectile:
            case EliminationCause.Hammer:
                return Setting<bool>("m_onAttack");
            case EliminationCause.Fall:
                return Setting<bool>("m_onFall");
            default:
                return Setting<bool>("m_onOther");
        }
    }

    void StartZoom(Vector3 focus)
    {
        if (m_camera == null && Cinematic != null) m_camera = Cinematic.GetComponent<MatchCamera>();
        if (m_camera == null) return;
        if (!m_playing)
        {
            m_baseTimeScale = Time.timeScale;
            m_baseFixedDeltaTime = Time.fixedDeltaTime;
            // 止まっている(必殺技のカットイン中など)ときは時間には触れない
            m_ownsTime = m_baseTimeScale > 0.0f;
        }
        m_focus = focus;
        m_time = 0.0f;
        m_playing = true;
        ApplyZoom();
    }

    private void Update()
    {
        if (!m_playing) return;
        m_time += Time.unscaledDeltaTime;
        ApplyZoom();
    }

    // EliminationCinematic.Apply と同じ(時間の速さ・クローズアップ)
    void ApplyZoom()
    {
        if (m_camera == null) { StopZoom(); return; }
        float slow = Setting<float>("m_slowDuration");

        if (m_ownsTime && m_appliedTimeScale >= 0.0f && !Mathf.Approximately(Time.timeScale, m_appliedTimeScale))
        {
            // 他が時間の速さを変えた: そちらを優先し、物理の刻みだけ元に戻す
            m_ownsTime = false;
            Time.fixedDeltaTime = m_baseFixedDeltaTime;
        }
        float slowScale = Setting<float>("m_slowTimeScale");
        float rate = m_time < slow
            ? slowScale
            : Mathf.Lerp(slowScale, 1.0f, Smooth((m_time - slow) / Mathf.Max(0.0001f, Setting<float>("m_recoverDuration"))));
        if (m_ownsTime)
        {
            Time.timeScale = m_baseTimeScale * rate;
            Time.fixedDeltaTime = m_baseFixedDeltaTime * rate;
            m_appliedTimeScale = Time.timeScale;
        }

        float weight = m_time < slow
            ? Smooth(m_time / Mathf.Max(0.01f, Setting<float>("m_zoomInTime")))
            : 1.0f - Smooth((m_time - slow) / Mathf.Max(0.01f, Setting<float>("m_zoomOutTime")));
        Vector3 position = m_focus - m_camera.BaseRotation * Vector3.forward * Setting<float>("m_closeDistance");
        m_camera.SetOverride(position, Setting<float>("m_closeFov"), weight);
        if (m_time >= slow + Mathf.Max(Setting<float>("m_recoverDuration"), Setting<float>("m_zoomOutTime"))) StopZoom();
    }

    void StopZoom()
    {
        if (!m_playing) return;
        m_playing = false;
        if (m_ownsTime)
        {
            Time.timeScale = m_baseTimeScale;
            Time.fixedDeltaTime = m_baseFixedDeltaTime;
        }
        m_ownsTime = false;
        m_appliedTimeScale = -1.0f;
        if (m_camera != null) m_camera.ClearOverride();
    }

    static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3.0f - 2.0f * t);
    }
}
