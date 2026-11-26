using System.Collections.Generic;
using Nakahira;
using UnityEngine;

// 1. 土埃：だるまが地面を走る・ダッシュすると、足元に白い煙が少しずつ出て、うしろへ流れて消える。
//    ジャンプした瞬間と、勢いよく着地した瞬間は、足元に輪のように煙が広がる
public class DustVfx : MonoBehaviour
{
    class State
    {
        public Vector3 lastPos;
        public float distance;
        public bool wasGrounded = true;
        public float lastVy;
    }

    readonly Dictionary<DarumaCharacter, State> m_states = new Dictionary<DarumaCharacter, State>();
    readonly List<DarumaCharacter> m_dead = new List<DarumaCharacter>();
    ParticleSystem m_dust;

    void LateUpdate()
    {
        var s = MatchVfxSettings.Current;
        var match = DarumaMatch.Current;
        if (!s.dust || match == null) return;
        if (m_dust == null) m_dust = VfxKit.CreateSystem("Dust", transform, false, VfxKit.SoftDot, 600, 2.2f, -0.03f, false);

        foreach (var p in match.Players)
        {
            var c = p != null ? p.Character : null;
            if (c == null) continue;
            if (!m_states.TryGetValue(c, out var st))
            {
                m_states[c] = st = new State { lastPos = c.transform.position, wasGrounded = c.IsGrounded };
                continue;
            }
            Step(c, st, s);
        }

        // 消えた体を忘れる
        m_dead.Clear();
        foreach (var kv in m_states) if (kv.Key == null) m_dead.Add(kv.Key);
        foreach (var k in m_dead) m_states.Remove(k);
    }

    void Step(DarumaCharacter c, State st, MatchVfxSettings s)
    {
        Vector3 pos = c.transform.position;
        Vector3 move = pos - st.lastPos;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        st.lastPos = pos;
        bool grounded = c.IsGrounded;
        var rb = c.GetComponent<Rigidbody>();
        float vy = rb != null ? rb.linearVelocity.y : move.y / dt;
        Vector3 feet = pos + Vector3.up * 0.06f;
        bool visible = c.CanBeAffected && move.sqrMagnitude < 4f;   // ワープ・出現の瞬間（大きく飛んだ）には出さない

        if (visible)
        {
            // ジャンプ：地面から上へ跳んだ
            if (st.wasGrounded && !grounded && vy > 1.5f) Ring(feet, s.jumpPuffs, 1.6f, s.runSize * 1.2f, s);
            // 着地：勢いよく落ちてきた
            if (!st.wasGrounded && grounded && st.lastVy < -s.landSpeed) Ring(feet, s.landPuffs, 2.2f, s.runSize * 1.5f, s);

            // 走る・ダッシュ：一定の距離ごとに、足元から少しうしろへ流れる煙
            Vector3 flat = new Vector3(move.x, 0f, move.z);
            float speed = flat.magnitude / dt;
            if (grounded && speed > 1.5f)
            {
                bool dash = c.IsDashing;
                st.distance += flat.magnitude;
                float spacing = dash ? s.dashSpacing : s.runSpacing;
                while (st.distance >= spacing)
                {
                    st.distance -= spacing;
                    Vector3 back = -flat.normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, back);
                    int n = dash ? 2 : 1;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 at = feet + back * 0.35f + side * Random.Range(-0.3f, 0.3f);
                        Vector3 v = back * Random.Range(0.4f, dash ? 1.6f : 0.9f) + Vector3.up * Random.Range(0.25f, 0.6f) + side * Random.Range(-0.3f, 0.3f);
                        float size = (dash ? s.dashSize : s.runSize) * Random.Range(0.8f, 1.2f);
                        VfxKit.Emit(m_dust, at, v, size, Random.Range(0.45f, 0.7f), s.dustColor);
                    }
                }
            }
            else st.distance = 0f;
        }

        st.wasGrounded = grounded;
        st.lastVy = vy;
    }

    // 足元に輪のように広がる煙
    void Ring(Vector3 at, int count, float speed, float size, MatchVfxSettings s)
    {
        for (int i = 0; i < count; i++)
        {
            float a = (i + Random.Range(-0.3f, 0.3f)) / count * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            VfxKit.Emit(m_dust, at + dir * 0.35f, dir * speed * Random.Range(0.7f, 1.2f) + Vector3.up * Random.Range(0.1f, 0.5f),
                size * Random.Range(0.8f, 1.3f), Random.Range(0.4f, 0.65f), s.dustColor);
        }
    }
}
