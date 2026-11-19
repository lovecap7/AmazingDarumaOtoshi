using Nakahira;
using UnityEngine;

// ブリンク(瞬間移動)の行き先を決める
public static class DashBlink
{
    // 行き先を少しずつ手前に戻して探す間隔(m)
    const float kStep = 0.25f;
    // これより近くにしか出られないなら、ブリンクしない(m)
    const float kMinDistance = 0.5f;
    // 床と重ならないように、当たりの確認を足元から少し上げる(m)
    const float kFloorClearance = 0.1f;

    static readonly Collider[] s_overlaps = new Collider[32];

    // start から dir へ distance 進んだ所を目指す。壁・ギミック・ほかのキャラクターと重なる所や、ステージの範囲の外は避けて手前に戻す
    // 穴の上は避けない(落ちる)。出られる所がなければ false
    public static bool FindDestination(Vector3 start, Vector3 dir, float distance, float radius, float height,
        StageArea area, Rigidbody self, out Vector3 destination)
    {
        destination = start;
        dir.y = 0.0f;
        if (dir.sqrMagnitude < 0.0001f) return false;
        dir.Normalize();

        for (float d = distance; d >= kMinDistance - 0.0001f; d -= kStep)
        {
            Vector3 p = start + dir * d;
            if (area != null && !IsInside(area, p, radius)) continue;
            if (Overlaps(p, radius, height, self)) continue;
            destination = p;
            return true;
        }
        return false;
    }

    static bool IsInside(StageArea area, Vector3 p, float radius)
    {
        Vector3 c = area.Clamp(p, radius);
        return Mathf.Abs(c.x - p.x) < 0.001f && Mathf.Abs(c.z - p.z) < 0.001f;
    }

    // 体(カプセル)を置いたときに、固い物と重なるか。積み木は押しのけられるので重なってもよい
    static bool Overlaps(Vector3 feet, float radius, float height, Rigidbody self)
    {
        float r = radius * 0.95f;
        Vector3 bottom = feet + Vector3.up * (r + kFloorClearance);
        Vector3 top = feet + Vector3.up * Mathf.Max(r + kFloorClearance, height - r);
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, r, s_overlaps, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = s_overlaps[i];
            if (self != null && col.attachedRigidbody == self) continue;
            if (col.GetComponentInParent<TumikiBlock>() != null) continue;
            return true;
        }
        return false;
    }
}

// ボタンを素早く2回押したかを調べる
public sealed class DoubleTapDetector
{
    float m_lastPressTime = float.NegativeInfinity;

    // pressedThisFrame: このフレームで押されたか。2回目の押下のフレームだけ true を返す
    public bool Update(bool pressedThisFrame, float window)
    {
        if (!pressedThisFrame) return false;
        float now = Time.unscaledTime;
        if (now - m_lastPressTime <= window)
        {
            // 3回目の押下で再び出ないように、続けて押しても次は1回目から数える
            m_lastPressTime = float.NegativeInfinity;
            return true;
        }
        m_lastPressTime = now;
        return false;
    }
}
