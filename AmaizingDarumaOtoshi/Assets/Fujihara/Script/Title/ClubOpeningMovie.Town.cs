using UnityEngine;

// オープニングムービーの「夕焼けの帰り道」のセット：住宅街の細い道。
// 道は +Z の奥へまっすぐ伸び、突き当たりの空に夕日。両わきに家・ブロック塀・電柱と電線。
public partial class ClubOpeningMovie
{
    static readonly Vector3 TownSet = new Vector3(8000f, 0f, 0f);

    void BuildTown()
    {
        var root = SetBase("Town", TownSet);

        // 道（アスファルト）と、両はしの白線・歩道
        var ground = ToyKit.Primitive(PrimitiveType.Plane, "Ground", root);
        ground.position = TownSet + new Vector3(0f, -0.01f, 20f);
        ground.localScale = new Vector3(8f, 1f, 12f);
        ground.GetComponent<Renderer>().sharedMaterial = ToyKit.Lit(new Color(0.55f, 0.5f, 0.5f), 0.2f);
        Box(root, "Road", TownSet + new Vector3(0f, 0f, 20f), new Vector3(5.6f, 0.02f, 120f), ToyKit.Lit(new Color(0.32f, 0.3f, 0.32f), 0.3f));
        var white = ToyKit.Lit(new Color(0.95f, 0.93f, 0.88f), 0.2f);
        foreach (float x in new[] { -2.5f, 2.5f })
            Box(root, "EdgeLine", TownSet + new Vector3(x, 0.02f, 20f), new Vector3(0.12f, 0.01f, 120f), white);

        // 夕焼けの空と夕日（道の突き当たり）
        var sky = TitleDressingKit.Quad(root, "SunsetSky", TownSet + new Vector3(0f, 18f, 75f), new Vector3(160f, 50f, 1f),
                                        TitleDressingKit.Unlit(Color.white, SkyGradient(new Color(0.42f, 0.3f, 0.6f), new Color(1f, 0.62f, 0.32f))));
        var sun = ToyKit.Primitive(PrimitiveType.Sphere, "Sun", root);
        sun.position = TownSet + new Vector3(-1.5f, 5f, 72f);
        sun.localScale = Vector3.one * 7f;
        sun.GetComponent<Renderer>().sharedMaterial = TitleDressingKit.Unlit(new Color(1.6f, 0.95f, 0.45f));   // ブルームで光る
        _ = sky;

        // 両わきの家（壁と三角屋根、明かりのついた窓）
        var walls = new[] { new Color(0.93f, 0.88f, 0.8f), new Color(0.85f, 0.8f, 0.72f), new Color(0.9f, 0.82f, 0.75f), new Color(0.8f, 0.78f, 0.74f) };
        var roofs = new[] { new Color(0.3f, 0.32f, 0.4f), new Color(0.45f, 0.22f, 0.18f), new Color(0.25f, 0.3f, 0.28f) };
        var window = TitleDressingKit.Unlit(new Color(1.4f, 1f, 0.55f));   // 夕方なので明かりがともっている
        var rng = new System.Random(21);
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 9; i++)
            {
                float z = -10f + i * 7.5f + (float)rng.NextDouble() * 1.5f;
                float w = 5.5f, h = 3.6f + (float)rng.NextDouble() * 1.8f, d = 5.5f;
                var c = TownSet + new Vector3(side * 8.2f, 0f, z);
                Box(root, "House", c + Vector3.up * h * 0.5f, new Vector3(w, h, d), ToyKit.Lit(walls[rng.Next(walls.Length)], 0.2f));
                // 屋根：45 度回した箱を半分うめて、三角屋根に見せる
                var roof = Box(root, "Roof", c + Vector3.up * h, new Vector3(w * 0.75f, w * 0.75f, d + 0.6f), ToyKit.Lit(roofs[rng.Next(roofs.Length)], 0.4f));
                roof.rotation = Quaternion.Euler(0f, 0f, 45f);
                // 道に面した窓
                for (int k = 0; k < 2; k++)
                    if (rng.NextDouble() < 0.75)
                    {
                        var wq = TitleDressingKit.Quad(root, "Window", c + new Vector3(-side * (w * 0.5f + 0.02f), h * 0.55f, -1.2f + k * 2.4f), new Vector3(1f, 0.8f, 1f), window);
                        wq.rotation = Quaternion.Euler(0f, side > 0f ? -90f : 90f, 0f);
                    }
            }

        // ブロック塀（道ぞい）
        var block = ToyKit.Lit(new Color(0.68f, 0.66f, 0.62f), 0.15f);
        foreach (float side in new[] { -1f, 1f })
            Box(root, "BlockWall", TownSet + new Vector3(side * 4.4f, 0.6f, 20f), new Vector3(0.25f, 1.2f, 120f), block);

        // 電柱と電線
        var pole = ToyKit.Lit(new Color(0.55f, 0.53f, 0.5f), 0.2f);
        var wireMat = TitleDressingKit.Unlit(new Color(0.12f, 0.1f, 0.12f));
        const float poleH = 7.5f;
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3? prev = null;
            for (int i = 0; i < 8; i++)
            {
                var p = TownSet + new Vector3(side * 3.6f, 0f, -6f + i * 11f);
                var cyl = ToyKit.Primitive(PrimitiveType.Cylinder, "Pole", root);
                cyl.position = p + Vector3.up * poleH * 0.5f;
                cyl.localScale = new Vector3(0.28f, poleH * 0.5f, 0.28f);
                cyl.GetComponent<Renderer>().sharedMaterial = pole;
                Box(root, "CrossArm", p + Vector3.up * (poleH - 0.6f), new Vector3(1.6f, 0.12f, 0.12f), pole);
                var top = p + Vector3.up * (poleH - 0.55f);
                if (prev.HasValue)
                    foreach (float o in new[] { -0.6f, 0.6f })
                    {
                        // 少したるんだ電線
                        var lr = new GameObject("Wire").AddComponent<LineRenderer>();
                        lr.transform.SetParent(root, false);
                        lr.positionCount = 9;
                        for (int k = 0; k < 9; k++)
                        {
                            float u = k / 8f;
                            lr.SetPosition(k, Vector3.Lerp(prev.Value, top, u) + new Vector3(o, -Mathf.Sin(u * Mathf.PI) * 0.5f, 0f));
                        }
                        lr.widthMultiplier = 0.03f;
                        lr.material = wireMat;
                        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                prev = top;
            }
        }
    }
}
