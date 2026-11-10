using System.Collections.Generic;
using UnityEngine;

namespace Nakagawa
{
    // ルーレット盤(Stage20)で使う、扇形・輪の形のメッシュを作る
    // 角度は Unity の Y 回転と同じ(0° = +Z、上から見て時計回りが正)。点 = Quaternion.Euler(0, 角度, 0) * forward * 半径
    public static class RouletteMeshes
    {
        // 半径 r・角度 deg の水平の点(高さ y)
        public static Vector3 Point(float r, float deg, float y = 0.0f)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
        }

        // 扇形の板(r0〜r1、角度 a0〜a1、上面 top・下面 bottom)。sides: 内側・外側・始め・終わりの側面を付けるか
        public static Mesh Sector(float r0, float r1, float a0, float a1, float top, float bottom,
            bool inner = true, bool outer = true, bool start = true, bool end = true, int segments = 0)
        {
            var b = new Builder();
            if (segments <= 0) segments = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 4.0f));
            for (int i = 0; i < segments; i++)
            {
                float s0 = Mathf.Lerp(a0, a1, (float)i / segments), s1 = Mathf.Lerp(a0, a1, (float)(i + 1) / segments);
                b.Cell(r0, r1, s0, s1, top, bottom);
                if (inner && r0 > 0.001f) b.ArcSide(r0, s0, s1, top, bottom, false);
                if (outer) b.ArcSide(r1, s0, s1, top, bottom, true);
            }
            if (start && Mathf.Abs(a1 - a0) < 359.9f) b.RadialSide(r0, r1, a0, top, bottom, false);
            if (end && Mathf.Abs(a1 - a0) < 359.9f) b.RadialSide(r0, r1, a1, top, bottom, true);
            return b.ToMesh("RouletteSector");
        }

        // 面を足していくための入れ物(上面・下面・側面。法線は外向きになるよう向きを合わせる)
        public class Builder
        {
            readonly List<Vector3> m_v = new List<Vector3>();
            readonly List<Vector3> m_n = new List<Vector3>();
            readonly List<Vector2> m_uv = new List<Vector2>();
            readonly List<int> m_t = new List<int>();

            // 扇形の1マス分の上面と下面
            public void Cell(float r0, float r1, float a0, float a1, float top, float bottom, bool withBottom = true)
            {
                Quad(Point(r0, a0, top), Point(r0, a1, top), Point(r1, a1, top), Point(r1, a0, top), Vector3.up);
                if (withBottom) Quad(Point(r0, a0, bottom), Point(r0, a1, bottom), Point(r1, a1, bottom), Point(r1, a0, bottom), Vector3.down);
            }

            // 半径 r の弧の側面。outward: 外向き(中心から離れる向き)の面か
            public void ArcSide(float r, float a0, float a1, float top, float bottom, bool outward)
            {
                Vector3 mid = Point(1.0f, (a0 + a1) * 0.5f);
                Quad(Point(r, a0, top), Point(r, a1, top), Point(r, a1, bottom), Point(r, a0, bottom), outward ? mid : -mid);
            }

            // 角度 a の半径方向の側面。positive: 角度が増える向きを向く面か
            public void RadialSide(float r0, float r1, float a, float top, float bottom, bool positive)
            {
                Vector3 dir = Point(1.0f, a + 90.0f);
                Quad(Point(r0, a, top), Point(r1, a, top), Point(r1, a, bottom), Point(r0, a, bottom), positive ? dir : -dir);
            }

            // 四角(a b c d の順に周る)。法線が normal の向きになるよう三角形の向きを決める
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                // 向きは対角線どうしの外積で調べる(中心で2点が重なる扇形の四角でも正しく求まるように)
                Vector3 n = Vector3.Cross(c - a, d - b);
                if (Vector3.Dot(n, normal) < 0.0f)
                {
                    (b, d) = (d, b);
                }
                int i = m_v.Count;
                m_v.Add(a); m_v.Add(b); m_v.Add(c); m_v.Add(d);
                Vector3 nn = normal.normalized;
                for (int k = 0; k < 4; k++) m_n.Add(nn);
                m_uv.Add(new Vector2(a.x, a.z)); m_uv.Add(new Vector2(b.x, b.z)); m_uv.Add(new Vector2(c.x, c.z)); m_uv.Add(new Vector2(d.x, d.z));
                m_t.Add(i); m_t.Add(i + 1); m_t.Add(i + 2);
                m_t.Add(i); m_t.Add(i + 2); m_t.Add(i + 3);
            }

            public int VertexCount => m_v.Count;

            public Mesh ToMesh(string name, Mesh reuse = null)
            {
                var mesh = reuse != null ? reuse : new Mesh();
                mesh.Clear();
                mesh.name = name;
                if (m_v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(m_v);
                mesh.SetNormals(m_n);
                mesh.SetUVs(0, m_uv);
                mesh.SetTriangles(m_t, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
