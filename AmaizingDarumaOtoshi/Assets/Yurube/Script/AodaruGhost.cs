using UnityEngine;

namespace Yurube
{
    // あおだるの移動速度演出で出す「残像」1枚。
    // 色を少しずつ薄くして、寿命が来たら自分で消える。
    public class AodaruGhost : MonoBehaviour
    {
        static readonly int kBaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int kColorId = Shader.PropertyToID("_Color");

        MeshRenderer m_renderer;
        MaterialPropertyBlock m_property;
        Color m_color;
        float m_life = 0.25f;
        float m_time;

        public void Init(float life, Color color)
        {
            m_life = Mathf.Max(0.01f, life);
            m_color = color;
        }

        private void Awake()
        {
            m_renderer = GetComponent<MeshRenderer>();
            m_property = new MaterialPropertyBlock();
        }

        private void Update()
        {
            m_time += Time.deltaTime;
            float remain = 1.0f - m_time / m_life;
            if (remain <= 0.0f)
            {
                Destroy(gameObject);
                return;
            }

            if (m_renderer == null) return;

            Color color = m_color;
            color.a *= remain;
            m_property.SetColor(kBaseColorId, color);
            m_property.SetColor(kColorId, color);
            m_renderer.SetPropertyBlock(m_property);
        }
    }
}
