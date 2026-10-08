using UnityEngine;
using UnityEngine.UI;

// 企画書の「ドカッ」のようなギザギザの星形をUIとして描く（画像素材なし）。
// 縁取り → 塗りの順に2枚重ねて描画する。
[RequireComponent(typeof(CanvasRenderer))]
public class StarBurst : MaskableGraphic
{
    [SerializeField, Range(3, 32)] int points = 10;
    [SerializeField] float outerRadius = 80f;
    [SerializeField] float innerRadius = 40f;
    [SerializeField] float outlineWidth = 8f;
    [SerializeField] Color outlineColor = new Color(0.27f, 0.15f, 0.08f);

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        AddStar(vh, outerRadius + outlineWidth, innerRadius + outlineWidth, outlineColor * color);
        AddStar(vh, outerRadius, innerRadius, color);
    }

    void AddStar(VertexHelper vh, float outer, float inner, Color32 c)
    {
        int start = vh.currentVertCount;
        vh.AddVert(Vector3.zero, c, Vector2.zero);

        int n = points * 2;
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.PI * 0.5f + i * Mathf.PI * 2f / n;
            float r = (i % 2 == 0) ? outer : inner;
            vh.AddVert(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r), c, Vector2.zero);
        }
        for (int i = 0; i < n; i++)
            vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % n);
    }
}
