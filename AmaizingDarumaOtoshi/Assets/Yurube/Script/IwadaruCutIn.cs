using System.Collections.Generic;
using UnityEngine;

namespace Yurube
{
    // いわだる必殺技「いわだる崩し」専用カットイン。
    // あかだるのカットインと同じ「顔ドアップ→衝撃波→揺れ→フラッシュ→画面下部の技名」構成を、
    // 岩盤の亀裂・岩石片・砂煙・黄土色の光で表現する。
    public class IwadaruCutIn : MonoBehaviour
    {
        const float Duration = 1.35f;
        const int CutInLayer = 30;

        static IwadaruCutIn s_instance;
        static Font s_font;
        static Texture2D s_rockTexture;
        static Texture2D s_ringTexture;
        static Texture2D s_dustTexture;

        string m_specialName;
        float m_start;
        bool m_active;

        GameObject m_stage;
        Camera m_camera;
        RenderTexture m_renderTexture;
        Transform m_subject;
        GameObject m_backdrop;
        Material m_backdropMaterial;
        Material m_rockMaterial;
        Material m_dustMaterial;
        Material m_shockwaveMaterial;

        readonly List<GameObject> m_createdObjects = new List<GameObject>();
        readonly List<Transform> m_rocks = new List<Transform>();
        readonly List<Vector3> m_rockBasePositions = new List<Vector3>();
        readonly List<float> m_rockSpins = new List<float>();
        readonly List<Transform> m_dusts = new List<Transform>();
        readonly List<Vector3> m_dustBaseScales = new List<Vector3>();
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
                var go = new GameObject("IwadaruCutIn");
                s_instance = go.AddComponent<IwadaruCutIn>();
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
                // 時間を止める(ほかの演出と重なっても正しく戻るように共通の仕組みを使う。中平変更)
                Nakahira.TimeStop.Begin(this, 0.0f);
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
            Nakahira.TimeStop.End(this);
        }

        void BuildStage(GameObject subjectSource)
        {
            m_stage = new GameObject("IwadaruCutInStage");
            m_stage.transform.position = new Vector3(10000.0f, 10000.0f, 10000.0f);

            m_renderTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            m_renderTexture.Create();

            var cameraObject = new GameObject("IwadaruCutInCamera");
            cameraObject.transform.SetParent(m_stage.transform, false);
            cameraObject.transform.localPosition = new Vector3(0.0f, 1.65f, -4.4f);

            m_camera = cameraObject.AddComponent<Camera>();
            m_camera.orthographic = false;
            m_camera.fieldOfView = 55.0f;
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = new Color(0.055f, 0.04f, 0.03f, 1.0f);
            m_camera.cullingMask = 1 << CutInLayer;
            m_camera.nearClipPlane = 0.05f;
            m_camera.farClipPlane = 40.0f;
            m_camera.targetTexture = m_renderTexture;
            m_camera.enabled = true;
            cameraObject.transform.LookAt(m_stage.transform.TransformPoint(new Vector3(0.0f, 1.55f, 0.0f)));

            CreateLight("CutInRockKey", new Vector3(2.5f, 3.8f, -2.0f),
                Quaternion.Euler(38.0f, -30.0f, 0.0f), new Color(1.0f, 0.78f, 0.48f), 2.0f);
            CreateLight("CutInRockRim", new Vector3(-2.5f, 2.2f, -1.2f),
                Quaternion.identity, new Color(0.95f, 0.62f, 0.30f), 8.0f);

            CreateBackdrop();
            CreateMaterials(subjectSource);

            if (subjectSource != null)
            {
                var subject = CreateClone(subjectSource, "CutInIwadaruSubject");
                m_subject = subject.transform;
                m_subject.localScale = Vector3.one;
                m_subject.localPosition = new Vector3(0.0f, 1.55f, 0.0f);
                m_subject.localRotation = Quaternion.identity;
                m_createdObjects.Add(subject);
            }

            CreateRocks();
            CreateDust();
            CreateShockwaveMaterial();
        }

        void CreateBackdrop()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "IwadaruCutInRockFractures";
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
                m_backdropMaterial.mainTexture = GetRockTexture();
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

        void CreateMaterials(GameObject subjectSource)
        {
            Shader rockShader = Shader.Find("Universal Render Pipeline/Lit");
            if (rockShader == null) rockShader = Shader.Find("Standard");

            Material sourceMaterial = null;
            if (subjectSource != null)
            {
                var sourceRenderer = subjectSource.GetComponentInChildren<MeshRenderer>();
                if (sourceRenderer != null) sourceMaterial = sourceRenderer.sharedMaterial;
            }

            m_rockMaterial = sourceMaterial != null
                ? new Material(sourceMaterial)
                : new Material(rockShader);
            m_rockMaterial.name = "IwadaruCutInRock";
            if (m_rockMaterial.HasProperty("_BaseColor"))
                m_rockMaterial.SetColor("_BaseColor", new Color(0.34f, 0.28f, 0.21f, 1.0f));
            if (m_rockMaterial.HasProperty("_Color"))
                m_rockMaterial.SetColor("_Color", new Color(0.34f, 0.28f, 0.21f, 1.0f));

            Shader spriteShader = Shader.Find("Sprites/Default");
            if (spriteShader == null) spriteShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (spriteShader != null)
            {
                m_dustMaterial = new Material(spriteShader);
                m_dustMaterial.mainTexture = GetDustTexture();
                if (m_dustMaterial.HasProperty("_Color"))
                    m_dustMaterial.SetColor("_Color", new Color(0.62f, 0.52f, 0.38f, 0.35f));
            }
        }

        void CreateRocks()
        {
            // 周囲に浮かぶ岩石片
            for (int i = 0; i < 20; i++)
            {
                var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rock.name = $"IwadaruCutInRock{i}";
                var collider = rock.GetComponent<Collider>();
                if (collider != null) Destroy(collider);

                rock.transform.SetParent(m_stage.transform, false);
                float angle = (i / 20.0f) * Mathf.PI * 2.0f + Random.Range(-0.2f, 0.2f);
                float radius = Random.Range(1.5f, 3.0f);
                Vector3 position = new Vector3(
                    Mathf.Cos(angle) * radius,
                    Random.Range(0.35f, 3.2f),
                    Mathf.Abs(Mathf.Sin(angle)) * radius * 0.6f + Random.Range(0.2f, 1.0f));
                rock.transform.localPosition = position;
                rock.transform.localRotation = Random.rotation;
                float size = Random.Range(0.18f, 0.75f);
                rock.transform.localScale = new Vector3(size, size * Random.Range(0.7f, 1.4f), size);
                rock.layer = CutInLayer;

                var renderer = rock.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = m_rockMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                m_rocks.Add(rock.transform);
                m_rockBasePositions.Add(position);
                m_rockSpins.Add(Random.Range(8.0f, 35.0f) * (Random.value < 0.5f ? -1.0f : 1.0f));
                m_createdObjects.Add(rock);
            }

            // 画面の縁を固める巨大な岩盤
            CreateLargeRock(new Vector3(-4.0f, 1.3f, 2.0f), new Vector3(2.0f, 3.2f, 1.6f), 12.0f);
            CreateLargeRock(new Vector3(4.0f, 1.2f, 2.2f), new Vector3(2.2f, 3.0f, 1.8f), -15.0f);
            CreateLargeRock(new Vector3(-3.3f, 0.2f, 3.5f), new Vector3(2.8f, 1.2f, 2.0f), 22.0f);
            CreateLargeRock(new Vector3(3.4f, 0.1f, 3.6f), new Vector3(3.0f, 1.0f, 2.2f), -26.0f);
        }

        void CreateLargeRock(Vector3 position, Vector3 scale, float yaw)
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = "IwadaruCutInLargeRock";
            var collider = rock.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            rock.transform.SetParent(m_stage.transform, false);
            rock.transform.localPosition = position;
            rock.transform.localRotation = Quaternion.Euler(
                Random.Range(-15.0f, 15.0f), yaw, Random.Range(-15.0f, 15.0f));
            rock.transform.localScale = scale;
            rock.layer = CutInLayer;

            var renderer = rock.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = m_rockMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            m_createdObjects.Add(rock);
        }

        void CreateDust()
        {
            for (int i = 0; i < 10; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"IwadaruCutInDust{i}";
                var collider = quad.GetComponent<Collider>();
                if (collider != null) Destroy(collider);

                quad.transform.SetParent(m_stage.transform, false);
                quad.transform.localPosition = new Vector3(
                    Random.Range(-3.2f, 3.2f),
                    Random.Range(0.2f, 1.9f),
                    Random.Range(0.5f, 3.0f));
                quad.transform.localScale = Vector3.one * Random.Range(2.2f, 4.5f);
                quad.transform.rotation = m_camera.transform.rotation;
                quad.layer = CutInLayer;

                var renderer = quad.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = m_dustMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                m_dusts.Add(quad.transform);
                m_dustBaseScales.Add(quad.transform.localScale);
                m_createdObjects.Add(quad);
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
            float punch = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(age / 0.65f));

            if (m_camera != null && m_stage != null)
            {
                Vector3 position = new Vector3(0.0f, 1.65f,
                    Mathf.Lerp(-4.4f, -3.5f, punch));
                m_camera.fieldOfView = Mathf.Lerp(55.0f, 43.0f, punch);

                float shake = Mathf.Max(Pulse(age, 0.05f, 0.12f),
                    Mathf.Max(Pulse(age, 0.45f, 0.14f), Pulse(age, 0.85f, 0.18f))) * 0.055f;
                if (shake > 0.0f)
                {
                    position.x += Mathf.Sin(age * 180.0f) * shake;
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
                    * Quaternion.Euler(0.0f, 0.0f, age * 7.0f);
                float pulse = 1.0f + 0.05f * Mathf.Sin(age * 17.0f);
                m_backdrop.transform.localScale = new Vector3(20.0f, 12.0f, 1.0f) * pulse;
            }

            if (m_subject != null)
            {
                m_subject.localRotation = Quaternion.Euler(
                    Mathf.Sin(age * 3.0f) * 2.0f,
                    Mathf.Sin(age * 2.0f) * 3.0f,
                    Mathf.Sin(age * 4.0f) * 2.0f);
            }

            for (int i = 0; i < m_rocks.Count; i++)
            {
                Transform rock = m_rocks[i];
                if (rock == null) continue;
                Vector3 basePosition = m_rockBasePositions[i];
                rock.localPosition = basePosition + new Vector3(
                    Mathf.Sin(age * 1.7f + i) * 0.06f,
                    Mathf.Sin(age * 1.3f + i * 0.7f) * 0.08f,
                    0.0f);
                rock.Rotate(Vector3.one * (m_rockSpins[i] * Time.unscaledDeltaTime), Space.Self);
            }

            for (int i = 0; i < m_dusts.Count; i++)
            {
                Transform dust = m_dusts[i];
                if (dust == null) continue;
                float pulse = 1.0f + 0.06f * Mathf.Sin(age * 3.0f + i);
                dust.localScale = m_dustBaseScales[i] * pulse;
                dust.rotation = m_camera.transform.rotation;
            }

            if (!m_shockA && age >= 0.05f)
            {
                m_shockA = true;
                CreateShockwave(SubjectPosition(), new Color(0.95f, 0.72f, 0.42f, 0.9f), 4.2f, age);
            }
            if (!m_shockB && age >= 0.45f)
            {
                m_shockB = true;
                CreateShockwave(SubjectPosition(), new Color(0.80f, 0.58f, 0.30f, 0.85f), 6.8f, age);
            }
            if (!m_shockC && age >= 0.85f)
            {
                m_shockC = true;
                CreateShockwave(SubjectPosition(), new Color(1.0f, 0.85f, 0.60f, 0.95f), 9.2f, age);
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
            quad.name = "IwadaruCutInShockwave";
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
            if (m_rockMaterial != null) Destroy(m_rockMaterial);
            if (m_dustMaterial != null) Destroy(m_dustMaterial);
            if (m_shockwaveMaterial != null) Destroy(m_shockwaveMaterial);

            m_stage = null;
            m_camera = null;
            m_renderTexture = null;
            m_subject = null;
            m_backdrop = null;
            m_backdropMaterial = null;
            m_rockMaterial = null;
            m_dustMaterial = null;
            m_shockwaveMaterial = null;
            m_rocks.Clear();
            m_rockBasePositions.Clear();
            m_rockSpins.Clear();
            m_dusts.Clear();
            m_dustBaseScales.Clear();
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
                GUI.color = new Color(1.0f, 0.82f, 0.55f, 0.60f * flash * alpha);
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

            // 焦げ茶色の太い縁取り
            GUI.color = new Color(0.20f, 0.11f, 0.05f, 0.95f * alpha);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.25f;
                float offset = 9.0f;
                GUI.Label(new Rect(rect.x + Mathf.Cos(angle) * offset,
                    rect.y + Mathf.Sin(angle) * offset, rect.width, rect.height), text, m_titleStyle);
            }

            // 黒い影
            GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.9f * alpha);
            GUI.Label(new Rect(rect.x + 11.0f, rect.y + 11.0f, rect.width, rect.height), text, m_titleStyle);

            // 白い本体
            GUI.color = new Color(1.0f, 0.98f, 0.92f, alpha);
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

        static Texture2D GetRockTexture()
        {
            if (s_rockTexture != null) return s_rockTexture;

            const int size = 1024;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            // 岩盤の下地。細かい線ではなく、大きな面のムラで作る
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.008f, y * 0.008f);
                    float broad = Mathf.PerlinNoise(x * 0.0025f + 17.0f, y * 0.0025f + 31.0f);
                    pixels[y * size + x] = Color.Lerp(
                        new Color(0.055f, 0.043f, 0.032f),
                        new Color(0.18f, 0.135f, 0.095f),
                        n * 0.55f + broad * 0.45f);
                }
            }

            // 背景を構成する大きな岩石の破片
            const int fragmentCount = 7;
            for (int i = 0; i < fragmentCount; i++)
            {
                float angle = i * Mathf.PI * 2.0f / fragmentCount
                    + Random.Range(-0.24f, 0.24f);
                float distance = size * Random.Range(0.28f, 0.45f);
                Vector2 fragmentCenter = center
                    + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                float radius = size * Random.Range(0.09f, 0.17f);
                int sides = Random.Range(5, 8);

                var outer = new Vector2[sides];
                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2.0f / sides + Random.Range(-0.16f, 0.16f);
                    float r = radius * Random.Range(0.72f, 1.16f);
                    outer[j] = fragmentCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }

                var inner = ScalePolygon(outer, fragmentCenter, 0.86f);
                FillPolygon(pixels, size, outer, new Color(0.36f, 0.27f, 0.18f));
                FillPolygon(pixels, size, inner, new Color(0.15f, 0.115f, 0.082f));
            }

            // 画面中心から放射状に走る、太く角張った岩盤の亀裂。
            // 枝分かれ・うねりなし。中心で太く、外へ向かって先細りにする。
            const int fissureCount = 10;
            for (int i = 0; i < fissureCount; i++)
            {
                float baseAngle = i * Mathf.PI * 2.0f / fissureCount
                    + Random.Range(-0.08f, 0.08f);
                float startRadius = size * Random.Range(0.07f, 0.11f);
                Vector2 previous = center
                    + new Vector2(Mathf.Cos(baseAngle), Mathf.Sin(baseAngle)) * startRadius;
                int segments = Random.Range(4, 7);
                float totalLength = size * Random.Range(0.48f, 0.68f);
                float startWidth = Random.Range(34.0f, 54.0f);
                float angle = baseAngle;
                float previousWidth = startWidth;

                for (int s = 1; s <= segments; s++)
                {
                    float t = s / (float)segments;
                    // 小さな角度差だけを持たせ、全体は直線的に外へ走らせる
                    angle += Random.Range(-0.10f, 0.10f);
                    float step = totalLength / segments * Random.Range(0.85f, 1.15f);
                    Vector2 next = previous
                        + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * step;
                    float nextWidth = Mathf.Lerp(startWidth, 0.5f, Mathf.Pow(t, 0.85f));

                    // 岩の縁のベージュ。先端では消して、細い線に見えないようにする
                    float lip = Mathf.Lerp(8.0f, 0.0f, t);
                    if (lip > 0.25f)
                    {
                        FillTaperedSegment(pixels, size, previous, next,
                            previousWidth + lip * 2.0f, nextWidth + lip * 2.0f,
                            new Color(0.36f, 0.27f, 0.17f));
                    }

                    FillTaperedSegment(pixels, size, previous, next,
                        previousWidth, nextWidth, new Color(0.012f, 0.009f, 0.006f));

                    previous = next;
                    previousWidth = nextWidth;
                }
            }

            // 中心の黄土色の光
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
                        (byte)Mathf.Clamp(current.r + Mathf.RoundToInt(glow * 82.0f), 0, 255),
                        (byte)Mathf.Clamp(current.g + Mathf.RoundToInt(glow * 52.0f), 0, 255),
                        (byte)Mathf.Clamp(current.b + Mathf.RoundToInt(glow * 22.0f), 0, 255),
                        255);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_rockTexture = texture;
            return texture;
        }

        static Vector2[] ScalePolygon(Vector2[] polygon, Vector2 center, float scale)
        {
            var result = new Vector2[polygon.Length];
            for (int i = 0; i < polygon.Length; i++)
                result[i] = center + (polygon[i] - center) * scale;
            return result;
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
                    float ring = Mathf.Clamp01(1.0f - Mathf.Abs(distance - 0.45f) / 0.06f);
                    float inner = Mathf.Clamp01(1.0f - Mathf.Abs(distance - 0.30f) / 0.035f) * 0.4f;
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

        static Texture2D GetDustTexture()
        {
            if (s_dustTexture != null) return s_dustTexture;

            const int size = 256;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float max = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center) / max;
                    float alpha = Mathf.Pow(Mathf.Clamp01(1.0f - distance), 2.2f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255.0f));
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
