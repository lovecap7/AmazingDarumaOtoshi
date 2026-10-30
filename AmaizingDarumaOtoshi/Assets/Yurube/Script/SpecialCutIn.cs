using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // あかだる必殺技「終焉の軌跡」の発動カットイン。
    // 以前の積み木・ハンマー・レーザー・破片の演出は使わず、
    // 発動瞬間の顔アップ、赤黒い集中線、衝撃波、画面揺れ、フラッシュ、
    // 画面下中央の大型技名だけで構成する。
    public class SpecialCutIn : MonoBehaviour
    {
        const float Duration = 1.3f;
        const int CutInLayer = 30;

        static SpecialCutIn s_instance;
        static Font s_font;
        static Texture2D s_speedTexture;
        static Texture2D s_ringTexture;

        string m_specialName;
        Color m_accent;
        float m_start;
        bool m_active;
        float m_previousTimeScale = 1.0f;

        GameObject m_stage;
        Camera m_camera;
        RenderTexture m_renderTexture;
        Transform m_head;
        GameObject m_backdrop;
        Material m_backdropMaterial;
        Material m_shockwaveMaterial;

        readonly List<GameObject> m_createdObjects = new List<GameObject>();
        readonly List<GameObject> m_shockwaves = new List<GameObject>();
        readonly List<float> m_shockwaveStarts = new List<float>();
        readonly List<float> m_shockwaveMaxScales = new List<float>();
        readonly List<Color> m_shockwaveColors = new List<Color>();
        bool m_shockA;
        bool m_shockB;
        bool m_shockC;

        GUIStyle m_titleStyle;
        bool m_stylesReady;

        public static void Show(string specialName, Color accent, Material effectMaterial,
                                DarumaCharacter character, TumikiBlock extraBlock, int blockCount)
        {
            if (s_instance == null)
            {
                var go = new GameObject("SpecialCutIn");
                s_instance = go.AddComponent<SpecialCutIn>();
            }

            s_instance.Play(specialName, accent, character);
        }

        private void Awake()
        {
            s_instance = this;
        }

        private void OnDisable()
        {
            End();
        }

        private void OnDestroy()
        {
            End();
            if (s_instance == this) s_instance = null;
        }

        void Play(string specialName, Color accent, DarumaCharacter character)
        {
            if (!m_active)
            {
                m_previousTimeScale = Time.timeScale;
                Time.timeScale = 0.0f;
            }
            else
            {
                CleanupStage();
            }

            m_specialName = specialName;
            m_accent = accent;
            m_start = Time.unscaledTime;
            m_active = true;
            m_shockA = false;
            m_shockB = false;
            m_shockC = false;

            BuildStage(character);
        }

        private void Update()
        {
            if (!m_active) return;

            float age = Time.unscaledTime - m_start;
            if (age > Duration)
            {
                End();
                return;
            }

            Animate(age);
        }

        void End()
        {
            if (!m_active) return;

            m_active = false;
            CleanupStage();
            Time.timeScale = m_previousTimeScale;
        }

        void BuildStage(DarumaCharacter character)
        {
            m_stage = new GameObject("SpecialCutInStage");
            m_stage.transform.position = new Vector3(10000.0f, 10000.0f, 10000.0f);

            m_renderTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            m_renderTexture.Create();

            var cameraObject = new GameObject("CutInCamera");
            cameraObject.transform.SetParent(m_stage.transform, false);
            cameraObject.transform.localPosition = new Vector3(0.0f, 1.7f, -3.0f);

            m_camera = cameraObject.AddComponent<Camera>();
            m_camera.orthographic = false;
            m_camera.fieldOfView = 50.0f;
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = new Color(0.02f, 0.002f, 0.002f, 1.0f);
            m_camera.cullingMask = 1 << CutInLayer;
            m_camera.nearClipPlane = 0.05f;
            m_camera.farClipPlane = 40.0f;
            m_camera.targetTexture = m_renderTexture;
            m_camera.enabled = true;
            cameraObject.transform.LookAt(m_stage.transform.TransformPoint(new Vector3(0.0f, 1.6f, 0.0f)));

            CreateLight("CutInKey", new Vector3(2.0f, 3.5f, -2.0f),
                Quaternion.Euler(36.0f, -28.0f, 0.0f), new Color(1.0f, 0.9f, 0.8f), 1.8f);
            CreateLight("CutInRim", new Vector3(-2.0f, 2.0f, -1.5f),
                Quaternion.identity, m_accent, 9.0f);

            CreateBackdrop();
            CreateShockwaveMaterial();

            if (character == null) return;

            Transform headSource = FindPart(character, "Head");
            if (headSource == null) return;

            var head = CreateClone(headSource.gameObject, "CutInHead");
            m_head = head.transform;
            m_head.localScale = Vector3.one * 3.2f;
            m_head.localPosition = new Vector3(0.0f, 1.6f, 0.0f);
            m_head.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
            m_createdObjects.Add(head);
        }

        void CreateBackdrop()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "CutInConcentrationLines";
            var collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            quad.transform.SetParent(m_stage.transform, false);
            quad.transform.position = m_camera.transform.position + m_camera.transform.forward * 12.0f;
            quad.transform.rotation = m_camera.transform.rotation;
            quad.transform.localScale = new Vector3(20.0f, 12.0f, 1.0f);
            quad.layer = CutInLayer;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader != null)
            {
                m_backdropMaterial = new Material(shader);
                m_backdropMaterial.mainTexture = GetSpeedTexture();
                if (m_backdropMaterial.HasProperty("_Color"))
                    m_backdropMaterial.SetColor("_Color", Color.white);
                quad.GetComponent<MeshRenderer>().sharedMaterial = m_backdropMaterial;
            }

            var renderer = quad.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            m_backdrop = quad;
            m_createdObjects.Add(quad);
        }

        void CreateShockwaveMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;

            m_shockwaveMaterial = new Material(shader);
            m_shockwaveMaterial.mainTexture = GetRingTexture();
            if (m_shockwaveMaterial.HasProperty("_Color"))
                m_shockwaveMaterial.SetColor("_Color", Color.white);
        }

        void CreateLight(string name, Vector3 position, Quaternion rotation, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_stage.transform, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = 8.0f;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << CutInLayer;
        }

        GameObject CreateClone(GameObject source, string name)
        {
            var clone = Instantiate(source, m_stage.transform, false);
            clone.name = name;
            clone.SetActive(false);

            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;

            foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true))
            {
                body.detectCollisions = false;
                body.useGravity = false;
                body.isKinematic = true;
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            foreach (var particles in clone.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
                if (particleRenderer != null) particleRenderer.enabled = false;
            }

            foreach (var trail in clone.GetComponentsInChildren<TrailRenderer>(true))
                trail.enabled = false;

            foreach (var mesh in clone.GetComponentsInChildren<MeshRenderer>(true))
                mesh.enabled = true;
            foreach (var skinned in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skinned.enabled = true;

            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = CutInLayer;

            clone.SetActive(true);
            return clone;
        }

        static Transform FindPart(DarumaCharacter character, string name)
        {
            foreach (var t in character.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }

        void Animate(float age)
        {
            float punch = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(age / 0.65f));

            if (m_camera != null && m_stage != null)
            {
                Vector3 position = new Vector3(0.0f, 1.7f,
                    Mathf.Lerp(-3.0f, -2.3f, punch));
                m_camera.fieldOfView = Mathf.Lerp(50.0f, 36.0f, punch);

                float shake = Mathf.Max(Pulse(age, 0.05f, 0.12f),
                    Mathf.Max(Pulse(age, 0.45f, 0.14f), Pulse(age, 0.85f, 0.18f))) * 0.055f;
                if (shake > 0.0f)
                {
                    position.x += Mathf.Sin(age * 180.0f) * shake;
                    position.y += Mathf.Cos(age * 230.0f) * shake;
                }

                m_camera.transform.localPosition = position;
                m_camera.transform.LookAt(m_stage.transform.TransformPoint(new Vector3(0.0f, 1.6f, 0.0f)));
            }

            if (m_backdrop != null)
            {
                m_backdrop.transform.position = m_camera.transform.position
                    + m_camera.transform.forward * 12.0f;
                m_backdrop.transform.rotation = m_camera.transform.rotation
                    * Quaternion.Euler(0.0f, 0.0f, age * 16.0f);
                float pulse = 1.0f + 0.07f * Mathf.Sin(age * 22.0f);
                m_backdrop.transform.localScale = new Vector3(20.0f, 12.0f, 1.0f) * pulse;
            }

            if (m_head != null)
            {
                m_head.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f)
                    * Quaternion.Euler(0.0f, 0.0f, Mathf.Lerp(-8.0f, 6.0f, age / Duration));
            }

            if (!m_shockA && age >= 0.06f)
            {
                m_shockA = true;
                CreateShockwave(HeadPosition(), new Color(1.0f, 0.9f, 0.75f, 0.95f), 4.0f, age);
            }
            if (!m_shockB && age >= 0.45f)
            {
                m_shockB = true;
                CreateShockwave(HeadPosition(), new Color(1.0f, 0.4f, 0.12f, 0.95f), 6.5f, age);
            }
            if (!m_shockC && age >= 0.85f)
            {
                m_shockC = true;
                CreateShockwave(HeadPosition(), new Color(1.0f, 0.8f, 0.65f, 1.0f), 9.0f, age);
            }
            UpdateShockwaves(age);
        }

        Vector3 HeadPosition()
        {
            return m_head != null ? m_head.position : m_stage.transform.position;
        }

        static float Pulse(float age, float center, float width)
        {
            return Mathf.Clamp01(1.0f - Mathf.Abs(age - center) / width);
        }

        void CreateShockwave(Vector3 worldPosition, Color color, float maxScale, float startAge)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "CutInShockwave";
            var collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            quad.transform.SetParent(m_stage.transform, false);
            quad.transform.position = worldPosition;
            quad.transform.rotation = m_camera.transform.rotation;
            quad.transform.localScale = Vector3.one * 0.3f;
            quad.layer = CutInLayer;

            var renderer = quad.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (m_shockwaveMaterial != null) renderer.sharedMaterial = m_shockwaveMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            m_shockwaves.Add(quad);
            m_shockwaveStarts.Add(startAge);
            m_shockwaveMaxScales.Add(maxScale);
            m_shockwaveColors.Add(color);
            m_createdObjects.Add(quad);
        }

        void UpdateShockwaves(float age)
        {
            for (int i = 0; i < m_shockwaves.Count; i++)
            {
                GameObject wave = m_shockwaves[i];
                if (wave == null) continue;

                float e = age - m_shockwaveStarts[i];
                if (e < 0.0f) continue;

                float t = Mathf.Clamp01(e / 0.50f);
                float scale = Mathf.Lerp(0.3f, m_shockwaveMaxScales[i], t);
                wave.transform.localScale = new Vector3(scale, scale, 1.0f);
                wave.transform.rotation = m_camera.transform.rotation;

                Color color = m_shockwaveColors[i];
                color.a *= (1.0f - t);
                var renderer = wave.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_Color", color);
                    renderer.SetPropertyBlock(block);
                }

                if (t >= 1.0f) wave.SetActive(false);
            }
        }

        void CleanupStage()
        {
            if (m_camera != null) m_camera.targetTexture = null;

            for (int i = 0; i < m_createdObjects.Count; i++)
            {
                if (m_createdObjects[i] != null) Destroy(m_createdObjects[i]);
            }
            m_createdObjects.Clear();

            if (m_stage != null) Destroy(m_stage);
            if (m_renderTexture != null)
            {
                m_renderTexture.Release();
                Destroy(m_renderTexture);
            }
            if (m_backdropMaterial != null) Destroy(m_backdropMaterial);
            if (m_shockwaveMaterial != null) Destroy(m_shockwaveMaterial);

            m_stage = null;
            m_camera = null;
            m_renderTexture = null;
            m_head = null;
            m_backdrop = null;
            m_backdropMaterial = null;
            m_shockwaveMaterial = null;
            m_shockwaves.Clear();
            m_shockwaveStarts.Clear();
            m_shockwaveMaxScales.Clear();
            m_shockwaveColors.Clear();
        }

        private void OnGUI()
        {
            if (!m_active) return;

            float age = Time.unscaledTime - m_start;
            if (age > Duration)
            {
                End();
                return;
            }

            EnsureStyles();

            float fadeIn = Mathf.Clamp01(age / 0.08f);
            float fadeOut = Mathf.Clamp01((Duration - age) / 0.18f);
            float alpha = fadeIn * fadeOut;
            if (alpha <= 0.001f) return;

            int oldDepth = GUI.depth;
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;

            GUI.depth = -1000;
            float scale = Mathf.Max(0.5f, Screen.height / 1080.0f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1.0f));

            float w = Screen.width / scale;
            float h = Screen.height / scale;

            if (m_renderTexture != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(0.0f, 0.0f, w, h), m_renderTexture, ScaleMode.ScaleAndCrop);
            }

            float flash = Mathf.Max(Pulse(age, 0.05f, 0.12f),
                Mathf.Max(Pulse(age, 0.45f, 0.14f), Pulse(age, 0.85f, 0.18f)));
            if (flash > 0.0f)
            {
                GUI.color = new Color(1.0f, 0.72f, 0.55f, 0.65f * flash * alpha);
                GUI.DrawTexture(new Rect(0.0f, 0.0f, w, h), Texture2D.whiteTexture);
            }

            float black = Mathf.Max(1.0f - fadeIn, 1.0f - fadeOut);
            if (black > 0.0f)
            {
                GUI.color = new Color(0.0f, 0.0f, 0.0f, black);
                GUI.DrawTexture(new Rect(0.0f, 0.0f, w, h), Texture2D.whiteTexture);
            }

            float textAlpha = Mathf.Clamp01((age - 0.04f) / 0.10f) * fadeOut;
            if (textAlpha > 0.001f)
            {
                float titleScale = Mathf.Lerp(1.40f, 1.0f,
                    Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01((age - 0.04f) / 0.30f)));
                DrawTitle(new Rect(0.0f, h * 0.68f, w, 220.0f),
                    m_specialName, textAlpha, titleScale);
            }

            GUI.color = oldColor;
            GUI.matrix = oldMatrix;
            GUI.depth = oldDepth;
        }

        void DrawTitle(Rect rect, string text, float alpha, float scale)
        {
            Color oldColor = GUI.color;
            Matrix4x4 oldMatrix = GUI.matrix;
            Vector2 pivot = new Vector2(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f);
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), pivot);

            GUI.color = new Color(1.0f, 0.05f, 0.02f, 0.65f * alpha);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.25f;
                float offset = 8.0f;
                GUI.Label(new Rect(rect.x + Mathf.Cos(angle) * offset,
                    rect.y + Mathf.Sin(angle) * offset, rect.width, rect.height), text, m_titleStyle);
            }

            GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.92f * alpha);
            GUI.Label(new Rect(rect.x + 6.0f, rect.y + 6.0f, rect.width, rect.height), text, m_titleStyle);

            GUI.color = new Color(1.0f, 1.0f, 1.0f, alpha);
            GUI.Label(rect, text, m_titleStyle);

            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        void EnsureStyles()
        {
            if (m_stylesReady) return;
            m_stylesReady = true;

            Font font = GetFont();
            if (font == null) font = GUI.skin.font;

            m_titleStyle = new GUIStyle(GUI.skin.label);
            m_titleStyle.font = font;
            m_titleStyle.fontSize = 150;
            m_titleStyle.fontStyle = FontStyle.Bold;
            m_titleStyle.alignment = TextAnchor.MiddleCenter;
            m_titleStyle.normal.textColor = Color.white;
        }

        static Font GetFont()
        {
            if (s_font != null) return s_font;

            s_font = Font.CreateDynamicFontFromOSFont(
                new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Noto Sans CJK JP" }, 40);
            return s_font;
        }

        static Texture2D GetSpeedTexture()
        {
            if (s_speedTexture != null) return s_speedTexture;

            const int size = 1024;
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(7, 0, 2, 255);

            var random = new System.Random(20261030);
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            for (int i = 0; i < 300; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2.0f;
                float length = size * (0.25f + (float)random.NextDouble() * 0.75f);
                float width = 1.0f + (float)random.NextDouble() * 3.5f;
                Vector2 end = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length;
                byte red = (byte)(140 + random.Next(115));
                byte green = (byte)(random.Next(24));
                DrawSpeedLine(pixels, size, center, end, width, new Color32(red, green, 6, 255));
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center) / (size * 0.5f);
                    if (distance >= 1.0f) continue;
                    float glow = Mathf.Pow(1.0f - distance, 3.0f);
                    int index = y * size + x;
                    Color32 current = pixels[index];
                    pixels[index] = new Color32(
                        (byte)Mathf.Clamp(current.r + Mathf.RoundToInt(glow * 80.0f), 0, 255),
                        (byte)Mathf.Clamp(current.g + Mathf.RoundToInt(glow * 20.0f), 0, 255),
                        (byte)Mathf.Clamp(current.b + Mathf.RoundToInt(glow * 14.0f), 0, 255),
                        255);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_speedTexture = texture;
            return texture;
        }

        static void DrawSpeedLine(Color32[] pixels, int size, Vector2 a, Vector2 b,
                                  float width, Color32 color)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 point = Vector2.Lerp(a, b, i / (float)steps);
                int radius = Mathf.Max(1, Mathf.RoundToInt(width));
                for (int y = -radius; y <= radius; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (x * x + y * y > radius * radius) continue;
                        int px = Mathf.RoundToInt(point.x) + x;
                        int py = Mathf.RoundToInt(point.y) + y;
                        if (px < 0 || px >= size || py < 0 || py >= size) continue;
                        pixels[py * size + px] = color;
                    }
                }
            }
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
                    float distance = Vector2.Distance(new Vector2(x, y), center) / max;
                    float ring = Mathf.Clamp01(1.0f - Mathf.Abs(distance - 0.42f) / 0.075f);
                    float inner = Mathf.Clamp01(1.0f - Mathf.Abs(distance - 0.28f) / 0.035f) * 0.45f;
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
    }
}
