using System.Collections.Generic;
using UnityEngine;

namespace Yurube
{
    // いわだる着弾点から外へ伸びる地面の亀裂。
    // メッシュは発動時に1回だけ生成し、伸長はシェーダーの_Growで表現する。
    // 描画はクレーターより後、Depth Bias付きにしてカメラ角度で消えないようにする。
    public class IwaGroundCracks : MonoBehaviour
    {
        class Crack
        {
            public Mesh Mesh;
        }

        readonly List<Crack> m_cracks = new List<Crack>();
        readonly List<Mesh> m_meshes = new List<Mesh>();
        Material m_material;
        Vector3 m_center;
        float m_age;
        float m_lifetime;
        float m_fadeTime;

        public static void Spawn(Vector3 center, float radius, int mainCount,
            float lengthScale, float lifetime, float fadeTime)
        {
            var go = new GameObject("IwaGroundCracks");
            var cracks = go.AddComponent<IwaGroundCracks>();
            cracks.Initialize(center, radius, mainCount, lengthScale, lifetime, fadeTime);
        }

        void Initialize(Vector3 center, float radius, int mainCount,
            float lengthScale, float lifetime, float fadeTime)
        {
            m_center = center;
            m_lifetime = Mathf.Max(1.0f, lifetime);
            m_fadeTime = Mathf.Clamp(fadeTime, 0.1f, m_lifetime);

            Shader shader = Shader.Find("Yurube/IwaCrack");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return;

            m_material = new Material(shader);
            if (m_material.HasProperty("_Color"))
                m_material.SetColor("_Color", new Color(0.012f, 0.010f, 0.008f, 0.95f));
            if (m_material.HasProperty("_Grow")) m_material.SetFloat("_Grow", 0.0f);
            if (m_material.HasProperty("_Alpha")) m_material.SetFloat("_Alpha", 1.0f);

            mainCount = Mathf.Clamp(mainCount, 4, 6);
            float mainLength = Mathf.Max(1.0f, radius * lengthScale);
            float baseAngle = Random.Range(0.0f, Mathf.PI * 2.0f);
            for (int i = 0; i < mainCount; i++)
            {
                float angle = baseAngle + i * Mathf.PI * 2.0f / mainCount
                    + Random.Range(-0.45f, 0.45f);
                Vector3[] path = BuildZigzagPath(angle, mainLength, 6);
                AddCrack(path, radius * 0.055f);

                int branchCount = Random.Range(1, 4);
                for (int b = 0; b < branchCount; b++)
                {
                    int startIndex = Random.Range(2, Mathf.Max(3, path.Length - 1));
                    Vector3 origin = path[startIndex];
                    float branchAngle = angle + Random.Range(-1.1f, 1.1f);
                    float branchLength = mainLength * Random.Range(0.22f, 0.55f);
                    Vector3[] branch = BuildZigzagPathFrom(origin, branchAngle, branchLength, 3);
                    AddCrack(branch, radius * 0.030f);
                }
            }
        }

        Vector3[] BuildZigzagPath(float angle, float length, int segments)
        {
            var points = new Vector3[segments + 1];
            Vector3 current = new Vector3(m_center.x, m_center.y + 0.028f, m_center.z);
            points[0] = current;
            float step = length / segments;
            for (int i = 1; i <= segments; i++)
            {
                angle += Random.Range(-0.30f, 0.30f);
                current += new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * step;
                points[i] = current;
            }
            return points;
        }

        Vector3[] BuildZigzagPathFrom(Vector3 origin, float angle, float length, int segments)
        {
            var points = new Vector3[segments + 1];
            Vector3 current = origin + Vector3.up * 0.002f;
            points[0] = current;
            float step = length / segments;
            for (int i = 1; i <= segments; i++)
            {
                angle += Random.Range(-0.42f, 0.42f);
                current += new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * step;
                points[i] = current;
            }
            return points;
        }

        void AddCrack(Vector3[] targets, float width)
        {
            int count = targets.Length;
            var vertices = new Vector3[count * 2];
            var colors = new Color[count * 2];
            var uv = new Vector2[count * 2];
            var triangles = new int[(count - 1) * 6];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                Vector3 point = targets[i];
                Vector3 previous = i == 0 ? point : targets[i - 1];
                Vector3 direction = point - previous;
                direction.y = 0.0f;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
                direction.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;

                // 先端に向かって細くする
                float half = width * Mathf.Lerp(1.0f, 0.06f, t) * 0.5f;
                vertices[i * 2] = point - side * half;
                vertices[i * 2 + 1] = point + side * half;
                colors[i * 2] = Color.white;
                colors[i * 2 + 1] = Color.white;
                uv[i * 2] = new Vector2(0.0f, t);
                uv[i * 2 + 1] = new Vector2(1.0f, t);
            }

            for (int i = 0; i < count - 1; i++)
            {
                int v = i * 2;
                int index = i * 6;
                triangles[index] = v;
                triangles[index + 1] = v + 2;
                triangles[index + 2] = v + 1;
                triangles[index + 3] = v + 1;
                triangles[index + 4] = v + 2;
                triangles[index + 5] = v + 3;
            }

            var mesh = new Mesh { name = "IwaCrackMesh" };
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.uv = uv;
            mesh.RecalculateBounds();
            m_meshes.Add(mesh);

            var go = new GameObject("IwaCrack");
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (m_material != null) renderer.sharedMaterial = m_material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            m_cracks.Add(new Crack { Mesh = mesh });
        }

        private void Update()
        {
            m_age += Time.deltaTime;
            float grow = Mathf.Clamp01(m_age / 0.26f);
            grow = Mathf.SmoothStep(0.0f, 1.0f, grow);
            float fade = Mathf.Clamp01((m_lifetime - m_age) / m_fadeTime);

            if (m_material != null)
            {
                if (m_material.HasProperty("_Grow")) m_material.SetFloat("_Grow", grow);
                if (m_material.HasProperty("_Alpha")) m_material.SetFloat("_Alpha", fade);
            }

            if (m_age >= m_lifetime) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (m_material != null) Destroy(m_material);
            for (int i = 0; i < m_meshes.Count; i++)
            {
                if (m_meshes[i] != null) Destroy(m_meshes[i]);
            }
            m_meshes.Clear();
        }
    }
}
