using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

// 6. あおだるの必殺技「疾風連撃」の斬撃の演出：稲妻をまとってビリビリしている、プラズマの斬撃。色はプレイヤーの色
//    末原さんの AodaruGaleSlash（飛んでいく斬撃。当たり判定はそちら）を見つけて、その形（三日月）に合わせて見た目を重ねるだけ。
//    斬撃の動き・当たり判定は変えない（設定でもとの水色の三日月を隠すだけ）
//    ・光の帯：三日月の形に、プレイヤーの色のにじんだ光と、白く光る芯
//    ・稲妻：三日月に沿って、ギザギザの稲妻が何本もまとわりつき、短い間隔で形を変えてバチバチする
//    ・枝分かれ：三日月から外へ、短い稲妻が飛び出しては消える
//    ・火花・残光：火花が後ろへ散り、通ったあとに光の残像が残る。消えるときはバチッとはじける
//    ・強化中のハンマー：疾風連撃の効果時間の間、あおだるのハンマーにも同じ稲妻（光と黒）がまとわりつき、火花が散る
public class AodaruPlasmaVfx : MonoBehaviour
{
    class Fork
    {
        public LineRenderer line;
        public float life, age;
        public float u;           // 三日月のどこから出るか（-1〜1）
        public Vector3 dir;       // 出る向き（斬撃のローカル）
        public float length;
        public bool dark;         // 黒い枝分かれ
    }

    class Slash
    {
        public Yurube.AodaruGaleSlash src;
        public Transform tr;
        public float width, height, depth, age, flicker;
        public Color color, core;
        public LineRenderer aura, glow, coreLine;
        public LineRenderer[] bolts;
        public LineRenderer[] darkBolts;     // 黒い稲妻（光の稲妻と混ざって、黒い放電に見える。白い床でも見えやすく）
        public readonly List<Fork> forks = new List<Fork>();
        public Vector3 lastPos, lastDir;
        public float forkAcc, sparkAcc, trailAcc;
    }

    const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo F_owner = typeof(Yurube.AodaruGaleSlash).GetField("m_owner", Priv);
    static readonly FieldInfo F_width = typeof(Yurube.AodaruGaleSlash).GetField("m_width", Priv);
    static readonly FieldInfo F_height = typeof(Yurube.AodaruGaleSlash).GetField("m_height", Priv);
    static readonly FieldInfo F_depth = typeof(Yurube.AodaruGaleSlash).GetField("m_depth", Priv);
    static readonly FieldInfo F_fade = typeof(Yurube.AodaruGaleSlash).GetField("m_fade", Priv);
    const float kFadeTime = 0.15f;   // もとの斬撃が消えるまでの秒数（AodaruGaleSlash.kFadeTime と同じ）
    const int kGlowPoints = 17, kBoltPoints = 26, kForkPoints = 7;

    readonly List<Slash> m_slashes = new List<Slash>();
    readonly HashSet<Yurube.AodaruGaleSlash> m_known = new HashSet<Yurube.AodaruGaleSlash>();
    ParticleSystem m_sparks, m_crackle, m_trail;
    Material m_auraMat, m_glowMat, m_coreMat, m_boltMat, m_darkBoltMat;
    ParticleSystem m_darkSparks;

    static float Dt => Mathf.Min(Time.unscaledDeltaTime, 0.1f);

    void LateUpdate()
    {
        var s = MatchVfxSettings.Current;
        if (!s.aodaruPlasma) { ClearAll(); return; }

        // 新しく出た斬撃を見つける
        foreach (var src in FindObjectsByType<Yurube.AodaruGaleSlash>())
            if (src != null && m_known.Add(src)) Register(src, s);

        for (int i = m_slashes.Count - 1; i >= 0; i--)
        {
            var sl = m_slashes[i];
            if (sl.src == null)
            {
                // 斬撃が消えた（壁に当たった・届ききった）：バチッとはじける
                Burst(sl, sl.lastPos, sl.lastDir, 1f);
                m_known.Remove(sl.src);
                m_slashes.RemoveAt(i);
                continue;
            }
            Step(sl, s);
        }
        m_known.RemoveWhere(k => k == null);

        UpdateHammers(s);
    }

    // ---------------- 必殺技の強化中：ハンマーにも稲妻をまとわせる ----------------
    class Charge
    {
        public Yurube.AodaruSpecial special;
        public Nakahira.DarumaCharacter character;
        public Nakahira.DarumaHammer hammer;
        public Transform root;              // ハンマー（支点）の子。ここにハンマーの向きで稲妻を置く
        public LineRenderer glow;
        public LineRenderer[] bolts, darkBolts;
        public readonly List<Fork> forks = new List<Fork>();
        public float tip, age, flicker, forkAcc, sparkAcc;
        public Color color, core;
    }
    readonly Dictionary<Yurube.AodaruSpecial, Charge> m_charges = new Dictionary<Yurube.AodaruSpecial, Charge>();
    readonly List<Yurube.AodaruSpecial> m_chargeDead = new List<Yurube.AodaruSpecial>();
    float m_chargeScan;

    void UpdateHammers(MatchVfxSettings s)
    {
        if ((m_chargeScan -= Dt) <= 0f)
        {
            m_chargeScan = 0.25f;
            foreach (var sp in FindObjectsByType<Yurube.AodaruSpecial>())
                if (sp != null && !m_charges.ContainsKey(sp)) m_charges[sp] = new Charge { special = sp, character = sp.GetComponent<Nakahira.DarumaCharacter>() };
        }
        m_chargeDead.Clear();
        foreach (var kv in m_charges)
        {
            var c = kv.Value;
            if (kv.Key == null || c.character == null) { DropCharge(c); m_chargeDead.Add(kv.Key); continue; }
            // 疾風連撃の効果時間中だけ（残り時間がある間）
            bool on = c.special.RemainingSeconds > 0f && c.character.Hammer != null && c.character.Hammer.gameObject.activeInHierarchy;
            if (!on || (c.hammer != null && c.hammer != c.character.Hammer)) DropCharge(c);
            if (on) StepCharge(c, s);
        }
        foreach (var k in m_chargeDead) m_charges.Remove(k);
    }

    void StepCharge(Charge c, MatchVfxSettings s)
    {
        Ensure(s);
        float dt = Dt;
        if (c.root == null)
        {
            c.hammer = c.character.Hammer;
            c.tip = HammerTip(c.hammer);
            c.age = 0f;
            var go = new GameObject("PlasmaHammer");
            go.transform.SetParent(c.hammer.transform, false);
            c.root = go.transform;
            var player = MatchVfx.PlayerOf(c.character);
            var col = player != null ? MatchVfx.ColorOf(player) : new Color(0.45f, 0.85f, 1f);
            c.color = col;
            c.core = Color.Lerp(col, Color.white, 0.55f);
            c.glow = MakeLine("PlasmaHammerGlow", c.root, m_glowMat, 8);
            c.glow.widthCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0.6f));
            c.bolts = new LineRenderer[4];
            c.darkBolts = new LineRenderer[3];
            for (int i = 0; i < c.bolts.Length; i++) c.bolts[i] = MakeLine("PlasmaHammerBolt", c.root, m_boltMat, 14);
            for (int i = 0; i < c.darkBolts.Length; i++) c.darkBolts[i] = MakeLine("PlasmaHammerDarkBolt", c.root, m_darkBoltMat, 14);
            ReshapeCharge(c);
            // 強化が始まった瞬間：バチッと放電
            for (int i = 0; i < 30; i++) EmitHammerSpark(c, 2.5f);
        }
        c.age += dt;
        float intro = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(c.age / 0.15f));
        float dark = Mathf.Clamp01(s.aodaruDark);
        bool swinging = c.hammer.IsBusy;
        float hum = 0.85f + 0.15f * Mathf.Sin(c.age * 80f) + Random.Range(-0.1f, 0.1f);

        // 柄から頭へ、光のにじみ（控えめ）
        for (int i = 0; i < 8; i++) c.glow.SetPosition(i, new Vector3(0f, 0f, Mathf.Lerp(0.1f, 1.02f, i / 7f) * c.tip));
        c.glow.widthMultiplier = 0.8f * c.tip * intro * hum * Mathf.Max(0.1f, s.aodaruGlowWidth);
        SetColor(c.glow, c.color, 0.7f * intro);

        c.flicker -= dt;
        if (c.flicker <= 0f) { c.flicker = Mathf.Max(0.01f, s.aodaruFlicker) * Random.Range(0.7f, 1.3f); ReshapeCharge(c); }
        for (int i = 0; i < c.bolts.Length; i++) SetColor(c.bolts[i], i == 0 ? c.core : Color.Lerp(c.color, Color.white, 0.25f), intro);
        for (int i = 0; i < c.darkBolts.Length; i++) SetColor(c.darkBolts[i], new Color(0.03f, 0.02f, 0.05f), 0.9f * dark * intro);

        // 頭から外へ、枝分かれの稲妻（振っている間は多め）
        c.forkAcc += dt * s.aodaruForkRate * (swinging ? 1.6f : 1.0f);
        while (c.forkAcc >= 1f) { c.forkAcc -= 1f; SpawnHammerFork(c, Random.value < 0.4f * dark / 0.8f); }
        for (int i = c.forks.Count - 1; i >= 0; i--)
        {
            var f = c.forks[i];
            if (f.line == null) { c.forks.RemoveAt(i); continue; }
            f.age += dt;
            if (f.age >= f.life) { f.line.enabled = false; continue; }
            float k = 1f - f.age / f.life;
            if (f.dark) SetColor(f.line, new Color(0.03f, 0.02f, 0.05f), 0.9f * dark * k);
            else SetColor(f.line, Random.value < 0.5f ? c.core : c.color, k);
        }

        // 火花（振っている間は多め）
        c.sparkAcc += Time.deltaTime * s.aodaruSparkRate * (swinging ? 0.8f : 0.3f);
        while (c.sparkAcc >= 1f) { c.sparkAcc -= 1f; EmitHammerSpark(c, 1f); }
    }

    // ハンマーの頭のあたり（支点から見た、ハンマーの向きでの点）。t は柄の根元 0 〜 先 1
    Vector3 HammerPoint(Charge c, float t, Vector2 around)
    {
        return new Vector3(around.x, around.y, t * c.tip);
    }

    // 稲妻の形を作り直す：光の稲妻 2 本は柄から頭へ、2 本は頭のまわりを巻く。黒い稲妻も同じように
    void ReshapeCharge(Charge c)
    {
        float r = 0.12f * c.tip;
        for (int b = 0; b < c.bolts.Length; b++) ShapeHammerBolt(c, c.bolts[b], b % 2 == 0, r, Random.Range(0.07f, 0.13f) * (b == 0 ? 1.3f : 1f));
        for (int b = 0; b < c.darkBolts.Length; b++) ShapeHammerBolt(c, c.darkBolts[b], b % 2 == 1, r * 1.3f, Random.Range(0.08f, 0.15f));
    }

    void ShapeHammerBolt(Charge c, LineRenderer line, bool shaft, float r, float width)
    {
        int n = line.positionCount;
        float phase = Random.Range(0f, Mathf.PI * 2f);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1);
            Vector3 p;
            if (shaft)
            {
                // 柄に沿って、根元から先へ（ギザギザ）
                float env = Mathf.Sin(t * Mathf.PI) * 0.7f + 0.3f;
                p = HammerPoint(c, Mathf.Lerp(0.15f, 1.02f, t), new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * r * env);
            }
            else
            {
                // 頭のまわりを巻きつく
                float a = phase + t * Mathf.PI * 2.2f;
                // 頭（ハンマーの先の方）より一回り大きく巻いて、頭に隠れないようにする
                float rad = r * Random.Range(2.6f, 3.8f);
                p = HammerPoint(c, Mathf.Lerp(0.62f, 1.08f, t) + Random.Range(-0.06f, 0.06f), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad);
            }
            line.SetPosition(i, p);
        }
        line.widthMultiplier = width;
    }

    void SpawnHammerFork(Charge c, bool dark)
    {
        Fork f = null;
        foreach (var x in c.forks) if (x.line != null && !x.line.enabled) { f = x; break; }
        if (f == null)
        {
            if (c.forks.Count >= 10) return;
            f = new Fork { line = MakeLine("PlasmaHammerFork", c.root, m_boltMat, kForkPoints) };
            f.line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f));
            c.forks.Add(f);
        }
        f.dark = dark;
        f.line.sharedMaterial = dark ? m_darkBoltMat : m_boltMat;
        f.line.enabled = true;
        f.age = 0f;
        f.life = Random.Range(0.06f, 0.14f);
        var start = HammerPoint(c, Random.Range(0.6f, 1.0f), Random.insideUnitCircle * 0.15f * c.tip);
        var dir = (new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-0.2f, 0.8f))).normalized;
        float length = Random.Range(0.45f, 1.0f) * c.tip;
        var side = Vector3.Cross(dir, Vector3.forward);
        if (side.sqrMagnitude < 0.01f) side = Vector3.right;
        side.Normalize();
        for (int i = 0; i < kForkPoints; i++)
        {
            float t = (float)i / (kForkPoints - 1);
            var p = start + dir * (length * t);
            if (i > 0) p += side * Random.Range(-1f, 1f) * 0.2f * length;
            f.line.SetPosition(i, p);
        }
        f.line.widthMultiplier = Random.Range(0.04f, 0.08f) * (dark ? 1.6f : 1f);
    }

    void EmitHammerSpark(Charge c, float speed)
    {
        var at = c.root.TransformPoint(HammerPoint(c, Random.Range(0.55f, 1.05f), Random.insideUnitCircle * 0.2f * c.tip));
        var vel = Random.insideUnitSphere * Random.Range(2f, 6f) * speed + Vector3.up * Random.Range(0.5f, 2.5f);
        if (Random.value < 0.35f * Mathf.Clamp01(MatchVfxSettings.Current.aodaruDark) / 0.8f)
            VfxKit.Emit(m_darkSparks, at, vel, Random.Range(0.05f, 0.1f), Random.Range(0.12f, 0.3f), new Color(0.03f, 0.02f, 0.05f, 0.9f));
        else
            VfxKit.Emit(m_sparks, at, vel, Random.Range(0.035f, 0.08f), Random.Range(0.12f, 0.35f), Random.value < 0.5f ? c.core : c.color);
    }

    void DropCharge(Charge c)
    {
        if (c.root != null) Destroy(c.root.gameObject);
        c.root = null;
        c.hammer = null;
        c.forks.Clear();
    }

    // ハンマーの見た目で、支点から一番遠い所までの長さ（ローカル）。見た目が無ければ 1.4
    static float HammerTip(Nakahira.DarumaHammer h)
    {
        float tip = 0f;
        var pivot = h.transform;
        foreach (var mf in h.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || !mf.gameObject.activeInHierarchy || mf.name.StartsWith("Plasma")) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                tip = Mathf.Max(tip, pivot.InverseTransformPoint(mf.transform.TransformPoint(corner)).z);
            }
        }
        return tip > 0.3f ? tip : 1.4f;
    }

    void Register(Yurube.AodaruGaleSlash src, MatchVfxSettings s)
    {
        Ensure(s);
        var sl = new Slash
        {
            src = src,
            tr = src.transform,
            width = Get(F_width, src, 2.4f),
            height = Get(F_height, src, 0.6f),
            depth = Get(F_depth, src, 0.4f),
        };
        var owner = F_owner != null ? F_owner.GetValue(src) as Component : null;
        var player = MatchVfx.PlayerOf(owner);
        var c = player != null ? MatchVfx.ColorOf(player) : new Color(0.45f, 0.85f, 1f);
        // 光は足し算で明るくなるので、色はやや濃いめにしておく（白く飛ばずにプレイヤーの色が残るように）
        sl.color = c;
        sl.core = Color.Lerp(c, Color.white, 0.55f);

        // もとの水色の三日月を隠す（当たり判定には関係ない見た目だけ）
        if (s.aodaruHideOriginal)
            foreach (var lr in src.GetComponentsInChildren<LineRenderer>(true)) lr.enabled = false;

        sl.aura = MakeLine("PlasmaAura", sl.tr, m_auraMat, kGlowPoints);
        sl.glow = MakeLine("PlasmaGlow", sl.tr, m_glowMat, kGlowPoints);
        sl.coreLine = MakeLine("PlasmaCore", sl.tr, m_coreMat, kGlowPoints);
        var taper = new AnimationCurve(new Keyframe(0f, 0.08f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.08f));
        sl.aura.widthCurve = new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.25f));
        sl.glow.widthCurve = taper;
        sl.coreLine.widthCurve = taper;
        for (int i = 0; i < kGlowPoints; i++)
        {
            float u = (float)i / (kGlowPoints - 1) * 2f - 1f;
            var p = Crescent(sl, u);
            sl.aura.SetPosition(i, p);
            sl.glow.SetPosition(i, p);
            sl.coreLine.SetPosition(i, p);
        }

        int bolts = Mathf.Clamp(s.aodaruBolts, 1, 8);
        sl.bolts = new LineRenderer[bolts];
        for (int i = 0; i < bolts; i++)
        {
            sl.bolts[i] = MakeLine("PlasmaBolt", sl.tr, m_boltMat, kBoltPoints);
            sl.bolts[i].widthCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.3f));
        }
        int darkBolts = bolts;   // 光の稲妻と同じ本数だけ、黒い稲妻をからませる
        sl.darkBolts = new LineRenderer[darkBolts];
        for (int i = 0; i < darkBolts; i++)
        {
            sl.darkBolts[i] = MakeLine("PlasmaDarkBolt", sl.tr, m_darkBoltMat, kBoltPoints);
            sl.darkBolts[i].widthCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.3f));
        }


        sl.lastPos = sl.tr.position;
        sl.lastDir = sl.tr.forward;
        m_slashes.Add(sl);
        Reshape(sl, s);
        // 出た瞬間：放電の火花
        Burst(sl, sl.tr.position, sl.tr.forward, 0.5f);
    }

    void Step(Slash sl, MatchVfxSettings s)
    {
        float dt = Dt;
        sl.age += dt;
        sl.lastPos = sl.tr.position;
        sl.lastDir = sl.tr.forward;

        float fade = Get(F_fade, sl.src, -1f);
        float alpha = fade < 0f ? 1f : 1f - Mathf.Clamp01(fade / kFadeTime);
        float intro = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sl.age / 0.07f));
        float hum = 0.85f + 0.15f * Mathf.Sin(sl.age * 90f) + Random.Range(-0.08f, 0.08f);   // 細かく脈打つ

        // 光の帯と芯
        float gw = (sl.depth + 0.9f) * Mathf.Max(0.1f, s.aodaruGlowWidth);
        // 黒の濃さ。黒い稲妻が見えるよう、光のオーラ・にじみは黒が濃いほど控えめにする
        float dark = Mathf.Clamp01(s.aodaruDark);
        sl.aura.widthMultiplier = gw * Mathf.Lerp(2.6f, 1.2f, dark) * intro * (0.9f + 0.1f * hum);
        SetColor(sl.aura, sl.color, Mathf.Lerp(0.35f, 0.12f, dark) * alpha);
        sl.glow.widthMultiplier = gw * Mathf.Lerp(1.3f, 1.0f, dark) * intro * hum;
        SetColor(sl.glow, sl.color, Mathf.Lerp(0.8f, 0.65f, dark) * alpha);
        sl.coreLine.widthMultiplier = (0.14f + sl.depth * 0.3f) * intro * hum;
        SetColor(sl.coreLine, sl.core, alpha);

        // 稲妻：短い間隔で形を変える
        sl.flicker -= dt;
        if (sl.flicker <= 0f) { sl.flicker = Mathf.Max(0.01f, s.aodaruFlicker) * Random.Range(0.7f, 1.3f); Reshape(sl, s); }
        // 稲妻は1本だけ白っぽい芯の色、ほかはプレイヤーの色（明るめ）
        for (int i = 0; i < sl.bolts.Length; i++)
            SetColor(sl.bolts[i], i == 0 ? sl.core : Color.Lerp(sl.color, Color.white, 0.25f), alpha * intro);
        for (int i = 0; i < sl.darkBolts.Length; i++)
            SetColor(sl.darkBolts[i], new Color(0.03f, 0.02f, 0.05f), 0.9f * dark * alpha * intro);

        // 枝分かれの稲妻
        sl.forkAcc += dt * s.aodaruForkRate * alpha;
        while (sl.forkAcc >= 1f) { sl.forkAcc -= 1f; SpawnFork(sl, Random.value < 0.4f * Mathf.Clamp01(s.aodaruDark) / 0.8f); }
        for (int i = sl.forks.Count - 1; i >= 0; i--)
        {
            var f = sl.forks[i];
            if (f.line == null) { sl.forks.RemoveAt(i); continue; }
            f.age += dt;
            if (f.age >= f.life) { f.line.enabled = false; continue; }
            float k = 1f - f.age / f.life;
            if (f.dark) SetColor(f.line, new Color(0.03f, 0.02f, 0.05f), 0.9f * Mathf.Clamp01(s.aodaruDark) * k * alpha);
            else SetColor(f.line, Random.value < 0.5f ? sl.core : sl.color, k * alpha);
        }


        // 火花：三日月から、後ろと横へ散る
        float sdt = Time.deltaTime;
        sl.sparkAcc += sdt * s.aodaruSparkRate * alpha;
        while (sl.sparkAcc >= 1f)
        {
            sl.sparkAcc -= 1f;
            float u = Random.Range(-1f, 1f);
            Vector3 at = sl.tr.TransformPoint(Crescent(sl, u));
            Vector3 vel = -sl.tr.forward * Random.Range(1f, 5f) + Random.insideUnitSphere * Random.Range(2f, 7f) + Vector3.up * Random.Range(0.5f, 3f);
            // 3割くらいは黒い火花（黒い放電のかけら）
            if (Random.value < 0.35f * Mathf.Clamp01(s.aodaruDark) / 0.8f)
                VfxKit.Emit(m_darkSparks, at, vel, Random.Range(0.06f, 0.12f), Random.Range(0.15f, 0.35f), new Color(0.03f, 0.02f, 0.05f, 0.9f));
            else
                VfxKit.Emit(m_sparks, at, vel, Random.Range(0.04f, 0.09f), Random.Range(0.15f, 0.4f), Random.value < 0.5f ? sl.core : sl.color);
            if (Random.value < 0.4f)
                VfxKit.Emit(m_crackle, at + Random.insideUnitSphere * 0.25f, Vector3.zero, Random.Range(0.12f, 0.3f), Random.Range(0.04f, 0.09f), sl.core);
        }
        // 残光：通ったあとに、プレイヤーの色の光の残像
        sl.trailAcc += sdt * 90f * alpha;
        while (sl.trailAcc >= 1f)
        {
            sl.trailAcc -= 1f;
            float u = Random.Range(-0.95f, 0.95f);
            Vector3 at = sl.tr.TransformPoint(Crescent(sl, u));
            var col = sl.color; col.a = Mathf.Lerp(0.45f, 0.25f, dark);   // 黒い稲妻を消さないよう、残光は控えめに
            VfxKit.Emit(m_trail, at, Vector3.zero, Random.Range(0.3f, 0.5f) * (1f - 0.5f * Mathf.Abs(u)), Random.Range(0.1f, 0.18f), col);
        }
    }

    // 三日月の上の点（斬撃のローカル。z が前。u は -1〜1、両端は細く）
    static Vector3 Crescent(Slash sl, float u)
    {
        return new Vector3(u * sl.width * 0.5f, sl.height, (1f - u * u) * sl.depth);
    }

    // 稲妻の形を作り直す（三日月に沿って、ギザギザに）
    void Reshape(Slash sl, MatchVfxSettings s)
    {
        for (int b = 0; b < sl.bolts.Length; b++)
        {
            var line = sl.bolts[b];
            float spread = 0.12f + 0.1f * b;
            float lag = -0.15f * (b % 3);   // 少し後ろにずらした稲妻も混ぜる
            for (int i = 0; i < kBoltPoints; i++)
            {
                float t = (float)i / (kBoltPoints - 1);
                float u = Mathf.Lerp(-1.05f, 1.05f, t);
                float env = Mathf.Sin(t * Mathf.PI);   // 両端は三日月の先にくっつく
                var p = Crescent(sl, Mathf.Clamp(u, -1f, 1f));
                p += new Vector3(Random.Range(-1f, 1f) * spread * 0.5f, Random.Range(-1f, 1f) * spread, Random.Range(-1f, 1f) * spread + lag) * env;
                line.SetPosition(i, p);
            }
            line.widthMultiplier = Random.Range(0.05f, 0.12f) * (b == 0 ? 1.4f : 1f);
        }
        // 黒い稲妻：光の稲妻とは別の形で、少し太め（黒い放電が光の稲妻にからみつく）
        for (int b = 0; b < sl.darkBolts.Length; b++)
        {
            var line = sl.darkBolts[b];
            // 光の外側（前と後ろ）にはみ出すように、大きめに揺らす
            float spread = 0.3f + 0.15f * b;
            float side = b % 2 == 0 ? 1f : -1f;
            for (int i = 0; i < kBoltPoints; i++)
            {
                float t = (float)i / (kBoltPoints - 1);
                float env = Mathf.Sin(t * Mathf.PI);
                var p = Crescent(sl, Mathf.Lerp(-1.1f, 1.1f, t));
                p += new Vector3(Random.Range(-1f, 1f) * spread * 0.4f, Random.Range(-1f, 1f) * spread * 0.5f, Random.Range(-0.5f, 1f) * spread * side) * env;
                line.SetPosition(i, p);
            }
            line.widthMultiplier = Random.Range(0.1f, 0.2f);
        }
    }

    // dark：黒い枝分かれにする
    void SpawnFork(Slash sl, bool dark)
    {
        Fork f = null;
        foreach (var x in sl.forks) if (x.line != null && !x.line.enabled) { f = x; break; }
        if (f == null)
        {
            if (sl.forks.Count >= 14) return;
            f = new Fork { line = MakeLine("PlasmaFork", sl.tr, m_boltMat, kForkPoints) };
            f.line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f));
            sl.forks.Add(f);
        }
        f.dark = dark;
        f.line.sharedMaterial = dark ? m_darkBoltMat : m_boltMat;
        f.line.enabled = true;
        f.age = 0f;
        f.life = Random.Range(0.06f, 0.14f);
        f.u = Random.Range(-0.95f, 0.95f);
        // 三日月の外側（前と、先の方は横）へ飛び出す
        var outward = new Vector3(f.u * 0.9f, Random.Range(-0.2f, 0.6f), 1f - Mathf.Abs(f.u) * 0.5f).normalized;
        f.dir = (outward + Random.insideUnitSphere * 0.5f).normalized;
        f.length = Random.Range(0.5f, 1.5f) * Mathf.Max(0.6f, sl.width / 2.4f);
        var start = Crescent(sl, f.u);
        var side = Vector3.Cross(f.dir, Vector3.up).normalized;
        for (int i = 0; i < kForkPoints; i++)
        {
            float t = (float)i / (kForkPoints - 1);
            var p = start + f.dir * (f.length * t);
            if (i > 0) p += (side * Random.Range(-1f, 1f) + Vector3.up * Random.Range(-0.6f, 0.6f)) * 0.18f * f.length;
            f.line.SetPosition(i, p);
        }
        f.line.widthMultiplier = Random.Range(0.035f, 0.075f) * (dark ? 1.6f : 1f);
    }

    // バチッとはじける（出た瞬間・消えた瞬間）
    void Burst(Slash sl, Vector3 pos, Vector3 dir, float strength)
    {
        if (m_sparks == null) return;
        int n = Mathf.RoundToInt(45 * strength);
        for (int i = 0; i < n; i++)
        {
            float u = Random.Range(-1f, 1f);
            Vector3 at = pos + Quaternion.LookRotation(dir) * Crescent(sl, u);
            Vector3 vel = Random.insideUnitSphere * Random.Range(4f, 11f) + Vector3.up * Random.Range(1f, 4f) + dir * Random.Range(0f, 4f);
            if (i % 3 == 0)   // 黒い火花も混ぜる
                VfxKit.Emit(m_darkSparks, at, vel, Random.Range(0.07f, 0.13f), Random.Range(0.2f, 0.4f), new Color(0.03f, 0.02f, 0.05f, 0.9f));
            else
                VfxKit.Emit(m_sparks, at, vel, Random.Range(0.05f, 0.11f), Random.Range(0.2f, 0.5f), Random.value < 0.5f ? sl.core : sl.color);
        }
        for (int i = 0; i < n / 2; i++)
            VfxKit.Emit(m_crackle, pos + Vector3.up * sl.height + Random.insideUnitSphere * sl.width * 0.45f, Vector3.zero,
                Random.Range(0.2f, 0.45f), Random.Range(0.05f, 0.12f), sl.core);
    }

    // ---------------- 部品 ----------------
    void Ensure(MatchVfxSettings s)
    {
        float k = Mathf.Max(0.5f, s.aodaruIntensity);
        // 明るくしすぎると白く飛んでプレイヤーの色が消えるので、にじみ・オーラは控えめ、芯と稲妻だけ明るく
        // 黒い稲妻はふつうの半透明。光より先に描いて、光が黒の上に乗るようにする
        if (m_darkBoltMat == null) { m_darkBoltMat = VfxKit.Mat(VfxKit.Beam); m_darkBoltMat.renderQueue = 2995; }
        if (m_darkSparks == null)
        {
            m_darkSparks = VfxKit.CreateSystem("PlasmaDarkSparks", transform, false, VfxKit.SoftDot, 800, 0.3f, 0.6f, false, 0.035f);
            m_darkSparks.GetComponent<ParticleSystemRenderer>().sharedMaterial.renderQueue = 2995;
        }
        if (m_auraMat == null) m_auraMat = VfxKit.AddMat(VfxKit.Beam, k * 0.3f);
        if (m_glowMat == null) m_glowMat = VfxKit.AddMat(VfxKit.Beam, k * 0.45f);
        if (m_coreMat == null) m_coreMat = VfxKit.AddMat(VfxKit.Beam, k * 0.8f);
        if (m_boltMat == null) m_boltMat = VfxKit.AddMat(VfxKit.Beam, k * 0.9f);
        if (m_sparks == null) m_sparks = VfxKit.CreateSystem("PlasmaSparks", transform, true, VfxKit.SoftDot, 1500, 0.3f, 0.6f, false, 0.035f, k);
        if (m_crackle == null) m_crackle = VfxKit.CreateSystem("PlasmaCrackle", transform, true, VfxKit.SoftDot, 600, 0.6f, 0f, false, 0f, k * 1.3f);
        if (m_trail == null) m_trail = VfxKit.CreateSystem("PlasmaTrail", transform, true, VfxKit.SoftDot, 1500, 0.6f, 0f, false, 0f, k * 0.6f);
    }

    static LineRenderer MakeLine(string name, Transform parent, Material mat, int points)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat;
        lr.useWorldSpace = false;
        lr.positionCount = points;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 1;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    static void SetColor(LineRenderer lr, Color c, float a)
    {
        c.a = Mathf.Clamp01(a);
        lr.startColor = c;
        lr.endColor = c;
    }

    static float Get(FieldInfo f, object o, float fallback)
    {
        if (f == null || o == null) return fallback;
        var v = f.GetValue(o);
        return v is float x ? x : fallback;
    }

    void ClearAll()
    {
        foreach (var sl in m_slashes)
        {
            if (sl.darkBolts != null) foreach (var b in sl.darkBolts) if (b != null) Destroy(b.gameObject);
            if (sl.aura != null) Destroy(sl.aura.gameObject);
            if (sl.glow != null) Destroy(sl.glow.gameObject);
            if (sl.coreLine != null) Destroy(sl.coreLine.gameObject);
            if (sl.bolts != null) foreach (var b in sl.bolts) if (b != null) Destroy(b.gameObject);
            foreach (var f in sl.forks) if (f.line != null) Destroy(f.line.gameObject);
        }
        m_slashes.Clear();
        m_known.Clear();
        foreach (var c in m_charges.Values) DropCharge(c);
        m_charges.Clear();
    }
}
