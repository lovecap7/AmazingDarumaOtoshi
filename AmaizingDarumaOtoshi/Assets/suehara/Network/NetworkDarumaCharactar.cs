using System.Reflection;
using Nakahira;
using Unity.Netcode;
using UnityEngine;

// ネットワーク対応のキャラクターに付ける。
// 物理・当たり判定・ルールはホストだけで動かし、クライアントは表示専用にする。
// (クライアントでも動いたままだと、奈落判定の Eliminate → Destroy などがクライアント側で勝手に走るため)
//  ・頭(本体)の高さ: 積み木の段数をホストから受け取り、クライアントで DarumaStack と同じように頭を上下させる
//    (積み木そのものは NetworkTumiki が位置を同期する)
//  ・ハンマーの振りの見た目は NetworkHammerView が見せる
//  ・操作はキャラクターではなく1人分(NetPlayer)の NetworkCommandSource が送り、DarumaMatch がつなぐ
//  ・無敵の残り時間: ホストから受け取り、クライアントでも同じように点滅させる(必殺技の見た目も IsInvincible を読む)
//  ・必殺技ゲージ(Yurube.SpecialGauge): ホストの溜まり具合(最大に対する割合)を受け取る。クライアントでは自分で溜めない(SpecialGauge.NetDriven)
//    (2026-11-12 中平: ゲージが小数のポイント・キャラごとの最大になったので、割合を 0〜254・満タン 255 で送る形にした。末原さん了承済み)
public class NetworkDarumaCharacter : NetworkBehaviour
{
    const BindingFlags kPrivate = BindingFlags.NonPublic | BindingFlags.Instance;
    // 無敵の残り時間は、これ以上ずれたら送り直す(間はクライアントが自分で減らす)
    const float kInvincibleResend = 0.25f;
    // 必殺技ゲージの割合の送り方: 0〜kGaugeSteps(途中)、kGaugeFull(満タン)
    const int kGaugeSteps = 254;
    const byte kGaugeFull = 255;

    readonly NetworkVariable<byte> m_stackCount = new NetworkVariable<byte>();
    readonly NetworkVariable<float> m_invincible = new NetworkVariable<float>();
    readonly NetworkVariable<byte> m_gauge = new NetworkVariable<byte>();
    // 積んでいる積み木の色ごとの数(4ビットずつ。赤・青・緑・黄)。パッシブの見た目が読む(あおだるの風など。Yurube.SpecialNet.CountColor)
    readonly NetworkVariable<ushort> m_colors = new NetworkVariable<ushort>();
    static readonly TumikiColor[] kColors = { TumikiColor.Red, TumikiColor.Blue, TumikiColor.Green, TumikiColor.Yellow };

    DarumaCharacter m_character;
    DarumaStack m_stack;
    Transform m_head;
    float m_fallSpeed = 8.0f;

    Yurube.SpecialGauge m_specialGauge;
    FieldInfo m_invincibleField;
    float m_blinkInterval = 0.1f;
    float m_invincibleLeft;     // クライアント: 無敵の残り(届いた値から自分で減らす)
    bool m_blinking;

    public override void OnNetworkSpawn()
    {
        m_character = GetComponent<DarumaCharacter>();
        m_stack = GetComponentInChildren<DarumaStack>(true);
        m_specialGauge = GetComponent<Yurube.SpecialGauge>();
        m_invincibleField = typeof(DarumaCharacter).GetField("m_invincibleTimer", kPrivate);

        if (IsServer) return;

        // クライアントのゲージは自分で溜めず、ホストの値を受け取るだけ
        if (m_specialGauge != null) m_specialGauge.NetDriven = true;

        if (m_character != null)
        {
            var blink = typeof(DarumaCharacter).GetField("m_blinkInterval", kPrivate);
            if (blink != null) m_blinkInterval = Mathf.Max(0.01f, (float)blink.GetValue(m_character));
            m_character.enabled = false;
        }
        m_invincibleLeft = m_invincible.Value;
        m_invincible.OnValueChanged += (_, v) => m_invincibleLeft = v;

        // 積み木を持っていないので、DarumaStack の代わりにここで頭の高さを動かす
        if (m_stack != null)
        {
            m_head = typeof(DarumaStack).GetField("m_head", kPrivate)?.GetValue(m_stack) as Transform;
            var speed = typeof(DarumaStack).GetField("m_fallSpeed", kPrivate);
            if (speed != null) m_fallSpeed = (float)speed.GetValue(m_stack);
            m_stack.enabled = false;
        }
        // Rigidbody を動かさない設定(Kinematic)は NetworkRigidbody が行う
    }

    private void Update()
    {
        if (!IsSpawned || IsServer) return;
        ClientUpdateInvincible();
        ClientUpdateGauge();
    }

    private void LateUpdate()
    {
        if (!IsSpawned) return;
        if (IsServer)
        {
            ServerSend();
            return;
        }
        ClientLateUpdate();
    }

    void ServerSend()
    {
        if (m_stack != null && m_stackCount.Value != (byte)m_stack.Count) m_stackCount.Value = (byte)m_stack.Count;

        if (m_character != null && m_invincibleField != null)
        {
            float left = Mathf.Max(0.0f, (float)m_invincibleField.GetValue(m_character));
            float sent = m_invincible.Value;
            // 始まり・終わりはすぐ、間は大きくずれたときだけ送る
            if ((left > 0.0f) != (sent > 0.0f) || Mathf.Abs(left - sent) > kInvincibleResend) m_invincible.Value = left;
        }

        if (m_specialGauge != null)
        {
            byte gauge = m_specialGauge.IsFull ? kGaugeFull
                : (byte)Mathf.Min(kGaugeSteps, Mathf.FloorToInt(m_specialGauge.Normalized * kGaugeSteps));
            if (m_gauge.Value != gauge) m_gauge.Value = gauge;
        }

        if (m_stack != null)
        {
            int packed = 0;
            for (int i = 0; i < kColors.Length; i++) packed |= Mathf.Min(m_stack.CountColor(kColors[i]), 15) << (i * 4);
            if (m_colors.Value != (ushort)packed) m_colors.Value = (ushort)packed;
        }
    }

    // クライアント: ホストのキャラクターが積んでいるその色の積み木の数
    public int ColorCount(TumikiColor color)
    {
        int i = System.Array.IndexOf(kColors, color);
        return i < 0 ? 0 : (m_colors.Value >> (i * 4)) & 15;
    }

    // DarumaCharacter.UpdateInvincible と同じ: 残り時間を減らし、その間は点滅する
    void ClientUpdateInvincible()
    {
        if (m_character == null) return;
        if (m_invincibleLeft <= 0.0f)
        {
            if (m_blinking)
            {
                // 終わったら表示を戻す
                m_blinking = false;
                m_character.SetInvincible(0.0f);
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;
            }
            return;
        }
        m_invincibleLeft = Mathf.Max(0.0f, m_invincibleLeft - Time.deltaTime);
        m_character.SetInvincible(m_invincibleLeft);
        m_blinking = true;
        bool visible = m_invincibleLeft <= 0.0f || Mathf.FloorToInt(m_invincibleLeft / m_blinkInterval) % 2 == 0;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = visible;
    }

    void ClientUpdateGauge()
    {
        if (m_specialGauge == null) return;
        byte v = m_gauge.Value;
        m_specialGauge.NetSetNormalized(v >= kGaugeFull ? 1.0f : (float)v / kGaugeSteps);
    }

    // DarumaStack.LateUpdate の頭の部分と同じ動き
    void ClientLateUpdate()
    {
        if (m_head == null || m_stack == null) return;
        Vector3 target = new Vector3(0.0f, m_stackCount.Value * TumikiBlock.kHeight + m_stack.HeadRadius, 0.0f);
        m_head.localPosition = Vector3.MoveTowards(m_head.localPosition, target, m_fallSpeed * Time.deltaTime);
    }
}
