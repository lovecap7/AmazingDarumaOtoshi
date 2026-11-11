using UnityEngine;
using Nakahira;

// オープニングのムービーに、Blender のだるま・積み木のモデルを使う（GameModelSettings の設定を使う）。
// 色からだるまを決める：赤＝あかだる、青＝あおだる、黄＝きだる、緑＝みだる、ライバル（紫）＝へびだる、王者（銀）＝いわだる
// 積み木は、試合と同じ積み木のモデル（色の近いもの）。モデルが無ければ今までの見た目のまま
public partial class ClubOpeningMovie
{
    const float MovieHeadModelHeight = 0.85f;   // ToyKit の頭（球 0.9）に乗せるモデルの高さ

    static GameModelSettings ModelSettings
    {
        get
        {
            var s = GameModelSettings.Current;
            return s != null && s.useModels ? s : null;
        }
    }

    // 色に合うキャラ（無ければ null）
    static CharacterData CharacterFor(Color c)
    {
        var s = ModelSettings;
        if (s == null || s.roster == null) return null;
        string id = Near(c, Red) ? "akadaru" : Near(c, Blue) ? "aodaru" : Near(c, Yellow) ? "kidaru" : Near(c, Green) ? "midaru"
                  : Near(c, RivalFace) ? "hebidaru" : Near(c, ChampFace) ? "iwadaru" : null;
        return id == null ? null : s.roster.characters.Find(x => x != null && x.id == id);
    }

    // 色に合うだるまのモデル（無ければ null）
    static GameObject DarumaModelFor(Color c) => CharacterFor(c) != null ? CharacterFor(c).model : null;

    // 色に合うキャラのハンマー（無ければ null ＝ 共通のハンマー）
    static GameObject HammerModelFor(Color c) => CharacterFor(c) != null ? CharacterFor(c).hammerModel : null;

    static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.05f;

    // ToyKit の頭（部員・ノートの小さなだるま）をモデルにする
    static void UseHeadModel(ToyKit.Face face, Color color)
    {
        var model = DarumaModelFor(color);
        if (model != null) ToyKit.AttachHeadModel(face.root, model, MovieHeadModelHeight, 180f);
    }

    // 円柱の積み木（Primitive の Cylinder）を積み木のモデルにする。pattern：柄あり（false なら柄なし）
    static void UseTsumikiModel(Transform block, Color color, bool pattern)
    {
        var s = ModelSettings;
        if (s == null || !s.UseTsumiki || block == null) return;
        var mf = block.GetComponent<MeshFilter>();
        var mr = block.GetComponent<MeshRenderer>();
        if (mf == null || mr == null || mf.sharedMesh == null) return;
        var tc = GameModelSettings.NearestColor(color);
        var list = pattern ? s.patternMaterials : s.plainMaterials;
        var mat = list != null && (int)tc < list.Length ? list[(int)tc] : null;
        if (mat == null) mat = s.TsumikiMaterial(tc);
        var fitted = GameModelSettings.Fit(s.tsumikiMesh, mf.sharedMesh.bounds, false, 0f);
        if (fitted == null || mat == null) return;
        mf.sharedMesh = fitted;
        mr.sharedMaterial = mat;
    }

    // 試合の場面のだるま（球の頭 "Head" と目 "Eye"）の頭をモデルにする。球と同じ大きさに収め、目は隠す
    static void UsePuppetHeadModel(Transform body, Color color)
    {
        var model = DarumaModelFor(color);
        if (!GameModelSettings.MeshOf(model, out var mesh, out var mat)) return;
        var head = body.Find("Head");
        if (head == null) return;
        var mf = head.GetComponent<MeshFilter>();
        var fitted = GameModelSettings.Fit(mesh, mf.sharedMesh.bounds, true, 0f);   // だるまも人形も +Z が正面
        if (fitted == null) return;
        mf.sharedMesh = fitted;
        if (mat != null) head.GetComponent<MeshRenderer>().sharedMaterial = mat;
        foreach (Transform c in body) if (c.name == "Eye") c.gameObject.SetActive(false);
    }
}
