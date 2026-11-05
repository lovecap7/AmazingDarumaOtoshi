using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // いわだる着弾地点のクレーター。
    // 床面すれすれの窪みメッシュ＋外周ノイズ＋頂点カラーで、台座ではなく「抉れた地面」に見せる。
    public class IwaCraterEffect : MonoBehaviour
    {
        class Rock
        {
            public Transform Transform;
            public Vector3 BaseScale;
        }

        class Decoration
        {
            public Renderer Renderer;
            public bool Enabled;
            public Transform Transform;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        Material m_material;
        Mesh m_mesh;
        bool m_ownsMesh;
        static readonly List<Mesh> s_cachedRockMeshes = new List<Mesh>();
        static readonly Dictionary<string, Mesh> s_craterMeshCache = new Dictionary<string, Mesh>();
        readonly List<Material> m_rockMaterials = new List<Material>();
        readonly List<Rock> m_rocks = new List<Rock>();
        readonly List<Decoration> m_decorations = new List<Decoration>();

        Vector3 m_center;
        float m_radius = 1.0f;
        float m_depth = 0.2f;
        float m_lifetime = 10.0f;
        float m_fadeTime = 2.5f;
        float m_emissionStrength = 2.2f;
        float m_age;

        public static void Spawn(Vector3 center, float radius, float depth,
            float lifetime, float fadeTime, float outlineNoise, float emissionStrength,
            Material groundSource)
        {
            var go = new GameObject("IwaCrater");
            var effect = go.AddComponent<IwaCraterEffect>();
            effect.Initialize(center, radius, depth, lifetime, fadeTime,
                outlineNoise, emissionStrength, groundSource);
        }

        void Initialize(Vector3 center, float radius, float depth,
            float lifetime, float fadeTime, float outlineNoise, float emissionStrength,
            Material groundSource)
        {
            m_center = center;
            m_radius = Mathf.Max(0.7f, radius);
            m_depth = Mathf.Clamp(depth, 0.04f, m_radius * 0.25f);
            m_lifetime = Mathf.Max(1.0f, lifetime);
            m_fadeTime = Mathf.Clamp(fadeTime, 0.1f, m_lifetime);
            m_emissionStrength = Mathf.Max(0.0f, emissionStrength);
            transform.position = center;

            Color groundColor = GetMaterialColor(groundSource, new Color(0.48f, 0.46f, 0.42f));
            m_material = CreateCraterMaterial();
            BuildCraterMesh(groundColor, Mathf.Clamp01(outlineNoise));
            BuildRocks(groundColor);
            HideAndTiltDecorations();
        }

        Material CreateCraterMaterial()
        {
            Shader shader = Shader.Find("Yurube/IwaCrater");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            var material = new Material(shader);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", new Color(1.0f, 0.24f, 0.04f));
            if (material.HasProperty("_EmissionStrength")) material.SetFloat("_EmissionStrength", m_emissionStrength);
            if (material.HasProperty("_Fade")) material.SetFloat("_Fade", 1.0f);
            return material;
        }

        void BuildCraterMesh(Color groundColor, float outlineNoise)
        {
            const int segments = 96;
            // 同じ半径・深さ・色ならメッシュを再利用し、発動ごとの生成を避ける
            string cacheKey = string.Format("{0:F2}_{1:F2}_{2:F2}_{3:F2}_{4:F2}_{5:F2}",
                m_radius, m_depth, outlineNoise, groundColor.r, groundColor.g, groundColor.b);
            if (s_craterMeshCache.TryGetValue(cacheKey, out Mesh cachedMesh) && cachedMesh != null)
            {
                m_mesh = cachedMesh;
                m_ownsMesh = false;
                CreateCraterSurface(cachedMesh);
                return;
            }

            // 中心→底→内壁→リム→外周スカート。外周は頂点アルファ0で床に溶かす。
            float[] radii = { 0.01f, 0.22f, 0.45f, 0.68f, 0.86f, 1.00f, 1.18f };
            float[] heights = { 0.01f, 0.04f, 0.12f, 0.32f, 0.60f, 0.82f, 0.0f };
            float[] colorT = { 0.02f, 0.05f, 0.12f, 0.28f, 0.55f, 0.80f, 0.95f };
            int ringCount = radii.Length;

            var vertices = new Vector3[ringCount * segments];
            var colors = new Color[ringCount * segments];
            var uv = new Vector2[ringCount * segments];
            var triangles = new List<int>();

            Color charColor = new Color(0.025f, 0.022f, 0.019f, 1.0f);
            for (int ring = 0; ring < ringCount; ring++)
            {
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * Mathf.PI * 2.0f / segments;
                    float outline = 1.0f;
                    if (ring >= 3)
                    {
                        float noise = (Mathf.PerlinNoise(angle * 1.7f + 3.1f, 7.7f) - 0.5f) * 2.0f;
                        outline += noise * outlineNoise * (ring == ringCount - 1 ? 1.3f : 1.0f);
                    }

                    float radius = radii[ring] * outline * m_radius;
                    float height = heights[ring] * m_depth;
                    int index = ring * segments + i;
                    vertices[index] = new Vector3(
                        Mathf.Cos(angle) * radius,
                        height,
                        Mathf.Sin(angle) * radius);

                    float t = Mathf.Clamp01(radii[ring] / 1.0f);
                    Color c = Color.Lerp(charColor, groundColor, Mathf.SmoothStep(0.0f, 1.0f, colorT[ring]));
                    c.a = ring == ringCount - 1 ? 0.0f : 1.0f;
                    colors[index] = c;
                    uv[index] = new Vector2(t, 0.0f);
                }
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                triangles.Add(i);
                triangles.Add(next);
                triangles.Add(segments + i);
                triangles.Add(next);
                triangles.Add(segments + next);
                triangles.Add(segments + i);
            }

            for (int ring = 1; ring < ringCount - 1; ring++)
            {
                int startA = ring * segments;
                int startB = (ring + 1) * segments;
                for (int i = 0; i < segments; i++)
                {
                    int next = (i + 1) % segments;
                    int a = startA + i;
                    int b = startA + next;
                    int c = startB + i;
                    int d = startB + next;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }

            m_mesh = new Mesh { name = "IwaCraterMesh" };
            m_mesh.vertices = vertices;
            m_mesh.colors = colors;
            m_mesh.uv = uv;
            m_mesh.triangles = triangles.ToArray();
            m_mesh.RecalculateNormals();
            m_mesh.RecalculateBounds();
            s_craterMeshCache[cacheKey] = m_mesh;
            m_ownsMesh = false;
            CreateCraterSurface(m_mesh);
        }

        void CreateCraterSurface(Mesh mesh)
        {
            var go = new GameObject("IwaCraterSurface");
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = m_material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        void BuildRocks(Color groundColor)
        {
            // クレーター縁の岩石。外周を囲む均等リングにはせず、ランダムに散らす。
            Material sharedRockMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color rockColor = Color.Lerp(groundColor, new Color(0.22f, 0.17f, 0.13f), 0.18f);
            if (sharedRockMaterial.HasProperty("_BaseColor")) sharedRockMaterial.SetColor("_BaseColor", rockColor);
            if (sharedRockMaterial.HasProperty("_Color")) sharedRockMaterial.SetColor("_Color", rockColor);
            m_rockMaterials.Add(sharedRockMaterial);

            int[] counts = { 2, 4, 8 };
            for (int sizeClass = 0; sizeClass < counts.Length; sizeClass++)
            {
                for (int i = 0; i < counts[sizeClass]; i++)
                {
                    float angle = Random.Range(0.0f, Mathf.PI * 2.0f);
                    float distance = m_radius * Random.Range(0.55f, 1.35f);
                    float size = m_radius * (sizeClass == 0
                        ? Random.Range(0.075f, 0.12f)
                        : sizeClass == 1 ? Random.Range(0.04f, 0.075f) : Random.Range(0.018f, 0.04f));

                    var rock = new GameObject($"IwaCraterRock_{sizeClass}_{i}");
                    rock.transform.SetParent(transform, false);
                    var filter = rock.AddComponent<MeshFilter>();
                    Mesh mesh = GetRockMesh(i % 6);
                    filter.sharedMesh = mesh;

                    var renderer = rock.AddComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = sharedRockMaterial;
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false;

                        // 同じマテリアルを使い、色のばらつきだけPropertyBlockで付ける
                        Color color = Color.Lerp(groundColor, new Color(0.22f, 0.17f, 0.13f),
                            Random.Range(0.0f, 0.25f));
                        var block = new MaterialPropertyBlock();
                        block.SetColor("_BaseColor", color);
                        block.SetColor("_Color", color);
                        renderer.SetPropertyBlock(block);
                    }

                    Vector3 scale = new Vector3(
                        size * Random.Range(0.8f, 1.6f),
                        size * Random.Range(0.55f, 1.2f),
                        size * Random.Range(0.8f, 1.5f));
                    float y = size * Random.Range(-0.15f, 0.30f);
                    rock.transform.localPosition = new Vector3(
                        Mathf.Cos(angle) * distance, y, Mathf.Sin(angle) * distance);
                    rock.transform.localRotation = Quaternion.Euler(
                        Random.Range(-45.0f, 45.0f),
                        Random.Range(0.0f, 360.0f),
                        Random.Range(-45.0f, 45.0f));
                    rock.transform.localScale = Vector3.zero;
                    m_rocks.Add(new Rock { Transform = rock.transform, BaseScale = scale });
                }
            }
        }

        void HideAndTiltDecorations()
        {
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.transform.IsChildOf(transform)) continue;
                if (renderer.GetComponentInParent<DarumaCharacter>() != null) continue;
                if (renderer.GetComponentInParent<TumikiBlock>() != null) continue;

                Bounds bounds = renderer.bounds;
                if (bounds.size.y > 0.55f) continue;
                if (Mathf.Abs(bounds.center.y - m_center.y) > 1.2f) continue;
                float extent = Mathf.Max(bounds.extents.x, bounds.extents.z);
                if (extent > m_radius * 1.8f) continue; // 床そのものは除外

                Vector3 delta = bounds.center - m_center;
                delta.y = 0.0f;
                float distance = delta.magnitude;
                if (distance > m_radius * 1.45f) continue;

                Material material = renderer.sharedMaterial;
                Color color = GetMaterialColor(material, Color.white);
                float luma = color.r * 0.3f + color.g * 0.59f + color.b * 0.11f;
                bool white = luma > 0.58f && Mathf.Max(color.r, color.g, color.b) - Mathf.Min(color.r, color.g, color.b) < 0.25f;
                bool named = renderer.name.IndexOf("Line", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || renderer.name.IndexOf("Ring", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || renderer.name.IndexOf("Plate", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || renderer.name.IndexOf("Dash", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || renderer.name.IndexOf("Marker", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!white && !named) continue;

                var state = new Decoration
                {
                    Renderer = renderer,
                    Enabled = renderer.enabled,
                    Transform = renderer.transform,
                    Position = renderer.transform.position,
                    Rotation = renderer.transform.rotation,
                };
                m_decorations.Add(state);

                if (distance <= m_radius * 0.98f)
                {
                    // クレーター内の装飾板は隠す。
                    // 白い板状の破片が空中に残ると岩と紛れるため、破片化はしない。
                    renderer.enabled = false;
                }
                else
                {
                    // 外周の装飾板は衝撃で少し傾ける
                    renderer.transform.rotation = renderer.transform.rotation
                        * Quaternion.Euler(Random.Range(-7.0f, 7.0f), Random.Range(-12.0f, 12.0f),
                            Random.Range(-7.0f, 7.0f));
                }
            }
        }

        static Color GetMaterialColor(Material material, Color fallback)
        {
            if (material == null) return fallback;
            if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color")) return material.GetColor("_Color");
            return fallback;
        }

        public static Material FindGroundMaterial(Vector3 center)
        {
            Vector3 origin = center + Vector3.up * 4.0f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 8.0f,
                ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Material found = null;
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || hit.normal.y < 0.5f) continue;
                if (hit.collider.GetComponentInParent<DarumaCharacter>() != null) continue;
                if (hit.collider.GetComponentInParent<TumikiBlock>() != null) continue;
                if (hit.distance >= best) continue;
                var renderer = hit.collider.GetComponent<Renderer>();
                if (renderer == null || renderer.sharedMaterial == null) continue;
                best = hit.distance;
                found = renderer.sharedMaterial;
            }
            return found;
        }

        public static Mesh GetRockMesh(int index)
        {
            while (s_cachedRockMeshes.Count < 6)
                s_cachedRockMeshes.Add(CreateRockMesh());
            return s_cachedRockMeshes[Mathf.Abs(index) % s_cachedRockMeshes.Count];
        }

        public static void Warmup()
        {
            // 初回の発動時にシェーダー検索と岩メッシュ生成が固まらないように先に作る
            Shader.Find("Yurube/IwaCrater");
            Shader.Find("Yurube/IwaCrack");
            Shader.Find("Yurube/IwaShockwaveRing");
            GetRockMesh(0);
        }

        public static Mesh CreateRockMesh()
        {
            // 球を元に頂点をランダムにずらし、上下に潰した岩
            const int segments = 8;
            const int rings = 5;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            vertices.Add(new Vector3(0.0f, 0.55f, 0.0f));
            for (int ring = 1; ring < rings; ring++)
            {
                float v = ring / (float)rings;
                float phi = v * Mathf.PI;
                float y = Mathf.Cos(phi) * 0.55f;
                float r = Mathf.Sin(phi) * 0.65f;
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * Mathf.PI * 2.0f / segments;
                    float jitter = Random.Range(0.72f, 1.28f);
                    vertices.Add(new Vector3(
                        Mathf.Cos(angle) * r * jitter,
                        y * Random.Range(0.92f, 1.08f),
                        Mathf.Sin(angle) * r * jitter));
                }
            }
            vertices.Add(new Vector3(0.0f, -0.55f, 0.0f));

            int bottomIndex = vertices.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                triangles.Add(0); triangles.Add(1 + next); triangles.Add(1 + i);
            }
            for (int ring = 0; ring < rings - 2; ring++)
            {
                int a = 1 + ring * segments;
                int b = 1 + (ring + 1) * segments;
                for (int i = 0; i < segments; i++)
                {
                    int next = (i + 1) % segments;
                    triangles.Add(a + i); triangles.Add(a + next); triangles.Add(b + i);
                    triangles.Add(a + next); triangles.Add(b + next); triangles.Add(b + i);
                }
            }
            int lastRing = 1 + (rings - 2) * segments;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                triangles.Add(lastRing + i); triangles.Add(lastRing + next); triangles.Add(bottomIndex);
            }

            var mesh = new Mesh { name = "IwaRockMesh" };
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Update()
        {
            m_age += Time.deltaTime;
            float spawn = Mathf.SmoothStep(0.25f, 1.0f, Mathf.Clamp01(m_age / 0.22f));
            float fade = Mathf.Clamp01((m_lifetime - m_age) / m_fadeTime);
            transform.localScale = Vector3.one * Mathf.Min(spawn, Mathf.Lerp(0.05f, 1.0f, fade));

            if (m_material != null)
            {
                if (m_material.HasProperty("_Fade")) m_material.SetFloat("_Fade", fade);
                if (m_material.HasProperty("_EmissionStrength"))
                    m_material.SetFloat("_EmissionStrength",
                        Mathf.Lerp(m_emissionStrength, 0.0f, Mathf.Clamp01(m_age / 1.5f)));
            }

            for (int i = 0; i < m_rocks.Count; i++)
            {
                var rock = m_rocks[i];
                if (rock.Transform == null) continue;
                float rise = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(m_age / 0.3f));
                rock.Transform.localScale = rock.BaseScale * Mathf.Min(rise, fade);
            }

            if (m_age >= m_lifetime) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (m_material != null) Destroy(m_material);
            if (m_ownsMesh && m_mesh != null) Destroy(m_mesh);
            for (int i = 0; i < m_rockMaterials.Count; i++)
            {
                if (m_rockMaterials[i] != null) Destroy(m_rockMaterials[i]);
            }
            m_rockMaterials.Clear();

            for (int i = 0; i < m_decorations.Count; i++)
            {
                var decoration = m_decorations[i];
                if (decoration.Renderer != null) decoration.Renderer.enabled = decoration.Enabled;
                if (decoration.Transform != null) decoration.Transform.rotation = decoration.Rotation;
            }
            m_decorations.Clear();
        }
    }

    // 飛び散る岩片。即Destroyせず、最後に縮小しながら消す。
    public class IwaDebrisPiece : MonoBehaviour
    {
        Mesh m_mesh;
        Vector3 m_baseScale;
        float m_lifetime;
        float m_fadeTime;
        float m_age;
        bool m_destroyMesh;

        public void Begin(Mesh mesh, Vector3 baseScale, float lifetime, float fadeTime,
            bool destroyMesh = true)
        {
            m_mesh = mesh;
            m_destroyMesh = destroyMesh;
            m_baseScale = baseScale;
            m_lifetime = Mathf.Max(0.2f, lifetime);
            m_fadeTime = Mathf.Clamp(fadeTime, 0.1f, m_lifetime);
            m_age = 0.0f;
            transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            m_age += Time.deltaTime;
            float appear = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(m_age / 0.12f));
            float fade = Mathf.Clamp01((m_lifetime - m_age) / m_fadeTime);
            float scale = Mathf.Min(appear, fade);
            transform.localScale = m_baseScale * Mathf.Max(0.0f, scale);

            if (m_age >= m_lifetime) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (m_destroyMesh && m_mesh != null) Destroy(m_mesh);
        }
    }
}
