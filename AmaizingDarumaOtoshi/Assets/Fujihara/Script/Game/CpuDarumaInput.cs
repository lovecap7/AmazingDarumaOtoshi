using System.Collections.Generic;
using Nakahira;
using UnityEngine;

// CPU の操作（仮）。本格的な AI ができたら差し替える前提の、簡単な動き
//  ・積み木が少ないうちは、近くの落ちている積み木へ向かい、真上でジャンプして拾う
//  ・積み木がそろったら一番近い相手へ向かい、近ければ薙ぎ払い、少し離れていれば股抜きショット
//  ・幽霊になったら生存者へ向かって薙ぎ払う（当てれば復活）
// level（1〜9）が高いほど、反応が速く迷いが少ない。
public class CpuDarumaInput : MonoBehaviour, IDarumaCommandSource
{
    [Range(1, 9)] public int level = 3;

    DarumaPlayer m_player;
    float m_thinkTimer, m_sweepCooldown, m_shotCooldown, m_jumpCooldown;
    Vector2 m_move;
    Transform m_target;
    static readonly List<TumikiBlock> s_blocks = new List<TumikiBlock>();
    static float s_blocksTime = -1f;

    float Skill => (level - 1) / 8f;   // 0〜1

    public DarumaCommand ReadCommand()
    {
        var cmd = new DarumaCommand();
        // DarumaPlayer より先に付けるので、使うときに取る
        if (m_player == null) m_player = GetComponent<DarumaPlayer>();
        if (m_player == null) return cmd;
        Transform body = m_player.CurrentBody;
        if (body == null) return cmd;

        float dt = Time.deltaTime;
        m_sweepCooldown -= dt;
        m_shotCooldown -= dt;
        m_jumpCooldown -= dt;

        // 考え直す間隔（強いほど短い）
        m_thinkTimer -= dt;
        if (m_thinkTimer <= 0f)
        {
            m_thinkTimer = Mathf.Lerp(0.6f, 0.15f, Skill) * Random.Range(0.8f, 1.2f);
            m_target = ChooseTarget(body);
        }
        if (m_target == null) return cmd;

        Vector3 to = m_target.position - body.position;
        to.y = 0f;
        float dist = to.magnitude;
        Vector2 dir = dist > 0.01f ? new Vector2(to.x, to.z) / dist : Vector2.zero;
        // 弱いほどふらつく
        dir = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 1.7f + level) * Mathf.Lerp(35f, 5f, Skill)) * dir;
        m_move = Vector2.Lerp(m_move, dir, 1f - Mathf.Exp(-8f * dt));

        var target = m_target.GetComponentInParent<TumikiBlock>();
        if (target != null)
        {
            // 積み木：近づいて真上に来たらジャンプ（落ちながら触れると拾える）
            cmd.Move = dist > 0.3f ? m_move : Vector2.zero;
            if (dist < 1.1f && m_jumpCooldown <= 0f && m_player.Character != null && m_player.Character.IsGrounded)
            {
                cmd.Jump = true;
                m_jumpCooldown = 1.2f;
            }
            return cmd;
        }

        // 相手：近ければ薙ぎ払い、少し離れていて積み木があれば股抜きショット
        cmd.Move = dist > 1.4f ? m_move : m_move * 0.2f;
        if (dist < 2.2f && m_sweepCooldown <= 0f)
        {
            cmd.Sweep = true;
            m_sweepCooldown = Mathf.Lerp(1.6f, 0.5f, Skill);
        }
        else if (m_player.Character != null && m_player.Character.Stack.Count > 2 && dist < 9f && m_shotCooldown <= 0f)
        {
            Vector3 fwd = body.forward; fwd.y = 0f;
            if (Vector3.Dot(fwd.normalized, to / Mathf.Max(dist, 0.01f)) > Mathf.Lerp(0.8f, 0.95f, Skill))
            {
                cmd.Shot = true;
                m_shotCooldown = Mathf.Lerp(3.5f, 1.2f, Skill);
            }
        }
        return cmd;
    }

    Transform ChooseTarget(Transform body)
    {
        var character = m_player.Character;
        // 生存中で積み木が少なければ、近くの積み木を拾いに行く
        if (character != null && character.Stack.Count < 3 + level / 3)
        {
            var block = NearestLooseBlock(body.position, 8f);
            if (block != null) return block.transform;
        }
        return NearestOpponent(body.position);
    }

    TumikiBlock NearestLooseBlock(Vector3 from, float maxDist)
    {
        // 積み木の一覧は全 CPU で共有し、0.5秒ごとに取り直す
        if (Time.time - s_blocksTime > 0.5f)
        {
            s_blocks.Clear();
            s_blocks.AddRange(FindObjectsByType<TumikiBlock>(FindObjectsSortMode.None));
            s_blocksTime = Time.time;
        }
        TumikiBlock best = null;
        float bestD = maxDist;
        foreach (var b in s_blocks)
        {
            if (b == null || b.CurrentState != TumikiBlock.State.Loose) continue;
            float d = Vector3.Distance(from, b.transform.position);
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    Transform NearestOpponent(Vector3 from)
    {
        Transform best = null;
        float bestD = float.MaxValue;
        foreach (var p in FindObjectsByType<DarumaPlayer>(FindObjectsSortMode.None))
        {
            if (p == m_player || p.Character == null) continue;   // 狙うのは生存者だけ
            float d = Vector3.Distance(from, p.Character.transform.position);
            if (d < bestD) { bestD = d; best = p.Character.transform; }
        }
        return best;
    }
}
