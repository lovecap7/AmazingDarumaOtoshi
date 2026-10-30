using UnityEngine;
using UnityEngine.Rendering;

namespace Yurube
{
    // トロンの見えない当たり判定(BoxCollider)を、赤い半透明の箱で表示するデバッグ用。
    // F3 キー、またはキャラクターラボのトグルで表示を切り替える。
    public class TronHitboxView : MonoBehaviour
    {
        public static bool Visible { get; set; }

        BoxCollider m_box;
        GameObject m_visual;
        Material m_material;

        // 再生のたびにOFFから始める
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetVisible()
        {
            Visible = false;
        }

        public void Initialize(BoxCollider box)
        {
            m_box = box;
            CreateVisual();
        }

        void CreateVisual()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return;

            var color = new Color(1.0f, 0.1f, 0.1f, 0.25f);
            m_material = new Material(shader);
            if (m_material.HasProperty("_BaseColor")) m_material.SetColor("_BaseColor", color);
            if (m_material.HasProperty("_Color")) m_material.SetColor("_Color", color);

            m_visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_visual.name = "TronHitboxView";
            m_visual.transform.SetParent(transform, false);

            var collider = m_visual.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }

            var renderer = m_visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = m_material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            m_visual.SetActive(Visible);
        }

        private void Update()
        {
            if (m_box == null || m_visual == null) return;

            bool show = Visible && m_box.enabled;
            if (m_visual.activeSelf != show) m_visual.SetActive(show);
            if (!show) return;

            m_visual.transform.localPosition = m_box.center;
            m_visual.transform.localScale = m_box.size;
        }

        private void OnDestroy()
        {
            if (m_material != null) Destroy(m_material);
        }
    }
}
