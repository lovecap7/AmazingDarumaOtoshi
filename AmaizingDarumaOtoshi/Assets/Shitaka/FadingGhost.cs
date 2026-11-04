using UnityEngine;
using UnityEngine.Rendering;

// その場に残って、時間をかけて薄れて消える、見た目だけのオブジェクト(テレポートの残像用)。
// 少しずつ大きくなる(積み木と同じ位置に出しても、外側にはみ出して見えるようにするため)。
// コライダーもRigidbodyも持たないので、攻撃判定はない
public class FadingGhost : MonoBehaviour
{
    const float StartScaleRatio = 1.05f; // 出現時の大きさ(積み木の表面と重なってちらつかないよう、わずかに大きくする)

    Material mat;
    Color baseColor;
    float startAlpha;
    float lifetime;
    float elapsed;
    Vector3 baseScale;
    float endScaleRatio; // 消えるときの大きさ(積み木の大きさに対する倍率)

    // owner の現在の位置・向き・大きさで、残像を作る。色は baseColor(アルファは startAlpha から薄れていく)
    public static FadingGhost Create(BlockController owner, Vector3 position, float lifetime,
                                 float startAlpha, Color baseColor, Material customMaterial,
                                 float endScaleRatio = 1f)
    {
        var ownerRend = owner.GetComponent<Renderer>();
        var ownerFilter = owner.GetComponent<MeshFilter>();
        if (ownerRend == null || ownerFilter == null) return null;

        var go = new GameObject("TeleportGhost");
        // go.transform.SetPositionAndRotation(owner.transform.position, owner.transform.rotation); // 出現位置を引数で指定するため変更
        go.transform.SetPositionAndRotation(position, owner.transform.rotation);


        // 本体と同じ形のメッシュだけを複製する
        go.AddComponent<MeshFilter>().sharedMesh = ownerFilter.sharedMesh;
        var rend = go.AddComponent<MeshRenderer>();
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        var ghost = go.AddComponent<FadingGhost>();
        ghost.baseScale = owner.transform.localScale;
        ghost.endScaleRatio = endScaleRatio;
        go.transform.localScale = ghost.baseScale * StartScaleRatio;

        ghost.mat = new Material(customMaterial != null ? customMaterial : ownerRend.sharedMaterial);
        ghost.baseColor = baseColor;
        ghost.startAlpha = startAlpha;
        ghost.lifetime = Mathf.Max(0.01f, lifetime);
        ghost.ApplyAlpha(startAlpha);
        rend.material = ghost.mat;
        return ghost;
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        float t = elapsed / lifetime;
        ApplyAlpha(Mathf.Lerp(startAlpha, 0f, t));
        transform.localScale = baseScale * Mathf.Lerp(StartScaleRatio, endScaleRatio, t);
    }

    void ApplyAlpha(float a)
    {
        mat.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
    }

    void OnDestroy()
    {
        if (mat != null) Destroy(mat); // 複製したマテリアルが残り続けないようにする
    }
}