using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 飛んでいる積み木を、ギミックの側面で壁と同じように反射させるかの共通の判定(ITumikiReflectFilter の中から使う)
    //  ・横向きの面(側面)に当たったときだけ反射する(上面に乗った・上から落ちてきたときは反射しない)
    //  ・側面でも上面の縁に触れただけで、積み木の真下に別の足場がある(隣の足場から滑ってきた継ぎ目)なら反射しない
    //    (隣り合ったせり上がり塀の上を滑っているとき、隣の塀の縁に触れて跳ね返らないように)
    //  ・上面の縁でも真下に足場がない(穴を渡ってきて縁にぶつかった)なら反射する(縁に押し付けられて宙に止まらないように)
    public static class TumikiReflectRules
    {
        // 法線の上下成分がこれ未満の面を側面とみなす
        public const float kSideNormalY = 0.5f;
        // 側面の接触点が上面からこれ以内なら、上面の縁に触れたとみなす
        public const float kTopMargin = 0.12f;
        // 真下の足場を探す深さ(積み木の底から)と、当たった面から手前へずらして調べる距離
        const float kProbeDepth = 0.3f;
        const float kBackProbe = 0.3f;

        static readonly RaycastHit[] s_hits = new RaycastHit[8];

        // surface: 当たった当たり判定(この判定で反射させる物)
        public static bool ShouldReflectAtSide(TumikiBlock block, Collider surface, ContactPoint contact)
        {
            Vector3 normal = contact.normal;
            if (Mathf.Abs(normal.y) >= kSideNormalY) return false;
            if (contact.point.y <= surface.bounds.max.y - kTopMargin) return true;
            if (block == null) return true;

            // 上面の縁: 真下(か手前)に別の足場があれば、継ぎ目に触れただけとみなす
            normal.y = 0.0f;
            Vector3 back = normal.sqrMagnitude > 0.0001f ? normal.normalized * kBackProbe : Vector3.zero;
            Vector3 center = block.transform.position;
            return !HasFloorBelow(block, surface, center) && !HasFloorBelow(block, surface, center + back);
        }

        static bool HasFloorBelow(TumikiBlock block, Collider surface, Vector3 origin)
        {
            float length = TumikiBlock.kHeight * 0.5f + kProbeDepth;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = s_hits[i].collider;
                if (c == surface || c.transform.IsChildOf(block.transform)) continue;
                if (c.GetComponentInParent<TumikiBlock>() != null || c.GetComponentInParent<DarumaCharacter>() != null) continue;
                return true;
            }
            return false;
        }
    }
}
