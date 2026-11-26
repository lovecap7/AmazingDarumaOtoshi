using System.Collections.Generic;
using UnityEngine;

// 鉢巻の、結び目のあとの余り（うしろに垂れている2本）を揺らす。
// 鉢巻のモデルには骨が無いので、実行中に「余りに沿った骨の鎖（左右2本）」を作ってメッシュに重みを付け、
// SkinnedMeshRenderer にしてから CapeSway（マントと同じ揺れもの）で揺らす。
//  ・鉢巻の輪と結び目（輪の下の端より上）は頭に固定、それより下（余り）だけが揺れる
//  ・余りは x の符号で左右に分け、上（結び目）から下（先）まで均等な高さで骨を置く
// 鉢巻のモデルは Read/Write をオンにしておく（Model/Hachimaki/*.fbx）
public static class HachimakiRig
{
    const int Joints = 5;   // 1本の余りの骨の数（最後は先）

    class Rig
    {
        public Mesh mesh;                 // 重みを付けたメッシュ（元のメッシュごとに1回だけ作る）
        public Vector3[][] joints;        // 左右の骨の位置（モデルの座標）
    }

    static readonly Dictionary<Mesh, Rig> s_rigs = new Dictionary<Mesh, Rig>();

    // 鉢巻を置いたオブジェクト（Instantiate したもの）を揺れるようにする。できたら CapeSway を返す
    public static CapeSway Make(GameObject band)
    {
        if (band == null) return null;
        var mf = band.GetComponentInChildren<MeshFilter>(true);
        var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
        if (mf == null || mr == null || mf.sharedMesh == null) return null;
        var rig = RigOf(mf.sharedMesh);
        if (rig == null) return null;

        var owner = mf.transform;
        var material = mr.sharedMaterials;

        // 骨を作る：0 番は鉢巻そのもの（輪と結び目）、そのあと左の鎖・右の鎖
        var bones = new List<Transform> { owner };
        var roots = new List<Transform>();
        string[] names = { "Hachi_L", "Hachi_R" };
        for (int s = 0; s < 2; s++)
        {
            Transform parent = owner;
            for (int i = 0; i < Joints; i++)
            {
                var b = new GameObject(names[s] + (i + 1)).transform;
                b.SetParent(parent, false);
                b.position = owner.TransformPoint(rig.joints[s][i]);
                b.rotation = owner.rotation;
                bones.Add(b);
                if (i == 0) roots.Add(b);
                parent = b;
            }
        }

        var bind = new Matrix4x4[bones.Count];
        for (int i = 0; i < bones.Count; i++) bind[i] = bones[i].worldToLocalMatrix * owner.localToWorldMatrix;

        // MeshRenderer を SkinnedMeshRenderer に替える
        Object.DestroyImmediate(mr);
        Object.DestroyImmediate(mf);
        var smr = owner.gameObject.AddComponent<SkinnedMeshRenderer>();
        var mesh = Object.Instantiate(rig.mesh);
        mesh.bindposes = bind;
        smr.sharedMesh = mesh;
        smr.bones = bones.ToArray();
        smr.rootBone = owner;
        smr.sharedMaterials = material;
        smr.updateWhenOffscreen = true;

        var sway = owner.gameObject.AddComponent<CapeSway>();
        sway.Setup(roots);
        sway.UseRibbonPreset();
        return sway;
    }

    static Rig RigOf(Mesh src)
    {
        if (s_rigs.TryGetValue(src, out var cached) && cached != null && cached.mesh != null) return cached;
        if (!src.isReadable)
        {
            Debug.LogWarning($"[HachimakiRig] {src.name} の Read/Write がオフなので、鉢巻を揺らせません");
            return null;
        }
        var v = src.vertices;
        if (v.Length == 0) return null;

        // 輪の下の端：前半分（z > 0）の一番下。これより下が余り
        float bandBottom = float.MaxValue;
        foreach (var p in v) if (p.z > 0f && p.y < bandBottom) bandBottom = p.y;
        float top = bandBottom - 0.005f;

        var joints = new Vector3[2][];
        float[] tipY = new float[2];
        for (int s = 0; s < 2; s++)
        {
            float sign = s == 0 ? -1f : 1f;
            float minY = float.MaxValue;
            foreach (var p in v) if (p.y < top && SideOf(p) == sign) minY = Mathf.Min(minY, p.y);
            if (minY == float.MaxValue) return null;   // 余りが無い形
            tipY[s] = minY;
            joints[s] = new Vector3[Joints];
            float step = (top - minY) / (Joints - 1);
            for (int i = 0; i < Joints; i++)
            {
                float y = top - step * i;
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var p in v)
                    if (SideOf(p) == sign && p.y < top + 0.02f && Mathf.Abs(p.y - y) <= step * 0.5f) { sum += p; n++; }
                joints[s][i] = n > 0 ? sum / n : (i > 0 ? joints[s][i - 1] + Vector3.down * step : new Vector3(0f, y, -0.52f));
                if (i == 0) joints[s][i].y = top;
            }
        }

        // 重み：輪と結び目は 0 番（鉢巻そのもの）。余りは高さで、どの骨の区間かを決めてなめらかにつなぐ
        var weights = new BoneWeight[v.Length];
        for (int k = 0; k < v.Length; k++)
        {
            var p = v[k];
            if (p.y >= top) { weights[k] = new BoneWeight { boneIndex0 = 0, weight0 = 1f }; continue; }
            int s = SideOf(p) < 0f ? 0 : 1;
            float t = Mathf.Clamp01((top - p.y) / Mathf.Max(top - tipY[s], 1e-4f)) * (Joints - 1);
            int seg = Mathf.Min(Mathf.FloorToInt(t), Joints - 2);
            float f = t - seg;
            int bone = 1 + s * Joints + seg;                 // この区間の骨
            int prev = seg == 0 ? 0 : bone - 1;              // ひとつ上の骨（いちばん上なら鉢巻そのもの）
            var w = new BoneWeight();
            if (f < 0.5f)
            {
                // 区間の上半分は、上の骨と半分ずつから、だんだんこの骨だけへ
                w.boneIndex0 = bone; w.weight0 = 0.5f + f;
                w.boneIndex1 = prev; w.weight1 = 0.5f - f;
            }
            else { w.boneIndex0 = bone; w.weight0 = 1f; }
            weights[k] = w;
        }

        var mesh = Object.Instantiate(src);
        mesh.name = src.name + "_Rig";
        mesh.boneWeights = weights;
        var rig = new Rig { mesh = mesh, joints = joints };
        s_rigs[src] = rig;
        return rig;
    }

    static float SideOf(Vector3 p) => p.x < 0f ? -1f : 1f;
}
