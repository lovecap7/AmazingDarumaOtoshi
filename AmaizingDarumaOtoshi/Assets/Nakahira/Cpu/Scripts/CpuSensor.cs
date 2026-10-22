using UnityEngine;

namespace Nakahira
{
    // CPU が周りを調べるための問い合わせ(足場・壁)。積み木とキャラクターは足場・壁として扱わない
    public static class CpuSensor
    {
        // 地面を探し始める高さ(調べる位置からの上方向のオフセット)
        const float kProbeStartHeight = 0.5f;
        // 壁を調べる高さ(足元から。積み木の上面より少し上なので、落ちている積み木には当たらない)
        const float kWallCheckHeight = 0.6f;

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

        // from から to まで、step m ごとに足元に地面があるか(途中に奈落がないか)
        public static bool HasGroundAlong(Vector3 from, Vector3 to, float step, float depth)
        {
            Vector3 delta = to - from;
            delta.y = 0.0f;
            float length = delta.magnitude;
            int samples = Mathf.CeilToInt(length / step);
            for (int i = 1; i <= samples; i++)
            {
                Vector3 p = from + delta * Mathf.Min(1.0f, i * step / length);
                if (!HasGround(p, depth)) return false;
            }
            return true;
        }

        // from から to までの間に壁(積み木・キャラクター以外の当たり判定)があるか
        public static bool HasWallBetween(Vector3 from, Vector3 to)
        {
            Vector3 a = from + Vector3.up * kWallCheckHeight;
            Vector3 b = new Vector3(to.x, from.y, to.z) + Vector3.up * kWallCheckHeight;
            Vector3 dir = b - a;
            float length = dir.magnitude;
            if (length < 0.01f) return false;
            int count = Physics.RaycastNonAlloc(a, dir / length, s_hits, length, ~0, QueryTriggerInteraction.Ignore);
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
