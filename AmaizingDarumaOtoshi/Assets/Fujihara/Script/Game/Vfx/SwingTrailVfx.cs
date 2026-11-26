using System.Collections.Generic;
using Nakahira;
using UnityEngine;

// 3. ハンマーの軌跡：ハンマーを振っている間（薙ぎ払い・股抜きショット）、ハンマーの通った帯（柄の途中〜頭の先）に、
//    プレイヤーの色の弧を残す。先ほど濃く、古いほど薄くなって消える。幽霊のハンマーも同じ（少し薄く）
//    中平さんの細い白い線（HammerTrail）はそのまま。その外側に色の帯を重ねる
public class SwingTrailVfx : MonoBehaviour
{
    class Ribbon
    {
        public DarumaHammer hammer;
        public MeshFilter filter;
        public MeshRenderer renderer;
        public Mesh mesh;
        public float tip = -1f;    // ハンマーの先（支点から見た +Z の長さ）
        public readonly List<Sample> samples = new List<Sample>();
        public Vector3 lastPos;
        public Quaternion lastRot;
        public bool wasBusy;
    }
    struct Sample { public Vector3 inner, outer; public float time; }

    readonly Dictionary<DarumaHammer, Ribbon> m_ribbons = new Dictionary<DarumaHammer, Ribbon>();
    readonly List<DarumaHammer> m_dead = new List<DarumaHammer>();
    readonly List<Vector3> m_v = new List<Vector3>();
    readonly List<Color> m_c = new List<Color>();
    readonly List<int> m_t = new List<int>();
    Material m_material;
    float m_scan;

    void LateUpdate()
    {
        var s = MatchVfxSettings.Current;
        var match = DarumaMatch.Current;
        if (!s.swingTrail || match == null) return;
        if (m_material == null) m_material = VfxKit.Mat(null);

        // ハンマーを探す（体が作り直されるので、ときどき探し直す）
        if ((m_scan -= Time.unscaledDeltaTime) <= 0f)
        {
            m_scan = 0.5f;
            foreach (var h in FindObjectsByType<DarumaHammer>())
                if (h != null && !m_ribbons.ContainsKey(h)) m_ribbons[h] = MakeRibbon(h);
        }

        m_dead.Clear();
        foreach (var kv in m_ribbons)
        {
            if (kv.Key == null) { m_dead.Add(kv.Key); continue; }
            Step(kv.Value, s);
        }
        foreach (var k in m_dead)
        {
            var r = m_ribbons[k];
            if (r.filter != null) Destroy(r.filter.gameObject);
            if (r.mesh != null) Destroy(r.mesh);
            m_ribbons.Remove(k);
        }
    }

    Ribbon MakeRibbon(DarumaHammer h)
    {
        var go = new GameObject("SwingTrail");
        go.transform.SetParent(transform, false);
        var r = new Ribbon { hammer = h, filter = go.AddComponent<MeshFilter>(), renderer = go.AddComponent<MeshRenderer>() };
        r.mesh = new Mesh { name = "SwingTrail" };
        r.mesh.MarkDynamic();
        r.filter.sharedMesh = r.mesh;
        r.renderer.sharedMaterial = m_material;
        r.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.renderer.receiveShadows = false;
        return r;
    }

    // ハンマーの見た目（支点の子のメッシュ）で一番遠い所までの長さ。見た目が無ければ 1.4
    static float TipOf(DarumaHammer h)
    {
        float tip = 0f;
        var pivot = h.transform;
        foreach (var mf in h.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || !mf.gameObject.activeInHierarchy) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var local = pivot.InverseTransformPoint(mf.transform.TransformPoint(corner));
                tip = Mathf.Max(tip, local.z);
            }
        }
        return tip > 0.3f ? tip : 1.4f;
    }

    void Step(Ribbon r, MatchVfxSettings s)
    {
        var h = r.hammer;
        bool busy = h.IsBusy && h.gameObject.activeInHierarchy;
        float now = Time.time;
        var pivot = h.transform;
        if (busy)
        {
            // 見た目のハンマーは試合の途中でモデルに差し替わるので、振り始めるたびに長さを測り直す
            if (!r.wasBusy || r.tip < 0f) { r.tip = TipOf(h); r.samples.Clear(); r.lastPos = pivot.position; r.lastRot = pivot.rotation; }
            // 1フレームで大きく回ったときは、間を回転でつないでなめらかな弧にする
            float angle = Quaternion.Angle(r.lastRot, pivot.rotation);
            int steps = Mathf.Clamp(Mathf.CeilToInt(angle / 6f), 1, 8);
            for (int i = 1; i <= steps; i++)
            {
                float u = i / (float)steps;
                var rot = Quaternion.Slerp(r.lastRot, pivot.rotation, u);
                var pos = Vector3.Lerp(r.lastPos, pivot.position, u);
                Vector3 axis = rot * Vector3.forward;
                float scale = pivot.lossyScale.z;
                r.samples.Add(new Sample
                {
                    inner = pos + axis * r.tip * s.trailInner * scale,
                    outer = pos + axis * r.tip * scale,
                    time = Mathf.Lerp(now - Time.deltaTime, now, u),
                });
            }
            r.lastPos = pivot.position;
            r.lastRot = pivot.rotation;
        }
        r.wasBusy = busy;

        // 古いものを消す
        float life = Mathf.Max(0.02f, s.trailLife);
        int drop = 0;
        while (drop < r.samples.Count && now - r.samples[drop].time > life) drop++;
        if (drop > 0) r.samples.RemoveRange(0, drop);
        Build(r, s, life, now);
    }

    void Build(Ribbon r, MatchVfxSettings s, float life, float now)
    {
        r.mesh.Clear();
        if (r.samples.Count < 2) return;
        var player = MatchVfx.PlayerOf(r.hammer.GetComponentInParent<DarumaCharacter>() as Component ?? r.hammer.GetComponentInParent<DarumaGhost>());
        bool ghost = r.hammer.GetComponentInParent<DarumaGhost>() != null;
        Color col = MatchVfx.ColorOf(player);
        float alpha = s.trailAlpha * (ghost ? 0.5f : 1f);
        m_v.Clear(); m_c.Clear(); m_t.Clear();
        for (int i = 0; i < r.samples.Count; i++)
        {
            var smp = r.samples[i];
            float age = Mathf.Clamp01((now - smp.time) / life);
            float a = alpha * (1f - age) * (1f - age);
            m_v.Add(smp.inner);
            m_v.Add(smp.outer);
            // 柄の側は透明、先は色＋少し白く
            m_c.Add(new Color(col.r, col.g, col.b, 0f));
            m_c.Add(new Color(Mathf.Lerp(col.r, 1f, 0.35f), Mathf.Lerp(col.g, 1f, 0.35f), Mathf.Lerp(col.b, 1f, 0.35f), a));
            if (i == 0) continue;
            int b = i * 2;
            m_t.Add(b - 2); m_t.Add(b - 1); m_t.Add(b);
            m_t.Add(b - 1); m_t.Add(b + 1); m_t.Add(b);
            // 裏からも見えるように反対向きも
            m_t.Add(b); m_t.Add(b - 1); m_t.Add(b - 2);
            m_t.Add(b); m_t.Add(b + 1); m_t.Add(b - 1);
        }
        r.mesh.SetVertices(m_v);
        r.mesh.SetColors(m_c);
        r.mesh.SetTriangles(m_t, 0);
        r.mesh.RecalculateBounds();
    }
}
