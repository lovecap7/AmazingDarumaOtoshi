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
    public float diameterPerScreen = 0.35f;  // 円柱の直径（画面の縦幅に対する割合）
    public float thickness = 0.5f;           // 厚み（直径に対する割合。ゲームの積み木と同じ 0.5）
    public float yaw = 25f;                  // 横の厚みが見えるように回す角度
    public float spacingX = 0.5f;            // 横の間隔（直径に対する割合）
    // 転がる速さ（画面の縦幅 / 秒）。入ってくるときと抜けるときは速く、全面が埋まっている間は止める。
    // 埋まっている間（シーンの読み込み・有効化・GC が走る間）に動いていると、その重さがカクつきとして見えるため。
    public float speedFast = 3.0f;
    public float speedSlow = 0f;
    public float speedEase = 0.2f;           // 速さを切り替える時間（秒。easeInCubic）
    public float minHold = 0.15f;            // 埋まって止まってから抜け始めるまでの最短の時間
    // 段の色（上から）。段の数はこの色の数で、画面の縦幅をちょうど等分して並べる
    public Color[] rowColors =
    {
        new Color(0.91f, 0.26f, 0.24f),   // 赤
        new Color(0.97f, 0.56f, 0.18f),   // 橙
        new Color(0.98f, 0.83f, 0.22f),   // 黄
        new Color(0.40f, 0.76f, 0.30f),   // 緑
        new Color(0.26f, 0.62f, 0.90f),   // 青
        new Color(0.25f, 0.32f, 0.70f),   // 藍
        new Color(0.58f, 0.36f, 0.76f),   // 紫
    };

    // ---- 調整用（TumikiRollTransitionTuner から使う） ----
    // 今 Fader に入っている演出（入っていなければ null）
    public static TumikiRollTransition Current => Fader.Transition as TumikiRollTransition;

    // 転がっている途中か
    public bool IsPlaying => state != State.Idle;

    // 厚み・段の色を変えたら呼ぶ。メッシュは次に転がし始めるときに作り直す（転がっている途中は変えない）
    public void MarkMeshesDirty() => meshesDirty = true;

    // 調整できる値を src からまるごと写す（既定値に戻すときは new TumikiRollTransition() を渡す）
    public void CopyTuning(TumikiRollTransition src)
    {
        diameterPerScreen = src.diameterPerScreen;
        thickness = src.thickness;
        yaw = src.yaw;
        spacingX = src.spacingX;
        speedFast = src.speedFast;
        speedSlow = src.speedSlow;
        speedEase = src.speedEase;
        minHold = src.minHold;
        rowColors = (Color[])src.rowColors.Clone();
        MarkMeshesDirty();
    }

    bool meshesDirty;

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
        public int row;        // 段（下から 0）
    }
    float rowDepth;            // 段ごとの奥行きの間隔（下の段ほど手前）
    readonly List<Disc> discs = new List<Disc>();

    enum State { Idle, Covering, Holding, Revealing }
    State state;
    float front, coverEnd, bandLength, halfW, halfH;
    float startFront;
    float clock, holdStart;                                   // 演出の中の時計（秒）、埋まった時刻
    float easeFrom, easeTo, easeStart, currentSpeed;           // 速さの切り替え（画面の縦幅 / 秒）

    // 今の速さから to へ切り替え始める
    void StartEase(float to)
    {
        easeFrom = currentSpeed;
        easeTo = to;
        easeStart = clock;
    }

    // easeInCubic で切り替えた今の速さ
    float SpeedNow()
    {
        float t = Mathf.Clamp01((clock - easeStart) / Mathf.Max(0.01f, speedEase));
        return Mathf.Lerp(easeFrom, easeTo, t * t * t);
    }

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

        // ステージは起動中ずっと有効のままにしておく（毎回 SetActive すると、その分がカクつきになる）。
        // カメラは普段は切っておき、遷移のときだけ有効にする。円柱と RenderTexture も、
        // 起動時の黒画面の間にここで作っておく。
        stage.SetActive(true);
        cam.enabled = false;
        Layout();
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
        image.enabled = true;
        cam.enabled = true;
        clock = 0f;
        currentSpeed = speedFast;
        easeFrom = easeTo = speedFast;   // 入ってくるときは速く
        easeStart = 0f;
        state = State.Covering;
        Tick(0f);
        while (state == State.Covering) yield return null;

        // 覆いきったあと、円柱が止まるまで待つ。
        // 止まった状態なら、この後の読み込み・シーン有効化・GC が重くても画面が変わらず、カクつきが見えない。
        float stopAt = clock + speedEase + 0.02f;
        while (clock < stopAt) yield return null;
        holdStart = clock;
    }

    public IEnumerator Reveal()
    {
        // 止まっているのを見せる時間を確保してから、速くして抜ける
        while (state == State.Holding && clock - holdStart < minHold) yield return null;
        StartEase(speedFast);
        state = State.Revealing;
        while (state == State.Revealing) yield return null;
        image.enabled = false;
        cam.enabled = false;
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
        if (meshesDirty && bodyMeshes != null)
        {
            // 調整で厚み・色が変わった：古いメッシュを捨てて作り直す
            foreach (var m in bodyMeshes) if (m != null) Object.Destroy(m);
            foreach (var m in patternMeshes) if (m != null) Object.Destroy(m);
            bodyMeshes = patternMeshes = null;
        }
        meshesDirty = false;
        if (bodyMeshes == null) BuildMeshes();

        // 段（色の数。画面の縦幅を等分）× 列（先頭から後ろへ、等間隔）。ばらつき無しで縦横にそろえる
        int rows = Mathf.Max(1, bodyMeshes.Length);
        float spacingY = halfH * 2f / rows;
        int cols = Mathf.CeilToInt((halfW * 2f + 3f) / spacingX) + 1;
        bandLength = cols * spacingX;
        rowDepth = bandLength * 2f + 5f;
        int need = rows * cols;
        while (discs.Count < need) discs.Add(MakeDisc(discs.Count));

        for (int i = 0; i < discs.Count; i++)
        {
            var d = discs[i];
            bool used = i < need;
            d.root.gameObject.SetActive(used);
            if (!used) continue;
            int row = i / cols, col = i % cols;   // row は下から
            d.row = row;
            d.ox = -col * spacingX;
            d.oy = -halfH + (row + 0.5f) * spacingY;
            d.root.localPosition = new Vector3(0f, d.oy, Depth(d));
            d.root.localRotation = Quaternion.Euler(0f, yaw * dir, 0f);
            int c = rows - 1 - row;   // 色は上から
            d.root.GetComponent<MeshFilter>().sharedMesh = bodyMeshes[c];
            d.pattern.GetComponent<MeshFilter>().sharedMesh = patternMeshes[c];
            d.pattern.localRotation = Quaternion.identity;
        }
        cam.farClipPlane = rows * rowDepth + 20f;

        // 先頭は画面の外（入ってくる側）から。先頭が反対側の外まで来たら埋まっている
        front = startFront = -halfW - 0.8f;
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
        clock += dt;
        currentSpeed = SpeedNow();
        front += currentSpeed * halfH * 2f * dt;

        // 模様の角度（先頭の列 = front の位置での回り方）。入ってくる前の先頭（front = -halfW - 0.8）で 0°
        float patternAngle = -(front - startFront) * 2f * Mathf.Rad2Deg * dir;
        bool allGone = true;
        foreach (var d in discs)
        {
            if (!d.root.gameObject.activeSelf) continue;
            float x = front + d.ox;
            // 埋まっている間（読み込み中）は、抜けた円柱を後ろへ回して、いつまでも埋めておく
            if (state == State.Holding && x > halfW + 1.2f) { d.ox -= bandLength; x -= bandLength; }
            if (x - 0.7f <= halfW) allGone = false;
            d.root.localPosition = new Vector3(x * dir, d.oy, Depth(d));
            // 転がる：進んだ距離 ÷ 半径 だけ回る（右へ進むときは時計回り）。
            // 模様の向きは全部そろえる（どれも先頭の列と同じ回り方。位置で角度を変えない）
            d.pattern.localRotation = Quaternion.Euler(0f, 0f, patternAngle);
        }

        if (state == State.Covering && front >= coverEnd)
        {
            // 全面が埋まった：遅くする
            state = State.Holding;
            holdStart = clock;
            StartEase(speedSlow);
        }
        else if (state == State.Revealing && allGone) state = State.Idle;
    }

    // 重なり順：下の段ほど手前、同じ段では先頭ほど手前（隣どうしが食い込まない間隔をあける）
    float Depth(Disc d) => d.row * rowDepth + (-d.ox) * 2f;

    class Runner : MonoBehaviour
    {
        public TumikiRollTransition owner;
        bool fogWas;
        void Update() { if (owner != null) owner.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.05f)); }

        // シーンのフォグがこのカメラ（円柱）にかかって白っぽくならないように、描く間だけ切る
        void OnEnable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += FogOff;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += FogBack;
        }
        void OnDisable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= FogOff;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= FogBack;
        }
        void FogOff(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera c)
        {
            if (owner == null || c != owner.cam) return;
            fogWas = RenderSettings.fog;
            RenderSettings.fog = false;
        }
        void FogBack(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera c)
        {
            if (owner == null || c != owner.cam) return;
            RenderSettings.fog = fogWas;
        }
    }

    // ---------------- 円柱のメッシュ ----------------
    // 直径 1、厚み thickness、角を少し丸めた円柱。軸は z（手前の面が -z）。陰影を頂点カラーに焼き込む
    static readonly Vector3 LightDir = new Vector3(-0.35f, 0.75f, -0.55f).normalized;

    void BuildMeshes()
    {
        var palette = rowColors != null && rowColors.Length > 0 ? rowColors : ToyKit.Palette;
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
