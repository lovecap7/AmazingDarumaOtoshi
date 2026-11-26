using System.Reflection;
using Nakahira;
using UnityEngine;

// 打ち返しだけを見るシーン（RallyScene）。ステージ1（ギミックなし）で2人が向かい合い、1つの積み木を打ち返し合う。
//  ・試合（DarumaMatch）は使わない。キャラクター2人と積み木1つをここで出す
//  ・2人には RallyInput を付ける：その場で相手を向き、飛んできた積み木がハンマーの届くところに来たら薙ぎ払って打ち返す
//  ・積み木が止まったり（打ち返しそこね）消えたりしたら、交互にサーブし直す
//  ・打ち返しの音は MatchSounds の①〜④（回数で変わる）。試合ではないので MatchAudio の代わりにここで鳴らす
// 2026-11-26：試合と同じものをそのまま使う（キャラクターのプレハブ・ハンマーの性能 Hammer_Default・積み木のプレハブ）。
//   このシーンだけの速さ・打ち返す向きの書き替えはやめた。サーブの速さも、サーブする人のハンマーの性能（SweepLaunchSpeed）
//   MissAfter 回打ち返したら、次に受ける人は打ち返さずに当たる（無敵も外す）→ 即死の大爆発（KillExplosionVfx）を見せる。
//   やられた人は RespawnDelay 秒後に出直して、ラリーを始め直す
public class RallyDemo : MonoBehaviour
{
    [Tooltip("2人のキャラクター（試合と同じ CharacterData のプレハブを使う）。空なら characterPrefab")]
    [SerializeField] CharacterData[] characters = new CharacterData[2];
    [SerializeField] DarumaCharacter characterPrefab;
    [SerializeField] TumikiBlock tumikiPrefab;
    [Tooltip("2人の間の距離")]
    [SerializeField] float distance = 12f;
    [Tooltip("キャラクターに積んでおく積み木の数（見た目用）")]
    [SerializeField] int stackBlocks = 3;
    [Tooltip("積み木が止まってからサーブし直すまでの秒数")]
    [SerializeField] float serveDelay = 1f;
    [Header("わざと食らう")]
    [Tooltip("この回数打ち返したら、次に受ける人は打ち返さずに当たる（0 でずっと打ち返す）")]
    [SerializeField] int missAfter = 15;
    [Tooltip("やられてから出直すまでの秒数")]
    [SerializeField] float respawnDelay = 3.5f;

    readonly DarumaCharacter[] players = new DarumaCharacter[2];
    readonly Vector3[] homes = new Vector3[2];
    readonly float[] respawnAt = new float[2];
    bool missArmed;
    TumikiBlock ball;
    AudioSource audioSource;
    int server;
    float idle;

    void Start()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        DarumaHammer.Hit += OnHammerHit;
        for (int i = 0; i < 2; i++) Spawn(i);
        idle = serveDelay;   // 始まってすぐサーブ
    }

    void OnDestroy()
    {
        DarumaHammer.Hit -= OnHammerHit;
    }

    DarumaCharacter PrefabFor(int i)
    {
        var data = characters != null && i < characters.Length ? characters[i] : null;
        var p = data != null && data.prefab != null ? data.prefab.GetComponent<DarumaCharacter>() : null;
        return p != null ? p : characterPrefab;
    }

    void Spawn(int i)
    {
        homes[i] = new Vector3(i == 0 ? -distance * 0.5f : distance * 0.5f, 0f, 0f);
        // 名前はプレハブのまま（「Akadaru(Clone)」。GameModelSwapper が名前からキャラのモデルを探す）
        var ch = Instantiate(PrefabFor(i), homes[i], Quaternion.LookRotation(i == 0 ? Vector3.right : Vector3.left));
        ch.SetInvincible(float.MaxValue);   // 打ち返しそこねても倒れない（わざと食らうときだけ外す）
        var input = ch.gameObject.AddComponent<RallyInput>();
        ch.SetCommandSource(input);
        players[i] = ch;
    }

    void Update()
    {
        for (int i = 0; i < 2; i++)
        {
            var ch = players[i];
            if (ch == null)
            {
                // やられた：少し待ってから出直す
                if (respawnAt[i] <= 0f) respawnAt[i] = Time.time + respawnDelay;
                else if (Time.time >= respawnAt[i]) { respawnAt[i] = 0f; Spawn(i); idle = 0f; }
                continue;
            }
            var other = players[1 - i];
            if (other != null) ch.GetComponent<RallyInput>().Setup(ch, other);
            Pin(ch, homes[i], other != null ? other.transform.position : homes[1 - i]);
            Refill(ch);
        }
        // 片方がいない間はサーブしない
        if (players[0] == null || players[1] == null) { idle = 0f; return; }

        // 積み木：積まれてしまったら新しく出す。止まっていたらしばらく待ってサーブ
        if (ball != null && ball.CurrentState == TumikiBlock.State.Stacked) ball = null;
        bool flying = ball != null && ball.IsHarmful;
        idle = flying ? 0f : idle + Time.deltaTime;
        if (idle >= serveDelay) Serve();
    }

    // その場に立たせて、相手の方を向かせる（打ち返しはいつも向いている向き＝相手へ）
    static void Pin(DarumaCharacter ch, Vector3 home, Vector3 target)
    {
        Vector3 to = target - home;
        to.y = 0f;
        Quaternion rot = to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to) : ch.transform.rotation;
        Vector3 pos = ch.transform.position;
        pos.x = home.x;
        pos.z = home.z;
        var rb = ch.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = pos;
            rb.rotation = rot;
            var v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(0f, v.y, 0f);
        }
        ch.transform.SetPositionAndRotation(pos, rot);
    }

    void Serve()
    {
        idle = 0f;
        var from = players[server];
        var to = players[1 - server];
        if (from == null || to == null || tumikiPrefab == null) return;
        // ラリーを始め直すので、2人とも「打ち返す・無敵」に戻す（わざと食らう状態を残さない）
        missArmed = false;
        foreach (var p in players)
        {
            if (p == null) continue;
            p.GetComponent<RallyInput>().Miss = false;
            p.SetInvincible(float.MaxValue);
        }
        Vector3 dir = to.transform.position - from.transform.position;
        dir.y = 0f;
        dir.Normalize();
        Vector3 pos = from.transform.position + dir * 1.3f;
        if (ball == null)
        {
            ball = Instantiate(tumikiPrefab, pos, Quaternion.identity);
            ball.SetColor(TumikiColor.Red);
        }
        else ball.Teleport(pos, 0f);
        // サーブの速さは、試合でハンマーが積み木を弾く速さ（サーブする人のハンマーの性能そのもの）
        var hammer = from.GetComponentInChildren<DarumaHammer>();
        float speed = hammer != null && hammer.Params != null ? hammer.Params.SweepLaunchSpeed : 30f;
        ball.Launch(dir, speed, from, from);
        Play(MatchSounds.Instance.Swing);
        server = 1 - server;   // 次のサーブは相手
    }

    void OnHammerHit(HammerHitInfo h)
    {
        // MissAfter 回打ち返したら、次に受ける人（打った人の相手）は打ち返さずに当たる。無敵も外して、即死するようにする
        if (missAfter > 0 && h.ReturnCount >= missAfter && !missArmed)
        {
            missArmed = true;   // 1回のラリーで食らうのは1人だけ（サーブし直すまで）
            for (int i = 0; i < 2; i++)
            {
                var ch = players[i];
                if (ch == null || ReferenceEquals(ch, h.Owner)) continue;
                var input = ch.GetComponent<RallyInput>();
                if (!input.Miss) Debug.Log($"[RallyScene] {h.ReturnCount} 回打ち返した。{ch.name} は打ち返さずに食らう");
                input.Miss = true;
                ch.SetInvincible(0f);
            }
        }
        var s = MatchSounds.Instance;
        var clip = h.ReturnCount > 0 ? s.ReturnHit(h.ReturnCount) : null;
        if (clip != null) Play(clip, s.ReturnPitch(h.ReturnCount));
        else Play(s.HitBlock);
    }

    // 音の高さを変えて鳴らすので、前の音の余韻まで高さが変わらないよう AudioSource を交互に使う
    AudioSource[] sources;
    int nextSource;

    void Play(AudioClip clip, float pitch = 1f)
    {
        if (clip == null) return;
        if (sources == null) sources = new[] { audioSource, gameObject.AddComponent<AudioSource>() };
        var src = sources[nextSource];
        nextSource = (nextSource + 1) % sources.Length;
        src.pitch = pitch;
        src.PlayOneShot(clip, MatchSounds.Instance.SeVolume);
    }

    void Refill(DarumaCharacter ch)
    {
        if (tumikiPrefab == null || ch.Stack.Count >= stackBlocks) return;
        var block = Instantiate(tumikiPrefab, ch.transform.position, ch.transform.rotation);
        block.SetColor((TumikiColor)(ch.Stack.Count % 4));
        ch.Stack.AddBottom(block);
    }
}

// 打ち返し専用の操作：動かずに、飛んできた積み木がハンマーの届くところまで来たら薙ぎ払う（向きは RallyDemo が相手に合わせる）
public class RallyInput : MonoBehaviour, IDarumaCommandSource
{
    // ハンマーが正面を通るときに、積み木が体の中心からこの距離にあれば当たる（届く距離 2.1 の内側）
    const float HitDistance = 1.6f;

    DarumaCharacter me, opponent;
    float sweepCooldown;

    // true の間は振らない（わざと食らう）
    public bool Miss { get; set; }

    public void Setup(DarumaCharacter self, DarumaCharacter other)
    {
        me = self;
        opponent = other;
    }

    public DarumaCommand ReadCommand()
    {
        var cmd = new DarumaCommand();
        if (me == null || Miss) return cmd;
        sweepCooldown -= Time.deltaTime;
        if (sweepCooldown > 0f) return cmd;

        var hammer = me.GetComponentInChildren<DarumaHammer>();
        // ハンマーは振り始めから振り終わりまでに体の横から反対の横へ回り、ちょうど半分で正面を通る。
        // 積み木が当たる距離に来る時刻に、ハンマーが正面を通るように振り始める
        float frontTime = hammer != null ? hammer.SweepDuration * 0.5f : 0.125f;
        Vector3 body = me.transform.position;
        foreach (var b in TumikiBlock.All)
        {
            if (b == null || !b.IsHarmful || ReferenceEquals(b.Owner, me)) continue;
            Vector3 rel = b.transform.position - body;
            Vector3 v = b.HorizontalVelocity;
            rel.y = 0f;
            v.y = 0f;
            if (v.sqrMagnitude < 1f || Vector3.Dot(rel, v) >= 0f) continue;   // こちらへ向かっていない
            float timeToReach = (rel.magnitude - HitDistance) / v.magnitude;
            if (timeToReach <= frontTime + Time.deltaTime)
            {
                cmd.Sweep = true;
                sweepCooldown = 0.35f;
                break;
            }
        }
        return cmd;
    }
}
