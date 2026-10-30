using System.Collections.Generic;
using Nakagawa;
using Nakahira;
using Unity.Netcode;
using UnityEngine;

// ホストで出た積み木の演出を、クライアントでも出す。NetMatch と同じ GameObject(シーンの NetworkObject)に付ける。
//  ・生成の予告: ホストの予告(TumikiSpawnWarning)を見つけたら、クライアントに見た目だけの予告を出す
//  ・割れる    : ホストで割れて消えた積み木の破片(TumikiShatter)を出す(NetworkTumiki が知らせる)
// 積み木そのものは消えると届かなくなるので、シーンに残るここから送る。ポンと出る演出は NetworkTumiki が出す
public class NetEffects : NetworkBehaviour
{
    // 破片の材質を取るのに使う(積み木のネット用プレハブ)
    [SerializeField] TumikiBlock m_tumikiPrefab;

    static NetEffects s_instance;
    // ホスト: 送った予告
    readonly HashSet<TumikiSpawnWarning> m_sent = new HashSet<TumikiSpawnWarning>();
    Material m_shatterMaterial;

    void Awake() => s_instance = this;

    public override void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer) return;
        foreach (var w in TumikiSpawnWarning.Active)
        {
            if (w == null || !m_sent.Add(w)) continue;
            WarningRpc((byte)w.Color, w.Position, w.transform.position.y, w.Duration * (1.0f - w.Progress));
        }
        m_sent.RemoveWhere(w => w == null);
    }

    [Rpc(SendTo.NotServer)]
    void WarningRpc(byte color, Vector3 pos, float markY, float duration)
    {
        TumikiSpawnWarning.CreateView((TumikiColor)color, pos, markY, duration);
    }

    // ホスト: 積み木が割れた(NetworkTumiki から)
    public static void SendShatter(Vector3 pos, Quaternion rot, Vector3 velocity, TumikiColor color, float duration, int pieces, float spread)
    {
        if (s_instance == null || !s_instance.IsSpawned || !s_instance.IsServer) return;
        s_instance.ShatterRpc(pos, rot, velocity, (byte)color, duration, (byte)Mathf.Clamp(pieces, 0, 255), spread);
    }

    [Rpc(SendTo.NotServer)]
    void ShatterRpc(Vector3 pos, Quaternion rot, Vector3 velocity, byte color, float duration, byte pieces, float spread)
    {
        if (m_shatterMaterial == null && m_tumikiPrefab != null)
        {
            var r = m_tumikiPrefab.GetComponentInChildren<Renderer>(true);
            if (r != null) m_shatterMaterial = r.sharedMaterial;
        }
        if (m_shatterMaterial == null) return;
        TumikiShatter.Spawn(pos, rot, velocity, m_shatterMaterial, TumikiColorUtil.ToUnityColor((TumikiColor)color), duration, pieces, spread);
    }
}
