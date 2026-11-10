using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Nakagawa;
using Unity.Netcode;
using UnityEngine;

// ホストだけが動かすギミック(ステージ19 ビリヤード・20 ルーレットなど)の見た目を、クライアントにも見せる。
// NetMatch と同じ GameObject(シーンの NetworkObject)に付ける。中川さん側の入口は Nakagawa/Scripts/Gimmicks/Common/GimmickNet.cs
//  ・状態: ホストが 1/20 秒ごとに、表示中のギミック(INetGimmickState)の状態をまとめて送る。
//          クライアントは少し遅らせた時刻で前後の2つの間をなめらかにつなぎ、毎フレーム ReadNetState で見た目に反映する
//  ・エフェクト・音・文字・カメラの揺れ: ホストで GimmickNet.Relay() の範囲に出たものを、そのままクライアントにも出す
// ギミックの見分け方: シーンの中の並び(親からの順番)で番号を付ける。ホストとクライアントは同じシーンなので番号がそろう
public class NetGimmickSync : NetworkBehaviour
{
    const float kSendInterval = 0.05f;
    // クライアントで表示を遅らせる時間(この間に次の状態が届いていれば、なめらかにつなげる)
    const double kDelay = 0.12;
    const int kMaxSnapshots = 16;

    class Snapshot
    {
        public double Time;
        public readonly Dictionary<int, (int start, int length)> Chunks = new Dictionary<int, (int, int)>();
        public float[] Values;
        public bool[] Discrete;
    }

    readonly List<INetGimmickState> m_targets = new List<INetGimmickState>();
    readonly GimmickNetState m_state = new GimmickNetState();
    readonly List<Snapshot> m_snapshots = new List<Snapshot>();
    float m_sendTimer;

    // ホスト: 送るときに使い回す
    readonly List<ushort> m_ids = new List<ushort>();
    readonly List<ushort> m_lengths = new List<ushort>();

    static Dictionary<string, AudioClip> s_clips;

    //========================================
    // 準備
    //========================================

    public override void OnNetworkSpawn()
    {
        FindTargets();
        if (!IsServer) return;
        GimmickNet.BurstSent = SendBurst;
        GimmickNet.SoundSent = SendSound;
        GimmickNet.ShakeSent = SendShake;
        GimmickNet.MessageSent = SendStageMessage;
    }

    public override void OnNetworkDespawn()
    {
        if (GimmickNet.BurstSent == (Action<GimmickBurstSettings, Vector3, Quaternion, float, Color?>)SendBurst) GimmickNet.BurstSent = null;
        if (GimmickNet.SoundSent == (Action<AudioClip, float, float>)SendSound) GimmickNet.SoundSent = null;
        if (GimmickNet.ShakeSent == (Action<float, float>)SendShake) GimmickNet.ShakeSent = null;
        if (GimmickNet.MessageSent == (Action<string, Color, float, float>)SendStageMessage) GimmickNet.MessageSent = null;
        m_snapshots.Clear();
    }

    // シーンの中の INetGimmickState を、親からの並び順で集める(ホストとクライアントで同じ順番になる)
    void FindTargets()
    {
        m_targets.Clear();
        var found = new List<(string key, INetGimmickState target)>();
        var scene = gameObject.scene;
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (!(mb is INetGimmickState target) || mb.gameObject.scene != scene) continue;
            found.Add((OrderKey(mb), target));
        }
        found.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        foreach (var f in found) m_targets.Add(f.target);
    }

    // 一番上は名前(シーン直下は、実行中に作られた・消えた物で順番がずれることがあるため)、その下は親からの順番
    static string OrderKey(Component c)
    {
        var sb = new StringBuilder();
        for (var t = c.transform; t != null; t = t.parent)
        {
            sb.Insert(0, (t.parent == null ? t.name : t.GetSiblingIndex().ToString("D5")) + "/");
        }
        var all = c.GetComponents<Component>();
        sb.Append(Array.IndexOf(all, c).ToString("D3"));
        return sb.ToString();
    }

    //========================================
    // 状態
    //========================================

    private void LateUpdate()
    {
        if (!IsSpawned) return;
        if (IsServer) ServerSend();
        else ClientApply();
    }

    void ServerSend()
    {
        m_sendTimer -= Time.unscaledDeltaTime;
        if (m_sendTimer > 0.0f) return;
        m_sendTimer = kSendInterval;
        // 見ている人がいなければ送らない
        if (NetworkManager.ConnectedClientsIds.Count <= 1) return;

        m_state.Clear();
        m_ids.Clear();
        m_lengths.Clear();
        for (int i = 0; i < m_targets.Count; i++)
        {
            var mb = m_targets[i] as MonoBehaviour;
            if (mb == null || !mb.isActiveAndEnabled) continue;
            int start = m_state.Values.Count;
            m_targets[i].WriteNetState(m_state);
            int length = m_state.Values.Count - start;
            if (length <= 0) continue;
            m_ids.Add((ushort)i);
            m_lengths.Add((ushort)length);
        }
        if (m_ids.Count == 0) return;

        var discrete = new byte[(m_state.Discrete.Count + 7) / 8];
        for (int i = 0; i < m_state.Discrete.Count; i++)
        {
            if (m_state.Discrete[i]) discrete[i >> 3] |= (byte)(1 << (i & 7));
        }
        StateRpc(NetworkManager.ServerTime.Time, m_ids.ToArray(), m_lengths.ToArray(), m_state.Values.ToArray(), discrete);
    }

    [Rpc(SendTo.NotServer)]
    void StateRpc(double time, ushort[] ids, ushort[] lengths, float[] values, byte[] discrete)
    {
        if (ids == null || lengths == null || values == null || ids.Length != lengths.Length) return;
        var snap = new Snapshot { Time = time, Values = values, Discrete = new bool[values.Length] };
        for (int i = 0; i < values.Length; i++)
        {
            snap.Discrete[i] = discrete != null && (i >> 3) < discrete.Length && (discrete[i >> 3] & (1 << (i & 7))) != 0;
        }
        int start = 0;
        for (int i = 0; i < ids.Length; i++)
        {
            if (start + lengths[i] > values.Length) return;
            snap.Chunks[ids[i]] = (start, lengths[i]);
            start += lengths[i];
        }
        // 古い順に並べておく(遅れて届いたものは捨てる)
        if (m_snapshots.Count > 0 && time <= m_snapshots[m_snapshots.Count - 1].Time) return;
        m_snapshots.Add(snap);
        while (m_snapshots.Count > kMaxSnapshots) m_snapshots.RemoveAt(0);
    }

    void ClientApply()
    {
        if (m_snapshots.Count == 0) return;
        double renderTime = NetworkManager.ServerTime.Time - kDelay;

        // renderTime をはさむ2つ(なければ一番新しいもの)
        Snapshot a = null, b = m_snapshots[m_snapshots.Count - 1];
        for (int i = m_snapshots.Count - 1; i > 0; i--)
        {
            if (m_snapshots[i - 1].Time <= renderTime)
            {
                a = m_snapshots[i - 1];
                b = m_snapshots[i];
                break;
            }
        }
        // 使い終わった古いもの(a より前)は捨てる
        if (a != null)
        {
            int ai = m_snapshots.IndexOf(a);
            if (ai > 0) m_snapshots.RemoveRange(0, ai);
        }
        float t = a == null ? 1.0f : (float)Math.Min(1.0, Math.Max(0.0, (renderTime - a.Time) / Math.Max(0.0001, b.Time - a.Time)));

        foreach (var pair in b.Chunks)
        {
            int id = pair.Key;
            if (id >= m_targets.Count) continue;
            var mb = m_targets[id] as MonoBehaviour;
            if (mb == null || !mb.isActiveAndEnabled) continue;

            var (bStart, length) = pair.Value;
            m_state.Clear();
            bool blend = a != null && t < 1.0f && a.Chunks.TryGetValue(id, out var ac) && ac.length == length;
            int aStart = blend ? a.Chunks[id].start : 0;
            for (int k = 0; k < length; k++)
            {
                float vb = b.Values[bStart + k];
                if (!blend)
                {
                    m_state.Values.Add(vb);
                    continue;
                }
                float va = a.Values[aStart + k];
                // 番号・フラグは補間しない(表示している時刻の側の値を使う)
                m_state.Values.Add(b.Discrete[bStart + k] ? (t < 1.0f ? va : vb) : Mathf.LerpUnclamped(va, vb, t));
            }
            m_state.BeginRead();
            try
            {
                m_targets[id].ReadNetState(m_state);
            }
            catch (Exception e)
            {
                Debug.LogException(e, mb);
            }
        }
    }

    //========================================
    // エフェクト・音・文字・カメラの揺れ
    //========================================

    void SendBurst(GimmickBurstSettings s, Vector3 position, Quaternion rotation, float scale, Color? tint)
    {
        if (!IsSpawned || s == null) return;
        // 材質(シーンの中の物)は送れないので外す(クライアントでは演出用の材質になる)
        var copy = s.Clone();
        copy.Material = null;
        BurstRpc(JsonUtility.ToJson(copy), position, rotation, scale, tint.HasValue, tint ?? Color.white);
    }

    [Rpc(SendTo.NotServer)]
    void BurstRpc(string json, Vector3 position, Quaternion rotation, float scale, bool hasTint, Color tint)
    {
        var s = new GimmickBurstSettings();
        JsonUtility.FromJsonOverwrite(json, s);
        GimmickBurst.Play(s, position, rotation, scale, hasTint ? tint : (Color?)null);
    }

    void SendSound(AudioClip clip, float volume, float pitch)
    {
        if (!IsSpawned || clip == null) return;
        SoundRpc(clip.name, volume, pitch);
    }

    [Rpc(SendTo.NotServer)]
    void SoundRpc(string clip, float volume, float pitch)
    {
        var c = FindClip(clip);
        if (c != null) GimmickSound.Play(c, volume, pitch);
    }

    void SendShake(float amplitude, float duration)
    {
        if (IsSpawned) ShakeRpc(amplitude, duration);
    }

    [Rpc(SendTo.NotServer)]
    void ShakeRpc(float amplitude, float duration) => BilliardFx.Shake(amplitude, duration);

    void SendStageMessage(string text, Color color, float size, float duration)
    {
        if (IsSpawned) MessageRpc(text, color, size, duration);
    }

    [Rpc(SendTo.NotServer)]
    void MessageRpc(string text, Color color, float size, float duration) => StageMessageUI.Show(text, color, size, duration);

    // 音を名前から探す。ギミックの音はプログラムで作るので、GimmickSound・BilliardFx の音を作って名前で引く
    static AudioClip FindClip(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (s_clips == null)
        {
            s_clips = new Dictionary<string, AudioClip>();
            foreach (var type in new[] { typeof(GimmickSound), typeof(BilliardFx) })
            {
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
                {
                    if (p.PropertyType != typeof(AudioClip) || p.GetIndexParameters().Length > 0) continue;
                    if (p.GetValue(null) is AudioClip c && c != null) s_clips[c.name] = c;
                }
            }
        }
        if (s_clips.TryGetValue(name, out var clip) && clip != null) return clip;
        // インスペクターで指定した音のファイル
        foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
        {
            if (c != null && c.name == name)
            {
                s_clips[name] = c;
                return c;
            }
        }
        return null;
    }
}
