using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // みだる必殺技「ファンネル」専用カットイン。
    // 元のTumikiBlockの見た目だけを複製し、積み木の数だけ画面手前へ発射する。
    public class MidaruCutIn : MonoBehaviour
    {
        const float Duration = 2.0f;
        const int CutInLayer = 30;
        const float FireTime = 0.78f;
        const float FinishTime = 1.55f;

        static MidaruCutIn s_instance;
        static Font s_font;
        static Texture2D s_backdropTexture;

        string m_specialName;
        float m_start;
        bool m_active;
        float m_previousTimeScale = 1.0f;

        GameObject m_stage;
        Camera m_camera;
        RenderTexture m_renderTexture;
        Transform m_subject;
        Material m_backdropMaterial;
        Material m_trailMaterial;
        Material m_ringMaterial;
        GameObject m_ring;
        float m_ringStart;
        float m_flashUntil;
        float m_shakePulse;
        bool m_fired;
        bool m_finished;

        class BlockShot
        {
            public Transform Transform;
            public TrailRenderer Trail;
            public Vector3 Start;
            public Vector3 Deploy;
            public Vector3 Direction;
            public Vector3 BaseScale;
            public float Spin;
            public float Delay;
        }

        readonly List<BlockShot> m_blocks = new List<BlockShot>();
        readonly List<GameObject> m_createdObjects = new List<GameObject>();
        readonly List<TumikiBlock> m_sourceBlocks = new List<TumikiBlock>();
        GUIStyle m_titleStyle;
        bool m_stylesReady;

        public static void Show(string specialName, DarumaCharacter character, IList<TumikiBlock> blocks)
        {
            if (s_instance == null)
            {
                var go = new GameObject("MidaruCutIn");
                s_instance = go.AddComponent<MidaruCutIn>();
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

            m_start = Time.unscaledTime;
            m_active = true;
            m_fired = false;
            m_finished = false;
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
            m_stage = new GameObject("MidaruCutInStage");
            m_stage.transform.position = new Vector3(10000.0f, 10000.0f, 10000.0f);

            m_renderTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            m_renderTexture.Create();

            var cameraObject = new GameObject("MidaruCutInCamera");
            cameraObject.transform.SetParent(m_stage.transform, false);
            cameraObject.transform.localPosition = new Vector3(0.0f, 2.35f, -4.80f);
            m_camera = cameraObject.AddComponent<Camera>();
            m_camera.orthographic = false;
            m_camera.fieldOfView = 54.0f;
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = new Color(0.020f, 0.060f, 0.035f, 1.0f);
            m_camera.cullingMask = 1 << CutInLayer;
            m_camera.nearClipPlane = 0.05f;
            m_camera.farClipPlane = 40.0f;
            m_camera.targetTexture = m_renderTexture;
            m_camera.enabled = true;
            LookAtSubject(cameraObject.transform);

            CreateLight("MidaruKey", new Vector3(2.0f, 3.5f, -2.0f),
                Quaternion.Euler(35.0f, -30.0f, 0.0f), new Color(0.85f, 1.0f, 0.85f), 2.2f);
            CreateLight("MidaruRim", new Vector3(-2.0f, 2.0f, -1.5f),
                Quaternion.identity, new Color(0.30f, 1.0f, 0.45f), 8.0f);

            CreateBackdrop();
            CreateMaterials();

            if (character != null)
            {
                Transform headSource = FindPart(character.transform, "Head");
                if (headSource != null)
                {
                    var head = CloneVisual(headSource.gameObject, m_stage.transform, "CutInMidaruHead");
                    m_subject = head.transform;
                    m_subject.localPosition = new Vector3(0.0f, 1.55f, 0.0f);
                    m_subject.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
                    m_subject.localScale = Vector3.one * 2.15f;
                    m_createdObjects.Add(head);
                }
            }

            CreateBlocks();
        }

        void CreateMaterials()
        {
            m_trailMaterial = CreateUnlitMaterial(new Color(1.0f, 0.72f, 0.12f, 0.9f), true);
            m_ringMaterial = CreateUnlitMaterial(new Color(0.60f, 1.0f, 0.70f, 0.8f), true);
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
            quad.name = "MidaruCutInBackdrop";
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

        void CreateBlocks()
        {
            int count = m_sourceBlocks.Count;
            for (int i = 0; i < count; i++)
            {
                var source = m_sourceBlocks[i];
                if (source == null) continue;

                var root = CreateBlockVisual(source, i);
                if (root == null) continue;

                float angle = i * Mathf.PI * 2.0f / Mathf.Max(1, count);
                // 全ブロックが同じ位置から出ると積み重なって見えるため、開始位置をずらす
                Vector3 start = new Vector3(
                    count > 1 ? Mathf.Cos(angle) * 0.16f : 0.0f,
                    1.05f + i * 0.12f,
                    0.05f + i * 0.012f);
                Vector3 deploy = new Vector3(
                    Mathf.Cos(angle) * 1.25f,
                    1.55f + Mathf.Sin(angle) * 0.72f,
                    0.10f + (i % 2) * 0.08f);

                var trailObject = new GameObject("BlockTrail");
                trailObject.layer = CutInLayer;
                trailObject.transform.SetParent(root.transform, false);
                var trail = trailObject.AddComponent<TrailRenderer>();
                if (m_trailMaterial != null) trail.sharedMaterial = m_trailMaterial;
                trail.time = 0.38f;
                trail.minVertexDistance = 0.02f;
                trail.autodestruct = false;
                trail.emitting = false;
                trail.alignment = LineAlignment.View;
                trail.startWidth = 0.20f;
                trail.endWidth = 0.0f;
                // 背景の緑集中線と同化しないよう、積み木の軌跡は黄〜金色にする
                trail.startColor = new Color(1.0f, 0.95f, 0.55f, 0.95f);
                trail.endColor = new Color(1.0f, 0.42f, 0.04f, 0.0f);

                m_blocks.Add(new BlockShot
                {
                    Transform = root.transform,
                    Trail = trail,
                    Start = start,
                    Deploy = deploy,
                    Direction = Vector3.zero,
                    BaseScale = root.transform.localScale,
                    Spin = Random.Range(-240.0f, 240.0f),
                    Delay = i * 0.055f,
                });
            }
        }

        // 元のTumikiBlockのMeshFilter/MeshRendererだけを複製する。
        // Rigidbody・TumikiBlock・パーティクルは持たせず、見た目と色を完全に維持する。
        GameObject CreateBlockVisual(TumikiBlock source, int index)
        {
            var root = new GameObject($"MidaruBlock{index}");
            root.layer = CutInLayer;
            root.transform.SetParent(m_stage.transform, false);

            // 積み木1個分のVisualだけを使う。積み重なったモデルや追加パーツは複製しない。
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
            Vector3 partScale = meshFilter.transform.lossyScale;
            // ルート側のスケールは1なので、元のVisualのワールドスケールをそのまま使う。
            // ここで比率にするとTumikiBlockの平たい形が崩れるため注意。
            part.transform.localScale = partScale;

            var filter = part.AddComponent<MeshFilter>();
            filter.sharedMesh = meshFilter.sharedMesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;

            // TumikiBlockの色はMaterialPropertyBlockで付いているため、それも複製する
            var propertyBlock = new MaterialPropertyBlock();
            sourceRenderer.GetPropertyBlock(propertyBlock);
            renderer.SetPropertyBlock(propertyBlock);

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return root;
        }

        void Animate(float age)
        {
            AnimateCamera(age);
            AnimateSubject(age);
            AnimateBlocks(age);

            if (!m_fired && age >= FireTime)
            {
                m_fired = true;
                m_flashUntil = Time.unscaledTime + 0.16f;
                m_shakePulse = 0.85f;
                CreateFireRing();
            }
            if (!m_finished && age >= FinishTime)
            {
                m_finished = true;
                m_flashUntil = Time.unscaledTime + 0.22f;
                m_shakePulse = 1.0f;
            }

            AnimateRing(age);
            if (m_shakePulse > 0.0f)
                m_shakePulse = Mathf.Max(0.0f, m_shakePulse - Time.unscaledDeltaTime * 4.5f);
        }

        void AnimateCamera(float age)
        {
            if (m_camera == null) return;

            float intro = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(age / 0.3f));
            Vector3 position = Vector3.Lerp(new Vector3(0.0f, 2.55f, -5.20f),
                new Vector3(0.0f, 2.25f, -4.55f), intro);
            if (m_shakePulse > 0.0f)
            {
                Vector2 shake = Random.insideUnitCircle * (0.11f * m_shakePulse);
                position += new Vector3(shake.x, shake.y, Random.Range(-0.06f, 0.06f) * m_shakePulse);
            }
            m_camera.transform.localPosition = position;
            m_camera.fieldOfView = Mathf.Lerp(54.0f, 50.0f, intro);
            LookAtSubject(m_camera.transform);
        }

        void AnimateSubject(float age)
        {
            if (m_subject == null) return;
            float charge = Mathf.Clamp01(age / 0.5f);
            m_subject.localPosition = new Vector3(
                Mathf.Sin(age * 2.2f) * 0.02f,
                1.55f + Mathf.Sin(age * 2.7f) * 0.02f,
                0.0f);
            m_subject.localRotation = Quaternion.Euler(
                Mathf.Sin(age * 2.0f) * 1.5f,
                180.0f + Mathf.Sin(age * 1.7f) * 2.0f,
                Mathf.Sin(age * 1.9f) * 1.2f);
        }

        void AnimateBlocks(float age)
        {
            for (int i = 0; i < m_blocks.Count; i++)
            {
                var block = m_blocks[i];
                if (block.Transform == null) continue;

                float appear = Mathf.Clamp01((age - block.Delay) / 0.20f);
                float fireAge = age - FireTime;
                Vector3 position;
                if (fireAge <= 0.0f)
                {
                    float deploy = Mathf.SmoothStep(0.0f, 1.0f, appear);
                    position = Vector3.Lerp(block.Start, block.Deploy, deploy);
                    block.Transform.localScale = block.BaseScale * appear;
                    block.Trail.emitting = false;
                }
                else
                {
                    if (block.Direction == Vector3.zero)
                    {
                        Vector3 radial = (block.Deploy - new Vector3(0.0f, 1.55f, 0.0f)).normalized;
                        block.Direction = (Vector3.back + radial * Random.Range(0.35f, 0.75f)).normalized;
                        block.Trail.emitting = true;
                    }
                    position = block.Deploy + block.Direction * (5.2f * fireAge);
                    block.Transform.localScale = block.BaseScale;
                }

                block.Transform.localPosition = position;
                block.Transform.localRotation = Quaternion.Euler(
                    age * block.Spin, age * block.Spin * 0.7f, age * block.Spin * 0.45f);
                block.Transform.localScale = block.BaseScale * Mathf.Max(0.01f,
                    fireAge > 0.0f ? 1.0f : Mathf.Clamp01(appear));
            }
        }

        void CreateFireRing()
        {
            m_ring = new GameObject("MidaruFireRing");
            m_ring.layer = CutInLayer;
            m_ring.transform.SetParent(m_stage.transform, false);
            m_ring.transform.localPosition = new Vector3(0.0f, 1.55f, 0.0f);
            var line = m_ring.AddComponent<LineRenderer>();
            line.positionCount = 48;
            line.loop = true;
            line.useWorldSpace = false;
            line.widthMultiplier = 0.05f;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (m_ringMaterial != null) line.sharedMaterial = m_ringMaterial;
            line.startColor = new Color(0.55f, 1.0f, 0.70f, 0.9f);
            line.endColor = new Color(0.10f, 0.60f, 0.30f, 0.0f);
            for (int i = 0; i < line.positionCount; i++)
            {
                float a = i * Mathf.PI * 2.0f / line.positionCount;
                line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0.0f));
            }
            m_ringStart = Time.unscaledTime;
            m_createdObjects.Add(m_ring);
        }

        void AnimateRing(float age)
        {
            if (m_ring == null) return;
            float t = Mathf.Clamp01((age - FireTime) / 0.45f);
            float s = Mathf.Lerp(0.4f, 3.2f, 1.0f - Mathf.Pow(1.0f - t, 3.0f));
            m_ring.transform.localScale = new Vector3(s, s, s);
            var line = m_ring.GetComponent<LineRenderer>();
            if (line != null)
            {
                Color c = new Color(0.55f, 1.0f, 0.70f, (1.0f - t) * 0.9f);
                line.startColor = c;
                line.endColor = new Color(c.r, c.g, c.b, 0.0f);
            }
            if (t >= 1.0f) m_ring.SetActive(false);
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
                GUI.color = new Color(0.75f, 1.0f, 0.80f,
                    Mathf.Clamp01(flash / 0.16f) * 0.35f);
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
            GUI.color = new Color(0.0f, 0.02f, 0.01f, 0.95f * alpha);
            GUI.Label(new Rect(rect.x + 12.0f, rect.y + 12.0f, rect.width, rect.height), text, m_titleStyle);
            GUI.color = new Color(0.06f, 0.55f, 0.18f, 0.98f * alpha);
            DrawOutline(rect, text, 9.0f);
            GUI.color = new Color(0.35f, 0.95f, 0.55f, 0.95f * alpha);
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
            if (m_ringMaterial != null) Destroy(m_ringMaterial);
            m_stage = null;
            m_camera = null;
            m_renderTexture = null;
            m_subject = null;
            m_ring = null;
            m_backdropMaterial = null;
            m_trailMaterial = null;
            m_ringMaterial = null;
            m_blocks.Clear();
            m_sourceBlocks.Clear();
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
                    Color color = Color.Lerp(new Color(0.045f, 0.145f, 0.075f),
                        new Color(0.005f, 0.025f, 0.012f), Mathf.Clamp01(r));
                    float glow = Mathf.Pow(Mathf.Clamp01(1.0f - r), 3.0f);
                    color += new Color(glow * 0.06f, glow * 0.25f, glow * 0.12f);
                    pixels[y * size + x] = color;
                }
            }

            for (int i = 0; i < 20; i++)
            {
                float angle = i * Mathf.PI * 2.0f / 20.0f + 0.05f;
                Vector2 a = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size * 0.04f;
                Vector2 b = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size * 0.72f;
                Color ray = i % 3 == 0
                    ? new Color(0.85f, 1.0f, 0.88f)
                    : new Color(0.25f, 0.90f, 0.40f);
                FillTaperedSegment(pixels, size, a, b, size * 0.016f, 1.0f, ray);
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
