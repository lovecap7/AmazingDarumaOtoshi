using UnityEngine;

namespace Nakahira
{
    // CPU が周りを調べるための問い合わせ(足場)。積み木とキャラクターは足場として扱わない
    // (奈落・壁を迂回する道は CpuNavMesh で求める。ここは道の途中で落ちないための念のための確認)
    public static class CpuSensor
    {
        // 地面を探し始める高さ(調べる位置からの上方向のオフセット)
        const float kProbeStartHeight = 0.5f;

        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        // pos の真下(depth m 以内)に地面があるか
        public static bool HasGround(Vector3 pos, float depth)
        {
            Vector3 origin = pos + Vector3.up * kProbeStartHeight;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, s_hits, kProbeStartHeight + depth, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (!IsIgnored(s_hits[i].collider)) return true;
            }
            return false;
        }

        static bool IsIgnored(Collider col)
        {
            return col.GetComponentInParent<TumikiBlock>() != null || col.GetComponentInParent<DarumaCharacter>() != null;
        }
    }
}
