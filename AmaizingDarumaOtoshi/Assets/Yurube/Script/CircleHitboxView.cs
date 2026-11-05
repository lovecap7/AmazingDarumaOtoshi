using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yurube
{
    // 必殺技の実際の攻撃半径を、地面のワールド空間リングで表示するデバッグ用。
    // 見た目のエフェクトとは別に、判定に使っている中心と半径をそのまま描く。
    public class CircleHitboxView : MonoBehaviour
    {
        Func<Vector3> m_center;
        Func<float> m_radius;
        Func<bool> m_active;
        LineRenderer m_line;
        Material m_material;

        public static CircleHitboxView Create(string name, Transform parent,
            Func<Vector3> center, Func<float> radius, Func<bool> active, Color color)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var view = go.AddComponent<CircleHitboxView>();
            view.m_center = center;
            view.m_radius = radius;
            view.m_active = active;
            view.Build(color);
            return view;
        }

        void Build(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            m_material = shader != null ? new Material(shader) : null;
            if (m_material != null)
            {
                if (m_material.HasProperty("_Color")) m_material.SetColor("_Color", color);
                if (m_material.HasProperty("_BaseColor")) m_material.SetColor("_BaseColor", color);
            }

            m_line = gameObject.AddComponent<LineRenderer>();
            m_line.useWorldSpace = true;
            m_line.loop = true;
            m_line.positionCount = 64;
            m_line.widthMultiplier = 0.11f;
            m_line.numCornerVertices = 2;
            m_line.numCapVertices = 2;
            m_line.textureMode = LineTextureMode.Stretch;
            m_line.alignment = LineAlignment.View;
            m_line.startColor = color;
            m_line.endColor = color;
            m_line.shadowCastingMode = ShadowCastingMode.Off;
            m_line.receiveShadows = false;
            if (m_material != null) m_line.sharedMaterial = m_material;
            m_line.enabled = false;
        }

        private void Update()
        {
            if (m_line == null) return;

            bool show = SpecialHitboxDebug.Visible
                && m_center != null
                && m_radius != null
                && (m_active == null || m_active());
            if (!show)
            {
                if (m_line.enabled) m_line.enabled = false;
                return;
            }

            Vector3 center = m_center();
            float radius = Mathf.Max(0.0f, m_radius());
            if (radius <= 0.01f)
            {
                m_line.enabled = false;
                return;
            }

            m_line.enabled = true;
            center.y += 0.07f;
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2.0f / 64.0f;
                m_line.SetPosition(i, center + new Vector3(
                    Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius));
            }
        }

        private void OnDestroy()
        {
            if (m_material != null) Destroy(m_material);
        }
    }
}
