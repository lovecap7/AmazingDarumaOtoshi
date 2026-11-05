using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // 毒沼デバフ中の敵に付ける視覚エフェクト。
    // 足元の紫緑の毒リング、体の周りの毒泡、頭上の「移動低下・反転」アイコンで状態を示す。
    public class HebiPoisonDebuffVisual : MonoBehaviour
    {
        class Bubble
        {
            public Transform Transform;
            public Vector3 BasePosition;
            public float Phase;
            public float Speed;
            public float Size;
        }

        static Texture2D s_ringTexture;
        static Texture2D s_bubbleTexture;
        static Texture2D s_iconTexture;

        DarumaCharacter m_target;
        Material m_ringMaterial;
        Material m_bubbleMaterial;
        Material m_iconMaterial;
        Transform m_ring;
        Transform m_icon;
        readonly List<Bubble> m_bubbles = new List<Bubble>();

        public static HebiPoisonDebuffVisual Attach(DarumaCharacter target)
        {
            if (target == null) return null;

            var go = new GameObject("HebiPoisonDebuffVisual");
            go.transform.SetParent(target.transform, false);
            var visual = go.AddComponent<HebiPoisonDebuffVisual>();
            visual.Initialize(target);
            return visual;
        }

        void Initialize(DarumaCharacter target)
        {
            m_target = target;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) return;

            m_ringMaterial = new Material(shader);
            m_ringMaterial.mainTexture = GetRingTexture();
            if (m_ringMaterial.HasProperty("_Color"))
                m_ringMaterial.SetColor("_Color", new Color(0.65f, 0.30f, 1.0f, 0.65f));

            m_bubbleMaterial = new Material(shader);
            m_bubbleMaterial.mainTexture = GetBubbleTexture();
            if (m_bubbleMaterial.HasProperty("_Color"))
                m_bubbleMaterial.SetColor("_Color", new Color(0.55f, 1.0f, 0.35f, 0.7f));

            m_iconMaterial = new Material(shader);
            m_iconMaterial.mainTexture = GetIconTexture();
            if (m_iconMaterial.HasProperty("_Color"))
                m_iconMaterial.SetColor("_Color", new Color(0.55f, 1.0f, 0.38f, 0.95f));

            m_ring = CreateQuad("DebuffRing", m_ringMaterial, new Vector3(0.0f, 0.03f, 0.0f),
                Quaternion.Euler(90.0f, 0.0f, 0.0f), 1.9f);
            m_icon = CreateQuad("DebuffIcon", m_iconMaterial, new Vector3(0.0f, 1.8f, 0.0f),
                Quaternion.identity, 0.75f);

            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI * 2.0f / 6.0f;
                var bubble = new Bubble
                {
                    BasePosition = new Vector3(Mathf.Cos(angle) * 0.55f, 0.25f,
                        Mathf.Sin(angle) * 0.55f),
                    Phase = i / 6.0f,
                    Speed = Random.Range(0.5f, 0.9f),
                    Size = Random.Range(0.16f, 0.30f),
                };
                var quad = CreateQuad($"DebuffBubble{i}", m_bubbleMaterial,
                    bubble.BasePosition, Quaternion.identity, bubble.Size);
                bubble.Transform = quad;
                m_bubbles.Add(bubble);
            }
        }

        Transform CreateQuad(string name, Material material, Vector3 localPosition,
            Quaternion localRotation, float scale)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            var collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = localPosition;
            quad.transform.localRotation = localRotation;
            quad.transform.localScale = new Vector3(scale, scale, 1.0f);

            var renderer = quad.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return quad.transform;
        }

        private void Update()
        {
            if (m_target == null)
            {
                Destroy(gameObject);
                return;
            }

            float time = Time.time;

            if (m_ring != null)
            {
                float pulse = 0.9f + 0.16f * Mathf.Sin(time * 6.0f);
                m_ring.localScale = new Vector3(1.9f * pulse, 1.9f * pulse, 1.0f);
                m_ring.localRotation = Quaternion.Euler(90.0f, time * 45.0f, 0.0f);
            }

            if (m_icon != null)
            {
                float height = m_target.Stack != null ? m_target.Stack.Height : 1.0f;
                m_icon.localPosition = new Vector3(0.0f, height + 0.55f, 0.0f);
                float pulse = 0.72f + 0.10f * Mathf.Sin(time * 8.0f);
                m_icon.localScale = new Vector3(pulse, pulse, 1.0f);
                m_icon.rotation = Camera.main != null
                    ? Camera.main.transform.rotation
                    : Quaternion.identity;
            }

            for (int i = 0; i < m_bubbles.Count; i++)
            {
                var bubble = m_bubbles[i];
                if (bubble.Transform == null) continue;

                float cycle = Mathf.Repeat(time * bubble.Speed + bubble.Phase, 1.0f);
                bubble.Transform.localPosition = new Vector3(
                    bubble.BasePosition.x,
                    0.15f + cycle * 1.4f,
                    bubble.BasePosition.z);
                float scale = bubble.Size * (0.6f + 0.5f * Mathf.Sin(cycle * Mathf.PI));
                bubble.Transform.localScale = new Vector3(scale, scale, 1.0f);
                bubble.Transform.rotation = Camera.main != null
                    ? Camera.main.transform.rotation
                    : Quaternion.identity;
            }
        }

        private void OnDestroy()
        {
            if (m_ringMaterial != null) Destroy(m_ringMaterial);
            if (m_bubbleMaterial != null) Destroy(m_bubbleMaterial);
            if (m_iconMaterial != null) Destroy(m_iconMaterial);
        }

        static Texture2D GetRingTexture()
        {
            if (s_ringTexture != null) return s_ringTexture;

            const int size = 128;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float max = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x, y), center) / max;
                    float ring = Mathf.Clamp01(1.0f - Mathf.Abs(r - 0.45f) / 0.055f);
                    float inner = Mathf.Clamp01(1.0f - Mathf.Abs(r - 0.30f) / 0.03f) * 0.35f;
                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)(Mathf.Clamp01(ring + inner) * 255.0f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_ringTexture = texture;
            return texture;
        }

        static Texture2D GetBubbleTexture()
        {
            if (s_bubbleTexture != null) return s_bubbleTexture;

            const int size = 64;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float max = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x, y), center) / max;
                    float alpha = Mathf.Clamp01(1.0f - Mathf.Abs(r - 0.42f) / 0.16f);
                    alpha += Mathf.Clamp01(1.0f - r) * 0.15f;
                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)(Mathf.Clamp01(alpha) * 255.0f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_bubbleTexture = texture;
            return texture;
        }

        static Texture2D GetIconTexture()
        {
            if (s_iconTexture != null) return s_iconTexture;

            const int size = 128;
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 0);

            Color32 white = new Color32(255, 255, 255, 255);
            // 下向き矢印(移動速度低下)
            FillRect(pixels, size, 58, 45, 12, 48, white);
            FillTriangle(pixels, size, 42, 47, 86, 47, 64, 8, white);
            // 左右反転を表す小さな矢印
            FillRect(pixels, size, 14, 92, 28, 7, white);
            FillTriangle(pixels, size, 14, 84, 14, 107, 3, 95, white);
            FillRect(pixels, size, 86, 92, 28, 7, white);
            FillTriangle(pixels, size, 114, 84, 114, 107, 125, 95, white);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_iconTexture = texture;
            return texture;
        }

        static void FillRect(Color32[] pixels, int size, int x, int y, int width, int height, Color32 color)
        {
            for (int py = y; py < y + height; py++)
            {
                if (py < 0 || py >= size) continue;
                for (int px = x; px < x + width; px++)
                {
                    if (px < 0 || px >= size) continue;
                    pixels[py * size + px] = color;
                }
            }
        }

        static void FillTriangle(Color32[] pixels, int size, int ax, int ay, int bx, int by,
            int cx, int cy, Color32 color)
        {
            int minX = Mathf.Clamp(Mathf.Min(ax, Mathf.Min(bx, cx)), 0, size - 1);
            int maxX = Mathf.Clamp(Mathf.Max(ax, Mathf.Max(bx, cx)), 0, size - 1);
            int minY = Mathf.Clamp(Mathf.Min(ay, Mathf.Min(by, cy)), 0, size - 1);
            int maxY = Mathf.Clamp(Mathf.Max(ay, Mathf.Max(by, cy)), 0, size - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float d1 = (x - bx) * (ay - by) - (ax - bx) * (y - by);
                    float d2 = (x - cx) * (by - cy) - (bx - cx) * (y - cy);
                    float d3 = (x - ax) * (cy - ay) - (cx - ax) * (y - ay);
                    bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
                    bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
                    if (!(hasNeg && hasPos)) pixels[y * size + x] = color;
                }
            }
        }
    }
}
