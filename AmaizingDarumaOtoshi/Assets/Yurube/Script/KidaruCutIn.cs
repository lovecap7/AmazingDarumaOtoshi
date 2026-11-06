using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // きだる必殺技「黄金化」専用カットイン。
    // 積み木を吸収して黄金オーラ・電撃をまとい、高速移動の残像を見せる。
    public class KidaruCutIn : MonoBehaviour
    {
        const float Duration = 2.20f;
        const int CutInLayer = 30;
        const float AwakenTime = 1.00f;
        const float DashStart = 1.08f;
        const float PoseTime = 1.72f;

        static KidaruCutIn s_instance;
        static Font s_font;
        static Texture2D s_backdropTexture;

        string m_specialName;
        float m_start;
        bool m_active;
        float m_previousTimeScale = 1.0f;
        int m_blockCount;

        GameObject m_stage;
        Camera m_camera;
        RenderTexture m_renderTexture;
        Transform m_subject;
        Vector3 m_cameraStart;
        Vector3 m_cameraClose;
        Material m_backdropMaterial;
        Material m_trailMaterial;
        Material m_auraMaterial;
        Material m_ghostMaterial;
        Material m_electricMaterial;
        GameObject m_aura;
        GameObject m_shockwave;
        float m_shockwaveStart;
        float m_flashUntil;
        float m_shakePulse;
        bool m_awakened;
        bool m_posed;
        int m_dashIndex;
        float m_nextDash;
        Vector3 m_subjectCenter = new Vector3(0.0f, 1.55f, 0.0f);

        class AbsorbBlock
        {
            public Transform Transform;
            public TrailRenderer Trail;
            public float Angle;
            public float Radius;
            public float Height;
            public float Speed;
            public float AbsorbDelay;
            public Vector3 BaseScale;
            public bool Absorbed;
        }

        class Ghost
        {
            public Transform Transform;
            public float Age;
        }

        readonly List<AbsorbBlock> m_blocks = new List<AbsorbBlock>();
        readonly List<Ghost> m_ghosts = new List<Ghost>();
        readonly List<LineRenderer> m_bolts = new List<LineRenderer>();
        readonly List<GameObject> m_createdObjects = new List<GameObject>();
        readonly List<TumikiBlock> m_sourceBlocks = new List<TumikiBlock>();
        readonly float[] m_dashX = { -1.55f, 1.55f, -0.85f, 0.95f, -1.20f, 1.20f, 0.0f };
        GUIStyle m_titleStyle;
        bool m_stylesReady;

        public static void Show(string specialName, DarumaCharacter character, IList<TumikiBlock> blocks)
        {
            if (s_instance == null)
            {
                var go = new GameObject("KidaruCutIn");
                s_instance = go.AddComponent<KidaruCutIn>();
            }
            s_instance.Play(specialName, character, blocks);
        }

        public static bool IsPlaying => s_instance != null && s_instance.m_active;

        private void Awake() => s_instance = this;
        private void OnDisable() => End();
        private void OnDestroy()
        {
            End();
            if (s_instance == this) s_instance = null;
        }

        void Play(string specialName, DarumaCharacter character, IList<TumikiBlock> blocks)
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
            m_sourceBlocks.Clear();
            if (blocks != null)
            {
                for (int i = 0; i < blocks.Count; i++)
                {
                    if (blocks[i] != null) m_sourceBlocks.Add(blocks[i]);
                }
            }
            m_blockCount = m_sourceBlocks.Count;

            m_start = Time.unscaledTime;
            m_active = true;
            m_awakened = false;
            m_posed = false;
            m_dashIndex = 0;
            m_nextDash = DashStart;
            m_flashUntil = 0.0f;
            m_shakePulse = 0.0f;
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
            m_stage = new GameObject("KidaruCutInStage");
            m_stage.transform.position = new Vector3(10000.0f, 10000.0f, 10000.0f);

            m_renderTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            m_renderTexture.Create();

            var cameraObject = new GameObject("KidaruCutInCamera");
            cameraObject.transform.SetParent(m_stage.transform, false);
            m_cameraStart = new Vector3(0.0f, 2.35f, -5.20f);
            m_cameraClose = new Vector3(0.0f, 1.95f, -3.45f);
            cameraObject.transform.localPosition = m_cameraStart;
            m_camera = cameraObject.AddComponent<Camera>();
            m_camera.orthographic = false;
            m_camera.fieldOfView = 55.0f;
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = new Color(0.060f, 0.045f, 0.010f, 1.0f);
            m_camera.cullingMask = 1 << CutInLayer;
            m_camera.nearClipPlane = 0.05f;
            m_camera.farClipPlane = 40.0f;
            m_camera.targetTexture = m_renderTexture;
            m_camera.enabled = true;
            LookAtSubject(cameraObject.transform);

            CreateLight("KidaruKey", new Vector3(2.5f, 3.5f, -2.0f),
                Quaternion.Euler(35.0f, -30.0f, 0.0f), new Color(1.0f, 0.95f, 0.75f), 2.5f);
            CreateLight("KidaruRim", new Vector3(-2.2f, 2.0f, -1.5f),
                Quaternion.identity, new Color(1.0f, 0.78f, 0.20f), 10.0f);

            CreateBackdrop();
            CreateMaterials();

            if (character != null)
            {
                Transform headSource = FindPart(character.transform, "Head");
                if (headSource != null)
                {
                    var head = CloneVisual(headSource.gameObject, m_stage.transform, "CutInKidaruHead");
                    m_subject = head.transform;
                    m_subject.localPosition = m_subjectCenter;
                    m_subject.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
                    m_subject.localScale = Vector3.one * 2.10f;
                    m_createdObjects.Add(head);
                }
            }

            CreateEnergyBlocks();
            CreateAura();
            CreateBolts();
        }

        void CreateMaterials()
        {
            m_trailMaterial = CreateUnlitMaterial(new Color(1.0f, 0.82f, 0.25f, 0.9f), true);
            m_auraMaterial = CreateUnlitMaterial(new Color(1.0f, 0.82f, 0.25f, 0.13f), true);
            m_ghostMaterial = CreateUnlitMaterial(new Color(1.0f, 0.85f, 0.30f, 0.35f), true);
            m_electricMaterial = CreateUnlitMaterial(new Color(1.0f, 0.98f, 0.72f, 0.9f), true);
        }

        Material CreateUnlitMaterial(Color color, bool additive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (additive)
            {
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1.0f);
                if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 2.0f);
                if (material.HasProperty("_SrcBlend"))
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend"))
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0.0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return material;
        }

        void CreateBackdrop()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "KidaruCutInBackdrop";
            var collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            quad.transform.SetParent(m_stage.transform, false);
            quad.layer = CutInLayer;
            quad.transform.position = m_camera.transform.position + m_camera.transform.forward * 12.0f;
            quad.transform.rotation = m_camera.transform.rotation;
            quad.transform.localScale = new Vector3(20.0f, 12.0f, 1.0f);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                m_backdropMaterial = new Material(shader);
                m_backdropMaterial.mainTexture = GetBackdropTexture();
                var renderer = quad.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sharedMaterial = m_backdropMaterial;
            }
            m_createdObjects.Add(quad);
        }

        void CreateEnergyBlocks()
        {
            int count = m_sourceBlocks.Count;
            for (int i = 0; i < count; i++)
            {
                var source = m_sourceBlocks[i];
                if (source == null) continue;
                var root = CreateBlockVisual(source, i);
                if (root == null) continue;
                root.transform.localScale = Vector3.zero;

                var trailObject = new GameObject("BlockTrail");
                trailObject.layer = CutInLayer;
                trailObject.transform.SetParent(root.transform, false);
                var trail = trailObject.AddComponent<TrailRenderer>();
                if (m_trailMaterial != null) trail.sharedMaterial = m_trailMaterial;
                trail.time = 0.30f;
                trail.minVertexDistance = 0.02f;
                trail.autodestruct = false;
                trail.emitting = false;
                trail.alignment = LineAlignment.View;
                trail.startWidth = 0.14f;
                trail.endWidth = 0.0f;
                trail.startColor = new Color(1.0f, 0.95f, 0.60f, 0.85f);
                trail.endColor = new Color(1.0f, 0.45f, 0.05f, 0.0f);

                m_blocks.Add(new AbsorbBlock
                {
                    Transform = root.transform,
                    Trail = trail,
                    Angle = i * Mathf.PI * 2.0f / Mathf.Max(1, count),
                    Radius = Random.Range(1.20f, 1.65f),
                    Height = Random.Range(0.9f, 2.2f),
                    Speed = Random.Range(90.0f, 170.0f),
                    AbsorbDelay = 0.50f + i * 0.045f,
                    BaseScale = Vector3.one,
                });
            }
        }

        // 積み木1個分のVisualのみを複製し、元の形・色を維持する
        GameObject CreateBlockVisual(TumikiBlock source, int index)
        {
            var root = new GameObject($"KidaruBlock{index}");
            root.layer = CutInLayer;
            root.transform.SetParent(m_stage.transform, false);

            Transform visualSource = FindPart(source.transform, "Visual");
            MeshFilter meshFilter = visualSource != null
                ? visualSource.GetComponent<MeshFilter>()
                : source.GetComponentInChildren<MeshFilter>(true);
            if (meshFilter == null) return root;

            var sourceRenderer = meshFilter.GetComponent<MeshRenderer>();
            if (sourceRenderer == null) return root;

            var part = new GameObject("BlockVisual");
            part.layer = CutInLayer;
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = source.transform.InverseTransformPoint(meshFilter.transform.position);
            part.transform.localRotation = Quaternion.Inverse(source.transform.rotation)
                * meshFilter.transform.rotation;
            part.transform.localScale = meshFilter.transform.lossyScale;

            var filter = part.AddComponent<MeshFilter>();
            filter.sharedMesh = meshFilter.sharedMesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            var propertyBlock = new MaterialPropertyBlock();
            sourceRenderer.GetPropertyBlock(propertyBlock);
            renderer.SetPropertyBlock(propertyBlock);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return root;
        }

        void CreateAura()
        {
            m_aura = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_aura.name = "KidaruAura";
            var collider = m_aura.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            m_aura.layer = CutInLayer;
            m_aura.transform.SetParent(m_stage.transform, false);
            m_aura.transform.localPosition = m_subjectCenter;
            m_aura.transform.localScale = Vector3.zero;
            var renderer = m_aura.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (m_auraMaterial != null) renderer.sharedMaterial = m_auraMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            m_createdObjects.Add(m_aura);
        }

        void CreateBolts()
        {
            for (int i = 0; i < 4; i++)
            {
                var boltObject = new GameObject($"KidaruBolt{i}");
                boltObject.layer = CutInLayer;
                boltObject.transform.SetParent(m_stage.transform, false);
                var line = boltObject.AddComponent<LineRenderer>();
                line.positionCount = 4;
                line.useWorldSpace = false;
                line.widthMultiplier = 0.035f;
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                if (m_electricMaterial != null) line.sharedMaterial = m_electricMaterial;
                line.startColor = new Color(1.0f, 1.0f, 0.8f, 0.85f);
                line.endColor = new Color(1.0f, 0.85f, 0.2f, 0.0f);
                m_bolts.Add(line);
                m_createdObjects.Add(boltObject);
            }
        }

        void Animate(float age)
        {
            AnimateCamera(age);
            AnimateSubject(age);
            AnimateBlocks(age);
            AnimateAura(age);
            AnimateBolts(age);
            AnimateGhosts(age);

            if (!m_awakened && age >= AwakenTime)
            {
                m_awakened = true;
                m_flashUntil = Time.unscaledTime + 0.20f;
                m_shakePulse = 1.0f;
                CreateShockwave();
            }
            if (!m_posed && age >= PoseTime)
            {
                m_posed = true;
                m_flashUntil = Time.unscaledTime + 0.16f;
                m_shakePulse = 0.7f;
            }
            if (age >= m_nextDash && age < PoseTime)
            {
                SkipDash(age);
            }

            AnimateShockwave(age);
            if (m_shakePulse > 0.0f)
                m_shakePulse = Mathf.Max(0.0f, m_shakePulse - Time.unscaledDeltaTime * 4.5f);
        }

        void AnimateCamera(float age)
        {
            if (m_camera == null) return;

            float intro = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(age / 0.30f));
            Vector3 pos = Vector3.Lerp(m_cameraStart, m_cameraClose, intro);
            if (m_shakePulse > 0.0f)
            {
                Vector2 shake = Random.insideUnitCircle * (0.12f * m_shakePulse);
                pos += new Vector3(shake.x, shake.y, Random.Range(-0.06f, 0.06f) * m_shakePulse);
            }
            m_camera.transform.localPosition = pos;
            m_camera.fieldOfView = Mathf.Lerp(55.0f, 50.0f, intro);
            LookAtSubject(m_camera.transform);
        }

        void AnimateSubject(float age)
        {
            if (m_subject == null) return;

            float pose = Mathf.Clamp01((age - PoseTime) / 0.25f);
            if (age >= DashStart && age < PoseTime)
            {
                int index = Mathf.Clamp(m_dashIndex, 0, m_dashX.Length - 1);
                float targetX = m_dashX[index];
                m_subject.localPosition = Vector3.Lerp(m_subject.localPosition,
                    new Vector3(targetX, 1.55f, 0.0f), Mathf.Clamp01((age - DashStart) * 10.0f));
            }
            else if (age < DashStart)
            {
                m_subject.localPosition = new Vector3(
                    Mathf.Sin(age * 3.0f) * 0.025f,
                    1.55f + Mathf.Sin(age * 3.4f) * 0.02f, 0.0f);
            }
            else if (age >= PoseTime)
            {
                m_subject.localPosition = new Vector3(0.0f, 1.55f, 0.0f);
            }
            m_subject.localRotation = Quaternion.Euler(
                Mathf.Sin(age * 2.0f) * 1.0f,
                180.0f + Mathf.Sin(age * 1.7f) * 2.0f,
                Mathf.Sin(age * 1.9f) * 1.0f - 9.0f * pose);
        }

        void AnimateBlocks(float age)
        {
            float auraScale = 1.0f + Mathf.Min(0.8f, m_blockCount * 0.08f);
            for (int i = 0; i < m_blocks.Count; i++)
            {
                var block = m_blocks[i];
                if (block.Transform == null) continue;

                if (!block.Absorbed)
                {
                    float appear = Mathf.Clamp01(age / 0.22f);
                    float angle = block.Angle + block.Speed * age;
                    block.Transform.localPosition = new Vector3(
                        Mathf.Cos(angle * Mathf.Deg2Rad) * block.Radius,
                        block.Height + Mathf.Sin(age * 3.0f + i) * 0.10f,
                        Mathf.Sin(angle * Mathf.Deg2Rad) * block.Radius * 0.35f);
                    block.Transform.localScale = block.BaseScale * appear;
                    block.Transform.localRotation = Quaternion.Euler(
                        age * 180.0f, age * 240.0f, age * 150.0f);
                    block.Trail.emitting = true;

                    if (age >= block.AbsorbDelay)
                    {
                        block.Absorbed = true;
                        block.Trail.emitting = false;
                    }
                }
                else
                {
                    Vector3 target = new Vector3(0.0f, 1.55f, 0.0f);
                    block.Transform.localPosition = Vector3.Lerp(block.Transform.localPosition,
                        target, Mathf.Clamp01(Time.unscaledDeltaTime * 8.0f));
                    block.Transform.localScale = Vector3.Lerp(block.Transform.localScale,
                        Vector3.zero, Mathf.Clamp01(Time.unscaledDeltaTime * 10.0f));
                    if (block.Transform.localScale.sqrMagnitude < 0.0004f)
                        block.Transform.gameObject.SetActive(false);
                }
            }
        }

        void AnimateAura(float age)
        {
            if (m_aura == null) return;
            float awaken = Mathf.Clamp01((age - AwakenTime) / 0.20f);
            float pulse = 1.0f + 0.06f * Mathf.Sin(age * 16.0f);
            float countBoost = 1.0f + Mathf.Min(0.9f, m_blockCount * 0.09f);
            float scale = Mathf.Lerp(0.7f, 2.25f * countBoost * pulse, awaken);
            m_aura.transform.localPosition = new Vector3(m_subject != null ? m_subject.localPosition.x : 0.0f, 1.55f, 0.0f);
            m_aura.transform.localScale = Vector3.one * scale;
            if (m_auraMaterial != null && m_auraMaterial.HasProperty("_BaseColor"))
            {
                Color c = new Color(1.0f, 0.82f, 0.25f, 0.10f + 0.10f * awaken);
                m_auraMaterial.SetColor("_BaseColor", c);
                m_auraMaterial.SetColor("_Color", c);
            }
        }

        void AnimateBolts(float age)
        {
            float awaken = Mathf.Clamp01((age - AwakenTime) / 0.2f);
            if (awaken <= 0.0f) return;
            for (int b = 0; b < m_bolts.Count; b++)
            {
                var line = m_bolts[b];
                if (line == null) continue;
                Vector3 center = new Vector3(
                    m_subject != null ? m_subject.localPosition.x : 0.0f, 1.55f, 0.0f);
                for (int i = 0; i < line.positionCount; i++)
                {
                    float a = i * Mathf.PI * 2.0f / line.positionCount + age * 3.0f + b;
                    float r = 0.8f + Random.Range(-0.18f, 0.18f);
                    line.SetPosition(i, center + new Vector3(
                        Mathf.Cos(a) * r, Mathf.Sin(a * 1.7f) * 0.75f, Mathf.Sin(a) * r * 0.4f));
                }
            }
        }

        void AnimateGhosts(float age)
        {
            for (int i = m_ghosts.Count - 1; i >= 0; i--)
            {
                var ghost = m_ghosts[i];
                if (ghost.Transform == null)
                {
                    m_ghosts.RemoveAt(i);
                    continue;
                }
                ghost.Age += Time.unscaledDeltaTime;
                float fade = Mathf.Clamp01(1.0f - ghost.Age / 0.28f);
                ghost.Transform.localScale = Vector3.one * (2.10f * fade);
                if (ghost.Age >= 0.28f)
                {
                    Destroy(ghost.Transform.gameObject);
                    m_ghosts.RemoveAt(i);
                }
            }
        }

        void SkipDash(float age)
        {
            if (m_subject != null)
            {
                CreateGhost(m_subject.localPosition, m_subject.localRotation);
            }
            m_dashIndex = (m_dashIndex + 1) % m_dashX.Length;
            m_nextDash = age + 0.075f;
            m_flashUntil = Mathf.Max(m_flashUntil, Time.unscaledTime + 0.04f);
        }

        void CreateGhost(Vector3 position, Quaternion rotation)
        {
            if (m_ghostMaterial == null) return;
            var source = FindPart(m_stage.transform, "CutInKidaruHead");
            if (source == null) return;

            var ghost = Instantiate(source.gameObject, m_stage.transform, false);
            ghost.name = "KidaruAfterImage";
            ghost.transform.localPosition = position;
            ghost.transform.localRotation = rotation;
            ghost.transform.localScale = source.localScale;
            foreach (var collider in ghost.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (var body in ghost.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            foreach (var mono in ghost.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mono);
            foreach (var renderer in ghost.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null) renderer.sharedMaterial = m_ghostMaterial;
            }
            SetLayerRecursively(ghost);
            m_ghosts.Add(new Ghost { Transform = ghost.transform, Age = 0.0f });
        }

        void CreateShockwave()
        {
            m_shockwave = new GameObject("KidaruShockwave");
            m_shockwave.layer = CutInLayer;
            m_shockwave.transform.SetParent(m_stage.transform, false);
            m_shockwave.transform.localPosition = new Vector3(0.0f, 0.02f, 0.0f);
            var line = m_shockwave.AddComponent<LineRenderer>();
            line.positionCount = 48;
            line.loop = true;
            line.useWorldSpace = false;
            line.widthMultiplier = 0.06f;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (m_electricMaterial != null) line.sharedMaterial = m_electricMaterial;
            line.startColor = new Color(1.0f, 0.88f, 0.35f, 0.9f);
            line.endColor = new Color(1.0f, 0.55f, 0.10f, 0.0f);
            for (int i = 0; i < line.positionCount; i++)
            {
                float a = i * Mathf.PI * 2.0f / line.positionCount;
                line.SetPosition(i, new Vector3(Mathf.Cos(a), 0.0f, Mathf.Sin(a)));
            }
            m_shockwaveStart = Time.unscaledTime;
            m_createdObjects.Add(m_shockwave);
        }

        void AnimateShockwave(float age)
        {
            if (m_shockwave == null) return;
            float t = Mathf.Clamp01((age - AwakenTime) / 0.50f);
            float s = Mathf.Lerp(0.5f, 4.0f, 1.0f - Mathf.Pow(1.0f - t, 3.0f));
            m_shockwave.transform.localScale = new Vector3(s, s, s);
            var line = m_shockwave.GetComponent<LineRenderer>();
            if (line != null)
            {
                Color c = new Color(1.0f, 0.88f, 0.35f, (1.0f - t) * 0.9f);
                line.startColor = c;
                line.endColor = new Color(c.r, c.g, c.b, 0.0f);
            }
            if (t >= 1.0f) m_shockwave.SetActive(false);
        }

        void LookAtSubject(Transform cameraTransform)
        {
            cameraTransform.LookAt(m_stage.transform.TransformPoint(new Vector3(0.0f, 1.35f, 0.0f)));
        }

        void CreateLight(string name, Vector3 position, Quaternion rotation, Color color, float intensity)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(m_stage.transform, false);
            lightObject.transform.localPosition = position;
            lightObject.transform.localRotation = rotation;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << CutInLayer;
        }

        private void OnGUI()
        {
            if (!m_active) return;
            float age = Time.unscaledTime - m_start;
            if (age > Duration + 0.15f) return;

            GUI.depth = -1000;
            if (!m_stylesReady) CreateTitleStyle();
            float fade = Mathf.Clamp01((Duration - age) / 0.16f)
                * Mathf.Clamp01(age / 0.08f);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0.0f, 0.0f, Screen.width, Screen.height),
                m_renderTexture, ScaleMode.ScaleAndCrop, false);

            float flash = m_flashUntil - Time.unscaledTime;
            if (flash > 0.0f)
            {
                GUI.color = new Color(1.0f, 0.95f, 0.65f,
                    Mathf.Clamp01(flash / 0.18f) * 0.35f);
                GUI.DrawTexture(new Rect(0.0f, 0.0f, Screen.width, Screen.height),
                    Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            GUI.color = new Color(1.0f, 1.0f, 1.0f, fade);
            DrawTitle(new Rect(0.0f, Screen.height * 0.66f, Screen.width, 220.0f),
                m_specialName, fade);
            GUI.color = Color.white;
        }

        void CreateTitleStyle()
        {
            if (m_stylesReady) return;
            m_stylesReady = true;
            if (s_font == null)
            {
                s_font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Noto Sans CJK JP" }, 24);
            }
            if (s_font == null) s_font = GUI.skin.font;
            m_titleStyle = new GUIStyle(GUI.skin.label);
            m_titleStyle.font = s_font;
            m_titleStyle.fontSize = 150;
            m_titleStyle.fontStyle = FontStyle.Bold;
            m_titleStyle.alignment = TextAnchor.MiddleCenter;
            m_titleStyle.normal.textColor = Color.white;
        }

        void DrawTitle(Rect rect, string text, float alpha)
        {
            if (m_titleStyle == null) return;
            GUI.color = new Color(0.02f, 0.01f, 0.0f, 0.95f * alpha);
            GUI.Label(new Rect(rect.x + 12.0f, rect.y + 12.0f, rect.width, rect.height), text, m_titleStyle);
            GUI.color = new Color(0.95f, 0.55f, 0.05f, 0.98f * alpha);
            DrawOutline(rect, text, 9.0f);
            GUI.color = new Color(1.0f, 0.88f, 0.45f, 0.95f * alpha);
            DrawOutline(rect, text, 4.0f);
            GUI.color = new Color(1.0f, 1.0f, 1.0f, alpha);
            GUI.Label(rect, text, m_titleStyle);
        }

        void DrawOutline(Rect rect, string text, float radius)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2.0f / 8.0f;
                GUI.Label(new Rect(rect.x + Mathf.Cos(a) * radius, rect.y + Mathf.Sin(a) * radius,
                    rect.width, rect.height), text, m_titleStyle);
            }
        }

        void CleanupStage()
        {
            if (m_stage != null) Destroy(m_stage);
            if (m_renderTexture != null)
            {
                m_renderTexture.Release();
                Destroy(m_renderTexture);
            }
            if (m_backdropMaterial != null) Destroy(m_backdropMaterial);
            if (m_trailMaterial != null) Destroy(m_trailMaterial);
            if (m_auraMaterial != null) Destroy(m_auraMaterial);
            if (m_ghostMaterial != null) Destroy(m_ghostMaterial);
            if (m_electricMaterial != null) Destroy(m_electricMaterial);
            m_stage = null;
            m_camera = null;
            m_renderTexture = null;
            m_subject = null;
            m_aura = null;
            m_shockwave = null;
            m_backdropMaterial = null;
            m_trailMaterial = null;
            m_auraMaterial = null;
            m_ghostMaterial = null;
            m_electricMaterial = null;
            m_blocks.Clear();
            m_ghosts.Clear();
            m_bolts.Clear();
            m_createdObjects.Clear();
        }

        static Transform FindPart(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindPart(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        GameObject CloneVisual(GameObject source, Transform parent, string name)
        {
            var clone = Instantiate(source, parent, false);
            clone.name = name;
            foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            foreach (var mono in clone.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mono);
            SetLayerRecursively(clone);
            return clone;
        }

        static void SetLayerRecursively(GameObject go)
        {
            go.layer = CutInLayer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursively(go.transform.GetChild(i).gameObject);
        }

        static Texture2D GetBackdropTexture()
        {
            if (s_backdropTexture != null) return s_backdropTexture;

            const int size = 1024;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float max = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x, y), center) / max;
                    Color color = Color.Lerp(new Color(0.100f, 0.070f, 0.020f),
                        new Color(0.020f, 0.012f, 0.004f), Mathf.Clamp01(r));
                    float glow = Mathf.Pow(Mathf.Clamp01(1.0f - r), 3.0f);
                    color += new Color(glow * 0.35f, glow * 0.24f, glow * 0.06f);
                    pixels[y * size + x] = color;
                }
            }

            for (int i = 0; i < 24; i++)
            {
                float angle = i * Mathf.PI * 2.0f / 24.0f + 0.05f;
                Vector2 a = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size * 0.04f;
                Vector2 b = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size * 0.72f;
                Color ray = i % 3 == 0
                    ? new Color(1.0f, 0.98f, 0.78f)
                    : new Color(1.0f, 0.72f, 0.12f);
                FillTaperedSegment(pixels, size, a, b, size * 0.018f, 1.0f, ray);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_backdropTexture = texture;
            return texture;
        }

        static void FillTaperedSegment(Color32[] pixels, int size, Vector2 a, Vector2 b,
            float widthA, float widthB, Color color)
        {
            Vector2 direction = (b - a).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            var polygon = new Vector2[4];
            polygon[0] = a + normal * widthA * 0.5f;
            polygon[1] = b + normal * widthB * 0.5f;
            polygon[2] = b - normal * widthB * 0.5f;
            polygon[3] = a - normal * widthA * 0.5f;
            FillPolygon(pixels, size, polygon, color);
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
                    for (int x = startX; x <= endX; x++) pixels[y * size + x] = color;
                }
            }
        }
    }
}
