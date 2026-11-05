using UnityEngine;

namespace Yurube
{
    // 着弾直後に床面を広がる白〜薄黄色の衝撃波リング。
    // 1枚のクアッドとシェーダーで表現し、オブジェクトを再利用する。
    public class IwaShockwaveRing : MonoBehaviour
    {
        static IwaShockwaveRing s_instance;
        static Material s_material;

        Transform m_quad;
        float m_age;
        float m_duration;
        float m_stageRadius;
        float m_startRadius;
        float m_endRadius;

        public static void Warmup()
        {
            if (s_instance != null) return;
            var go = new GameObject("IwaShockwaveRing");
            s_instance = go.AddComponent<IwaShockwaveRing>();
            s_instance.Build();
            s_instance.gameObject.SetActive(false);
        }

        public static void Spawn(Vector3 center, float stageRadius,
            float startRadius, float endRadius, float duration)
        {
            if (stageRadius <= 0.01f) return;
            if (s_instance == null)
            {
                var go = new GameObject("IwaShockwaveRing");
                s_instance = go.AddComponent<IwaShockwaveRing>();
                s_instance.Build();
            }
            s_instance.transform.position = new Vector3(center.x, center.y + 0.03f, center.z);
            s_instance.m_age = 0.0f;
            s_instance.m_duration = Mathf.Max(0.1f, duration);
            s_instance.m_stageRadius = stageRadius;
            s_instance.m_startRadius = Mathf.Clamp(startRadius, 0.1f, stageRadius);
            s_instance.m_endRadius = Mathf.Clamp(endRadius, s_instance.m_startRadius, stageRadius);
            if (s_instance.m_quad != null)
                s_instance.m_quad.localScale = new Vector3(stageRadius * 2.0f, stageRadius * 2.0f, 1.0f);
            s_instance.gameObject.SetActive(true);
            s_instance.UpdateMaterial(0.0f);
        }

        void Build()
        {
            Shader shader = Shader.Find("Yurube/IwaShockwaveRing");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                s_material = new Material(shader);
                if (s_material.HasProperty("_Color"))
                    s_material.SetColor("_Color", new Color(1.0f, 0.97f, 0.80f, 1.0f));
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "IwaShockwaveQuad";
            var collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = Vector3.zero;
            quad.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            var renderer = quad.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (s_material != null) renderer.sharedMaterial = s_material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            m_quad = quad.transform;
        }

        void UpdateMaterial(float t)
        {
            if (s_material == null) return;

            float eased = 1.0f - Mathf.Pow(1.0f - t, 3.0f);
            float radius = Mathf.Lerp(m_startRadius, m_endRadius, eased);
            float normalizedRadius = Mathf.Clamp(radius / Mathf.Max(0.01f, m_stageRadius), 0.0f, 0.5f);
            // 外へ広がるほどリングを細く、アルファを0へ
            float thickness = Mathf.Lerp(0.085f, 0.012f, t);
            float alpha = 1.0f - t;

            if (s_material.HasProperty("_RingRadius")) s_material.SetFloat("_RingRadius", normalizedRadius);
            if (s_material.HasProperty("_Thickness")) s_material.SetFloat("_Thickness", thickness);
            if (s_material.HasProperty("_Alpha")) s_material.SetFloat("_Alpha", alpha);
        }

        private void Update()
        {
            if (!gameObject.activeSelf) return;
            m_age += Time.deltaTime;
            float t = Mathf.Clamp01(m_age / Mathf.Max(0.01f, m_duration));
            UpdateMaterial(t);
            if (m_quad != null)
                m_quad.localScale = new Vector3(m_stageRadius * 2.0f, m_stageRadius * 2.0f, 1.0f);

            if (m_age >= m_duration) gameObject.SetActive(false);
        }
    }
}
