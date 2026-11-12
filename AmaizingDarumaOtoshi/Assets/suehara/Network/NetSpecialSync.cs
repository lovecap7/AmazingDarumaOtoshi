using System.Collections.Generic;
using Nakahira;
using Unity.Netcode;
using UnityEngine;
using Yurube;

// 必殺技をネット対戦でもローカルと同じに動かす(Yurube さんの必殺技の入口 Yurube.SpecialNet をつなぐ)。
// NetMatch と同じ GameObject(シーンの NetworkObject)に付ける。
//  ・ホスト      : ネット対戦の人の必殺技ボタン・行動ボタン(NetworkCommandSource に届いたもの)を必殺技に渡す。
//                  必殺技の出来事(発動・方向の確定など。SpecialNet.Send)をクライアントへ送る
//  ・クライアント: 届いた出来事を、そのキャラクターの同じ必殺技に渡す(SpecialNet.Receive。見た目だけを動かす)。
//                  クライアントの積み木の「飛んでいるか・速さ」を、NetworkTumiki に届いた状態で答える
// ゲージ・無敵の残り時間はキャラクターごとに NetworkDarumaCharacter が配る
public class NetSpecialSync : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            SpecialNet.Sender = Send;
            SpecialNet.NetSpecialPressed = p => Source(p) != null && Source(p).ConsumeSpecial();
            SpecialNet.NetAnyActionPressed = p => Source(p) != null && Source(p).ConsumeAnyAction();
            SpecialNet.NetInstantiateBlock = InstantiateNetBlock;
            return;
        }
        SpecialNet.NetBlockFlying = b => b != null && b.TryGetComponent<NetworkTumiki>(out var t) && t.IsSpawned ? t.IsFlying : (bool?)null;
        SpecialNet.NetBlockSpeed = b => b != null && b.TryGetComponent<NetworkTumiki>(out var t) && t.IsSpawned ? t.Speed : (float?)null;
        SpecialNet.NetCountColor = (c, color) => c != null && c.TryGetComponent<NetworkDarumaCharacter>(out var n) && n.IsSpawned ? n.ColorCount(color) : (int?)null;
    }

    // ホスト: パッシブが作る積み木(きだるの黄色など)を、ネット用の同じ積み木(<名前>_Net)で作ってすぐ配る。
    // 積む前に配らないと、配ったときに NetworkRigidbody が物理を戻して、積んだ積み木が落ちてしまう
    GameObject InstantiateNetBlock(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        GameObject netPrefab = prefab.GetComponent<NetworkObject>() != null ? prefab : FindNetPrefab(prefab.name + "_Net");
        if (netPrefab == null)
        {
            Debug.LogWarning($"[NetSpecialSync] ネット用の積み木が見つかりません: {prefab.name}_Net");
            return null;
        }
        var go = Instantiate(netPrefab, position, rotation);
        go.name = netPrefab.name;
        go.GetComponent<NetworkObject>().Spawn(true);
        return go;
    }

    static GameObject FindNetPrefab(string name)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return null;
        foreach (var p in nm.NetworkConfig.Prefabs.Prefabs)
        {
            if (p.Prefab != null && p.Prefab.name == name) return p.Prefab;
        }
        return null;
    }

    public override void OnNetworkDespawn()
    {
        SpecialNet.Sender = null;
        SpecialNet.NetSpecialPressed = null;
        SpecialNet.NetAnyActionPressed = null;
        SpecialNet.NetBlockFlying = null;
        SpecialNet.NetBlockSpeed = null;
        SpecialNet.NetCountColor = null;
        SpecialNet.NetInstantiateBlock = null;
    }

    // ネット対戦の人の入力(1人分 NetPlayer の NetworkCommandSource)
    static NetworkCommandSource Source(MatchPlayer p) => p != null ? p.GetComponent<NetworkCommandSource>() : null;

    // ホスト: 必殺技の出来事をクライアントへ送る
    void Send(DarumaCharacter character, string key, int ev, float[] values, TumikiBlock[] blocks)
    {
        if (!IsSpawned || character == null) return;
        var no = character.GetComponent<NetworkObject>();
        if (no == null || !no.IsSpawned) return;
        var refs = new List<NetworkObjectReference>();
        var present = new List<bool>();
        foreach (var b in blocks)
        {
            var bno = b != null ? b.GetComponent<NetworkObject>() : null;
            bool ok = bno != null && bno.IsSpawned;
            present.Add(ok);
            refs.Add(ok ? new NetworkObjectReference(bno) : default);
        }
        EventRpc(new NetworkObjectReference(no), key, (byte)ev, values, refs.ToArray(), present.ToArray());
    }

    [Rpc(SendTo.NotServer)]
    void EventRpc(NetworkObjectReference character, string key, byte ev, float[] values, NetworkObjectReference[] blocks, bool[] present)
    {
        if (!character.TryGet(out NetworkObject no)) return;
        var ch = no.GetComponent<DarumaCharacter>();
        if (ch == null) return;
        var list = new TumikiBlock[blocks != null ? blocks.Length : 0];
        for (int i = 0; i < list.Length; i++)
        {
            if (present != null && i < present.Length && present[i] && blocks[i].TryGet(out NetworkObject bno)) list[i] = bno.GetComponent<TumikiBlock>();
        }
        try
        {
            SpecialNet.Receive(ch, key, ev, values, list);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, ch);
        }
    }
}
