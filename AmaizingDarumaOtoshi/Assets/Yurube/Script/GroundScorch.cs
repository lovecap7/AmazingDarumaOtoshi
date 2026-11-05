using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // あかだると同じ Yurube/TronScorch シェーダーを使う地面の焦げ跡。
    // 円形（いわだるの着弾）と直線（だるきんぐの斬撃）の2種類を生成できる。
    public class GroundScorch : MonoBehaviour
    {
        const float kSpawnFade = 0.16f;

        static Material s_baseMaterial;
        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        float m_lifetime = 5.0f;
        float m_fadeTime = 2.5f;
        float m_emberTime = 0.8f;
        float m_opacity = 0.32f;
        float m_age;
        Material m_material;
        Mesh m_mesh;
        MeshRenderer m_renderer;

        public static GroundScorch SpawnCircle(Vector3 center, float radius,
            float lifetime, float fadeTime, Color charColor, Color emberColor, float opacity = 0.32f)
        {
            var go = new GameObject("GroundScorchCircle");
            var scorch = go.AddComponent<GroundScorch>();
            scorch.Initialize(lifetime, fadeTime, 0.8f, opacity);
            scorch.BuildCircle(center, radius, charColor, emberColor);
            return scorch;
        }

        public static GroundScorch SpawnLine(Vector3 start, Vector3 end, float width,
            float lifetime, float fadeTime, Color charColor, Color emberColor, float opacity = 0.32f)
        {
            var go = new GameObject("GroundScorchSlash");
            var scorch = go.AddComponent<GroundScorch>();
            scorch.Initialize(lifetime, fadeTime, 1.0f, opacity);
            scorch.BuildLine(start, end, width, charColor, emberColor);
            return scorch;
        }

        void Initialize(float lifetime, float fadeTime, float emberTime, float opacity)
        {
            m_lifetime = Mathf.Max(0.2f, lifetime);
            m_fadeTime = Mathf.Clamp(fadeTime, 0.05f, m_lifetime);
            m_emberTime = Mathf.Max(0.05f, emberTime);
            m_opacity = Mathf.Clamp01(opacity);
            m_age = 0.0f;

            m_mesh = new Mesh { name = "GroundScorchMesh" };
            m_mesh.MarkDynamic();
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = m_mesh;
            m_renderer = gameObject.AddComponent<MeshRenderer>();
            m_renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_renderer.receiveShadows = false;
            m_renderer.enabled = false;
        }

        Material CreateScorchMaterial(Color charColor, Color emberColor)
        {
            var baseMaterial = GetBaseMaterial();
            if (baseMaterial == null) return null;

            var material = new Material(baseMaterial);
            material.SetFloat("_FadeStart", Mathf.Max(0.0f, m_lifetime - m_fadeTime));
            material.SetFloat("_FadeTime", m_fadeTime);
            material.SetFloat("_EmberTime", m_emberTime);
            material.SetFloat("_Now", Time.time);
            material.SetFloat("_Opacity", 0.0f);
            material.SetFloat("_EmberStrength", 0.55f);
            material.SetFloat("_NoiseScale", 5.2f);
            material.SetFloat("_NoiseStrength", 0.72f);
            material.SetFloat("_EdgeSoftness", 0.38f);
            material.SetColor("_CharColor", charColor);
            material.SetColor("_EmberColor", emberColor);
            return material;
        }

        static Material GetBaseMaterial()
        {
            if (s_baseMaterial != null) return s_baseMaterial;

            s_baseMaterial = Resources.Load<Material>("TronScorch");
            if (s_baseMaterial == null)
            {
                var shader = Shader.Find("Yurube/TronScorch");
                if (shader != null) s_baseMaterial = new Material(shader);
            }
            return s_baseMaterial;
        }

        void BuildCircle(Vector3 center, float radius, Color charColor, Color emberColor)
        {
            radius = Mathf.Max(0.05f, radius);
            const int segments = 64;
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[segments + 1];
            var uv2 = new Vector2[segments + 1];
            var triangles = new int[segments * 3];

            vertices[0] = center + Vector3.up * 0.015f;
            uv[0] = new Vector2(0.5f, 0.5f);
            uv2[0] = new Vector2(Time.time, 0.0f);

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2.0f / segments;
                vertices[i + 1] = center + new Vector3(
                    Mathf.Cos(angle) * radius, 0.015f, Mathf.Sin(angle) * radius);
                uv[i + 1] = new Vector2(1.0f, 0.0f);
                uv2[i + 1] = new Vector2(Time.time, 0.0f);
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                triangles[i * 3 + 0] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = next + 1;
            }

            ApplyMesh(vertices, uv, uv2, triangles, charColor, emberColor);
        }

        void BuildLine(Vector3 start, Vector3 end, float width, Color charColor, Color emberColor)
        {
            const int segments = 16;
            width = Mathf.Max(0.05f, width);
            float half = width * 0.5f;

            var points = new Vector3[segments + 1];
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[(segments + 1) * 2];
            var uv2 = new Vector2[(segments + 1) * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                Vector3 point = Vector3.Lerp(start, end, i / (float)segments);
                if (TryGround(point, out Vector3 ground)) point = ground;
                points[i] = point + Vector3.up * 0.015f;
            }

            for (int i = 0; i <= segments; i++)
            {
                Vector3 previous = points[Mathf.Max(0, i - 1)];
                Vector3 next = points[Mathf.Min(segments, i + 1)];
                Vector3 direction = next - previous;
                direction.y = 0.0f;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
                direction.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;

                vertices[i * 2] = points[i] - side * half;
                vertices[i * 2 + 1] = points[i] + side * half;
                uv[i * 2] = new Vector2(0.0f, i);
                uv[i * 2 + 1] = new Vector2(1.0f, i);
                uv2[i * 2] = new Vector2(Time.time, 0.0f);
                uv2[i * 2 + 1] = new Vector2(Time.time, 0.0f);
            }

            for (int i = 0; i < segments; i++)
            {
                int v = i * 2;
                triangles[i * 6 + 0] = v;
                triangles[i * 6 + 1] = v + 2;
                triangles[i * 6 + 2] = v + 1;
                triangles[i * 6 + 3] = v + 1;
                triangles[i * 6 + 4] = v + 2;
                triangles[i * 6 + 5] = v + 3;
            }

            ApplyMesh(vertices, uv, uv2, triangles, charColor, emberColor);
        }

        bool TryGround(Vector3 point, out Vector3 ground)
        {
            ground = point;
            Vector3 origin = point + Vector3.up * 3.0f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, s_hits, 8.0f,
                ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = s_hits[i];
                if (hit.collider == null) continue;
                if (hit.normal.y < 0.5f) continue;
                if (hit.collider.GetComponentInParent<DarumaCharacter>() != null) continue;
                if (hit.collider.GetComponentInParent<TumikiBlock>() != null) continue;
                if (hit.collider.attachedRigidbody != null) continue;
                if (hit.distance >= best) continue;
                best = hit.distance;
                ground = hit.point;
            }
            return best < float.MaxValue;
        }

        void ApplyMesh(Vector3[] vertices, Vector2[] uv, Vector2[] uv2, int[] triangles,
            Color charColor, Color emberColor)
        {
            m_material = CreateScorchMaterial(charColor, emberColor);
            if (m_material == null)
            {
                Destroy(gameObject);
                return;
            }

            m_mesh.Clear();
            m_mesh.vertices = vertices;
            m_mesh.uv = uv;
            m_mesh.uv2 = uv2;
            m_mesh.triangles = triangles;
            m_mesh.RecalculateBounds();
            m_renderer.sharedMaterial = m_material;
            m_renderer.enabled = true;
        }

        private void Update()
        {
            if (m_material == null) return;

            m_age += Time.deltaTime;
            m_material.SetFloat("_Now", Time.time);
            float spawn = Mathf.Clamp01(m_age / kSpawnFade);
            m_material.SetFloat("_Opacity", m_opacity * spawn);

            if (m_age >= m_lifetime) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (m_material != null) Destroy(m_material);
            if (m_mesh != null) Destroy(m_mesh);
        }
    }
}
