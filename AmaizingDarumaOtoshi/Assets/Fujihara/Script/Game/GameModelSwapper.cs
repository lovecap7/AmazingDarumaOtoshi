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
        // 木の積み木が崩れたときの破片は、染めた色（誰も触っていなければ木の色）にする
        if (settings != null && settings.UseWood) TumikiBlock.ShatterColorOverride = WoodColorOf;
    }

    void OnDestroy()
    {
        if (TumikiBlock.ShatterColorOverride == (System.Func<TumikiBlock, Color>)WoodColorOf) TumikiBlock.ShatterColorOverride = null;
    }

    // 破片の色：木の積み木に掛けている色（破片も木のマテリアルなので、木目にこの色が掛かる）
    Color WoodColorOf(TumikiBlock b)
    {
        foreach (var w in woods)
            if (w.block == b) return w.owner != null ? settings.WoodTintFor(w.owner.Color) : Color.white;
        return Color.white;
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
                if (p.Character != null) SwapHead(p.Character.transform, p.CharacterData, false, p.Entry);
                if (p.Ghost != null && settings.ghostUsesModelShape) SwapHead(p.Ghost.transform, p.CharacterData, true, p.Entry);
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
    // entry：そのだるまのプレイヤー（分からなければ null ＝ いつもの木の色）。モデルはプレイヤーの色、鉢巻はチーム戦ならチームの色
    void SwapHead(Transform body, CharacterData data, bool ghost, MatchSetup.Entry entry = null)
    {
        var id = body;
        if (doneHeads.Contains(id)) return;
        doneHeads.Add(id);
        if (data == null || data.model == null) data = FindByName(body.name);
        if (data == null) return;
        var color = entry != null ? GameModelSettings.CharacterColorOf(entry) : DarumaColor.Wood;
        var band = GameModelSettings.HachimakiFor(entry != null ? GameModelSettings.BandColorOf(entry) : DarumaColor.Wood);
        if (!GameModelSettings.MeshOf(data.ModelFor(color), out var mesh, out var mat)) return;

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

        if (!ghost) SwapHammer(body, data, data.HammerFor(color));

        if (!ghost)
        {
            AttachExtras(body, head, mesh, target, data, band);
            if (mat != null) SetFirstMaterial(mr, mat);
            // ゲームは頭の色を _BaseColor で付けるが、モデルは画像に色があるので、この材質だけ白にしておく
            mr.SetPropertyBlock(white, 0);
            // モデルに顔が描いてあるので、球の目は隠す
            foreach (Transform c in head) if (c.name == "Eye") c.gameObject.SetActive(false);
        }
    }

    // ---------------- マント・アクセサリー ----------------
    // CharacterData.extraModels を、頭（Fit した頭のメッシュ）にぴったり重ねて付ける。
    // マント（Cape_ の骨があるもの）には CapeSway を付けて揺らし、必殺技の溜め中は大きくなびかせる
    // band：着ける鉢巻（null なら着けない）。マントと同じく、頭のモデルと同じ原点で作ってあるので同じように重ねる
    void AttachExtras(Transform body, Transform head, Mesh headMesh, Bounds target, CharacterData data, GameObject band)
    {
        var list = new List<GameObject>();
        if (data.extraModels != null) list.AddRange(data.extraModels);
        if (band != null) list.Add(band);
        if (list.Count == 0) return;
        if (!GameModelSettings.FitTransform(headMesh, target, settings.headYaw, out var rot, out float k, out var offset)) return;
        foreach (var extra in list)
        {
            if (extra == null) continue;
            var go = Instantiate(extra, head, false);
            go.name = "Extra_" + extra.name;
            go.transform.localRotation = rot;
            go.transform.localScale = Vector3.one * k;
            go.transform.localPosition = offset;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;

            // 鉢巻：結び目のあとの余りを揺らす（必殺技の溜め中は、マントと同じく大きくなびく）
            if (extra == band)
            {
                var ribbon = HachimakiRig.Make(go);
                if (ribbon != null) go.AddComponent<CapeChargeFromSpecial>().Init(ribbon, body);
                continue;
            }

            bool hasCape = false;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Cape_") && t.name.EndsWith("1")) { hasCape = true; break; }
            if (!hasCape) continue;
            var sway = go.AddComponent<CapeSway>();
            go.AddComponent<CapeChargeFromSpecial>().Init(sway, body);
        }
    }

    // ---------------- ハンマー ----------------
    // キャラのハンマー（CharacterData.hammerModel）を、元の角ばったハンマー（柄 Handle・頭 HammerHead）の位置に置き、元の見た目は消す。
    // 元のハンマー：柄が +Z に 0〜1、頭の中心が z = 1.2、頭は縦（Y）向きで長さ 0.6
    const float GameHammerHeadZ = 1.2f;

    // hammerModel：持たせるハンマー（プレイヤーの色のもの）
    void SwapHammer(Transform body, CharacterData data, GameObject hammerModel)
    {
        if (data == null || hammerModel == null) return;
        var hammer = body.GetComponentInChildren<DarumaHammer>(true);
        if (hammer == null) return;

        // 武器の届く距離（だるきんぐの剣は倍）。当たる距離はレベル（Yurube.DarumaLevel）がレベルの分と一緒に伸ばす。
        // 前のパッシブに戻しているとき（レベルが止まっているとき）だけ、ここで性能の複製を作って伸ばす
        float reach = Mathf.Max(0.5f, data.weaponReach);
        if (Yurube.DarumaLevel.LegacyPassives && !Mathf.Approximately(reach, 1f) && hammer.Params != null)
        {
            var p = Instantiate(hammer.Params);
            p.HitReachScale *= reach;
            hammer.Params = p;
        }
        var pivot = hammer.transform;
        var model = ToyKit.CreateHammerModel("HammerModelHead", hammerModel);
        if (model == null) return;

        // 元の柄と頭は、メッシュを外して見えなくする（点滅で Renderer がまた有効になっても出ないように）
        foreach (Transform c in pivot)
        {
            if (c.name != "Handle" && c.name != "HammerHead") continue;
            var mf = c.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = null;
        }
        model.name = "HammerModel";
        model.SetParent(pivot, false);
        model.localRotation = Quaternion.Euler(0f, 0f, 90f);   // 頭を X 向きから Y 向きへ
        float scale = GameHammerHeadZ / ToyKit.HammerLength;
        // 届く距離を変えた武器は、先の長さが「ふつうのハンマーの先 × 倍率」になるように大きさを合わせる
        if (!Mathf.Approximately(reach, 1f) && GameModelSettings.MeshOf(hammerModel, out var wMesh, out _))
        {
            float normalTop = GameModelSettings.MeshOf(GameModelSettings.HammerModel, out var hMesh, out _) ? hMesh.bounds.max.y : ToyKit.HammerLength + 0.3f;
            scale *= reach * normalTop / Mathf.Max(wMesh.bounds.max.y, 0.01f);
        }
        model.localScale = Vector3.one * scale;
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

        // 木の積み木：形とマテリアルを木にして、持ち主（積んだ人・打った人）が変わるたびに色を付け直す（Update）
        if (settings.UseWood)
        {
            var woodFitted = GameModelSettings.Fit(settings.woodMesh, mf.sharedMesh.bounds, false, 0f);
            if (woodFitted == null) return;
            mf.sharedMesh = woodFitted;
            SetFirstMaterial(mr, settings.woodMaterial);
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, Color.white);
            mr.SetPropertyBlock(mpb, 0);
            woods.Add(new Wood { block = b, renderer = mr, mpb = mpb, owner = null });
            return;
        }

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

    // ---------------- 木の積み木の色 ----------------
    // 積まれている間は、積んでいるだるまのプレイヤーの色。打たれて飛んでいる間は、打った（撃った）プレイヤーの色。
    // 落ちて止まったら、最後の色のまま。まだ誰も触っていなければ木の色
    class Wood { public TumikiBlock block; public Renderer renderer; public MaterialPropertyBlock mpb; public MatchPlayer owner; }
    readonly List<Wood> woods = new List<Wood>();

    void UpdateWoods()
    {
        var match = DarumaMatch.Current;
        for (int i = woods.Count - 1; i >= 0; i--)
        {
            var w = woods[i];
            if (w.block == null || w.renderer == null) { woods.RemoveAt(i); continue; }
            if (match == null) continue;
            MatchPlayer owner = null;
            if (w.block.CurrentState == TumikiBlock.State.Stacked)
            {
                var c = w.block.GetComponentInParent<DarumaCharacter>();
                if (c != null) owner = PlayerOf(match, c);
            }
            else if (w.block.CurrentState == TumikiBlock.State.Flying && w.block.Owner != null)
                owner = PlayerOf(match, w.block.Owner as Component);
            if (owner == null || owner == w.owner) continue;
            w.owner = owner;
            w.mpb.SetColor(BaseColorId, settings.WoodTintFor(owner.Color));
            w.renderer.SetPropertyBlock(w.mpb, 0);
        }
    }

    static MatchPlayer PlayerOf(DarumaMatch match, Component who)
    {
        if (who == null) return null;
        foreach (var p in match.Players)
            if (p != null && ((p.Character != null && p.Character == who) || (p.Ghost != null && p.Ghost == who))) return p;
        return null;
    }

    void Update()
    {
        UpdateWoods();
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
