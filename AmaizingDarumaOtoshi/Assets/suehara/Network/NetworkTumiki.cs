using System.Reflection;
using Nakahira;
using Unity.Netcode;
using UnityEngine;

// ネット対戦用の積み木。ホストだけが物理とルールを動かし、クライアントは位置の見た目だけを受け取る。
// 積まれている積み木も NetworkTransform がワールド座標で同期するので、タワーはそのまま見える。
// クライアントでは TumikiBlock を止めているので、煙・炎のエフェクトはここで切り替える。
// (積み木のプレハブ Tumiki_Net にだけ付ける。ローカル用の Tumiki.prefab には付けない)
[RequireComponent(typeof(TumikiBlock))]
public class NetworkTumiki : NetworkBehaviour
{
    const byte kHarmful = 1;   // 攻撃判定がある(炎)
    const byte kStacked = 2;   // 積まれている

    readonly NetworkVariable<byte> m_color = new NetworkVariable<byte>();
    readonly NetworkVariable<byte> m_flags = new NetworkVariable<byte>();
    TumikiBlock m_block;

    // クライアントのエフェクト
    ParticleSystem m_smokeTrail;
    ParticleSystem m_fireAura;
    float m_smokeMinSpeed = 1.0f;
    Vector3 m_prevPos;
    float m_speed;

    // 接続する前にローカルで生成された(ネットワークに乗っていない)積み木
    public bool CreatedOffline { get; private set; }

    // 生成エリア・大砲は普通に Instantiate するだけなので、ホストでは最初のフレームに自分で Spawn する
    // (位置・色・発射の指定が済んだあとに配られる)
    private void Start()
    {
        var nm = NetworkManager.Singleton;
        var no = GetComponent<NetworkObject>();
        if (nm == null || !nm.IsListening) { CreatedOffline = true; return; }
        if (nm.IsServer && !no.IsSpawned) no.Spawn(true);
    }

    public override void OnNetworkSpawn()
    {
        m_block = GetComponent<TumikiBlock>();
        if (IsServer)
        {
            m_color.Value = (byte)m_block.Color;
            m_flags.Value = ReadFlags();
            return;
        }

        // クライアントは見るだけ。ゲームのルール(奈落判定・減速・当たり)はホストが決める
        m_block.enabled = false;
        ApplyColor(m_color.Value);
        m_color.OnValueChanged += (_, c) => ApplyColor(c);

        const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
        var t = typeof(TumikiBlock);
        m_smokeTrail = t.GetField("m_smokeTrail", f)?.GetValue(m_block) as ParticleSystem;
        m_fireAura = t.GetField("m_fireAura", f)?.GetValue(m_block) as ParticleSystem;
        var min = t.GetField("m_smokeMinSpeed", f);
        if (min != null) m_smokeMinSpeed = (float)min.GetValue(m_block);
        m_prevPos = transform.position;
        UpdateClientEffects();
    }

    byte ReadFlags()
    {
        byte b = 0;
        if (m_block.IsHarmful) b |= kHarmful;
        if (m_block.CurrentState == TumikiBlock.State.Stacked) b |= kStacked;
        return b;
    }

    void ApplyColor(byte c)
    {
        if (m_block != null) m_block.SetColor((TumikiColor)c);
    }

    private void Update()
    {
        if (!IsSpawned || m_block == null) return;
        if (IsServer)
        {
            // 生成エリアは Spawn 後に SetColor するので、ホストは色・状態の変化を拾って配る
            if (m_color.Value != (byte)m_block.Color) m_color.Value = (byte)m_block.Color;
            byte flags = ReadFlags();
            if (m_flags.Value != flags) m_flags.Value = flags;
            return;
        }
        UpdateClientEffects();
    }

    // 煙: 動いている間 / 炎: 攻撃判定がある間(TumikiBlock.UpdateEffects と同じ決まり)
    void UpdateClientEffects()
    {
        float dt = Time.deltaTime;
        if (dt > 0.0f)
        {
            Vector3 d = transform.position - m_prevPos;
            d.y = 0.0f;
            // 届く位置はとびとびなので、速さはならして使う
            m_speed = Mathf.Lerp(m_speed, d.magnitude / dt, 0.3f);
        }
        m_prevPos = transform.position;

        bool stacked = (m_flags.Value & kStacked) != 0;
        SetEmission(m_smokeTrail, !stacked && m_speed > m_smokeMinSpeed);
        SetEmission(m_fireAura, (m_flags.Value & kHarmful) != 0);
    }

    static void SetEmission(ParticleSystem ps, bool enabled)
    {
        if (ps == null) return;
        var emission = ps.emission;
        if (emission.enabled != enabled) emission.enabled = enabled;
    }
}
