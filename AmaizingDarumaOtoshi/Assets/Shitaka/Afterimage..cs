using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 本体の動きを、少し遅れてなぞる見た目だけのオブジェクト(残像)。
// コライダーもRigidbodyも持たないので、攻撃判定はない。寿命が来たら消える
public class Afterimage : MonoBehaviour
{
    struct Sample
    {
        public float time;
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 scale;
    }

    readonly List<Sample> history = new List<Sample>();
    Transform source;   // 追従する本体
    float delay;        // 本体より何秒遅れるか
    float dieAt;        // 消える時刻

    public static Afterimage Create(BlockController owner, float delay, float lifetime,
                                    float alpha, Material customMaterial)
    {
        var ownerRend = owner.GetComponent<Renderer>();
        var ownerFilter = owner.GetComponent<MeshFilter>();
        if (ownerRend == null || ownerFilter == null) return null;

        var go = new GameObject("Afterimage"); // タグは付けない(Blockタグを探す処理の対象外にするため)
        go.transform.SetPositionAndRotation(owner.transform.position, owner.transform.rotation);
        go.transform.localScale = owner.transform.localScale;

        // 本体と同じ形のメッシュだけを複製する
        go.AddComponent<MeshFilter>().sharedMesh = ownerFilter.sharedMesh;
        var rend = go.AddComponent<MeshRenderer>();

        // 本体と同じ色で、アルファだけ下げる。マテリアルが指定されていれば、それをベースにする
        var mat = new Material(customMaterial != null ? customMaterial : ownerRend.sharedMaterial);
        Color c = ownerRend.sharedMaterial.color;
        c.a = alpha;
        mat.color = c;
        rend.material = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        var image = go.AddComponent<Afterimage>();
        image.source = owner.transform;
        image.delay = delay;
        image.dieAt = Time.time + lifetime;
        return image;
    }

    void Update()
    {
        if (Time.time >= dieAt)
        {
            Destroy(gameObject);
            return;
        }

        // 本体が残っている間は、毎フレームの位置を記録する
        if (source != null)
        {
            history.Add(new Sample
            {
                time = Time.time,
                pos = source.position,
                rot = source.rotation,
                scale = source.localScale
            });
        }
        if (history.Count == 0) return;

        // delay秒前の時刻に、本体がいた位置を再生する
        float playTime = Time.time - delay;

        // 再生し終わった古い記録を捨てる
        while (history.Count >= 2 && history[1].time <= playTime) history.RemoveAt(0);

        // 前後2つの記録の間を補間する(最初のdelay秒は、先頭の記録=発射位置にとどまる)
        Sample a = history[0];
        Sample b = history.Count >= 2 ? history[1] : a;
        float t = (b.time > a.time) ? Mathf.Clamp01((playTime - a.time) / (b.time - a.time)) : 0f;

        transform.SetPositionAndRotation(Vector3.Lerp(a.pos, b.pos, t), Quaternion.Slerp(a.rot, b.rot, t));
        transform.localScale = Vector3.Lerp(a.scale, b.scale, t);

        // 本体が先に壊れていて、記録の最後まで再生したら消える
        if (source == null && history.Count == 1 && playTime >= a.time) Destroy(gameObject);
    }
}