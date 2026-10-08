using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // ギミック共通の処理
    public static class GimmickUtil
    {
        static readonly Collider[] s_buffer = new Collider[128];
        static readonly HashSet<IGimmickAffectable> s_found = new HashSet<IGimmickAffectable>();

        // コライダーの持ち主で、ギミックの影響を受けられるもの(なければnull)
        public static IGimmickAffectable Find(Collider col)
        {
            Rigidbody rb = col.attachedRigidbody;
            if (rb == null) return null;
            var target = rb.GetComponent<IGimmickAffectable>();
            if (target == null || !IsAlive(target) || !target.CanBeAffected) return null;
            return target;
        }

        // 箱の範囲内にいる、影響を受けられるものを集める(同じ相手は1回だけ)
        public static void OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion rotation,
            List<IGimmickAffectable> results, int layerMask = Physics.DefaultRaycastLayers)
        {
            results.Clear();
            s_found.Clear();
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, s_buffer, rotation, layerMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                IGimmickAffectable target = Find(s_buffer[i]);
                if (target != null && s_found.Add(target)) results.Add(target);
            }
        }

        // 破棄済みのMonoBehaviourもnull扱いにする
        public static bool IsAlive(IGimmickAffectable target)
        {
            return target is Object obj && obj != null;
        }
    }
}
