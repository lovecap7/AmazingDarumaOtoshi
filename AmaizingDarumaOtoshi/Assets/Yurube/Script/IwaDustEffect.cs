using System.Collections.Generic;
using UnityEngine;

namespace Yurube
{
    // いわだる着弾時の土煙。中心から外へ広がり、ゆっくり薄くなる。
    public class IwaDustEffect : MonoBehaviour
    {
        class Puff
        {
            public Transform Transform;
            public Vector3 Direction;
            public float Speed;
            public float Size;
            public float Phase;
        }

        static Texture2D s_dustTexture;

        readonly List<Puff> m_puffs = new List<Puff>();
        Material m_material;
        Vector3 m_center;
        float m_radius;
        float m_age;

        public static void Spawn(Vector3 center, float radius)
        {
            var go = new GameObject("IwaDust");
            var effect = go.AddComponent<IwaDustEffect>();
            effect.Initialize(center, radius);
        }

        void Initialize(Vector3 center, float radius)
        {
            m_center = center;
            m_radius = Mathf.Max(1.0f, radius);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) return;

            m_material = new Material(shader);
            m_material.mainTexture = GetDustTexture();
            if (m_material.HasProperty("_Color"))
                m_material.SetColor("_Color", new Color(0.34f, 0.32f, 0.29f, 0.0f));

            for (int i = 0; i < 24; i++)
            {
                float angle = Random.Range(0.0f, Mathf.PI * 2.0f);
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"IwaDust{i}";
                var collider = quad.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                quad.transform.SetParent(transform, false);
                quad.transform.position = center + Vector3.up * 0.25f;
                quad.transform.rotation = Camera.main != null
                    ? Camera.main.transform.rotation
                    : Quaternion.identity;

                var renderer = quad.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = m_material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                m_puffs.Add(new Puff
                {
                    Transform = quad.transform,
                    Direction = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)),
                    Speed = Random.Range(0.55f, 1.0f),
                    Size = Random.Range(0.7f, 1.5f),
                    Phase = Random.Range(0.0f, 0.2f),
                });
            }
        }

        private void Update()
        {
            m_age += Time.deltaTime;
            const float life = 1.8f;
            float appear = Mathf.Clamp01(m_age / 0.12f);
            float fade = Mathf.Clamp01((life - m_age) / 1.0f);
            float alpha = Mathf.Min(appear, fade);

            if (m_material != null && m_material.HasProperty("_Color"))
                m_material.SetColor("_Color", new Color(0.34f, 0.32f, 0.29f, 0.30f * alpha));

            for (int i = 0; i < m_puffs.Count; i++)
            {
                var puff = m_puffs[i];
                if (puff.Transform == null) continue;
                float t = Mathf.Clamp01((m_age + puff.Phase) / life);
                float distance = m_radius * t * puff.Speed;
                puff.Transform.position = m_center
                    + puff.Direction * distance
                    + Vector3.up * (0.15f + t * 0.65f);
                float scale = m_radius * 0.20f * puff.Size * (0.35f + t * 1.2f);
                puff.Transform.localScale = new Vector3(scale, scale, 1.0f);
                puff.Transform.rotation = Camera.main != null
                    ? Camera.main.transform.rotation
                    : Quaternion.identity;
            }

            if (m_age >= life) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (m_material != null) Destroy(m_material);
        }

        static Texture2D GetDustTexture()
        {
            if (s_dustTexture != null) return s_dustTexture;

            const int size = 128;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float max = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x, y), center) / max;
                    float n = Mathf.PerlinNoise(x * 0.09f, y * 0.09f);
                    float alpha = Mathf.Clamp01(1.0f - r) * (0.55f + n * 0.45f);
                    alpha = Mathf.Pow(Mathf.Clamp01(alpha), 1.6f);
                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)(alpha * 255.0f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_dustTexture = texture;
            return texture;
        }
    }
}
