using System.Collections.Generic;
using Nakahira;
using UnityEngine;

// CPU の操作（仮）。本格的な AI ができたら差し替える前提だが、ひととおり「それっぽく」戦う
//  ・足もとの先に床が無ければ引き返す（自分から場外に落ちない）
//  ・飛んでくる積み木に気づいたら、近ければハンマーで打ち返し、間に合わなければジャンプ・横へよける
//  ・積み木が少ないうちは、安全な場所に落ちている積み木を拾いに行く（首だけのときは相手から離れて拾う）
//  ・相手は「近さ」と「弱さ（積み木の少なさ・首だけ）」で選ぶ
//  ・近づくときはステージの中心側に回り込んでから薙ぎ払い、相手を外へ押し出す
//  ・少し離れていて積み木があれば、相手の動きを先読みして股抜きショット
//  ・幽霊になったら生存者へ向かって薙ぎ払う（当てれば復活）
// level（1〜9）が高いほど、反応が速く、狙いが正確で、よけるのがうまい。
public class CpuDarumaInput : MonoBehaviour, IDarumaCommandSource
{
    [Range(1, 9)] public int level = 3;

    MatchPlayer m_player;
    float m_thinkTimer, m_sweepCooldown, m_shotCooldown, m_jumpCooldown, m_dodgeTimer;
    Vector2 m_move, m_dodgeDir;
    Transform m_target;
    bool m_targetIsBlock;
    float m_aimNoise;

    static readonly List<TumikiBlock> s_blocks = new List<TumikiBlock>();
    static float s_blocksTime = -1f;
    static readonly RaycastHit[] s_hits = new RaycastHit[8];

    float Skill => (level - 1) / 8f;   // 0〜1

    public DarumaCommand ReadCommand()
    {
        var cmd = new DarumaCommand();
        // MatchPlayer より先に付けるので、使うときに取る
        if (m_player == null) m_player = GetComponent<MatchPlayer>();
        if (m_player == null) return cmd;
        Transform body = m_player.Body;
        if (body == null) return cmd;
        var character = m_player.Character;

        float dt = Time.deltaTime;
        m_sweepCooldown -= dt;
        m_shotCooldown -= dt;
        m_jumpCooldown -= dt;
        m_dodgeTimer -= dt;
        RefreshBlocks();

        // ---- 1. 飛んでくる積み木への対応（生存中だけ） ----
        if (character != null && HandleIncoming(character, body, ref cmd)) return cmd;

        // ---- 2. よけている最中はそのまま横へ ----
        if (m_dodgeTimer > 0f)
        {
            cmd.Move = SafeMove(body.position, m_dodgeDir);
            return cmd;
        }

        // ---- 3. 何をするか考え直す（強いほど頻繁） ----
        m_thinkTimer -= dt;
        if (m_thinkTimer <= 0f || m_target == null)
        {
            m_thinkTimer = Mathf.Lerp(0.6f, 0.15f, Skill) * Random.Range(0.8f, 1.2f);
            ChooseTarget(body, character);
            m_aimNoise = Random.Range(-1f, 1f) * Mathf.Lerp(18f, 2f, Skill);   // ショットの狙いのずれ（度）
        }
        if (m_target == null) { cmd.Move = SafeMove(body.position, ToCenter(body.position) * 0.5f); return cmd; }

        if (m_targetIsBlock) Collect(character, body, ref cmd);
        else Fight(character, body, ref cmd);
        return cmd;
    }

    // ================= 積み木を拾う =================
    void Collect(DarumaCharacter character, Transform body, ref DarumaCommand cmd)
    {
        Vector3 to = Flat(m_target.position - body.position);
        float dist = to.magnitude;
        Vector2 dir = Steer(to / Mathf.Max(dist, 0.01f));
        // 真上に来たらジャンプ（落ちながら触れると拾える）
        cmd.Move = SafeMove(body.position, dist > 0.3f ? dir : Vector2.zero);
        if (dist < 1.1f && m_jumpCooldown <= 0f && character != null && character.IsGrounded)
        {
            cmd.Jump = true;
            m_jumpCooldown = 1.0f;
        }
    }

    // ================= 戦う =================
    void Fight(DarumaCharacter character, Transform body, ref DarumaCommand cmd)
    {
        Vector3 targetPos = m_target.position;
        Vector3 to = Flat(targetPos - body.position);
        float dist = to.magnitude;
        Vector3 toDir = to / Mathf.Max(dist, 0.01f);

        // 首だけ（積み木なし）で相手が近いなら、無理に戦わず距離を取る（幽霊は除く）
        if (character != null && character.Stack.Count == 0 && dist < 4f)
        {
            cmd.Move = SafeMove(body.position, Steer(-toDir + ToCenter3(body.position) * 0.6f));
            if (dist < 2.2f && m_sweepCooldown <= 0f && Facing(body, toDir) > 0.5f) { cmd.Sweep = true; m_sweepCooldown = 1f; }
            return;
        }

        // 中心側に回り込む：相手から見て「ステージの中心の方向」に立ってから叩くと、相手は外へ飛ぶ
        Vector3 center = StageCenter();
        Vector3 outward = Flat(targetPos - center);
        outward = outward.sqrMagnitude > 0.01f ? outward.normalized : -toDir;
        Vector3 stand = targetPos - outward * 1.4f;
        Vector3 toStand = Flat(stand - body.position);
        bool inPosition = toStand.magnitude < 0.9f || dist < 1.6f;

        // 薙ぎ払い：届く距離で、だいたい相手を向いていたら
        if (dist < 2.1f)
        {
            cmd.Move = SafeMove(body.position, Steer(toDir) * 0.35f);   // 向きを合わせる程度に寄る
            if (m_sweepCooldown <= 0f && (inPosition || Skill < 0.3f) && Facing(body, toDir) > Mathf.Lerp(0.2f, 0.6f, Skill))
            {
                cmd.Sweep = true;
                m_sweepCooldown = Mathf.Lerp(1.4f, 0.45f, Skill);
            }
            return;
        }

        // 股抜きショット：少し離れていて積み木が3つ以上。相手の動きを先読みして、その方向へ撃つ（撃つ瞬間にその向きを向く）
        if (character != null && character.Stack.Count >= 3 && dist > 3f && dist < 11f && m_shotCooldown <= 0f && character.CanStartShot)
        {
            Vector3 lead = targetPos + TargetVelocity() * Mathf.Lerp(0.1f, 0.35f, Skill) * (dist / 6f);
            Vector3 aim = Quaternion.Euler(0f, m_aimNoise, 0f) * Flat(lead - body.position).normalized;
            // 撃った方向の先が自分より外（場外）なら撃たない（反動で落ちないように、念のため）
            if (IsSafe(body.position - aim * 0.8f))
            {
                cmd.Move = new Vector2(aim.x, aim.z);
                cmd.Shot = true;
                m_shotCooldown = Mathf.Lerp(3.2f, 1.1f, Skill);
                return;
            }
        }

        // 近づく：回り込み位置へ。遠いうちは相手へ直接
        Vector3 goal = dist > 5f || Skill < 0.3f ? toDir : toStand.normalized;
        cmd.Move = SafeMove(body.position, Steer(goal));
    }

    // ================= 飛んでくる積み木 =================
    // 対応したら true（このフレームの命令はそれで決まり）
    bool HandleIncoming(DarumaCharacter me, Transform body, ref DarumaCommand cmd)
    {
        // 気づくかどうか（毎フレームの確率。弱いほど気づくのが遅れる：平均で 0.2 秒 〜 ほぼすぐ）
        if (Random.value > Mathf.Lerp(0.08f, 0.6f, Skill)) return false;

        TumikiBlock threat = null;
        float bestTime = float.MaxValue;
        Vector3 threatVel = Vector3.zero;
        foreach (var b in s_blocks)
        {
            if (b == null || !b.IsHarmful) continue;
            if (ReferenceEquals(b.Owner, me)) continue;
            Vector3 rel = Flat(body.position - b.transform.position);
            Vector3 v = Flat(b.HorizontalVelocity);
            float speed = v.magnitude;
            if (speed < 1f || rel.magnitude > 9f) continue;
            float closing = Vector3.Dot(rel, v / speed);
            if (closing <= 0f) continue;                       // 離れていく
            float time = closing / speed;
            float miss = Vector3.Cross(v / speed, rel).magnitude;  // まっすぐ来たときに、どれだけ横にそれるか
            if (miss > 1.1f || time > 0.9f) continue;
            if (time < bestTime) { bestTime = time; threat = b; threatVel = v; }
        }
        if (threat == null) return false;

        Vector3 toBlock = Flat(threat.transform.position - body.position);
        float d = toBlock.magnitude;

        // 打ち返し：ハンマーが空いていて、もうすぐ届くなら、そちらを向いて薙ぎ払う
        if (d < Mathf.Lerp(1.8f, 2.6f, Skill) && m_sweepCooldown <= 0f)
        {
            var dir = toBlock / Mathf.Max(d, 0.01f);
            cmd.Move = new Vector2(dir.x, dir.z) * 0.3f;
            cmd.Sweep = true;
            m_sweepCooldown = Mathf.Lerp(1.2f, 0.4f, Skill);
            return true;
        }

        // よける：ジャンプできるならジャンプ、でなければ飛んでくる向きと直角に（床がある側へ）
        if (me.IsGrounded && m_jumpCooldown <= 0f && bestTime < 0.45f && Random.value < 0.5f)
        {
            cmd.Jump = true;
            m_jumpCooldown = 0.9f;
            return true;
        }
        Vector3 side = Vector3.Cross(Vector3.up, threatVel.normalized);
        if (!IsSafe(body.position + side * 1.5f)) side = -side;
        m_dodgeDir = new Vector2(side.x, side.z);
        m_dodgeTimer = 0.35f;
        cmd.Move = SafeMove(body.position, m_dodgeDir);
        return true;
    }

    // ================= 相手・積み木を選ぶ =================
    void ChooseTarget(Transform body, DarumaCharacter character)
    {
        m_target = null;
        m_targetIsBlock = false;

        // 生存中で積み木が少なければ拾いに行く（首だけなら遠くても、相手から離れた積み木を優先）
        if (character != null)
        {
            int want = 3 + level / 3;
            int count = character.Stack.Count;
            if (count < want)
            {
                float range = count == 0 ? 20f : 9f;
                var block = BestLooseBlock(body.position, range, count == 0);
                if (block != null) { m_target = block.transform; m_targetIsBlock = true; return; }
            }
        }

        // 相手：近い・弱い（積み木が少ない、首だけ）・場外に近い ほど狙う
        var director = DarumaMatch.Current;
        if (director == null) return;
        Vector3 center = StageCenter();
        float best = float.MaxValue;
        foreach (var p in director.Players)
        {
            if (p == m_player || p.Character == null) continue;   // 狙うのは生存者だけ
            var c = p.Character;
            float d = Vector3.Distance(body.position, c.transform.position);
            float score = d + c.Stack.Count * Mathf.Lerp(0.2f, 1.2f, Skill);
            if (c.Stack.Count == 0) score -= Mathf.Lerp(0f, 4f, Skill);                      // 首だけ＝一撃で倒せる
            score -= Mathf.Lerp(0f, 0.25f, Skill) * Flat(c.transform.position - center).magnitude;   // 外側にいる相手は押し出しやすい
            if (c.IsInvincible) score += 6f;
            if (score < best) { best = score; m_target = c.transform; }
        }
    }

    TumikiBlock BestLooseBlock(Vector3 from, float maxDist, bool avoidOpponents)
    {
        TumikiBlock best = null;
        float bestScore = float.MaxValue;
        foreach (var b in s_blocks)
        {
            if (b == null || b.CurrentState != TumikiBlock.State.Loose) continue;
            Vector3 p = b.transform.position;
            float d = Vector3.Distance(from, p);
            if (d > maxDist || !IsSafe(p)) continue;
            float score = d;
            if (avoidOpponents) score -= Mathf.Min(6f, NearestOpponentDistance(p)) * 0.8f;
            if (score < bestScore) { bestScore = score; best = b; }
        }
        return best;
    }

    float NearestOpponentDistance(Vector3 p)
    {
        float best = 99f;
        var director = DarumaMatch.Current;
        if (director == null) return best;
        foreach (var o in director.Players)
        {
            if (o == m_player || o.Character == null) continue;
            best = Mathf.Min(best, Vector3.Distance(p, o.Character.transform.position));
        }
        return best;
    }

    Vector3 TargetVelocity()
    {
        if (m_target == null) return Vector3.zero;
        var rb = m_target.GetComponentInParent<Rigidbody>();
        return rb != null ? Flat(rb.linearVelocity) : Vector3.zero;
    }

    // ================= 床の確認（場外に落ちない） =================
    // 行きたい方向の少し先に床が無ければ、中心のほうへ向きを変える
    Vector2 SafeMove(Vector3 pos, Vector2 move)
    {
        if (move.sqrMagnitude < 0.0001f) return move;
        var dir = new Vector3(move.x, 0f, move.y);
        float look = Mathf.Lerp(0.9f, 1.8f, Skill);
        if (IsSafe(pos + dir.normalized * look)) return move;
        // 中心寄りに曲げて、それでも無理なら中心へまっすぐ
        Vector3 c = ToCenter3(pos);
        Vector3 bent = (dir.normalized + c * 1.5f).normalized;
        if (IsSafe(pos + bent * look)) return new Vector2(bent.x, bent.z) * move.magnitude;
        return new Vector2(c.x, c.z);
    }

    // その位置の下に床（積み木・キャラクター以外の、動かない当たり判定）があるか
    static bool IsSafe(Vector3 p)
    {
        int n = Physics.RaycastNonAlloc(p + Vector3.up * 1.5f, Vector3.down, s_hits, 4f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (s_hits[i].collider.attachedRigidbody == null) return true;
        return false;
    }

    // ================= 小さな道具 =================
    Vector2 Steer(Vector3 dir)
    {
        var d = new Vector2(dir.x, dir.z);
        if (d.sqrMagnitude > 1f) d.Normalize();
        // 弱いほどふらつく
        d = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 1.7f + level) * Mathf.Lerp(30f, 3f, Skill)) * d;
        m_move = Vector2.Lerp(m_move, d, 1f - Mathf.Exp(-Mathf.Lerp(6f, 14f, Skill) * Time.deltaTime));
        return m_move;
    }

    static Vector3 StageCenter()
    {
        var director = DarumaMatch.Current;
        if (director == null) return Vector3.zero;
        if (director.StageArea != null) return director.StageArea.transform.position;
        var pts = director.SpawnPoints;
        if (pts.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        foreach (var t in pts) sum += t.position;
        return sum / pts.Count;
    }

    static Vector3 ToCenter3(Vector3 pos)
    {
        var v = Flat(StageCenter() - pos);
        return v.sqrMagnitude > 0.01f ? v.normalized : Vector3.zero;
    }

    static Vector2 ToCenter(Vector3 pos)
    {
        var v = ToCenter3(pos);
        return new Vector2(v.x, v.z);
    }

    static float Facing(Transform body, Vector3 dir)
    {
        var f = Flat(body.forward);
        return f.sqrMagnitude > 0.0001f ? Vector3.Dot(f.normalized, dir) : 0f;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // 積み木の一覧は全 CPU で共有し、少しごとに取り直す（飛んでくる積み木に気づけるよう短めに）
    static void RefreshBlocks()
    {
        if (Time.time - s_blocksTime < 0.15f) return;
        s_blocks.Clear();
        s_blocks.AddRange(FindObjectsByType<TumikiBlock>());
        s_blocksTime = Time.time;
    }
}
