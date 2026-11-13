using System;
using System.Collections.Generic;
using System.Reflection;
using Nakagawa;
using Nakahira;
using Unity.Netcode;
using UnityEngine;

// ホストで出た積み木の演出を、クライアントでも出す。NetMatch と同じ GameObject(シーンの NetworkObject)に付ける。
//  ・生成の予告: ホストの予告(TumikiSpawnWarning)を見つけたら、クライアントに見た目だけの予告を出す
//  ・割れる    : ホストで割れて消えた積み木の破片(TumikiShatter)を出す(NetworkTumiki が知らせる)
//  ・当たった・拾った: ホストの知らせ(TumikiBlock.Hit・DarumaHammer.Hit / ShotFired・DarumaCharacter.AnyPickedUp)を送り、
//    クライアントでは、見た目と音を出すもの(カメラの揺れ CameraShake・効果音 MatchAudio・火花 HammerHitEffect・乗って止めたエフェクト TumikiCatchEffect)
//    にだけ同じ知らせを渡す。知らせそのもの(static event)は起こさない(実績・必殺技ゲージがクライアントで数えないように)
// 積み木そのものは消えると届かなくなるので、シーンに残るここから送る。ポンと出る演出は NetworkTumiki が出す
public class NetEffects : NetworkBehaviour
{
    // 積み木どうしの当たりは、これより遅いものは送らない(音も揺れも出ない遅さ。MatchAudio は 1.5 未満を鳴らさない)
    const float kMinBlockHitSpeed = 1.5f;
    const BindingFlags kPrivate = BindingFlags.NonPublic | BindingFlags.Instance;

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

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        TumikiBlock.Hit += OnBlockHit;
        DarumaHammer.Hit += OnHammerHit;
        DarumaHammer.ShotFired += OnShotFired;
        DarumaCharacter.AnyPickedUp += OnPickedUp;
        MatchAudio.Jumped += OnJumped;
    }

    public override void OnNetworkDespawn()
    {
        TumikiBlock.Hit -= OnBlockHit;
        DarumaHammer.Hit -= OnHammerHit;
        DarumaHammer.ShotFired -= OnShotFired;
        DarumaCharacter.AnyPickedUp -= OnPickedUp;
        MatchAudio.Jumped -= OnJumped;
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

    //========================================
    // 当たった・拾った
    //========================================

    // ネットワークに乗っているもの(キャラ・幽霊・積み木)の参照。乗っていなければ送らない
    static bool TryRef(Component c, out NetworkObjectReference r)
    {
        r = default;
        var no = c != null ? c.GetComponentInParent<NetworkObject>() : null;
        if (no == null || !no.IsSpawned) return false;
        r = new NetworkObjectReference(no);
        return true;
    }

    static Component Resolve(NetworkObjectReference r, bool present, Type type)
    {
        if (!present || !r.TryGet(out NetworkObject no)) return null;
        return no.GetComponentInChildren(type, true);
    }

    // ハンマーの持ち主(キャラ・幽霊)の中での番号(NetworkHammerView と同じ数え方)
    static int HammerIndex(DarumaHammer hammer, out NetworkObjectReference body)
    {
        body = default;
        if (hammer == null) return -1;
        var no = hammer.GetComponentInParent<NetworkObject>();
        if (no == null || !no.IsSpawned) return -1;
        body = new NetworkObjectReference(no);
        return Array.IndexOf(no.GetComponentsInChildren<DarumaHammer>(true), hammer);
    }

    static DarumaHammer ResolveHammer(NetworkObjectReference body, int index)
    {
        if (index < 0 || !body.TryGet(out NetworkObject no)) return null;
        var hammers = no.GetComponentsInChildren<DarumaHammer>(true);
        return index < hammers.Length ? hammers[index] : null;
    }

    // ホスト
    void OnBlockHit(TumikiHitInfo h)
    {
        if (!IsSpawned || h.Block == null) return;
        bool character = h.Target is DarumaCharacter;
        if (!character && h.Speed < kMinBlockHitSpeed) return;
        if (!TryRef(h.Block, out var block)) return;
        bool hasTarget = TryRef(h.Target, out var target);
        BlockHitRpc(block, target, hasTarget, character, h.Point, h.Speed, h.IsHarmful);
    }

    void OnHammerHit(HammerHitInfo h) => SendHammer(h, false);
    void OnShotFired(HammerHitInfo h) => SendHammer(h, true);

    void SendHammer(HammerHitInfo h, bool shot)
    {
        if (!IsSpawned) return;
        int index = HammerIndex(h.Hammer, out var body);
        if (index < 0) return;
        bool hasTarget = TryRef(h.Target, out var target);
        bool character = h.Target is DarumaCharacter;
        HammerRpc(body, (byte)index, target, hasTarget, character, h.Point, h.Damaged, (byte)Mathf.Clamp(h.ReturnCount, 0, 255), shot);
    }

    void OnPickedUp(TumikiPickupInfo info)
    {
        if (!IsSpawned || !TryRef(info.Character, out var character)) return;
        bool hasBlock = TryRef(info.Block, out var block);
        int count = info.Character.Stack != null ? info.Character.Stack.Count : 0;
        PickupRpc(character, block, hasBlock, info.WasHarmful, info.Speed, info.StackGrew, (byte)Mathf.Clamp(count, 0, 255));
    }

    // ジャンプの音(MatchAudio はキャラクターの物理の速さから見つけるので、ホストでしか見つからない)
    void OnJumped(DarumaCharacter character)
    {
        if (!IsSpawned || !TryRef(character, out var body)) return;
        JumpRpc(body);
    }

    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
    void JumpRpc(NetworkObjectReference bodyRef)
    {
        if (!bodyRef.TryGet(out NetworkObject no)) return;
        var audio = FindAnyObjectByType<MatchAudio>();
        if (audio != null && audio.isActiveAndEnabled) audio.PlayJump(no.transform);
    }

    // クライアント
    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
    void BlockHitRpc(NetworkObjectReference blockRef, NetworkObjectReference targetRef, bool hasTarget, bool targetIsCharacter,
                     Vector3 point, float speed, bool harmful)
    {
        var block = Resolve(blockRef, true, typeof(TumikiBlock)) as TumikiBlock;
        if (block == null) return;
        var target = Resolve(targetRef, hasTarget, targetIsCharacter ? typeof(DarumaCharacter) : typeof(TumikiBlock));
        var info = new TumikiHitInfo(block, target, point, speed, harmful);
        Invoke<CameraShake>("OnTumikiHit", info);
        Invoke<MatchAudio>("OnBlockHit", info);
    }

    [Rpc(SendTo.NotServer)]
    void HammerRpc(NetworkObjectReference bodyRef, byte index, NetworkObjectReference targetRef, bool hasTarget, bool targetIsCharacter,
                   Vector3 point, bool damaged, byte returnCount, bool shot)
    {
        var hammer = ResolveHammer(bodyRef, index);
        if (hammer == null) return;
        var owner = hammer.GetComponentInParent<IDarumaAttacker>();
        var target = Resolve(targetRef, hasTarget, targetIsCharacter ? typeof(DarumaCharacter) : typeof(TumikiBlock));
        var info = new HammerHitInfo(hammer, owner, target, point, damaged, returnCount);
        if (shot)
        {
            Invoke<HammerHitEffect>("OnShotFired", info);
            return;
        }
        Invoke<CameraShake>("OnHammerHit", info);
        Invoke<MatchAudio>("OnHammerHit", info);
        Invoke<HammerHitEffect>("OnHammerHit", info);
    }

    [Rpc(SendTo.NotServer)]
    void PickupRpc(NetworkObjectReference characterRef, NetworkObjectReference blockRef, bool hasBlock, bool wasHarmful, float speed, bool stackGrew, byte stackCount)
    {
        var character = Resolve(characterRef, true, typeof(DarumaCharacter)) as DarumaCharacter;
        if (character == null) return;
        // 拾った音の高さは積んだ数で決まる(MatchAudio が DarumaStack.NetCount を読む)。段数の同期より先に届くことがあるので、ここで入れる
        if (character.Stack != null) character.Stack.NetCount = stackCount;
        var block = Resolve(blockRef, hasBlock, typeof(TumikiBlock)) as TumikiBlock;
        var info = new TumikiPickupInfo(character, block, wasHarmful, speed, stackGrew);
        Invoke<MatchAudio>("OnPickedUp", info);
        Invoke<TumikiCatchEffect>("OnPickedUp", info);
    }

    // シーンにある T(なければ何もしない)の、知らせを受け取る private メソッドを呼ぶ
    static readonly Dictionary<(Type, string), MethodInfo> s_methods = new Dictionary<(Type, string), MethodInfo>();
    static readonly Dictionary<Type, Component> s_receivers = new Dictionary<Type, Component>();

    static void Invoke<T>(string method, object info) where T : Component
    {
        if (!s_receivers.TryGetValue(typeof(T), out var receiver) || receiver == null)
        {
            receiver = FindAnyObjectByType<T>();
            s_receivers[typeof(T)] = receiver;
        }
        if (receiver == null || (receiver is Behaviour b && !b.isActiveAndEnabled)) return;
        if (!s_methods.TryGetValue((typeof(T), method), out var m))
        {
            m = typeof(T).GetMethod(method, kPrivate, null, new[] { info.GetType() }, null);
            s_methods[(typeof(T), method)] = m;
            if (m == null) Debug.LogWarning($"[NetEffects] {typeof(T).Name}.{method} が見つかりません(名前が変わった?)");
        }
        if (m == null) return;
        try
        {
            m.Invoke(receiver, new[] { info });
        }
        catch (TargetInvocationException e)
        {
            Debug.LogException(e.InnerException ?? e, receiver);
        }
    }

    // ほかのネット用の部品から使う(脱落の揺れ・音など): シーンの T の private メソッドを呼ぶ
    public static void InvokeReceiver<T>(string method, object arg) where T : Component => Invoke<T>(method, arg);
}
