using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // へびだる必殺技「反転毒沼」のフィールド効果。
    // 表示している円と当たり判定は同じ m_radius を使う。
    // 沼に入っている敵には HebiPoisonDebuff を付与し、沼から出てもデバフ時間は残る。
    public class HebiPoisonSwamp : MonoBehaviour
    {
        class Bubble
        {
            public Transform Transform;
            public Vector3 BasePosition;
            public float Speed;
            public float Phase;
            public float Size;
        }

        class Ripple
        {
            public Transform Transform;
            public float Phase;
        }

        static Texture2D s_swampTexture;
        static Texture2D s_ringTexture;
        static Texture2D s_bubbleTexture;
        static readonly RaycastHit[] s_groundHits = new RaycastHit[16];

        DarumaCharacter m_owner;
        float m_radius = 4.5f;
        float m_duration = 8.0f;
        float m_fadeDuration = 0.7f;
        float m_debuffDuration = 3.0f;
        float m_slowMultiplier = 0.55f;
        float m_tickInterval = 0.15f;
        bool m_followCaster;
        Vector3 m_centerOffset;
        float m_timeLeft;
        float m_tickTimer;
        bool m_finished;
        bool m_fading;
        float m_fadeTime;
        float m_fadeScale = 1.0f;
        float m_fadeAlpha = 1.0f;

        Vector3 m_center;
        float m_groundY;

        GameObject m_disc;
        GameObject m_boundary;
        LineRenderer m_boundaryLine;
        Material m_boundaryLineMaterial;
        CircleHitboxView m_hitboxView;
        Material m_discMaterial;
        Material m_boundaryMaterial;
        Material m_bubbleMaterial;
        Material m_rippleMaterial;

        readonly List<Bubble> m_bubbles = new List<Bubble>();
        readonly List<Ripple> m_ripples = new List<Ripple>();
        readonly List<DarumaCharacter> m_affected = new List<DarumaCharacter>();
        readonly List<GameObject> m_created = new List<GameObject>();

        public event Action Finished;

        // ---- CPU が毒沼を避けるための読み取り用(中平追加。動きには関係しない) ----
        // 沼を出したキャラクター(この人にはデバフが掛からない)
        public DarumaCharacter Owner => m_owner;
        // 当たり判定の半径(m)。中心は transform.position
        public float Radius => m_radius;
        // デバフを掛けている最中か(消えかけ・消えたあとは false)
        public bool IsEffective => !m_finished && !m_fading;

        public void Initialize(DarumaCharacter owner, float radius, float duration, float fadeDuration,
            float debuffDuration, float slowMultiplier, float tickInterval,
            bool followCaster, Vector3 centerOffset)
        {
            m_owner = owner;
            m_radius = Mathf.Max(0.5f, radius);
            m_duration = Mathf.Max(0.1f, duration);
            m_fadeDuration = Mathf.Max(0.0f, fadeDuration);
            m_debuffDuration = Mathf.Max(0.1f, debuffDuration);
            m_slowMultiplier = Mathf.Clamp01(slowMultiplier);
            m_tickInterval = Mathf.Max(0.02f, tickInterval);
            m_followCaster = followCaster;
            m_centerOffset = centerOffset;
            m_timeLeft = m_duration;
            m_tickTimer = 0.0f;

            UpdateCenter(true);
            CreateVisuals();
            m_hitboxView = CircleHitboxView.Create("HebiSwampHitbox", transform,
                () => transform.position, () => m_radius, () => !m_fading,
                new Color(0.55f, 1.0f, 0.30f, 0.95f));
        }

        private void Update()
        {
            if (m_finished) return;

            if (m_owner == null)
            {
                ClearAndDestroy(true);
                return;
            }

            if (m_followCaster) UpdateCenter(false);

            if (m_fading)
            {
                m_fadeTime += Time.deltaTime;
                UpdateFadeValues();
                AnimateVisuals();
                if (m_fadeTime >= m_fadeDuration) ClearAndDestroy(false);
                return;
            }

            AnimateVisuals();
            m_timeLeft -= Time.deltaTime;
            if (m_timeLeft <= 0.0f)
            {
                m_fading = true;
                m_fadeTime = 0.0f;
                UpdateFadeValues();
                AnimateVisuals();
                if (m_fadeDuration <= 0.001f) ClearAndDestroy(false);
                return;
            }

            m_tickTimer += Time.deltaTime;
            if (m_tickTimer >= m_tickInterval)
            {
                m_tickTimer = 0.0f;
                ApplyDebuffToEnemies();
            }
        }

        void UpdateFadeValues()
        {
            float t = m_fadeDuration <= 0.001f
                ? 1.0f
                : Mathf.Clamp01(m_fadeTime / m_fadeDuration);
            float eased = Mathf.SmoothStep(0.0f, 1.0f, t);
            m_fadeScale = Mathf.Lerp(1.0f, 0.18f, eased);
            m_fadeAlpha = 1.0f - eased;
        }

        void UpdateCenter(bool force)
        {
            if (m_owner == null) return;

            Vector3 position = m_owner.transform.position + m_centerOffset;
            Vector3 origin = position + Vector3.up * 2.0f;
            float ground = position.y;
            int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, s_groundHits, 8.0f,
                ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                var hit = s_groundHits[i];
                if (hit.collider == null) continue;
                if (hit.collider.GetComponentInParent<DarumaCharacter>() != null) continue;
                if (hit.collider.GetComponentInParent<TumikiBlock>() != null) continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    ground = hit.point.y;
                }
            }

            m_center = new Vector3(position.x, ground + 0.035f, position.z);
            m_groundY = ground;
            transform.position = m_center;

            if (force) return;

            // 追従時は子の基準位置も中心に合わせる
            if (m_disc != null) m_disc.transform.position = m_center;
            if (m_boundary != null) m_boundary.transform.position = m_center + Vector3.up * 0.01f;
        }

        void ApplyDebuffToEnemies()
        {
            foreach (var character in FindObjectsByType<DarumaCharacter>(FindObjectsInactive.Exclude))
            {
                if (character == null || character == m_owner) continue;

                Vector3 delta = character.transform.position - transform.position;
                delta.y = 0.0f;
                if (delta.magnitude > m_radius) continue;

                HebiPoisonDebuff.Apply(character, m_debuffDuration, m_slowMultiplier);
                if (!m_affected.Contains(character)) m_affected.Add(character);
            }
        }

        void CreateVisuals()
        {
            Shader spriteShader = Shader.Find("Sprites/Default");
            if (spriteShader == null) spriteShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (spriteShader == null) spriteShader = Shader.Find("Unlit/Texture");

            if (spriteShader != null)
            {
                m_discMaterial = new Material(spriteShader);
                m_discMaterial.mainTexture = GetSwampTexture();
                if (m_discMaterial.HasProperty("_Color"))
                    m_discMaterial.SetColor("_Color", new Color(1.0f, 1.0f, 1.0f, 1.0f));

                m_boundaryMaterial = new Material(spriteShader);
                m_boundaryMaterial.mainTexture = GetRingTexture();
                if (m_boundaryMaterial.HasProperty("_Color"))
                    m_boundaryMaterial.SetColor("_Color", new Color(0.55f, 1.0f, 0.35f, 0.85f));

                m_bubbleMaterial = new Material(spriteShader);
                m_bubbleMaterial.mainTexture = GetBubbleTexture();
                if (m_bubbleMaterial.HasProperty("_Color"))
                    m_bubbleMaterial.SetColor("_Color", new Color(0.55f, 1.0f, 0.45f, 0.8f));

                m_rippleMaterial = new Material(spriteShader);
                m_rippleMaterial.mainTexture = GetRingTexture();
                if (m_rippleMaterial.HasProperty("_Color"))
                    m_rippleMaterial.SetColor("_Color", new Color(0.7f, 0.45f, 1.0f, 0.55f));
            }

            m_disc = CreateQuad("HebiPoisonSurface", m_discMaterial,
                m_center, m_radius * 2.0f, Quaternion.Euler(90.0f, 0.0f, 0.0f));
            m_boundary = CreateQuad("HebiPoisonBoundary", m_boundaryMaterial,
                m_center + Vector3.up * 0.01f, m_radius * 2.0f, Quaternion.Euler(90.0f, 0.0f, 0.0f));

            CreateBoundaryLine();
            CreateBubbles();
            CreateRipples();
        }

        void CreateBoundaryLine()
        {
            var lineObject = new GameObject("HebiPoisonBoundaryLine");
            lineObject.transform.SetParent(transform, false);
            m_boundaryLine = lineObject.AddComponent<LineRenderer>();
            m_boundaryLine.useWorldSpace = true;
            m_boundaryLine.loop = true;
            m_boundaryLine.positionCount = 64;
            m_boundaryLine.widthMultiplier = 0.12f;
            m_boundaryLine.numCornerVertices = 2;
            m_boundaryLine.numCapVertices = 2;
            m_boundaryLine.textureMode = LineTextureMode.Stretch;
            m_boundaryLine.alignment = LineAlignment.View;
            m_boundaryLine.startColor = new Color(0.55f, 1.0f, 0.35f, 0.9f);
            m_boundaryLine.endColor = new Color(0.55f, 1.0f, 0.35f, 0.9f);
            m_boundaryLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_boundaryLine.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                m_boundaryLineMaterial = new Material(shader);
                if (m_boundaryLineMaterial.HasProperty("_Color"))
                    m_boundaryLineMaterial.SetColor("_Color", new Color(0.55f, 1.0f, 0.35f, 0.9f));
                if (m_boundaryLineMaterial.HasProperty("_BaseColor"))
                    m_boundaryLineMaterial.SetColor("_BaseColor", new Color(0.55f, 1.0f, 0.35f, 0.9f));
                m_boundaryLine.sharedMaterial = m_boundaryLineMaterial;
            }

            m_created.Add(lineObject);
            UpdateBoundaryLine(Time.time);
        }

        void UpdateBoundaryLine(float time)
        {
            if (m_boundaryLine == null) return;
            Vector3 center = transform.position + Vector3.up * 0.075f;
            float radius = m_radius * m_fadeScale;
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2.0f / 64.0f;
                m_boundaryLine.SetPosition(i, center + new Vector3(
                    Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius));
            }
            float pulse = 0.55f + 0.30f * (0.5f + 0.5f * Mathf.Sin(time * 4.0f));
            Color color = new Color(0.55f, 1.0f, 0.35f, pulse * m_fadeAlpha);
            m_boundaryLine.startColor = color;
            m_boundaryLine.endColor = color;
        }

        GameObject CreateQuad(string name, Material material, Vector3 position, float scale, Quaternion rotation)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            var collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            quad.transform.SetParent(transform, false);
            quad.transform.position = position;
            quad.transform.rotation = rotation;
            quad.transform.localScale = new Vector3(scale, scale, 1.0f);

            var renderer = quad.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (material != null) renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            m_created.Add(quad);
            return quad;
        }

        void CreateBubbles()
        {
            for (int i = 0; i < 18; i++)
            {
                float angle = UnityEngine.Random.Range(0.0f, Mathf.PI * 2.0f);
                float distance = m_radius * UnityEngine.Random.Range(0.15f, 0.82f);
                var bubble = new Bubble
                {
                    BasePosition = new Vector3(
                        Mathf.Cos(angle) * distance,
                        0.06f,
                        Mathf.Sin(angle) * distance),
                    Speed = UnityEngine.Random.Range(0.35f, 0.9f),
                    Phase = UnityEngine.Random.Range(0.0f, 1.0f),
                    Size = UnityEngine.Random.Range(0.12f, 0.34f),
                };

                var quad = CreateQuad($"HebiPoisonBubble{i}", m_bubbleMaterial,
                    transform.position, bubble.Size, Quaternion.identity);
                quad.transform.position = transform.position + bubble.BasePosition;
                bubble.Transform = quad.transform;
                m_bubbles.Add(bubble);
            }
        }

        void CreateRipples()
        {
            for (int i = 0; i < 3; i++)
            {
                var quad = CreateQuad($"HebiPoisonRipple{i}", m_rippleMaterial,
                    m_center + Vector3.up * 0.015f, 0.5f, Quaternion.Euler(90.0f, 0.0f, 0.0f));
                var ripple = new Ripple { Transform = quad.transform, Phase = i / 3.0f };
                m_ripples.Add(ripple);
            }
        }

        void AnimateVisuals()
        {
            float time = Time.time;

            if (m_disc != null)
            {
                m_disc.transform.rotation = Quaternion.Euler(90.0f, time * 7.0f, 0.0f);
                m_disc.transform.localScale = new Vector3(
                    m_radius * 2.0f * m_fadeScale,
                    m_radius * 2.0f * m_fadeScale,
                    1.0f);
            }
            if (m_discMaterial != null && m_discMaterial.HasProperty("_Color"))
                m_discMaterial.SetColor("_Color", new Color(1.0f, 1.0f, 1.0f, m_fadeAlpha));

            if (m_boundary != null)
            {
                float pulse = 0.62f + 0.18f * Mathf.Sin(time * 4.0f);
                m_boundary.transform.localScale = new Vector3(
                    m_radius * 2.0f * m_fadeScale,
                    m_radius * 2.0f * m_fadeScale,
                    1.0f);
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", new Color(0.55f, 1.0f, 0.35f, pulse * m_fadeAlpha));
                m_boundary.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            }

            UpdateBoundaryLine(time);

            if (m_bubbleMaterial != null && m_bubbleMaterial.HasProperty("_Color"))
                m_bubbleMaterial.SetColor("_Color", new Color(0.55f, 1.0f, 0.45f, 0.8f * m_fadeAlpha));

            for (int i = 0; i < m_bubbles.Count; i++)
            {
                var bubble = m_bubbles[i];
                if (bubble.Transform == null) continue;

                float cycle = Mathf.Repeat(time * bubble.Speed + bubble.Phase, 1.0f);
                float scale = bubble.Size * (0.7f + 0.5f * Mathf.Sin(cycle * Mathf.PI)) * m_fadeScale;
                bubble.Transform.position = transform.position + new Vector3(
                    bubble.BasePosition.x * m_fadeScale,
                    0.06f + cycle * 0.55f,
                    bubble.BasePosition.z * m_fadeScale);
                bubble.Transform.localScale = new Vector3(scale, scale, 1.0f);
                bubble.Transform.rotation = Camera.main != null
                    ? Camera.main.transform.rotation
                    : Quaternion.identity;
            }

            for (int i = 0; i < m_ripples.Count; i++)
            {
                var ripple = m_ripples[i];
                if (ripple.Transform == null) continue;

                float cycle = Mathf.Repeat(time * 0.45f + ripple.Phase, 1.0f);
                float scale = m_radius * (0.25f + cycle * 0.68f) * m_fadeScale;
                ripple.Transform.position = m_center + Vector3.up * 0.015f;
                ripple.Transform.localScale = new Vector3(scale, scale, 1.0f);

                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", new Color(0.7f, 0.45f, 1.0f,
                    (1.0f - cycle) * 0.45f * m_fadeAlpha));
                ripple.Transform.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            }
        }

        public void ClearAndDestroy(bool clearDebuffs)
        {
            if (m_finished) return;

            if (clearDebuffs)
            {
                for (int i = 0; i < m_affected.Count; i++)
                {
                    var target = m_affected[i];
                    if (target != null) HebiPoisonDebuff.Clear(target);
                }
            }

            DestroySwamp();
        }

        void DestroySwamp()
        {
            if (m_finished) return;
            m_finished = true;

            Finished?.Invoke();

            for (int i = 0; i < m_created.Count; i++)
            {
                if (m_created[i] != null) Destroy(m_created[i]);
            }
            m_created.Clear();
            m_bubbles.Clear();
            m_ripples.Clear();
            m_affected.Clear();

            if (m_discMaterial != null) Destroy(m_discMaterial);
            if (m_boundaryMaterial != null) Destroy(m_boundaryMaterial);
            if (m_boundaryLineMaterial != null) Destroy(m_boundaryLineMaterial);
            if (m_bubbleMaterial != null) Destroy(m_bubbleMaterial);
            if (m_rippleMaterial != null) Destroy(m_rippleMaterial);
            if (m_hitboxView != null) Destroy(m_hitboxView.gameObject);

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (m_finished) return;
            m_finished = true;
            Finished?.Invoke();

            for (int i = 0; i < m_created.Count; i++)
            {
                if (m_created[i] != null) Destroy(m_created[i]);
            }
            m_created.Clear();
            m_bubbles.Clear();
            m_ripples.Clear();
            m_affected.Clear();

            if (m_discMaterial != null) Destroy(m_discMaterial);
            if (m_boundaryMaterial != null) Destroy(m_boundaryMaterial);
            if (m_boundaryLineMaterial != null) Destroy(m_boundaryLineMaterial);
            if (m_bubbleMaterial != null) Destroy(m_bubbleMaterial);
            if (m_rippleMaterial != null) Destroy(m_rippleMaterial);
            if (m_hitboxView != null) Destroy(m_hitboxView.gameObject);
        }

        static Texture2D GetSwampTexture()
        {
            if (s_swampTexture != null) return s_swampTexture;

            const int size = 512;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float max = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 d = new Vector2(x, y) - center;
                    float r = d.magnitude / max;
                    if (r >= 1.0f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    float angle = Mathf.Atan2(d.y, d.x);
                    float n = Mathf.PerlinNoise(x * 0.018f, y * 0.018f);
                    float swirl = Mathf.Sin(angle * 3.0f + r * 26.0f + n * 4.0f) * 0.5f + 0.5f;
                    float green = Mathf.Lerp(0.20f, 0.52f, swirl);
                    float purple = Mathf.Lerp(0.22f, 0.42f, 1.0f - swirl);
                    float alpha = Mathf.Clamp01((1.0f - r) / 0.10f) * 0.78f;

                    // 毒の泡
                    float bubble = Mathf.Sin(x * 0.13f + n * 8.0f) * Mathf.Sin(y * 0.11f - n * 6.0f);
                    if (bubble > 0.72f)
                    {
                        green += 0.20f;
                        purple += 0.10f;
                        alpha += 0.10f;
                    }

                    pixels[y * size + x] = new Color(green * 0.45f, green, purple * 0.75f, alpha);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_swampTexture = texture;
            return texture;
        }

        static Texture2D GetRingTexture()
        {
            if (s_ringTexture != null) return s_ringTexture;

            const int size = 256;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float max = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x, y), center) / max;
                    float ring = Mathf.Clamp01(1.0f - Mathf.Abs(r - 0.98f) / 0.02f);
                    float inner = Mathf.Clamp01(1.0f - Mathf.Abs(r - 0.82f) / 0.025f) * 0.25f;
                    float alpha = Mathf.Clamp01(ring + inner);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255.0f));
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
                    float alpha = Mathf.Clamp01(1.0f - Mathf.Abs(r - 0.42f) / 0.13f);
                    alpha += Mathf.Clamp01(1.0f - r) * 0.18f;
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
    }
}
