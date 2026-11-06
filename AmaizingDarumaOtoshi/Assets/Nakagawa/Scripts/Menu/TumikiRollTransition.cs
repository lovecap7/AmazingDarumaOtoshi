using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// シーン遷移の演出：ゲームの積み木（平たい円柱）を寝かせて、大量に転がして画面を埋め尽くし、
// 埋まっている間にシーンを切り替えて、そのまま転がり抜けていく（フェードはしない）。
// ・転がす向きは routes で決める（タイトル→メニュー、メニュー→セレクトは左から右、逆は右から左）
// ・routes に無い組み合わせ（タイトルへ戻る、ゲームへの出入りなど）は演出なし（Fader の黒いフェード）
// ・円柱は世界のはるか下（StageOrigin）に置いた専用のカメラで描き、Fader のキャンバス（いちばん手前）に貼る。
//   色は頂点カラーに陰影を焼き込んだ Unlit なので、シーンのライトが変わっても見た目は変わらない
// 起動時に Fader.Transition へ自動で入る（Enabled = false にすると黒いフェードだけになる）。
public class TumikiRollTransition : ISceneTransition
{
    public static bool Enabled = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        // 再生のたびに新しく作る（ドメインリロード無しでも前の再生の物を持ち越さない）
        if (Enabled && (Fader.Transition == null || Fader.Transition is TumikiRollTransition)) Fader.Transition = new TumikiRollTransition();
    }

    // 転がす組み合わせ。dir：1 で左から右、-1 で右から左
    public struct Route
    {
        public string from, to;
        public int dir;
        public Route(string from, string to, int dir) { this.from = from; this.to = to; this.dir = dir; }
    }

    public readonly List<Route> routes = new List<Route>
    {
        new Route("TitleScene", "MenuScene", 1),
        new Route("TitleClubRoomScene", "MenuScene", 1),
        new Route("MenuScene", "SelectScene", 1),
        new Route("MenuScene", "NetConnectScene", 1),
        new Route("SelectScene", "MenuScene", -1),
        new Route("NetConnectScene", "MenuScene", -1),
    };

    // ---- 調整できる値（大きさは円柱の直径 = 1 とした単位） ----
    public float diameterPerScreen = 0.7f;   // 円柱の直径（画面の縦幅に対する割合）
    public float thickness = 0.5f;           // 厚み（直径に対する割合。ゲームの積み木と同じ 0.5）
    public float yaw = 25f;                  // 横の厚みが見えるように回す角度
    public float spacingX = 0.5f;            // 横の間隔（直径に対する割合）
    public float spacingY = 0.4f;            // 段の間隔
    public float jitter = 0.07f;             // 並びのばらつき
    public float speed = 3f;                 // 転がる速さ（画面の縦幅 / 秒）

    static readonly Vector3 StageOrigin = new Vector3(0f, -20000f, 0f);

    RectTransform root;
    RawImage image;
    GameObject stage;
    Camera cam;
    RenderTexture rt;
    Runner runner;
    Mesh[] bodyMeshes, patternMeshes;
    Material material;
    int dir = 1, pendingDir = 1;

    class Disc
    {
        public Transform root, pattern;
        public float ox, oy;   // 先頭からの位置（進む向きで）、高さ
    }
    readonly List<Disc> discs = new List<Disc>();

    enum State { Idle, Covering, Holding, Revealing }
    State state;
    float front, coverEnd, bandLength, halfW, halfH;

    public void Build(RectTransform root)
    {
        this.root = root;
        discs.Clear();
        bodyMeshes = patternMeshes = null;
        rt = null;

        var img = new GameObject("TumikiRoll", typeof(RectTransform));
        img.transform.SetParent(root, false);
        var r = (RectTransform)img.transform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        image = img.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.enabled = false;

        stage = new GameObject("TumikiRollStage");
        Object.DontDestroyOnLoad(stage);
        stage.transform.position = StageOrigin;
        runner = stage.AddComponent<Runner>();
        runner.owner = this;

        var camGo = new GameObject("TumikiRollCamera");
        camGo.transform.SetParent(stage.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 0f, -10f);
        cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 300f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.allowHDR = false;
        cam.allowMSAA = true;
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.antialiasing = AntialiasingMode.None;

        material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        material.SetFloat("_Cull", 0f);   // 両面
        stage.SetActive(false);
    }

    public bool Plays(string fromScene, string toScene)
    {
        if (stage == null) return false;
        foreach (var r in routes)
            if (r.from == fromScene && r.to == toScene) { pendingDir = r.dir >= 0 ? 1 : -1; return true; }
        return false;
    }

    public IEnumerator Cover()
    {
        dir = pendingDir;   // 向きは転がし始めるときに決める（転がっている途中で Plays を聞かれても変わらない）
        Layout();
        stage.SetActive(true);
        image.enabled = true;
        state = State.Covering;
        Tick(0f);
        while (state == State.Covering) yield return null;
    }

    public IEnumerator Reveal()
    {
        state = State.Revealing;
        while (state == State.Revealing) yield return null;
        image.enabled = false;
        if (stage != null) stage.SetActive(false);
    }

    // ---------------- 並べる ----------------
    void Layout()
    {
        int w = Mathf.Max(16, Screen.width), h = Mathf.Max(16, Screen.height);
        if (rt == null || rt.width != w || rt.height != h)
        {
            if (rt != null) rt.Release();
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "TumikiRoll" };
            cam.targetTexture = rt;
            image.texture = rt;
        }
        float aspect = (float)w / h;
        halfH = 0.5f / Mathf.Max(0.05f, diameterPerScreen);   // 直径 1 が画面の縦幅の diameterPerScreen 倍
        halfW = halfH * aspect;
        cam.orthographicSize = halfH;
        if (bodyMeshes == null) BuildMeshes();

        // 段（下から上へ）× 列（先頭から後ろへ）。隣の段は半分ずらす
        int rows = Mathf.CeilToInt(halfH * 2f / spacingY) + 2;
        int cols = Mathf.CeilToInt((halfW * 2f + 3f) / spacingX) + 1;
        bandLength = cols * spacingX;
        int need = rows * cols;
        while (discs.Count < need) discs.Add(MakeDisc(discs.Count));

        // 重なり順（奥行き）はばらばらに
        var order = new List<int>();
        for (int i = 0; i < need; i++) order.Add(i);
        for (int i = need - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }

        for (int i = 0; i < discs.Count; i++)
        {
            var d = discs[i];
            bool used = i < need;
            d.root.gameObject.SetActive(used);
            if (!used) continue;
            int row = i / cols, col = i % cols;
            d.ox = -(col * spacingX + (row % 2) * spacingX * 0.5f) + Random.Range(-jitter, jitter);
            d.oy = -halfH - spacingY * 0.5f + row * spacingY + Random.Range(-jitter, jitter);
            d.root.localPosition = new Vector3(0f, d.oy, order[i] * 1f);
            d.root.localRotation = Quaternion.Euler(0f, yaw * dir, 0f);
            int c = Random.Range(0, bodyMeshes.Length);
            d.root.GetComponent<MeshFilter>().sharedMesh = bodyMeshes[c];
            d.pattern.GetComponent<MeshFilter>().sharedMesh = patternMeshes[c];
            d.pattern.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        }
        cam.farClipPlane = need + 20f;

        // 先頭は画面の外（入ってくる側）から。先頭が反対側の外まで来たら埋まっている
        front = -halfW - 0.8f;
        coverEnd = halfW + 0.95f;
    }

    Disc MakeDisc(int i)
    {
        var d = new Disc();
        var go = new GameObject("Tumiki" + i);
        go.transform.SetParent(stage.transform, false);
        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        d.root = go.transform;

        var p = new GameObject("Pattern");
        p.transform.SetParent(go.transform, false);
        p.AddComponent<MeshFilter>();
        var pr = p.AddComponent<MeshRenderer>();
        pr.sharedMaterial = material;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        d.pattern = p.transform;
        return d;
    }

    // ---------------- 毎フレーム ----------------
    void Tick(float dt)
    {
        if (state == State.Idle) return;
        float v = speed * halfH * 2f;
        front += v * dt;

        bool allGone = true;
        foreach (var d in discs)
        {
            if (!d.root.gameObject.activeSelf) continue;
            float x = front + d.ox;
            // 埋まっている間（読み込み中）は、抜けた円柱を後ろへ回して、いつまでも埋めておく
            if (state == State.Holding && x > halfW + 1.2f) { d.ox -= bandLength; x -= bandLength; }
            if (x - 0.7f <= halfW) allGone = false;
            var p = d.root.localPosition;
            d.root.localPosition = new Vector3(x * dir, p.y, p.z);
            // 転がる：進んだ距離 ÷ 半径 だけ回る（右へ進むときは時計回り）
            d.pattern.localRotation = Quaternion.Euler(0f, 0f, -x * 2f * Mathf.Rad2Deg * dir);
        }

        if (state == State.Covering && front >= coverEnd) state = State.Holding;
        else if (state == State.Revealing && allGone) state = State.Idle;
    }

    class Runner : MonoBehaviour
    {
        public TumikiRollTransition owner;
        void Update() { if (owner != null) owner.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.05f)); }
    }

    // ---------------- 円柱のメッシュ ----------------
    // 直径 1、厚み thickness、角を少し丸めた円柱。軸は z（手前の面が -z）。陰影を頂点カラーに焼き込む
    static readonly Vector3 LightDir = new Vector3(-0.35f, 0.75f, -0.55f).normalized;

    void BuildMeshes()
    {
        var palette = ToyKit.Palette;
        bodyMeshes = new Mesh[palette.Length];
        patternMeshes = new Mesh[palette.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            bodyMeshes[i] = BodyMesh(palette[i]);
            patternMeshes[i] = PatternMesh(palette[i]);
        }
    }

    static Color Shade(Color c, Vector3 n, float extra = 1f)
    {
        float diff = Mathf.Max(0f, Vector3.Dot(n.normalized, LightDir));
        float k = (0.58f + 0.5f * diff) * extra;
        var o = c * k;
        o.a = 1f;
        // 頂点カラーは色空間の変換がかからないので、リニアのプロジェクトでは自分で変換する（白っぽくならないように）
        return QualitySettings.activeColorSpace == ColorSpace.Linear ? o.linear : o;
    }

    Mesh BodyMesh(Color c)
    {
        const int seg = 48;
        float r = 0.5f, t = thickness * 0.5f, bevel = 0.05f;
        var verts = new List<Vector3>();
        var cols = new List<Color>();
        var tris = new List<int>();

        // 輪（半径・z・法線）を順に並べて、隣どうしを帯でつなぐ
        var rings = new List<(float rad, float z, Vector3 n)>();
        for (int k = 0; k <= 3; k++)   // 手前の角の丸み（0°〜90°）
        {
            float a = k / 3f * Mathf.PI * 0.5f;
            rings.Add((r - bevel + bevel * Mathf.Sin(a), -t + bevel - bevel * Mathf.Cos(a), new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a))));
        }
        for (int k = 0; k <= 3; k++)   // 奥の角
        {
            float a = k / 3f * Mathf.PI * 0.5f;
            rings.Add((r - bevel * (1f - Mathf.Cos(a)), t - bevel + bevel * Mathf.Sin(a), new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a))));
        }

        int ringStart = 0;
        for (int ri = 0; ri < rings.Count; ri++)
        {
            var ring = rings[ri];
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                verts.Add(new Vector3(ca * ring.rad, sa * ring.rad, ring.z));
                var n = new Vector3(ca * ring.n.x, sa * ring.n.x, ring.n.z);
                cols.Add(Shade(c, n));
            }
            if (ri > 0)
            {
                int a0 = ringStart - (seg + 1), b0 = ringStart;
                for (int s = 0; s < seg; s++)
                {
                    tris.Add(a0 + s); tris.Add(b0 + s); tris.Add(a0 + s + 1);
                    tris.Add(a0 + s + 1); tris.Add(b0 + s); tris.Add(b0 + s + 1);
                }
            }
            ringStart += seg + 1;
        }

        // 手前と奥のふた（上ほど少し明るい）
        foreach (float side in new[] { -1f, 1f })
        {
            int center = verts.Count;
            verts.Add(new Vector3(0f, 0f, side * t));
            cols.Add(Shade(c, new Vector3(0f, 0f, side)));
            float capR = r - bevel;
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a) * capR, Mathf.Sin(a) * capR, side * t));
                cols.Add(Shade(c, new Vector3(0f, 0f, side), 1f + 0.08f * Mathf.Sin(a)));
            }
            for (int s = 0; s < seg; s++) { tris.Add(center); tris.Add(center + 1 + s); tris.Add(center + 2 + s); }
        }

        var m = new Mesh { name = "TumikiRollBody" };
        m.SetVertices(verts);
        m.SetColors(cols);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    // 手前の面の模様（転がっているのが分かるように）：明るい輪と、輪の内側に3つの丸
    Mesh PatternMesh(Color c)
    {
        const int seg = 48;
        float z = -thickness * 0.5f - 0.004f;
        var light = Shade(Color.Lerp(c, Color.white, 0.4f), Vector3.back);
        var verts = new List<Vector3>();
        var cols = new List<Color>();
        var tris = new List<int>();

        // 輪
        float r0 = 0.3f, r1 = 0.37f;
        for (int s = 0; s <= seg; s++)
        {
            float a = s / (float)seg * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Cos(a) * r0, Mathf.Sin(a) * r0, z));
            verts.Add(new Vector3(Mathf.Cos(a) * r1, Mathf.Sin(a) * r1, z));
            cols.Add(light); cols.Add(light);
            if (s < seg)
            {
                int i = s * 2;
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i + 2); tris.Add(i + 1); tris.Add(i + 3);
            }
        }
        // 3つの丸
        for (int k = 0; k < 3; k++)
        {
            float a = Mathf.PI * 0.5f + k * Mathf.PI * 2f / 3f;
            var center = new Vector3(Mathf.Cos(a) * 0.17f, Mathf.Sin(a) * 0.17f, z);
            int ci = verts.Count;
            verts.Add(center); cols.Add(light);
            const int dseg = 16;
            for (int s = 0; s <= dseg; s++)
            {
                float b = s / (float)dseg * Mathf.PI * 2f;
                verts.Add(center + new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0f) * 0.06f);
                cols.Add(light);
            }
            for (int s = 0; s < dseg; s++) { tris.Add(ci); tris.Add(ci + 1 + s); tris.Add(ci + 2 + s); }
        }

        var m = new Mesh { name = "TumikiRollPattern" };
        m.SetVertices(verts);
        m.SetColors(cols);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }
}
