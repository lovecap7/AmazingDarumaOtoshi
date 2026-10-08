using UnityEngine;
using UnityEngine.UI;

// 企画書のだるまの顔（縁取りした丸・目・にっこり口）をUIとして描く（画像素材なし）。
// RectTransform の大きさに合わせて楕円になる。塗りの色は Graphic の Color。
[RequireComponent(typeof(CanvasRenderer))]
public class FaceGraphic : MaskableGraphic
{
    [SerializeField] Color lineColor = new Color(0.27f, 0.15f, 0.08f);
    [SerializeField] float outlineWidth = 5f;

    [Header("目（半径に対する割合）")]
    [SerializeField] Vector2 eyeOffset = new Vector2(0.28f, 0.18f);
    [SerializeField] float eyeSize = 0.11f;

    [Header("口（半径に対する割合）")]
    [SerializeField] float smileWidth = 0.42f;
    [SerializeField] float smileY = -0.22f;
    [SerializeField] float smileDepth = 0.16f;
    [SerializeField] float smileThickness = 0.07f;

    const int Segments = 40;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = rectTransform.rect;
        Vector2 c = r.center;
        float rx = r.width * 0.5f, ry = r.height * 0.5f;

        Ellipse(vh, c, rx, ry, lineColor);
        Ellipse(vh, c, rx - outlineWidth, ry - outlineWidth, color);

        float ex = eyeOffset.x * rx, ey = eyeOffset.y * ry;
        Ellipse(vh, c + new Vector2(-ex, ey), eyeSize * rx, eyeSize * ry * 1.2f, lineColor);
        Ellipse(vh, c + new Vector2(ex, ey), eyeSize * rx, eyeSize * ry * 1.2f, lineColor);

        Smile(vh, c, rx, ry);
    }

    void Ellipse(VertexHelper vh, Vector2 center, float rx, float ry, Color32 col)
    {
        int start = vh.currentVertCount;
        vh.AddVert(center, col, Vector2.zero);
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            vh.AddVert(center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry), col, Vector2.zero);
        }
        for (int i = 0; i < Segments; i++)
            vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % Segments);
    }

    // 下にふくらんだ弧を太線で描く
    void Smile(VertexHelper vh, Vector2 center, float rx, float ry)
    {
        const int n = 16;
        float half = smileThickness * ry * 0.5f;
        int start = vh.currentVertCount;
        for (int i = 0; i <= n; i++)
        {
            float u = i / (float)n * 2f - 1f;   // -1〜1
            float x = u * smileWidth * rx;
            float y = (smileY - smileDepth * (1f - u * u)) * ry;
            vh.AddVert(center + new Vector2(x, y + half), lineColor, Vector2.zero);
            vh.AddVert(center + new Vector2(x, y - half), lineColor, Vector2.zero);
        }
        for (int i = 0; i < n; i++)
        {
            int a = start + i * 2;
            vh.AddTriangle(a, a + 1, a + 3);
            vh.AddTriangle(a, a + 3, a + 2);
        }
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        SetVerticesDirty();
    }
}
