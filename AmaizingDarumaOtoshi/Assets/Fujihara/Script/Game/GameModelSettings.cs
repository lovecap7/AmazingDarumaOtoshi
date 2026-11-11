using UnityEngine;
using Nakahira;

// 試合・リザルトで使う、だるまの頭と積み木のモデルの設定。
// Resources/GameModelSettings.asset を読み込んで使う（GameModelSwapper・リザルト画面）。
// だるまの頭のモデルは各キャラの CharacterData.model（キャラクターセレクトの顔写真と同じもの）を使う。
[CreateAssetMenu(menuName = "Daruma/Game Model Settings", fileName = "GameModelSettings")]
public class GameModelSettings : ScriptableObject
{
    public const string ResourcePath = "GameModelSettings";

    [Tooltip("オフにすると、試合の頭も積み木も元の見た目（球・円柱）のまま")]
    public bool useModels = true;

    [Header("だるま")]
    [Tooltip("キャラの一覧（CharacterData.model を頭に使う。ネット対戦などで持ち主が分からない体は、プレハブの名前から探す）")]
    public SelectRoster roster;
    [Tooltip("頭のモデルの向き（モデルの顔は +Z 向き。キャラも +Z が正面なので 0）")]
    public float headYaw = 0f;
    [Tooltip("幽霊の頭もモデルの形にする（色は幽霊の半透明のまま）")]
    public bool ghostUsesModelShape = true;

    [Header("積み木")]
    [Tooltip("積み木の形（Model/Tsumiki/Goldline/tsumiki_goldline.fbx）")]
    public Mesh tsumikiMesh;
    [Tooltip("オン：柄あり（桜・青海波・麻の葉・雲）　オフ：柄なし（金の線だけ）")]
    public bool tsumikiPattern = true;
    [Tooltip("柄ありの積み木のマテリアル（赤・青・緑・黄の順）")]
    public Material[] patternMaterials = new Material[4];
    [Tooltip("柄なしの積み木のマテリアル（赤・青・緑・黄の順）")]
    public Material[] plainMaterials = new Material[4];

    static GameModelSettings s_loaded;
    static bool s_tried;

    public static GameModelSettings Current
    {
        get
        {
            if (!s_tried) { s_tried = true; s_loaded = Resources.Load<GameModelSettings>(ResourcePath); }
            return s_loaded;
        }
    }

    public bool UseTsumiki => useModels && tsumikiMesh != null;

    // その色の積み木のマテリアル（無ければ null）
    public Material TsumikiMaterial(TumikiColor color)
    {
        var list = tsumikiPattern ? patternMaterials : plainMaterials;
        int i = (int)color;
        return list != null && i >= 0 && i < list.Length ? list[i] : null;
    }

    // ゲームの積み木の色（Unity の色）から、一番近い TumikiColor を探す（リザルトに渡ってくるのは色だけなので）
    public static TumikiColor NearestColor(Color c)
    {
        var best = TumikiColor.Red;
        float bestD = float.MaxValue;
        foreach (TumikiColor t in System.Enum.GetValues(typeof(TumikiColor)))
        {
            var u = TumikiColorUtil.ToUnityColor(t);
            float d = (u.r - c.r) * (u.r - c.r) + (u.g - c.g) * (u.g - c.g) + (u.b - c.b) * (u.b - c.b);
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    [Header("ハンマー（タイトル・メニュー・リザルトの ToyKit のハンマー）")]
    [Tooltip("Blender のハンマー（Model/Hammer/hammer.fbx）。柄が +Y、頭の中心が y = 2.2、頭は X 向き（ToyKit のハンマーと同じ寸法）")]
    public GameObject hammerModel;
    [Tooltip("オフにすると、ハンマーは元の見た目（円柱の組み合わせ）")]
    public bool useHammerModel = true;

    [Tooltip("タイトル・メニューのカーソル（ロゴや項目を叩く）ハンマー。空なら共通のハンマー（hammerModel）")]
    public GameObject cursorHammerModel;

    // タイトル・メニューのカーソルのハンマー（無ければ null ＝ 共通のハンマー）
    public static GameObject CursorHammer
    {
        get
        {
            var s = Current;
            return s != null && s.useHammerModel ? s.cursorHammerModel : null;
        }
    }

    public static GameObject HammerModel
    {
        get
        {
            var s = Current;
            return s != null && s.useHammerModel ? s.hammerModel : null;
        }
    }

    // ---------------- リザルト画面（ToyKit の頭・積み木）用 ----------------
    [Header("リザルト")]
    [Tooltip("リザルトの頭のモデルの高さ（ToyKit の頭の球は 0.9）")]
    public float resultHeadHeight = 0.85f;

    // ToyKit.CreateHead の頭を、そのキャラのモデルにする（モデルが無ければそのまま）。差し替えたら true
    public static bool ApplyHead(ToyKit.Face face, CharacterData character)
    {
        var s = Current;
        if (s == null || !s.useModels || face == null || character == null || character.model == null) return false;
        ToyKit.AttachHeadModel(face.root, character.model, s.resultHeadHeight, 180f);   // ToyKit の頭は -Z が正面
        return true;
    }

    // ToyKit.CreateBlock の積み木を、試合と同じ積み木のモデルにする（color に一番近い色の積み木）。差し替えたら true
    public static bool ApplyBlock(Transform block, Color color)
    {
        var s = Current;
        if (s == null || !s.UseTsumiki || block == null) return false;
        var mat = s.TsumikiMaterial(NearestColor(color));
        if (mat == null) return false;
        ToyKit.UseBlockModel(block, null, s.tsumikiMesh, mat, new Vector3(ToyKit.BlockW, ToyKit.BlockH, ToyKit.BlockD), 0f);
        return true;
    }

    // ---------------- メッシュを入れ物に合わせる ----------------
    static readonly System.Collections.Generic.Dictionary<Mesh, System.Collections.Generic.Dictionary<string, Mesh>> s_fitted = new System.Collections.Generic.Dictionary<Mesh, System.Collections.Generic.Dictionary<string, Mesh>>();

    // src を、target の大きさ（Bounds）にぴったり収まるメッシュにする（元のメッシュの Transform のまま見た目だけ差し替えるため）。
    // uniform = true なら縦横の比率を保って、底をそろえて中に収める（だるまの頭）。false なら縦横それぞれ合わせる（積み木）
    public static Mesh Fit(Mesh src, Bounds target, bool uniform, float yaw)
    {
        if (src == null) return null;
        string key = target.center.ToString("F4") + target.size.ToString("F4") + uniform + yaw;
        if (!s_fitted.TryGetValue(src, out var byKey)) s_fitted[src] = byKey = new System.Collections.Generic.Dictionary<string, Mesh>();
        if (byKey.TryGetValue(key, out var cached) && cached != null) return cached;
        if (!src.isReadable)
        {
            Debug.LogWarning($"[GameModelSettings] {src.name} の Read/Write が オフ なので差し替えられません（fbx の Import Settings で Read/Write をオンにする）");
            return null;
        }

        var rot = Quaternion.Euler(0f, yaw, 0f);
        var v = src.vertices;
        for (int i = 0; i < v.Length; i++) v[i] = rot * v[i];
        var b = new Bounds(v.Length > 0 ? v[0] : Vector3.zero, Vector3.zero);
        foreach (var p in v) b.Encapsulate(p);

        Vector3 scale;
        Vector3 offset;
        if (uniform)
        {
            float k = Mathf.Min(target.size.x / Mathf.Max(b.size.x, 1e-5f), target.size.y / Mathf.Max(b.size.y, 1e-5f), target.size.z / Mathf.Max(b.size.z, 1e-5f));
            scale = Vector3.one * k;
            // 横は真ん中、縦は底をそろえる
            offset = new Vector3(target.center.x - b.center.x * k, target.min.y - b.min.y * k, target.center.z - b.center.z * k);
        }
        else
        {
            scale = new Vector3(target.size.x / Mathf.Max(b.size.x, 1e-5f), target.size.y / Mathf.Max(b.size.y, 1e-5f), target.size.z / Mathf.Max(b.size.z, 1e-5f));
            offset = target.center - Vector3.Scale(b.center, scale);
        }
        for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], scale) + offset;

        var m = new Mesh { name = src.name + "_Fit" };
        m.indexFormat = src.indexFormat;
        m.vertices = v;
        // 法線は拡大の逆数で曲げる（縦横で拡大がちがうときも、元の Transform の縮小で元の向きに戻る）
        var n = src.normals;
        if (n != null && n.Length == v.Length)
        {
            var inv = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.Scale(rot * n[i], inv).normalized;
            m.normals = n;
        }
        var t = src.tangents;
        if (t != null && t.Length == v.Length)
        {
            for (int i = 0; i < t.Length; i++)
            {
                var d = Vector3.Scale(rot * new Vector3(t[i].x, t[i].y, t[i].z), scale).normalized;
                t[i] = new Vector4(d.x, d.y, d.z, t[i].w);
            }
            m.tangents = t;
        }
        var uv = new System.Collections.Generic.List<Vector2>();
        src.GetUVs(0, uv); if (uv.Count == v.Length) m.SetUVs(0, uv);
        uv.Clear(); src.GetUVs(1, uv); if (uv.Count == v.Length) m.SetUVs(1, uv);
        var col = src.colors; if (col != null && col.Length == v.Length) m.colors = col;
        m.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++) m.SetTriangles(src.GetTriangles(s), s);
        m.RecalculateBounds();
        byKey[key] = m;
        return m;
    }

    // モデル（fbx）の中の最初のメッシュとマテリアル
    public static bool MeshOf(GameObject model, out Mesh mesh, out Material material)
    {
        mesh = null; material = null;
        if (model == null) return false;
        var mf = model.GetComponentInChildren<MeshFilter>(true);
        var mr = model.GetComponentInChildren<MeshRenderer>(true);
        if (mf == null || mf.sharedMesh == null) return false;
        mesh = mf.sharedMesh;
        material = mr != null ? mr.sharedMaterial : null;
        return true;
    }
}
