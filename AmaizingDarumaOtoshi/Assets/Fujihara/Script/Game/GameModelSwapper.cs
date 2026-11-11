using System.Collections.Generic;
using UnityEngine;
using Nakahira;

// 試合中のだるまの頭と積み木を、Blender で作ったモデルに差し替える（見た目だけ。当たり判定や動きは変えない）。
// ・頭：選んだキャラの CharacterData.model。元の「Head」（球）のメッシュとマテリアルを差し替え、目（Eye）を隠す
// ・積み木：GameModelSettings の積み木のモデル。色（赤・青・緑・黄）ごとのマテリアルにする
// 元の Transform・Renderer はそのまま使うので、点滅・カットイン・残像・輪郭などはそのまま動く。
// 差し替えたメッシュは元の球・円柱と同じ大きさに収めてある。
// 他の人のプレハブ・シーンは変えず、ゲームが始まると自動で1つ作られる（設定は Resources/GameModelSettings）。
public class GameModelSwapper : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var s = GameModelSettings.Current;
        if (s == null || !s.useModels) return;
        var go = new GameObject("GameModelSwapper");
        DontDestroyOnLoad(go);
        go.AddComponent<GameModelSwapper>();
    }

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly Bounds UnitBox = new Bounds(Vector3.zero, Vector3.one);

    GameModelSettings settings;
    MaterialPropertyBlock white;
    readonly HashSet<Object> doneBlocks = new HashSet<Object>();
    readonly HashSet<Object> doneHeads = new HashSet<Object>();
    int scanFrame;

    void Awake()
    {
        settings = GameModelSettings.Current;
        white = new MaterialPropertyBlock();
        white.SetColor(BaseColorId, Color.white);
    }

    void LateUpdate()
    {
        if (settings == null || !settings.useModels) return;

        if (settings.UseTsumiki)
            foreach (var b in TumikiBlock.All) if (b != null) SwapBlock(b);

        var match = DarumaMatch.Current;
        if (match != null)
            foreach (var p in match.Players)
            {
                if (p == null) continue;
                if (p.Character != null) SwapHead(p.Character.transform, p.CharacterData, false);
                if (p.Ghost != null && settings.ghostUsesModelShape) SwapHead(p.Ghost.transform, p.CharacterData, true);
            }

        // 試合の管理が無い場面（ネット対戦のクライアント・テスト用のシーン）は、プレハブの名前からキャラを探す
        if (++scanFrame >= 20)
        {
            scanFrame = 0;
            foreach (var c in FindObjectsByType<DarumaCharacter>(FindObjectsSortMode.None))
                if (!doneHeads.Contains(c.transform)) SwapHead(c.transform, FindByName(c.name), false);
        }
    }

    // ---------------- 頭 ----------------
    void SwapHead(Transform body, CharacterData data, bool ghost)
    {
        var id = body;
        if (doneHeads.Contains(id)) return;
        doneHeads.Add(id);
        if (data == null || data.model == null) data = FindByName(body.name);
        if (data == null || !GameModelSettings.MeshOf(data.model, out var mesh, out var mat)) return;

        Transform head = null;
        foreach (var t in body.GetComponentsInChildren<Transform>(true))
            if (t.name == "Head" && t.GetComponent<MeshFilter>() != null) { head = t; break; }
        if (head == null) return;

        var mf = head.GetComponent<MeshFilter>();
        var mr = head.GetComponent<MeshRenderer>();
        var src = mf.sharedMesh;
        var target = src != null ? src.bounds : UnitBox;
        var fitted = GameModelSettings.Fit(mesh, target, true, settings.headYaw);
        if (fitted == null) return;
        mf.sharedMesh = fitted;

        if (!ghost)
        {
            if (mat != null) SetFirstMaterial(mr, mat);
            // ゲームは頭の色を _BaseColor で付けるが、モデルは画像に色があるので、この材質だけ白にしておく
            mr.SetPropertyBlock(white, 0);
            // モデルに顔が描いてあるので、球の目は隠す
            foreach (Transform c in head) if (c.name == "Eye") c.gameObject.SetActive(false);
        }
    }

    // ---------------- 積み木 ----------------
    void SwapBlock(TumikiBlock b)
    {
        var id = b;
        if (doneBlocks.Contains(id)) return;
        doneBlocks.Add(id);

        var visual = b.transform.Find("Visual");
        if (visual == null) return;
        var mf = visual.GetComponent<MeshFilter>();
        var mr = visual.GetComponent<MeshRenderer>();
        if (mf == null || mr == null || mf.sharedMesh == null) return;

        var fitted = GameModelSettings.Fit(settings.tsumikiMesh, mf.sharedMesh.bounds, false, 0f);
        if (fitted == null) return;
        var mat = settings.TsumikiMaterial(b.Color);
        if (mat == null) return;
        mf.sharedMesh = fitted;
        SetFirstMaterial(mr, mat);
        mr.SetPropertyBlock(white, 0);
        // 途中で色が変わったら、マテリアルも変える（Update）
        tracked.Add(new Tracked { block = b, renderer = mr, color = b.Color });
    }

    struct Tracked { public TumikiBlock block; public Renderer renderer; public TumikiColor color; }
    readonly List<Tracked> tracked = new List<Tracked>();

    void Update()
    {
        // 色が変わった積み木（特殊な積み木など）のマテリアルを付け直す
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            var t = tracked[i];
            if (t.block == null || t.renderer == null) { tracked.RemoveAt(i); continue; }
            if (t.block.Color == t.color) continue;
            var mat = settings.TsumikiMaterial(t.block.Color);
            if (mat != null) SetFirstMaterial(t.renderer, mat);
            t.color = t.block.Color;
            tracked[i] = t;
        }
    }

    static readonly List<Material> s_mats = new List<Material>();
    static void SetFirstMaterial(Renderer r, Material m)
    {
        r.GetSharedMaterials(s_mats);
        if (s_mats.Count == 0) s_mats.Add(m); else s_mats[0] = m;
        r.SetSharedMaterials(s_mats);
    }

    CharacterData FindByName(string objectName)
    {
        if (settings.roster == null) return null;
        CharacterData best = null;
        int bestLen = 0;
        foreach (var c in settings.roster.characters)
        {
            if (c == null || c.prefab == null) continue;
            string n = c.prefab.name;
            if (n.Length > bestLen && objectName.StartsWith(n)) { best = c; bestLen = n.Length; }
        }
        return best;
    }
}
