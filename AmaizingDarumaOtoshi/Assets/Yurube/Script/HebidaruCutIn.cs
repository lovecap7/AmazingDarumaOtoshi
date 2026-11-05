using System.Collections.Generic;
using UnityEngine;

namespace Yurube
{
    // へびだる必殺技「反転毒沼」専用カットイン。
    // あかだる・いわだると同じ「顔ドアップ→衝撃波→揺れ→フラッシュ→画面下部の技名」構成。
    // 緑・紫・黒、蛇のうねり、毒の泡、蛇の目と牙、歪む渦で「操作が狂う」印象を作る。
    public class HebidaruCutIn : MonoBehaviour
    {
        const float Duration = 1.30f;
        const int CutInLayer = 30;

        class Bubble
        {
            public Transform Transform;
            public Vector3 BasePosition;
            public float Speed;
            public float Phase;
            public float Size;
        }

        static HebidaruCutIn s_instance;
        static Font s_font;
        static Texture2D s_toxicTexture;
        static Texture2D s_ringTexture;
        static Texture2D s_bubbleTexture;

        string m_specialName;
        float m_start;
        bool m_active;
        float m_previousTimeScale = 1.0f;

        GameObject m_stage;
        Camera m_camera;
        RenderTexture m_renderTexture;
        Transform m_subject;
        GameObject m_backdrop;
        Material m_backdropMaterial;
        Material m_bubbleMaterial;
        Material m_shockwaveMaterial;

        readonly List<GameObject> m_created = new List<GameObject>();
        readonly List<Bubble> m_bubbles = new List<Bubble>();
        readonly List<GameObject> m_shockwaves = new List<GameObject>();
        readonly List<float> m_shockwaveStarts = new List<float>();
        readonly List<float> m_shockwaveMaxScales = new List<float>();
        readonly List<Color> m_shockwaveColors = new List<Color>();
        bool m_shockA;
        bool m_shockB;
        bool m_shockC;

        GUIStyle m_titleStyle;
        bool m_stylesReady;

        public static void Show(string specialName, GameObject subject)
        {
            if (s_instance == null)
            {
                var go = new GameObject("HebidaruCutIn");
                s_instance = go.AddComponent<HebidaruCutIn>();
            }
            s_instance.Play(specialName, subject);
        }

        public static bool IsPlaying => s_instance != null && s_instance.m_active;

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

        void Play(string specialName, GameObject subject)
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
            m_start = Time.unscaledTime;
            m_active = true;
            m_shockA = false;
            m_shockB = false;
            m_shockC = false;

            BuildStage(subject);
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

        void BuildStage(GameObject subjectSource)
        {
            m_stage = new GameObject("HebidaruCutInStage");
            m_stage.transform.position = new Vector3(10000.0f, 10000.0f, 10000.0f);

            m_renderTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            m_renderTexture.Create();

            var cameraObject = new GameObject("HebidaruCutInCamera");
            cameraObject.transform.SetParent(m_stage.transform, false);
            cameraObject.transform.localPosition = new Vector3(0.0f, 1.55f, -4.0f);

            m_camera = cameraObject.AddComponent<Camera>();
            m_camera.orthographic = false;
            m_camera.fieldOfView = 52.0f;
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = new Color(0.035f, 0.02f, 0.055f, 1.0f);
            m_camera.cullingMask = 1 << CutInLayer;
            m_camera.nearClipPlane = 0.05f;
            m_camera.farClipPlane = 40.0f;
            m_camera.targetTexture = m_renderTexture;
            m_camera.enabled = true;
            cameraObject.transform.LookAt(m_stage.transform.TransformPoint(new Vector3(0.0f, 1.5f, 0.0f)));

            CreateLight("HebiCutInToxicKey", new Vector3(2.0f, 3.4f, -1.8f),
                Quaternion.Euler(35.0f, -28.0f, 0.0f), new Color(0.48f, 1.0f, 0.40f), 2.2f);
            CreateLight("HebiCutInToxicRim", new Vector3(-2.3f, 2.2f, -1.0f),
                Quaternion.identity, new Color(0.72f, 0.35f, 1.0f), 7.5f);

            CreateBackdrop();
            CreateMaterials();

            if (subjectSource != null)
            {
                var subject = CreateClone(subjectSource, "CutInHebidaruHead");
                m_subject = subject.transform;
                // へびだるの頭は実寸が小さいため、カットインでは巨人化させて画面いっぱいに見せる
                m_subject.localScale = Vector3.one * 2.0f;
                m_subject.localPosition = new Vector3(0.0f, 1.5f, 0.0f);
                // へびだるの頭は +Z 側が顔。カメラは -Z にいるため180度回す
                m_subject.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
                m_created.Add(subject);
            }

            CreateBubbles();
            CreateShockwaveMaterial();
        }

        void CreateBackdrop()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "HebidaruCutInToxicSwirl";
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
                m_backdropMaterial.mainTexture = GetToxicTexture();
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
            m_created.Add(quad);
        }

        void CreateMaterials()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;

            m_bubbleMaterial = new Material(shader);
            m_bubbleMaterial.mainTexture = GetBubbleTexture();
            if (m_bubbleMaterial.HasProperty("_Color"))
                m_bubbleMaterial.SetColor("_Color", new Color(0.55f, 1.0f, 0.42f, 0.8f));
        }

        void CreateBubbles()
        {
            if (m_bubbleMaterial == null) return;

            for (int i = 0; i < 16; i++)
            {
                float angle = Random.Range(0.0f, Mathf.PI * 2.0f);
                float radius = Random.Range(1.4f, 2.9f);
                var bubble = new Bubble
                {
                    BasePosition = new Vector3(
                        Mathf.Cos(angle) * radius,
                        Random.Range(0.2f, 2.8f),
                        Mathf.Sin(angle) * radius * 0.55f + Random.Range(0.2f, 1.2f)),
                    Speed = Random.Range(0.3f, 0.8f),
                    Phase = Random.Range(0.0f, 1.0f),
                    Size = Random.Range(0.18f, 0.62f),
                };

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"HebidaruCutInBubble{i}";
                var collider = quad.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                quad.transform.SetParent(m_stage.transform, false);
                quad.transform.localPosition = bubble.BasePosition;
                quad.transform.localScale = Vector3.one * bubble.Size;
                quad.layer = CutInLayer;

                var renderer = quad.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = m_bubbleMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                bubble.Transform = quad.transform;
                m_bubbles.Add(bubble);
                m_created.Add(quad);
            }
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
            light.range = 9.0f;
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
            }
            foreach (var particles in clone.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
                if (particleRenderer != null) particleRenderer.enabled = false;
            }
            foreach (var trail in clone.GetComponentsInChildren<TrailRenderer>(true))
                trail.enabled = false;
            foreach (var light in clone.GetComponentsInChildren<Light>(true))
                light.enabled = false;
            foreach (var mesh in clone.GetComponentsInChildren<MeshRenderer>(true))
                mesh.enabled = true;
            foreach (var skinned in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skinned.enabled = true;
            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = CutInLayer;

            clone.SetActive(true);
            return clone;
        }

        void Animate(float age)
        {
            float punch = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(age / 0.60f));

            if (m_camera != null && m_stage != null)
            {
                Vector3 position = new Vector3(0.0f, 1.55f,
                    Mathf.Lerp(-4.0f, -3.25f, punch));
                m_camera.fieldOfView = Mathf.Lerp(52.0f, 43.0f, punch);

                float shake = Mathf.Max(Pulse(age, 0.05f, 0.12f),
                    Mathf.Max(Pulse(age, 0.45f, 0.14f), Pulse(age, 0.85f, 0.18f))) * 0.06f;
                if (shake > 0.0f)
                {
                    position.x += Mathf.Sin(age * 190.0f) * shake;
                    position.y += Mathf.Cos(age * 240.0f) * shake;
                }

                m_camera.transform.localPosition = position;
                m_camera.transform.LookAt(m_stage.transform.TransformPoint(new Vector3(0.0f, 1.5f, 0.0f)));
            }

            if (m_backdrop != null)
            {
                m_backdrop.transform.position = m_camera.transform.position
                    + m_camera.transform.forward * 12.0f;
                m_backdrop.transform.rotation = m_camera.transform.rotation
                    * Quaternion.Euler(0.0f, 0.0f, Mathf.Sin(age * 4.0f) * 12.0f + age * 8.0f);
                float pulse = 1.0f + 0.035f * Mathf.Sin(age * 15.0f);
                m_backdrop.transform.localScale = new Vector3(20.0f, 12.0f, 1.0f) * pulse;
            }

            if (m_subject != null)
            {
                // 正面(目・牙)をカメラへ向けたまま、わずかに揺らす
                m_subject.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f)
                    * Quaternion.Euler(
                        Mathf.Sin(age * 4.0f) * 2.5f,
                        Mathf.Sin(age * 3.0f) * 5.0f,
                        Mathf.Sin(age * 5.0f) * 2.5f);
            }

            for (int i = 0; i < m_bubbles.Count; i++)
            {
                var bubble = m_bubbles[i];
                if (bubble.Transform == null) continue;
                float cycle = Mathf.Repeat(age * bubble.Speed + bubble.Phase, 1.0f);
                Vector3 position = bubble.BasePosition;
                position.y = 0.1f + cycle * 3.2f;
                position.x += Mathf.Sin(age * 2.0f + i) * 0.12f;
                bubble.Transform.localPosition = position;
                float scale = bubble.Size * (0.75f + 0.35f * Mathf.Sin(cycle * Mathf.PI));
                bubble.Transform.localScale = new Vector3(scale, scale, 1.0f);
                bubble.Transform.rotation = m_camera.transform.rotation;
            }

            if (!m_shockA && age >= 0.05f)
            {
                m_shockA = true;
                CreateShockwave(SubjectPosition(), new Color(0.55f, 1.0f, 0.35f, 0.9f), 4.3f, age);
            }
            if (!m_shockB && age >= 0.45f)
            {
                m_shockB = true;
                CreateShockwave(SubjectPosition(), new Color(0.75f, 0.35f, 1.0f, 0.9f), 6.8f, age);
            }
            if (!m_shockC && age >= 0.85f)
            {
                m_shockC = true;
                CreateShockwave(SubjectPosition(), new Color(0.65f, 1.0f, 0.45f, 0.9f), 9.0f, age);
            }
            UpdateShockwaves(age);
        }

        Vector3 SubjectPosition()
        {
            return m_subject != null ? m_subject.position : m_stage.transform.position;
        }

        static float Pulse(float age, float center, float width)
        {
            return Mathf.Clamp01(1.0f - Mathf.Abs(age - center) / width);
        }

        void CreateShockwave(Vector3 worldPosition, Color color, float maxScale, float startAge)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "HebidaruCutInShockwave";
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
            m_created.Add(quad);
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
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", color);
                wave.GetComponent<MeshRenderer>().SetPropertyBlock(block);

                if (t >= 1.0f) wave.SetActive(false);
            }
        }

        void CleanupStage()
        {
            if (m_camera != null) m_camera.targetTexture = null;

            for (int i = 0; i < m_created.Count; i++)
            {
                if (m_created[i] != null) Destroy(m_created[i]);
            }
            m_created.Clear();

            if (m_stage != null) Destroy(m_stage);
            if (m_renderTexture != null)
            {
                m_renderTexture.Release();
                Destroy(m_renderTexture);
            }
            if (m_backdropMaterial != null) Destroy(m_backdropMaterial);
            if (m_bubbleMaterial != null) Destroy(m_bubbleMaterial);
            if (m_shockwaveMaterial != null) Destroy(m_shockwaveMaterial);

            m_stage = null;
            m_camera = null;
            m_renderTexture = null;
            m_subject = null;
            m_backdrop = null;
            m_backdropMaterial = null;
            m_bubbleMaterial = null;
            m_shockwaveMaterial = null;
            m_bubbles.Clear();
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
                GUI.color = new Color(0.55f, 1.0f, 0.45f, 0.48f * flash * alpha);
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

            // 黒い影
            GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.92f * alpha);
            GUI.Label(new Rect(rect.x + 12.0f, rect.y + 12.0f, rect.width, rect.height),
                text, m_titleStyle);

            // 紫の縁取り
            GUI.color = new Color(0.28f, 0.05f, 0.38f, 0.95f * alpha);
            DrawOutline(rect, text, 8.0f);

            // 毒々しい緑の縁取り
            GUI.color = new Color(0.24f, 0.55f, 0.08f, 0.95f * alpha);
            DrawOutline(rect, text, 4.0f);

            // 白い本体
            GUI.color = new Color(0.96f, 1.0f, 0.94f, alpha);
            GUI.Label(rect, text, m_titleStyle);

            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        void DrawOutline(Rect rect, string text, float radius)
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.25f;
                GUI.Label(new Rect(rect.x + Mathf.Cos(angle) * radius,
                    rect.y + Mathf.Sin(angle) * radius, rect.width, rect.height), text, m_titleStyle);
            }
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

        static Texture2D GetToxicTexture()
        {
            if (s_toxicTexture != null) return s_toxicTexture;

            const int size = 1024;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            // 毒の渦を巻く下地
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 d = new Vector2(x, y) - center;
                    float r = d.magnitude / (size * 0.5f);
                    float angle = Mathf.Atan2(d.y, d.x);
                    float n = Mathf.PerlinNoise(x * 0.012f, y * 0.012f);
                    float swirl = Mathf.Sin(angle * 4.0f + r * 24.0f + n * 6.0f) * 0.5f + 0.5f;
                    float green = Mathf.Lerp(0.06f, 0.30f, swirl);
                    float purple = Mathf.Lerp(0.20f, 0.40f, 1.0f - swirl);
                    float dark = Mathf.Lerp(0.02f, 0.10f, 1.0f - r);
                    pixels[y * size + x] = new Color(green * 0.45f, green, purple * 0.85f, 1.0f);
                }
            }

            // 大きな蛇のうねり
            for (int i = 0; i < 3; i++)
            {
                float baseY = size * (0.24f + i * 0.24f);
                float amplitude = size * (0.07f + i * 0.015f);
                float frequency = 8.0f + i * 2.0f;
                float thickness = size * (0.028f + i * 0.007f);
                Vector2 previous = new Vector2(-size * 0.08f,
                    baseY + Mathf.Sin(-size * 0.08f / size * frequency) * amplitude);
                float previousWidth = thickness * 0.55f;

                for (int s = 1; s <= 20; s++)
                {
                    float x = Mathf.Lerp(-size * 0.08f, size * 1.08f, s / 20.0f);
                    float y = baseY + Mathf.Sin(x / size * frequency + i * 1.7f) * amplitude;
                    Vector2 next = new Vector2(x, y);
                    float width = thickness * Mathf.Sin(Mathf.PI * s / 20.0f) + 4.0f;

                    FillTaperedSegment(pixels, size, previous, next,
                        previousWidth + 12.0f, width + 12.0f,
                        new Color(0.035f, 0.015f, 0.055f));
                    FillTaperedSegment(pixels, size, previous, next,
                        previousWidth, width,
                        i == 1 ? new Color(0.52f, 0.16f, 0.75f) : new Color(0.25f, 0.72f, 0.14f));

                    previous = next;
                    previousWidth = width;
                }
            }

            // 大きな蛇の目(背景モチーフ)
            Vector2 eye = new Vector2(size * 0.30f, size * 0.40f);
            DrawEllipse(pixels, size, eye, size * 0.20f, size * 0.10f,
                new Color(0.035f, 0.08f, 0.03f));
            DrawEllipse(pixels, size, eye, size * 0.14f, size * 0.072f,
                new Color(0.38f, 0.10f, 0.55f));
            DrawEllipse(pixels, size, eye, size * 0.032f, size * 0.070f,
                new Color(0.005f, 0.005f, 0.008f));
            DrawEllipse(pixels, size, eye + new Vector2(-size * 0.055f, -size * 0.030f),
                size * 0.025f, size * 0.018f, new Color(0.78f, 1.0f, 0.62f));

            // 牙
            FillPolygon(pixels, size, new[]
            {
                new Vector2(size * 0.04f, size * 0.78f),
                new Vector2(size * 0.30f, size * 0.72f),
                new Vector2(size * 0.20f, size * 0.97f),
            }, new Color(0.82f, 0.80f, 0.64f));
            FillPolygon(pixels, size, new[]
            {
                new Vector2(size * 0.96f, size * 0.78f),
                new Vector2(size * 0.70f, size * 0.72f),
                new Vector2(size * 0.80f, size * 0.97f),
            }, new Color(0.82f, 0.80f, 0.64f));

            // 毒の泡
            for (int i = 0; i < 26; i++)
            {
                Vector2 position = new Vector2(
                    Random.Range(size * 0.05f, size * 0.95f),
                    Random.Range(size * 0.05f, size * 0.95f));
                float radius = Random.Range(size * 0.008f, size * 0.030f);
                DrawRing(pixels, size, position, radius, Random.Range(2.0f, 5.0f),
                    i % 3 == 0 ? new Color(0.62f, 0.25f, 0.82f) : new Color(0.55f, 0.92f, 0.30f));
            }

            // 画面中心から放射状に走る太い衝撃線
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 2.0f / 8.0f + 0.12f;
                Vector2 a = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size * 0.10f;
                Vector2 b = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size * 0.72f;
                FillTaperedSegment(pixels, size, a, b, size * 0.028f, 2.0f,
                    i % 2 == 0 ? new Color(0.28f, 0.05f, 0.40f) : new Color(0.12f, 0.35f, 0.06f));
            }

            // 周辺を暗くする
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x, y), center) / (size * 0.5f);
                    float dark = Mathf.Clamp01((r - 0.55f) / 0.55f) * 0.72f;
                    int index = y * size + x;
                    Color32 current = pixels[index];
                    pixels[index] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(current.r * (1.0f - dark)), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(current.g * (1.0f - dark)), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(current.b * (1.0f - dark)), 0, 255),
                        255);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_toxicTexture = texture;
            return texture;
        }

        static void FillTaperedSegment(Color32[] pixels, int size, Vector2 a, Vector2 b,
            float widthA, float widthB, Color color)
        {
            Vector2 direction = (b - a).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            var quad = new Vector2[4];
            quad[0] = a + normal * (widthA * 0.5f);
            quad[1] = b + normal * (widthB * 0.5f);
            quad[2] = b - normal * (widthB * 0.5f);
            quad[3] = a - normal * (widthA * 0.5f);
            FillPolygon(pixels, size, quad, color);
        }

        static void FillPolygon(Color32[] pixels, int size, Vector2[] polygon, Color color)
        {
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < polygon.Length; i++)
            {
                minY = Mathf.Min(minY, polygon[i].y);
                maxY = Mathf.Max(maxY, polygon[i].y);
            }

            int startY = Mathf.Clamp(Mathf.FloorToInt(minY), 0, size - 1);
            int endY = Mathf.Clamp(Mathf.CeilToInt(maxY), 0, size - 1);
            var intersections = new List<float>(8);

            for (int y = startY; y <= endY; y++)
            {
                float scanY = y + 0.5f;
                intersections.Clear();
                for (int i = 0; i < polygon.Length; i++)
                {
                    Vector2 a = polygon[i];
                    Vector2 b = polygon[(i + 1) % polygon.Length];
                    if ((a.y <= scanY && b.y > scanY) || (b.y <= scanY && a.y > scanY))
                    {
                        float t = (scanY - a.y) / (b.y - a.y);
                        intersections.Add(a.x + (b.x - a.x) * t);
                    }
                }
                intersections.Sort();
                for (int i = 0; i + 1 < intersections.Count; i += 2)
                {
                    int startX = Mathf.Clamp(Mathf.CeilToInt(intersections[i]), 0, size - 1);
                    int endX = Mathf.Clamp(Mathf.FloorToInt(intersections[i + 1]), 0, size - 1);
                    for (int x = startX; x <= endX; x++)
                        pixels[y * size + x] = color;
                }
            }
        }

        static void DrawEllipse(Color32[] pixels, int size, Vector2 center,
            float radiusX, float radiusY, Color color)
        {
            int minX = Mathf.Clamp(Mathf.FloorToInt(center.x - radiusX), 0, size - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(center.x + radiusX), 0, size - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(center.y - radiusY), 0, size - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(center.y + radiusY), 0, size - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - center.x) / radiusX;
                    float dy = (y - center.y) / radiusY;
                    if (dx * dx + dy * dy <= 1.0f)
                        pixels[y * size + x] = color;
                }
            }
        }

        static void DrawRing(Color32[] pixels, int size, Vector2 center, float radius,
            float width, Color color)
        {
            int min = Mathf.Clamp(Mathf.FloorToInt(radius - width), 0, size - 1);
            int max = Mathf.Clamp(Mathf.CeilToInt(radius + width), 0, size - 1);
            for (int y = -max; y <= max; y++)
            {
                for (int x = -max; x <= max; x++)
                {
                    float d = Mathf.Sqrt(x * x + y * y);
                    if (Mathf.Abs(d - radius) > width) continue;
                    int px = Mathf.RoundToInt(center.x) + x;
                    int py = Mathf.RoundToInt(center.y) + y;
                    if (px < 0 || px >= size || py < 0 || py >= size) continue;
                    pixels[py * size + px] = color;
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
                    float r = Vector2.Distance(new Vector2(x, y), center) / max;
                    float ring = Mathf.Clamp01(1.0f - Mathf.Abs(r - 0.44f) / 0.055f);
                    float inner = Mathf.Clamp01(1.0f - Mathf.Abs(r - 0.30f) / 0.025f) * 0.35f;
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

            const int size = 128;
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
    }
}
