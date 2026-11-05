using System.Collections.Generic;
using UnityEngine;

namespace Yurube
{
    // だるきんぐ必殺技「エクスカリバー」専用カットイン。
    // 既存カットインと同じ「顔ドアップ→衝撃波→揺れ→フラッシュ→画面下部の技名」構成。
    // 金・白・青・淡い紫、聖剣の輝き、黄金の光線と光の粒子で王者らしさを出す。
    public class DaruKingCutIn : MonoBehaviour
    {
        const float Duration = 1.35f;
        const int CutInLayer = 30;

        class Sparkle
        {
            public Transform Transform;
            public Vector3 BasePosition;
            public float Phase;
            public float Speed;
            public float Size;
        }

        static DaruKingCutIn s_instance;
        static Font s_font;
        static Texture2D s_royalTexture;
        static Texture2D s_ringTexture;
        static Texture2D s_sparkleTexture;

        string m_specialName;
        float m_start;
        bool m_active;
        float m_previousTimeScale = 1.0f;

        GameObject m_stage;
        Camera m_camera;
        RenderTexture m_renderTexture;
        Transform m_subject;
        GameObject m_backdrop;
        GameObject m_sword;
        Material m_backdropMaterial;
        Material m_ringMaterial;
        Material m_sparkleMaterial;
        Material m_swordMaterial;
        Material m_swordWhiteMaterial;

        readonly List<GameObject> m_created = new List<GameObject>();
        readonly List<Sparkle> m_sparkles = new List<Sparkle>();
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
                var go = new GameObject("DaruKingCutIn");
                s_instance = go.AddComponent<DaruKingCutIn>();
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
            m_stage = new GameObject("DaruKingCutInStage");
            m_stage.transform.position = new Vector3(10000.0f, 10000.0f, 10000.0f);

            m_renderTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            m_renderTexture.Create();

            var cameraObject = new GameObject("DaruKingCutInCamera");
            cameraObject.transform.SetParent(m_stage.transform, false);
            cameraObject.transform.localPosition = new Vector3(0.0f, 1.65f, -4.2f);

            m_camera = cameraObject.AddComponent<Camera>();
            m_camera.orthographic = false;
            m_camera.fieldOfView = 52.0f;
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = new Color(0.03f, 0.035f, 0.09f, 1.0f);
            m_camera.cullingMask = 1 << CutInLayer;
            m_camera.nearClipPlane = 0.05f;
            m_camera.farClipPlane = 40.0f;
            m_camera.targetTexture = m_renderTexture;
            m_camera.enabled = true;
            cameraObject.transform.LookAt(m_stage.transform.TransformPoint(new Vector3(0.0f, 1.6f, 0.0f)));

            CreateLight("DaruKingGoldKey", new Vector3(2.2f, 3.6f, -1.8f),
                Quaternion.Euler(34.0f, -28.0f, 0.0f), new Color(1.0f, 0.78f, 0.25f), 2.2f);
            CreateLight("DaruKingBlueRim", new Vector3(-2.4f, 2.2f, -1.0f),
                Quaternion.identity, new Color(1.0f, 0.95f, 0.82f), 5.0f);

            CreateBackdrop();
            CreateMaterials();

            if (subjectSource != null)
            {
                var subject = CreateClone(subjectSource, "CutInDaruKingHead");
                m_subject = subject.transform;
                m_subject.localScale = Vector3.one * 2.1f;
                m_subject.localPosition = new Vector3(0.0f, 1.55f, 0.0f);
                // だるきんぐの頭は +Z 側が顔。カメラは -Z 側なので180度回す
                m_subject.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
                m_created.Add(subject);
            }

            CreateSword();
            CreateSparkles();
            CreateRingMaterial();
        }

        void CreateBackdrop()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "DaruKingHolyRays";
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
                m_backdropMaterial.mainTexture = GetRoyalTexture();
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
            m_sparkleMaterial = CreateUnlitMaterial(GetSparkleTexture(),
                new Color(1.0f, 0.93f, 0.62f, 0.9f));
        }

        void CreateSword()
        {
            m_sword = new GameObject("Excalibur");
            m_sword.transform.SetParent(m_stage.transform, false);
            m_sword.transform.localPosition = new Vector3(1.4f, 1.4f, 0.1f);
            m_sword.transform.localRotation = Quaternion.Euler(0.0f, -26.0f, -20.0f);
            m_sword.layer = CutInLayer;
            m_created.Add(m_sword);

            m_swordMaterial = CreateUnlitMaterial(null, new Color(1.0f, 0.80f, 0.24f, 1.0f));
            m_swordWhiteMaterial = CreateUnlitMaterial(null, new Color(1.0f, 1.0f, 0.92f, 1.0f));

            CreateSwordPart("Blade", new Vector3(0.0f, 1.95f, 0.0f),
                new Vector3(0.26f, 3.9f, 0.12f), Quaternion.identity, m_swordMaterial);
            CreateSwordPart("BladeCore", new Vector3(0.0f, 1.97f, -0.07f),
                new Vector3(0.09f, 3.72f, 0.05f), Quaternion.identity, m_swordWhiteMaterial);
            CreateSwordPart("BladeTip", new Vector3(0.0f, 3.92f, 0.0f),
                new Vector3(0.36f, 0.36f, 0.12f), Quaternion.Euler(0.0f, 0.0f, 45.0f), m_swordMaterial);
            CreateSwordPart("Guard", new Vector3(0.0f, -0.10f, 0.0f),
                new Vector3(1.25f, 0.18f, 0.22f), Quaternion.identity, m_swordMaterial);
            CreateSwordPart("GuardCore", new Vector3(0.0f, -0.10f, -0.07f),
                new Vector3(0.92f, 0.09f, 0.24f), Quaternion.identity, m_swordWhiteMaterial);
            CreateSwordPart("Grip", new Vector3(0.0f, -0.55f, 0.0f),
                new Vector3(0.18f, 0.80f, 0.18f), Quaternion.identity, m_swordMaterial);
            CreateSwordPart("Pommel", new Vector3(0.0f, -1.02f, 0.0f),
                new Vector3(0.32f, 0.32f, 0.32f), Quaternion.Euler(0.0f, 45.0f, 0.0f), m_swordWhiteMaterial);

            var glow = new GameObject("ExcaliburGlow");
            glow.transform.SetParent(m_sword.transform, false);
            glow.transform.localPosition = new Vector3(0.0f, 2.0f, 0.0f);
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1.0f, 0.88f, 0.45f);
            light.intensity = 10.0f;
            light.range = 11.0f;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << CutInLayer;
        }

        void CreateSwordPart(string name, Vector3 localPosition, Vector3 localScale,
            Quaternion localRotation, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            var collider = cube.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            cube.transform.SetParent(m_sword.transform, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;
            cube.transform.localRotation = localRotation;
            cube.layer = CutInLayer;

            var renderer = cube.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (material != null) renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        void CreateSparkles()
        {
            if (m_sparkleMaterial == null) return;

            for (int i = 0; i < 22; i++)
            {
                float angle = Random.Range(0.0f, Mathf.PI * 2.0f);
                float radius = Random.Range(1.3f, 3.2f);
                var sparkle = new Sparkle
                {
                    BasePosition = new Vector3(
                        Mathf.Cos(angle) * radius,
                        Random.Range(0.3f, 3.2f),
                        Mathf.Sin(angle) * radius * 0.6f + Random.Range(0.2f, 1.0f)),
                    Phase = Random.Range(0.0f, 1.0f),
                    Speed = Random.Range(0.3f, 0.8f),
                    Size = Random.Range(0.10f, 0.36f),
                };

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"DaruKingSparkle{i}";
                var collider = quad.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                quad.transform.SetParent(m_stage.transform, false);
                quad.transform.localPosition = sparkle.BasePosition;
                quad.transform.localScale = Vector3.one * sparkle.Size;
                quad.layer = CutInLayer;

                var renderer = quad.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = m_sparkleMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                sparkle.Transform = quad.transform;
                m_sparkles.Add(sparkle);
                m_created.Add(quad);
            }
        }

        void CreateRingMaterial()
        {
            m_ringMaterial = CreateUnlitMaterial(GetRingTexture(), Color.white);
        }

        Material CreateUnlitMaterial(Texture texture, Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            var material = new Material(shader);
            if (texture != null) material.mainTexture = texture;
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            return material;
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
            float punch = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(age / 0.62f));

            if (m_camera != null && m_stage != null)
            {
                Vector3 position = new Vector3(0.0f, 1.65f,
                    Mathf.Lerp(-4.2f, -3.35f, punch));
                m_camera.fieldOfView = Mathf.Lerp(52.0f, 43.0f, punch);

                float shake = Mathf.Max(Pulse(age, 0.05f, 0.12f),
                    Mathf.Max(Pulse(age, 0.45f, 0.14f), Pulse(age, 0.85f, 0.18f))) * 0.055f;
                if (shake > 0.0f)
                {
                    position.x += Mathf.Sin(age * 175.0f) * shake;
                    position.y += Mathf.Cos(age * 230.0f) * shake;
                }

                m_camera.transform.localPosition = position;
                m_camera.transform.LookAt(m_stage.transform.TransformPoint(new Vector3(0.0f, 1.55f, 0.0f)));
            }

            if (m_backdrop != null)
            {
                m_backdrop.transform.position = m_camera.transform.position
                    + m_camera.transform.forward * 12.0f;
                m_backdrop.transform.rotation = m_camera.transform.rotation
                    * Quaternion.Euler(0.0f, 0.0f, age * 8.0f);
                float pulse = 1.0f + 0.035f * Mathf.Sin(age * 14.0f);
                m_backdrop.transform.localScale = new Vector3(20.0f, 12.0f, 1.0f) * pulse;
            }

            if (m_subject != null)
            {
                m_subject.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f)
                    * Quaternion.Euler(
                        Mathf.Sin(age * 4.0f) * 2.0f,
                        Mathf.Sin(age * 3.0f) * 4.0f,
                        Mathf.Sin(age * 5.0f) * 2.0f);
            }

            if (m_sword != null)
            {
                m_sword.transform.localPosition = new Vector3(
                    1.4f + Mathf.Sin(age * 2.0f) * 0.04f,
                    1.4f + Mathf.Sin(age * 2.4f) * 0.05f,
                    0.1f);
                m_sword.transform.localRotation = Quaternion.Euler(
                    Mathf.Sin(age * 1.7f) * 2.0f,
                    -26.0f + Mathf.Sin(age * 2.0f) * 4.0f,
                    -20.0f + Mathf.Sin(age * 1.9f) * 2.0f);
            }

            for (int i = 0; i < m_sparkles.Count; i++)
            {
                var sparkle = m_sparkles[i];
                if (sparkle.Transform == null) continue;
                float cycle = Mathf.Repeat(age * sparkle.Speed + sparkle.Phase, 1.0f);
                Vector3 position = sparkle.BasePosition;
                position.y += cycle * 1.4f;
                position.x += Mathf.Sin(age * 2.0f + i) * 0.10f;
                sparkle.Transform.localPosition = position;
                float scale = sparkle.Size * (0.7f + 0.5f * Mathf.Sin(cycle * Mathf.PI));
                sparkle.Transform.localScale = new Vector3(scale, scale, 1.0f);
                sparkle.Transform.rotation = m_camera.transform.rotation;
            }

            if (!m_shockA && age >= 0.05f)
            {
                m_shockA = true;
                CreateShockwave(SubjectPosition(), new Color(1.0f, 0.85f, 0.35f, 0.95f), 4.0f, age);
            }
            if (!m_shockB && age >= 0.45f)
            {
                m_shockB = true;
                CreateShockwave(SubjectPosition(), new Color(1.0f, 0.94f, 0.68f, 0.9f), 6.6f, age);            }
            if (!m_shockC && age >= 0.85f)
            {
                m_shockC = true;
                CreateShockwave(SubjectPosition(), new Color(1.0f, 0.96f, 0.78f, 1.0f), 9.0f, age);
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
            quad.name = "DaruKingShockwave";
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
                if (m_ringMaterial != null) renderer.sharedMaterial = m_ringMaterial;
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
                float t = Mathf.Clamp01(e / 0.5f);
                float scale = Mathf.Lerp(0.3f, m_shockwaveMaxScales[i], t);
                wave.transform.localScale = new Vector3(scale, scale, 1.0f);
                wave.transform.rotation = m_camera.transform.rotation;

                Color color = m_shockwaveColors[i];
                color.a *= 1.0f - t;
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
            if (m_ringMaterial != null) Destroy(m_ringMaterial);
            if (m_sparkleMaterial != null) Destroy(m_sparkleMaterial);
            if (m_swordMaterial != null) Destroy(m_swordMaterial);
            if (m_swordWhiteMaterial != null) Destroy(m_swordWhiteMaterial);

            m_stage = null;
            m_camera = null;
            m_renderTexture = null;
            m_subject = null;
            m_backdrop = null;
            m_sword = null;
            m_backdropMaterial = null;
            m_ringMaterial = null;
            m_sparkleMaterial = null;
            m_swordMaterial = null;
            m_swordWhiteMaterial = null;
            m_sparkles.Clear();
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
                GUI.color = new Color(1.0f, 0.94f, 0.66f, 0.50f * flash * alpha);
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

            GUI.color = new Color(0.01f, 0.018f, 0.06f, 0.95f * alpha);
            GUI.Label(new Rect(rect.x + 13.0f, rect.y + 13.0f, rect.width, rect.height),
                text, m_titleStyle);

            // 太い黄金の縁取り
            GUI.color = new Color(1.0f, 0.70f, 0.10f, 0.98f * alpha);
            DrawOutline(rect, text, 10.0f);

            // 内側の淡い黄金
            GUI.color = new Color(1.0f, 0.88f, 0.48f, 0.95f * alpha);
            DrawOutline(rect, text, 5.0f);

            // 下側に淡い黄金を残して、白→淡い黄金のグラデーションに見せる
            GUI.color = new Color(1.0f, 0.90f, 0.55f, 0.96f * alpha);
            GUI.Label(new Rect(rect.x, rect.y + 4.0f, rect.width, rect.height), text, m_titleStyle);

            GUI.color = new Color(1.0f, 1.0f, 0.97f, alpha);
            GUI.Label(new Rect(rect.x, rect.y - 1.0f, rect.width, rect.height), text, m_titleStyle);

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
            m_titleStyle.fontSize = 170;
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

        static Texture2D GetRoyalTexture()
        {
            if (s_royalTexture != null) return s_royalTexture;

            const int size = 1024;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.008f, y * 0.008f);
                    float r = Vector2.Distance(new Vector2(x, y), center) / (size * 0.5f);
                    Color color = Color.Lerp(new Color(0.025f, 0.03f, 0.08f),
                        new Color(0.10f, 0.12f, 0.22f), n * 0.55f);
                    float centerGlow = Mathf.Pow(Mathf.Clamp01(1.0f - r), 3.0f);
                    color += new Color(centerGlow * 0.35f, centerGlow * 0.30f, centerGlow * 0.12f);
                    pixels[y * size + x] = color;
                }
            }

            // 黄金の放射光
            for (int i = 0; i < 18; i++)
            {
                float angle = i * Mathf.PI * 2.0f / 18.0f + 0.10f;
                Vector2 a = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size * 0.04f;
                Vector2 b = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size * 0.70f;
                Color ray = i % 4 == 0
                    ? new Color(1.0f, 0.98f, 0.82f)
                    : new Color(1.0f, 0.78f, 0.22f);
                FillTaperedSegment(pixels, size, a, b, size * 0.030f, 1.5f, ray);
            }

            // 光の粒子
            for (int i = 0; i < 80; i++)
            {
                Vector2 position = new Vector2(
                    Random.Range(size * 0.03f, size * 0.97f),
                    Random.Range(size * 0.03f, size * 0.97f));
                float radius = Random.Range(size * 0.004f, size * 0.018f);
                Color color = i % 3 == 0
                    ? new Color(1.0f, 0.99f, 0.86f)
                    : new Color(1.0f, 0.80f, 0.26f);
                DrawCircle(pixels, size, position, radius, color);
            }

            // 周辺減光
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x, y), center) / (size * 0.5f);
                    float dark = Mathf.Clamp01((r - 0.55f) / 0.55f) * 0.65f;
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
            s_royalTexture = texture;
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

        static void DrawCircle(Color32[] pixels, int size, Vector2 center, float radius, Color color)
        {
            int minX = Mathf.Clamp(Mathf.FloorToInt(center.x - radius), 0, size - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(center.x + radius), 0, size - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(center.y - radius), 0, size - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(center.y + radius), 0, size - 1);
            float radiusSqr = radius * radius;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - center.x;
                    float dy = y - center.y;
                    if (dx * dx + dy * dy <= radiusSqr)
                        pixels[y * size + x] = color;
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
                    float inner = Mathf.Clamp01(1.0f - Mathf.Abs(r - 0.30f) / 0.025f) * 0.3f;
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

        static Texture2D GetSparkleTexture()
        {
            if (s_sparkleTexture != null) return s_sparkleTexture;

            const int size = 64;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float max = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x, y), center) / max;
                    float alpha = Mathf.Pow(Mathf.Clamp01(1.0f - r), 2.2f);
                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)(alpha * 255.0f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_sparkleTexture = texture;
            return texture;
        }
    }
}
