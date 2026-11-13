using Unity.Netcode.Components;
using UnityEngine;

// ワープ(中川さんの WarpGateGimmick)などで、ホストの上で一瞬で遠くへ移ったものを、クライアントでもすべらせずにパッと移すための見張り。
// NetworkTransform はふだん、届いた位置の間をなめらかにつなぐので、瞬間移動もすべるように見えてしまう。
// ホストで、1フレームで普通の動きではありえないほど遠くへ移ったのを見つけたら、NetworkTransform.Teleport で「つながずに移した」と送る。
// 使うのは NetworkHammerView(キャラ・幽霊)と NetworkTumiki(積み木)のホスト側
public class NetTeleportWatch
{
    // これだけ離れたら瞬間移動とみなす(m)。速く飛ぶ積み木(秒速数十m)が重いフレームで進む距離より大きくする
    const float kMinDistance = 3.0f;
    const float kMaxSpeed = 60.0f;

    readonly Transform m_transform;
    readonly NetworkTransform m_networkTransform;
    Vector3 m_last;
    bool m_hasLast;

    public NetTeleportWatch(Component owner)
    {
        m_transform = owner.transform;
        m_networkTransform = owner.GetComponent<NetworkTransform>();
    }

    // ホスト: 毎フレーム呼ぶ
    public void Update()
    {
        if (m_transform == null) return;
        Vector3 pos = m_transform.position;
        if (m_hasLast && m_networkTransform != null && m_networkTransform.IsSpawned)
        {
            float limit = kMinDistance + kMaxSpeed * Time.deltaTime;
            if ((pos - m_last).sqrMagnitude > limit * limit)
                m_networkTransform.Teleport(pos, m_transform.rotation, m_transform.localScale);
        }
        m_last = pos;
        m_hasLast = true;
    }
}
