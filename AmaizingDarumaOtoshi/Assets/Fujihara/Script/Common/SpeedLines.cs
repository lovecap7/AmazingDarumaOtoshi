using UnityEngine;
using UnityEngine.UI;

// マンガの集中線。画面の外周から中心へ向かう細い三角形をランダムに描く。
// Reroll() を数フレームおきに呼ぶと線がチラついて勢いが出る。
[RequireComponent(typeof(CanvasRenderer))]
public class SpeedLines : MaskableGraphic
{
    [SerializeField] int count = 90;
    [SerializeField, Range(0f, 1f)] float innerMin = 0.42f;   // 線の先端がどこまで中心に寄るか（外周を1とした割合）
    [SerializeField, Range(0f, 1f)] float innerMax = 0.78f;
    [SerializeField] float widthMin = 2f;
    [SerializeField] float widthMax = 10f;

    int seed;

    public void Reroll()
    {
        seed++;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = rectTransform.rect;
        Vector2 c = r.center;
        float outer = r.size.magnitude * 0.5f + 20f;
        var rng = new System.Random(seed);

        for (int i = 0; i < count; i++)
        {
            float a = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var n = new Vector2(-d.y, d.x);
            float w = Mathf.Lerp(widthMin, widthMax, (float)rng.NextDouble());
            float inner = outer * Mathf.Lerp(innerMin, innerMax, (float)rng.NextDouble());

            int s = vh.currentVertCount;
            vh.AddVert(c + d * outer + n * w, color, Vector2.zero);
            vh.AddVert(c + d * outer - n * w, color, Vector2.zero);
            vh.AddVert(c + d * inner, color, Vector2.zero);
            vh.AddTriangle(s, s + 1, s + 2);
        }
    }
}
